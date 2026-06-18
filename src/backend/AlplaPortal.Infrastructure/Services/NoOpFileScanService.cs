using AlplaPortal.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// G5: No-op malware scanning implementation — placeholder until a real AV
/// solution is integrated. Logs a one-time warning at first usage.
/// 
/// IMPORTANT: This does NOT provide actual malware scanning protection.
/// Files are accepted without AV scan. Real malware scanning is recommended
/// before unrestricted production usage, especially for externally sourced files.
/// </summary>
public class NoOpFileScanService : IFileScanService
{
    private readonly ILogger<NoOpFileScanService> _logger;
    private static bool _warningLogged = false;
    private static readonly object _lock = new();

    public NoOpFileScanService(ILogger<NoOpFileScanService> logger)
    {
        _logger = logger;
    }

    public Task<FileScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        // Log warning once at first usage to avoid log noise (per user requirement Q3)
        if (!_warningLogged)
        {
            lock (_lock)
            {
                if (!_warningLogged)
                {
                    _logger.LogWarning(
                        "[G5-PLACEHOLDER] Malware scanning is not configured. Files are accepted without AV scan. " +
                        "Real malware scanning is recommended before unrestricted production usage.");
                    _warningLogged = true;
                }
            }
        }

        return Task.FromResult(new FileScanResult(IsClean: true, ThreatName: null, ScannerName: "NONE"));
    }
}
