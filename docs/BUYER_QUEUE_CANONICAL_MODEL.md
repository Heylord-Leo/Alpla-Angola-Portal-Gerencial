# Buyer Queue — Canonical Model (GESTÃO DE COTAÇÕES)

> **Status:** Phase 0 — approved canonical design reference. Authoritative for the `/buyer/items`
> redesign campaign. **No implementation yet.** This document defines the target model; it does not
> change current behavior.
>
> Companion: `docs/SUPPLIER_INTELLIGENCE_MODEL.md`.

## 0. Scope & principles

- The Buyer queue answers one question per row: **"Qual pedido precisa da minha atenção agora, por
  quê, e o que devo fazer?"**
- **Workflow truth is server-derived.** The frontend must not infer operational state, next action,
  coverage, priority, or eligibility. A pure projection (Finance `FinanceObligationProjectionBuilder`
  doctrine) is the single source of truth.
- **`Request.Status` is macro lifecycle metadata**, not the Buyer operational state. It is demoted to
  tertiary/detail information in the UI.

> ## ⛔ HARD RULE — the existing Quotation Wizard is a STABLE INTEGRATION BOUNDARY
>
> This Buyer redesign campaign **MUST NOT redesign, refactor, or change the internal behavior of the
> existing Quotation Wizard** (`src/frontend/src/pages/Buyer/QuotationWizard/*`). The new Queue and
> Workspace may **invoke** it, **pass** Request/LineItem context to it, **receive** its completion
> result, and **refresh** after a successful quotation create/update — nothing more. They must **not**
> alter its steps, OCR flow, PDF/document upload, manual-quotation flow, supplier selection/creation,
> financial-integrity validation, field validation, modal navigation, or create/update semantics.
> If a Wizard-specific defect is found later: **STOP and report it as a separate work item.** This
> constraint is binding on every implementation-phase plan in this campaign.

---

## 1. Two-screen architecture (approved)

| Screen | Route | Purpose |
|---|---|---|
| **A — Buyer Queue** | `/buyer/items` (unchanged) | Request-level operational triage queue. |
| **B — Request Workspace** | **`/buyer/requests/{requestId}`** | Work on a single Request. |

Route rationale: full-page detail routes are the current convention (`/requests/:id`,
`/requests/:id/edit` in `App.tsx`), so `/buyer/requests/{requestId}` is idiomatic, deep-linkable and
refresh-safe. **Split-pane is rejected** as the primary desktop architecture. The read-only
`RequestDrawerPresentation` may remain as an optional quick-view only.

**Queue state must survive round-trips** — page + filters + ownership tab + selected work-queue card +
sort must be restored when returning from Screen B. (The current screen already persists queue state
via `useSearchParams` + `useTablePreferences('buyer-items')` + `?highlightRequestId`; the redesign
keeps this.)

---

## 2. Queue unit — REQUEST (approved)

The canonical Buyer queue **paginates and counts by Request**. Never by line item.

### Confirmed current defect (to be fixed in Phase 1, not before)
`GET /api/v1/line-items` (`LineItemsController.GetLineItems`) flattens `Request × LineItem` with
`SelectMany(r => r.LineItems.DefaultIfEmpty(), …)` (`:127-130`), so:
- **`totalCount` counts line-item rows** (`:155`), not Requests;
- **`Skip/Take` slices the flat row stream** (`:164-165`) with no Request-boundary awareness;
- ordering (`CreatedAtUtc, Request.Id, LineNumber`) keeps a Request's rows contiguous but a Request
  with more items than the remaining page room **splits across pages** (e.g. a 25-item Request at
  `pageSize=20` → items 1-20 page 1, 21-25 page 2);
- the frontend regroups rows by `RequestId` client-side, so a straddling Request is grouped from a
  **partial** item set → wrong coverage/status/next-action/counts, and "N resultados" ≠ cards shown.

**The existing `/line-items` endpoint remains untouched** for compatibility until the new queue ships.

---

## 3. Canonical Buyer operational states (approved)

Backend-derived, request-level. **State labels are professional descriptions** (no "Precisa Cotar" /
"Precisa Fazer…"); verbs live only on the next-action button.

| State | Display (PT) | Next action (button verb) | Actionable | Priority impact |
|---|---|---|---|---|
| `NEEDS_QUOTATION` | **Cotação Pendente** | Adicionar cotação | Yes | high |
| `PARTIAL_COVERAGE` | **Cobertura Parcial** | Completar cotações *(or, when covered items are independently eligible)* Completar cotações ou enviar itens cobertos | Yes | high |
| `READY_FOR_APPROVAL` | **Pronto para Aprovação** | Enviar itens para aprovação | Yes | med |
| `AWAITING_APPROVAL` | **Em Aprovação** | *(informational)* Aguardando aprovação | No | low |
| `ADJUSTMENT_REQUIRED` | **Ajuste Solicitado** | Revisar e reenviar lote | Yes | **exception (Band 1)** |
| `AWAITING_REQUESTER_DECISION` | **Aguardando Decisão** | *(informational)* Aguardando decisão do requisitante | No | low |
| `COMPLETED_FOR_BUYER` | **Concluído para Compras** | — | No | none (hidden by default, §8) |
| `NO_BUYER_ACTION` | **Sem Ação do Comprador** | — | No | none (hidden by default, §8) |

### Derivation (from per-item coverage §4 + batch state + ownership)
Precedence order (first match wins), evaluated over the Request's **active** (non-cancelled/deleted)
line items and its `ApprovalBatches`:

1. **`ADJUSTMENT_REQUIRED`** — any batch in `AREA_ADJUSTMENT` or `FINAL_ADJUSTMENT` (Buyer must act).
2. **`AWAITING_APPROVAL`** — any batch in `WAITING_AREA_APPROVAL` or `WAITING_FINAL_APPROVAL`, and no
   adjustment batch (info only).
3. **`AWAITING_REQUESTER_DECISION`** — any item `NOT_QUOTED_PROPOSED` (legacy) and nothing above.
4. **`READY_FOR_APPROVAL`** — every not-yet-terminal item is in the "Cotado — pronto para lote" bucket
   (all coverable items have a selectable candidate), none currently in an active batch.
5. **`PARTIAL_COVERAGE`** — a mix: at least one covered/ready item **and** at least one "Pendente de
   cotação" item.
6. **`NEEDS_QUOTATION`** — one or more items and none are covered/ready (all pending).
7. **`COMPLETED_FOR_BUYER`** — all active items terminal (`QUOTATION_APPROVED` / `CLOSED_NOT_QUOTED` /
   `NOT_QUOTED_ACCEPTED`) or the Request has advanced past the Buyer quotation phase (PO/payment/…).
8. **`NO_BUYER_ACTION`** — not a Buyer-phase Request (e.g. non-QUOTATION, or Buyer scope N/A).

> The exact predicates must be implemented **once** in `BuyerQueueProjectionBuilder` and validated
> against `RequestWorkflowProjectionBuilder` / `StatusSyncService`; do not re-derive in the frontend.

---

## 4. Coverage taxonomy — per line item, mutually exclusive (approved)

Source of truth: `RequestLineItem.QuotationLifecycleStatus` (**null ≈ `QUOTATION_PENDING`**), the
operational `lineItemStatusCode`, and cross-reference to `ApprovalBatches` + `Quotations`.
**`RequestPoGroup` is downstream of batch approval and must NOT influence Buyer quotation coverage.**

| Bucket (PT) | Backend condition |
|---|---|
| **Cancelado/Excluído** | `IsDeleted = true` OR `lineItemStatusCode ∈ {CANCELLED, DELETED}` |
| **Aprovado** | `QuotationLifecycleStatus = QUOTATION_APPROVED` |
| **Em lote ativo** | `QuotationLifecycleStatus = BATCH_ASSIGNED` and its batch ∈ `{WAITING_AREA_APPROVAL, AREA_ADJUSTMENT, WAITING_FINAL_APPROVAL, FINAL_ADJUSTMENT, APPROVED}` |
| **Encerrado sem cotação** | `QuotationLifecycleStatus = CLOSED_NOT_QUOTED` (terminal, current one-step Buyer close) |
| **Não-cotado proposto** *(legacy)* | `QuotationLifecycleStatus = NOT_QUOTED_PROPOSED` (0 in current data; still code-live) |
| **Não-cotado aceito** *(legacy)* | `QuotationLifecycleStatus = NOT_QUOTED_ACCEPTED` (terminal; 0 in current data) |
| **Cotado — pronto para lote** | (`QuotationLifecycleStatus` ∈ {null, `QUOTATION_PENDING`}) **and** ∃ `QuotationItem` with `MappedRequestLineItemId = item` and `ReconciliationStatus ∈ {MAPPED, SUBSTITUTE}` and **selectable** (not held by an active/approved batch) |
| **Pendente de cotação** | (`QuotationLifecycleStatus` ∈ {null, `QUOTATION_PENDING`}) and no selectable candidate |

### Batch-release & selectability rules (must be honored)
- **REJECTED batch** → its items return to the pending pool (`QuotationLifecycleStatus = null`);
  `SelectedQuotationItemId` preserved for audit.
- **CANCELLED batch** → items released; a quotation item used in a cancelled batch may not be
  re-selected without an explicit active `QuotationReuseAuthorization` (Option C).
- **Superseded batch** → a batch still "in approval" whose items were already processed by another
  active flow is **excluded from active units and surfaced as a warning** (never deleted) —
  `SupersededBatchPolicy`.
- **Active batch detection** = batch status ∈ `{WAITING_AREA_APPROVAL, AREA_ADJUSTMENT,
  WAITING_FINAL_APPROVAL, FINAL_ADJUSTMENT, APPROVED}` (CANCELLED/REJECTED do **not** hold items).
- **Selectable quotation candidate** = a `QuotationItem` not referenced (as `selectedQuotationItemId`
  or a submitted candidate) by any active/approved batch.

Current fleet distribution (QUOTATION, non-deleted): `null 240, QUOTATION_APPROVED 166,
BATCH_ASSIGNED 29, CLOSED_NOT_QUOTED 5`; batches `APPROVED 72, REJECTED 7, WAITING_FINAL 7,
WAITING_AREA 6, AREA_ADJUSTMENT 1, CANCELLED 1`; **14 multi-batch Requests**.

---

## 5. Priority model — approved (transparent two-band)

**Band 1 — `EXCEPTION_OR_OVERDUE`** (any of): `ADJUSTMENT_REQUIRED` / other documented blocking Buyer
exception; **or** deadline `OVERDUE`.
**Band 2 — `STANDARD`** (everything else).

Within each band, sort by:
1. **Need Level** — `CRITICO > URGENTE > NORMAL > BAIXO`
2. **Deadline Condition** — `OVERDUE > DUE_TODAY > APPROACHING > WITHIN_DEADLINE`
3. **NeedByDate** ASC
4. **CreatedAtUtc** ASC

**No opaque numeric priority score.**

---

## 6. Deadline conditions — approved (`ApproachingDeadlineDays = 3`)

| Condition | Rule (`today` = UTC date) |
|---|---|
| `OVERDUE` | `NeedByDate < today` |
| `DUE_TODAY` | `NeedByDate = today` |
| `APPROACHING` | `today < NeedByDate <= today + 3 days` |
| `WITHIN_DEADLINE` | `NeedByDate > today + 3 days` |

The `3` is approved but must be **centralized in backend configuration** (Phase 1+), not scattered.

---

## 7. Summary cards — approved definitions (REQUEST counts only)

Counts are always **Requests in the current scope/ownership/search set**, computed server-side over the
same filtered set as the list (Finance card-scoping doctrine). Never line-item counts.

| Card | Backend condition (Request-level) | Overlap |
|---|---|---|
| **Todos os Pedidos** | Requests in Buyer scope **and** in an active Buyer phase (excludes `COMPLETED_FOR_BUYER`/`NO_BUYER_ACTION` by default, §8) | superset of all below |
| **Sem Cotação** | OperationalState = `NEEDS_QUOTATION` | disjoint from Cobertura Parcial / Em Aprovação |
| **Cobertura Parcial** | OperationalState = `PARTIAL_COVERAGE` | disjoint from Sem Cotação |
| **Em Aprovação** | OperationalState = `AWAITING_APPROVAL` (no adjustment batch) | disjoint from Requer Atenção |
| **Requer Atenção** | `ADJUSTMENT_REQUIRED` **∪** deadline `OVERDUE` **∪** superseded-batch warning | **intentionally overlaps** the others (a `NEEDS_QUOTATION` Request that is also overdue appears in both Sem Cotação and Requer Atenção) |

**Overlap rule:** the four operational cards (Sem Cotação / Cobertura Parcial / Em Aprovação / +
implicit others) are mutually exclusive by OperationalState; **Requer Atenção is an orthogonal
attention lens** that may overlap any actionable state. Cards are clickable filters (Finance
work-queue pattern).

Approx. fleet: `WAITING_QUOTATION` 36 (Sem Cotação ~30 + Cobertura Parcial ~5); `WAITING_FINAL` 6 /
`WAITING_AREA` 2 (Em Aprovação); `AREA_ADJUSTMENT` 3 (Requer Atenção) + overdue.

---

## 8. Completed-request visibility — approved

`COMPLETED_FOR_BUYER` and `NO_BUYER_ACTION` Requests are **hidden from the default active queue**.
They remain **queryable via an explicit filter/history scope** (e.g. an "Incluir concluídos" toggle or
a History view). **No data is deleted or archived.**

---

## 9. Ownership — approved UX

Tabs: **Todos · Meus Pedidos · Não Atribuídos** (server-side `owner` filter, kept).

| Situation | Action | Confirmation |
|---|---|---|
| Unassigned → current Buyer | **Atribuir a Mim** | **No** confirmation |
| Owned by another Buyer, actor is a regular Buyer | (no mutate/reassign) | n/a — blocked |
| SystemAdministrator / LocalManager reassigning | **Assumir Pedido** / reassign | **Yes** — confirmation required |

---

## 10. assign-buyer — security finding (Phase-1 correctness item)

**Current authorization (READ-ONLY finding):** `RequestsController.AssignBuyer`
(`POST /api/v1/requests/{id}/assign-buyer?targetUserId=`) is method-gated only by the controller's
class-level `[Authorize]` — **no role policy**. The 409 "reencaminhamento só é permitido por
coordenadores" guard (`:3003`) triggers **only when `BuyerId.HasValue`**. Therefore:
- **An UNASSIGNED Request can be self-claimed as buyer by ANY authenticated user** (Requester, Area/
  Final Approver, Finance, Viewer) — not just Buyers. There is no `RoleConstants.Buyer` check.
- `canReassign = SystemAdministrator || LocalManager` (`:2999-3001`); those roles may assign **any
  `targetUserId`** with **no validation that the target holds the Buyer role**.
- Current owner is idempotent (`:3015`).

**Expected policy:** claiming/self-assign restricted to **Buyer** (∪ SystemAdministrator/LocalManager);
reassignment restricted to **SystemAdministrator/LocalManager**; `targetUserId` must be a user with the
Buyer role.

**Callers / FE dependency:** `BuyerItemsList.handleAssignToMe → api.requests.assignBuyer(requestId)`
(self-assign, no `targetUserId`); the Buyer screen route is already Buyer-gated (`AdminRoute
allowedRoles=[BUYER]`), so the UI path is safe — **but the endpoint itself is exploitable** by any
authenticated caller. **Recommended fix (Phase 1, not now):** add an explicit role policy on the
action (`[Authorize(Roles = Buyer + "," + SystemAdministrator + "," + LocalManager)]`) plus a target-
is-Buyer check on reassign. Preserve the 409 reassignment guard.

---

## 11. Request Workspace tabs — approved (3 tabs, no History yet)

| Tab | Responsibilities — data | Actions |
|---|---|---|
| **1. Itens & Cobertura** | Request header context; per-item table with the §4 coverage bucket per item; coverage summary (counts per bucket); operational state + next action. | Quotar item, marcar/encerrar sem cotação (`close-not-quoted`), add requested item (reconciliation flow), advance covered items → batch. |
| **2. Cotações & Documentos** | Saved quotations (supplier snapshot, currency, totals, document type/OCR), quotation items & mappings (MAPPED/SUBSTITUTE/EXTRA/IGNORED), attachments/proformas; **contextual supplier summary** (§12/§16 of Supplier model) with "Ver histórico do fornecedor". | Quotation wizard (OCR/manual), edit/replace document, reuse quotation, delete quotation, quick-create supplier/currency. |
| **3. Lotes & Aprovações** | All `ApprovalBatches` for the Request with batch status, items/candidate options, winners, approver names, superseded warnings. | Create partial-approval batch, corrigir lote (`*_ADJUSTMENT`), cancelar lote (reversible), reuse from cancelled batch. |

---

## 12. Known current defects (documented for the campaign)
1. **Pagination unit = line items** → partial-Request grouping, wrong coverage/status/action/counts (§2).
2. **Macro `Request.Status` presented as the Buyer operational signal** — misleading for partial-
   coverage Requests (measured: 5 of 35 `WAITING_QUOTATION` Requests are partially covered yet still
   show "AGUARDANDO COTAÇÃO / COTAR"); the red "AÇÃO NECESSÁRIA" badge is a static re-label of
   `Request.Status` (`getActionBadge`), blind to coverage/ownership.
3. **3163-line monolith re-derives workflow truth client-side** (`buyerItemStatus.ts`,
   `batchEligibility.ts`, `calculateCoverage`), duplicating backend rules → drift risk.
4. **assign-buyer has no role gate** (§10).
5. Three co-equal loud badges (blue status + red action + ownership CTA) obscure the decision signal.

---

## 13. Addendum — Main screen & Workspace UI decisions (approved)

### 13.1 Main screen `/buyer/items` — target IA (list/dashboard mockup)
The main screen **stays at `/buyer/items`** and moves toward the approved **first (list/dashboard)
mockup**. Its role is **triage · prioritization · quick filtering · opening a specific Request
Workspace** — not a work surface. It shows: page header, ownership tabs, KPI/queue-summary cards
(§7), search + operational-state filter + sort + advanced filters, **Request-level** rows/cards each
with a **kebab actions menu**, pagination (by Request). **Exclude the "Precisa de Ajuda?" element** —
it is not part of the target design.

### 13.2 Main-screen kebab actions (per Request row)
| Action | Behavior (approved) |
|---|---|
| **Ver detalhes** | **Navigate to the dedicated Workspace** `/buyer/requests/{requestId}` (cleanest with the new two-screen IA). The read-only `RequestDrawerPresentation` may remain only as an optional quick-peek, not the primary path. |
| **Cancelar pedido** | Shown **only when eligible** (see §13.3). Opens the existing cancel-reason flow (`POST /requests/{id}/cancel?mode=BUYER`). |
| **Adicionar observação** | Reuse the **Finance note pattern** (see §13.4). |

### 13.3 Cancellation eligibility — current rules (documented; not changed)
From `RequestsController.CancelRequest` (`:5053-5128`) — a **QUOTATION** request is cancellable by a
Buyer (`mode=BUYER`) **only when**:
- not already terminal (`IsCancelled`/`CANCELLED`/`COMPLETED`/`REJECTED` → 409), **and**
- status is `WAITING_QUOTATION` (a Buyer may **not** cancel `DRAFT` via `mode=BUYER`), **and**
- **no Buyer processing has started** — blocked if `Request.SupplierId` is set, **or** a `PROFORMA`/
  `QUOTATION` attachment exists, **or** any line item has a supplier or a `LineItemStatus` beyond
  `WAITING_QUOTATION`/`PENDING`.
- (`PAYMENT` requests: Buyer cannot cancel at all — 409.)

**Flagged issues (fix later, not now):**
1. **No role gate** on the cancel endpoint — like `assign-buyer`, it is only class-level `[Authorize]`
   and trusts the client-supplied `mode`. A non-Buyer could omit `mode` and use the broader path.
2. **Divergent client eligibility checks** — `RequestEdit.canCancelRequest` checks `formData.supplierId`
   while `BuyerItemsList` checks `group.requestSupplierId` (`api.ts:4946` comment). The redesign should
   **expose a server-computed `canCancel` flag** on the queue projection so the kebab never re-derives
   eligibility client-side (correctness item).

### 13.4 "Adicionar observação" — reuse the Finance note pattern
Do **not** invent a second notes pattern. Reuse the Finance mechanism:
- **Persistence model:** request-level `RequestStatusHistory` row, `ActionTaken = "<note code>"`,
  `Comment = "<prefix>: {text}"` (Finance uses `NOTA_FINANCEIRA` / "Nota de Finanças: "). Notes are
  **request-level**, not entity/line-item level.
- **Indicator:** a subtle **StickyNote** icon in the row header once a note exists, with a
  **ModernTooltip** showing the latest note + "+N observação(ões) anterior(es)" (mirrors Finance
  `NoteIndicator` in `FinancePaymentsList.tsx`). The queue projection exposes `hasNotes`, `noteCount`,
  `latestNoteText`, `latestNoteAtUtc`, `latestNoteActorName` (batched for the page only).
- **Reuse strategy (documented; implement later):** the literal `POST /finance/{id}/note` is
  Finance-scoped (`NOTA_FINANCEIRA`). For Buyer, add a **generic request-note endpoint**
  (`POST /requests/{id}/note`, `ActionTaken = "OBSERVACAO"` or `"NOTA_COMPRADOR"`) and have the queue
  projection read that action code (and optionally surface Finance notes too). Same UX, same
  `RequestStatusHistory` model, new action code — **not** a second notes subsystem.

### 13.5 `/buyer/requests/{requestId}` — Workspace: "Solicitar cotação" (email helper, NOT the Wizard)
The Workspace button **"Solicitar cotação"** is a **communication convenience**, not a quotation-
registration flow, and **MUST NOT launch or modify the Quotation Wizard** (§0 hard rule). It opens the
user's mail client (Outlook) with a prefilled draft to request quotes for the **still-open/uncovered**
items:
- **Launch strategy:** `mailto:?subject=…&body=…` (URL-encoded), consistent with existing in-app
  `mailto:` usage (`UserProfileDrawer`). Windows opens Outlook by default. **Limitation:** `mailto`
  bodies are length-capped (~1900-2000 chars) — for large item lists, truncate the list and/or offer a
  "Copiar rascunho" fallback or a tiny compose-preview modal.
- **Reliable template fields** (all present in the request DTO): **Request number**, **title**,
  **company / plant / department**, **required-by date** (`needByDateUtc`), and the **list of open
  items** (description · quantity · unit) — restricted to items in the *Pendente de cotação* / *open*
  buckets (§4). Do not include prices or supplier data.
- Formal quotation registration continues **only** through the unchanged Quotation Wizard.

### 13.6 Workspace — "Inteligência dos Fornecedores" (contextual carousel; involved suppliers only)
A **carousel** section inside the Workspace showing **only suppliers involved in the currently opened
Request** — never a global recommendation/search widget (global search is the separate future
"Pesquisa de Fornecedores"). **Involved supplier =** derived from **this Request's `Quotations`** (and
their `QuotationItems`), plus the **selected quotation** where applicable — the quotation-stage truth.
**Do not** use downstream `RequestPoGroup` data to define quotation-stage involvement. **Deduplicate by
`SupplierId` → normalized NIF fallback; never merge by name alone.** Each card shows only the
approved-reliable metrics from `SUPPLIER_INTELLIGENCE_MODEL.md` (purchase count, Total comprado per
currency, last purchase, quotation participation), scoped to that supplier's global history but
**surfaced here only because the supplier is on this Request**. See `SUPPLIER_INTELLIGENCE_MODEL.md §11`.

### 13.7 Workspace — "Ver Perfil Completo" → Supplier Sheet in a right-side drawer (reuse, don't rebuild)
"Ver Perfil Completo" opens a **right-side drawer** that renders the **same Supplier Sheet component**
used by `/contracts/fichas/{supplierId}` — **no second supplier form.** Reuse strategy and the critical
**permissions** finding are documented in `SUPPLIER_INTELLIGENCE_MODEL.md §12`. **Do not implement the
extraction in this phase.**

### 13.8 Target resolutions & responsive layout (approved)
Official desktop acceptance targets: **1600 × 900** and **1920 × 1080**. Both the Queue and the
Workspace must: **no page-level horizontal scroll**; primary action always visible; Request header
readable; tabs accessible; long names truncated with tooltip; readable tables; Portal tokens/components;
**dark mode**.
- **1600 × 900:** prioritize **density** and efficient vertical use (compact rows, tighter KPI cards);
  wide tables scroll inside their own `overflow-x:auto` container, never the page.
- **1920 × 1080:** use the available width **without excessive whitespace** (max content width caps,
  balanced columns). **Do not design exclusively for 1920.**

### 13.9 Supplier drawer sizing (recommendation)
Responsive desktop drawer, **~520–620px class** (use `min-width`/`max-width`, not a single fixed px, per
Portal standards). At **1600 × 900** the remaining Workspace content must stay usable with the drawer
open; at **1920 × 1080** the drawer may take the wider end of the range. **The exact width is validated
against the Supplier Sheet form during implementation** (the sheet is currently a 2-column layout that
needs a `hostMode="drawer"` single-column modifier — see `SUPPLIER_INTELLIGENCE_MODEL.md §12`).

### 13.10 Updated future-phase boundaries (this campaign)
- **Phase 1 — backend Buyer queue only.** `BuyerQueueProjectionBuilder` + `GET /buyer/queue` + summary,
  Request-level pagination, server operational-state/next-action/priority/ownership, assign-buyer
  security fix, and a server `canCancel` flag. **NO Quotation Wizard change. NO UI.**
- **Future Main-screen phase** — redesign `/buyer/items` toward the first mockup; kebab actions;
  Add-Note pattern reuse; preserve/clarify cancellation rules (exclude "Precisa de Ajuda?").
- **Future Workspace phase** — implement `/buyer/requests/{requestId}` (second mockup); integrate the
  **unchanged** Quotation Wizard; add "Solicitar cotação" (Outlook helper); Supplier Intelligence
  **carousel** (involved suppliers only); reuse the Supplier Sheet via drawer.
- **Global "Pesquisa de Fornecedores"** remains a **separate** later feature.
