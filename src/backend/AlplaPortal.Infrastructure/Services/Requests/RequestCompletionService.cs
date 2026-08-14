using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Requests;

/// <summary>
/// Two-phase Post-Payment Completion evaluation.
///
/// <para><b>Phase 4A state</b>: Phase 1 (GROUP completion) is REAL — it derives the
/// <see cref="GroupCompletionProjector"/> reading of every evaluated group inside the caller's
/// transaction and performs the WAITING_FISCAL_RECEIPT / COMPLETED transitions with their
/// idempotent history. Phase 2 (PARENT completion) remains dormant until Phase 4C: the ambient
/// transaction guard and the feature gate are live, the evaluation body is not.</para>
///
/// <para>While <c>Enabled &amp;&amp; CompletionEnabled</c> is not true — the committed default and
/// the current TEST state — both methods are pure no-ops that read nothing and write nothing.</para>
/// </summary>
public class RequestCompletionService : IRequestCompletionService
{
    private const string ParentNotActivatedMessage =
        "Post-Payment PARENT completion evaluation is not implemented in Release 4 Phase 4A. " +
        "Phase 2 (parent) is activated in Phase 4C.";

    private readonly ApplicationDbContext _context;
    private readonly PostPaymentCompletionOptions _options;
    private readonly ILogger<RequestCompletionService> _logger;

    public RequestCompletionService(
        ApplicationDbContext context,
        IOptions<PostPaymentCompletionOptions> options,
        ILogger<RequestCompletionService> logger)
    {
        _context = context;
        _options = options?.Value ?? new PostPaymentCompletionOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GroupCompletionResult> EvaluateGroupCompletionAsync(
        Guid requestId,
        Guid? groupId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        // Completion gate first (Phase 3A checkpoint split: intake may be on while the Phase 4
        // lifecycle is not): while completion is disabled this method must be indistinguishable
        // from not existing at all — no query, no tracked entity, no log noise on the hot path.
        if (PostPaymentCompletionPolicy.IsCompletionDisabled(_options))
            return GroupCompletionResult.NoOp;

        var result = new GroupCompletionResult();

        // Contract: never opens a transaction and never calls SaveChanges — the caller's single
        // SaveChanges persists the dimension change and the completion together.
        var request = await _context.Requests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null)
        {
            result.ErrorMessage = $"Request {requestId} not found for completion evaluation.";
            return result;
        }

        // Tracked on purpose, with the same change-tracker reconciliation the coverage service
        // uses: the evaluation must see the CALLER'S IN-TRANSACTION STATE (a stamp or status flip
        // made in memory before saving), not the database's last committed reading.
        var dbGroups = await _context.RequestPoGroups
            .Include(g => g.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(g => g.LineItems).ThenInclude(li => li.SelectedQuotationItem!)
                .ThenInclude(qi => qi.LineItemStatus)
            .Where(g => g.RequestId == requestId && (groupId == null || g.Id == groupId.Value))
            .ToListAsync(ct);

        var groups = dbGroups
            .Where(g => _context.Entry(g).State != EntityState.Deleted)
            .Concat(_context.RequestPoGroups.Local.Where(g =>
                g.RequestId == requestId &&
                (groupId == null || g.Id == groupId.Value) &&
                _context.Entry(g).State == EntityState.Added))
            .Distinct()
            .ToList();

        if (groups.Count == 0)
            return result;

        var payments = await LoadTrackedRequestRowsAsync(
            _context.RequestPayments, p => p.RequestId == requestId,
            _context.RequestPayments.Local.Where(p => p.RequestId == requestId), ct);

        var reconciliations = await LoadTrackedRequestRowsAsync(
            _context.RequestReconciliations, r => r.RequestId == requestId,
            _context.RequestReconciliations.Local.Where(r => r.RequestId == requestId), ct);

        var groupIds = groups.Select(g => g.Id).ToList();
        var approvedShortCloseGroupIds = (await _context.OperationInvoiceShortCloses
                .Where(c => groupIds.Contains(c.RequestPoGroupId))
                .ToListAsync(ct))
            .Where(c => _context.Entry(c).State != EntityState.Deleted &&
                        string.Equals(c.Status, RequestConstants.ShortCloseStatuses.Approved,
                            StringComparison.OrdinalIgnoreCase))
            .Select(c => c.RequestPoGroupId)
            .ToHashSet();

        foreach (var group in groups)
        {
            // Terminal states first: a cancelled group carries no obligations, and a COMPLETED
            // group is a strict no-op — no re-stamp, no duplicate history, no touch.
            if (StatusIs(group.Status, RequestConstants.PoGroupStatuses.Cancelled) ||
                StatusIs(group.Status, RequestConstants.PoGroupStatuses.Completed))
                continue;

            // R15 fail-closed: an unclassified group is SKIPPED, never thrown on and never
            // inferred over. It stays exactly where it is until the Release 5 classification tool.
            var unclassified = StatusIs(group.OperationInvoiceStatus,
                    RequestConstants.OperationInvoiceStatuses.Unclassified)
                || group.SourceDocumentType == null;
            if (unclassified)
                continue;

            // ── Lazy operational receipt (approved Phase 4 decision) ──
            // Pre-activation groups had no writer for the stamp; when the item records already
            // prove full receipt, the WRITE path derives the stamp here. The stamp time is the
            // evaluation instant — the physical receiving date is not provable and is never
            // fabricated; the history says so explicitly.
            if (group.OperationalReceiptCompletedAtUtc == null &&
                OperationalReceiptFacts.AreAllGroupItemsReceived(group))
            {
                var stampedAt = DateTime.UtcNow;
                group.OperationalReceiptCompletedAtUtc = stampedAt;
                group.OperationalReceiptCompletedByUserId = actorUserId;
                group.UpdatedAtUtc = stampedAt;

                await AddHistoryOnceAsync(
                    request, actorUserId,
                    actionTaken: WorkflowEventCodes.OperationalReceiptCompleted,
                    idempotencyKey: PostPaymentIdempotencyKeys.OperationalReceiptCompleted(group.Id),
                    comment: $"Recebimento operacional concluído no grupo {GroupLabel(group)}: " +
                             "derivado dos registos de recebimento pré-existentes (todos os itens " +
                             "recebidos). A data registada é a da avaliação, não a data física do " +
                             "recebimento.",
                    ct);
            }

            var projection = GroupCompletionProjector.Project(
                group, payments, reconciliations,
                hasApprovedShortClose: approvedShortCloseGroupIds.Contains(group.Id));

            if (projection.Complete)
            {
                group.Status = RequestConstants.PoGroupStatuses.Completed;
                group.CompletedAtUtc ??= DateTime.UtcNow;
                group.UpdatedAtUtc = DateTime.UtcNow;

                // Approved identity rule: the Fiscal Receipt keys the completion when one is
                // owed; the NOFR literal keys it when none is — never an empty GUID.
                var completionKey = group.RequiresSeparateFiscalReceipt
                    ? PostPaymentIdempotencyKeys.GroupCompleted(
                        group.Id, group.FiscalReceiptAttachmentId!.Value)
                    : PostPaymentIdempotencyKeys.GroupCompletedWithoutFiscalReceipt(group.Id);

                await AddHistoryOnceAsync(
                    request, actorUserId,
                    actionTaken: WorkflowEventCodes.GroupCompleted,
                    idempotencyKey: completionKey,
                    comment: $"Grupo {GroupLabel(group)} CONCLUÍDO: todas as obrigações " +
                             "pós-pagamento satisfeitas (P.O., pagamento, recebimento operacional, " +
                             "Fatura Final" +
                             (group.RequiresSeparateFiscalReceipt
                                 ? " e Recibo Fiscal)."
                                 : "); Recibo Fiscal separado não exigido pela classificação do documento.") +
                             (projection.ClosedShort
                                 ? " Obrigação de Fatura Final satisfeita por encerramento com saldo aceite."
                                 : string.Empty),
                    ct);

                result.CompletedGroupIds.Add(group.Id);
                result.AnyGroupCompleted = true;
            }
            else if (projection.ReadyForFiscalReceipt &&
                     !StatusIs(group.Status, RequestConstants.PoGroupStatuses.WaitingFiscalReceipt))
            {
                // Everything but the owed Fiscal Receipt is satisfied — the group enters the
                // antechamber. Written only when the status actually changes.
                group.Status = RequestConstants.PoGroupStatuses.WaitingFiscalReceipt;
                group.UpdatedAtUtc = DateTime.UtcNow;

                await AddHistoryOnceAsync(
                    request, actorUserId,
                    actionTaken: WorkflowEventCodes.FiscalReceiptUnlocked,
                    idempotencyKey: PostPaymentIdempotencyKeys.FiscalReceiptUnlocked(group.Id),
                    comment: $"Recibo Fiscal desbloqueado no grupo {GroupLabel(group)}: " +
                             "recebimento operacional e Fatura Final satisfeitos. Aguardando " +
                             "apenas o Recibo Fiscal do Financeiro.",
                    ct);
            }
        }

        // Optimization hint only — callers still invoke Phase 2 whenever their transaction
        // succeeded (see the interface remarks). Phase 2 itself arrives in Phase 4C.
        result.ParentEvaluationRequired = result.AnyGroupCompleted;
        return result;
    }

    /// <inheritdoc />
    public Task<ParentCompletionResult> EvaluateParentCompletionAsync(
        Guid requestId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        if (PostPaymentCompletionPolicy.IsCompletionDisabled(_options))
            return Task.FromResult(ParentCompletionResult.NoOp);

        // Contract guard — this is the whole reason the phase exists. Running the parent
        // evaluation inside the caller's transaction makes it read the caller's own uncommitted
        // state and miss sibling groups committed elsewhere, which is exactly the race that
        // leaves every group COMPLETED and the Request permanently open. A caller that reaches
        // here with an ambient transaction has a defect that no runtime fallback can repair, so
        // it fails loudly instead of silently degrading to the broken behaviour.
        if (_context.Database.CurrentTransaction != null)
        {
            _logger.LogError(
                "EvaluateParentCompletionAsync was called inside an ambient transaction for Request {RequestId}. " +
                "The parent evaluation must run AFTER the caller's transaction has committed.",
                requestId);

            throw new InvalidOperationException(
                "EvaluateParentCompletionAsync must be called after the caller's transaction has " +
                "committed, never inside it.");
        }

        // Phase 4C replaces this with the atomic transition of plan v7/R4:
        //   own ReadCommitted transaction; reload Request (+RowVersion); return AlreadyCompleted
        //   when already COMPLETED; re-read every active group; when all are COMPLETED, assign
        //   Request.CompletionCycleId, write REQUEST_COMPLETED (RC:{RequestId}:{CompletionCycleId})
        //   and enqueue RequestFinalized with the same CorrelationId, all in ONE SaveChanges +
        //   commit; on DbUpdateConcurrencyException reload once and retry, second failure returns
        //   ConflictUnresolved.
        throw new NotImplementedException(ParentNotActivatedMessage);
    }

    /// <summary>
    /// Tracked request-scoped rows reconciled with the change tracker: Deleted rows drop out,
    /// Added-but-unsaved rows join — the same in-transaction visibility rule the coverage
    /// service established.
    /// </summary>
    private async Task<List<T>> LoadTrackedRequestRowsAsync<T>(
        IQueryable<T> set,
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        IEnumerable<T> localCandidates,
        CancellationToken ct) where T : class
    {
        var fromDb = await set.Where(predicate).ToListAsync(ct);

        return fromDb
            .Where(e => _context.Entry(e).State != EntityState.Deleted)
            .Concat(localCandidates.Where(e => _context.Entry(e).State == EntityState.Added))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Adds one history row per business fact: the idempotency key is checked against both the
    /// change tracker (same-transaction retry) and the database (cross-transaction retry); the
    /// filtered unique index remains the concurrency backstop. Group-scoped events keep the
    /// parent status ids — the request status itself does not change in Phase 1.
    /// </summary>
    private async Task AddHistoryOnceAsync(
        Request request, Guid actorUserId, string actionTaken, string idempotencyKey,
        string comment, CancellationToken ct)
    {
        var exists = _context.RequestStatusHistories.Local
                         .Any(h => h.IdempotencyKey == idempotencyKey)
                     || await _context.RequestStatusHistories
                         .AnyAsync(h => h.IdempotencyKey == idempotencyKey, ct);
        if (exists) return;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorUserId,
            ActionTaken = actionTaken,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = comment,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static string GroupLabel(RequestPoGroup group) =>
        group.SupplierNameSnapshot ?? group.Id.ToString("D")[..8];

    private static bool StatusIs(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
