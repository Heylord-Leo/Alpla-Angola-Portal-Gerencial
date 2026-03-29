# debug_regressao_pos_ajuste

## Objetivo
Investigar e corrigir regressões introduzidas por uma alteração recente no projeto **Alpla Angola Portal Gerencial**, identificando a causa raiz e corrigindo o problema sem ampliar escopo desnecessariamente.

## Quando usar
Use esta diretiva quando algo que antes funcionava passa a falhar após um ajuste recente, especialmente em fluxos como:
- criação de pedido
- edição de pedido
- exclusão de rascunho
- numeração
- feedbacks
- itens do pedido
- regras condicionais por status/tipo

## Inputs
- Descrição clara do problema observado
- Última mudança ou prompt que antecedeu o bug
- Tela/fluxo afetado
- Mensagem de erro visível, se houver
- Arquivos ou módulos alterados recentemente

## Outputs
- causa raiz identificada
- correção aplicada
- arquivos modificados
- resumo técnico objetivo
- verificação manual realizada

## Ferramentas
- leitura do código recentemente alterado
- histórico local das mudanças
- execução local do backend/frontend
- browser DevTools
- Network tab
- Console
- logs do backend
- documentação do projeto quando relevante

## Procedimento

### 1. Reproduzir o problema
Não começar corrigindo no escuro.
Primeiro:
- reproduza o erro localmente
- confirme o fluxo exato que quebra
- identifique se o erro é sempre reproduzível ou intermitente

### 2. Delimitar a regressão
Compare o comportamento atual com o comportamento esperado.
Perguntas importantes:
- o que mudou recentemente?
- qual foi o último prompt/ajuste relacionado?
- a falha está no frontend, backend, integração, validação ou dados?

### 3. Inspecionar as últimas alterações
Revisar primeiro os arquivos tocados pela mudança mais recente.
Dar prioridade a:
- handlers de submit/click
- payloads enviados
- DTOs
- validações
- transações
- lógica de numbering
- condicionais por status/tipo
- efeitos colaterais em feedback/navigation

### 4. Verificar sinais técnicos
Conferir:
- Console do navegador
- request/response na aba Network
- status HTTP
- corpo da resposta com erro
- logs e stack trace do backend

### 5. Identificar a causa raiz
Não parar em sintoma superficial.
Exemplos de causa raiz real:
- campo removido no frontend mas ainda obrigatório no backend
- botão com `type=submit` quando deveria ser `button`
- request ID indefinido
- transação criada mas não concluída
- endpoint incorreto
- unique constraint colidindo
- estado antigo persistindo em navegação

### 6. Corrigir com escopo mínimo
Aplicar a menor correção correta possível.
Evitar:
- refatorações amplas sem necessidade
- redesign da tela
- mudanças em áreas não relacionadas

### 7. Revalidar o fluxo principal
Depois da correção:
- reproduzir novamente o cenário original
- confirmar que o problema sumiu
- testar também o fluxo feliz mais próximo
- testar um possível efeito colateral direto

### 8. Documentar apenas se houver impacto real
Se a regressão e sua correção alterarem comportamento documentado, atualizar apenas o necessário:
- `FRONTEND_FOUNDATION.md`
- `CHANGELOG.md`
- `DECISIONS.md` (se necessário)

## Perguntas-guia
- Qual foi a última mudança feita neste fluxo?
- A falha é frontend, backend ou contrato entre ambos?
- O erro aparece no console, na network ou só como mensagem genérica?
- Há validação inconsistente entre tela e API?
- Houve quebra por mudança de estado, rota ou payload?
- O backend continua sendo a barreira final correta?

## Checklist mínimo
- erro reproduzido
- causa raiz identificada
- correção aplicada
- fluxo original revalidado
- efeito colateral direto testado
- erros silenciosos eliminados
- documentação revisada apenas se impactada

## Critério de pronto
Considerar a regressão corrigida apenas se:
- a causa raiz estiver identificada claramente
- a correção resolver o problema original
- o fluxo relacionado continuar funcionando
- não houver nova quebra óbvia no mesmo caminho
- houver evidência mínima de validação manual

## Edge cases
- erro mascarado por mensagem genérica no frontend
- botão presente sem handler funcional
- endpoint correto mas payload inválido
- rota correta com ID nulo
- feedback impedindo leitura do erro real
- mudança recente quebrando fluxo anterior já estável
- correção parcial que resolve um caso e quebra outro no mesmo post-create flow

## Important project rules

do not work on deployment

focus only on this conditional UX correction

if you find a directly related issue in the same post-create flow, fix it too

## Documentation maintenance

If this changes the documented UX behavior, update only what is truly impacted, especially:

FRONTEND_FOUNDATION.md

CHANGELOG.md

DECISIONS.md (if needed)
