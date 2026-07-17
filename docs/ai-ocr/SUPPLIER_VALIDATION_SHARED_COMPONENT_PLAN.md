# Shared Supplier-Validation Component — Inventory & Plan (Design Only)

> **Status:** DESIGN / AWAITING APPROVAL. Nothing in this document is implemented.
> Per the approval note, only three items were applied now (Empresas focus/inline UX, mandatory-items
> animation, and the **simple** internal-NIF fallback). The broader shared-component extraction and the
> full cadastral-data reuse described here require explicit approval before any code change.

## 1. Supplier-step components in the Quotation wizard

- [`WizardStepSupplierValidation.tsx`](../../src/frontend/src/pages/Buyer/QuotationWizard/WizardStepSupplierValidation.tsx) (273 lines) — the step itself. Responsibilities today:
  - Renders the supplier **snapshot** (name, NIF, portal code, Primavera code) from `draft`.
  - Derives status flags: `isSupplierDraft`, `isSupplierPendingValidation`, `isSupplierValid`, `hasSupplier`.
  - Hosts the **optional enrichment panel** (address, contact, e-mail, phone, IBAN, bank account, SWIFT, payment terms) persisted via `api.lookups.updateSupplierFicha`.
  - Opens `QuickSupplierModal` for creation and, on success, writes back to the draft header.
  - Offers manual re-selection via `SupplierAutocomplete` and a "remove supplier" action.
- [`QuickSupplierModal.tsx`](../../src/frontend/src/components/Buyer/QuickSupplierModal.tsx) — **already shared** between the wizard and the Payment flow (`mode='GENERAL' | 'PAYMENT_OCR'`). Now also owns the internal-NIF fallback.
- [`SupplierAutocomplete.tsx`](../../src/frontend/src/components/SupplierAutocomplete.tsx) — manual supplier search/select (reusable as-is).
- `QuotationWizardModal.tsx` — wizard shell / step router (Overview → Documents/OCR → Reconciliation → Supplier → Final Review).

## 2. Hooks used

- [`useQuotationWizardState.ts`](../../src/frontend/src/pages/Buyer/QuotationWizard/hooks/useQuotationWizardState.ts) — owns the `draft` (`OcrDraft`) and `updateDraftHeader(field, value)`. The supplier step reads/writes `supplierId`, `supplierNameSnapshot`, `supplierPortalCode`, `supplierPrimaveraCode`, `supplierRegistrationStatus`, plus the enrichment fields.
- `useQuotationValidation.ts` — step gating for the wizard (Quotation-specific).
- `useOcrProcessor.ts` — produces the OCR draft and runs the authoritative `matchSupplier` (shared by both Quotation and Payment entry points).

## 3. Quotation-specific dependencies (must NOT leak into the shared piece)

- `UseQuotationWizardStateReturn` / `OcrDraft` draft shape and `updateDraftHeader`.
- The wizard step router, step gating (`useQuotationValidation`), and the sibling steps (Overview, Reconciliation, Final Review).
- `updateSupplierFicha` enrichment persistence is **generic** (supplier-scoped), but it is currently wired to the wizard draft.

## 4. Already-reusable pieces

- `QuickSupplierModal` (both modes, incl. the new internal-NIF fallback).
- `SupplierAutocomplete` (manual search).
- Backend authoritative match/create (`ISupplierCreationService`) and endpoints (`suppliers/match`, `suppliers/from-payment-ocr`).
- `updateSupplierFicha` for enrichment data.

## 5. Common DTOs / API

- `matchSupplier(name, taxId)` → `{ status, code, supplier, candidates[], internalCompany }` (`SupplierBody` now includes `PrimaveraCode`).
- `createSupplierFromPaymentOcr(...)` → same result body; DRAFT-only, `Origin=PAYMENT_OCR`; blocks internal NIF (`INTERNAL_COMPANY_TAX_ID`) and PrimaveraCode/administrative fields.
- `SupplierSummaryDto` (Id, Name, TaxId, PortalCode, PrimaveraCode, IsActive, RegistrationStatus).

## 6. Proposed shared component

- **`SupplierValidationPanel`** (presentational + light orchestration) covering the states in Section 8:
  found-active, found-inactive, internal-NIF, same-name/different-NIF, no-match, multiple-candidates.
- **`useSupplierValidation`** hook owning: authoritative matching, internal-NIF fallback (drop NIF → re-match by name), candidate selection, conflict/duplicate handling, and DRAFT creation. It wraps the existing API and normalizes results into a single `SupplierValidationState`.
- Reuse `QuickSupplierModal` and `SupplierAutocomplete` inside the panel rather than reimplementing.

## 7. Props / contexts

```ts
type SupplierValidationContext = 'QUOTATION' | 'PAYMENT_OCR';

interface SupplierValidationPanelProps {
  context: SupplierValidationContext;
  extracted: { name?: string; taxId?: string; address?: string; contactName?: string;
               email?: string; phone?: string; iban?: string; bankAccount?: string;
               swift?: string; paymentTerms?: string };
  value: { supplierId: number | null; snapshot?: SupplierSnapshot };
  onChange: (next: { supplierId: number | null; snapshot?: SupplierSnapshot }) => void;
  showEnrichmentPanel?: boolean;   // true for QUOTATION; optional/collapsed for PAYMENT_OCR
  allowManualSearch?: boolean;     // SupplierAutocomplete
}
```

- `QUOTATION` binds `onChange` to `wizardState.updateDraftHeader(...)`; `PAYMENT_OCR` binds it to the Payment request form state.
- The panel never sets PrimaveraCode / activates / approves / changes RegistrationStatus from the contextual path — those remain admin-only (enforced server-side).

## 8. Flow in the Payment context

- Open the panel in a **modal/drawer showing only the supplier step** — no Overview, Reconciliation, Final Review or other wizard steps.
- States supported: found-active (select, no duplicate), found-inactive (show state, no reactivate), internal-NIF (drop NIF → name match → select/search/create-without-NIF), same-name/different-NIF (confirm only if truly distinct), no-match (create DRAFT), multiple-candidates (manual choice — see limitation below).
- Reuses OCR enrichment data (address/contact/e-mail/phone/NIF/name) for review before creation.

## 9. Test impact

- No frontend test runner exists today (`vitest`/`jest`/testing-library absent). Introducing `SupplierValidationPanel` should come with **adding a frontend test runner** (recommend Vitest + Testing Library) — itself a decision to approve.
- Backend: the shared hook relies on `ISupplierCreationService`, already covered by `SupplierCreationServiceTests` (incl. internal-NIF and name-only cases) and the SQL integration test.
- **Known backend limitation for the multi-candidate state:** `Classify` currently returns a **single** first name-match candidate. True multi-candidate selection needs a small, deliberate change to return all normalized-name matches (with a cap) — scoped to the shared-component task, not the simple fallback.

## 10. Files that would change (shared refactor — pending approval)

- **New:** `src/frontend/src/components/Supplier/SupplierValidationPanel.tsx`, `src/frontend/src/hooks/useSupplierValidation.ts`.
- **Refactor:** `WizardStepSupplierValidation.tsx` → thin wrapper delegating to the shared panel (`context='QUOTATION'`), preserving current behavior.
- **New Payment usage:** a supplier-only modal/drawer host in the Payment new-request flow (and, later, Payment DRAFT).
- **Possibly:** backend `Classify` multi-candidate return; `SupplierBody` already extended with `PrimaveraCode`.
- **Tooling:** add Vitest + Testing Library config and the first component tests.

---
## What was applied now (no approval needed — already authorized)

1. **Empresas NIF conflict UX** — structured API code (`COMPANY_TAX_ID_CONFLICT` + `conflictCompanyName`); the frontend scrolls to the NIF field, highlights it, shows an inline message, focuses it, and preserves the other values.
2. **Mandatory-items UX** — scroll to the section, ~5s red pulse (`.error-pulse`, honors reduced-motion) then a discreet error border, section-level and per-field inline messages, per-field highlight when a row exists, and focus on the first fixable field.
3. **Simple internal-NIF fallback** in `QuickSupplierModal` — on `INTERNAL_COMPANY_TAX_ID`: drop the internal NIF, show the identified internal company, re-match by name only, offer "Usar este fornecedor" for existing matches (active/inactive shown with state), and "Criar fornecedor sem NIF" only when no name match exists. The internal NIF is never re-sent.

*Everything under §1–§10 (the shared `SupplierValidationPanel`, full cadastral-data reuse, multi-candidate backend change, and the frontend test runner) is design only and awaits approval.*
