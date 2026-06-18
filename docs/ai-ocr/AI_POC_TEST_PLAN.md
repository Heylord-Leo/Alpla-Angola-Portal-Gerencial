# AI OCR PoC Test Plan — Portal Gerencial

> **Version**: 2.0 | **Date**: 2026-06-18 | **Status**: Post-Hardening — Ready for Execution

---

## Test Matrix

### Security Tests

| Test ID | Area | Scenario | Preconditions | Steps | Expected Result | Evidence Required | Status |
|:---|:---|:---|:---|:---|:---|:---|:---|
| SEC-01 | Auth | Unauthorized user cannot trigger OCR | Unauthenticated session | 1. Call extraction endpoint without JWT token | 401 Unauthorized | API response screenshot | ⬜ Not tested |
| SEC-02 | Auth | User without module role cannot use OCR | User with no Contract/Request role | 1. Login as restricted user 2. Navigate to contracts 3. Attempt upload + OCR trigger | 403 Forbidden or feature hidden | API response + UI screenshot | ⬜ Not tested |
| SEC-03 | Secrets | API key not exposed in frontend | Normal user session | 1. Open browser DevTools (Network tab) 2. Trigger OCR extraction 3. Inspect all request/response payloads | No API key, token, or secret in any frontend request/response | DevTools screenshot | ⬜ Not tested |
| SEC-04 | Upload | Unsupported file type rejected | Authenticated user | 1. Attempt to upload `.exe` file 2. Attempt to upload `.bat` file 3. Attempt to upload `.js` file | 400 Bad Request with clear error message | API response + UI error screenshot | ⬜ Not tested |
| SEC-05 | Upload | Dangerous filename sanitized | Authenticated user | 1. Upload file named `../../etc/passwd.pdf` 2. Upload file named `<script>alert(1)</script>.pdf` | Filename sanitized; no path traversal or XSS | DB record showing sanitized name | ⬜ Not tested |
| SEC-06 | Upload | Oversized file rejected | Authenticated user | 1. Attempt to upload file > 15MB | 400 Bad Request with size limit message | API response screenshot | ⬜ Not tested |
| SEC-07 | Injection | Prompt injection sample does not override extraction | Authenticated user, `prompt_injection_sample.txt` | 1. Upload `evidence/test-samples/prompt_injection_sample.txt` content as PDF 2. Trigger OCR | AI returns only extraction fields; injected instructions ignored | Extracted result JSON | ⬜ Not tested |
| SEC-08 | Secrets | API key redacted in admin logs | Admin user | 1. Trigger OCR extraction 2. Check AdminLogEntry records | No API key, token, or secret values in log entries | DB query result (masked) | ⬜ Not tested |
| SEC-09 | CORS | Cross-origin request blocked | External origin | 1. Attempt API call from non-allowed origin | CORS error; request blocked | Browser console screenshot | ⬜ Not tested |
| SEC-10 | Feature Flag | Module-blocked extraction logged | `AllowedModules` excludes target module | 1. Remove module from `AiOcrPolicy.AllowedModules` 2. Trigger OCR | Extraction blocked; `OCR_MODULE_BLOCKED` event in AdminLogEntry | DB log query | ⬜ Not tested |
| SEC-11 | Feature Flag | Doc-type-blocked extraction logged | `AllowedDocumentTypes` excludes target type | 1. Remove doc type from allowlist 2. Trigger OCR | Extraction blocked; `OCR_DOCUMENT_TYPE_BLOCKED` event in AdminLogEntry | DB log query | ⬜ Not tested |
| SEC-12 | Debug Guard | No debug files in non-Development | TEST or PROD environment | 1. Trigger OCR 2. Check `debug/openai-json/` and `debug/openai-rasterized/` | No files written; `[G1-DEBUG]` log entry present | File system + DB log | ⬜ Not tested |

### Data Protection Tests

| Test ID | Area | Scenario | Preconditions | Steps | Expected Result | Evidence Required | Status |
|:---|:---|:---|:---|:---|:---|:---|:---|
| DP-01 | Env | DEV/TEST uses test data only | TEST environment | 1. Verify document types processed in TEST 2. Confirm no real high-risk data | Only test/sample documents processed | Admin confirmation | ⬜ Not tested |
| DP-02 | Logging | Raw payload logging controlled | Non-Development environment | 1. Verify `debug/openai-json/` directory behavior in TEST/PROD | No debug files created | File system check | ⬜ Not tested |
| DP-03 | Upload | Document hash created | Any upload | 1. Upload a document 2. Check `RequestAttachment.FileHash` | SHA-256 hash populated | DB query result | ⬜ Not tested |
| DP-04 | Retention | Retention/cleanup behavior defined | `OcrCleanupService` configured | 1. Set `AutoCleanupEnabled=true` in test config 2. Place old debug files 3. Wait for daily run or trigger manually | Old debug files deleted; `OCR_CLEANUP_EXECUTED` event logged | File system + DB log | ⬜ Not tested |
| DP-05 | Logging | SafePayload redacts secrets | Trigger admin log write | 1. Create log entry with sensitive field names 2. Verify stored entry | Fields like `apiKey`, `token`, `password` show `[REDACTED]` | DB query result (masked) | ⬜ Not tested |
| DP-06 | DB | RawJsonResult not exposed via API | Contract with OCR completed | 1. Call contract detail API 2. Check response JSON | `RawJsonResult` not present in API response | API response body | ⬜ Not tested |

### Human Oversight Tests

| Test ID | Area | Scenario | Preconditions | Steps | Expected Result | Evidence Required | Status |
|:---|:---|:---|:---|:---|:---|:---|:---|
| HO-01 | UI | AI output shown as suggestion, not fact | Contract with completed OCR | 1. View contract form after OCR 2. Identify AI-populated fields | OCR badge visible, amber border on AUTO_FILL fields, blue chip on SUGGESTION fields | Screenshot | ⬜ Not tested |
| HO-02 | UI | User must confirm before saving | Contract with OCR AUTO_FILL fields | 1. Do not confirm any fields 2. Click save | Caution banner appears; unconfirmed values excluded from save | Screenshot + DB verification | ⬜ Not tested |
| HO-03 | UI | User can edit extracted data | Contract with OCR AUTO_FILL field | 1. Change an AI-suggested value 2. Click "Confirmar" 3. Save contract | `WasOverridden = true`, `FinalSavedValue` contains edited value | DB query result | ⬜ Not tested |
| HO-04 | UI | User can reject extracted data | Contract with OCR field | 1. Click "Limpar" or "Ignorar" 2. Save contract | `DiscardedByUser = true`; field empty in saved contract | DB query result | ⬜ Not tested |
| HO-05 | UI | Manual fallback works | No OCR performed | 1. Create contract without uploading any document 2. Enter all fields manually 3. Save | Contract created successfully without OCR | DB record | ⬜ Not tested |
| HO-06 | Audit | Confirmation logged with user/timestamp | Confirmed OCR field | 1. Confirm an OCR field 2. Check `ContractOcrExtractedField` record | `ConfirmedByUserId`, `ConfirmedAtUtc` populated correctly | DB query result | ⬜ Not tested |

### Reliability Tests

| Test ID | Area | Scenario | Preconditions | Steps | Expected Result | Evidence Required | Status |
|:---|:---|:---|:---|:---|:---|:---|:---|
| REL-01 | Provider | Provider timeout handled gracefully | Configured timeout (reduce for test) | 1. Set timeout to 1 second 2. Upload large document 3. Trigger OCR | Error message shown to user; record status = FAILED; no crash | Screenshot + DB status | ⬜ Not tested |
| REL-02 | Provider | Provider error handled gracefully | Simulate provider error (invalid key) | 1. Set invalid API key 2. Trigger OCR | Error message shown to user; record status = FAILED; logging captured | Screenshot + DB + logs | ⬜ Not tested |
| REL-03 | Provider | Invalid JSON response handled | Simulate malformed response | 1. If possible, trigger extraction on corrupted document | Graceful failure; status = FAILED; user informed | Screenshot + logs | ⬜ Not tested |
| REL-04 | Workflow | Extraction failure does not block process | Contract with failed OCR | 1. Trigger OCR (expect failure) 2. Enter data manually 3. Save contract | Contract saved successfully despite OCR failure | DB record | ⬜ Not tested |
| REL-05 | Logging | Correlation ID present in all logs | Any OCR extraction | 1. Trigger extraction 2. Note Correlation ID from response header 3. Query AdminLogEntry | Same Correlation ID appears in extraction-related log entries | DB query + response header | ⬜ Not tested |
| REL-06 | Settings | Connection test works | Admin user | 1. Navigate to extraction settings 2. Click "Test Connection" | Success or clear error message; result logged | Screenshot + log entry | ⬜ Not tested |

### Business Validation Tests

| Test ID | Area | Scenario | Preconditions | Steps | Expected Result | Evidence Required | Status |
|:---|:---|:---|:---|:---|:---|:---|:---|
| BV-01 | Invoice | Invoice extraction — standard sample | Sample proforma invoice (PDF) | 1. Upload proforma 2. Trigger OCR 3. Review extracted items | Line items extracted: description, quantity, unit, price | Extracted fields screenshot | ⬜ Not tested |
| BV-02 | Contract | Contract extraction — standard sample | Sample contract (PDF) | 1. Upload contract 2. Trigger OCR 3. Review extracted fields | Dates, counterparty, value, currency extracted | Extracted fields screenshot | ⬜ Not tested |
| BV-03 | PDF | Multi-page PDF extraction | 3+ page PDF document | 1. Upload multi-page document 2. Trigger OCR | All pages processed; fields from all pages extracted | Extracted result | ⬜ Not tested |
| BV-04 | Image | Low-quality image extraction | Low-resolution or skewed image | 1. Upload poor quality image 2. Trigger OCR | Extraction attempts gracefully; quality score reflects difficulty | Quality score + extracted result | ⬜ Not tested |
| BV-05 | Language | Portuguese document extraction | Portuguese language invoice | 1. Upload PT invoice 2. Trigger OCR | Portuguese text correctly extracted; no language errors | Extracted fields | ⬜ Not tested |
| BV-06 | Language | English document extraction | English language contract | 1. Upload EN contract 2. Trigger OCR | English text correctly extracted | Extracted fields | ⬜ Not tested |
| BV-07 | Edge Case | Document with missing fields | Document with only partial information | 1. Upload document with few extractable fields 2. Trigger OCR | Available fields extracted; `IsPartial = true`; caution banner shown | Screenshot + DB | ⬜ Not tested |
| BV-08 | Edge Case | Empty/blank PDF | Empty PDF file | 1. Upload empty/blank PDF 2. Trigger OCR | Graceful handling; status = COMPLETED with no fields or FAILED with clear message | Result + DB | ⬜ Not tested |

---

## Test Execution Instructions

### Prerequisites

1. TEST environment configured with:
   - Valid AI provider API key
   - Test database with sample data
   - Admin and non-admin user accounts
2. Sample documents prepared:
   - Standard Portuguese invoice PDF
   - Standard English contract PDF
   - Multi-page PDF (3+ pages)
   - Low-quality scanned image
   - Document with prompt injection text (`docs/ai-ocr/evidence/test-samples/prompt_injection_sample.txt`)
   - Empty/blank PDF
   - Oversized file (> 15MB)
   - File with dangerous extension (.exe, .bat)
   - File with path traversal filename

### Evidence Collection

For each test:
1. Record the test result (Pass/Fail/Partial)
2. Capture screenshots as specified
3. Record relevant database query results (with sensitive values masked)
4. Save API response bodies where specified
5. Note any deviations from expected results

### Pass Criteria

- **Security tests**: All SEC-* tests must PASS
- **Data protection**: All DP-* tests must PASS or have documented exceptions
- **Human oversight**: All HO-* tests must PASS
- **Reliability**: All REL-* tests must PASS
- **Business validation**: At least BV-01, BV-02, BV-05 must PASS; others recommended

---

## Evidence Package References

| Area | Evidence Files |
|:---|:---|
| Prompt injection test | `evidence/test-results/G3-prompt-injection-test-result.md`, `evidence/test-samples/prompt_injection_sample.txt` |
| Cleanup validation | `evidence/test-results/G4-cleanup-validation.md` |
| File scan validation | `evidence/test-results/G5-file-scan-placeholder-validation.md` |
| Provider endpoint test | `evidence/test-results/G6-provider-endpoint-validation.md` |
| PoC execution status | `evidence/test-results/poc-test-execution-status.md` |
| SQL evidence queries | `evidence/sql/ocr_system_log_events.sql`, `evidence/sql/ocr_field_review_evidence.sql` |

> 👉 Full evidence index: [`evidence/EVIDENCE_INDEX.md`](evidence/EVIDENCE_INDEX.md)
