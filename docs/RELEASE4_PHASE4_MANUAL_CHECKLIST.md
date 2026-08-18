# Release 4 — Phase 4 Manual TEST Validation Checklist (AUTHORITATIVE)

> ## RELEASE 4 — CLOSED / ACCEPTED IN TEST (2026-08-18, v2.229.9 / `d0c91d4`)
>
> **STATE 1 PASSED** (REQ-17/08/2026-232, dormant safety: every dimension satisfied, no
> automatic closure; six findings fixed in v2.229.1–.6). **STATE 2 PASSED**
> (REQ-18/08/2026-233, clean end-to-end automatic completion: persisted "Grupo Concluído"
> and "Pedido Concluído" with GROUP_COMPLETED/REQUEST_COMPLETED history and
> CompletionCycleId — sections B, C, D, K, L proven live; no manual FinalizeRequest).
> **Recovery PASSED** (REQ-232: dormant facts → activation → legitimate repeated
> ConfirmReceiving → automatic Phase-1 + Phase-2 completion; sweep PREVIEW live returned
> `eligibleCount=0` correctly — Phase-2-only by design; APPLY never run). **Timeline
> PASSED** (8 stages, Recebimento / Execução × Documentação Fiscal). Final validation:
> backend 1489/1490 (sole failure = pre-existing GroupBuilder baseline), frontend
> tsc/build clean. Remaining findings are backlog / Release 4.1 (see
> POST_PAYMENT_COMPLETION_RELEASE4.md closure section) — no Release 4 correctness blocker.
> PROD untouched; PROD rollout (migrations → deploy with CompletionEnabled=false → staged
> activation) requires separate authorization.

The authoritative manual validation record of the Release 4 completion lifecycle
(v2.229.0). Complements `RELEASE4_PHASE3B_MANUAL_CHECKLIST.md` (coverage workspace) — the
Phase 3 checks are not repeated here.

## The two validation states

**STATE 1 — `Enabled=true, CompletionEnabled=false`** (the configuration v2.229.0 deploys
with). Validates DORMANT-SAFE behavior: readiness/UX render honestly, dimension writes work
(operational receipt stamp, fiscal receipt storage+binding), and NOTHING transitions
automatically — no group leaves its status via the completion engine, no request completes,
no "Pronto para concluir" appears.
**State 1 sections: A, B (partial — no auto-completion), D, E, F, G, H, I, J, M.**
Execute State 1 FIRST; activation is only authorized after it passes.

**STATE 2 — `Enabled=true, CompletionEnabled=true`** (activation, separately authorized).
Validates the ACTIVE lifecycle: `WAITING_FISCAL_RECEIPT` transitions, automatic group and
parent completion, terminal COMPLETED presentation.
**State 2 sections: B (full), C, K, L, plus re-running D to observe automatic completion.**
**State 2 is NOT executed yet** — do not check its boxes before activation is authorized.

Suggested synthetic data (from the approved Phase 4 manual plan): Request A (PAYMENT, 1 group,
Proforma origin — normal completion), Request B (short-close completion), Request C
(QUOTATION, 2 groups — blocked completion), plus one legacy UNCLASSIFIED request read-only.

> **STATE 1 execution note (2026-08-17).** REQ-17/08/2026-232 exposed two defects during
> STATE 1: (1) after Final Approval the request presented "Cotação Concluída" while actively
> awaiting its first P.O. — invisible to Buyer status filters; (2) "ADIANTAMENTO NECESSÃ¡RIO"
> mojibake from a migration-transport encoding defect. Both fixed in **v2.229.1** (data-only
> migration `RepairWorkflowStatusNamesAndAwaitingPo` + pipeline UTF-8 hardening). **STATE 1
> execution is PAUSED until v2.229.1 is deployed to TEST.** After deployment, re-check before
> resuming: a) status after Final Approval reads "Aguardando P.O." and is Buyer-filterable;
> b) awaiting-P.O. dashboard counts include it; c) "Adiantamento Necessário" renders with
> correct accents (also "Ag. Entrega/Serviço" / "Ag. Reconciliação"); d) the REQ-232 R4-A
> lifecycle continues normally from its registered P.O. (payment → receipt → Final Invoice →
> readiness).

> **STATE 1 execution note 2 (2026-08-17, after v2.229.1).** Resumed STATE 1 exposed a second
> finding on REQ-17/08/2026-232: after the real "Confirmar Adiantamento" (100%, group left in
> ADVANCE_PAYMENT_COMPLETED — the shape the Phase 4A projection pins never used), completion
> readiness still showed "Aguardando pagamento — Financeiro". Fixed in **v2.229.2**
> (projection-only: COMPLETED owed-money evidence covering the group total now satisfies the
> payment dimension; partial advances/final balances/reconciliations stay fail-closed).
> **STATE 1 remains PAUSED until v2.229.2 is deployed to TEST.** After deployment, re-check on
> REQ-232: Payment = satisfied on the readiness card, "Aguardando pagamento" gone, remaining
> blockers only Recebimento / Fatura Final / Recibo Fiscal — then continue the R4-A lifecycle
> (delivery → receipt → Final Invoice → fiscal receipt readiness).

> **STATE 1 execution note 3 (2026-08-17, after v2.229.2).** Resumed STATE 1 exposed a third
> finding on REQ-17/08/2026-232: the ADVANCE_PAYMENT_COMPLETED → WAITING_SUPPLIER_DELIVERY
> transition never existed, so the confirmed-advance request was invisible to the Receiving
> workspace and rejected by every receiving endpoint. Fixed in **v2.229.3** (ConfirmAdvancePayment
> hands the group to "Ag. Entrega/Serviço"; data-only migration
> `HandoffParkedAdvancePaidGroupsToDelivery` repairs parked groups/parents — REQ-232 included).
> **STATE 1 remains PAUSED until v2.229.3 is deployed to TEST** (migration + deploy). After
> deployment, re-check: a) REQ-232 shows "Ag. Entrega/Serviço" and appears in the Receiving
> workspace's delivery section; b) "Receber" opens and receiving confirms; c) readiness then
> shows Recebimento ✓ (Pagamento still ✓); d) continue with Final Invoice → fiscal receipt
> readiness.

> **STATE 1 execution note 4 (2026-08-17, after v2.229.3).** The live receiving confirmation
> exposed a fourth finding: the modal presented the legacy "FINALIZAR PEDIDO … encerrado
> permanentemente" wording and demanded a mandatory legacy RECEIPT attachment that cannot even
> be uploaded in this state (Finance-only, WAITING_RECEIPT-only) — while the backend
> ConfirmReceiving was already correct. Fixed in **v2.229.4** (frontend + taxonomy only):
> "Confirmar Recebimento" with mandatory attestation checkbox, optional "Comprovativo de
> recebimento/execução" (new RECEIVING_EVIDENCE type), no document required, no closure claims.
> **STATE 1 remains PAUSED until v2.229.4 is deployed to TEST** (code-only, no migration).
> Live re-check on REQ-232 (still 1/1 received, awaiting confirmation): a) modal title
> "Confirmar Recebimento", attestation required, attachment optional; b) confirm WITHOUT a
> file; c) readiness shows Recebimento ✓, Pagamento ✓, Recibo Fiscal still pending; d) history
> carries the attestation statement; e) no automatic completion (CompletionEnabled=false);
> f) then continue with Final Invoice → fiscal receipt readiness.

> **STATE 1 execution note 5 (2026-08-17, after v2.229.4).** The v2.229.4 attestation modal
> PASSED live (title, mandatory attestation, no document, confirmation succeeded, statement in
> history), but exposed a fifth finding: the group landed in "Em Acompanhamento" and readiness
> stayed "Aguardando recebimento" despite 1/1 received. Root cause: the dual receiving-record
> mismatch — the batch/candidate model keeps `SelectedQuotationItemId` as a compatibility
> pointer, the receiving UI registers on the RequestLineItem, and the rulebook read only the
> never-updated quotation item. Fixed in **v2.229.5** (Domain rulebook only: received = either
> record RECEIVED; no frontend change, no migration). **STATE 1 remains PAUSED until v2.229.5
> is deployed to TEST** (code-only). Live re-check on REQ-232 (currently IN_FOLLOWUP with 1/1
> already received, no stamp): a) reopen "Confirmar Recebimento", attest, attach nothing,
> confirm; b) group moves to WAITING_RECEIPT (never stays IN_FOLLOWUP); c) readiness shows
> Recebimento ✓ and Pagamento still ✓, Fatura Final/Recibo Fiscal still pending;
> d) OPERATIONAL_RECEIPT_COMPLETED history + operational receipt stamp present, OR_DONE once;
> e) no automatic completion (CompletionEnabled=false); f) then continue with Final Invoice →
> fiscal receipt readiness.

> **STATE 1 execution note 6 (2026-08-17, after v2.229.5).** The healing confirmation and the
> full dormant-safety pass SUCCEEDED on REQ-232 (all five dimensions ✓, request honestly
> non-completed, correct header + explanatory sentence) — but the group card presented
> "Grupo Concluído" from `projection.Complete` alone, conflating satisfied requirements with
> the persisted COMPLETED status the lifecycle never wrote. Fixed in **v2.229.6**
> (frontend-only): "Grupo Concluído" (+timestamp) requires persisted COMPLETED;
> readiness-complete groups read "Requisitos Satisfeitos" (lifecycle off) or "Pronto para
> Concluir" (lifecycle on, transient); the "N de M grupos concluídos" count is persisted-based.
> **STATE 1 remains PAUSED until v2.229.6 is deployed to TEST** (code-only, no migration).
> Live re-check on REQ-232: a) group badge reads "Requisitos Satisfeitos" — NOT "Grupo
> Concluído"; b) header keeps "Requisitos de conclusão satisfeitos" + the inactive-lifecycle
> sentence; c) request stays "Aguardando Recibo"; d) no lifecycle mutation. Section J below
> now validates with this badge; sections K/L (STATE 2) will prove the persisted
> "Grupo Concluído"/"Pedido Concluído" distinction after activation.

> **STATE 2 execution note 1 (2026-08-18, activation + REQ-18/08/2026-233).** STATE 1 was
> formally CLOSED and `CompletionEnabled=true` activated in TEST (config-only, build
> v2.229.6/63fb0ad preserved). The first STATE 2 request (REQ-233) validated the lifecycle
> through operational receiving, but exposed a UI regression at WAITING_RECEIPT: the legacy
> status header offered "FINALIZAR PEDIDO (Recibo Fiscal)" / "Anexar recibo do fornecedor e
> finalizar pedido" — an action the backend Phase 4C guard refuses ("Fluxo Atualizado") for
> grouped requests under the active lifecycle. Fixed in **v2.229.7** (frontend-only): legacy
> finalize suppressed for grouped+classified+lifecycle-active requests; header guidance now
> readiness-derived (Fatura Final → Recibo Fiscal → conclusão automática). **STATE 2
> validation continues after v2.229.7 reaches TEST.** Live re-check on REQ-233: a) no
> "FINALIZAR PEDIDO (Recibo Fiscal)"; b) header "Financeiro / Registrar / validar a Fatura
> Final"; c) after the Fatura Final, header moves to "Registrar o Recibo Fiscal"; d) sections
> C/K/L then prove WAITING_FISCAL_RECEIPT and the persisted automatic completions; e) legacy
> groupless requests keep the legacy button. The parent-completion sweep APPLY remains
> forbidden until REQ-233 completes end-to-end.

> **STATE 2 execution note 2 (2026-08-18, monetary input hardening).** REQ-233's Final
> Invoice (FT-S2A-001) validated correctly, but confirmed the monetary-input backlog item
> live: currency fields depended on the Windows/browser decimal locale (English Windows
> required "."). Fixed in **v2.229.8** (frontend-only): shared `MoneyInput` — "." or ","
> accepted, pt-AO display "120 000,00", canonical values to the API. **STATE 2 remains
> paused before the Fiscal Receipt until v2.229.8 reaches TEST.** Manual regression cases
> (no frontend test framework — pinned by the pure-helper check script + live re-check):
> a) type Net `105263,16`, Tax `14736.84`, Gross `120000` → blur shows 105 263,16 /
> 14 736,84 / 120 000,00 and persisted totals remain 120 000,00 Kz / 105 263,16 Kz /
> 14 736,84 Kz; b) paste "120 000,00", "120,000.00", "120.000,00" all read 120 000,00;
> c) blank while editing allowed, zero representable, no browser spinner/alert;
> d) payment/reconciliation/quotation money fields behave identically; e) then resume
> STATE 2: fiscal receipt on REQ-233 → sections C/K/L automatic completions.

> **STATE 2 execution note 3 (2026-08-18, STATE 2 A closed + presentation polish).**
> REQ-233 completed the FULL automatic lifecycle: fiscal receipt bound → group persisted
> COMPLETED ("Grupo Concluído") → request persisted COMPLETED ("Pedido Concluído" /
> Finalizado). Sections C, K and L are functionally PROVEN. Two presentation findings fixed
> in **v2.229.9**: (1) the timeline now separates "Recebimento / Execução" (WSD/IN_FOLLOWUP —
> operational receipt/attestation) from "Documentação Fiscal" (WAITING_RECEIPT/
> WAITING_FISCAL_RECEIPT — Fatura Final/Recibo Fiscal), 8 stages, no duplicate "Agendamento",
> stage-6 date = operational receipt stamp; (2) "Registrar Recibo Fiscal" modal aligned with
> the Portal upload/typography standards. Live re-check after v2.229.9 reaches TEST:
> a) REQ-233 timeline renders Rascunho → Cotação → Aprovações → P.O. / Contratação →
> Pagamento → Recebimento / Execução → Documentação Fiscal → Concluído, all completed, no
> mutation on open; b) stage 6 date = the operational receipt instant; c) a WSD request
> shows "Recebimento / Execução" as current; d) the fiscal receipt modal (on a future
> request) shows the Portal upload area, remove-file and ✓ evidence rows. Remaining STATE 2
> items: sweep preview/validation with REQ-232, then PROD planning.

## A — Checklist dimensions render correctly

For a request with one classified group, open "Conclusão do Pedido" (below "Fatura Final —
Cobertura") and verify the five rows P.O. · Pagamento · Recebimento · Fatura Final · Recibo
Fiscal each show the correct state symbol (✓ Concluído / ○ Pendente / — Não aplicável /
⚠ Correção ou Bloqueio) as the request progresses through PO registration, payment,
receiving confirmation and Final Invoice validation.

- [ ] Each dimension flips exactly when its backend fact changes (refresh with "Atualizar").
- [ ] A group parked in WAITING_PO_CORRECTION shows ⚠ on P.O. and "P.O. em correção — Compras".

## B — Multi-group mixed state

Request C: complete group 1 fully; leave group 2 waiting receipt.

- [ ] Header shows "Conclusão pendente" + "1 de 2 grupos concluídos".
- [ ] Group 1 card shows "Grupo Concluído" (checklist still visible); group 2 lists its
      pending items with owners.
- [ ] The request never reads ready while any group is blocked.

## C — WAITING_FISCAL_RECEIPT label

(State 2, or via seeded data.) With a group in WAITING_FISCAL_RECEIPT:

- [ ] Request/group badges read "Aguardando Recibo Fiscal" — the raw code appears nowhere.
- [ ] Responsible panel shows Financeiro / "Registrar o Recibo Fiscal para concluir o grupo".

## D — Finance fiscal-receipt upload

As Finance, on a group whose only missing item is the Recibo Fiscal:

- [ ] "Registrar Recibo Fiscal" appears on the card (and only there).
- [ ] Modal shows supplier, P.O., invoice/receipt state and the explanatory sentence.
- [ ] Upload + confirm succeeds; the section refreshes; the receipt shows file/date/uploader
      with a working "Abrir" download.
- [ ] Retrying the same binding (double-click / refresh replay) never duplicates anything.
- [ ] No replace action exists for a bound receipt.

## E — Non-Finance read-only

As Requester/Receiving/Buyer:

- [ ] The readiness checklist is visible under normal request visibility.
- [ ] "Registrar Recibo Fiscal" is absent; no mutation CTA is offered.

## F — No separate fiscal receipt

For a Factura-Recibo-classified group (`RequiresSeparateFiscalReceipt=false`):

- [ ] The Recibo Fiscal row shows "— Não aplicável", never "missing".
- [ ] No upload CTA is offered; the group can read Complete without any receipt.

## G — ClosedShort evidence

Request B (approved short-close):

- [ ] Card shows "Encerrado com Saldo Aceite"; Fatura Final row reads ✓.
- [ ] The card never presents the group as 100% invoiced.

## H — Accepted divergence evidence

For a group validated above expected with explicit acceptance:

- [ ] "Divergência Aceite: +valor" badge appears on the completion card (same value as the
      coverage section).

## I — Legacy UNCLASSIFIED blocker

Open a pre-flow request with an UNCLASSIFIED group:

- [ ] "Classificação pendente — Financeiro / Administração" with the explanatory sentence.
- [ ] No fix/backfill button exists; the group can never read Complete.

## J — CompletionEnabled=false explanatory state (State 1)

Satisfy every requirement of a single-group request (incl. fiscal receipt when owed):

- [ ] Header shows "Requisitos de conclusão satisfeitos" + "O ciclo automático de conclusão
      ainda não está ativo neste ambiente."
- [ ] The request does NOT complete; no "Pronto para concluir", no manual complete button.
- [ ] The fiscal receipt uploaded in this state shows as satisfied (honest dimension fact).
- [ ] The group badge reads "Requisitos Satisfeitos" — NEVER "Grupo Concluído" (v2.229.6:
      that badge requires the persisted COMPLETED group status, which only STATE 2 writes).

## K — Completed group rendering (State 2)

- [ ] After the last obligation, the group reads "Grupo Concluído" + "Concluído em {data}";
      checklist and evidence remain visible.

## L — Completed request rendering (State 2)

- [ ] When every group completes, the request transitions automatically (no user action);
      header shows "Pedido Concluído" + instant; status badge "Finalizado"; all Phase 3/4
      mutation actions disappear; no reopen UI exists.

## M — Error handling spot-checks

- [ ] Binding a receipt to a group whose invoice is still pending → the precise "Recibo
      Fiscal bloqueado" message (never a raw code).
- [ ] Concurrent edit (two tabs) → "Os dados deste grupo foram alterados por outro
      utilizador." + "Recarregar dados"; no auto-resubmit.
- [ ] Expired session during any action → standard login redirect ("Sessão expirada…").
