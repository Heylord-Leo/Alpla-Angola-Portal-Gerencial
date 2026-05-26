namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Runtime diagnostic state for an integration provider.
/// Tracks connection health, test results, and failure history.
///
/// One-to-one with IntegrationProvider — each provider has exactly one status record.
/// The record is created by seed/migration and updated at runtime by health checks.
/// </summary>
public class IntegrationConnectionStatus
{
    public int Id { get; set; }

    /// <summary>FK to the owning integration provider.</summary>
    public int IntegrationProviderId { get; set; }
    public IntegrationProvider Provider { get; set; } = null!;

    /// <summary>
    /// Machine-readable status code. Must use values from IntegrationStatusCodes.
    /// Values: HEALTHY, UNHEALTHY, UNREACHABLE, NOT_CONFIGURED, PLANNED.
    /// </summary>
    public string CurrentStatus { get; set; } = IntegrationStatusCodes.NotConfigured;

    /// <summary>Timestamp of the last successful connection test.</summary>
    public DateTime? LastSuccessUtc { get; set; }

    /// <summary>Timestamp of the last failed connection test.</summary>
    public DateTime? LastFailureUtc { get; set; }

    /// <summary>Response time of the last connection test in milliseconds.</summary>
    public int? LastResponseTimeMs { get; set; }

    /// <summary>Error message from the most recent failure.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Number of consecutive failures since last success.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Email of the user who last triggered a manual connection test.</summary>
    public string? LastTestedByEmail { get; set; }

    /// <summary>
    /// Timestamp of the last connection test execution, regardless of result.
    /// Answers "when was this provider last actively checked?"
    /// </summary>
    public DateTime? LastCheckedAtUtc { get; set; }
}
