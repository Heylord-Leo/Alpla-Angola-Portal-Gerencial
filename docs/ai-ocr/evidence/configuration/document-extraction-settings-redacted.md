# Configuration Evidence — DocumentExtraction Settings (Redacted)

> **Source**: `appsettings.json` lines 33–59
> **File**: [`appsettings.json`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Api/appsettings.json#L33-L59)

```json
"DocumentExtraction": {
  "DefaultProvider": "OPENAI",
  "GlobalTimeoutSeconds": 30,
  "DebugRawPayloadLogging": false,
  "AiOcrPolicy": {
    "RequireHumanConfirmation": true,
    "AllowedModules": [ "CONTRACTS", "REQUESTS" ],
    "AllowedDocumentTypes": [ ".pdf", ".jpg", ".jpeg", ".png" ],
    "AllowedRoles": [],
    "BlockHighRiskDocuments": true
  },
  "Retention": {
    "DebugFileRetentionDays": 7,
    "RawJsonResultRetentionDays": 90,
    "AutoCleanupEnabled": false
  },
  "OpenAi": {
    "Enabled": true,
    "TimeoutSeconds": 60,
    "Model": "gpt-4-turbo",
    "DeploymentName": "",
    "Endpoint": ""
  },
  "AzureDocumentIntelligence": {
    "Enabled": false,
    "TimeoutSeconds": 60
  }
}
```

## Key Points

| Setting | Value | Compliance Purpose |
|:---|:---|:---|
| `DebugRawPayloadLogging` | `false` | G1: Raw payload logging disabled by default |
| `RequireHumanConfirmation` | `true` | G2: Human oversight enforced |
| `AllowedModules` | `["CONTRACTS", "REQUESTS"]` | G2: Only approved modules can use OCR |
| `AllowedDocumentTypes` | `[".pdf", ".jpg", ".jpeg", ".png"]` | G2: Only safe file types processed |
| `AutoCleanupEnabled` | `false` | G4: Cleanup disabled until Legal confirms |
| `Endpoint` | `""` (empty = default OpenAI) | G6: Configurable for Azure OpenAI switch |

## Security Notes

- **No API keys or secrets** appear in this configuration section
- API keys are stored encrypted in the database via `IntegrationConfigResolver`
- Fallback: `OPENAI_API_KEY` environment variable (never in config files)
