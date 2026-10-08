using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
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
/// Proforma alerts through the outbox. An alert record now means QUEUED (an EmailOutbox row exists,
/// written atomically with the record), never delivered; delivery is read from the outbox row. A level
/// whose latest outbox row dead-lettered/expired is RE-QUEUED on the next cycle against the same record
/// (unique key preserved); pending/sent levels are skipped. Final-stage recipient = the single nominee
/// (request nominee, else company nominee) under the current approval model; a role-holder who is not
/// nominee can approve but receives no alert (documented gap). Also pins the processor's expiry rule.
/// </summary>
public class ProformaDeadlineAlertCycleTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IConfiguration Config(int expiryHours = 24) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AppConfig:FrontendBaseUrl"] = "https://portal.test",
        ["AppConfig:ProformaDeadlineAlerts:ThresholdDays:0"] = "3",
        ["AppConfig:ProformaDeadlineAlerts:ThresholdDays:1"] = "1",
        ["AppConfig:ProformaDeadlineAlerts:ThresholdDays:2"] = "0",
        ["AppConfig:ProformaDeadlineAlerts:OutboxExpiryHours"] = expiryHours.ToString()
    }).Build();

    private static ProformaDeadlineAlertCycle Cycle(ApplicationDbContext ctx, Mock<INotificationService>? notifications = null)
    {
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return new ProformaDeadlineAlertCycle(ctx, (notifications ?? new Mock<INotificationService>()).Object, Config(), adminLog.Object, NullLogger.Instance);
    }

    private sealed record Seed(Request Request, User Nominee, User RoleHolder, Company Company);

    /// <summary>One PAYMENT request in WAITING_FINAL_APPROVAL with NeedBy = today + <paramref name="daysAhead"/>; one company nominee (also the request nominee) and one role-holder who is NOT nominee.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, int daysAhead, bool requestNomineeSet = true)
    {
        var payment = new RequestType { Id = 2, Code = "PAYMENT", Name = "Pagamento" };
        var status = new RequestStatus { Id = 5, Code = "WAITING_FINAL_APPROVAL", Name = "Aprovação Final" };
        var role = new Role { Id = 7, RoleName = RoleConstants.FinalApprover };
        var nominee = new User { Id = Guid.NewGuid(), FullName = "Final Nominee", Email = "final.nominee@test.local", IsActive = true };
        var roleHolder = new User { Id = Guid.NewGuid(), FullName = "Final RoleHolder", Email = "final.roleholder@test.local", IsActive = true };
        var requester = new User { Id = Guid.NewGuid(), FullName = "Req", Email = "req@test.local", IsActive = true };
        var company = new Company { Id = 100, Name = "ZZ Co", IsActive = true, FinalApproverUserId = nominee.Id };
        ctx.RequestTypes.Add(payment); ctx.RequestStatuses.Add(status); ctx.Roles.Add(role); ctx.Companies.Add(company);
        ctx.Departments.Add(new Department { Id = 900, Name = "ZZ Dep" });
        ctx.Users.AddRange(nominee, roleHolder, requester);
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = nominee.Id, RoleId = role.Id }, new UserRoleAssignment { UserId = roleHolder.Id, RoleId = role.Id });
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-PF-001", Title = "proforma", StatusId = status.Id, Status = status,
            RequestTypeId = payment.Id, RequestType = payment, DepartmentId = 900, CompanyId = company.Id, RequesterId = requester.Id,
            FinalApproverId = requestNomineeSet ? nominee.Id : null,
            NeedByDateUtc = Now.Date.AddDays(daysAhead), CreatedAtUtc = Now
        };
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return new Seed(request, nominee, roleHolder, company);
    }

    [Fact]
    public async Task Eligible_request_queues_one_outbox_row_for_the_nominee_atomically_with_the_alert_record()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, daysAhead: 3); // WARNING_3D today
        var notifications = new Mock<INotificationService>();

        var result = await Cycle(ctx, notifications).RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, result.Eligible);
        Assert.Equal(1, result.Queued);
        Assert.Equal(0, result.SkippedDedup);
        Assert.Equal(0, result.NoRecipient);

        var al = Assert.Single(await ctx.ProformaDeadlineAlerts.Include(x => x.OutboxEntry).ToListAsync());
        Assert.Equal("WARNING_3D", al.AlertLevel);
        Assert.Equal(s.Nominee.Id, al.RecipientUserId);
        Assert.NotNull(al.OutboxEntryId);
        Assert.Equal(1, al.QueuedCount);
        Assert.False(al.EmailSent);                                           // never "sent" at queue time
        Assert.Equal("PENDING", al.OutboxEntry!.Status);
        Assert.Equal(ProformaDeadlineAlertCycle.DeliveryQueued, ProformaDeadlineAlertCycle.DescribeDelivery(al));
        Assert.Equal(al.Id, al.OutboxEntry.CorrelationId);                    // correlation = alert record
        Assert.Equal("PROFORMA_DEADLINE_WARNING_3D", al.OutboxEntry.EventCode);
        Assert.Equal(Now.AddHours(24), al.OutboxEntry.ExpiresAtUtc);         // stale alerts never go out a day late
        Assert.Contains("Proforma vence em 3 dias", al.OutboxEntry.Subject);
        Assert.Equal(s.Nominee.Email, al.OutboxEntry.RecipientEmail);
        Assert.DoesNotContain("Olá", al.OutboxEntry.BodyHtml);            // greeting is owned by the e-mail template (no duplicate "Olá Nome,")
        Assert.Equal("Final Nominee", al.OutboxEntry.RecipientName);       // the template greets from RecipientName
        Assert.DoesNotContain(await ctx.EmailOutbox.ToListAsync(), o => o.RecipientEmail == s.RoleHolder.Email); // can approve, not alerted (documented gap)
        notifications.Verify(n => n.CreateNotificationAsync(s.Nominee.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Without_a_request_nominee_the_company_nominee_is_alerted()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, daysAhead: 3, requestNomineeSet: false);
        var result = await Cycle(ctx).RunAsync(Now, CancellationToken.None);
        Assert.Equal(1, result.Queued);
        Assert.Equal(s.Nominee.Id, (await ctx.ProformaDeadlineAlerts.SingleAsync()).RecipientUserId);
    }

    [Fact]
    public async Task Nominee_without_email_means_no_recipient_and_nothing_queued()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, daysAhead: 3);
        s.Nominee.Email = string.Empty; // cannot be e-mailed; can still approve
        await ctx.SaveChangesAsync();
        var result = await Cycle(ctx).RunAsync(Now, CancellationToken.None);
        Assert.Equal(1, result.Eligible); Assert.Equal(0, result.Queued); Assert.Equal(1, result.NoRecipient);
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
        Assert.Equal(0, await ctx.ProformaDeadlineAlerts.CountAsync());
    }

    [Fact]
    public async Task Pending_or_sent_levels_are_not_requeued_but_dead_lettered_or_expired_ones_are()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, daysAhead: 3);
        await Cycle(ctx).RunAsync(Now, CancellationToken.None);

        // Second run the same day: still PENDING → dedup
        var again = await Cycle(ctx).RunAsync(Now.AddHours(1), CancellationToken.None);
        Assert.Equal(0, again.Queued); Assert.Equal(0, again.Requeued); Assert.Equal(1, again.SkippedDedup);

        // Simulate the processor: SENT → not re-queued
        var alert = await ctx.ProformaDeadlineAlerts.Include(x => x.OutboxEntry).SingleAsync();
        alert.OutboxEntry!.Status = "SENT";
        await ctx.SaveChangesAsync();
        Assert.Equal(ProformaDeadlineAlertCycle.DeliverySent, ProformaDeadlineAlertCycle.DescribeDelivery(alert));
        var third = await Cycle(ctx).RunAsync(Now.AddHours(2), CancellationToken.None);
        Assert.Equal(0, third.Queued); Assert.Equal(0, third.Requeued); Assert.Equal(1, third.SkippedDedup);

        // DEAD_LETTER → re-queued against the same record (unique key kept), new outbox row, counter incremented
        alert.OutboxEntry.Status = "DEAD_LETTER";
        await ctx.SaveChangesAsync();
        Assert.Equal(ProformaDeadlineAlertCycle.DeliveryFailed, ProformaDeadlineAlertCycle.DescribeDelivery(alert));
        var fourth = await Cycle(ctx).RunAsync(Now.AddHours(3), CancellationToken.None);
        Assert.Equal(0, fourth.Queued); Assert.Equal(1, fourth.Requeued); Assert.Equal(0, fourth.SkippedDedup);

        Assert.Equal(1, await ctx.ProformaDeadlineAlerts.CountAsync());
        var re = await ctx.ProformaDeadlineAlerts.Include(x => x.OutboxEntry).SingleAsync();
        Assert.Equal(2, re.QueuedCount);
        Assert.Equal("PENDING", re.OutboxEntry!.Status);
        Assert.Contains("Re-queued after DEAD_LETTER", re.ErrorMessage);
        Assert.Equal(2, await ctx.EmailOutbox.CountAsync());                       // original + re-queue

        // EXPIRED is also retryable; PENDING / SENT are not
        re.OutboxEntry.Status = "EXPIRED";
        Assert.True(ProformaDeadlineAlertCycle.IsRetryable(re));
        re.OutboxEntry.Status = "PENDING";
        Assert.False(ProformaDeadlineAlertCycle.IsRetryable(re));
    }

    [Fact]
    public async Task Legacy_direct_send_rows_are_described_by_their_flag_and_never_requeued()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, daysAhead: 3);
        ctx.ProformaDeadlineAlerts.AddRange(
            new ProformaDeadlineAlert { RequestId = s.Request.Id, AlertLevel = "WARNING_3D", RecipientUserId = s.Nominee.Id, EmailSent = false, ErrorMessage = "Email dispatch failed", SentAtUtc = Now.AddDays(-1) },
            new ProformaDeadlineAlert { RequestId = s.Request.Id, AlertLevel = "WARNING_1D", RecipientUserId = s.Nominee.Id, EmailSent = true, SentAtUtc = Now.AddDays(-1) });
        await ctx.SaveChangesAsync();

        var result = await Cycle(ctx).RunAsync(Now, CancellationToken.None);
        Assert.Equal(0, result.Queued); Assert.Equal(0, result.Requeued); Assert.Equal(1, result.SkippedDedup); // today's level (3D) exists as a legacy row → skipped, not retried
        var rows = await ctx.ProformaDeadlineAlerts.ToListAsync();
        Assert.Equal(ProformaDeadlineAlertCycle.DeliveryFailedLegacy, ProformaDeadlineAlertCycle.DescribeDelivery(rows.Single(r => r.AlertLevel == "WARNING_3D")));
        Assert.Equal(ProformaDeadlineAlertCycle.DeliverySentLegacy, ProformaDeadlineAlertCycle.DescribeDelivery(rows.Single(r => r.AlertLevel == "WARNING_1D")));
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
    }

    [Theory]
    [InlineData(4, null)]
    [InlineData(3, "WARNING_3D")]
    [InlineData(2, null)]
    [InlineData(1, "WARNING_1D")]
    [InlineData(0, "CRITICAL_0D")]
    [InlineData(-1, "EXPIRED")]
    [InlineData(-30, "EXPIRED")]
    public void Alert_level_thresholds(int daysRemaining, string? expected)
        => Assert.Equal(expected, ProformaDeadlineAlertCycle.DetermineAlertLevel(daysRemaining, new[] { 3, 1, 0 }));

    [Fact]
    public void Processor_expiry_rule_is_strictly_past_and_null_never_expires()
    {
        var now = Now;
        Assert.False(EmailOutboxProcessor.IsExpired(new EmailOutboxEntry { ExpiresAtUtc = null }, now));
        Assert.False(EmailOutboxProcessor.IsExpired(new EmailOutboxEntry { ExpiresAtUtc = now }, now));
        Assert.False(EmailOutboxProcessor.IsExpired(new EmailOutboxEntry { ExpiresAtUtc = now.AddMinutes(1) }, now));
        Assert.True(EmailOutboxProcessor.IsExpired(new EmailOutboxEntry { ExpiresAtUtc = now.AddSeconds(-1) }, now));
    }
}
