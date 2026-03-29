namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Persisted global settings for document extraction.
/// These settings override appsettings.json when present in the database.
/// All fields are nullable to allow for field-level fallback to configuration.
/// </summary>
public class DocumentExtractionSettings
{
    public int Id { get; set; }

    // Global Settings
    public string? DefaultProvider { get; set; }
    public bool? IsEnabled { get; set; }
    public int? GlobalTimeoutSeconds { get; set; }

    // Local OCR Settings
    public bool? LocalOcrEnabled { get; set; }
    public string? LocalOcrBaseUrl { get; set; }
    public int? LocalOcrTimeoutSeconds { get; set; }

    // OpenAI Settings (Operational flags only, no secrets)
    public bool? OpenAiEnabled { get; set; }
    public string? OpenAiModel { get; set; }
    public int? OpenAiTimeoutSeconds { get; set; }

    // Azure Settings (Operational flags only, no secrets)
    public bool? AzureDocumentIntelligenceEnabled { get; set; }
    public int? AzureDocumentIntelligenceTimeoutSeconds { get; set; }

    // Audit
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
