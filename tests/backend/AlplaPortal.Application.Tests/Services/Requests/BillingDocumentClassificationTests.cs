using System;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 2 (Document Classification) — the rules that decide a PO group's Final Invoice
/// obligation from the billing document that originated the request or the winning quotation.
///
/// The single most important property here: an unknown or ambiguous classification resolves to
/// UNCLASSIFIED, never to "nothing is owed". Getting that backwards would let a request that owes
/// a Final Invoice close silently.
/// </summary>
public class BillingDocumentClassificationTests
{
    // ── Obligation mapping ──

    [Fact]
    public void Proforma_creates_a_pending_final_invoice_obligation()
    {
        Assert.Equal(
            RequestConstants.FinalInvoiceStatuses.PendingUpload,
            RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(RequestConstants.BillingDocumentTypes.Proforma));
    }

    [Fact]
    public void Initial_final_invoice_creates_no_further_obligation()
    {
        Assert.Equal(
            RequestConstants.FinalInvoiceStatuses.NotApplicableInitialFinalInvoice,
            RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(RequestConstants.BillingDocumentTypes.FinalInvoice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("RECEIPT")]
    [InlineData("FATURA")]
    [InlineData("PROFORMA_INVOICE")]
    public void Unknown_or_missing_classification_never_silently_clears_the_obligation(string? raw)
    {
        // The dangerous failure mode is mapping an unrecognised value to NOT_APPLICABLE.
        var status = RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(raw);

        Assert.Equal(RequestConstants.FinalInvoiceStatuses.Unclassified, status);
        Assert.NotEqual(RequestConstants.FinalInvoiceStatuses.NotApplicableInitialFinalInvoice, status);
    }

    // ── Normalisation ──

    [Theory]
    [InlineData("proforma", "PROFORMA")]
    [InlineData("  PROFORMA  ", "PROFORMA")]
    [InlineData("final_invoice", "FINAL_INVOICE")]
    public void Values_are_normalised_to_canonical_form(string raw, string expected)
    {
        Assert.Equal(expected, RequestConstants.BillingDocumentTypes.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_normalises_to_null_never_to_a_type(string? raw)
    {
        Assert.Null(RequestConstants.BillingDocumentTypes.Normalize(raw));
    }

    [Fact]
    public void Normalised_blank_is_not_a_valid_selection()
    {
        Assert.False(RequestConstants.BillingDocumentTypes.IsValid(
            RequestConstants.BillingDocumentTypes.Normalize("   ")));
    }

    [Fact]
    public void Display_names_are_the_portuguese_labels_used_in_the_ui()
    {
        Assert.Equal("Fatura Proforma",
            RequestConstants.BillingDocumentTypes.DisplayName(RequestConstants.BillingDocumentTypes.Proforma));
        Assert.Equal("Fatura Final",
            RequestConstants.BillingDocumentTypes.DisplayName(RequestConstants.BillingDocumentTypes.FinalInvoice));
        Assert.Equal("Não classificado", RequestConstants.BillingDocumentTypes.DisplayName(null));
    }

    // ── Submission gate ──

    private static PostPaymentCompletionOptions Enabled(DateTime effective) => new()
    {
        Enabled = true,
        EffectiveDateUtc = effective
    };

    private static Request PaymentRequest(DateTime createdAtUtc, string? billingDocumentType) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAtUtc = createdAtUtc,
        BillingDocumentType = billingDocumentType
    };

    /// <summary>Mirrors the SubmitRequest gate: classification required only when it is mandatory AND absent.</summary>
    private static bool SubmissionBlocked(PostPaymentCompletionOptions options, Request request) =>
        PostPaymentCompletionPolicy.IsNewWorkflowMandatory(options, request) &&
        !RequestConstants.BillingDocumentTypes.IsValid(request.BillingDocumentType);

    [Fact]
    public void Submission_is_blocked_when_a_mandatory_request_has_no_classification()
    {
        var effective = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var request = PaymentRequest(effective.AddDays(1), billingDocumentType: null);

        Assert.True(SubmissionBlocked(Enabled(effective), request));
    }

    [Theory]
    [InlineData("PROFORMA")]
    [InlineData("FINAL_INVOICE")]
    public void Submission_passes_once_a_valid_classification_is_chosen(string chosen)
    {
        var effective = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var request = PaymentRequest(effective.AddDays(1), chosen);

        Assert.False(SubmissionBlocked(Enabled(effective), request));
    }

    [Fact]
    public void Submission_of_a_pre_effective_date_request_is_not_blocked()
    {
        // Historical requests keep the old submission rules; they are classified later through the
        // Release 5 Finance workflow rather than being retro-blocked here.
        var effective = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var request = PaymentRequest(effective.AddDays(-1), billingDocumentType: null);

        Assert.False(SubmissionBlocked(Enabled(effective), request));
    }

    [Fact]
    public void Submission_is_never_blocked_while_the_feature_is_disabled()
    {
        var request = PaymentRequest(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), billingDocumentType: null);

        Assert.False(SubmissionBlocked(new PostPaymentCompletionOptions(), request));
    }

    // ── PO group propagation ──

    [Theory]
    [InlineData("PROFORMA", "PENDING_UPLOAD")]
    [InlineData("FINAL_INVOICE", "NOT_APPLICABLE")]
    [InlineData(null, "UNCLASSIFIED")]
    public void Propagating_a_request_classification_sets_the_group_obligation(string? requestType, string expectedStatus)
    {
        var group = new RequestPoGroup
        {
            BillingDocumentType = requestType,
            FinalInvoiceStatus = RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(requestType)
        };

        Assert.Equal(expectedStatus, group.FinalInvoiceStatus);
        Assert.Equal(requestType, group.BillingDocumentType);
    }

    [Fact]
    public void A_group_left_unclassified_still_blocks_completion()
    {
        var group = new RequestPoGroup
        {
            FinalInvoiceStatus = RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(null),
            OperationalReceiptCompletedAtUtc = DateTime.UtcNow,
            FiscalReceiptUploadedAtUtc = DateTime.UtcNow,
            FiscalReceiptAttachmentId = Guid.NewGuid()
        };

        // Every other dimension is satisfied; the missing classification alone must hold it back.
        Assert.False(Domain.Services.FiscalReceiptStateDeriver.IsGroupCompletable(group));
    }
}
