# Supplier Intelligence — Canonical Model (Pesquisa de Fornecedores)

> **Status:** Phase 0 — approved canonical design reference for the Compras campaign. **No
> implementation yet.** Companion: `docs/BUYER_QUEUE_CANONICAL_MODEL.md`.

## 1. Purpose

Let a Buyer (and, later, other Compras roles) **search a supplier and understand ALPLA's commercial
history** with that supplier — reliably, from current Portal data only, with honest confidence and no
invented figures. Delivered as a **separate, reusable Compras capability**, never embedded into the
Buyer Queue DTOs.

### Product surfaces (approved direction)
- **GLOBAL** — "Pesquisa de Fornecedores" (search by name / NIF / Primavera code → supplier profile).
- **CONTEXTUAL** — a compact supplier summary on quotation cards inside the Request Workspace, with a
  "**Ver histórico do fornecedor**" link.
- **ARTICLE CONTEXT** — "**Ver histórico de compras deste artigo**" from a requested item, where the
  `ItemCatalog` linkage is reliable (§ Article history).

---

## 2. Identity matching policy — SAFETY DECISION (approved)

Aggregation join priority:
1. **`SupplierId`** when available.
2. **normalized NIF** (`Domain/Common/TaxIdNormalizer` / `SupplierCreationService.NormalizeNif`) when
   `SupplierId` is unavailable.

**Never automatically merge monetary history by supplier NAME alone.** Name matching may only surface a
**"Possível correspondência histórica"** requiring human review — it must never silently roll up money.

### Linkage reality (measured on the DEV clone — validate on live before shipping any total)
| Entity | SupplierId present | NIF/name snapshot |
|---|---|---|
| `Quotation` | **112/112 (100%)** in clone (field is nullable; old data may differ) | `SupplierNameSnapshot` always; **no NIF snapshot on Quotation** |
| `RequestPoGroup` | **158/196 (81%)**; **38 null (19%)** | `SupplierNifSnapshot` on 147/196 (75%), `SupplierNameSnapshot` |
| `Request` | nullable (legacy "sem fornecedor estruturado") | — |
| `RequestPayment` | **no supplier FK** — only via group/request | — |
| `Supplier` master | 304 rows | **TaxId present 98%**, PrimaveraCode present 97% |

Implication: **NIF is the reliable cross-lifecycle key**; ~19% of PO groups need NIF/name fallback;
payments inherit their group's (possibly null) supplier. Confidence must be disclosed per metric.

---

## 3. Metric definitions & authoritative sources (approved semantics)

**Precise names. Do not conflate.** All monetary metrics are **per currency** (§4).

| Metric | Display (PT) | Authoritative source | Meaning | Confidence |
|---|---|---|---|---|
| Total ordered | **Total comprado** | `RequestPoGroup.TotalAmount` where `PurchaseOrderNumber != null` (PO issued), attributed to supplier | **committed/ordered** amount — never call it "Total gasto" | Med-High |
| Total paid | **Total pago** | `RequestPayment.ActualPaidAmount` where `PaymentStatus = COMPLETED` (supplier via group/request) | actual cash disbursed | Med-High |
| Purchase count | **Nº de compras** | distinct issued `RequestPoGroup` / `PurchaseOrderNumber` | one PO group = one purchase (advance+balance payments do **not** inflate) | Med-High |
| Last purchase | **Última compra** | latest issued-PO event — recommend `MAX(RequestPoGroup.CreatedAtUtc)` where `PurchaseOrderNumber != null`; fallback `MAX(Request.ApprovedAtUtc)` | last order date | High (given attribution) |
| Quotations received | **Cotações recebidas** | `Quotation` count attributable to supplier | count of quote docs | Med-High |
| Quotations selected | **Cotações selecionadas** | `Quotation.IsSelected = true` (corroborated by `Request.SelectedQuotationId`) | won quotes | Med-High |
| Plants supplied | **Plantas atendidas** | distinct `RequestPoGroup.PlantId` / `Request.PlantId` | — | Med-High |
| Departments supplied | **Departamentos atendidos** | distinct `Request.DepartmentId` | — | Med-High |
| Currencies used | **Moedas** | distinct currency across Quotation/PoGroup/Payment | — | High |
| Registration/status | **Estado / registo** | `Supplier.RegistrationStatus`, `IsActive`, `Origin`, `SupplierStatusHistory` | first-party | High |

### Explicitly NOT shipped
- **Selection rate** — deferred (denominator is often 1 quote per request → saturates; ship only if
  semantics proven useful).
- **Received monetary value** — **impossible**: the model has only `ReceivedQuantity`, no received-
  money field. Do not expose.
- **Cross-currency total, supplier ranking/score, inferred savings, supplier performance score** — out
  of scope (see MVP exclusions).

---

## 4. Multi-currency policy — approved

**Never** compute `AOA + EUR + USD` as one total. Display **separate per-currency totals**:
```
Total comprado:  AOA 1.234.567  ·  EUR 12.000
Total pago:      AOA   980.000
```
No conversion until an **authoritative FX source/policy** exists. (Clone currencies: AOA 165, EUR 2,
null 29 on PO groups — mostly single-currency, but the policy holds unconditionally.)

Data-quality note: monetary aggregates must **cap/flag outliers** (e.g. a single PO group summing to
~18.4B AOA in the clone) so one bad row cannot distort a supplier total.

---

## 5. Supplier Intelligence MVP (approved)

**IDENTITY:** Supplier name · NIF · PrimaveraCode · registration/active status.
**COMMERCIAL:** Nº de compras · Total comprado (per currency) · Última compra · Cotações recebidas ·
Cotações selecionadas · plants supplied · departments supplied · currencies used.
**PURCHASE HISTORY (table):** date · Request · PO · article/item (where attributable) · quantity ·
amount · currency · plant.

**Not in MVP:** received monetary value · cross-currency total · supplier ranking · score · inferred
savings · supplier performance score · selection rate.

---

## 6. History window — approved

Backend **retains all reliable historical data** (never discards server-side). The **UI defaults to
recent history first** with a time filter: **Últimos 12 meses · Últimos 24 meses · Todo o histórico**.

---

## 7. Article history — confidence model (approved)

| Basis | Confidence | Rule |
|---|---|---|
| **Catalog-linked** (`RequestLineItem.ItemCatalogId` / `ItemCatalog.Code`) | **Medium/High** | preferred join; **~90% of current line items carry `ItemCatalogId`** (627/693 in clone) |
| **Free-text normalized description** | **Low** | fallback only; surface as "possível correspondência", **never auto-merge** distinct free-text items as the same article |

Initial article-history must **prefer `ItemCatalogId` / `ItemCatalog.Code`**. A Primavera article↔
supplier mapping exists (`PrimaveraArticleSupplierController`) but is ERP-side/live and not joined into
Portal transactional data — out of scope for MVP aggregation.

---

## 8. Access policy (recommendation — implement later)

Supplier Intelligence belongs to the **broader Compras capability**, not the Buyer Queue. Recommended
initial consumers: **Buyer · LocalManager · SystemAdministrator**. Design the read service/endpoints so
**Finance or Viewer-type access can be added later via an explicit permission** without redesign
(a single `SupplierInsightsService` behind a role/permission policy, not per-role branching).

---

## 9. Existing building blocks to reuse
- `GET /api/lookups/suppliers` (paged search Name/TaxId/PrimaveraCode/PortalCode) and
  `/lookups/suppliers/search` (autocomplete).
- `ISupplierCreationService` — `NormalizeName` / `NormalizeNif` (authoritative join normalizers),
  NIF-then-name dedup, internal-company exclusion.
- `Supplier` EF config (`ApplicationDbContext.cs:632-639`): unique indexes on PortalCode, Name,
  filtered PrimaveraCode, filtered TaxId; index on RegistrationStatus.
- `FinanceController.GetSummary → TopSuppliers` is the closest existing rollup (top-5 by **pending**,
  grouped by `SupplierNameSnapshot`, per currency) — a pattern, **not** a source of truth to copy
  (name-based, pending-only).

**No dedicated buyer-facing `SuppliersController`, supplier-stats, or purchase-history endpoint exists
today** — these are net-new (separate read services, Phase 5+).

---

## 10. Limitations & risks (must be surfaced honestly)
- Historical supplier attribution is **partial** (nullable snapshot linkage); never present a total as
  complete without the confidence caveat.
- Payments have **no supplier FK** — "Total pago" attribution is transitive and inherits the group's
  (possibly null) supplier.
- Multi-currency and outliers must be handled explicitly (§4).
- Article history reliable only for catalog-linked lines (§7).

---

## 11. Contextual carousel in the Request Workspace (approved)

Inside `/buyer/requests/{requestId}` the **"Inteligência dos Fornecedores"** section is **contextual,
not global** — a **carousel** of **only the suppliers involved in the currently opened Request**. It is
**not** a recommendation/search widget; global search is the separate future **"Pesquisa de
Fornecedores"** feature.

- **"Involved supplier" definition (quotation-stage truth):** derived from **this Request's
  `Quotations`** and their `QuotationItems`, plus the **selected quotation** where applicable.
  **Do not** use downstream `RequestPoGroup`/PO data to define quotation-stage involvement (that is a
  later lifecycle phase).
- **Deduplicate** involved suppliers by **`SupplierId` → normalized NIF fallback; never by name alone**
  (§2). In current data, `Quotation.SupplierId` is fully populated (112/112 in clone), so involvement
  resolution is reliable at the quotation stage.
- **Each carousel card** shows only the approved-reliable metrics (§3/§5) — purchase count, Total
  comprado per currency, última compra, cotações recebidas/selecionadas, plants/departments — computed
  over that supplier's **global** history but **surfaced here only because the supplier is on this
  Request**. Never show recommended/global/top/unrelated suppliers here.

---

## 12. "Ver Perfil Completo" — reuse the existing Supplier Sheet in a drawer (approved)

The supplier card's **"Ver Perfil Completo"** opens a **right-side drawer** that renders the **same
Supplier Sheet** used by the full page `/contracts/fichas/{supplierId}` — **one component, one set of
APIs/validation, no second supplier form.** (Do NOT implement in Phase 0.)

### Component reuse strategy (from current-state analysis)
`SupplierFichaDetail.tsx` (795 lines) is a single self-contained route-page file: shallow coupling
(reads `useParams` `id`, owns `useNavigate` + its own fetch + a hand-rolled header), no shared child
components, all data via `api.lookups.suppliers/{id}/{ficha|completeness|history|status|documents|
submit-approval}` keyed on `supplierId` only, and **server-driven validation** (a completeness
checklist gates "Submeter para Aprovação"). Clean extraction:
1. Extract the body into **`SupplierFichaDetailContent({ supplierId, hostMode, readOnly, onClose, onSaved? })`**
   (takes `supplierId` as a prop instead of `useParams`).
2. Keep `SupplierFichaDetail` as a 3-line route wrapper rendering the content with `hostMode="page"`.
3. The Buyer Workspace hosts the **same** content in a drawer (`hostMode="drawer"`, `readOnly` per
   permissions) via the established right-side drawer pattern (`RequestDrawerPresentation` /
   `UserProfileDrawer` / `EquipmentQuickViewDrawer`).
4. **Width:** the sheet is a **2-column, max-width-1200px** layout whose single-column collapse is
   **viewport-based (`@media max-width:900px`)**, so a ~560px drawer in a wide viewport would keep 2
   columns and overflow. Add a **`hostMode="drawer"` CSS modifier** forcing `grid-template-columns: 1fr`
   on `.ficha-detail-grid`/`.ficha-doc-grid`, dropping the max-width/auto-margins and tightening
   padding. Drawer width: **~520–620px** (`min/max-width`, §10 of the Buyer doc).
5. **Save/refresh:** `handleSave` already does `PUT …/ficha` then re-fetches — unchanged; expose an
   optional `onSaved` so the Workspace can refresh the involved-supplier card.
6. **Close + unsaved changes (MUST ADD — currently absent):** the sheet has **no dirty/unsaved-changes
   tracking and no discard confirmation** (Cancel silently re-fetches). For a drawer, add a dirty check
   and intercept close while editing to reuse the existing confirm-modal pattern — otherwise a Buyer
   closing mid-edit loses data with no warning.

### Permission findings — **NEW BUSINESS/PERMISSION DECISION REQUIRED**
- **Current access:** the Supplier Sheet route is gated **only at the route level**:
  `AdminRoute allowedRoles={[ROLES.CONTRACTS]}`. **The component itself contains ZERO role/permission
  checks** — any user who reaches it gets full **edit / status-change / document upload-delete /
  submit-for-approval** powers (DAF/DG approval decisions live elsewhere, in the Centro de Aprovações).
- **Buyer today has NO access** to the Supplier Sheet (blocked by the Contracts-only route). Yet the
  Buyer role description is "Gere cotações, **fornecedores** e o andamento do processo de compra."
- **Consequence:** hosting the sheet in a Buyer drawer would **grant Buyers full Contracts-level edit
  powers** unless a `readOnly`/`canEdit` capability prop is **added** during extraction (gating the
  Editar button, status actions, document mutations, and submit).
- **DECISION REQUIRED (do not weaken permissions now):** should the Buyer drawer be **read-only** (view
  identity/commercial/documents), or should Buyers gain a **scoped edit** capability on supplier info
  from the Workspace? Recommended default = **read-only** for Buyers (add the prop; keep Contracts as
  the only full-edit role) until the product owner decides otherwise. The route stays `ROLES.CONTRACTS`;
  the drawer passes an explicit capability, so access is never granted implicitly by "being a Buyer".

### Access scope for Supplier Intelligence (recap)
Supplier Intelligence (metrics/history) is a Compras capability for **Buyer · LocalManager ·
SystemAdministrator** (§8); the **Supplier Sheet drawer** (identity/edit) is separately governed by the
Contracts permission decision above — the two are distinct authorizations.
