# SCR-31 — System Logs OCR Filter

> **Status**: Placeholder — requires manual capture

## Screenshot Details

| Field | Value |
|:---|:---|
| **Screenshot ID** | SCR-31 |
| **Description** | System Logs page filtered to show OCR events |
| **URL/Page** | Portal → Settings → System Logs |
| **Role Required** | Admin |
| **Evidence Type** | UI Screenshot |

## Setup

1. At least one OCR extraction must have been executed to produce log entries
2. Alternatively: Use SQL to verify `AdminLogEntries` contain `OCR_*` events

## Capture Steps

1. Log in as Admin
2. Navigate to Settings → System Logs
3. Filter by source containing "Extraction" or event type containing "OCR"
4. Screenshot showing OCR events in the log list:
   - `OCR_EXTRACTION_STARTED`
   - `OCR_EXTRACTION_COMPLETED`
   - Any other `OCR_*` events
5. Click on one event to show detail panel
6. Screenshot showing the payload does NOT contain raw AI responses or API keys

## Expected Visible Result

- Log entries with `OCR_*` event types
- Timestamps, user, source, level visible
- Payload shows metadata only (file name, model, tokens) — no raw content

## Masking Requirements

- Mask user emails if real users
- Verify no API keys in payload

## Alternative Evidence

- SQL query: [`ocr_system_log_events.sql`](../sql/ocr_system_log_events.sql)
- Log samples: [`OCR_EXTRACTION_STARTED-sanitized.json`](../logs/OCR_EXTRACTION_STARTED-sanitized.json)
