using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Requests;

/// <summary>
/// Two-phase Post-Payment Completion evaluation — REAL since Phase 4C.
///
/// <para><b>Phase 1</b> (GROUP completion) derives the <see cref="GroupCompletionProjector"/>
/// reading of every evaluated group inside the caller's transaction and performs the
/// WAITING_FISCAL_RECEIPT / COMPLETED transitions with their idempotent history.</para>
///
/// <para><b>Phase 2</b> (PARENT completion) runs strictly AFTER the caller's commit, in its own
/// short transaction, and is the SINGLE authoritative writer that transitions a grouped,
/// classified Request to COMPLETED — assigning <c>CompletionCycleId</c> exactly once, writing
/// REQUEST_COMPLETED (<c>RC:{RequestId}:{CompletionCycleId}</c>) and emitting the
/// RequestFinalized notification with the cycle id as correlation identity. COMPLETED is
/// terminal: an already-completed request returns AlreadyCompleted with the persisted cycle id
/// and produces nothing new.</para>
///
/// <para>While <c>Enabled &amp;&amp; CompletionEnabled</c> is not true — the committed default and
/// the current TEST state — both methods are pure no-ops that read nothing and write nothing.</para>
/// </summary>
public class RequestCompletionService : IRequestCompletionService
{
    private readonly ApplicationDbContext _context;
    private readonly PostPaymentCompletionOptions _options;
    private readonly ILogger<RequestCompletionService> _logger;
    private readonly IWorkflowNotificationOrchestrator? _orchestrator;

    /// <summary>
    /// The orchestrator is optional-by-default so the many existing direct constructions (tests)
    /// keep compiling; the DI container always supplies the registered instance in production.
    /// A null orchestrator only skips the non-critical notification, never the transition.
    /// </summary>
    public RequestCompletionService(
        ApplicationDbContext context,
        IOptions<PostPaymentCompletionOptions> options,
        ILogger<RequestCompletionService> logger,
        IWorkflowNotificationOrchestrator? orchestrator = null)
    {
        _context = context;
        _options = options?.Value ?? new PostPaymentCompletionOptions();
        _logger = logger;
        _orchestrator = orchestrator;
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
    public async Task<ParentCompletionResult> EvaluateParentCompletionAsync(
        Guid requestId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        if (PostPaymentCompletionPolicy.IsCompletionDisabled(_options))
            return ParentCompletionResult.NoOp;

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

        // Retry-once semantics: a DbUpdateConcurrencyException means another evaluator raced us
        // on Request.RowVersion. The retry reloads committed state — if the winner completed the
        // request, the reload returns AlreadyCompleted with the winner's persisted cycle id and
        // never generates another one. A second consecutive conflict is surfaced, not looped on.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return await TryParentTransitionAsync(requestId, actorUserId, ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _context.ChangeTracker.Clear();

                if (attempt == 2)
                {
                    _logger.LogError(ex,
                        "Parent completion for Request {RequestId} lost two consecutive concurrency " +
                        "races. The dimension change is committed and unaffected; the request stays " +
                        "open until the next trigger or the recovery sweep re-evaluates it.",
                        requestId);
                    return new ParentCompletionResult { ConflictUnresolved = true };
                }

                _logger.LogInformation(
                    "Parent completion for Request {RequestId} hit a concurrency conflict; retrying once.",
                    requestId);
            }
        }

        return new ParentCompletionResult { ConflictUnresolved = true }; // unreachable
    }

    /// <summary>
    /// One atomic parent-transition attempt in its OWN short transaction over freshly reloaded
    /// state — never the caller's possibly-stale change tracker.
    /// </summary>
    private async Task<ParentCompletionResult> TryParentTransitionAsync(
        Guid requestId, Guid actorUserId, CancellationToken ct)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        var request = await _context.Requests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null)
        {
            await tx.RollbackAsync(ct);
            return new ParentCompletionResult
            {
                ErrorMessage = $"Request {requestId} not found for parent completion evaluation."
            };
        }

        // The scoped DbContext may still track this request from Phase 1 — refresh it so the
        // decision (and the RowVersion the UPDATE will be guarded by) reflects the DATABASE'S
        // committed state, not a stale in-memory copy.
        await _context.Entry(request).ReloadAsync(ct);

        var statusCode = await _context.RequestStatuses.AsNoTracking()
            .Where(s => s.Id == request.StatusId)
            .Select(s => s.Code)
            .FirstOrDefaultAsync(ct);

        if (string.Equals(statusCode, RequestConstants.Statuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            // Terminal and idempotent: the persisted cycle id is returned unchanged; no history,
            // no notification, no new identity — ever.
            await tx.RollbackAsync(ct);
            return new ParentCompletionResult
            {
                AlreadyCompleted = true,
                CompletionCycleId = request.CompletionCycleId
            };
        }

        if (request.IsCancelled ||
            string.Equals(statusCode, RequestConstants.Statuses.Rejected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(statusCode, RequestConstants.Statuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(ct);
            return ParentCompletionResult.NoOp;
        }

        // Fresh, tracker-independent group reads: the Phase-1 commitments are what Phase 2
        // trusts — group obligations are never re-derived here.
        var groups = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => g.RequestId == requestId)
            .Select(g => new { g.Status, g.OperationInvoiceStatus, g.SourceDocumentType })
            .ToListAsync(ct);

        // Zero groups → the groupless legacy flow owns completion; Phase 4 never touches it.
        if (groups.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return ParentCompletionResult.NoOp;
        }

        var relevant = groups
            .Where(g => !StatusIs(g.Status, RequestConstants.PoGroupStatuses.Cancelled))
            .ToList();

        // All groups cancelled → nothing was fulfilled; cancellation flows own that request.
        if (relevant.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return ParentCompletionResult.NoOp;
        }

        // R15 fail-closed: an UNCLASSIFIED group can never contribute to completion — even one
        // blocks the parent until the Release 5 classification tool resolves it.
        var anyUnclassified = relevant.Any(g =>
            g.SourceDocumentType == null ||
            StatusIs(g.OperationInvoiceStatus, RequestConstants.OperationInvoiceStatuses.Unclassified));

        var anyIncomplete = relevant.Any(g =>
            !StatusIs(g.Status, RequestConstants.PoGroupStatuses.Completed));

        // Request-level blocker: ANY active reconciliation of this request — including rows with
        // a null RequestPoGroupId, which cannot be attributed to a group and would otherwise
        // slip past every group-scoped predicate.
        var activeReconciliation = await _context.RequestReconciliations.AsNoTracking()
            .AnyAsync(r => r.RequestId == requestId &&
                           (r.ReconciliationStatus == RequestReconciliation.ReconciliationStatuses.Draft ||
                            r.ReconciliationStatus == RequestReconciliation.ReconciliationStatuses.InProgress),
                ct);

        if (anyUnclassified || anyIncomplete || activeReconciliation)
        {
            await tx.RollbackAsync(ct);
            return ParentCompletionResult.NoOp;
        }

        var completedStatus = await _context.RequestStatuses
            .FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.Completed, ct);
        if (completedStatus == null)
        {
            await tx.RollbackAsync(ct);
            return new ParentCompletionResult
            {
                ErrorMessage = "Status COMPLETED is not configured — parent completion aborted."
            };
        }

        // ── The winning transition: identity + status + history in ONE SaveChanges/commit ──
        var cycleId = Guid.NewGuid();
        var completedAt = DateTime.UtcNow;
        var previousStatusId = request.StatusId;

        request.CompletionCycleId = cycleId;
        request.StatusId = completedStatus.Id;
        request.UpdatedAtUtc = completedAt;
        request.UpdatedByUserId = actorUserId;

        var key = PostPaymentIdempotencyKeys.RequestCompleted(requestId, cycleId);
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorUserId,
            ActionTaken = "REQUEST_COMPLETED",
            PreviousStatusId = previousStatusId,
            NewStatusId = completedStatus.Id,
            Comment = $"Pedido CONCLUÍDO pelo fluxo de conclusão pós-pagamento: " +
                      $"{relevant.Count} grupo(s) com todas as obrigações satisfeitas. " +
                      $"Ciclo de conclusão {cycleId:D}.",
            IdempotencyKey = key,
            CreatedAtUtc = completedAt
        });

        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Post-commit, non-critical notification: same RequestFinalized mechanism the legacy
        // finalization used, correlated by the completion cycle so retries can never duplicate.
        await EmitRequestFinalizedAsync(request, actorUserId, cycleId);

        _logger.LogInformation(
            "Request {RequestId} completed by the post-payment completion workflow " +
            "(cycle {CycleId}, {GroupCount} group(s)).",
            requestId, cycleId, relevant.Count);

        return new ParentCompletionResult
        {
            RequestCompleted = true,
            CompletionCycleId = cycleId
        };
    }

    private async Task EmitRequestFinalizedAsync(Request request, Guid actorUserId, Guid cycleId)
    {
        if (_orchestrator == null) return;

        try
        {
            var actorName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == actorUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();

            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.RequestFinalized,
                RequestId = request.Id,
                RequestNumber = request.RequestNumber ?? "S/N",
                RequestTitle = request.Title ?? "",
                TargetStatusCode = RequestConstants.Statuses.Completed,
                ActionTaken = "REQUEST_COMPLETED",
                ActorUserId = actorUserId,
                ActorName = actorName ?? "Sistema",
                CorrelationId = cycleId,
                RequesterId = request.RequesterId,
                BuyerId = request.BuyerId,
                AreaApproverId = request.AreaApproverId,
                FinalApproverId = request.FinalApproverId,
                PlantId = request.PlantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Non-critical: RequestFinalized notification failed for completed Request {RequestId} (cycle {CycleId}).",
                request.Id, cycleId);
        }
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
