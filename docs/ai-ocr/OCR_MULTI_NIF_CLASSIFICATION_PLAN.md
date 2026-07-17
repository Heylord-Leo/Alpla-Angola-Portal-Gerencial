# OCR Multi-NIF Classification — Technical Plan (Design Only)

> **Status:** DESIGN / NOT IMPLEMENTED. This document is the approved Part C deliverable.
> No production OCR behavior is changed by this plan. Implementation is a separate, future task
> to be scheduled and validated on its own. The singular `supplierTaxId` field remains the
> authoritative source until this plan is executed and validated.

## 1. Problem Statement

The AI extraction returns a **single** `header.supplierTaxId` (plus a free-text
`header.billedCompanyName`). On documents that print several fiscal numbers — the supplier's NIF,
the billed ALPLA company's NIF, and sometimes a customer/third-party NIF — the model can pick the
**wrong** one.

**Reproducing case (Zeepack):** the invoice was billed to an internal ALPLA company. The model
returned ALPLA's own NIF `5417567485` (AlplaPLASTICO) as `supplierTaxId`. Downstream this NIF was
carried into supplier matching/creation as if it were the vendor's NIF.

Two independent defenses already exist (shipped in Parts A/B of this initiative):

1. **`Company.TaxId` master data** (normalized, unique filtered index) for the internal ALPLA
   companies — seeded `APA → 5417567485`, `APS → 5001760246`, admin-managed in *Dados Mestres →
   Empresas*.
2. **Backend-authoritative block** in `SupplierCreationService.CheckInternalCompanyAsync`, called
   at the start of both `MatchAsync` and `CreateAsync`. Any NIF that equals a `Company.TaxId` is
   rejected with `Status = InternalCompanyTaxId` / `code = INTERNAL_COMPANY_TAX_ID`, so an internal
   NIF can **never** become a supplier — even if the model still mis-picks it.

This plan is the **third** (accuracy) layer: give the model a structured way to return **all** NIFs
it sees with a suggested role, so the backend can pick the correct supplier NIF instead of trusting
a single guessed field. It is an accuracy improvement, not a safety net — the safety net (defense #2)
is already in place.

## 2. Goals & Non-Goals

**Goals**
- Extraction returns a **list** of NIF candidates with role + evidence, not a single value.
- Backend deterministically selects the supplier NIF, using `Company.TaxId` to exclude internal
  NIFs and `billedCompanyName` to corroborate the billed company.
- Surface a **strong alert + explicit confirmation** when the document's billed company diverges
  from the request's company/plant — no hard block, no silent auto-change.
- Be fully backward compatible: keep populating the singular `supplierTaxId` during a transition
  window so nothing downstream breaks before consumers are migrated.

**Non-Goals**
- No change to the supplier match/create *contract* (already correct and defended).
- No auto-editing of the request's company/plant from OCR.
- No removal of the singular field in the same change that introduces the list.

## 3. Current Pipeline (as-is)

| Stage | File | Behavior today |
|---|---|---|
| Prompt / schema | `OpenAiDocumentExtractionProvider.GetSystemPrompt()` | JSON schema exposes `header.supplierTaxId` (string) + `header.billedCompanyName` (string). |
| Model → DTO | `OpenAiDocumentExtractionProvider.MapFromJson()` | Reads `header.supplierTaxId` / `billedCompanyName` into `ExtractionHeaderDto`. |
| DTO | `ExtractionResultDto.ExtractionHeaderDto` | `SupplierName`, `SupplierTaxId`, `BilledCompanyName` (all singular). |
| API mapping | `ExtractionMapper` | Projects header into the request/OCR result DTOs. |
| Frontend | `useOcrProcessor.ts` → `api.lookups.matchSupplier(name, taxId)` | Passes the single `supplierTaxId` to authoritative matching; feeds `QuickSupplierModal`. |
| Backend match/create | `SupplierCreationService` | Normalizes NIF; `CheckInternalCompanyAsync` blocks internal NIFs. |

## 4. Proposed Design

### 4.1 New extraction contract — `DocumentTaxIdCandidate`

Add a **candidate list** alongside the existing singular field (transition-safe). Proposed DTO:

```csharp
// AlplaPortal.Application/DTOs/Extraction/ExtractionResultDto.cs
public class DocumentTaxIdCandidate
{
    public string? RawValue { get; set; }        // as printed on the document
    public string? NormalizedValue { get; set; }  // TaxIdNormalizer.Normalize(RawValue)
    public string? SuggestedRole { get; set; }    // SUPPLIER | BILLED_COMPANY | CUSTOMER | OTHER | UNKNOWN
    public string? AssociatedName { get; set; }    // party name nearest this NIF
    public string? ContextText { get; set; }       // short surrounding snippet (evidence)
    public string? NearbyLabel { get; set; }       // label that preceded it (e.g. "NIF", "Contribuinte", "Cliente")
    public int? Page { get; set; }
    public decimal? Confidence { get; set; }       // 0.0–1.0
}
```

`ExtractionHeaderDto` gains: `public List<DocumentTaxIdCandidate> TaxIdCandidates { get; set; } = new();`
The singular `SupplierTaxId` / `BilledCompanyName` **stay** and are populated from the resolver's
choice during the transition window.

### 4.2 Prompt / schema changes

In `GetSystemPrompt()`, add a `taxIdCandidates` array to the JSON schema and an instruction block:

- "List **every** fiscal number (NIF/Contribuinte/VAT) that appears in the document, once each."
- For each: `rawValue`, `normalizedValue` (digits/letters only, uppercased), `suggestedRole`,
  `associatedName`, `contextText` (≤120 chars), `nearbyLabel`, `page`, `confidence`.
- Role guidance: the party that **issued/sold** = `SUPPLIER`; the party **billed/recipient**
  (typically *AlplaPLASTICO*/*AlplaSOPRO*) = `BILLED_COMPANY`; a named third-party buyer =
  `CUSTOMER`; anything else = `OTHER`; unsure = `UNKNOWN`.
- Bump `InvoicePromptVersion` (e.g. `v2.2-multinif`) so metadata records which prompt produced the
  output. Keep the existing singular fields in the schema for backward compatibility.

`MapFromJson()` deserializes the array defensively (missing/empty → empty list; each field optional).

### 4.3 Backend NIF resolver (deterministic, server-side)

A new `SupplierTaxIdResolver` (Infrastructure) turns candidates + `Company.TaxId` into a decision.
**The model suggests; the backend decides.**

Algorithm:
1. Normalize every candidate NIF via `TaxIdNormalizer.Normalize`.
2. Load internal `Company.TaxId` set (already normalized in DB).
3. Partition candidates: `internal` (NIF ∈ Company.TaxId) vs `external`.
4. **Supplier pick** = the highest-confidence **external** candidate whose role is `SUPPLIER`
   (fallback: highest-confidence external of any non-internal role; then `UNKNOWN`).
5. **Billed-company pick** = the internal candidate whose `Company.TaxId` matches; corroborate
   against `billedCompanyName` where possible.
6. Emit a resolution object: chosen supplier NIF+name, chosen billed company (id/name/NIF),
   `internalOnly` flag, and a `divergence` flag (see 4.4).
7. Populate the singular `SupplierTaxId` from the supplier pick during the transition window.

Edge cases, by design:
- **Only-internal NIF(s), no external** → `internalOnly = true`; **do not** associate/create any
  supplier. The contextual endpoint already returns `INTERNAL_COMPANY_TAX_ID`; the resolver makes
  this the deterministic outcome instead of relying on the model to have avoided the internal NIF.
- **No NIF at all** → supplier pick null; fall back to name-only matching (unchanged behavior).
- **Multiple external suppliers** → pick highest confidence; expose the rest as alternates for the UI.

### 4.4 Company/plant divergence — strong alert, explicit confirmation

When the resolved billed company (from an internal NIF and/or `billedCompanyName`) does **not** match
the company/plant selected on the request:
- Return a structured `divergence` payload: `expectedCompany` (from request's plant→company),
  `documentCompany` (resolved), `documentTaxId`, `message`.
- Frontend shows a **prominent** alert and requires an **explicit confirmation** to proceed.
- **No hard block. No automatic change** of the request's company/plant. (Mirrors the approved
  decision #3.)

### 4.5 Frontend consumption

- `useOcrProcessor.ts`: consume the resolver output. Pass the resolved **supplier** NIF (never an
  internal NIF) to `api.lookups.matchSupplier`.
- If `internalOnly`, do **not** open supplier creation; show an informational notice ("O único NIF
  do documento pertence a uma empresa interna").
- Render the divergence alert + confirmation gate (4.4).
- Optional: a small "NIFs detectados" evidence panel (value · role · party · page) for auditability.

## 5. Data & Migration Impact

- **None to the schema.** `Company.TaxId` already exists (shipped in Part B). The candidate list is
  transport-only (extraction JSON + DTOs), not persisted — unless we later choose to store evidence
  on `OcrExtractedItem`/an extraction record, which is explicitly **out of scope** here.
- No historical-data mutation.

## 6. Backward Compatibility & Rollout

1. **Phase C-1 (additive):** ship DTO list + prompt + resolver; keep populating singular fields.
   Consumers unchanged. Log resolver decisions for observation.
2. **Phase C-2:** migrate `useOcrProcessor.ts` and any other reader to the resolver output; add the
   divergence gate.
3. **Phase C-3 (cleanup, optional/much later):** once no consumer reads the singular
   `supplierTaxId` directly, consider deprecating it. Not in the initial change.

Feature-flaggable via the existing extraction settings/prompt-version mechanism so it can be rolled
back to the singular path without a deploy.

## 7. Testing Strategy (for the future implementation task)

- **Resolver unit tests:** internal-only → `internalOnly`, no supplier; supplier+billed internal →
  correct split; multiple externals → highest confidence; no-NIF → name-only fallback; formatted vs
  unformatted NIFs normalize equal; Zeepack fixture → supplier NIF ≠ `5417567485`.
- **`MapFromJson` tests:** missing array, empty array, partial candidate objects, malformed
  confidence → graceful defaults.
- **Divergence tests:** billed company ≠ request company → divergence payload present; equal → absent.
- **Regression:** existing single-NIF documents still resolve identically; internal-NIF block
  (`INTERNAL_COMPANY_TAX_ID`) still fires.
- **Prompt eval:** a small labeled fixture set (supplier-only, supplier+billed, supplier+customer,
  internal-only) checked against expected role assignments.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Model over/under-lists NIFs | Backend resolver is authoritative; model output is advisory only. |
| Token/cost increase from richer schema | Small array; monitor via existing token metadata; prompt version tag enables A/B. |
| New field breaks a consumer | Additive rollout — singular fields kept until consumers migrate. |
| Internal NIF still mis-picked by model | Already defended: `CheckInternalCompanyAsync` blocks it regardless. |
| Divergence false positives | Alert + confirmation (not a block); admin-managed `Company.TaxId` keeps mapping correct. |

## 9. Touch Points Summary (for implementation)

- `ExtractionResultDto.cs` — add `DocumentTaxIdCandidate` + `TaxIdCandidates`.
- `OpenAiDocumentExtractionProvider.cs` — schema + prompt block + `MapFromJson` array parsing +
  prompt-version bump.
- **New** `SupplierTaxIdResolver` (Infrastructure) + interface (Application) + DI registration.
- `ExtractionMapper.cs` — carry resolver output / candidate list into API DTOs.
- `useOcrProcessor.ts` + OCR panel / `QuickSupplierModal` — consume resolver output, divergence gate,
  evidence panel.
- Tests as in §7.

---
*Design only. Implementation deferred to a dedicated, separately-validated task. The internal-NIF
safety block and `Company.TaxId` master data (Parts A/B) are already live and do not depend on this
plan.*
