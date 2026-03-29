namespace AlplaPortal.Application.DTOs.Extraction;

public class DocumentExtractionSettingsDto
{
    public string DefaultProvider { get; set; } = "LOCAL_OCR";
    public bool IsEnabled { get; set; } = true;
    public int GlobalTimeoutSeconds { get; set; } = 30;

    // Local OCR
    public bool LocalOcrEnabled { get; set; } = true;
    public string? LocalOcrBaseUrl { get; set; }
    public int? LocalOcrTimeoutSeconds { get; set; }

    // OpenAI
    public bool OpenAiEnabled { get; set; } = false;
    public string? OpenAiModel { get; set; }
    public int? OpenAiTimeoutSeconds { get; set; }

    // Azure Document Intelligence
    public bool AzureDocumentIntelligenceEnabled { get; set; } = false;
    public int? AzureDocumentIntelligenceTimeoutSeconds { get; set; }
}
