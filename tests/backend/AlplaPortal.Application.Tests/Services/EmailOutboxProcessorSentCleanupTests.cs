using System;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services;

/// <summary>
/// Regression (TEST validation of v2.246.0): a row that finally went SENT after earlier failures kept
/// LastError and NextRetryAtUtc from those attempts. Both SENT paths (real send, duplicate suppression)
/// must clear them; RetryCount, the FAILED/DEAD_LETTER backoff and EXPIRED behaviour stay as they were.
/// </summary>
public class EmailOutboxProcessorSentCleanupTests
{
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (EmailOutboxProcessor Processor, Mock<IEmailService> Email, Mock<AdminLogWriter> Log) Processor()
    {
        var env = new Mock<IHostEnvironment>(); env.SetupGet(e => e.EnvironmentName).Returns("Production");
        var log = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        log.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return (new EmailOutboxProcessor(Mock.Of<IServiceScopeFactory>(), NullLogger<EmailOutboxProcessor>.Instance, env.Object), new Mock<IEmailService>(), log);
    }

    /// <summary>A claimed (PROCESSING) row that failed twice before: retry residue present.</summary>
    private static EmailOutboxEntry ClaimedRowWithRetryResidue(Guid? correlationId = null) => new()
    {
        RecipientEmail = "approver@test.local", RecipientName = "Ana Aprovadora", Subject = "s", Headline = "h", BodyHtml = "<p/>",
        Status = "PROCESSING", RetryCount = 2, MaxRetries = 3,
        LastError = "smtp refused (attempt 2)", NextRetryAtUtc = DateTime.UtcNow.AddMinutes(-1),
        CorrelationId = correlationId ?? Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20)
    };

    [Fact]
    public async Task Successful_send_after_failures_clears_LastError_and_NextRetryAtUtc_and_keeps_RetryCount()
    {
        await using var ctx = NewCtx();
        var row = ClaimedRowWithRetryResidue(); ctx.EmailOutbox.Add(row); await ctx.SaveChangesAsync();
        var (processor, email, log) = Processor();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);

        var saved = await ctx.EmailOutbox.SingleAsync();
        Assert.Equal("SENT", saved.Status);
        Assert.Null(saved.LastError);
        Assert.Null(saved.NextRetryAtUtc);
        Assert.Equal(3, saved.RetryCount);          // attempts are history, not retry state
        Assert.NotNull(saved.ProcessedAtUtc);
        email.Verify(e => e.SendWorkflowNotificationAsync("approver@test.local", "Ana Aprovadora", "s", "h", "<p/>", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Duplicate_suppression_marks_SENT_without_sending_and_clears_retry_fields()
    {
        await using var ctx = NewCtx();
        var correlation = Guid.NewGuid();
        ctx.EmailOutbox.Add(new EmailOutboxEntry { RecipientEmail = "approver@test.local", Subject = "s", Headline = "h", BodyHtml = "<p/>", Status = "SENT", CorrelationId = correlation, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30), ProcessedAtUtc = DateTime.UtcNow.AddMinutes(-29) });
        var dup = ClaimedRowWithRetryResidue(correlation); ctx.EmailOutbox.Add(dup); await ctx.SaveChangesAsync();
        var (processor, email, log) = Processor();

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, dup, CancellationToken.None);

        var saved = await ctx.EmailOutbox.SingleAsync(e => e.Id == dup.Id);
        Assert.Equal("SENT", saved.Status);
        Assert.Null(saved.LastError);
        Assert.Null(saved.NextRetryAtUtc);
        Assert.Equal(2, saved.RetryCount);          // suppression is not an attempt
        Assert.NotNull(saved.ProcessedAtUtc);
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        log.Verify(a => a.WriteAsync("Info", "EmailOutboxProcessor", "EMAIL_OUTBOX_DEDUP", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once); // the reason lives here now
    }

    [Fact]
    public async Task Failure_still_records_LastError_and_backoff_and_dead_letters_after_MaxRetries()
    {
        await using var ctx = NewCtx();
        var row = ClaimedRowWithRetryResidue(); row.RetryCount = 1; ctx.EmailOutbox.Add(row); await ctx.SaveChangesAsync();
        var (processor, email, log) = Processor();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(false);

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);
        Assert.Equal("FAILED", row.Status); Assert.Equal(2, row.RetryCount); Assert.Contains("returned false", row.LastError); Assert.NotNull(row.NextRetryAtUtc);

        row.Status = "PROCESSING";
        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);
        Assert.Equal("DEAD_LETTER", row.Status); Assert.Equal(3, row.RetryCount); Assert.NotNull(row.LastError);
    }

    [Fact]
    public async Task Expired_row_keeps_its_expiry_reason_and_is_never_sent()
    {
        await using var ctx = NewCtx();
        var row = ClaimedRowWithRetryResidue(); row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5); ctx.EmailOutbox.Add(row); await ctx.SaveChangesAsync();
        var (processor, email, log) = Processor();

        await processor.ProcessEntryAsync(ctx, email.Object, log.Object, row, CancellationToken.None);

        Assert.Equal("EXPIRED", row.Status);
        Assert.Contains("Expired before dispatch", row.LastError);
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }
}
