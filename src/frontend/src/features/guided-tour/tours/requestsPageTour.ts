import type { TourStep } from '../guidedTourTypes';

/**
 * Requests Page Tour (page-requests)
 * 
 * Walks through the full Requests (Pedidos) page in the recommended order:
 * 1. Action Carousel — KPI stats & action queue cards
 * 2. Kebab menu — contextual actions on cards
 * 3. Floating total — sum based on applied filters (current page)
 * 4. Floating toggle — enable/disable floating summary
 * 5. Quick filter tabs — Todos / Meus Pedidos / Minha Área
 * 6. Search & advanced filters
 * 7. Requests table — sortable columns and status hover info
 * 8. Timeline expand/collapse — chevron button on the left of each row
 * 
 * Steps whose target is not in the DOM (due to loading, RBAC, or empty data)
 * are filtered out automatically by filterActiveSteps.
 */
export const REQUESTS_PAGE_STEPS: TourStep[] = [
    // 1. Action Carousel & KPIs
    {
        target: '[data-tour="requests-action-carousel"]',
        title: 'Fila de Ação & Indicadores',
        content: 'Os cards no topo mostram as filas prioritárias de trabalho: aprovações, cotações, correções e pagamentos. Abaixo, a fila de ação lista os pedidos atribuídos a si ou que requerem atenção imediata.',
        placement: 'bottom',
        skipBeacon: true,
    },
    // 2. Kebab menu on action cards (only present if there are cards)
    {
        target: '[data-tour="requests-card-kebab-menu"]',
        title: 'Menu de Ações',
        content: 'O menu de três pontos reúne ações rápidas relacionadas ao pedido, como abrir detalhes, continuar o fluxo ou executar ações permitidas para o seu perfil.',
        placement: 'left',
    },
    // 3. Floating total
    {
        target: '[data-tour="requests-floating-total"]',
        title: 'Total Filtrado',
        content: 'Este indicador mostra a soma dos valores dos pedidos exibidos na página atual. Ao mudar os filtros da lista, o total reflete apenas os pedidos visíveis na página corrente.',
        placement: 'top',
    },
    // 4. Floating toggle
    {
        target: '[data-tour="requests-floating-toggle"]',
        title: 'Ativar ou Ocultar Resumo',
        content: 'Use este botão para alternar entre o modo flutuante (resumo fixo no canto inferior) e o modo inline (resumo integrado abaixo da tabela). Isso ajuda a manter a tela mais limpa quando não precisar do resumo visível.',
        placement: 'bottom',
    },
    // 5. Quick filter tabs
    {
        target: '[data-tour="requests-filter-tabs"]',
        title: 'Filtros Rápidos',
        content: 'Use as abas "Todos", "Meus Pedidos" e "Minha Área" para filtrar rapidamente o conjunto de pedidos visíveis no explorador.',
        placement: 'bottom',
    },
    // 6. Search & advanced filters
    {
        target: '[data-tour="requests-filter-button"]',
        title: 'Pesquisa & Filtros Avançados',
        content: 'Use a barra de pesquisa para buscar por número, título ou outros campos. O botão "Filtro" abre opções avançadas como empresa, planta, departamento e status.',
        placement: 'bottom',
    },
    // 7. Table columns and hover info
    {
        target: '[data-tour="requests-table"]',
        title: 'Tabela de Pedidos',
        content: 'A tabela mostra os pedidos conforme os filtros aplicados. Clique nos cabeçalhos para ordenar. Passe o mouse sobre o status para ver detalhes da situação atual e a próxima ação esperada.',
        placement: 'top',
    },
    // 8. Timeline expand/collapse button
    {
        target: '[data-tour="request-timeline-toggle"]',
        title: 'Ver Timeline do Pedido',
        content: 'Clique no botão à esquerda da linha para abrir ou ocultar a timeline do pedido. A timeline permite acompanhar em que etapa o pedido está e visualizar o histórico do processo — desde rascunho até conclusão.',
        placement: 'right',
    },
];
