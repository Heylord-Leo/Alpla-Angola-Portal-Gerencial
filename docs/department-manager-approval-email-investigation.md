# Investigação — Departamentos, Local Managers e E-mails de Aprovação

> **Data:** 15/07/2026 · **Tipo:** Diagnóstico read-only (nenhuma alteração de código, banco ou migrations foi feita)
> **Escopo:** Modelo de dados, fluxo de aprovação de área e envio de e-mails do Portal Gerencial.

---

## 1. Resumo executivo

**Os dois cadastros NÃO duplicam a mesma função, mas o naming induz ao erro:**

- A role **"Local Manager"** é uma role de **administração de utilizadores** ("Gere utilizadores dentro do seu escopo permitido de planta e departamento" — `roles.ts:41`). Ela **não participa da escolha do aprovador de área** nem do envio de e-mails de aprovação. Seu único toque no workflow de pedidos é permitir **reatribuir comprador** (`RequestsController.cs:2487`).
- Quem aprova a etapa de área é definido por **duas fontes independentes**, e é aí que mora a duplicidade real:
  1. **`Department.ResponsibleUserId`** (Dados Mestres → Departamentos, rotulado na UI como *"Responsável (Aprovador de Área)"*) — define **quem é nomeado** `Request.AreaApproverId` e **quem recebe o e-mail** de aprovação pendente.
  2. **Role "Area Approver" + `UserDepartmentScopes`** — define **quem enxerga a fila** de área e **quem tem permissão de executar** a aprovação.

**Consequências práticas confirmadas no código:**

1. **O responsável do departamento é um único usuário global** (`Guid? ResponsibleUserId`) — o modelo **não suporta** um manager por planta. `Department` não tem `PlantId` (`Organization.cs:3-12`).
2. **O e-mail de aprovação pendente vai para exatamente 1 pessoa** (o responsável do departamento), para pedidos de **todas as plantas**. Managers de outra planta não recebem, e um manager da planta certa que não seja o "responsável" também não.
3. **Qualquer usuário com a role "Area Approver" pode aprovar qualquer pedido** — a ação (`ProcessAreaApproval`, linha 4806) valida **apenas a role**, sem verificar `AreaApproverId`, departamento ou planta.
4. **Se o responsável for desativado, o e-mail se perde silenciosamente** (log warning apenas) — o fallback de fan-out por departamento praticamente nunca executa, porque o submit garante `AreaApproverId` preenchido.
5. Existe uma **estrutura dormente** (`UserRoleAssignment.DepartmentScopeId`, `Role.cs:18`) desenhada para escopar aprovadores por departamento, mas **nunca lida nem gravada** em lugar nenhum.

**Recomendação central (seção 8):** criar a tabela `DepartmentManager (DepartmentId + PlantId + UserId)`, tratar `Department.ResponsibleUserId` como fallback durante a transição, e resolver aprovador + destinatários de e-mail por **departamento + planta**.

---

## 2. Modelo atual encontrado

### 2.1 Entidades e campos

| Conceito | Onde está | Cardinalidade | Observação |
|---|---|---|---|
| Responsável do departamento | `Department.ResponsibleUserId` — [Organization.cs:10](file:///C:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/Organization.cs) | **1 por departamento** (Guid nullable) | UI: *"Responsável (Aprovador de Área)"* (`MasterData.tsx:647`) |
| Aprovador final | `Company.FinalApproverUserId` — Organization.cs:24 | 1 por empresa | Mesmo padrão do responsável de departamento |
| Departamento ↔ planta | **Não existe** | — | `Department` é global: `Id, Name, Code, IsActive, ResponsibleUserId`. `Plant` pertence a `Company`, mas departamento não pertence a planta |
| Departamento "de casa" do usuário | `User.DepartmentId` — User.cs | 1 por usuário | Nullable; não é usado pelo fluxo de aprovação |
| Escopo de plantas do usuário | `UserPlantScope` (UserId + PlantId) — UserScope.cs | N:N | Usado para **visibilidade** de pedidos e autorização de criação |
| Escopo de departamentos do usuário | `UserDepartmentScope` (UserId + DepartmentId) — UserScope.cs | N:N | Usado para visibilidade **e** para o fan-out de e-mails informativos |
| Roles do usuário | `UserRoleAssignment` (UserId + RoleId + `DepartmentScopeId?`) — Role.cs | N:N | **`DepartmentScopeId` é dormente** — declarado mas nunca lido/gravado (grep no repo: só a declaração em Role.cs:18) |
| Role "Local Manager" | `RoleConstants.LocalManager` | — | Administração de utilizadores por escopo (UsersController) + reatribuir comprador (RequestsController.cs:2487-2488) |
| Role "Area Approver" | `RoleConstants.AreaApprover` | — | Habilita ver a fila de área e executar a aprovação |
| Nomeação no pedido | `Request.AreaApproverId` / `Request.FinalApproverId` | 1 por pedido | Snapshot resolvido do master data (ver seção 3) |

### 2.2 Como cada tela alimenta o modelo

- **Dados Mestres → Departamentos** (`MasterData.tsx:645-660`, `LookupsController.cs:514-566`): um `<select>` de **um único usuário** grava `ResponsibleUserId`. A limitação de "1 responsável" está **nas três camadas**: banco (coluna única), DTO (`CreateLookupDto.ResponsibleUserId`, LookupsController.cs:2217), e UI (select único).
- **Cadastro de Usuários** (`UserManagement.tsx`, `UsersController.cs:206-238, 331-379`): grava roles (checkboxes, incluindo "Local Manager" e "Area Approver") + escopos de plantas (`plantIds`) + escopos de departamentos (`departmentIds`). **Não grava** `Department.ResponsibleUserId` — ou seja, marcar alguém como Local Manager ou Area Approver **não** o torna o responsável do departamento.

---

## 3. Fluxo atual de aprovação

```text
Pedido criado (POST /requests)
  └─ AreaApproverId = Department.ResponsibleUserId   ← RequestsController.cs:1824 ("Auto-resolved")
  └─ FinalApproverId = Company.FinalApproverUserId   ← RequestsController.cs:1825

Draft editado (PUT /requests/{id}/draft), se trocar departamento
  └─ AreaApproverId = novoDept.ResponsibleUserId     ← RequestsController.cs:2209

Submit (POST /requests/{id}/submit)
  └─ RE-resolve (sobrescreve o que estiver no pedido):
       if (Department.ResponsibleUserId != null) AreaApproverId = Department.ResponsibleUserId   ← linha 2363-2366
       if (Company.FinalApproverUserId != null) FinalApproverId = Company.FinalApproverUserId    ← linha 2368-2371
  └─ Se AreaApproverId ficar nulo → ERRO de submissão:
       "Não foi possível determinar o Aprovador de Área. Verifique se o Departamento
        selecionado tem um responsável definido no cadastro."                                    ← linha 2384-2385

Fila de aprovações pendentes (GET pending)                                                       ← linhas 476-509
  └─ Visibilidade base: GetScopedRequestsQuery() = filtro por UserPlantScopes + UserDepartmentScopes (BaseController.cs:29-64)
  └─ Fila de área: quem tem role "Area Approver" (ou Admin) vê TODOS os pedidos em
     WAITING_AREA_APPROVAL dentro do seu escopo; quem NÃO tem a role só vê onde r.AreaApproverId == userId
  └─ Fila final: exige role "Final Approver" (ou Admin)

Ação de aprovar/rejeitar área (POST /requests/{id}/area-approval/*)
  └─ ProcessAreaApproval valida SOMENTE a role "Area Approver"                                   ← linha 4806-4807
  └─ NÃO valida: AreaApproverId == actor, departamento, planta, nem usa GetScopedRequestsQuery
     (o pedido é buscado direto por id em _context.Requests, linha 4809)
```

**Nota sobre o frontend:** `RequestCreate.tsx:599-602` também auto-preenche `areaApproverId` ao escolher o departamento, mas isso é cosmético — o backend re-resolve na criação e o submit sobrescreve de novo. A fonte de verdade efetiva é sempre `Department.ResponsibleUserId` no momento do submit.

---

## 4. Fluxo atual de envio de e-mails

Todo o envio passa pelo `WorkflowNotificationOrchestrator` ([WorkflowNotificationOrchestrator.cs](file:///C:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs)) via padrão **EmailOutbox** (não SMTP direto):

```text
Ação de workflow → EmitAsync(WorkflowEvent { AreaApproverId = request.AreaApproverId, DepartmentId, PlantId, ... })
  └─ ResolveRecipientsAsync (switch por EventCode)
       └─ RequestSubmitted / QuotationCompleted → HandlePendingAreaApprovalFanningAsync (linha 248):
            • Se evt.AreaApproverId != null → e-mail SOMENTE para esse usuário (linha 295-301)
            • Senão (fallback) → todos os usuários ATIVOS com role "Area Approver"
              E UserDepartmentScope no departamento (linha 304-325)
            ⚠ Como o submit garante AreaApproverId preenchido, o fallback quase nunca roda.
       └─ PaymentScheduled / PaymentCompleted → HandlePaymentFanningOverridesAsync (linha 356):
            • Fan-out informativo para TODOS os area approvers com escopo no departamento
            • ⚠ NÃO filtra por planta — managers de outras plantas recebem
  └─ AddUserRecipientAsync: filtra usuário inativo silenciosamente (linha 605)
  └─ DispatchToRecipientAsync (linha 767):
       • Notificação in-app com dedup por CorrelationId
       • E-mail: só se config.SendEmail && !SuppressEmail && email não vazio (linha 795)
       • Dedup no outbox: CorrelationId + RecipientEmail (exceto DEAD_LETTER) (linha 801-804)
       • Insere EmailOutbox com Status=PENDING
  └─ EmailOutboxProcessor (background): retry com backoff 30s/2min/10min → DEAD_LETTER após MaxRetries
```

**Logs disponíveis para diagnóstico** (bons):
- `EMAIL_OUTBOX_QUEUED` no AdminLog com destinatário, pedido, evento e subject (orquestrador, linha 838-840).
- Retries e DEAD_LETTER logados no AdminLog pelo `EmailOutboxProcessor` (linhas 213-255).
- Identificação de ambiente TEST/PROD existe (migration `AddEmailEnvironmentIdentification`).
- **Lacuna:** quando o único destinatário é filtrado (inativo), há apenas `LogWarning("No recipients resolved...")` no logger — **não vai ao AdminLog** e não há rastro de "quem deveria ter recebido".

---

## 5. Conflitos e riscos encontrados

| # | Risco | Evidência | Severidade |
|---|---|---|---|
| R1 | **Duas fontes de verdade para "quem aprova a área"**: `ResponsibleUserId` decide nomeação + e-mail; role "Area Approver" + escopos decide visibilidade + permissão de executar. Podem divergir sem nenhum aviso. | Seções 3 e 4 | Alta |
| R2 | **E-mail de aprovação vai a 1 única pessoa** (o responsável), mesmo havendo vários managers aptos. Férias/ausência = pedido parado sem ninguém notificado. | Orquestrador linha 295-301 | Alta |
| R3 | **Responsável desativado → e-mail silenciosamente perdido.** `AddUserRecipientAsync` filtra inativos; com destinatário único, a lista fica vazia e só há um log warning. O submit continua funcionando (o Guid segue válido). | Orquestrador linhas 64-67, 605 | Alta |
| R4 | **Planta é ignorada na aprovação de área.** Departamento é global; o responsável recebe pedidos das 3 plantas. Cenário "Produção Viana 1/2/3 com managers A/B/C" é **irrepresentável** no modelo atual. | Organization.cs:3-12 | Alta |
| R5 | **Qualquer "Area Approver" aprova qualquer pedido** — a ação não valida titularidade, departamento nem planta (não usa o scoped query). A fila esconde, mas a API não impede. | RequestsController.cs:4806-4814 | Média-alta |
| R6 | **Fan-out de pagamentos vaza entre plantas**: e-mails informativos de PaymentScheduled/Completed vão a todos os approvers com escopo no departamento, sem filtro de planta (diferente de Finance/Buyer, que são plant-scoped). | Orquestrador linhas 391-397 vs 625-663 | Média |
| R7 | **Naming enganoso**: "Local Manager" sugere gestão/aprovação local, mas é role de administração de utilizadores. O rótulo "Responsável (Aprovador de Área)" nos Dados Mestres é quem realmente roteia a aprovação. Usuários configuram um esperando efeito no outro. | roles.ts:41, MasterData.tsx:647 | Média |
| R8 | **Snapshot sem re-sincronização**: `AreaApproverId` é resolvido no submit; trocar o responsável do departamento depois **não** re-roteia pedidos já em fila (nem há tela para reatribuir aprovador de área). | Linhas 2363-2366 (só no submit) | Média |
| R9 | **Estrutura dormente**: `UserRoleAssignment.DepartmentScopeId` foi criada para escopar aprovadores e nunca foi usada — sinal de que essa dor já foi antecipada e abandonada. | Role.cs:18 | Info |
| R10 | **Duplicidade de e-mail é mitigada** (dedup por CorrelationId+email no outbox), e usuário sem e-mail é filtrado no dispatch. Não encontrei risco real de duplicado no fluxo de área. | Orquestrador linhas 795-812 | OK |

### Resposta direta aos cenários levantados

**Cenário A — Só responsável nos Dados Mestres, nenhum Local Manager:**
Funciona "como desenhado": o responsável é nomeado, recebe o e-mail e — **desde que também tenha a role "Area Approver"** — consegue aprovar. Sem a role, ele recebe o e-mail e **vê** o pedido na fila (`r.AreaApproverId == userId`), mas a ação de aprovar retorna **403** (linha 4806). Configuração parcial = beco sem saída.

**Cenário B — Local Manager associado ao departamento, mas responsável é outro:**
Sem conflito de dados, mas com surpresa funcional: o e-mail e a nomeação vão **só para o responsável**. O Local Manager não recebe nada (a role dele nem participa). Se ele também tiver "Area Approver" + escopo, vê a fila e **pode** aprovar um pedido "nomeado" para outra pessoa — aprovação cruzada sem aviso.

**Cenário C — Responsável + vários Area Approvers no mesmo departamento:**
E-mail de pendência: **apenas o responsável** (sem duplicidade, mas sem redundância). Fila: todos os role-holders com escopo veem. Ação: qualquer um deles pode aprovar. E-mails informativos de pagamento: **todos** recebem (fan-out).

**Cenário D — Mesmo departamento em 3 plantas, managers diferentes:**
**Não suportado.** Departamento é global; um único `ResponsibleUserId` recebe as aprovações das 3 plantas. O workaround de criar 3 departamentos ("Produção V1/V2/V3") funciona tecnicamente, mas polui orçamento departamental, relatórios e o contexto financeiro dos e-mails (que agregam por `DepartmentId`).

---

## 6. Limitações para múltiplas plantas e múltiplos managers

1. `Department` não tem dimensão de planta — a granularidade "departamento+planta" não existe em nenhuma tabela.
2. `ResponsibleUserId` é `Guid?` único — banco, DTO e UI limitam a 1 responsável (as três camadas precisariam mudar).
3. O snapshot `Request.AreaApproverId` é um único Guid — suportar "qualquer um dos N managers da planta X" exige mudar a resolução de destinatários (o snapshot pode continuar single como "primeiro que aprovar assume", desde que a fila e o e-mail sejam resolvidos por grupo).
4. A visibilidade (plant/dept scopes) **já é** multi-dimensional — a infraestrutura de escopos existe e funciona; o que falta é usá-la (ou uma tabela dedicada) para **roteamento** de aprovação, não só visibilidade.

---

## 7. Cenários de teste recomendados (ambiente TEST)

Preparação: 3 plantas ativas, 1 departamento "Produção" global, usuários A (responsável do dept), B e C (role Area Approver + UserDepartmentScope em Produção; B com escopo só na Planta 1, C só na Planta 2).

| # | Cenário | Passos | Verificar |
|---|---|---|---|
| T1 | Só responsável | Criar pedido em Produção/Planta 1 e submeter | `Request.AreaApproverId == A`; outbox tem 1 e-mail `[AÇÃO NECESSÁRIA]` para A (AdminLog `EMAIL_OUTBOX_QUEUED`) |
| T2 | Responsável sem role | Remover role "Area Approver" de A; A tenta aprovar | Esperado hoje: **403**. Confirma o beco sem saída do Cenário A |
| T3 | Aprovação cruzada | B (não nomeado) aprova pedido nomeado para A | Esperado hoje: **sucesso** — evidencia R5 |
| T4 | Cross-plant | B (escopo só Planta 1) aprova via API pedido da Planta 2 | Esperado hoje: **sucesso** (fila esconde, API permite) — evidencia R5 |
| T5 | Responsável inativo | Desativar A; submeter novo pedido | Submit passa; **nenhum** e-mail na outbox; só LogWarning — evidencia R3 |
| T6 | Departamento sem responsável | Limpar `ResponsibleUserId`; tentar submeter | Erro de submissão (linha 2384) — comportamento correto atual |
| T7 | Troca de responsável com fila | Pedido em WAITING_AREA_APPROVAL; trocar responsável de A→B | `AreaApproverId` do pedido **continua A** — evidencia R8 |
| T8 | Fan-out de pagamento | Agendar pagamento de pedido da Planta 1 | B **e** C recebem o informativo (C é de outra planta) — evidencia R6 |
| T9 | Usuário sem e-mail | Responsável com Email vazio; submeter | Sem e-mail (filtro no dispatch); in-app criada; verificar se há rastro suficiente |
| T10 | Dedup | Reprocessar/reemitir mesmo evento | Outbox não duplica (CorrelationId+email) |

---

## 8. Recomendações de solução

### Recomendação principal: tabela `DepartmentManager` por departamento + planta

```text
DepartmentManager
- Id            (int)
- DepartmentId  (FK, obrigatório)
- PlantId       (FK, NULL = vale para todas as plantas)   ← permite migração suave
- UserId        (FK, obrigatório)
- IsActive      (bool)
- UNIQUE (DepartmentId, PlantId, UserId)
```

Decisões associadas:

1. **`Department.ResponsibleUserId` vira fallback**, não é removido: a resolução passa a ser
   `DepartmentManager(dept, planta)` → `DepartmentManager(dept, planta=NULL)` → `ResponsibleUserId` (legado).
   Zero quebra para dados existentes; a coluna pode ser aposentada numa fase posterior.
2. **Migração de dados**: para cada departamento com `ResponsibleUserId`, semear 1 linha `DepartmentManager (dept, NULL, responsável)`. Comportamento atual preservado bit a bit.
3. **Múltiplos managers**: a resolução retorna uma **lista**; o e-mail de pendência é enviado a todos; `Request.AreaApproverId` pode continuar single (preenchido quando um deles age — "primeiro que aprovar assume") ou nullable com a fila baseada no grupo. Sugestão: manter o snapshot e preenchê-lo no ato da aprovação.
4. **Não usar a role "Local Manager" para aprovação** — manter sua semântica atual (administração de utilizadores). A role "Area Approver" continua sendo o *gate* de permissão; `DepartmentManager` passa a ser o *roteamento*.
5. **Aproveitar ou remover `UserRoleAssignment.DepartmentScopeId`**: recomendo **remover** (dormente) para eliminar a terceira fonte potencial de verdade, em favor de `DepartmentManager`.
6. **Corrigir R5 junto**: `ProcessAreaApproval` deve validar que o ator é manager do (departamento, planta) do pedido — ou o `AreaApproverId` nomeado — além da role.
7. **Corrigir R6**: fan-out de pagamento deve cruzar `UserPlantScopes` com `evt.PlantId` (mesmo padrão já usado para Finance/Buyer).
8. **Corrigir R3**: ao resolver 0 destinatários para evento de aprovação, gravar `AdminLog` de erro (não só LogWarning) indicando quem deveria receber e por que foi filtrado.
9. **UI Dados Mestres**: trocar o select único por uma grade "Managers por planta" (linhas Planta × Usuário) dentro do departamento; UI de usuários permanece como está.

### Alternativas consideradas e descartadas

- **Só Local Managers por usuário (sem tabela nova):** descartada — mistura administração de utilizadores com aprovação e não expressa "manager da Planta 2 para o dept X" sem inventar semântica nova para escopos (que hoje significam *visibilidade*; reutilizá-los para *roteamento* mudaria silenciosamente o comportamento de todos os usuários já cadastrados).
- **Departamentos por planta (duplicar "Produção" 3×):** descartada — quebra orçamento/relatórios departamentais e o "Contexto Financeiro Departamental" dos e-mails, que agregam por `DepartmentId`.
- **Remover `ResponsibleUserId` de imediato:** descartada — fallback é necessário para migração sem downtime e rollback simples.

---

## 9. Plano de implementação sugerido (faseado — nada implementado ainda)

### Fase 1 — Diagnóstico e evidência *(este documento)*
- Executar os testes T1–T10 em TEST para confirmar os comportamentos descritos, em especial T3/T4/T5/T8 (hipóteses de maior impacto derivadas de leitura de código, ainda não exercitadas em runtime).

### Fase 2 — Ajuste de modelo
- Entidade `DepartmentManager` + configuração EF + migration.
- Migration de seed: `ResponsibleUserId` → `DepartmentManager(dept, NULL, user)`.
- Índices: `(DepartmentId, PlantId)`, unique composto.

### Fase 3 — Backend
- **Resolução de aprovadores** (novo serviço, ex.: `IApprovalRoutingService`): usado em `CreateRequest` (linha 1824), `UpdateRequestDraft` (linha 2209) e `Submit` (linhas 2363-2371).
- **`ProcessAreaApproval`**: adicionar validação de titularidade (manager do dept+planta OU `AreaApproverId` nomeado).
- **Orquestrador**: `HandlePendingAreaApprovalFanningAsync` resolve lista por (dept, planta); `HandlePaymentFanningOverridesAsync` ganha filtro de planta; alerta AdminLog para 0 destinatários.
- **LookupsController**: endpoints CRUD para `DepartmentManager` (ou sub-recurso de departments).
- Validações: manager deve ser usuário ativo; avisar (não bloquear) se não tiver role "Area Approver".

### Fase 4 — Frontend
- **Dados Mestres → Departamentos**: grade de managers por planta (substitui o select único; manter leitura do legado durante transição).
- **Cadastro de Usuários**: sem mudança estrutural; opcional exibir "é manager de: Dept X @ Planta Y" como informação derivada.
- **Telas de aprovação**: sem mudança de layout; a fila passa a refletir o novo roteamento naturalmente via backend.

### Fase 5 — Testes
- Unitários: serviço de resolução (dept+planta, fallback NULL-plant, fallback legado, inativos).
- Integração: submit → outbox com N managers; aprovação por manager de outra planta → 403.
- Manuais em TEST: repetir T1–T10 esperando os novos comportamentos.
- Regressão: pedidos existentes (AreaApproverId já preenchido) seguem aprováveis; e-mails de eventos não relacionados inalterados.

---

## 10. Arquivos analisados

| Arquivo | Relevância |
|---|---|
| `src/backend/AlplaPortal.Domain/Entities/Organization.cs` | `Department.ResponsibleUserId` (linha 10), `Company.FinalApproverUserId` (24), `Plant` sem ligação com departamento |
| `src/backend/AlplaPortal.Domain/Entities/User.cs` | `User.DepartmentId`, coleções de escopos e roles |
| `src/backend/AlplaPortal.Domain/Entities/UserScope.cs` | `UserPlantScope`, `UserDepartmentScope` |
| `src/backend/AlplaPortal.Domain/Entities/Role.cs` | `UserRoleAssignment.DepartmentScopeId` dormente (18) |
| `src/backend/AlplaPortal.Domain/Constants/RoleConstants.cs` | Roles "Local Manager", "Area Approver" etc. |
| `src/backend/AlplaPortal.Api/Controllers/RequestsController.cs` | Create (1824-1825), UpdateDraft (2209), Submit re-resolução (2363-2388), fila (476-509), ProcessAreaApproval só-role (4806), Local Manager em assign-buyer (2487), emissão de eventos (1930-1972, 6760-6812) |
| `src/backend/AlplaPortal.Api/Controllers/BaseController.cs` | `GetScopedRequestsQuery` (29-64) |
| `src/backend/AlplaPortal.Api/Controllers/LookupsController.cs` | CRUD de departamentos com `ResponsibleUserId` único (508-566, 2217) |
| `src/backend/AlplaPortal.Api/Controllers/UsersController.cs` | Persistência de roles e escopos; permissões do Local Manager (206-238, 486-492) |
| `src/backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs` | Resolução de destinatários (119-424), dispatch/outbox/dedup (767-842), filtro de inativos (598-618), fan-outs plant-scoped de Finance/Buyer (625-760) |
| `src/backend/AlplaPortal.Infrastructure/Services/EmailOutboxProcessor.cs` | Retries, DEAD_LETTER, logs |
| `src/frontend/src/pages/Settings/MasterData.tsx` | UI "Responsável (Aprovador de Área)" — select único (645-660) |
| `src/frontend/src/pages/Admin/UserManagement.tsx` | UI de roles + escopos de plantas/departamentos |
| `src/frontend/src/constants/roles.ts` | Descrição oficial da role Local Manager (41) |
| `src/frontend/src/pages/Requests/RequestCreate.tsx` | Auto-fill cosmético de `areaApproverId` (599-602, 716-719) |
