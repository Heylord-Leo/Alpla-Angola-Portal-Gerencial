# Configuration Evidence — Retention Policy (G4)

> **Source**: `appsettings.json` lines 44–48
> **Config Key**: `DocumentExtraction:Retention`

## Configuration

```json
"Retention": {
  "DebugFileRetentionDays": 7,
  "RawJsonResultRetentionDays": 90,
  "AutoCleanupEnabled": false
}
```

## Field Descriptions

| Key | Default | Purpose | Legal Dependency |
|:---|:---|:---|:---|
| `DebugFileRetentionDays` | `7` | Days before old debug files are deleted | None (debug only) |
| `RawJsonResultRetentionDays` | `90` | Days before DB raw JSON cleanup (future) | Yes — requires Legal confirmation |
| `AutoCleanupEnabled` | `false` | Master switch for cleanup job | Yes — disabled until Legal confirms |

## Why Cleanup Is Disabled by Default

The `OcrCleanupService` is implemented and registered but will not execute cleanup until `AutoCleanupEnabled` is set to `true`. This is intentional:

1. **Legal must confirm** acceptable retention periods for AI extraction records
2. **AI CoE must confirm** whether raw AI responses must be retained for audit
3. **Data Owner must confirm** data lifecycle requirements

> [!IMPORTANT]
> Do not enable `AutoCleanupEnabled` without Legal/AI CoE approval.
