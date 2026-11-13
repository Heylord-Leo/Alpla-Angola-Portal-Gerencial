using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Services.Finance;
using Xunit;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Supplier account credit — the Finance queue never advertises an action the endpoint refuses: APPLY_CREDIT replaces
/// SCHEDULE/PAY when the current stage owes no bank part; REVERSE_CREDIT appears only for an APPLIED full coverage with
/// no cash and no evidence; the obligation projection reports the bank part as the amount to pay and the CREDIT flow.
/// Completion counts a COMPLETED SUPPLIER_CREDIT row as paid money and a PLANNED one as blocking.
/// </summary>
public class FinanceSupplierCreditEligibilityAndProjectionTests
{
    private readonly FinancePaymentEligibilityService _sut = new();

    private static FinanceGroupCreditFacts Facts(bool plannedCredit, bool pendingBank, string? status, bool cash = false, bool evidence = false) => new()
    {
        IsCreditTouched = true, CreditCoverageAmount = 350_000m, HasPlannedCreditRow = plannedCredit, HasPendingBankRow = pendingBank,
        ReservationStatus = status, HasCompletedCashRow = cash, HasPostSettlementEvidence = evidence,
    };

    [Fact]
    public void Full_coverage_po_issued_offers_apply_credit_and_hides_schedule_and_pay()
    {
        var actions = _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoIssued,
            Facts(plannedCredit: true, pendingBank: false, status: SC.ReservationStatuses.Reserved));
        Assert.Contains(FinancePaymentActionCodes.ApplyCredit, actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Schedule, actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Pay, actions);
        Assert.Contains(FinancePaymentActionCodes.Return, actions); // Finance return stays available before settlement
    }

    [Fact]
    public void Partial_coverage_keeps_schedule_and_pay_and_never_offers_apply_credit_alone()
    {
        var actions = _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoIssued,
            Facts(plannedCredit: true, pendingBank: true, status: SC.ReservationStatuses.Reserved));
        Assert.Contains(FinancePaymentActionCodes.Schedule, actions);
        Assert.Contains(FinancePaymentActionCodes.Pay, actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.ApplyCredit, actions);
    }

    [Fact]
    public void Applied_full_coverage_without_cash_or_evidence_offers_reverse_credit_only()
    {
        var actions = _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.PaymentCompleted,
            Facts(plannedCredit: false, pendingBank: false, status: SC.ReservationStatuses.Applied));
        Assert.Equal(new[] { FinancePaymentActionCodes.ReverseCredit }, actions.ToArray());
    }

    [Fact]
    public void Reverse_credit_is_withheld_with_cash_evidence_or_receiving_status()
    {
        Assert.DoesNotContain(FinancePaymentActionCodes.ReverseCredit, _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, "PAYMENT_COMPLETED", RequestConstants.Statuses.PaymentCompleted,
            Facts(false, false, SC.ReservationStatuses.Applied, cash: true)));
        Assert.DoesNotContain(FinancePaymentActionCodes.ReverseCredit, _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, "PAYMENT_COMPLETED", RequestConstants.Statuses.PaymentCompleted,
            Facts(false, false, SC.ReservationStatuses.Applied, evidence: true)));
        Assert.DoesNotContain(FinancePaymentActionCodes.ReverseCredit, _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, "WAITING_RECEIPT", RequestConstants.Statuses.WaitingReceipt,
            Facts(false, false, SC.ReservationStatuses.Applied)));
    }

    [Fact]
    public void Null_facts_keep_the_plain_rules()
    {
        var plain = _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoIssued);
        var withNull = _sut.EvaluateGroupActions(RequestConstants.Types.Quotation, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoIssued, null);
        Assert.Equal(plain, withNull);
    }

    [Fact]
    public void Request_level_union_includes_apply_credit_from_a_full_coverage_group()
    {
        var result = _sut.Evaluate(new FinanceEligibilityInput
        {
            RequestTypeCode = RequestConstants.Types.Quotation, RequestStatusCode = RequestConstants.Statuses.PoIssued,
            PoGroups = new List<FinancePoGroupEligibilityInput> { new() { GroupId = Guid.NewGuid(), GroupStatus = RequestConstants.Statuses.PoIssued, Credit = Facts(true, false, SC.ReservationStatuses.Reserved) } }
        });
        Assert.Contains(FinancePaymentActionCodes.ApplyCredit, result.Actions);
        Assert.DoesNotContain(FinancePaymentActionCodes.Pay, result.Actions);
    }

    // ── Facts builder ──

    [Fact]
    public void Facts_builder_reads_the_groups_own_rows_only()
    {
        var g = new RequestPoGroup { Id = Guid.NewGuid(), Status = RequestConstants.Statuses.PoIssued, SupplierCreditReservationId = Guid.NewGuid(), CreditCoverageAmount = 500_000m, TotalAmount = 500_000m };
        g.Payments.Add(new RequestPayment { PaymentType = RequestPayment.PaymentTypes.SupplierCredit, PaymentStatus = RequestPayment.PaymentStatuses.Planned, PlannedAmount = 500_000m });
        var facts = SupplierCreditFactsBuilder.Build(g, new SupplierCreditReservation { Status = SC.ReservationStatuses.Reserved, Amount = 500_000m });
        Assert.NotNull(facts);
        Assert.True(facts!.HasPlannedCreditRow); Assert.False(facts.HasPendingBankRow); Assert.False(facts.HasCompletedCashRow); Assert.False(facts.HasPostSettlementEvidence);
        Assert.Null(SupplierCreditFactsBuilder.Build(new RequestPoGroup { Status = "PO_ISSUED" }, null));
    }

    // ── Obligation projection ──

    private static FinanceObligationProjectionBuilder.RequestInput Req() => new(Guid.NewGuid(), "REQ-09/11/2026-001", RequestConstants.Types.Quotation, "ZZTEST", "TI", "Viana 1");

    [Fact]
    public void Projection_partial_coverage_reports_bank_part_as_amount_and_standard_flow()
    {
        var payments = new List<FinanceObligationProjectionBuilder.PaymentInput>
        {
            new(1, "FINAL_BALANCE", "PLANNED", 150_000m, null, null, null, false, "AOA"),
            new(2, "SUPPLIER_CREDIT", "PLANNED", 350_000m, null, null, null, false, "AOA"),
        };
        var g = new FinanceObligationProjectionBuilder.GroupInput(Guid.NewGuid(), 10, "S", null, null, RequestConstants.Statuses.PoIssued, "ECF10 2026/1", "AOA", 500_000m,
            new[] { "SCHEDULE", "PAY", "RETURN" }, payments, null, new FinanceObligationProjectionBuilder.CreditInput(350_000m, 0m, 350_000m, "RESERVED", "CCS-000001"));
        var o = FinanceObligationProjectionBuilder.Build(Req(), g, DateTime.UtcNow.Date);
        Assert.Equal(150_000m, o.ObligationAmount);
        Assert.Equal(150_000m, o.BankPayableAmount);
        Assert.Equal(350_000m, o.CreditCoverageAmount); Assert.Equal(350_000m, o.CreditReservedAmount); Assert.Equal("RESERVED", o.CreditReservationStatus);
        Assert.Equal(FinancePaymentFlows.Standard, o.PaymentFlow);
        Assert.Equal(1, o.PaymentId); // the cash row, never the credit row
    }

    [Fact]
    public void Projection_full_coverage_reports_credit_flow_and_next_action()
    {
        var payments = new List<FinanceObligationProjectionBuilder.PaymentInput> { new(2, "SUPPLIER_CREDIT", "PLANNED", 500_000m, null, null, null, false, "AOA") };
        var g = new FinanceObligationProjectionBuilder.GroupInput(Guid.NewGuid(), 10, "S", null, null, RequestConstants.Statuses.PoIssued, "ECF10 2026/1", "AOA", 500_000m,
            new[] { "APPLY_CREDIT", "RETURN" }, payments, null, new FinanceObligationProjectionBuilder.CreditInput(500_000m, 0m, 500_000m, "RESERVED", "CCS-000001"));
        var o = FinanceObligationProjectionBuilder.Build(Req(), g, DateTime.UtcNow.Date);
        Assert.Equal(FinancePaymentFlows.Credit, o.PaymentFlow);
        Assert.Equal("Aplicar crédito em conta corrente", o.NextActionLabel);
        Assert.Equal(0m, o.BankPayableAmount); Assert.Equal(0m, o.ObligationAmount);
        Assert.Null(o.PaymentId);
    }

    [Fact]
    public void Projection_without_credit_is_unchanged()
    {
        var g = new FinanceObligationProjectionBuilder.GroupInput(Guid.NewGuid(), 10, "S", null, null, RequestConstants.Statuses.PoIssued, null, "AOA", 500_000m,
            new[] { "SCHEDULE", "PAY", "RETURN" }, new List<FinanceObligationProjectionBuilder.PaymentInput>());
        var o = FinanceObligationProjectionBuilder.Build(Req(), g, DateTime.UtcNow.Date);
        Assert.Equal(500_000m, o.ObligationAmount); Assert.Equal(500_000m, o.BankPayableAmount); Assert.Equal(0m, o.CreditCoverageAmount); Assert.Null(o.CreditReservationStatus);
        Assert.Equal(FinancePaymentFlows.Standard, o.PaymentFlow);
    }

    // ── Completion ──

    private static RequestPoGroup PaidGroup() => new()
    {
        Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), Status = RequestConstants.PoGroupStatuses.PaymentCompleted, TotalAmount = 500_000m,
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Invoice, OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.NotRequired,
        OperationalReceiptCompletedAtUtc = DateTime.UtcNow, RequiresSeparateFiscalReceipt = false,
    };

    [Fact]
    public void Completion_counts_completed_supplier_credit_as_paid_money()
    {
        var g = PaidGroup();
        var rows = new[]
        {
            new RequestPayment { RequestPoGroupId = g.Id, PaymentType = "FINAL_BALANCE", PaymentStatus = "COMPLETED", ActualPaidAmount = 150_000m },
            new RequestPayment { RequestPoGroupId = g.Id, PaymentType = "SUPPLIER_CREDIT", PaymentStatus = "COMPLETED", ActualPaidAmount = 350_000m },
        };
        var p = GroupCompletionProjector.Project(g, rows, Array.Empty<RequestReconciliation>());
        Assert.True(p.PaymentSatisfied);
        Assert.True(p.Complete);
    }

    [Fact]
    public void Completion_blocks_on_a_planned_supplier_credit_row_and_on_cash_alone_for_a_partial_group()
    {
        var g = PaidGroup();
        var pending = new[]
        {
            new RequestPayment { RequestPoGroupId = g.Id, PaymentType = "FINAL_BALANCE", PaymentStatus = "COMPLETED", ActualPaidAmount = 150_000m },
            new RequestPayment { RequestPoGroupId = g.Id, PaymentType = "SUPPLIER_CREDIT", PaymentStatus = "PLANNED", PlannedAmount = 350_000m },
        };
        var p = GroupCompletionProjector.Project(g, pending, Array.Empty<RequestReconciliation>());
        Assert.False(p.PaymentSatisfied);
        Assert.Contains(GroupCompletionBlockingReasons.PaymentPending, p.BlockingReasons);

        // The ladder (PAYMENT_COMPLETED) never overrides evidence proving less than the total (cash 150 000 of 500 000).
        var cashOnly = new[] { new RequestPayment { RequestPoGroupId = g.Id, PaymentType = "FINAL_BALANCE", PaymentStatus = "COMPLETED", ActualPaidAmount = 150_000m } };
        Assert.False(GroupCompletionProjector.Project(g, cashOnly, Array.Empty<RequestReconciliation>()).PaymentSatisfied);
    }
}
