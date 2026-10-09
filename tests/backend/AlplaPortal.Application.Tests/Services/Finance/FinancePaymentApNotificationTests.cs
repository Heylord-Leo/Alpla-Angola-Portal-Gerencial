using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Regression for TEST REQ-07/10/2026-538 (v2.247.1): the Accounts Payable scheduling/completion notices showed
/// Supplier "—" and shared one subject/body because FinanceController emitted request-level events while both actions
/// operate on ONE P.O. group and ONE payment row. Runs the REAL FinanceController.SchedulePayment / MarkAsPaid against
/// the REAL WorkflowNotificationOrchestrator on an InMemory context: a QUOTATION request with no header supplier, a zero
/// header estimate and two groups in different currencies.
/// </summary>
public class FinancePaymentApNotificationTests
{
    private const int PlantA = 5;
    private const int S_PO_ISSUED = 9, S_PAYMENT_SCHEDULED = 14, S_PAYMENT_COMPLETED = 15;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed
    {
        public Guid RequestId, GroupA, GroupB, ProofId, Actor, Requester;
        public const string RequestNumber = "REQ-07/10/2026-538";
    }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), GroupA = Guid.NewGuid(), GroupB = Guid.NewGuid(), ProofId = Guid.NewGuid(), Actor = Guid.NewGuid(), Requester = Guid.NewGuid() };
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true });
        ctx.Plants.Add(new Plant { Id = PlantA, Name = "Planta 2" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_PO_ISSUED, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" },
            new RequestStatus { Id = S_PAYMENT_SCHEDULED, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado" },
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado" });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 10, Name = "PTA-ÁGUAS, LDA", TaxId = "5010", RegistrationStatus = "ACTIVE" },
            new Supplier { Id = 11, Name = "USD VENDOR & CO", TaxId = "5011", RegistrationStatus = "ACTIVE" });
        ctx.Users.AddRange(
            new User { Id = s.Actor, FullName = "Milton Finanças", Email = "finance@test.local", IsActive = true },
            new User { Id = s.Requester, FullName = "Requisitante", Email = "requester@test.local", IsActive = true });
        ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig
        {
            Id = 1, CompanyId = 1, Email = "alpla-plasticos-accounts@alpla.com", CcEmails = "aovia-treasury@alpla.com", IsActive = true,
            NotifyOnScheduled = true, NotifyOnCompleted = true, NotifyOnPoRegistered = false, NotifyFinanceUsersByEmail = false
        });
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = Seed.RequestNumber, Title = "Água engarrafada — Planta 2", RequestTypeId = 1, StatusId = S_PO_ISSUED,
            RequesterId = s.Requester, DepartmentId = 7, CompanyId = 1, PlantId = PlantA, CurrencyId = 1,
            SupplierId = null, EstimatedTotalAmount = 0m, CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        });
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = s.GroupA, RequestId = s.RequestId, SupplierId = 10, SupplierNameSnapshot = "PTA-ÁGUAS, LDA", TotalAmount = 126787.50m, CurrencyCode = "AOA", Status = RequestConstants.Statuses.PoIssued, CreatedByUserId = s.Actor, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
            new RequestPoGroup { Id = s.GroupB, RequestId = s.RequestId, SupplierId = 11, SupplierNameSnapshot = "USD VENDOR & CO", TotalAmount = 1500m, CurrencyCode = "USD", Status = RequestConstants.Statuses.PoIssued, CreatedByUserId = s.Actor, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) });
        ctx.RequestAttachments.Add(new RequestAttachment { Id = s.ProofId, RequestId = s.RequestId, FileName = "comprovativo.pdf", FileExtension = ".pdf", AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, IsDeleted = false });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return s;
    }

    private static (FinanceController Controller, Mock<IEmailService> Email) Build(ApplicationDbContext ctx, Guid actorId)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
        var orchestrator = new WorkflowNotificationOrchestrator(ctx, inApp.Object, email.Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, adminLog.Object, routing.Object);

        var controller = new FinanceController(ctx, orchestrator, NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance), new FinancePaymentEligibilityService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test"))
            }
        };
        return (controller, email);
    }

    private static List<(string To, string Subject, string Headline, string Body, string? Cc)> ApMails(Mock<IEmailService> email) =>
        email.Invocations.Where(i => i.Method.Name == nameof(IEmailService.SendWorkflowNotificationAsync))
            .Select(i => ((string)i.Arguments[0], (string)i.Arguments[2], (string)i.Arguments[3], (string)i.Arguments[4], (string?)i.Arguments[7])).ToList();

    [Fact]
    public async Task SchedulePayment_sends_the_AP_scheduling_notice_with_the_scheduled_groups_supplier_planned_amount_currency_and_date()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (controller, email) = Build(ctx, s.Actor);

        var result = await controller.SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupA, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), Comment = "Agendado" });
        Assert.IsType<OkResult>(result);

        var mail = Assert.Single(ApMails(email));
        Assert.Equal("alpla-plasticos-accounts@alpla.com", mail.To);
        Assert.Equal("aovia-treasury@alpla.com", mail.Cc);
        Assert.Equal($"[Portal Gerencial] Pagamento agendado — Pedido {Seed.RequestNumber}", mail.Subject);
        Assert.Equal("Pagamento Agendado — AlplaPLASTICO", mail.Headline);
        Assert.Contains("foi <b>agendado</b> pelas Finan&#231;as e entrou na lista de Contas a Pagar", mail.Body);
        Assert.Contains($"<b>Fornecedor:</b></td><td style='padding:6px 0;'>{System.Net.WebUtility.HtmlEncode("PTA-ÁGUAS, LDA")}</td>", mail.Body);
        Assert.Contains($"<b>Montante agendado (grupo P.O.):</b></td><td style='padding:6px 0;'>{126787.50m:N2} AOA</td>", mail.Body);
        Assert.Contains("<b>Data agendada:</b></td><td style='padding:6px 0;'>20/10/2026</td>", mail.Body);
        Assert.Contains("<b>A&#231;&#227;o realizada por:</b></td><td style='padding:6px 0;'>Milton Finan&#231;as</td>", mail.Body);
        Assert.DoesNotContain("USD", mail.Body); Assert.DoesNotContain($"{1500m:N2}", mail.Body); Assert.DoesNotContain("—</td>", mail.Body); Assert.DoesNotContain($"{0m:N2} AOA", mail.Body);

        // Persisted unit the notice was built from: the scheduled row of group A
        var pay = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == s.GroupA);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, pay.PaymentStatus);
        Assert.Equal(126787.50m, pay.PlannedAmount);

        // AP log + recipients + dedup unchanged
        var log = Assert.Single(await ctx.AccountsPayableNotificationLogs.ToListAsync());
        Assert.True(log.Success); Assert.False(log.Skipped); Assert.Null(log.ErrorMessage); Assert.Null(log.CorrelationId); Assert.Equal(WorkflowEventCodes.PaymentScheduled, log.EventCode);
        Assert.Contains(await ctx.EmailOutbox.ToListAsync(), x => x.RecipientEmail == "requester@test.local" && x.EventCode == WorkflowEventCodes.PaymentScheduled);

        // Documented, unchanged limitation: AP dedup for scheduling is per (request, event, recipient) — scheduling the
        // second group of the same request does not produce a second AP notice (a skipped log row is written instead).
        Assert.IsType<OkResult>(await controller.SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupB, ScheduledDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc) }));
        Assert.Single(ApMails(email));
        Assert.Equal(1, await ctx.AccountsPayableNotificationLogs.CountAsync(l => l.Skipped));
    }

    [Fact]
    public async Task MarkAsPaid_sends_the_AP_completion_notice_with_the_paid_groups_supplier_actual_amount_currency_and_date()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (controller, email) = Build(ctx, s.Actor);

        var result = await controller.MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, PaymentProofAttachmentId = s.ProofId, ActualPaidAmount = 1500m, PaidDate = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), Comment = "Pago" });
        Assert.IsType<OkResult>(result);

        var mail = Assert.Single(ApMails(email));
        Assert.Equal($"[Portal Gerencial] Pagamento realizado — Pedido {Seed.RequestNumber}", mail.Subject);
        Assert.Equal("Pagamento Realizado — AlplaPLASTICO", mail.Headline);
        Assert.Contains("confirmado como <b>realizado</b> pelas Finan&#231;as", mail.Body);
        Assert.DoesNotContain("entrou na lista de Contas a Pagar", mail.Body);
        Assert.DoesNotContain("Novo pedido de pagamento", mail.Subject);
        Assert.Contains($"<b>Fornecedor:</b></td><td style='padding:6px 0;'>{System.Net.WebUtility.HtmlEncode("USD VENDOR & CO")}</td>", mail.Body);
        Assert.Contains($"<b>Montante pago (grupo P.O.):</b></td><td style='padding:6px 0;'>{1500m:N2} USD</td>", mail.Body);   // group B currency, not the request's AOA
        Assert.Contains("<b>Data do pagamento:</b></td><td style='padding:6px 0;'>09/10/2026</td>", mail.Body);
        Assert.Contains("<b>Status atual:</b></td><td style='padding:6px 0;'>Pagamento Realizado", mail.Body);
        Assert.DoesNotContain("AOA", mail.Body); Assert.DoesNotContain("PTA-", mail.Body); Assert.DoesNotContain($"{126787.50m:N2}", mail.Body); Assert.DoesNotContain("Data agendada", mail.Body);

        var pay = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == s.GroupB);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, pay.PaymentStatus);
        Assert.Equal(1500m, pay.ActualPaidAmount);

        var log = Assert.Single(await ctx.AccountsPayableNotificationLogs.ToListAsync());
        Assert.True(log.Success); Assert.Null(log.CorrelationId); Assert.Equal(WorkflowEventCodes.PaymentCompleted, log.EventCode);
        Assert.Contains(await ctx.EmailOutbox.ToListAsync(), x => x.RecipientEmail == "requester@test.local" && x.EventCode == WorkflowEventCodes.PaymentCompleted);
    }
}
