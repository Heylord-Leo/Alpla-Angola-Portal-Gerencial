# Screenshot Capture Guide — AI OCR Compliance Evidence

> **Version**: 2.0 | **Date**: 2026-06-18
>
> This guide defines which screenshots must be captured manually to complete the AI OCR Compliance Evidence Package. Each screenshot has a unique ID for cross-referencing in other documents.

---

## Capture Instructions

### General Rules

1. **Use the TEST environment** — never capture production data
2. **Redact sensitive data** — mask real supplier names, values, or personal info if present
3. **Include browser URL bar** — shows environment URL for evidence
4. **Include timestamps** — visible in UI or browser DevTools
5. **Save as PNG** — high quality, no lossy compression
6. **Filename convention**: `SCR-{ID}_{DESCRIPTION}.png` (e.g., `SCR-07_contract_ocr_auto_fill.png`)
7. **Store in**: `docs/ai-ocr/evidence/screenshots/`

---

## Screenshot List

### Contract OCR Flow

| Screenshot ID | Description | Steps to Capture | Referenced By |
|:---|:---|:---|:---|
| SCR-01 | Contract creation page (empty, no OCR) | 1. Navigate to Contracts → New Contract 2. Leave all fields empty 3. Take screenshot | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V5) |
| SCR-02 | Contract OCR upload zone | 1. Open new contract form 2. Scroll to document upload area 3. Screenshot the OCR upload zone component | AI_OCR_FEATURE_OVERVIEW.md |
| SCR-03 | OCR processing state (loading) | 1. Upload a contract PDF 2. Click extract OCR 3. Screenshot during processing spinner/animation | AI_OCR_FEATURE_OVERVIEW.md |
| SCR-04 | OCR extraction complete — summary panel | 1. After successful extraction 2. Screenshot the ContractOcrSummaryPanel showing all extracted fields | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V1) |
| SCR-05 | OCR extraction complete — partial extraction banner | 1. Upload document with limited extractable data 2. After extraction, screenshot the "Extracção parcial" banner | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V13) |
| SCR-06 | OCR extraction complete — conflicts banner | 1. Upload document that triggers conflicts 2. Screenshot the "Conflitos detectados" banner | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V12) |
| SCR-07 | AUTO_FILL field — pre-confirmation (amber border + OCR badge) | 1. After extraction, find an AUTO_FILL field (e.g., Effective Date) 2. Screenshot showing amber left border, OCR badge with %, Confirmar/Limpar buttons | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V1, V3) |
| SCR-08 | AUTO_FILL field — after confirmation (green border + "Confirmado") | 1. Click "Confirmar" on an AUTO_FILL field 2. Screenshot showing green border, "Confirmado pelo utilizador" text | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V3) |
| SCR-09 | SUGGESTION chip (blue chip with Aplicar/Ignorar) | 1. After extraction, find a SUGGESTION field (e.g., CounterpartyName) 2. Screenshot showing Sparkles icon, "OCR (nn%):" prefix, suggested value, Aplicar/Ignorar buttons | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V2) |
| SCR-10 | Field after "Limpar" / discard | 1. Click "Limpar" on a confirmed field 2. Screenshot showing the cleared field (no amber border, no badge) | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V6) |
| SCR-11 | Caution banner — unconfirmed at submit | 1. Leave some AUTO_FILL fields unconfirmed 2. Click Save/Submit 3. Screenshot the warning banner listing unconfirmed fields | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V11) |

### Invoice/Proforma OCR Flow

| Screenshot ID | Description | Steps to Capture | Referenced By |
|:---|:---|:---|:---|
| SCR-12 | Proforma upload with OCR trigger | 1. Navigate to a Request → Quotation 2. Upload proforma invoice 3. Screenshot showing upload area with OCR trigger | AI_OCR_FEATURE_OVERVIEW.md |
| SCR-13 | Extracted line items in quotation form | 1. After proforma OCR extraction 2. Screenshot showing extracted line items (description, qty, unit, price) | AI_OCR_FEATURE_OVERVIEW.md |
| SCR-14 | Extracted header data (supplier, invoice number) | 1. After proforma OCR extraction 2. Screenshot showing extracted supplier name, tax ID, invoice number | AI_OCR_FEATURE_OVERVIEW.md |

### Admin/Settings Flow

| Screenshot ID | Description | Steps to Capture | Referenced By |
|:---|:---|:---|:---|
| SCR-15 | Document Extraction Settings page (overview) | 1. Navigate to Settings → Document Extraction 2. Screenshot the full settings page | AI_SUPPLIER_API_READINESS.md |
| SCR-16 | Provider selection dropdown | 1. On extraction settings page, expand provider selector 2. Screenshot showing available providers | AI_SUPPLIER_API_READINESS.md |
| SCR-17 | Connection test result (success) | 1. Click "Test Connection" 2. Screenshot showing success toast/result | AI_SUPPLIER_API_READINESS.md |
| SCR-18 | Feature disabled state | 1. Toggle provider to disabled 2. Screenshot showing disabled state | AI_HUMAN_OVERSIGHT_TRANSPARENCY.md (V8) |

### Security Evidence

| Screenshot ID | Description | Steps to Capture | Referenced By |
|:---|:---|:---|:---|
| SCR-19 | File upload rejection (wrong extension) | 1. Attempt to upload .exe file 2. Screenshot the error message | AI_SECURITY_CONTROLS_CHECKLIST.md |
| SCR-20 | File upload rejection (too large) | 1. Attempt to upload file > 15MB 2. Screenshot the error message | AI_SECURITY_CONTROLS_CHECKLIST.md |
| SCR-21 | Browser DevTools — no API key in network | 1. Open DevTools Network tab 2. Trigger OCR extraction 3. Screenshot showing request/response without API keys | AI_SECURITY_CONTROLS_CHECKLIST.md |

### Hardening Evidence (G1–G8)

| Screenshot ID | Description | Steps to Capture | Referenced By |
|:---|:---|:---|:---|
| SCR-28 | AdminLogEntry — OCR_EXTRACTION_STARTED event (G8) | 1. Trigger OCR extraction 2. Query AdminLogEntries for `OCR_EXTRACTION_STARTED` 3. Screenshot showing prompt version + module in payload | AI_LOGGING_MONITORING_DESIGN.md, AI_SECURITY_CONTROLS_CHECKLIST.md |
| SCR-29 | AdminLogEntry — OCR_MODULE_BLOCKED event (G2) | 1. Remove module from `AllowedModules` 2. Trigger OCR 3. Query AdminLogEntries for `OCR_MODULE_BLOCKED` | AI_GOVERNANCE_GAP_ANALYSIS.md |
| SCR-30 | AdminLogEntry — OCR_CLEANUP_EXECUTED event (G4) | 1. Enable `AutoCleanupEnabled=true` 2. Wait for daily run 3. Query AdminLogEntries for `OCR_CLEANUP_EXECUTED` | AI_LOGGING_MONITORING_DESIGN.md |
| SCR-31 | File system — no debug files in TEST/PROD (G1) | 1. Trigger OCR in TEST environment 2. Screenshot `debug/openai-json/` directory showing no files | AI_PRODUCTION_READINESS_CHECKLIST.md |
| SCR-32 | AdminLogEntry — [G5-PLACEHOLDER] warning (G5) | 1. Trigger OCR extraction 2. Query AdminLogEntries for `[G5-PLACEHOLDER]` | AI_SECURITY_CONTROLS_CHECKLIST.md |

### Database Evidence

| Screenshot ID | Description | Steps to Capture | Referenced By |
|:---|:---|:---|:---|
| SCR-22 | ContractOcrExtractionRecord sample row | 1. Query `ContractOcrExtractionRecords` table 2. Screenshot a sample row showing audit fields (mask sensitive data) | AI_LOGGING_MONITORING_DESIGN.md |
| SCR-23 | ContractOcrExtractedField — confirmed row | 1. Query `ContractOcrExtractedFields` where `ConfirmedByUser = 1` 2. Screenshot showing confirmation audit fields | AI_LOGGING_MONITORING_DESIGN.md |
| SCR-24 | ContractOcrExtractedField — overridden row | 1. Query where `WasOverridden = 1` 2. Screenshot showing original + final values | AI_LOGGING_MONITORING_DESIGN.md |
| SCR-25 | ContractOcrExtractedField — discarded row | 1. Query where `DiscardedByUser = 1` 2. Screenshot | AI_LOGGING_MONITORING_DESIGN.md |
| SCR-26 | AdminLogEntry — OCR event sample | 1. Query `AdminLogEntries` where `EventType LIKE 'OCR%'` 2. Screenshot showing redacted log entries | AI_LOGGING_MONITORING_DESIGN.md |
| SCR-27 | OcrExtractedItem sample rows | 1. Query `OcrExtractedItems` for a batch 2. Screenshot showing immutable extraction records | AI_LOGGING_MONITORING_DESIGN.md |

---

## SQL Query Templates for Database Evidence

> [!CAUTION]
> **Mask all sensitive data** before including in evidence. Replace real names, values, and IDs with anonymized placeholders.

### Query: ContractOcrExtractionRecord Sample

```sql
SELECT TOP 3
    Id, ContractId, TriggeredByUserId, TriggeredAtUtc, ProcessedAtUtc,
    Status, ProviderName, RoutingStrategy, ChunkCount, TotalTokensUsed,
    QualityScore, IsPartial, ConflictsDetected, NativeTextDetected,
    LEN(RawJsonResult) AS RawJsonLengthBytes, ErrorMessage
FROM ContractOcrExtractionRecords
ORDER BY TriggeredAtUtc DESC;
```

### Query: Confirmed/Overridden/Discarded Fields

```sql
SELECT TOP 5
    Id, FieldName, RawExtractedValue, NormalisedValue, ConfidenceScore,
    DisplayHint, ConfirmedByUser, ConfirmedAtUtc, WasOverridden,
    FinalSavedValue, DiscardedByUser
FROM ContractOcrExtractedFields
WHERE ConfirmedByUser = 1
ORDER BY ConfirmedAtUtc DESC;
```

### Query: AdminLogEntry OCR Events

```sql
SELECT TOP 10
    Id, TimestampUtc, Level, EventType, Message,
    CorrelationId, UserEmail
FROM AdminLogEntries
WHERE EventType LIKE '%OCR%' OR Source LIKE '%Extraction%'
ORDER BY TimestampUtc DESC;
```

### Query: OcrExtractedItem Batch

```sql
SELECT TOP 10
    Id, RequestId, ExtractionBatchId, LineNumber,
    RawDescription, Quantity, RawUnit, UnitPrice, LineTotal,
    QualityScore, ProviderName, ExtractedAtUtc
FROM OcrExtractedItems
ORDER BY ExtractedAtUtc DESC;
```

---

## Evidence File Naming Convention

```
docs/ai-ocr/evidence/
├── screenshots/
│   ├── SCREENSHOT_CAPTURE_GUIDE.md  (this file)
│   ├── SCR-01_contract_empty_form.png
│   ├── SCR-02_ocr_upload_zone.png
│   ├── SCR-07_auto_fill_amber_border.png
│   ├── SCR-08_auto_fill_confirmed_green.png
│   ├── SCR-09_suggestion_chip_blue.png
│   ├── ...
│   └── SCR-27_ocr_extracted_items.png
├── sql/
│   ├── extraction_records_sample.sql
│   ├── extracted_fields_confirmed.sql
│   ├── admin_log_ocr_events.sql
│   └── ocr_extracted_items.sql
├── api/
│   ├── extraction_response_sample.json (redacted)
│   └── upload_rejection_response.json
└── configuration/
    ├── appsettings_extraction_section.json (redacted)
    └── upload_security_config.json
```
