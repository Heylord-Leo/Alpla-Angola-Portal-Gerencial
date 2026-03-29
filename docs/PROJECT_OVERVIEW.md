# Visão Geral do Projeto Alpla Gerencial

## Objetivo

Portal interno para gestão de Pedidos de Compras e Pagamentos da Alpla Angola. O sistema digitaliza o fluxo completo: criação de pedidos, aprovações multi-nível, controle financeiro e rastreabilidade de auditoria.

## Escopo

- **Funcionalidades Implementadas (V1.1)**:
  - Pedidos de compra (Cotação) e pagamento (Pagamento) com fluxo de aprovação multi-etapa.
  - Gestão de Cotações (itens de pedido, fornecedores e centros de custo).
  - Upload de anexos (Proforma, P.O, Comprovantes) integrado ao workflow.
  - OCR para extração automática de dados de faturas proformas.
  - Dados Mestres gerenciáveis: Unidades, Moedas, Graus de Necessidade, Departamentos, Plantas, Fornecedores, Centros de Custo.
  - Dashboard operacional com resumo de status e guia interativo.
- **Limitações (V1.1)**: Sem integração bidirecional em tempo real com Primavera/AlplaPROD (apenas snapshots).

## Estrutura de Pastas

- `docs/` – Documentação do projeto
- `src/frontend/` – SPA React (Vite + TypeScript)
- `src/backend/` – ASP.NET Core 8 Web API + EF Core
- `tests/` – Testes automatizados (Unitários e de Integração)

## Próximos Passos

1. Estágio 10 Concluído: Introdução do Workspace de Recebimento.
2. Iniciar Estágio 11: Notificações via e-mail/Teams.
3. Dashboards analíticos avançados.

- **Versão Atual**: v1.1.41 (Stage 10 - Workspace de Recebimento)
- **Status**: Estável (Workspace de Recebimento e Role RECEIVING Implementados)
