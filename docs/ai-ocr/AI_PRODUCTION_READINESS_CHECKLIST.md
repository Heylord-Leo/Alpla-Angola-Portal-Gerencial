# AI OCR Production Readiness Checklist — Portal Gerencial

> **Version**: 2.0 | **Date**: 2026-06-18 | **Status**: Post-Hardening (G1–G8)

---

## Go/No-Go Summary

> **Current Recommendation**: **Not ready to be declared fully compliant** for unrestricted production use until governance confirmations and hardening actions are completed.

| Level | Status | Rationale |
|:---|:---|:---|
| **Ready for Controlled PoC** | ✅ Yes | Technical infrastructure hardened; all PoC blockers resolved |
| **Ready for Limited Production** | 🔶 Conditional | Requires C1–C4 governance confirmations |
| **Ready for Full Production** | 🔴 No | Requires all gaps addressed + all confirmations |
| **Ready for Compliance Declaration** | 🔴 No | Requires formal AI CoE review + sign-off |

---

## 1. Technical Readiness

| # | Item | Status | Evidence | Remaining Action |
|:---|:---|:---|:---|:---|
| T1 | Provider abstraction (IDocumentExtractionProvider) | ✅ Ready | `IDocumentExtractionProvider.cs`, `DocumentExtractionService.cs` | None |
| T2 | OpenAI provider implementation | ✅ Ready | `OpenAiDocumentExtractionProvider.cs` (1185 lines) | None |
| T3 | Azure Document Intelligence provider | 🔴 Not implemented | Config placeholder only | Implement if Corporate IT requires |
| T4 | Settings cascade (DB → config → defaults) | ✅ Ready | `DocumentExtractionSettingsService.cs` | None |
| T5 | Admin settings UI | ✅ Ready | `DocumentExtractionSettings.tsx` | None |
| T6 | Connection testing | ✅ Ready | `TestConnectionAsync()` | None |
| T7 | Contract OCR entities + audit fields | ✅ Ready | `ContractOcrExtractionRecord.cs`, `ContractOcrExtractedField.cs` | None |
| T8 | Invoice OCR entities | ✅ Ready | `OcrExtractedItem.cs` | None |
| T9 | Frontend OCR components (Contract) | ✅ Ready | 6 components in `pages/Contracts/ocr/` | None |
| T10 | Frontend OCR hooks | ✅ Ready | `useContractOcr.ts`, `useOcrProcessor.ts` | None |
| T11 | Upload validation pipeline | ✅ Ready | `AttachmentsController.cs` — whitelist, blocklist, size, MIME, hash | None |
| T12 | Extraction result DTOs | ✅ Ready | `ExtractionResultDto.cs` (8.6KB) | None |
| T13 | Field propagation standard | ✅ Ready | `DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md` | None |
| T14 | API key management (AES-256) | ✅ Ready | `AesEncryptionHelper.cs`, `IntegrationConfigResolver` | None |
| T15 | Correlation ID tracing | ✅ Ready | `CorrelationIdMiddleware.cs` | None |
| T16 | Admin audit logging | ✅ Ready | `AdminLogWriter.cs`, `SafePayload.cs` | None |
| T17 | Prompt template versioning | ✅ Done (G3) | `InvoicePromptVersion`, `ContractPromptVersion` constants; logged in `OCR_EXTRACTION_STARTED` | None |
| T18 | JSON schema validation | 🔴 Missing | Only JSON deserialization | Add strict schema |
| T19 | Per-module feature flags | ✅ Done (G2) | `AiOcrPolicy.AllowedModules`, `AllowedDocumentTypes` enforced in `DocumentExtractionService` with audit log | None |
| T20 | Debug logging environment guard | ✅ Done (G1) | `IsDebugLoggingAllowed()` dual check: `IsDevelopment()` + `DebugRawPayloadLogging=true` (default: false) | None |

---

## 2. Governance Readiness

| # | Item | Status | Evidence | Remaining Action |
|:---|:---|:---|:---|:---|
| G1 | AI Registry Number | 🔴 Missing | No registry entry found | Register with AI CoE |
| G2 | AI Product Owner assigned | 🔴 Missing | No assignment found | Management decision |
| G3 | Sponsor / Process Owner documented | 🔴 Missing | No documentation found | Management decision |
| G4 | Data Owner assigned | 🔴 Missing | No assignment found | Management decision |
| G5 | Risk classification approved | 🔶 Proposed | This package proposes Limited-Risk | AI CoE approval |
| G6 | Quick Check AI Project completed | 🔴 Missing | Not found | Complete template |
| G7 | Business Data Science Canvas completed | 🔴 Missing | Not found | Complete template |
| G8 | Quick Check AI Supplier completed | 🔴 Missing | Not found | Complete for OpenAI/Azure |
| G9 | Quick Check AI Architecture completed | 🔴 Missing | Not found | Complete template |
| G10 | AI CoE formal review | 🔴 Missing | No review conducted | Submit package for review |
| G11 | Legal/privacy review | 🟣 Unconfirmed | No evidence in repo | Confirm with Legal |
| G12 | Supplier approval (Corporate IT) | 🟣 Unconfirmed | No evidence in repo | Confirm with Corporate IT |
| G13 | DPA with AI provider | 🟣 Unconfirmed | No DPA found in repo | Confirm with Legal |
| G14 | SCC/TIA for cross-border data | 🟣 Unconfirmed | No SCC/TIA found | Confirm with Legal |
| G15 | Data residency confirmation | 🟣 Unconfirmed | No region config | Confirm with Legal/IT |

---

## 3. Security Readiness

| # | Item | Status | Evidence | Remaining Action |
|:---|:---|:---|:---|:---|
| S1 | JWT authentication | ✅ Ready | `[Authorize]` on all controllers | None |
| S2 | RBAC enforcement | ✅ Ready | Role checks + scoping | None |
| S3 | API key encryption (AES-256) | ✅ Ready | `AesEncryptionHelper.cs` | None |
| S4 | No secrets in frontend | ✅ Ready | Backend-only AI calls | None |
| S5 | Upload whitelist/blocklist | ✅ Ready | Config-driven lists | None |
| S6 | File size limits | ✅ Ready | 15MB configurable | None |
| S7 | Filename sanitization | ✅ Ready | Regex + truncation | None |
| S8 | SHA-256 hashing | ✅ Ready | Deduplication | None |
| S9 | Log payload sanitization | ✅ Ready | `SafePayload.cs` | None |
| S10 | Malware scanning | 🔶 Extension Point (G5) | `IFileScanService` + `NoOpFileScanService` registered | Integrate real AV before unrestricted production |
| S11 | Prompt injection defense | ✅ Done (G3) | Security preamble on both prompts + test sample | Execute test sample in PoC |
| S12 | Output schema validation | 🔴 Missing | JSON deser only | Add strict validation |
| S13 | Outbound network control | 🔴 Missing | Direct internet | Add proxy/allowlist for prod |
| S14 | MIME hard block | 🔶 Soft check | Warning logged only | Make blocking |
| S15 | API key rotation | 🔴 Missing | Manual only | Add rotation mechanism |

---

## 4. Operational Readiness

| # | Item | Status | Evidence | Remaining Action |
|:---|:---|:---|:---|:---|
| O1 | Monitoring dashboard | 🔴 Missing | No dashboards | Build dashboard with KPIs |
| O2 | AI incident handling process | 🔴 Missing | No formal process | Define process document |
| O3 | Support/escalation process | 🔴 Missing | No AI-specific support process | Define with IT operations |
| O4 | Retention/cleanup policy | 🔶 Technical (G4) | `OcrCleanupService` + `RetentionPolicyOptions` configured; disabled until Legal confirms | Confirm periods with Legal |
| O5 | Cost monitoring | 🔶 Partial | `TotalTokensUsed` tracked (contracts) | Add cost estimation + budget alerts |
| O6 | Provider outage fallback | 🔶 Partial | Manual entry works; no auto failover | Acceptable for Limited-Risk |
| O7 | Backup/recovery for AI data | ✅ Ready | SQL Server backup covers AI entities | None (standard DB backup) |
| O8 | Environment separation | ✅ Ready | DEV/TEST/PROD config, banners | None |
| O9 | Deployment checklist | ✅ Ready | `docs/DEPLOYMENT_CHECKLIST.md` exists | Add AI-specific items |
| O10 | User training/documentation | 🔴 Missing | No end-user OCR guide | Create user guide |

---

## 5. Final Go/No-Go Criteria

### ✅ Ready for Controlled PoC

All of the following must be true:

| # | Criterion | Current Status |
|:---|:---|:---|
| 1 | Provider abstraction implemented | ✅ |
| 2 | At least one provider functional | ✅ (OpenAI) |
| 3 | Human oversight UI implemented | ✅ |
| 4 | Upload security controls active | ✅ |
| 5 | Audit logging operational | ✅ |
| 6 | Test environment available | ✅ |
| 7 | Debug logging env-guarded | ✅ Done (G1) |
| 8 | Prompt injection basic defense | ✅ Done (G3) |

**Verdict**: ✅ Ready for controlled PoC

---

### 🔶 Ready for Limited Production (Current Scope Only)

All PoC criteria PLUS:

| # | Criterion | Current Status |
|:---|:---|:---|
| 9 | AI CoE registration completed | 🔴 **Required** |
| 10 | AI Product Owner assigned | 🔴 **Required** |
| 11 | Supplier approval confirmed | 🟣 **Required** |
| 12 | DPA confirmed | 🟣 **Required** |
| 13 | Malware scanning implemented | 🔶 Extension point (G5) |
| 14 | Retention policy defined | 🔶 Technical ready (G4) |
| 15 | Monitoring dashboard operational | 🔴 **Required** |
| 16 | Prompt injection defense hardened | ✅ Done (G3) |

**Verdict**: 🔶 Conditional — governance confirmations required

---

### 🔴 Ready for Full Production Rollout

All Limited Production criteria PLUS:

| # | Criterion | Current Status |
|:---|:---|:---|
| 17 | All Quick Checks completed | 🔴 |
| 18 | AI CoE formal review passed | 🔴 |
| 19 | Legal/privacy review completed | 🟣 |
| 20 | Data residency confirmed | 🟣 |
| 21 | Incident handling process defined | 🔴 |
| 22 | User training completed | 🔴 |
| 23 | All PoC tests passed | ⬜ |
| 24 | Cost monitoring active | 🔴 |
| 25 | API key rotation implemented | 🔴 |

**Verdict**: 🔴 Not ready — significant gaps remain

---

### ⛔ Blocked

The feature MUST be blocked if:

| Condition | Current Status |
|:---|:---|
| Corporate IT explicitly rejects OpenAI as provider | 🟣 Unknown — must confirm |
| Legal determines DPA/SCC cannot be established | 🟣 Unknown — must confirm |
| AI CoE classifies as High-Risk requiring Decision Card | 🟣 Possible |
| Processed documents contain prohibited data categories | 🟣 Unknown — must confirm |

---

## 6. Recommended Progression Timeline

| Phase | Estimated Effort | Dependencies |
|:---|:---|:---|
| **Phase 1: PoC Hardening** (T17–T20, S11) | ✅ COMPLETED (G1–G8) | None |
| **Phase 2: Governance Submission** (G1–G9) | 1–2 weeks business | Management + AI CoE |
| **Phase 3: Legal/IT Confirmation** (G11–G15) | 2–4 weeks | Legal + Corporate IT |
| **Phase 4: Security Hardening** (S10, S12–S15) | 3–5 days development | IT Security |
| **Phase 5: Operational Readiness** (O1–O10) | 1–2 weeks | DevOps + IT Operations |
| **Phase 6: Full Production Sign-Off** | After all phases | AI CoE + Management |

---

## Evidence Package References

| Area | Evidence Files |
|:---|:---|
| Build validation | `evidence/build/backend-build-result.md`, `evidence/build/frontend-build-result.md` |
| Configuration evidence | `evidence/configuration/document-extraction-settings-redacted.md` |
| Debug logging (G1) | `evidence/code-references/G1-debug-logging-guard.md` |
| System Logs (G8) | `evidence/code-references/G8-system-logs-integration.md` |
| PoC test status | `evidence/test-results/poc-test-execution-status.md` |
| Screenshot guide | `evidence/screenshots/SCREENSHOT_CAPTURE_GUIDE.md` |

> 👉 Full evidence index: [`evidence/EVIDENCE_INDEX.md`](evidence/EVIDENCE_INDEX.md)
