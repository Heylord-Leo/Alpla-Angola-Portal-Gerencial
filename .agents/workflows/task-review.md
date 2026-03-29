---
description: Revisão do estado da tarefa sem commit ou push
---

> [!NOTE]
> Este workflow é destinado apenas à análise e validação.
> Siga rigorosamente o **`directives/SOP_TASK_LIFECYCLE.md`**.
> **NUNCA** realize commit ou push a partir deste comando.

1. **Status do Git**: Identificar arquivos modificados e não rastreados.
2. **Análise de Alterações**: Revisar o que foi implementado em termos de código e UX.
3. **Avaliação de Documentao**: Verificar se `directives/`, `docs/`, `CHANGELOG.md` e `VERSION.md` precisam de atualização.
4. **Impacto de Versionamento**: Avaliar o impacto (PATCH/MINOR/MAJOR).
5. **Recomendação**: Informar se o trabalho está pronto para o comando `/task-publish`.
