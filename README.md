# Alpla Angola Gerencial

## Visão Geral

Este repositório contém a estrutura principal do projeto **Alpla Angola Gerencial**. O foco principal é fornecer um ambiente automatizado e organizado usando a arquitetura de agentes **DOE** (Directives, Orchestration, Execution), separando intenção, decisão e execução determinística.

## Estrutura de Pastas

### Estrutura DOE (Core)

- `directives/` – Instruções e manuais em formato Markdown (SOPs, o que o agente deve fazer).
- `execution/` – Scripts Python executáveis e determinísticos focados em processos (como fazer).
- `.tmp/` – Arquivos temporários, de log (`.tmp/logs/`) e artefatos de processo gerados em tempo de execução (ignorados pelo git).

### Pastas de Suporte

- `docs/` – Documentação geral do projeto. Recomendamos começar lendo `docs/PROJECT_OVERVIEW.md`.
- `src/` – Módulos e código-fonte com lógicas reutilizáveis.
- `tests/` – Testes automatizados.
- `data/` – Dados locais persistentes de referência ou exemplos (fixtures).
- `configs/` – Arquivos de configuração e templates. (Use `.env` para dados sensíveis).

## Como Começar (Setup Básico)

1. **Clone o repositório** para sua máquina local.
2. **Configure o Ambiente**:
   - Copie o arquivo `.env.example` para criar o seu `.env`.
   - Preencha as variáveis de ambiente necessárias (nunca faça commit do `.env`).
3. **Instale as dependências** pertinentes para os scripts Python em `execution/` e `src/`.
4. **Leia os Documentos**:
   - Veja `docs/PROJECT_OVERVIEW.md` para entender o escopo do projeto.
   - Leia `directives/README.md` e `execution/README.md` para as regras de desenvolvimento do modelo DOE.
