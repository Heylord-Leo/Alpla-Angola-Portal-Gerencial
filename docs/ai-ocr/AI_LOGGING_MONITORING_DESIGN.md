# AI Logging and Monitoring Design — Portal Gerencial OCR Feature

> **Version**: 2.0 | **Date**: 2026-06-18 | **Status**: Post-Hardening (G1–G8 Applied)

---

## 1. Existing Audit Logging Infrastructure

### 1.1 AdminLogWriter

**Source**: [AdminLogWriter.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Logging/AdminLogWriter.cs)

| Feature | Implementation |
|:---|:---|
| Persistence | `AdminLogEntry` table in SQL Server |
| Fail-safe | Errors swallowed — logging never breaks request flow |
| User resolution | Server-side only (`ResolveUserEmail()`) — never trusts client payload |
| Correlation | `X-Correlation-ID` from `CorrelationIdMiddleware` |
| Sanitization | `SafePayload.Sanitize()` on messages and exception details |
| Scope isolation | Creates fresh `DbContext` scope to avoid conflicts |

### 1.2 AdminLogEntry Entity

**Source**: [AdminLogEntry.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/AdminLogEntry.cs)

| Field | Type | Description |
|:---|:---|:---|
| `Id` | int | Auto-increment PK |
| `TimestampUtc` | DateTime | Event timestamp |
| `Level` | string(20) | Information / Warning / Error |
| `Source` | string(256) | Component that produced the event |
| `EventType` | string(64) | Machine-readable code (e.g., `OCR_SETTINGS_SAVED`) |
| `Message` | string | Human-readable summary |
| `CorrelationId` | string(50) | Request correlation ID |
| `UserEmail` | string(256) | Server-resolved user email |
| `ExceptionDetail` | string? | Sanitized exception (errors only) |
| `Payload` | string? | Safe shaped payload (never raw bodies) |

### 1.3 LogEntry Entity

**Source**: [LogEntry.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/LogEntry.cs)

| Field | Type | Description |
|:---|:---|:---|
| `Id` | int | Auto-increment PK |
| `Timestamp` | DateTime | Event timestamp |
| `Level` | string(20) | Log level |
| `Source` | string(256) | Source component |
| `Message` | string | Log message |
| `UserEmail` | string(256) | User email |
| `Path` | string(2048) | Request path |
| `CorrelationId` | string(50) | Correlation ID |
| `Exception` | string? | Exception details |
| `Payload` | string? | Sanitized JSON or text |

### 1.4 CorrelationIdMiddleware

**Source**: [CorrelationIdMiddleware.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Logging/CorrelationIdMiddleware.cs)

- Accepts existing `X-Correlation-ID` or generates 12-char GUID
- Stored in `HttpContext.Items["CorrelationId"]`
- Returned in response header for client-side tracing

### 1.5 SafePayload Sanitization

**Source**: [SafePayload.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Logging/SafePayload.cs)

Two-layer approach:
1. **Field-name masking**: 14 known sensitive field names → `[REDACTED]`
2. **Regex redaction**: Bearer tokens, key/token/secret patterns, OpenAI key prefixes (`sk-`, `pk-`)

---

## 2. Contract OCR Audit Fields

### ContractOcrExtractionRecord

**Source**: [ContractOcrExtractionRecord.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/ContractOcrExtractionRecord.cs)

| Field | Purpose | Compliance Value |
|:---|:---|:---|
| `TriggeredByUserId` | Who initiated extraction | ✅ User accountability |
| `TriggeredAtUtc` | When extraction was triggered | ✅ Temporal audit |
| `ProcessedAtUtc` | When processing completed | ✅ Duration tracking |
| `Status` | PENDING → PROCESSING → COMPLETED / FAILED | ✅ Lifecycle tracking |
| `ProviderName` | AI provider used (e.g., "OPENAI") | ✅ Provider accountability |
| `RoutingStrategy` | How the document was processed | ✅ Processing transparency |
| `ChunkCount` | Number of chunks processed | ✅ Resource tracking |
| `TotalTokensUsed` | Token consumption | ✅ Cost tracking |
| `QualityScore` | Overall extraction quality (0.0–1.0) | ✅ Quality monitoring |
| `IsPartial` | Whether extraction was incomplete | ✅ Reliability tracking |
| `ConflictsDetected` | Whether conflicting values found | ✅ Data quality |
| `NativeTextDetected` | PDF text layer availability | ✅ Processing insight |
| `RawJsonResult` | Full LLM response (64KB max) | 🔶 Useful for debugging but needs retention policy |
| `ErrorMessage` | Error details if failed | ✅ Incident tracking |

### ContractOcrExtractedField

**Source**: [ContractOcrExtractedField.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/ContractOcrExtractedField.cs)

| Field | Purpose | Compliance Value |
|:---|:---|:---|
| `FieldName` | Which contract field was extracted | ✅ Field-level tracking |
| `RawExtractedValue` | Value as returned by AI | ✅ Original AI output preserved |
| `NormalisedValue` | Value after parsing/normalisation | ✅ Processing transparency |
| `ConfidenceScore` | Per-field confidence (0.0–1.0) | ✅ Quality tracking |
| `DisplayHint` | AUTO_FILL / SUGGESTION / REFERENCE_ONLY | ✅ UX behavior tracking |
| `ConfirmedByUser` | Whether user confirmed | ✅ Human oversight proof |
| `ConfirmedAtUtc` | When confirmed | ✅ Temporal audit |
| `ConfirmedByUserId` | Who confirmed | ✅ User accountability |
| `WasOverridden` | Whether user changed the value | ✅ Edit tracking |
| `FinalSavedValue` | Final value persisted | ✅ Decision trail |
| `DiscardedByUser` | Whether user rejected suggestion | ✅ Rejection tracking |

### Invoice OCR Audit Fields

**Source**: [OcrExtractedItem.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/OcrExtractedItem.cs)

| Field | Purpose | Compliance Value |
|:---|:---|:---|
| `ExtractionBatchId` | Groups items from same run | ✅ Batch tracking |
| `AttachmentId` | Source document link | ✅ Document traceability |
| `QualityScore` | Extraction quality (0–100) | ✅ Quality tracking |
| `ProviderName` | AI provider used | ✅ Provider tracking |
| `ExtractedAtUtc` | Extraction timestamp | ✅ Temporal audit |
| Immutable records | Never modified after creation | ✅ Audit integrity |

---

## 3. Existing Gaps in Logging

| # | Gap | Impact | Severity | Status |
|:---|:---|:---|:---|:---|
| G1 | No prompt template version tracking | Cannot trace which prompt produced specific results | Medium | ✅ Implemented — `InvoicePromptVersion` and `ContractPromptVersion` constants logged in `OCR_EXTRACTION_STARTED` payload |
| G2 | No per-request cost estimate (invoice flow) | Cannot monitor costs per module | Low | Pending |
| G3 | No explicit retry count field | Cannot track retry behavior | Low | Pending |
| G4 | No file page count field | Cannot correlate quality with document complexity | Low | Pending |
| G5 | No AI-specific event types standardized | Inconsistent event filtering | Medium | ✅ Implemented — `OCR_EXTRACTION_STARTED`, `OCR_EXTRACTION_COMPLETED`, `OCR_EXTRACTION_FAILED`, `OCR_EXTRACTION_TIMEOUT`, `OCR_FEATURE_DISABLED`, `OCR_MODULE_BLOCKED`, `OCR_DOCUMENT_TYPE_BLOCKED`, `OCR_CLEANUP_EXECUTED` events now emitted |
| G6 | Debug file writes not environment-guarded | Raw responses may leak to production disk | High | ✅ Implemented (G1) — `IsDebugLoggingAllowed()` dual guard |
| G7 | No formal AI incident log entity | AI-specific incidents mixed with general logs | Medium | Deferred — all AI events now use standard `AdminLogEntry` with OCR-prefixed event types |

---

## 4. Recommended AI-Specific Log Events

| Event Type | Level | When | Fields to Log |
|:---|:---|:---|:---|
| `AI_EXTRACTION_REQUESTED` | Information | User triggers extraction | UserId, Module, EntityId, DocumentId, DocumentHash, FileSize |
| `AI_EXTRACTION_STARTED` | Information | Provider call begins | CorrelationId, ProviderName, Model, PromptVersion |
| `AI_EXTRACTION_COMPLETED` | Information | Provider returns success | Duration, TokensUsed, FieldCount, QualityScore |
| `AI_EXTRACTION_FAILED` | Error | Provider returns error | ErrorCategory, ErrorMessage, RetryCount |
| `AI_EXTRACTION_TIMEOUT` | Warning | Provider times out | ConfiguredTimeout, ActualDuration |
| `AI_FIELD_CONFIRMED` | Information | User confirms a field | FieldName, OriginalValue, FinalValue, WasOverridden |
| `AI_FIELD_DISCARDED` | Information | User rejects a field | FieldName, RejectedValue |
| `AI_EXTRACTION_BATCH_CONFIRMED` | Information | User confirms all fields | ConfirmedCount, DiscardedCount, OverriddenCount |
| `AI_SETTINGS_UPDATED` | Warning | Admin changes AI settings | SettingName, OldValue (masked), NewValue (masked) |
| `AI_PROVIDER_TEST` | Information | Admin tests connection | ProviderName, Result, Duration |
| `AI_BLOCKED_DOCUMENT` | Warning | Upload rejected by validation | Reason, FileExtension, FileSize, UserEmail |

---

## 5. Recommended Database Models for Gaps

### 5.1 AIExtractionRequest (Enhancement to existing)

> **Note**: Most fields already exist in `ContractOcrExtractionRecord`. The following are recommended additions:

```
Additional fields for ContractOcrExtractionRecord or new table:

- PromptTemplateVersion : string(50)    -- e.g., "invoice_v3", "contract_v2"
- PageCount             : int?          -- Number of pages processed
- RetryCount            : int           -- Number of retries attempted
- EstimatedCostUsd      : decimal?      -- Estimated cost based on token usage
- InputTokensUsed       : int?          -- Prompt/input tokens
- OutputTokensUsed      : int?          -- Completion/output tokens
- ModelVersion          : string(100)   -- Exact model version used
```

### 5.2 AIProviderUsageLog (New — Optional)

For aggregated monitoring:

```
AIProviderUsageLog:
- Id                : int (PK)
- DateUtc           : Date           -- Aggregation date
- ProviderName      : string(50)     -- OPENAI, AZURE_DOC_INTELLIGENCE
- Module            : string(50)     -- CONTRACTS, REQUESTS
- RequestCount      : int            -- Total extraction requests
- SuccessCount      : int            -- Successful extractions
- FailureCount      : int            -- Failed extractions
- TotalTokensUsed   : long           -- Total tokens consumed
- EstimatedCostUsd  : decimal        -- Estimated total cost
- AvgQualityScore   : decimal?       -- Average quality score
- AvgDurationMs     : int            -- Average processing duration
```

### 5.3 AIIncidentLog (New — Optional)

For AI-specific incidents:

```
AIIncidentLog:
- Id                : Guid (PK)
- TimestampUtc      : DateTime
- Severity          : string(20)     -- LOW, MEDIUM, HIGH, CRITICAL
- Category          : string(50)     -- PROVIDER_ERROR, HALLUCINATION, INJECTION_ATTEMPT, DATA_LEAK
- Description       : string
- AffectedModule    : string(50)
- AffectedEntityId  : Guid?
- DetectedBy        : string(50)     -- SYSTEM, USER_REPORT, MONITORING
- Resolution        : string?
- ResolvedAtUtc     : DateTime?
- ResolvedByUserId  : Guid?
```

---

## 6. Retention Recommendations

| Data Type | Recommended Retention | Rationale | Action |
|:---|:---|:---|:---|
| `ContractOcrExtractionRecord` (metadata) | Same as contract retention | Audit trail for contract lifecycle | Define with Legal |
| `ContractOcrExtractedField` (field audit) | Same as contract retention | Per-field decision trail | Define with Legal |
| `OcrExtractedItem` (invoice extraction) | Same as request retention | Extraction history | Define with Legal |
| `RawJsonResult` (raw AI response) | 90 days recommended | Debugging value decreases over time | Implement cleanup job |
| `AdminLogEntry` (AI events) | 1 year recommended | Operational monitoring | Implement cleanup job |
| `debug/openai-json/` (debug files) | 0 days in non-dev | Development-only; production risk | Disable in non-dev environments |
| `AIProviderUsageLog` (aggregated) | 2 years recommended | Cost and trend analysis | Implement aggregation job |

### Cleanup Job Design — **✅ Implemented (G4)**

`OcrCleanupService.cs` is a `BackgroundService` that runs daily:

1. **Guard**: Skips if `AutoCleanupEnabled=false` (default: disabled until Legal/AI CoE confirms).
2. **Debug files**: Deletes files in `debug/openai-json/` and `debug/openai-rasterized/` older than `DebugFileRetentionDays` (default: 7).
3. **DB raw JSON**: `RawJsonRetentionDays` configured (default: 90) but DB cleanup not yet wired (reserved for future).
4. **Audit logs**: Never deleted by this job.
5. **Logging**: Emits `OCR_CLEANUP_EXECUTED` or `OCR_CLEANUP_FAILED` to `AdminLogEntry`.

Configuration:
```json
"Retention": {
  "AutoCleanupEnabled": false,
  "DebugFileRetentionDays": 7,
  "RawJsonRetentionDays": 90
}
```

---

## 7. Monitoring KPIs

### Operational KPIs

| # | KPI | Description | Data Source | Alert Threshold |
|:---|:---|:---|:---|:---|
| M1 | Extraction Volume | Total extraction requests per day/week/month | `ContractOcrExtractionRecord` + `OcrExtractedItem` | Unusual spike (>2x baseline) |
| M2 | Failure Rate | % of extractions with Status=FAILED | `ContractOcrExtractionRecord.Status` | >10% per day |
| M3 | Avg Processing Time | Mean time from trigger to completion | `TriggeredAtUtc` → `ProcessedAtUtc` | >30 seconds |
| M4 | Avg Quality Score | Mean quality/confidence across extractions | `QualityScore` fields | <0.5 (50%) |
| M5 | Manual Override Rate | % of fields where user changed AI value | `WasOverridden` / total confirmed | >30% (may indicate poor extraction) |
| M6 | Discard Rate | % of fields explicitly rejected by users | `DiscardedByUser` / total fields | >50% (AI output not useful) |
| M7 | Token Consumption | Total tokens per day/week | `TotalTokensUsed` | Budget threshold |
| M8 | Cost Per Module | Estimated cost for Contracts vs Requests | Token usage × rate | Budget threshold |

### Security KPIs

| # | KPI | Description | Data Source | Alert Threshold |
|:---|:---|:---|:---|:---|
| M9 | Blocked Document Attempts | Files rejected by upload validation | `AdminLogEntry` (AI_BLOCKED_DOCUMENT) | Any occurrence |
| M10 | Provider Errors | API errors from AI provider | `AdminLogEntry` (AI_EXTRACTION_FAILED) | >5 per hour |
| M11 | Timeout Rate | % of requests that timeout | `AdminLogEntry` (AI_EXTRACTION_TIMEOUT) | >5% per day |
| M12 | Repeated Failures by User | Same user hitting failures repeatedly | `AdminLogEntry` correlation | >3 failures/hour per user |

### Business KPIs

| # | KPI | Description | Data Source | Alert Threshold |
|:---|:---|:---|:---|:---|
| M13 | Adoption Rate | % of eligible uploads that use OCR | Extractions / total uploads | Track trend |
| M14 | Data Quality Improvement | Reduction in manual corrections over time | Override rate trend | Track improvement |
| M15 | Time Savings | Estimated time saved per extraction | Manual entry time − OCR time | Track value |

---

## Evidence Package References

| Area | Evidence Files |
|:---|:---|
| System Logs integration | `evidence/code-references/G8-system-logs-integration.md` |
| OCR event log samples | `evidence/logs/OCR_EXTRACTION_STARTED-sanitized.json` through `OCR_CLEANUP_EXECUTED-sanitized.json` |
| Cleanup service | `evidence/code-references/G4-retention-cleanup-service.md`, `evidence/configuration/retention-policy-redacted.md` |
| SQL evidence queries | `evidence/sql/ocr_system_log_events.sql`, `evidence/sql/ocr_extraction_records.sql` |
| Safe payload masking | `evidence/screenshots/SCR-32-ocr-log-detail-safe-payload.md` |

> 👉 Full evidence index: [`evidence/EVIDENCE_INDEX.md`](evidence/EVIDENCE_INDEX.md)

