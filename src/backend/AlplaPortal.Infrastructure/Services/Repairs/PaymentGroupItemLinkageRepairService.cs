using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Repairs;

/// <summary>
/// v2.245.4 — controlled, atomic repair for PAYMENT groups whose <see cref="RequestLineItem"/>s were never
/// linked (<c>RequestPoGroupId = NULL</c>). The legacy header plan created the group without attributing the
/// request's items to it, so the receiving operation found no items for the group ("0/N, no conference
/// table") and the group could never be confirmed. Some of those groups were additionally pushed to
/// WAITING_RECEIPT by the (since deprecated) move-to-receipt action WITHOUT any operational confirmation —
/// a state v2.245.2's finalization guard treats as "confirmed".
///
/// <para><b>What it does, per eligible request, in ONE transaction:</b> (1) link every active unlinked item to
/// the request's single non-cancelled group; (2) when the group sits at WAITING_RECEIPT with NO
/// group-correlated confirmation but provable move-from-PAYMENT_COMPLETED + payment evidence, demote it to
/// PAYMENT_COMPLETED (the pre-confirmation state it provably came from); (3) reconcile the request scalar
/// exclusively through <see cref="IStatusAggregationService"/> (audited STATUS_SYNC); (4) write one technical
/// audit row. Any failure rolls the whole request back.</para>
///
/// <para><b>What it never does:</b> assign items when more than one active group exists (AMBIGUOUS); touch
/// items linked elsewhere (CONFLICTING); reopen terminal groups; write CONFIRM_RECEIVING /
/// OPERATIONAL_RECEIPT_COMPLETED / PAYMENT_COMPLETED; change quantities, item statuses, completion stamps or
/// attachments. PREVIEW and APPLY share <see cref="Classify"/> — APPLY can only touch what PREVIEW reports.</para>
///
/// <para><b>Confirmation correlation (strict):</b> a CONFIRM_RECEIVING counts for the current group only when
/// (a) an OPERATIONAL_RECEIPT_COMPLETED row carries this group's idempotency key, or (b) a CONFIRM_RECEIVING
/// row written after the group's creation carries this group's 8-character <c>GroupId:</c> tag, or (c) the
/// request has exactly ONE group ever, the CONFIRM_RECEIVING was written after that group's creation and
/// carries no group tag at all. A confirmation that exists but cannot be correlated makes the case
/// CONFLICTING — nothing is linked, changed or preserved by assumption.</para>
/// </summary>
public sealed class PaymentGroupItemLinkageRepairService
{
    public const string RepairActionCode = "PAYMENT_GROUP_ITEM_LINK_REPAIR";
    private const string ConfirmAction = "CONFIRM_RECEIVING";
    private const string MoveAction = "MOVE_TO_RECEIPT";
    private const string OpReceiptAction = "OPERATIONAL_RECEIPT_COMPLETED";
    private const string PaymentCompletedAction = "PAYMENT_COMPLETED";
    private const string AdvanceCompletedAction = "ADVANCE_PAYMENT_COMPLETED";
    private const string GroupTag = "GroupId: ";

    private static readonly string[] TerminalGroupStatuses =
    {
        RequestConstants.PoGroupStatuses.Completed,
        RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
        RequestConstants.PoGroupStatuses.Cancelled,
    };

    private readonly ApplicationDbContext _context;
    private readonly IStatusAggregationService _aggregator;

    public PaymentGroupItemLinkageRepairService(ApplicationDbContext context, IStatusAggregationService aggregator)
    {
        _context = context;
        _aggregator = aggregator;
    }

    // ── Decision model (shared by PREVIEW and APPLY) ─────────────────────────────────────────────
    private enum Kind { Link, LinkAndDemote, AlreadyHealthy, Ambiguous, Conflicting, Refused }

    private sealed record Decision(
        Kind Kind,
        RequestPoGroup? Group,
        IReadOnlyList<RequestLineItem> UnlinkedItems,
        string? TargetGroupStatus,
        PaymentGroupItemLinkageRowDto Row)
    {
        public bool IsRepairable => Kind is Kind.Link or Kind.LinkAndDemote;
    }

    public async Task<PaymentGroupItemLinkageRepairResultDto> RunAsync(
        bool apply, Guid actorId, string? reason, CancellationToken ct = default)
    {
        var result = new PaymentGroupItemLinkageRepairResultDto { Status = apply ? "APPLIED" : "PREVIEW" };

        // Population: PAYMENT requests with at least one active unlinked item, plus requests already carrying
        // this repair's audit (so a repaired request re-scans as ALREADY_HEALTHY — idempotency).
        var ids = await _context.Requests
            .Where(r => r.RequestType.Code == RequestConstants.Types.Payment
                        && (r.LineItems.Any(li => !li.IsDeleted && li.RequestPoGroupId == null)
                            || r.StatusHistories.Any(h => h.ActionTaken == RepairActionCode)))
            .Select(r => r.Id)
            .ToListAsync(ct);

        var statusCodeById = await _context.RequestStatuses.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Code, ct);

        foreach (var id in ids)
        {
            result.ScannedRequests++;

            if (!apply)
            {
                var previewRequest = await LoadAsync(id, track: false, ct);
                if (previewRequest == null) continue;
                var d = Classify(previewRequest, statusCodeById);
                Tally(result, d, apply: false);
                result.Rows.Add(d.Row);
                continue;
            }

            // APPLY — one atomic transaction per request. The same classifier runs on a fresh, tracked load.
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            PaymentGroupItemLinkageRowDto? row = null;
            try
            {
                var request = await LoadAsync(id, track: true, ct);
                if (request == null) { await tx.RollbackAsync(ct); continue; }

                var d = Classify(request, statusCodeById);
                row = d.Row;
                Tally(result, d, apply: true);
                if (!d.IsRepairable)
                {
                    await tx.RollbackAsync(ct); // nothing staged; make it explicit
                    result.Rows.Add(row);
                    continue;
                }

                var group = d.Group!;
                var previousGroupStatus = group.Status;

                // (1) Linkage — the only item mutation: the FK. Quantities/statuses are never touched.
                foreach (var li in d.UnlinkedItems)
                {
                    li.RequestPoGroupId = group.Id;
                    li.UpdatedAtUtc = DateTime.UtcNow;
                }

                // (2) Conditional status correction — provable pre-confirmation state only.
                if (d.Kind == Kind.LinkAndDemote)
                {
                    group.Status = d.TargetGroupStatus!;
                    group.UpdatedAtUtc = DateTime.UtcNow;
                    group.UpdatedByUserId = actorId;
                }

                // (4) Technical audit — staged now, persisted with everything else. Idempotent per group.
                var key = $"PAY_GROUP_LINK:{group.Id}";
                var auditExists = _context.RequestStatusHistories.Local.Any(h => h.IdempotencyKey == key)
                                  || await _context.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey == key, ct);
                if (!auditExists)
                {
                    _context.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = request.Id,
                        ActorUserId = actorId,
                        ActionTaken = RepairActionCode,
                        PreviousStatusId = request.StatusId,
                        NewStatusId = request.StatusId, // the scalar changes only through the aggregator (STATUS_SYNC)
                        Comment = $"[Reparo de vínculo de itens ao grupo | Grupo {group.SupplierNameSnapshot ?? group.Id.ToString().Substring(0, 8)}] " +
                                  $"{d.UnlinkedItems.Count} item(ns) vinculado(s) ao grupo. " +
                                  (d.Kind == Kind.LinkAndDemote
                                      ? $"Status do grupo corrigido de {previousGroupStatus} para {d.TargetGroupStatus} (sem confirmação de recebimento correlacionada; estado anterior comprovado). "
                                      : $"Status do grupo mantido em {previousGroupStatus}. ") +
                                  $"Nenhuma quantidade, confirmação ou evento de negócio fabricado. Motivo: {reason}",
                        IdempotencyKey = key,
                        CreatedAtUtc = DateTime.UtcNow,
                    });
                }

                // (3) Scalar reconciliation — canonical aggregator only. Persistence of everything staged above
                // happens here (its SaveChanges) or in the SaveChanges right after; a failure anywhere before
                // the commit leaves nothing persisted.
                await _aggregator.AggregateRequestStatusAsync(request.Id, actorId, ct);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                row.Decision = d.Kind == Kind.LinkAndDemote ? "REPAIRED_LINK_AND_DEMOTE" : "REPAIRED_LINK";
                row.GroupStatus = group.Status;
                result.Repaired++;
                result.Rows.Add(row);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                result.Errors++;
                row ??= new PaymentGroupItemLinkageRowDto { RequestId = id.ToString() };
                row.Decision = "ERROR";
                row.Reason = $"Falha ao reparar — transação revertida, nada persistido para este pedido. ({ex.GetType().Name})";
                result.Rows.Add(row);
            }
            finally
            {
                // Every request is loaded fresh; discarding the tracker guarantees a failed request never leaks
                // staged mutations into the next one (and, on providers without transactions, that a failure
                // before persistence leaves no trace).
                _context.ChangeTracker.Clear();
            }
        }

        result.Message = apply
            ? $"Reparo aplicado: {result.Repaired} grupo(s) reparado(s) de {result.ScannedRequests} pedido(s) PAGAMENTO examinado(s) " +
              $"({result.Ambiguous} ambíguo(s), {result.Conflicting} conflitante(s), {result.Refused} recusado(s), {result.Errors} erro(s))."
            : $"Pré-visualização: {result.WouldRepair} grupo(s) reparável(is) de {result.ScannedRequests} pedido(s) PAGAMENTO examinado(s) " +
              $"({result.Ambiguous} ambíguo(s), {result.Conflicting} conflitante(s), {result.Refused} recusado(s), {result.AlreadyHealthy} já saudável(is)).";
        return result;
    }

    private Task<Request?> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        IQueryable<Request> q = _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.PoGroups)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.StatusHistories)
            .Include(r => r.Payments)
            .Include(r => r.Attachments)
            .AsSplitQuery();
        if (!track) q = q.AsNoTracking();
        return q.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    private static void Tally(PaymentGroupItemLinkageRepairResultDto result, Decision d, bool apply)
    {
        if (d.Group != null || d.Kind is Kind.Ambiguous or Kind.Conflicting) result.ScannedGroups++;
        switch (d.Kind)
        {
            case Kind.Link:
            case Kind.LinkAndDemote:
                result.Eligible++;
                if (!apply) result.WouldRepair++;
                break;
            case Kind.AlreadyHealthy: result.AlreadyHealthy++; break;
            case Kind.Ambiguous: result.Ambiguous++; break;
            case Kind.Conflicting: result.Conflicting++; break;
            case Kind.Refused: result.Refused++; break;
        }
    }

    // ── The single classifier ─────────────────────────────────────────────────────────────────────
    private static Decision Classify(Request request, IReadOnlyDictionary<int, string> statusCodeById)
    {
        var requestStatus = request.Status?.Code;
        var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var nonCancelledGroups = request.PoGroups
            .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled).ToList();
        var group = nonCancelledGroups.Count == 1 ? nonCancelledGroups[0] : null;

        var unlinked = activeItems.Where(li => li.RequestPoGroupId == null).ToList();
        var foreign = activeItems.Where(li => li.RequestPoGroupId != null
                                              && !nonCancelledGroups.Any(g => g.Id == li.RequestPoGroupId)).ToList();
        var histories = request.StatusHistories;
        var repairAuditExists = histories.Any(h => h.ActionTaken == RepairActionCode);

        var row = new PaymentGroupItemLinkageRowDto
        {
            RequestNumber = request.RequestNumber ?? "",
            RequestId = request.Id.ToString(),
            PoGroupId = group?.Id.ToString(),
            RequestStatus = requestStatus,
            GroupStatus = group?.Status,
            TargetGroupStatus = group?.Status,
            ActiveItems = activeItems.Count,
            UnlinkedItems = unlinked.Count,
            ReceivedItems = activeItems.Count(li => li.LineItemStatus?.Code == "RECEIVED"),
            ReceiptPresent = request.Attachments.Any(a => a.AttachmentTypeCode == RequestAttachment.TYPE_RECEIPT
                                                          && !a.IsDeleted && a.VoidedAtUtc == null),
        };

        Decision Done(Kind kind, string decision, string reason, string? target = null)
        {
            row.Decision = decision;
            row.Reason = reason;
            if (target != null) row.TargetGroupStatus = target;
            return new Decision(kind, group, unlinked, target ?? group?.Status, row);
        }

        // Terminal request → never touched.
        if (requestStatus is RequestConstants.Statuses.Cancelled or RequestConstants.Statuses.Completed
                          or RequestConstants.Statuses.Rejected)
            return Done(Kind.Refused, "REFUSED", $"Pedido em estado terminal ({requestStatus}) — não alterado.");

        // Group identity must be unambiguous.
        if (nonCancelledGroups.Count == 0)
            return Done(Kind.Refused, "REFUSED", "Pedido sem grupo operacional ativo — fora do âmbito (ver reparação de grupos de P.O.).");
        if (nonCancelledGroups.Count > 1)
            return Done(Kind.Ambiguous, "AMBIGUOUS", $"{nonCancelledGroups.Count} grupos ativos — nunca se atribuem todos os itens a um único grupo.");

        // Linkage topology must be clean.
        if (foreign.Count > 0)
            return Done(Kind.Conflicting, "CONFLICTING", $"{foreign.Count} item(ns) vinculado(s) a um grupo cancelado ou de outro pedido — revisão manual.");
        if (unlinked.Count == 0)
            return repairAuditExists
                ? Done(Kind.AlreadyHealthy, "ALREADY_HEALTHY", "Todos os itens já vinculados ao grupo (reparo anterior aplicado).")
                : Done(Kind.AlreadyHealthy, "ALREADY_HEALTHY", "Todos os itens já vinculados ao grupo.");

        // Terminal group → never reopened.
        if (TerminalGroupStatuses.Contains(group!.Status))
            return Done(Kind.Refused, "REFUSED", $"Grupo em estado terminal ({group.Status}) — não alterado.");

        // Evidence.
        var confirms = histories.Where(h => h.ActionTaken == ConfirmAction).ToList();
        var confirmCorrelated = HasCorrelatedConfirmation(request, group, histories);
        row.ConfirmEvidence = confirmCorrelated ? "CORRELATED" : confirms.Count > 0 ? "UNCORRELATED" : "NONE";
        row.PaymentEvidence = histories.Any(h => h.ActionTaken == PaymentCompletedAction)
                              || request.Payments.Any(p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance
                                                           && p.PaymentStatus == RequestPayment.PaymentStatuses.Completed);
        row.AdvanceEvidence = histories.Any(h => h.ActionTaken == AdvanceCompletedAction)
                              || request.Payments.Any(p => p.PaymentType == RequestPayment.PaymentTypes.Advance);
        row.MoveEvidence = HasCorrelatedMoveFromPaymentCompleted(request, group, histories, statusCodeById);

        // A confirmation exists but cannot be tied to THIS group → fail closed, touch nothing.
        if (confirms.Count > 0 && !confirmCorrelated)
            return Done(Kind.Conflicting, "CONFLICTING",
                "Existe CONFIRM_RECEIVING que não pode ser correlacionado com o grupo atual — revisão manual (nada alterado).");

        if (group.Status == RequestConstants.PoGroupStatuses.WaitingReceipt)
        {
            if (confirmCorrelated)
                return Done(Kind.Link, "REPAIR_LINK",
                    "Confirmação de recebimento correlacionada com o grupo — itens vinculados, WAITING_RECEIPT preservado.");

            // No confirmation at all: WAITING_RECEIPT is premature. Demote ONLY to a provable prior state.
            if (row.MoveEvidence && row.PaymentEvidence && !row.AdvanceEvidence)
                return Done(Kind.LinkAndDemote, "REPAIR_LINK_AND_DEMOTE",
                    "WAITING_RECEIPT sem confirmação; movido a partir de PAYMENT_COMPLETED com pagamento concluído — itens vinculados e grupo devolvido a PAYMENT_COMPLETED.",
                    target: RequestConstants.PoGroupStatuses.PaymentCompleted);

            return Done(Kind.Refused, "REFUSED",
                row.AdvanceEvidence
                    ? "WAITING_RECEIPT sem confirmação num fluxo com adiantamento — estado anterior não comprovável, revisão manual."
                    : "WAITING_RECEIPT sem confirmação e sem evidência de movimento/pagamento — estado anterior não comprovável, revisão manual.");
        }

        // Any other non-terminal (pre-confirmation) status: linkage only, status untouched.
        return Done(Kind.Link, "REPAIR_LINK", $"Itens vinculados ao grupo; status {group.Status} mantido.");
    }

    // ── Correlation rules ─────────────────────────────────────────────────────────────────────────
    private static string Tag(Guid groupId) => GroupTag + groupId.ToString().Substring(0, 8);

    private static bool HasCorrelatedConfirmation(Request request, RequestPoGroup group, IEnumerable<RequestStatusHistory> histories)
    {
        var list = histories.ToList();
        // (a) the operational-receipt stamp carries the FULL group id.
        var orKey = PostPaymentIdempotencyKeys.OperationalReceiptCompleted(group.Id);
        if (list.Any(h => h.ActionTaken == OpReceiptAction && h.IdempotencyKey == orKey)) return true;

        var confirms = list.Where(h => h.ActionTaken == ConfirmAction && h.CreatedAtUtc >= group.CreatedAtUtc).ToList();
        // (b) explicit group tag written by ConfirmReceiving.
        if (confirms.Any(h => h.Comment != null && h.Comment.Contains(Tag(group.Id), StringComparison.Ordinal))) return true;
        // (c) strict single-group fallback: the request never had another group, the event post-dates this
        //     group and carries NO group tag at all (a tag for a different group is never accepted).
        var onlyGroupEver = request.PoGroups.Count == 1;
        return onlyGroupEver && confirms.Any(h => h.Comment == null || !h.Comment.Contains(GroupTag, StringComparison.Ordinal));
    }

    private static bool HasCorrelatedMoveFromPaymentCompleted(
        Request request, RequestPoGroup group, IEnumerable<RequestStatusHistory> histories,
        IReadOnlyDictionary<int, string> statusCodeById)
    {
        var onlyGroupEver = request.PoGroups.Count == 1;
        return histories.Any(h =>
            h.ActionTaken == MoveAction
            && h.CreatedAtUtc >= group.CreatedAtUtc
            && h.PreviousStatusId.HasValue
            && statusCodeById.TryGetValue(h.PreviousStatusId.Value, out var prev)
            && prev == RequestConstants.Statuses.PaymentCompleted
            && ((h.Comment != null && h.Comment.Contains(Tag(group.Id), StringComparison.Ordinal))
                || (onlyGroupEver && (h.Comment == null || !h.Comment.Contains(GroupTag, StringComparison.Ordinal)))));
    }
}
