# Redesign — Aprovação de Área por Departamento e Planta

> **Data:** 15/07/2026 · **Tipo:** Plano técnico · **Status:** ✅ **REDESIGN CONCLUÍDO** — Fase A (15/07/2026), Fase B (corte definitivo sem feature flag, 16/07/2026) e **Fase C (limpeza do legado, 16/07/2026)** implementadas. Estado final: `Department.ResponsibleUserId` **removido do modelo e do banco**; role "Area Approver" **100% derivada** de `DepartmentManagers` (atribuições manuais removidas com backup de auditoria; atribuição manual bloqueada na API); HR e Contratos migrados para `DepartmentManagers`; única compatibilidade restante é a cláusula `IsLegacyNamedAreaApprover` para pedidos pré-corte em etapa de área (removível quando `LegacyPendingRequests` do relatório de reconciliação estiver vazio em PROD). As seções abaixo preservam o histórico de decisão; onde conflitarem com este cabeçalho, vale o cabeçalho.
> **Base:** [department-manager-approval-email-investigation.md](file:///C:/dev/alpla-portal/docs/department-manager-approval-email-investigation.md)
> **Decisões de negócio já tomadas pelo product owner:** `DepartmentManager` como fonte de verdade; Local Manager fora da aprovação; Area Approver no máximo como compatibilidade temporária; validação de titularidade na API; fila e e-mails por departamento + planta. **D1, D2 e D3 confirmadas em 15/07/2026** (detalhes na seção 19).

---

## 1. Resumo executivo

Substituímos o responsável único global (`Department.ResponsibleUserId`) por um cadastro **`DepartmentManager (DepartmentId, PlantId?, UserId)`** que passa a ser a **única fonte de verdade** para três coisas que hoje vivem separadas e divergem: quem é notificado, quem vê a fila e quem pode aprovar a etapa de área.

A transição acontece em **3 fases**:

- **Fase A (fundação):** tabela nova + seed a partir do legado + serviço central de resolução (`IApprovalRoutingService`). Nada muda para o usuário final.
- **Fase B (corte de comportamento):** submit, fila, autorização de aprovação (individual **e em lote**) e e-mails passam a resolver por `DepartmentManager`. A role "Area Approver" vira **derivada** (injetada automaticamente no login para quem tem linhas ativas em `DepartmentManager`), o que mantém funcionando as ~20 superfícies que hoje checam a role sem precisar tocá-las uma a uma.
- **Fase C (limpeza):** UI dos Dados Mestres deixa de gravar `ResponsibleUserId`; atribuição manual da role some do cadastro de usuários; coluna legada e checks-por-role residuais são aposentados.

**Mecanismo-chave da transição:** descobri na investigação complementar que a role "Area Approver" gateia muito mais do que a decisão de aprovar — rota `/approvals`, menu, `BudgetPreviewController` (`[Authorize]`), seleção de vencedor, fluxos not-quoted, `LineItemsController`, aprovação de **contratos** e o próprio dropdown dos Dados Mestres. Remover a role de imediato quebraria tudo isso. Derivá-la do cadastro novo dá o resultado que você pediu ("Area Approver não obrigatório, só compatibilidade") com risco mínimo: ela deixa de ser **atribuída** e passa a ser **consequência** de ser manager.

---

## 2. Problema atual

Resumo do relatório de investigação (evidências e linhas lá):

1. `Department.ResponsibleUserId` é 1 usuário global por departamento → irrepresentável ter managers por planta (R4).
2. E-mail de aprovação pendente vai a exatamente 1 pessoa; inativo = e-mail perdido em silêncio (R2, R3).
3. Qualquer portador da role "Area Approver" aprova qualquer pedido — sem validação de departamento, planta ou nomeação (R5).
4. Fan-out informativo de pagamentos vaza entre plantas (R6).
5. Nomeação (`ResponsibleUserId`), visibilidade (escopos) e permissão (role) são três fontes de verdade que divergem sem aviso (R1); `AreaApproverId` é snapshot sem re-sincronização (R8).

## 3. Decisão de negócio recomendada

| Decisão | Recomendação |
|---|---|
| Fonte de verdade da aprovação de área | **`DepartmentManager`** (departamento + planta), com `PlantId NULL` = manager global do departamento |
| Um departamento, várias plantas | **Sim** — departamento continua único e global; a dimensão de planta entra só no cadastro de managers |
| Múltiplos managers por planta | **Sim** — todos são notificados, todos veem a fila, o primeiro que decidir assume |
| Role "Area Approver" | **Derivada** do cadastro (Fase B) e não mais atribuível manualmente (Fase C); deixa de ser suficiente para aprovar já na Fase B |
| Role "Local Manager" | **Intocada** — administração de utilizadores por escopo, zero participação na aprovação |
| `Department.ResponsibleUserId` | Congelado como fallback de leitura na Fase B; removido na Fase C |
| Sem manager resolvível no submit | **Bloquear** com erro claro (mesmo padrão da validação atual da linha 2384) |

## 4. Novo modelo de dados

```csharp
// src/backend/AlplaPortal.Domain/Entities/DepartmentManager.cs  (novo)
public class DepartmentManager
{
    public int Id { get; set; }

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    /// <summary>NULL = manager global do departamento (todas as plantas).</summary>
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
```

Configuração EF (em `ApplicationDbContext`/`EntityConfigurations`):

- `UNIQUE (DepartmentId, PlantId, UserId)` — impede duplicata exata; permite o mesmo usuário em plantas diferentes e como global + específico.
- Índice `(DepartmentId, PlantId, IsActive)` — é a consulta quente da resolução e da fila.
- Índice `(UserId, IsActive)` — consulta da fila inversa ("de quais dept/plantas sou manager?") e da claim derivada no login.
- FKs `Restrict` (usuários são desativados, nunca apagados; departamentos/plantas idem).
- **Validação de consistência na gravação (não no banco):** `PlantId`, quando preenchido, deve pertencer a uma planta ativa; par (dept, planta) não valida empresa porque departamento é global.

Exemplo do cenário-alvo:

```text
Produção (DepartmentId=5):
  (5, Viana1, ManagerA)  (5, Viana1, ManagerB)
  (5, Viana2, ManagerC)
  (5, Viana3, ManagerD)
  (5, NULL,   ManagerE)   ← global: cobre qualquer planta, inclusive futuras
```

## 5. Regras de resolução de responsáveis

Serviço novo e único ponto de verdade — `IApprovalRoutingService` (Infrastructure/Services/Approvals, ao lado de `ApprovalIntelligenceService`):

```csharp
Task<ApprovalRoutingResult> ResolveAreaManagersAsync(int departmentId, int? plantId);
Task<bool> IsAreaManagerAsync(Guid userId, int departmentId, int? plantId);
Task<List<(int DepartmentId, int? PlantId)>> GetManagedScopesAsync(Guid userId);
```

Cascata do `ResolveAreaManagersAsync` (exatamente a que você especificou):

```text
1. DepartmentManager ativos com DepartmentId + PlantId == pedido.PlantId
   (managers de planta específica)
2. Se vazio → DepartmentManager ativos com DepartmentId + PlantId IS NULL
   (managers globais do departamento)
3. Se vazio → lista vazia + motivo ("NO_MANAGER") → chamador decide
   (submit bloqueia; orquestrador grava AdminLog APPROVAL_EMAIL_NO_RECIPIENT)
   [ESTADO FINAL — o fallback para Department.ResponsibleUserId, planejado como
    nível 3, foi abandonado no corte definitivo da Fase B e a coluna foi
    removida na Fase C.]
```

Regras transversais aplicadas **dentro** do serviço (nenhum chamador reimplementa):

- Filtra `DepartmentManager.IsActive` **e** `User.IsActive` **e** `Email` não vazio. Um manager inativo em uma planta **não** dispara silenciosamente a cascata para o nível global — a linha inativa é ignorada, mas se restarem 0 no nível 1, a cascata segue normalmente (comportamento desejado: cobertura, não buraco).
- `pedido.PlantId == null` (pedidos antigos): pula o nível 1, resolve direto por globais (nível 2).
- **Autorização vs. e-mail (regra confirmada — D1):** os dois métodos têm semânticas deliberadamente diferentes.
  - `ResolveAreaManagersAsync` (destinatários de e-mail e exibição de "responsáveis elegíveis"): **cascata estrita** — apenas o primeiro nível não-vazio recebe.
  - `IsAreaManagerAsync` (autorização de aprovar/rejeitar e fila): **inclusiva** — aceita qualquer linha ativa com `DepartmentId` do pedido e `PlantId == pedido.PlantId OR PlantId IS NULL`. Manager global pode aprovar mesmo quando a planta tem managers próprios; **manager de outra planta nunca** (não existe linha compatível).
  - Racional: e-mail direcionado a quem é "dono" da planta, sem spam ao global; autorização mantém o global como backup sempre disponível.

## 6. Papel de `Department.ResponsibleUserId` no legado

> **ESTADO FINAL (Fase C, 16/07/2026):** coluna removida do modelo e do banco pela migration
> `PhaseCRemoveLegacyAreaApprovalConfig` (valores preservados em `_PhaseC_DepartmentResponsibleBackup`).
> Nunca houve fallback ativo em Fase B — o corte foi direto. Os consumidores fora do fluxo de
> pedidos (HR `managedDepartmentIds`, contratos `TechnicalApproverId`) foram migrados para
> `DepartmentManagers` na própria Fase C. O texto abaixo é o plano original, mantido como histórico.

- **Fase A:** continua funcionando como hoje (nada muda); a migration copia seu valor para `DepartmentManager (dept, NULL, user)`.
- **Fase B:** vira **somente leitura** — a UI dos Dados Mestres deixa de expor o select antigo; o backend para de gravá-lo (endpoint aceita e ignora, com log de deprecação); permanece como nível 3 da cascata para proteger contra seed incompleto.
- **Fase C:** removido da cascata → coluna marcada `[Obsolete]` → migration de drop após 1–2 releases estáveis. As linhas do submit que hoje o consomem (RequestsController.cs:1824, 2209, 2363) já terão sido substituídas na Fase B.

## 7. Papel de `Request.AreaApproverId`

Mudança de semântica: de **"aprovador nomeado no submit"** para **"aprovador que decidiu"** (audit trail).

- **Submit (Fase B):** não pré-nomeia mais. A validação da linha 2384 muda de "AreaApproverId != null" para "`ResolveAreaManagersAsync(...)` retorna ≥ 1", com a mensagem: *"Não foi possível determinar um responsável de área para o departamento/planta deste pedido. Configure os managers em Dados Mestres → Departamentos."*
- **Decisão:** `ProcessAreaApproval` grava `request.AreaApproverId = actorId` no ato de aprovar/rejeitar/pedir reajuste. É o que os e-mails pós-decisão (`HandleAreaFanningOverridesAsync`, que assume "actor == AreaApproverId") já esperam — essa suposição passa a ser verdadeira por construção.
- **Coluna permanece** — sem migration destrutiva; pedidos antigos mantêm o valor nomeado; exibição no detalhe do pedido muda (seção 15).
- **Fila para não-managers** (cláusula legada `r.AreaApproverId == userId`, linha 494): mantida durante a Fase B para pedidos in-flight nomeados no modelo antigo; removida na Fase C.

## 8. A role "Area Approver" ainda é necessária?

**Como atribuição manual, não. Como claim derivada, sim — temporariamente.**

O que ela gateia hoje (mapeado por grep, fora migrations): rota e menu `/approvals` (App.tsx:283, navigation.tsx:54), `ApprovalCenter.tsx:61`, `useRequestDetail.ts:125`, `QuickActions`, dropdown dos Dados Mestres (MasterData.tsx:662), `BudgetPreviewController` (atributo `[Authorize]`), `RequestsController` (fila 125/476/646, decisão 4806, vencedor 6501, not-quoted 7181/7317), `LineItemsController:646`, `ApprovalBatchController` (344/694/770), `ContractsController` (687/724/851 — **fluxo de contratos, fora do escopo deste redesign**), `ProformaDeadlineAlertService:254` e `WorkflowNotificationOrchestrator` (305/388).

Plano:

- **Fase B:** no login (`AuthServices.cs`, montagem das claims), usuários com ≥1 linha ativa em `DepartmentManager` recebem a claim de role "Area Approver" **automaticamente** (role virtual). Todos os gates acima continuam funcionando sem edição. Em paralelo, os pontos de **decisão** (4806, batch 344/694/770) ganham a validação de titularidade — ter a role deixa de ser suficiente.
- **Fase C:** a role passa a ser **100% derivada** de `DepartmentManager` (confirmado — D2): o checkbox "Area Approver" some do cadastro de usuários e as atribuições manuais antigas são **ignoradas** na montagem de claims. Pré-requisito obrigatório: o **relatório de reconciliação** (seção 16.1) executado e validado pelo negócio **antes** do corte. Os checks de role em pontos de *decisão* são trocados por `IsAreaManagerAsync`; os gates de *navegação/leitura* podem continuar consumindo a claim derivada indefinidamente.
- **Contratos:** continuam usando a role (agora derivada) até terem redesign próprio — comportamento preservado: managers de departamento aprovam contratos como hoje os role-holders fazem.

## 9. Separação entre Local Manager e Department Manager

| | Local Manager (role, existente) | Department Manager (cadastro, novo) |
|---|---|---|
| O que é | Role de **administração de utilizadores** dentro do escopo | Registro de **responsabilidade de aprovação** por dept+planta |
| Onde se configura | Cadastro de Usuários (checkbox de role) | Dados Mestres → Departamentos (grade por planta) |
| Efeito no workflow | Nenhum (exceto reatribuir comprador, mantido) | Fila, autorização e e-mails de aprovação de área |

Ações anti-confusão:

1. **Nenhuma linha de código compartilhada** entre os dois conceitos — `IApprovalRoutingService` não consulta roles de administração.
2. Renomear o rótulo de exibição da role para **"Gestor de Utilizadores (Local Manager)"** em `roles.ts` — só texto, sem tocar em `RoleConstants` (evita migration de dados de role).
3. A descrição da grade nova nos Dados Mestres diz explicitamente: *"Managers de aprovação de área. Não confundir com a role Local Manager (administração de utilizadores)."*

## 10. Fluxo novo de criação/submissão do pedido

```text
Criação (POST /requests)
  └─ NÃO nomeia AreaApproverId (remove linha 1824)
  └─ FinalApproverId: inalterado (Company.FinalApproverUserId — fora de escopo)

Edição de draft (PUT /requests/{id}/draft)
  └─ Remove a re-nomeação da linha 2209 (trocar departamento não mexe mais em AreaApproverId)

Submit (POST /requests/{id}/submit)
  └─ managers = ResolveAreaManagersAsync(request.DepartmentId, request.PlantId)
  └─ Se vazio → 400 "Não foi possível determinar um responsável de área..."
  └─ AreaApproverId permanece null (será preenchido na decisão)
  └─ Evento RequestSubmitted emitido com DepartmentId + PlantId
     (WorkflowEvent.AreaApproverId passa null → orquestrador resolve a lista ele próprio)
```

O auto-fill cosmético do frontend (`RequestCreate.tsx:599-602`, `useRequestDetail.ts` equivalente) é removido — o campo deixa de existir no formulário (seção 15).

## 11. Fluxo novo da fila de aprovação

`GET pending` (RequestsController.cs:476-495) — a cláusula de área muda de "tem a role → vê tudo no escopo de visibilidade" para:

```csharp
// pseudocódigo da cláusula de área
var scopes = await _routing.GetManagedScopesAsync(userId); // [(deptId, plantId?)]
areaQuery = areaQuery.Where(r =>
    isAdmin
    || r.AreaApproverId == userId                     // legado in-flight (remover na Fase C)
    || scopes.Any(s => s.DepartmentId == r.DepartmentId
                    && (s.PlantId == null || s.PlantId == r.PlantId)));
```

Consequências intencionais:

- **Estreitamento** vs. hoje: um "Area Approver" genérico deixa de ver pedidos de departamentos/plantas que não gere — isso é o fix do R5 aplicado também à leitura. Comunicar no changelog.
- `GetScopedRequestsQuery` (visibilidade por `UserPlantScope`/`UserDepartmentScope`) **continua aplicado por fora** — manager sem escopo de visibilidade na planta não veria o pedido. **Regra confirmada (D3):** ao salvar um `DepartmentManager`, o backend **auto-completa** os escopos ausentes (`UserDepartmentScope` do departamento; `UserPlantScope` da planta — ou de **todas as plantas ativas**, no caso de manager global), na mesma transação. Invariante do sistema: *não existe manager autorizado a aprovar mas sem visibilidade na fila.* A remoção de um manager **não** remove escopos automaticamente (podem ter outras origens) — apenas o relatório 16.1 aponta sobras para limpeza manual.
- Mesma mudança nos 3 pontos de fila/contagem que replicam a lógica (linhas 125, 646) e no `ApprovalBatchController` (linhas 61/282/318/... que montam listas de batch).

## 12. Fluxo novo da aprovação/rejeição

`ProcessAreaApproval` (linha 4801) e os 3 checks do `ApprovalBatchController`:

```text
1. Autorização: isAdmin OR IsAreaManagerAsync(actor, r.DepartmentId, r.PlantId)
                OR r.AreaApproverId == actor (legado in-flight, Fase B)
   → 403 "Você não é responsável pelo departamento/planta deste pedido."
2. Guard de status já existente (WAITING_AREA_APPROVAL) → cobre corrida entre
   múltiplos managers: o segundo a agir recebe o 400 atual "não está em fase de
   aprovação de área" (mensagem melhorada: "já decidido por {AreaApproverId}").
3. Ao decidir: request.AreaApproverId = actorId (audit).
4. Batch: validar POR PEDIDO do lote (um lote pode misturar plantas), não uma vez só.
```

Concorrência: o par (guard de status + `SaveChanges`) já é suficiente para o caso prático; se quisermos rigor, adicionar `rowversion` no `Request` é uma extensão opcional (D4) — não bloqueia este redesign.

## 13. Fluxo novo de envio de e-mails

Todas as mudanças no `WorkflowNotificationOrchestrator`, consumindo `IApprovalRoutingService`:

1. **`HandlePendingAreaApprovalFanningAsync` (linha 248):** ignora `evt.AreaApproverId`; resolve a lista por `(evt.DepartmentId, evt.PlantId)` e envia o e-mail `[AÇÃO NECESSÁRIA]` (com o Contexto Financeiro atual) para **todos** os managers resolvidos. Dedup existente por CorrelationId+email já impede duplicata por destinatário.
2. **`HandlePaymentFanningOverridesAsync` (linha 356):** o fan-out informativo passa a usar a mesma resolução por dept+planta — corrige o vazamento entre plantas (R6) e de quebra alinha "quem é informado" com "quem gere".
3. **Zero destinatários em evento de aprovação:** além do `LogWarning` (linha 66), gravar `AdminLog` nível Error (`APPROVAL_EMAIL_NO_RECIPIENT`) com departamento, planta e motivo da cascata — fecha o R3.
4. **`HandleAreaFanningOverridesAsync` (pós-decisão):** inalterado na estrutura; `evt.AreaApproverId` agora carrega o decisor real, então "Você aprovou..." fica correto por construção.
5. **`ProformaDeadlineAlertService:254`:** troca "todos com role Area Approver" por resolução por dept+planta do pedido em alerta (mesma correção, fluxo secundário).
6. Infra intocada: outbox, retries, DEAD_LETTER, dedup, filtro de inativo/sem-email (que passa a viver também na resolução), identificação TEST/PROD.

## 14. Alterações no backend

| Arquivo | Mudança |
|---|---|
| `Domain/Entities/DepartmentManager.cs` | **Novo** — entidade (seção 4) |
| `Domain/Entities/Organization.cs` | Navegação `Department.Managers`; depois `[Obsolete]` em `ResponsibleUserId` (Fase C) |
| `Infrastructure/Data/ApplicationDbContext.cs` + `EntityConfigurations.cs` | DbSet, índices, unique |
| `Infrastructure/Data/Migrations/*` | Migration 1: criar tabela + **seed** do legado (seção 16) |
| `Infrastructure/Services/Approvals/ApprovalRoutingService.cs` | **Novo** — cascata, filtros, `GetManagedScopesAsync` |
| `Infrastructure/Services/Auth/AuthServices.cs` | Claim derivada "Area Approver" para quem tem linha ativa |
| `Api/Controllers/RequestsController.cs` | Linhas 1824, 2209 (remover nomeação); 2363-2385 (submit resolve por serviço); 476-495, 125, 646 (fila); 4801+ (autorização + audit); 6501/7181/7317 (trocar check quando na Fase C) |
| `Api/Controllers/ApprovalBatchController.cs` | Linhas 344/694/770 + montagem de listas: autorização por pedido |
| `Api/Controllers/LookupsController.cs` | CRUD de managers como sub-recurso: `GET/POST/DELETE /lookups/departments/{id}/managers`; o POST **auto-completa escopos de visibilidade** na mesma transação e retorna no response quais escopos foram criados (para o aviso da UI — D3); congelar gravação de `ResponsibleUserId` (Fase B) |
| `Api/Controllers/AdminReportsController.cs` (ou existente equivalente) | **Novo endpoint** `GET /admin/reports/area-approver-reconciliation` (seção 16.1 — D2) |
| `Infrastructure/Services/WorkflowNotificationOrchestrator.cs` | Seção 13 (fanning, plant-filter, AdminLog zero-recipient) |
| `Infrastructure/Services/ProformaDeadlineAlertService.cs` | Resolução por dept+planta |
| `Application/DTOs/...` | `DepartmentManagerDto` (novo); `LookupDto` de departamento ganha `managers[]` opcional |

Fora de escopo declarado: `ContractsController` (mantém role derivada), `Company.FinalApproverUserId` (aprovação final inalterada), Local Manager/UsersController (inalterado, exceto ocultar o checkbox na Fase C).

## 15. Alterações no frontend

| Tela | Mudança |
|---|---|
| **Dados Mestres → Departamentos** (`MasterData.tsx`) | Substituir o select único "Responsável (Aprovador de Área)" por uma **grade de managers**: linhas Planta (ou "Global") × Usuário, com adicionar/remover e toggle ativo. Dropdown de usuário lista **todos os ativos** (o filtro atual por role, linha 662, vira circular com a role derivada — removê-lo). **Aviso D3 em dois momentos:** antes de salvar, se o usuário não tem escopo na planta/departamento, mostrar *"Ao confirmar, o escopo de visibilidade (Planta X / Departamento Y) será adicionado automaticamente a este usuário"*; após salvar, confirmação discreta listando os escopos efetivamente criados (vindos do response do POST) |
| **Cadastro de Usuários** (`UserManagement.tsx`) | Fase B: nada. Fase C: ocultar checkbox "Area Approver"; exibir seção somente leitura "Manager de: Produção @ Viana 1, ..." derivada do cadastro novo |
| **Criação/edição de pedido** (`RequestCreate.tsx`, `useRequestDetail.ts`) | Remover auto-fill e envio de `areaApproverId` (599-602, 716-719 e equivalentes); o campo some do payload |
| **Detalhe do pedido / Aprovações** (`ApprovalCenter`, `ApprovalDetailPanel`, timeline) | Onde exibe "Aprovador de Área": antes da decisão mostrar *"Pendente — N responsáveis elegíveis"* (nomes via endpoint de resolução ou no DTO); depois da decisão mostrar o decisor (AreaApproverId preenchido) |
| **Gates de rota/menu** (`App.tsx:283`, `navigation.tsx:54`, `roles.ts`) | Nenhuma mudança de código — a claim derivada mantém tudo; só o rename cosmético do Local Manager em `roles.ts` |

## 16. Estratégia de migração

```text
Migration 1 (Fase A) — estrutura + seed idempotente:
  CREATE TABLE DepartmentManagers (...);
  INSERT INTO DepartmentManagers (DepartmentId, PlantId, UserId, IsActive, CreatedAtUtc)
  SELECT d.Id, NULL, d.ResponsibleUserId, 1, GETUTCDATE()
  FROM Departments d
  WHERE d.ResponsibleUserId IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM DepartmentManagers m
                    WHERE m.DepartmentId = d.Id AND m.PlantId IS NULL
                      AND m.UserId = d.ResponsibleUserId);
```

- Seed inclui responsáveis atualmente **inativos** (linha criada, filtro de ativo é em runtime) — preserva o dado e torna o problema visível na UI em vez de escondê-lo.
- **Rollback Fase A:** drop da tabela; comportamento intocado (nenhum consumidor ainda).
- **Rollback Fase B:** ~~feature flag de configuração~~ **[SUPERSEDED]** — o PO decidiu em 16/07/2026 fazer o corte definitivo SEM feature flag; rollback de Fase B é por revert de release. A Fase C tem `Down()` parcial documentado na migration (recria coluna nullable + restaura atribuições manuais do backup; valores de ResponsibleUserId não são reconstruídos automaticamente).
- **Fase C** só inicia após ≥1 release da Fase B estável em PROD, cadastros das 3 plantas conferidos pelo negócio **e o relatório de reconciliação (16.1) aprovado**.
- Sequência operacional em PROD: deploy Fase A → janela para o negócio **cadastrar os managers reais por planta** nos Dados Mestres (a UI da grade entra na Fase A para isso) → ativar Fase B → relatório 16.1 → Fase C.

### 16.1 Relatório de reconciliação Role × DepartmentManager (pré-requisito da Fase C — D2)

Entregável: endpoint admin `GET /admin/reports/area-approver-reconciliation` (JSON + export CSV), consumível também como query SQL avulsa para conferência direta no banco. Colunas por usuário:

| Coluna | Fonte |
|---|---|
| Usuário (nome, e-mail, ativo?) | `Users` |
| Tem role "Area Approver" **manual**? | `UserRoleAssignments` × `Roles` |
| Linhas `DepartmentManager` ativas (dept @ planta / Global) | `DepartmentManagers` |
| **Classificação** | ver abaixo |

Classificações que o negócio valida linha a linha:

1. **`OK_DERIVADO`** — tem role manual **e** ≥1 linha ativa: nada muda na prática; atribuição manual será ignorada sem perda.
2. **`PERDE_ACESSO`** — tem role manual, **nenhuma** linha ativa: deixará de ver/aprovar na Fase C. É a lista crítica: o negócio decide, caso a caso, cadastrar como manager ou confirmar a perda.
3. **`SO_CADASTRO`** — sem role manual, com linha ativa: ganhou acesso via derivação na Fase B (conferir se intencional).
4. **`INATIVO_COM_VINCULO`** — usuário inativo com role manual e/ou linha de manager: limpar cadastro (linhas de manager de inativos não resolvem, mas poluem a grade).
5. **`INCONSISTENTE`** — anomalias: linha de manager apontando para departamento/planta inativos; usuário sem e-mail; duplicatas lógicas (mesma pessoa global **e** específica no mesmo departamento — legal, mas listada para revisão).

O mesmo relatório roda de novo **após** a Fase C como verificação (categoria 2 deve estar vazia ou aceita).

## 17. Compatibilidade com pedidos existentes

| Situação | Comportamento após Fase B |
|---|---|
| Pedido em `WAITING_AREA_APPROVAL` com `AreaApproverId` nomeado (modelo antigo) | Nomeado continua vendo (cláusula legada da fila) e aprovando (check legado da autorização); managers novos do dept/planta **também** podem — cobertura só aumenta |
| Pedido antigo com `PlantId NULL` | Resolução pula para managers globais do departamento (nível 2); sem fallback legado (estado final) |
| Pedido já decidido | Intocado — `AreaApproverId` histórico preservado, telas de histórico inalteradas |
| Draft criado antes do corte, submetido depois | Segue o fluxo novo no submit (resolução por dept+planta); a nomeação antiga que carregue é ignorada e sobrescrita apenas no ato da decisão |
| E-mails de eventos não relacionados a área | Inalterados (Finance/Buyer/AP/Requester já são plant-scoped ou diretos) |

## 18. Testes necessários

**Unitários — `ApprovalRoutingService`** (novos, projeto `AlplaPortal.Application.Tests` ou Infrastructure.Tests):

1. Nível 1: managers da planta específica retornados; globais não incluídos no `Resolve`.
2. Cascata: planta sem managers → globais; sem globais → `ResponsibleUserId`; sem nada → vazio + motivo.
3. Filtros: linha inativa, usuário inativo e e-mail vazio excluídos; linha inativa no nível 1 não "vaza" para nível 2 se restarem ativos no nível 1.
4. `pedido.PlantId NULL` → resolve por globais.
5. `IsAreaManagerAsync`: manager da planta ✓; manager global ✓ mesmo com managers específicos na planta (D1); manager de outra planta ✗; não-manager com role antiga ✗.
5b. Assimetria D1: com managers no nível 1, `Resolve` **não** inclui o global (e-mail), mas `IsAreaManagerAsync` do global retorna true (autorização).

**Integração (API):**

6. Submit sem nenhum manager resolvível → 400 com a mensagem nova.
7. Submit com 2 managers na planta → outbox com 2 e-mails `[AÇÃO NECESSÁRIA]`, dedup ok.
8. Aprovação por manager da planta → 200 e `AreaApproverId = actor`.
9. Aprovação por manager de **outra** planta → 403 (fix do T4 da investigação).
10. Aprovação por usuário com role manual "Area Approver" mas sem linha no cadastro → 403 (fix do T3).
11. Corrida: segundo manager decide após o primeiro → 409/400 "já decidido".
12. Batch com pedidos de plantas distintas → autorização avaliada por pedido.
13. Pedido legado nomeado → nomeado ainda aprova; manager novo também.
14. Fila: manager vê apenas seus dept+plantas; global vê todas as plantas do dept; admin vê tudo.
15. Feature flag off → comportamento legado integral (rollback de Fase B).
15b. **D3:** POST de manager para usuário sem escopos → `UserPlantScope`/`UserDepartmentScope` criados na mesma transação; response lista os escopos adicionados; manager global → escopos de todas as plantas ativas; remoção do manager não remove escopos.
15c. **D2:** relatório de reconciliação classifica corretamente os 5 casos (OK_DERIVADO, PERDE_ACESSO, SO_CADASTRO, INATIVO_COM_VINCULO, INCONSISTENTE) em um dataset de fixture com todos eles.

**Manuais em TEST (espelham T1–T10 da investigação, com expectativa nova):**

16. Cenário-alvo completo (Produção com A,B@V1, C@V2, D@V3, E global): submeter 1 pedido por planta e conferir destinatários exatos na outbox (`EMAIL_OUTBOX_QUEUED` no AdminLog).
17. Desativar todos os managers da V1 → novo submit da V1 roteia para E (global); desativar E também → submit bloqueado.
18. PaymentScheduled da V1 → informativo **não** chega a C/D (fix do T8).
19. Login de usuário recém-cadastrado como manager → menu Aprovações aparece sem role manual (claim derivada).
20. Regressão: fluxo de contratos e reatribuição de comprador (Local Manager) inalterados.

## 19. Riscos e decisões pendentes

| # | Item | Decisão | Status |
|---|---|---|---|
| D1 | Manager global pode aprovar quando a planta tem managers próprios? | **Sim** — autorização: manager específico da planta OU global; manager de outra planta **nunca**. E-mail: apenas o nível resolvido pela cascata | ✅ Confirmado 15/07/2026 |
| D2 | Atribuições manuais existentes da role "Area Approver" na Fase C | Role 100% derivada de `DepartmentManager`; atribuições manuais ignoradas. **Pré-requisito:** relatório de reconciliação (seção 16.1) com role manual × cadastro ativo × quem perde acesso × inativos × inconsistências, validado pelo negócio antes do corte | ✅ Confirmado 15/07/2026 |
| D3 | Manager sem escopo de visibilidade (`UserPlantScope`/`UserDepartmentScope`) na planta/dept que gere | **Auto-completar escopos ausentes** ao salvar o manager, na mesma transação, com aviso na UI antes e depois da ação. Invariante: não existe manager autorizado a aprovar mas sem visibilidade na fila | ✅ Confirmado 15/07/2026 |
| D4 | Concurrency rigorosa (`rowversion` em `Request`) | Adiar — guard de status cobre o caso prático | Adiado |
| R-a | Estreitamento da fila pode "sumir" pedidos para approvers genéricos atuais | Mitigação: janela de cadastro na Fase A + comparativo de fila antes/depois em TEST + changelog explícito | Aceito com mitigação |
| R-b | Seed incompleto (departamento sem responsável legado e sem cadastro novo) | Submit bloqueia com mensagem acionável; relatório pré-corte: query de departamentos sem manager | Aceito com mitigação |
| R-c | Fluxo de contratos continua role-based | Explícito como fora de escopo; role derivada preserva comportamento; redesign próprio depois | Aceito |
| R-d | `MasterData.tsx:662` filtra candidatos por role — circular com role derivada | Resolvido no plano (dropdown passa a listar todos os ativos) | Resolvido no plano |
