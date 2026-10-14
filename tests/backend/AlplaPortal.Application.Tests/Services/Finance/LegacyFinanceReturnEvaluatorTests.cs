using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Services.Finance;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>Decision table of the legacy request-level Finance-return predicate (docs §G.19) and of the eligibility flag.</summary>
public class LegacyFinanceReturnEvaluatorTests
{
    private static readonly Guid G = Guid.NewGuid(), G2 = Guid.NewGuid();
    private static readonly DateTime Reg = new(2026, 8, 3, 16, 23, 10, DateTimeKind.Utc);
    private static readonly DateTime Ret = new(2026, 8, 11, 9, 42, 23, DateTimeKind.Utc);

    private static LegacyFinanceReturnFacts Req156(
        string? scalar = RequestConstants.Statuses.WaitingPoCorrection,
        IReadOnlyList<LegacyFinanceReturnGroupFact>? groups = null,
        DateTime? latestReturn = null, DateTime? latestRegistration = null, int livePayments = 0, bool noReturn = false, bool noRegistration = false) =>
        new(Guid.NewGuid(), scalar,
            groups ?? new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PoIssued) },
            noReturn ? null : latestReturn ?? Ret,
            noRegistration ? null : latestRegistration ?? Reg,
            livePayments);

    [Fact]
    public void Req156_shape_qualifies_and_names_the_group()
    {
        var v = LegacyFinanceReturnEvaluator.Evaluate(Req156());
        Assert.True(v.Qualifies); Assert.Equal(G, v.GroupId); Assert.Equal(LegacyFinanceReturnEvaluator.ReasonCode, v.Reason);
    }

    [Theory]
    [InlineData("scalarPoIssued", "não é WAITING_PO_CORRECTION")]
    [InlineData("scalarPartial", "não é WAITING_PO_CORRECTION")]         // REQ-275 shape: PO_PARTIALLY_UPLOADED
    [InlineData("twoGroups", "vários grupos")]
    [InlineData("noGroups", "sem grupo")]
    [InlineData("groupAlreadyCorrection", "já em WAITING_PO_CORRECTION")] // current-code return: nothing to repair
    [InlineData("groupScheduled", "não PO_ISSUED")]
    [InlineData("noReturn", "sem FINANCE_RETURN_ADJUSTMENT")]
    [InlineData("noRegistration", "sem REGISTER_PO")]
    [InlineData("registrationAfterReturn", "posterior ou simultâneo")]
    [InlineData("registrationSameInstant", "posterior ou simultâneo")]
    [InlineData("livePayment", "pagamento(s) ativo(s)")]
    public void Missing_or_contradicting_evidence_never_qualifies(string defect, string reasonFragment)
    {
        var f = defect switch
        {
            "scalarPoIssued" => Req156(scalar: RequestConstants.Statuses.PoIssued),
            "scalarPartial" => Req156(scalar: RequestConstants.Statuses.PoPartiallyUploaded, groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.WaitingPoCorrection), new LegacyFinanceReturnGroupFact(G2, RequestConstants.PoGroupStatuses.PoIssued) }),
            "twoGroups" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PoIssued), new LegacyFinanceReturnGroupFact(G2, RequestConstants.PoGroupStatuses.PoIssued) }),
            "noGroups" => Req156(groups: Array.Empty<LegacyFinanceReturnGroupFact>()),
            "groupAlreadyCorrection" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.WaitingPoCorrection) }),
            "groupScheduled" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PaymentScheduled) }),
            "noReturn" => Req156(noReturn: true),
            "noRegistration" => Req156(noRegistration: true),
            "registrationAfterReturn" => Req156(latestRegistration: Ret.AddMinutes(5)),
            "registrationSameInstant" => Req156(latestRegistration: Ret),
            "livePayment" => Req156(livePayments: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var v = LegacyFinanceReturnEvaluator.Evaluate(f);
        Assert.False(v.Qualifies); Assert.Null(v.GroupId); Assert.Contains(reasonFragment, v.Reason);
    }

    [Fact]
    public void Cancelled_groups_are_not_live_groups_by_contract_of_the_loader_so_a_sole_live_issued_group_still_qualifies()
    {
        // The loader filters CANCELLED groups out before evaluation; the evaluator only sees live groups.
        var v = LegacyFinanceReturnEvaluator.Evaluate(Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PoIssued) }));
        Assert.True(v.Qualifies);
    }

    [Fact]
    public void Eligibility_flag_withholds_schedule_and_pay_with_the_legacy_reason_and_keeps_other_actions()
    {
        var svc = new FinancePaymentEligibilityService();
        var input = new AlplaPortal.Application.Interfaces.Finance.FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment, RequestStatusCode = RequestConstants.Statuses.WaitingPoCorrection, IsPaid = false, HasProof = false,
            LegacyReturnPendingCorrection = true,
            PoGroups = new[] { new AlplaPortal.Application.Interfaces.Finance.FinancePoGroupEligibilityInput { GroupId = G, GroupStatus = RequestConstants.PoGroupStatuses.PoIssued } }
        };
        var r = svc.Evaluate(input);
        Assert.DoesNotContain(AlplaPortal.Application.Interfaces.Finance.FinancePaymentActionCodes.Schedule, r.Actions);
        Assert.DoesNotContain(AlplaPortal.Application.Interfaces.Finance.FinancePaymentActionCodes.Pay, r.Actions);
        Assert.Equal(AlplaPortal.Application.Interfaces.Finance.FinancePaymentUnavailableReasons.LegacyReturnPendingCorrection, r.UnavailableReasons[AlplaPortal.Application.Interfaces.Finance.FinancePaymentActionCodes.Schedule]);
        Assert.Equal(AlplaPortal.Application.Interfaces.Finance.FinancePaymentUnavailableReasons.LegacyReturnPendingCorrection, r.UnavailableReasons[AlplaPortal.Application.Interfaces.Finance.FinancePaymentActionCodes.Pay]);
        Assert.Contains(AlplaPortal.Application.Interfaces.Finance.FinancePaymentActionCodes.AddNote, r.Actions);

        var same = svc.Evaluate(new AlplaPortal.Application.Interfaces.Finance.FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Payment, RequestStatusCode = RequestConstants.Statuses.WaitingPoCorrection, PoGroups = input.PoGroups, LegacyReturnPendingCorrection = false
        });
        Assert.Contains(AlplaPortal.Application.Interfaces.Finance.FinancePaymentActionCodes.Schedule, same.Actions);   // without the flag the group status still advertises SCHEDULE (unchanged rule)
    }
    // ── Financial blocking decision (separate from repair eligibility) ──────

    [Theory]
    [InlineData("partialScalarReq275", false)]      // scalar PO_PARTIALLY_UPLOADED, one returned group among others → group guards decide
    [InlineData("poIssuedScalar", false)]
    [InlineData("groupScopedReturnSingle", false)]  // current-code single-group return: the group itself carries WAITING_PO_CORRECTION
    [InlineData("groupScopedReturnWithSibling", false)] // current-code multi-group return: sibling stays actionable
    [InlineData("legacyFullEvidence", true)]
    [InlineData("legacyNoReturnHistory", true)]     // incomplete history never enables a financial action
    [InlineData("legacyNoRegistration", true)]
    [InlineData("legacyWithScheduledPayment", true)]
    [InlineData("legacyTwoLiveGroupsNoneReturned", true)] // returned group unknown → everything blocked, no guessing
    [InlineData("legacyAdvanceGroup", true)]
    [InlineData("legacyNoLiveGroups", true)]
    public void Financial_block_decision_table(string shape, bool expectedBlocked)
    {
        var f = shape switch
        {
            "partialScalarReq275" => Req156(scalar: RequestConstants.Statuses.PoPartiallyUploaded, groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.WaitingPoCorrection), new LegacyFinanceReturnGroupFact(G2, RequestConstants.PoGroupStatuses.PoIssued) }),
            "poIssuedScalar" => Req156(scalar: RequestConstants.Statuses.PoIssued),
            "groupScopedReturnSingle" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.WaitingPoCorrection) }),
            "groupScopedReturnWithSibling" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.WaitingPoCorrection), new LegacyFinanceReturnGroupFact(G2, RequestConstants.PoGroupStatuses.PoIssued) }),
            "legacyFullEvidence" => Req156(),
            "legacyNoReturnHistory" => Req156(noReturn: true),
            "legacyNoRegistration" => Req156(noRegistration: true),
            "legacyWithScheduledPayment" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PaymentScheduled) }, livePayments: 1),
            "legacyTwoLiveGroupsNoneReturned" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PoIssued), new LegacyFinanceReturnGroupFact(G2, RequestConstants.PoGroupStatuses.PoIssued) }),
            "legacyAdvanceGroup" => Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.AdvancePaymentRequired) }),
            "legacyNoLiveGroups" => Req156(groups: Array.Empty<LegacyFinanceReturnGroupFact>()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var b = LegacyFinanceReturnEvaluator.EvaluateFinancialBlock(f);
        Assert.Equal(expectedBlocked, b.Blocked);
        if (expectedBlocked) Assert.StartsWith(LegacyFinanceReturnEvaluator.ReasonCode, b.Reason);
    }

    [Fact]
    public void Repair_eligibility_and_financial_block_are_different_decisions()
    {
        var incomplete = Req156(noReturn: true);
        Assert.False(LegacyFinanceReturnEvaluator.Evaluate(incomplete).Qualifies);        // not repairable: evidence missing
        Assert.True(LegacyFinanceReturnEvaluator.EvaluateFinancialBlock(incomplete).Blocked); // still blocked: missing evidence never enables actions
        var twoGroups = Req156(groups: new[] { new LegacyFinanceReturnGroupFact(G, RequestConstants.PoGroupStatuses.PoIssued), new LegacyFinanceReturnGroupFact(G2, RequestConstants.PoGroupStatuses.PoIssued) });
        Assert.False(LegacyFinanceReturnEvaluator.Evaluate(twoGroups).Qualifies);
        Assert.True(LegacyFinanceReturnEvaluator.EvaluateFinancialBlock(twoGroups).Blocked);
    }
}
