# Guia de Migrações de Banco de Dados — Julho de 2026

Este documento mapeia as 6 migrações de banco de dados do Entity Framework Core criadas para suportar as novas funcionalidades de compras, cotações, rateios e aprovação em lote.

---

## 1. 20260702073857_AddRequestPoGroup

* **Objetivo**: Adicionar suporte para o agrupamento de pedidos de compra aprovados em Ordens de Compra (P.O) consolidadas por fornecedor, moeda e condições de pagamento.
* **Tabelas & Colunas Afetadas**:
  * **[NEW] `RequestPoGroups`**:
    * `Id` (Uniqueidentifier, PK)
    * `SupplierId` (Int, FK para `Suppliers`)
    * `PoNumber` (Nvarchar(max))
    * `Currency` (Nvarchar(max))
    * `Status` (Int)
    * `CreatedAt` (Datetime2)
  * **[MODIFY] `Requests`**:
    * `RequestPoGroupId` (Uniqueidentifier, Nullable FK para `RequestPoGroups`)
* **Módulo Relacionado**: Compras / Emissão de P.O
* **Impacto**: Permite que múltiplos itens aprovados sejam agrupados sob a mesma P.O física no PRIMAVERA.
* **Riscos de Deploy**: Baixo. A nova coluna em `Requests` é nullable, não quebrando registros existentes.
* **Validação pós-migration**: Verificar se novos pedidos aprovados conseguem associar seu `RequestPoGroupId` corretamente.

---

## 2. 20260702090830_AddMappedRequestLineItemIdToQuotationItems

* **Objetivo**: Criar o mapeamento direto entre itens de cotação preenchidos pelo comprador e os itens originais da solicitação de compra.
* **Tabelas & Colunas Afetadas**:
  * **[MODIFY] `QuotationItems`**:
    * `MappedRequestLineItemId` (Uniqueidentifier, Nullable FK para `RequestLineItems`)
* **Módulo Relacionado**: Gestão de Cotações (Lista de Itens do Comprador)
* **Impacto**: Permite rastrear exatamente qual cotação de fornecedor atende a qual linha específica da requisição.
* **Riscos de Deploy**: Baixo. A coluna é nullable.
* **Validação pós-migration**: Testar a submissão de cotações parciais e verificar se o mapeamento persiste em banco.

---

## 3. 20260702133925_AddAdvancePaymentScheduledStatus

* **Objetivo**: Adicionar o novo status de fluxo de trabalho `ADVANCE_PAYMENT_SCHEDULED` (Pagamento Adiantado Programado) na tabela de estados de pedidos.
* **Tabelas & Colunas Afetadas**:
  * **[MODIFY] `RequestStatuses`**: Inserção de registro estático na tabela para o código correspondente (`ADVANCE_PAYMENT_SCHEDULED`).
* **Módulo Relacionado**: Financeiro / Pagamentos
* **Impacto**: Permite reter a entrega física do fornecedor até que a prova do pagamento adiantado seja carregada e processada.
* **Riscos de Deploy**: Mínimo. Apenas adição de registro em tabela de lookups/status.
* **Validação pós-migration**: Verificar se as telas de fila financeira exibem corretamente o novo status.

---

## 4. 20260707200549_AddRequestLineItemAllocations

* **Objetivo**: Permitir o rateio de custos de itens individuais de pedidos de compra entre múltiplos Centros de Custo.
* **Tabelas & Colunas Afetadas**:
  * **[NEW] `RequestLineItemAllocations`**:
    * `Id` (Uniqueidentifier, PK)
    * `RequestLineItemId` (Uniqueidentifier, FK para `RequestLineItems`)
    * `CostCenterId` (Int, FK para `CostCenters`)
    * `Percentage` (Decimal(18,2))
    * `Amount` (Decimal(18,2))
* **Módulo Relacionado**: Orçamento e Rateio de Centros de Custo (Batch Wizard)
* **Impacto**: O sistema passa de um Centro de Custo único por Pedido para múltiplos Centros de Custo por Item.
* **Riscos de Deploy**: Médio. Operações antigas que liam o Centro de Custo do cabeçalho do pedido devem continuar funcionando devido à retrocompatibilidade das colunas originais (não excluídas).
* **Validação pós-migration**: Executar teste de gravação de rateio de item em lote e validar as somas decimais de porcentagens e valores.

---

## 5. 20260708190647_PhaseR1ReconciliationPersistence

* **Objetivo**: Dar suporte à gravação e persistência de dados extraídos por OCR em faturas fiscais para fins de auditoria e reconciliação financeira (fatura física vs aprovado).
* **Tabelas & Colunas Afetadas**:
  * **[MODIFY] `RequestReconciliations`**:
    * `OcrDataRaw` (Nvarchar(max)) — Armazenamento do payload JSON completo retornado pela IA.
    * `ReconciledAmount` (Decimal(18,2))
    * `MatchStatus` (Int)
* **Módulo Relacionado**: Contas a Pagar / Conciliação OCR de Faturas
* **Impacto**: Permite guardar o histórico de tentativas de reconciliação de forma persistente em banco.
* **Riscos de Deploy**: Baixo. Apenas novos campos de auditoria.
* **Validação pós-migration**: Testar o processamento de uma fatura e verificar se o JSON OCR é salvo no campo `OcrDataRaw`.

---

## 6. 20260710212404_AddApprovalBatchModel

* **Objetivo**: Criar a estrutura física para armazenamento de Lotes de Aprovação contendo múltiplos pedidos agrupados para aprovação em lote.
* **Tabelas & Colunas Afetadas**:
  * **[NEW] `ApprovalBatches`**:
    * `Id` (Uniqueidentifier, PK)
    * `ApproverId` (Int, FK para `Users`)
    * `BatchCode` (Nvarchar(max)) — Ex: `LOT-202607-000001`
    * `Status` (Int)
    * `CreatedAt` (Datetime2)
  * **[NEW] `ApprovalBatchItems`**:
    * `Id` (Uniqueidentifier, PK)
    * `ApprovalBatchId` (Uniqueidentifier, FK para `ApprovalBatches`)
    * `RequestLineItemId` (Uniqueidentifier, FK para `RequestLineItems`)
    * `Status` (Int)
  * **[NEW] `ApprovalBatchExtraItemDecisions`**:
    * `Id` (Uniqueidentifier, PK)
    * `ApprovalBatchId` (Uniqueidentifier, FK para `ApprovalBatches`)
    * `RequestLineItemId` (Uniqueidentifier, FK para `RequestLineItems`)
    * `Decision` (Int)
    * `Notes` (Nvarchar(max))
* **Módulo Relacionado**: Fluxo de Aprovação em Lote (Batch Wizard)
* **Impacto**: O sistema passa a gerenciar lotes de forma transacional persistida, facilitando a auditoria de quem aprovou o lote de cotações.
* **Riscos de Deploy**: Médio. Exige validação de integridade referencial nas chaves estrangeiras com `RequestLineItems`.
* **Validação pós-migration**: Criar um lote contendo 3 itens e verificar se os registros correspondentes são gerados nas tabelas `ApprovalBatches` e `ApprovalBatchItems`.
