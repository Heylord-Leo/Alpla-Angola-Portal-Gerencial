# Quotation discounts in approved amounts and P.O.-wizard refusal state — investigation, decision and fixes (v2.249.6, unreleased)

> Source: manual TEST validation of **v2.249.5 (`a3f1b3d`)** on **REQ-21/10/2026-595** (LUANDAY - COMERCIO E SERVIÇOS, LDA,
> NIF 5417010871, proforma **PP ZL972200ZM2026/5044066758**, file `20261021_104430-LUANDAY.pdf`). Work done **locally** on
> `Portal-Gerencial-rev1`; nothing committed, pushed, deployed or run against TEST/PROD; no request or financial data touched.
> The PDF is **not** in the workspace: every statement about the document comes from the operator's transcription in the
> finding. This document is the implementation plan and the evidence record of the task (SOP: plan → implement → stop).

## 1. Finding 1 — commercial discounts absent from the approved amount

### 1.1 Figures (operator's transcription)

| Line | Qty | Unit price | Line discount | Net | IVA 5 % | Payable |
|---|---:|---:|---:|---:|---:|---:|
| AGUA MESA PURA MB 6X1,5L | 150 CX | 1.390,00 | 37.530,00 | 170.970,00 | 8.548,50 | 179.518,50 |
| AGUA MESA PURA MB 16X0,5L | 100 CX | 1.885,00 | 30.160,00 | 158.340,00 | 7.917,00 | 166.257,00 |
| Document | | | **67.690,00** | **329.310,00** | **16.465,50** | **345.775,50** |

Portal: quotation summary **345.775,50**; approval history item 1 **218.925,00**, item 2 **197.925,00**; batch and P.O. group
**416.850,00** (= 150 × 1.390 × 1,05 + 100 × 1.885 × 1,05, i.e. the undiscounted lines with IVA).

### 1.2 Trace of the pricing chain (code, `a3f1b3d`)

| Stage | Where | Discount handling |
|---|---|---|
| Extraction | `OpenAiDocumentExtractionProvider` prompt; raw lines persisted in `OcrExtractedItems` by `ocr-extract` | Per-line `discountAmount/discountPercent`; header `discountAmount` instructed to be the summary "Desconto Comercial" (**global**), with a note not to confuse it with line discounts — but no rule for the common case where the summary row is just the **sum** of the line discounts. |
| Draft building | `useOcrProcessor` ("Strategy A") | Replaced the extracted line discount by `gross − totalPrice` whenever they disagreed by > 1: **zeroes a real discount when the provider reads the gross into `totalPrice`** (silent, console only). Header discount copied to `draft.discountAmount` (global) unconditionally. |
| Wizard totals | `useQuotationWizardState.recalculateQuotationTotal` | Global discount reduces the taxable base and re-proportions IVA — **on the quotation total only**; line `totalPrice` never includes it. |
| Save / Update | `RequestsController.SaveQuotation/UpdateQuotation` | `QuotationItem.LineTotal = Round2(net_line + IVA_line)` (line discount only). `Quotation.DiscountAmount` (global) enters `TotalTaxableBase/TotalIvaAmount/TotalAmount` only. Reconciliation gate: residual anchored on header vs reconstructed lines. |
| Candidate submission | `ApprovalBatchCandidateSnapshotFactory` | Freezes `LineTotal`, `DiscountAmount` (line), `IvaAmount`, `GrossSubtotal`. **No field for a quotation-level discount.** |
| Area approval → groups | `GroupBuilderService` | `RequestPoGroup.TotalAmount = Σ winners' candidate LineTotal`. |
| Final approval | `BatchFinalApprove` | `ApprovedTotalAmount = Σ pending groups' TotalAmount`; `Request.ApprovedTotalAmount` cumulative. |

### 1.3 What is confirmed, what is not

**Confirmed root cause (model-level, reproduced locally).** A quotation-level discount is **unsupported by the per-line model
downstream of the quotation**: candidates, groups and approved totals are frozen per line and no line carries any share of
`Quotation.DiscountAmount`. Any quotation saved with the discount at quotation level (lines without discount) is approved at
the undiscounted line values while its card shows the discounted total. The synthetic fixture reproduces the TEST figures
exactly through the real endpoints (§4): `SaveQuotation` writes lines 218.925,00 / 197.925,00 and total 345.775,50; the
candidate snapshot sums 416.850,00; the approval path would freeze 416.850,00 (test 3 proves the correct shape freezes
345.775,50 end to end).

**Confirmed (write path).** The persisted shape (line discounts 0 + global 67.690,00) **cannot be written by the OCR flow
unchallenged when the OCR header total is present**: the reconciliation gate refuses it with `409 DOCUMENT_RESIDUAL_UNEXPLAINED`
(residual −71.074,50 — the document's header is inconsistent with a line baseline that carries no discount; test 1). It
reaches the database only through one of three human/data paths:

| Hypothesis | Mechanism | Persisted trace the diagnostic reads |
|---|---|---|
| H1 — provider never extracted the line discounts, put the summary 67.690,00 in the header | wizard: lines without discount, global 67.690,00; gate residual −71.074,50 → Buyer **accepted the residual** | `OcrExtractedItems.DiscountAmount = 0` on the raw rows; `RequestStatusHistories.QUOTATION_RESIDUAL_JUSTIFIED` with "desconto global=…" |
| H2 — provider extracted the line discounts but "Strategy A" zeroed them (gross read into `totalPrice`) | same as H1 downstream | raw rows carry `DiscountAmount` 37.530 / 30.160 **and** `QuotationItems.OcrOriginalDiscountAmount = 0` (the baseline is written from the wizard's resolved value) |
| H3 — line discounts arrived, the Buyer moved/removed them during manual review | per-line material change → `LineAdjustmentJustification` required | `OcrOriginalDiscountAmount` 37.530 / 30.160, final `DiscountAmount` 0, `LineAdjustmentJustification` set, or `QUOTATION_UPDATED` rows |
| H4 — no OCR header total (gate not applicable) | save unchallenged (test 1-bis) | `OcrOriginal*` present **and** no `QUOTATION_RESIDUAL_JUSTIFIED` row **and** no `LineAdjustmentJustification` / `QUOTATION_UPDATED` evidence; the raw rows (#4) read against the PDF's own summary. A missing audit row alone is **not** proof of H4 — the row can be absent for other reasons (H3 edits, an older build) |

Which of H1–H4 happened on REQ-595 is **not established** here (no TEST access, no PDF) and **stays open until the diagnostic
results discriminate it**. `scripts/db/quotation-discount-pricing-req595-readonly.sql` (guarded for `[Portal-Gerencial-Test]`,
SELECT-only) returns the discriminating rows (#3 lines with baseline, #4 raw OCR rows, #5 history) plus the batch/candidate/group
facts (#6, #7), the P.O.-wizard extraction of the proforma (#8) and the inventories (#9 global discount in approval, #10 line-sum
mismatch, #11 legacy zero totals). The operator holds the actual PDF (`20261021_104430-LUANDAY.pdf`): it must be compared column by
column with #4 (what the provider read) before any hypothesis is called confirmed; the figures in this document are the
operator's transcription, not an inspection of the file.

### 1.4 Decision (DEC-154) and fixes

1. **Line-resolved pricing is a precondition of approval** — new pure Domain rule `QuotationLinePricingPolicy`: a quotation is
   submittable only when it carries **no unallocated global discount** (`GLOBAL_DISCOUNT_UNALLOCATED`) and its considered lines
   (MAPPED / SUBSTITUTE / EXTRA_ITEM) add up to its `TotalAmount` within 1,00 (`LINE_TOTAL_MISMATCH`, legacy drift; a never-computed
   total of 0 is not judged). `CreateBatch` and `UpdateBatch` evaluate it **before any write** for every quotation behind a submitted
   candidate — whole or partial batch, create or rework — and answer `409 QUOTATION_PRICING_NOT_LINE_RESOLVED` with the per-quotation
   figures. Nothing is redistributed silently; the Buyer resolves it on the quotation.
2. **Discounts are counted once, at the line, and never dropped silently.** Draft building (`lib/quotationPricing`):
   `resolveOcrLineDiscount` keeps the extracted line discount and flags the line `discountUncertain` when the document's own line
   total contradicts it (the Buyer confirms in the document step before advancing — same gating as uncertain IVA); only a line
   extracted with no discount takes the discount the printed net line total implies (flagged too). `resolveOcrHeaderDiscount`
   treats the summary "Desconto Comercial" as the **aggregate** of the line discounts when it equals their sum (within 1,00) —
   not a second discount; otherwise it stays a document-level discount, explained in the step. The extraction prompt gains the
   amount-column and aggregate rules (text only).
3. **A genuine document-level discount is distributed explicitly, never automatically.** The document step shows the warning and
   the action **"Distribuir pelas linhas (proporcional ao valor líquido)"**: largest-remainder proportional shares that add up
   exactly, added to each considered line's discount, per-line adjustment reason pre-filled with the arithmetic and editable,
   global zeroed, totals recomputed by the server on save. The final review warns while the global discount is still there.
4. **One verdict everywhere.** `SavedQuotationDto.isLinePricingResolved / linePricingReasonCode / consideredLineTotalSum`
   (details read, Save/Update responses; the details projection now also carries the quotation's own totals incl. the global
   discount); cards show **"Desconto global não distribuído"**; the batch modal never offers such a quotation's lines (hint under
   the pending line) and explains the 409 if the server refuses anyway.
5. **No recalculation of history.** Candidates, groups and approved totals already written are untouched (§6).
6. **Arithmetic boundaries (follow-up, same version).** *Allocation (`allocateGlobalDiscountToLines` /
   `applyGlobalDiscountAllocationToDraft`, pure):* shares go only to considered lines (MAPPED / SUBSTITUTE / EXTRA_ITEM; a draft
   without statuses treats every line as considered) with a positive net; IGNORED / NOT_QUOTED and zero-value lines never receive
   a share; an amount above the eligible net is **refused** (nothing changes, the note says why — no line may go negative) and a
   share is by construction ≤ the line's net (re-checked after rounding); the transform zeroes the global discount, so a repeat
   press finds nothing to allocate and returns the same draft — the discount is never applied twice (the button also disappears);
   every line total and the draft total are recomputed with the server arithmetic. *Server (`SaveQuotation`, `UpdateQuotation`,
   `reconcile-preview`):* line totals are always recomputed from quantity, price, line discount and IVA (the DTO carries no line
   total at all; the header totals of the DTO are never persisted — a zero/omitted header cannot bypass the rule); a line discount
   above the line's gross is refused (400, both endpoints); a global discount above the net of the considered lines after their
   own discounts is refused with `400 QUOTATION_GLOBAL_DISCOUNT_EXCEEDS_NET` instead of being clamped into a zero taxable base.
   *Zero totals:* the policy classifies every evaluation (`Kind`, exposed as `linePricingKind`): `LINE_RESOLVED`, `ZERO_VALUE`
   (no considered value), `LEGACY_TOTAL_NOT_COMPUTED` (total 0 with positive lines — a legacy row the current write path cannot
   produce; still submittable for compatibility, approval values the lines; inventoried by #11), `GLOBAL_DISCOUNT_UNALLOCATED`,
   `LINE_TOTAL_MISMATCH`. *Mixed VAT rates:* a proportional-to-net allocation scales every line's IVA by the same ratio, so its
   total equals the global-discount arithmetic (16.024,84 in the fixture) — but a document that assigns the discount differently
   (e.g. the whole amount on the 14 % line: IVA 1.490,00, total 15.990,00) has a **different VAT and total**; the allocation must
   therefore be reconciled against the document's own VAT/total, never only against its aggregate discount, and **when the
   document prints per-line discount amounts those amounts are entered per line — the proportional allocation is inappropriate
   there** (LUANDAY: 35.550,04 / 32.139,96 would give line totals 181.597,46 / 164.178,04 instead of the printed 179.518,50 /
   166.257,00, with the same 345.775,50 total — and the line values are what approval and the P.O. group freeze).
   *Submission paths:* the only three sites that freeze a candidate snapshot are CreateBatch, UpdateBatch and the EXTRA_ITEM
   INCLUDE path (`IBatchExtraItemDecisionService.ApplyAsync`), and the last one runs only inside the first two, **after** the
   pricing refusal (test: an unresolved quotation with an EXTRA_ITEM line is refused with the pricing 409 before
   `EXTRA_ITEMS_PENDING_DECISION` is even evaluated).

### 1.5 Payment condition "C001 - pagamento imediato s/desconto" (separate question)

Extraction: the prompt classifies free text into `POST_PAID | ADVANCE_FULL | ADVANCE_PARTIAL` with a confidence; "pagamento
imediato" is in none of the prompt's examples, so the provider guessed `POST_PAID`. Mapping: `ExtractionMapper` marks the
classification `recommended` (≥ 0,70) or `suggested`; **both P.O. wizards ignored the status and the raw text and pre-selected the
classified value**, including from a document the server refuses as a P.O. (the proforma). Business reading of "pronto/imediato
pagamento" (payment at delivery vs before dispatch) is **not decided here**; what changed is that the Portal no longer decides it
for the Buyer: `lib/poPaymentCondition.resolveOcrPaymentConditionProposal` applies the classification only when the document is
not refused, the wording is not immediate-payment, and the confidence is high; otherwise the raw text is shown under the selector
("Condição lida no documento: «…». … selecione a condição acordada") and nothing is pre-selected; an `ADVANCE_PARTIAL` percentage
is applied only together with its classification (clamped 1–99), never inferred from "imediato". Open item for the owner: the
intended Portal code for Primavera condition C001 (if a fixed mapping is wanted it belongs in the prompt examples + this rule). **Until that
is decided, "pagamento imediato" / "pronto pagamento" wording always requires the Buyer's own selection** (nothing pre-selected).

## 2. Finding 2 — definitively rejected documents still displayed override controls

**Confirmed root cause.** Both wizards rendered the amount/supplier divergence block from `ocrResult.hasMismatches` alone, with the
"Estou ciente das divergências… sob minha responsabilidade" checkbox and the mandatory comment, independently of the document
verdict; the submit button and the server (`register-po` → `400 DOCUMENT_NOT_PURCHASE_ORDER`, re-pinned in
`RegisterPoDocumentIdentificationTests`) already refused the registration, so the controls could not actually override — but they
implied they could. **Fix:** pure `describeDivergenceControls(identification, documentRefusedByServer, hasDivergences)` in
`lib/poDocumentIdentification`: IDENTIFIED_OTHER_TYPE (client verdict) or a server refusal of the file
(`DOCUMENT_NOT_PURCHASE_ORDER` / `DOCUMENT_IS_QUOTATION_EVIDENCE`) → mode `DIAGNOSTIC_ONLY`: block titled "Informação de
diagnóstico (não permite o registo)", no checkbox, comment optional, "Escolha outro ficheiro"; UNCERTAIN keeps the justified
fallback (`OVERRIDE`). Applied to initial registration (client + backend mismatch blocks, comment label/placeholder, refusal
decided first in `handleConfirm`) and to the correction (`resolveJustificationPolicy(…, documentRefused)` →
`source: 'DOCUMENT_REFUSED'`, `validateCorrectPoSubmission` already refused the document before any upload). Selecting another
file resets verdict, server block, acceptance and the payment hint (existing resets + `setPaymentConditionHint`). Server-side
enforcement unchanged and re-tested with every override flag plus a payment condition: nothing written (group, number, payment
condition, history untouched; the temporary upload stays for the wizard's own clean-up).

## 3. Files

Backend: `Domain/Services/QuotationLinePricingPolicy.cs` (new; follow-up: `Kinds`, `GlobalDiscountExceedsNet`, `GlobalDiscountExceedsNetCode`); `ApprovalBatchController` (guard in CreateBatch/UpdateBatch,
`RefuseUnresolvedLinePricingAsync`, `PricingNotLineResolvedConflict`); `RequestsController` (details projection header totals,
`WithLinePricing` on details/Save/Update; follow-up: `GlobalDiscountExceedsNetProblem` 400 in Save/Update/preview); `SavedQuotationDto` (4 fields incl. `LinePricingKind`); `OpenAiDocumentExtractionProvider` (prompt text).
Frontend: `lib/quotationPricing.ts` (new; follow-up: allocation refusals, `applyGlobalDiscountAllocationToDraft`, `computeDraftQuotationTotal`, kinds), `lib/poPaymentCondition.ts` (new), `lib/poDocumentIdentification.ts`
(`describeDivergenceControls`), `lib/ocrPoValidation.ts` (status + raw text), `lib/correctPoSubmission.ts`
(`DOCUMENT_REFUSED`), `hooks/useOcrProcessor.ts`, `types/index.ts`, `QuotationWizard/hooks/useQuotationWizardState.ts`
(`distributeGlobalDiscountToLines`, `discountUncertain` gating), `WizardStepDocumentsOcr.tsx`, `WizardStepFinalReview.tsx`,
`batchEligibility.ts`, `PartialApprovalBatchModal.tsx`, `RequestQuotations.tsx`, `BuyerItemsList.tsx`, `RegisterPoModal.tsx`,
`CorrectPoModal.tsx`. Scripts: `scripts/db/quotation-discount-pricing-req595-readonly.sql` (new, read-only). Docs: this file,
`DECISIONS.md` DEC-154, test script §3.11. **No migration; no change to any migration file; no configuration change.**

## 4. Tests added

| File | What |
|---|---|
| `Validation/QuotationLinePricingPolicyTests` | REQ-595 shapes (416.850,00 vs 345.775,50; per-line 345.775,50), any global discount, IGNORED/NOT_QUOTED exclusion, legacy status, tolerance 1,00, per-line rounding never a false mismatch; **follow-up:** zero total classified LEGACY_TOTAL_NOT_COMPUTED vs ZERO_VALUE (global discount still blocks), kinds for every evaluation, `GlobalDiscountExceedsNet` matrix (equal to the net allowed, above refused, nothing left) |
| `Services/Approvals/QuotationGlobalDiscountSubmissionTests` | real endpoints on InMemory: gate refuses the TEST shape (−71.074,50); saved unchallenged without OCR total → 409 at submission; saved with residual override → exact TEST figures, 409 at submission, nothing written; snapshot has no place for a global discount (416.850,00); per-line shape → candidates/group/batch/request all 345.775,50; partial batch refused / coherent (179.518,50); legacy mismatch reason; UpdateBatch rework refused; details read exposes the verdict; **follow-up:** zero/omitted DTO header → server-computed 345.775,50 (resolved, and unresolved with the global discount — never "never computed"; the item DTO has no line-total property); global discount above the remaining net → 400 on Save, Update and preview, nothing written / persisted values unchanged, line discount above gross → 400 on Save; mixed VAT (14 % / 5 %) + existing line discount + zero-value line recomputed per line (10.830,00 / 6.300,00 / 0; global 1.000,00 → 1.524,84 IVA, 16.024,84), the allocated shape (1.112,90 / 387,10) → 10.131,29 / 5.893,55, same 16.024,84, line-resolved, and the non-proportional document shape → 1.490,00 / 15.990,00; EXTRA_ITEM line of an unresolved quotation refused by the pricing 409 before `EXTRA_ITEMS_PENDING_DECISION`, resolved one frozen at 5.000,00; legacy zero total with positive lines → LEGACY_TOTAL_NOT_COMPUTED and still submittable at the line value, zero-value → ZERO_VALUE |
| `Services/Purchasing/RegisterPoDocumentIdentificationTests` (+1) | REQ-595 proforma with every override + payment condition: `DOCUMENT_NOT_PURCHASE_ORDER`, nothing written |
| `lib/quotationPricing.test.ts` (34) | evaluation mirror + kinds, chip text, 409 reader, line-discount resolution (kept / implied / contradicted / VAT-inclusive / percent), header aggregate rule, largest-remainder allocation (exact sums), draft allocation (considered lines only, reason pre-filled, existing reason kept); **follow-up:** proportional allocation ≠ printed line discounts (LUANDAY 35.550,04 / 32.139,96 → 181.597,46 / 164.178,04 vs printed 179.518,50 / 166.257,00, same total); mixed VAT + existing discount + zero-value line (Σ lines = 16.024,84 = global-discount total); repeat press returns the same draft; excess refused (nothing changes, note), boundary = eligible net (lines to 0, never below); IGNORED / NOT_QUOTED / zero lines never receive a share, no eligible line refused; rounding remainders (exact cents, largest fractions, no share above its net); line/draft arithmetic equals the server's |
| `lib/poPaymentCondition.test.ts` (6) | refused document, immediate wording (no percentage), applied cases + clamp, low confidence, unknown code |
| `lib/poDocumentIdentification.test.ts` (+4) | divergence controls: client verdict, server refusal, no divergences, UNCERTAIN/IDENTIFIED keep the override |
| `components/RegisterPoModal.refusal.test.ts` (6) | both wizards: shared rule, checkbox only in OVERRIDE, diagnostic block, comment optional while refused, refusal decided first, state reset, payment proposal routing |
| `QuotationWizard/globalDiscountWiring.test.ts` (8) | processor uses the shared resolution (Strategy A gone), header rule before totals, confirmation gating, explicit allocation delegated to the pure draft transform (no auto-run; running total and allocation share one arithmetic; the step says when NOT to distribute and that excess is refused), final-review warning, eligibility helper, batch modal, both card chips |

## 5. Checks (actual results, local)

| Check | 2026-11-02 (initial) | 2026-11-03 (boundary follow-up) |
|---|---|---|
| `dotnet build AlplaPortal.Api` | Build succeeded | Build succeeded |
| Backend tests — full suite | **3301/3301 passed** (2 m 15 s) | **3312/3312 passed** (1 m 49 s); targeted run `QuotationLinePricingPolicyTests` + `QuotationGlobalDiscountSubmissionTests` = 31 passed |
| vitest | **114 files / 1277 tests passed** | **114 files / 1285 tests passed** (`quotationPricing` 34, `globalDiscountWiring` 8, `RegisterPoModal.refusal` 8) |
| `tsc --noEmit` | clean | clean |
| `vite build` | ok (pre-existing chunk-size warning only) | ok (same warning only) |
| EF `migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." | same (pre-existing `AdvancePaymentPercent` precision warning only) |
| Migrations folder | no new or modified files | no new or modified files (`git status` clean for `Migrations/`) |
| Working tree | — | 24 modified files (+720/−109) + the new files of §3; the two unrelated TEST-account files untouched |
| Browser | **NOT executed** — manual script §3.11 | **NOT executed** — manual script §3.11 (J/K) |
| TEST/PROD | not accessed; REQ-595 and its data untouched; nothing committed, pushed or deployed | same |
| Supported-ECF identifier recognition | pending (not reported as passed) | still pending |

CHANGELOG / VERSION / `config.ts` **not** bumped (stop before publication; the version label v2.249.6 in this document is the
proposed next PATCH). **No migration is required** by either pass.

## 6. Existing inconsistent requests — proposal (nothing executed)

Facts the proposal relies on (code, `a3f1b3d` + local): a quotation ever submitted for approval (selected item or candidate of
any batch, any status, or the durable `FirstSubmittedForApprovalAtUtc` marker) is **locked** — `PUT`/`DELETE` answer
`409 QUOTATION_SUBMITTED` (`QuotationSubmissionLock`) and its OCR baseline is immutable; a quotation is corrected only by a
**new quotation identity**: the rework revision (`SaveQuotation?reworkBatchId=…&revisesQuotationId=…`, allowed only while that
batch is in AREA_ADJUSTMENT / FINAL_ADJUSTMENT, by the assigned Buyer or a SysAdmin, never as a replace — the original stays and
the new one records `RevisesQuotationId`) or a new quotation registered on released / never-quoted lines (item-level window). The
former candidates of a released line need the explicit per-item **reuse authorization** (`authorize-reuse`, DEC-147 / D3) before
they can enter a new batch; nothing in the Portal edits approved snapshots, group totals or approved amounts.

1. **Inventory first** (read-only): result sets #9/#10/#11 of the diagnostic list every quotation in TEST (same script in PROD only
   with separate authorization and the guard changed) whose global discount reached a non-cancelled batch, the line-sum mismatches
   and the legacy zero totals, with the winning lines, batch status and the groups of that supplier; the owner classifies each
   case by stage.
2. **Never recalculate approved snapshots** (candidates, `RequestPoGroup.TotalAmount`, `ApprovedTotalAmount`) and **never edit a
   submitted quotation or its evidence**: they are what the approvers signed; history stays. The locked quotation remains visible
   as evidence with its chip.
3. **Per stage, the supported path is the existing one — always a corrected replacement quotation followed by fresh approvals:**
   (a) batch in WAITING_AREA / WAITING_FINAL_APPROVAL → the approver returns it for adjustment (`request-adjustment`); the Buyer
   creates a **revision** of the quotation through the rework flow (new identity with the document's per-line discounts, or the
   explicit allocation for a summary-only discount; the server recomputes and the revision must be line-resolved), swaps the
   candidate in `UpdateBatch` (v2.249.6 refuses an unresolved revision there too) and the batch is approved again on the new
   snapshots; (b) APPROVED with the group in WAITING_PO (REQ-595's case) → the correct price must be re-approved: P.O.-group
   cancellation with **"Cancelar P.O. e voltar a cotar"** (`RESTART_QUOTATION`, DEC-151 — the group had no registered P.O., so no
   ECF declaration / Finance confirmation is involved and no replacement-P.O. block applies; reason code + explanation; the
   cancelled group, the batch, the old quotation and the history are preserved), then the Buyer registers a **new quotation** for
   the released lines (Importar cotação with the same PDF, or Inserir manualmente, entering the printed per-line discounts
   37.530,00 / 30.160,00 — never the proportional allocation for this document), submits a new batch (the old quotation's items are
   `REUSE_AUTHORIZATION_REQUIRED` and must not be reused: their pricing is the wrong one), obtains area and final approval
   (345.775,50) and registers the P.O. against the new group. Registering the P.O. against the current 416.850,00 group is **not**
   recommended: the divergence override would record a 345.775,50 P.O. against a 416.850,00 approval; (c) PO_ISSUED or paid → no
   automatic repair; Finance decision case by case (the P.O./invoice value is the commercial truth; the approved total is
   historical).
4. **Data repair of REQ-595 itself** (e.g. rewriting its lines/snapshots) is **not proposed**; if the owner prefers a guarded SQL
   repair over (b), it needs its own diagnostic/repair/rollback package and authorization (pattern of REQ-518/547). **REQ-595 is
   unchanged by this work.**

## 7. Manual retest (TEST, pt-PT) — see `docs/PO_GROUP_CANCELLATION_TEST_SCRIPT_v2.249.0.md` §3.11

## 8. Limitations

- The provider's behaviour on this exact PDF was not observed; the prompt changes are text and need a real extraction run in TEST
  (step §3.11-1) — the frontend rules protect the draft whichever way the provider reads the columns.
- The reconciliation calculator's known limitation (summary-only discounts not credited) is unchanged: a document whose discount
  exists only in the summary still shows a residual the Buyer must justify — now with the line-level consequence made visible.
- The allocation is proportional to line nets; it reproduces the document only when the document's discount is itself
  summary-only / proportional. When the document prints per-line discounts (LUANDAY), the Buyer **must** enter the printed
  amounts per line — the step and the allocation note say so, and the test script keeps the two fixtures apart (J printed
  per line, K summary-only); the Portal cannot tell the two documents apart from the aggregate discount alone, so the Buyer's
  comparison with the document's VAT/total remains a manual step.
- A draft allocated in the document step before the reconciliation step treats every line as considered; a line ignored
  afterwards keeps its share and leaves the totals (the server excludes it) — the reconciliation gate then shows the residual.
- Browser verification was not performed (no browser tooling/credentials): every UI statement is pinned by source-level wiring
  tests and awaits the manual script.
