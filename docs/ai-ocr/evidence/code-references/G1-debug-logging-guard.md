# G1 Evidence — Debug Logging Guard

## Code Reference

### `IsDebugLoggingAllowed()` Method

**File**: [`OpenAiDocumentExtractionProvider.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/OpenAiDocumentExtractionProvider.cs#L84-L88)

```csharp
/// <summary>G1: Returns true only when both environment is Development AND config flag is true.</summary>
private bool IsDebugLoggingAllowed(DocumentExtractionOptions options)
{
    return _hostEnvironment.IsDevelopment() && options.DebugRawPayloadLogging;
}
```

### Dual Guard Logic

Both conditions must be `true` for raw payloads to be written to disk:

1. **`_hostEnvironment.IsDevelopment()`** — Checks `ASPNETCORE_ENVIRONMENT=Development`
2. **`options.DebugRawPayloadLogging`** — Config flag from `appsettings.json` (default: `false`)

### Guard Usage Points

| Location | Purpose | Line |
|:---|:---|:---|
| JSON response debug write | Prevents raw AI JSON responses from being saved to `debug/openai-json/` | ~L320 |
| Rasterized image debug write | Prevents rasterized document images from being saved to `debug/openai-rasterized/` | ~L580 |

### Non-Debug Path: Metadata-Only Logging

When debug logging is not allowed, the system logs **metadata only** via structured logging:

```csharp
_logger.LogInformation("[G1-DEBUG] Debug logging disabled for extraction of '{FileName}'. " +
    "Hash: {Hash}, Size: {Size} bytes.", fileName, documentHash, fileStream.Length);
```

**No raw content, no AI responses, no document images** are logged in non-debug mode.

### Configuration Key

| Key | Default | Purpose |
|:---|:---|:---|
| `DocumentExtraction:DebugRawPayloadLogging` | `false` | Must be explicitly set to `true` AND environment must be Development |

### Evidence Files

- Configuration: [`debug-raw-payload-logging-redacted.md`](../configuration/debug-raw-payload-logging-redacted.md)
- Log sample: [`G1-metadata-only-log-sample.json`](../logs/G1-metadata-only-log-sample.json)
