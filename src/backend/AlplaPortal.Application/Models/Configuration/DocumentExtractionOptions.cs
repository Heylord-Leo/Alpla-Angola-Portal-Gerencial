namespace AlplaPortal.Application.Models.Configuration;

public class DocumentExtractionOptions
{
    public string DefaultProvider { get; set; } = "OPENAI";
    public bool IsEnabled { get; set; } = true;
    public int GlobalTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// G1: When true AND environment is Development, raw AI request/response
    /// payloads are written to disk for debugging. Must NEVER be true in
    /// TEST or PROD environments. Default: false.
    /// </summary>
    public bool DebugRawPayloadLogging { get; set; } = false;

    /// <summary>G2: AI OCR policy controls — module, document type, role, and human-confirmation flags.</summary>
    public AiOcrPolicyOptions AiOcrPolicy { get; set; } = new();

    /// <summary>G4: Retention and cleanup policy for OCR artifacts and debug files.</summary>
    public RetentionPolicyOptions Retention { get; set; } = new();

    public OpenAiSettings OpenAi { get; set; } = new();
    public AzureSettings AzureDocumentIntelligence { get; set; } = new();
}

/// <summary>
/// G2: Explicit AI OCR policy controls. Enforced by DocumentExtractionService
/// and OCR-related controllers. All defaults are secure-by-default.
/// </summary>
public class AiOcrPolicyOptions
{
    /// <summary>When true, AI-extracted data must be confirmed by a human before final persistence.</summary>
    public bool RequireHumanConfirmation { get; set; } = true;

    /// <summary>Modules allowed to trigger AI OCR extraction (e.g. CONTRACTS, REQUESTS).</summary>
    public List<string> AllowedModules { get; set; } = new() { "CONTRACTS", "REQUESTS" };

    /// <summary>File extensions allowed for AI OCR processing.</summary>
    public List<string> AllowedDocumentTypes { get; set; } = new() { ".pdf", ".jpg", ".jpeg", ".png" };

    /// <summary>
    /// Roles allowed to trigger AI OCR. Empty list = all authenticated users
    /// with existing permissions. Non-empty = user must have at least one role.
    /// Enforced at controller level to avoid service-layer coupling with HttpContext.
    /// </summary>
    public List<string> AllowedRoles { get; set; } = new();

    /// <summary>When true, documents classified as high-risk are blocked from extraction.</summary>
    public bool BlockHighRiskDocuments { get; set; } = true;
}

/// <summary>
/// G4: Retention and cleanup policy for OCR debug artifacts and raw JSON results.
/// AutoCleanupEnabled is false by default — must remain disabled until Legal/AI CoE
/// confirms retention requirements.
/// </summary>
public class RetentionPolicyOptions
{
    /// <summary>Number of days to retain debug files (rasterized images, raw JSON). Default: 7.</summary>
    public int DebugFileRetentionDays { get; set; } = 7;

    /// <summary>Number of days to retain raw JSON extraction results in DB. Default: 90.</summary>
    public int RawJsonResultRetentionDays { get; set; } = 90;

    /// <summary>
    /// When true, the OcrCleanupService will actively delete expired artifacts.
    /// Must be false until Legal/AI CoE approves retention policy.
    /// </summary>
    public bool AutoCleanupEnabled { get; set; } = false;
}

public class ProviderSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public int? TimeoutSeconds { get; set; }
}

public class OpenAiSettings
{
    /// <summary>
    /// G6: Configurable API base URL. Empty = default OpenAI (api.openai.com).
    /// Set to Azure OpenAI or ALPLA-approved endpoint base URL when required.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public int? TimeoutSeconds { get; set; }
    public string Model { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
}

public class AzureSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public int? TimeoutSeconds { get; set; }
}
