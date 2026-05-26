namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Generic registry of external integration platforms/providers.
/// Each provider represents an external system the Portal can connect to.
///
/// Designed to be provider-agnostic — supports SQL-based ERPs, biometric systems,
/// REST APIs, and future integration targets without domain-specific coupling.
///
/// The Capabilities field uses a lightweight JSON array approach for Phase 0.
/// If capability management becomes complex, this may evolve into a relational
/// structure in a future phase (see DECISIONS.md).
/// </summary>
public class IntegrationProvider
{
    public int Id { get; set; }

    /// <summary>Unique machine-readable code (e.g., PRIMAVERA, INNUX, ALPLAPROD).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider category: ERP, BIOMETRIC, PRODUCTION, API, OTHER.</summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>Connection transport: SQL, REST_API, SOAP, FILE.</summary>
    public string ConnectionType { get; set; } = string.Empty;

    /// <summary>Optional human-readable description of the provider's role.</summary>
    public string? Description { get; set; }

    /// <summary>Deployment environment: PRODUCTION, TEST, DEVELOPMENT.</summary>
    public string? Environment { get; set; }

    /// <summary>Whether the provider is operationally enabled for health checks and future data access.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Whether the provider is a planned/roadmap item not yet implemented.</summary>
    public bool IsPlanned { get; set; }

    /// <summary>UI display ordering (lower values appear first).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Lightweight JSON array of capability codes this provider supports.
    /// Example: ["EMPLOYEES","MATERIALS","SUPPLIERS","DEPARTMENTS"]
    ///
    /// Phase 0 initial approach — may evolve to relational structure if
    /// capability lifecycle management becomes complex.
    /// </summary>
    public string? Capabilities { get; set; }

    // Audit
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public IntegrationConnectionStatus? ConnectionStatus { get; set; }
    public IntegrationProviderSettings? Settings { get; set; }
}
