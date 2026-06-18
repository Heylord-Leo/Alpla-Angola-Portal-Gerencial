# Configuration Evidence — Debug Raw Payload Logging (G1)

> **Source**: `appsettings.json` line 36
> **Config Key**: `DocumentExtraction:DebugRawPayloadLogging`

## Default Value

```json
"DebugRawPayloadLogging": false
```

## Environment Applicability

| Environment | `IsDevelopment()` | Config Default | Debug Files Written? |
|:---|:---|:---|:---|
| **Development** | `true` | `false` | ❌ No (flag is false) |
| **Development** | `true` | `true` (if explicitly set) | ✅ Yes |
| **TEST** | `false` | `false` | ❌ No (env check fails) |
| **TEST** | `false` | `true` (even if set) | ❌ No (env check fails) |
| **PROD** | `false` | `false` | ❌ No (env check fails) |
| **PROD** | `false` | `true` (even if set) | ❌ No (env check fails) |

## Compliance Purpose

Ensures that raw AI request/response payloads (which may contain document content, extracted text, or AI-generated JSON) are **never written to disk** in TEST or PRODUCTION environments, regardless of configuration.

## Guard Implementation

```csharp
private bool IsDebugLoggingAllowed(DocumentExtractionOptions options)
{
    return _hostEnvironment.IsDevelopment() && options.DebugRawPayloadLogging;
}
```

Both conditions must be `true`:
1. `ASPNETCORE_ENVIRONMENT=Development`
2. `DebugRawPayloadLogging=true`
