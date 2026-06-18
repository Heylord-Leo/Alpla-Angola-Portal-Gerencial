# G8 Evidence — System Logs Integration

## Code Reference

### AdminLogWriter Integration

AI OCR events are emitted through the existing `AdminLogWriter` infrastructure:

**File**: [`AdminLogWriter.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Logging/AdminLogWriter.cs)

`AdminLogWriter` writes structured events to `AdminLogEntry` (SQL Server table). It uses `SafePayload.From()` to mask sensitive fields before persistence.

### SafePayload Sanitization

**File**: [`SafePayload.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Logging/SafePayload.cs)

`SafePayload.From()` applies two-layer masking:
1. **Field-name masking**: Fields named `apiKey`, `token`, `secret`, `password` → `[REDACTED]`
2. **Regex redaction**: Patterns matching API keys, tokens, secrets → `[REDACTED]`

### OCR Event Types Emitted

| Event Type | Source File | Line | Severity | Trigger |
|:---|:---|:---|:---|:---|
| `OCR_EXTRACTION_STARTED` | `OpenAiDocumentExtractionProvider.cs` | L119 | Information | Extraction begins |
| `OCR_EXTRACTION_COMPLETED` | `OpenAiDocumentExtractionProvider.cs` | L443 | Information | Extraction succeeds |
| `OCR_EXTRACTION_FAILED` | `OpenAiDocumentExtractionProvider.cs` | ~L460 | Error | Provider error |
| `OCR_EXTRACTION_TIMEOUT` | `OpenAiDocumentExtractionProvider.cs` | catch block | Error | Request times out |
| `OCR_FEATURE_DISABLED` | `DocumentExtractionService.cs` | L36 | Warning | Global feature off |
| `OCR_MODULE_BLOCKED` | `DocumentExtractionService.cs` | L50 | Warning | Module not allowed |
| `OCR_DOCUMENT_TYPE_BLOCKED` | `DocumentExtractionService.cs` | L62 | Warning | Doc type not allowed |
| `OCR_CLEANUP_EXECUTED` | `OcrCleanupService.cs` | L118 | Information | Cleanup completes |
| `OCR_CLEANUP_FAILED` | `OcrCleanupService.cs` | L118 | Warning | Cleanup has errors |

### Payload Schema — OCR_EXTRACTION_STARTED

```json
{
  "fileName": "invoice-sample.pdf",
  "extension": ".pdf",
  "sourceContext": "Contracts",
  "provider": "OPENAI",
  "model": "gpt-4-turbo",
  "streamSize": 245780,
  "documentHash": "A1B2C3D4E5F67890",
  "invoicePromptVersion": "v2.1-hardened",
  "contractPromptVersion": "v2.1-hardened"
}
```

### Payload Schema — OCR_EXTRACTION_COMPLETED

```json
{
  "fileName": "contract-sample.pdf",
  "provider": "OPENAI",
  "model": "gpt-4-turbo",
  "strategy": "ContractTextFirst",
  "detailMode": "high",
  "promptVersion": "v2.1-hardened",
  "promptTokens": 1250,
  "completionTokens": 450,
  "totalTokens": 1700,
  "qualityScore": 0.87,
  "executionStatus": "Success",
  "durationMs": 3420,
  "responseSize": 1856
}
```

### Payload Schema — OCR_MODULE_BLOCKED

```json
{
  "fileName": "test.pdf",
  "module": "INVENTORY",
  "allowedModules": "CONTRACTS,REQUESTS",
  "reason": "MODULE_NOT_ALLOWED"
}
```

### SystemLogs.tsx Compatibility

**File**: [`SystemLogs.tsx`](file:///c:/dev/alpla-portal/src/frontend/src/pages/Settings/SystemLogs.tsx)

The existing System Logs UI uses a filter by `EventType` or `Source`. All `OCR_*` events are visible using:
- Filter by source: `OpenAiDocumentExtractionProvider` or `DocumentExtractionService`
- Filter by event type: `OCR_EXTRACTION_STARTED`, etc.
- The filter is a free-text search — no code changes needed for new event types.

### Evidence Files

- Log samples: See [`logs/`](../logs/) directory for all sanitized JSON samples
- Screenshot placeholder: [`SCR-31-system-logs-ocr-filter.md`](../screenshots/SCR-31-system-logs-ocr-filter.md)
