# G6 — Provider Endpoint Validation

> **Status**: Code-verified — switching requires Corporate IT decision

## What Was Validated

| Check | Method | Result |
|:---|:---|:---|
| `Endpoint` config key exists | `appsettings.json` inspection | ✅ Verified |
| `ResolveApiUrl()` reads from config | Code inspection | ✅ Verified |
| Empty endpoint falls back to default OpenAI | Code inspection | ✅ Verified |
| Connection test uses configured endpoint | Code inspection | ✅ Verified |
| `DeploymentName` config key exists | `appsettings.json` inspection | ✅ Verified |
| `IDocumentExtractionProvider` interface in place | Code inspection | ✅ Verified |

## Provider Switch Test

> [!WARNING]
> **Not executed live.** Switching to Azure OpenAI requires a valid Azure OpenAI resource and deployment, which requires Corporate IT approval.

## Manual Validation Instructions

1. Set `DocumentExtraction:OpenAI:Endpoint` to a test URL (e.g., `https://httpbin.org/post`)
2. Navigate to Settings → Document Extraction → Test Connection
3. Verify the connection test targets the configured URL (not `api.openai.com`)
4. Restore empty endpoint after testing

## Conclusion

Provider endpoint is configurable. Switching from direct OpenAI to Azure OpenAI (or any compatible endpoint) requires only a configuration change.
