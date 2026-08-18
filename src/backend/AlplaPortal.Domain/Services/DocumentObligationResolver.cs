using System;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Where a document is being presented. The same document implies different things depending on
/// the point in the process at which it arrives — a Factura-Recibo is a perfectly good piece of
/// post-payment evidence but must never start a payable request.
/// </summary>
public enum DocumentUsageContext
{
    /// <summary>A new PAYMENT request: the document is the payable origin.</summary>
    PaymentRequest,

    /// <summary>Buyer quotation management, before the purchase is committed.</summary>
    QuotationManagement,

    /// <summary>Evidence supplied after payment to discharge an outstanding obligation.</summary>
    PostPaymentEvidence
}

/// <summary>
/// What remains required once a document has been identified. Produced only by
/// <see cref="DocumentObligationResolver"/> — never assembled by hand at a call site, and never
/// stored as the document identity itself.
/// </summary>
public sealed record DocumentObligations
{
    public string SourceDocumentType { get; init; } = RequestConstants.SourceDocumentTypes.Unclassified;
    public DocumentUsageContext Context { get; init; }

    /// <summary>The document has fiscal force under Decree 71/25.</summary>
    public bool IsFiscal { get; init; }

    /// <summary>May legitimately originate a payable PAYMENT request.</summary>
    public bool CanInitiatePayment { get; init; }

    /// <summary>Is a normal input to Buyer quotation management.</summary>
    public bool CanBeUsedInQuotation { get; init; }

    /// <summary>A Factura for the actual supply is still owed.</summary>
    public bool RequiresOperationInvoice { get; init; }

    /// <summary>A separate payment receipt is still owed (false when the document already proves payment).</summary>
    public bool RequiresSeparateFiscalReceipt { get; init; }

    /// <summary>Advance regularization is owed — credit note, final invoice and payment evidence.</summary>
    public bool RequiresAdvanceRegularization { get; init; }

    /// <summary>Finance must confirm or correct the classification before the process continues.</summary>
    public bool RequiresFinanceClassificationReview { get; init; }

    /// <summary>Physical receipt of the goods/services must still be confirmed.</summary>
    public bool RequiresOperationalReceipt { get; init; }

    /// <summary>The document cannot carry the process forward in this context.</summary>
    public bool BlocksProgression { get; init; }

    /// <summary>Human-readable reason, in pt-PT, shown when <see cref="BlocksProgression"/> is true.</summary>
    public string? BlockingReason { get; init; }

    /// <summary>The initial operation-invoice status implied by this identity.</summary>
    public string OperationInvoiceStatus =>
        SourceDocumentType == RequestConstants.SourceDocumentTypes.Unclassified
            ? RequestConstants.OperationInvoiceStatuses.Unclassified
            : RequiresOperationInvoice
                ? RequestConstants.OperationInvoiceStatuses.PendingUpload
                : RequestConstants.OperationInvoiceStatuses.NotRequired;
}

/// <summary>
/// Single source of truth translating a document's IDENTITY into the obligations that remain.
///
/// <para>This exists because the superseded model used one field for both questions — "what is this
/// document?" and "what is still required?" — which made it impossible to represent a Factura de
/// Adiantamento (fiscal, yet still owing an operation invoice) or a Factura-Recibo (owes no
/// separate receipt). Identity is recorded; obligations are always recomputed from it here.</para>
///
/// <para>Pure and side-effect-free: no database, no clock, no configuration.</para>
/// </summary>
public static class DocumentObligationResolver
{
    private const string AlreadyPaidCannotPayAgain =
        "A Factura-Recibo já documenta a operação e o respetivo pagamento integral. " +
        "Não pode originar um novo pedido de pagamento.";

    private const string NotAQuotationOrigin =
        "A Factura-Recibo não é um documento de origem para cotação. " +
        "Só é aceite mediante exceção revista pelo Financeiro.";

    private const string NeedsFinanceReview =
        "Documento não reconhecido: requer revisão do Financeiro antes de prosseguir.";

    private const string NotClassified =
        "Documento por classificar: selecione o tipo de documento anexado para prosseguir.";

    public static DocumentObligations Resolve(string? sourceDocumentType, DocumentUsageContext context)
    {
        var type = RequestConstants.SourceDocumentTypes.Normalize(sourceDocumentType)
                   ?? RequestConstants.SourceDocumentTypes.Unclassified;

        return type switch
        {
            RequestConstants.SourceDocumentTypes.Proforma => Proforma(context),
            RequestConstants.SourceDocumentTypes.AdvanceInvoice => AdvanceInvoice(context),
            RequestConstants.SourceDocumentTypes.Invoice => Invoice(context),
            RequestConstants.SourceDocumentTypes.InvoiceReceipt => InvoiceReceipt(context),
            RequestConstants.SourceDocumentTypes.Other => Other(context),
            _ => Unclassified(context)
        };
    }

    /// <summary>Convenience for UI/validation: may this identity originate a payable request?</summary>
    public static bool CanInitiatePayment(string? sourceDocumentType) =>
        Resolve(sourceDocumentType, DocumentUsageContext.PaymentRequest).CanInitiatePayment;

    /// <summary>Convenience for UI/validation: is this a normal quotation-origin document?</summary>
    public static bool CanBeUsedInQuotation(string? sourceDocumentType) =>
        Resolve(sourceDocumentType, DocumentUsageContext.QuotationManagement).CanBeUsedInQuotation;

    // ── Factura Pró-forma — non-fiscal, but the established payable origin ────
    // Legally not a Factura, yet it is how suppliers quote a payable amount. It always leaves an
    // operation invoice owed. Since v2.229.10 this is also the canonical identity of every
    // commercial offer (orçamento, cotação, proposta): SourceDocumentTypes.Normalize folds the
    // legacy ESTIMATE code here, so no separate Estimate branch can be reached.
    private static DocumentObligations Proforma(DocumentUsageContext context) => new()
    {
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
        Context = context,
        IsFiscal = false,
        CanInitiatePayment = true,
        CanBeUsedInQuotation = true,
        RequiresOperationInvoice = true,
        RequiresSeparateFiscalReceipt = true,
        RequiresAdvanceRegularization = false,
        RequiresFinanceClassificationReview = false,
        RequiresOperationalReceipt = true,
        BlocksProgression = false
    };

    // ── Factura de Adiantamento — fiscal advance (PROVISIONAL ALPLA rule) ─────
    // Fiscal, so it may originate an advance payment — but it documents an advance, not the supply.
    // It is never PROFORMA (that would understate a fiscal document) and never the operation
    // invoice (that would clear an obligation that is still open). Regularization reuses the
    // existing Buy-to-Pay RequestReconciliation/RequestPayment machinery.
    private static DocumentObligations AdvanceInvoice(DocumentUsageContext context) => new()
    {
        SourceDocumentType = RequestConstants.SourceDocumentTypes.AdvanceInvoice,
        Context = context,
        IsFiscal = true,
        CanInitiatePayment = true,
        // Unusual as a quotation origin, but not forbidden — Finance reviews it.
        CanBeUsedInQuotation = true,
        RequiresOperationInvoice = true,
        // Provisional: a separate receipt is required unless other legally valid proof exists.
        RequiresSeparateFiscalReceipt = true,
        RequiresAdvanceRegularization = true,
        RequiresFinanceClassificationReview = true,
        RequiresOperationalReceipt = true,
        BlocksProgression = false
    };

    // ── Factura — fiscal, documents the actual operation ──────────────────────
    private static DocumentObligations Invoice(DocumentUsageContext context) => new()
    {
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Invoice,
        Context = context,
        IsFiscal = true,
        CanInitiatePayment = true,
        CanBeUsedInQuotation = true,
        RequiresOperationInvoice = false,
        RequiresSeparateFiscalReceipt = true,
        RequiresAdvanceRegularization = false,
        RequiresFinanceClassificationReview = false,
        RequiresOperationalReceipt = true,
        BlocksProgression = false
    };

    // ── Factura-Recibo — fiscal; operation AND full payment already documented ─
    // The decisive rule: it must never originate a payable request, or the Portal would be asked to
    // pay the same thing twice. As post-payment evidence it is excellent — it discharges both the
    // operation-invoice and the separate-receipt obligations at once.
    private static DocumentObligations InvoiceReceipt(DocumentUsageContext context) => new()
    {
        SourceDocumentType = RequestConstants.SourceDocumentTypes.InvoiceReceipt,
        Context = context,
        IsFiscal = true,
        CanInitiatePayment = false,
        CanBeUsedInQuotation = false,
        RequiresOperationInvoice = false,
        RequiresSeparateFiscalReceipt = false,
        RequiresAdvanceRegularization = false,
        RequiresFinanceClassificationReview = context != DocumentUsageContext.PostPaymentEvidence,
        RequiresOperationalReceipt = true,
        BlocksProgression = context != DocumentUsageContext.PostPaymentEvidence,
        BlockingReason = context switch
        {
            DocumentUsageContext.PaymentRequest => AlreadyPaidCannotPayAgain,
            DocumentUsageContext.QuotationManagement => NotAQuotationOrigin,
            _ => null
        }
    };

    // ── Outro documento — identity unknown, obligations undecidable ───────────
    // Deliberately conservative: every obligation is assumed open until Finance decides.
    private static DocumentObligations Other(DocumentUsageContext context) => new()
    {
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Other,
        Context = context,
        IsFiscal = false,
        CanInitiatePayment = false,
        CanBeUsedInQuotation = false,
        RequiresOperationInvoice = true,
        RequiresSeparateFiscalReceipt = true,
        RequiresAdvanceRegularization = false,
        RequiresFinanceClassificationReview = true,
        RequiresOperationalReceipt = true,
        BlocksProgression = true,
        BlockingReason = NeedsFinanceReview
    };

    // ── Não classificado — the safe default ──────────────────────────────────
    private static DocumentObligations Unclassified(DocumentUsageContext context) => new()
    {
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Unclassified,
        Context = context,
        IsFiscal = false,
        CanInitiatePayment = false,
        CanBeUsedInQuotation = false,
        RequiresOperationInvoice = true,
        RequiresSeparateFiscalReceipt = true,
        RequiresAdvanceRegularization = false,
        RequiresFinanceClassificationReview = true,
        RequiresOperationalReceipt = true,
        BlocksProgression = true,
        BlockingReason = NotClassified
    };
}
