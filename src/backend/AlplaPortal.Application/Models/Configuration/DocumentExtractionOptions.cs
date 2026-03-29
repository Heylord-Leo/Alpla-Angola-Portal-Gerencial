namespace AlplaPortal.Application.Models.Configuration;

public class DocumentExtractionOptions
{
    public string DefaultProvider { get; set; } = "LOCAL_OCR";
    public bool IsEnabled { get; set; } = true;
    public int GlobalTimeoutSeconds { get; set; } = 30;

    public ProviderSettings LocalOcr { get; set; } = new();
    public OpenAiSettings OpenAi { get; set; } = new();
    public AzureSettings AzureDocumentIntelligence { get; set; } = new();
}

public class ProviderSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public int? TimeoutSeconds { get; set; }
}

public class OpenAiSettings
{
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
