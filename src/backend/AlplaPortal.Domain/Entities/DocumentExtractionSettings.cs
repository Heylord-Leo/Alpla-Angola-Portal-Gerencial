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

    // Local OCR Settings — DEPRECATED (v2.128.0): LOCAL_OCR provider has been removed.
    // Columns are kept for EF Core mapping stability. Values are cleared on save.
    [Obsolete("LOCAL_OCR provider removed in v2.128.0. These columns are retained for DB compatibility only.")]
    public bool? LocalOcrEnabled { get; set; }
    [Obsolete("LOCAL_OCR provider removed in v2.128.0. These columns are retained for DB compatibility only.")]
    public string? LocalOcrBaseUrl { get; set; }
    [Obsolete("LOCAL_OCR provider removed in v2.128.0. These columns are retained for DB compatibility only.")]
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
