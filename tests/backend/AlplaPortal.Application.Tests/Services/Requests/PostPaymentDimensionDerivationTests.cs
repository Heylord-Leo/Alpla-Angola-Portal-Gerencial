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
    private static RequestPoGroup Group(
        bool receiptDone = false,
        string finalInvoiceStatus = RequestConstants.FinalInvoiceStatuses.Unclassified,
        bool fiscalUploaded = false,
        Guid? fiscalAttachmentId = null) => new()
        {
            Id = Guid.NewGuid(),
            OperationalReceiptCompletedAtUtc = receiptDone ? DateTime.UtcNow : null,
            FinalInvoiceStatus = finalInvoiceStatus,
            FiscalReceiptUploadedAtUtc = fiscalUploaded ? DateTime.UtcNow : null,
            FiscalReceiptAttachmentId = fiscalUploaded ? (fiscalAttachmentId ?? Guid.NewGuid()) : fiscalAttachmentId
        };

    // ── Entity default ──

    [Fact]
    public void New_group_defaults_to_unclassified()
    {
        var group = new RequestPoGroup();

        Assert.Equal(RequestConstants.FinalInvoiceStatuses.Unclassified, group.FinalInvoiceStatus);
        Assert.Null(group.BillingDocumentType);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.Null(group.FiscalReceiptAttachmentId);
        Assert.Null(group.CompletedAtUtc);
    }

    // ── Billing document type → obligation ──

    [Theory]
    [InlineData("PROFORMA", "PENDING_UPLOAD")]
    [InlineData("proforma", "PENDING_UPLOAD")]
    [InlineData("FINAL_INVOICE", "NOT_APPLICABLE")]
    [InlineData(null, "UNCLASSIFIED")]
    [InlineData("", "UNCLASSIFIED")]
    [InlineData("SOMETHING_ELSE", "UNCLASSIFIED")]
    public void Billing_document_type_maps_to_the_expected_obligation(string? type, string expected)
    {
        Assert.Equal(expected, RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(type));
    }

    [Fact]
    public void Only_proforma_requires_a_subsequent_final_invoice()
    {
        Assert.True(RequestConstants.BillingDocumentTypes.RequiresFinalInvoice("PROFORMA"));
        Assert.False(RequestConstants.BillingDocumentTypes.RequiresFinalInvoice("FINAL_INVOICE"));
        Assert.False(RequestConstants.BillingDocumentTypes.RequiresFinalInvoice(null));
    }

    [Fact]
    public void Only_two_billing_document_types_are_in_scope()
    {
        Assert.Equal(2, RequestConstants.BillingDocumentTypes.ValidValues.Length);
        Assert.True(RequestConstants.BillingDocumentTypes.IsValid("PROFORMA"));
        Assert.True(RequestConstants.BillingDocumentTypes.IsValid("FINAL_INVOICE"));
        Assert.False(RequestConstants.BillingDocumentTypes.IsValid("RECEIPT"));
        Assert.False(RequestConstants.BillingDocumentTypes.IsValid(null));
    }

    // ── Fiscal Receipt is the terminal step ──

    [Fact]
    public void Fiscal_receipt_is_locked_while_operational_receipt_is_pending()
    {
        var group = Group(receiptDone: false, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
        Assert.False(FiscalReceiptStateDeriver.CanUploadFiscalReceipt(group));
    }

    [Fact]
    public void Fiscal_receipt_is_locked_while_the_final_invoice_is_outstanding()
    {
        var group = Group(receiptDone: true, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.PendingValidation);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
    }

    [Fact]
    public void Unclassified_group_can_never_unlock_the_fiscal_receipt()
    {
        var group = Group(receiptDone: true, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Unclassified);

        Assert.Equal(RequestConstants.FiscalReceiptStatuses.Locked, FiscalReceiptStateDeriver.Derive(group));
        Assert.False(FiscalReceiptStateDeriver.CanUploadFiscalReceipt(group));
        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    [Theory]
    [InlineData("VALIDATED")]
    [InlineData("NOT_APPLICABLE")]
    public void Fiscal_receipt_unlocks_once_receipt_and_invoice_are_satisfied(string satisfiedStatus)
    {
        var group = Group(receiptDone: true, finalInvoiceStatus: satisfiedStatus);

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
            finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated,
            fiscalUploaded: true);

        Assert.True(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    [Fact]
    public void Group_is_not_completable_without_a_fiscal_receipt_attachment_id()
    {
        // A timestamp without an attachment id would leave GROUP_COMPLETED with no stable
        // identity — the evaluation must refuse rather than invent one.
        var group = Group(receiptDone: true, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated);
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
            finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated,
            fiscalUploaded: true);

        Assert.False(FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }

    // ── Pending reason ──

    [Fact]
    public void Pending_reason_lists_classification_before_anything_invoice_shaped()
    {
        var group = Group(receiptDone: true, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Unclassified);

        Assert.Equal(PostPaymentPendingReason.ClassificationPending, PostPaymentPendingReason.Compute(group));
    }

    [Fact]
    public void Pending_reason_lists_both_open_dimensions()
    {
        var group = Group(receiptDone: false, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.PendingUpload);

        var reason = PostPaymentPendingReason.Compute(group);

        Assert.Contains(PostPaymentPendingReason.OperationalReceipt, reason, StringComparison.Ordinal);
        Assert.Contains(PostPaymentPendingReason.FinalInvoice, reason, StringComparison.Ordinal);
        Assert.DoesNotContain(PostPaymentPendingReason.FiscalReceipt, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_reason_never_lists_a_locked_fiscal_receipt_it_is_not_actionable()
    {
        var group = Group(receiptDone: false, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated);

        Assert.Equal(PostPaymentPendingReason.OperationalReceipt, PostPaymentPendingReason.Compute(group));
    }

    [Fact]
    public void Pending_reason_lists_the_fiscal_receipt_once_it_becomes_actionable()
    {
        var group = Group(receiptDone: true, finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated);

        Assert.Equal(PostPaymentPendingReason.FiscalReceipt, PostPaymentPendingReason.Compute(group));
    }

    [Fact]
    public void Pending_reason_reports_completed_when_nothing_is_outstanding()
    {
        var group = Group(
            receiptDone: true,
            finalInvoiceStatus: RequestConstants.FinalInvoiceStatuses.Validated,
            fiscalUploaded: true);

        Assert.Equal(PostPaymentPendingReason.Completed, PostPaymentPendingReason.Compute(group));
    }

    // ── Status set consistency ──

    [Fact]
    public void Satisfied_and_blocking_status_sets_are_disjoint_and_cover_every_status()
    {
        var all = new[]
        {
            RequestConstants.FinalInvoiceStatuses.Unclassified,
            RequestConstants.FinalInvoiceStatuses.NotApplicableInitialFinalInvoice,
            RequestConstants.FinalInvoiceStatuses.PendingUpload,
            RequestConstants.FinalInvoiceStatuses.PendingValidation,
            RequestConstants.FinalInvoiceStatuses.Validated,
            RequestConstants.FinalInvoiceStatuses.Rejected,
            RequestConstants.FinalInvoiceStatuses.ReplacementRequested,
            RequestConstants.FinalInvoiceStatuses.DivergenceDetected
        };

        foreach (var status in all)
        {
            var satisfied = RequestConstants.FinalInvoiceStatuses.IsSatisfied(status);
            var blocking = RequestConstants.FinalInvoiceStatuses.IsBlocking(status);

            Assert.NotEqual(satisfied, blocking);
        }
    }

    [Fact]
    public void Unclassified_never_accepts_an_upload_classification_comes_first()
    {
        Assert.DoesNotContain(
            RequestConstants.FinalInvoiceStatuses.Unclassified,
            RequestConstants.FinalInvoiceStatuses.AcceptsUpload);
    }
}
