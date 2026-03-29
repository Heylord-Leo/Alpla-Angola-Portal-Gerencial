---
description: Finalizao, registro oficial e commit/push da tarefa
---

1. Executar todos os passos de `/task-review`.
2. Se houver alterao instvel ou no relacionada (unrelated), interromper.
3. Atualizar `docs/CHANGELOG.md` e `docs/VERSION.md` (Pode ser manual ou automtico).
4. Reconciliar `directives/` e `docs/` se necessrio.
5. Criar commit em **Conventional Commits** (`feat:`, `fix:`, `docs:`, etc.).
6. Enviar para `origin/main` se o trabalho estiver estvel e coerente.
