# Guião de testes manuais em TEST — v2.249.0 (cancelamento de grupo P.O. / devolução à cotação)

> Versão alvo: **v2.249.2** (correções da v2.249.1 encontradas neste guião após o cancelamento do grupo S1: item libertado
> mostrado como “pronto para lote”, sem entrada de cotação, navegação da devolução, “Corrigir P.O.” antes do primeiro registo,
> textos do tipo de devolução, estado de entrega dos e-mails), build esperado **2.249.2+<sha do commit da v2.249.2>**
> (a confirmar após publicação; a v2.249.1 era 2.249.1+9de44f1, a v2.249.0 era 2.249.0+c40ebb3), ramo `Portal-Gerencial-rev1`.
> O pedido **REQ-28/10/2026-628** já criado em TEST **é reaproveitado** no estado em que ficou (grupo S1 TREVOTECH cancelado — voltar a
> cotar, confirmação de Finanças pendente; grupo S2 MULTIBIZ com P.O. emitida; nenhuma nova cotação): ver **§3.4-bis** e §3.5. Não
> recriar nem “reparar” o pedido.
> Preparado a partir do código implementado (ecrãs, rótulos, permissões) e de `docs/PO_GROUP_CANCELLATION.md`.
> Executado manualmente pelo proprietário. O agente **não** acedeu a TEST/PROD, não executou workflows, não alterou dados.
> **Nunca** usar REQ-27/09/2026-483 nem qualquer pedido operacional real. Só pedidos sintéticos com título a começar por **`TEST v2.249.0`**.
> Nenhum passo deste guião altera o Primavera nem executa pagamentos reais.

---

## 0. Visão geral — contas e pedidos sintéticos

### 0.1 Contas necessárias (planta de teste = **P1**; segunda planta = **P2**)

| Código | Perfil (papéis no Portal) | Planta(s) | Para quê | Pode ser a sua conta? |
|---|---|---|---|---|
| **BUYER** | Buyer (comprador) | P1 | cria cotações, submete lotes, regista/cancela P.O., declara ECF | Sim, se tiver o papel Buyer e for o comprador responsável |
| **REQ** | Solicitante (utilizador normal) | P1 | cria o pedido sintético; recebe aviso in-app + e-mail do cancelamento | Pode ser a mesma pessoa que BUYER (o aviso ao solicitante é então o seu) |
| **AREA** | Area Approver do departamento do pedido | P1 | aprovação de área de cada lote | Outra conta (recomendado) |
| **FINAL** | Final Approver | P1 | aprovação final de cada lote | Outra conta |
| **FIN1** | Finance (ativo) com âmbito de planta P1 | P1 | devolve o pedido; **confirma/rejeita** a anulação da ECF | Outra conta — **não** pode ser quem declarou |
| **FIN2** | Finance (ativo) com âmbito P1, pessoa diferente de FIN1 | P1 | prova “dois utilizadores Finanças da planta recebem e-mail”; alternativa de revisão | Outra conta |
| **FINP2** | Finance (ativo) com âmbito **apenas** P2 | P2 | prova que Finanças de outra planta não vê nem decide | Outra conta |
| **SYSADMIN** | System Administrator **sem** papel Finance | — | prova que o administrador sozinho não confirma/rejeita | Outra conta (ou a sua, se não tiver Finance) |
| **BUYERFIN** (opcional) | Buyer **+** Finance (+ SysAdmin) com âmbito P1 | P1 | prova “ninguém confirma a própria declaração” | Outra conta |

Regras que o servidor aplica (D12) e que o guião verifica: só utilizadores **ativos**, com papel **Finance atribuído na base de dados** e com **âmbito da planta do grupo** podem confirmar ou rejeitar; SysAdmin sozinho é recusado; quem declarou nunca confirma a própria declaração; pedido fora do âmbito → 404.

**Com a sua conta** pode executar tudo o que for do papel que a sua conta tiver. Sempre que o passo indicar **[Sair → entrar como X]**, termine a sessão (menu do utilizador → sair) e entre com a conta X. Nunca use dois separadores com contas diferentes ao mesmo tempo (a sessão é partilhada no browser).

### 0.2 Pedidos sintéticos (criar em TEST, tipo **Cotação**)

| Pedido | Título | Linhas | Fornecedores (cotações) | Usado em | Dados alterados de forma irreversível? |
|---|---|---|---|---|---|
| **A** | `TEST v2.249.0 – A – voltar a cotar` | 2 | Fornecedor S1 cota linha 1; S2 cota linha 2 → 2 grupos | Secção 3 (fluxo principal), 6.2 (grupo irmão), 6.7 | Sim (cancelamento, confirmação, 2.ª P.O.) |
| **B** | `TEST v2.249.0 – B – rejeição e 2.º ciclo` | 1 | S1 | Secções 4, 6.4 (mesmo número), 6.3-bis (2.º ciclo) | Sim |
| **C** | `TEST v2.249.0 – C – encerrar itens` | 1 | S1 | Secção 5 | Sim (pedido fica CANCELADO) |
| **D** | `TEST v2.249.0 – D – sem P.O.` | 1 | S1 | Secção 6.1 | Sim |
| **E** | `TEST v2.249.0 – E – pagamento agendado` | 1 | S1 | Secção 6.5 | Sim (agendamento + cancelamento do agendamento) |
| **F** | `TEST v2.249.0 – F – moedas mistas` (opcional) | 2 | S1 em AOA linha 1; S3 em USD/EUR linha 2 | Secção 6.6 | Sim |
| **G** | `TEST v2.249.0 – G – pagamento` (tipo **Pagamento**) | 1 | — | Secção 6.5 (tipo não suportado) | Não |
| **L** | pedido sintético **devolvido por Finanças antes do deploy** (se existir) | — | — | Secção 6.3 | Sim |

Dados de exemplo (fictícios): item “Rolo de filme stretch 500 mm (TEST v2.249.0)”, 10 UN; S1 = qualquer fornecedor ATIVO de teste; valor da cotação inicial 250 000,00 AOA; cotação mais baixa no reinício 230 000,00 AOA; números de P.O. fictícios `ECF99 2026/9001`, `ECF99 2026/9002`, …; referência de ECF anulada `ECF99 2026/9001`; PDF de teste: qualquer PDF de uma página com o texto “TESTE v2.249.0 – documento fictício” (a validação OCR pode assinalar divergência; ver 2.3).

### 0.3 Duração estimada

| Secção | Tempo |
|---|---|
| 1 Verificações de ambiente | 20 min |
| 2 Preparar pedidos sintéticos (A, B, C, D, E; F/G opcionais) | 90–120 min |
| 3 Fluxo principal — voltar a cotar | 60–75 min |
| 4 Rejeição de Finanças e correção | 25 min |
| 5 Encerrar itens | 25 min |
| 6 Casos adicionais | 90–120 min |
| 7 Permissões | 40 min |
| 8 Notificações e recuperação | 40 min |
| 9 Conclusão e limpeza | 20 min |
| **Caminho essencial (parte A: 1, 2 (A/B/C/D), 3, 4, 5, 6.1, 6.2, 7.1–7.4, 8.1–8.3, 9)** | **≈ 5 h** |

### 0.4 Como registar evidências

- Capturas de ecrã com o nome `v2249-<secção>-<passo>.png` (ex.: `v2249-S3-07.png`).
- Para e-mails: captura do assunto + cabeçalho (destinatário redirecionado e prefixo de ambiente) na caixa de teste.
- Anotar sempre o **número do pedido** (REQ-…) e o **número de P.O.** fictício usados.
- Cada cenário termina com um campo `Resultado: PASS / FAIL — observações`.

Separação pedida: **A = aceitação essencial** (correr primeiro), **B = regressão e permissões adicionais**, **C = verificações técnicas** (logs, API, SQL só de leitura). A letra está indicada no título de cada cenário.

---

## 1. Verificações de ambiente (A) — 20 min

### 1.1 Versão e build (A)

1. Abra a página de login de TEST. No rodapé do formulário aparece a versão da interface: deve ler **v2.249.0**.
2. Entre com qualquer conta. No fundo da barra lateral aparece a mesma versão **v2.249.0**.
3. Abra no browser `https://<host-de-TEST>/api/app/version` (anónimo). Verifique:
   - `version` = `2.249.0`
   - `buildId` = `2.249.1+<sha>` (versão `2.249.1`; a v2.249.0 não contém a correção do §2.2-bis)
   - `shortSha` começa por `c40ebb3`
   - `buildMetadataStatus` = `Valid`
4. Se a interface mostrar v2.249.0 mas a API não (ou vice-versa), **pare**: o deploy não ficou coerente.

Evidência: captura do login/rodapé + captura da resposta JSON. `Resultado: ____`

### 1.2 Redirecionamento de e-mails (A) — fazer ANTES de qualquer notificação

1. **[Entrar como SYSADMIN]** Menu **Administração → Gestão de Integrações**. Abra o fornecedor **SMTP**.
2. Na secção de identificação de ambiente confirme:
   - caixa **“Redirecionar todos os e-mails para destinatário de teste”** = **ligada**;
   - campo do destinatário de teste preenchido (ex.: `teste@empresa.com`) com uma caixa a que tem acesso;
   - banner de aviso de ambiente ativo (texto por defeito: “Esta mensagem foi gerada pelo ambiente TEST do ALPLA Portal…”).
3. Não altere nada se já estiver correto. Se tiver de ligar o redirecionamento, grave e anote a alteração em 9.3.
4. Opcional: **Testar Conexão** (não envia e-mails de negócio).

Evidência: captura da secção com a caixa ligada. `Resultado: ____`

### 1.3 Contas e âmbitos (A)

1. **Administração → Gestão de Usuários**. Para cada conta da tabela 0.1 confirme: ativa; papéis corretos; **planta(s)** no âmbito conforme a tabela (FIN1 e FIN2 com P1; FINP2 **só** P2; SYSADMIN sem Finance).
2. Confirme que FIN1 e FIN2 têm **endereço de e-mail** preenchido (necessário para o resultado `QUEUED` em 8.1).
3. Anote os nomes reais das contas usadas: BUYER=____ REQ=____ AREA=____ FINAL=____ FIN1=____ FIN2=____ FINP2=____ SYSADMIN=____ BUYERFIN=____

### 1.4 Configuração de Contas a Pagar — só leitura (A)

1. **Configurações → Dados Mestres → separador “📧 E-mails Contas a Pagar”**. Anote, para a empresa do pedido: e-mail principal, CC, e o estado de **“Também enviar e-mail individual aos utilizadores Finance (da planta do pedido)”**.
2. **Facto verificado no código:** o interruptor `NotifyOnPoCancelled` (aviso de cancelamento à caixa de Contas a Pagar) **não tem campo neste ecrã nem na API de configuração** (`/api/v1/ap-notification-configs` não o lê nem o grava). Por defeito está **desligado**. Consequência para este guião: o e-mail à caixa AP **não** é enviado em nenhum cenário, e isso é o comportamento esperado (ver 8.2 e Apêndice C.4). Gravar este ecrã **não** altera o interruptor.

`Resultado: ____`

---

## 2. Preparar os pedidos sintéticos (A) — 90–120 min

Repita 2.1–2.4 para **A, B, C, D, E** (e F, G se for fazer a parte B). Para o **A**, crie 2 linhas; para o **F**, 2 linhas com cotações em moedas diferentes.

### 2.1 Criar o pedido (REQ)

1. **[Entrar como REQ]** Menu **Compras & Logística → Pedidos** → **CRIAR PEDIDO**. Tipo **Cotação**. Título exatamente como na tabela 0.2 (começa por `TEST v2.249.0`). Departamento/planta **P1**. Preencha **Necessário até** (obrigatório para cotação).
2. Linha 1: “Rolo de filme stretch 500 mm (TEST v2.249.0)”, 10 UN. No pedido **A**: linha 2 “Fita adesiva 48 mm (TEST v2.249.0)”, 20 UN.
3. **CRIAR PEDIDO**. Mensagem esperada: “Pedido de Cotação criado…”. Anote o número: A=____ B=____ C=____ D=____ E=____ F=____ G=____

### 2.2 Cotação e lote (BUYER)

1. **[Sair → entrar como BUYER]** **Compras & Logística → Gestão de Cotações** → encontre o pedido → **Abrir Workspace**.
2. Se o cabeçalho mostrar **“Comprador: Não atribuído”** ou outro comprador, use **Assumir pedido** (justificação se for transferência). O BUYER tem de ficar como comprador responsável — só ele (ou SysAdmin) poderá cancelar a P.O.
3. **Inserir Manualmente** (ou **Importar Cotação**): fornecedor S1, linha 1, 250 000,00 AOA, prazo qualquer. No pedido **A** adicione uma segunda cotação de S2 para a linha 2 (120 000,00 AOA). No **F**, S3 cota a linha 2 noutra moeda.
4. Separador **Itens & Cobertura**: todas as linhas cobertas. No cabeçalho clique na ação de envio (rótulo do servidor **“Enviar itens para aprovação”** ou **“Enviar itens cobertos para aprovação”**) → no modal **Submeter para Aprovação**.
5. Esperado: separador **Lotes & Aprovações** mostra o Lote 1 em aprovação de área.

### 2.2-bis Regressão v2.249.1 — cotar a linha restante com o Lote 1 em aprovação (BUYER) — 10 min — pedido **A** (REQ-28/10/2026-628)

Objetivo: confirmar a correção dos dois defeitos encontrados na aceitação da v2.249.0 (ações de cotação escondidas após envio
parcial; menu ⋮ do item cortado). Pré-condição: linha 1 (TREVOTECH, 250 000,00 AOA) no **Lote 1** em aprovação de área; linha 2
**Pendente de cotação**. Não é preciso criar outro pedido; o fluxo de cancelamento do §3 continua depois sobre o mesmo pedido.

1. **[BUYER]** abrir o Workspace do pedido. Esperado no cabeçalho: estado **Cobertura Parcial** (não “Em Aprovação”), “Próxima
   ação” = **Completar cotações**, botões **Importar Cotação** e **Inserir Manualmente** visíveis; **sem** botão de envio (nada
   pronto). Painel de detalhes: estado do pedido continua `WAITING_QUOTATION` (o estado escalar **não** foi alterado pela correção).
2. **Itens & Cobertura** → na linha 2, abrir o menu **⋮** (“Ações do item 2”). Esperado: o menu abre **por cima** da tabela, inteiro,
   com **exatamente uma** opção — **Desconsiderar item** — e fecha com Esc, clique fora ou scroll. Repetir com a janela estreita
   (≈ 400 px) e com zoom 150 %: o menu nunca sai do ecrã nem fica cortado; junto ao fundo da janela abre **para cima**. Na linha 1
   (em lote) **não** há menu ⋮ (o servidor não autoriza “Desconsiderar” para itens em lote). *Não* clicar em Desconsiderar.
3. **Inserir Manualmente** → fornecedor S2, **linha 2**, 120 000,00 AOA → guardar. Esperado: cotação criada (sem 409); a linha 2 passa
   a **Cotado — pronto para lote**; com nada pendente o cabeçalho passa a **Pronto para Aprovação** com o botão **Enviar itens para aprovação** (os botões de cotação deixam de ser necessários e desaparecem). Tentar mapear uma
   cotação à **linha 1** no assistente: a linha 1 **não** aparece como destino (já está no Lote 1).
4. **Enviar itens para aprovação** → **Submeter para Aprovação**. Esperado: **Lote 2** só com a linha 2; o Lote 1 continua
   em aprovação de área, intocado (Lotes & Aprovações). Com ambos os lotes em aprovação e nada pendente, o estado passa a **Em
   Aprovação** (“Aguardando aprovação”, sem botões de cotação) — comportamento correto.
5. Continuar em §2.3 aprovando **os dois lotes** (área e final). Nota para o resto do guião: no pedido A desta execução o grupo
   S2 pertence ao **Lote 2**; onde o guião diz “Lote 1 permanece Aprovado” para o grupo S2, ler “Lote 2”. O cancelamento do grupo S1
   (Lote 1) e todas as verificações do §3 mantêm-se iguais.

Evidência: `v2249-2.2bis-01.png` (cabeçalho Cobertura Parcial com os botões), `v2249-2.2bis-02.png` (menu ⋮ aberto, inteiro),
`v2249-2.2bis-03.png` (menu junto ao fundo da janela / janela estreita), `v2249-2.2bis-04.png` (Lote 2 criado, Lote 1 intocado).

`Resultado: ____`

### 2.3 Aprovação de área e final (AREA, FINAL)

1. **[Sair → entrar como AREA]** **Centro de Aprovações** → separador **Pendentes** → selecione o cartão do lote → no rodapé do painel **Revisar Pedido** (abre o Wizard de Aprovação) → percorra os passos → **👍 Aprovar Pedido**.
2. **[Sair → entrar como FINAL]** Repita no Centro de Aprovações (fase **Aprovação Final**) → **👍 Aprovar Pedido**.
3. Esperado: no Workspace do BUYER o separador **Grupos P.O.** mostra um cartão por fornecedor com estado **Aguardando P.O.** (WAITING_PO), “Sem P.O. registada”, lote 1. No pedido **A** há **dois** cartões (S1 e S2).

### 2.4 Registar a P.O. (BUYER) — **não** fazer no pedido D

1. **[Sair → entrar como BUYER]** **Pedidos** → abra o pedido (`/requests/<id>`) → no painel de ações do grupo clique **REGISTRAR P.O (S1)**.
2. Modal **“Emitir P.O Sistêmica”**: **Número da P.O** = `ECF99 2026/9001` (A-S1), `…/9002` (A-S2), `…/9003` (B), `…/9004` (C), `…/9005` (E), `…/9006`/`…/9007` (F); **Documento da P.O (Apenas PDF)** = PDF de teste; condição de pagamento normal (sem adiantamento, salvo indicação). **REGISTRAR P.O**.
3. Se aparecer **“Divergência Detectada”** (OCR do PDF fictício), marque a caixa “Estou ciente das divergências…” e justifique “TEST v2.249.0 – documento fictício”. Isto é comportamento pré-existente.
4. Esperado: grupo passa a **P.O. Emitida** (PO_ISSUED); em **Grupos P.O.** o cartão mostra “P.O. ECF99 2026/9001”; o PDF aparece na lista de documentos como tipo **P.O**.

### 2.5 Devolução por Finanças (FIN1) — só nos pedidos **A (grupo S1), B, C**

1. **[Sair → entrar como FIN1]** **Finanças → Pagamentos**. Cartão **Aguardando Agendamento** → localize a linha do pedido/fornecedor → menu de ações da linha → **Devolver para ajuste**.
2. Modal **“Devolver Pedido para Ajuste”**:
   - **Tipo de devolução**: escolha **“Pedido cancelado — não corrigir”** (texto explicativo: “Cancelamento solicitado — não corrigir: o Comprador deve cancelar a P.O. em vez de a corrigir.”).
   - **Motivo estruturado (opcional)**: “Pedido/encomenda cancelado”.
   - **Motivo da Devolução (obrigatório)**: “PEDIDO CANCELADO – TEST v2.249.0”.
   - Confirmar.
3. **Verificação negativa (A):** repita numa linha qualquer deixando o motivo **vazio** → o botão fica inativo; se conseguir submeter, o servidor responde **400 “Motivo obrigatório — Indique o motivo da devolução para Compras.”** (código `RETURN_NOTES_REQUIRED`). Cancele o modal.
4. Esperado: grupo passa a **Devolvido para Compras** (WAITING_PO_CORRECTION). E-mail (redirecionado) com assunto **“Pedido Devolvido — cancelamento solicitado — REQ-…”** (e não “Pedido Devolvido para Correção”).
5. **Regressão (B):** no pedido **E** faça uma devolução com tipo **“Corrigir P.O.”** → assunto **“Pedido Devolvido para Correção — REQ-…”**; como BUYER, no Workspace → Grupos P.O. → **Corrigir P.O.** → página do pedido → **CORRIGIR P.O (S1)** → modal **“Correção de P.O”** → novo PDF, mesmo número → **REGISTRAR P.O CORRIGIDA** → grupo volta a PO_ISSUED e nada mais muda. (O pedido E volta assim ao estado de P.O. Emitida, pronto para 6.5.)

Estado de partida esperado no fim da secção 2:
- **A**: grupo S1 em *Devolvido para Compras* (devolvido “cancelamento solicitado”), grupo S2 em *P.O. Emitida*.
- **B, C**: grupo em *Devolvido para Compras*.
- **D**: grupo em *Aguardando P.O.* (nunca registada).
- **E**: grupo em *P.O. Emitida*.
- **G**: pedido de pagamento criado (sem cotação).

`Resultado secção 2: ____`

---

## 3. Fluxo principal — voltar a cotar (A) — 60–75 min — pedido **A**, grupo **S1**

**Objetivo:** cancelar a P.O. ECF99 2026/9001, devolver a linha 1 à cotação, declarar a ECF já anulada, obter novas aprovações enquanto Finanças ainda não confirmou, comprovar o bloqueio da P.O. de substituição, confirmar em Finanças e registar a nova P.O.
**Papéis:** BUYER, AREA, FINAL, FIN1. **Irreversível:** sim (usa o pedido A).

### 3.1 Ver o estado antes de cancelar (BUYER)

1. **[Entrar como BUYER]** Workspace do pedido A → separador **Grupos P.O.**
2. Esperado no cartão S1: estado **Devolvido para Compras**; caixa laranja **“Devolvido por Finanças — Cancelamento solicitado — não corrigir — Motivo: PEDIDO CANCELADO – TEST v2.249.0”** com nome de FIN1 e data; botões **Corrigir P.O.** e **Cancelar P.O.** ativos. Cartão S2: **P.O. Emitida**, botão **Cancelar P.O.** ativo, sem **Corrigir P.O.**
3. No cabeçalho do Workspace existe o botão vermelho **Cancelar P.O.** (como há dois grupos, abre o separador Grupos P.O. em vez do diálogo).
4. Anote os valores do cabeçalho (ainda não há **Aprovado ativo/histórico** separados).

Evidência: captura do separador. `Resultado: ____`

### 3.2 Cancelar o grupo S1 — voltar a cotar (BUYER)

1. No cartão S1 clique **Cancelar P.O.** Abre o diálogo **“Cancelar P.O. — REQ-…”** com a linha “S1 · P.O. ECF99 2026/9001 · Lote 1”.
2. **Validação do formulário (A):** clique **Confirmar cancelamento da P.O.** sem preencher nada. Esperado: mensagens “Escolha o que acontece aos itens…”, “Escolha o motivo do cancelamento.”, “A explicação é obrigatória (mínimo 20 caracteres).”, “Declare o estado da ECF (este grupo já teve P.O. registada).” Nada é enviado.
3. Preencha:
   - **O que acontece aos itens?** → **Devolver itens à cotação**.
   - **Motivo** → **Necessidade cancelada pela empresa**.
   - **Explicação** → “Encomenda cancelada pelo fornecedor; nova cotação necessária (TEST v2.249.0).”
   - **Declaração do comprador sobre a ECF** → **Já foi anulada fora do Portal**; **Referência da ECF anulada** = `ECF99 2026/9001`; nota opcional “Anulada no Primavera em DD/MM (teste)”.
4. Leia a caixa **“Consequências desta ação”**: deve dizer que os itens (#1) voltam à cotação e exigirão NOVO lote; que o documento da P.O. ECF99 2026/9001 fica “Sem efeito”; que a declaração é do comprador e Finanças confirma separadamente; que a nova P.O. fica bloqueada até essa confirmação. Confirme que **não** existe campo de carregamento de evidência neste diálogo (o tipo de anexo `PO_CANCELLATION` existe no servidor mas não tem UI nesta versão — anote como observação, não como falha).
5. **Confirmar cancelamento da P.O.**
6. Esperado (mensagem verde): “Grupo P.O cancelado. 1 item(ns) voltaram à cotação; será necessário um novo lote e novas aprovações de área e final. O registo da nova P.O fica bloqueado até Finanças confirmar…”. Se aparecer um aviso adicional sobre destinatários Finanças, anote-o (ver 8.1).

Evidência: captura do diálogo preenchido + mensagem de sucesso. `Resultado: ____`

### 3.3 Verificar o que ficou preservado (BUYER)

1. **Grupos P.O.** — cartão S1: estado **Cancelado**; bloco tracejado com três colunas:
   - **Cancelamento**: “Devolver itens à cotação”, “Motivo: Necessidade cancelada pela empresa”, a explicação, “Por BUYER · data”.
   - **Declaração do comprador sobre a ECF**: “O comprador declara que a ECF já foi anulada fora do Portal”, “Referência: ECF99 2026/9001”, “Declarado por BUYER · data”.
   - **Confirmação de Finanças**: **“Aguarda confirmação de Finanças”** (laranja); linha “Notificação: N destinatário(s) Finanças · data”.
   - Linha **“E-mail para Finanças: E-mails enfileirados para Finanças da planta: n (n de N utilizador(es) com e-mail)…”** e, por baixo, a **lista de entrega** por destinatário com chips **Pendente** → (após 1–2 min) **Enviado**.
   - Botões **Atualizar declaração ECF** e **Reenviar notificação**.
   Cartão S2: **inalterado** (P.O. Emitida, Cancelar P.O. ativo).
2. **Lotes & Aprovações**: Lote 1 continua **Aprovado**; linha laranja **“Itens libertados: #1”**; à direita **“Aprovado (histórico): 370 000,00 AOA · Ativo: 120 000,00 AOA · Libertado: 250 000,00 AOA”** (valores conforme as suas cotações).
3. **Cotações & Documentos**: a cotação de S1 continua listada (histórico); o PDF da P.O. continua listado e **descarregável**, marcado **“Sem efeito”** com o motivo.
4. **Itens & Cobertura**: linha 1 volta a **pendente de cotação**; linha 2 mantém-se coberta. *v2.249.2:* a linha 1 **não** pode
   aparecer como “Pronto para lote” (a cotação antiga de S1 exige autorização de reutilização — D3); contadores **2 tratados? não: 1
   tratado (aprovado) · 1 pendente · 0 prontos para lote**; cabeçalho **Cobertura Parcial** com **Importar Cotação** e **Inserir
   Manualmente** visíveis e **sem** botão “Enviar itens para aprovação”; painel de detalhes: estado do pedido continua
   `PO_PARTIALLY_UPLOADED` (o escalar não foi alterado). **Cotações & Documentos**: a cotação de S1 mostra **“Reutilização requer
   autorização (1)”**.
5. Cabeçalho: **Aprovado ativo** = 120 000,00 AOA; **Aprovado histórico** = 370 000,00 AOA (rasurado).
6. **Pedidos → pedido A**: chip vermelho **“P.O. cancelada — S1 · ECF99 2026/9001”**; estado do pedido re-derivado (volta a refletir cotação pendente). Painel **Histórico do Pedido**: linhas **“P.O. cancelada”**, **“Itens devolvidos à cotação”**, **“Declaração do comprador sobre a ECF”**.
7. Sino de notificações (canto superior): aviso **“P.O cancelada — itens voltam a cotação”**.

Evidência: capturas de Grupos P.O., Lotes, documentos “Sem efeito”, histórico. `Resultado: ____`

### 3.4 E-mails do cancelamento (caixa de teste)

Na caixa do destinatário de teste devem existir (todos com o prefixo/banner de ambiente TEST e destinatário redirecionado):
- **Para FIN1 e FIN2** (um por utilizador Finanças da planta P1): assunto **“Cancelamento de P.O — confirmar anulação da ECF ECF99 2026/9001 — Pedido REQ-…”**. O corpo diz “O comprador **declara** que a ECF … já foi anulada fora do Portal… o Portal não verifica o Primavera” e pede para confirmar em “Obrigações & Pagamentos → Cancelamentos a confirmar”. **Nunca** diz que o Portal anulou ou verificou a ECF.
- **Para BUYER**: assunto **“P.O cancelada — itens voltam a cotação — REQ-…”**.
- **Para REQ, AREA, FINAL** (participantes): assunto **“P.O cancelada no pedido REQ-…”**.
- **Nenhum** e-mail para FINP2. **Nenhum** e-mail à caixa de Contas a Pagar (interruptor desligado, ver 1.4).
- Nota sobre D16: o e-mail ao solicitante **para o cancelamento em si** é comportamento implementado conforme a decisão D16 registada no documento de referência (“in-app + e-mail para o cancelamento; o resultado da confirmação de Finanças só ao comprador”). Se a intenção aprovada for “apenas aviso in-app ao solicitante”, trata-se de um **pedido de alteração** a registar em 9.2 — o código não foi alterado.

Evidência: lista dos assuntos recebidos. `Resultado: ____`

### 3.4-bis Retoma após o deploy da v2.249.2 (BUYER / FIN1) — verificações das correções no pedido **A**, sem alterar dados — 20 min

Pré-condição: TEST com build **2.249.2+<sha>** (§1.1); o pedido A exatamente como ficou (S1 cancelado, confirmação pendente,
S2 PO_ISSUED, sem nova cotação). Nada a recriar; **não** executar reparações.

1. **[BUYER] Gestão de Cotações** (`/buyer/items`): o pedido A **aparece** na fila (estava escondido pelo filtro de admissão) com
   estado **Cobertura Parcial**, “1/2 tratados · 1 pendente”, ação **Completar cotações**; CTA **Abrir Workspace**. Esperado: nenhuma
   fila/cartão mostra “Pronto para Aprovação”.
2. **[BUYER] Workspace do A** (`/buyer/requests/<id>`): cabeçalho **Cobertura Parcial**, “Próxima ação: Completar cotações”, botões
   **Importar Cotação** / **Inserir Manualmente** visíveis; **sem** “Enviar itens para aprovação”. **Itens & Cobertura**: linha 1
   **Pendente de cotação** (nunca “Pronto para lote”); linha 2 **Aprovado**; contadores 1 tratado · 1 pendente · 1 aprovado · 0 prontos.
   **Cotações & Documentos**: cotação S1 com **“Reutilização requer autorização (1)”**; os botões Editar/Excluir dessa cotação **não**
   aparecem (está submetida — Lote 1).
3. **[BUYER]** Abrir o modal de envio **só se** existir um botão de envio (não deve existir). Se o abrir por outra via (ferramentas
   clássicas → enviar para aprovação): **ITENS COM COTAÇÃO (0)**, linha 1 em “Itens restantes” com a nota **“1 cotação(ões)
   anterior(es) exige(m) autorização de reutilização (ferramentas clássicas → Reutilizar cotação) ou registe uma nova cotação.”** e
   o botão de submissão desativado — coerente com o cabeçalho (nada pronto). Fechar sem submeter.
4. **[BUYER] Grupos P.O.**: cartão S1 (Cancelado) — lista de entrega com a linha **“Estado de entrega consultado às HH:MM:SS · o envio é
   processado em segundo plano (≈ 10 s por ciclo); ‘Pendente’ logo após o cancelamento é normal.”** e o botão **Atualizar estado**;
   os chips devem estar **Enviado** com data/hora (os e-mails foram recebidos). Clicar **Atualizar estado**: a hora atualiza, **nenhum**
   e-mail novo é enviado, “reenvio(s)” **não** aumenta. Cartão S2 (P.O. Emitida): **sem** “Registrar P.O.” e **sem** “Corrigir P.O.”;
   “Cancelar P.O.” conforme elegibilidade.
5. **[BUYER] Pedidos → Para Minha Ação**: se existir um cartão de devolução para o pedido A (não deve — o grupo S1 já foi cancelado),
   o seu CTA abre o Workspace (`/buyer/requests/<id>?tab=groups`). Verificação equivalente no pedido **B** (devolvido por FIN1 em §2.5,
   ainda não cancelado): na fila, a linha do B mostra **“P.O. devolvida — cancelamento solicitado”** se FIN1 escolheu “Pedido cancelado —
   não corrigir”, ou **“Correção de P.O.”** se escolheu “Corrigir P.O.”, chip “FORNECEDOR · cancelamento solicitado/corrigir”, CTA
   **Gerir P.O. devolvida** / **Corrigir P.O.** → abre o Workspace, separador **Grupos P.O.** (não a página geral de Pedidos); menu ⋮
   com **Abrir Workspace**. No cartão do grupo: linha de orientação por tipo (“Finanças pede o cancelamento desta P.O. — não a corrija…”
   ou “Finanças pede a correção da P.O.…”); botão **Corrigir P.O.** (abre a página do pedido) e **Cancelar P.O.** ambos presentes.
6. **[FIN1] Obrigações & Pagamentos** (pedido B, grupo devolvido): coluna “Próxima ação” = **“Devolvido — cancelamento solicitado (não
   corrigir)”** ou **“Devolvido para correção da P.O.”** conforme o tipo escolhido; estado = “Devolvido para Compras”.
7. **[BUYER] Página do pedido B → Imprimir / Vista de impressão**: “Próxima ação” = **“P.O devolvida por Finanças — cancelamento
   solicitado: cancelar a P.O (voltar a cotar ou encerrar itens) ou, se discutível, corrigir”** (ou o texto de correção); no histórico,
   o cabeçalho da linha é **“Devolvido por Finanças”** e o texto da linha indica o tipo.
8. **[BUYER] Pedido D** (aprovado, grupo em **Aguardando P.O.**, §6.1): Workspace → cabeçalho **“P.O. a registar”** (não “Concluído para
   Compras”), “Próxima ação: Registar a P.O. na página do pedido” (sem botão); **Grupos P.O.** → botão **Registrar P.O.** (abre a página
   do pedido, secção P.O.) e **nenhum** “Corrigir P.O.”. Na página do pedido, **REGISTRAR P.O (fornecedor)** continua a ser o fluxo de
   registo; **não** registar ainda a P.O. do pedido D se o §6.1 ainda não foi executado.

9. **[BUYER] Dashboard (Visão Geral)**: secção do Comprador / “Minha Operação” — o pedido A conta **uma** vez (1 pedido
   acionável, 1 item pendente, 0 prontos) e aparece **uma** ação “Adicionar/Completar cotação” para ele; se a data necessária já
   passou, **um** alerta “Cotação vencida”; o clique leva à fila (Gestão de Cotações) onde “Abrir Workspace” abre o pedido.
   **Pedidos → Para Minha Ação → Cotar itens** no pedido A: o botão **Cotar itens** abre diretamente o Workspace do pedido.
10. **[BUYER] Cotações & Documentos / Documentos do pedido A**: tentar **remover** o documento (proforma/cotação) da cotação
    antiga de S1 → recusado com **“Documento de cotação submetida”**; tentar **Editar/Excluir** essa cotação → não oferecido
    (submetida). Depois de registar a nova cotação (§3.5), o documento **dela** pode ser removido e ela pode ser editada
    enquanto não for enviada para aprovação.

Evidência: `v2249-3.4bis-01.png` (fila), `-02.png` (cabeçalho + Itens), `-03.png` (Cotações com o aviso), `-04.png` (entrega +
Atualizar estado), `-05.png` (pedido B na fila e cartão), `-06.png` (Obrigações), `-07.png` (impressão), `-08.png` (pedido D),
`-09.png` (Dashboard / Para Minha Ação), `-10.png` (recusa de remoção do documento).

`Resultado: ____`

### 3.5 Nova cotação, novo lote, novas aprovações com a confirmação pendente

1. **[BUYER]** Workspace do A → **Inserir Manualmente**: fornecedor S1 (ou outro), linha 1, **230 000,00 AOA**. Se preferir reutilizar a cotação antiga: **Abrir ferramentas clássicas** → encontre a cotação de S1 → **Reutilizar cotação** → modal “Reutilizar cotação em novo lote” → **AUTORIZAR REUSO (1)** (autorização explícita exigida por D3). Qualquer das vias deve funcionar.
   *v2.249.1:* os botões **Importar Cotação** / **Inserir Manualmente** têm de estar visíveis mesmo que o estado do pedido no painel de detalhes já não seja `WAITING_QUOTATION` (grupo irmão S2 com P.O. emitida → estado escalar `PO_PARTIALLY_UPLOADED`); o cabeçalho mostra **Cobertura Parcial**. A cotação só pode ser mapeada à **linha 1** (libertada); a linha 2 (grupo S2 operacional) não é oferecida e, se forçada pela API, devolve 409 “Item já comprometido”.
   *v2.249.2:* testar **Importar Cotação** com um PDF fictício (o upload do documento de cotação/proforma era recusado com “Upload Bloqueado” na v2.249.1 — agora é aceite) **e/ou** Inserir Manualmente. Depois de guardar, a linha 1 passa a **Cotado — pronto para lote** só com a **nova** cotação; a cotação antiga de S1 continua com “Reutilização requer autorização (1)” e **não** entra no lote sem autorização explícita. Com a nova cotação **não submetida**, em Cotações & Documentos aparecem **Editar/Excluir** para ela (janela por item); testar **Editar** (alterar o preço e guardar) — aceite; o escalar do pedido não muda. Se preferir a reutilização: ferramentas clássicas → **Reutilizar cotação** → **AUTORIZAR REUSO (1)** → o Workspace passa a mostrar “Reutilização autorizada (1)” e a linha 1 fica pronta com a cotação antiga.
2. Enviar para aprovação (**Enviar itens para aprovação** → **Submeter para Aprovação**). Esperado: **Lote 2** criado só com a linha 1; o Lote 1 permanece Aprovado com “Itens libertados: #1”.
3. **[Sair → AREA]** aprovar o Lote 2. **[Sair → FINAL]** aprovar o Lote 2. Esperado: aprovações **não** são bloqueadas pela confirmação pendente.
4. **[Sair → BUYER]** **Grupos P.O.**: novo cartão S1 (Lote 2) em **Aguardando P.O.** com faixa vermelha **“Registo da nova P.O. bloqueado até Finanças confirmar a anulação da ECF anterior (ECF99 2026/9001). …”**. Página do pedido: a mesma faixa por baixo dos chips. *v2.249.2:* o cartão mostra **Registrar P.O.** **desativado** (título com o motivo) e a linha **“Registo da P.O. indisponível: Registo da nova P.O. bloqueado até…”**; **nenhum** “Corrigir P.O.” neste cartão; cabeçalho do Workspace **“P.O. a registar”**.
5. Cabeçalho: **Aprovado ativo** = 350 000,00 AOA (120 000 + 230 000); **Aprovado histórico** = 600 000,00 AOA (370 000 + 230 000) — valores por moeda, nunca somados entre moedas.

`Resultado: ____`

### 3.6 Tentar registar a P.O. de substituição ANTES da confirmação (BUYER)

1. **Pedidos → A** → **REGISTRAR P.O (S1)** (grupo do Lote 2) → **Número da P.O** `ECF99 2026/9010`, PDF de teste → **REGISTRAR P.O**.
2. Esperado: o modal **não** regista; aparece a caixa vermelha **“Registo da nova P.O. bloqueado”** com o texto do servidor “A nova P.O não pode ser registada: a anulação da(s) P.O/ECF anterior(es) (ECF99 2026/9001) ainda não foi confirmada por Finanças…”. Não há botão de repetir. (Tecnicamente o bloqueio ocorre já no carregamento do PDF — o modal carrega o ficheiro antes de registar — por isso **nenhum documento** fica anexado: confirme em **Cotações & Documentos** que não surgiu novo PDF.)
3. **Grupos P.O.**: o grupo do Lote 2 continua **Aguardando P.O.**, sem número.

Evidência: captura da caixa vermelha. `Resultado: ____`

### 3.7 Finanças confirma a anulação (FIN1)

1. **[Sair → entrar como FIN1]** **Finanças → Pagamentos**. Acima da lista aparece a secção âmbar **“Cancelamentos a confirmar”** com contador e a linha do pedido A: número, título, S1, empresa · planta, valor, “P.O. anterior: ECF99 2026/9001”; coluna **Cancelamento** (tipo, motivo, por quem); coluna **Declaração do comprador** (“O comprador declara que a ECF já foi anulada fora do Portal”, “Referência: ECF99 2026/9001”, “Aguarda confirmação de Finanças”, linha “E-mail para Finanças: …”); à direita botões **Confirmar anulação** e **Rejeitar declaração**.
2. Clique **Confirmar anulação** → diálogo **“Confirmar anulação da ECF”** com a declaração do comprador e o aviso “Ao confirmar, Finanças atesta que a ECF anterior foi anulada no sistema externo. O Portal não verifica o Primavera…”. **Nota (opcional)**: “Anulação verificada no Primavera (TEST).” → **Confirmar anulação**.
3. Esperado: mensagem “Anulação da ECF ECF99 2026/9001 confirmada por Finanças (REQ-…).”; a linha desaparece da secção (ou a secção desaparece se não houver mais).
4. **Finanças → Histórico**: linha com etiqueta **“Anulação da ECF confirmada por Finanças”**.

`Resultado: ____`

### 3.8 Registar a P.O. de substituição com sucesso (BUYER)

1. **[Sair → BUYER]** Workspace A → **Grupos P.O.**: cartão S1 (Lote 1, cancelado) mostra **“Anulação confirmada por Finanças”** (verde), “Por FIN1 · data”, nota; os botões **Atualizar declaração ECF** desaparece (declaração fechada). A faixa vermelha do grupo do Lote 2 **desapareceu**.
2. Caixa de teste: e-mail **só para BUYER** com assunto **“Anulação da ECF confirmada — REQ-…”**; sino: “Anulação da ECF confirmada por Finanças”. **Nenhum** e-mail ao solicitante nem aos aprovadores para este evento (D16).
3. **Pedidos → A → REGISTRAR P.O (S1)** → **Número da P.O** `ECF99 2026/9010` (número **diferente** do cancelado), PDF → **REGISTRAR P.O**. Esperado: sucesso; grupo do Lote 2 passa a **P.O. Emitida**; histórico com o registo da P.O.
4. Cabeçalho e Lote 2: valores ativos coerentes (ativo 350 000,00 AOA).

Evidência: capturas do cartão confirmado, do e-mail ao comprador e do grupo com P.O. Emitida. `Resultado: ____`

---

## 4. Rejeição de Finanças e correção (A) — 25 min — pedido **B**

**Objetivo:** Finanças rejeita a declaração; o comprador é informado; a P.O. de substituição continua bloqueada; o comprador atualiza a declaração; Finanças volta a rever e confirma.
**Papéis:** BUYER, FIN1 (ou FIN2), AREA, FINAL. **Irreversível:** sim.

1. **[BUYER]** Workspace B → **Grupos P.O.** → **Cancelar P.O.** → **Devolver itens à cotação**; motivo **Fornecedor cancelou / não consegue fornecer**; explicação ≥ 20 caracteres; ECF → **Ainda não foi anulada**; **Confirmar cancelamento da P.O.**
   Esperado: cartão com “O comprador declara que a ECF ainda não foi anulada”; e-mail a Finanças com assunto **“Cancelamento de P.O — anulação de ECF necessária — Pedido REQ-…”** (o corpo pede que a ECF seja anulada no Primavera e depois confirmada no Portal).
2. Nova cotação + lote 2 + aprovações de área e final (como em 3.5). Confirme a faixa vermelha de bloqueio no novo grupo.
3. **[Sair → FIN1]** Finanças → Pagamentos → **Cancelamentos a confirmar** → **Rejeitar declaração** → diálogo **“Rejeitar declaração do comprador”**.
   - **Validação (A):** tente submeter com o motivo vazio ou com 10 caracteres → o botão fica inativo e surge “A justificação da rejeição é obrigatória (mínimo 20 caracteres).”
   - Motivo: “A ECF continua ativa no Primavera; anular antes de nova P.O. (TEST).” → **Rejeitar declaração**.
   Esperado: mensagem “Declaração do comprador sobre a ECF … rejeitada (REQ-…). O comprador foi notificado.”; a linha **continua** listada, agora com estado **“Declaração rejeitada por Finanças”** e “Nota de Finanças: …”.
4. **[Sair → BUYER]** Workspace B → cartão cancelado mostra **“Declaração rejeitada por Finanças”** (vermelho), a nota, e o botão **Atualizar declaração ECF** ativo. E-mail **só ao BUYER**: **“Anulação da ECF NÃO confirmada — REQ-…”**; sino: “Finanças não confirmou a anulação da ECF”. Histórico do pedido: **“Declaração da ECF rejeitada por Finanças”**.
5. **Bloqueio mantido:** **Pedidos → B → REGISTRAR P.O (S1)** com `ECF99 2026/9020` → caixa **“Registo da nova P.O. bloqueado”**.
6. **[BUYER]** **Atualizar declaração ECF** → diálogo **“Atualizar declaração sobre a ECF — REQ-…”** → **Já foi anulada fora do Portal**, referência `ECF99 2026/9003` → **Guardar declaração**. Esperado: mensagem “Declaração sobre a ECF atualizada. Finanças confirma separadamente a anulação.”; estado volta a **“Aguarda confirmação de Finanças”**; e-mail a Finanças com prefixo **“[Declaração atualizada] Cancelamento de P.O — confirmar anulação da ECF ECF99 2026/9003 …”**.
7. **[Sair → FIN2]** (pessoa diferente, para variar) → **Confirmar anulação**. **[Sair → BUYER]** → registar a P.O. de substituição… **PARE aqui** se for fazer 6.4 (mesmo número) com este pedido; caso contrário registe `ECF99 2026/9020` com sucesso.

Evidência: capturas da rejeição, do e-mail “NÃO confirmada”, do bloqueio mantido e da declaração atualizada. `Resultado: ____`

---

## 5. Cancelar sem reiniciar — encerrar itens (A) — 25 min — pedido **C**

**Objetivo:** encerrar a única linha; o pedido fica cancelado; evidência histórica e confirmação pendente continuam acessíveis.
**Papéis:** BUYER, FIN1. **Irreversível:** sim (pedido C fica CANCELADO).

1. **[BUYER]** Workspace C → **Grupos P.O.** → **Cancelar P.O.** → **Encerrar itens (cancelar a necessidade)**; motivo **Necessidade cancelada pela empresa**; explicação; ECF → **Não sei**. Consequências: “Os itens deste grupo (#1) ficam encerrados após a aprovação — nada será comprado para eles.” → **Confirmar cancelamento da P.O.**
2. Esperado: mensagem de sucesso referindo itens encerrados; pedido **C** passa a **Cancelado** (lista **Pedidos** e página do pedido); histórico com **“P.O. cancelada”** e **“Itens encerrados após aprovação”**; a linha mostra o estado de encerrada após aprovação.
3. Continuam acessíveis: cotação de S1 em **Cotações & Documentos**; PDF da P.O. “Sem efeito” descarregável; Lote 1 Aprovado com “Itens libertados: #1”; cartão cancelado com declaração “O comprador declara não saber o estado da ECF” e **“Aguarda confirmação de Finanças”**.
4. E-mail a Finanças: **“Cancelamento de P.O — anulação de ECF necessária — Pedido REQ-…”** (o corpo pede para verificar a situação da ECF no Primavera). E-mail ao BUYER: **“P.O cancelada — itens encerrados — REQ-…”**. Participantes: “P.O cancelada no pedido REQ-…”.
5. **[Sair → FIN1]** **Cancelamentos a confirmar** continua a listar o pedido C apesar de o pedido estar cancelado → **Confirmar anulação** (nota opcional). Esperado: sucesso; **[BUYER]** cartão mostra “Anulação confirmada por Finanças”; e-mail “Anulação da ECF confirmada — REQ-…” só ao comprador.
6. **Grupos P.O.**: o pedido C não mostra qualquer ação de cancelar/corrigir (pedido terminal).

`Resultado: ____`

---

## 6. Casos adicionais — 90–120 min

### 6.1 Cancelamento sem P.O. registada — sem confirmação de Finanças (A) — pedido **D**

1. **[BUYER]** Workspace D → **Grupos P.O.** → cartão em **Aguardando P.O.**, “Sem P.O. registada” → **Cancelar P.O.**
2. Esperado no diálogo: **não** existe o bloco “Declaração do comprador sobre a ECF”; nas consequências: “Nenhum documento de P.O. está registado neste grupo…” e “Não é necessária confirmação de Finanças: este grupo nunca teve P.O. registada.”
3. **Devolver itens à cotação**, motivo **Preço ou condições alterados — nova cotação necessária**, explicação → **Confirmar cancelamento da P.O.**
4. Esperado: cartão **Cancelado** com “Não aplicável (nunca houve P.O. registada)” e **“Confirmação de Finanças não necessária”**; **sem** botão Atualizar declaração ECF; **sem** faixa de bloqueio depois de novo lote; e-mail a Finanças **informativo**: **“Grupo P.O cancelado antes do registo da P.O — Pedido REQ-…”** (“Nenhum ação é necessária por parte de Finanças…”). **[FIN1]** a secção **Cancelamentos a confirmar** **não** lista o pedido D.
5. Opcional: nova cotação → lote 2 → aprovações → **REGISTRAR P.O (S1)** com `ECF99 2026/9030` → **sucesso imediato** (sem bloqueio).

`Resultado: ____`

### 6.2 Cancelamento parcial com grupo irmão intacto (A) — já coberto no pedido **A**

Confirme explicitamente, no pedido A (após a secção 3):
1. Cartão **S2** nunca mudou: **P.O. Emitida**, P.O. `ECF99 2026/9002`, PDF **sem** “Sem efeito”, botão **Cancelar P.O.** ativo e **sem** faixa de bloqueio (o bloqueio é por linha e só afeta a linha 1).
2. **[FIN1]** Finanças → Pagamentos: a obrigação de S2 continua em **Aguardando Agendamento**; a obrigação do grupo cancelado **não** aparece.
3. Lote 1: “Itens libertados: #1” (só a linha 1); “Ativo” = valor da linha 2.

`Resultado: ____`

### 6.3 Devolução legada — “tipo não classificado” (B) — pedido **L**

Pré-requisito: existir em TEST um pedido **sintético** cujo grupo tenha sido **devolvido por Finanças antes do deploy** de v2.249.0 (estado *Devolvido para Compras*). **Não** use REQ-483 nem pedidos reais. Se não existir, marque **N/A** (a regra está coberta por testes automáticos; não é possível criar uma devolução “anterior à versão” depois do deploy e não se devem forçar estados por SQL).

1. **[BUYER responsável de L]** Workspace L → **Grupos P.O.** → caixa laranja **“Devolvido por Finanças — Tipo não classificado (devolução anterior à versão) — Motivo: <texto original da devolução>”**.
2. Botões **Corrigir P.O.** e **Cancelar P.O.** ambos disponíveis; **não** é pedida uma segunda devolução.
3. (Opcional) **Cancelar P.O.** → **Encerrar itens** com motivo **Devolvido por Finanças sem correção possível** → funciona como nos cenários anteriores.

`Resultado: ____ / N/A`

### 6.3-bis Segundo ciclo — cancelar também a P.O. de substituição (B) — pedido **B**

Pré-requisito: pedido B com a 2.ª P.O. registada (fim da secção 4 ou de 6.4).
1. **[BUYER]** **Grupos P.O.** → cartão do Lote 2 → **Cancelar P.O.** → voltar a cotar; ECF **Ainda não foi anulada** → confirmar.
2. Nova cotação → Lote 3 → aprovações. Esperado: faixa de bloqueio referindo as **duas** P.O. anteriores (“… ECF99 2026/9003, ECF99 2026/9020”); **[FIN1]** só o 2.º cancelamento está em **Cancelamentos a confirmar** (o 1.º já está confirmado). Registar P.O. → bloqueado até confirmar o 2.º; após confirmar → sucesso.

`Resultado: ____`

### 6.4 Mesmo número de P.O. — aviso, explicação obrigatória, histórico (A) — pedido **B**

Pré-requisito: cancelamento de B confirmado (fim de 4.7), grupo do Lote 2 em Aguardando P.O.
1. **[BUYER]** **Pedidos → B → REGISTRAR P.O (S1)** → **Número da P.O** = `ECF99 2026/9003` (**o mesmo** da P.O. cancelada) → PDF → **REGISTRAR P.O**.
2. Esperado: caixa âmbar **“Número de P.O. de uma P.O. cancelada”** com o texto do servidor; caixa de verificação **“Confirmo que a nova P.O. reutiliza intencionalmente o número da P.O. cancelada”**; campo “Explique porque a nova P.O. reutiliza o número da P.O. cancelada (mínimo 20 caracteres)…” com contador. O documento **não** é carregado uma segunda vez na repetição.
3. Tente **REGISTRAR P.O** sem marcar a caixa → mensagem “Confirme que a nova P.O. reutiliza intencionalmente…”; marque e escreva < 20 caracteres → “Explique porque…”. Escreva “Fornecedor reemitiu a encomenda com o mesmo número (TEST v2.249.0).” → **REGISTRAR P.O**.
4. Esperado: registo com sucesso; **Histórico do Pedido** com **“Número de P.O. reutilizado após cancelamento”** e a explicação; grupo em **P.O. Emitida**.

`Resultado: ____`

### 6.5 Recusas: pagamento agendado, pagamento concluído, tipo Pagamento, item ORDERED (B) — pedidos **E** e **G**

1. **Pagamento agendado (E):** **[FIN1]** Finanças → Pagamentos → linha do pedido E → **Agendar pagamento** → modal **“Agendar Pagamento”** → data futura → confirmar. **[Sair → BUYER]** Workspace E → **Grupos P.O.** → botão **Cancelar P.O.** **inativo** com o texto por baixo **“Existe um pagamento agendado para este grupo. Finanças deve cancelar o agendamento ou devolver o grupo antes do cancelamento.”** (o título do botão repete a mensagem). **[Sair → FIN1]** → **Cancelar agendamento** (motivo ≥ 20 caracteres: “Agendamento de teste v2.249.0 cancelado.”) → **[BUYER]** o botão volta a ficar ativo.
2. **Pagamento concluído (E, opcional e só em TEST):** **[FIN1]** **Agendar pagamento** de novo e depois **Pagar** (modal **“Confirmar Liquidação”**, montante e um PDF fictício como comprovante — **é uma marcação no Portal, não um pagamento real**). **[BUYER]** **Cancelar P.O.** inativo com “Existe um pagamento concluído para este grupo; não pode ser cancelado no Portal.” (ou a mensagem equivalente para o estado pós-pagamento). Isto cobre também a recusa por receção: a receção só começa depois do pagamento, portanto um item recebido pertence sempre a um grupo já recusado por esta regra. Não é necessário registar receção.
3. **Tipo Pagamento (G):** abra o pedido G. Esperado: **não** existe Workspace de cotação com separador Grupos P.O. nem ação **Cancelar P.O.** em lado nenhum. A recusa server-side (`REQUEST_TYPE_NOT_SUPPORTED`) só é verificável por API — Apêndice C.3.
4. **Item ORDERED:** **não reproduzível pela interface** (nenhum ecrã de v2.249.0 escreve o estado ORDERED; só o sincronizador legado de estados o faz). Não forçar por SQL. Coberto por teste automático (`PoGroupCancellationDomainTests`). Marque **N/A (UI)**; a mensagem esperada para referência é “Os itens #n estão marcados como ‘Encomendado’ (ORDERED). Este caso requer investigação…”.

`Resultado: ____`

### 6.6 Valores históricos vs ativos e moedas mistas (B) — pedido **F**

1. Pedido F com dois grupos em moedas diferentes (AOA e USD/EUR), ambos com P.O. Emitida.
2. **[BUYER]** cancelar o grupo em AOA (encerrar itens ou voltar a cotar). Esperado no cabeçalho: **Aprovado ativo** mostra **só** o valor da outra moeda (ex.: “USD 1 500,00”); **Aprovado histórico** mostra os dois valores **separados por moeda** (nunca uma soma); nos Lotes a linha “Aprovado (histórico) · Ativo · Libertado” aparece **por moeda**.
3. E-mails deste cancelamento: o contexto departamental (se presente) usa os valores ativos por moeda; se existir mais de uma moeda não deve aparecer um total único.

`Resultado: ____`

### 6.7 Ramos DUPLICATE_PO e OCR_MISMATCH do modal de registo (B) — qualquer grupo em Aguardando P.O.

Estes ramos existiam antes mas só ficaram efetivos com a correção do classificador de erros em v2.249.0.
1. **DUPLICATE_PO:** registe uma P.O. com um número que **já exista noutro pedido** de TEST (ex.: `ECF99 2026/9002`, já usado por A-S2) → caixa vermelha **“P.O Duplicada (SISTEMA CENTRAL)”** → caixa **“Confirmar uso do número de P.O duplicado sob minha responsabilidade.”** → campo “Justificativa obrigatória para P.O duplicada…” → **REGISTRAR P.O** → sucesso com auditoria de override.
2. **OCR_MISMATCH:** registe com um PDF cujo valor/fornecedor não bate com o grupo e que o serviço central rejeite → caixa **“Divergência Detectada (SISTEMA CENTRAL)”** com a lista de motivos → caixa “Estou ciente das divergências reportadas e confirmo a emissão sob minha responsabilidade.” → registar. Se o OCR de TEST não produzir rejeição central, marque N/A e anote.

`Resultado: ____`

---

## 7. Permissões (A/B) — 40 min

**Importante:** um botão escondido ou inativo prova apenas a decisão de **visibilidade** calculada pelo servidor para esse utilizador (`canReview`); a **aplicação server-side** só fica provada por uma chamada direta à API recusada (Apêndice C.2). Marque em cada linha o que verificou: **UI**, **API** ou ambos.

Pré-requisito: existir pelo menos um cancelamento em **Aguarda confirmação de Finanças** (ex.: criar agora um novo cancelamento no pedido A grupo S2, ou usar o 2.º ciclo de B). Anote o pedido usado: ____

### 7.1 SysAdmin sozinho não confirma nem rejeita (A)

1. **[Entrar como SYSADMIN]** O administrador consegue abrir **Finanças → Pagamentos** (o painel ignora a restrição de papel para administradores). Na secção **Cancelamentos a confirmar** a linha aparece **sem** os botões Confirmar/Rejeitar e com o texto de bloqueio do servidor (ex.: “Apenas utilizadores com o perfil Finanças e acesso à planta do grupo podem confirmar ou rejeitar a anulação da ECF. O perfil de administrador, por si só, não concede esta permissão.”).
2. API (C.2): `POST /api/v1/finance/po-cancellations/{id}/confirm` → **403 `FINANCE_SCOPE_REQUIRED`**; o mesmo para `/reject`.

`Resultado: ____ (UI / API)`

### 7.2 Finanças de outra planta não vê nem decide (A)

1. **[Entrar como FINP2]** **Finanças → Pagamentos** (planta P2): a secção **Cancelamentos a confirmar** **não** lista o pedido (ou nem aparece).
2. API (C.2): confirm/reject com o id do cancelamento → **404** (pedido fora do âmbito; o servidor não revela a existência). `GET /api/v1/finance/po-cancellations?status=OPEN` não devolve o item.

`Resultado: ____ (UI / API)`

### 7.3 Ninguém confirma a própria declaração (A se tiver a conta BUYERFIN; senão B)

1. **[Entrar como BUYERFIN]** Torne BUYERFIN comprador responsável de um pedido sintético (Assumir pedido), cancele um grupo com P.O. registada e declare a ECF.
2. Ainda como BUYERFIN: **Finanças → Pagamentos → Cancelamentos a confirmar**: a linha aparece **sem** botões e com o texto do servidor (“A confirmação de Finanças tem de ser feita por uma pessoa diferente de quem declarou…”).
3. API (C.2): confirm → **403 `SELF_CONFIRMATION_FORBIDDEN`**, mesmo tendo Finance + SysAdmin.
4. **[Entrar como FIN1]** a mesma linha mostra os botões e confirma com sucesso.

`Resultado: ____ (UI / API)`

### 7.4 Finanças ativo e com âmbito correto decide a declaração de outro (A)

Já provado em 3.7 (FIN1 confirma), 4.3 (FIN1 rejeita) e 4.7 (FIN2 confirma). Registe aqui os três resultados. `Resultado: ____`

### 7.5 Comprador não responsável não cancela (B)

1. **[Entrar com outro Buyer que não seja o responsável]** Workspace de um pedido com grupo ativo: a ação **Cancelar P.O.** não é listada (o servidor não a devolve como ação) ou o cartão não permite.
2. API (C.2): `POST …/po-groups/{gid}/cancel` → **403 `NOT_RESPONSIBLE_BUYER`**.

`Resultado: ____ (UI / API)`

### 7.6 Utilizador Finance inativo (C — exige desativar uma conta de teste)

Só se tiver uma conta Finance de teste descartável: desative-a em Gestão de Usuários → a API responde 403 “Utilizador inativo” (a conta inativa nem consegue entrar na UI). Reative no fim. `Resultado: ____ / N/A`

---

## 8. Notificações e recuperação — 40 min

### 8.1 E-mail a Finanças não depende de `NotifyFinanceUsersByEmail` (A)

1. **[SYSADMIN]** **Configurações → Dados Mestres → 📧 E-mails Contas a Pagar** → configuração da empresa do pedido → confirme que **“Também enviar e-mail individual aos utilizadores Finance (da planta do pedido)”** está **desligada** (se estiver ligada, desligue, grave, e anote para repor em 9.3).
2. **[BUYER]** faça um cancelamento com P.O. registada (ex.: grupo S2 do pedido A, ou o 2.º ciclo de B).
3. Esperado: FIN1 **e** FIN2 recebem o e-mail “Cancelamento de P.O — …” (redirecionado); no cartão: **“E-mail para Finanças: E-mails enfileirados para Finanças da planta: 2 (2 de 2 utilizador(es) com e-mail). Os utilizadores Finanças da planta recebem sempre este e-mail; a caixa de Contas a Pagar é adicional.”** e “E-mail ao comprador enfileirado.”; lista de entrega com os dois utilizadores **Enviado**. FINP2 não recebe. Utilizador Finance inativo (se existir) não recebe.

`Resultado: ____`

### 8.2 Caixa de Contas a Pagar segundo `NotifyOnPoCancelled` (A para o caso “desligado”; C para o caso “ligado”)

1. Com o interruptor no valor por defeito (**desligado**, e sem forma de o ligar na UI/API — ver 1.4): em **nenhum** cancelamento aparece um e-mail dirigido ao endereço de Contas a Pagar, e a lista de entrega do cartão **não** contém esse endereço. Esperado e correto.
2. O caso **ligado** (e-mail “[Portal Gerencial] P.O. cancelada — … — Pedido REQ-…” com CC, como linha da lista de entrega, deduplicado por correlação) só é testável depois de o operador ativar o interruptor fora da UI — Apêndice C.4. Não executar neste guião.

`Resultado: ____`

### 8.3 Estado de entrega e reenvio auditado (A)

1. Num cartão cancelado recente, observe a lista de entrega: chips **Pendente** → **Enviado** com data/hora (o worker de outbox envia em ≤ 1–2 min).
2. Clique **Reenviar notificação** **imediatamente** → diálogo **“Reenviar notificação de cancelamento”** (“… será reenviada a Finanças com uma nova correlação. O cancelamento em si não é repetido. O servidor recusa reenvios com menos de 2 minutos de intervalo.”) → **Reenviar**. Esperado: erro **“A notificação foi enviada há menos de 2 minutos. Aguarde antes de reenviar…”** (`RESEND_TOO_SOON`).
3. Aguarde > 2 min e repita. Esperado: mensagem “Notificação de cancelamento reenviada (N destinatário(s) Finanças).”; cartão passa a mostrar “· 1 reenvio(s)”; nova lista de entrega (nova correlação) com todos os destinatários; e-mails com prefixo **“[Reenvio] …”**; histórico **“Notificação de cancelamento reenviada”**; **nenhuma** segunda linha “P.O. cancelada” (o cancelamento não se repete); **Finanças → Histórico** mostra “Notificação de cancelamento reenviada”.
4. **Sem destinatários Finanças** (B, reversível): retire temporariamente a planta P1 do âmbito de FIN1 e FIN2 em Gestão de Usuários; faça um novo cancelamento; esperado: sucesso com aviso **“Finanças não foi notificada por e-mail/aviso: nenhum utilizador ativo com o perfil Finanças tem acesso à planta deste grupo…”**, cartão com aviso âmbar “Nenhum utilizador Finanças elegível na planta…” e botão **Reenviar notificação** em destaque vermelho; **Administração → Logs do Sistema** → filtro **Tipo de Evento** = `PO_CANCELLATION_NO_FINANCE_RECIPIENT` → entrada presente. Reponha o âmbito de P1 e, após 2 min, **Reenviar notificação** → agora “E-mails enfileirados…”. **Obrigatório repor o âmbito** (anote em 9.3).
5. Falhas de SMTP (Falhou — nova tentativa / Não entregue) **não** são provocadas neste guião — Apêndice C.5.

`Resultado: ____`

### 8.4 Aviso ao solicitante — comportamento real (A)

- Cancelamento/reinício: o solicitante recebe **aviso in-app** (“P.O cancelada — itens voltam a cotação”/“itens encerrados”) **e e-mail** “P.O cancelada no pedido REQ-…”. Este e-mail é **comportamento implementado** segundo a decisão D16 tal como registada em `docs/PO_GROUP_CANCELLATION.md` §2 (“requester notified in-app + e-mail of the cancellation”). Se a intenção do proprietário for apenas o aviso in-app, registe em 9.2 como pedido de alteração; nada foi alterado no código.
- Confirmação/rejeição da ECF: **nenhum** e-mail nem aviso ao solicitante; só ao comprador responsável (verificado em 3.8 e 4.4).

`Resultado: ____`

---

## 9. Conclusão e limpeza — 20 min

### 9.1 Tabela de resultados

| # | Cenário | Parte | PASS/FAIL | Evidência (ficheiros) | Observações |
|---|---|---|---|---|---|
| 1.1 | Versão/build 2.249.1+<sha> | A | | | |
| 2.2-bis | Envio parcial: Importar/Inserir visíveis, “Cobertura Parcial”, menu ⋮ da linha 2 inteiro (1 ação) | A | | | |
| 1.2 | Redirecionamento SMTP ligado | A | | | |
| 1.3 | Contas e âmbitos | A | | | |
| 1.4 | Config AP (só leitura; interruptor sem UI) | A | | | |
| 2 | Pedidos sintéticos preparados (A–E) | A | | | |
| 2.5.3 | Devolução sem motivo → 400 | A | | | |
| 2.5.5 | Devolução “Corrigir P.O.” + correção inalterada | B | | | |
| 3.2 | Cancelar voltar a cotar + validações | A | | | |
| 3.3 | Evidência preservada, lote, documento “Sem efeito”, histórico | A | | | |
| 3.4 | E-mails do cancelamento (Finanças/comprador/participantes) | A | | | |
| 3.5 | Nova cotação + aprovações com confirmação pendente | A | | | |
| 3.6 | P.O. de substituição bloqueada | A | | | |
| 3.7 | Finanças confirma | A | | | |
| 3.8 | P.O. de substituição registada; e-mail só ao comprador | A | | | |
| 4 | Rejeição → bloqueio mantido → declaração atualizada → confirmação | A | | | |
| 5 | Encerrar itens → pedido cancelado; evidência e confirmação disponíveis | A | | | |
| 6.1 | Sem P.O.: sem ECF, sem bloqueio | A | | | |
| 6.2 | Grupo irmão intacto | A | | | |
| 6.3 | Devolução legada “tipo não classificado” | B | | | N/A se não existir pedido sintético pré-deploy |
| 6.3-bis | Segundo ciclo | B | | | |
| 6.4 | Mesmo número: aviso, explicação, histórico | A | | | |
| 6.5 | Recusas (agendado, pago, tipo Pagamento, ORDERED N/A) | B | | | |
| 6.6 | Moedas mistas / histórico vs ativo | B | | | |
| 6.7 | DUPLICATE_PO / OCR_MISMATCH | B | | | |
| 7.1 | SysAdmin sozinho | A | | | UI / API |
| 7.2 | Finanças de outra planta | A | | | UI / API |
| 7.3 | Própria declaração | A/B | | | UI / API |
| 7.4 | Finanças correto decide | A | | | |
| 7.5 | Comprador não responsável | B | | | UI / API |
| 8.1 | E-mail Finanças com interruptor AP desligado | A | | | |
| 8.2 | Caixa AP (desligado) | A | | | caso “ligado” = C.4 |
| 8.3 | Entrega + reenvio (409 < 2 min; sucesso > 2 min) | A | | | |
| 8.3.4 | Sem destinatários Finanças + log | B | | | âmbito reposto? |
| 8.4 | Aviso ao solicitante conforme D16 | A | | | |
| C.x | Verificações técnicas executadas | C | | | |

### 9.2 Critério para considerar PROD

Todos os cenários da **parte A** com PASS, incluindo as três verificações **API** de 7.1–7.3 (sem elas, a aplicação server-side de D12 em TEST fica apenas inferida dos testes automáticos). Qualquer FAIL em 3.6, 3.8, 4.5, 6.4 ou 7.x bloqueia PROD. Pedidos de alteração (ex.: e-mail ao solicitante, UI para `NotifyOnPoCancelled`, campo de evidência no diálogo) registam-se aqui e **não** se corrigem em TEST sem nova versão:
- ____________________________________________

### 9.3 Reposição de configurações alteradas durante os testes

| Configuração | Valor original | Reposto? |
|---|---|---|
| SMTP “Redirecionar todos os e-mails…” | | |
| AP “Também enviar e-mail individual aos utilizadores Finance” | | |
| Âmbito de planta de FIN1/FIN2 (8.3.4) | | |
| Conta Finance desativada (7.6) | | |

### 9.4 Registos sintéticos — identificar, reter ou encerrar (sem apagar auditoria)

- Identificação: título começa por **`TEST v2.249.0`**; filtre em **Pedidos** por esse texto; os números REQ-… estão anotados em 2.1.
- **Não apagar** pedidos, cancelamentos, anexos “Sem efeito” nem linhas de histórico: são a evidência da aceitação e os registos de cancelamento/confirmação não têm UI de eliminação (por desenho).
- Encerramento recomendado: pedidos com grupos ainda ativos (A, B, D, E, F) → como BUYER, **Cancelar P.O.** → **Encerrar itens** com motivo **Outro (explicar)** e explicação “Encerramento de dados de teste v2.249.0”; quando houver P.O. registada, FIN1 confirma a anulação declarada (ECF fictícia). O pedido fica **Cancelado** e permanece pesquisável. O pedido C já está cancelado; G (tipo Pagamento) pode ser cancelado pelo fluxo normal de pedidos de pagamento ou retido.
- Caixa de e-mail de teste: arquivar os e-mails com os assuntos capturados numa pasta “v2.249.0”.

---

## Apêndice C — Verificações técnicas (opcionais; logs, API, SQL só de leitura)

### C.1 Logs do Sistema
**Administração → Logs do Sistema** → campo **Tipo de Evento**: `PO_CANCELLATION_NO_FINANCE_RECIPIENT` (8.3.4), `EMAIL_OUTBOX_RETRY_SCHEDULED` e `EMAIL_OUTBOX_DEAD_LETTER` (só se C.5 for executado). Campo **Correlation ID** aceita o id de correlação mostrado em “Notificação:” do cartão, quando disponível.

### C.2 Chamadas diretas à API (prova de aplicação server-side)
Use as ferramentas de programador do browser (**Network → copiar como fetch/cURL**) numa sessão autenticada com a conta em teste, ou uma ferramenta REST com o mesmo token da sessão. Nunca use credenciais de outra pessoa.
- `POST /api/v1/finance/po-cancellations/{cancellationId}/confirm` com corpo `{}` e `POST …/reject` com `{"note":"<≥20 caracteres>"}`:
  - SYSADMIN sem Finance → **403**, `code` = `FINANCE_SCOPE_REQUIRED`;
  - FINP2 (outra planta) → **404**;
  - BUYERFIN que declarou → **403**, `SELF_CONFIRMATION_FORBIDDEN`;
  - FIN1 válido numa confirmação já feita → **409**, `FINANCE_CONFIRMATION_ALREADY_DONE`;
  - FIN1 num cancelamento sem P.O. (pedido D) → **409**, `FINANCE_CONFIRMATION_NOT_REQUIRED`.
- `GET /api/v1/finance/po-cancellations?status=OPEN` → por linha `canReview` e `reviewBlockReason` coerentes com a UI; FINP2 não recebe itens de P1.
- `POST /api/v1/requests/{requestId}/po-groups/{groupId}/cancel` por um Buyer não responsável → **403** `NOT_RESPONSIBLE_BUYER`; repetido num grupo já cancelado → **409** `GROUP_ALREADY_CANCELLED`.
- `POST /api/v1/requests/{id}/operational/register-po` num grupo bloqueado → **409** `PRIOR_ECF_CONFIRMATION_PENDING`; `POST /api/v1/attachments/upload/{requestId}` de tipo `PO` para esse grupo → **409** com título “Registo de P.O bloqueado”.
- `POST …/cancellation/resend-notification` dentro de 2 min → **409** `RESEND_TOO_SOON`.
O `cancellationId` obtém-se em `GET /api/v1/requests/{requestId}/po-groups/{groupId}/cancellation` ou na resposta da lista Finance.

### C.3 Tipo Pagamento e outros códigos de recusa só por API
`POST …/po-groups/{groupId}/cancel` num pedido de tipo Pagamento (se tiver grupo) → **409** `REQUEST_TYPE_NOT_SUPPORTED`; num grupo em **Aguardando P.O.** de lote ainda não aprovado → `GROUP_STATUS_NOT_CANCELLABLE`.

### C.4 Caixa de Contas a Pagar com `NotifyOnPoCancelled` ligado
Não existe campo na UI nem na API (`/api/v1/ap-notification-configs` ignora a coluna). Ativar exige uma alteração direta à coluna `AccountsPayableNotificationConfigs.NotifyOnPoCancelled` pelo operador da base de dados de TEST — **fora deste guião** e a decidir pelo proprietário. Se for feita: um novo cancelamento deve gerar e-mail “[Portal Gerencial] P.O. cancelada — … — Pedido REQ-…” para o endereço AP com os CC configurados (redirecionado em TEST), a linha correspondente na lista de entrega, e um reenvio gera **uma** nova linha (nova correlação). Registar o pedido de UI para o interruptor em 9.2.

### C.5 Injeção de falha de SMTP (disruptivo — só com autorização e fora do horário de uso partilhado)
Em **Gestão de Integrações → SMTP**, alterar temporariamente a porta/host para um valor inválido, fazer um cancelamento, observar na lista de entrega **Falhou — nova tentativa** (tentativa 1, próxima em ~30 s), depois ~120 s, ~600 s e finalmente **Não entregue** com o erro; **Logs do Sistema** com `EMAIL_OUTBOX_RETRY_SCHEDULED` e `EMAIL_OUTBOX_DEAD_LETTER`. Repor o SMTP, aguardar > 2 min e **Reenviar notificação** → nova lista **Enviado**. Afeta todos os e-mails de TEST enquanto durar; não executar durante outros testes.

### C.6 Consultas SQL só de leitura (operador de TEST, nunca UPDATE)
```sql
-- cancelamentos e estado de confirmação dos pedidos sintéticos
SELECT r.RequestNumber, r.Title, c.Kind, c.ReasonCode, c.EcfStatus, c.EcfReference,
       c.FinanceConfirmationRequired, c.FinanceConfirmationStatus, c.FinanceEmailPolicyOutcome,
       c.FinanceRecipientsResolvedCount, c.FinanceEmailsQueuedCount, c.NotificationResendCount, c.LastNotificationCorrelationId
FROM RequestPoGroupCancellations c JOIN Requests r ON r.Id = c.RequestId
WHERE r.Title LIKE 'TEST v2.249.0%' ORDER BY c.CancelledAtUtc;

-- entregas de outbox da última correlação de um cancelamento
SELECT RecipientEmail, Status, RetryCount, NextRetryAtUtc, LastError, ProcessedAtUtc
FROM EmailOutbox WHERE CorrelationId = '<LastNotificationCorrelationId>';

-- itens libertados do lote (D7)
SELECT bi.Id, bi.ReleaseKind, bi.ReleasedAtUtc, bi.PoGroupCancellationId FROM ApprovalBatchItems bi
WHERE bi.PoGroupCancellationId IS NOT NULL;
```
Os nomes de colunas seguem a migração `20261028082044_AddPoGroupCancellation`; se uma coluna não existir, a migração não foi aplicada — parar e verificar o deploy.
