# Candidate-Based Approval — Phase D Final/Queue UI Manual Checklist

> Manual validation script for the FINAL APPROVER / Approval Center side of the candidate-based
> ApprovalBatch flow. A+B+C+D deploy together (migration
> `20260811143822_AddCandidateBasedApprovalModel` first). Reference scenario = the 4-item /
> 8-candidate manual TEST request with Area selection Rolamento→Kwanza 253.080,
> Sensor→Luanda 625.860, Kit→Kwanza 266.760, Serviço→Luanda 312.360 → selected total
> **1.458.060 AOA**.

## A. Final winner visible
Open the Final wizard/drawer on the decided batch: the step reads **"Desfecho Comercial
Selecionado pelo Aprovador de Área"**; every item shows the winner card with the badge
**"Vencedor selecionado pelo Aprovador de Área"**, full frozen facts (supplier, quoted
description/qty/unit, unit price, discount, IVA, total, doc number/date, BuyerNote, warnings)
and the decision metadata "Selecionado por {nome} em {data}" (name, never an id).

## B. Losing candidates collapsed
Each item shows **"Ver outras opções (1)"** below the winner — collapsed by default. An item
that had a single option shows no expander at all (never "Ver outras opções (0)").

## C. Expand losing candidates
Expanding lists the losing option(s) read-only in muted cards with the same comparable frozen
fields + MENOR VALOR / Substituto / Qtd-difere badges and BuyerNotes. Collapse works; the state
is local (reopening the wizard collapses again).

## D. No mutation controls
Nowhere in the Final view are there radios, checkboxes, editable fields, or any way to change
the winner. The Final actions remain Approve / Reject / Solicitar Reajuste only. (Backend pin:
a Final payload carrying Selections is ignored.)

## E. Non-cheapest justification visible
For a batch where Area picked a more expensive option with justification: the winner card shows
the prominent amber block **"Justificativa da escolha (acima do menor valor)"** with the stored
text; the cheaper losing option is visible under "Ver outras opções". An optional note on a
cheapest pick renders as **"Nota da decisão (opcional)"** instead.

## F. Legacy item display
A pre-candidate batch item still renders the read-only winner box labelled **"Modelo anterior —
vencedor definido pelo Comprador"** — no fabricated losing options, no expander.

## G. Pre-Area queue amount = "a definir"
The Approval Center AREA card of an undecided candidate batch shows
**"A definir pelo Aprovador de Área"** (no warning icon, no 0, no request estimate); the queue
total KPI simply excludes it.

## H. Post-Area total = selected snapshot total
After the Area decision, the FINAL queue card shows **1.458.060** (frozen snapshot combination);
editing a live quotation price afterwards changes nothing (backend pin included). After Final
approval the amount follows the existing ApprovedTotalAmount semantics.

## I. Mixed currency rendering
For a decided batch with winners in two currencies, the wizard summary shows one
"Total selecionado (MOEDA)" line per currency and never a cross-currency sum.

## J. Final request-adjustment copy
"Solicitar Reajuste" on a decided candidate batch shows: "Este lote será devolvido para correção
e a seleção de vencedores deverá ser refeita pelo Aprovador de Área (a decisão anterior fica
registrada no histórico)." After confirming, winner fields are cleared (Phase A) and the history
retains BATCH_CANDIDATES_SUBMITTED → QUOTATION_ITEM_AWARDED → BATCH_FINAL_ADJUSTMENT.

## K. Guided-tour correctness
Area drawer tour: Buyer sends options / Area selects the winner. Final drawer tour ("Cotações e
Escolha do Fornecedor" step): Final reviews the Area-selected outcome and never changes winners.
Approval Center tour mentions the "A definir pelo Aprovador de Área" card state. No tour implies
the Buyer selects winners in the new flow.

## L. Area header no request-estimate fallback
The Area drawer hero header of an undecided candidate batch shows **"A definir pelo Aprovador de
Área"** instead of a monetary value (never 0 / the request estimate). After the Area decision the
header shows the snapshot-based lot total.
