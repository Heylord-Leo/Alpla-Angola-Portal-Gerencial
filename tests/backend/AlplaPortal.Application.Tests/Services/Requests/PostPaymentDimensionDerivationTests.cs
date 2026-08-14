using System;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 1 foundation tests for the derived (never persisted) post-payment dimension state:
/// Fiscal Receipt availability, group completability and the pending-reason label
/// (plan v6 §8.3/§8.4).
/// </summary>
public class PostPaymentDimensionDerivationTests
{
    // Phase 4A: the fiscal-receipt dimension is conditional on RequiresSeparateFiscalReceipt.
    // The helper defaults it to TRUE (the pre-4A behaviour these tests always pinned); the
    // conditional tests below flip it explicitly.
    private static RequestPoGroup Group(
        bool receiptDone = false,
        string operationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Unclassified,
        bool fiscalUploaded = false,
        Guid? fiscalAttachmentId = null,
        bool separateReceiptRequired = true) => new()
        {
            Id = Guid.NewGuid(),
            OperationalReceiptCompletedAtUtc = receiptDone ? DateTime.UtcNow : null,
            OperationInvoiceStatus = operationInvoiceStatus,
            RequiresSeparateFiscalReceipt = separateReceiptRequired,
            FiscalReceiptUploadedAtUtc = fiscalUploaded ? DateTime.UtcNow : null,
            FiscalReceiptAttachmentId = fiscalUploaded ? (fiscalAttachmentId ?? Guid.NewGuid()) : fiscalAttachmentId
        };

    // ── Entity default ──

    [Fact]
    public void New_group_defaults_to_unclassified()
    {
        var group = new RequestPoGroup();

        Assert.Equal(RequestConstants.OperationInvoiceStatuses.Unclassified, group.OperationInvoiceStatus);
        Assert.Null(group.SourceDocumentType);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.Null(group.FiscalReceiptAttachmentId);
        Assert.Null(group.CompletedAtUtc);
    }

    // ── Taxonomy shape ──

    [Fact]
    public void Six_selectable_document_types_are_in_scope()
    {
        Assert.Equal(6, RequestConstants.SourceDocumentTypes.ValidValues.Length);
        foreach (var t in new[] { "ESTIMATE", "PROFORMA", "ADVANCE_INVOICE", "INVOICE", "INVOICE_RECEIPT", "OTHER" })
            Assert.True(RequestConstants.SourceDocumentTypes.IsValid(t), t);

        Assert.False(RequestConstants.SourceDocumentTypes.IsValid("UNCLASSIFIED"));
        Assert.False(RequestConstants.SourceDocumentTypes.IsValid(null));
    }

    [Fact]
    public void Only_the_three_fiscal_documents_are_marked_fiscal()
    {
        Assert.True(RequestConstants.SourceDocumentTypes.IsFiscal("ADVANCE_INVOICE"));
        Assert.True(RequestConstants.SourceDocumentTypes.IsFiscal("INVOICE"));
        Assert.True(RequestConstants.SourceDocumentTypes.IsFiscal("INVOICE_RECEIPT"));

        // A Pró-forma and an Orçamento are not Facturas under Decree 71/25.
        Assert.False(RequestConstants.SourceDocumentTypes.IsFiscal("PROFORMA"));
        Assert.False(RequestConstants.SourceDocumentTypes.IsFiscal("ESTIMATE"));
        // OTHER/UNCLASSIFIED are unknown, not "non-fiscal" — but they must never read as fiscal.
        Assert.False(RequestConstants.SourceDocumentTypes.IsFiscal("OTHER"));
        Assert.False(RequestConstants.SourceDocumentTypes.IsFiscal(null));
    }

    [Fact]
    public void The_superseded_final_invoice_code_normalizes_to_invoice()
    {
        // Read-time alias only: a stray legacy value is interpreted, never rejected — and never
        // written back.
        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice,
            RequestConstants.SourceDocumentTypes.Normalize("FINAL_INVOICE"));
        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice,
            RequestConstants.SourceDocumentTypes.Normalize("final_invoice"));
    }

    // ── Fiscal Receipt is the terminal step ──

    [Fact]
    public void Fiscal_receipt_is_locked_while_operational_receipt_is_pending()
    {
        var group = Group(receiptDone: false, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
        Assert.False(FiscalReceiptStateDeriver.CanUploadFiscalReceipt(group));
    }

    [Fact]
    public void Fiscal_receipt_is_locked_while_the_final_invoice_is_outstanding()
    {
        var group = Group(receiptDone: true, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.PendingValidation);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
    }

    [Fact]
    public void Unclassified_group_can_never_unlock_the_fiscal_receipt()
    {
        var group = Group(receiptDone: true, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Unclassified);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
        Assert.False(FiscalReceiptStateDeriver.CanUploadFiscalReceipt(group));
        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    [Theory]
    [InlineData("SATISFIED")]      // Release 3: cumulative coverage complete, or short-closed
    [InlineData("NOT_REQUIRED")]   // the source document already documents the operation
    public void Fiscal_receipt_unlocks_once_receipt_and_invoice_are_satisfied(string satisfiedStatus)
    {
        var group = Group(receiptDone: true, operationInvoiceStatus: satisfiedStatus);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.PendingUpload, FiscalReceiptStateDeriver.Derive(group));
        Assert.True(FiscalReceiptStateDeriver.CanUploadFiscalReceipt(group));
    }

    [Fact]
    public void Uploaded_fiscal_receipt_reports_uploaded_regardless_of_order()
    {
        // Dimensions are parallel: an uploaded receipt stays UPLOADED even if the invoice were
        // still open — the upload guard, not this deriver, is what prevents that state.
        var group = Group(receiptDone: false, fiscalUploaded: true);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Uploaded, FiscalReceiptStateDeriver.Derive(group));
    }

    // ── Completability requires a stable completion identity ──

    [Fact]
    public void Group_is_completable_when_all_three_dimensions_are_satisfied()
    {
        var group = Group(
            receiptDone: true,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied,
            fiscalUploaded: true);

        Assert.True(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    [Fact]
    public void Group_is_not_completable_without_a_fiscal_receipt_attachment_id()
    {
        // A timestamp without an attachment id would leave GROUP_COMPLETED with no stable
        // identity — the evaluation must refuse rather than invent one.
        var group = Group(receiptDone: true, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied);
        group.FiscalReceiptUploadedAtUtc = DateTime.UtcNow;
        group.FiscalReceiptAttachmentId = null;

        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));

        group.FiscalReceiptAttachmentId = Guid.Empty;
        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    [Fact]
    public void Group_is_not_completable_while_the_operational_receipt_is_pending()
    {
        var group = Group(
            receiptDone: false,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied,
            fiscalUploaded: true);

        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    // ── Phase 4A: conditional fiscal receipt (RequiresSeparateFiscalReceipt) ──

    [Fact]
    public void No_separate_receipt_owed_derives_not_required()
    {
        // A classified Factura-Recibo group owes no separate receipt — whatever the state of the
        // other dimensions, this dimension reads NOT_REQUIRED, never LOCKED/PENDING.
        var open = Group(receiptDone: false,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.NotRequired,
            separateReceiptRequired: false);
        var done = Group(receiptDone: true,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.NotRequired,
            separateReceiptRequired: false);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.NotRequired, FiscalReceiptStateDeriver.Derive(open));
        Assert.Equal(RequestConstants.FiscalReceiptStatuses.NotRequired, FiscalReceiptStateDeriver.Derive(done));
        // Nothing owed means nothing to upload.
        Assert.False(FiscalReceiptStateDeriver.CanUploadFiscalReceipt(done));
    }

    [Fact]
    public void No_separate_receipt_group_is_completable_without_any_attachment()
    {
        var group = Group(receiptDone: true,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.NotRequired,
            separateReceiptRequired: false);

        Assert.True(FiscalReceiptStateDeriver.IsGroupCompletable(group));
        Assert.Null(group.FiscalReceiptAttachmentId);
    }

    [Fact]
    public void Unclassified_group_stays_locked_even_with_the_receipt_flag_at_its_default()
    {
        // RequiresSeparateFiscalReceipt is still at its column default on an unclassified group —
        // reading that default as "not owed" would silently discharge an unknown obligation.
        var group = Group(receiptDone: true, separateReceiptRequired: false);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    [Fact]
    public void Pending_reason_reports_completed_for_a_no_separate_receipt_group()
    {
        var group = Group(receiptDone: true,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied,
            separateReceiptRequired: false);

        Assert.Equal(PostPaymentPendingReason.Completed, PostPaymentPendingReason.Compute(group));
    }

    // ── Pending reason ──

    [Fact]
    public void Pending_reason_lists_classification_before_anything_invoice_shaped()
    {
        var group = Group(receiptDone: true, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Unclassified);

        Assert.Equal(PostPaymentPendingReason.ClassificationPending, PostPaymentPendingReason.Compute(group));
    }

    [Fact]
    public void Pending_reason_lists_both_open_dimensions()
    {
        var group = Group(receiptDone: false, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.PendingUpload);

        var reason = PostPaymentPendingReason.Compute(group);

        Assert.Contains(PostPaymentPendingReason.OperationalReceipt, reason, StringComparison.Ordinal);
        Assert.Contains(PostPaymentPendingReason.FinalInvoice, reason, StringComparison.Ordinal);
        Assert.DoesNotContain(PostPaymentPendingReason.FiscalReceipt, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_reason_never_lists_a_locked_fiscal_receipt_it_is_not_actionable()
    {
        var group = Group(receiptDone: false, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied);

        Assert.Equal(PostPaymentPendingReason.OperationalReceipt, PostPaymentPendingReason.Compute(group));
    }

    [Fact]
    public void Pending_reason_lists_the_fiscal_receipt_once_it_becomes_actionable()
    {
        var group = Group(receiptDone: true, operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied);

        Assert.Equal(PostPaymentPendingReason.FiscalReceipt, PostPaymentPendingReason.Compute(group));
    }

    [Fact]
    public void Pending_reason_reports_completed_when_nothing_is_outstanding()
    {
        var group = Group(
            receiptDone: true,
            operationInvoiceStatus: RequestConstants.OperationInvoiceStatuses.Satisfied,
            fiscalUploaded: true);

        Assert.Equal(PostPaymentPendingReason.Completed, PostPaymentPendingReason.Compute(group));
    }

    // ── Status set consistency ──

    [Fact]
    public void Satisfied_and_blocking_status_sets_are_disjoint_and_cover_every_status()
    {
        var all = new[]
        {
            // Release 3: REJECTED and REPLACEMENT_REQUESTED are no longer AGGREGATE states — a
            // rejected document leaves its group PENDING_UPLOAD or PARTIALLY_INVOICED, and the
            // rejection itself lives on the document. PARTIALLY_INVOICED is new: real coverage,
            // legitimately short of the total.
            RequestConstants.OperationInvoiceStatuses.Unclassified,
            RequestConstants.OperationInvoiceStatuses.NotRequired,
            RequestConstants.OperationInvoiceStatuses.PendingUpload,
            RequestConstants.OperationInvoiceStatuses.PartiallyInvoiced,
            RequestConstants.OperationInvoiceStatuses.PendingValidation,
            RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequestConstants.OperationInvoiceStatuses.DivergenceDetected
        };

        foreach (var status in all)
        {
            var satisfied = RequestConstants.OperationInvoiceStatuses.IsSatisfied(status);
            var blocking = RequestConstants.OperationInvoiceStatuses.IsBlocking(status);

            Assert.NotEqual(satisfied, blocking);
        }
    }

    [Fact]
    public void Unclassified_never_accepts_an_upload_classification_comes_first()
    {
        Assert.DoesNotContain(
            RequestConstants.OperationInvoiceStatuses.Unclassified,
            RequestConstants.OperationInvoiceStatuses.AcceptsUpload);
    }
}
