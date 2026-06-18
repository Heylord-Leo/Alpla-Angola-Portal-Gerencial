# SCR-28 — Debug Logging Disabled Evidence

> **Status**: Placeholder — requires manual capture

## Screenshot Details

| Field | Value |
|:---|:---|
| **Screenshot ID** | SCR-28 |
| **Description** | Evidence that debug logging is disabled in non-Development environments |
| **URL/Page** | `appsettings.json` or File System check |
| **Role Required** | Developer / DevOps |
| **Evidence Type** | Configuration + File System |

## Setup

1. Deploy to TEST environment
2. Trigger at least one OCR extraction

## Capture Steps

1. Navigate to `debug/openai-json/` directory on the TEST server
2. Screenshot showing the directory is **empty** (no debug files)
3. Open `appsettings.json` and screenshot `DebugRawPayloadLogging: false`
4. Optionally: show server environment variable `ASPNETCORE_ENVIRONMENT=Production` or `Test`

## Expected Visible Result

- `debug/openai-json/` directory is empty or does not exist
- `DebugRawPayloadLogging` is `false` in config
- No raw AI response files on disk

## Masking Requirements

- None (no sensitive data in empty directory or config flag)

## Alternative Evidence (if screenshot not available)

- Configuration excerpt: [`debug-raw-payload-logging-redacted.md`](../configuration/debug-raw-payload-logging-redacted.md)
- Code reference: [`G1-debug-logging-guard.md`](../code-references/G1-debug-logging-guard.md)
- Log sample: [`G1-metadata-only-log-sample.json`](../logs/G1-metadata-only-log-sample.json)
