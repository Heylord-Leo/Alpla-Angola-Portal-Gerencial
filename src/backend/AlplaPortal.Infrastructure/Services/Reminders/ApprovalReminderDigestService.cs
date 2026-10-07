using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Reminders;

/// <summary>
/// Hosted scheduler for <see cref="ApprovalReminderDigestCycle"/>: once per Luanda business day at
/// the configured local time. On (re)start it catches up only when today is a business day, the
/// send time has passed and no run row exists for today's local date. Does nothing while
/// <see cref="ApprovalReminderOptions.Enabled"/> is false (the default).
/// </summary>
public sealed class ApprovalReminderDigestService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ApprovalReminderOptions> _options;
    private readonly ILogger<ApprovalReminderDigestService> _logger;

    public ApprovalReminderDigestService(IServiceScopeFactory scopeFactory, IOptionsMonitor<ApprovalReminderOptions> options, ILogger<ApprovalReminderDigestService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            _logger.LogInformation("[ApprovalReminders] Disabled (AppConfig:ApprovalReminders:Enabled=false). Service idle.");
            return;
        }

        var zone = ApprovalReminderSchedule.ResolveZone(opts.TimeZoneId);
        _logger.LogInformation("[ApprovalReminders] Enabled. DryRun={DryRun}, send time {Time} ({Zone}), min age > {Days} days, business days {Days2}.",
            opts.DryRun, opts.SendTimeLocal, zone.Id, opts.MinPendingAgeDays, string.Join(",", opts.BusinessDays));

        if (!await DelayAsync(TimeSpan.FromSeconds(30), stoppingToken)) return;

        var lastRunLocalDate = await TryGetLastRunLocalDateAsync(stoppingToken);
        var initialDelay = ApprovalReminderSchedule.ComputeInitialDelay(DateTime.UtcNow, opts, zone, lastRunLocalDate);
        _logger.LogInformation("[ApprovalReminders] Last recorded run date: {Last}. First run in {Delay}.", lastRunLocalDate?.ToString("yyyy-MM-dd") ?? "none", initialDelay);
        if (!await DelayAsync(initialDelay, stoppingToken)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var cycle = scope.ServiceProvider.GetRequiredService<ApprovalReminderDigestCycle>();
                await cycle.RunAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[ApprovalReminders] Unhandled error in digest cycle. Will retry at the next scheduled send.");
            }

            var next = ApprovalReminderSchedule.ComputeNextRunUtc(DateTime.UtcNow, _options.CurrentValue, zone) - DateTime.UtcNow;
            _logger.LogInformation("[ApprovalReminders] Next run in {Delay}.", next);
            if (!await DelayAsync(next, stoppingToken)) break;
        }

        _logger.LogInformation("[ApprovalReminders] Service stopped.");
    }

    private async Task<DateTime?> TryGetLastRunLocalDateAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await context.ApprovalReminderRuns.AsNoTracking()
                .Where(r => r.Trigger == ApprovalReminderDigestCycle.TriggerScheduled)
                .OrderByDescending(r => r.LocalDate)
                .Select(r => (DateTime?)r.LocalDate)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[ApprovalReminders] Could not read the last run; assuming none.");
            return null;
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return !ct.IsCancellationRequested;
        try { await Task.Delay(delay, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }
}
