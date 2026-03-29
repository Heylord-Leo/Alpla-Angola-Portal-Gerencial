# Diretiva - Fechar Tarefa (Reviso e Publicar)

## Objetivo

Garantir que cada tarefa concluda no portal siga um processo padronizado de reviso, reconciliao de documentao e versionamento antes de ser oficialmente entregue ao repositrio.

## Fluxo de Fechamento

### 1. Reviso (`/task-review`)

**Foco**: Anlise de qualidade e impacto, sem alteraes permanentes.

1. **Status do Git**: Identificar arquivos modificados e no rastreados.
2. **Anlise de Alteraes**: Revisar o que foi implementado em termos de cdigo e UX.
3. **Avaliao de Documentao**: Verificar se `directives/`, `docs/`, `CHANGELOG.md` e `VERSION.md` precisam de atualizao.
4. **Impacto de Versionamento**: Avaliar separadamente o impacto funcional no Frontend e Backend.
    - **PATCH**: Correo pontual ou bugfix.
    - **MINOR**: Nova funcionalidade ou melhoria de workflow significativa.
    - **MAJOR**: Alterao estrutural ou milestone importante.
5. **Recomendao**: Informar se o trabalho est pronto para commit ou se deve aguardar alteraes relacionadas.

### 2. Publicar (`/task-publish`)

**Foco**: Finalizao, registro oficial e commit/push.

1. **Reviso Final**: Executar todos os passos da Reviso.
2. **Verificao de Integridade**: Se houver alteraes pendentes ou misturadas (unrelated/unfinished), o processo deve interromper e solicitar limpeza.
3. **Atualizao de Docs**: Aplicar mudanas necessrias em `docs/` e `directives/`.
4. **Versionamento**:
    - Atualizar `docs/CHANGELOG.md` com a descrio concisa da entrega.
    - Atualizar `docs/VERSION.md` se for uma verso de entrega real.
5. **Reincio de Servios**: Se o cdigo foi alterado, reiniciar backend e frontend antes da confirmao final (se solicitado pelo fluxo).
6. **Commit**: Criar commit usando **Conventional Commits** (`feat:`, `fix:`, `docs:`, `refactor:`, etc.).
7. **Push**: Enviar para `origin/main` somente se o trabalho estiver estvel e coerente.

## Regras de Ouro

- Nunca publique se o trabalho no for uma unidade estvel e funcional.
- O `CHANGELOG.md`  a referncia principal de verso; o `VERSION.md` deve segui-lo.
- Se houver dvida sobre o nvel de versionamento (Patch/Minor/Major), solicite confirmao do usurio.
