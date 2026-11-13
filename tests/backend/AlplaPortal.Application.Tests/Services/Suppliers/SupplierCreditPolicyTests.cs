using System;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// Supplier account credit — the pure rulebook (docs/SUPPLIER_CREDIT_PO_COVERAGE.md §4.1–§4.2): ledger figures, coverage
/// decision with near-full normalisation, stage split, owed-row plan, strict cash rule, reversal kind, idempotency keys.
/// </summary>
public class SupplierCreditPolicyTests
{
    // ── Ledger figures ──

    [Fact]
    public void Figures_follow_the_table_of_the_plan_reserve_apply_reverse()
    {
        // Balance 1 000 000, hold 350 000 (RESERVED) → Available 650 000.
        var f = SupplierCreditPolicy.Figures(new[] { 1_000_000m }, new[] { (SC.ReservationStatuses.Reserved, 350_000m, 0m) });
        Assert.Equal(1_000_000m, f.Balance); Assert.Equal(350_000m, f.Reserved); Assert.Equal(650_000m, f.Available);

        // Apply advance stage 200 000: entry −200 000, hold shrinks to 150 000 → Available unchanged (650 000).
        f = SupplierCreditPolicy.Figures(new[] { 1_000_000m, -200_000m }, new[] { (SC.ReservationStatuses.PartiallyApplied, 350_000m, 200_000m) });
        Assert.Equal(800_000m, f.Balance); Assert.Equal(150_000m, f.Reserved); Assert.Equal(650_000m, f.Available);

        // Reverse: +200 000 entry only; hold released by the status change → Available 1 000 000 (+350 000).
        f = SupplierCreditPolicy.Figures(new[] { 1_000_000m, -200_000m, 200_000m }, new[] { (SC.ReservationStatuses.Reversed, 350_000m, 200_000m) });
        Assert.Equal(1_000_000m, f.Balance); Assert.Equal(0m, f.Reserved); Assert.Equal(1_000_000m, f.Available);
    }

    [Theory]
    [InlineData("RESERVED", 350, 0, 350)]
    [InlineData("PARTIALLY_APPLIED", 350, 200, 150)]
    [InlineData("APPLIED", 350, 350, 0)]
    [InlineData("RELEASED", 350, 0, 0)]
    [InlineData("REVERSED", 350, 200, 0)]
    public void Hold_is_amount_minus_applied_only_while_holding(string status, decimal amount, decimal applied, decimal expected)
        => Assert.Equal(expected, SupplierCreditPolicy.Hold(status, amount, applied));

    // ── Coverage decision ──

    [Fact]
    public void Coverage_partial_exact_amount_is_reserved()
    {
        var d = SupplierCreditPolicy.DecideCoverage(350_000m, 500_000m, 1_000_000m);
        Assert.True(d.IsValid); Assert.Equal(350_000m, d.Coverage); Assert.False(d.IsFullCoverage);
    }

    [Fact]
    public void Coverage_near_full_normalises_to_full_only_when_available_covers_the_total()
    {
        var total = 500_000m;
        var nearFull = total - 0.50m; // within the near-full tolerance of the group total
        var withRoom = SupplierCreditPolicy.DecideCoverage(nearFull, total, 600_000m);
        Assert.True(withRoom.IsValid); Assert.Equal(total, withRoom.Coverage); Assert.True(withRoom.IsFullCoverage);

        var exactRoom = SupplierCreditPolicy.DecideCoverage(nearFull, total, nearFull);
        Assert.True(exactRoom.IsValid); Assert.Equal(nearFull, exactRoom.Coverage); Assert.False(exactRoom.IsFullCoverage);
    }

    [Fact]
    public void Coverage_refuses_zero_above_total_and_insufficient_balance()
    {
        Assert.Equal(SC.ErrorCodes.InvalidAmount, SupplierCreditPolicy.DecideCoverage(0m, 500_000m, 1_000_000m).Code);
        Assert.Equal(SC.ErrorCodes.InvalidAmount, SupplierCreditPolicy.DecideCoverage(500_000.01m, 500_000m, 1_000_000m).Code);
        var insufficient = SupplierCreditPolicy.DecideCoverage(350_000m, 500_000m, 300_000m);
        Assert.False(insufficient.IsValid); Assert.Equal(SC.ErrorCodes.Insufficient, insufficient.Code);
    }

    /// <summary>
    /// TEST smoke fixture F4 (plan rev. 3, 2026-11-13): the insufficient-credit refusal must come from the AVAILABLE balance, not
    /// from the P.O.-amount limit. Available after the F1 + F2 + F3 holds = 300 000 of 1 000 000. Requested 400 000 on a 400 000
    /// group → insufficient (the near-full normalisation never lifts coverage above the available); 350 000 on the old 120 000
    /// group would have been refused as "above the P.O." (the wrong refusal); 50 000 on the same 400 000 group is accepted.
    /// </summary>
    [Theory]
    [InlineData(400_000, 400_000, 300_000, false, "INSUFFICIENT_SUPPLIER_CREDIT")]
    [InlineData(350_000, 120_000, 300_000, false, "CREDIT_AMOUNT_INVALID")]
    [InlineData(50_000, 400_000, 300_000, true, null)]
    [InlineData(300_000, 400_000, 300_000, true, null)]
    public void Fixture_F4_refusal_is_insufficient_credit_not_the_po_limit(decimal requested, decimal total, decimal available, bool valid, string? code)
    {
        var d = SupplierCreditPolicy.DecideCoverage(requested, total, available);
        Assert.Equal(valid, d.IsValid);
        Assert.Equal(code, d.Code);
        if (valid) Assert.Equal(requested, d.Coverage);
    }

    // ── Split (rev. 4 §4.2 table: T = 500 000, pct 40) ──

    [Theory]
    [InlineData(100_000, 100_000, 0, 100_000, 300_000)]
    [InlineData(200_000, 200_000, 0, 0, 300_000)]
    [InlineData(350_000, 200_000, 150_000, 0, 150_000)]
    [InlineData(500_000, 200_000, 300_000, 0, 0)]
    public void Split_applies_credit_to_the_advance_first_then_the_balance(decimal c, decimal credAdv, decimal credBal, decimal bankAdv, decimal bankBal)
    {
        var s = SupplierCreditPolicy.Split(500_000m, 40m, c);
        Assert.Equal(200_000m, s.Advance); Assert.Equal(300_000m, s.Balance);
        Assert.Equal(credAdv, s.CreditAdvance); Assert.Equal(credBal, s.CreditBalance);
        Assert.Equal(bankAdv, s.BankAdvance); Assert.Equal(bankBal, s.BankBalance);
    }

    [Fact]
    public void Split_single_stage_terms_put_everything_in_one_stage()
    {
        var pp = SupplierCreditPolicy.Split(500_000m, 0m, 350_000m);
        Assert.Equal(0m, pp.Advance); Assert.Equal(0m, pp.CreditAdvance); Assert.Equal(350_000m, pp.CreditBalance); Assert.Equal(150_000m, pp.BankBalance);
        var adv = SupplierCreditPolicy.Split(500_000m, 100m, 350_000m);
        Assert.Equal(500_000m, adv.Advance); Assert.Equal(350_000m, adv.CreditAdvance); Assert.Equal(150_000m, adv.BankAdvance); Assert.Equal(0m, adv.CreditBalance);
    }

    // ── Owed rows ──

    [Fact]
    public void Owed_rows_post_paid_partial_are_final_balance_bank_part_and_supplier_credit_single_stage()
    {
        var rows = SupplierCreditPolicy.PlanOwedRows(RequestConstants.PaymentConditions.PostPaid, SupplierCreditPolicy.Split(500_000m, 0m, 350_000m));
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.FinalBalance && r.Stage == SC.Stages.Single && r.PlannedAmount == 150_000m);
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && r.Stage == SC.Stages.Single && r.PlannedAmount == 350_000m);
    }

    [Fact]
    public void Owed_rows_full_coverage_have_no_bank_row()
    {
        var pp = SupplierCreditPolicy.PlanOwedRows(RequestConstants.PaymentConditions.PostPaid, SupplierCreditPolicy.Split(500_000m, 0m, 500_000m));
        Assert.Single(pp); Assert.Equal(RequestPayment.PaymentTypes.SupplierCredit, pp[0].PaymentType); Assert.Equal(500_000m, pp[0].PlannedAmount);
        var adv = SupplierCreditPolicy.PlanOwedRows(RequestConstants.PaymentConditions.AdvanceFull, SupplierCreditPolicy.Split(500_000m, 100m, 500_000m));
        Assert.Single(adv); Assert.Equal(RequestPayment.PaymentTypes.SupplierCredit, adv[0].PaymentType);
    }

    [Fact]
    public void Owed_rows_advance_full_partial_are_advance_bank_part_and_supplier_credit()
    {
        var rows = SupplierCreditPolicy.PlanOwedRows(RequestConstants.PaymentConditions.AdvanceFull, SupplierCreditPolicy.Split(500_000m, 100m, 350_000m));
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.Advance && r.PlannedAmount == 150_000m && r.Stage == SC.Stages.Single);
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && r.PlannedAmount == 350_000m);
    }

    [Fact]
    public void Owed_rows_antecipado_parcial_are_two_stages_stage2_ready()
    {
        var rows = SupplierCreditPolicy.PlanOwedRows(RequestConstants.PaymentConditions.AdvancePartial, SupplierCreditPolicy.Split(500_000m, 40m, 350_000m));
        Assert.Equal(3, rows.Count); // CredAdv 200 000 (ADVANCE), CredBal 150 000 (BALANCE), BankBal 150 000 (BALANCE); BankAdv 0 → no row
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && r.Stage == SC.Stages.Advance && r.PlannedAmount == 200_000m);
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && r.Stage == SC.Stages.Balance && r.PlannedAmount == 150_000m);
        Assert.Contains(rows, r => r.PaymentType == RequestPayment.PaymentTypes.FinalBalance && r.Stage == SC.Stages.Balance && r.PlannedAmount == 150_000m);
    }

    // ── Strict cash, reversal kind, keys ──

    [Theory]
    [InlineData(150_000, 150_000, true)]
    [InlineData(150_000.01, 150_000, false)]
    [InlineData(149_999.99, 150_000, false)]
    [InlineData(150_000.004, 150_000, true)] // sub-cent noise rounds to the row amount
    public void Cash_must_equal_the_planned_row_exactly(decimal cash, decimal planned, bool ok)
        => Assert.Equal(ok, SupplierCreditPolicy.CashMatchesRow(cash, planned));

    [Theory]
    [InlineData(500_000, 500_000, false, "FULL_COVERAGE")]
    [InlineData(500_000, 500_000, true, "FULL_COVERAGE")]
    [InlineData(350_000, 500_000, false, "PARTIAL_NO_CASH")]
    [InlineData(350_000, 500_000, true, "PARTIAL_WITH_CASH")]
    public void Reversal_kind_never_reads_partial_as_full(decimal c, decimal t, bool cash, string expected)
        => Assert.Equal(expected, SupplierCreditPolicy.ReversalKind(c, t, cash));

    [Fact]
    public void Idempotency_keys_are_derived_from_persisted_identifiers_only()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("APPLY:11111111-2222-3333-4444-555555555555:SINGLE", SupplierCreditPolicy.Keys.Application(id, SC.Stages.Single));
        Assert.Equal("REV:11111111-2222-3333-4444-555555555555", SupplierCreditPolicy.Keys.Reversal(id));
        Assert.Equal(SupplierCreditPolicy.Keys.Application(id, SC.Stages.Single), SupplierCreditPolicy.Keys.Application(id, SC.Stages.Single));
        Assert.NotEqual(SupplierCreditPolicy.Keys.Application(id, SC.Stages.Advance), SupplierCreditPolicy.Keys.Application(id, SC.Stages.Balance));
    }

    [Fact]
    public void Stage1_eligibility_is_quotation_with_post_paid_or_advance_full()
    {
        Assert.True(SupplierCreditPolicy.IsRequestTypeSupported(RequestConstants.Types.Quotation));
        Assert.False(SupplierCreditPolicy.IsRequestTypeSupported(RequestConstants.Types.Payment));
        Assert.True(SupplierCreditPolicy.IsPaymentConditionSupported(RequestConstants.PaymentConditions.PostPaid));
        Assert.True(SupplierCreditPolicy.IsPaymentConditionSupported(RequestConstants.PaymentConditions.AdvanceFull));
        Assert.False(SupplierCreditPolicy.IsPaymentConditionSupported(RequestConstants.PaymentConditions.AdvancePartial));
        Assert.False(SupplierCreditPolicy.IsPaymentConditionSupported(null));
    }
}
