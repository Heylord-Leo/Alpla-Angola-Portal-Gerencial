# Dashboard V2 — Functional & Technical Specification

**Status:** APPROVED — slice B1+B2 implemented in v2.237.0 (DEV human acceptance PASS, 2026-09-02)
**Phase:** B
**Product decisions:** 7 / 7 DECIDED (closed 2026-09-02)
**Baseline application version:** v2.236.0 (this spec's baseline); B1+B2 shipped in v2.237.0
**Baseline main commit:** 176543ed4f6337785db8c87cc48aebc820047d02
**Validation basis:**
- Phase A code audit
- Phase A.1 read-only PROD validation

**Date:** 2026-09-02

> **Note.** This specification defines the target semantics and architecture.
> Validated PROD counts are evidence supporting the design, **not** hard-coded
> expected values. No number in this document may be encoded as a business constant.

> **Decision closure.** The seven Phase B product decisions (PD-01…PD-07) are all
> **DECIDED** and binding for the Dashboard V2 baseline — see §27. There are zero open
> product decisions remaining from this set.

---

## The Three-Plane Model (non-negotiable)

Every section of Dashboard V2 is classified into exactly one of three planes, and
the plane is always visible to the user (via a labelled pill). This distinction is
the product spine and is **non-negotiable**.

1. **PESSOAL** — Work explicitly owned by the current user (personally assigned to,
   or owned by, the signed-in user *now*).
2. **COMPARTILHADO** — Operational queues available through **role + scope**, but
   **not personally assigned**. Counted as queue depth, never as "mine".
   Includes Compras, Finance, Recebimento **and Final Approval** (PD-01).
3. **GERENCIAL** — Management visibility that **must never imply personal
   ownership**. Global/scope overview only. Includes the buyer workload panel, the
   pipeline, alerts, the financial summary, and (PD-04) Finance/Receiving aggregate
   cards shown to a Local Manager without the operational role.

A shared queue must never be presented as personal work. A managerial overview must
never be presented as personal work.

---

## Canonical Sources (reimplementation prohibited)

Dashboard V2 **MUST NOT** re-implement a domain predicate where a canonical
operational projection/service already exists. The dashboard **calls** the canonical
source; it never copies its status logic. The current dashboard bug is precisely this
duplication (status predicates inlined in the controller), and it must not be repeated.

| Domain | Canonical source (authoritative) |
|---|---|
| Buyer | `BuyerQueueProjectionBuilder` / `NextActions` / item coverage (`ClassifyItemCoverage`) |
| Finance | `FinancePaymentEligibilityService` + `RequestPoGroup` |
| Receiving | `RequestPoGroup` receiving actions (`MOVE_TO_RECEIPT`, `CONFIRM_RECEIVING`) |
| Approvals | `ApprovalBatch` semantics (batch state + candidate/scope assignment); Final approval is a shared queue (PD-01) |
| Adjustments | `ApprovalBatchAdjustment` cycles (`AdjustmentConstants.States`) |
| Financial | stage-authoritative entity + currency-safe aggregation |

**Guardrail:** no dashboard projection re-implements a status predicate that already
lives in a domain service.

---

## Validated PROD Evidence — 2026-09-01

The following observations are historical validation evidence from the Phase A.1
read-only PROD run. **They MUST NOT be encoded as business constants.** They justify
the design; they are not expected outputs.

**OLD "Aguardando minha ação" (Leonardo Cintra):**
- 180 unique requests

**Old clause decomposition:**
- Finance: 93
- Receiving: 66
- Buyer unassigned: 11
- Final Approver: 9
- Requester: 3
- Buyer assigned to Leonardo: 1
- Area Approval: 0
- unique requests: 180 · total clause matches: 183 · 3 requests matched multiple clauses

**Buyer (Leonardo):**
- assigned / actionable = 1
- unassigned shared = 11
- unassigned actionable = 10

**Finance:**
- old dashboard population = 93 requests
- canonical = 105 actionable requests
- 113 actionable `RequestPoGroup`s
- OLD_AND_ACTIONABLE = 76
- OLD_ONLY = 17
- CANONICAL_ONLY = 29
- Finance is a **shared role + scope** queue (no per-user assignment)

**Receiving:**
- old dashboard population = 66 requests
- canonical = 70 actionable requests
- 70 actionable `RequestPoGroup`s
- OLD_AND_ACTIONABLE = 63
- OLD_ONLY = 3
- CANONICAL_ONLY = 7
- Receiving is a **shared role + scope** queue (no per-user assignment)

**Old global overdue:**
- 217 flagged overdue
- stale classification = 140
- stale share = 64.52%

> These values are historical validation evidence and MUST NOT be encoded as
> business constants. All counts remain subject to live re-confirmation via the
> sanctioned read-only path.

---

## 1. Executive Summary

The current Cockpit (`GET /api/requests/cockpit-summary`) unions role + scalar-status
predicates and presents the result as personal work. Phase A.1 proved on PROD that
this is materially wrong: of the 180 requests in Leonardo's "Aguardando minha ação",
only **1** is truly personal (a buyer-assigned actionable request); the rest are
shared queues (Finance 93, Receiving 66), an unassigned buyer pool (11), Final-Approver
role fan-out (9), or stale.

Dashboard V2 replaces one flat metric with three honestly-labelled planes — Pessoal,
Compartilhado, Gerencial — each sourced from the canonical projection that already
governs the matching operational screen. **This is a re-sourcing, not a workflow
change.** No business rule is altered. The single most important behavioral change:
**the dashboard stops claiming shared work as personal.** The seven product decisions
that shape the planes, thresholds and placements are all closed (§27).

---

## 2. Validated Design Constraints

Eight findings from Phase A / A.1 are treated as fixed inputs.

| # | Finding | Consequence for V2 |
|---|---|---|
| A | "Minha ação" is invalid — role×status union, not ownership (180 → 1 personal) | Must not be preserved; split into three planes |
| B | Buyer canonical = coverage projection; unassigned is shared (assigned 1 · unassigned 11, 10 actionable) | Never attribute `BuyerId NULL` personally |
| C | Finance shared by role+scope; scheduled is still actionable (old 93 · canon 105 req / 113 grp) | Present as shared queue; `ScheduledDateUtc` for urgency |
| D | Receiving action = `RequestPoGroup`; shared by role+scope (old 66 · canon 70 req / 70 grp) | Requester alone never creates a receiving action |
| E | Overdue global `NeedByDate` is stale past-action (217 flagged · 140 stale · 64.52%) | Due-date semantics must be domain-specific; an alert requires an OPEN obligation (PD-02) |
| F | Pipeline is a scalar-status histogram | Must be group/batch/item-aware; allow multi-stage |
| G | Financial sums currencies numerically (NULL-currency contamination) | Never aggregate across currency; partition by it |
| H | Bottleneck age = request age, not stage dwell (`today − CreatedAtUtc`) | No SLA claim without a stage-entry timestamp (PD-06) |

---

## 3. Information Architecture

Six sections, ordered from "what only I can do" to "how the whole operation looks".
The Pessoal / Compartilhado / Gerencial separation is the spine of the page; every
card carries its ownership pill.

- **A · Minha Operação** — `PESSOAL`. Only operations personally owned by the signed-in
  user now. May be empty — a correct, honest result. Final Approval is **not** here (PD-01).
- **B · Filas Operacionais** — `COMPARTILHADO`. Role+scope queues: Compras, Finance,
  Recebimento, **Aprovações incl. Final** (PD-01). Queue depth, never "mine".
- **C · Gestão da Equipe de Compras** — `GERENCIAL`. Buyer workload distribution
  including the UNASSIGNED pool. No performance scoring.
- **D · Pipeline Gerencial** — `GERENCIAL`. Operational domains (Compras → Completion),
  group/batch/item-aware, multi-stage allowed.
- **E · Alertas e Gargalos** — `GERENCIAL`. Canonical alerts (an alert requires an OPEN
  obligation, PD-02) + stage-aging where a defensible timestamp exists (PD-06).
- **F · Resumo Financeiro** — `GERENCIAL`. Currency-partitioned totals from
  stage-authoritative amounts; paid history is a secondary managerial summary (PD-05).

---

## 4. Minha Operação (Personal Work)

"Minha ação" means an operation **personally assigned to or owned by** the signed-in
user right now — nothing weaker.

**Explicitly excluded:**
- Shared Finance queue merely because the user holds the Finance role.
- Shared Receiving queue (role-gated, not assigned).
- The `BuyerId NULL` unassigned pool.
- **Final Approval — decided SHARED, not personal (PD-01).** Holding the Final Approver
  role never makes eligible batches "my actions"; Final Approval lives under COMPARTILHADO.
- Managerial / admin global visibility.

| Metric | Definition (personal ownership only) | Canonical source | Level | Due date | Drill-down |
|---|---|---|---|---|---|
| Minhas ações | Buyer requests where `BuyerId = me` & actionable; adjustment cycles where I am the resolver; requester DRAFT/adjustment I own | BuyerQueueProjectionBuilder · adjustment-cycle svc | request / cycle | domain-specific (§10) | Buyer Workspace / request |
| Minhas ações vencidas | Subset past its *domain* due date, still actionable | same + §10 date map | request | domain | same, filtered vencidas |
| Minhas ações críticas | Subset within the critical window (PD-03) | same | request | domain | same, filtered críticas |
| Próximas ações | Actionable within the near window (PD-03: 1–2 days) | same | request | domain | same |

**Honesty rule.** For a pure-Finance or pure-Receiving user, true personal ownership is
near-zero — the system genuinely has no per-user assignment there, and Final Approval is
shared (PD-01). This section then shows an explicit empty state ("Nenhuma ação atribuída
pessoalmente — veja as filas compartilhadas abaixo") and directs the user to Section B.
**We do not fabricate personalization.**

---

## 5. Filas Operacionais Compartilhadas

Role-aware queue cards. Every count is queue depth within the viewer's plant/department
scope — never personal ownership. Shown to users holding the matching role (or, per PD-04,
to a Local Manager / SysAdmin as a `GERENCIAL` aggregate card without action buttons).

**Compras — fila partilhada:** Source `BuyerQueueProjectionBuilder` over
`BuyerId IS NULL`. Metrics: pedidos não atribuídos (11), não atribuídos acionáveis (10),
itens sem cotação, itens prontos para lote. Item counts primary, request count secondary.

**Finance — fila partilhada:** Source `FinancePaymentEligibilityService` per group
(SCHEDULE/PAY/CANCEL_SCHEDULE/RETURN). Metrics: pedidos acionáveis (105), grupos
acionáveis (113), para agendar (7), agendado/acionável (106), agendado vencido. Groups
primary. Urgency from `ScheduledDateUtc` (PD-02).

**Recebimento — fila partilhada:** Source = `RequestPoGroup` status ∈
{PAYMENT_COMPLETED, WAITING_RECEIPT, IN_FOLLOWUP, WAITING_SUPPLIER_DELIVERY}. Metrics:
pedidos acionáveis (70), grupos acionáveis (70), entrada/pago (48), aguardando recibo
(19), acompanhamento (2), aguardando fornecedor (1). Groups primary. Aging wording
"há X dias nesta etapa" (PD-02/PD-06); never "overdue" from aging alone.

**Aprovações — fila partilhada (Final) + Área scoped:** Per PD-01, **Final Approval is a
shared queue** based on role + scope and belongs to COMPARTILHADO — primary unit
`ApprovalBatch`, secondary distinct requests; it is never personal. Area approval is
candidate/DepartmentManager-scoped (partly personal — it appears in Minha Operação only
for the specific approver/manager who owns the batch). No `FinalApproverId` or new
assignment model is introduced (PD-01).

For each queue: eligibility = the exact canonical predicate (no scalar shortcuts);
visibility = role held OR manager (then Gerencial-labelled, no action buttons — PD-04);
scope = `RequestAccessScope` (plant/dept), SysAdmin global; drill-down = the operational
screen with the server filter that reproduces the count (§14). **Do not display queue
counts as personal ownership.**

---

## 6. Buyer Team Workload Panel

Management distribution, one row per buyer plus an UNASSIGNED row — a workload view,
never a scorecard. All values from `BuyerQueueProjectionBuilder`; no frontend
recomputation.

| Column | Semantic | Level | Default visible |
|---|---|---|---|
| Buyer | Owner (or UNASSIGNED) | — | ✓ |
| AssignedRequests | `BuyerId = buyer`, non-terminal | request | ✓ |
| ActionableRequests | ≥1 actionable NextAction | request | ✓ |
| PendingQuotationItems | Σ PendingQuotation items | item | ✓ |
| ReadyForBatchItems | Σ QuotedReadyForBatch items | item | ✓ |
| NeedsQuotationRequests | operational-state count | request | ⌄ |
| PartialCoverageRequests | operational-state count | request | ✓ |
| ReadyForApprovalRequests | operational-state count | request | ⌄ |
| AdjustmentRequests | open cycle, buyer is resolver | cycle | ✓ |
| OverdueActionableRequests | actionable ∩ `NeedByDate < today` (PD-03) | request | ✓ |
| CriticalActionableRequests | actionable ∩ `NeedByDate = today` (PD-03) | request | ⌄ |

- **UNASSIGNED:** separate row, `COMPARTILHADO` treatment, pinned to top; never merged
  into any buyer.
- **Sort:** default ActionableRequests desc; UNASSIGNED pinned regardless.
- **Drill-down:** row → Buyer Workspace filtered to that buyer; UNASSIGNED → unassigned filter.
- **Zero-state:** "Sem carga atribuída" per empty buyer; hide fully-idle buyers behind a toggle.
- **Permissions:** Local Manager / SysAdmin / (Buyer lead if defined). A plain Buyer sees
  only their own row + UNASSIGNED.
- **Deferred:** OldestActionAge / AverageActionAge / MedianActionAge — need a reliable
  action/stage-entry timestamp (PD-06); omitted until that persisted source exists.

Buyer urgency (Overdue/Critical columns and buyer alerts) follows PD-03 and applies only
while a real Buyer action (`ADD_QUOTATION`, `SUBMIT_BATCH`, `RESOLVE_ADJUSTMENT`) is
available — once the obligation moves to Approval/PO/Finance/Receiving, the buyer is no
longer flagged.

**No subjective scoring, no good/bad ranking.** Age metrics are deferred rather than
approximated from request creation.

### 6b. Buyer Workload UX
- **Dashboard:** compact management table (columns above), collapsible, UNASSIGNED pinned.
  A manager summary — not an operations tool.
- **Gestão de Cotações / Buyer list:** a slim distribution strip of buyer chips above the
  request list. Clicking a buyer chip filters the list; clicking UNASSIGNED filters
  unassigned; the active filter is visibly stated. Counts must reconcile exactly with the
  same projection — the strip renders server counts, it does not recompute them.

---

## 7. Canonical Operational Pipeline

Replaces the scalar histogram with operational domains. A request with obligations in
several groups/batches legitimately contributes to several stages — the pipeline stops
pretending each request is in exactly one place.

| Domain · Stage | Entity | Source | Count unit | Inclusion rule | Drill-down |
|---|---|---|---|---|---|
| Compras · Sem cotação | request/item | BuyerQueueProjectionBuilder | items + req | state = NeedsQuotation | /buyer/items |
| Compras · Cobertura parcial | request/item | same | items + req | state = PartialCoverage | /buyer/items |
| Compras · Pronto p/ aprovação | request/item | same | req | state = ReadyForApproval | /buyer/items |
| Aprovações · Área | ApprovalBatch | batch state + scope | batches + req | batch = WAITING_AREA_APPROVAL | /approvals |
| Aprovações · Final (shared, PD-01) | ApprovalBatch | batch state | batches + req | batch = WAITING_FINAL_APPROVAL | /approvals |
| Aprovações · Reajuste | AdjustmentCycle | adjustment-cycle svc | cycles + req | cycle status ∈ open | /approvals |
| P.O. · Aguardando P.O. | RequestPoGroup | group status | groups + req | WAITING_PO / PENDING | /buyer or /requests |
| P.O. · Correção | RequestPoGroup | group status | groups | WAITING_PO_CORRECTION | /requests |
| Finance · Para agendar | RequestPoGroup | eligibility svc | groups + req | CanSchedule | /finance/payments |
| Finance · Agendado | RequestPoGroup | eligibility svc | groups | PAYMENT_SCHEDULED (actionable) | /finance/payments |
| Finance · Vencido | RequestPayment | ScheduledDateUtc | groups | scheduled < today, unpaid & action open | /finance/payments |
| Finance · Pago | RequestPoGroup | group status | groups | PAYMENT_COMPLETED | /finance |
| Recebimento · Entrada | RequestPoGroup | receiving rules | groups | PAYMENT_COMPLETED | /receiving/workspace |
| Recebimento · Aguardando | RequestPoGroup | receiving rules | groups + req | WAITING_RECEIPT | /receiving/workspace |
| Recebimento · Acompanhamento | RequestPoGroup | receiving rules | groups | IN_FOLLOWUP | /receiving/workspace |
| Recebimento · Aguardando fornecedor | RequestPoGroup | receiving rules | groups | WAITING_SUPPLIER_DELIVERY | /receiving/workspace |
| Completion · Documentação fiscal | RequestPoGroup | GroupCompletionProjection (endpoint-validate) | groups | WAITING_FISCAL_RECEIPT / RECONCILIATION | /requests |
| Completion · Concluído | RequestPoGroup / request | completion svc | groups + req | COMPLETED | /requests |

**Exclusion & overlap.** Cancelled groups are excluded everywhere. A request may appear
in Finance *and* Recebimento if one group is scheduled while another is being received —
this overlap is the point; the header always states the count unit so totals are never
expected to sum to a single request population.

---

## 8. Counting Units — explicit everywhere

Ambiguity between "requests" and "groups/items/batches" is the fastest way to lose trust.
Every number states its unit; where two units matter, both are shown.

| Domain | Primary unit | Secondary | Label pattern |
|---|---|---|---|
| Buyer coverage | items | request summary | `32 itens · 8 pedidos` |
| Approvals (Área + Final) | ApprovalBatches | distinct requests | `16 lotes · 14 pedidos` |
| Adjustment | AdjustmentCycles | distinct requests | `3 ciclos · 3 pedidos` |
| Finance | RequestPoGroups | distinct requests | `113 grupos · 105 pedidos` |
| Receiving | RequestPoGroups | distinct requests | `70 grupos · 70 pedidos` |

The primary unit is the large number; the secondary is a muted suffix. Cards never mix
units silently, and pipeline stage totals are labelled by unit so a reader never sums
batches and requests by accident. Final Approval (PD-01) uses ApprovalBatch primary,
distinct requests secondary.

---

## 9. Role-Aware Behavior

Section visibility derives from *roles held + scope + ownership*, not a hard persona — a
user with several roles sees several sections. Managerial visibility is always labelled
Gerencial and never rendered as personal work.

| Role | Minha Operação | Shared queues | Buyer workload | Pipeline / Financial |
|---|---|---|---|---|
| System Administrator | own only | all (Gerencial) | full | global |
| Local Manager | own only | all in scope (Gerencial); Finance/Receiving as aggregate cards, no action buttons (PD-04) | full (scope) | scope |
| Buyer | own buyer/adjustment work | Compras | own row + UNASSIGNED | scope |
| Area Approver | batches assigned to their area | Aprovações (scoped) | — | scope |
| Final Approver | — (Final is shared, PD-01) | Aprovações · Final (shared) | — | scope |
| Finance | usually empty → points to shared | Finance | — | scope |
| Receiving | usually empty → points to shared | Recebimento | — | scope |
| Requester | own drafts / adjustment responses | — | — | own requests |

SysAdmin / Local Manager may see global managerial information, but it MUST NOT be
presented as personal work, and (PD-04) a Local Manager's Finance/Receiving aggregate
cards never expose operational action buttons. A user can hold many roles; visibility is
by permissions + scope + ownership, not a hard-coded persona.

---

## 10. Date & Urgency Model — per domain

One date does not fit all stages. Each domain names its own date source, and a stage
without a real due concept is never called "overdue". Thresholds are the PD-02/PD-03/PD-07
approved baseline.

| Domain | Date source | Attention | Critical / Overdue | Notes |
|---|---|---|---|---|
| Buyer | `NeedByDateUtc` | 1–2 days remaining | today = crítico; `< today` = vencido (PD-03) | only while a real Buyer action is available |
| Finance | `RequestPayments.ScheduledDateUtc` | scheduled today/tomorrow | passed **and** a Finance action still open (PD-02) | never NeedByDate for finance urgency |
| Approval / Adjustment / P.O. | stage-entry (history, best effort) | > 3 days in stage | > 7 days in stage (PD-02/PD-07) | operational aging, not a contractual SLA |
| Receiving | status-entry (history) only | > 7 days in stage | > 14 days in stage (PD-02) | "há X dias nesta etapa"; not overdue from aging |

**General rule (PD-02):** an alert/urgency flag must refer to an **OPEN operational
obligation**. A historical date being past is not, by itself, enough to raise an active
alert. Windows: hoje/amanhã = date ∈ [today, today+2). One date is never reused globally.

---

## 11. Alerts V2

Every alert names its source entity, owner type and severity, and is de-duplicated per
underlying obligation. **An alert must refer to an OPEN operational obligation (PD-02);
a past date alone never generates one.** Thresholds are the PD-02/PD-03/PD-07 baseline.

| Alert | Source | Owner | Severity | Rule |
|---|---|---|---|---|
| Cotação acionável vencida | Buyer projection + NeedByDate | Buyer | crítico | NeedByDate < today & buyer action open (PD-03) |
| Cotação acionável hoje | Buyer projection | Buyer | crítico | NeedByDate = today (PD-03) |
| Cotação acionável próxima | Buyer projection | Buyer | atenção | 1–2 days remaining (PD-03) |
| Reajuste aguardando comprador | cycle WAITING_BUYER | Buyer | atenção/crítico | > 3 / > 7 days (PD-02) |
| Não atribuído a envelhecer | unassigned pool age | Compras (shared) | atenção/crítico | > 3 / > 7 days (PD-02) |
| Lote em aprovação há muito tempo | batch stage age | Approver (shared, PD-01) | atenção/crítico | > 3 / > 7 days (PD-02) |
| Pagamento agendado vencido | ScheduledDateUtc < today & action open | Finance (shared) | crítico | PD-02 |
| Pagamento agendado hoje/amanhã | ScheduledDateUtc ∈ [today, today+2) | Finance (shared) | atenção | PD-02 |
| Correção de P.O. requerida | group WAITING_PO_CORRECTION | Buyer | atenção/crítico | > 3 / > 7 days (PD-07) |
| Aguardando P.O. há muito | group WAITING_PO age | Buyer/Compras | atenção/crítico | > 3 / > 7 days; wording "Aguardando P.O. há X dias" (PD-07) |
| Grupo em recebimento há muito | status-entry age | Receiving (shared) | atenção/crítico | > 7 / > 14 days; "há X dias nesta etapa" (PD-02) |
| Moeda nula com valor | data-quality scan | Admin | dados | review |

**Retired.** The old OVERDUE alert (any non-terminal request past `NeedByDate`) is
removed — it produced the 140 stale alerts. Overdue now fires only from a
domain-appropriate date **and** an open obligation, so a PAYMENT_COMPLETED request never
appears as "late procurement". "P.O. atrasada" wording is prohibited until a formal SLA
exists (PD-07).

---

## 12. Bottlenecks V2 — stage aging

A real bottleneck view needs stage dwell, not request age. Model it now, but only surface
aging where a defensible stage-entry timestamp already exists (PD-06).

- **Target metrics (per stage):** open entities · over-threshold entities · median dwell ·
  average dwell · oldest dwell — all on the stage's own count unit (§8), with PD-02
  thresholds.
- **Data prerequisite (PD-06, DECIDED):** a reliable persisted stage-entry source is
  eventually required. Its conceptual shape: EntityType, EntityId, Stage, EnteredAt,
  ExitedAt, transition/event source. Until it exists, aging is best-effort and labelled.

| Stage | Defensible aging today? | Basis |
|---|---|---|
| Finance scheduled-overdue | yes | ScheduledDateUtc (persisted, exact) |
| Adjustment waiting | yes | cycle RequestedAtUtc |
| Receiving / PO / approval dwell | best-effort | status-history entry, labelled "há X dias nesta etapa" |
| Group-level dwell (per obligation) | no | needs the PD-06 persisted transition source — deferred (not in B1+B2) |

**Do not fabricate precision (PD-06).** Group-level dwell shows open-count and
over-threshold-by-history only, with an explicit "aproximado" label, until the persisted
transition source lands (Phase B9). Best-effort history is never presented as a formal SLA.

---

## 13. Financial Summary V2 — currency-safe

Never numerically aggregate different currencies. Every total is partitioned by currency
with an explicit UNKNOWN/NULL bucket, and each stage uses its own authoritative amount.

| Summary | Source entity | Amount | Currency | Unit | Cancelled/completed |
|---|---|---|---|---|---|
| Em Aprovação | ApprovalBatch candidate snapshot | approved candidate total | candidate currency | batch → req | exclude cancelled batches |
| Aguardando P.O. | RequestPoGroup | group TotalAmount | group CurrencyCode | group | exclude cancelled groups |
| Finance acionável | RequestPoGroup (eligible) | group TotalAmount | group currency | group | actionable only |
| Pagamento agendado | RequestPayment | PlannedAmount | payment CurrencyCode | payment/group | unpaid scheduled |
| Pago (histórico) | RequestPayment | ActualPaidAmount | payment currency | payment/group | paid — secondary summary (PD-05) |

Each card shows one row per currency; a NULL/UNKNOWN currency is shown, never folded in.
**No silent fallback:** `EstimatedTotalAmount` is never a universal fallback. If a stage
lacks an authoritative amount, show count only or an explicitly-labelled "estimativa".

**Paid history placement (PD-05):** "Pago / Finalizado" is a **secondary managerial
historical summary**, not a primary operational queue — payments completed in a
selected/default period (default last 30 days), partitioned by currency. Full detail lives
in Finance / drill-down. Primary dashboard emphasis stays on pending obligations, available
actions, deadlines, bottlenecks and blockers.

---

## 14. Drill-Down Contract

Every card resolves to an existing operational screen with a server-supported filter that
reproduces the count exactly. No dashboard-only interpretation the destination cannot
reproduce.

| Card / KPI | Destination | Filter |
|---|---|---|
| Personal buyer action | Buyer Workspace | buyer = me + actionable |
| Unassigned buyer | /buyer/items | BuyerId = null |
| Buyer workload row | /buyer/items | buyer = row |
| Finance shared queue | /finance/payments | filter=action / scheduled |
| Receiving shared queue | /receiving/workspace | receiving-actionable statuses |
| Approvals (Área / Final) | /approvals | batch stage |
| Adjustment | /approvals | open cycle |
| P.O. | /requests or /buyer | group WAITING_PO |
| Alert row | the owning screen | the alert's own predicate |
| Financial card | /requests or /finance | stage + currency |
| Paid history summary | /finance | period + currency |
| Bottleneck row | owning screen | stage filter |

---

## 15. Backend Architecture

One dashboard service composes existing domain projections. The current bug is duplicated
status logic inside the controller; the cure is reuse, not a second copy.

| Component | Responsibility | Reuses | Reusable elsewhere? |
|---|---|---|---|
| `DashboardQueryService` | compose + scope + assemble DTO | all below | no (orchestrator) |
| PersonalActionProjection | truly-owned actions for a user (excludes Final, PD-01) | Buyer, adjustment, area assignment | yes — notifications |
| BuyerWorkloadProjection | per-buyer + UNASSIGNED rollup | `BuyerQueueProjectionBuilder` | yes — Buyer list §6 |
| SharedQueueProjection | Finance/Receiving/Compras/Final depth | eligibility svc · PoGroup rules · batch state | yes — those screens' badges |
| OperationalPipelineProjection | domain-stage counts (multi-stage) | batch · PoGroup · coverage | partly |
| FinancialSummaryProjection | currency-partitioned totals + paid history (PD-05) | PoGroup · RequestPayment | yes — finance summary |
| AlertProjection | canonical alerts + dedupe + open-obligation gate (PD-02) | all above | yes — notification centre |

**Guardrail:** no projection re-implements a status predicate that already lives in a
domain service. `BuyerQueueProjectionBuilder` and `FinancePaymentEligibilityService` are
the single sources; the dashboard calls them.

---

## 16. UserAction Projection — recommendation

**Recommendation: (B) runtime projection.** Model a canonical `UserAction` shape computed
on read from the existing projections — do **not** persist it.

Shape (runtime): ActionKey, ActionType, Module, EntityType, EntityId, RequestId,
OwnerType (Personal/Shared), OwnerUserId, Scope, AvailableNow, Priority, RelevantDate,
Route, Label. Final Approval actions carry OwnerType = Shared (PD-01).

**Why not persisted (A):** actionability is a pure function of live state that already
changes through the domain services. A persisted table would need invalidation on every
workflow write — a new drift surface, exactly the class of bug this redesign removes.
**(C) "unnecessary abstraction" is rejected too:** the shape is the shared contract that
keeps personal / shared / alerts consistent. (Note: this runtime `UserAction` is distinct
from the PD-06 persisted *stage-transition* source, which is a separate future concern.)

---

## 17. DTO Contracts

Server calculates; frontend renders. Every value below is final for display — no business
logic on the client.

- **`DashboardV2SummaryDto`** — personal, sharedQueues[], buyerWorkload[], pipeline[],
  alerts[], financial[], paidHistory[], generatedAtUtc, scopeLabel, visibleSections[]
- **`PersonalWorkDto`** — actions, overdue, critical, upcoming, isEmpty, emptyReason
- **`SharedQueueSummaryDto`** — module, label, primaryCount, primaryUnit, secondaryCount,
  secondaryUnit, breakdown[], route, ownerType=Shared
- **`BuyerWorkloadRowDto`** — buyerId, buyerName, isUnassigned, assigned, actionable,
  pendingItems, readyItems, needsQuotation, partialCoverage, readyForApproval, adjustment,
  overdueActionable, criticalActionable, route
- **`OperationalPipelineStageDto`** — domain, stage, label, count, unit, requestCount,
  route, canOverlap=true
- **`DashboardAlertDto`** — alertKey, type, message, entityType, entityId, requestId,
  ownerType, severity, isActionable, relevantDateUtc, route (severity from PD-02/PD-03/PD-07)
- **`FinancialCurrencySummaryDto`** — stageLabel, currencyCode (nullable→"UNKNOWN"),
  amount, count, unit, isAuthoritative, isPaidHistory, periodLabel (PD-05)

---

## 18. API Design

**Recommendation: (C) hybrid.** A summary endpoint for the cheap sections, plus lazy
endpoints for the expensive buyer/pipeline projections, so first paint is fast and heavy
work loads on demand.

| Endpoint | Responsibility | Cost |
|---|---|---|
| `GET /api/dashboard/v2/summary` | personal + shared depth + financial + paid history + alerts | moderate |
| `GET /api/dashboard/v2/buyer-workload` | per-buyer table (manager) | expensive — lazy |
| `GET /api/dashboard/v2/pipeline` | domain-stage counts | expensive — lazy |

Rationale: permission-based sections load only when visible; the expensive
`BuyerQueueProjectionBuilder` sweep is isolated so it never blocks first paint; each
endpoint is independently cacheable and refreshable. Avoid one monolithic `/v2` that
always pays for the heaviest projection.

---

## 19. Performance

| Hot path | Risk | Mitigation |
|---|---|---|
| BuyerQueueProjectionBuilder over all active requests | N+1 / full sweep each load | single batched query; bound to non-terminal; lazy endpoint; short cache (30–60s) |
| Per-group Finance eligibility | row-by-row eval | SQL-side pre-aggregate group status counts; evaluate in-memory on the bounded set |
| Item-level coverage | heavy joins | reuse the coverage query shape validated in blast-radius; project only counts for summary |
| Approval-batch aggregation (Área + Final) | joins | grouped counts, not entity hydration |
| Repeated refresh | recompute storms | cache summary only where semantics permit; never cache personal actionability beyond seconds |

Pagination applies to detail screens, never to summary counts. Indexes worth investigating
(no migration yet): `RequestPoGroups(Status, RequestId)`,
`RequestPayments(RequestPoGroupId, ScheduledDateUtc)`, `ApprovalBatches(RequestId, Status)`,
`Requests(StatusId, BuyerId)`.

---

## 20. Scope & Permissions

Every section runs through `RequestAccessScope`: SysAdmin bypasses; others filter by
plant/dept; **no scope rows currently means unfiltered**.

**Flag for authorization review (do not change here).** "No scope = unfiltered" is
acceptable for the legacy dashboard but becomes riskier when V2 surfaces richer per-buyer
and financial data, and when PD-04 grants Local Managers aggregate Finance/Receiving
cards. Recommend a separate authorization decision on whether a scope-less non-admin
should default to *unfiltered* or *empty*. Cockpit V2 must not silently widen exposure;
PD-04 aggregate cards remain view-only (no action buttons).

---

## 21. UX Layout

Modern corporate, calm, scannable — not a wall of giant KPI cards. Three planes are
visually distinct via the ownership pill, never via loud color washes.

1. **Top summary strip** — slim band: my open actions · my overdue · (role-conditional)
   my shared-queue depth. Compact, no hero.
2. **Minha Operação** (`PESSOAL`) — small action list; honest empty state; no Final work.
3. **Filas operacionais** (`COMPARTILHADO`) — role-conditional queue cards (Compras,
   Finance, Recebimento, Aprovações·Final) with count-unit chips.
4. **Gestão da equipe** (`GERENCIAL`) — compact buyer table, UNASSIGNED pinned.
5. **Pipeline** — grouped by domain, horizontal-scroll container, count-unit labelled.
6. **Alertas & gargalos** — severity-striped rows, deduped, open-obligation only.
7. **Resumo financeiro** — currency-partitioned cards; paid history as a secondary
   period summary (PD-05).

Affordance: chips/badges over big cards; interactive rows look interactive; visible
keyboard focus. Color: one accent; semantic good/warn/crit reserved for state; category
hues only for the three planes. Responsive: tables scroll inside their own container; body
never scrolls sideways. Dark mode: token-based; both themes carried; explicit backgrounds.
The page clearly labels **Pessoal**, **Compartilhado**, **Gerencial**.

---

## 22. Legacy Migration Strategy

1. **Coexist** behind a feature flag in DEV/TEST — V2 endpoints live beside
   `cockpit-summary`; both callable.
2. **Human acceptance** on TEST reconciling V2 numbers against the operational screens.
3. **Flip** Dashboard.tsx to V2 endpoints; keep `cockpit-summary` reachable one release
   for rollback.
4. **Deprecate** then remove `cockpit-summary`; retire the already-dead `dashboard-summary`
   immediately (no live caller).

Nothing is deleted in Phase B. Removal is a later, separate, authorized step.

---

## 23. Implementation Roadmap

| Phase | Backend | Frontend | Migration | Risk |
|---|---|---|---|---|
| B1 Foundation / DTOs | DashboardQueryService skeleton + DTOs | — | no | low |
| B2 Buyer workload + shared | BuyerWorkloadProjection | table + Buyer-list strip | no | low |
| B3 Finance shared queue | SharedQueueProjection (finance) | finance card | no | low |
| B4 Receiving shared queue | SharedQueueProjection (receiving) | receiving card | no | low |
| B5 Personal actions | PersonalActionProjection (excludes Final, PD-01) | Minha Operação | no | low — PD-01 resolved |
| B6 Operational pipeline | OperationalPipelineProjection (+ Aprovações·Final shared) | pipeline | no | med |
| B7 Financial summary | FinancialSummaryProjection + paid history (PD-05) | currency cards | no | med |
| B8 Alerts | AlertProjection + dedupe + open-obligation gate + PD-02/03/07 thresholds | alert list | no | med |
| B9 Stage-aging prereqs (PD-06) | persisted stage-transition source | — | **yes (later)** | high — deferred |
| B10 UI replacement | — | Dashboard.tsx → V2 | no | med |
| B11 Legacy cleanup | remove old endpoints | — | no | low |

Every phase carries backend projection unit tests + cross-screen reconciliation + human
browser acceptance as a release gate. Slices are individually releasable.

### FIRST SLICE — B1 + B2 (IMPLEMENTED)

**B1 + B2 — Canonical Dashboard foundation + Buyer workload / shared Compras queue.**
**Status: implemented in v2.237.0; DEV human acceptance PASS (2026-09-02).**

Reasons it was chosen first:
- no migration required (confirmed — none created)
- no unresolved product decision blocks it (all 7 are DECIDED, §27)
- immediate management value
- existing canonical Buyer projection (`BuyerQueueProjectionBuilder`)
- establishes the cross-screen reconciliation pattern (dashboard == Buyer Workspace)

As-built notes: the entity→projection-input mapping was extracted to a shared Domain factory
(`BuyerQueueProjectionInputFactory`) consumed by the Buyer queue, the Buyer Workspace and the
Dashboard service, so no surface duplicates coverage/state logic. Remaining B-phases (B3–B11) are
not started.

---

## 24. Acceptance Criteria

**Buyer:** UNASSIGNED never inflates a buyer's personal count; workload totals reconcile
with Buyer Workspace exactly; PartialCoverage matches the canonical projection; Overdue =
`NeedByDate < today`, Critical = `NeedByDate = today`, applied only while a buyer action is
open (PD-03).

**Finance:** actionable request/group counts equal the eligibility service;
PAYMENT_SCHEDULED remains actionable; shown as shared, never personal; urgency uses
`ScheduledDateUtc` with attention today/tomorrow and critical past+open (PD-02).

**Receiving:** counts equal canonical `RequestPoGroup` actionability; IN_FOLLOWUP and
WAITING_SUPPLIER_DELIVERY included; Requester alone never creates a receiving action;
aging worded "há X dias nesta etapa", never overdue from aging (PD-02).

**Final Approval (PD-01):** appears under COMPARTILHADO, never in Minha Operação; counted
as `ApprovalBatch` primary / distinct requests secondary; no `FinalApproverId` introduced.

**Financial:** no cross-currency SUM; NULL currency visibly separated; paid history is a
secondary period summary (default 30 days), not a primary queue (PD-05).

**Alerts:** every alert refers to an OPEN obligation; PAYMENT_COMPLETED never flagged
"late procurement"; PO aging worded "Aguardando P.O. há X dias", never "P.O. atrasada"
(PD-02/PD-07); one obligation → at most one alert.

**Manager visibility (PD-04):** a Local Manager sees Finance/Receiving aggregate cards
within scope with no action buttons; those cards sit in the GERENCIAL plane.

---

## 25. Test Strategy

**Automated:** projection unit tests per service; integration over the LocalDB sandbox;
**cross-screen reconciliation** (dashboard count == operational screen count); role/scope
matrix (SysAdmin vs scoped user; Local Manager PD-04 view-only cards); multi-group,
multi-batch, partial coverage, adjustment; currency + NULL-currency partition tests;
Final-Approval-is-shared test (never personal, PD-01); alert-requires-open-obligation test
(a past date with no open obligation raises nothing, PD-02); buyer threshold tests
(overdue/critical/attention per PD-03); PO aging wording test (PD-07); paid-history
placement test (secondary, period-scoped, PD-05); a request with simultaneous stages
appears in each stage correctly.

**Human:** browser acceptance remains a release gate every phase; a manager validates
buyer workload against the Buyer list; Finance/Receiving validate shared-queue depth
against their screens.

---

## 26. Risks & Non-Goals

**Risks:** Buyer projection cost on every load (mitigated by lazy endpoint + cache, §19);
reconciliation drift if a projection re-implements a predicate (forbidden by §15); scope
"no rows = unfiltered" over-exposure, heightened by PD-04 manager cards (§20 review);
over-claiming stage-aging precision (PD-06 guardrail, §12); users trained on the inflated
"180" perceiving V2 as "losing" work (needs change communication).

**Non-goals (explicitly out of scope):** redesigning Buyer/approval/Finance/Receiving
workflows; changing eligibility or transition rules; automatic buyer assignment;
introducing `FinalApproverId` or any individual assignment model (PD-01); employee
performance scoring; currency conversion / ERP concepts; automatic historical data repair;
implementing the PD-06 persisted stage-transition source inside the B1+B2 slice.

---

## 27. Product Decisions (RESOLVED)

**Baseline status: 7 / 7 DECIDED (approved 2026-09-02).** The seven decisions below were
previously open; all are now approved and are binding for the Dashboard V2 baseline. There
are **zero open product decisions remaining** from this set.

### PD-01 — Final Approver ownership semantics

**Status:** DECIDED

**Decision:** Final Approval is a **SHARED** queue based on role + scope (COMPARTILHADO plane).

**Rules:**
- Final Approval must NOT be presented as personally-owned work.
- Holding the Final Approver role does not make all eligible batches "my actions".
- Final Approval belongs under the COMPARTILHADO plane.
- Primary counting unit = `ApprovalBatch`; secondary = distinct Requests.
- Personal ownership may be reconsidered only if a future explicit individual-assignment
  model is introduced.
- Do **not** introduce `FinalApproverId` or any new assignment model as part of Dashboard V2.

### PD-02 — Alert / aging thresholds

**Status:** DECIDED

**Decision:** Approved initial thresholds, by domain. Every alert must refer to an **OPEN
operational obligation** — a past date alone never generates an active alert.

**Rules:**
- **Buyer** — Attention: `NeedByDateUtc` within the next 2 days; Critical: `NeedByDateUtc`
  already passed. Only while a real Buyer action is currently available.
- **Finance** — relevant date = `RequestPayments.ScheduledDateUtc`. Attention: scheduled
  for today or tomorrow; Critical: `ScheduledDateUtc` passed and a Finance action is still open.
- **Approval / Adjustment / P.O.** (until a formal SLA exists): Normal ≤ 3 days in stage;
  Attention > 3 days; Critical > 7 days. Described as operational aging, not a contractual SLA.
- **Receiving** — Normal ≤ 7 days in stage; Attention > 7 days; Critical > 14 days. Wording
  "há X dias nesta etapa"; never called overdue from aging alone.
- **General rule:** an alert must refer to an OPEN operational obligation; a historical date
  being past is not enough to generate an active alert.

### PD-03 — Buyer critical window

**Status:** DECIDED

**Decision:** Approved Buyer urgency semantics, applied ONLY while at least one real Buyer
action is available (`ADD_QUOTATION`, `SUBMIT_BATCH`, `RESOLVE_ADJUSTMENT`).

**Rules:**
- Normal: more than 2 days before `NeedByDateUtc`.
- Attention: 1–2 days remaining.
- Critical (today): `NeedByDateUtc` = today.
- Overdue: `NeedByDateUtc` < today.
- Do NOT keep labelling the Buyer overdue once the obligation has moved to another
  actor/domain (Approval, PO, Finance, Receiving).

### PD-04 — Local Manager visibility into Finance / Receiving managerial cards

**Status:** DECIDED

**Decision:** A Local Manager may see managerial aggregate cards for Finance and Receiving
within the user's effective scope, even without the operational Finance/Receiving role.

**Rules:**
- This visibility belongs to the `GERENCIAL` plane.
- It does NOT grant operational actions and must not expose action buttons that require the
  Finance/Receiving roles.
- It does NOT place those queues under PESSOAL or COMPARTILHADO for that manager.
- System Administrator follows the same conceptual separation while retaining the current
  admin scope-bypass behavior.

### PD-05 — Paid financial history placement

**Status:** DECIDED

**Decision:** Paid / Finalized financial data remains on the main Dashboard only as a
**secondary managerial historical summary**, never a primary operational queue.

**Rules:**
- Primary Dashboard emphasis stays on pending obligations, currently-available actions,
  deadlines, bottlenecks and blockers.
- Paid-history summary: payments completed in a selected/default period (default last 30
  days), amounts partitioned by currency.
- Full historical detail lives in Finance / drill-down.
- Never aggregate different currencies together.

### PD-06 — Reliable stage aging needs new persisted data

**Status:** DECIDED

**Decision:** Dashboard V2 will eventually require a reliable persisted source for entity
stage-entry timestamps. Until it exists, aging is best-effort and clearly labelled.

**Rules:**
- Use current reliable timestamps where available; use history-derived timestamps only when
  defensible; label them "há X dias nesta etapa".
- Never present best-effort history as a formal SLA; never fabricate precision.
- A future persisted stage-transition source should conceptually support: EntityType,
  EntityId, Stage, EnteredAt, ExitedAt, transition/event source.
- Do NOT implement this persistence in the current B1+B2 slice.

### PD-07 — Waiting-for-PO formal SLA

**Status:** DECIDED

**Decision:** There is **NO** formal PO SLA at this time. WAITING_PO remains a
Buyer / Compras-owned operational obligation, tracked by operational aging.

**Rules:**
- Normal ≤ 3 days waiting; Attention > 3 days; Critical > 7 days.
- Wording: "Aguardando P.O. há X dias".
- Do NOT use "P.O. atrasada" unless a formal SLA is later introduced.

---

## 28. Recommended First Implementation Slice

**B1 + B2 — Canonical Dashboard foundation + Buyer workload / shared Compras queue.**
Highest value, lowest risk, no migration, no open product decision (all 7 DECIDED).

- **Why first:** reuses `BuyerQueueProjectionBuilder` wholesale; delivers the
  originally-requested management panel; proves the reconciliation discipline
  (dashboard == Buyer Workspace) that every later phase depends on.
- **Ships:** `DashboardQueryService` + `BuyerWorkloadProjection` + DTOs;
  `GET /api/dashboard/v2/buyer-workload`; the buyer table (UNASSIGNED pinned) on the
  dashboard and the compact strip on the Buyer list.
- **Acceptance:** UNASSIGNED never folded into a buyer; every row reconciles exactly with
  the Buyer Workspace; PartialCoverage matches the canonical projection; buyer
  overdue/critical follow PD-03.
- **Not in this slice:** personal actions, pipeline, financial, alerts, PD-06 stage-aging
  persistence, legacy removal.

**Implementation status:** PRODUCT DECISIONS FOR DASHBOARD V2 BASELINE: **7 / 7 DECIDED**.
**B1+B2 is implemented and released as v2.237.0 (DEV human acceptance PASS, 2026-09-02);** no DB
migration was required. Later slices (B3 Finance, B4 Receiving, B5 personal actions, B6 pipeline,
B7 financial, B8 alerts, B9 stage-aging, B10 UI replacement, B11 legacy cleanup) remain not started,
and the legacy `cockpit-summary` dashboard remains in place until the V2 UI-replacement slice.

---

*End of specification. Documentation only — no application code, tests, database,
migration, version, or CHANGELOG changed. Reference: origin/main @
176543ed4f6337785db8c87cc48aebc820047d02 · v2.236.0.*
