using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// G4: Background cleanup service for OCR debug artifacts and expired data.
/// Runs daily when AutoCleanupEnabled is true. Disabled by default until
/// Legal/AI CoE confirms retention requirements.
/// 
/// Cleanup targets:
/// - Debug files in debug/openai-json/ and debug/openai-rasterized/ (DebugFileRetentionDays)
/// - Does NOT delete official audit logs (AdminLogEntry)
/// - Does NOT delete field-level audit trail
/// </summary>
public class OcrCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OcrCleanupService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    // Known debug folders — these contain only debugging artifacts, never official data
    private static readonly string[] DebugFolders = new[]
    {
        @"C:\dev\alpla-portal\debug\openai-json",
        @"C:\dev\alpla-portal\debug\openai-rasterized"
    };

    public OcrCleanupService(IServiceScopeFactory scopeFactory, ILogger<OcrCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[OcrCleanupService] Started. Checking every {Interval} hours.", Interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OcrCleanupService] Unexpected error during cleanup cycle.");
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider
            .GetRequiredService<Application.Interfaces.Extraction.IDocumentExtractionSettingsService>();
        var adminLogWriter = scope.ServiceProvider.GetRequiredService<AdminLogWriter>();

        var options = await settingsService.GetEffectiveSettingsAsync(ct);
        var retention = options.Retention;

        if (!retention.AutoCleanupEnabled)
        {
            _logger.LogDebug("[OcrCleanupService] AutoCleanupEnabled is false. Skipping cleanup.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int totalFilesDeleted = 0;
        var errors = new List<string>();

        foreach (var folder in DebugFolders)
        {
            if (!Directory.Exists(folder))
                continue;

            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-retention.DebugFileRetentionDays);
                var files = Directory.GetFiles(folder)
                    .Select(f => new FileInfo(f))
                    .Where(f => f.LastWriteTimeUtc < cutoff)
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        file.Delete();
                        totalFilesDeleted++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{folder}: {ex.Message}");
            }
        }

        sw.Stop();

        var eventType = errors.Count > 0 ? "OCR_CLEANUP_FAILED" : "OCR_CLEANUP_EXECUTED";
        var level = errors.Count > 0 ? "Warning" : "Information";

        await adminLogWriter.WriteAsync(level, nameof(OcrCleanupService), eventType,
            $"OCR cleanup completed. Files deleted: {totalFilesDeleted}. Errors: {errors.Count}.",
            exceptionDetail: errors.Count > 0 ? string.Join("; ", errors.Take(5)) : null,
            payload: SafePayload.From(new
            {
                debugFilesDeleted = totalFilesDeleted,
                rawJsonRecordsCleaned = 0, // Reserved for future DB cleanup
                durationMs = sw.ElapsedMilliseconds,
                retentionDays = retention.DebugFileRetentionDays,
                errorCount = errors.Count
            }));

        _logger.LogInformation("[OcrCleanupService] Cleanup completed in {Duration}ms. Files deleted: {Deleted}. Errors: {Errors}.",
            sw.ElapsedMilliseconds, totalFilesDeleted, errors.Count);
    }
}
