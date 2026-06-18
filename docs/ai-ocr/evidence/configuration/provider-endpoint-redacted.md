# Configuration Evidence — Provider Endpoint (G6)

> **Source**: `appsettings.json` lines 49–55
> **Config Key**: `DocumentExtraction:OpenAI`

## Configuration

```json
"OpenAi": {
  "Enabled": true,
  "TimeoutSeconds": 60,
  "Model": "gpt-4-turbo",
  "DeploymentName": "",
  "Endpoint": ""
}
```

## Field Descriptions

| Key | Default | Purpose |
|:---|:---|:---|
| `Enabled` | `true` | Provider is active |
| `TimeoutSeconds` | `60` | Per-request timeout |
| `Model` | `"gpt-4-turbo"` | AI model selection |
| `DeploymentName` | `""` | Azure OpenAI deployment name (unused for direct OpenAI) |
| `Endpoint` | `""` | Custom API URL; empty = `https://api.openai.com/v1/chat/completions` |

## Endpoint Resolution Logic

```
IF Endpoint is empty or whitespace
    USE "https://api.openai.com/v1/chat/completions" (default)
ELSE
    USE configured Endpoint value (trimmed)
```

## Provider Switch Examples

| Provider | Endpoint Value |
|:---|:---|
| Direct OpenAI (current) | `""` (empty — uses default) |
| Azure OpenAI | `"https://your-resource.openai.azure.com/openai/deployments/your-deployment/chat/completions?api-version=2024-02-01"` |
| Corporate proxy | `"https://ai-gateway.alpla.internal/v1/chat/completions"` |

## Security Notes

- **No API keys** in this configuration section
- API key resolved separately via `IntegrationConfigResolver` (DB encrypted or env var)
- Connection test automatically uses the configured endpoint
