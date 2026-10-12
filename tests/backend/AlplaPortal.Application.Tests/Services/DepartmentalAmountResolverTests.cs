using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services;

/// <summary>
/// Decision table of the shared request-level amount/currency resolver (docs §G.18) and of the batch-approval currency
/// helper. Pure unit tests: no database.
/// </summary>
public class DepartmentalAmountResolverTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private static RequestAmountFacts Facts(
        decimal estimate = 0m, string? requestCurrency = "AOA", decimal? approved = null, string? approvedCurrency = null,
        IReadOnlyList<string?>? groups = null, decimal groupSum = 0m, int pending = 0, int cancelled = 0,
        int batches = 0, int approvedBatches = 0, decimal approvedBatchSum = 0m, int history = 0) =>
        new(Id, estimate, requestCurrency, approved, approvedCurrency, groups ?? Array.Empty<string?>(), groupSum, pending, cancelled, batches, approvedBatches, approvedBatchSum, history);

    // ── A. recorded single currency ─────────────────────────────────────────

    [Fact]
    public void Recorded_single_currency_with_consistent_groups_resolves_to_the_approved_total()
    {
        var r = DepartmentalAmountResolver.Resolve(Facts(estimate: 0m, approved: 390000m, approvedCurrency: "AOA", groups: new[] { "AOA", "AOA" }, groupSum: 390000m));
        Assert.Equal(RequestAmountSource.RecordedApproved, r.Source); Assert.Equal(390000m, r.Amount); Assert.Equal("AOA", r.Currency); Assert.True(r.IsResolved);
    }

    [Fact]
    public void Recorded_single_currency_without_groups_is_vacuously_consistent_and_request_currency_may_be_unregistered()
    {
        Assert.Equal(RequestAmountSource.RecordedApproved, DepartmentalAmountResolver.Resolve(Facts(approved: 10m, approvedCurrency: "AOA")).Source);
        Assert.Equal(RequestAmountSource.RecordedApproved, DepartmentalAmountResolver.Resolve(Facts(requestCurrency: null, approved: 10m, approvedCurrency: "USD")).Source);
    }

    [Theory]
    [InlineData("MULTIPLE", "AOA", "AOA", "MULTIPLE")]
    [InlineData("AOA", "USD", "AOA", "noutra moeda")]
    [InlineData("AOA", "AOA", "USD", "diferente da moeda do pedido")]
    [InlineData("AOA", null, "AOA", "não tem moeda registada")]
    public void Recorded_currency_conflicts_are_unresolved_with_a_reason(string approvedCurrency, string? groupCurrency, string requestCurrency, string reasonFragment)
    {
        var r = DepartmentalAmountResolver.Resolve(Facts(requestCurrency: requestCurrency, approved: 100m, approvedCurrency: approvedCurrency, groups: new[] { groupCurrency }, groupSum: 100m));
        Assert.Equal(RequestAmountSource.Unresolved, r.Source); Assert.Null(r.Currency); Assert.Contains(reasonFragment, r.Reason);
    }

    // ── B. derived currency (legacy batch approvals) ────────────────────────

    private static RequestAmountFacts Req478() => Facts(estimate: 0m, requestCurrency: "AOA", approved: 351179.88m, approvedCurrency: null,
        groups: new[] { "AOA", "AOA" }, groupSum: 351179.88m, pending: 0, cancelled: 0, batches: 2, approvedBatches: 2, approvedBatchSum: 351179.88m, history: 2);

    [Fact]
    public void Legacy_batch_approval_with_full_evidence_derives_the_request_currency()
    {
        var r = DepartmentalAmountResolver.Resolve(Req478());
        Assert.Equal(RequestAmountSource.DerivedApproved, r.Source); Assert.Equal(351179.88m, r.Amount); Assert.Equal("AOA", r.Currency);
    }

    [Fact]
    public void Derivation_tolerates_two_decimal_storage_rounding_only()
    {
        Assert.Equal(RequestAmountSource.DerivedApproved, DepartmentalAmountResolver.Resolve(Req478() with { LiveGroupSum = 351179.90m }).Source);   // 0.01 per group, 2 groups
        Assert.Equal(RequestAmountSource.DerivedApproved, DepartmentalAmountResolver.Resolve(Req478() with { ApprovedBatchSum = 351179.89m }).Source);
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Req478() with { LiveGroupSum = 351179.91m }).Source);
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Req478() with { ApprovedBatchSum = 351179.90m }).Source);
    }

    [Theory]
    [InlineData("cancelled", "grupos P.O. cancelados")]
    [InlineData("pending", "pendentes de aprovação")]
    [InlineData("unapprovedBatch", "lotes de aprovação em falta ou não aprovados")]
    [InlineData("noBatch", "lotes de aprovação em falta ou não aprovados")]
    [InlineData("history", "histórico de aprovação final incompleto")]
    [InlineData("batchSum", "soma dos lotes aprovados difere")]
    [InlineData("groupSum", "total dos grupos P.O. difere")]
    [InlineData("mixedGroups", "moeda diferente, mista ou não registada")]
    [InlineData("blankGroup", "moeda diferente, mista ou não registada")]
    [InlineData("noGroups", "sem grupos P.O. ativos")]
    [InlineData("noRequestCurrency", "moeda do pedido não registada")]
    public void Any_missing_derivation_evidence_leaves_the_legacy_approval_unresolved(string defect, string reasonFragment)
    {
        var f = defect switch
        {
            "cancelled" => Req478() with { CancelledGroupCount = 1 },
            "pending" => Req478() with { PendingGroupCount = 1 },
            "unapprovedBatch" => Req478() with { BatchCount = 3 },
            "noBatch" => Req478() with { BatchCount = 0, ApprovedBatchCount = 0, ApprovedBatchSum = 0m },
            "history" => Req478() with { BatchFinalApprovalHistoryCount = 1 },
            "batchSum" => Req478() with { ApprovedBatchSum = 300000m },
            "groupSum" => Req478() with { LiveGroupSum = 300000m },
            "mixedGroups" => Req478() with { LiveGroupCurrencies = new[] { "AOA", "USD" } },
            "blankGroup" => Req478() with { LiveGroupCurrencies = new[] { "AOA", " " } },
            "noGroups" => Req478() with { LiveGroupCurrencies = Array.Empty<string?>(), LiveGroupSum = 0m },
            "noRequestCurrency" => Req478() with { RequestCurrency = null },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var r = DepartmentalAmountResolver.Resolve(f);
        Assert.Equal(RequestAmountSource.Unresolved, r.Source);
        Assert.Null(r.Currency);
        Assert.StartsWith("valor aprovado sem moeda registada", r.Reason);
        Assert.Contains(reasonFragment, r.Reason);
    }

    // ── C. estimate fallback ────────────────────────────────────────────────

    [Fact]
    public void Without_a_positive_approved_total_the_estimate_is_used_under_its_comparability_rules()
    {
        var ok = DepartmentalAmountResolver.Resolve(Facts(estimate: 1200m, groups: new[] { "AOA" }));
        Assert.Equal(RequestAmountSource.EstimateFallback, ok.Source); Assert.Equal(1200m, ok.Amount); Assert.Equal("AOA", ok.Currency);
        Assert.Equal(RequestAmountSource.EstimateFallback, DepartmentalAmountResolver.Resolve(Facts(estimate: 0m, approved: 0m)).Source);            // zero approved → estimate, even when zero
        Assert.Equal(RequestAmountSource.EstimateFallback, DepartmentalAmountResolver.Resolve(Facts(estimate: 5m)).Source);                          // no groups
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Facts(estimate: 5m, groups: new[] { "USD" })).Source);
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Facts(estimate: 5m, groups: new[] { (string?)null })).Source);
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Facts(estimate: 5m, requestCurrency: "")).Source);
    }

    [Fact]
    public void Currency_comparison_is_ordinal_and_never_trims()
    {
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Facts(approved: 1m, approvedCurrency: "aoa", groups: new[] { "AOA" }, groupSum: 1m)).Source);
        Assert.Equal(RequestAmountSource.Unresolved, DepartmentalAmountResolver.Resolve(Facts(approved: 1m, approvedCurrency: "AOA ", groups: new[] { "AOA" }, groupSum: 1m)).Source);
    }

    // ── batch-approval currency helper: truth table ─────────────────────────

    [Fact]
    public void Batch_currency_is_the_single_code_of_the_non_zero_contributions_MULTIPLE_when_they_differ_and_unknown_when_any_is_unknown()
    {
        Assert.Equal("AOA", ApprovalCurrencyResolver.ResolveBatchCurrency(new[] { ("AOA", 10m), ("AOA", 5m) }));
        Assert.Equal("MULTIPLE", ApprovalCurrencyResolver.ResolveBatchCurrency(new[] { ("AOA", 10m), ("USD", 5m) }));
        Assert.Null(ApprovalCurrencyResolver.ResolveBatchCurrency(new[] { ((string?)null, 10m), (" ", 5m) }));
        Assert.Null(ApprovalCurrencyResolver.ResolveBatchCurrency(new[] { ("AOA", 10m), ((string?)null, 5m) }));            // unknown non-zero amount → batch currency unknown, never "AOA"
        Assert.Equal("AOA", ApprovalCurrencyResolver.ResolveBatchCurrency(new[] { ("AOA", 10m), ((string?)null, 0m) }));    // zero amounts carry no evidence
        Assert.Null(ApprovalCurrencyResolver.ResolveBatchCurrency(Array.Empty<(string?, decimal)>()));
        Assert.Null(ApprovalCurrencyResolver.ResolveBatchCurrency(new[] { ("AOA", 0m) }));
    }

    [Theory]
    // recorded request currency, previously approved batches?, this batch → expected
    [InlineData(null, false, "AOA", "AOA")]            // first approval, known single currency
    [InlineData(null, false, null, null)]              // first approval, unknown currency → explicit uncertainty
    [InlineData(null, false, "MULTIPLE", "MULTIPLE")]  // first approval already mixed
    [InlineData(null, true, "AOA", null)]              // unknown/legacy prior currency NEVER becomes known because a later batch is known
    [InlineData(null, true, null, null)]               // legacy prior + unknown batch
    [InlineData("AOA", true, null, null)]              // known followed by unknown → no longer provably single
    [InlineData("AOA", true, "AOA", "AOA")]            // same known currency across batches
    [InlineData("AOA", true, "USD", "MULTIPLE")]       // different known currencies
    [InlineData("AOA", true, "MULTIPLE", "MULTIPLE")]
    [InlineData("MULTIPLE", true, "AOA", "MULTIPLE")]  // MULTIPLE followed by anything stays MULTIPLE
    [InlineData("MULTIPLE", true, null, "MULTIPLE")]
    [InlineData("MULTIPLE", true, "USD", "MULTIPLE")]
    [InlineData("AOA", false, "USD", "MULTIPLE")]      // recorded without prior batches (single-path approval) is prior evidence too
    [InlineData("AOA", false, "AOA", "AOA")]
    [InlineData("AOA", false, null, null)]
    public void Cumulative_currency_truth_table(string? recorded, bool hadPrevious, string? batch, string? expected)
    {
        Assert.Equal(expected, ApprovalCurrencyResolver.CombineCumulative(recorded, hadPrevious, batch));
    }
}
