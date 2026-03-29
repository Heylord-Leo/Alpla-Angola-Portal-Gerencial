# Diretiva - Revisar Numeracao de Pedidos

## Objetivo

Revisar, corrigir e validar a logica de geracao de numeros de pedido no projeto **Alpla Angola Portal Gerencial**, garantindo unicidade, nao reutilizacao e comportamento seguro mesmo apos exclusao de rascunhos ou criacoes concorrentes.

## Inputs esperados

- codigo-fonte em `C:\dev\alpla-portal`
- implementacao atual de geracao de `RequestNumber`
- entidades e persistencia relacionadas a numeracao, como contadores, sequences, constraints ou servicos equivalentes
- formato funcional esperado do numero do pedido: `REQ-DD/MM/YYYY-SequentialNumber`
- fluxo atual de criacao de pedidos e exclusao de rascunhos

## Saidas esperadas

- logica de numeracao revisada e consistente (Unified `REQ` format)
- numero de pedido unico e nao reutilizavel (Global persistent counter)
- protecao contra duplicidade por concorrencia
- resumo tecnico com causa raiz, arquivos alterados e verificacao manual

## Regras funcionais

### 1. Numero de pedido nao pode ser reutilizado

Mesmo que um rascunho seja excluido:

- o numero ja usado nao volta a ficar disponivel
- o proximo pedido deve continuar a sequencia correta
- buracos na sequencia sao aceitaveis

Exemplo:

- REQ-01/03/2026-023
- REQ-01/03/2026-024
- REQ-01/03/2026-025
- excluir 024 e 025
- proximo pedido deve ser 026, nunca 024 ou 025

### 2. A numeracao nao deve depender apenas dos registros restantes

Nao basear a logica em:

- buscar o ultimo registro existente (Scanning tables)
- contar registros atuais na tabela
- recalcular sequencia a partir do conjunto remanescente

A fonte de verdade deve ser o `GLOBAL_REQUEST_COUNTER` persistente.

### 3. A geracao deve ser controlada pelo backend

- o frontend nao deve montar ou sugerir o numero final do pedido
- o backend deve ser a fonte de verdade
- o numero deve ser alocado de forma transacional ou equivalente

### 4. Identificadores de negocio devem usar chaves estaveis

- O `GLOBAL_REQUEST_COUNTER` deve ser unico e independente do tipo de pedido.
- Nao depender de `RequestTypeId` para a chave do contador, pois o prefixo agora e unificado (`REQ`).

### 5. Deve haver barreira final contra duplicidade

Sempre que possivel, adicionar protecao de banco para `RequestNumber`, como:

- indice unico
- constraint unica

A aplicacao nao deve confiar apenas na logica do codigo.

## Implementacao tecnica esperada

### Backend

Inspecionar e ajustar, conforme necessario:

- `CreateRequest`
- Formato final: `$"REQ-{today:dd/MM/yyyy}-{counter:D3}"`
- Uso de `GLOBAL_REQUEST_COUNTER` no `SystemCounters`
- Lock adequado no `SaveChangesAsync` do contador antes da insercao do pedido

### Banco de dados

- Entidade `SystemCounter`
- Indice unico em `RequestNumber`

### Frontend

- Exibicao do numero unificado.
- NUNCA inferir o tipo do pedido a partir do prefixo `REQ`. Use `requestTypeCode`.

## Critarios de pronto

A tarefa so pode ser considerada concluida se:

1. excluir rascunhos nao permitir reutilizacao de numero
2. criacoes subsequentes seguirem a sequencia correta (Global/Monotonic)
3. criacoes quase simultaneas nao gerarem duplicidade
4. o novo formato `REQ-DD/MM/YYYY-###` for respeitado
5. houver protecao final em banco contra duplicidade
6. a solucao nao depender de IDs numericos instaveis para chaves de negocio

## Verificacao manual minima

1. criar 3 pedidos em sequencia
2. anotar os numeros
3. excluir um ou mais rascunhos
4. criar novo pedido
5. confirmar que o numero continua em frente e nao reutiliza antigo
6. testar criacao rapida em sequencia
7. validar que `QUOTATION` e `PAYMENT` usam o mesmo prefixo `REQ`
8. confirmar ausencia de erro de duplicidade inesperada em fluxo normal

## Edge cases

- contador ainda nao existe na base
- duas criacoes simultaneas tentando alocar o mesmo numero
- constraint unica disparando por colisao de formato
- logica reiniciando sequencia de forma incorreta por data, tipo ou ambiente
- dependencia de `RequestTypeId` mudando entre migrations ou seeds
- exclusao fisica de rascunho alterando indevidamente a leitura do "ultimo numero"

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
