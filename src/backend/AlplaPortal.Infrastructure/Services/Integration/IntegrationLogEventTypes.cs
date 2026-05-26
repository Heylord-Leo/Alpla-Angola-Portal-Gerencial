namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Standard event type constants for integration-related admin log entries.
/// Used consistently across all integration providers via AdminLogWriter.
///
/// These constants become the standard for current and future providers.
/// New providers should reuse these types rather than creating one-off event names.
/// </summary>
public static class IntegrationLogEventTypes
{
    /// <summary>A manual connection test was initiated for a provider.</summary>
    public const string ConnectionTestStarted = "INTEGRATION_CONNECTION_TEST_STARTED";

    /// <summary>A connection test completed successfully.</summary>
    public const string ConnectionTestOk = "INTEGRATION_CONNECTION_TEST_OK";

    /// <summary>A connection test failed.</summary>
    public const string ConnectionTestFailed = "INTEGRATION_CONNECTION_TEST_FAILED";

    /// <summary>The health summary for all providers was retrieved.</summary>
    public const string HealthRetrieved = "INTEGRATION_HEALTH_RETRIEVED";

    /// <summary>Integration settings were updated for a provider.</summary>
    public const string SettingsUpdated = "INTEGRATION_SETTINGS_UPDATED";

    /// <summary>Common source identifier for integration log entries.</summary>
    public const string LogSource = "IntegrationHealth";
}
