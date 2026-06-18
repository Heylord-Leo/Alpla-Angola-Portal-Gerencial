# AI Risk Classification — Portal Gerencial OCR Feature

> **Version**: 1.0 | **Date**: 2026-06-18 | **Status**: PROPOSED — Requires AI CoE Approval

---

## 1. Proposed Classification: Limited-Risk AI System

### Rationale

The Portal Gerencial AI-assisted OCR feature is classified as **Limited-Risk** based on the following analysis:

| Factor | Assessment | Justification |
|:---|:---|:---|
| **User Interaction** | AI interacts with users | Users upload documents and receive AI-generated suggestions |
| **Content Generation** | AI generates/extracts content | Extracted data is presented as structured fields |
| **Human Review** | Mandatory before persistence | Users must confirm, edit, or reject each AI suggestion |
| **Autonomous Decision-Making** | None | AI does not approve, reject, or execute any business action |
| **Data Sensitivity** | Business documents (invoices, contracts) | Not health, biometric, or criminal data |
| **Impact of Errors** | Correctable | User reviews all values; errors are caught before persistence |
| **Scope** | Internal business tool | Used only by authenticated employees within Portal Gerencial |

---

## 2. Mandatory Transparency Obligations (Limited-Risk)

Under Limited-Risk classification, the following obligations apply:

| Obligation | Current Status | Evidence |
|:---|:---|:---|
| Inform users they are interacting with AI | ✅ Implemented | OCR badges, Sparkles icon, caution banners |
| Mark AI-generated content as such | ✅ Implemented | "OCR" labels, amber borders, confidence percentages |
| Allow users to review and override | ✅ Implemented | Confirmar/Limpar/Aplicar/Ignorar actions |
| Document the AI system | 🔶 In Progress | This compliance package |

---

## 3. Human Oversight Requirement

| Requirement | Implementation |
|:---|:---|
| No AI output persisted without human action | ✅ `ConfirmedByUser` required before field values are saved to `Contract` entity |
| User can edit AI suggestions | ✅ `WasOverridden` and `FinalSavedValue` fields track edits |
| User can reject AI suggestions | ✅ `DiscardedByUser` flag; "Limpar"/"Ignorar" buttons |
| Manual fallback available | ✅ OCR is optional; all fields can be entered manually |
| Unconfirmed fields excluded on save | ✅ `ContractOcrCautionBanner` warns about unconfirmed AUTO_FILL fields |

---

## 4. Escalation Triggers to High-Risk

The classification MUST be escalated to **High-Risk** if ANY of the following conditions occur:

| # | Trigger Condition | Current Status | Action Required |
|:---|:---|:---|:---|
| E1 | HR/payroll/health data processed via AI | ⚪ Not applicable — OCR processes business documents only | Monitor scope changes |
| E2 | Secret/confidential corporate know-how processed | 🟣 Unknown — depends on document classification | Confirm with Information Security |
| E3 | AI output used for automatic financial posting | ⚪ Not applicable — human confirmation required | Prevent auto-posting design |
| E4 | AI output used for automatic approval decisions | ⚪ Not applicable — separate approval workflow | Prevent auto-approval design |
| E5 | Supplier/customer-impacting decisions without human review | ⚪ Not applicable — suggestions only | Monitor feature evolution |
| E6 | Non-approved external data transfer (e.g., to non-EU) | 🟣 Unknown — OpenAI processes in US by default | Confirm with Legal |
| E7 | Critical operational dependency without fallback | 🔶 Low risk — manual fallback exists | Maintain manual fallback |
| E8 | Processing legally privileged documents (attorney-client) | 🟣 Unknown — depends on contract types | Confirm with Legal |
| E9 | Processing government-classified documents | ⚪ Not applicable | N/A |
| E10 | AI used for employee evaluation or scoring | ⚪ Not applicable — OCR only | N/A |

---

## 5. Unacceptable Risk Conditions

The AI feature MUST be immediately disabled if ANY of the following conditions are detected:

| # | Condition | Monitoring |
|:---|:---|:---|
| U1 | No human oversight — AI output directly saved without user action | Code review: verify `ConfirmedByUser` gate exists |
| U2 | Deceptive AI behavior — AI output presented as human-generated | UX review: verify AI labels/badges are present |
| U3 | Processing prohibited sensitive data without explicit approval | Document classification + admin controls |
| U4 | AI provider uses submitted data for model training without consent | DPA review + Legal confirmation |
| U5 | AI system makes safety-critical decisions (e.g., equipment safety) | Scope review |

---

## 6. Risk Register

| # | Risk | Severity | Likelihood | Current Mitigation | Gap | Required Action | Owner |
|:---|:---|:---|:---|:---|:---|:---|:---|
| R1 | **Direct OpenAI API — data leaves corporate boundary** | 🔴 High | Certain (currently active) | Provider abstraction enables switch | No Azure provider | Evaluate Azure OpenAI migration | Corporate IT |
| R2 | **No AI CoE registration** | 🔴 High | Certain | None | No registration | Register with AI CoE | AI Product Owner |
| R3 | **No DPA/SCC/TIA** | 🔴 High | Uncertain (may exist outside repo) | None visible in repo | No evidence | Confirm with Legal | Legal |
| R4 | **Debug raw JSON logging to disk** | 🟡 Medium | Only in development | `.gitignore` covers `debug/` dir | Active in dev | Add env guard | Dev Team |
| R5 | **Raw JSON in DB (`RawJsonResult`)** | 🟡 Medium | Certain (stored per extraction) | 64KB truncation, not API-exposed | No encryption, no retention | Encrypt + retention policy | Dev Team + Legal |
| R6 | **No malware scanning** | 🟡 Medium | Low (whitelist + blocklist exist) | Extension/MIME validation | No content scanning | Integrate AV engine | IT Security |
| R7 | **Prompt injection via document content** | 🟡 Medium | Low but increasing | System prompt instructs JSON-only | No explicit defense | Add injection guardrails | Dev Team |
| R8 | **No data retention policy** | 🟡 Medium | Certain (records accumulate) | None | Indefinite storage | Define retention period | Legal + Dev |
| R9 | **Single provider dependency** | 🟡 Medium | Certain | Manual fallback exists | No automatic failover | Implement 2nd provider | Dev Team |
| R10 | **No monitoring/alerting** | 🟡 Medium | Certain | `AdminLogWriter` captures events | No dashboards or alerts | Build monitoring | DevOps |
| R11 | **JWT secret in appsettings.json** | 🟡 Medium | Low (only in repo-committed base config) | `appsettings.Development.json` gitignored | Base config has default key | Use env vars for prod secrets | Dev Team |
| R12 | **No per-request cost tracking (invoices)** | 🟢 Low | Certain | Contract flow tracks `TotalTokensUsed` | Invoice flow lacks token tracking | Add token tracking | Dev Team |
| R13 | **No API key rotation mechanism** | 🟢 Low | Low | Manual key update via admin UI | No automated rotation | Add rotation reminders | Dev Team |
| R14 | **No confidence threshold auto-reject** | 🟢 Low | Low | User reviews all fields | Low-quality suggestions shown | Add configurable threshold | Dev Team |

---

## 7. Risk Mitigation Priority

### Critical (Must address before production sign-off)

1. **R1** — Evaluate and potentially migrate to Azure OpenAI
2. **R2** — Register with AI CoE
3. **R3** — Confirm DPA/SCC/TIA status

### High (Must address before controlled PoC)

4. **R4** — Disable debug logging in non-dev environments
5. **R7** — Add prompt injection defenses

### Medium (Address during hardening phase)

6. **R5** — Encrypt `RawJsonResult` and define retention
7. **R6** — Integrate malware scanning
8. **R8** — Define data retention policy
9. **R10** — Build monitoring dashboard

### Low (Recommended improvements)

10. **R9** — Implement Azure Document Intelligence provider
11. **R12–R14** — Operational improvements

---

## 8. Post-Hardening Status Update (G1–G8)

| Risk ID | Pre-Hardening Status | Post-Hardening Status | Evidence |
|:---|:---|:---|:---|
| R4 | 🟡 Active in dev | ✅ **Resolved** — dual guard implemented | `evidence/code-references/G1-debug-logging-guard.md` |
| R5 | 🟡 No retention | 🟡 Cleanup service ready, disabled pending Legal | `evidence/code-references/G4-retention-cleanup-service.md` |
| R6 | 🟡 No scanning | 🟡 Extension point ready (`IFileScanService`) | `evidence/code-references/G5-malware-scan-extension.md` |
| R7 | 🟡 No defense | ✅ **Resolved** — security preamble v2.1-hardened | `evidence/code-references/G3-prompt-injection-defense.md` |
| R10 | 🟡 No monitoring | 🟡 8 `OCR_*` events via `AdminLogWriter` | `evidence/code-references/G8-system-logs-integration.md` |

> 👉 Full evidence index: [`evidence/EVIDENCE_INDEX.md`](evidence/EVIDENCE_INDEX.md)

