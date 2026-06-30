import type { TourStep } from '../guidedTourTypes';

export const IT_EQUIPMENT_STEPS: TourStep[] = [
    {
        target: 'body',
        placement: 'center',
        content: 'Bem-vindo ao módulo de Gestão de Equipamentos de IT. Aqui pode gerir o inventário, ciclo de vida e atribuições de equipamentos na empresa.',
        skipBeacon: true,
    },
    {
        target: '[data-tour="it-module-tabs"]',
        content: 'Navegue entre o Estoque de Equipamentos, Termos de Entrega, e as páginas de configurações de Catálogos e Tipos de Equipamento usando estes separadores.',
    },
    {
        target: '[data-tour="it-summary-cards"]',
        content: 'Estes cartões fornecem um resumo rápido do estado do inventário: equipamentos totais, disponíveis, em uso e o valor contabilístico atual.',
    },
    {
        target: '[data-tour="it-action-buttons"]',
        content: 'Aqui encontra as ações principais: exportar/importar CSV e adicionar equipamentos manualmente.',
    },
    {
        target: '[data-tour="it-equipment-table"]',
        content: 'A tabela principal do inventário. Clique na lupa numa linha para ver detalhes e na caneta para editar dados. O estado e localização de cada equipamento também estão visíveis.',
    }
];
