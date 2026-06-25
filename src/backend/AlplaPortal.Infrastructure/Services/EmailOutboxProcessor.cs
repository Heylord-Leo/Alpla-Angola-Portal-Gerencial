using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Background service that processes the EmailOutbox queue.
/// Polls the database every <see cref="PollingIntervalSeconds"/> seconds,
/// atomically claims a batch of PENDING or retryable FAILED entries,
/// and dispatches them through the existing <see cref="IEmailService.SendWorkflowNotificationAsync"/> pipeline.
///
/// <para><b>Concurrency Safety:</b> Uses atomic SQL UPDATE...OUTPUT to claim rows,
/// preventing duplicate processing across multiple application instances or overlapping cycles.</para>
///
/// <para><b>Crash Recovery:</b> Entries stuck in PROCESSING for more than
/// <see cref="StuckProcessingTimeoutMinutes"/> minutes are automatically recovered.</para>
///
/// <para><b>Retry strategy:</b> Exponential backoff — 30s, 2min, 10min.
/// After <see cref="EmailOutboxEntry.MaxRetries"/> failures, entries are moved to DEAD_LETTER.</para>
///
/// <para><b>Dedup:</b> Before sending, checks if a SENT entry already exists
/// for the same (CorrelationId, RecipientEmail) pair.</para>
/// </summary>
public class EmailOutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailOutboxProcessor> _logger;

    private const int PollingIntervalSeconds = 10;
    private const int BatchSize = 10;
    private const int StuckProcessingTimeoutMinutes = 5;

    /// <summary>Backoff schedule in seconds for retries: 30s, 2min, 10min.</summary>
    private static readonly int[] BackoffScheduleSeconds = { 30, 120, 600 };

    public EmailOutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailOutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailOutboxProcessor started. Polling every {Interval}s, batch size {BatchSize}.",
            PollingIntervalSeconds, BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailOutboxProcessor encountered an unhandled error during batch processing. Will retry on next cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("EmailOutboxProcessor stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var adminLog = scope.ServiceProvider.GetRequiredService<AdminLogWriter>();

        // ── Step 1: Recover stuck PROCESSING entries (app crash recovery) ──
        await RecoverStuckEntriesAsync(context, adminLog, ct);

        // ── Step 2: Atomically claim a batch using UPDATE...OUTPUT ──
        // This is a single atomic SQL statement — only one processor instance
        // can claim each row, preventing duplicate processing entirely.
        var now = DateTime.UtcNow;
        var claimedEntries = await context.EmailOutbox
            .FromSqlRaw(@"
                UPDATE TOP({0}) EmailOutbox
                SET Status = 'PROCESSING'
                OUTPUT INSERTED.*
                WHERE
                    (Status = 'PENDING')
                    OR (Status = 'FAILED' AND RetryCount < MaxRetries AND (NextRetryAtUtc IS NULL OR NextRetryAtUtc <= {1}))
            ", BatchSize, now)
            .AsNoTracking()
            .ToListAsync(ct);

        if (claimedEntries.Count == 0) return;

        _logger.LogInformation("EmailOutboxProcessor: claimed {Count} entries for processing.", claimedEntries.Count);

        // ── Step 3: Process each claimed entry ──
        foreach (var claimedEntry in claimedEntries)
        {
            if (ct.IsCancellationRequested) break;

            // Re-attach the entity to the current context for tracking
            var entry = await context.EmailOutbox.FindAsync(new object[] { claimedEntry.Id }, ct);
            if (entry == null || entry.Status != "PROCESSING")
            {
                _logger.LogWarning("EmailOutboxProcessor: entry {OutboxId} was not found or status changed. Skipping.", claimedEntry.Id);
                continue;
            }

            await ProcessEntryAsync(context, emailService, adminLog, entry, ct);
        }
    }

    /// <summary>
    /// Recovers entries stuck in PROCESSING status due to application crash or restart.
    /// Entries in PROCESSING for longer than <see cref="StuckProcessingTimeoutMinutes"/> minutes
    /// are reset to FAILED with their RetryCount preserved, so normal retry logic handles them.
    /// </summary>
    private async Task RecoverStuckEntriesAsync(ApplicationDbContext context, AdminLogWriter adminLog, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-StuckProcessingTimeoutMinutes);

        var stuckCount = await context.EmailOutbox
            .Where(e => e.Status == "PROCESSING" && e.CreatedAtUtc < cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, "FAILED")
                .SetProperty(e => e.LastError, $"Auto-recovered: stuck in PROCESSING for >{StuckProcessingTimeoutMinutes} min (app crash/restart)")
                .SetProperty(e => e.NextRetryAtUtc, DateTime.UtcNow), ct);

        if (stuckCount > 0)
        {
            _logger.LogWarning("EmailOutboxProcessor: recovered {Count} entries stuck in PROCESSING state.", stuckCount);

            await adminLog.WriteAsync("Warning", "EmailOutboxProcessor", "EMAIL_OUTBOX_STUCK_RECOVERED",
                $"{stuckCount} e-mail(s) preso(s) em PROCESSING por mais de {StuckProcessingTimeoutMinutes} min foram recuperados para reprocessamento.",
                payload: $"Cutoff: {cutoff:O}. ResetTo: FAILED.");
        }
    }

    private async Task ProcessEntryAsync(
        ApplicationDbContext context,
        IEmailService emailService,
        AdminLogWriter adminLog,
        EmailOutboxEntry entry,
        CancellationToken ct)
    {
        var logContext = $"OutboxId={entry.Id}, RequestId={entry.RequestId}, Event={entry.EventCode}, To={entry.RecipientEmail}, Attempt={entry.RetryCount + 1}/{entry.MaxRetries}";

        try
        {
            // ── Dedup check: skip if already sent for this correlation + recipient ──
            if (entry.CorrelationId.HasValue)
            {
                var alreadySent = await context.EmailOutbox.AnyAsync(
                    e => e.CorrelationId == entry.CorrelationId
                      && e.RecipientEmail == entry.RecipientEmail
                      && e.Status == "SENT"
                      && e.Id != entry.Id, ct);

                if (alreadySent)
                {
                    entry.Status = "SENT";
                    entry.ProcessedAtUtc = DateTime.UtcNow;
                    entry.LastError = "Skipped (duplicate — another entry with same CorrelationId already sent)";
                    await context.SaveChangesAsync(ct);

                    _logger.LogInformation("EmailOutboxProcessor: DEDUP skip. {Context}", logContext);

                    await adminLog.WriteAsync("Info", "EmailOutboxProcessor", "EMAIL_OUTBOX_DEDUP",
                        $"E-mail duplicado ignorado para {entry.RecipientEmail}. Pedido: {entry.RequestNumber ?? "N/A"}. Evento: {entry.EventCode}.",
                        payload: $"OutboxId: {entry.Id}. CorrelationId: {entry.CorrelationId}. RequestId: {entry.RequestId}.");
                    return;
                }
            }

            // ── Entry is already in PROCESSING (claimed atomically). Send now. ──
            var sent = await emailService.SendWorkflowNotificationAsync(
                entry.RecipientEmail,
                entry.RecipientName ?? "Utilizador",
                entry.Subject,
                entry.Headline,
                entry.BodyHtml,
                entry.ActionUrl,
                entry.ActionLabel,
                entry.CcEmails);

            if (!sent)
            {
                throw new InvalidOperationException("EmailService.SendWorkflowNotificationAsync returned false (email not sent).");
            }

            // ── Success ──
            entry.Status = "SENT";
            entry.ProcessedAtUtc = DateTime.UtcNow;
            entry.RetryCount++;
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("EmailOutboxProcessor: SENT. {Context}", logContext);

            await adminLog.WriteAsync("Info", "EmailOutboxProcessor", "EMAIL_OUTBOX_SENT",
                $"E-mail enviado com sucesso para {entry.RecipientEmail}. Assunto: {entry.Subject}. Pedido: {entry.RequestNumber ?? "N/A"}.",
                payload: $"OutboxId: {entry.Id}. EventCode: {entry.EventCode}. RequestId: {entry.RequestId}. RetryCount: {entry.RetryCount}. CorrelationId: {entry.CorrelationId}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EmailOutboxProcessor: FAILED. {Context}", logContext);

            entry.RetryCount++;
            entry.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            if (entry.RetryCount >= entry.MaxRetries)
            {
                // ── Dead Letter ──
                entry.Status = "DEAD_LETTER";
                entry.ProcessedAtUtc = DateTime.UtcNow;

                _logger.LogError("EmailOutboxProcessor: DEAD_LETTER after {MaxRetries} attempts. {Context}", entry.MaxRetries, logContext);

                await adminLog.WriteAsync("Error", "EmailOutboxProcessor", "EMAIL_OUTBOX_DEAD_LETTER",
                    $"E-mail para {entry.RecipientEmail} marcado como DEAD_LETTER após {entry.MaxRetries} tentativas. Pedido: {entry.RequestNumber ?? "N/A"}. Evento: {entry.EventCode}.",
                    exceptionDetail: entry.LastError,
                    payload: $"OutboxId: {entry.Id}. RequestId: {entry.RequestId}. Subject: {entry.Subject}. CorrelationId: {entry.CorrelationId}.");
            }
            else
            {
                // ── Schedule retry with exponential backoff ──
                var backoffIndex = Math.Min(entry.RetryCount - 1, BackoffScheduleSeconds.Length - 1);
                var backoffSeconds = BackoffScheduleSeconds[backoffIndex];
                entry.Status = "FAILED";
                entry.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(backoffSeconds);

                _logger.LogWarning("EmailOutboxProcessor: RETRY_SCHEDULED in {Backoff}s. {Context}", backoffSeconds, logContext);

                await adminLog.WriteAsync("Warning", "EmailOutboxProcessor", "EMAIL_OUTBOX_RETRY_SCHEDULED",
                    $"Reenvio agendado para {entry.RecipientEmail} em {backoffSeconds}s (tentativa {entry.RetryCount}/{entry.MaxRetries}). Pedido: {entry.RequestNumber ?? "N/A"}. Evento: {entry.EventCode}.",
                    exceptionDetail: entry.LastError,
                    payload: $"OutboxId: {entry.Id}. RequestId: {entry.RequestId}. NextRetry: {entry.NextRetryAtUtc:O}. CorrelationId: {entry.CorrelationId}.");
            }

            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "EmailOutboxProcessor: failed to persist error state for OutboxId={OutboxId}.", entry.Id);
            }
        }
    }
}
