using AlplaPortal.Domain.Approvals;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

// v2.244.0 Approval Center V2 — Phase 2. Locks the proven PAYMENT/QUOTATION classification into the
// single Domain classifier (§20, §21). Transitions verified against source + the DEV clone.
public class ApprovalHistoryClassifierTests
{
    // ── §20 PAYMENT / legacy scalar: the same ActionTaken="APPROVE" means AREA vs FINAL by NewStatus ──

    [Fact]
    public void Approve_ToWaitingFinal_IsAreaApproved()
    {
        var c = ApprovalHistoryClassifier.Classify("APPROVE", "WAITING_FINAL_APPROVAL", "WAITING_AREA_APPROVAL");
        Assert.Equal("AREA", c.LevelCode);
        Assert.Equal("APPROVED", c.DecisionCode);
        Assert.True(c.IsApprovalDecision);
    }

    [Fact]
    public void Approve_ToApproved_IsFinalApproved()
    {
        var c = ApprovalHistoryClassifier.Classify("APPROVE", "APPROVED", "WAITING_FINAL_APPROVAL");
        Assert.Equal("FINAL", c.LevelCode);
        Assert.Equal("APPROVED", c.DecisionCode);
        Assert.True(c.IsApprovalDecision);
    }

    [Theory]
    [InlineData("WAITING_AREA_APPROVAL", "AREA")]
    [InlineData("WAITING_COST_CENTER", "AREA")]
    [InlineData("WAITING_FINAL_APPROVAL", "FINAL")]
    public void Reject_ClassifiesByPreviousStatus(string prev, string expectedLevel)
    {
        var c = ApprovalHistoryClassifier.Classify("REJECT", "REJECTED", prev);
        Assert.Equal(expectedLevel, c.LevelCode);
        Assert.Equal("REJECTED", c.DecisionCode);
    }

    [Theory]
    [InlineData("AREA_ADJUSTMENT", "AREA")]
    [InlineData("FINAL_ADJUSTMENT", "FINAL")]
    public void RequestAdjustment_ClassifiesByNewStatus_AsReturned(string newStatus, string expectedLevel)
    {
        var c = ApprovalHistoryClassifier.Classify("REQUEST_ADJUSTMENT", newStatus, "WAITING_AREA_APPROVAL");
        Assert.Equal(expectedLevel, c.LevelCode);
        Assert.Equal("RETURNED", c.DecisionCode);
    }

    // ── §21 QUOTATION batch codes are self-describing ──

    [Theory]
    [InlineData("BATCH_AREA_APPROVED", "AREA", "APPROVED")]
    [InlineData("BATCH_FINAL_APPROVED", "FINAL", "APPROVED")]
    [InlineData("BATCH_AREA_REJECTED", "AREA", "REJECTED")]
    [InlineData("BATCH_FINAL_REJECTED", "FINAL", "REJECTED")]
    [InlineData("BATCH_AREA_ADJUSTMENT", "AREA", "RETURNED")]
    [InlineData("BATCH_FINAL_ADJUSTMENT", "FINAL", "RETURNED")]
    public void BatchCodes_SelfClassify_IgnoringStatus(string action, string level, string decision)
    {
        // Deliberately pass a misleading request-level status — batch codes must ignore it.
        var c = ApprovalHistoryClassifier.Classify(action, "WAITING_QUOTATION", "WAITING_QUOTATION");
        Assert.Equal(level, c.LevelCode);
        Assert.Equal(decision, c.DecisionCode);
        Assert.True(c.IsApprovalDecision);
    }

    [Fact]
    public void BatchResubmitted_IsStagelessResubmit()
    {
        var c = ApprovalHistoryClassifier.Classify("BATCH_RESUBMITTED", null, null);
        Assert.Null(c.LevelCode);
        Assert.Equal("RESUBMITTED", c.DecisionCode);
        Assert.True(c.IsApprovalDecision);
    }

    // ── Context / non-decision events ──

    [Theory]
    [InlineData("BATCH_CREATED")]
    [InlineData("SUBMIT")]
    [InlineData("CREATED")]
    [InlineData("FINANCE_RETURN_ADJUSTMENT")]
    [InlineData("REGISTER_PO")]
    public void ContextEvents_AreNotApprovalDecisions(string action)
    {
        var c = ApprovalHistoryClassifier.Classify(action, "X", "Y");
        Assert.False(c.IsApprovalDecision);
        Assert.Null(c.DecisionCode);
    }

    [Fact]
    public void DecisionActionCodes_ExcludeContextEvents()
    {
        Assert.Contains("APPROVE", ApprovalHistoryClassifier.DecisionActionCodes);
        Assert.Contains("BATCH_FINAL_APPROVED", ApprovalHistoryClassifier.DecisionActionCodes);
        Assert.DoesNotContain("BATCH_CREATED", ApprovalHistoryClassifier.DecisionActionCodes);
        Assert.DoesNotContain("FINANCE_RETURN_ADJUSTMENT", ApprovalHistoryClassifier.DecisionActionCodes);
    }

    // ── §23 "Lote #N" display parse (never a batch identity) ──

    [Theory]
    [InlineData("Aprovação Final do Lote #1 realizada. Montante: 3,400,000.00.", 1)]
    [InlineData("Aprovação Final do Lote #12 realizada.", 12)]
    [InlineData("Lote #3", 3)]
    [InlineData("lote  #  7 ", 7)]
    public void ParseLoteNumber_ExtractsDisplayNumber(string comment, int expected)
        => Assert.Equal(expected, ApprovalHistoryClassifier.ParseLoteNumber(comment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Aprovação da Área realizada.")]
    public void ParseLoteNumber_NullWhenAbsent(string? comment)
        => Assert.Null(ApprovalHistoryClassifier.ParseLoteNumber(comment));
}
