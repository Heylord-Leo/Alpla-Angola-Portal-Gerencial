# Diretiva - Corrigir Fluxo Pos-Criacao

## Objetivo

Ajustar o comportamento do fluxo imediatamente apos a criacao de pedidos no projeto **Alpla Angola Portal Gerencial**, com foco em UX condicional, feedback ao usuario e navegacao correta conforme o tipo e o estado do pedido.

## Inputs esperados

- codigo-fonte em `C:\dev\alpla-portal`
- comportamento atual observado apos criar um pedido
- tipo do pedido (`QUOTATION`, `PAYMENT` ou equivalente)
- regras funcionais ja definidas em documentacao e implementacao existente
- componentes e paginas envolvidos no post-create flow, especialmente:
  - `RequestCreate`
  - `RequestsList`
  - `RequestEdit`
  - componentes reutilizaveis de feedback

## Saidas esperadas

- redirecionamento correto apos a criacao do pedido
- feedback visivel, claro e coerente com o fluxo
- comportamento condicional por tipo de pedido e por status
- ausencia de mensagens persistentes indevidas ou silencios de erro
- resumo tecnico com arquivos alterados e verificacao manual

## Regras funcionais

### 1. O pos-criacao deve respeitar o tipo de pedido

Exemplos esperados, se esta for a regra vigente no projeto:

- `QUOTATION`: redirecionar para lista e exibir mensagem orientando a aguardar cotacao
- `PAYMENT`: redirecionar para a tela de edicao para permitir inclusao de itens

Nao aplicar redirecionamento generico se a regra do projeto exigir comportamento condicional.

### 2. O feedback deve ser util e temporario

- exibir mensagem de sucesso ou erro de forma clara
- permitir fechamento manual quando houver componente reutilizavel para isso
- usar auto-close quando este for o padrao vigente do projeto
- evitar que mensagens antigas reaparecam indevidamente apos refresh ou navegacao

### 3. Erros nao podem falhar silenciosamente

Se a criacao falhar:

- mostrar feedback de erro ao usuario
- registrar causa suficiente para diagnostico no console/log, se apropriado
- nao redirecionar como se a operacao tivesse sido bem-sucedida

### 4. Navegacao deve ser deterministica

- o usuario deve parar na tela correta apos criar o pedido
- o comportamento nao deve depender de IDs numericos fixos se houver campo `Code` ou equivalente mais estavel
- limpar estados temporarios de navegacao apos consumo do feedback, quando aplicavel

## Implementacao tecnica esperada

### Frontend

Verificar e ajustar, conforme necessario:

- payload de criacao
- consumo de lookups (`request type`, `status`, etc.)
- logica de decisao apos criacao
- navegacao com `navigate(...)`
- transporte de feedback via estado de navegacao ou estado local
- componentes de feedback reutilizaveis
- scroll/destaque visual, se fizer parte do fluxo atual

### Backend

Somente ajustar se houver causa diretamente relacionada ao pos-criacao, como:

- resposta de criacao incompleta
- falta de campo necessario para o frontend decidir o fluxo
- inconsistencias no retorno do tipo/status

Nao expandir escopo para outras areas nao relacionadas.

## Critarios de pronto

A tarefa so pode ser considerada concluida se:

1. `QUOTATION` seguir o redirecionamento e feedback corretos
2. `PAYMENT` seguir o redirecionamento e feedback corretos
3. mensagens de sucesso/erro nao ficarem presas indefinidamente sem regra definida
4. o fluxo permanecer funcional apos refresh onde aplicavel
5. erros nao ocorrerem silenciosamente
6. o comportamento estiver alinhado com a UX documentada do projeto

## Verificacao manual minima

1. criar um pedido `QUOTATION`
2. confirmar redirecionamento para a lista
3. confirmar feedback correto
4. criar um pedido `PAYMENT`
5. confirmar redirecionamento para edicao
6. confirmar feedback correto
7. testar falha de criacao e validar feedback de erro
8. atualizar a pagina e garantir que a mensagem nao reapareca indevidamente
9. verificar console e network para ausencia de falhas silenciosas

## Edge cases

- lookup de tipo ainda nao carregado no momento do submit
- `Code` ausente ou divergente do esperado
- mensagem de sucesso persistindo alem do necessario
- erro de API mascarado por mensagem generica sem diagnostico
- redirecionamento incorreto apos alteracoes recentes na UX
- refresh reexibindo mensagem ja consumida
- comportamento diferente entre ambientes por dependencia de IDs fixos

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
