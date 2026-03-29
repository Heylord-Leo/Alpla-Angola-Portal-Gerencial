# Diretiva - Regras de Status e Permissoes

## Objetivo

Garantir que ações de UI e operações de backend no projeto **Alpla Angola Portal Gerencial** respeitem o status do pedido e as permissões do usuário, com o backend atuando como barreira final de segurança.

## Inputs esperados

- status atual do pedido
- ação que está sendo implementada ou ajustada
- papel do usuário ou regra de acesso aplicável
- comportamento atual da tela e endpoints envolvidos

## Saídas esperadas

- ações visíveis apenas quando fizerem sentido
- mutações bloqueadas em estados não editáveis
- backend validando novamente a regra, independentemente da UI

## Princípios obrigatórios

### 1. UI não é a única proteção

Mesmo que um botão esteja oculto no frontend, o backend deve validar a regra novamente.

### 2. Status governa a ação

A disponibilidade de ações deve depender do status real do pedido.

### 3. Permissão e status trabalham juntos

Não basta o usuário ter acesso à tela. A operação também precisa fazer sentido para o estado atual do documento.

## Regras práticas

### Pedido editável

Considerar como editável apenas os estados oficialmente permitidos pelo projeto, com prioridade para:

- `DRAFT`
- estados equivalentes de retrabalho, se implementados

Nestes casos, pode haver:

- edição do cabeçalho permitida (bloqueada em WAITING_QUOTATION para preservar intenção original)
- adição, edição e remoção de itens
- exclusão de rascunho, se a regra do projeto permitir

### Pedido não editável

Em estados não editáveis:

- ocultar ou desabilitar ações de mutação
- impedir add, edit e delete de itens
- impedir exclusão de rascunho
- manter backend retornando erro apropriado caso a operação seja tentada diretamente

## Padrões esperados

### Frontend

- botões condicionais por `statusCode`
- mensagens claras quando uma operação falhar
- não deixar ação visível se a regra bloquear completamente seu uso

### Backend

- validar existência do pedido
- validar status permitido para a operação
- validar permissão/autorização do usuário
- retornar erro consistente, como `400`, `403` ou `409`, conforme o caso

## Exemplos de aplicação

### Excluir rascunho

- mostrar botão apenas em `DRAFT`
- confirmar ação com o usuário
- backend só permite exclusão se status continuar sendo `DRAFT`

### Gerenciar cotações (Workspace)

- permitir add/edit/delete apenas em pedido editável
- backend deve bloquear mutação em pedido não editável, mesmo se a rota for chamada manualmente

### Gerenciar anexos do pedido

- permitir remoção apenas em estados editáveis (`DRAFT`, `AREA_ADJUSTMENT`, `FINAL_ADJUSTMENT`, `WAITING_QUOTATION`)
- backend deve bloquear a exclusão em qualquer outro status para preservar a integridade do processo auditável
- adição de anexos é permitida em estágios operacionais pós-aprovação para cumprir requisitos de workflow (ex: Comprovante de Pagamento)

### Campos sensíveis do workflow

- não expor ao solicitante campos que pertençam à etapa do aprovador ou fluxo posterior
- se necessário, manter o campo no backend/modelo mas fora da UI do requester

## Critérios de pronto

A implementação está correta quando:

1. a UI mostra apenas ações válidas para o status atual
2. o backend impede operações inválidas
3. não há inconsistência entre o que a tela permite e o que a API aceita
4. cenários de erro retornam feedback compreensível

## Edge cases

- usuário abre tela em status editável, mas outro processo altera o status antes do save
- usuário tenta chamar endpoint manualmente fora da UI
- item ou pedido já foi removido quando a ação é enviada
- feedback de erro silencioso no frontend
- status exibido na tela desatualizado em relação ao backend

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
