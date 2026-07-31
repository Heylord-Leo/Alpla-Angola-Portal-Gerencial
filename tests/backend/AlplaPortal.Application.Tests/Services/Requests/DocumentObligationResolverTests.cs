using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 2 corrected: the obligation matrix for Angolan billing documents
/// (Regime Jurídico das Facturas, Decreto Presidencial 71/25).
///
/// These tests encode the two rules that the superseded binary model got wrong and that are
/// expensive to get wrong again:
/// <list type="bullet">
/// <item>a Factura-Recibo already documents the operation AND its full payment, so it must never
/// originate a payable request and must never be asked for a separate receipt;</item>
/// <item>a Factura de Adiantamento is fiscal, yet still owes an operation invoice, a Credit Note
/// and Finance validation — it is neither a Pró-forma nor an ordinary Factura.</item>
/// </list>
/// </summary>
public class DocumentObligationResolverTests
{
    private static DocumentObligations Payment(string type) =>
        DocumentObligationResolver.Resolve(type, DocumentUsageContext.PaymentRequest);

    private static DocumentObligations Quotation(string type) =>
        DocumentObligationResolver.Resolve(type, DocumentUsageContext.QuotationManagement);

    private static DocumentObligations Evidence(string type) =>
        DocumentObligationResolver.Resolve(type, DocumentUsageContext.PostPaymentEvidence);

    // ── Who may originate a payable request ──

    [Theory]
    [InlineData("PROFORMA")]
    [InlineData("ADVANCE_INVOICE")]
    [InlineData("INVOICE")]
    public void Payable_origins_can_initiate_a_payment(string type)
    {
        Assert.True(Payment(type).CanInitiatePayment);
        Assert.False(Payment(type).BlocksProgression);
    }

    [Fact]
    public void An_estimate_cannot_initiate_a_payment()
    {
        var o = Payment(RequestConstants.SourceDocumentTypes.Estimate);

        Assert.False(o.CanInitiatePayment);
        Assert.True(o.BlocksProgression);
        Assert.False(o.IsFiscal);
        Assert.Contains("não fiscal", o.BlockingReason!);
    }

    [Fact]
    public void An_invoice_receipt_cannot_initiate_a_payment_it_would_pay_twice()
    {
        var o = Payment(RequestConstants.SourceDocumentTypes.InvoiceReceipt);

        Assert.False(o.CanInitiatePayment);
        Assert.True(o.BlocksProgression);
        Assert.Contains("já documenta", o.BlockingReason!);
    }

    [Theory]
    [InlineData("OTHER")]
    [InlineData("UNCLASSIFIED")]
    public void Unknown_documents_cannot_initiate_a_payment(string type)
    {
        var o = Payment(type);

        Assert.False(o.CanInitiatePayment);
        Assert.True(o.BlocksProgression);
        Assert.True(o.RequiresFinanceClassificationReview);
    }

    // ── Quotation management ──

    [Theory]
    [InlineData("ESTIMATE")]
    [InlineData("PROFORMA")]
    [InlineData("INVOICE")]
    public void Natural_quotation_documents_are_accepted(string type)
    {
        Assert.True(Quotation(type).CanBeUsedInQuotation);
    }

    [Fact]
    public void An_advance_invoice_is_accepted_in_quotation_but_flagged_for_review()
    {
        var o = Quotation(RequestConstants.SourceDocumentTypes.AdvanceInvoice);

        Assert.True(o.CanBeUsedInQuotation);
        Assert.True(o.RequiresFinanceClassificationReview);
    }

    [Fact]
    public void An_invoice_receipt_is_not_a_quotation_origin()
    {
        var o = Quotation(RequestConstants.SourceDocumentTypes.InvoiceReceipt);

        Assert.False(o.CanBeUsedInQuotation);
        Assert.True(o.BlocksProgression);
    }

    // ── The obligation matrix ──

    [Fact]
    public void Proforma_owes_an_operation_invoice_and_a_receipt()
    {
        var o = Payment(RequestConstants.SourceDocumentTypes.Proforma);

        Assert.True(o.RequiresOperationInvoice);
        Assert.True(o.RequiresSeparateFiscalReceipt);
        Assert.False(o.RequiresAdvanceRegularization);
        Assert.Equal(RequestConstants.OperationInvoiceStatuses.PendingUpload, o.OperationInvoiceStatus);
    }

    [Fact]
    public void Invoice_owes_only_the_payment_receipt()
    {
        var o = Payment(RequestConstants.SourceDocumentTypes.Invoice);

        Assert.False(o.RequiresOperationInvoice);
        Assert.True(o.RequiresSeparateFiscalReceipt);
        Assert.False(o.RequiresAdvanceRegularization);
        Assert.False(o.RequiresFinanceClassificationReview);
        Assert.Equal(RequestConstants.OperationInvoiceStatuses.NotApplicable, o.OperationInvoiceStatus);
    }

    [Fact]
    public void Advance_invoice_owes_operation_invoice_regularization_and_finance_validation()
    {
        // The provisional ALPLA rule, encoded. Collapsing this into PROFORMA would understate a
        // fiscal document; collapsing it into INVOICE would clear an obligation that is still open.
        var o = Payment(RequestConstants.SourceDocumentTypes.AdvanceInvoice);

        Assert.True(o.IsFiscal);
        Assert.True(o.CanInitiatePayment);
        Assert.True(o.RequiresOperationInvoice);
        Assert.True(o.RequiresAdvanceRegularization);
        Assert.True(o.RequiresFinanceClassificationReview);
        Assert.Equal(RequestConstants.OperationInvoiceStatuses.PendingUpload, o.OperationInvoiceStatus);
    }

    [Fact]
    public void Advance_invoice_is_neither_proforma_nor_ordinary_invoice()
    {
        var advance = Payment(RequestConstants.SourceDocumentTypes.AdvanceInvoice);
        var proforma = Payment(RequestConstants.SourceDocumentTypes.Proforma);
        var invoice = Payment(RequestConstants.SourceDocumentTypes.Invoice);

        Assert.NotEqual(proforma.IsFiscal, advance.IsFiscal);                                   // vs PROFORMA
        Assert.NotEqual(invoice.RequiresOperationInvoice, advance.RequiresOperationInvoice);     // vs INVOICE
        Assert.NotEqual(invoice.RequiresAdvanceRegularization, advance.RequiresAdvanceRegularization);
    }

    [Fact]
    public void Invoice_receipt_as_evidence_closes_both_document_obligations()
    {
        var o = Evidence(RequestConstants.SourceDocumentTypes.InvoiceReceipt);

        Assert.False(o.RequiresOperationInvoice);
        Assert.False(o.RequiresSeparateFiscalReceipt);   // demanding one would be legally wrong
        Assert.False(o.BlocksProgression);
        Assert.Equal(RequestConstants.OperationInvoiceStatuses.NotApplicable, o.OperationInvoiceStatus);
    }

    [Fact]
    public void Unclassified_blocks_and_leaves_every_obligation_open()
    {
        var o = Payment(RequestConstants.SourceDocumentTypes.Unclassified);

        Assert.True(o.BlocksProgression);
        Assert.True(o.RequiresOperationInvoice);
        Assert.True(o.RequiresSeparateFiscalReceipt);
        Assert.Equal(RequestConstants.OperationInvoiceStatuses.Unclassified, o.OperationInvoiceStatus);
    }

    [Fact]
    public void Every_document_type_still_requires_physical_receipt()
    {
        // Paperwork never substitutes for confirming the goods or services actually arrived —
        // a Fiscal Receipt is not evidence of operational receipt.
        foreach (var t in RequestConstants.SourceDocumentTypes.ValidValues)
            Assert.True(Payment(t).RequiresOperationalReceipt, t);
    }

    // ── Robustness ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT_A_TYPE")]
    public void Unrecognised_input_resolves_to_the_blocking_default(string? raw)
    {
        var o = DocumentObligationResolver.Resolve(raw, DocumentUsageContext.PaymentRequest);

        Assert.Equal(RequestConstants.SourceDocumentTypes.Unclassified, o.SourceDocumentType);
        Assert.True(o.BlocksProgression);
        Assert.False(o.CanInitiatePayment);
    }

    [Fact]
    public void The_legacy_final_invoice_code_resolves_as_invoice()
    {
        var o = Payment("FINAL_INVOICE");

        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice, o.SourceDocumentType);
        Assert.True(o.CanInitiatePayment);
        Assert.False(o.RequiresOperationInvoice);
    }
}
