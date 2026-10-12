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
/// Departmental "Contexto Financeiro" block on PAYMENT_SCHEDULED / PAYMENT_COMPLETED through the REAL orchestrator on an
/// InMemory context. v2.247.2 fixed the dead status filter; this version (docs §G.18) applies ONE request-level resolver
/// (recorded approved → derived approved for legacy batch approvals → estimate fallback → unresolved) to the current
/// request and to every denominator candidate, counts each request once, buckets strictly by resolved currency, and
/// discloses composition and exclusions. Candidate statuses and the UpdatedAtUtc month filter are unchanged.
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

    private static Request NewRequest(string number, decimal amount, int statusId, int dept = Dept, int? currencyId = AOA, DateTime? updatedAtUtc = null,
        decimal? approved = null, string? approvedCurrency = null) => new()
    {
        Id = Guid.NewGuid(), RequestNumber = number, Title = number, RequestTypeId = 2, StatusId = statusId, RequesterId = Requester,
        DepartmentId = dept, PlantId = Plant, CurrencyId = currencyId, EstimatedTotalAmount = amount, ApprovedTotalAmount = approved, ApprovedCurrencyCode = approvedCurrency,
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

    /// <summary>Legacy batch approval evidence: APPROVED batches summing the request's approved total plus one BATCH_FINAL_APPROVED row each.</summary>
    private static void AddLegacyBatchEvidence(ApplicationDbContext ctx, Request r, params decimal[] batchAmounts)
    {
        var n = 1;
        foreach (var amt in batchAmounts)
        {
            ctx.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = r.Id, BatchNumber = n++, Status = RequestConstants.ApprovalBatchStatuses.Approved, ApprovedTotalAmount = amt, CreatedAtUtc = DateTime.UtcNow.AddDays(-5), CreatedByUserId = Requester });
            ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = r.Id, ActorUserId = Requester, ActionTaken = "BATCH_FINAL_APPROVED", NewStatusId = S_PO_ISSUED, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) });
        }
    }

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
        CorrelationId = Guid.NewGuid(), RequesterId = r.RequesterId, DepartmentId = r.DepartmentId, PlantId = r.PlantId, CompanyId = null,
        PoGroupId = group?.Id, PaymentId = payment?.Id
    };

    private static async Task<string> ManagerBodyAsync(ApplicationDbContext ctx, string eventCode)
    {
        var row = Assert.Single(await ctx.EmailOutbox.Where(x => x.RecipientEmail == "manager@test.local").ToListAsync());
        Assert.StartsWith(eventCode == WorkflowEventCodes.PaymentScheduled ? "[AGENDADO] Informa" : "[REALIZADO] Informa", row.Subject);
        Assert.Contains("Contexto Financeiro Departamental (pedidos atualizados no m", row.BodyHtml);
        Assert.Contains($"<b>Período:</b> pedidos do departamento com última atualização desde {MonthStart:dd/MM/yyyy} (UTC); não comprova que o agendamento/pagamento ocorreu neste mês. Valores ao nível do pedido (aprovado registado, aprovado com moeda derivada de dados atuais consistentes, ou estimativa como fallback), não montantes agendados/pagos.", row.BodyHtml);
        return row.BodyHtml;
    }

    private const string ActionScheduled = "<b>Valor desta ação (agendado, grupo P.O.):</b>";
    private const string ActionPaid = "<b>Valor desta ação (pago, grupo P.O.):</b>";
    private const string This = "<b>Valor deste pedido:</b>";
    private const string Accumulated = "<b>Acumulado (pedidos agendados/pagos atualizados no mês, na moeda resolvida deste pedido, incl. este):</b>";
    private const string Excluded = "<b>Excluídos:</b>";
    private const string BaseRecorded = "(base: valor aprovado, moeda registada na aprovação)";
    private const string BaseDerived = "(base: valor aprovado, moeda derivada de dados atuais consistentes (não registada na aprovação))";
    private const string BaseEstimate = "(base: estimativa do pedido (fallback: sem valor aprovado utilizável))";

    [Fact]
    public async Task Estimate_fallback_aggregation_counts_same_department_same_resolved_currency_requests_updated_this_month_plus_this_request_once()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 100000m, S_PAYMENT_SCHEDULED);
        var g = Group(current, 10, 100000m, "AOA"); var pay = Payment(current, g, 100000m);
        var otherMixed = NewRequest("REQ-EX-MIXEDGROUPS", 123456m, S_PAYMENT_SCHEDULED);               // AOA request whose group is in USD → unresolved
        var otherNoCurrency = NewRequest("REQ-EX-NOCURRENCY", 654321m, S_PAYMENT_SCHEDULED);           // AOA request whose group has no currency → unresolved
        ctx.Requests.AddRange(current,
            NewRequest("REQ-IN-SCHED", 60000m, S_PAYMENT_SCHEDULED),                                        // counted (no groups → estimate fallback)
            NewRequest("REQ-IN-PAID", 40000m, S_PAYMENT_COMPLETED, updatedAtUtc: MonthStart),                // counted (boundary inclusive)
            NewRequest("REQ-EX-STATUS", 999999m, S_PO_ISSUED),                                               // not scheduled/paid → not a candidate
            NewRequest("REQ-EX-AREA", 888888m, S_WAITING_AREA),                                              // not a candidate
            NewRequest("REQ-EX-USD", 500000m, S_PAYMENT_SCHEDULED, currencyId: USD),                         // resolves in USD → other currency, never summed
            NewRequest("REQ-EX-DEPT", 300000m, S_PAYMENT_SCHEDULED, dept: OtherDept),                        // other department
            NewRequest("REQ-EX-LASTMONTH", 700000m, S_PAYMENT_COMPLETED, updatedAtUtc: MonthStart.AddSeconds(-1)), // previous month
            otherMixed, otherNoCurrency);
        ctx.RequestPoGroups.AddRange(g, Group(otherMixed, 11, 1500m, "USD"), Group(otherNoCurrency, null, 654321m, null));
        ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{ActionScheduled} {100000m:N2} AOA", body);
        Assert.Contains($"{This} {100000m:N2} AOA <span style='color:#555;'>{BaseEstimate}</span>", body);
        Assert.Contains($"{Accumulated} {200000m:N2} AOA <span style='color:#555;'>(composição: 0 aprovado(s) registado(s), 0 com moeda derivada, 3 estimativa(s) — fallback)</span>", body);
        Assert.Contains($"representa <b>{50m:N1}%</b> do acumulado resolvido (AOA)", body);
        Assert.Contains($"{Excluded} 3 pedido(s) agendado(s)/pago(s) do departamento neste período — 1 noutra moeda, 2 sem valor resolvido (estimativa: grupos P.O. ativos noutra moeda; estimativa: um grupo P.O. ativo não tem moeda registada). O acumulado e a percentagem não representam toda a atividade do departamento.", body);
        Assert.DoesNotContain("n/d", body);
    }

    [Fact]
    public async Task One_resolver_governs_numerator_and_denominator_recorded_derived_and_estimate_sources_in_one_bucket()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        // current: legacy batch approval (REQ-478 shape) → derived AOA 351,179.88 although the estimate is 0
        var current = NewRequest("REQ-478", 0m, S_PAYMENT_COMPLETED, approved: 351179.88m, approvedCurrency: null);
        var ca = Group(current, 10, 319180.08m, "AOA", RequestConstants.Statuses.PaymentCompleted); var cb = Group(current, 10, 31999.80m, "AOA", RequestConstants.Statuses.PaymentCompleted);
        var payA = Payment(current, ca, 319180.08m, paid: 319180.08m);
        AddLegacyBatchEvidence(ctx, current, 319180.08m, 31999.80m);
        // recorded approved AOA (single-path approval), estimate 0 → 390,000.00
        var recorded = NewRequest("REQ-REC", 0m, S_PAYMENT_SCHEDULED, approved: 390000m, approvedCurrency: "AOA");
        // estimate fallback, no approved total → 60,000.00
        var est = NewRequest("REQ-EST", 60000m, S_PAYMENT_SCHEDULED);
        // legacy approval WITHOUT evidence (no batches) → unresolved, excluded with reason
        var noEvidence = NewRequest("REQ-NOEV", 0m, S_PAYMENT_SCHEDULED, approved: 5000m, approvedCurrency: null);
        // recorded MULTIPLE → unresolved
        var multiple = NewRequest("REQ-MULTI", 0m, S_PAYMENT_SCHEDULED, approved: 9000m, approvedCurrency: "MULTIPLE");
        // recorded USD → other currency bucket
        var usd = NewRequest("REQ-USD", 0m, S_PAYMENT_SCHEDULED, currencyId: USD, approved: 7000m, approvedCurrency: "USD");
        ctx.Requests.AddRange(current, recorded, est, noEvidence, multiple, usd);
        ctx.RequestPoGroups.AddRange(ca, cb, Group(recorded, 10, 390000m, "AOA"), Group(noEvidence, 10, 5000m, "AOA"), Group(multiple, 10, 5000m, "AOA"), Group(multiple, 11, 4000m, "USD"), Group(usd, 11, 7000m, "USD"));
        ctx.RequestPayments.Add(payA);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentCompleted, ca, payA));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentCompleted);
        Assert.Contains($"{ActionPaid} {319180.08m:N2} AOA", body);
        Assert.Contains($"{This} {351179.88m:N2} AOA <span style='color:#555;'>{BaseDerived}</span>", body);
        Assert.Contains($"{Accumulated} {801179.88m:N2} AOA <span style='color:#555;'>(composição: 1 aprovado(s) registado(s), 1 com moeda derivada, 1 estimativa(s) — fallback)</span>", body);
        Assert.Contains($"representa <b>{43.8m:N1}%</b>", body);                                                     // 351,179.88 / 801,179.88
        Assert.Contains($"{Excluded} 3 pedido(s) agendado(s)/pago(s) do departamento neste período — 1 noutra moeda, 2 sem valor resolvido (valor aprovado registado em várias moedas (MULTIPLE); valor aprovado sem moeda registada; lotes de aprovação em falta ou não aprovados)", body);
        Assert.DoesNotContain($"{59159.90m:N2}", body);
    }

    [Fact]
    public async Task Current_request_with_recorded_approved_currency_uses_the_approved_total_not_the_estimate()
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 120000m, S_PAYMENT_SCHEDULED, approved: 390000m, approvedCurrency: "AOA");
        var g = Group(current, 10, 390000m, "AOA"); var pay = Payment(current, g, 390000m);
        ctx.Requests.Add(current); ctx.RequestPoGroups.Add(g); ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{This} {390000m:N2} AOA <span style='color:#555;'>{BaseRecorded}</span>", body);
        Assert.DoesNotContain($"{120000m:N2}", body);
        Assert.Contains($"representa <b>{100m:N1}%</b>", body);
        Assert.DoesNotContain(Excluded, body);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("pending")]
    [InlineData("mismatch")]
    [InlineData("noHistory")]
    public async Task Legacy_approval_with_undermined_evidence_renders_no_aggregate_and_discloses_the_reason(string defect)
    {
        await using var ctx = NewCtx(); await SeedCatalogAsync(ctx);
        var current = NewRequest("REQ-CUR", 0m, S_PAYMENT_SCHEDULED, approved: 900m, approvedCurrency: null);
        var g = Group(current, 10, 900m, "AOA"); var pay = Payment(current, g, 900m);
        ctx.Requests.AddRange(current, NewRequest("REQ-OTHER", 300000m, S_PAYMENT_SCHEDULED));
        ctx.RequestPoGroups.Add(g); ctx.RequestPayments.Add(pay);
        switch (defect)
        {
            case "cancelled": ctx.RequestPoGroups.Add(Group(current, 11, 300m, "AOA", RequestConstants.PoGroupStatuses.Cancelled)); AddLegacyBatchEvidence(ctx, current, 900m); break;
            case "pending": ctx.RequestPoGroups.Add(Group(current, 11, 200m, "AOA", RequestConstants.PoGroupStatuses.Pending)); AddLegacyBatchEvidence(ctx, current, 900m); break;
            case "mismatch": AddLegacyBatchEvidence(ctx, current, 600m); break;
            case "noHistory":
                ctx.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = current.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved, ApprovedTotalAmount = 900m, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = Requester });
                break;
        }
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        await Build(ctx).EmitAsync(Event(current, WorkflowEventCodes.PaymentScheduled, g, pay));

        var body = await ManagerBodyAsync(ctx, WorkflowEventCodes.PaymentScheduled);
        Assert.Contains($"{ActionScheduled} {900m:N2} AOA", body);                                                    // action amount stays separate
        Assert.Contains($"{This} <b>n/d</b> — valor aprovado sem moeda registada;", body);
        Assert.Contains($"{Accumulated} <b>n/d</b> — valor aprovado sem moeda registada;", body);
        Assert.Contains("<b>Impacto:</b> <b>n/d</b>", body);
        Assert.DoesNotContain($"{300000m:N2}", body); Assert.DoesNotContain("%</b>", body); Assert.DoesNotContain(Excluded, body);
        var expected = defect switch { "cancelled" => "grupos P.O. cancelados", "pending" => "pendentes de aprovação", "mismatch" => "soma dos lotes aprovados difere", _ => "histórico de aprovação final incompleto" };
        Assert.Contains(expected, body);
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
        Assert.Contains($"{This} <b>n/d</b> — estimativa: grupos P.O. ativos noutra moeda", body);
        Assert.Contains($"{Accumulated} <b>n/d</b> — estimativa: grupos P.O. ativos noutra moeda", body);
        Assert.Contains("<b>Impacto:</b> <b>n/d</b> — estimativa: grupos P.O. ativos noutra moeda", body);
        Assert.DoesNotContain($"{386500m:N2}", body); Assert.DoesNotContain($"{100000m:N2}", body); Assert.DoesNotContain("%</b>", body); Assert.DoesNotContain(Excluded, body);
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
        Assert.Contains($"{ActionScheduled} {50000m:N2} AOA", b1);
        Assert.Contains($"{This} <b>n/d</b> — estimativa: um grupo P.O. ativo não tem moeda registada", b1);
        var b2 = Assert.Single(rows, r => r.RequestId == noRequestCurrency.Id).BodyHtml;
        Assert.Contains($"{This} <b>n/d</b> — estimativa: moeda do pedido não registada", b2);
        Assert.DoesNotContain("%</b>", b1); Assert.DoesNotContain("%</b>", b2);
    }

    [Fact]
    public async Task This_request_is_included_even_when_its_parent_status_is_not_yet_scheduled_or_paid()
    {
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
    public async Task Legacy_event_without_group_or_payment_shows_not_available_for_the_action_amount_but_keeps_the_request_level_metric()
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
    [InlineData(0, 250000)]   // zero request estimate with other activity
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
