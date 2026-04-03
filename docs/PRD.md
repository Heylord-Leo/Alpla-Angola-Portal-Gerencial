# Product Requirements Document (PRD) - Alpla Angola Gerencial

## 1. Visão Geral do Projeto

O Alpla Angola Gerencial é um portal interno desenvolvido para os colaboradores da Alpla Angola. O módulo inicial (V1) é focado em "Solicitações de Compra e Pagamentos" (Purchase Requests & Payments). Seu objetivo principal é fornecer um ambiente automatizado e organizado usando a arquitetura de agentes **DOE** (Directives, Orchestration, Execution), separando intenção, decisão e execução em componentes distintos.

## 2. Objetivos de Negócio (V1)

- **Padronizar** o processo de solicitação e aprovação.
- **Melhorar a visibilidade** sobre o status de uma requisição e seu responsável atual.
- **Reduzir solicitações informais** (atualmente conduzidas via e-mail e WhatsApp).
- **Criar rastreabilidade** (quem fez o quê, quando e por quê), através de trilhas de auditoria transparentes.
- Preparar uma **base sólida e escalável** para a inclusão de módulos futuros (Manutenção, Orçamentos, Férias).
- Estabelecer as bases (via camada API) para uma integração futura com os sistemas *AlplaPROD* e *Primavera*.

## 3. Escopo (Versão 1.0)

### 3.1 O que está no escopo (In Scope)

- Criação e edição de solicitações de compra/pagamento.
- Opção de salvar requisição como **Rascunho** (Draft).
- Submissão da requisição para aprovação.
- Fluxo de aprovação básico (mínimo de uma camada, inicialmente através do Aprovador de Área).
- Ações pontuais de "Aprovar" e "Rejeitar" (sendo obrigatório o comentário na rejeição).
- Acompanhamento do status da solicitação pelo usuário.
- Upload, download e listagem de anexos por solicitação.
- Página detalhada da solicitação com histórico cronológico/linha do tempo.
- Controle de acesso baseado em papéis (RBAC - Role-Based Access Control) que habilita ou oculta as opções da interface.
- Listagens de ações ("Minhas Solicitações", "Aprovações Pendentes") com recursos básicos de pesquisa e filtragem.
- Sistema básico de notificações (por e-mail ou in-app).
- Trilha de Auditoria detalhando a data/hora e ação de cada evento.

### 3.2 O que NÃO está no escopo (Out of Scope)

- Integração completa com API do AlplaPROD ou Primavera.
- Criação automática de "Purchase Orders" (PO) no ERP Primavera.
- Validação avançada de orçamento consultando a base de dados ao vivo (ERP).
- Experiência otimizada "Mobile-First" (a V1 focará primariamente num contexto de desktop/escritório).
- Dashboards analíticos ou Business Intelligence avançados.
- UI multilíngue (A versão primária e única da V1 será em **Português**, adotando um modelo de nomenclatura robusto).

## 4. Perfis de Usuário e Acesso

O sistema V1 contempla 5 perfis de uso práticos:

1. **Solicitante (Requester):** Cria requisições, pode salvar em formato de rascunho, altera quando em status de ajuste/rejeitado, submete o pedido final e monitora o andamento de suas próprias solicitações.
2. **Aprovador de Área (Area Approver):** Revisa requisições em sua fila que estejam no status de pendente, aprovando ou rejeitando-as.
3. **Comprador (Purchaser/Procurement):** Assume pedidos aprovados que requerem orçamentos, finalizando as etapas de compras e inserindo os dados pertinentes.
4. **Financeiro (Finance):** Visualiza e interage com pedidos relacionados diretamente ao setor de pagamentos.
5. **Administrador do Sistema (System Admin):** Atua com privilégios master, lidando com mapeamento de usuários, parametrizações sistêmicas (lista de departamentos, categorias) e ações de suporte.

## 5. Fluxos de Trabalho / Workflow

### 5.1 Fluxo de Alto Nível

1. O *Solicitante* cria uma requisição e opta por salvá-la em Rascunho ou a Submete diretamente.
2. Com a submissão, a requisição trafega para o *Aprovador de Área* de referência.
3. O *Aprovador* inspeciona a demanda e a Aprova ou Rejeita.
4. **Se rejeitada:** A requisição desce para um estado de devolução (retrabalho), podendo ir para o solicitante original resolver ou para o comprador (dependendo de regras processuais preestabelecidas). O comentário deve orientar o conserto.
5. **Se aprovada:** O sistema faz com que a requisição siga para a esteira do status operacional subsequente (para a área de Compras e/ou Financeiro, a depender do seu propósito).
6. **Contínuo:** Todas e quaisquer tratativas no caminho deixam marcas em um registro de log (Audit Trail), garantindo clareza do processamento.

### 5.2 Estrutura de Status Principal

- `Draft` (Rascunho)
- `Pending Approval` (Aprovação Pendente)
- `Rejected` (Devolvido para ajuste)
- `Approved` (Aprovado)
- `In Procurement` (Opcional na V1 - em negociação/compras)
- `Waiting Payment` (Opcional na V1 - aguardando pagamento)
- `Paid` (Opcional na V1 - pago)
- `Cancelled` (Cancelado por ação autoral caso elegível)

## 6. Telas Mínimas Necessárias

1. **Dashboard / Minhas Solicitações (Listagem):** Grade com status em formato de "badges", filtros por tipo, status e período, e ações rápidas no nível da linha.
2. **Fila de Aprovações Pendentes (Listagem):** Quadro centrado em permitir visibilidade total das ações restritas à revisão imediata, abrigando atalhos para os próximos passos rápidos.
3. **Formulário de Pedido/Edição:** Layout limpo e dividido por seções/cartões. Indicação visual estrita de requesitos de campo e seção de anexos intuitiva.
4. **Resumo da Solicitação (Detail Page):** A central da rastreabilidade da solicitação. Layout em formato Split/Duplo ou linha do tempo vertical exibindo transições, histórico e ações de aprovação acessível para botões de macro decisão.

## 7. Requisitos Não Funcionais (NFR)

- **Desempenho (Performance):** Resposta média aceitável de 2 a 4 segundos em consultas de lista nas condições da rede local. Validações e salvamento de ações básicas devem conferir resposta sistêmica (UI) quase imediata.
- **Auditoria Segura:** Em nenhum momento a base deve subscrever eventos. Uma requisição que é rejeitada e posteriormente retorna não remove seu passado, senão anexa um novo evento temporal.
- **UI/UX:** O desenho de interface obedece a um design padronizado denominado "**Modern Corporate**". É obrigatória a legibilidade profunda com cores concisas, tipografia refinada e foco exclusivo no objetivo central, utilizando elevações suaves e hierarquia clara.
- **Segurança (Security):** Checagens (RBAC) precisam envolver tanto regras de interface do lado cliente, quanto bloqueios rígidos em solicitações de servidor. Validação e sanitização mandatórias nos anexos enviados ao sistema.
- **Confiança e Resiliência:** Evitar cliques acidentais ou duplo-clicks em botões de "Salvar/Submeter" prevenindo dados replicados. Erraticidade (falhas de login, uploads mal formados) de retornar alertas amigáveis aos usuários.

## 8. Pilares de Arquitetura e Extensão

- **Paradigma DOE (Directives, Orchestration, Execution):** O backend é fortemente tipado e separa a intenção do usuário dos "workers" (executáveis).
- **Dados Resilientes:** Aplicação orientada a ser escalável, de modo que adições de "Tipos de Chamado" se comportem nativamente na API subjacente, não demandando o descarte de componentes originais da V1.

---
*Este PRD é dinâmico e atuará como âncora para os desenvolvedores, testers e partes interessadas na entrega da Release Initial V1 do Alpla Angola Gerencial.*
