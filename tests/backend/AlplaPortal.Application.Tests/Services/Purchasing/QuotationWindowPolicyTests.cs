using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;
using P = AlplaPortal.Domain.Services.QuotationWindowPolicy;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// v2.249.1 — item-level quotation window (pure policy). Locks the rule shared by the write endpoints
/// (SaveQuotation / OcrExtract / quotation-document upload) and the Buyer queue projection: open lines =
/// pending, not deleted, not held by an active/approved batch; only for non-terminal batch-model QUOTATION
/// requests. Legacy requests without batches stay closed regardless of null lifecycles.
/// </summary>
public class QuotationWindowPolicyTests
{
    private static P.LineFacts Line(string? lifecycle = null, bool deleted = false, bool held = false)
        => new(Guid.NewGuid(), deleted, lifecycle, held);

    [Fact]
    public void Released_line_with_sibling_group_past_po_gate_is_open()
    {
        var released = Line(RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        var approvedHeld = Line(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, held: true);
        var e = P.Evaluate("QUOTATION", "PO_PARTIALLY_UPLOADED", false, true, new[] { released, approvedHeld });
        Assert.True(e.IsOpen);
        Assert.Equal(new[] { released.LineItemId }, e.OpenLineItemIds);
        Assert.True(e.IsLineOpen(released.LineItemId));
        Assert.False(e.IsLineOpen(approvedHeld.LineItemId));
    }

    [Fact]
    public void Partial_submission_pending_line_is_open_but_batch_held_line_is_not()
    {
        var pending = Line(null);
        var inBatch = Line(RequestConstants.QuotationLifecycleStatuses.BatchAssigned, held: true);
        var e = P.Evaluate("QUOTATION", "WAITING_QUOTATION", false, true, new[] { pending, inBatch });
        Assert.True(e.IsOpen);
        Assert.Single(e.OpenLineItemIds, pending.LineItemId);
    }

    [Fact]
    public void Legacy_request_without_batches_stays_closed_even_with_null_lifecycles()
    {
        var e = P.Evaluate("QUOTATION", "PO_ISSUED", false, hasApprovalBatches: false, new[] { Line(null), Line(null) });
        Assert.False(e.IsOpen);
        Assert.Equal(P.ClosedNoBatchWorkflow, e.ClosedReasonCode);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REJECTED")]
    [InlineData("COMPLETED")]
    public void Terminal_request_is_closed(string status)
    {
        var e = P.Evaluate("QUOTATION", status, false, true, new[] { Line(null) });
        Assert.False(e.IsOpen);
        Assert.Equal(P.ClosedTerminal, e.ClosedReasonCode);
    }

    [Fact]
    public void Cancelled_flag_closes_regardless_of_status_code()
    {
        var e = P.Evaluate("QUOTATION", "WAITING_QUOTATION", isCancelled: true, true, new[] { Line(null) });
        Assert.False(e.IsOpen);
    }

    [Fact]
    public void Payment_request_is_never_open()
    {
        var e = P.Evaluate("PAYMENT", "WAITING_QUOTATION", false, true, new[] { Line(null) });
        Assert.False(e.IsOpen);
        Assert.Equal(P.ClosedNotQuotationType, e.ClosedReasonCode);
    }

    [Fact]
    public void No_open_lines_when_everything_is_held_approved_closed_or_deleted()
    {
        var e = P.Evaluate("QUOTATION", "PO_ISSUED", false, true, new[]
        {
            Line(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, held: true),
            Line(RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted),
            Line(RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval),
            Line(RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted),
            Line(null, deleted: true),
            Line(null, held: true),
        });
        Assert.False(e.IsOpen);
        Assert.Equal(P.ClosedNoOpenLines, e.ClosedReasonCode);
    }

    [Fact]
    public void HeldLineItemIds_ignores_released_items_and_non_holding_batches()
    {
        var heldLine = Guid.NewGuid(); var releasedLine = Guid.NewGuid(); var rejectedLine = Guid.NewGuid(); var cancelledBatchLine = Guid.NewGuid();
        var batches = new[]
        {
            new ApprovalBatch { Status = RequestConstants.ApprovalBatchStatuses.Approved, Items = new List<ApprovalBatchItem>
            {
                new() { RequestLineItemId = heldLine },
                new() { RequestLineItemId = releasedLine, ReleasedAtUtc = DateTime.UtcNow, ReleaseKind = "RESTART_QUOTATION" },
            }},
            new ApprovalBatch { Status = RequestConstants.ApprovalBatchStatuses.Rejected, Items = new List<ApprovalBatchItem> { new() { RequestLineItemId = rejectedLine } } },
            new ApprovalBatch { Status = RequestConstants.ApprovalBatchStatuses.Cancelled, Items = new List<ApprovalBatchItem> { new() { RequestLineItemId = cancelledBatchLine } } },
        };
        var held = P.HeldLineItemIds(batches);
        Assert.Equal(new[] { heldLine }, held.ToArray());
    }

    [Fact]
    public void EvaluateLoaded_uses_navigation_and_holds()
    {
        var line1 = new RequestLineItem { Id = Guid.NewGuid(), QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved };
        var line2 = new RequestLineItem { Id = Guid.NewGuid(), QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending };
        var r = new Request
        {
            RequestType = new RequestType { Code = RequestConstants.Types.Quotation },
            Status = new RequestStatus { Code = "PO_PARTIALLY_UPLOADED" },
            LineItems = new List<RequestLineItem> { line1, line2 },
            ApprovalBatches = new List<ApprovalBatch>
            {
                new() { Status = RequestConstants.ApprovalBatchStatuses.Approved, Items = new List<ApprovalBatchItem>
                {
                    new() { RequestLineItemId = line1.Id },
                    new() { RequestLineItemId = line2.Id, ReleasedAtUtc = DateTime.UtcNow },
                }},
            },
        };
        var e = P.EvaluateLoaded(r);
        Assert.True(e.IsOpen);
        Assert.Single(e.OpenLineItemIds, line2.Id);
    }

    // v2.249.2 — committed lifecycles: never a MAPPED/SUBSTITUTE target, in the scalar window too (mixed payloads fail as a whole).
    [Theory]
    [InlineData("QUOTATION_APPROVED", true)]
    [InlineData("CLOSED_NOT_QUOTED", true)]
    [InlineData("CANCELLED_AFTER_APPROVAL", true)]
    [InlineData("NOT_QUOTED_ACCEPTED", true)]
    [InlineData("BATCH_ASSIGNED", false)]   // rework revisions map onto items of a batch under adjustment
    [InlineData("QUOTATION_PENDING", false)]
    [InlineData("NOT_QUOTED_PROPOSED", false)]
    [InlineData(null, false)]
    public void Committed_lifecycles_are_exactly_the_post_approval_terminal_ones(string? lifecycle, bool committed)
    {
        Assert.Equal(committed, P.IsCommittedLifecycle(lifecycle));
    }
}
