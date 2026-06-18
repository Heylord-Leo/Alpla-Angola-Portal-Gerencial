# AI-Assisted OCR Compliance Evidence Report — Portal Gerencial

> **Classification**: INTERNAL — For review by AI CoE, Corporate IT, Information Security, Legal/Privacy, and Business Stakeholders.
> **Version**: 2.0 | **Date**: 2026-06-18 | **Author**: Technical Assessment (Agent-Assisted)
> **Status**: Post-Hardening (G1–G8) — Awaiting AI CoE / Legal / Corporate IT Confirmation

---

## 1. Executive Summary

### Current Status

The Portal Gerencial **already operates a mature, production-active AI-assisted OCR/document extraction system**. This is **not a greenfield assessment** — significant infrastructure exists across both the **Request** (invoice/proforma) and **Contract** modules.

### Production-Active Areas

| Module | AI OCR Capability | Status |
|:---|:---|:---|
| **Requests — Invoice/Proforma OCR** | Extracts line items, supplier data, financial values from uploaded proforma/invoices | ✅ Active |
| **Contracts — Contract OCR** | Extracts dates, values, counterparty, terms from contract PDFs | ✅ Active |
| **Admin — Settings Management** | Runtime provider config, connection testing, model selection | ✅ Active |

### Overall Readiness Assessment

| Dimension | Status | Summary |
|:---|:---|:---|
| **Technical Maturity** | ✅ Mature | Provider abstraction, audit logging, human oversight UI, upload security, settings management |
| **Governance Documentation** | 🔴 Incomplete | No evidence of AI CoE registration, AI Registry Number, or formal governance package |
| **Legal/Privacy Compliance** | 🟣 Unconfirmed | No DPA/SCC/TIA documentation found in repository; requires Legal confirmation |
| **Supplier Approval** | 🟣 Unconfirmed | Direct OpenAI API usage; no evidence of Corporate IT or AI CoE provider approval |
| **Security Hardening** | ✅ Hardened (G1–G5) | Debug guard, prompt injection defense, feature flags, retention cleanup, malware scan extension point |

### Main Risks

| # | Risk | Severity | Current Mitigation |
|:---|:---|:---|:---|
| R1 | Direct OpenAI API usage (not Azure tenant-controlled) | 🔴 High | Provider abstraction enables switch; no switch initiated |
| R2 | No evidence of AI CoE registration | 🔴 High | None — requires business action |
| R3 | No DPA/SCC/TIA evidence in repository | 🔴 High | None — requires Legal action |
| R4 | Debug raw JSON response logging to disk | ✅ Resolved (G1) | Guarded by `IsDebugLoggingAllowed()` dual check |
| R5 | Raw JSON stored in `ContractOcrExtractionRecord.RawJsonResult` | 🟡 Medium | 64KB truncation; not exposed via API; retention cleanup configured |
| R6 | No malware scanning on uploaded files | 🔶 Extension Point (G5) | `IFileScanService` + `NoOpFileScanService` registered; real AV pending |
| R7 | No explicit data retention/cleanup policy | ✅ Resolved (G4) | `OcrCleanupService` + `RetentionPolicyOptions` configured |
| R8 | No formal prompt injection protections | ✅ Resolved (G3) | Security preamble on both prompts; test sample created |

### Conclusion

> **The Portal Gerencial AI-assisted OCR feature is technically mature and appears suitable for a controlled compliance hardening phase. However, it should be classified as governance-incomplete until AI CoE registration, supplier approval, legal/data processing validation, and production evidence are completed. The current status is: ready for compliance documentation and controlled PoC hardening; not yet ready to be declared fully compliant.**

---

## 2. System Scope

### What the AI OCR Feature Does

The system allows authenticated users to upload documents (PDFs, images) within the Portal Gerencial. The backend sends these documents to an AI provider (currently OpenAI GPT-4 Turbo with Vision) for structured data extraction. The AI returns JSON with extracted fields that are presented to the user as **suggestions only** — the user must review, edit, confirm, or reject each extracted value before it is persisted.

### Modules Using AI OCR

| Module | Use Case | Document Types | Data Extracted |
|:---|:---|:---|:---|
| **Requests** | Invoice/proforma line item extraction | PDF, JPG, PNG (proformas, invoices) | Supplier name/TaxID, line items (description, qty, unit, price, discount, tax), document number, totals |
| **Contracts** | Contract metadata extraction | PDF (contracts, amendments) | Dates (effective, expiration, signature), counterparty, total value, currency, payment terms, governing law, termination clauses |

### Users Who Interact

- **Requesters**: Upload proformas, review OCR-extracted line items
- **Buyers**: Review and process OCR-extracted quotation data
- **Contract Managers**: Upload contract documents, review/confirm OCR fields
- **System Administrators**: Configure OCR provider settings, manage API keys, monitor logs

### AI Output Handling

> **Critical Design Principle**: AI output is **strictly suggestive**. No extracted value is directly saved to business entities without explicit human confirmation.

- **Contracts**: Each extracted field has `ConfirmedByUser`, `WasOverridden`, `FinalSavedValue`, and `DiscardedByUser` audit fields
- **Requests/Invoices**: Extracted items populate draft UI state; user must review and submit the final values

---

## 3. Evidence-Based Current State

| # | Area | Status | Evidence / Source Files | Notes |
|:---|:---|:---|:---|:---|
| 1 | Provider Abstraction Interface | ✅ Implemented | `IDocumentExtractionProvider.cs` — `Name` + `ExtractAsync()` | Strategy pattern; multiple providers injectable via DI |
| 2 | Extraction Service Orchestrator | ✅ Implemented | `DocumentExtractionService.cs` — `IEnumerable<IDocumentExtractionProvider>` injection | Resolves active provider from settings cascade |
| 3 | Settings Service Interface | ✅ Implemented | `IDocumentExtractionSettingsService.cs` — `GetEffectiveSettingsAsync`, `TestConnectionAsync` | DB → appsettings → defaults cascade |
| 4 | OpenAI Provider | ✅ Implemented | `OpenAiDocumentExtractionProvider.cs` (1185 lines) | Vision API, TextFirst strategy, PDF triage, rasterization |
| 5 | Azure Document Intelligence | 🔶 Placeholder | `appsettings.json` line 42–45: `"Enabled": false` | Config exists, no provider class implemented |
| 6 | Document Triage | ✅ Implemented | `OpenAiDocumentExtractionProvider.cs` — keyword-based invoice vs contract classification | Falls back to `INVOICE` for unknown types |
| 7 | PDF Rasterization | ✅ Implemented | `OpenAiDocumentExtractionProvider.cs` — PdfiumViewer, configurable DPI/quality | Native text detection → TextFirst or Rasterize |
| 8 | Prompt Templates | ✅ Implemented | `OpenAiDocumentExtractionProvider.cs` — 71-line invoice prompt, contract prompt | System role instructs JSON-only output |
| 9 | Extraction DTOs | ✅ Implemented | `ExtractionResultDto.cs` (8.6KB) — `ExtractionHeaderDto`, `ExtractionLineItemDto`, `ExtractionContractDto` | Provider-agnostic canonical model |
| 10 | Admin Settings UI | ✅ Implemented | `DocumentExtractionSettings.tsx` | Provider selection, model config, connection testing |
| 11 | Contract OCR Audit Entity | ✅ Implemented | `ContractOcrExtractionRecord.cs` — trigger user, timestamp, provider, tokens, quality score, status lifecycle | Full audit trail per extraction run |
| 12 | Contract OCR Field Entity | ✅ Implemented | `ContractOcrExtractedField.cs` — per-field raw/normalised values, confidence, display hint, user confirmation | `ConfirmedByUser`, `WasOverridden`, `FinalSavedValue`, `DiscardedByUser` |
| 13 | Invoice OCR Entity | ✅ Implemented | `OcrExtractedItem.cs` — immutable snapshot, batch grouping, quality score, provider name | Linked to `RequestAttachment` via `AttachmentId` |
| 14 | Frontend: OcrFieldWrapper | ✅ Implemented | `OcrFieldWrapper.tsx` — amber border for AUTO_FILL, Confirmar/Limpar buttons, confirmed state | Visual treatment for AI-populated fields |
| 15 | Frontend: OcrSuggestionChip | ✅ Implemented | `OcrSuggestionChip.tsx` — blue chip with Aplicar/Ignorar, confidence %, Sparkles icon | Clear AI origin indicator |
| 16 | Frontend: ContractOcrUploadZone | ✅ Implemented | `ContractOcrUploadZone.tsx` | Upload trigger for contract OCR |
| 17 | Frontend: ContractOcrSummaryPanel | ✅ Implemented | `ContractOcrSummaryPanel.tsx` | Overview of all extracted fields and their confirmation status |
| 18 | Frontend: ContractOcrCautionBanner | ✅ Implemented | `ContractOcrCautionBanner.tsx` — 3 variants: unconfirmed_at_submit, conflicts_detected, partial_extraction | Inline warnings with dismissible UI |
| 19 | Frontend: useOcrProcessor | ✅ Implemented | `useOcrProcessor.ts` | Hook for invoice/proforma OCR triggering |
| 20 | Frontend: useContractOcr | ✅ Implemented | `useContractOcr.ts` | Hook for contract OCR state management |
| 21 | API Key Management | ✅ Implemented | `IntegrationConfigResolver` — DB-first with AES-256 encryption + env var fallback | `AesEncryptionHelper.cs` — AES-CBC, HMAC-SHA256 key derivation |
| 22 | AdminLogWriter | ✅ Implemented | `AdminLogWriter.cs` — best-effort, fail-safe, server-side user resolution, `SafePayload` sanitization | Events: `OCR_SETTINGS_SAVED`, `OCR_PROVIDER_TEST_OK`, etc. |
| 23 | CorrelationIdMiddleware | ✅ Implemented | `CorrelationIdMiddleware.cs` — `X-Correlation-ID` header, 12-char GUID | Applied to all requests via `Program.cs` |
| 24 | Payload Sanitization | ✅ Implemented | `SafePayload.cs` — field-name masking + regex redaction for secrets/tokens/keys | Two-layer: known fields → regex patterns |
| 25 | Upload Security | ✅ Implemented | `AttachmentsController.cs` — whitelist, blocklist, size limit, MIME check, SHA-256 hash, filename sanitization | Configurable via `Security:Upload` in `appsettings.json` |
| 26 | RBAC | ✅ Implemented | JWT auth, `User`/`Role`/`UserRoleAssignment`, plant/department scoping | `[Authorize]` on all controllers |
| 27 | Environment Separation | ✅ Implemented | `AppEnvironment` config — `PROD`/`TEST` code, visual banner, migration controls | Non-dev: no auto-migrate, fail-fast on pending |
| 28 | Rate Limiting | ✅ Implemented | `Program.cs` — `LoginPolicy` fixed window limiter, IP-based | `AdminLogWriter` logs throttled events |
| 29 | Field Propagation Standard | ✅ Documented | `DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md` | 10-step checklist, naming rules, compatibility guidance |

---

## 4. AI Governance Classification

### Why This Qualifies as an AI System

The Portal's OCR feature uses a large language model (GPT-4 Turbo) to interpret unstructured document content and produce structured data outputs. This meets the definition of an AI system under ALPLA AI Governance Policy 2.0 and the EU AI Act because:

1. It processes input data (document images/text) using machine learning models
2. It generates output (structured JSON with extracted fields) that influences user decisions
3. It operates with varying degrees of autonomy (automated extraction with human review)

### Proposed Classification: Limited-Risk AI System

The system **likely qualifies as Limited-Risk** because:

- ✅ It interacts with users who know they are using AI-assisted extraction
- ✅ It generates/extracts content that users must review and confirm
- ✅ Users retain full decision-making authority over final data
- ✅ It does not make binding financial, legal, or operational decisions autonomously
- ✅ The AI output is clearly marked as AI-generated in the UI (OCR badges, caution banners)

### Mandatory Transparency Obligations (Limited-Risk)

- Users must be informed that content is AI-generated → ✅ **Implemented** (OCR badges, caution banners)
- Users must be able to review and override AI output → ✅ **Implemented** (Confirmar/Limpar/Aplicar/Ignorar)
- AI system must be documented → 🔶 **This package addresses this gap**

### Escalation Triggers to High-Risk

The classification MUST be escalated to **High-Risk** if any of these conditions occur:

| Trigger | Current Status |
|:---|:---|
| Processing HR/payroll/health data via AI | ⚪ Not applicable currently |
| Processing secret/confidential corporate IP | 🟣 Requires classification confirmation |
| Automatic financial posting without review | ⚪ Not applicable — human confirmation required |
| Supplier/customer-impacting decisions without review | ⚪ Not applicable — suggestions only |
| Non-approved external data transfer | 🟣 OpenAI API usage requires confirmation |
| Critical operational dependency without fallback | 🔶 Manual entry fallback exists |

### Why Full Compliance Cannot Be Declared

Full compliance requires confirmations that are **beyond the scope of technical assessment**:

1. 🔴 No AI CoE registration evidence
2. 🔴 No AI Registry Number
3. 🟣 No supplier (OpenAI) approval documentation
4. 🟣 No DPA/SCC/TIA documentation
5. 🟣 No data residency confirmation
6. 🟣 No formal risk classification sign-off

---

## 5. Compliance Matrix

| # | Requirement | Policy Expectation | Current Implementation | Status | Evidence | Gap | Recommended Action | Owner |
|:---|:---|:---|:---|:---|:---|:---|:---|:---|
| 1 | AI Registry Number | Mandatory for all AI systems | Not found | 🔴 Missing | No registry evidence in repo | No registration | Register with AI CoE | AI Product Owner |
| 2 | AI Product Owner | Designated person accountable for the AI system | Not assigned | 🔴 Missing | No assignment in docs | No designated owner | Assign AI Product Owner | Management |
| 3 | Sponsor / Process Owner | Business sponsor who owns the process | Not formally documented | 🔴 Missing | No assignment in docs | No documentation | Document ownership | Management |
| 4 | Data Owner | Person accountable for data quality and governance | Not formally documented | 🔴 Missing | No assignment in docs | No documentation | Assign Data Owner | Management |
| 5 | Risk Classification | Formal risk level determination | Limited-Risk proposed (this report) | 🔶 Proposed | This document | Not formally approved | Submit for AI CoE review | AI CoE |
| 6 | Quick Check AI Project | Standardized AI project assessment | Not completed | 🔴 Missing | Not found | Not started | Complete template | AI Product Owner |
| 7 | Business Data Science Canvas | Business case and technical description | Not completed | 🔴 Missing | Not found | Not started | Complete template | AI Product Owner |
| 8 | Quick Check AI Supplier | Supplier assessment for AI component | Not completed | 🔴 Missing | Not found | Not started | Complete for OpenAI/Azure | AI CoE + Procurement |
| 9 | Quick Check AI Architecture | Architecture assessment | Not completed | 🔴 Missing | Not found | Not started | Complete template | IT Architecture |
| 10 | Decision Card | Required if High-Risk classification | N/A if Limited-Risk | ⚪ N/A | — | — | Required only if escalated | AI CoE |
| 11 | Data Flow Map | Document all data flows to/from AI | Documented in this package | ✅ Implemented | `AI_DATA_FLOW_MAP.md` | None | Validate with IT Security | IT Security |
| 12 | Supplier Approval | AI CoE / Corporate IT approval of provider | No evidence of approval | 🟣 Unconfirmed | No approval docs | Possible gap | Confirm with Corporate IT | Corporate IT |
| 13 | DPA/SCC | Data Processing Agreement with AI provider | Not found in repo | 🟣 Unconfirmed | No contracts found | Possible gap | Confirm with Legal | Legal |
| 14 | TIA | Transfer Impact Assessment for non-EU processing | Not found in repo | 🟣 Unconfirmed | No TIA found | Possible gap | Confirm with Legal | Legal |
| 15 | Data Residency | AI processing within approved regions | Unknown — OpenAI default regions | 🟣 Unconfirmed | No config found | Possible gap | Confirm API region config | Corporate IT |
| 16 | Human Oversight | Human review before AI output is persisted | Implemented — Confirmar/Limpar/Aplicar/Ignorar | ✅ Implemented | `OcrFieldWrapper.tsx`, `ContractOcrExtractedField.cs` | None | Verify with screenshots | UX Team |
| 17 | Transparency Notice | Users informed of AI involvement | Implemented — OCR badges, caution banners, Sparkles icon | ✅ Implemented | `OcrSuggestionChip.tsx`, `ContractOcrCautionBanner.tsx`, `OcrFieldWrapper.tsx` | None | Verify with screenshots | UX Team |
| 18 | RBAC | Role-based access to AI features | Implemented — JWT + roles + scoping | ✅ Implemented | `Program.cs`, `User.cs`, `Role.cs`, `BaseController` | None | — | IT Team |
| 19 | Upload Controls | File validation before AI processing | Implemented — whitelist, blocklist, size, MIME, hash | ✅ Implemented | `AttachmentsController.cs`, `appsettings.json` Security section | Missing malware scan | Add antivirus scanning | IT Security |
| 20 | Prompt Injection Controls | Protection against adversarial document content | Partial — system prompt instructs JSON-only output | 🔶 Partial | `OpenAiDocumentExtractionProvider.cs` system prompt | No explicit injection defense | Add injection guardrails | Dev Team |
| 21 | Output Validation | Schema/type validation of AI responses | Partial — JSON deserialization with fallbacks | 🔶 Partial | `OpenAiDocumentExtractionProvider.cs` | Basic validation only | Add strict schema validation | Dev Team |
| 22 | Audit Logging | Track all AI interactions | Implemented — `AdminLogWriter`, `ContractOcrExtractionRecord`, `OcrExtractedItem` | ✅ Implemented | Multiple entities and services | Missing: prompt template version, token cost per request in invoice flow | Add AI-specific log fields | Dev Team |
| 23 | Monitoring | Real-time monitoring of AI operations | Not implemented | 🔴 Missing | No dashboards found | No monitoring | Build monitoring dashboard | DevOps |
| 24 | Incident Handling | Process for AI-related incidents | Not formally documented | 🔴 Missing | No process found | No process | Define AI incident process | IT Operations |
| 25 | Retention Policy | Data lifecycle management for AI data | Not implemented | 🔴 Missing | No cleanup jobs found | No policy | Define with Legal/AI CoE | Legal + IT |
| 26 | Debug Logging | Development debug payload dumps controlled | Partial — `debug/openai-json/` exists, `.gitignore` covers it | 🔶 Partial | `.gitignore` line 18: `debug/` | Active in dev; no prod guard | Add environment guard | Dev Team |
| 27 | Raw Payload Logging | Raw AI responses not exposed | Partial — `RawJsonResult` in DB (64KB max), not API-exposed | 🔶 Partial | `ContractOcrExtractionRecord.cs` line 54 | Stored in DB | Define retention + encryption | Dev Team + Legal |
| 28 | Malware Scanning | Uploaded files scanned for malware | Not implemented | 🔴 Missing | Only extension/MIME validation | No scanning | Integrate AV engine | IT Security |
| 29 | Feature Flags | AI feature can be toggled | Partial — `DocumentExtractionSettings.IsEnabled` per provider | 🔶 Partial | `DocumentExtractionSettingsService.cs` | No kill-switch per module | Add granular feature flags | Dev Team |
| 30 | Environment Separation | DEV/TEST/PROD isolation | Implemented — `AppEnvironment` config, visual banners, migration controls | ✅ Implemented | `appsettings.json`, `Program.cs` lines 236–329 | None | — | IT Team |

---

## 6. Key Gaps

### Must Fix Before Formal PoC

| # | Gap | Priority | Owner | Status |
|:---|:---|:---|:---|:---|
| G1 | Add environment guard to disable debug file writes | High | Dev Team | ✅ **DONE** |
| G2 | Add explicit prompt injection defense instructions to system prompts | High | Dev Team | ✅ **DONE** |
| G3 | Add strict JSON schema validation for AI responses | Medium | Dev Team | Pending |
| G4 | Add granular feature flags (kill-switch per module) | Medium | Dev Team | ✅ **DONE** |

### Must Fix Before Production Approval

| # | Gap | Priority | Owner | Status |
|:---|:---|:---|:---|:---|
| G5 | Implement malware scanning for uploaded files | High | IT Security | 🔶 Extension point ready |
| G6 | Define and implement data retention policy | High | Legal + Dev Team | 🔶 Technical ready |
| G7 | Build monitoring dashboard | Medium | DevOps | Pending |
| G8 | Define formal AI incident handling process | Medium | IT Operations | Pending |
| G9 | Add encryption/masking for `RawJsonResult` | Medium | Dev Team | Pending |
| G10 | Add token cost tracking per extraction request | Low | Dev Team | Pending |

### Requires AI CoE / Legal / Corporate IT Confirmation

| # | Item | Owner |
|:---|:---|:---|
| C1 | AI CoE Registration — Has the feature been registered? Does an AI Registry Number exist? | AI CoE |
| C2 | AI Product Owner — Who is the designated AI Product Owner? | Management |
| C3 | Supplier Approval — Is direct OpenAI API approved, or is Azure OpenAI required? | Corporate IT |
| C4 | DPA/SCC/TIA — Does ALPLA have a Data Processing Agreement with OpenAI Inc.? | Legal |
| C5 | Document Classification — Are processed documents CONFIDENTIAL/SECRET? | Information Security |
| C6 | Data Residency — Must AI processing occur in EU/EEA? | Legal + Corporate IT |
| C7 | Training Data — Does ALPLA's agreement with OpenAI prevent use of submitted data for training? | Legal |
| C8 | Retention Period — What is the required retention period for AI audit records? | Legal + AI CoE |

### Recommended Improvements

| # | Improvement | Priority | Owner |
|:---|:---|:---|:---|
| I1 | Switch to Azure OpenAI for tenant-controlled, region-specific processing | Recommended | Corporate IT + Dev Team |
| I2 | Add API key rotation mechanism | Low | Dev Team |
| I3 | Add per-request cost estimation and budget alerts | Low | DevOps |
| I4 | Add confidence threshold configuration (auto-reject low-confidence values) | Low | Dev Team |
| I5 | Add batch extraction support for multi-document uploads | Low | Dev Team |

---

## 7. Final Readiness Conclusion

> **The Portal Gerencial AI-assisted OCR feature is technically mature and appears suitable for a controlled compliance hardening phase. However, it should be classified as governance-incomplete until AI CoE registration, supplier approval, legal/data processing validation, and production evidence are completed. The current status is: ready for compliance documentation and controlled PoC hardening; not yet ready to be declared fully compliant.**

### Readiness Classification

| Level | Status | Rationale |
|:---|:---|:---|
| Ready for Controlled PoC | ✅ Yes | Phase 1 hardening complete; all technical PoC blockers resolved |
| Ready for Limited Production (current scope) | 🔶 Conditional | Requires C1–C4 governance confirmations |
| Ready for Full Production Rollout | 🔴 No | Requires all gaps addressed, all confirmations obtained |
| Ready for Full Compliance Declaration | 🔴 No | Requires formal AI CoE review and sign-off |

---

## Supporting Documents

| Document | Path | Purpose |
|:---|:---|:---|
| Feature Overview | `AI_OCR_FEATURE_OVERVIEW.md` | Detailed feature description with architecture diagrams |
| Governance Gap Analysis | `AI_GOVERNANCE_GAP_ANALYSIS.md` | ALPLA AI Principles mapping |
| Risk Classification | `AI_RISK_CLASSIFICATION.md` | Risk level assessment and register |
| Data Flow Map | `AI_DATA_FLOW_MAP.md` | Architecture and data flow diagrams |
| Supplier/API Readiness | `AI_SUPPLIER_API_READINESS.md` | Provider assessment |
| Security Controls | `AI_SECURITY_CONTROLS_CHECKLIST.md` | Security checklist |
| Logging & Monitoring | `AI_LOGGING_MONITORING_DESIGN.md` | Audit and monitoring design |
| Human Oversight | `AI_HUMAN_OVERSIGHT_TRANSPARENCY.md` | UX evidence |
| PoC Test Plan | `AI_POC_TEST_PLAN.md` | Test matrix |
| Production Readiness | `AI_PRODUCTION_READINESS_CHECKLIST.md` | Go/no-go checklist |
| Screenshot Guide | `evidence/screenshots/SCREENSHOT_CAPTURE_GUIDE.md` | Manual evidence capture instructions |
| **Master Evidence Index** | `evidence/EVIDENCE_INDEX.md` | **Central traceability matrix for all compliance evidence** |

---

## 10. Evidence Package Reference

> For the complete technical evidence package with code references, configuration evidence, sanitized log samples, SQL queries, build results, test results, and screenshot placeholders, see:
>
> 👉 **[`evidence/EVIDENCE_INDEX.md`](evidence/EVIDENCE_INDEX.md)**

### Key Evidence Files

| Category | Path |
|:---|:---|
| Code References | `evidence/code-references/G1-debug-logging-guard.md` through `G8-system-logs-integration.md` |
| Configuration | `evidence/configuration/document-extraction-settings-redacted.md` |
| Log Samples | `evidence/logs/OCR_EXTRACTION_STARTED-sanitized.json` and 9 others |
| SQL Queries | `evidence/sql/ocr_system_log_events.sql` and 4 others |
| Build Results | `evidence/build/backend-build-result.md`, `evidence/build/frontend-build-result.md` |
| Test Results | `evidence/test-results/poc-test-execution-status.md` |
| Screenshots | `evidence/screenshots/SCR-28-debug-logging-disabled.md` through `SCR-32-ocr-log-detail-safe-payload.md` |

