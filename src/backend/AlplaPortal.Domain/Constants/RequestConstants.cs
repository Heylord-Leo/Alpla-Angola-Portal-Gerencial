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

        // ── Post-Payment Completion Workflow ──
        /// <summary>
        /// Operational receipt and Final Invoice satisfied; only the Fiscal Receipt is missing.
        /// Release 1 seeds the lookup row; NO code assigns this status until Release 4.
        /// </summary>
        public const string WaitingFiscalReceipt = "WAITING_FISCAL_RECEIPT";
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

        /// <summary>
        /// Post-Payment Completion Workflow: operational receipt done and Final Invoice satisfied,
        /// waiting only for Finance to upload the Fiscal Receipt.
        /// Release 1 declares the constant; NO code assigns it and it is deliberately absent from
        /// the finalization helper arrays below until Release 4 activates the workflow.
        /// </summary>
        public const string WaitingFiscalReceipt = "WAITING_FISCAL_RECEIPT";

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
    /// Canonical reconciliation statuses for quotation items (wizard reconciliation step).
    /// Single source of truth — frontend, DTOs, controllers and the integrity calculator must
    /// all use these exact values (the API boundary normalizes casing/whitespace).
    /// </summary>
    public static class ReconciliationStatuses
    {
        /// <summary>Document line matched to a requested item.</summary>
        public const string Mapped = "MAPPED";
        /// <summary>Document line replaces a requested item (justification required).</summary>
        public const string Substitute = "SUBSTITUTE";
        /// <summary>Document line proposed as an additional item (justification required; approver decides).</summary>
        public const string ExtraItem = "EXTRA_ITEM";
        /// <summary>Document line explicitly excluded from the quotation (justification required when it has value).</summary>
        public const string Ignored = "IGNORED";
        /// <summary>Requested item the supplier did not quote (zeroed; document-scoped info).</summary>
        public const string NotQuoted = "NOT_QUOTED";

        /// <summary>Statuses whose lines compose the quotation's financial totals.</summary>
        public static readonly string[] Considered = { Mapped, Substitute, ExtraItem };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Post-Payment Completion Workflow — Release 1 foundation constants.
    // No code writes these values while PostPaymentCompletion.Enabled is false.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Billing document that originated a request or PO group. These two values are the
    /// complete set — no other initial billing document type exists (rule R1).
    /// Neither field carrying this value has a default: PROFORMA vs FINAL_INVOICE must always
    /// be an explicit human decision (rules R13/R14).
    /// </summary>
    public static class BillingDocumentTypes
    {
        /// <summary>Proforma invoice — always requires a subsequent Final Invoice (rule R2).</summary>
        public const string Proforma = "PROFORMA";
        /// <summary>Final invoice supplied up front — no subsequent Final Invoice required (rule R3).</summary>
        public const string FinalInvoice = "FINAL_INVOICE";

        public static readonly string[] ValidValues = { Proforma, FinalInvoice };

        public static bool IsValid(string? type) =>
            ValidValues.Any(v => string.Equals(v, type, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Canonical form of a submitted value: trimmed and upper-cased, or null when blank.
        /// A blank selection stays null — it is never coerced into a type on the user's behalf.
        /// </summary>
        public static string? Normalize(string? type) =>
            string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToUpperInvariant();

        /// <summary>Portuguese label used in validation messages and history entries.</summary>
        public static string DisplayName(string? type) => type switch
        {
            Proforma => "Fatura Proforma",
            FinalInvoice => "Fatura Final",
            _ => "Não classificado"
        };

        /// <summary>
        /// True only for PROFORMA. A null/unknown type returns false here and must be handled as
        /// UNCLASSIFIED by the caller — it is never silently treated as "nothing required" (rule R12).
        /// </summary>
        public static bool RequiresFinalInvoice(string? type) =>
            string.Equals(type, Proforma, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Maps a billing document type to the Final Invoice obligation state of a PO group.
        /// Null/unknown → UNCLASSIFIED (blocks completion until Finance classifies it).
        /// </summary>
        public static string ToFinalInvoiceStatus(string? type)
        {
            if (RequiresFinalInvoice(type)) return FinalInvoiceStatuses.PendingUpload;
            if (string.Equals(type, FinalInvoice, StringComparison.OrdinalIgnoreCase))
                return FinalInvoiceStatuses.NotApplicableInitialFinalInvoice;
            return FinalInvoiceStatuses.Unclassified;
        }
    }

    /// <summary>
    /// Final Invoice obligation state of a RequestPoGroup (post-payment dimension 2).
    /// UNCLASSIFIED is the persisted default: an unclassified group can never complete and can
    /// never accept a Fiscal Receipt (rule R15).
    /// </summary>
    public static class FinalInvoiceStatuses
    {
        /// <summary>Billing document type unknown. Default for every group. Blocks completion.</summary>
        public const string Unclassified = "UNCLASSIFIED";
        /// <summary>Request started with a FINAL_INVOICE — no subsequent invoice is owed.</summary>
        public const string NotApplicableInitialFinalInvoice = "NOT_APPLICABLE";
        /// <summary>Started with a PROFORMA — a Final Invoice is owed and not yet uploaded.</summary>
        public const string PendingUpload = "PENDING_UPLOAD";
        /// <summary>Uploaded, awaiting Finance validation (rule R10).</summary>
        public const string PendingValidation = "PENDING_VALIDATION";
        /// <summary>Validated by Finance. Obligation satisfied.</summary>
        public const string Validated = "VALIDATED";
        /// <summary>Rejected by Finance. A new upload is expected.</summary>
        public const string Rejected = "REJECTED";
        /// <summary>Finance asked for a replacement document.</summary>
        public const string ReplacementRequested = "REPLACEMENT_REQUESTED";
        /// <summary>Reconciliation found a divergence Finance must decide on explicitly.</summary>
        public const string DivergenceDetected = "DIVERGENCE_DETECTED";

        /// <summary>States in which the Final Invoice dimension counts as done.</summary>
        public static readonly string[] Satisfied =
            { NotApplicableInitialFinalInvoice, Validated };

        /// <summary>States in which a new Final Invoice upload is accepted.</summary>
        public static readonly string[] AcceptsUpload =
            { PendingUpload, Rejected, ReplacementRequested };

        /// <summary>States that block group completion and Fiscal Receipt upload.</summary>
        public static readonly string[] Blocking =
            { Unclassified, PendingUpload, PendingValidation,
              Rejected, ReplacementRequested, DivergenceDetected };

        public static bool IsSatisfied(string? status) =>
            Satisfied.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

        public static bool IsBlocking(string? status) =>
            Blocking.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Fiscal Receipt state (post-payment dimension 3). DERIVED, never persisted as a column —
    /// see <see cref="Services.FiscalReceiptStateDeriver"/>. The Fiscal Receipt is the terminal
    /// closing step and only Finance may upload it (rules R5/R6).
    /// </summary>
    public static class FiscalReceiptStatuses
    {
        /// <summary>Other dimensions still pending — upload not yet available.</summary>
        public const string Locked = "LOCKED";
        /// <summary>All other dimensions satisfied — Finance may upload.</summary>
        public const string PendingUpload = "PENDING";
        /// <summary>Fiscal receipt attached. Group is completable.</summary>
        public const string Uploaded = "UPLOADED";
    }

    /// <summary>
    /// Completion policy markers. Requests completed before the post-payment workflow existed
    /// stay completed and are never reconstructed or backfilled (rule R16).
    /// </summary>
    public static class CompletionPolicies
    {
        /// <summary>Completed under the pre-post-payment rules. No historical fact invention.</summary>
        public const string LegacyCompleted = "LEGACY_COMPLETED";
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
