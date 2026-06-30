import type { TourStep } from '../guidedTourTypes';

export const IT_CATALOGS_PAGE_STEPS: TourStep[] = [
    {
        target: 'body',
        placement: 'center',
        content: 'Esta página permite gerir os dados mestre que alimentam o módulo de IT. As alterações aqui refletem-se nos formulários de equipamentos.',
        skipBeacon: true,
    },
    {
        target: '[data-tour="it-catalog-tabs"]',
        content: 'Navegue entre os diferentes catálogos: Fabricantes, Modelos, Processadores e opções de Memória RAM.',
    },
    {
        target: '[data-tour="it-catalog-actions"]',
        content: 'Utilize a barra de pesquisa e filtros para encontrar registos, ou clique no botão Novo para adicionar uma nova entrada ao catálogo atual.',
    },
    {
        target: '[data-tour="it-catalog-table"]',
        content: 'Aqui estão listados os registos. Utilize o menu de opções na direita (três pontos) para editar ou ativar/desativar um registo. Registos desativados não aparecerão para novas seleções.',
    }
];
