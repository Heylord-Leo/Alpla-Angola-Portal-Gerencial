# Fluxo de Aprovação em Lote (Approval Batch Wizard)

Este documento descreve o funcionamento funcional e técnico do novo módulo de aprovação em lote (Batch Wizard) do Portal Gerencial da ALPLA Angola.

---

## 1. Objetivo do Módulo

O objetivo principal do **Approval Batch Wizard** é permitir que compradores consolidem múltiplos pedidos de compra de um ou mais solicitantes em lotes de aprovação estruturados, e que os aprovadores (de Área ou Finais) comparem cotações, realizem rateios por Centro de Custo (CC) e adjudiquem os itens em lote através de um assistente passo-a-passo.

Isso elimina o processo manual e repetitivo de abrir individualmente cada pedido, agilizando as rodadas de negociação e decisão financeira.

---

## 2. Ponto de Entrada na Interface

O assistente é iniciado a partir da tela de **Central de Aprovações**:
👉 **URL Local**: [http://localhost:5173/approvals](http://localhost:5173/approvals)

Ao selecionar múltiplos pedidos elegíveis do comprador, o aprovador clica no botão "Aprovação em Lote" para abrir o modal unificado.

---

## 3. Arquitetura de Componentes e Arquivos

### Componentes de Interface (Frontend)
- **[ApprovalWizardModal.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/ApprovalWizardModal.tsx)**: Modal principal que orquestra o estado do assistente e renderiza os passos.
- **[WizardStepOverview.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepOverview.tsx)**: Tela inicial com resumo dos pedidos selecionados, solicitantes, valores totais e o gráfico de tendência ("Contexto Financeiro Visual").
- **[WizardStepSelection.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepSelection.tsx)**: Confirmação e refinamento dos itens do lote.
- **[WizardStepComparison.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepComparison.tsx)**: Tela de comparação técnica e comercial das cotações recebidas de diferentes fornecedores.
- **[WizardStepAllocation.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepAllocation.tsx)**: Interface de rateio de Centros de Custo (CC) por item.
- **[WizardStepBudget.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepBudget.tsx)**: Validação e pré-visualização de impacto orçamentário real por CC.
- **[WizardStepAward.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepAward.tsx)**: Seleção dos fornecedores vencedores (adjudicação) por item.
- **[WizardStepReview.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/WizardStepReview.tsx)**: Revisão final das escolhas e submissão da decisão em lote.
- **[AwardSummary.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/components/AwardSummary.tsx)** & **[ItemAwardMatrix.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/Approvals/components/ItemAwardMatrix.tsx)**: Grid visual de cotações para apoio na adjudicação.

### Entidades do Banco de Dados (Backend)
- **`ApprovalBatch`**: Cabeçalho do lote contendo o aprovador responsável, status, data de criação e metadados.
- **`ApprovalBatchItem`**: Tabela associativa que vincula itens de pedidos de compra (`RequestLineItem`) ao lote correspondente.
- **`ApprovalBatchExtraItemDecision`**: Persiste decisões adicionais tomadas sobre itens que não possuíam cotações anexas no momento da criação do lote.
- **`RequestLineItemAllocation`**: Persiste os rateios de Centro de Custo definidos para cada item de pedido.
- **`RequestPoGroup`**: Agrupa pedidos aprovados com o mesmo fornecedor e moeda para fins de emissão de P.O unificada.

### DTOs de Integração (API)
- **`ApprovalBatchDtos`**: Contratos de criação de lote, carregamento do assistente e envio da decisão final.
- **`BudgetPreviewDtos`**: Estruturas de retorno da pré-visualização de saldo orçamentário consolidado por CC dos itens rateados.

---

## 4. Fluxo de Execução Etapa por Etapa

```
[Overview] ➔ [Selection] ➔ [Comparison] ➔ [Allocation] ➔ [Budget Check] ➔ [Awarding] ➔ [Final Review]
```

1. **Visão Geral (Overview)**: O aprovador vê a volumetria financeira acumulada do lote e o gráfico de tendência nos filtros de período selecionados (Dias, Semanas, Meses).
2. **Seleção de Itens (Selection)**: Permite desmarcar itens específicos que necessitem de mais análises antes de prosseguir com o lote.
3. **Comparação de Cotações (Comparison)**: Exibe lado a lado os preços, prazos de entrega e condições de pagamento propostos por cada fornecedor para os itens cotados.
4. **Rateio de Custos (Allocation)**: O aprovador pode dividir o custo de qualquer item entre múltiplos Centros de Custo definindo percentuais (ex: 50% Planta Viana, 50% Escritório Central) ou valores nominais diretos.
5. **Pré-visualização do Orçamento (Budget)**: O sistema simula o rateio e consome o saldo orçamentário dos Centros de Custo envolvidos, exibindo alertas visuais caso algum CC ultrapasse o limite aprovado.
6. **Adjudicação (Award)**: O aprovador escolhe formalmente qual proposta fornecedora será vencedora para cada item do lote.
7. **Revisão e Envio (Review)**: Consolida todas as cotações vencedoras, rateios de CC e valores finais em uma transação única submetida ao backend.

---

## 5. Lógica de Negócios Específicas

### Rateio por Centro de Custo (Allocations)
O rateio pode ser feito de duas formas no componente:
- **Percentual**: O aprovador insere o percentual desejado para cada CC, e o sistema auto-calcula o valor monetário. A soma dos percentuais deve totalizar exatamente **100%**.
- **Valor Nominal**: O aprovador insere o valor monetário exato para cada CC. O sistema calcula a equivalência percentual e valida se a soma dos valores corresponde exatamente ao `ApprovedAmount` do item.

No backend, a classe `AllocationHelper.cs` executa validações rigorosas de ponto flutuante para evitar perdas ou ganhos de centavos por arredondamento durante a conversão.

### Análise Orçamentária no Wizard
A validação orçamentária é preditiva:
- O banco calcula o orçamento disponível original para o Centro de Custo no mês atual.
- Subtrai o valor já pré-comprometido em outros pedidos pendentes de aprovação.
- Simula o impacto do lote atual com base nas porcentagens configuradas na etapa de rateio.
- Exibe o status consolidado: **Dentro do Limite** (Verde), **Atenção/Próximo do Limite** (Laranja) ou **Orçamento Estourado** (Vermelho).

### Adjudicação e Aprovação Parcial
Se o aprovador decidir aprovar apenas uma parte dos itens contidos no lote (adjudicando fornecedores apenas para os itens prioritários), o lote é fechado como **Aprovado Parcialmente**.
Os itens não aprovados/rejeitados retornam para a fila do comprador como "Elegíveis para novo Lote" ou "Para nova rodada de Cotação", garantindo que a esteira operacional não pare.

---

## 6. Riscos Técnicos e Mitigações

* **Concorrência na Modificação de Pedidos**: Dois aprovadores tentando processar lotes que contêm o mesmo item de pedido.
  * *Mitigação*: Implementação de bloqueio pessimista ou verificação de versão no banco ao gravar o lote. Se um item já foi aprovado em outro escopo, a transação do lote é abortada emitindo código `REQUEST_ALREADY_PROCESSED`.
* **Arredondamento Centesimal no Rateio**: Rateios complexos (ex: dividir $100.00 por 3 centros de custo, resultando em dízima periódica).
  * *Mitigação*: O backend ajusta a diferença de arredondamento no último Centro de Custo cadastrado no rateio para garantir que o somatório final bata exatamente com o valor total aprovado.

---

## 7. Roteiro de Testes Manuais Recomendado

1. **Criação de Lote Múltiplo**: Selecione 3 pedidos de diferentes departamentos, crie o lote e valide se o assistente é aberto carregando os dados de todos eles na tela inicial.
2. **Teste de Gráfico de Tendência (Dias)**: Na tela inicial do Wizard, selecione o botão "Dias" e verifique se a linha conecta corretamente todos os pontos diários de dados sem flicker ou oscilação ao redimensionar o modal.
3. **Validação de Rateio (100%)**: Na tela de rateio, tente submeter um item com rateio somando 99% ou 101%. O frontend deve exibir erro impeditivo de validação.
4. **Impacto Orçamentário com Rateio**: Crie um rateio que empurre um Centro de Custo específico para além do limite disponível e verifique se a tela de orçamento exibe o alerta visual correspondente em vermelho.
5. **Adjudicação Mista**: Selecione fornecedor "Fornecedor A" para o item 1 e "Fornecedor B" para o item 2, envie o lote e verifique no banco se foram gerados `RequestPoGroups` separados por fornecedor.
