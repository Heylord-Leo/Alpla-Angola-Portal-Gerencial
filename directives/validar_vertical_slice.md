# validar_vertical_slice

## Objetivo
Validar uma entrega de vertical slice no projeto **Alpla Angola Portal Gerencial** de forma funcional, técnica e documental antes de considerar a etapa concluída.

## Quando usar
Use esta diretiva quando uma implementação estiver finalizada e for necessário verificar se o fluxo está realmente estável, coerente com o projeto e pronto para seguir para o próximo passo.

## Inputs
- Descrição da funcionalidade entregue
- Arquivos alterados
- Fluxo funcional esperado
- Regras de status/permissão relacionadas
- Documentação do projeto aplicável

## Outputs
- Resumo objetivo do que foi validado
- Lista de testes executados
- Bugs, inconsistências ou riscos encontrados
- Confirmação do que está pronto
- Indicação do que ainda precisa ser corrigido

## Ferramentas
- Leitura de código backend/frontend
- Execução local do sistema
- Testes manuais no navegador
- Console do navegador
- Aba Network
- Logs do backend
- Documentação do projeto (`DECISIONS.md`, `API_V1_DRAFT.md`, `FRONTEND_FOUNDATION.md`, `ACCESS_MODEL.md`, `CHANGELOG.md`, etc.)

## Procedimento

### 1. Entender o slice validado
Antes de testar, identifique:
- qual fluxo foi implementado
- qual comportamento era esperado
- quais arquivos foram alterados
- quais estados/status devem permitir ou bloquear a ação

### 2. Validar o cenário feliz principal
Executar o fluxo principal completo de ponta a ponta.
Exemplos:
- criar pedido
- redirecionar corretamente
- editar pedido
- adicionar item
- salvar alteração
- excluir rascunho

Confirmar:
- ação concluída com sucesso
- feedback visual correto
- persistência dos dados
- navegação esperada

### 3. Validar cenário de erro
Forçar pelo menos um erro funcional relevante:
- campo obrigatório vazio
- operação inválida por status
- falha de API simulada ou observada

Confirmar:
- erro aparece de forma clara
- não há falha silenciosa
- a tela não quebra
- o usuário não perde controle do fluxo

### 4. Validar persistência
Após concluir a ação:
- atualizar a página
- reabrir o registro
- voltar para lista
- navegar novamente

Confirmar que:
- os dados permanecem corretos
- mensagens temporárias não reaparecem indevidamente
- totais e listas permanecem consistentes

### 5. Validar regras por status e permissão
Testar a mesma tela ou ação em estados diferentes quando aplicável.
Confirmar:
- ações visíveis apenas quando permitidas
- backend bloqueia operações indevidas mesmo se o frontend falhar
- não existe brecha por manipulação direta da UI

### 6. Validar efeitos colaterais
Checar se a mudança criou regressão em áreas próximas do mesmo fluxo.
Exemplos:
- pós-criação
- feedbacks
- numeração
- totals
- navegação
- botão visível mas sem ação

### 7. Validar frontend tecnicamente
Abrir DevTools e conferir:
- erros no Console
- requests corretos na Network
- ausência de warnings React relevantes
- ausência de loops, duplicidade de requests ou comportamento inesperado

### 8. Validar backend tecnicamente
Conferir:
- logs do backend
- exceções silenciosas
- validações incorretas
- problemas de concorrência simples
- inconsistências de persistência

### 9. Revisar documentação impactada
Se a entrega mudou comportamento documentado, atualizar apenas o necessário:
- `FRONTEND_FOUNDATION.md`
- `CHANGELOG.md`
- `DECISIONS.md` (se necessário)

Não atualizar documentação sem impacto real.

## Checklist mínimo de validação
- fluxo feliz validado
- fluxo de erro validado
- persistência validada após refresh
- regras por status/permissão validadas
- feedback visual validado
- console limpo de erros relevantes
- backend sem falhas silenciosas
- documentação revisada apenas se impactada

## Critério de pronto
Considerar o vertical slice validado apenas se:
- o fluxo principal funcionar de ponta a ponta
- os dados persistirem corretamente
- não houver regressão evidente no mesmo fluxo
- erros estiverem tratados com feedback adequado
- regras de status/permissão estiverem respeitadas
- documentação essencial estiver coerente

## Edge cases
- botão visível sem ação real
- feedback que não some ou reaparece indevidamente
- operação permitida no frontend mas rejeitada no backend
- dados órfãos após exclusão
- numeração duplicada
- total incorreto após add/edit/remove
- ação funcionando só sem refresh
- erro mascarado por mensagem genérica

## Important project rules

do not work on deployment

focus only on this conditional UX correction

if you find a directly related issue in the same post-create flow, fix it too

## Documentation maintenance

If this changes the documented UX behavior, update only what is truly impacted, especially:

FRONTEND_FOUNDATION.md

CHANGELOG.md

DECISIONS.md (if needed)
