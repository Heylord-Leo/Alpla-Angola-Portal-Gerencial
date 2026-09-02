using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Services.Finance;
using Xunit;
using AC = AlplaPortal.Domain.Constants.FinanceActionClasses;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// Dashboard V2 slice B3.1 — Finance shared queue. Locks the reconciliation contract: the Dashboard
/// Finance counts are derived purely from the canonical <see cref="FinanceObligationSummaryProjection"/>
/// summary (same computation the /finance/obligations endpoint uses), never a Dashboard-only classifier.
/// These tests operate on the pure summary + mapping over FinanceObligationDto fixtures (no DB).
/// </summary>
public class FinanceSharedQueueProjectionTests
{
    private static FinanceObligationDto Ob(string actionClass, Guid requestId, bool overdue = false, bool dueToday = false, decimal amount = 100m, string currency = "AOA")
        => new()
        {
            RequestId = requestId,
            RequestPoGroupId = Guid.NewGuid(),
            ActionClass = actionClass,
            IsOverdue = overdue,
            IsDueToday = dueToday,
            ObligationAmount = amount,
            CurrencyCode = currency,
        };

    // Mirror of DashboardV2QueryService.BuildFinanceSectionAsync's mapping (pure part), so the test
    // exercises the exact reconciliation the service performs.
    private static FinanceSharedQueueSummaryDto MapDashboard(IReadOnlyList<FinanceObligationDto> obligations)
    {
        var summary = FinanceObligationSummaryProjection.BuildSummary(obligations);
        return new FinanceSharedQueueSummaryDto
        {
            ActionableGroups = summary.ActionableTotal,
            ActionableRequests = obligations.Where(o => AC.IsFinanceActionable(o.ActionClass)).Select(o => o.RequestId).Distinct().Count(),
            NeedsSchedulingGroups = summary.NeedsScheduling.Count,
            NeedsPaymentGroups = summary.NeedsPayment.Count,
            DueTodayGroups = summary.DueToday.Count,
            OverdueGroups = summary.Overdue.Count,
            PaidWaitingReceivingGroups = summary.PaidWaitingReceiving.Count,
        };
    }

    [Fact]
    public void IsFinanceActionable_covers_the_three_finance_classes_only()
    {
        Assert.True(AC.IsFinanceActionable(AC.NeedsScheduling));
        Assert.True(AC.IsFinanceActionable(AC.NeedsPayment));
        Assert.True(AC.IsFinanceActionable(AC.FiscalDocumentPending));
        Assert.False(AC.IsFinanceActionable(AC.PaidWaitingReceiving)); // PAYMENT_COMPLETED family — not actionable
        Assert.False(AC.IsFinanceActionable(AC.Completed));
        Assert.False(AC.IsFinanceActionable(AC.NoFinanceAction));
    }

    [Fact]
    public void Dashboard_counts_reconcile_with_the_canonical_obligation_summary()
    {
        var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid(); var r3 = Guid.NewGuid();
        var obligations = new List<FinanceObligationDto>
        {
            Ob(AC.NeedsScheduling, r1),                       // PO_ISSUED / PAYMENT_REQUEST_SENT
            Ob(AC.NeedsPayment, r1, dueToday: true),         // PAYMENT_SCHEDULED — actionable, due today
            Ob(AC.NeedsPayment, r2, overdue: true),          // scheduled overdue
            Ob(AC.PaidWaitingReceiving, r2),                 // PAYMENT_COMPLETED — informational
            Ob(AC.NoFinanceAction, r3),                      // e.g. WAITING_PO-equivalent — not counted
        };

        var summary = FinanceObligationSummaryProjection.BuildSummary(obligations);
        var dash = MapDashboard(obligations);

        // §14 reconciliation: every dashboard count equals the canonical summary card count.
        Assert.Equal(summary.ActionableTotal, dash.ActionableGroups);          // 3 (1 sched + 2 pay)
        Assert.Equal(summary.NeedsScheduling.Count, dash.NeedsSchedulingGroups); // 1
        Assert.Equal(summary.NeedsPayment.Count, dash.NeedsPaymentGroups);       // 2
        Assert.Equal(summary.DueToday.Count, dash.DueTodayGroups);               // 1
        Assert.Equal(summary.Overdue.Count, dash.OverdueGroups);                 // 1
        Assert.Equal(summary.PaidWaitingReceiving.Count, dash.PaidWaitingReceivingGroups); // 1

        Assert.Equal(3, dash.ActionableGroups);
        Assert.Equal(1, dash.NeedsSchedulingGroups);
        Assert.Equal(2, dash.NeedsPaymentGroups);
        Assert.Equal(1, dash.DueTodayGroups);
        Assert.Equal(1, dash.OverdueGroups);
        Assert.Equal(1, dash.PaidWaitingReceivingGroups);
    }

    [Fact]
    public void ActionableRequests_is_distinct_over_the_actionable_group_population()
    {
        var r1 = Guid.NewGuid();
        // One request, three groups: one actionable (NeedsPayment), one paid (informational), one no-action.
        var obligations = new List<FinanceObligationDto>
        {
            Ob(AC.NeedsPayment, r1),        // actionable  (e.g. PAYMENT_SCHEDULED)
            Ob(AC.PaidWaitingReceiving, r1),// PAYMENT_COMPLETED — 0 finance action
            Ob(AC.NoFinanceAction, r1),     // WAITING_PO-equivalent — 0 finance action
        };

        var dash = MapDashboard(obligations);

        Assert.Equal(1, dash.ActionableGroups);       // only the NeedsPayment obligation
        Assert.Equal(1, dash.ActionableRequests);     // the single distinct request
        Assert.Equal(1, dash.PaidWaitingReceivingGroups);
    }

    [Fact]
    public void PaymentScheduled_stays_actionable_and_completed_never_actionable()
    {
        var obligations = new List<FinanceObligationDto>
        {
            Ob(AC.NeedsPayment, Guid.NewGuid()),          // PAYMENT_SCHEDULED maps to NeedsPayment
            Ob(AC.PaidWaitingReceiving, Guid.NewGuid()),  // PAYMENT_COMPLETED
        };
        var dash = MapDashboard(obligations);
        Assert.Equal(1, dash.ActionableGroups);            // scheduled counts
        Assert.Equal(0, dash.OverdueGroups);
        Assert.Equal(1, dash.PaidWaitingReceivingGroups);  // completed is informational, not actionable
    }

    [Fact]
    public void Empty_population_yields_zeroes()
    {
        var dash = MapDashboard(new List<FinanceObligationDto>());
        Assert.Equal(0, dash.ActionableGroups);
        Assert.Equal(0, dash.ActionableRequests);
        Assert.Equal(0, dash.NeedsSchedulingGroups);
        Assert.Equal(0, dash.NeedsPaymentGroups);
        Assert.Equal(0, dash.DueTodayGroups);
        Assert.Equal(0, dash.OverdueGroups);
        Assert.Equal(0, dash.PaidWaitingReceivingGroups);
    }
}
