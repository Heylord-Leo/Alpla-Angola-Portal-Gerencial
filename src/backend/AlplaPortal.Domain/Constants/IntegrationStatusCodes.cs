namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Stable, machine-readable status codes for integration provider connection state.
/// Frontend uses separate display labels — logic must rely on these codes only.
///
/// These codes are persisted in IntegrationConnectionStatus.CurrentStatus.
/// They must remain backward-compatible once deployed.
/// </summary>
public static class IntegrationStatusCodes
{
    /// <summary>Provider connection is healthy and responsive.</summary>
    public const string Healthy = "HEALTHY";

    /// <summary>Provider is configured but the connection is failing.</summary>
    public const string Unhealthy = "UNHEALTHY";

    /// <summary>Provider is configured but the target is unreachable (network/timeout).</summary>
    public const string Unreachable = "UNREACHABLE";

    /// <summary>Provider exists but connection settings are not yet configured.</summary>
    public const string NotConfigured = "NOT_CONFIGURED";

    /// <summary>Provider is registered as a future/roadmap item — no implementation exists yet.</summary>
    public const string Planned = "PLANNED";
}
