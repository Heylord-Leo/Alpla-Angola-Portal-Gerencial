using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Daily background host for <see cref="ProformaDeadlineAlertCycle"/>: scans PAYMENT requests in
/// approval stages and queues Proforma deadline alerts to the responsible approvers (area cascade /
/// alternative final approvers) through the e-mail outbox.
///
/// <para><b>Alert levels:</b> WARNING_3D, WARNING_1D, CRITICAL_0D, EXPIRED (see the cycle).</para>
/// <para><b>Schedule:</b> once per UTC day at <c>AppConfig:ProformaDeadlineAlerts:CheckTimeUtcHour</c>
/// (see <see cref="ProformaDeadlineSchedule"/>): startup catch-up when today's anchor passed without a
/// completed cycle, otherwise wait for the anchor; subsequent runs at the next anchor (drift-free);
/// every delay is cancellation-aware.</para>
/// <para><b>Delivery:</b> an alert record means "queued in EmailOutbox", never "delivered" — see
/// <see cref="ProformaDeadlineAlertCycle.DescribeDelivery"/>.</para>
///
/// Configuration section: <c>AppConfig:ProformaDeadlineAlerts</c> (Enabled, CheckTimeUtcHour,
/// ThresholdDays, OutboxExpiryHours; CheckIntervalHours is informational).
/// </summary>
public class ProformaDeadlineAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ProformaDeadlineAlertService> _logger;

    public ProformaDeadlineAlertService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ProformaDeadlineAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue<bool>("AppConfig:ProformaDeadlineAlerts:Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("[ProformaDeadlineAlerts] Service is DISABLED via configuration. Exiting.");
            return;
        }

        var checkTimeUtcHour = _config.GetValue<int>("AppConfig:ProformaDeadlineAlerts:CheckTimeUtcHour", 7);
        var legacyIntervalHours = _config.GetValue<int>("AppConfig:ProformaDeadlineAlerts:CheckIntervalHours", 24);

        _logger.LogInformation(
            "[ProformaDeadlineAlerts] Service started. Daily anchor: {CheckTimeUtcHour:00}:00 UTC (CheckIntervalHours={IntervalHours} is informational).",
            checkTimeUtcHour, legacyIntervalHours);

        // Short warm-up so the host finishes starting before the first database access.
        if (!await DelayAsync(TimeSpan.FromSeconds(30), stoppingToken)) { _logger.LogInformation("[ProformaDeadlineAlerts] Service stopped."); return; }

        var lastCycleUtc = await TryGetLastCycleUtcAsync(stoppingToken);
        var initialDelay = ProformaDeadlineSchedule.ComputeInitialDelay(DateTime.UtcNow, checkTimeUtcHour, lastCycleUtc);
        _logger.LogInformation("[ProformaDeadlineAlerts] Last recorded cycle: {LastCycle}. First run in {Delay}.",
            lastCycleUtc?.ToString("u") ?? "none", initialDelay);
        if (!await DelayAsync(initialDelay, stoppingToken)) { _logger.LogInformation("[ProformaDeadlineAlerts] Service stopped."); return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAlertCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[ProformaDeadlineAlerts] Unhandled error in alert cycle. Will retry at the next daily anchor.");
            }

            var nextDelay = ProformaDeadlineSchedule.ComputeNextDelay(DateTime.UtcNow, checkTimeUtcHour);
            _logger.LogInformation("[ProformaDeadlineAlerts] Next run in {Delay}.", nextDelay);
            if (!await DelayAsync(nextDelay, stoppingToken)) break;
        }

        _logger.LogInformation("[ProformaDeadlineAlerts] Service stopped.");
    }

    private async Task RunAlertCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("[ProformaDeadlineAlerts] Starting alert cycle at {UtcNow:u}.", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var adminLog = scope.ServiceProvider.GetRequiredService<AdminLogWriter>();

        var cycle = new ProformaDeadlineAlertCycle(context, notificationService, config, adminLog, _logger);
        await cycle.RunAsync(DateTime.UtcNow, ct);
    }

    /// <summary>Cancellation-aware delay. Returns false when the host is stopping.</summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return !ct.IsCancellationRequested;
        try { await Task.Delay(delay, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>
    /// UTC timestamp of the last completed cycle, read from the PROFORMA_DEADLINE_CYCLE admin-log
    /// event every cycle writes. Best-effort: a read failure is logged and treated as "no cycle".
    /// </summary>
    private async Task<DateTime?> TryGetLastCycleUtcAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await context.AdminLogEntries.AsNoTracking()
                .Where(a => a.EventType == "PROFORMA_DEADLINE_CYCLE")
                .MaxAsync(a => (DateTime?)a.TimestampUtc, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[ProformaDeadlineAlerts] Could not read the last cycle timestamp; assuming none.");
            return null;
        }
    }
}
