using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Services.Finance;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers IFinancePaymentEligibilityService — the single source of truth shared by
/// FinanceController.GetPayments (listing) and SchedulePayment/MarkAsPaid/ReturnForAdjustment
/// (execution). Every case here mirrors a real state observed or confirmed possible during the
/// Finance &gt; Payments investigation (2026-07-22), including the specific legacy-data bug
/// (PAYMENT + parent PO_ISSUED + group PENDING) that motivated centralizing this logic.
/// </summary>
public class FinancePaymentEligibilityServiceTests
{
    private readonly FinancePaymentEligibilityService _sut = new();

    private static FinancePoGroupEligibilityInput Group(string status, System.Guid? id = null) => new()
    {
        GroupId = id ?? System.Guid.NewGuid(),
        GroupStatus = status
    };

    // ── PAYMENT type ──────────────────────────────────────────────────────

    [Fact]
    public void Payment_ParentPoIssued_GroupPoIssued_SchedulesAndPaysAreAvailable()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PoIssued) }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.Schedule, result.Actions);
        Assert.Contains(FinancePaymentActionCodes.Pay, result.Actions);
        Assert.Contains(FinancePaymentActionCodes.Return, result.Actions);
    }

    [Fact]
    public void Payment_ParentPoIssued_LegacyGroupPending_PayAndScheduleAreBothAvailable()
    {
        // This is the confirmed root-cause scenario: REQ-14/07/2026-059 and 11 other legacy rows
        // (2026-07-25 Finance Payments UI regression). MarkAsPaid authorizes PAYMENT-type requests
        // from the PARENT status, not the group status, so PAY has always been available even
        // though the group itself is stuck at PENDING. SchedulePayment now applies the same
        // parent-status fallback for PAYMENT type ONLY when the group carries no real information
        // (PENDING/null/empty) — so SCHEDULE must now also be available, closing the asymmetry that
        // hid "Agendar pagamento" in the Finance Payments UI for these legacy rows.
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.PoGroupStatuses.Pending) }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.Pay, result.Actions);
        Assert.Contains(FinancePaymentActionCodes.Schedule, result.Actions);
    }

    [Fact]
    public void Payment_ParentPaymentScheduled_GroupPaymentScheduled_PayAvailable_ScheduleNotAvailable()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PaymentScheduled,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PaymentScheduled) }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.Pay, result.Actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Schedule, result.Actions);
    }

    [Fact]
    public void AlreadyPaid_NoScheduleOrPayOrReturn_OnlyAddNoteAndAddProofWhenMissing()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PaymentCompleted,
            IsPaid = true,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PaymentCompleted) }
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.Schedule, result.Actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Pay, result.Actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Return, result.Actions);
        Assert.Contains(FinancePaymentActionCodes.AddNote, result.Actions);
        Assert.Contains(FinancePaymentActionCodes.AddProof, result.Actions);
    }

    [Fact]
    public void Payment_NoPoGroupAtAll_ScheduleUnavailable_PayStillFollowsParentStatus()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput>()
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.Schedule, result.Actions);
        Assert.Equal(FinancePaymentUnavailableReasons.NoPoGroup, result.UnavailableReasons[FinancePaymentActionCodes.Schedule]);
        // PAY for PAYMENT type is parent-status-driven and does not require a group at all —
        // matching MarkAsPaid's own PAYMENT-branch guard, which never inspects PoGroups.
        Assert.Contains(FinancePaymentActionCodes.Pay, result.Actions);
    }

    [Fact]
    public void Payment_MultipleGroups_OneEligibleOneNot_ScheduleAvailableBecauseAtLeastOneGroupQualifies()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput>
            {
                Group(RequestConstants.PoGroupStatuses.Pending),
                Group(RequestConstants.Statuses.PoIssued)
            }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.Schedule, result.Actions);
    }

    // ── QUOTATION type ────────────────────────────────────────────────────

    [Fact]
    public void Quotation_ValidFinanceGroup_ScheduleAndPayAvailable()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Quotation,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PoIssued) }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.Schedule, result.Actions);
        Assert.Contains(FinancePaymentActionCodes.Pay, result.Actions);
    }

    [Fact]
    public void Quotation_LegacyGroupPending_ScheduleAndPayUnavailable()
    {
        // Unlike PAYMENT, MarkAsPaid's QUOTATION branch guards on the GROUP status — so a
        // QUOTATION request with a legacy PENDING group must NOT show PAY either.
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Quotation,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.PoGroupStatuses.Pending) }
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.Schedule, result.Actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Pay, result.Actions);
    }

    [Fact]
    public void Quotation_NoPoGroupAtAll_PayUnavailable_UnlikePaymentType()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Quotation,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput>()
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.Pay, result.Actions);
        Assert.Equal(FinancePaymentUnavailableReasons.NoPoGroup, result.UnavailableReasons[FinancePaymentActionCodes.Pay]);
    }

    // ── SCHEDULE mirrors CanSchedule exactly (systematic sweep) ───────────

    [Theory]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "PO_ISSUED", true)]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "PAYMENT_REQUEST_SENT", true)]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "ADVANCE_PAYMENT_REQUIRED", true)]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "PENDING", false)]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "WAITING_PO", false)]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "PAYMENT_SCHEDULED", false)]
    [InlineData(RequestConstants.Types.Quotation, "irrelevant-for-quotation", "PAYMENT_COMPLETED", false)]
    [InlineData(RequestConstants.Types.Payment, "PO_ISSUED", "PO_ISSUED", true)]
    [InlineData(RequestConstants.Types.Payment, "PO_ISSUED", "PAYMENT_REQUEST_SENT", true)]
    [InlineData(RequestConstants.Types.Payment, "PO_ISSUED", "ADVANCE_PAYMENT_REQUIRED", true)]
    [InlineData(RequestConstants.Types.Payment, "PO_ISSUED", "PAYMENT_SCHEDULED", false)]
    [InlineData(RequestConstants.Types.Payment, "PO_ISSUED", "PAYMENT_COMPLETED", false)]
    public void CanSchedule_MeaningfulGroupStatus_IsAlwaysAuthoritative_RegardlessOfType(string requestType, string requestStatus, string groupStatus, bool expected)
    {
        Assert.Equal(expected, _sut.CanSchedule(requestType, requestStatus, groupStatus));
    }

    [Fact]
    public void Evaluate_ScheduleActionAgreesWithCanSchedule_ForEachGroupInARow()
    {
        var statuses = new[] { "PO_ISSUED", "PENDING", "PAYMENT_SCHEDULED", "ADVANCE_PAYMENT_REQUIRED" };
        foreach (var status in statuses)
        {
            var input = new FinanceEligibilityInput
            {
                RequestTypeCode = RequestConstants.Types.Payment,
                RequestStatusCode = RequestConstants.Statuses.PoIssued,
                IsPaid = false,
                HasProof = true,
                PoGroups = new List<FinancePoGroupEligibilityInput> { Group(status) }
            };
            var result = _sut.Evaluate(input);
            Assert.Equal(
                _sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, status),
                result.Actions.Contains(FinancePaymentActionCodes.Schedule));
        }
    }

    // ── PAYMENT legacy-PENDING-group fallback: conflicting-status safety matrix ───────────
    // Covers the exact combinations from the 2026-07-25 Finance Payments UI regression fix —
    // proves the group's own status always wins once meaningful, and the parent fallback is both
    // narrow (only PO_ISSUED/PAYMENT_REQUEST_SENT) and scoped to PAYMENT type only.

    [Fact]
    public void PaymentFallback_ParentPoIssued_GroupPending_CanSchedule()
    {
        Assert.True(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.Pending));
    }

    [Fact]
    public void PaymentFallback_ParentPoIssued_GroupPoIssued_CanSchedule()
    {
        Assert.True(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.PoIssued));
    }

    [Fact]
    public void PaymentFallback_ParentPoIssued_GroupPaymentScheduled_GroupWins_CannotReschedule()
    {
        // The group has already moved on — a stale parent status must never re-enable scheduling.
        Assert.False(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.PaymentScheduled));
    }

    [Fact]
    public void PaymentFallback_ParentPoIssued_GroupPaymentCompleted_GroupWins_CannotSchedule()
    {
        Assert.False(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.PaymentCompleted));
    }

    [Fact]
    public void PaymentFallback_ParentPaymentCompleted_GroupPending_FallbackApplies_CannotSchedule()
    {
        // Group carries no information (PENDING) -> falls back to parent, but PAYMENT_COMPLETED is
        // not in the narrow schedulable-parent-status set.
        Assert.False(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PaymentCompleted, RequestConstants.PoGroupStatuses.Pending));
    }

    [Fact]
    public void PaymentFallback_ParentCancelled_GroupPending_CannotSchedule()
    {
        Assert.False(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.Cancelled, RequestConstants.PoGroupStatuses.Pending));
    }

    [Fact]
    public void Quotation_ParentPoIssued_GroupPending_RemainsBlocked_NoPaymentFallbackApplied()
    {
        // QUOTATION must never receive the PAYMENT-only legacy fallback, even with the exact same
        // parent/group shape that would schedule for PAYMENT type.
        Assert.False(_sut.CanSchedule(RequestConstants.Types.Quotation, RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.Pending));
    }

    [Fact]
    public void Quotation_GroupPoIssued_RemainsSchedulable()
    {
        Assert.True(_sut.CanSchedule(RequestConstants.Types.Quotation, "irrelevant-for-quotation", RequestConstants.PoGroupStatuses.PoIssued));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PaymentFallback_NullOrEmptyGroupStatus_TreatedAsMeaningless_FallsBackToParent(string? groupStatus)
    {
        Assert.True(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, groupStatus));
        Assert.False(_sut.CanSchedule(RequestConstants.Types.Payment, RequestConstants.Statuses.PaymentCompleted, groupStatus));
    }

    // ── PAY mirrors CanPay exactly (systematic sweep) ─────────────────────

    [Theory]
    [InlineData(RequestConstants.Types.Payment, "PO_ISSUED", true)]
    [InlineData(RequestConstants.Types.Payment, "PAYMENT_REQUEST_SENT", true)]
    [InlineData(RequestConstants.Types.Payment, "PAYMENT_SCHEDULED", true)]
    [InlineData(RequestConstants.Types.Payment, "APPROVED", false)]
    [InlineData(RequestConstants.Types.Payment, "PAYMENT_COMPLETED", false)]
    public void CanPay_MatchesMarkAsPaidGuard_ForPaymentType_UsingParentStatus(string requestType, string parentStatus, bool expected)
    {
        // PAYMENT type: CanPay ignores groupStatus entirely — pass null to prove that.
        Assert.Equal(expected, _sut.CanPay(requestType, parentStatus, null));
    }

    [Theory]
    [InlineData("PO_ISSUED", true)]
    [InlineData("PAYMENT_REQUEST_SENT", true)]
    [InlineData("PAYMENT_SCHEDULED", true)]
    [InlineData("ADVANCE_PAYMENT_REQUIRED", true)]
    [InlineData("ADVANCE_PAYMENT_SCHEDULED", true)]
    [InlineData("PENDING", false)]
    [InlineData("WAITING_PO", false)]
    public void CanPay_MatchesMarkAsPaidGuard_ForQuotationType_UsingGroupStatus(string groupStatus, bool expected)
    {
        Assert.Equal(expected, _sut.CanPay(RequestConstants.Types.Quotation, "irrelevant-for-quotation", groupStatus));
    }

    [Fact]
    public void CanPay_ExplicitlyCovers_PaymentType_ParentPoIssued_GroupPending()
    {
        // The exact confirmed bug scenario, asserted directly against the predicate the mutation
        // endpoint itself uses (not just through Evaluate).
        var canPay = _sut.CanPay(RequestConstants.Types.Payment, RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.Pending);
        Assert.True(canPay);
    }

    // ── RETURN mirrors CanReturn exactly ───────────────────────────────────

    [Theory]
    [InlineData("PO_ISSUED", true)]
    [InlineData("PAYMENT_SCHEDULED", true)]
    [InlineData("APPROVED", false)]
    [InlineData("PAYMENT_COMPLETED", false)]
    [InlineData(null, false)]
    public void CanReturn_MatchesReturnForAdjustmentGuard(string? parentStatus, bool expected)
    {
        Assert.Equal(expected, _sut.CanReturn(parentStatus));
    }

    // ── CANCEL_SCHEDULE ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("PAYMENT_SCHEDULED", true)]
    [InlineData("ADVANCE_PAYMENT_SCHEDULED", true)]
    [InlineData("PO_ISSUED", false)]
    [InlineData("ADVANCE_PAYMENT_REQUIRED", false)]
    [InlineData("PAYMENT_COMPLETED", false)]
    [InlineData("ADVANCE_PAYMENT_COMPLETED", false)]
    [InlineData("WAITING_RECEIPT", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("PENDING", false)]
    [InlineData(null, false)]
    public void CanCancelSchedule_OnlyEligibleForScheduledGroupStatuses(string? groupStatus, bool expected)
    {
        Assert.Equal(expected, _sut.CanCancelSchedule(groupStatus));
    }

    [Fact]
    public void Evaluate_PaymentScheduledGroup_ExposesCancelSchedule()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PaymentScheduled,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PaymentScheduled) }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.CancelSchedule, result.Actions);
    }

    [Fact]
    public void Evaluate_AdvancePaymentScheduledGroup_ExposesCancelSchedule()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Quotation,
            RequestStatusCode = RequestConstants.Statuses.AdvancePaymentScheduled,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.AdvancePaymentScheduled) }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.CancelSchedule, result.Actions);
    }

    [Fact]
    public void Evaluate_NoGroupScheduled_CancelScheduleUnavailable()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PoIssued) }
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.CancelSchedule, result.Actions);
        Assert.Equal(FinancePaymentUnavailableReasons.NoGroupCurrentlyScheduled, result.UnavailableReasons[FinancePaymentActionCodes.CancelSchedule]);
    }

    [Fact]
    public void Evaluate_AlreadyPaid_CancelScheduleUnavailable()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PaymentCompleted,
            IsPaid = true,
            HasProof = true,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PaymentCompleted) }
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.CancelSchedule, result.Actions);
    }

    [Fact]
    public void Evaluate_MultiGroup_OneScheduledOneNot_CancelScheduleAvailableBecauseAtLeastOneGroupQualifies()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Quotation,
            RequestStatusCode = RequestConstants.Statuses.AdvancePaymentCompleted,
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput>
            {
                Group(RequestConstants.Statuses.AdvancePaymentCompleted),
                Group(RequestConstants.Statuses.PaymentScheduled)
            }
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.CancelSchedule, result.Actions);
    }

    // ── ADD_NOTE / ADD_PROOF ────────────────────────────────────────────────

    [Fact]
    public void AddNote_AlwaysAvailable_RegardlessOfGroupsOrStatus()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = "ANY_STATUS",
            IsPaid = false,
            HasProof = false,
            PoGroups = new List<FinancePoGroupEligibilityInput>()
        };

        var result = _sut.Evaluate(input);

        Assert.Contains(FinancePaymentActionCodes.AddNote, result.Actions);
    }

    [Fact]
    public void AddProof_NotAddedWhenProofAlreadyExists()
    {
        var input = new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment,
            RequestStatusCode = RequestConstants.Statuses.PoIssued,
            IsPaid = false,
            HasProof = true,
            PoGroups = new List<FinancePoGroupEligibilityInput> { Group(RequestConstants.Statuses.PoIssued) }
        };

        var result = _sut.Evaluate(input);

        Assert.DoesNotContain(FinancePaymentActionCodes.AddProof, result.Actions);
    }
}
