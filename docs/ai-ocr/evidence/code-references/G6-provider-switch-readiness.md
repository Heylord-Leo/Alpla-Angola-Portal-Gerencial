# G6 Evidence — Provider Switch Readiness

## Code Reference

### Configurable API Endpoint

**File**: [`OpenAiDocumentExtractionProvider.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/OpenAiDocumentExtractionProvider.cs#L76-L82)

```csharp
/// <summary>G6: Resolves API URL — uses configured endpoint if set, otherwise default OpenAI.</summary>
private string ResolveApiUrl(OpenAiSettings settings)
{
    if (string.IsNullOrWhiteSpace(settings.Endpoint))
        return DefaultApiBaseUrl;  // "https://api.openai.com/v1/chat/completions"
    return settings.Endpoint.TrimEnd('/');
}
```

### Configurable Connection Test

**File**: [`DocumentExtractionSettingsService.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionSettingsService.cs)

The connection test endpoint reads from the same `Endpoint` configuration:
```csharp
var testUrl = string.IsNullOrWhiteSpace(openAiSettings.Endpoint)
    ? "https://api.openai.com/v1/models"
    : openAiSettings.Endpoint.TrimEnd('/') + "/models";
```

### Provider Abstraction

**Interface**: [`IDocumentExtractionProvider.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Application/Interfaces/Extraction/IDocumentExtractionProvider.cs)

```csharp
public interface IDocumentExtractionProvider
{
    string Name { get; }
    Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, ...);
}
```

### How to Switch to Azure OpenAI

| Step | Action |
|:---|:---|
| 1 | Set `DocumentExtraction:OpenAI:Endpoint` to Azure OpenAI URL |
| 2 | Set `DocumentExtraction:OpenAI:DeploymentName` if needed |
| 3 | Update API key in DB or `OPENAI_API_KEY` env var |
| 4 | Connection test will use the new endpoint automatically |
| **OR** | Implement `AzureOpenAiDocumentExtractionProvider : IDocumentExtractionProvider` for Azure-specific auth |

### Configuration

| Key | Default | Purpose |
|:---|:---|:---|
| `DocumentExtraction:OpenAI:Endpoint` | `""` (uses `api.openai.com`) | Custom endpoint URL |
| `DocumentExtraction:OpenAI:DeploymentName` | `""` | Azure deployment name |
| `DocumentExtraction:OpenAI:Model` | `"gpt-4-turbo"` | Model selection |

### Corporate IT Decision Pending

> [!IMPORTANT]
> The decision between direct OpenAI API and Azure OpenAI is pending Corporate IT approval. The provider abstraction ensures either path can be implemented with configuration or minimal code changes.

### Evidence Files

- Configuration: [`provider-endpoint-redacted.md`](../configuration/provider-endpoint-redacted.md)
- Test result: [`G6-provider-endpoint-validation.md`](../test-results/G6-provider-endpoint-validation.md)
