# Manual TEST validation of v2.249.7 — remaining findings (v2.249.8)

> Findings of the manual TEST validation of **v2.249.7** (`f9e0df2`) on the synthetic request **REQ-03/11/2026-630**
> (id `c9046cea-02d1-4123-a3a0-7e5a7e99133f`, LUANDAY, NIF 5417010871, approved 61.560,00 AOA). Published as **v2.249.8**
> on `Portal-Gerencial-rev1` (commit and push only: **not deployed; TEST/PROD not accessed; no request, attachment or
> environment data changed** — REQ-595, REQ-628, REQ-629, REQ-630, REQ-631 untouched). **No migration.**
>
> What the operator already validated on v2.249.7 and is NOT re-claimed here: REQ-629 line discounts preserved to the P.O.
> group at 345.775,50; REQ-630 genuine global discount allocated once, residual 0 without justification, 61.560,00 preserved
> through approvals, P.O. registration, Finance and invoice coverage; REQ-631 unallocated global discount saved but excluded from
> submission; duplicate-discount draft (54.720 vs 61.560, residual 6.840, mandatory justification, save disabled) as UI evidence
> only; ECF10 recognition and correct-number registration; **and the manual pre-allocation submission block, PASSED on
> REQ-631**: the quotation with an undistributed global discount was saved, the submission screen showed zero eligible items,
> explained that the global discount required distribution, and kept the submit button disabled. **Still pending, not
> passed:** browser verification of the three changes below, ECF and ECF11 recognition, and the validity-date extraction (the
> original date discrepancy has **no confirmed cause**).

## 1. Finding 1 — P.O. number divergence missing from the registration wizard

### 1.1 Reproduction (operator) and trace (code, `f9e0df2`)

Registration wizard ("Registrar P.O.") on REQ-630: the extraction of `PO_TEST_LUANDAY_ECF10_2026_9630_REQ630.pdf` identified
**ECF10 2026/9630**, the supplier, the amount and POST_PAID (green verdict). The Buyer typed **"AAAAAA"** over the number: the field
showed "Introduzido manualmente", kept the extracted number as a suggestion with "Usar número extraído", **but** no divergence
warning appeared, the comment stayed optional and "Registar P.O." stayed enabled. The mismatched number was not submitted.

Trace:

- `RegisterPoModal.runOcrValidation` computes the client divergence set **once, at extraction**, from
  `buildOcrMismatchResult(extractedTotal, expectedTotal, extractedSupplier, expectedSupplier)` plus the P.O.-reference warnings —
  the **number is never compared**, and nothing recomputes the set when the number input changes. The divergence panel, the
  acknowledgement checkbox, the mandatory comment and the submit gate all read that extraction-time set (`ocrResult.hasMismatches`).
  `CorrectPoModal` has the same shape; its only number rule is `numberDecisionPending` (current vs detected, before a choice).
- The **server** (`RequestsController.RegisterPo`, rule (4)) compares the submitted number with its own evidence
  (`PoDocumentExtraction` bound to the request and to the registered file's SHA-256): canonical identity for an IDENTIFIED
  document (`PrimaveraPoReference.TryParse(submitted)?.Canonical == identification.Canonical`), loose comparison with the raw
  number read for an UNCERTAIN document (letter-bearing, non-fiscal). A difference joins `backendMismatches` → **`400
  OCR_MISMATCH`** unless `OverrideConfirmed`, and **`400 Ação Bloqueada`** when the override has no Comment; the override is audited
  in the registration history ("Divergência OCR confirmada pelo comprador. Divergências: Número de P.O. divergente: introduzido
  "AAAAAA" (Identificado no documento: ECF10 2026/9630). Justificativa: …").

**Root cause (confirmed):** the client never evaluated the number divergence (not at extraction, not from the current input), so
the wizard could not warn, require the acknowledgement or disable the button; the server rule existed and would have refused the
submission — but only after the file upload, through the "Divergência Detectada (SISTEMA CENTRAL)" branch, and this was not
exercised manually. Identification and registration consistency were also conflated on screen: the only place a number mismatch
could show was the verdict block, which legitimately stayed green (the document **is** the identified P.O.).

### 1.2 Change

- **Shared rule, client mirror of the server rule (4)** — `lib/poDocumentIdentification.describePoNumberDivergence(typed,
  identification, detectedNumber)`: IDENTIFIED_PO → the typed value must parse to the same canonical identity (formatting
  differences are not divergences: `ecf10 2026-9630` = `ECF10 2026/9630`); UNCERTAIN with a letter-bearing, non-NIF reading → loose
  comparison (`normalizePoNumberLoose`, mirror of `PrimaveraPoReference.NormalizeLoose`); blank input / no verdict / refused
  document / NIF-shaped or numeric reading / no reading → nothing to compare. The detail line uses the server's wording.
  `withNumberDivergence(ocrResult, divergence)` builds the **reviewed set** = extraction-time divergences + the number line.
- **Both wizards evaluate it from the current input on every render** (`numberDivergence`, `reviewMismatches`) and feed the reviewed
  set — not the extraction-time set — into `describeDivergenceControls` (acknowledgement vs diagnostic-only), the submit gate, the
  pre-submit checks, the mandatory comment, the divergence panel and the payload (`hasMismatches`, `mismatchDetails`; the extraction
  facts `extractedSupplierName` / `extractedTotalAmount` still come from the extraction). `CorrectPoModal` passes the reviewed set
  to `resolveJustificationPolicy` and `validateCorrectPoSubmission`; the pending number decision is still asked first.
- **Inline warning in the number field** (`PoNumberFieldBlock`, `data-testid="po-number-divergence"`, `role="alert"`): "Número
  introduzido diferente do número do documento — Introduzido: AAAAAA · Identificado no documento: ECF10 2026/9630", the consequence
  (acknowledgement + justification, audited) and a "Usar número extraído" shortcut. Hidden while the correction's number decision is
  pending (that box asks first). The plain suggestion row yields to the warning while divergent.
- **Identification stays separate from registration consistency**: the verdict block keeps its green "Ordem de Compra identificada"
  and adds one amber line (`data-testid="po-document-number-divergence"`) saying the typed number differs from the document's and
  that the identification does not change.
- **Restoring the extracted number clears exactly that divergence** (pure recomputation: the number line disappears from the reviewed
  set; amount / supplier divergences and their acknowledgement are untouched). Keeping a different number requires the existing
  "Estou ciente das divergências…" checkbox **and** a justification — the same policy the server enforces.
- Unchanged: definitive quotation / proforma refusal (`DOCUMENT_NOT_PURCHASE_ORDER`, diagnostics only), uncertain-document
  confirmation, amount / supplier / currency checks, duplicate-number rules, previous-number reuse, prior-ECF block, the correction's
  number decision. The server was not changed.

### 1.3 Server enforcement proven (real endpoint, server-held evidence bound to the exact file)

`tests/backend/AlplaPortal.Application.Tests/Services/Purchasing/RegisterPoNumberDivergenceTests.cs` (InMemory, real
`RegisterPo` action, `PoDocumentExtraction` rows with the registered file's hash; supplier / amount / currency matching so the
**only** divergence is the number):

| Case | Result | State after |
|---|---|---|
| Initial registration (group WAITING_PO, no number, no file): typed `AAAAAA`, document identified `ECF10 2026/9630`, no override | `400 OCR_MISMATCH`, detail = exactly `Número de P.O. divergente: introduzido "AAAAAA" (Identificado no documento: ECF10 2026/9630)` | group status / number / current-document pointer / payment condition / responsible buyer / request status / history count / files unchanged; no REREGISTER_PO / PO_NUMBER_CORRECTED |
| Same, `OverrideConfirmed` without a Comment | `400 Ação Bloqueada` | unchanged |
| Same, override + justification | `200`; group PO_ISSUED with the typed number, pointer = the file; REGISTER_PO audited with "Divergência OCR confirmada…", the number line, the justification **and** "Documento identificado como P.O. ECF10 2026/9630" (separate facts) | — |
| Extracted number in any formatting (`ECF10 2026/9630`, `ecf10 2026-9630`, padded) | `200`, no "Divergência" in the audit | — |
| Typed `AAAAAA` against a file with **no evidence** (another file was extracted) | `400 DOCUMENT_TYPE_CONFIRMATION_REQUIRED`, `evidence=NONE`, no number divergence (the evidence is bound to the exact file) | unchanged |
| Correction (group WAITING_PO_CORRECTION, current `ECF10 2026/364`, replacement identified `ECF10 2026/9630`): typed MANUAL `AAAAAA`, no override | `400 OCR_MISMATCH` | number stays `ECF10 2026/364`, status, pointer, old file not superseded, history unchanged |
| Correction, override + justification | `200`; number `AAAAAA`, old file superseded, REREGISTER_PO audited with the divergence, PO_NUMBER_CORRECTED `'ECF10 2026/364' → 'AAAAAA'` (MANUAL) | — |
| Correction with the extracted number | `200`, no divergence, PO_NUMBER_CORRECTED `'ECF10 2026/364' → 'ECF10 2026/9630'` | — |

10 tests (7 facts + 3 theory cases), all passing. The pre-existing `RegisterPoDocumentIdentificationTests` (stale current number on
a correction = divergence; detected number adopted) keep passing.

### 1.4 Limitations

- Browser verification of the inline warning, the panel and the button state is **pending** (no browser tools here); the behaviour
  is pinned by source-level and pure tests (`poDocumentIdentification.test.ts`, `RegisterPoModal.refusal.test.ts`,
  `correctPoSubmission.test.ts`).
- The client rule is a mirror: the server remains authoritative (an older client still gets `400 OCR_MISMATCH` → the existing
  "SISTEMA CENTRAL" branch).

## 2. Finding 2 — zero item prices in the Print View

### 2.1 Trace and root cause (confirmed)

`requestPrintModel.mapLineItem` printed `Preço unit.` = `RequestLineItemDto.unitPrice` and `Total` = `totalPrice ?? totalAmount` —
the **request line's own values**. For a QUOTATION request the requester is not asked for a price: `RequestCreate` sends
`unitPrice: 0, totalAmount: 0` for every requester item (`LineItemFactory` stores them as given), and nothing in the approval or
P.O. flow ever writes a request line's price (the only writers are line creation and the requester's/Buyer's line edit). So the
"ITENS" section of REQ-630 showed 0,00 / 0,00 while the approved purchase (51.300,00 + 10.260,00 = 61.560,00) lives in the
**APPROVED batch's winning candidate snapshots** (`ApprovalBatchItemCandidate`: unit price, line total, currency, supplier, frozen at
submission) — which the print never read. They are the requester's original (empty) estimates, **not** an incorrect read of
approved prices; printing them unlabelled as "Preço unit. / Total" was the defect.

### 2.2 Change (presentation only; no data written, no snapshot recalculated)

- The requester's figures are printed **only when given** (> 0) and labelled **"Preço unit. (estimativa do solicitante)" / "Total
  (estimativa do solicitante)"**; a 0 placeholder is omitted (a 0 is not a price).
- The approved value per line is derived from the authoritative data (`resolveApprovedLineValues`): the **latest APPROVED batch**
  holding the line, **released items excluded** (a P.O.-group cancellation that returned the line to quotation), the **selected
  candidate** (`selectedCandidateId`, fallback `isWinner`) in **its own currency**; legacy pre-candidate batches fall back to the
  Buyer-selected quotation item (`selectedQuotationItemId`, submission-locked); an item without a resolvable winner prints no
  approved value. Fields: "Preço unit. aprovado", "Total aprovado (c/ IVA)", "Fornecedor aprovado", "Lote de aprovação #n". A line
  closed by a CANCEL_ITEMS cancellation (`CANCELLED_AFTER_APPROVAL`) keeps its value labelled "(histórico — P.O. cancelada)".
- Never a group total divided over lines, never a cross-currency sum (the model still exposes no total field).
- A line with neither an estimate nor an approved value prints "Valores: Sem estimativa do solicitante; sem valor aprovado para esta
  linha" instead of 0,00.

Tests: `requestPrintModel.test.ts` "Print View v2.249.8 — line values" (REQ-630 shape; estimate > 0; batch in approval / rejected /
released item; CANCEL_ITEMS historical label; latest APPROVED batch wins and currencies stay per line; legacy winner; inputs
untouched). Existing print tests unchanged and passing.

## 3. Finding 3 — quotation validity date discrepancy

### 3.1 Confirmed facts (code, `f9e0df2`)

- The wizard's "Data de Vencimento da Cotação" (document step) and "Data Validade" (final review) are the same draft field,
  `OcrDraft.dueDate`. It is filled from `headerSuggestions.dueDate` through `resolveDateAlias` in `useOcrProcessor` — a deterministic
  normaliser (YYYY-MM-DD unchanged; dd/MM/yyyy, dd-MM-yyyy, dd.MM.yyyy and yyyy/MM/dd re-encoded; anything else returned trimmed).
  **No default, no "today", no timezone conversion** on this path (`new Date()` is not involved; `documentDate` is the only date the
  save converts with `toISOString()`, and only at save time).
- `ExtractionMapper` copies `Header.DueDate` **verbatim** (a string) from the provider; `OpenAiDocumentExtractionProvider` copies
  the model's `dueDate` string. The prompt defined `dueDate` as **"Payment due date … null if the document states none"** — a
  *payment* semantics; the quotation's **validity** date was never asked for. The model is instructed to return YYYY-MM-DD.
- The value is **wizard-only**: `buildQuotationPayload` does not send it, `SaveQuotationRequestDto` / `Quotation` have no such field,
  and the edit hydration (`buyerQuotationWizardController`) does not restore it (the Buyer must re-enter it when editing). It
  therefore affects nothing stored and nothing approved.
- The final review printed the draft string verbatim (no conversion); the document step uses a native `<input type="date">`, whose
  display format is the browser's (dd/MM/yyyy on pt locales).

### 3.2 Hypotheses (not reproduced — no provider access locally; stated as such)

With the deterministic path proven stable (tests below), the remaining sources of a different date between imports of the same PDF
are: (a) **provider variance** — a free-text model answering a *payment-due-date* question on a document that prints a *validity*
(e.g. reading "validade 30 dias" and adding days to the document date, or picking another date), which differs run to run; (b) a
**manual entry** on one of the imports (the field is mandatory to advance and empty on edit). Neither is claimed as the cause.

### 3.3 Change

- **Prompt semantics (backend, `OpenAiDocumentExtractionProvider`)**: `dueDate` is now defined per document type — invoice /
  proforma / receipt: the printed payment due date; **quotation / proposal: the printed validity (expiry) date** ("Validade",
  "Válido até", "Valid until") — strictly YYYY-MM-DD, **copied, never derived**: a validity or term expressed only as a number of
  days returns `null`; never add days to the document date, never use today's date. Provider behaviour cannot be verified locally
  (manual retest §3.12 of the test script; `DocumentDateExtractionTests` still cover the parsing of what the provider returns).
- **Pure, tested date alias** (`lib/ocrDateAlias.resolveOcrDateAlias`, used by `useOcrProcessor`): same rules, plus an ISO date-time
  ("2026-11-30T00:00:00", "…Z") keeps its calendar day by slicing (a `Date` round-trip on a UTC+1 browser could move it). Tests pin:
  no default, no clock, no day shift.
- **Provenance**: `OcrDraft.dueDateSource` = `EXTRACTED` when the extraction returned a value, `MANUAL` when the Buyer types it
  (`updateDraftHeader`), undefined when none. Document step: field relabelled **"Validade da Cotação (Data de Vencimento)"** with the
  line "Lida do documento pela extração — confirme com a validade impressa." / "Introduzida manualmente." and "Informativa: não
  fica guardada com a cotação." Final review: **"Validade da cotação"**, value verbatim, provenance + scope underneath.
- Not changed: the field stays mandatory to advance (existing rule); it is still not persisted (adding it to the quotation would be a
  migration and a separate decision).

## 4. Files

Backend: `src/backend/AlplaPortal.Infrastructure/Services/Extraction/OpenAiDocumentExtractionProvider.cs` (prompt only);
`tests/backend/AlplaPortal.Application.Tests/Services/Purchasing/RegisterPoNumberDivergenceTests.cs` (new).
Frontend: `lib/poDocumentIdentification.ts` (+ test), `components/po/PoDocumentReviewBlocks.tsx`, `components/RegisterPoModal.tsx`,
`components/CorrectPoModal.tsx`, `components/RegisterPoModal.refusal.test.ts`, `lib/correctPoSubmission.test.ts`,
`pages/Requests/components/print/requestPrintModel.ts` (+ test), `lib/ocrDateAlias.ts` (new, + test), `hooks/useOcrProcessor.ts`,
`pages/Buyer/QuotationWizard/hooks/useQuotationWizardState.ts`, `WizardStepDocumentsOcr.tsx`, `WizardStepFinalReview.tsx`,
`pages/Buyer/QuotationWizard/quotationValidityDateWiring.test.ts` (new), `types/index.ts`.
Docs: this file, `docs/DECISIONS.md` (DEC-155), `docs/PO_DOCUMENT_VALIDATION_v2.249.4.md` (§3 pointer, §11),
`docs/PO_GROUP_CANCELLATION_TEST_SCRIPT_v2.249.0.md` (§3.11 result of the pre-allocation block, §3.12),
`docs/QUOTATION_DISCOUNT_PRICING_AND_PO_WIZARD_REFUSAL_v2.249.6.md` (§9.10 result), CHANGELOG / VERSION / `config.ts` v2.249.8.

## 5. Checks (actual results, local, 2026-11-03)

| Check | Result |
|---|---|
| Backend `dotnet test` (full) | PASS 3352/3352 (1 m 14 s) — includes the 10 new endpoint tests |
| Frontend `vitest run` (full) | PASS 116 files / 1330 tests |
| `tsc -b` | clean |
| `vite build` | PASS (13.41 s) |
| `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| Migration files | none added or changed |

## 6. Migration

**None.** No entity, configuration or DTO persistence change; the P.O. number rule is client-side plus tests, the print change is
read-side, the validity-date provenance lives in the wizard draft only, the prompt is a string constant.

## 7. Manual retest

`docs/PO_GROUP_CANCELLATION_TEST_SCRIPT_v2.249.0.md` **§3.12** (pt-PT): registration and correction wizards with a mismatched
number (warning, acknowledgement, mandatory comment, button; API refusal with state unchanged; restore clears only that line), the
Print View "Itens" of REQ-630 (no 0,00; approved values per line), and the validity date on a fresh import of the same quotation PDF
(provenance line; compare with the printed validity; note the raw extraction value via the API).

## 8. Remaining limitations

- Browser verification of all three changes is pending; ECF / ECF11 recognition remains pending (not passed). The manual
  pre-allocation submission block **passed** on REQ-631 (v2.249.7) and is not re-claimed by this version.
- **The original validity-date discrepancy (30/11/2026 printed, 01/12/2026 once shown) has no confirmed cause.** The prompt
  semantics and the provenance display were improved; the provider's reading is not verifiable locally, the manual retest is
  the evidence. If the printed validity still differs after the retest, the raw `headerSuggestions.dueDate` of the extraction
  response (API, C.2) tells whether the provider or a manual entry produced it.
- The quotation validity date is still not persisted (by design today); persisting it is a separate decision with a migration.
- The OCR header evidence-authenticity limitation of v2.249.7 (§9.8 of the discount design doc) is unchanged.
