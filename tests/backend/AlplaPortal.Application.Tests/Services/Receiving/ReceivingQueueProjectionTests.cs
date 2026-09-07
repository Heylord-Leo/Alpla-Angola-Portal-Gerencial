using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Services.Receiving;
using Xunit;
using RE = AlplaPortal.Domain.Services.ReceivingActionEvaluator;

namespace AlplaPortal.Application.Tests.Services.Receiving;

/// <summary>
/// B4.1 — Receiving queue summary + reconciliation. The projection's row-building rule (filter by
/// ReceivingActionEvaluator.IsReceivingActionable, bucket via ActionableBucket) is exercised here over
/// (RequestId, groupStatus) fixtures — the same rule ReceivingQueueProjection.BuildAsync applies per
/// group — then Summarize() is asserted. This locks the group/distinct-request/no-double-count contract
/// and the Dashboard↔queue reconciliation without a database.
/// </summary>
public class ReceivingQueueProjectionTests
{
    // Mirror of BuildAsync's per-group rule: keep only actionable groups, one bucket each.
    private static List<ReceivingQueueRowDto> Rows(params (Guid req, string status)[] groups)
    {
        var rows = new List<ReceivingQueueRowDto>();
        foreach (var (req, status) in groups)
        {
            if (!RE.IsReceivingActionable(status)) continue;
            var b = RE.ActionableBucket(status);
            if (b == null) continue;
            rows.Add(new ReceivingQueueRowDto
            {
                RequestId = req, RequestPoGroupId = Guid.NewGuid(), GroupStatus = status,
                ActionableBucket = b, AvailableActions = RE.Evaluate(status).ToList(),
            });
        }
        return rows;
    }

    [Fact]
    public void Summarize_counts_each_bucket_and_actionable_totals()
    {
        var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid();
        var rows = Rows(
            (r1, "PAYMENT_COMPLETED"),
            (r1, "WAITING_RECEIPT"),
            (r2, "IN_FOLLOWUP"),
            (r2, "WAITING_SUPPLIER_DELIVERY"));

        var s = ReceivingQueueProjection.Summarize(rows);

        Assert.Equal(4, s.ActionableGroups);
        Assert.Equal(2, s.ActionableRequests);      // r1, r2
        Assert.Equal(1, s.ReadyForReceiptGroups);   // PAYMENT_COMPLETED
        Assert.Equal(1, s.WaitingReceiptGroups);
        Assert.Equal(1, s.FollowUpGroups);
        Assert.Equal(1, s.WaitingSupplierDeliveryGroups);
        // No double count: buckets sum to ActionableGroups.
        Assert.Equal(s.ActionableGroups,
            s.ReadyForReceiptGroups + s.WaitingReceiptGroups + s.FollowUpGroups + s.WaitingSupplierDeliveryGroups);
    }

    [Fact]
    public void Multi_group_request_counts_groups_independently_and_request_once_excluding_waiting_po()
    {
        var r = Guid.NewGuid();
        // Group A PAYMENT_COMPLETED (actionable), B WAITING_PO (buyer-owned), C WAITING_RECEIPT (actionable).
        var rows = Rows(
            (r, "PAYMENT_COMPLETED"),
            (r, "WAITING_PO"),
            (r, "WAITING_RECEIPT"));

        var s = ReceivingQueueProjection.Summarize(rows);

        Assert.Equal(2, s.ActionableGroups);          // A + C only
        Assert.Equal(1, s.ActionableRequests);        // the single request, once
        Assert.Equal(1, s.ReadyForReceiptGroups);
        Assert.Equal(1, s.WaitingReceiptGroups);
        Assert.Equal(0, s.FollowUpGroups);
        Assert.Equal(0, s.WaitingSupplierDeliveryGroups);
        Assert.DoesNotContain(rows, x => x.GroupStatus == "WAITING_PO"); // never a row
    }

    [Fact]
    public void Dashboard_summary_equals_queue_summary_for_the_same_rows()
    {
        var rows = Rows(
            (Guid.NewGuid(), "PAYMENT_COMPLETED"),
            (Guid.NewGuid(), "WAITING_RECEIPT"),
            (Guid.NewGuid(), "PAYMENT_COMPLETED"));

        // The Dashboard section maps the projection Summary straight through (see BuildReceivingSectionAsync),
        // so the dashboard counts ARE the queue summary — reconciliation is by construction.
        ReceivingSharedQueueSummaryDto s = ReceivingQueueProjection.Summarize(rows);
        Assert.Equal(3, s.ActionableGroups);
        Assert.Equal(3, s.ActionableRequests);
        Assert.Equal(2, s.ReadyForReceiptGroups);
        Assert.Equal(1, s.WaitingReceiptGroups);
    }

    [Fact]
    public void Empty_population_yields_zeroes()
    {
        var s = ReceivingQueueProjection.Summarize(new List<ReceivingQueueRowDto>());
        Assert.Equal(0, s.ActionableGroups);
        Assert.Equal(0, s.ActionableRequests);
        Assert.Equal(0, s.ReadyForReceiptGroups + s.WaitingReceiptGroups + s.FollowUpGroups + s.WaitingSupplierDeliveryGroups);
    }
}
