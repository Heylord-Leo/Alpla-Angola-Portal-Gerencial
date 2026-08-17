# Release 4 — Phase 4 Manual TEST Validation Checklist (AUTHORITATIVE)

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
