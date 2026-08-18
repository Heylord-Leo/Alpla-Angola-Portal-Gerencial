using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Req = RequestConstants.Statuses;

/// <summary>
/// Release 4 Phase 2: the operation-invoice document lifecycle, pinned as data.
///
/// <para>Two approved rules carry most of the weight: an invoice may arrive at any point of the
/// post-approval window INCLUDING after payment (the PROFORMA regularization case), and a
/// VALIDATED invoice is immutable — no edit, no void, replacement only.</para>
/// </summary>
public class OperationInvoiceLifecyclePolicyTests
{
    // ── Request-status window ──

    [Theory]
    [InlineData(Req.FinalApproved)]
    [InlineData(Req.PoPartiallyUploaded)]
    [InlineData(Req.PoIssued)]
    [InlineData(Req.PaymentRequestSent)]
    [InlineData(Req.PaymentScheduled)]
    [InlineData(Req.Paid)]
    [InlineData(Req.PaymentCompleted)]
    [InlineData(Req.WaitingSupplierDelivery)]
    [InlineData(Req.WaitingReceipt)]
    [InlineData(Req.WaitingReconciliation)]
    [InlineData(Req.InFollowup)]
    [InlineData(Req.WaitingFiscalReceipt)]
    [InlineData(Req.AdvancePaymentRequired)]
    [InlineData(Req.AdvancePaymentScheduled)]
    [InlineData(Req.AdvancePaymentCompleted)]
    public void The_whole_post_approval_window_accepts_an_invoice_including_after_payment(string status)
    {
        Assert.True(OperationInvoiceLifecyclePolicy.CanCreateInRequestStatus(status));
    }

    [Theory]
    [InlineData(Req.Draft)]
    [InlineData(Req.WaitingQuotation)]
    [InlineData(Req.WaitingAreaApproval)]
    [InlineData(Req.WaitingFinalApproval)]
    [InlineData(Req.WaitingCostCenter)]
    [InlineData(Req.AreaAdjustment)]
    [InlineData(Req.FinalAdjustment)]
    [InlineData(Req.Rejected)]
    [InlineData(Req.Cancelled)]
    [InlineData(Req.Completed)]
    // Not in the approved allow-list, pinned as blocked; flagged for review since it is a
    // post-approval correction stage.
    [InlineData(Req.WaitingPoCorrection)]
    [InlineData(null)]
    public void Pre_approval_terminal_and_completed_requests_accept_nothing(string? status)
    {
        // COMPLETED stays closed on purpose: its obligation is satisfied by construction, and a
        // reopen mechanism is a future Finance decision, not a side door here.
        Assert.False(OperationInvoiceLifecyclePolicy.CanCreateInRequestStatus(status));
    }

    // ── Document lifecycle ──

    [Fact]
    public void Manual_creation_lands_in_finances_queue()
    {
        Assert.Equal(Doc.PendingValidation, OperationInvoiceLifecyclePolicy.InitialManualStatus);
    }

    [Theory]
    [InlineData(Doc.Uploaded, true)]
    [InlineData(Doc.PendingValidation, true)]
    [InlineData(Doc.Validated, false)]
    [InlineData(Doc.Rejected, false)]
    [InlineData(Doc.ReplacementRequested, false)]
    [InlineData(Doc.DivergenceDetected, false)]
    [InlineData(Doc.Voided, false)]
    public void Editing_stops_at_validation_and_never_returns(string status, bool editable)
    {
        Assert.Equal(editable, OperationInvoiceLifecyclePolicy.IsEditable(status));
    }

    [Fact]
    public void The_broad_IsEditable_helper_is_not_this_policy()
    {
        // The old helper answers "not yet validated" and wrongly reads terminal documents as
        // editable — the policy exists precisely to not inherit that.
        Assert.True(Doc.IsEditable(Doc.Rejected));
        Assert.False(OperationInvoiceLifecyclePolicy.IsEditable(Doc.Rejected));
    }

    [Theory]
    [InlineData(Doc.PendingValidation, true)]
    [InlineData(Doc.Uploaded, false)]
    [InlineData(Doc.Validated, false)]
    [InlineData(Doc.Voided, false)]
    public void Only_a_document_in_the_queue_can_be_validated(string status, bool can)
    {
        Assert.Equal(can, OperationInvoiceLifecyclePolicy.CanValidate(status));
    }

    [Theory]
    [InlineData(Doc.Uploaded, true)]
    [InlineData(Doc.PendingValidation, true)]
    [InlineData(Doc.Validated, false)]     // approved rule #8: no void after validation
    [InlineData(Doc.Rejected, false)]
    [InlineData(Doc.Voided, false)]
    public void Void_exists_only_before_validation(string status, bool can)
    {
        Assert.Equal(can, OperationInvoiceLifecyclePolicy.CanVoid(status));
    }

    [Theory]
    [InlineData(Doc.Validated, true)]
    [InlineData(Doc.PendingValidation, false)]   // pre-validation: edit or void, never a chain
    [InlineData(Doc.Rejected, false)]
    [InlineData(Doc.ReplacementRequested, false)]
    public void Replacement_is_the_only_path_out_of_validated(string status, bool can)
    {
        Assert.Equal(can, OperationInvoiceLifecyclePolicy.CanReplace(status));
    }

    // ── Duplicate effectiveness ──

    [Theory]
    [InlineData(Doc.Uploaded, true)]
    [InlineData(Doc.PendingValidation, true)]
    [InlineData(Doc.Validated, true)]
    [InlineData(Doc.DivergenceDetected, true)]
    [InlineData(Doc.Voided, false)]
    [InlineData(Doc.Rejected, false)]
    [InlineData(Doc.ReplacementRequested, false)]
    public void Terminal_documents_no_longer_occupy_a_fiscal_identity(string status, bool effective)
    {
        // A voided/rejected/superseded invoice is not a recognized debt, so a corrected reissue
        // with the same supplier + number + series must pass the duplicate check.
        Assert.Equal(effective, OperationInvoiceLifecyclePolicy.IsEffectiveForDuplicateCheck(status));
    }

    [Fact]
    public void Voided_is_terminal_in_the_shared_status_family_too()
    {
        Assert.Contains(Doc.Voided, Doc.Terminal);
        Assert.False(Doc.CountsTowardCoverage(Doc.Voided));
        Assert.False(Doc.IsAwaitingDecision(Doc.Voided));
    }
}
