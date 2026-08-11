# Candidate-Based Approval — Phase C Area UI Manual Checklist

> Manual validation script for the AREA APPROVER side of the candidate-based ApprovalBatch flow.
> A+B+C is the minimum deployable/testable unit (apply migration
> `20260811143822_AddCandidateBasedApprovalModel` before deploying). Reference scenario = the
> manual TEST request (Rolamento/Sensor/Kit/Serviço quoted by Kwanza and Luanda — totals in the
> Phase B checklist). Wizard expectations everywhere: the batch step is titled
> **"Seleção do Vencedor"**, uses **radio** semantics, shows FROZEN snapshot values, and no
> candidate commercial fact is editable.

## A. Two candidates on one item
1. Open the Area wizard on a candidate batch; the item shows both option cards with supplier,
   quoted description/qty/unit, unit price, discount, IVA rate+amount, line total, doc number/date.
2. Select one via radio — card highlights (border + "Selecionada" text + radio), header badge
   flips to "Vencedor selecionado".

## B. One candidate still requires explicit selection
1. An item with a single option (including a buyer-included EXTRA_ITEM) shows the badge
   **"Única opção enviada"** and starts UNSELECTED.
2. "Próximo"/Aprovar is blocked until the radio is explicitly clicked — nothing auto-selects,
   not even the only/cheapest option.

## C. Four items / eight candidates reference scenario
1. All 4 items render with 2 options each; MENOR VALOR sits on Kwanza (items 1/3) and Luanda
   (items 2/4).
2. Select K, L, K, L → tentative summary shows "Total da combinação selecionada (AOA):
   **1.458.060**"; suppliers "Kwanza Industrial, Luanda Suprimentos"; decididos 4 / pendentes 0.
3. Approve → success; groups Kwanza 519.840 + Luanda 938.220 (backend-built; verify in detail).

## D. Approval blocked with one item undecided
1. Leave one item unselected; "Próximo" on the selection step shows "Decisões pendentes: 1" and
   the undecided card gains the red highlight + "Decisão pendente" badge.
2. The Aprovar button on the final step stays disabled (step 3 invalid); "Solicitar Reajuste"
   remains available as the escape path.

## E. Cheapest candidate selected
No justification field is demanded; an optional "Adicionar justificativa da escolha (opcional)"
expander is available; text entered there is submitted and appears in the award history.

## F. More expensive candidate selected → justification required
1. Select Rolamento → Luanda (272.232 > 253.080 + tolerância): the amber block
   **"Justificativa para escolha acima do menor valor"** appears, marked obrigatória.
2. Empty/short/filler text (< 20 meaningful chars, "aaaaaaaa…", digits only) keeps the step
   invalid with the exact validator message; a real reason unlocks it.
3. Switching back to the cheapest candidate clears the mandatory error; text already typed is
   KEPT (submitted as optional justification — same persistence rule as the backend).

## G. Tie within tolerance → no required justification
With two options at the same total (or within max(1; 0,1%)), selecting either shows the
"Empate no menor valor" badge and requires no justification.

## H. BuyerNote visible
A candidate with "Observação do Comprador" shows it in a blue note block on the option card —
informational only, no preference styling.

## I. Quantity discrepancy warning visible
A candidate whose quoted qty differs from the requested qty shows the "Qtd difere do pedido"
badge (amber, not red) and remains selectable.

## J. Returned batch requires fresh selections
1. Approve → Final requests adjustment → Buyer resubmits.
2. Reopening the Area wizard: NO previous selection is restored (all radios empty, decision
   pending); the previous decision exists only in the request history.

## K. Legacy batch read-only winner
A pre-candidate batch shows the winner box with the badge **"Modelo anterior — vencedor definido
pelo Comprador"** — no radios, no selection required, approval proceeds as before.

## L. Tentative total updates as radios change
The summary block recomputes live per currency; while undecided it is labelled
**"Total parcial das seleções"**, flipping to **"Total da combinação selecionada"** when all
items are decided. It is never called "Total aprovado".

## M. Budget preview uses tentative selection
1. With a partial selection, the Disponibilidade Orçamental step previews ONLY the selected
   subset (labelled by its own amounts) — nothing persists.
2. Complete the selection → preview totals equal the selected combination (1.458.060 in C).

## N. Changing live quotation does not alter frozen Area values
Edit a live quotation price after Buyer submission (e.g. via SQL/TEST data): the wizard cards,
tentative totals and budget preview keep the FROZEN submitted values; the awarded group totals
after approval also match the snapshots.
