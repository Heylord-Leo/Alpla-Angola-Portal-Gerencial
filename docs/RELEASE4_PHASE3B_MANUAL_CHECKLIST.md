# Release 4 Phase 3B — Manual TEST Checklist (Operation Invoice / Fatura Final)

## v2.228.3 patch regression (SATISFIED divergence + ClosedShort + wizard eligibility)

- [ ] Luanda (938.220 / 938.220, "Fatura Final Completa"): as **Finance**, Step 2 shows the group
      selectable with "Grupo totalmente coberto — qualquer nova distribuição exigirá análise de
      divergência."; FT-LU-003 (30.000) allocates as divergence candidate (no generic
      eligibility error).
- [ ] After the draft: group shows Em validação 30.000, Validado unchanged 938.220.
- [ ] Validate FT-LU-003 without "ACEITAR DIVERGÊNCIA" → refused; with acceptance +
      justification → succeeds; snapshot freezes Expected 938.220 / Validado antes 938.220 /
      Alocação 30.000 / Variação +30.000.
- [ ] As **Buyer**, the same fully covered group is disabled ("Grupo totalmente coberto");
      forcing via API returns `OI_ALLOC_GROUP_OVER`.
- [ ] A short-closed group is disabled for ALL actors ("Grupo encerrado com saldo aceite");
      forcing via API returns `OI_ALLOC_GROUP_CLOSED_SHORT`.
- [ ] Supplier-mismatch and currency-mismatch groups appear disabled with their reasons;
      Finance cannot bypass identity via divergence.
- [ ] Step 3: a divergence candidate with a short/placeholder justification cannot advance to
      Revisão; a meaningful justification (≥20 chars) can.

## v2.228.2 patch regression (drawer menu + calendar dates)

- [ ] **A** — Request Drawer → FT-KW-001 → ⋮ : the action menu is fully visible ABOVE the
      drawer (not a movement behind the blur); actions readable and clickable; menu closes on
      action click and on outside click.
- [ ] **B** — "Distribuir Fatura Final" is reachable from that menu (per lifecycle/role).
- [ ] **C** — the allocation wizard (and any modal opened from the menu) stacks above BOTH the
      drawer and the menu.
- [ ] **D** — page-context kebab menus (Finance payments list, Buyer items, Contracts, IT,
      requests table) behave exactly as before (default layer unchanged).
- [ ] **E** — FT-KW-001 shows **Doc: 12/08/2026** (the exact entered date; previously 11/08).
- [ ] **F** — FT-KW-001 shows **Venc: 26/08/2026** (previously 25/08). No re-registration —
      the persisted values were always correct.
- [ ] **G** — "Registada por … em …", "Validada em …" and the short-close "proposta em …"
      timestamps still show the correct day per Portal conventions (UTC value, local display).
- [ ] **H** — no dark/light visual regression on the menu or the invoice card.

> Prerequisite TEST configuration (server-side, applied at the approved activation step):
>
> ```json
> "PostPaymentCompletion": { "Enabled": true, "CompletionEnabled": false, "EffectiveDateUtc": "<approved cut-off>" }
> ```
>
> Committed defaults remain `false`/`false`. The migration
> `AddOperationInvoiceAllocationAudit` must run before the backend deploys.
> With `CompletionEnabled=false`, legacy finalization keeps working for classified grouped
> requests — that is the INTENDED Phase 3B state, not an error.

## 0. Capability gating

- [ ] With `Enabled=false`: the "Fatura Final — Cobertura" section does not render anywhere;
      obligations endpoint returns 404.
- [ ] With `Enabled=true, CompletionEnabled=false`: the section renders on requests with
      obligation-bearing groups; **no warning** about completion being disabled appears.
- [ ] `/api/v1/config/features` returns `postPaymentCompletionEnabled=true`,
      `completionLifecycleEnabled=false`.

## 1. Reference scenario (REQ-183 shape)

After Final Approval + PO/payment prerequisites, the request holds two commercial groups:

| Group | Supplier | Expected |
|---|---|---|
| Kwanza | Kwanza | 519.840,00 AOA |
| Luanda | Luanda | 938.220,00 AOA |

- [ ] Coverage cards show: Esperado / Validado (0) / Em validação (0) / Restante / Cobertura 0% /
      "Aguardando Fatura Final".
- [ ] A legacy group with no expected total shows "Valor esperado ainda não definido" — never
      "0 AOA" — and the informational activation note for Finance/Admin.

## 2. Registration ("Registrar Fatura Final")

- [ ] Buyer and Finance see the button; Requester/Receiving/Viewer do not.
- [ ] Register invoice FT-A (supplier Kwanza, 519.840 AOA, PDF attached, doc date) → appears as
      "Aguarda Validação" with "Valores informados manualmente".
- [ ] The attachment lands as type OPERATION_INVOICE (Fatura Final) — request quotation/proforma
      attachments are never reclassified.
- [ ] Duplicate preflight: registering the same supplier+number+series again shows the advisory
      warning naming the existing invoice/request; proceeding is refused by the server (409).
- [ ] Net+Tax ≠ Gross shows the inline warning; the backend refuses outside tolerance.
- [ ] Editing a PENDING_VALIDATION invoice works; editing Supplier/Currency after allocation is
      refused with the supplier/currency mismatch message; editing Notes still works.
- [ ] A VALIDATED invoice offers no Edit — only Substituir/Ver distribuição.

## 3. Allocation wizard ("Distribuir Fatura Final") — scenarios A–G

- [ ] **A — one invoice fully covers one group**: FT-A → Kwanza 519.840 → after Finance validates,
      Kwanza shows Validado 519.840, Cobertura 100%, "Fatura Final Completa".
- [ ] **B — two invoices partially cover one group**: FT-B1 (500.000) + FT-B2 (438.220) → Luanda;
      after validating B1 only: Validado 500.000, Em validação 438.220, "Fatura em Validação";
      after validating B2: "Fatura Final Completa".
- [ ] **C — one invoice across several groups**: a single supplier invoice allocated to two
      eligible groups of the same supplier+currency splits correctly; supplier mismatch against a
      different supplier's group is refused (OI_ALLOC_SUPPLIER_MISMATCH).
- [ ] **D — pending vs validated**: while pending, amounts appear ONLY under "Em validação";
      never in "Validado". Chips distinguish Validado / Em validação per allocation row.
- [ ] **E — incomplete allocation blocks validation**: allocate 300.000 of a 519.840 invoice →
      "Validar Fatura" shows "Distribuição: Incompleta" and the confirm stays disabled; forcing
      via API returns OI_VALIDATE_ALLOCATION_INCOMPLETE.
- [ ] **F — Buyer over-coverage**: Buyer allocating beyond the group's expected+tolerance sees
      the hard business error with no override offered; server answers OI_ALLOC_GROUP_OVER.
- [ ] **G — Finance divergence candidate**: Finance allocating over-expected must write the
      ≥20-char "Justificativa da divergência"; the draft is labelled "Candidato a divergência",
      NEVER accepted; validation demands the explicit "ACEITAR DIVERGÊNCIA" checkbox (never
      pre-selected) + justification; only then does validation succeed and the group read
      SATISFIED with the reconciliation snapshot recording the decision.
- [ ] Replace-set semantics: reopening the wizard and unchecking a group removes its allocation;
      an identical resubmit is a harmless no-op (no new audit rows).
- [ ] Wizard shows both live balances (invoice Gross/Distribuído/Restante; group Esperado/
      Validado/Em validação/Restante) and never silently caps a value.

## 4. Validation / Rejection — scenario H

- [ ] "Validar Fatura" summary: gross, total allocated, completeness, groups affected.
- [ ] After validating: invoice chip → "Validada"; coverage moves from Em validação to Validado;
      group card, section header and Finance drawer all agree (single read model).
- [ ] **H — reject**: rejecting a PENDING_VALIDATION invoice explains that allocations remain in
      history but stop contributing; after rejection the group re-derives (coverage drops,
      status returns to Aguardando/Parcialmente conforme validated remainder); the historical
      allocation rows are still visible on the invoice ("Ver distribuição").
- [ ] Reject requires a reason; the modal clearly says it rejects THE DOCUMENT, not the request.
- [ ] Void ("registada por engano") requires a reason and is offered only before validation.
- [ ] Replace on a VALIDATED invoice with allocations is refused with the backend's
      downstream-evidence explanation (no automatic transfer offered).

## 5. Short-close — scenario I

- [ ] "Propor Encerramento com Saldo" appears only on eligible groups with real remaining beyond
      tolerance (and no active proposal / not closed short).
- [ ] Proposal requires ≥20-char justification; RemainingAmountAtProposal is displayed frozen.
- [ ] The proposer sees "Retirar Proposta" (never Approve); a second Finance user sees
      Aprovar/Rejeitar; rejection demands a reason.
- [ ] Self-approval attempt via API returns the structured refusal.
- [ ] After approval: group shows "Encerrado com Saldo Aceite" with the short amount and approver
      — NOT "100% faturado"; coverage percent stays at its validated value.
- [ ] After rejection/withdrawal the slot frees and a new proposal is possible.

## 6. Concurrency

- [ ] Two browsers editing the same invoice/allocations: the second write shows "Os dados desta
      fatura ou grupo foram alterados por outro utilizador." with "Recarregar dados"; stale
      values are never auto-resubmitted.

## 7. PAYMENT and QUOTATION

- [ ] The section and wizard behave identically on a QUOTATION request with awarded groups and on
      a PAYMENT request with classified groups; no candidate-approval concepts leak into the
      allocation UI.

## 8. Authorization matrix

| Action | Buyer | Finance | SysAdmin | Requester/Receiving/Viewer |
|---|---|---|---|---|
| Register / edit draft / allocate draft | ✔ | ✔ | ✔ | — (read-only) |
| Propose short-close | ✔ | ✔ | ✔ | — |
| Validate / Reject / divergence acceptance | — | ✔ | ✔ | — |
| Short-close decision | — | ✔ (not own proposal) | ✔ (not own proposal) | — |
| Replace validated | — | ✔ | ✔ | — |

- [ ] Frontend hides/disables accordingly; forcing via API still returns 403 (backend authority).

## 9. Expected-total activation (backend tool — no everyday UI)

Deliberate Phase 3B decision: no admin activation panel was built. Before TEST activation, run
the documented technical step (SysAdmin token):

- `GET /api/v1/admin/release4/expected-operation-invoice-totals/preview` (Finance may also view)
- `POST …/apply` body `{ "reason": "<meaningful reason ≥20 chars>" }` — SysAdmin only.

- [ ] Preview lists eligible vs skipped (NOT_CLASSIFIED / NOT_REQUIRED / NO_TOTAL); apply writes
      only null-expected groups, never overwrites, and a re-run writes zero.
