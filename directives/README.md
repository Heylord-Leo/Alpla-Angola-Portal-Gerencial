# Antigravity Directives - Alpla Angola Portal Gerencial

Este pacote reúne diretivas base para orientar o Antigravity com mais consistência no projeto **Alpla Angola Portal Gerencial**.

## Como usar

Ao abrir uma nova tarefa, referencie explicitamente a diretiva aplicável no prompt, por exemplo:

- Siga `implementar_request_items.md`
- Aplique também `regras_de_documentacao.md`
- Respeite `regras_de_status_e_permissoes.md`

## Diretivas incluídas

0. `SOP_TASK_LIFECYCLE.md`
   - **MANDATÓRIO**: SOP de ciclo de vida de tarefa (Análise, Plano, Checks e Fechamento Seguro)

1. `implementar_request_items.md`
   - Implementação do vertical slice de itens do pedido (`RequestLineItem`) dentro de `RequestEdit`

2. `regras_de_documentacao.md`
   - Regras para manutenção mínima e precisa da documentação do projeto

3. `regras_de_status_e_permissoes.md`
   - Regras funcionais para edição, ações condicionais por status e barreiras de backend

4. `fechar_tarefa.md`
   - SOP padronizado para revisão, versionamento e publicação de tarefas

## Regras padrão do projeto

Sempre que abrir uma nova tarefa, o primeiro passo deve ser a leitura de `SOP_TASK_LIFECYCLE.md` para garantir o cumprimento do fluxo de governança.

### Important project rules

- do not work on deployment
- focus only on this conditional UX correction
- if you find a directly related issue in the same post-create flow, fix it too

### Documentation maintenance

If this changes the documented UX behavior, update only what is truly impacted, especially:

- `FRONTEND_FOUNDATION.md`
- `CHANGELOG.md`
- `DECISIONS.md` (if needed)

## Observação

Estas diretivas não substituem o contexto do ticket atual. Elas servem para reduzir ambiguidade, manter consistência arquitetural e evitar retrabalho.
