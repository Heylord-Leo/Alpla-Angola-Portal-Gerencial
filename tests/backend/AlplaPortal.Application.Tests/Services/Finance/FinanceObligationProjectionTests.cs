using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers FinanceObligationProjectionBuilder — the pure per-RequestPoGroup Finance obligation
/// projection. Each group is judged in isolation from its own status/payments; the builder never
/// re-derives eligibility (financeActions are passed in) and never fabricates a due date from the
/// request deadline.
/// </summary>
public class FinanceObligationProjectionTests
{
    private static readonly DateTime Today = new(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc);

    private static FinanceObligationProjectionBuilder.RequestInput Req(string number = "REQ-20/07/2026-100") =>
        new(Guid.NewGuid(), number, RequestConstants.Types.Quotation, "Título", "Compras", "Planta A");

    private static FinanceObligationProjectionBuilder.GroupInput Group(
        string status, decimal total = 1000m, string currency = "AOA",
        IReadOnlyList<string>? actions = null,
        IReadOnlyList<FinanceObligationProjectionBuilder.PaymentInput>? payments = null) =>
        new(Guid.NewGuid(), 5, "Fornecedor X", "5000000000", "5000000000", status, "ECF10 2026/1", currency, total,
            actions ?? Array.Empty<string>(),
            payments ?? Array.Empty<FinanceObligationProjectionBuilder.PaymentInput>());

    private static FinanceObligationProjectionBuilder.PaymentInput ScheduledPayment(DateTime scheduled, decimal amount = 800m) =>
        new(1, "FINAL_BALANCE", "SCHEDULED", amount, null, scheduled, null, false, "AOA");

    [Fact]
    public void PoIssued_NeedsScheduling_NoDueDate_FinanceResponsible()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.PoIssued, 2000m,
            actions: new[] { "SCHEDULE", "PAY", "RETURN" }), Today);

        Assert.Equal(FinanceActionClasses.NeedsScheduling, o.ActionClass);
        Assert.Equal("Agendar pagamento", o.NextActionLabel);
        Assert.Equal(FinanceResponsibleRoles.Finance, o.ResponsibleRole);
        Assert.Null(o.DueDate); // never fabricated for an unscheduled obligation
        Assert.Equal(2000m, o.ObligationAmount); // group total when unscheduled
        Assert.False(o.IsOverdue);
    }

    [Fact]
    public void AdvancePaymentRequired_NeedsScheduling_AdvanceLabel()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.AdvancePaymentRequired), Today);
        Assert.Equal(FinanceActionClasses.NeedsScheduling, o.ActionClass);
        Assert.Equal("Agendar adiantamento", o.NextActionLabel);
    }

    [Fact]
    public void PaymentScheduled_FutureDate_NeedsPayment_DueDateFromPayment_NotOverdue()
    {
        var due = Today.AddDays(5);
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.PaymentScheduled,
            actions: new[] { "PAY", "CANCEL_SCHEDULE", "RETURN" },
            payments: new[] { ScheduledPayment(due, 800m) }), Today);

        Assert.Equal(FinanceActionClasses.NeedsPayment, o.ActionClass);
        Assert.Equal(due, o.DueDate);
        Assert.Equal(800m, o.ObligationAmount); // planned payment amount when scheduled
        Assert.False(o.IsOverdue);
        Assert.False(o.IsDueToday);
        Assert.Equal("Efetuar pagamento", o.NextActionLabel);
        Assert.Equal("Pagamento Pendente", o.OperationalStateLabel);
    }

    [Fact]
    public void PaymentScheduled_PastDate_Overdue_WithDaysAndLabel()
    {
        var due = Today.AddDays(-9);
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.PaymentScheduled,
            payments: new[] { ScheduledPayment(due) }), Today);

        Assert.Equal(FinanceActionClasses.NeedsPayment, o.ActionClass);
        Assert.True(o.IsOverdue);
        Assert.Equal(9, o.OverdueDays);
        Assert.False(o.IsDueToday);
        Assert.Equal("Efetuar pagamento", o.NextActionLabel);       // overdue days live in the due-date column
        Assert.Equal("Pagamento Vencido", o.OperationalStateLabel); // operational-state upgraded to overdue
    }

    [Fact]
    public void PaymentScheduled_DueToday_FlaggedDueTodayNotOverdue()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.PaymentScheduled,
            payments: new[] { ScheduledPayment(Today) }), Today);

        Assert.True(o.IsDueToday);
        Assert.False(o.IsOverdue);
    }

    [Fact]
    public void PaymentCompleted_PaidWaitingReceiving_NoFinanceMutation_ReceivingResponsible()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.PaymentCompleted,
            actions: Array.Empty<string>(),
            payments: new[] { new FinanceObligationProjectionBuilder.PaymentInput(1, "FINAL_BALANCE", "COMPLETED", 800m, 800m, null, Today.AddDays(-2), true, "AOA") }), Today);

        Assert.Equal(FinanceActionClasses.PaidWaitingReceiving, o.ActionClass);
        Assert.Equal("Aguardando Recebimento", o.NextActionLabel);
        Assert.Equal(FinanceResponsibleRoles.Receiving, o.ResponsibleRole);
        Assert.Empty(o.FinanceActions);
        Assert.Equal(800m, o.ObligationAmount); // actual paid
        Assert.Null(o.DueDate);
    }

    [Fact]
    public void WaitingPo_NoFinanceAction_BuyerMessage()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.PoGroupStatuses.WaitingPo,
            actions: Array.Empty<string>()), Today);

        Assert.Equal(FinanceActionClasses.NoFinanceAction, o.ActionClass);
        Assert.Equal("Aguardando emissão da P.O. pelo Comprador", o.NextActionLabel);
        Assert.Equal(FinanceResponsibleRoles.Buyer, o.ResponsibleRole);
        Assert.Empty(o.FinanceActions);
    }

    [Fact]
    public void WaitingPoCorrection_NoFinanceAction_ReturnedMessage()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.WaitingPoCorrection), Today);
        Assert.Equal(FinanceActionClasses.NoFinanceAction, o.ActionClass);
        Assert.Equal("Devolvido para correção da P.O.", o.NextActionLabel);
    }

    [Fact]
    public void WaitingFiscalReceipt_FiscalDocumentPending_FinanceResponsible()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.WaitingFiscalReceipt), Today);
        Assert.Equal(FinanceActionClasses.FiscalDocumentPending, o.ActionClass);
        Assert.Equal("Anexar recibo fiscal", o.NextActionLabel);
        Assert.Equal(FinanceResponsibleRoles.Finance, o.ResponsibleRole);
    }

    [Fact]
    public void Completed_NoNextAction()
    {
        var o = FinanceObligationProjectionBuilder.Build(Req(), Group(RequestConstants.Statuses.Completed), Today);
        Assert.Equal(FinanceActionClasses.Completed, o.ActionClass);
        Assert.Null(o.NextActionLabel);
    }

    [Fact]
    public void Req100Shape_SiblingsAreIndependent_PaidNoActions_ActionablePreservesActions()
    {
        var req = Req();
        var ncr = FinanceObligationProjectionBuilder.Build(req, Group(RequestConstants.Statuses.PaymentCompleted, 70341.42m,
            actions: Array.Empty<string>()), Today);
        var itec = FinanceObligationProjectionBuilder.Build(req, Group(RequestConstants.Statuses.PoIssued, 275139.00m,
            actions: new[] { "SCHEDULE", "PAY", "RETURN" }), Today);

        Assert.Equal(FinanceActionClasses.PaidWaitingReceiving, ncr.ActionClass);
        Assert.Empty(ncr.FinanceActions);

        Assert.Equal(FinanceActionClasses.NeedsScheduling, itec.ActionClass);
        Assert.Contains("SCHEDULE", itec.FinanceActions);
        Assert.Contains("PAY", itec.FinanceActions);
        Assert.Contains("RETURN", itec.FinanceActions);
    }
}
