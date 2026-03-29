---
description: Finalização, registro oficial e commit/push da tarefa
---

> [!IMPORTANT]
> Este é o **único** workflow autorizado para realizar `git commit` e `git push`.
> Siga rigorosamente o **`directives/SOP_TASK_LIFECYCLE.md`**.

1. Executar todos os passos de `/task-review`.
2. Se houver alteração instável ou não relacionada (unrelated), interromper.
3. Atualizar `docs/CHANGELOG.md` e `docs/VERSION.md` (Pode ser manual ou automático).
4. Reconciliar `directives/` e `docs/` se necessário.
5. Criar commit em **Conventional Commits** (`feat:`, `fix:`, `docs:`, etc.).
6. Enviar para `origin/main` se o trabalho estiver estável e coerente.
