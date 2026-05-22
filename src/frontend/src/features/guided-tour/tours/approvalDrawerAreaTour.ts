import type { TourStep } from '../guidedTourTypes';

/**
 * Approval Drawer — Area Approval Tour
 *
 * 8-step tour for the Quick Overview Drawer when opened from
 * the Area Approval queue. Focuses on operational validation:
 * allocation, cost center, plant, quotation, items, alerts.
 *
 * All targets use stable `data-tour` attributes.
 * Missing targets are skipped gracefully by `filterActiveSteps()`.
 */
export const APPROVAL_DRAWER_AREA_STEPS: TourStep[] = [
    {
        target: '[data-tour="approval-drawer-header"]',
        title: 'Resumo do Pedido',
        content: 'Aqui você confirma o número do pedido, o estado atual, o tipo de pedido e o valor total antes de tomar uma decisão.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-alerts"]',
        title: 'Alertas de Validação',
        content: 'Estes alertas indicam pendências que podem impedir ou orientar a aprovação, como cotação vencedora obrigatória, centro de custo, planta ou documentos em falta.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-request-info"]',
        title: 'Informações do Pedido',
        content: 'Consulte os dados principais do pedido, como solicitante, departamento, empresa, planta, data necessária, fornecedor e grau de necessidade.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-financial-allocation"]',
        title: 'Atribuição Financeira',
        content: 'Verifique se os itens possuem centro de custo, planta e alocação financeira correta antes de aprovar.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-financial-context"]',
        title: 'Contexto Financeiro',
        content: 'Esta área ajuda a entender o impacto do pedido no orçamento, comparando valores por período, planta ou departamento conforme os filtros disponíveis.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-quotations"]',
        title: 'Cotações e Documentos',
        content: 'Revise as cotações registradas, documentos anexados e a cotação vencedora selecionada para apoiar sua decisão.',
        placement: 'top',
    },
    {
        target: '[data-tour="approval-drawer-items"]',
        title: 'Itens do Pedido',
        content: 'Confira os itens solicitados, quantidades, valores, centro de custo e dados necessários para validar a necessidade do pedido.',
        placement: 'top',
    },
    {
        target: '[data-tour="approval-drawer-actions"]',
        title: 'Ações de Aprovação',
        content: 'Use estas ações para solicitar reajuste, rejeitar ou aprovar o pedido. A aprovação só deve ocorrer quando as informações estiverem corretas.',
        placement: 'top',
    },
];
