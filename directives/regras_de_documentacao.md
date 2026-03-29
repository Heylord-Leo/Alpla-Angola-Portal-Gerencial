# Diretiva - Regras de Documentacao

## Objetivo

Manter a documentação do projeto **Alpla Angola Portal Gerencial** consistente, mínima e alinhada às mudanças reais, sem gerar atualizações excessivas ou irrelevantes.

## Inputs esperados

- alteração implementada no código
- impacto funcional ou de UX observado
- arquivos de documentação existentes no projeto

## Saídas esperadas

- atualização apenas dos arquivos de documentação realmente impactados
- changelog coerente com a mudança
- decisão arquitetural atualizada apenas se a mudança alterar uma decisão de projeto

## Regra principal

**Atualizar somente o que foi realmente impactado.**

Não reescrever documentação ampla sem necessidade.

## Ordem de análise

### 1. `FRONTEND_FOUNDATION.md` e `UI_UX_GUIDELINES.md`

Atualizar somente se houver mudança real em:

- comportamento de UX
- padrão de feedback
- estrutura de tela
- padrão reutilizável de componentes frontend ou **novos tokens do Tailwind** mapeados do CSS
- fluxo oficial de interação do usuário

### 2. `CHANGELOG.md`

Atualizar quando houver:

- correção funcional relevante
- melhoria de UX
- novo componente reutilizável
- novo endpoint ou capacidade funcional visível

A entrada deve ser objetiva e proporcional ao impacto.

### 3. `DECISIONS.md`

Atualizar apenas se a implementação:

- introduzir uma nova decisão arquitetural
- alterar uma decisão formal existente
- consolidar uma regra estrutural que precisa permanecer registrada

Não usar `DECISIONS.md` para pequenos ajustes de tela que não alteram a direção do projeto.

## Critérios de pronto

A documentação está adequada quando:

1. reflete a mudança real implementada
2. não contém informação além do necessário
3. não contradiz comportamento atual do sistema
4. permanece consistente com os outros MDs principais

## Edge cases

- mudança técnica sem impacto de UX: atualizar apenas `CHANGELOG.md`, se relevante
- mudança de UX sem decisão arquitetural: atualizar `FRONTEND_FOUNDATION.md` e `CHANGELOG.md`
- mudança estrutural de domínio ou arquitetura: considerar `DECISIONS.md`
- documentação antiga divergente: corrigir apenas a parte comprovadamente afetada

## Regras fixas do projeto

### Important project rules

- do not work on deployment
- focus only on this conditional UX correction
- if you find a directly related issue in the same post-create flow, fix it too

### Documentation maintenance

If this changes the documented UX behavior, update only what is truly impacted, especially:

- `FRONTEND_FOUNDATION.md`
- `CHANGELOG.md`
- `DECISIONS.md` (if needed)
