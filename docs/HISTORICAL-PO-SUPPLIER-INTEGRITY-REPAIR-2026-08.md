# Historical PO / Supplier Data Integrity Repair Campaign

**ALPLA Angola Portal Gerencial**
**Closure date: 2026-08-21**

Status: **COMPLETE IN PROD**. All authorized, actionable historical repairs were executed and
postflight-verified. No application deployment was involved (PROD application remained on
v2.229.9 throughout; TEST on v2.229.12). All repairs were manual, operator-executed,
server-side `sqlcmd` runs of the guarded scripts inventoried in the appendix, each individually
authorized.

## 1. Background

The v2.229.12 investigation ("Primavera P.O Identification Hardening", see
[CHANGELOG.md](CHANGELOG.md)) exposed two classes of historical data defects created under
earlier application versions:

1. **Supplier-less legacy PO groups** — `RequestPoGroups` rows with `SupplierId NULL` and the
   legacy placeholder snapshot `'Fornecedor não definido'`/NULL, blocking the Buyer flow.
2. **Wrong PO identities** — values registered as `PurchaseOrderNumber` that are not Primavera
   PO references: supplier fiscal numbers (NIF-as-PO), external-document numbers
   (N.º Doc. Externo, `FT`/`FP`-style), and family-dropped references (`2026A/11`).

Every supplier identity and every corrected PO number in this campaign was confirmed by
**human review of the stored source documents** (PO PDFs and proformas). Filenames and OCR
output were never accepted as supplier-identity evidence.

## 2. PROD repairs completed — final wave (2026-08-21)

### A. Simple supplier-only repairs

| Request | Supplier written | NIF | Statuses preserved | PO | Audit tag |
|---|---|---|---|---|---|
| REQ-29/07/2026-178 | 66 — IMPORAFRICA VEICULOS LDA | 5417231983 | APPROVED / WAITING_PO | NULL (preserved) | `[HIST-SUPPLIER-REQ-178]` |
| REQ-12/08/2026-245 | 159 — MUSOLAND-MUNDO DAS SOLUCOES-ACESS.CONS.(SU),LDA | 5417386740 | APPROVED / WAITING_PO | NULL (preserved) | `[HIST-SUPPLIER-REQ-245]` |

Only `RequestPoGroups.SupplierId` + supplier snapshots (restored from the supplier master) and
bookkeeping fields were written, plus one tagged audit row each. MUSOLAND (159) was
`RegistrationStatus = DRAFT` at repair time — recorded as a non-blocking operational warning:
REGISTER_PO stays blocked for that group until the master registration completes.

### B. FIDELIDADE supplier + PO repairs (two-row atomic package)

| Request | Supplier written | Old stored PO | Corrected PO | Statuses preserved | Audit tag |
|---|---|---|---|---|---|
| REQ-31/07/2026-193 | 45 — FIDELIDADE ANGOLA-COMP. DE SEGUROS (NIF 5417061590) | `FT 26/72087` | `ECF11 2026/420` | ADVANCE_PAYMENT_REQUIRED / ADVANCE_PAYMENT_REQUIRED | `[HIST-SUPPLIER-PO-REQ-193]` |
| REQ-31/07/2026-194 | 45 — FIDELIDADE ANGOLA-COMP. DE SEGUROS (NIF 5417061590) | `FT 73094` | `ECF11 2026/38` | ADVANCE_PAYMENT_REQUIRED / ADVANCE_PAYMENT_REQUIRED | `[HIST-SUPPLIER-PO-REQ-194]` |

Human review of both PO PDFs confirmed N.º Contrib. 5417061590 and headings
"PO Serviços ECF11 2026/420" / "PO Serviços ECF11 2026/38". The old stored values
`FT 26/72087` and `FT 73094` are each document's **N.º Doc. Externo** — valid source
information, but not the PO identity.

### C. HENDA supplier + PO repair

| Request | Supplier written | Old stored PO | Corrected PO | Statuses preserved | Audit tag |
|---|---|---|---|---|---|
| REQ-31/07/2026-200 | 157 — HENDA HOTELARIA , LDA - HCTA (NIF 5001094645) | `FT 453` | `ECF11 2026/424` | PO_ISSUED / PO_ISSUED | `[HIST-SUPPLIER-PO-REQ-200]` |

Heading "PO Serviços ECF11 2026/424", N.º Contrib. 5001094645; `FT 453` confirmed as the
N.º Doc. Externo. HENDA (157) was `RegistrationStatus = DRAFT` at repair time (non-blocking;
the PO was already registered, so no immediate workflow impact).

### D. Historical supplier + PO backfill

| Request | Supplier written | Previous state | Backfilled PO | Audit tag |
|---|---|---|---|---|
| REQ-15/07/2026-071 | 257 — Embrace Angola - Prestação de Serviços, LDA (NIF 5417101524) | SupplierId NULL, snapshots all NULL, PO NULL | `ECF11 2026/371` | `[HIST-SUPPLIER-PO-REQ-071]` |

The PO had been registered on 2026-07-16 under the pre-group-model flow, before the current
`RequestPoGroups` row existed (created 2026-07-20), so the group carried neither supplier nor
PO. The document's N.º Doc. Externo is `FT FC202602/2101254` (not the PO). RequestStatus
ADVANCE_PAYMENT_REQUIRED preserved; **GroupStatus deliberately preserved as PENDING** — the
RequestStatus/GroupStatus mismatch was NOT repaired and remains a separate historical
reconciliation concern, explicitly out of this campaign's scope.

### E. BISMARK PO-only repair

| Request | Supplier | Old stored PO | Corrected PO | Statuses preserved | Audit tag |
|---|---|---|---|---|---|
| REQ-23/07/2026-146 | 14 — BISMARK PAPELARIA (NIF 5417371270) — already correct, untouched | `2026A/11` | `ECF10 2026A/11` | PAYMENT_SCHEDULED / PAYMENT_SCHEDULED | `[HIST-PO-REQ-146]` |

Heading "Encomenda Mat Escritório/Diversos ECF10 2026A/11"; the old value was the same
reference with the ECF10 family dropped; `FP - 63` is the N.º Doc. Externo. Only the PO field
was written. This case surfaced the `2026A` letter-suffixed year series that the current
parser grammar does not recognize — see
[BACKLOG-PRIMAVERA-PO-PARSER.md](BACKLOG-PRIMAVERA-PO-PARSER.md).

## 3. Completed earlier in the same campaign

Supplier-only (Population A1, ADVANCE_PAYMENT_REQUIRED with active finance flow):

- REQ-09/07/2026-031 → SupplierId 254 (RBC)
- REQ-14/07/2026-067 → SupplierId 102 (Gasp)

Historical PO-number corrections (document-confirmed, 3 and 2 independent positive parses of
the stored PDFs respectively):

- REQ-098 → PO corrected to `ECF10 2026/230` (`[PO-REPAIR-REQ-098]`)
- REQ-101 → PO corrected to `ECF11 2026/386` (`[PO-REPAIR-REQ-101]`)

Population-B supplier repairs (six NIF-exact document matches + two human-confirmed):

- REQ-208, REQ-215, REQ-222 → TDA (SupplierId 34, NIF 5410002857) — `[POP-B-SUPPLIER-REQ-***]`
- REQ-237, REQ-238, REQ-241 → Embrace (SupplierId 257, NIF 5417101524) — `[POP-B-SUPPLIER-REQ-***]`
- REQ-230 → TDA (one-row split package) — `[POP-B-SUPPLIER-REQ-230]`
- REQ-233 → TDA **+ PO corrected to `ECF11 2026/423`** — `[POP-B-SUPPLIER-PO-REQ-233]`.
  The old stored value `FT 00459` was confirmed by human review of the actual PO PDF
  (heading "PO Serviços ECF11 2026/423", N.º Contrib. 5410002857) to be the document's
  **N.º Doc. Externo**, not the PO identity.

## 4. Intentionally NOT repaired

### REQ-16/07/2026-084 — HISTORICAL_INERT_NO_REPAIR_RECOMMENDED

Live PROD drift detected during the final preflight: RequestStatus = **CANCELLED** (group
still WAITING_PO, supplier NULL, PO NULL). The full-predicate classification returned
MANUAL_REVIEW_REQUIRED exactly as designed, and the decision was to leave the request
**intentionally untouched**: no supplier repair, no group-status reconciliation, no historical
field modified. It was removed permanently from the repair packages.

### Population A2 (earlier decision)

REQ-003, REQ-004, REQ-005, REQ-006, REQ-009, REQ-015, REQ-021, REQ-022, REQ-023.

These were intentionally not repaired because they are historical/inert/completed-follow-up
cases; there was no operational benefit sufficient to justify historical mutation.

## 5. Safety / execution method

Controls applied uniformly across the campaign:

- **Read-only live PROD preflight before every repair** (the analysis clone was treated as
  stale by definition; only live preflight results gated execution).
- **Full-predicate state classification**: `PENDING_REPAIR` only when every reviewed predicate
  matched; `ALREADY_REPAIRED` only on the exact repaired state; anything else
  `MANUAL_REVIEW_REQUIRED` with no write.
- Exact pins per target: RequestNumber, GroupId, company, total; supplier Id + NIF;
  attachment Id + SHA-256 (source documents always; live PO attachments pinned via preflight
  output + operator-supplied `-v poHash` where the registration post-dated the analysis data).
- **Same-company canonical PO collision checks** before every PO write (suffix-anchored
  canonical comparison; over-matching aborts, never repairs).
- **No write on drift**; all-or-nothing transactions with `SET XACT_ABORT ON` and exact
  `@@ROWCOUNT` assertions on both the UPDATE and the audit INSERT.
- Explicit actor GUID (`-v actor`, validated against `Users`); environment allow-list
  restricted to `Portal-Gerencial-Test` / `Portal-Gerencial` with no bypass and a
  server/database/login/context banner.
- **Tagged `DATA_INTEGRITY_REPAIR` audit rows** in `RequestStatusHistories` with
  `PreviousStatusId = NewStatusId` (integrity repairs never move workflow state).
- Status/workflow preservation: request status, group status, finance/advance-payment state,
  totals, attachments, approvals and the supplier master were never modified by any repair.
- `sqlcmd -I` where required (`QUOTED_IDENTIFIER ON` requirement of the scripts).
- **Guarded rollbacks** available for every package (exact-state precondition, tag-scoped
  audit deletion, idempotent).
- **Postflight required after every repair**, expecting `ALREADY_REPAIRED`.

### Drift evidence — the preflight controls worked

Live PROD drift was detected several times during the campaign, and in each case the guard
design prevented a stale package from executing:

- **REQ-200** drifted from a simple supplier-only case to **PO_ISSUED** with `FT 453`
  registered as the PO → removed from the simple batch, repaired by a dedicated
  state-aware package.
- **REQ-193 / REQ-194** drifted to **ADVANCE_PAYMENT_REQUIRED** with `FT`-style PO values
  registered → removed from the simple batch, repaired by a dedicated two-row package.
- **REQ-084** drifted to **CANCELLED** → classified MANUAL_REVIEW_REQUIRED by the preflight
  and permanently excluded (no repair).

(An earlier instance of the same class: **REQ-233** drifted to PO_ISSUED with `FT 00459`
before its original supplier-only package could run — that package was split and superseded by
the dedicated supplier+PO repair.)

## 6. Final result

- All authorized, actionable historical repairs are **complete in PROD**.
- Every completed repair was **postflight-verified as ALREADY_REPAIRED**.
- Workflow/status fields were preserved everywhere; the only known intentional exception is
  REQ-071's pre-existing GroupStatus=PENDING mismatch, which was explicitly out of scope and
  remains a separate reconciliation concern.
- Historical inert cases (REQ-084, Population A2) remain intentionally untouched.
- **No further historical DB mutation is currently recommended.**

Open items outside this campaign's scope: the Primavera `2026A` parser gap
([BACKLOG-PRIMAVERA-PO-PARSER.md](BACKLOG-PRIMAVERA-PO-PARSER.md)); completion of the DRAFT
supplier registrations (45 FIDELIDADE, 157 HENDA, 159 MUSOLAND) so REGISTER_PO can proceed
where still pending; REQ-071's group-status reconciliation decision.

## 7. Appendix — final repair script inventory (`scripts/db/`)

Retained as operational/audit evidence — none deleted. Each package is a
preflight (read-only) / repair / rollback trio with identical environment guards.

Final wave (executed 2026-08-21):

| Package | Scripts | Scope |
|---|---|---|
| A | `po-flow-final-two-supplier-repair-*` | REQ-178 + REQ-245 supplier-only |
| B | `po-flow-req071-supplier-po-*` | REQ-071 supplier + PO backfill |
| C | `po-flow-req146-po-number-repair-*` | REQ-146 PO-only |
| D | `po-flow-req200-supplier-po-*` | REQ-200 supplier + PO |
| E | `po-flow-req193-194-supplier-po-*` | REQ-193 + REQ-194 supplier + PO (two-row atomic) |

Earlier completed packages (same campaign): `po-flow-a1-supplier-repair.sql` (A1:
REQ-031/067), `po-flow-po-number-repair-*` (REQ-098/101),
`po-flow-population-b-supplier-repair-*` (208/215/222/237/238/241),
`po-flow-population-b-tda-230-*` (REQ-230 one-row split),
`po-flow-req233-supplier-po-*` (REQ-233). Read-only analysis tooling:
`po-flow-supplier-backfill-dryrun-readonly.sql`, `po-flow-po-number-audit-dryrun-readonly.sql`,
`po-flow-evidence-attachments-readonly.sql`, `po-flow-evidence-scan.ps1`,
`po-flow-test-smoke-probes.ps1`.

Superseded intermediate batch trios (six-/five-/three-row simple batches and the original
230/233 pair) were retired from the working tree as live drift rescoped the targets; their
full content remains in git history.
