namespace AlplaPortal.Application.DTOs.Extraction;

public class DocumentExtractionSettingsDto
{
    public string DefaultProvider { get; set; } = "OPENAI";
    public bool IsEnabled { get; set; } = true;
    public int GlobalTimeoutSeconds { get; set; } = 30;

    // OpenAI
    public bool OpenAiEnabled { get; set; } = true;
    public string? OpenAiModel { get; set; }
    public int? OpenAiTimeoutSeconds { get; set; }

    // Azure Document Intelligence
    public bool AzureDocumentIntelligenceEnabled { get; set; } = false;
    public int? AzureDocumentIntelligenceTimeoutSeconds { get; set; }
}
