namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// G5: Extension point for malware/AV scanning of uploaded files.
/// Real implementations should integrate with an enterprise AV solution
/// (e.g., ClamAV, Windows Defender ATP, Azure Defender) before files
/// are processed by AI OCR or stored permanently.
/// </summary>
public interface IFileScanService
{
    Task<FileScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken ct = default);
}

/// <summary>
/// Result of a file malware scan.
/// </summary>
/// <param name="IsClean">True if no threats were detected.</param>
/// <param name="ThreatName">Name of the detected threat, if any.</param>
/// <param name="ScannerName">Name of the scanner that performed the scan.</param>
public record FileScanResult(bool IsClean, string? ThreatName = null, string ScannerName = "NONE");
