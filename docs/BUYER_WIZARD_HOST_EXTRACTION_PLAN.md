# Buyer Quotation Wizard — Host Extraction Plan (Phase 3C.1)

> **Status: DESIGN — ready to execute in a browser-capable session.**
> Goal: migrate **Adicionar cotação** / **Completar cotações** into `/buyer/requests/{requestId}`
> by extracting ONE reusable host for the EXISTING "REGISTRAR NOVA COTAÇÃO" Wizard, consumed by BOTH
> the classic `BuyerItemsList` and the new `BuyerRequestWorkspace`. **No Wizard internals change.**
>
> This was handed off (not executed inline) because the acceptance gate is a **browser OCR + manual +
> edit regression comparison** (classic vs Workspace) that cannot be run in a non-interactive session,
> and achieving zero-duplication forces rewiring the accepted production classic screen — which must
> be validated in a browser before it is trusted (Phase 3C.1 §9/§10/§13/§16).

## 1. Original host dependency map (BuyerItemsList.tsx)

Classification: **A** = reusable host · **B** = classic-page-only · **C** = Wizard-internal (do not touch) · **D** = dead.

| Item | Location | Class |
|---|---|---|
| `QuotationWizardModal` + steps/OCR/reconciliation/validation | `QuotationWizard/*` | **C** — reuse unmodified |
| `useQuotationWizardState()` | `QuotationWizard/hooks/useQuotationWizardState.ts` | **C** — reuse unmodified |
| `buildQuotationPayload(draft, conflict?)` | `BuyerItemsList.tsx:~275` (module-level, pure) | **A** — move to shared module |
| `findAmbiguousSaveMatch(...)`, `tryReconcileAmbiguousSave(...)` | `:100`, `:133` (module-level) | **A** — move to shared module |
| `handleWizardSaveQuotation(draft, override?)` (~159L; create/update + ambiguous-save read-back) | `:550-708` | **A** |
| `_startWizardUpload(file)` (attachment upload + OCR extract) | `:712-730` | **A** |
| `handleReconcilePreview(draft)` | `:735-742` | **A** |
| `handleUploadFileForWizard(file)` (dup-check → upload) | `:747-773` | **A** |
| `handleReplaceDocumentForWizard(attachmentId)` | `:775-785` | **A** |
| `handleWizardLineItemUpserted(item)` | `:789-814` | **A** |
| `onCancelWizard` (temp-attachment cleanup) | `:3141-3153` | **A** |
| `handleOpenWizard(group, mode, editQuotation?)` (NEW seeds NOT_QUOTED placeholders from eligible items; EDIT rehydrates OcrDraft) | `:816-~965` | **A** |
| state: `wizardActiveRequest`, `temporaryWizardAttachmentIds`, `preAttemptSnapshotRef`, `isSaving`, `isProcessingOcr`, `fileDuplicateWarning` | `:~418-540` | **A** |
| `useOcrProcessor(ivaRates, units, currencies)` + lookups load | `:534`, `:986-1006` | **A** (host loads its own) |
| `loadData()` / `groupItemsByRequest()` / `setFeedback` | `:1009-1117` | **B** — stays in classic; becomes the host's `onSaved`/`onFeedback` callbacks |
| `ApprovalModal` `SAVE_QUOTATION_OCR/MANUAL` branch (`/* legacy save removed */`) | `:~1230` | **D** — leave/remove, not migrated |

**Key enabler (already proven in 3C):** a host needs only a `RequestDetailsDto` (`api.requests.get(requestId)`) as the Wizard's `request` prop — the same shape `groupItemsByRequest` produces. This removes the only structural coupling to the classic grouping.

## 2. Extracted architecture (single source of truth)

```
                buildQuotationPayload / findAmbiguousSaveMatch / tryReconcileAmbiguousSave   (shared module)
                                     ▲
useBuyerQuotationWizard  ──►  QuotationWizardModal  ──►  useQuotationWizardState     (C, unchanged)
        ▲            ▲
BuyerItemsList   BuyerRequestWorkspace     (both hosts consume the SAME hook — no copied save/OCR logic)
```

- **New shared module** `QuotationWizard/quotationSaveLogic.ts` — MOVE (not copy) `buildQuotationPayload`, `findAmbiguousSaveMatch`, `tryReconcileAmbiguousSave` out of `BuyerItemsList` and `export` them.
- **New hook** `QuotationWizard/hooks/useBuyerQuotationWizard.ts`:

```ts
export function useBuyerQuotationWizard(opts: {
  onSaved: (result: QuotationSaveResult) => void;              // classic: loadData(); workspace: afterMutation(...)
  onFeedback: (f: { type: 'success' | 'error' | 'info'; message: string }) => void;
}): {
  openWizard: (group: RequestDetailsDto, mode: 'MANUAL' | 'UPLOAD', editQuotation?: SavedQuotationDto) => void;
  wizardProps: QuotationWizardModalProps | null;               // spread onto <QuotationWizardModal/>, null when closed
  isOpen: boolean;
};
```

  Internals = the **A** items lifted verbatim: `useQuotationWizardState()`, `useOcrProcessor(...)` + own lookup load, `wizardActiveRequest`, temp-attachment ids, `preAttemptSnapshotRef`, `isSaving`, `isProcessingOcr`, `fileDuplicateWarning`, and all handlers. Replace the classic-only touchpoints with the injected callbacks: every `loadData()` → `opts.onSaved(result)`, every `setFeedback(x)` → `opts.onFeedback(x)`. `wizardProps` assembles exactly the current modal props (`request: wizardActiveRequest`, `wizardState`, `onSaveQuotation`, `onReconcilePreview`, `isProcessingOcr`, `onUploadFile`, `onCancelWizard`, `onReplaceDocument`, `ivaRates`, `units`, `currencies`, `onRequestLineItemUpserted`).

## 3. Exact edits

**a) `quotationSaveLogic.ts` (new):** cut the three module-level helpers from `BuyerItemsList` and export them. Update `BuyerItemsList` imports.

**b) `useBuyerQuotationWizard.ts` (new):** move the **A** state + handlers verbatim; swap `loadData`/`setFeedback` for `opts.onSaved`/`opts.onFeedback`; import the shared helpers.

**c) `BuyerItemsList.tsx` (rewire — regression-critical):**
- delete the moved state/handlers/helpers (~600 lines);
- add `const wiz = useBuyerQuotationWizard({ onSaved: () => loadData(), onFeedback: setFeedback });`
- replace each `handleOpenWizard(group, mode, q)` call with `wiz.openWizard(group, mode, q)`;
- replace the `<QuotationWizardModal … />` render (`:3134-3159`) with `{wiz.wizardProps && <QuotationWizardModal {...wiz.wizardProps} />}`.
- Everything else stays byte-identical. **This is the change that must be browser-regression-checked.**

**d) `BuyerRequestWorkspace.tsx` (integration):**
- `const wiz = useBuyerQuotationWizard({ onSaved: () => afterMutation('Cotação registada.'), onFeedback: (f) => flash(f.message) });`
- `ADD_QUOTATION` button → load `api.requests.get(requestId)` then `wiz.openWizard(request, 'MANUAL')` (opens on OVERVIEW → shows **IMPORTAR DOCUMENTO / INSERIR MANUALMENTE**). No `/buyer/items/classic` navigation.
- render `{wiz.wizardProps && <QuotationWizardModal {...wiz.wizardProps} />}`.

## 4. Eligible-item context (§6) — do NOT invent in React

Preserve `handleOpenWizard`'s existing NEW-mode seeding: NOT_QUOTED placeholders from `group.lineItems.filter(isLineItemEligibleForQuotation)` (only `null`/`QUOTATION_PENDING`). The Workspace passes the full `RequestDetailsDto`; the existing builder decides eligibility. PARTIAL_COVERAGE uses the same path (the Wizard/host already scopes to open items).

## 5. Success refresh (§7)

`onSaved` → Workspace calls `loadWorkspace(true)` (silent) → refreshes operational state, next action, coverage, items, quotations, suppliers, batches, timeline; preserves route, current tab, queue back-state; supplier carousel index is component-local (preserved). Classic → `loadData()` (unchanged).

## 6. Edit-existing (§11)

`handleOpenWizard(..., editQuotation)` EDIT rehydration moves into the hook — **classic edit is preserved**. Workspace need not expose edit yet; the shared host supports it when wired.

## 7. Browser regression validation (the acceptance gate — §9/§10/§13)

Using **ZZTEST-BUY** fixtures only, compare **classic vs Workspace** for: initial step, IMPORTAR DOCUMENTO upload, OCR result, supplier, currency, items, totals, reconciliation, ambiguous-save read-back, final save; and INSERIR MANUALMENTE fields/validation/supplier/save; and edit-existing (classic). Any divergence → STOP (§16).

## 8. Wizard non-regression guarantee

`QuotationWizardModal` files: **NONE**. `useQuotationWizardState` files: **NONE**. `buildQuotationPayload`/ambiguous-save helpers: **MOVED, not duplicated**. No second quotation-save implementation.

## 9. Risk & why hand-off

The extraction is mechanically liftable (handlers are self-contained `api.* + wizardState`), but step **3c** rewires the accepted production classic screen, and only the browser OCR/manual/edit regression (step 7) proves behavior is preserved. Execute a→d, then run step 7 before trusting the classic screen.

## 10. Stage-2A coupling finding (2026-08-24) — interface CORRECTION

A Stage-2A attempt confirmed the hook handlers move faithfully, BUT the wizard STATE is **not cleanly separable** from `BuyerItemsList`; the "handlers lift verbatim" estimate understated the state coupling. Reference counts in `BuyerItemsList`:

- **`isSaving` is a SHARED cross-cutting "mutation in progress" flag** — 9 `setIsSaving` calls, of which only 2 (`:422`/`:565`) are the wizard save; the rest gate non-wizard operations (delete quotation, remove proforma, cancel, etc.), and ~7 non-wizard buttons read it. **`isSaving` MUST stay in classic** and be passed into the hook (`{ isSaving, setIsSaving }`) — the hook must NOT own it.
- **`quotationWizardState` (14 refs)** and **`wizardActiveRequest` (17 refs)** are woven through classic rendering/logic (modal render, step displays, the file-duplicate modal). They can move into the hook, but classic must then read them via `wiz.*` at ~30 sites.
- The exact-file **file-duplicate** sub-feature (state + countdown + focus-trap + refs + keydown + ~60-line render, and it shares `setIsProcessingOcr`) stays in classic and calls the hook's `startUpload(file)` to proceed; `isProcessingOcr`/`setIsProcessingOcr` must be exposed by the hook.

Net: Stage 2A is a **~40-reference rewire** of the just-restored production file, safe to do ONLY with the step-7 browser OCR/manual/edit regression — i.e., in an interactive session. Corrected hook signature:

```ts
useBuyerQuotationWizard({
  ivaRates, units, currencies, mapOcrResultToDraft,
  isSaving, setIsSaving,          // shared with classic — passed IN, not owned
  onSaved, onFeedback,
}) → { openWizard, startUpload, quotationWizardState, wizardActiveRequest,
       isProcessingOcr, setIsProcessingOcr, onSaveQuotation, onReconcilePreview,
       onReplaceDocument, onRequestLineItemUpserted, onCancelWizard }
```
