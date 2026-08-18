# Candidate-Based Approval — Phase B Buyer UI Manual Checklist

> Manual validation script for the Buyer side of the candidate-based ApprovalBatch flow
> (Phases A backend + B Buyer UI — must be validated and deployed **together**; apply migration
> `20260811143822_AddCandidateBasedApprovalModel` before deploying). No frontend test framework
> exists; this checklist is the Phase B acceptance evidence.

**Reference scenario** (mirrors the manual TEST request):

| Item | Kwanza Industrial | Luanda Suprimentos |
|---|---|---|
| 1. Rolamento industrial 6205-2RS | 253.080 | 272.232 |
| 2. Sensor fotoelétrico M18 24VDC | 660.060 | 625.860 |
| 3. Kit de conectores industriais M12 | 266.760 | 287.280 |
| 4. Serviço de calibração de sensores | 328.320 | 312.360 |

Modal expectations that apply to every step: title **"Enviar Cotações para Aprovação"**;
per-item section **"OPÇÕES A ENVIAR PARA APROVAÇÃO"** with **checkboxes** (never radios); no
"vencedor" language anywhere on the Buyer side; footer button **"Enviar opções para aprovação"**;
summary never shows a "Total considerado" — only counts, faixa comercial (min/max) and
**"Total aprovado: A definir pelo Aprovador de Área"**.

## A. One candidate on one item
1. Request with a single quoted item (one supplier). Open the modal.
2. The single option comes **prechecked**; "MENOR VALOR" badge present.
3. Submit → success toast "Lote criado — opções enviadas para o Aprovador de Área".
4. DB/detail: batch item has 1 candidate, `SelectedQuotationItemId` **NULL**, no winner badge anywhere.

## B. Two candidates on one item
1. Item quoted by two suppliers. Open modal — **both options prechecked** (approved default:
   precheck all; visibly editable).
2. Uncheck one → summary "Opções enviadas" drops by 1; min/max collapse to the single value.
3. Submit with both → 2 candidate rows persisted; still no winner.

## C. All 8 candidates in the four-item scenario
1. Open the modal for the reference request: 4 items, 8 prechecked options.
2. Summary shows: Itens selecionados **4** · Opções enviadas **8** · Fornecedores **2** ·
   Menor combinação **1.458.060** · Maior combinação **1.547.892** · Total aprovado **A definir**.
3. Submit → one batch, 4 items, 8 candidates, no winner, request lines → `BATCH_ASSIGNED`.

## D. Deselect all candidates for one included item → blocked inline
1. With item 1 still "Incluir no lote", uncheck both of its options.
2. Click "Enviar opções para aprovação" → the modal scrolls to item 1 and shows the inline error
   "Selecione pelo menos uma opção de cotação para este item (ou remova-o do lote)." — red border
   on the card, **no browser alert, not only a toast**. Nothing is submitted.

## E. Partial batch with only 2 of 4 requested items
1. Untick "Incluir no lote" on items 3 and 4 (cards dim, options hidden, note "Fora deste lote…").
2. Submit → batch carries only items 1–2; items 3–4 remain `QUOTATION_PENDING` in the queue
   ("Cotado — pronto para envio" badge still correct) and can enter a later batch.

## F. Optional BuyerNote
1. Check an option → the "Observação do Comprador (opcional)" input appears beneath it; it is
   never auto-populated and never labelled preferido/recomendado/vencedor.
2. Type e.g. "Prazo comercial mais favorável", submit → note persisted on that candidate snapshot
   (visible in batch detail); options without notes have none.

## G. Mixed candidate values / MENOR VALOR badge
1. On each item, the badge sits ONLY on the lowest same-currency total (items 1/3 → Kwanza,
   items 2/4 → Luanda). It never affects which boxes are checked.
2. A SUBSTITUTE option shows the "Substituto" badge + its justification; a quoted quantity
   different from the requested one shows "Qtd difere do pedido"; line-adjustment justifications
   appear in the amber warning strip. Warnings never block submission.

## H. Returned batch edit
1. Area Approver returns the batch (request-adjustment). Open "Corrigir Lote #N".
2. Persisted options are prechecked showing the badge **"Valores congelados no envio"** — edit a
   live quotation price first and confirm the displayed frozen value does NOT change.
3. Add a new option (unchecked, live values), remove one, edit a BuyerNote, then
   "Salvar Correções e Reenviar" → batch returns to WAITING_AREA_APPROVAL; the re-added/new
   option gets a fresh snapshot; no winner exists.
4. "Manter no lote" unticked on one item → after save, that line returns to the buyer queue.
5. A batch with a buyer-included EXTRA_ITEM: the extra stays governed by the "Itens adicionais"
   panel (INCLUDE/EXCLUDE), re-enters the batch automatically, and is not candidate-editable.

## I. Legacy batch display
1. Open "Corrigir Lote" on a pre-candidate batch (created before Phase A/B).
2. Amber banner "Lote do modelo anterior…" explains that saving converts it to the candidate
   model; the historical winner appears as the single prechecked option (no fabricated extras).
3. "Reenviar sem alterações" keeps legacy semantics; "Salvar Correções e Reenviar" converts.

## J. BATCH_ASSIGNED locking after successful create
1. After scenario C, the 4 items show "Lote #N — Aguardando Aprovação da Área" in the Buyer
   workspace and are absent from a new batch modal.
2. The quotations used show the "used in batch" badge (both winners and candidates count as used);
   quotation edit/delete of a candidate-bearing quotation is blocked by the backend (409).
3. The batch card shows "Itens: 4 · Opções: 8".
