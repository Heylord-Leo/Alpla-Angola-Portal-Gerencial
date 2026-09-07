using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;
using RE = AlplaPortal.Domain.Services.ReceivingActionEvaluator;
using S = AlplaPortal.Domain.Constants.RequestConstants;

namespace AlplaPortal.Application.Tests.Services.Receiving;

/// <summary>
/// B4.1 — canonical Receiving action evaluator. These lock the exact group-status guards previously
/// inline in RequestsController.MoveToReceipt / ConfirmReceiving (behavior-preserving extraction), so a
/// regression in either the endpoints or the Dashboard is caught here.
/// </summary>
public class ReceivingActionEvaluatorTests
{
    [Theory]
    [InlineData("PAYMENT_COMPLETED", true)]
    [InlineData("WAITING_RECEIPT", false)]
    [InlineData("IN_FOLLOWUP", false)]
    [InlineData("WAITING_SUPPLIER_DELIVERY", false)]
    [InlineData("WAITING_PO", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("CANCELLED", false)]
    public void CanMoveToReceipt_only_from_payment_completed(string status, bool expected)
        => Assert.Equal(expected, RE.CanMoveToReceipt(status));

    [Theory]
    [InlineData("WAITING_RECEIPT", true)]
    [InlineData("IN_FOLLOWUP", true)]
    [InlineData("PAYMENT_COMPLETED", true)]
    [InlineData("WAITING_SUPPLIER_DELIVERY", true)]
    [InlineData("WAITING_PO", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("CANCELLED", false)]
    public void CanConfirmReceiving_matches_the_endpoint_guard(string status, bool expected)
        => Assert.Equal(expected, RE.CanConfirmReceiving(status));

    [Fact]
    public void PaymentCompleted_exposes_both_actions()
    {
        var actions = RE.Evaluate(S.Statuses.PaymentCompleted);
        Assert.Contains(RE.MoveToReceipt, actions);
        Assert.Contains(RE.ConfirmReceiving, actions);
        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public void WaitingPo_and_null_and_completed_are_not_actionable()
    {
        foreach (var s in new[] { "WAITING_PO", "COMPLETED", "CANCELLED", "PENDING" })
            Assert.False(RE.IsReceivingActionable(s));
        Assert.False(RE.IsReceivingActionable(null));
        Assert.Empty(RE.Evaluate(null));
    }

    [Theory]
    [InlineData("PAYMENT_COMPLETED", "READY_FOR_RECEIPT")]
    [InlineData("WAITING_RECEIPT", "WAITING_RECEIPT")]
    [InlineData("IN_FOLLOWUP", "IN_FOLLOWUP")]
    [InlineData("WAITING_SUPPLIER_DELIVERY", "WAITING_SUPPLIER_DELIVERY")]
    public void ActionableBucket_maps_each_status_to_one_bucket(string status, string bucket)
        => Assert.Equal(bucket, RE.ActionableBucket(status));

    [Fact]
    public void NonActionable_status_has_no_bucket()
    {
        Assert.Null(RE.ActionableBucket("WAITING_PO"));
        Assert.Null(RE.ActionableBucket(null));
    }

    [Fact]
    public void ActionableStatuses_is_the_union_used_by_the_prefilter()
    {
        var set = RE.ActionableStatuses;
        Assert.Contains(S.Statuses.PaymentCompleted, set);
        Assert.Contains(S.Statuses.WaitingReceipt, set);
        Assert.Contains(S.Statuses.InFollowup, set);
        Assert.Contains(S.Statuses.WaitingSupplierDelivery, set);
        Assert.DoesNotContain("WAITING_PO", set);
        // Every listed status is actionable, and none duplicates.
        Assert.All(set, s => Assert.True(RE.IsReceivingActionable(s)));
        Assert.Equal(set.Count, set.Distinct().Count());
    }
}
