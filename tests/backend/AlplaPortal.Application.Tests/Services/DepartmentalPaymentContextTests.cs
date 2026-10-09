using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services;

/// <summary>
/// Regression for TEST REQ-07/10/2026-538 (v2.247.1): the departmental "Contexto Financeiro" block on PAYMENT_SCHEDULED /
/// PAYMENT_COMPLETED always showed "Acumulado 0.00" and "0.0%" because the aggregate filtered request statuses
/// "SCHEDULED" / "PAID" / "PARTIAL_PAID", none of which exists in the catalog (PAYMENT_SCHEDULED / PAYMENT_COMPLETED do).
/// Final rule (docs §G.13): "Valor desta ação" from the acted-on payment row (planned when scheduling, actual paid when
/// completing, its own currency); request-level figures labelled as ESTIMATES; aggregate = this request's estimate + the
/// estimates of OTHER comparable requests of the same department (status PAYMENT_SCHEDULED/PAID/PAYMENT_COMPLETED, same
/// CurrencyId, updated since the first day of the current UTC month); comparability requires a registered request
/// currency and every non-cancelled group in that same currency; "n/d" with a reason otherwise.
/// </summary>
public class DepartmentalPaymentContextTests
{
    private const int Dept = 7, OtherDept = 8, Plant = 5;
    private const int S_PO_ISSUED = 9, S_PAYMENT_SCHEDULED = 14, S_PAYMENT_COMPLETED = 15, S_WAITING_AREA = 3;
    private const int AOA = 1, USD = 2;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly Guid Requester = Guid.NewGuid(), Manager = Guid.NewGuid();

    private static async Task SeedCatalogAsync(ApplicationDbContext ctx)
    {
        ctx.Currencies.AddRange(new Currency { Id = AOA, Code = "AOA", Symbol = "Kz" }, new Currency { Id = USD, Code = "USD", Symbol = "$" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_WAITING_AREA, Code = RequestConstants.Statuses.WaitingAreaApproval, Name = "Área" },
            new RequestStatus { Id = S_PO_ISSUED, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" },
            new RequestStatus { Id = S_PAYMENT_SCHEDULED, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado" },
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado" });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 10, Name = "PTA-ÁGUAS, LDA", TaxId = "5010", RegistrationStatus = "ACTIVE" },
            new Supplier { Id = 11, Name = "USD VENDOR & CO", TaxId = "5011", RegistrationStatus = "ACTIVE" });
        ctx.Users.AddRange(
            new User { Id = Requester, FullName = "Requisitante", Email = "requester@test.local", IsActive = true },
            new User { Id = Manager, FullName = "Gestor de Área", Email = "manager@test.local", IsActive = true });
        await ctx.SaveChangesAsync();
    }

    private static Request NewRequest(string number, decimal amount, int statusId, int dept = Dept, int? currencyId = AOA, DateTime? updatedAtUtc = null) => new()
    {
        Id = Guid.NewGuid(), RequestNumber = number, Title = number, RequestTypeId = 2, StatusId = statusId, RequesterId = Requester,
        DepartmentId = dept, PlantId = Plant, CurrencyId = currencyId, EstimatedTotalAmount = amount,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-10), UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow
    };

    private static RequestPoGroup Group(Request r, int? supplierId, decimal total, string? currency, string status = RequestConstants.Statuses.PaymentScheduled) => new()
    {
        Id = Guid.NewGuid(), RequestId = r.Id, SupplierId = supplierId, SupplierNameSnapshot = supplierId == 10 ? "PTA-ÁGUAS, LDA" : supplierId == 11 ? "USD VENDOR & CO" : null,
        TotalAmount = total, CurrencyCode = currency, Status = status, CreatedByUserId = Requester, CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
    };

    private static RequestPayment Payment(Request r, RequestPoGroup g, decimal planned, decimal? paid = null, string currency = "AOA", int seq = 1) => new()
    {
        RequestId = r.Id, RequestPoGroupId = g.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = seq, PlannedAmount = planned,
        ActualPaidAmount = paid, CurrencyCode = currency, ScheduledDateUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
        PaidDateUtc = paid.HasValue ? new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc) : null,
        PaymentStatus = paid.HasValue ? RequestPayment.PaymentStatuses.Completed : RequestPayment.PaymentStatuses.Scheduled,
        CreatedByUserId = Requester, CreatedAtUtc = DateTime.UtcNow
    };

    private static DateTime MonthStart => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static WorkflowNotificationOrchestrator Build(ApplicationDbContext ctx)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(Dept, Plant)).ReturnsAsync(new ApprovalRoutingResultDto
        {
            Managers = new List<AreaManagerDto> { new() { UserId = Manager, FullName = "Gestor de Área", Email = "manager@test.local", PlantId = Plant } }
        });
        return new WorkflowNotificationOrchestrator(ctx, inApp.Object, new Mock<IEmailService>().Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, adminLog.Object, routing.Object);
    }

    private static WorkflowEvent Event(Request r, string code, RequestPoGroup? group = null, RequestPayment? payment = null) => new()
    {
        EventCode = code, RequestId = r.Id, RequestNumber = r.RequestNumber!, TargetStatusCode = code, ActionTaken = code, ActorUserId = Requester, ActorName = "Finance",
        CorrelationId = Guid.NewGuid(), RequesterId = r.RequesterId, DepartmentId = r.DepartmentId, PlantId = r.PlantId, CompanyId = null, // no AP config path
        PoGroupId = group?.Id, PaymentId = payment?.Id
    };

    private static async Task<string> ManagerBodyAsync(ApplicationDbContext ctx, string eventCode)
    {
        var row = Assert.Single(await ctx.EmailOutbox.Where(x => x.RecipientEmail == "manager@test.local").ToListAsync());
        Assert.StartsWith(eventCode == WorkflowEventCodes.PaymentScheduled ? "[AGENDADO] Informa" : "[REALIZADO] Informa", row.Subject);
        Assert.Contains("Contexto Financeiro Departamental (pedidos atualizados no m", row.BodyHtml);
        Assert.Contains($"<b>Período:</b> pedidos do departamento com última atualização desde {MonthStart:dd/MM/yyyy} (UTC); não comprova que o agendamento/pagamento ocorreu neste mês", row.BodyHtml);
        return row.BodyHtml;
    }

    private const string ActionScheduled = "<b>Valor desta ação (agendado, grupo P.O.):</b>";
    private const string ActionPaid = "<b>Valor desta ação (pago, grupo P.O.):</b>";
    private const string Estimate = "<b>Valor estimado deste Pedido:</b>";
    private const string Accumulated = "<b>Acumulado estimado (pedidos agendados/pagos atualizados no mês, apenas comparáveis na moeda do pedido, incl. este):</b>";
    private const string Excluded = "<b>Excluídos:</b>";

    [Fact]
    public async Task Accumulated_counts_only_same_department_same_currency_comparable_scheduled_or_paid_requests_updated_this_month_plus_this_request()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 100000m, S_PAYMENT_SCHEDULED);
        var g = Group(current, 10, 100000m, "AOA"); var pay = Payment(current, g, 100000m);
        var otherMixed = NewRequest("REQ-EX-MIXEDGROUPS", 123456m, S_PAYMENT_SCHEDULED);               // AOA request whose group is in USD → not comparable → excluded
        var otherNoCurrency = NewRequest("REQ-EX-NOCURRENCY", 654321m, S_PAYMENT_SCHEDULED);           // AOA request whose group has no currency → excluded
        ctx.Requests.AddRange(current,
            NewRequest("REQ-IN-SCHED", 60000m, S_PAYMENT_SCHEDULED),                                        // counted (no groups → comparable)
            NewRequest("REQ-IN-PAID", 40000m, S_PAYMENT_COMPLETED, updatedAtUtc: MonthStart),                // counted (boundary inclusive)
            NewRequest("REQ-EX-STATUS", 999999m, S_PO_ISSUED),                                               // not scheduled/paid
            NewRequest("REQ-EX-AREA", 888888m, S_WAITING_AREA),                                              // not scheduled/paid
            NewRequest("REQ-EX-USD", 500000m, S_PAYMENT_SCHEDULED, currencyId: USD),                         // other currency, never summed into AOA
            NewRequest("REQ-EX-DEPT", 300000m, S_PAYMENT_SCHEDULED, dept: OtherDept),                        // other department
            NewRequest("REQ-EX-LASTMONTH", 700000m, S_PAYMENT_COMPLETED, updatedAtUtc: MonthStart.AddSeconds(-1)), // previous month
            otherMixed, otherNoCurrency);
        ctx.RequestPoGroups.AddRange(g, Group(otherMixed, 11, 1500m, "USD"), Group(otherNoCurrency, null, 654321m, null));
        ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{ActionScheduled} {100000m:N2} AOA", body);
        Assert.Contains($"{Estimate} {100000m:N2} AOA", body);
        Assert.Contains($"{Accumulated} {200000m:N2} AOA", body);
        Assert.Contains($"representa <b>{50m:N1}%</b> do acumulado estimado (AOA)", body);
        Assert.Contains($"{Excluded} 3 pedido(s) agendado(s)/pago(s) do departamento neste período, por moeda diferente ou não comprovada — o acumulado e a percentagem não representam toda a atividade do departamento.", body);
        Assert.DoesNotContain("n/d", body);
    }

    [Fact]
    public async Task No_exclusion_disclosure_when_every_scheduled_or_paid_request_of_the_department_is_included()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 100000m, S_PAYMENT_SCHEDULED);
        var g = Group(current, 10, 100000m, "AOA"); var pay = Payment(current, g, 100000m);
        var otherWithCancelledUsdGroup = NewRequest("REQ-IN-CANCELLED-USD", 60000m, S_PAYMENT_SCHEDULED);
        ctx.Requests.AddRange(current, otherWithCancelledUsdGroup,
            NewRequest("REQ-EX-STATUS", 999999m, S_PO_ISSUED),                                                   // not scheduled/paid → not a candidate, not disclosed
            NewRequest("REQ-EX-USD-LASTMONTH", 500000m, S_PAYMENT_SCHEDULED, currencyId: USD, updatedAtUtc: MonthStart.AddDays(-1))); // outside the period → not disclosed
        ctx.RequestPoGroups.AddRange(g,
            Group(otherWithCancelledUsdGroup, 10, 60000m, "AOA"),
            Group(otherWithCancelledUsdGroup, 11, 1500m, "USD", RequestConstants.PoGroupStatuses.Cancelled));   // cancelled USD group is ignored → request stays comparable
        ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{Accumulated} {160000m:N2} AOA", body);
        Assert.Contains($"representa <b>{62.5m:N1}%</b>", body);
        Assert.DoesNotContain(Excluded, body);
    }

    [Fact]
    public async Task Cancelled_group_in_another_currency_on_this_request_does_not_break_comparability()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 285000m, S_PAYMENT_SCHEDULED);
        var a = Group(current, 10, 285000m, "AOA"); var cancelledUsd = Group(current, 11, 1500m, "USD", RequestConstants.PoGroupStatuses.Cancelled);
        var pay = Payment(current, a, 285000m);
        ctx.Requests.Add(current); ctx.RequestPoGroups.AddRange(a, cancelledUsd); ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, a, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{Accumulated} {285000m:N2} AOA", body);
        Assert.Contains($"representa <b>{100m:N1}%</b>", body);
        Assert.DoesNotContain("n/d", body);
    }

    [Fact]
    public async Task Action_amount_is_the_planned_amount_when_scheduling_and_the_actual_paid_amount_when_completing_independent_of_the_estimate()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 120000m, S_PAYMENT_SCHEDULED);                                 // estimate differs from both amounts
        var g = Group(current, 10, 126787.50m, "AOA");
        var scheduled = Payment(current, g, 126787.50m, seq: 1);
        var paid = Payment(current, g, 126787.50m, paid: 130000m, seq: 2);                                 // over-payment allowed by MarkAsPaid
        ctx.Requests.Add(current); ctx.RequestPoGroups.Add(g); ctx.RequestPayments.AddRange(scheduled, paid);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var o = Build(ctx);
        await o.EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, scheduled));
        var rows = await ctx.EmailOutbox.Where(x => x.RecipientEmail == "manager@test.local").ToListAsync();
        var sch = Assert.Single(rows).BodyHtml;
        Assert.Contains($"{ActionScheduled} {126787.50m:N2} AOA", sch);
        Assert.Contains($"{Estimate} {120000m:N2} AOA", sch);
        Assert.Contains($"{Accumulated} {120000m:N2} AOA", sch);                                            // aggregate stays estimate-based
        Assert.Contains($"representa <b>{100m:N1}%</b>", sch);
        Assert.DoesNotContain("Valor desta ação (pago", sch);

        await o.EmitAsync(Event(current, WorkflowEventCodes.PaymentCompleted, g, paid));
        rows = await ctx.EmailOutbox.Where(x => x.RecipientEmail == "manager@test.local").ToListAsync();
        var cmp = Assert.Single(rows, r => r.EventCode == WorkflowEventCodes.PaymentCompleted).BodyHtml;
        Assert.Contains($"{ActionPaid} {130000m:N2} AOA", cmp);
        Assert.DoesNotContain($"{126787.50m:N2}", cmp);
        Assert.Contains($"{Estimate} {120000m:N2} AOA", cmp);
    }

    [Fact]
    public async Task Mixed_currency_groups_render_the_action_amount_in_its_own_currency_but_no_aggregate_or_percentage()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 286500m, S_PAYMENT_SCHEDULED);                                 // AOA estimate built from an AOA and a USD group
        var a = Group(current, 10, 285000m, "AOA"); var b = Group(current, 11, 1500m, "USD");
        var payB = Payment(current, b, 1500m, currency: "USD");
        ctx.Requests.AddRange(current, NewRequest("REQ-OTHER", 100000m, S_PAYMENT_SCHEDULED));
        ctx.RequestPoGroups.AddRange(a, b); ctx.RequestPayments.Add(payB);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, b, payB));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{ActionScheduled} {1500m:N2} USD", body);
        Assert.Contains($"{Estimate} {286500m:N2} AOA", body);
        Assert.Contains($"{Accumulated} <b>n/d</b> — os grupos P.O. deste pedido estão em moedas diferentes da moeda do pedido (AOA, USD vs AOA)", body);
        Assert.Contains("<b>Impacto:</b> <b>n/d</b> — os grupos P.O. deste pedido estão em moedas diferentes", body);
        Assert.DoesNotContain($"{386500m:N2}", body); Assert.DoesNotContain($"{100000m:N2}", body); Assert.DoesNotContain("%</b>", body);
        Assert.DoesNotContain(Excluded, body);
    }

    [Fact]
    public async Task Group_without_a_registered_currency_or_request_without_currency_yields_not_available_with_a_reason()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var noGroupCurrency = NewRequest("REQ-NOGRPCUR", 50000m, S_PAYMENT_SCHEDULED);
        var g1 = Group(noGroupCurrency, null, 50000m, null); var p1 = Payment(noGroupCurrency, g1, 50000m, currency: "---");
        var noRequestCurrency = NewRequest("REQ-NOREQCUR", 50000m, S_PAYMENT_SCHEDULED, currencyId: null);
        var g2 = Group(noRequestCurrency, 10, 50000m, "AOA"); var p2 = Payment(noRequestCurrency, g2, 50000m);
        ctx.Requests.AddRange(noGroupCurrency, noRequestCurrency); ctx.RequestPoGroups.AddRange(g1, g2); ctx.RequestPayments.AddRange(p1, p2);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var o = Build(ctx);
        await o.EmitAsync(Event(noGroupCurrency, WorkflowEventCodes.PaymentScheduled, g1, p1));
        await o.EmitAsync(Event(noRequestCurrency, WorkflowEventCodes.PaymentScheduled, g2, p2));

        var rows = await ctx.EmailOutbox.Where(x => x.RecipientEmail == "manager@test.local").ToListAsync();
        Assert.Equal(2, rows.Count);
        var b1 = Assert.Single(rows, r => r.RequestId == noGroupCurrency.Id).BodyHtml;
        Assert.Contains($"{ActionScheduled} {50000m:N2} AOA", b1);                                          // "---" payment currency → group (null) → request currency AOA for display
        Assert.Contains($"{Accumulated} <b>n/d</b> — pelo menos um grupo P.O. deste pedido não tem moeda registada", b1);
        Assert.Contains("<b>Impacto:</b> <b>n/d</b>", b1);
        var b2 = Assert.Single(rows, r => r.RequestId == noRequestCurrency.Id).BodyHtml;
        Assert.Contains($"{Estimate} {50000m:N2} (moeda do pedido não registada)", b2);
        Assert.Contains($"{Accumulated} <b>n/d</b> — moeda do pedido não registada", b2);
        Assert.Contains("<b>Impacto:</b> <b>n/d</b>", b2);
        Assert.DoesNotContain("%</b>", b1); Assert.DoesNotContain("%</b>", b2);
    }

    [Fact]
    public async Task This_request_is_included_even_when_its_parent_status_is_not_yet_scheduled_or_paid()
    {
        // Multi-group QUOTATION in one currency: one group just completed, parent still at the furthest-behind sibling status (PO_ISSUED).
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 100000m, S_PO_ISSUED);
        var a = Group(current, 10, 60000m, "AOA", RequestConstants.Statuses.PaymentCompleted); var b = Group(current, 10, 40000m, "AOA", RequestConstants.Statuses.PoIssued);
        var payA = Payment(current, a, 60000m, paid: 60000m);
        ctx.Requests.AddRange(current, NewRequest("REQ-OTHER", 300000m, S_PAYMENT_COMPLETED));
        ctx.RequestPoGroups.AddRange(a, b); ctx.RequestPayments.Add(payA);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentCompleted, a, payA));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentCompleted);
        Assert.Contains($"{ActionPaid} {60000m:N2} AOA", body);
        Assert.Contains($"{Accumulated} {400000m:N2} AOA", body);
        Assert.Contains($"representa <b>{25m:N1}%</b>", body);
    }

    [Fact]
    public async Task Legacy_event_without_group_or_payment_shows_not_available_for_the_action_amount_but_keeps_the_estimate_metric()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 126787.50m, S_PAYMENT_SCHEDULED);
        ctx.Requests.Add(current); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{ActionScheduled} <b>n/d</b> (sem registo de pagamento associado a esta ação)", body);
        Assert.Contains($"{Accumulated} {126787.50m:N2} AOA", body);
        Assert.Contains($"representa <b>{100m:N1}%</b>", body);
        Assert.DoesNotContain(Excluded, body);
    }

    [Theory]
    [InlineData(0, 0)]        // nothing scheduled/paid and a zero request estimate
    [InlineData(0, 250000)]   // zero request estimate (e.g. QUOTATION without a selected quotation) with other activity
    public async Task Percentage_is_not_available_instead_of_a_misleading_zero_when_there_is_no_comparison_basis(decimal thisAmount, decimal otherAmount)
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", thisAmount, S_PAYMENT_SCHEDULED);
        var g = Group(current, 10, 10000m, "AOA"); var pay = Payment(current, g, 10000m);
        ctx.Requests.Add(current); ctx.RequestPoGroups.Add(g); ctx.RequestPayments.Add(pay);
        if (otherAmount > 0) ctx.Requests.Add(NewRequest("REQ-OTHER", otherAmount, S_PAYMENT_SCHEDULED));
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{ActionScheduled} {10000m:N2} AOA", body);
        Assert.Contains($"{Accumulated} {otherAmount:N2} AOA", body);
        Assert.Contains("<b>Impacto:</b> <b>n/d</b> — sem base de comparação", body);
        Assert.DoesNotContain("0.0%", body);
        Assert.DoesNotContain("representa <b>", body);
    }

    [Fact]
    public async Task Requester_still_receives_the_standard_mail_and_the_manager_the_departmental_one()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 100000m, S_PAYMENT_SCHEDULED);
        ctx.Requests.Add(current); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled));

        var rows = await ctx.EmailOutbox.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.RecipientEmail == "requester@test.local" && !r.BodyHtml.Contains("Contexto Financeiro"));
        Assert.Contains(rows, r => r.RecipientEmail == "manager@test.local" && r.BodyHtml.Contains("Contexto Financeiro"));
    }
}
