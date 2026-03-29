# Execução

Este diretório contém os **scripts Python determinísticos e executáveis** que realizam o trabalho pesado do projeto. Eles são as ferramentas chamadas para cumprir as diretivas.

## `execution/` vs `src/`

- **`execution/`**: Contém os scripts que atuam como *pontos de entrada* (entrypoints). São focados em processos e chamadas diretas de execução (ex.: `scrape_single_site.py`, `run_report.py`).
- **`src/`**: Contém módulos, bibliotecas, classes e utilitários reutilizáveis. Os scripts em `execution/` podem e devem importar lógicas complexas de `src/`, mantendo os entrypoints limpos.

## Boas Práticas

### Uso de `--dry-run`

Sempre que possível, implemente o modo `--dry-run` ou de visualização em seus scripts para permitir validação antes de realizar ações destrutivas ou de gravação (escritas no banco, envio de e-mails, deleção de arquivos).

### Logs e Arquivos Temporários

Toda execução deve registrar suas atividades de forma útil (sem ser ruidosa).

- Salve os logs no diretório: `.tmp/logs/`
- Arquivos intermediários (exports rápidos, caches) devem ficar em `.tmp/`. Lembre-se que o conteúdo de `.tmp/` não deve ser 'commitado' e deve ser seguro para ser deletado e gerado novamente.
