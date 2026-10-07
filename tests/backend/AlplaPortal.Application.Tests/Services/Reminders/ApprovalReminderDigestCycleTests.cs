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
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Reminders;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Reminders;

/// <summary>
/// Daily approval digests under the CURRENT approval model: area recipients = DepartmentManagers cascade
/// (already alternatives), final recipient = the single nominee (request nominee, else company nominee).
/// Uses the REAL routing service. Covers dry run (digest recorded, no outbox row), live (digest + items +
/// outbox in one commit, ExpiresAtUtc set), same-day restart dedup, allow-list, preview (nothing
/// persisted), reassignment of an area manager and of the company nominee, no-recipient reporting, units
/// without stage entry, the documented gap (role-holder who can approve but is not nominee gets no
/// digest), and the outbox processor on digest rows (SMTP failure → retry → DEAD_LETTER; stale → EXPIRED).
/// </summary>
public class ApprovalReminderDigestCycleTests
{
    /// <summary>Wednesday 2026-10-07 07:00 UTC = 08:00 Luanda.</summary>
    private static readonly DateTime Now = new(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewCtx(string? name = null) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AppConfig:FrontendBaseUrl"] = "https://portal.test"
    }).Build();

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static ApprovalReminderDigestCycle Cycle(ApplicationDbContext ctx, ApprovalReminderOptions? opts = null, Mock<AdminLogWriter>? adminLog = null) =>
        new(ctx, new ApprovalRoutingService(ctx), Options.Create(opts ?? new ApprovalReminderOptions { Enabled = true, DryRun = true }), Config(), (adminLog ?? AdminLog()).Object, NullLogger<ApprovalReminderDigestCycle>.Instance);

    private sealed class Seed
    {
        public User AreaA = null!, AreaB = null!, Nominee = null!, RoleHolder = null!, Requester = null!;
        public Department Dept = null!; public Company Company = null!;
        public Request Quotation = null!, Payment = null!;
        public ApprovalBatch AreaBatch = null!, FinalBatch = null!, NullEntryBatch = null!, FreshBatch = null!;
        public RequestStatus WaitingArea = null!, WaitingFinal = null!, WaitingQuotation = null!;
        public RequestType QuotationType = null!, PaymentType = null!;
    }

    /// <summary>
    /// Two area managers (global rows); ONE company nominee ("Nominee", role-holder) and one extra
    /// role-holder who is NOT nominee (can approve, is never notified — the documented gap). Units:
    ///  - QUOTATION request (Request.FinalApproverId = null → company-nominee fallback), batch #1 WAITING_AREA (5 days) → AREA unit
    ///  - batch #2 WAITING_FINAL (4 days) → FINAL unit
    ///  - batch #3 WAITING_AREA with NULL stage entry → reported, excluded
    ///  - batch #4 WAITING_AREA 1 day → below threshold
    ///  - PAYMENT request (no batches, Request.FinalApproverId = Nominee) WAITING_FINAL_APPROVAL since 6 days (history row) → FINAL request unit
    /// </summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var s = new Seed
        {
            QuotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" },
            PaymentType = new RequestType { Id = 2, Code = "PAYMENT", Name = "Pagamento" },
            WaitingQuotation = new RequestStatus { Id = 2, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Cotação" },
            WaitingArea = new RequestStatus { Id = 3, Code = RequestConstants.Statuses.WaitingAreaApproval, Name = "Área" },
            WaitingFinal = new RequestStatus { Id = 5, Code = RequestConstants.Statuses.WaitingFinalApproval, Name = "Final" },
            Dept = new Department { Id = 900, Name = "ZZ Dep", IsActive = true }
        };
        ctx.RequestTypes.AddRange(s.QuotationType, s.PaymentType);
        ctx.RequestStatuses.AddRange(s.WaitingQuotation, s.WaitingArea, s.WaitingFinal);
        ctx.Departments.Add(s.Dept);
        var role = new Role { Id = 7, RoleName = RoleConstants.FinalApprover }; ctx.Roles.Add(role);

        s.AreaA = new User { Id = Guid.NewGuid(), FullName = "Area A", Email = "area.a@test.local", IsActive = true };
        s.AreaB = new User { Id = Guid.NewGuid(), FullName = "Area B", Email = "area.b@test.local", IsActive = true };
        s.Nominee = new User { Id = Guid.NewGuid(), FullName = "Final Nominee", Email = "final.nominee@test.local", IsActive = true };
        s.RoleHolder = new User { Id = Guid.NewGuid(), FullName = "Final RoleHolder", Email = "final.roleholder@test.local", IsActive = true };
        s.Requester = new User { Id = Guid.NewGuid(), FullName = "Req", Email = "req@test.local", IsActive = true };
        ctx.Users.AddRange(s.AreaA, s.AreaB, s.Nominee, s.RoleHolder, s.Requester);
        s.Company = new Company { Id = 100, Name = "ZZ Co", IsActive = true, FinalApproverUserId = s.Nominee.Id };
        ctx.Companies.Add(s.Company);
        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = s.Dept.Id, PlantId = null, UserId = s.AreaA.Id, IsActive = true },
            new DepartmentManager { DepartmentId = s.Dept.Id, PlantId = null, UserId = s.AreaB.Id, IsActive = true });
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.Nominee.Id, RoleId = role.Id }, new UserRoleAssignment { UserId = s.RoleHolder.Id, RoleId = role.Id });

        s.Quotation = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-Q-001", Title = "q", StatusId = s.WaitingQuotation.Id, RequestTypeId = s.QuotationType.Id,
            DepartmentId = s.Dept.Id, CompanyId = s.Company.Id, RequesterId = s.Requester.Id, CreatedAtUtc = Now.AddDays(-10), FinalApproverId = null
        };
        ctx.Requests.Add(s.Quotation);
        s.AreaBatch = new ApprovalBatch { RequestId = s.Quotation.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = Now.AddDays(-5), StageEnteredAtUtc = Now.AddDays(-5), CreatedByUserId = s.Requester.Id };
        s.FinalBatch = new ApprovalBatch { RequestId = s.Quotation.Id, BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, CreatedAtUtc = Now.AddDays(-8), StageEnteredAtUtc = Now.AddDays(-4), CreatedByUserId = s.Requester.Id };
        s.NullEntryBatch = new ApprovalBatch { RequestId = s.Quotation.Id, BatchNumber = 3, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = Now.AddDays(-9), StageEnteredAtUtc = null, CreatedByUserId = s.Requester.Id };
        s.FreshBatch = new ApprovalBatch { RequestId = s.Quotation.Id, BatchNumber = 4, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = Now.AddDays(-1), StageEnteredAtUtc = Now.AddDays(-1), CreatedByUserId = s.Requester.Id };
        ctx.ApprovalBatches.AddRange(s.AreaBatch, s.FinalBatch, s.NullEntryBatch, s.FreshBatch);

        s.Payment = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-P-001", Title = "p", StatusId = s.WaitingFinal.Id, RequestTypeId = s.PaymentType.Id,
            DepartmentId = s.Dept.Id, CompanyId = s.Company.Id, RequesterId = s.Requester.Id, CreatedAtUtc = Now.AddDays(-7), FinalApproverId = s.Nominee.Id
        };
        ctx.Requests.Add(s.Payment);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { RequestId = s.Payment.Id, ActorUserId = s.Requester.Id, ActionTaken = "AREA_APPROVED", PreviousStatusId = s.WaitingArea.Id, NewStatusId = s.WaitingFinal.Id, CreatedAtUtc = Now.AddDays(-6) });
        await ctx.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Dry_run_records_one_digest_per_area_manager_and_one_for_the_single_final_nominee_without_outbox_rows()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);

        var result = await Cycle(ctx).RunAsync(Now, CancellationToken.None);
        var run = result.Run;

        Assert.Null(run.Error);
        Assert.Equal(5, run.UnitsConsidered);          // 4 batches + 1 payment request
        Assert.Equal(3, run.UnitsEligible);            // area#1, final#2, payment
        Assert.Equal(1, run.UnitsWithoutStageEntry);   // batch #3
        Assert.Equal(0, run.UnitsWithoutRecipient);
        Assert.Equal(3, run.Recipients);               // AreaA, AreaB, Nominee
        Assert.Equal(3, run.DigestsDryRun);
        Assert.Equal(0, run.DigestsQueued);
        Assert.NotNull(run.CompletedAtUtc);

        var digests = await ctx.ApprovalReminderDigests.Include(d => d.Items).ToListAsync();
        Assert.Equal(3, digests.Count);
        Assert.All(digests, d => { Assert.True(d.DryRun); Assert.Null(d.OutboxEntryId); Assert.Equal(new DateTime(2026, 10, 7), d.DigestDateLocal); });
        Assert.Empty(await ctx.EmailOutbox.ToListAsync());

        foreach (var areaUser in new[] { s.AreaA, s.AreaB })
        {
            var d = digests.Single(x => x.RecipientUserId == areaUser.Id);
            var item = Assert.Single(d.Items);
            Assert.Equal(s.AreaBatch.Id, item.ApprovalBatchId); Assert.Equal("AREA", item.Stage); Assert.Equal(5, item.DaysPending);
        }
        var nominee = digests.Single(x => x.RecipientUserId == s.Nominee.Id);
        Assert.Equal(2, nominee.Items.Count);
        Assert.Contains(nominee.Items, i => i.ApprovalBatchId == s.FinalBatch.Id && i.DaysPending == 4); // company-nominee fallback (request nominee null)
        Assert.Contains(nominee.Items, i => i.RequestId == s.Payment.Id && i.ApprovalBatchId == null && i.DaysPending == 6); // request nominee
        Assert.Equal("REQ-P-001", nominee.Items.OrderBy(i => i.StageEnteredAtUtc).First().RequestNumber); // oldest first
        Assert.DoesNotContain(digests, d => d.RecipientUserId == s.RoleHolder.Id); // can approve, is not notified (documented gap)
        Assert.Equal(1, await ctx.ApprovalReminderRuns.CountAsync());
    }

    [Fact]
    public async Task Live_mode_queues_outbox_row_atomically_with_digest_and_items_and_sets_expiry()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false, DigestExpiryHours = 4 };
        var adminLog = AdminLog();

        var result = await Cycle(ctx, opts, adminLog).RunAsync(Now, CancellationToken.None);

        Assert.Equal(3, result.Run.DigestsQueued);
        Assert.Equal(0, result.Run.DigestsDryRun);
        var digests = await ctx.ApprovalReminderDigests.Include(d => d.OutboxEntry).Include(d => d.Items).ToListAsync();
        var outbox = await ctx.EmailOutbox.ToListAsync();
        Assert.Equal(3, outbox.Count);
        foreach (var d in digests)
        {
            Assert.False(d.DryRun);
            Assert.NotNull(d.OutboxEntryId);
            var o = outbox.Single(x => x.Id == d.OutboxEntryId);
            Assert.Equal(d.Id, o.CorrelationId);
            Assert.Equal(ApprovalReminderDigestCycle.EventCode, o.EventCode);
            Assert.Equal("PENDING", o.Status);
            Assert.Equal(Now.AddHours(4), o.ExpiresAtUtc);
            Assert.Equal(d.Subject, o.Subject);
            Assert.Equal(d.PayloadHtml, o.BodyHtml);
            Assert.Equal("https://portal.test/approvals", o.ActionUrl);
            Assert.Null(o.RequestId); // a digest spans requests
            Assert.NotEmpty(d.Items);
        }
        var email = outbox.Single(o => o.RecipientEmail == s.Nominee.Email);
        Assert.Equal("Lembrete: 2 aprovações pendentes há mais de 3 dias", email.Subject);
        adminLog.Verify(a => a.WriteAsync("Info", "Notification", "APPROVAL_REMINDER_QUEUED", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Exactly(3));
        adminLog.Verify(a => a.WriteAsync("Info", "Notification", "APPROVAL_REMINDER_CYCLE", It.Is<string>(m => m.Contains("LIVE") && m.Contains("queued = outbox row created, not delivery")), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Same_day_rerun_after_restart_skips_every_recipient_and_queues_nothing_new()
    {
        await using var ctx = NewCtx();
        await SeedAsync(ctx);
        var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false };

        await Cycle(ctx, opts).RunAsync(Now, CancellationToken.None);
        var second = await Cycle(ctx, opts).RunAsync(Now.AddHours(2), CancellationToken.None); // restart later the same Luanda day

        Assert.Equal(0, second.Run.DigestsQueued);
        Assert.Equal(3, second.Run.SkippedDedup);
        Assert.Equal(3, await ctx.ApprovalReminderDigests.CountAsync());
        Assert.Equal(3, await ctx.EmailOutbox.CountAsync());
        Assert.Equal(2, await ctx.ApprovalReminderRuns.CountAsync()); // both runs are visible

        var tomorrow = await Cycle(ctx, opts).RunAsync(Now.AddDays(1), CancellationToken.None);
        Assert.Equal(3, tomorrow.Run.DigestsQueued);
        Assert.Equal(6, await ctx.EmailOutbox.CountAsync());
    }

    [Fact]
    public async Task Dry_run_digest_does_not_block_a_live_digest_on_the_same_day()
    {
        await using var ctx = NewCtx();
        await SeedAsync(ctx);
        await Cycle(ctx, new ApprovalReminderOptions { Enabled = true, DryRun = true }).RunAsync(Now, CancellationToken.None);
        var live = await Cycle(ctx, new ApprovalReminderOptions { Enabled = true, DryRun = false }).RunAsync(Now.AddMinutes(5), CancellationToken.None);
        Assert.Equal(3, live.Run.DigestsQueued);
        Assert.Equal(0, live.Run.SkippedDedup);
    }

    [Fact]
    public async Task Allow_list_restricts_live_sending_to_listed_recipients()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false, RecipientAllowList = new[] { " FINAL.NOMINEE@test.local " } };

        var result = await Cycle(ctx, opts).RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, result.Run.DigestsQueued);
        Assert.Equal(2, result.Run.SkippedAllowList);
        var only = Assert.Single(await ctx.EmailOutbox.ToListAsync());
        Assert.Equal(s.Nominee.Email, only.RecipientEmail);
    }

    [Fact]
    public async Task Preview_computes_digests_but_persists_and_queues_nothing()
    {
        await using var ctx = NewCtx();
        await SeedAsync(ctx);

        var result = await Cycle(ctx, new ApprovalReminderOptions { Enabled = false, DryRun = false }).PreviewAsync(Now, CancellationToken.None);

        Assert.Equal(ApprovalReminderDigestCycle.TriggerManualPreview, result.Run.Trigger);
        Assert.Equal(3, result.Digests.Count);
        Assert.All(result.Digests, d => { Assert.NotEmpty(d.PayloadHtml); Assert.NotEmpty(d.Items); });
        Assert.Equal(0, await ctx.ApprovalReminderRuns.CountAsync());
        Assert.Equal(0, await ctx.ApprovalReminderDigests.CountAsync());
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
    }

    [Fact]
    public async Task Reassignment_of_an_area_manager_and_of_the_company_nominee_is_reflected_the_next_day()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false };
        await Cycle(ctx, opts).RunAsync(Now, CancellationToken.None);

        // Day 2: Area A no longer manages; Area C takes over. Company nominee changes to RoleHolder
        // (affects the QUOTATION batch via fallback; the PAYMENT request keeps its own nominee).
        var rowA = await ctx.DepartmentManagers.SingleAsync(m => m.UserId == s.AreaA.Id);
        rowA.IsActive = false;
        var areaC = new User { Id = Guid.NewGuid(), FullName = "Area C", Email = "area.c@test.local", IsActive = true };
        ctx.Users.Add(areaC);
        ctx.DepartmentManagers.Add(new DepartmentManager { DepartmentId = s.Dept.Id, UserId = areaC.Id, IsActive = true });
        s.Company.FinalApproverUserId = s.RoleHolder.Id;
        await ctx.SaveChangesAsync();

        var day2 = await Cycle(ctx, opts).RunAsync(Now.AddDays(1), CancellationToken.None);

        Assert.Equal(4, day2.Run.DigestsQueued); // AreaB, AreaC, Nominee (payment), RoleHolder (final batch)
        var day2Digests = await ctx.ApprovalReminderDigests.Include(d => d.Items).Where(d => d.DigestDateLocal == new DateTime(2026, 10, 8)).ToListAsync();
        Assert.Contains(day2Digests, d => d.RecipientUserId == areaC.Id);
        Assert.Contains(day2Digests, d => d.RecipientUserId == s.AreaB.Id);
        Assert.DoesNotContain(day2Digests, d => d.RecipientUserId == s.AreaA.Id);
        Assert.Equal(s.FinalBatch.Id, Assert.Single(day2Digests.Single(d => d.RecipientUserId == s.RoleHolder.Id).Items).ApprovalBatchId);
        Assert.Equal(s.Payment.Id, Assert.Single(day2Digests.Single(d => d.RecipientUserId == s.Nominee.Id).Items).RequestId);
    }

    [Fact]
    public async Task Unit_without_any_resolvable_recipient_is_reported_not_silently_dropped()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        foreach (var m in await ctx.DepartmentManagers.ToListAsync()) m.IsActive = false; // nobody manages the department now
        s.Nominee.Email = string.Empty;                                                  // nominee cannot be e-mailed (can still approve)
        await ctx.SaveChangesAsync();
        var adminLog = AdminLog();

        var result = await Cycle(ctx, new ApprovalReminderOptions { Enabled = true, DryRun = true }, adminLog).RunAsync(Now, CancellationToken.None);

        Assert.Equal(3, result.Run.UnitsWithoutRecipient); // area batch + final batch + payment request
        Assert.Equal(0, result.Run.Recipients);            // the role-holder is never substituted in
        adminLog.Verify(a => a.WriteAsync("Warning", "Notification", "APPROVAL_REMINDER_NO_RECIPIENT", It.Is<string>(m => m.Contains("REQ-Q-001/AREA#1") && m.Contains("REQ-P-001/FINAL")), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Batch_less_request_without_a_status_history_row_is_reported_as_without_stage_entry()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        ctx.RequestStatusHistories.RemoveRange(await ctx.RequestStatusHistories.ToListAsync());
        await ctx.SaveChangesAsync();

        var result = await Cycle(ctx).RunAsync(Now, CancellationToken.None);

        Assert.Equal(2, result.Run.UnitsWithoutStageEntry); // batch #3 + payment request
        Assert.Equal(2, result.Run.UnitsEligible);
        var nomineeDigest = await ctx.ApprovalReminderDigests.Include(d => d.Items).SingleAsync(d => d.RecipientUserId == s.Nominee.Id);
        Assert.DoesNotContain(nomineeDigest.Items, i => i.RequestId == s.Payment.Id);
    }

    [Fact]
    public async Task Request_with_an_active_batch_in_the_stage_is_not_double_counted_as_a_request_unit()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        s.Quotation.StatusId = s.WaitingArea.Id; // scalar status also "waiting area" while batch #1 is the real unit
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { RequestId = s.Quotation.Id, ActorUserId = s.Requester.Id, ActionTaken = "X", NewStatusId = s.WaitingArea.Id, CreatedAtUtc = Now.AddDays(-9) });
        await ctx.SaveChangesAsync();

        var result = await Cycle(ctx).RunAsync(Now, CancellationToken.None);
        Assert.Equal(5, result.Run.UnitsConsidered); // unchanged: QUOTATION with batches never yields a request unit
    }

    // ───────────────────────── outbox processor on digest rows ─────────────────────────

    private static (EmailOutboxProcessor Processor, Mock<IEmailService> Email, Mock<AdminLogWriter> Log) Processor()
    {
        var env = new Mock<IHostEnvironment>(); env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return (new EmailOutboxProcessor(Mock.Of<IServiceScopeFactory>(), NullLogger<EmailOutboxProcessor>.Instance, env.Object), new Mock<IEmailService>(), AdminLog());
    }

    private static async Task<EmailOutboxEntry> QueuedDigestRowAsync(ApplicationDbContext ctx, DateTime? expiresAtUtc = null)
    {
        await SeedAsync(ctx);
        var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false };
        await Cycle(ctx, opts).RunAsync(Now, CancellationToken.None);
        var row = await ctx.EmailOutbox.FirstAsync();
        if (expiresAtUtc.HasValue) { row.ExpiresAtUtc = expiresAtUtc; await ctx.SaveChangesAsync(); }
        row.Status = "PROCESSING"; // as claimed by the processor's atomic UPDATE
        return row;
    }

    [Fact]
    public async Task Smtp_failure_schedules_retries_then_dead_letters_the_digest_without_duplicating_it()
    {
        await using var ctx = NewCtx();
        var row = await QueuedDigestRowAsync(ctx, expiresAtUtc: DateTime.UtcNow.AddHours(4));
        var (processor, email, log) = Processor();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
             .ReturnsAsync(false); // SMTP refused

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);
        Assert.Equal("FAILED", row.Status); Assert.Equal(1, row.RetryCount); Assert.NotNull(row.NextRetryAtUtc); Assert.Contains("returned false", row.LastError);

        row.Status = "PROCESSING";
        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);
        Assert.Equal("FAILED", row.Status); Assert.Equal(2, row.RetryCount);

        row.Status = "PROCESSING";
        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);
        Assert.Equal("DEAD_LETTER", row.Status); Assert.Equal(3, row.RetryCount); Assert.NotNull(row.ProcessedAtUtc);

        email.Verify(e => e.SendWorkflowNotificationAsync(row.RecipientEmail, It.IsAny<string>(), row.Subject, It.IsAny<string>(), row.BodyHtml, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Exactly(3));
        Assert.Equal(3, await ctx.EmailOutbox.CountAsync()); // retries reuse the row; nothing is re-queued
        var digest = await ctx.ApprovalReminderDigests.SingleAsync(d => d.OutboxEntryId == row.Id);
        Assert.False(digest.DryRun); // the digest record stands; its delivery status is read from the outbox row (FAILED)
    }

    [Fact]
    public async Task Stale_digest_row_is_marked_expired_and_never_sent()
    {
        await using var ctx = NewCtx();
        var row = await QueuedDigestRowAsync(ctx, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var (processor, email, log) = Processor();

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);

        Assert.Equal("EXPIRED", row.Status);
        Assert.NotNull(row.ProcessedAtUtc);
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        log.Verify(a => a.WriteAsync("Warning", "EmailOutboxProcessor", "EMAIL_OUTBOX_EXPIRED", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Successful_send_marks_the_row_sent_once()
    {
        await using var ctx = NewCtx();
        var row = await QueuedDigestRowAsync(ctx, expiresAtUtc: DateTime.UtcNow.AddHours(4));
        var (processor, email, log) = Processor();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);

        Assert.Equal("SENT", row.Status);
        Assert.NotNull(row.ProcessedAtUtc);
    }
}
