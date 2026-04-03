# Diretiva - Implementar Request Items

## Objetivo

Implementar o próximo vertical slice do projeto **Alpla Angola Portal Gerencial** para gestão de itens de pedido (`RequestLineItem`) dentro do fluxo existente de **Request Edit**.

O usuário deve poder:

1. adicionar item(s) ao pedido
2. visualizar os itens atuais em lista ou grid
3. editar item(s)
4. remover item(s)
5. visualizar o total do pedido atualizado corretamente

## Inputs esperados

- código-fonte em `C:\dev\alpla-portal`
- documentação do projeto, especialmente:
  - `DECISIONS.md`
  - `API_V1_DRAFT.md`
  - `DATA_MODEL_DRAFT.md`
  - `FRONTEND_FOUNDATION.md`
  - `ACCESS_MODEL.md`
  - `PROJECT_OVERVIEW.md`
- implementação atual de `RequestEdit`
- infraestrutura backend já existente para `Request` e possíveis endpoints de `RequestLineItem`

## Saídas esperadas

- seção **Items** implementada dentro de `RequestEdit`
- grid ou tabela com os itens atuais do pedido
- ações funcionais de adicionar, editar e remover itens
- total do pedido atualizado de forma consistente
- resumo técnico com arquivos modificados e verificação manual

## Regras funcionais

### 1. Modelo correto

- seguir a direção já definida de **1:N** entre `Request` e `RequestLineItem`
- não criar modelo paralelo de item
- reutilizar entidades e endpoints existentes, se já houver infraestrutura parcial

### 2. UX esperada

- usar **grid/lista primeiro** para os itens
- manter UX simples, limpa e coerente com o portal atual
- **Migração Shell 2.0**: Para novos componentes (v2), utilizar `TailwindCSS` mapeado rigorosamente para `tokens.css` seguindo o design **Modern Corporate** (8px-12px radius, soft elevations). Não criar CSS customizado fora do padrão.
- não redesenhar partes não relacionadas da tela
- reutilizar o padrão de feedback já adotado no projeto

### 3. Edição condicionada por status

- permitir mutação de itens apenas quando o pedido estiver em estado editável
- respeitar principalmente `DRAFT` e qualquer estado equivalente de retrabalho, se já existir
- em estados não editáveis, ocultar ou desabilitar ações de mutação

### 4. Totais

- o backend deve ser a fonte de verdade para totalização sempre que possível
- evitar duplicação frágil de regra de negócio no frontend
- após add, edit ou delete, atualizar a lista e os totais

## Implementação técnica esperada

### Backend

Verificar se já existem rotas, entidades e serviços para itens do pedido.

Se estiverem incompletos, implementar apenas o necessário para este slice:

- listar itens por pedido
- criar item
- editar item
- remover item
- retornar total atualizado do pedido ou dados suficientes para recarregar corretamente

### Frontend

Dentro de `RequestEdit.tsx`:

- adicionar a seção **Items**
- renderizar os itens em uma tabela ou grid simples
- implementar add, edit e remove
- pedir confirmação antes de remover
- atualizar a UI sem full page reload

## Critérios de pronto

A tarefa só pode ser considerada concluída se:

1. um pedido `PAYMENT` puder receber pelo menos 2 itens
2. os itens aparecerem persistidos após refresh
3. o total recalcular corretamente após add, edit e remove
4. a UI não quebrar com input inválido
5. não houver erro React ou falha backend no fluxo testado
6. ações de item respeitarem as regras de status/permissão

## Verificação manual mínima

1. criar um pedido `PAYMENT`
2. cair em `RequestEdit`
3. adicionar 2 itens
4. confirmar exibição no grid
5. confirmar total correto
6. editar 1 item e validar novo total
7. remover 1 item e validar novo total
8. atualizar a página e confirmar persistência
9. testar comportamento em pedido não editável

## Edge cases

- item com campos obrigatórios vazios
- quantidade zero ou inválida
- valor unitário inválido
- exclusão cancelada pelo usuário
- erro de API durante criação, edição ou exclusão
- pedido em status não editável
- inconsistência entre total exibido e total persistido

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
