# Matriz de Levantamento Técnico — Integrações Portal Gerencial

## 1. Visão Geral dos Sistemas

| Sistema | Finalidade Principal | Papel no Processo | Ambiente | Observações |
|---|---|---|---|---|
| Primavera ERP | Cadastro corporativo, RH, estrutura funcional, cálculos de horas/faltas | Fonte mestre cadastral | Produção | Foto do funcionário é gravada no Primavera |
| Innux Time Attendance | Biometria, terminais, marcações de entrada/saída | Fonte operacional de assiduidade | Produção | Focado em biometria e ponto |
| Portal Gerencial | Consumo e orquestração de dados para processos internos | Consumidor / camada de integração | Produção | Não deve consultar múltiplas fontes diretamente no frontend |

## 2. Panorama de Integração Atual

| Item | Primavera | Innux | Observações |
|---|---|---|---|
| Existe integração entre os sistemas? | Sim | Sim | Integração bidirecional confirmada |
| Sincronização automática? | Não | Não | Depende de ação manual do usuário |
| Atualização por botão do integrador? | Sim | Sim | O usuário aciona a checagem/sincronização |
| Foto do funcionário | Sim | Secundário / validar | Administrador informou que a foto é gravada no Primavera |
| Biometria | Não principal | Sim | Innux é a fonte operacional biométrica |
| Entradas e saídas | Consome para cálculo | Registra marcações | Primavera usa dados do Innux para cálculo de horas/faltas |

## 3. Infraestrutura Técnica

### 3.1 Primavera

| Campo | Valor | Status | Observações |
|---|---|---|---|
| Servidor SQL | `AOVIA1VMS012\SQLALPLA` | Confirmado |  |
| Bases produtivas relevantes | `PRI297514001`, `PRI297514003` | Confirmado | Bases mestres produtivas |
| Base mestre AlplaPLASTICO | `PRI297514001` | Confirmado | Produção |
| Base mestre AlplaSOPRO | `PRI297514003` | Confirmado | Produção |
| Base técnica de perfis/licenças | `PRIEMPRE` | Confirmado | Não é base mestre de cadastro |
| Ambientes de teste | `PRITESTES3`, `PRIFORMASSETS`, `PRILISTAS` | Confirmado | Não usar como fonte principal |
| Base DTI | `PRIDTI` | Confirmado | Voltada a guia de equipamentos TI |
| Web API instalada | Sim | Confirmado | Sem licença para uso |
| Web API utilizável no projeto? | Não por enquanto | Confirmado | Não considerar como caminho principal |

### 3.2 Innux

| Campo | Valor | Status | Observações |
|---|---|---|---|
| Servidor SQL | `AOVIA1VMS012\SQLINNUX` | Confirmado |  |
| Base produtiva relevante | `Innux` | Confirmado | Base principal identificada |
| Plugin Primavera instalado | Sim | Confirmado | Integração formal existente |
| Serviços relevantes | `AutoRec`, `AutoSync` | Confirmado | Ligados a recolha/sincronização |
| Base orientada a biometria/ponto | Sim | Confirmado | Foco operacional |
| API identificada no Innux | Não | Não identificada | Sem evidência útil de endpoint de integração |

## 4. Objetivo Inicial da Integração

| Domínio | Objetivo | Prioridade | Observações |
|---|---|---|---|
| Funcionários | Consulta de dados para crachá e perfil interno | Alta | Primeiro caso de uso |
| Biometria / Cartão | Complementar o cadastro do funcionário | Alta | Fonte operacional no Innux |
| Assiduidade | Futuro próximo | Média | Depende da regra entre bruto x calculado |
| Cadastro de materiais | Futuro próximo | Alta | Próxima frente após funcionários |

## 5. Tabelas e Views Relevantes — Primavera

### 5.1 Base `PRI297514001` / `PRI297514003`

| Objeto | Tipo | Finalidade provável | Prioridade | Observações |
|---|---|---|---|---|
| `dbo.Funcionarios` | Tabela | Cadastro principal de funcionário | Muito alta | Principal candidata |
| `dbo.Departamentos` | Tabela | Estrutura de departamentos | Muito alta | Usar para descrição do departamento |
| `dbo.MovimentosFuncionarios` | Tabela | Histórico/movimentos do funcionário | Alta | Pode ajudar em status e histórico |
| `dbo.V_MovimentosFuncionariosAtivos` | View | Funcionários ativos | Muito alta | Forte candidata para listas de ativos |
| `dbo.V_FuncionariosSIOE` | View | Visão derivada de funcionários | Média | Validar utilidade real |
| `dbo.CadastroPessoal` | Tabela | Histórico/eventos pessoais | Média | Não parece a principal |
| `dbo.CadastroEntidades` | Tabela | Entidades/cadastro auxiliar | Média | Validar necessidade |

### 5.2 Campos relevantes encontrados em `dbo.Funcionarios`

| Campo | Significado provável | Uso potencial no Portal | Fonte recomendada |
|---|---|---|---|
| `Codigo` | Código do funcionário | Chave principal de integração | Primavera |
| `Nome` | Nome completo | Perfil / crachá | Primavera |
| `NomeAbreviado` | Nome curto | Crachá / listagem | Primavera |
| `PrimeiroNome` | Primeiro nome | Exibição | Primavera |
| `PrimeiroApelido` | Apelido | Exibição | Primavera |
| `SegundoApelido` | Apelido adicional | Exibição | Primavera |
| `Email` | E-mail | Diretório / contato | Primavera |
| `Foto` | Foto do funcionário | Crachá / perfil | Primavera |
| `Telefone` | Telefone | Contato | Primavera |
| `Telemovel` | Celular | Contato | Primavera |
| `Morada` | Endereço | Uso eventual | Primavera |
| `DataAdmissao` | Data de admissão | RH / perfil | Primavera |
| `DataReadmissao` | Data de readmissão | RH / histórico | Primavera |
| `DataDemissao` | Data de desligamento | RH / status | Primavera |
| `CodDepartamento` | Código do departamento | Join com departamentos | Primavera |
| `InactivoTemp` | Inatividade temporária | Regra de status | Primavera |
| `Utilizador` | Utilizador/login | Integração / AD | Primavera |
| `CartaoResidente` | Cartão/identificador | Validar com Innux | Primavera |

## 6. Tabelas e Views Relevantes — Innux

### 6.1 Base `Innux`

| Objeto | Tipo | Finalidade provável | Prioridade | Observações |
|---|---|---|---|---|
| `dbo.Funcionarios` | Tabela | Cadastro operacional do funcionário | Muito alta | Principal no Innux |
| `dbo.Departamentos` | Tabela | Estrutura de departamento | Alta | Apoio |
| `dbo.AtribuirTerminaisFuncionarios` | Tabela | Relação funcionário-terminal | Alta | Biometria / terminais |
| `dbo.FuncionariosAnexos` | Tabela | Anexos / ficheiros do funcionário | Média | Validar utilidade |
| `dbo.FuncionariosContratos` | Tabela | Contratos | Média | Validar utilidade |
| `dbo.Ausencias` | Tabela | Ausências | Média | Mais ligada a assiduidade |
| `dbo.CodigosAusencia` | Tabela | Tipos de ausência | Média | Mais ligada a assiduidade |

### 6.2 Campos relevantes encontrados em `dbo.Funcionarios`

| Campo | Significado provável | Uso potencial no Portal | Fonte recomendada |
|---|---|---|---|
| `IDFuncionario` | ID interno | Chave técnica | Innux |
| `Numero` | Número do funcionário | Match com Primavera | Innux |
| `Nome` | Nome | Validação/complemento | Secundária |
| `Cartao` | Cartão | Crachá / biometria | Innux |
| `Telefone` | Telefone | Contato | Secundária |
| `Telemovel` | Celular | Contato | Secundária |
| `Email` | E-mail | Contato | Secundária |
| `Fotografia` | Foto | Validar se mais atual | Secundária |
| `Activo` | Ativo operacional | Status no ponto | Innux |
| `DataAdmissao` | Data de admissão | Validação | Secundária |
| `IDDepartamento` | Departamento | Join local | Secundária |
| `CentroCusto` | Centro de custo | Apoio | Innux/validar |
| `DataNascimento` | Data de nascimento | Uso eventual | Validar |
| `NomeAbreviado` | Nome curto | Exibição | Secundária |
| `DataDemissao` | Data de desligamento | Validação | Secundária |
| `Categoria` | Categoria | Apoio | Validar |
| `NIF` | Identificação fiscal | Validação | Validar |
| `LoginAD` | Login AD | Integração | Validar |

## 7. Fonte Mestre por Campo — Proposta Inicial

| Campo | Fonte Principal | Fonte Secundária | Regra Inicial |
|---|---|---|---|
| Código do funcionário | Primavera | Innux (`Numero`) | Validar correspondência |
| Nome | Primavera | Innux | Primavera vence |
| Nome abreviado | Primavera | Innux | Primavera vence |
| Foto | Primavera | Innux | Primavera como padrão inicial |
| E-mail | Primavera | Innux | Primavera vence |
| Telefone | Primavera | Innux | Primavera vence |
| Telemóvel | Primavera | Innux | Primavera vence |
| Morada | Primavera | — | Primavera vence |
| Data de admissão | Primavera | Innux | Primavera vence |
| Data de demissão | Primavera | Innux | Primavera vence |
| Departamento | Primavera | Innux | Primavera vence |
| Status funcional RH | Primavera | — | Base oficial |
| Status operacional no ponto | Innux | — | Fonte operacional |
| Cartão | Innux | Primavera (`CartaoResidente`) | Validar consistência |
| Número biométrico/ponto | Innux | Primavera | Innux vence |
| Biometria/terminal | Innux | — | Innux vence |
| Entradas e saídas brutas | Innux | — | Innux vence |
| Horas/faltas calculadas | Primavera | — | Primavera vence |

## 8. Chaves de Integração a Validar

| Origem | Campo | Destino | Campo | Status | Observações |
|---|---|---|---|---|---|
| Primavera | `Funcionarios.Codigo` | Innux | `Funcionarios.Numero` | Hipótese forte | Validar com sample comparativo |
| Primavera | `Funcionarios.Nome` | Innux | `Funcionarios.Nome` | Secundária | Não ideal como chave |
| Primavera | `Funcionarios.Email` | Innux | `Funcionarios.Email` | Secundária | Pode ajudar em conferência |
| Primavera | `Funcionarios.CodDepartamento` | Innux | `Funcionarios.IDDepartamento` | Não direta | Exige mapeamento |
| Primavera | `Funcionarios.CartaoResidente` | Innux | `Funcionarios.Cartao` | Possível | Validar |

## 9. Regras Operacionais já Confirmadas

| Regra | Status | Observações |
|---|---|---|
| Sincronização Primavera → Innux é manual | Confirmado | Depende de ação do usuário |
| Sincronização Innux → Primavera é manual | Confirmado | Depende de ação do usuário |
| O Innux registra biometria | Confirmado | Papel principal do sistema |
| O Primavera guarda foto | Confirmado | Deve ser a fonte padrão de foto |
| O Primavera usa entradas/saídas do Innux | Confirmado | Para cálculo de horas e faltas |
| Os bancos podem ficar temporariamente divergentes | Confirmado | Importante para regras do Portal |

## 10. Diretriz Inicial para o Portal Gerencial

| Tema | Diretriz |
|---|---|
| Consulta de funcionário | Portal deve consultar um serviço unificado, não os dois bancos diretamente |
| Cadastro do funcionário | Usar Primavera como principal |
| Complemento biométrico | Usar Innux |
| Assiduidade bruta | Usar Innux |
| Resultado calculado de RH | Usar Primavera |
| Tratamento de divergência | O serviço deve sinalizar inconsistências entre cadastro e biometria |
| Tela de saúde das integrações | Pode evoluir para mostrar última sincronização, origem e avisos |

## 11. Campos de Diagnóstico Futuro

| Campo técnico | Objetivo |
|---|---|
| `IsActiveHr` | Indicar se o colaborador está ativo no cadastro RH |
| `IsActiveAttendance` | Indicar se está ativo no sistema de ponto |
| `HasBiometricEnrollment` | Indicar se possui biometria/cadastro operacional |
| `HasPhoto` | Indicar se possui foto válida |
| `SyncWarning` | Alertar divergência entre Primavera e Innux |
| `PrimaveraLastSeen` | Última leitura bem-sucedida no Primavera |
| `InnuxLastSeen` | Última leitura bem-sucedida no Innux |

## 12. Próximo Escopo — Cadastro de Materiais

| Tema | Ação futura |
|---|---|
| Bases alvo | `PRI297514001` e `PRI297514003` |
| Objetos a procurar | `Artigos`, `Materiais`, `Produtos`, `Stocks`, `Armazens`, `Familias`, `Subfamilias`, `Unidades`, `Precos` |
| Objetivo | Definir fonte mestre de material para integração com Portal |
| Observação | Manter mesma metodologia usada para funcionários |

## 13. Decisões Provisórias

| Decisão | Status |
|---|---|
| Primavera será a fonte mestre cadastral de funcionários | Proposta forte |
| Innux será a fonte operacional de biometria e ponto | Proposta forte |
| Portal não deve consultar os dois bancos diretamente no frontend | Recomendado |
| Deve existir um serviço intermediário de integração | Recomendado |
| Próxima análise no Portal deve levantar o que já existe na tela de integrações | Pendente |
