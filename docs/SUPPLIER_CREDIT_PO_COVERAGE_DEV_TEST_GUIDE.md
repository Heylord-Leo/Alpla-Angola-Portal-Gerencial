# Crédito em conta corrente do fornecedor — Guia de teste manual (Stage 1)

> **Estado (2026-11-12):** a 1.ª ronda em DEV foi executada e aceite pelo proprietário (registo em
> [SUPPLIER_CREDIT_PO_COVERAGE.md §10](SUPPLIER_CREDIT_PO_COVERAGE.md)). As seis correções de feedback estão aplicadas na árvore
> de trabalho. A **2.ª ronda em DEV** segue o guia focado
> [SUPPLIER_CREDIT_PO_COVERAGE_DEV_RETEST_GUIDE.md](SUPPLIER_CREDIT_PO_COVERAGE_DEV_RETEST_GUIDE.md); este guia completo
> mantém-se como referência dos cenários e dos pontos de controlo.

**Ordem obrigatória:** a primeira validação é sempre em **DEV**. Só depois da aceitação em DEV se passa a **TEST**. PROD fica fora
deste guia.

Base verificada: árvore de trabalho de 2026-11-10 (HEAD `3d57d95` v2.249.9 + Stage 1 **não commitado**), documento técnico
[SUPPLIER_CREDIT_PO_COVERAGE.md](SUPPLIER_CREDIT_PO_COVERAGE.md). Todos os nomes de menus, campos, botões e mensagens abaixo
foram copiados do código atual.

Fora do âmbito de sucesso do Stage 1 (não testar como cenário de sucesso): **Antecipado Parcial**, pedidos **PAYMENT**,
**reversão após cobertura parcial** (esperada como recusa, ver 2.4.3). Este guia não executa commits, migrações, deploys nem
alterações de configuração: lista-os para revisão.

---

## 1. Preparação em DEV

### 1.1 Estado atual de DEV (verificado em 2026-11-10, só leitura)

| Item | Estado | Conclusão |
|---|---|---|
| Base `Portal-Gerencial-Dev-ProdClone` | `__EFMigrationsHistory` tem `20261109150614_AddSupplierCreditAccounts` como última migração; tabelas `SupplierCreditAccounts` / `SupplierCreditReservations` existem; coluna `RequestPoGroups.CreditCoverageAmount` existe; 0 contas, 0 reservas | **Migração já aplicada** (o arranque da API de 2026-11-09 16:34 executou `Database.Migrate()`, como sempre em Development). Nada a fazer. |
| API DEV (porta 5000, PID 30516) | arrancou 2026-11-09 16:34:09 com um build de 16:33–16:34 | Contém o Stage 1 **excepto** duas correções posteriores (16:47) em `FinancePaymentEligibilityService.cs` e `SupplierCreditSettlementService.cs`. **Precisa de rebuild + restart.** |
| Frontend DEV (porta 5173, PID 57536, Vite) | arrancou 2026-11-09 16:33:15 | Vite recarrega ficheiros alterados; ainda assim reiniciar junto com a API para garantir árvore idêntica à testada. |
| `appsettings.json` (commitado) | `SupplierCredit:NewReservationsEnabled = false` | Correcto: o smoke de 2.0 corre com a flag desligada. |
| `appsettings.Development.json` (gitignored) | **sem** secção `SupplierCredit` | Para os testes funcionais acrescentar a secção (1.3). |

### 1.2 Comandos de preparação (para a sua revisão; não executados)

O bloqueio anterior da pasta `src\backend\AlplaPortal.Api\bin` era causado pela própria API em execução. O script canónico
mata primeiro o processo da porta 5000, por isso o `dotnet build` seguinte já não encontra a pasta bloqueada.

```powershell
# 1) Reiniciar API + frontend com rebuild (script canónico; mata as portas 5000 e 5173, valida a base DEV, faz dotnet build e arranca)
powershell -ExecutionPolicy Bypass -File C:\dev\alpla-portal\execution\restart_services.ps1
```

Efeito esperado: duas janelas `cmd` (backend e frontend); na consola do script `[OK]   Verified: Server=(localdb)\MSSQLLocalDB, Database=Portal-Gerencial-Dev-ProdClone`;
o backend compila sem erros e responde em `http://localhost:5000`; o frontend em `http://localhost:5173`. A migração não volta a
correr (já aplicada). Se a janela do backend mostrar erro de ficheiro bloqueado, o processo antigo não morreu:

```powershell
# 2) Só se o passo 1 reportar bin bloqueado: confirmar e terminar o processo que ainda ocupa a porta 5000, depois repetir o passo 1
Get-NetTCPConnection -LocalPort 5000 -State Listen | Select-Object OwningProcess
Stop-Process -Id <PID_da_porta_5000> -Force
```

Verificação de que o build inclui o Stage 1 (após o passo 1):

- Abrir **Contratos → Fichas de Fornecedor → (qualquer ficha)**: tem de aparecer a secção **"Créditos em conta corrente"**.
- `GET http://localhost:5000/api/v1/lookups/suppliers/{supplierId}/credit-accounts` com token → `200` e corpo `{ "items": [], "canManage": … }`.

### 1.3 Flag `SupplierCredit:NewReservationsEnabled` em DEV

A flag é lida por `IOptions<SupplierCreditOptions>` (instância única criada no arranque). **Alterar o ficheiro não tem efeito até
reiniciar a API.** A flag governa só reservas novas ou alteração de valor; aplicação, libertação, reversão e Manter/Remover
funcionam com a flag desligada.

Para ligar (após o smoke 2.0), acrescentar em `src\backend\AlplaPortal.Api\appsettings.Development.json` (ficheiro gitignored,
nunca sai desta máquina):

```json
"SupplierCredit": { "NewReservationsEnabled": true }
```

e repetir o passo 1 de 1.2. Para desligar (teste 2.5) mudar para `false` e reiniciar de novo.

Como verificar a configuração efectiva (fazer sempre após cada restart):

- **UI:** no modal "Emitir P.O Sistêmica", escolhida a condição "Pós-Pago", o bloco "Utilizar crédito em conta corrente" mostra a
  caixa de verificação "Utilizar crédito em conta corrente nesta P.O" (flag ligada) ou o aviso
  "A utilização de crédito em conta corrente em novas P.O. não está ativa neste ambiente." (flag desligada).
- **API:** `GET /api/v1/requests/{requestId}/po-groups/{groupId}/credit-eligibility?paymentConditionCode=POST_PAID` → campo
  `enabled` (`true`/`false`) e `reasonCode` (`SUPPLIER_CREDIT_DISABLED` quando desligada e sem reserva).

### 1.4 Perfis, acessos e utilizadores

| Papel no teste | Perfil do Portal (nome exacto) | Onde actua | Requisitos |
|---|---|---|---|
| **Compras** | `Contracts` | Contratos → Fichas de Fornecedor → ficha → "Créditos em conta corrente" (botões "Nova conta", "Registar movimento") | A rota `/contracts` exige o perfil `Contracts` no utilizador; o perfil "System Administrator" sozinho **não** abre o menu Contratos (só a API). Na API, `canManage = true` para `Contracts` e para "System Administrator". |
| **Comprador responsável** | `Buyer` | Compras & Logística → Pedidos → pedido → "REGISTRAR P.O (…)" / "CORRIGIR P.O (…)"; Gestão de Cotações → workspace do pedido → "Cancelar P.O." e "Ver Perfil Completo" | Tem de ser o comprador do pedido (`Request.BuyerId` ou comprador responsável do grupo) e ter âmbito de planta/departamento do pedido. Outro comprador vê o aviso "Apenas o comprador responsável pelo pedido pode utilizar crédito em conta corrente." |
| **Finanças F1** | `Finance` activo, com âmbito da planta do pedido | Finanças → Pagamentos (aplica crédito, paga, agenda, devolve) | — |
| **Finanças F2** | `Finance` activo, **utilizador diferente de F1**, mesmo âmbito de planta | Finanças → Pagamentos → "Reverter crédito aplicado" | A reversão exige perfil `Finance` real na base (um "System Administrator" sem `Finance` recebe "Perfil Finanças necessário") e utilizador ≠ quem aplicou ("Reversão pela mesma pessoa"). |
| Local Manager (opcional) | `Local Manager` | só a API de libertação da reserva | — |
| Segundo comprador (opcional) | `Buyer` não responsável pelo pedido | teste negativo 2.2.1-N | — |

Utilizadores existentes no clone DEV (contagem, só leitura): Buyer 5, Contracts 9, Finance 7 (os 7 com âmbito de planta),
Local Manager 12, System Administrator 2. Use as contas com que já entra em DEV; escolha F1 e F2 entre os 7 de Finanças.

Passos que exigem **dois utilizadores de Finanças distintos**: 2.4.1 (F1 aplicou em 2.2.2, F2 reverte) e o teste negativo
2.4.2 (F1 tenta reverter o que ele próprio aplicou → recusa).

Para as **verificações API**: o token da sessão do browser está em `sessionStorage` (chave `auth_token`). Na consola do
DevTools: `sessionStorage.getItem('auth_token')`. Enviar em `Authorization: Bearer <token>`, base `http://localhost:5000`.

### 1.5 Dados de teste (AOA)

Empresas e plantas do clone (só leitura): **AlplaPLASTICO** (código APA; plantas **Viana 1** e **Viana 2**) e **AlplaSOPRO**
(código APS; planta **Viana 3**). A conta corrente é por fornecedor × **empresa compradora do pedido** × moeda; por isso Viana 1 e
Viana 2 partilham a conta APA e Viana 3 usa a conta APS.

**Fornecedor X:** escolha na lista "Fichas de Fornecedor" um fornecedor com estado "Ativo", que **não** seja empresa do grupo
ALPLA (a criação recusa com "Empresa interna") e sem P.O. em curso. Anote `supplierId` (URL `/contracts/fichas/{supplierId}`).

| Conta | Empresa compradora | Moeda | Crédito inicial | Finalidade |
|---|---|---|---|---|
| **A** | AlplaPLASTICO | AOA | 1 500 000,00 | todos os casos (Viana 1 e Viana 2) |
| **B** | AlplaSOPRO | AOA | 200 000,00 | prova de saldo separado da APS |

Pedidos QUOTATION do fornecedor X (empresa AlplaPLASTICO). Cada um precisa de chegar ao estado em que a página do pedido mostra o
botão **"REGISTRAR P.O (Fornecedor X)"** (cotação do fornecedor X seleccionada e aprovada, grupo a aguardar P.O), com a cotação
seleccionada no valor indicado, para que o "Valor da P.O" coincida:

| P.O | Planta | Condição | Valor da P.O | Crédito a utilizar | Parcela bancária | Teste |
|---|---|---|---|---|---|---|
| **C1** | Viana 1 | Pós-Pago | 500 000,00 | 350 000,00 (parcial) | 150 000,00 | 2.2.1 |
| **C2** | Viana 2 | Pós-Pago | 300 000,00 | 300 000,00 (integral) | 0,00 | 2.2.2, 2.4.1 |
| **C3** | Viana 1 | 100% Antecipado | 400 000,00 | 250 000,00 (parcial) | 150 000,00 | 2.2.3 |
| **C4** | Viana 2 | 100% Antecipado | 200 000,00 | 200 000,00 (integral) | 0,00 | 2.2.4 |
| **C5** | Viana 1 | Pós-Pago | 250 000,00 | 100 000,00 → 150 000,00 → removido | 150 000,00 → 100 000,00 → 250 000,00 | 2.3.1 |
| **C6** | Viana 2 | Pós-Pago | 180 000,00 | 180 000,00 (integral) | 0,00 | 2.3.2 (cancelamento) |
| **C7** | Viana 1 | Pós-Pago | 120 000,00 | 120 000,00 (integral) | 0,00 | 2.5 (reservada antes de desligar a flag) |
| **C8** | Viana 2 | Pós-Pago | 100 000,00 | tentativa com flag desligada | — | 2.5 (recusa) |

**Pontos de controlo da conta A** (coluna "Saldo" = soma dos movimentos; "Reservado" = reservas vivas ainda não aplicadas;
"Disponível" = Saldo − Reservado). A aplicação baixa o Saldo e o Reservado no mesmo valor, por isso **não altera o Disponível**.

| # | Depois de… | Saldo | Reservado | Disponível | Movimento no saldo? |
|---|---|---|---|---|---|
| P0 | crédito inicial (2.1) | 1 500 000,00 | 0,00 | 1 500 000,00 | sim, +1 500 000,00 "Crédito registado" |
| P1 | reserva C1 350 000 | 1 500 000,00 | 350 000,00 | 1 150 000,00 | não (só linha "Reserva — Reservado") |
| P2 | reserva C2 300 000 | 1 500 000,00 | 650 000,00 | 850 000,00 | não |
| P3 | reserva C3 250 000 | 1 500 000,00 | 900 000,00 | 600 000,00 | não |
| P4 | reserva C4 200 000 | 1 500 000,00 | 1 100 000,00 | 400 000,00 | não |
| P5 | Finanças paga C1 150 000 (crédito 350 000 aplicado) | 1 150 000,00 | 750 000,00 | 400 000,00 | sim, −350 000,00 "Crédito aplicado" |
| P6 | Finanças aplica crédito C2 | 850 000,00 | 450 000,00 | 400 000,00 | sim, −300 000,00 |
| P7 | Finanças confirma adiantamento C3 150 000 (crédito 250 000 aplicado) | 600 000,00 | 200 000,00 | 400 000,00 | sim, −250 000,00 |
| P8 | Finanças aplica crédito C4 | 400 000,00 | 0,00 | 400 000,00 | sim, −200 000,00 |
| P9 | reserva C5 100 000 | 400 000,00 | 100 000,00 | 300 000,00 | não |
| P10 | devolução por Finanças + correção "Manter" | 400 000,00 | 100 000,00 | 300 000,00 | não |
| P11 | devolução + correção "Alterar" para 150 000 | 400 000,00 | 150 000,00 | 250 000,00 | não (linha "Reserva — Libertado" + nova "Reserva — Reservado") |
| P12 | devolução + correção "Remover" | 400 000,00 | 0,00 | 400 000,00 | não |
| P13 | reserva C6 180 000 | 400 000,00 | 180 000,00 | 220 000,00 | não |
| P14 | "Cancelar P.O." C6 | 400 000,00 | 0,00 | 400 000,00 | **não** (linha "Reserva — Libertado") |
| P15 | F2 reverte C2 | 700 000,00 | 0,00 | 700 000,00 | sim, +300 000,00 "Aplicação revertida" |
| P16 | reserva C7 120 000 (flag ligada) | 700 000,00 | 120 000,00 | 580 000,00 | não |
| P17 | flag desligada: C8 recusada; Finanças aplica crédito C7 | 580 000,00 | 0,00 | 580 000,00 | sim, −120 000,00 |

A conta **B** fica em 200 000,00 / 0,00 / 200 000,00 durante todo o guia.

---

## 2. Sequência de testes em DEV

Convenções de cada teste: **Perfil → Onde → Introduzir → Deve aparecer → Evidência**. Evidência = captura de ecrã com data/hora
visível e, quando indicado, o JSON da verificação API. Verificações que a UI esconde estão marcadas **[Verificação API]**.

### 2.0 Smoke com a flag desligada (estado commitado)

Pré-condição: `appsettings.Development.json` **sem** secção `SupplierCredit`; API reiniciada (1.2).

1. **Compras** → Contratos → Fichas de Fornecedor → ficha do fornecedor X. Deve aparecer a secção "Créditos em conta corrente"
   com "0 conta(s)", o texto "Este fornecedor não tem contas correntes de crédito." e o botão "Nova conta". Evidência: captura.
2. **Compras** → "Nova conta" → Empresa compradora "AlplaPLASTICO", Moeda "AOA" → "Criar". Toast "Conta corrente de crédito
   criada."; linha com número `CCS-000001` (ou seguinte), Saldo 0,00, Reservado 0,00, Disponível 0,00, Estado "Ativa". Isto prova
   que o registo de contas **não depende da flag**. Evidência: captura da tabela.
3. **Comprador** → Pedidos → pedido C1 → "REGISTRAR P.O (Fornecedor X)" → modal "Emitir P.O Sistêmica" → Condição de Pagamento
   "Pós-Pago". O bloco "Utilizar crédito em conta corrente" aparece **sem** caixa de verificação, com o aviso
   "A utilização de crédito em conta corrente em novas P.O. não está ativa neste ambiente.". Fechar o modal sem registar.
   Evidência: captura do bloco.
4. **[Verificação API]** `GET /api/v1/requests/{requestId}/po-groups/{groupId}/credit-eligibility?paymentConditionCode=POST_PAID`
   → `200`, `enabled: false`, `canReserve: false`, `reasonCode: "SUPPLIER_CREDIT_DISABLED"`. Evidência: JSON.
5. **Finanças F1** → Finanças → Pagamentos: a fila abre normalmente; nenhuma obrigação mostra chip de crédito. Evidência: captura.

Resultado esperado do smoke: sem erros de consola/API; nada mais muda. Depois: ligar a flag (1.3) e reiniciar.

### 2.1 Registo do fornecedor (Compras)

Pré-condição: flag ligada e verificada (1.3).

1. **Compras** → ficha do fornecedor X → "Nova conta" → Empresa compradora "AlplaSOPRO", Moeda "AOA" → "Criar". Deve aparecer a
   2.ª linha (conta **B**), "2 conta(s)". Tentar criar de novo AlplaPLASTICO/AOA → erro "Conta já existe". Evidência: captura +
   toast de erro.
2. **Compras** → linha da conta **A** → "Movimentos" → "Registar movimento" → Tipo "Crédito registado (a favor)", Valor
   `1500000,00`, "Referência do documento *" = `NC-TESTE-A-001`, "Data do documento" = hoje, "Justificativa * (mín. 10
   caracteres)" = `Crédito inicial de teste DEV` → "Registar". Toast "Movimento registado."; a linha da conta A passa a Saldo
   1 500 000,00 / Reservado 0,00 / Disponível 1 500 000,00 (**P0**); na tabela "Movimentos — CCS-… (AlplaPLASTICO · AOA)" surge
   uma linha Tipo "Crédito registado", Valor +1 500 000,00 (verde), "Saldo após" 1 500 000,00, "Referência / motivo"
   `NC-TESTE-A-001 — Crédito inicial de teste DEV`, "Por" = o seu nome. Evidência: captura da tabela e dos movimentos.
3. Repetir para a conta **B** com Valor `200000,00`, referência `NC-TESTE-B-001`. Deve ficar 200 000,00 / 0,00 / 200 000,00.
4. Testes negativos de validação (conta A, "Registar movimento"): Valor `0` → toast "Indique um valor superior a zero."; sem
   referência → "A referência do documento do fornecedor é obrigatória."; justificativa com 5 caracteres → "A justificativa deve
   ter pelo menos 10 caracteres.". Evidência: toasts.
5. **Leitura pelo comprador:** **Comprador** → Gestão de Cotações → workspace de um pedido do fornecedor X → cartão do fornecedor →
   "Ver Perfil Completo" → painel "Ficha do fornecedor": a secção "Créditos em conta corrente" mostra as duas contas **sem** os
   botões "Nova conta" / "Registar movimento". Evidência: captura.
6. **[Verificação API] Finanças só leitura:** com o token de F1, `GET /api/v1/lookups/suppliers/{supplierId}/credit-accounts` →
   `200` com `canManage: false`; `POST` no mesmo endereço com `{"companyId":1,"currencyCode":"USD"}` → `403`. Com o token de
   Compras o mesmo `GET` devolve `canManage: true`. Evidência: dois JSON. (A rota de UI `/contracts` não abre para Finanças, por
   isso esta prova é feita por API.)

### 2.2 Os quatro casos QUOTATION

Fluxo comum do **Comprador** para registar: Pedidos → pedido → "REGISTRAR P.O (Fornecedor X)" → modal "Emitir P.O Sistêmica" →
anexar o PDF da P.O e preencher os campos habituais → "Condição de Pagamento" → bloco "Utilizar crédito em conta corrente" →
"REGISTRAR P.O". O bloco mostra "Conta", "Saldo", "Reservado", "Disponível para esta P.O" lidos do servidor; ao marcar a caixa
"Utilizar crédito em conta corrente nesta P.O" o campo "Valor de crédito a utilizar (máx. … AOA)" é pré-preenchido com o máximo e
aparece o resumo "Valor da P.O" / "Crédito em conta corrente" / "Pagamento bancário" (ou "Adiantamento bancário" em 100%
Antecipado); cobertura integral mostra "0,00 AOA — cobertura integral".

Fluxo comum de **Finanças F1**: Finanças → Pagamentos → cartão "Aguardando Agendamento" ou "Pagamento Pendente" → linha do pedido.
Em grupos com crédito a linha mostra o chip ("Crédito reservado", "Crédito integral", "Crédito aplicado"…) e, ao passar o rato,
"Crédito reservado: …", "Valor a pagar (banco): …", "Conta corrente: …". Acções: botão principal e menu ⋮ (kebab).

#### 2.2.1 C1 — Pós-Pago, cobertura parcial (350 000 de 500 000)

- **Comprador** introduz: "Pós-Pago"; caixa marcada; Valor de crédito `350000,00`. Deve aparecer: resumo Valor da P.O 500 000,00 /
  Crédito 350 000,00 / Pagamento bancário 150 000,00. Após "REGISTRAR P.O": grupo em `PO_ISSUED`; no histórico do pedido uma
  linha `SUPPLIER_CREDIT_RESERVED` (a página do pedido mostra o código em bruto; o texto diz "Crédito em conta corrente reservado…
  Banco: 150 000,00"). Ficha conta A = **P1**; em "Movimentos" surge "Reserva — Reservado", coluna Reserva 350 000,00, Pedido / P.O
  com o número do pedido e da P.O, planta "Viana 1". Evidência: capturas (resumo do bloco, histórico, ficha).
- Teste negativo de valor: antes de registar, escrever `600000,00` → campo a vermelho "Indique um valor entre 0,01 e
  500 000,00 AOA." e o botão não submete. Evidência: captura.
- **Finanças F1** vê: linha de C1 em "Aguardando Agendamento" com chip "Crédito reservado", "Valor a pagar (banco): 150 000,00".
  Botão principal "Agendar pagamento"; menu ⋮ com "Pagar", "Devolver para ajuste", "Adicionar observação", "Detalhes".
  **Não** aparece "Aplicar crédito em conta corrente" (há parcela bancária pendente).
- **Finanças F1** usa "Agendar pagamento" → modal "Agendar Pagamento" → data → "CONFIRMAR". A linha passa a "Pagamento
  Pendente" com valor agendado 150 000,00 (nunca 500 000,00). Depois ⋮ → "Cancelar agendamento" (motivo ≥ 20 caracteres) → volta a
  "Aguardando Agendamento" e a obrigação continua a existir. Evidência: capturas antes/depois.
- **Finanças F1** usa "Pagar" → modal "Confirmar Liquidação" → "Data do Pagamento (obrigatório)", "Montante Efetivamente Pago
  (obrigatório)" = `149999,99`, "Comprovante de Pagamento (obrigatório)" (qualquer PDF) → "CONFIRMAR". Deve recusar com
  "Montante divergente da parcela bancária" (texto: "…a parcela bancária a pagar é exatamente 150 000,00 AOA. Montante informado:
  149 999,99. Nenhuma alteração foi efetuada."). Repetir com `150000,01` → mesma recusa. Repetir com `150000,00` → sucesso.
  Deve acontecer: grupo `PAYMENT_COMPLETED`; ficha conta A = **P5** (Saldo 1 150 000,00; Disponível inalterado 400 000,00 se já
  tiver reservado C2–C4, ou 1 150 000,00 se C1 for a única reserva — o Disponível **não muda** com a aplicação); movimento
  "Crédito aplicado" −350 000,00 com "(SINGLE)" na coluna Tipo; Finanças → Histórico mostra "Pago" (150 000,00) **e** "Crédito
  aplicado" como etiquetas separadas. Evidência: 3 capturas (duas recusas, sucesso) + ficha + histórico.
- **[Verificação API]** `POST /api/v1/finance/{requestId}/apply-credit` com `{"requestPoGroupId":"{groupId}"}` **antes** de
  pagar → `409` "Parcela bancária pendente". Depois de pago → `409` "Crédito já aplicado". Evidência: JSON.

#### 2.2.1-N Outro comprador (negativo)

- **Segundo comprador** (não responsável) abre o mesmo pedido C1 antes do registo (ou qualquer pedido do fornecedor X ainda a
  aguardar P.O). Se o botão "REGISTRAR P.O" estiver visível, o bloco mostra o aviso "Apenas o comprador responsável pelo pedido
  pode utilizar crédito em conta corrente." e nenhuma caixa. **[Verificação API]** com o token desse comprador:
  `GET …/credit-eligibility?paymentConditionCode=POST_PAID` → `canReserve: false`, `reasonCode: "NOT_RESPONSIBLE_BUYER"`.
  Evidência: captura + JSON.

#### 2.2.2 C2 — Pós-Pago, cobertura integral (300 000 de 300 000)

- **Comprador** introduz: "Pós-Pago"; caixa marcada; valor pré-preenchido `300000,00` (manter). Resumo: Pagamento bancário
  "0,00 AOA — cobertura integral". Após registar: grupo `PO_ISSUED`; ficha = **P2**.
- **Finanças F1** vê: chip "Crédito integral", "Valor a pagar (banco): 0,00"; botão principal **"Aplicar crédito"**; o menu ⋮
  **não** tem "Agendar pagamento" nem "Pagar". Evidência: captura.
- **[Verificação API] bloqueio do pagamento bancário:**
  `POST /api/v1/finance/{requestId}/schedule` com `{"requestPoGroupId":"{groupId}","scheduledDate":"2026-11-20T00:00:00Z"}` →
  `409` "Liquidação por crédito em conta corrente" (`code: CREDIT_APPLICATION_REQUIRED`);
  `POST /api/v1/finance/{requestId}/pay` com `{"requestPoGroupId":"{groupId}","paymentProofAttachmentId":"{attachmentId}","actualPaidAmount":300000,"paidDate":"2026-11-10T00:00:00Z"}`
  → `409` com o mesmo título. Evidência: dois JSON.
- **Finanças F1** usa "Aplicar crédito" → modal "Confirmar Utilização do Crédito" com Fornecedor, Conta corrente, Valor da P.O
  300 000,00, "Crédito a aplicar" 300 000,00, "Valor a pagar (banco)" 0,00 → "CONFIRMAR". Toast "Crédito em conta corrente
  aplicado."; a linha sai de "Aguardando Agendamento"; grupo `PAYMENT_COMPLETED`; ficha = **P6**; Finanças → Histórico mostra
  "Crédito aplicado" (não "Pago"). Repetir "Aplicar crédito" por API → `409` "Crédito já aplicado". Evidência: capturas + JSON.

#### 2.2.3 C3 — 100% Antecipado, cobertura parcial (250 000 de 400 000)

- **Comprador** introduz: "100% Antecipado"; caixa marcada; Valor `250000,00`. Resumo: "Adiantamento bancário" 150 000,00. Após
  registar: grupo `ADVANCE_PAYMENT_REQUIRED`; ficha = **P3**.
- **Finanças F1** vê: chip "Crédito reservado", "Valor a pagar (banco): 150 000,00"; botão "Agendar adiantamento"; ⋮ "Pagar
  adiantamento". Sem "Aplicar crédito".
- **Finanças F1** usa "Pagar adiantamento" → modal "Confirmar Adiantamento" → "Montante Efetivamente Pago (obrigatório)" `149999,99`
  → recusa "Montante divergente da parcela bancária"; `150000,00` → sucesso. Deve acontecer: grupo `WAITING_SUPPLIER_DELIVERY`;
  ficha = **P7**; movimento "Crédito aplicado (ADVANCE)" −250 000,00; Histórico "Adiantamento Realizado" + "Crédito aplicado".
  Evidência: capturas.

#### 2.2.4 C4 — 100% Antecipado, cobertura integral (200 000 de 200 000)

- **Comprador** introduz: "100% Antecipado"; caixa marcada; valor `200000,00`. Resumo "0,00 AOA — cobertura integral". Grupo
  `ADVANCE_PAYMENT_REQUIRED`; ficha = **P4**.
- **Finanças F1** vê: chip "Crédito integral", botão principal "Aplicar crédito"; sem "Agendar adiantamento"/"Pagar adiantamento".
- **[Verificação API]** `POST /api/v1/requests/{requestId}/b2p/schedule-advance` e `POST …/b2p/confirm-advance` (corpo igual ao
  dos adiantamentos habituais, com `requestPoGroupId`) → `409` "Liquidação por crédito em conta corrente … Utilize "Aplicar
  crédito"". Evidência: JSON.
- **Finanças F1** usa "Aplicar crédito" → "CONFIRMAR". Grupo `WAITING_SUPPLIER_DELIVERY` (Recebimento pode seguir); ficha = **P8**
  (Reservado 0,00). Evidência: capturas.

#### 2.2.5 Verificação cruzada Viana 1 / Viana 2 / APS

- Nos modais de C1 (Viana 1) e C2 (Viana 2) a linha "Conta" mostra o **mesmo** número `CCS-…` · AlplaPLASTICO · AOA e os mesmos
  Saldo/Reservado. Evidência: as capturas de 2.2.1 e 2.2.2.
- **[Verificação API]** para um pedido QUOTATION de **Viana 3** do fornecedor X (se existir; opcional):
  `GET …/credit-eligibility?paymentConditionCode=POST_PAID` → `account.companyName: "AlplaSOPRO"`, `account.balance: 200000`,
  `availableForGroup: 200000`. Se não houver pedido de Viana 3, basta a ficha mostrar a conta B em 200 000,00 / 0,00 / 200 000,00
  depois de todas as reservas da conta A. Evidência: JSON ou captura.
- Finanças → Visão Geral: o total pago em dinheiro inclui 150 000,00 (C1) + 150 000,00 (C3) e **não** os 1 100 000,00 de crédito.
  Evidência: captura.

### 2.3 Correção e libertação

#### 2.3.1 C5 — devolução por Finanças e Manter / Alterar / Remover

1. **Comprador** regista C5: "Pós-Pago", crédito `100000,00` (banco 150 000,00). Ficha = **P9**.
2. **Finanças F1** → linha C5 → ⋮ "Devolver para ajuste" → modal "Devolver Pedido para Ajuste" → justificativa → "CONFIRMAR".
   Deve acontecer: grupo `WAITING_PO_CORRECTION`; a linha sai da fila; **a ficha mantém Reservado 100 000,00** (**P10** antes da
   correção); histórico "Devolvido". Evidência: captura da ficha depois da devolução.
3. **Comprador** → pedido C5 → "CORRIGIR P.O (Fornecedor X)" → modal "Correção de P.O" → bloco mostra "Reserva atual: 100 000,00
   AOA (Reservado)." com botões "Manter" (pré-seleccionado), "Alterar", "Remover" → manter "Manter" → "REGISTRAR P.O CORRIGIDA".
   Deve acontecer: grupo `PO_ISSUED`; histórico com "Reserva de crédito em conta corrente mantida: 100 000,00 AOA"; ficha = **P10**;
   Finanças volta a ver C5 com "Valor a pagar (banco): 150 000,00". Evidência: capturas.
4. **Finanças F1** devolve C5 de novo. **Comprador** corrige com "Alterar" → campo "Valor de crédito a utilizar (máx. …)" =
   `150000,00` → resumo banco 100 000,00 → "REGISTRAR P.O CORRIGIDA". Ficha = **P11**; Movimentos mostram a reserva anterior como
   "Reserva — Libertado" e uma nova "Reserva — Reservado" 150 000,00; histórico "Reserva de crédito em conta corrente alterada:
   100 000,00 → 150 000,00". Evidência: capturas.
5. **Finanças F1** devolve C5 de novo. **Comprador** corrige com "Remover" (aparece "A reserva é libertada; a P.O passa a ser paga
   integralmente por banco.") → "REGISTRAR P.O CORRIGIDA". Ficha = **P12** (Reservado 0,00, **sem** novo movimento no saldo);
   Finanças vê C5 **sem chip** de crédito e "Valor a pagar" 250 000,00; histórico "Crédito em conta corrente removido na correção
   da P.O". Evidência: capturas.
6. **[Verificação API] libertação directa** (a UI não tem botão; esta é a via suportada do Comprador/Local Manager): com C5 de
   novo devolvido (ou outro grupo em `WAITING_PO_CORRECTION`/`WAITING_PO` com reserva "Reservado"),
   `POST /api/v1/requests/{requestId}/po-groups/{groupId}/credit-reservation/release` com
   `{"reason":"Libertação de reserva em teste DEV, motivo com mais de vinte caracteres"}` → `200`, reserva "Libertado", ficha sem
   movimento no saldo. Em grupo `PO_ISSUED` o mesmo pedido → `409` "Libertação não permitida neste estado". Evidência: dois JSON.

#### 2.3.2 C6 — cancelamento da P.O liberta a reserva sem movimento

1. **Comprador** regista C6: "Pós-Pago", crédito integral `180000,00`. Ficha = **P13**.
2. **Comprador** → Gestão de Cotações → workspace do pedido C6 → cabeçalho ou separador "Grupos P.O." → "Cancelar P.O." → escolher
   o tipo de cancelamento, "Motivo (obrigatório)" → "Confirmar cancelamento da P.O.".
3. Deve acontecer: ficha = **P14** (Saldo 400 000,00 inalterado, Reservado 0,00); em Movimentos a linha da reserva passa a
   "Reserva — Libertado" e **não** existe nenhuma linha nova de saldo; histórico do pedido `SUPPLIER_CREDIT_RELEASED` com texto
   "…libertada (grupo P.O cancelado): 180 000,00 AOA voltam a estar disponíveis (sem movimento no saldo)"; Finanças → Histórico
   "Reserva de crédito libertada". Evidência: capturas da ficha antes/depois e do histórico.

### 2.4 Reversão de cobertura integral

#### 2.4.1 C2 — reversão por um segundo utilizador de Finanças (antes de qualquer evidência)

Pré-condição: C2 aplicado por **F1** em 2.2.2; nenhuma receção, fatura ou reconciliação registada em C2.

- **Finanças F2** → Finanças → Pagamentos → cartão "Pagos / Aguardando Recebimento" → linha C2 (chip "Crédito aplicado") → ⋮
  "Reverter crédito aplicado" → modal "Reverter Crédito em Conta Corrente" com "Crédito a reverter" 300 000,00 e "Valor a pagar
  após reversão" 300 000,00 → "Motivo da Reversão (obrigatório, mínimo 20 caracteres)" → "CONFIRMAR".
- Deve acontecer: toast "Crédito revertido; obrigação bancária reaberta."; grupo volta a `PO_ISSUED`; a **mesma** P.O reaparece em
  "Aguardando Agendamento" com chip "Crédito revertido — saldo em aberto", "Valor a pagar (banco): 300 000,00" e botões "Agendar
  pagamento"/"Pagar" (já sem "Aplicar crédito"); ficha = **P15** com movimento "Aplicação revertida" +300 000,00; Finanças →
  Histórico "Crédito revertido". Evidência: capturas da fila, do modal, da ficha e do histórico.
- Fecho opcional: **Finanças F1** → "Pagar" C2 → "Montante Efetivamente Pago" `299999,99` → recusa "Montante divergente da
  parcela bancária" (após a reversão o grupo continua ligado à reserva e a parcela bancária reaberta é exactamente o total,
  300 000,00); `300000,00` → sucesso, grupo `PAYMENT_COMPLETED`, ficha inalterada (P15). Evidência: capturas.

#### 2.4.2 Recusas da reversão (negativos)

- **Finanças F1** (quem aplicou) tenta reverter outro grupo integral aplicado por si (ex.: C4) → "Reversão pela mesma pessoa".
  **[Verificação API]** `POST /api/v1/finance/{requestId}/reverse-credit` com
  `{"requestPoGroupId":"{groupId}","reason":"Motivo de teste com mais de vinte caracteres"}` → `403`
  (`code: SELF_REVERSAL_FORBIDDEN`). Motivo com 10 caracteres → `400` "Motivo obrigatório". Token de um "System
  Administrator" sem perfil Finance → `403` "Perfil Finanças necessário". Evidência: JSON.

#### 2.4.3 Cobertura parcial — recusa esperada no Stage 1

- Na UI a linha de C1 (parcial, aplicado) **não** oferece "Reverter crédito aplicado".
- **[Verificação API]** com o token de **F2**: `POST /api/v1/finance/{requestIdC1}/reverse-credit` com
  `{"requestPoGroupId":"{groupIdC1}","reason":"Tentativa de reversão parcial em teste DEV"}` → `409` "Reversão parcial não
  suportada nesta fase" (`code: CREDIT_REVERSAL_PARTIAL_UNSUPPORTED`). Nada muda na ficha. Evidência: JSON + ficha.

### 2.5 Flag desligada com reserva em curso

1. Com a flag **ligada**, **Comprador** regista C7: "Pós-Pago", crédito integral `120000,00`. Ficha = **P16**.
2. Desligar a flag (`"NewReservationsEnabled": false` em `appsettings.Development.json`) e reiniciar (1.2). Confirmar pela API:
   `GET …/credit-eligibility` de C8 → `enabled: false`, `reasonCode: "SUPPLIER_CREDIT_DISABLED"`.
3. **Comprador** → pedido C8 → "REGISTRAR P.O" → "Pós-Pago": o bloco mostra o aviso "A utilização de crédito em conta corrente em
   novas P.O. não está ativa neste ambiente." e não há caixa. **[Verificação API]** `POST /api/v1/requests/{requestIdC8}/operational/register-po`
   com o corpo habitual do registo mais `"useSupplierCredit": true, "supplierCreditAmount": 100000` → `400` "Crédito em conta corrente
   desativado" (`code: SUPPLIER_CREDIT_DISABLED`) e **nenhuma** linha nova no histórico do pedido. Evidência: captura + JSON + histórico.
4. **Finanças F1** → linha C7 → "Aplicar crédito" → "CONFIRMAR": funciona com a flag desligada; ficha = **P17**. Evidência: captura.
   Alternativa equivalente: devolver C7 por Finanças e, na "Correção de P.O", confirmar que "Manter" e "Remover" funcionam e que
   "Alterar" aparece desactivado com a dica "A alteração do valor de crédito não está ativa neste ambiente."; ou libertar via
   `credit-reservation/release` (2.3.1-6) → `200`.
5. Voltar a ligar a flag se ainda houver testes a repetir; registar o estado final da flag no relatório.

---

## 3. Aceitação em DEV e passagem a TEST

### 3.1 Lista de verificação (Passa / Falha)

| # | Critério | Passa/Falha | Evidência |
|---|---|---|---|
| 1 | 2.0: com a flag desligada a ficha e a criação de conta funcionam e o bloco do wizard mostra o aviso de desactivação | | |
| 2 | 2.1: contas A e B criadas, "Conta já existe" na duplicação, créditos iniciais com movimentos e P0; validações de valor/referência/justificativa | | |
| 3 | 2.1-5/6: comprador vê só leitura no painel "Ficha do fornecedor"; Finanças `canManage:false` e `403` no POST | | |
| 4 | 2.2.1: C1 parcial — reserva P1, "Agendar pagamento" planeia 150 000, cancelar agendamento mantém a obrigação, 149 999,99 e 150 000,01 recusados, 150 000,00 aplica o crédito (P5), histórico com "Pago" e "Crédito aplicado" separados | | |
| 5 | 2.2.1-N: outro comprador sem acesso ao crédito | | |
| 6 | 2.2.2: C2 integral — só "Aplicar crédito"; `schedule`/`pay` → 409; aplicação → `PAYMENT_COMPLETED`, P6 | | |
| 7 | 2.2.3: C3 antecipado parcial de crédito — "Pagar adiantamento" exacto 150 000, `WAITING_SUPPLIER_DELIVERY`, P7 | | |
| 8 | 2.2.4: C4 antecipado integral — `schedule-advance`/`confirm-advance` → 409; "Aplicar crédito" → `WAITING_SUPPLIER_DELIVERY`, P8 | | |
| 9 | 2.2.5: Viana 1 e Viana 2 na mesma conta APA; conta APS separada em 200 000; Visão Geral exclui crédito | | |
| 10 | 2.3.1: devolução mantém a reserva; Manter/Alterar/Remover com P10–P12; libertação por API `200` e `409` em `PO_ISSUED` | | |
| 11 | 2.3.2: cancelamento liberta a reserva sem movimento no saldo (P14) | | |
| 12 | 2.4.1: F2 reverte C2; obrigação bancária de 300 000 reaberta na mesma P.O; P15 | | |
| 13 | 2.4.2: recusas (mesma pessoa, motivo curto, sem perfil Finance) | | |
| 14 | 2.4.3: reversão de cobertura parcial → 409 | | |
| 15 | 2.5: reserva feita antes de desligar; nova reserva recusada sem escrita; C7 liquidada/libertada com a flag desligada | | |
| 16 | Sem erros 500 na API nem excepções na consola do browser durante toda a sequência | | |
| 17 | Ficha, "Movimentos", histórico do pedido e Finanças → Histórico coerentes entre si em todos os pontos de controlo | | |

### 3.2 Critérios para passar de DEV a TEST

- Todos os 17 critérios "Passa", com as evidências guardadas (capturas + JSON das verificações API).
- Os saldos finais da conta A coincidem com P17 (580 000,00 / 0,00 / 580 000,00) e a conta B com 200 000,00 / 0,00 / 200 000,00.
- Nenhum valor de crédito aparece como dinheiro pago (Finanças → Histórico "Pagos", Visão Geral).
- Qualquer desvio é registado como defeito e corrigido em DEV **antes** de pedir o deploy para TEST; depois de uma correção,
  repetir pelo menos o caso afectado e o caso 2.2.1.

### 3.3 Pré-requisitos de TEST (cada um com autorização própria; nada disto é feito por este guia)

1. Commit e push do Stage 1 via `/task-publish` (só os ficheiros do crédito; as alterações concorrentes de HR/Viana 3/unlock DEV
   ficam de fora), com a flag commitada a `false`.
2. Workflow **Apply TEST Migrations** com a migração `20261109150614_AddSupplierCreditAccounts` (aditiva; nunca `Down`).
3. Workflow **Deploy to TEST** do build resultante; confirmar `buildId` na página de diagnóstico.
4. Smoke em TEST com a flag desligada (2.0); depois `SupplierCredit:NewReservationsEnabled=true` no `appsettings.Test.json`
   preservado no servidor e reinício da API TEST; confirmar `enabled:true` na API de elegibilidade.
5. Contas em TEST: um utilizador `Contracts`, um `Buyer` responsável, **dois** `Finance` activos com âmbito da planta, um segundo
   `Buyer` para o negativo; fornecedor X activo em TEST (não interno) e pedidos QUOTATION C1–C8 preparados em TEST.
6. Antes de iniciar: ficha do fornecedor X em TEST sem contas (0 conta(s)).

### 3.4 Cenários a repetir em TEST após a aceitação em DEV

Repetir integralmente: 2.0 (smoke com flag desligada), 2.1 (contas A e B, movimentos, só leitura), 2.2.1 a 2.2.4 (os quatro
casos, incluindo as recusas de montante e os 409 por API), 2.3.1 (um ciclo Manter + um Remover é suficiente; Alterar opcional),
2.3.2 (cancelamento), 2.4.1 e 2.4.3 (reversão integral por F2 e recusa da parcial), 2.5 (flag desligada com reserva em curso,
com restart da API TEST). Em TEST o backend corre em SQL Server real, por isso as verificações de concorrência (bloqueios
`UPDLOCK`) passam a ser exercidas; se for possível, repetir 2.2.2 "Aplicar crédito" em duas janelas quase simultâneas e confirmar
que uma delas recebe "Crédito já aplicado" ou "Dados desatualizados".

Fica fora do Stage 1 em qualquer ambiente: Antecipado Parcial com crédito (o bloco não aparece com essa condição), pedidos
PAYMENT (`400` "Crédito não suportado"), reversão de cobertura parcial (`409`), e qualquer passo em PROD.
