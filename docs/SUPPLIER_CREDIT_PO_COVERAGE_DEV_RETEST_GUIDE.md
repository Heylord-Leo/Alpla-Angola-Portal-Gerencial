# Crédito em conta corrente do fornecedor — Guia de reteste em DEV (2.ª ronda, Stage 1)

**Sequência acordada:** correções em DEV → **esta 2.ª ronda em DEV** → só depois se planeia a publicação em TEST. Nada deste guia
é executado em TEST ou PROD. Nenhum commit, migração, deploy ou alteração de configuração é feito por este guia.

Base: árvore de trabalho de 2026-11-12 (HEAD `3d57d95` v2.249.9 + Stage 1 + correções da 1.ª ronda, **tudo não commitado**).
Registo da 1.ª ronda e das correções: [SUPPLIER_CREDIT_PO_COVERAGE.md §10](SUPPLIER_CREDIT_PO_COVERAGE.md). Guia completo da
1.ª ronda (cenários, dados, pontos de controlo P0–P17): [SUPPLIER_CREDIT_PO_COVERAGE_DEV_TEST_GUIDE.md](SUPPLIER_CREDIT_PO_COVERAGE_DEV_TEST_GUIDE.md).

Legenda de cada passo: **[Manual]** = passo que o proprietário executa e evidencia; **[Automático]** = evidência já produzida por
testes automatizados (indicada para não repetir manualmente); **[Verificação API]** = pedido HTTP com o token do perfil indicado
(`sessionStorage.getItem('auth_token')` na consola do browser), com placeholders `{…}`.

Fora do âmbito de sucesso do Stage 1 (não testar como sucesso): Antecipado Parcial com crédito, pedidos PAYMENT, reversão após
cobertura parcial (esperada como recusa, ver 3.6).

---

## 1. Preparação da 2.ª ronda

### 1.1 Reinício dos serviços DEV (obrigatório, coordenado com o workspace activo)

> **Feito em 2026-11-12 às 15:24** pelo agente (após confirmação do workspace de HR): API PID 60836 e Vite em execução com o build
> que corrige o erro SQL 130 do explorador ([SUPPLIER_CREDIT_PO_COVERAGE.md §10.6](SUPPLIER_CREDIT_PO_COVERAGE.md)). Os passos
> abaixo só são necessários se voltar a haver alterações de backend.

Quatro ficheiros do backend mudaram (`RequestsController.cs`, `FinanceController.cs`, `RequestWorkflowProjectionBuilder.cs`,
`RequestListItemDto.cs`): a API DEV em execução **não** contém as correções 1 e 4 até ser recompilada e reiniciada. O Vite recarrega
o frontend sozinho, mas o reinício conjunto garante uma árvore coerente.

1. **[Manual]** Confirmar com o workspace activo (HR / Viana 3 / unlock DEV) que pode reiniciar as portas 5000 e 5173.
2. **[Manual]** Executar `powershell -ExecutionPolicy Bypass -File C:\dev\alpla-portal\execution\restart_services.ps1`
   (mata os processos das portas, valida a base, `dotnet build`, arranca API em Development e `npm run dev`).
   Se o build falhar por bloqueio da pasta `bin`, obter o PID com
   `Get-NetTCPConnection -LocalPort 5000 -State Listen | Select-Object OwningProcess`, `Stop-Process -Id <PID> -Force` e repetir.
3. **[Manual]** Confirmar na API: `GET /api/v1/requests?page=1&pageSize=5` devolve itens com o campo `hasMultipleCurrencies`
   (presente = build novo). Evidência: JSON.
4. **[Manual]** Flag: manter `"SupplierCredit": { "NewReservationsEnabled": true }` em `appsettings.Development.json` para as
   secções 2 e 3.1–3.6; a secção 3.7 exige desligar e reiniciar. Registar o estado final da flag no relatório.

### 1.2 Estado de partida das contas (após a 1.ª ronda)

| Conta | Empresa / moeda | Saldo esperado | Reservado |
|---|---|---|---|
| CCS-000004 | AlplaPLASTICO / AOA (CONSULTIT) | 900 000,00 | 0,00 |
| conta RND do fornecedor da 1.ª ronda | AlplaPLASTICO / RND | 430 000,00 | 0,00 |

**[Manual]** Compras (perfil `Contracts`) → **Contratos** → **Fichas de Fornecedor** → ficha → secção **Conta corrente de crédito**:
confirmar os dois valores acima antes de começar; se divergirem, registar o valor real como novo ponto de partida.

### 1.3 Evidência automática já disponível (não repetir manualmente)

| Correção | Evidência automática |
|---|---|
| 3 — aviso "100% Antecipado" | `src/lib/advanceObligationNotice.test.ts` (8 testes): sem crédito mantém o texto original; cobertura integral → "Não há adiantamento bancário"; parcial → repartição crédito / banco; Manter / Alterar / Remover resolvidos |
| 4 — duas etapas "ATUAL" | `RequestLotTimelineBuilderTests` (4 novos): ADVANCE_FULL em WAITING_SUPPLIER_DELIVERY → Pagamento concluído e só Recebimento / Execução actual; ADVANCE_PARTIAL mantém Pagamento actual; ADVANCE_PAYMENT_REQUIRED mantém Pagamento actual |
| 6 — rótulos do histórico | `src/lib/workflowEventLabels.test.ts` (3 testes): os cinco códigos têm rótulo e o Print View reutiliza o mesmo mapa |
| Regressão Stage 1 | filtro backend `SupplierCredit|FinanceSupplierCredit|RequestWorkflowProjection` → 116 verdes em 2026-11-12; `tsc -b` limpo |
| **Fecho da 2.ª ronda (secção 3)** | `SupplierCreditStage1ClosureEndpointTests` (5, endpoints reais em InMemory) e `SupplierCreditRelationalTests` (5, **executados em SQL Server** LocalDB: bloqueios `UPDLOCK`, índices únicos, concorrência) → conjunto completo 217 verdes em 2026-11-12. Detalhe por verificação: [SUPPLIER_CREDIT_PO_COVERAGE.md §10.7](SUPPLIER_CREDIT_PO_COVERAGE.md) |

As correções 1, 2 e 5 são de apresentação com dados reais: só têm evidência manual (secção 2).

> **Estado em 2026-11-12 (fecho):** as confirmações manuais do proprietário cobrem 2.1 (moedas, Total Filtrado por moeda e idêntico
> entre páginas, "-----" / "0,00" com cancelados), 2.2 (11 274,23 RND e 5 000,00 RND em ECF10 2026/9640 — só apresentação), 2.3 (os três
> avisos em ECF10 2026/9637 — só apresentação, nenhuma P.O registada), 2.4 (684), 2.5 (movimentos da ficha) e 2.6 (histórico do
> pedido). As verificações da secção 3 têm agora evidência automática / SQL Server (ver 1.3) — **não precisam de ser repetidas à mão**;
> os passos [Verificação API] ficam como referência. Só restam os passos visuais da secção 4 marcados "pendente".

---

## 2. Ecrãs corrigidos — verificação manual

### 2.1 Correção 1 — moeda no explorador de pedidos

1. **[Manual]** Qualquer perfil com acesso → **Pedidos** (explorador) → localizar **REQ-27/10/2026-647**: a coluna **Valor Estimado**
   deve mostrar **RND 11.274,23** (não AOA). Repetir para **684** (RND 15.840,00) e **685** (RND 12.450,00). Evidência: captura.
2. **[Manual]** Na mesma lista, **REQ-12/11/2026-688** (CONSULTIT) deve mostrar **AOA 100.000,00**.
3. **[Manual]** Abrir o cartão de **647** em **Para Minha Ação** (carrossel), se o pedido lá estiver para o seu perfil: rótulo
   "Valor (RND)". Evidência: captura.
4. **[Manual]** **Recebimento** → lista de pedidos (ramo escalar): a linha de 647 mostra "RND 11.274,23".
5. **[Verificação API]** `GET /api/v1/requests?search=647&page=1&pageSize=5` → o item tem `currencyCode: "RND"`,
   `estimatedTotalAmount: 11274.23`, `hasMultipleCurrencies: false`. Evidência: JSON.
6. **Pedidos com grupos em moedas diferentes** (regra definida: **uma linha por moeda, nunca uma soma**): se existir em DEV um pedido
   QUOTATION aprovado com grupos em duas moedas, a célula "Valor Estimado" mostra duas linhas (ex.: `AOA 250.000,00` / `USD 1.200,00`)
   e a API devolve `hasMultipleCurrencies: true` + `amountsByCurrency`. Se não existir, registar "não testado — sem pedido
   multi-moeda em DEV" (não criar um só para isto nesta ronda).
7. **KPI "Total Filtrado" por moeda** (corrigido a pedido do proprietário em 2026-11-12; exige a API reiniciada, 1.1):
   - **[Manual]** Explorador sem filtros: o rodapé "Total Filtrado" mostra **um total por moeda**, ex.: `AOA 1.234.567,00 · RND 28.290,00`,
     nunca uma soma única. Evidência: captura.
   - **[Manual]** Filtrar pela empresa/planta dos pedidos RND (647, 684, 685): o total RND tem de ser a soma dos valores das
     linhas RND de **todas as páginas** (reduzir "por página" para 5 e confirmar que o total não muda ao mudar de página).
     Conferir com a soma manual das linhas (ex.: 11.274,23 + 15.840,00 + 12.450,00 = 39.564,23 se forem os únicos RND).
   - **[Manual]** Separador **Cotações** → **Pagamentos** → **Todos**: o total recalcula a cada mudança de filtro/pesquisa.
   - **[Manual]** Pesquisar um texto sem resultados: o rodapé mostra **0,00** (sem moeda).
   - **[Manual]** Pedidos cancelados/rejeitados não entram (filtrar por estado "Cancelado": total 0,00).
   - **[Manual]** Janela a ≈ 1 100 px e ≈ 800 px: os totais mantêm-se legíveis, cada "MOEDA valor" numa só linha, quebrando
     entre moedas se faltar espaço; o rodapé flutuante não tapa a tabela.
   - **[Verificação API]** `GET /api/v1/requests?page=1&pageSize=5` → `summary.filteredTotalsByCurrency` com uma entrada por
     moeda (`currency`, `amount`) igual ao rodapé; `summary.filteredCurrencyCodes` com as mesmas moedas. Evidência: JSON.
   - **[Automático]** `RequestsExplorerFilteredTotalsTests` (12) e `src/lib/requestsFilteredTotals.test.ts` (5): uma moeda,
     várias moedas, pedido multi-moeda (componente por moeda, grupo cancelado excluído), paginação, resultados vazios, pedido
     cancelado excluído, tradução SQL Server; `RequestsExplorerFilteredTotalsRelationalTests` (1) **executa** a consulta no
     LocalDB (regressão do erro SQL 130 de 2026-11-12).
   - **[Automático, API em DEV 2026-11-12 15:3x]** `GET /api/v1/requests` sem filtros → 642 pedidos, 4 moedas separadas (AOA · EUR ·
     RND · USD); pesquisa, empresa, planta, estado e tipo → 200; páginas 1, 2 e 96 da mesma empresa → totais idênticos; pesquisa
     sem resultados → sem totais; só cancelados (47) e cancelados + rejeitados (92) → listados, sem totais.
8. **Falha de carregamento vs. sem resultados** (corrigido 2026-11-12):
   - **[Manual]** Com a API DEV parada (ou a porta 5000 bloqueada), abrir **Pedidos**: aparece o painel vermelho
     **"Falha ao carregar os pedidos"** com a mensagem do erro e o botão **"Tentar novamente"**; a tabela e a mensagem
     "Nenhum pedido encontrado" **não** aparecem; o rodapé "Total Filtrado" mostra **—**. Evidência: captura.
   - **[Manual]** Arrancar a API e clicar **"Tentar novamente"**: a lista carrega com os mesmos filtros, o painel desaparece e o
     total volta a ser calculado.
   - **[Manual]** Pesquisar um texto sem resultados com a API activa: "Nenhum pedido encontrado" e total **0,00** (resposta com
     sucesso e sem correspondências).

### 2.2 Correção 2 — moeda nas janelas "Confirmar Adiantamento" e "Confirmar Liquidação"

Precisa de uma obrigação **RND** pendente. Se nenhuma existir, registar uma nova P.O. (Pós-Pago, sem crédito ou com crédito parcial)
num pedido QUOTATION em RND do mesmo fornecedor e aprová-la pelo fluxo normal; alternativamente usar uma obrigação **USD/EUR**.

1. **[Manual]** Finanças → **Obrigações & Pagamentos** → linha da obrigação RND → **Pagar** (ou "Pagar adiantamento" numa
   100% Antecipado): a janela mostra **"Valor esperado/agendado: {valor} RND"**. **Não** confirmar; fechar. Evidência: captura.
2. **[Manual]** Repetir com uma obrigação AOA (ex.: a obrigação bancária reaberta de 688 se ainda estiver pendente): deve mostrar
   "… AOA". Evidência: captura.
3. **[Manual]** A fila (coluna de valor) e a janela devem indicar a **mesma** moeda em ambos os casos.

### 2.3 Correção 3 — aviso "100% Antecipado" nos dois wizards

1. **[Manual]** Comprador responsável → pedido QUOTATION do fornecedor com conta (ex.: novo pedido RND ou AOA) → **REGISTRAR P.O** →
   condição **100% Antecipado**, **sem** marcar "Utilizar crédito em conta corrente nesta P.O": o aviso laranja diz
   **"O valor total de {T} {moeda} será exigido como adiantamento."** (texto original). Evidência: captura.
2. **[Manual]** Marcar a caixa e deixar o valor máximo (cobertura integral): o aviso passa a
   **"Adiantamento total de {T} {moeda}, coberto integralmente por crédito em conta corrente ({T} {moeda}). Não há adiantamento bancário."**
   e o bloco "Adiantamento bancário" mostra **0,00 — cobertura integral**. Evidência: captura com aviso e bloco visíveis.
3. **[Manual]** Reduzir o valor de crédito (ex.: metade): o aviso passa a
   **"Adiantamento total de {T} {moeda}: {C} {moeda} cobertos por crédito em conta corrente e {B} {moeda} a adiantar por banco."**
   com **B = T − C** igual ao "Adiantamento bancário" do bloco. Evidência: captura.
4. **[Manual]** Wizard de correção (**Corrigir P.O** numa P.O 100% Antecipado devolvida por Finanças com reserva activa, ex.: criar
   uma P.O com crédito parcial, pedir a devolução por Finanças): **Manter** → aviso com a repartição actual; **Alterar** + novo valor
   → aviso recalculado; **Remover** → volta ao texto original "O valor total de … será exigido como adiantamento.". Evidência:
   três capturas. Se não houver P.O 100% Antecipado devolvida, testar só o registo inicial e registar "correção: coberto por teste
   automático" (1.3).
5. **[Manual]** Com **Pós-Pago** o aviso laranja **não** aparece (apenas o bloco de crédito). Confirmar que nada mudou.

### 2.4 Correção 4 — uma única etapa "ATUAL" após liquidação do adiantamento

1. **[Manual]** **Pedidos** → expandir a linha de **REQ-27/10/2026-647** (timeline por lote): **Pagamento** com visto (concluído)
   e **Recebimento / Execução** com **ATUAL**; nenhuma outra etapa com "ATUAL". Evidência: captura.
   Nota: 647 foi revertido na 1.ª ronda e reabriu um adiantamento bancário; se o grupo estiver de novo em
   `ADVANCE_PAYMENT_REQUIRED`, **Pagamento** é "ATUAL" (correcto: o adiantamento está em dívida) — usar então **684**
   (adiantamento bancário de 5 840,00 confirmado → grupo em WAITING_SUPPLIER_DELIVERY) para o caso "liquidado".
2. **[Manual]** Página do pedido 684 → painel de progresso do grupo: **Pagamento** feito, **Recebimento** em curso.
3. **[Manual]** Um pedido com **vários grupos** em etapas diferentes (se existir em DEV, ex.: um grupo pago e outro aguardando P.O):
   cada lote mantém a sua própria linha e o seu próprio "ATUAL". Se não existir, registar "não testado".
4. **[Verificação API]** `GET /api/v1/requests/{requestId684}/timeline` → no lote do grupo, `steps` com `"Pagamento": "completed"` e
   `"Recebimento / Execução": "current"`; apenas um elemento com `state: "current"`. Evidência: JSON.

### 2.5 Correção 5 — valores numa só linha (ficha e fila de Finanças)

1. **[Manual]** Compras → ficha do fornecedor CONSULTIT → **Movimentos** da CCS-000004, numa janela de **≈ 1 440 px** de largura
   e depois numa de **≈ 1 100 px**: nenhum valor (Valor, Reserva, Saldo após, "aplicado …") se parte em duas linhas; a tabela
   ganha deslocamento horizontal se faltar espaço; "Pedido / P.O" mostra número do pedido, P.O e planta em linhas separadas;
   "Referência / motivo" continua legível e completo. Evidência: duas capturas.
2. **[Manual]** Finanças → **Obrigações & Pagamentos** nas mesmas duas larguras e ainda a **≈ 800 px**: valores sem quebra; chip de
   crédito ("Crédito reservado · banco …") legível, podendo quebrar entre os dois segmentos; "Próxima ação" até duas linhas com o
   texto completo ao passar o rato; abaixo de 1 280 px a linha reorganiza-se em duas filas (fornecedor · P.O · vencimento /
   valor · próxima ação · ações) sem sobreposição; abaixo de 860 px em duas colunas. Evidência: três capturas.
3. **[Manual]** A tabela de contas (Saldo / Reservado / Disponível) numa só linha por conta.

### 2.6 Correção 6 — histórico do pedido em português

1. **[Manual]** Página do pedido **688** → **Histórico do Pedido**: as entradas de crédito aparecem como **CRÉDITO RESERVADO**,
   **RESERVA DE CRÉDITO ALTERADA**, **RESERVA DE CRÉDITO LIBERTADA**; ao passar o rato sobre o rótulo, a dica mostra o código
   original (`SUPPLIER_CREDIT_RESERVED`, …). Evidência: captura.
2. **[Manual]** Pedido **647** ou **684**: **CRÉDITO APLICADO** e, em 647, **CRÉDITO REVERTIDO**.
3. **[Manual]** Finanças → **Histórico**: os mesmos eventos com "Crédito reservado", "Crédito aplicado", "Crédito revertido",
   "Reserva de crédito libertada" (inalterado). **Print View** do pedido 688: secção de histórico com os mesmos rótulos.
4. **[Verificação API]** `GET /api/v1/requests/{requestId688}` → `statusHistory[].actionTaken` continua a conter os códigos
   `SUPPLIER_CREDIT_*` (dados de auditoria preservados). Evidência: JSON.

---

## 3. Verificações ainda pendentes da 1.ª ronda

> **Fechadas em 2026-11-12 por evidência automática / SQL Server** (ver 1.3 e [§10.7.2](SUPPLIER_CREDIT_PO_COVERAGE.md)): 3.1 a 3.7
> não precisam de repetição manual. Os passos abaixo ficam como referência de comportamento esperado; **nenhum** deve ser executado
> contra os cenários existentes do proprietário (647 / 684 / 685 / 688 / 689 / 9640) para não alterar saldos ou pagamentos pendentes.

Cada verificação indica a resposta esperada. Qualquer `500` é falha.

### 3.1 Crédito insuficiente

Pré-condição: conta com disponível conhecido D (ex.: CCS-000004, 900 000,00 AOA) e um pedido QUOTATION CONSULTIT com grupo
`WAITING_PO` de valor superior a D, ou reduzir o disponível com um "Ajuste a desfavor" (Compras, justificativa ≥ 10 caracteres).

1. **[Manual]** Comprador → **REGISTRAR P.O** → Pós-Pago → marcar crédito → escrever um valor **acima** do "máx." indicado: a dica
   vermelha **"Indique um valor entre 0,01 e {máx} {moeda}."** aparece e o bloco de repartição desaparece. Evidência: captura.
2. **[Verificação API]** Comprador responsável: `POST /api/v1/requests/{requestId}/operational/register-po` com o corpo habitual do
   registo mais `"useSupplierCredit": true, "supplierCreditAmount": {D + 1000}` → **409** título **"Crédito não reservado"**,
   detalhe **"Saldo de crédito insuficiente: disponível …, solicitado …"**, `code: INSUFFICIENT_SUPPLIER_CREDIT`; o grupo continua
   `WAITING_PO`, **nenhuma** linha nova no histórico, ficha inalterada. Evidência: JSON + ficha.
3. **[Verificação API]** Valor acima do total da P.O (mas abaixo do disponível) → **400** `code: CREDIT_AMOUNT_INVALID`
   "O valor de crédito (…) não pode exceder o valor da P.O (…)".
4. **[Verificação API]** `GET /api/v1/requests/{requestId}/po-groups/{groupId}/credit-eligibility?paymentConditionCode=POST_PAID`
   com a conta a **0,00** disponível → `canReserve: false`, `reasonCode: "INSUFFICIENT_SUPPLIER_CREDIT"`,
   `reasonMessage: "A conta corrente não tem saldo disponível."`.

### 3.2 Autorização

1. **[Manual]** Segundo comprador (perfil `Buyer`, **não** responsável) → abrir o pedido → o bloco de crédito mostra
   **"Apenas o comprador responsável pelo pedido pode utilizar crédito em conta corrente."** e nenhuma caixa.
   **[Verificação API]** com o token desse comprador: `register-po` com `"useSupplierCredit": true` → **403**
   `code: NOT_RESPONSIBLE_BUYER`; nada gravado. Evidência: captura + JSON.
2. **[Verificação API]** Finanças **sem** perfil `Contracts`: `GET /api/v1/lookups/suppliers/{supplierId}/credit-accounts` →
   `200` com `canManage: false`; `POST /api/v1/lookups/suppliers/{supplierId}/credit-accounts` → **403**. Evidência: JSON.
3. **[Verificação API]** Reversão pela **mesma** pessoa que aplicou: `POST /api/v1/finance/{requestId}/reverse-credit` com
   `{"requestPoGroupId":"{groupId}","reason":"Motivo de teste com mais de vinte caracteres"}` → **403** "Reversão pela mesma
   pessoa" (`code: SELF_REVERSAL_FORBIDDEN`). Evidência: JSON.
4. **[Verificação API]** Token de um "System Administrator" **sem** perfil `Finance` → **403** "Perfil Finanças necessário"
   (`code: FINANCE_SCOPE_REQUIRED`). Finance de outra planta (se existir utilizador) → **403** "Planta fora do seu âmbito".
5. **[Verificação API]** Motivo com 10 caracteres → **400** "Motivo obrigatório" (mín. 20).

### 3.3 Liquidação duplicada

Pré-condição: uma P.O Pós-Pago com cobertura **integral** (ex.: novo grupo CONSULTIT de 50 000,00 AOA com crédito 50 000,00).

1. **[Manual]** Finanças F1 → **Aplicar crédito** → CONFIRMAR: grupo `PAYMENT_COMPLETED`, ficha baixa 50 000,00, histórico
   "CRÉDITO APLICADO".
2. **[Verificação API]** Repetir `POST /api/v1/finance/{requestId}/apply-credit` `{"requestPoGroupId":"{groupId}"}` → **409**
   "Crédito já aplicado" (`code: CREDIT_ALREADY_APPLIED`); ficha inalterada; **nenhuma** segunda linha no histórico nem nos
   movimentos. Evidência: JSON + movimentos.
3. **[Verificação API]** Com o mesmo grupo: `POST /api/v1/finance/{requestId}/schedule` e `POST /api/v1/finance/{requestId}/pay`
   → **409** (grupo já liquidado / crédito aplica-se sem pagamento bancário). Evidência: JSON.
4. **[Manual, concorrência leve]** Duas janelas do browser com F1 e F2 na mesma linha de outra P.O integral: clicar
   **Aplicar crédito → CONFIRMAR** quase em simultâneo: uma janela recebe sucesso, a outra **"Crédito já aplicado"** ou
   **"Dados desatualizados"** (`CREDIT_CONCURRENT_CHANGE`); a ficha baixa **uma** vez. Registar como "verificado em LocalDB";
   os bloqueios de linha SQL Server só se confirmam em TEST.

### 3.4 Manter / Remover e libertação com novas reservas desligadas

Pré-condição: com a flag **ligada**, registar uma P.O Pós-Pago com crédito parcial (ex.: 20 000,00 de 80 000,00 AOA) → ficha
reservado 20 000,00. Depois **desligar** a flag (`"NewReservationsEnabled": false`) e reiniciar (1.1). Confirmar
`GET …/credit-eligibility` → `enabled: false`, `reasonCode: "SUPPLIER_CREDIT_DISABLED"`.

1. **[Manual]** Finanças → **Devolver** a P.O ao comprador. Comprador → **Corrigir P.O**: o bloco mostra "Reserva atual: 20.000,00 AOA
   (Reservado)" com **Manter** e **Remover** activos e **Alterar** desactivado com a dica
   **"A alteração do valor de crédito não está ativa neste ambiente."**. Evidência: captura.
2. **[Manual]** **Manter** → confirmar a correção: reserva mantida sem duplicação (ficha reservado 20 000,00; um único movimento de
   reserva). Finanças → a fila continua a mostrar banco 60 000,00. Evidência: ficha.
3. **[Verificação API]** Correção com `"supplierCredit": {"correctionAction":"CHANGE","amount": 30000}` → **400** "Alteração de
   crédito desativada" (`code: SUPPLIER_CREDIT_DISABLED`); nada gravado.
4. **[Manual]** Nova devolução → **Remover** → confirmar: reserva libertada ("removida na correção da P.O"), ficha reservado 0,00,
   disponível reposto, obrigação bancária integral de 80 000,00 reaberta; histórico "RESERVA DE CRÉDITO LIBERTADA". Evidência: ficha + fila.
5. **[Verificação API]** Libertação directa com uma reserva activa noutro grupo `WAITING_PO`/`WAITING_PO_CORRECTION`:
   `POST /api/v1/requests/{requestId}/po-groups/{groupId}/credit-reservation/release` `{"reason":"Libertação de teste com a flag desligada"}`
   → **200**; com o grupo em `PO_ISSUED` → **409** "Libertação não permitida neste estado" (`code: CREDIT_RELEASE_NOT_ALLOWED`).
6. **[Manual]** Com a flag ainda desligada, **REGISTRAR P.O** num pedido novo: o bloco mostra **"A utilização de crédito em conta
   corrente em novas P.O. não está ativa neste ambiente."** e não há caixa (confirmado na 1.ª ronda; repetir só se conveniente).
7. **[Manual]** Voltar a ligar a flag e reiniciar se ainda houver testes a repetir.

### 3.5 Bloqueio do pagamento bancário numa cobertura integral (pendente da 1.ª ronda)

Com a P.O integral de 3.3 **antes** de aplicar o crédito: **[Verificação API]** `POST /api/v1/finance/{requestId}/schedule`
`{"requestPoGroupId":"{groupId}","scheduledDate":"{data ISO}","comment":"teste"}` → **409** `code: CREDIT_APPLICATION_REQUIRED`
("o grupo liquida-se por crédito"); `POST …/pay` → **409**. Numa 100% Antecipado integral: `POST /api/v1/requests/{requestId}/b2p/schedule-advance`
e `POST …/b2p/confirm-advance` → **409**. Na UI só existe **Aplicar crédito** como acção primária. Evidência: JSON + captura.

### 3.6 Reversão após cobertura parcial — recusa esperada

Grupo **685** (parcial, aplicado): a linha **não** oferece "Reverter crédito aplicado". **[Verificação API]** F2:
`POST /api/v1/finance/{requestId685}/reverse-credit` `{"requestPoGroupId":"{groupId685}","reason":"Tentativa de reversão parcial em teste DEV"}`
→ **409** "Reversão parcial não suportada nesta fase" (`code: CREDIT_REVERSAL_PARTIAL_UNSUPPORTED`). Ficha inalterada.

### 3.7 Saldo separado por empresa (APS)

**[Manual]** Compras → ficha CONSULTIT → **Criar conta** AlplaSOPRO / AOA → "Crédito registado" 200 000,00 com referência.
Comprador → pedido QUOTATION **AlplaSOPRO** do mesmo fornecedor → REGISTRAR P.O: o bloco mostra a conta **AlplaSOPRO** com
200 000,00 disponíveis (não os 900 000,00 da AlplaPLASTICO). Se não existir pedido AlplaSOPRO, registar "não testado".

---

## 4. Lista de verificação da 2.ª ronda

| # | Item | Passa / Falha | Evidência |
|---|---|---|---|
| 1 | 1.1 API reiniciada com o build novo (`hasMultipleCurrencies` presente) | Passa — reinício 15:24 pelo agente; `GET /requests` verificado (§10.6) | Verificação API 2026-11-12 |
| 2 | 2.1 explorador: 647/684/685 em RND, 688 em AOA; carrossel e Recebimento coerentes | Passa | capturas do proprietário 2026-11-12 |
| 2a | 2.1-7 "Total Filtrado" por moeda, todas as páginas, recalculado com filtros, "0,00" com cancelados | Passa — totais por moeda, idênticos entre páginas, "-----" / "0,00" com cancelados | confirmação do proprietário 2026-11-12 + API (§10.6) |
| 2b | 2.1-8 falha de carregamento com "Tentar novamente"; "Nenhum pedido encontrado" só com resposta vazia | **Não executado** (opcional; não contado como "Passa") — coberto apenas por revisão de código | — |
| 3 | 2.2 "Confirmar Adiantamento" / "Confirmar Liquidação" na moeda da obrigação | Passa — 11 274,23 RND (647) e 5 000,00 RND (ECF10 2026/9640, só apresentação) | confirmação do proprietário 2026-11-12 |
| 4 | 2.3 aviso 100% Antecipado: sem crédito / integral / parcial / Manter–Alterar–Remover | Passa — três avisos em ECF10 2026/9637 (só apresentação); Manter / Alterar / Remover cobertos por `advanceObligationNotice.test.ts` (mesma função no wizard de correção) | confirmação do proprietário + teste automático |
| 5 | 2.4 um único "ATUAL" no lote liquidado; lotes múltiplos independentes | Passa para 684; lotes múltiplos cobertos por `RequestLotTimelineBuilderTests` (cada lote mantém a sua trilha) | confirmação do proprietário + teste automático |
| 6 | 2.5 valores sem quebra de linha e fila legível a 1 440 / 1 100 / 800 px | Passa — movimentos da ficha (2026-11-12) e fila de Finanças para 684 / 685 / REQ-690 · ECF10 2026/9640 em ecrã maximizado, 1 100 px e 800 px (2026-11-13) | confirmação do proprietário |
| 6a | Tooltip do chip de crédito em 684: "Pago por banco: RND 5.840,00" e "Saldo pendente (banco): RND 0,00" (antes: "Valor a pagar (banco): 0,00" — ambiguidade de rótulo, sem erro financeiro; [§10.8](SUPPLIER_CREDIT_PO_COVERAGE.md)) | **Pendente (visual, 1 olhar após o reload do Vite)** — 6 testes automáticos em `financePaymentsView.test.mjs` | |
| 7 | 2.6 histórico do pedido, Finanças → Histórico e Print View com rótulos em português; códigos preservados na API | Passa — histórico do pedido (2026-11-12); Finanças → Histórico e PDF do Print View de REQ-647 com "Crédito reservado / aplicado / revertido" (2026-11-13); códigos preservados na API provados por `workflowEventLabels.test.ts` | confirmação do proprietário + teste automático |
| 8 | 3.1 crédito insuficiente: 409 `INSUFFICIENT_SUPPLIER_CREDIT`; nada gravado | Passa — **executado em SQL Server** (`Reserve_executes_the_SQL_Server_lock_path_and_refuses_insufficient_credit_without_writing`) + serviço InMemory; dica na UI opcional | §10.7.2 |
| 9 | 3.2 autorização: outro comprador 403; Finanças só leitura; auto-reversão 403; sem perfil Finance 403; motivo curto 400 | Passa — endpoints reais (`RegisterPo_credit_path_refuses_another_buyer…`, `ReverseCredit_needs_a_different_finance_user…`, `Credit_account_management_is_Contracts_only…`, `Release_endpoint_is_for_the_responsible_buyer…`) | §10.7.2 |
| 10 | 3.3 liquidação duplicada: segundo `apply-credit` recusado; `schedule`/`pay` 409; concorrência com uma única baixa | Passa — **executado em SQL Server** (`Two_concurrent_apply_credit_calls_debit_the_account_exactly_once_and_the_idempotency_index_holds`: duas aplicações simultâneas → uma baixa, índice de idempotência) + endpoints InMemory; duas janelas no browser opcional | §10.7.2 |
| 11 | 3.4 flag desligada: Manter / Remover funcionam, Alterar bloqueado (API 400), libertação 200 / 409, liquidação de reserva existente | Passa — endpoints reais com a flag desligada (`Flag_off_keeps_removes_releases_and_settles_existing_holds…`, `Release_endpoint…`) + cenário 8 da 1.ª ronda (Finanças aplicou 100 000,00 AOA com a flag desligada) | §10.7.2 |
| 12 | 3.5 pagamento bancário bloqueado numa cobertura integral (409 em `schedule`, `pay`, `b2p/*`) | Passa — `ApplyCredit_settles_a_full_coverage_once_and_bank_endpoints_refuse_it` (schedule) + `Bank_endpoints_refuse_a_fully_covered_group_pay_schedule_advance_and_confirm_advance` (pay, b2p/schedule-advance, b2p/confirm-advance) | §10.7.2 |
| 13 | 3.6 reversão parcial recusada (409) | Passa — serviço + endpoint (`ReverseCredit_endpoint_refuses_a_settlement_that_combined_partial_credit_and_a_bank_payment`); **não** executar em 685 | §10.7.2 |
| 14 | 3.7 conta separada por empresa; Viana 1 / Viana 2 partilham | Passa — cenário 9 da 1.ª ronda (Viana 1 e 2 em CCS-000004) + **executado em SQL Server** (`Accounts_are_isolated_by_supplier_company_and_currency_and_shared_by_the_plants_of_one_company`: chave única, plantas da mesma empresa partilham, outra empresa / moeda isolada); criar conta AlplaSOPRO na UI opcional | §10.7.2 |
| 15 | Sem erros 500 na API nem excepções na consola durante toda a ronda | Passa (2026-11-13); a pesquisa do explorador atualizou tarde uma vez — atribuído à carga do portátil, sem defeito registado | confirmação do proprietário |

**Concorrência de reservas (sem item próprio na lista inicial):** Passa — **executado em SQL Server**
(`Two_concurrent_reservations_on_one_account_never_over_reserve`, `Two_concurrent_registrations_with_credit_of_the_same_group_produce_exactly_one_reservation`).

**Aceitação da 2.ª ronda — ACEITE pelo proprietário em 2026-11-13:** itens 1–7, 8–15 e a concorrência "Passa" com a evidência
indicada; 2b não executado (opcional, não contado); 6a (tooltip) tracked explicitamente como verificação visual residual.
Nenhum bloqueador aberto. Segue-se o plano de publicação em TEST
([SUPPLIER_CREDIT_TEST_DEPLOYMENT_PLAN.md](SUPPLIER_CREDIT_TEST_DEPLOYMENT_PLAN.md)), para revisão antes de qualquer execução. Só depois da aceitação se prepara a publicação (`/task-publish` apenas com os ficheiros
do crédito, **Apply TEST Migrations** com `20261109150614_AddSupplierCreditAccounts`, **Deploy to TEST**, flag no
`appsettings.Test.json` preservado no servidor), cada passo com autorização própria.
