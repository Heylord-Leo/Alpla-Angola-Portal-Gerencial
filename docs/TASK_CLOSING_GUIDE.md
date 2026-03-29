# Guia de Fechamento de Tarefas

Este guia explica como usar o novo fluxo de fechamento de tarefas no projeto **Alpla Angola Portal Gerencial**.

## Por que usar?

Seguir este fluxo garante que:
- O cdigo no repositrio seja sempre estvel.
- A documentao (`docs/` e `directives/`) acompanhe as mudanas.
- O versionamento (`CHANGELOG.md`) seja preciso e humano.

---

## Modos de Operao

### 1. Reviso (Review Mode)
**Comando**: `/task-review`

**Quando usar**:

- Quando terminar uma subtarefa ou parte de um recurso.
- Para verificar o impacto antes de decidir se deve commitar.
- Para validar se o versionamento sugerido faz sentido.

**O que ele faz**: Analisa mudanas, sugere a prxima verso (Patch/Minor/Major) e aponta documentos que precisam de ateno. **No faz alteraes no Git.**

### 2. Publicar (Publish Mode)
**Comando**: `/task-publish`

**Quando usar**:

- Quando a tarefa estiver 100% concluda e testada.
- Quando o trabalho formar uma unidade coerente e estvel.

**O que ele faz**: Realiza a reviso, atualiza o `CHANGELOG.md` e `VERSION.md`, cria o commit (Conventional Commits) e faz o `push` para o GitHub.

---

## Regras de Versionamento (Judgment)

No usamos incremento automtico cego. Avaliamos o impacto:

- **PATCH** (ex: 2.9.12 -> 2.9.13):
  - Correes de bugs.
  - Ajustes de estilo ou textos.
- **MINOR** (ex: 2.9.12 -> 2.10.0):
  - Novas funcionalidades funcionais.
  - Melhorias significativas em workflows ou ferramentas do agente.
- **MAJOR** (ex: 2.9.12 -> 3.0.0):
  - Mudanas que quebram retrocompatibilidade.
  - Marcos de entrega de grandes mdulos (Stages).

---

## Dicas para o Dia a Dia

- **Trabalho Incompleto**: Se as mudanas no working tree estiverem "misturadas" (metade de um bugfix e metade de uma feature nova), o modo Publicar vai parar. Limpe ou separe antes.
- **Mensagens de Commit**: Elas sero geradas no formato `feat: descrio`, `fix: descrio`, etc., mas sempre priorizando clareza para humanos.
- **Referncia de Verso**: O `CHANGELOG.md`  sempre a verdade absoluta. O `VERSION.md` apenas o segue.
