# SCR-32 — OCR Log Detail with Safe Payload

> **Status**: Placeholder — requires manual capture

## Screenshot Details

| Field | Value |
|:---|:---|
| **Screenshot ID** | SCR-32 |
| **Description** | Detail view of an OCR log entry showing safe payload |
| **URL/Page** | Portal → Settings → System Logs → [click on OCR event] |
| **Role Required** | Admin |
| **Evidence Type** | UI Screenshot |

## Setup

1. At least one OCR extraction must have been executed

## Capture Steps

1. Log in as Admin
2. Navigate to Settings → System Logs
3. Filter for OCR events
4. Click on an `OCR_EXTRACTION_COMPLETED` event
5. Screenshot the detail panel showing:
   - Event type: `OCR_EXTRACTION_COMPLETED`
   - Payload with metadata fields (tokens, model, quality score)
   - **No raw AI response content**
   - **No API key or secrets**
6. Verify `SafePayload` masking is applied (sensitive fields show `[REDACTED]`)

## Expected Visible Result

```json
{
  "fileName": "...",
  "provider": "OPENAI",
  "model": "gpt-4-turbo",
  "totalTokens": 1700,
  "qualityScore": 0.87,
  "durationMs": 3420
}
```

## Masking Requirements

- Verify no API keys or secrets in payload
- Verify no raw document content
- Mask real file names if they contain sensitive info

## Alternative Evidence

- Log sample: [`OCR_EXTRACTION_COMPLETED-sanitized.json`](../logs/OCR_EXTRACTION_COMPLETED-sanitized.json)
- Code reference: [`G8-system-logs-integration.md`](../code-references/G8-system-logs-integration.md)
