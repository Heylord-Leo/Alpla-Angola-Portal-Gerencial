namespace AlplaPortal.Domain.Constants;

public static class RequestConstants
{
    public static class Types
    {
        public const string Quotation = "QUOTATION";
        public const string Payment = "PAYMENT";
    }

    /// <summary>
    /// Need levels (Grau de Necessidade) and the minimum lead time each one requires between
    /// today and the NeedByDateUtc ("Necessário até") of a Quotation request.
    /// Mirrored on the frontend in src/frontend/src/lib/needByDate.ts.
    /// </summary>
    public static class NeedLevels
    {
        public const string Critico = "CRITICO";
        public const string Urgente = "URGENTE";
        public const string Normal = "NORMAL";
        public const string Baixo = "BAIXO";

        /// <summary>
        /// Minimum days between today and "Necessário até", keyed by NeedLevel.Code.
        /// Need levels are editable Master Data: a code absent from this map carries no minimum.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, int> MinLeadDays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Critico] = 0,   // Imediato — pode ser hoje
            [Urgente] = 1,   // 24 horas
            [Normal] = 7,    // 7 dias
            [Baixo] = 15     // 15 dias
        };

        /// <summary>
        /// Earliest NeedByDate accepted for the given need level code, or null when unconstrained.
        /// </summary>
        public static DateTime? GetMinNeedByDate(string? needLevelCode, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(needLevelCode)) return null;
            if (!MinLeadDays.TryGetValue(needLevelCode, out var days)) return null;
            return today.Date.AddDays(days);
        }
    }

    public static class Statuses
    {
        public const string Draft = "DRAFT";
        public const string WaitingQuotation = "WAITING_QUOTATION";
        public const string WaitingAreaApproval = "WAITING_AREA_APPROVAL";
        public const string WaitingFinalApproval = "WAITING_FINAL_APPROVAL";
        public const string FinalApproved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string Cancelled = "CANCELLED";
        public const string QuotationCompleted = "QUOTATION_COMPLETED";
        public const string PoIssued = "PO_ISSUED";
        public const string PaymentRequestSent = "PAYMENT_REQUEST_SENT";
        public const string PaymentScheduled = "PAYMENT_SCHEDULED";
        public const string Paid = "PAID";
        public const string AreaAdjustment = "AREA_ADJUSTMENT";
        public const string FinalAdjustment = "FINAL_ADJUSTMENT";
        public const string PaymentCompleted = "PAYMENT_COMPLETED";
        public const string InFollowup = "IN_FOLLOWUP";
        public const string Completed = "COMPLETED";
        public const string WaitingReceipt = "WAITING_RECEIPT";
        public const string WaitingCostCenter = "WAITING_COST_CENTER";
        public const string WaitingPoCorrection = "WAITING_PO_CORRECTION";

        // ── Buy-to-Pay (Advance Payment) ──
        public const string AdvancePaymentRequired = "ADVANCE_PAYMENT_REQUIRED";
        public const string AdvancePaymentScheduled = "ADVANCE_PAYMENT_SCHEDULED";
        public const string AdvancePaymentCompleted = "ADVANCE_PAYMENT_COMPLETED";
        public const string WaitingSupplierDelivery = "WAITING_SUPPLIER_DELIVERY";
        public const string WaitingReconciliation = "WAITING_RECONCILIATION";
        public const string PoPartiallyUploaded = "PO_PARTIALLY_UPLOADED";
    }

    /// <summary>
    /// Status codes for RequestPoGroup entities.
    /// Controls the PO group lifecycle from creation through completion.
    /// </summary>
    public static class PoGroupStatuses
    {
        // ── Pre-PO ──
        /// <summary>Groups created at Area Approval, before Final Approval.</summary>
        public const string Pending = "PENDING";
        /// <summary>Groups ready for Buyer to register/upload PO (set at Final Approval).</summary>
        public const string WaitingPo = "WAITING_PO";
        /// <summary>PO returned for correction by Finance.</summary>
        public const string WaitingPoCorrection = "WAITING_PO_CORRECTION";

        // ── Buy-to-Pay (Advance Payment) ──
        /// <summary>Group requires advance payment before PO issuance.</summary>
        public const string AdvancePaymentRequired = "ADVANCE_PAYMENT_REQUIRED";
        /// <summary>Advance payment scheduled by Finance.</summary>
        public const string AdvancePaymentScheduled = "ADVANCE_PAYMENT_SCHEDULED";
        /// <summary>Advance payment completed.</summary>
        public const string AdvancePaymentCompleted = "ADVANCE_PAYMENT_COMPLETED";
        /// <summary>Waiting for supplier to deliver goods/services.</summary>
        public const string WaitingSupplierDelivery = "WAITING_SUPPLIER_DELIVERY";

        // ── PO Issuance ──
        /// <summary>PO issued and registered by Buyer.</summary>
        public const string PoIssued = "PO_ISSUED";

        // ── Invoicing / Payment ──
        /// <summary>Payment request sent to Finance.</summary>
        public const string PaymentRequestSent = "PAYMENT_REQUEST_SENT";
        /// <summary>Payment scheduled by Finance.</summary>
        public const string PaymentScheduled = "PAYMENT_SCHEDULED";
        /// <summary>Payment completed by Finance.</summary>
        public const string PaymentCompleted = "PAYMENT_COMPLETED";

        // ── Receiving / Reconciliation ──
        /// <summary>Group waiting for supplier receipt/invoice.</summary>
        public const string WaitingReceipt = "WAITING_RECEIPT";
        /// <summary>Group in reconciliation process.</summary>
        public const string WaitingReconciliation = "WAITING_RECONCILIATION";
        /// <summary>Group in follow-up for partial receiving.</summary>
        public const string InFollowup = "IN_FOLLOWUP";

        // ── Terminal ──
        /// <summary>Group fully completed (all operational steps done).</summary>
        public const string Completed = "COMPLETED";
        /// <summary>Group cancelled.</summary>
        public const string Cancelled = "CANCELLED";

        // ── Helper arrays for finalization guards ──

        /// <summary>PO group is fully operationally done.</summary>
        public static readonly string[] OperationallyCompleted = new[] { Completed };

        /// <summary>PO group is ready for FinalizeRequest to proceed.</summary>
        public static readonly string[] ReadyToFinalize = new[] { WaitingReceipt, Completed };

        /// <summary>PO group blocks FinalizeRequest — still has active operational work.</summary>
        public static readonly string[] BlocksFinalization = new[]
        {
            Pending, WaitingPo, WaitingPoCorrection, PoIssued,
            AdvancePaymentRequired, AdvancePaymentScheduled, AdvancePaymentCompleted,
            WaitingSupplierDelivery, PaymentRequestSent, PaymentScheduled,
            PaymentCompleted, WaitingReconciliation, InFollowup
        };
    }

    /// <summary>
    /// Status codes for ApprovalBatch entities.
    /// Controls the batch lifecycle from creation through approval or rejection.
    /// Only applicable to QUOTATION requests.
    /// </summary>
    public static class ApprovalBatchStatuses
    {
        /// <summary>Batch created by Buyer, awaiting Area Approval.</summary>
        public const string WaitingAreaApproval = "WAITING_AREA_APPROVAL";
        /// <summary>Batch returned to Buyer for rework by Area Approver.</summary>
        public const string AreaAdjustment = "AREA_ADJUSTMENT";
        /// <summary>Batch area-approved, awaiting Final Approval.</summary>
        public const string WaitingFinalApproval = "WAITING_FINAL_APPROVAL";
        /// <summary>Batch returned to Buyer for rework by Final Approver.</summary>
        public const string FinalAdjustment = "FINAL_ADJUSTMENT";
        /// <summary>Batch fully approved. PO groups activated.</summary>
        public const string Approved = "APPROVED";
        /// <summary>Batch rejected. Items returned to QUOTATION_PENDING.</summary>
        public const string Rejected = "REJECTED";
        /// <summary>Batch cancelled by the Buyer or Admin due to mistaken submission.</summary>
        public const string Cancelled = "CANCELLED";
    }

    /// <summary>
    /// Lifecycle statuses for RequestLineItem.QuotationLifecycleStatus.
    /// Tracks an item's journey through the partial quotation approval process.
    /// Null means legacy/uninitialized (treated as QUOTATION_PENDING).
    /// </summary>
    public static class QuotationLifecycleStatuses
    {
        /// <summary>Item is available for batch inclusion.</summary>
        public const string QuotationPending = "QUOTATION_PENDING";
        /// <summary>Item is assigned to an active batch.</summary>
        public const string BatchAssigned = "BATCH_ASSIGNED";
        /// <summary>Item's batch has been fully approved.</summary>
        public const string QuotationApproved = "QUOTATION_APPROVED";
        /// <summary>Buyer has proposed this item for closure as not-quoted. (Legacy flow — no longer created by new code; kept for old data.)</summary>
        public const string NotQuotedProposed = "NOT_QUOTED_PROPOSED";
        /// <summary>Not-quoted proposal accepted — item is terminally closed. (Legacy flow — no longer created by new code; kept for old data.)</summary>
        public const string NotQuotedAccepted = "NOT_QUOTED_ACCEPTED";
        /// <summary>Buyer closed this item without quotation (reason + justification recorded in history). Terminal.</summary>
        public const string ClosedNotQuoted = "CLOSED_NOT_QUOTED";
    }

    /// <summary>
    /// Payment condition codes defined by Buyer at PO registration.
    /// </summary>
    public static class PaymentConditions
    {
        public const string PostPaid = "POST_PAID";
        public const string AdvanceFull = "ADVANCE_FULL";
        public const string AdvancePartial = "ADVANCE_PARTIAL";
    }

    /// <summary>
    /// Financial Integrity Gate configuration.
    /// Controls the tolerance used to compare OCR-extracted totals against
    /// system-calculated quotation totals at the quotation completion stage.
    /// Tolerance = max(AbsoluteFloor, OcrTotal × RelativeThreshold).
    /// </summary>
    public static class FinancialIntegrity
    {
        /// <summary>
        /// Absolute minimum tolerance in currency units.
        /// Prevents blocking for sub-unit rounding noise.
        /// </summary>
        public const decimal AbsoluteFloor = 1.00m;

        /// <summary>
        /// Relative tolerance as a fraction of the OCR original total.
        /// 0.001 = 0.1% — catches real mismatches while tolerating
        /// rounding differences between JavaScript and C# engines.
        /// </summary>
        public const decimal RelativeThreshold = 0.001m;

        /// <summary>
        /// Calculates the effective tolerance for a given OCR baseline amount.
        /// Returns the greater of AbsoluteFloor or the relative threshold.
        /// </summary>
        public static decimal CalculateTolerance(decimal ocrOriginalTotal)
        {
            var relativeTolerance = Math.Abs(ocrOriginalTotal) * RelativeThreshold;
            return Math.Max(AbsoluteFloor, relativeTolerance);
        }
    }
}
