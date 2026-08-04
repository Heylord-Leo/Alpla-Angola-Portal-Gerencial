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
    /// <summary>
    /// The document a supplier actually issued, under Angola's Regime Jurídico das Facturas
    /// (Presidential Decree 71/25). This is a statement of IDENTITY — what the paper is — and never
    /// of workflow. What remains owed is derived separately by
    /// <see cref="Services.DocumentObligationResolver"/>.
    ///
    /// <para>Neither field carrying this value has a default: the classification must always be an
    /// explicit human decision. OCR may propose, never select.</para>
    /// </summary>
    public static class SourceDocumentTypes
    {
        /// <summary>Orçamento / Cotação. Non-fiscal. Named ESTIMATE because "QUOTATION" is already
        /// the request type, the Quotation entity and QuotationLifecycleStatuses.</summary>
        public const string Estimate = "ESTIMATE";

        /// <summary>Factura Pró-forma. Non-fiscal — legally not a Factura.</summary>
        public const string Proforma = "PROFORMA";

        /// <summary>Factura de Adiantamento. Fiscal document for an advance/anticipated payment.</summary>
        public const string AdvanceInvoice = "ADVANCE_INVOICE";

        /// <summary>Factura. Fiscal document for the actual supply of goods or services.</summary>
        public const string Invoice = "INVOICE";

        /// <summary>Factura-Recibo. Fiscal; documents the operation AND its full payment.</summary>
        public const string InvoiceReceipt = "INVOICE_RECEIPT";

        /// <summary>Another document that Finance must review before the process continues.</summary>
        public const string Other = "OTHER";

        /// <summary>Not classified. The persisted default; blocks progression.</summary>
        public const string Unclassified = "UNCLASSIFIED";

        /// <summary>
        /// Legacy code from the superseded binary model. Accepted on READ and mapped to
        /// <see cref="Invoice"/>; never persisted again. Kept for a couple of releases so a stray
        /// value is interpreted rather than rejected.
        /// </summary>
        public const string LegacyFinalInvoice = "FINAL_INVOICE";

        /// <summary>Every classification a user may hold, excluding UNCLASSIFIED.</summary>
        public static readonly string[] ValidValues =
            { Estimate, Proforma, AdvanceInvoice, Invoice, InvoiceReceipt, Other };

        /// <summary>Fiscal documents under Decree 71/25. Estimate and Proforma are NOT fiscal.</summary>
        public static readonly string[] FiscalValues =
            { AdvanceInvoice, Invoice, InvoiceReceipt };

        public static bool IsValid(string? type) =>
            ValidValues.Any(v => string.Equals(v, type, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// True only for documents with fiscal force. OTHER and UNCLASSIFIED are unknown, not
        /// non-fiscal — callers must treat them as "needs review", never as "safe".
        /// </summary>
        public static bool IsFiscal(string? type) =>
            FiscalValues.Any(v => string.Equals(v, type, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Canonical form: trimmed, upper-cased, legacy alias resolved. Blank stays null — a blank
        /// selection is never coerced into a type on the user's behalf.
        /// </summary>
        public static string? Normalize(string? type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;

            var upper = type.Trim().ToUpperInvariant();
            return upper == LegacyFinalInvoice ? Invoice : upper;
        }

        /// <summary>Portuguese label used in the UI, validation messages and history entries.</summary>
        public static string DisplayName(string? type) => Normalize(type) switch
        {
            Estimate => "Orçamento / Cotação",
            Proforma => "Factura Pró-forma",
            AdvanceInvoice => "Factura de Adiantamento",
            Invoice => "Factura",
            InvoiceReceipt => "Factura-Recibo",
            Other => "Outro documento",
            _ => "Não classificado"
        };
    }

    /// <summary>
    /// Where a classification decision was taken. Part of the override idempotency key, so the same
    /// document classified in a quotation and later in a payment request stays two distinct events.
    /// </summary>
    public static class DocumentClassificationContexts
    {
        public const string PaymentRequest = "PAYMENT_REQUEST";
        public const string QuotationManagement = "QUOTATION_MANAGEMENT";

        public static readonly string[] ValidValues = { PaymentRequest, QuotationManagement };

        public static bool IsValid(string? context) =>
            ValidValues.Any(v => string.Equals(v, context, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// How the overridden suggestion was reached. Kept in the audit because the two carry very
    /// different weight: contradicting a document the provider actually read is a stronger act than
    /// contradicting a guess made from a filename.
    /// </summary>
    public static class DocumentClassificationSources
    {
        /// <summary>The extraction provider read the document and classified it.</summary>
        public const string Ocr = "OCR";

        /// <summary>Derived from Portal heuristics (document-number prefix, filename) only.</summary>
        public const string Fallback = "FALLBACK";

        public static readonly string[] ValidValues = { Ocr, Fallback };

        public static bool IsValid(string? source) =>
            ValidValues.Any(v => string.Equals(v, source, StringComparison.OrdinalIgnoreCase));

        /// <summary>Canonical form; anything unrecognised becomes null rather than a false claim.</summary>
        public static string? Normalize(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            var upper = source.Trim().ToUpperInvariant();
            return IsValid(upper) ? upper : null;
        }
    }

    /// <summary>
    /// Final Invoice obligation state of a RequestPoGroup (post-payment dimension 2).
    /// UNCLASSIFIED is the persisted default: an unclassified group can never complete and can
    /// never accept a Fiscal Receipt (rule R15).
    /// </summary>
    /// <summary>
    /// State of the OPERATION-INVOICE obligation on a PO group — the Factura documenting the actual
    /// supply. Renamed from FinalInvoiceStatuses: "final" wrongly implied terminality, when a
    /// Factura is simply the operation document and several other obligations may still be open.
    ///
    /// <para>This tracks ONE obligation. Whether the obligation exists at all is decided by
    /// <see cref="Services.DocumentObligationResolver"/> from the document identity.</para>
    /// </summary>
    public static class OperationInvoiceStatuses
    {
        /// <summary>Document identity unknown. Default for every group. Blocks completion.</summary>
        public const string Unclassified = "UNCLASSIFIED";
        /// <summary>The source document already documents the operation — no further invoice is owed.</summary>
        public const string NotRequired = "NOT_REQUIRED";
        /// <summary>An operation invoice is owed and nothing has been validated yet.</summary>
        public const string PendingUpload = "PENDING_UPLOAD";
        /// <summary>Some coverage validated, but less than expected. Legitimate partial invoicing.</summary>
        public const string PartiallyInvoiced = "PARTIALLY_INVOICED";
        /// <summary>At least one invoice awaits Finance validation (rule R10).</summary>
        public const string PendingValidation = "PENDING_VALIDATION";
        /// <summary>Reconciliation found a divergence Finance must decide on explicitly.</summary>
        public const string DivergenceDetected = "DIVERGENCE_DETECTED";
        /// <summary>Cumulative coverage complete, or Finance approved a short close.</summary>
        public const string Satisfied = "SATISFIED";

        /// <summary>States in which the operation-invoice dimension counts as done.</summary>
        public static readonly string[] SatisfiedStates =
            { NotRequired, Satisfied };

        /// <summary>States that block group completion and Fiscal Receipt upload.</summary>
        public static readonly string[] Blocking =
            { Unclassified, PendingUpload, PartiallyInvoiced, PendingValidation, DivergenceDetected };

        /// <summary>
        /// Aggregate states in which a further operation-invoice upload is accepted. A satisfied or
        /// not-required group takes no more documents; an unclassified one takes none yet.
        /// </summary>
        public static readonly string[] AcceptsUpload =
            { PendingUpload, PartiallyInvoiced, PendingValidation, DivergenceDetected };

        public static bool IsSatisfied(string? status) =>
            SatisfiedStates.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

        public static bool IsBlocking(string? status) =>
            Blocking.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

        public static bool AcceptsUploadIn(string? status) =>
            AcceptsUpload.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// State of ONE operation-invoice document. Distinct from the group aggregate above: a document
    /// is validated or it is not, while a group is measured by cumulative coverage.
    /// </summary>
    public static class OperationInvoiceDocumentStatuses
    {
        /// <summary>Received; extraction and reconciliation not yet computed. Transient.</summary>
        public const string Uploaded = "UPLOADED";
        /// <summary>Computed and queued for Finance.</summary>
        public const string PendingValidation = "PENDING_VALIDATION";
        /// <summary>Accepted. Its allocations count toward their groups' coverage.</summary>
        public const string Validated = "VALIDATED";
        /// <summary>Judged wrong. Terminal for this document.</summary>
        public const string Rejected = "REJECTED";
        /// <summary>Right document, unusable copy. Terminal for this document.</summary>
        public const string ReplacementRequested = "REPLACEMENT_REQUESTED";
        /// <summary>Something Finance must decide on explicitly before the document can be accepted.</summary>
        public const string DivergenceDetected = "DIVERGENCE_DETECTED";

        /// <summary>Contributes to a group's validated coverage.</summary>
        public static bool CountsTowardCoverage(string? status) =>
            string.Equals(status, Validated, StringComparison.OrdinalIgnoreCase);

        /// <summary>Awaiting a Finance decision — the group reads as PENDING_VALIDATION.</summary>
        public static readonly string[] AwaitingDecision = { Uploaded, PendingValidation };

        /// <summary>Terminal without coverage: contributes nothing but stays visible forever.</summary>
        public static readonly string[] Terminal = { Rejected, ReplacementRequested };

        public static bool IsAwaitingDecision(string? status) =>
            AwaitingDecision.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

        public static bool IsEditable(string? status) =>
            !string.Equals(status, Validated, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How an operation-invoice line relates to the baseline it was allocated against.</summary>
    public static class OperationInvoiceLineMatchStatuses
    {
        public const string Mapped = "MAPPED";
        public const string Substitute = "SUBSTITUTE";
        public const string ExtraItem = "EXTRA_ITEM";
        public const string Ignored = "IGNORED";

        /// <summary>
        /// Read from the document but not yet attributed to any group. Never silently discarded —
        /// an unallocated line blocks validation until it is allocated or explicitly ignored.
        /// </summary>
        public const string Unallocated = "UNALLOCATED";

        public static bool BlocksValidation(string? status) =>
            string.Equals(status, Unallocated, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lifecycle of a proposal to close an obligation below its expected total.</summary>
    public static class ShortCloseStatuses
    {
        public const string Proposed = "PROPOSED";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";

        /// <summary>States that occupy the single active slot per group.</summary>
        public static readonly string[] Active = { Proposed, Approved };
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
