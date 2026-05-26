namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Generic contract for an external integration provider.
///
/// Each concrete provider (Primavera, Innux, etc.) implements this interface
/// to register itself and expose connection testing capabilities.
///
/// This interface is intentionally minimal — it provides identity and connectivity only.
/// Business-domain operations (employee lookup, article sync, etc.) are NOT part of
/// this base contract. They should be defined in separate, domain-specific interfaces
/// layered on top of concrete provider implementations.
///
/// Pattern reference: follows the same strategy used by IDocumentExtractionProvider.
/// </summary>
public interface IIntegrationProvider
{
    /// <summary>Unique machine-readable code matching IntegrationProvider.Code (e.g., "PRIMAVERA").</summary>
    string Code { get; }

    /// <summary>Provider category matching IntegrationProvider.ProviderType (e.g., "ERP").</summary>
    string ProviderType { get; }

    /// <summary>Connection transport matching IntegrationProvider.ConnectionType (e.g., "SQL").</summary>
    string ConnectionType { get; }

    /// <summary>
    /// Tests connectivity to the external provider.
    /// Must be a lightweight, read-only probe — no data mutation.
    /// </summary>
    Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of a connection test against an external integration provider.
/// </summary>
public class IntegrationConnectionTestResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? ResponseTimeMs { get; set; }
}
