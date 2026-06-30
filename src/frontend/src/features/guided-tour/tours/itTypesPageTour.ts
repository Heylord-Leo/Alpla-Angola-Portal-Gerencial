import type { TourStep } from '../guidedTourTypes';

export const IT_TYPES_PAGE_STEPS: TourStep[] = [
    {
        target: 'body',
        placement: 'center',
        content: 'Esta página permite gerir os Tipos de Equipamento de IT (como Laptops, Monitores, etc). O tipo de equipamento define quais os campos técnicos que estarão disponíveis no formulário do equipamento.',
        skipBeacon: true,
    },
    {
        target: '[data-tour="it-type-actions"]',
        content: 'Utilize a barra de pesquisa para filtrar os tipos existentes ou adicione um Novo Tipo através deste botão.',
    },
    {
        target: '[data-tour="it-type-table"]',
        content: 'Aqui encontra todos os Tipos de Equipamento. Pode editá-los para alterar o nome, código curto para etiquetas, e os campos dinâmicos aplicáveis a cada tipo.',
    }
];
