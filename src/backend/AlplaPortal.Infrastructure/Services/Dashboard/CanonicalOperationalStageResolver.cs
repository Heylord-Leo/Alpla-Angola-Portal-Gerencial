using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

// ── Dashboard V2 B9.2 / B9.2b — pure canonical stage resolver. ──
// Maps an operational entity's CURRENT persisted status to the single canonical B9 AGING stage it occupies.
//
// Pipeline (B6) vs Aging (B9) — the closed B9.2b distinction:
//   • B6 PipelineStage is INFORMATIONAL and MAY OVERLAP (one entity can appear in several stages at once,
//     e.g. a paid group shows in both FIN_PAID and REC_READY).
//   • B9 AgingStage is EXCLUSIVE: it answers "where is this entity currently accumulating operational dwell
//     time?" — exactly ONE active aging clock per entity (enforced by the OperationalStageState unique key).
// The two therefore do NOT map 1:1 for overlap stages; the resolver returns the exclusive dwell owner. Codes
// still come from the SAME vocabulary (Application-layer PipelineStages) so names never drift, but not every
// PipelineStages value is an active aging stage (FIN_PAID, DRAFT, COMPLETED are B6-only).
// It is a PURE string→string map: no DB access, no navigations, and NEVER consults CreatedAtUtc /
// UpdatedAtUtc / NeedByDateUtc / ScheduledDateUtc (those are creation/edit/deadline dates, not stage age).
//
// Returns null when the entity's current status is NOT an active B9 aging stage (pre-active or terminal) —
// the caller then removes any snapshot and, for a real exit, records a terminal history event whose
// ToStageCode comes from ResolveTerminalCode.
public static class CanonicalOperationalStageResolver
{
    /// <summary>APPROVAL_BATCH grain: map ApprovalBatch.Status → AREA_APPROVAL | FINAL_APPROVAL | ADJUSTMENT, else null.</summary>
    public static string? ResolveApprovalBatchStage(string? batchStatus) => batchStatus switch
    {
        RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval => PipelineStages.AreaApproval,
        RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval => PipelineStages.FinalApproval,
        RequestConstants.ApprovalBatchStatuses.AreaAdjustment => PipelineStages.Adjustment,
        RequestConstants.ApprovalBatchStatuses.FinalAdjustment => PipelineStages.Adjustment,
        _ => null, // APPROVED (batch done), REJECTED, CANCELLED, and any other → not an active aging stage
    };

    /// <summary>PO_GROUP grain: map RequestPoGroup.Status → the single canonical stage the group occupies, else null.</summary>
    public static string? ResolvePoGroupStage(string? groupStatus) => groupStatus switch
    {
        RequestConstants.PoGroupStatuses.WaitingPo => PipelineStages.PoWaiting,
        RequestConstants.PoGroupStatuses.WaitingPoCorrection => PipelineStages.PoCorrection,

        // Finance: obligation exists and must be scheduled → paid.
        RequestConstants.PoGroupStatuses.PoIssued => PipelineStages.FinanceNeedsScheduling,
        RequestConstants.PoGroupStatuses.PaymentRequestSent => PipelineStages.FinanceNeedsScheduling,
        RequestConstants.PoGroupStatuses.AdvancePaymentRequired => PipelineStages.FinanceNeedsScheduling,
        RequestConstants.PoGroupStatuses.PaymentScheduled => PipelineStages.FinanceScheduled,
        RequestConstants.PoGroupStatuses.AdvancePaymentScheduled => PipelineStages.FinanceScheduled,

        // Receiving. EXCLUSIVE-AGING POLICY (B9.2b): once payment is COMPLETED, Finance has finished its
        // action and the group is IMMEDIATELY Receiving-actionable (ReceivingActionEvaluator: PAYMENT_COMPLETED
        // → MoveToReceipt available → bucket READY_FOR_RECEIPT). So the dwell clock belongs to Receiving:
        // PAYMENT_COMPLETED → REC_READY, NOT FIN_PAID. FIN_PAID stays a B6 informational overlap code (a paid
        // group is still shown in Finance's "pago/aguardando recebimento" pipeline stage) but is NOT a B9
        // active aging stage — B9 tracks a single exclusive dwell owner per entity. No source status rests in
        // "paid but not yet receiving-actionable", so there is no FIN_PAID aging period to lose.
        RequestConstants.PoGroupStatuses.PaymentCompleted => PipelineStages.ReceivingReady,
        // ADVANCE_PAYMENT_COMPLETED is a transient advance marker (ConfirmAdvancePayment immediately parks the
        // group at WAITING_SUPPLIER_DELIVERY in the same operation) — never a resting aging stage.
        RequestConstants.PoGroupStatuses.AdvancePaymentCompleted => null,

        // Receiving.
        RequestConstants.PoGroupStatuses.WaitingReceipt => PipelineStages.ReceivingWaiting,
        RequestConstants.PoGroupStatuses.InFollowup => PipelineStages.ReceivingFollowup,
        RequestConstants.PoGroupStatuses.WaitingSupplierDelivery => PipelineStages.ReceivingSupplier,

        // Documentation (fiscal / reconciliation).
        RequestConstants.PoGroupStatuses.WaitingFiscalReceipt => PipelineStages.Documentation,
        RequestConstants.PoGroupStatuses.WaitingReconciliation => PipelineStages.Documentation,

        // PENDING (pre-final-approval), COMPLETED, CANCELLED and anything else → not an active aging stage.
        _ => null,
    };

    /// <summary>
    /// FUTURE / NOT ACTIVE (B9.2d scope closure): Buyer/REQUEST aging is formally OUT OF SCOPE for the
    /// current B9 release — the Buyer domain has no cheap authoritative transition source, so capturing it
    /// would require heavy per-write request-level graph loads solely for B9 (see B9.2c audit). Buyer stays
    /// visible through B6 Pipeline (NEEDS_QUOTATION/PARTIAL_COVERAGE/READY_FOR_APPROVAL populations) and B8
    /// Alerts (NeedByDate urgency). This pure map is kept, dormant and tested, so a FUTURE release can wire
    /// live Buyer capture if a cheaper transition source appears. It is intentionally NOT called by B9
    /// capture, and the B9 read side must not expect Buyer REQUEST snapshots. The caller supplies the
    /// already-resolved Buyer operational state (BuyerQueueConstants.OperationalStates); this never computes
    /// buyer actionability itself.
    /// </summary>
    public static string? ResolveBuyerStage(string? buyerOperationalState) => buyerOperationalState switch
    {
        BuyerQueueConstants.OperationalStates.NeedsQuotation => PipelineStages.NeedsQuotation,
        BuyerQueueConstants.OperationalStates.PartialCoverage => PipelineStages.PartialCoverage,
        BuyerQueueConstants.OperationalStates.ReadyForApproval => PipelineStages.ReadyForApproval,
        _ => null,
    };

    /// <summary>The canonical B6 domain that owns a given B9 stage code.</summary>
    public static string DomainForStage(string stageCode) => stageCode switch
    {
        PipelineStages.NeedsQuotation or PipelineStages.PartialCoverage or PipelineStages.ReadyForApproval
            => PipelineDomains.Compras,
        PipelineStages.AreaApproval or PipelineStages.FinalApproval or PipelineStages.Adjustment
            => PipelineDomains.Aprovacoes,
        PipelineStages.PoWaiting or PipelineStages.PoCorrection
            => PipelineDomains.Po,
        PipelineStages.FinanceNeedsScheduling or PipelineStages.FinanceScheduled or PipelineStages.FinancePaid
            => PipelineDomains.Financas,
        PipelineStages.ReceivingReady or PipelineStages.ReceivingWaiting
            or PipelineStages.ReceivingFollowup or PipelineStages.ReceivingSupplier
            => PipelineDomains.Recebimento,
        PipelineStages.Documentation
            => PipelineDomains.Documentacao,
        _ => PipelineDomains.Conclusao,
    };

    /// <summary>
    /// History-only terminal code for a real EXIT (entity left an active stage into a non-aging state).
    /// Never used as a snapshot StageCode. Keeps auditability without polluting the aging taxonomy.
    /// </summary>
    public static string ResolveTerminalCode(string? rawStatus) => rawStatus switch
    {
        RequestConstants.PoGroupStatuses.Completed => OperationalStageTerminalCodes.Completed,
        RequestConstants.ApprovalBatchStatuses.Approved => OperationalStageTerminalCodes.Completed,
        RequestConstants.PoGroupStatuses.Cancelled => OperationalStageTerminalCodes.Cancelled,
        RequestConstants.ApprovalBatchStatuses.Rejected => OperationalStageTerminalCodes.Rejected,
        _ => OperationalStageTerminalCodes.Exited, // e.g. PENDING (de-activated) or any other out-of-scope status
    };
}

/// <summary>Terminal ToStageCode values allowed in HISTORY ONLY — never a snapshot StageCode, never a B9 aging stage.</summary>
public static class OperationalStageTerminalCodes
{
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string Rejected = "REJECTED";
    public const string Exited = "EXITED";
}
