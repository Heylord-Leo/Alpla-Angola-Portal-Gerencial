using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// One logical lot's STATUS/PROGRESS timeline for the Requests-list expanded row.
/// Not an audit-event history: stages derive from the unit's current lifecycle state,
/// no actors/comments/dates are attached and future stages are never fabricated.
/// </summary>
// At = the lot-specific timestamp for THIS step, attached ONLY when a direct persisted event records the
// transition (Phase 3E.2) — never inferred, never a generic Request.CreatedAt/UpdatedAt fallback. Null
// means "no recorded timestamp": a reached step then renders "Data não registada", a future step
// "Ainda não iniciado" — the presentation layer disambiguates via State.
public sealed record LotTimelineStep(string Label, string State, DateTimeOffset? At = null); // completed | current | pending | blocked

public sealed record LotTimeline(
    string UnitType,            // "BATCH" | "GROUP"
    Guid UnitId,
    int? LotNumber,             // REAL ApprovalBatch.BatchNumber only — never fabricated
    string Label,
    string? SupplierName,
    decimal TotalAmount,
    string? CurrencyCode,
    string? PurchaseOrderNumber,
    string StatusCode,
    string StatusLabel,
    IReadOnlyList<LotTimelineStep> Steps);

/// <summary>
/// Pure, static builder for the per-lot timelines of the Requests-list expanded row.
/// Logical-lot resolution is NOT re-implemented here: it reuses the v2.230.0 workflow
/// projection's units (batch∪group correlated by ApprovalBatchId, batch+PENDING-group
/// dedupe, superseded batches excluded, cancelled groups excluded, terminal requests
/// yield zero units). Lots are produced only for requests with ≥ 2 logical units — the
/// single-lot/PAYMENT/legacy timeline remains the existing request-level Steps path.
/// </summary>
public static class RequestLotTimelineBuilder
{
    public const string StageQuotation = "Cotação";
    public const string StageApprovals = "Aprovações";
    public const string StagePo = "P.O. / Contratação";
    public const string StagePayment = "Pagamento";
    public const string StageReceiving = "Recebimento / Execução";
    public const string StageFiscal = "Documentação Fiscal";
    public const string StageCompleted = "Concluído";

    private const string Done = "completed";
    private const string Current = "current";
    private const string Pending = "pending";
    private const string Blocked = "blocked";

    public static IReadOnlyList<LotTimeline> BuildLots(Request request, RequestWorkflowProjection projection)
    {
        // v2.230.0 historical compatibility: ANY reconstructible operational unit (even a single
        // legacy/batchless group) renders the unit-based timeline — the group lifecycle is the
        // authority. Only unit-less requests (class A: no batch, no group) keep the legacy
        // Request-level timeline; terminal requests already yield zero units upstream.
        if (projection.Units.Count < 1) return Array.Empty<LotTimeline>();

        var lots = new List<LotTimeline>(projection.Units.Count);
        foreach (var unit in projection.Units)
        {
            lots.Add(new LotTimeline(
                unit.UnitType,
                unit.UnitId,
                ResolveLotNumber(request, unit),
                unit.Label,
                unit.SupplierName,
                unit.TotalAmount,
                unit.CurrencyCode,
                unit.PurchaseOrderNumber,
                unit.StatusCode,
                unit.StatusLabel,
                MapStages(request, unit)));
        }
        return lots;
    }

    /// <summary>
    /// The lot number is REAL domain identity only: the unit's own BatchNumber (batch units),
    /// or the BatchNumber of the group's origin batch resolved structurally through
    /// RequestPoGroup.ApprovalBatchId. Legacy/batchless groups have none — never fabricated.
    /// </summary>
    public static int? ResolveLotNumber(Request request, WorkflowUnit unit)
    {
        if (unit.BatchNumber.HasValue) return unit.BatchNumber;
        if (unit.UnitType != "GROUP") return null;

        var group = request.PoGroups.FirstOrDefault(g => g.Id == unit.UnitId);
        if (group?.ApprovalBatchId == null) return null;
        return request.ApprovalBatches.FirstOrDefault(b => b.Id == group.ApprovalBatchId.Value)?.BatchNumber;
    }

    /// <summary>
    /// Semantic stage mapping (explicit, never numeric ordering). Receiving/Execução and
    /// Documentação Fiscal are DISTINCT stages: WAITING_RECONCILIATION/IN_FOLLOWUP (and the
    /// supplier-delivery wait) are receiving-phase work, while WAITING_RECEIPT and
    /// WAITING_FISCAL_RECEIPT are fiscal-documentation phase — the established v2.229.9
    /// semantics: WAITING_RECEIPT is entered only AFTER receiving completes and waits for the
    /// supplier's receipt document. WAITING_PO_CORRECTION stays PO-stage work. The
    /// advance-payment track legitimately shows payment activity while the P.O. stage is
    /// still open (that is the domain truth of adiantamento-antes-da-P.O.).
    /// </summary>
    public static IReadOnlyList<LotTimelineStep> MapStages(Request request, WorkflowUnit unit)
    {
        if (unit.UnitType == "BATCH")
        {
            var approvals = unit.ApprovalState == "ADJUSTMENT" ? Blocked : Current;
            // The batch-creation event IS the quotation→approval transition for this lot: quotation
            // coverage was consolidated and the lot entered approval at ApprovalBatch.CreatedAtUtc.
            var batch = request.ApprovalBatches.FirstOrDefault(b => b.Id == unit.UnitId);
            var enteredApproval = AsOffset(batch?.CreatedAtUtc);
            return new[]
            {
                new LotTimelineStep(StageQuotation, Done, enteredApproval),
                new LotTimelineStep(StageApprovals, approvals, enteredApproval),
                new LotTimelineStep(StagePo, Pending),
                new LotTimelineStep(StagePayment, Pending),
                new LotTimelineStep(StageReceiving, Pending),
                new LotTimelineStep(StageFiscal, Pending),
                new LotTimelineStep(StageCompleted, Pending),
            };
        }

        var approvalsState = unit.StatusCode == RequestConstants.PoGroupStatuses.Pending ? Current : Done;

        var poState = unit.PoState switch
        {
            "ISSUED" => Done,
            "CORRECTION" => Current,                       // correction is PO-stage work
            _ => approvalsState == Done ? Current : Pending,
        };

        var paymentState = unit.PaymentState switch
        {
            "COMPLETE" => Done,
            "SCHEDULED" or "ADVANCE_IN_PROGRESS" => Current,
            "PENDING" => poState == Done ? Current : Pending,
            _ => Pending,
        };

        // WAITING_SUPPLIER_DELIVERY is execution-phase work (the supplier owes delivery),
        // matching the request-level "Recebimento / Execução" stage semantics.
        // WAITING_RECEIPT is NOT receiving work: per the established v2.229.9 semantics the
        // goods are already received and the supplier's receipt (fiscal document) is owed —
        // its ReceivingState arrives COMPLETE and its CompletionState WAITING_SUPPLIER_RECEIPT,
        // so it lands as Recebimento=completed / Documentação Fiscal=current below.
        var receivingState = unit.StatusCode == RequestConstants.PoGroupStatuses.WaitingSupplierDelivery
            ? Current
            : unit.ReceivingState switch
            {
                "COMPLETE" => Done,
                "IN_PROGRESS" => Current,
                "PENDING" => Current,                      // payment done — receiving is next
                _ => Pending,
            };

        var fiscalState = unit.CompletionState switch
        {
            "COMPLETE" => Done,
            "WAITING_FISCAL_RECEIPT" or "WAITING_SUPPLIER_RECEIPT" => Current,
            _ => Pending,
        };

        var completedState = unit.StatusCode == RequestConstants.PoGroupStatuses.Completed ? Done : Pending;

        // Lot-specific timestamps ONLY from directly-recorded group events (no inference). Stages without
        // a recorded event carry null and render as "Data não registada" (reached) / future label.
        var group = request.PoGroups.FirstOrDefault(g => g.Id == unit.UnitId);
        var receivingAt = receivingState == Done ? AsOffset(group?.OperationalReceiptCompletedAtUtc) : null;
        var fiscalAt = fiscalState == Done ? AsOffset(group?.FiscalReceiptUploadedAtUtc) : null;
        var completedAt = completedState == Done ? AsOffset(group?.CompletedAtUtc) : null;

        return new[]
        {
            new LotTimelineStep(StageQuotation, Done),
            new LotTimelineStep(StageApprovals, approvalsState),
            new LotTimelineStep(StagePo, poState),
            new LotTimelineStep(StagePayment, paymentState),
            new LotTimelineStep(StageReceiving, receivingState, receivingAt),
            new LotTimelineStep(StageFiscal, fiscalState, fiscalAt),
            new LotTimelineStep(StageCompleted, completedState, completedAt),
        };
    }

    private static DateTimeOffset? AsOffset(DateTime? utc) =>
        utc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)) : null;
}
