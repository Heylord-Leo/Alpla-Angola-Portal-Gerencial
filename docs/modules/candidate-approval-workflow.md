# Candidate-Based Quotation Approval Workflow (v2.227.0)

> Canonical reference for the QUOTATION approval responsibility model introduced in v2.227.0.
> **Buyer submits alternatives → Area Approver selects the winner → Final Approver reviews the
> outcome.** The candidate-based ApprovalBatch is the canonical flow for every new QUOTATION
> request; the legacy paths described at the end exist for historical compatibility only.

## Roles at a glance

| Stage | Actor | Authority |
|---|---|---|
| Batch submission | Buyer | Composes the batch and the candidate OPTIONS per item — no winner authority |
| Area approval | Area Approver | Selects exactly ONE winner per item; approves/rejects/returns the batch |
| Final approval | Final Approver | Reviews the Area-selected outcome read-only; approves/rejects/returns |

## Buyer

- Selects which requested items enter the batch ("Incluir no lote" — partial batches preserved)
  and, per included item, checks **one or many candidate options** (checkbox semantics, at least
  one required, no arbitrary maximum). Default: all eligible options prechecked, visibly editable.
- **No winner authority**: the create/update contract (`Items[{requestLineItemId, candidates[]}]`)
  structurally carries no winner field and no commercial values — the backend freezes an
  `ApprovalBatchItemCandidate` snapshot (supplier + NIF, description, quantity/unit, unit price,
  discount, IVA, gross, line total, currency, document number/date, reconciliation context) from
  server-side truth at submission time.
- **BuyerNote** ("Observação do Comprador") is optional, per candidate, frozen with the snapshot,
  and strictly informational — it never implies preference, winner semantics, or authorization.
- Genuine EXTRA_ITEM lines of contributing quotations are decided INCLUDE/EXCLUDE at
  composition time; an included extra becomes a normal single-candidate batch item that the Area
  stage also confirms.
- The Buyer modal summary shows counts, the single-currency min/max combination range, and
  always "Total aprovado: A definir pelo Aprovador de Área" — never a lot total.
- Rework (returned batch): the Buyer edits the **candidate set** — add/remove options, edit
  BuyerNotes, keep/drop items — never winners. Retained candidates keep their frozen snapshot;
  re-added or new candidates are snapshotted again at save time.

## Area Approver

- The wizard step "Seleção do Vencedor" shows every frozen candidate snapshot per item as radio
  comparison cards (values = the ones submitted for approval, immune to later live-quotation
  edits) with MENOR VALOR / tie / substitute / quantity-divergence badges, BuyerNotes and
  reconciliation warnings.
- **Exactly one winner per candidate-based item; explicit selection always** — even a single
  option ("Única opção enviada") requires the click; nothing auto-selects, including the cheapest.
- **All-or-return**: approval is possible only when every item is decided; otherwise the wizard
  blocks with the pending count and offers the existing return path.
- **Non-cheapest justification**: selecting above the lowest same-currency snapshot beyond the
  FinancialIntegrity tolerance (max(1,00; 0,1%)) requires a meaningful justification (≥20
  significant characters, same validator as the backend). Cheapest/tied picks may carry an
  optional justification; supplied text is always persisted and audited.
- **Tentative totals & budget preview**: the wizard shows live per-currency selected totals
  ("Total parcial das seleções" → "Total da combinação selecionada") and previews the budget
  impact of the tentative selection (identity-only payload; the server values it from frozen
  snapshots; partial previews allowed; nothing persists before the approval submit).
- Winner selection is **local wizard state** until submit. On approve, one atomic backend
  transaction validates the selections, stamps `SelectedCandidateId` + `SelectedQuotationItemId`
  + `WinnerSelectedByUserId/AtUtc` + `WinnerSelectionJustification`, writes the line-level
  compatibility pointers and award history, builds the PENDING PO groups from the winners, and
  advances the batch — a failure persists nothing.
- **Snapshots are authoritative** everywhere: approval validation, budget preview, group values
  and audit all read the frozen candidate rows, never live `QuotationItem` values.

## Final Approver

- Reviews the **read-only commercial outcome**: per item, the winner card "Vencedor selecionado
  pelo Aprovador de Área" with decision metadata (selected by/at) and the stored justification —
  prominent ("Justificativa da escolha (acima do menor valor)") when the pick was non-cheapest,
  as an optional decision note otherwise.
- Losing candidates are available behind **"Ver outras opções (N)"** — expandable, read-only,
  same comparable frozen fields; no expander appears for single-option items.
- **Cannot mutate the winner**: no selection control exists at the Final stage and the backend
  ignores any selection payload on final endpoints (pinned by test). If the outcome must change,
  the Final Approver returns the batch ("Solicitar Reajuste") — the dialog states that the
  winner selection will have to be redone; Phase-A semantics then clear the decision fields and
  delete the PENDING groups while every audit event survives.
- Final approval snapshots `ApprovedTotalAmount` from the (snapshot-valued) PO groups, exactly
  as before.

## Group building

- `BuildGroupsForBatchAsync` runs inside area approval and consumes **only the Area-selected
  winners** — losing candidates never create groups, totals, or operation-invoice expected values.
- Group commercial values (supplier, NIF, currency, `TotalAmount`) come from the **winning
  candidate snapshots**; legacy items (below) keep the live-value path they always had.
- Grouping key (Supplier + Currency + payment condition) and Post-Payment document
  classification/obligation stamping are unchanged from previous releases.

## Amount display rule

Before the Area decision a candidate batch has **no commercial truth**: every surface (Approval
Center cards, Area drawer header, Buyer summary) shows **"A definir pelo Aprovador de Área"** —
never 0, a partial sum, or the request estimate. After the Area decision, surfaces show the
frozen selected-combination total (per currency, never summed across currencies); after Final
approval, the persisted `ApprovedTotalAmount` applies.

## Audit trail

`BATCH_CANDIDATES_SUBMITTED` (per item: which options the Buyer sent) → `QUOTATION_ITEM_AWARDED`
(the AREA decision: actor, supplier, frozen total, justification) → the existing batch
approval/rejection/adjustment and PO-group events. Returns revoke the active decision but never
rewrite history.

## Legacy compatibility

- **Historical batches** (created before v2.227.0): zero candidate rows, populated
  buyer-selected winner. They read, approve and rework with their original semantics, flagged
  `IsLegacyBuyerSelectedWinner` and labelled "Modelo anterior — vencedor definido pelo
  Comprador". **No synthetic candidate backfill ever occurs**; editing such a batch through the
  new rework contract explicitly converts it to the candidate model.
- **Legacy non-batch area approval** (whole-request path in RequestsController) remains solely
  for historical requests; new QUOTATION approvals always go through candidate-based batches.
  Its eventual retirement is a future cleanup decision.

## Deployment note

Backend and frontend of v2.227.0 must deploy **together**, after applying migration
`20260811143822_AddCandidateBasedApprovalModel` (candidate table + nullable winner pointer +
decision stamps; no data movement). Manual validation: Buyer checklist (A–J), Area checklist
(A–N), Final/queue checklist (A–L) in this folder.
