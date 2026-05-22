import type { TourStep } from '../guidedTourTypes';

/**
 * Approval Drawer — Final Approval Tour
 *
 * 8-step tour for the Quick Overview Drawer when opened from
 * the Final Approval queue. Focuses on decision validation:
 * financial impact, risks, documents, supplier choice, workflow history.
 *
 * All targets use stable `data-tour` attributes.
 * Missing targets are skipped gracefully by `filterActiveSteps()`.
 */
export const APPROVAL_DRAWER_FINAL_STEPS: TourStep[] = [
    {
        target: '[data-tour="approval-drawer-header"]',
        title: 'Resumo para Aprovação Final',
        content: 'Aqui você revisa o pedido em nível final, confirmando status, tipo de pedido e valor antes da decisão.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-alerts"]',
        title: 'Pendências e Riscos',
        content: 'Antes da aprovação final, verifique alertas que possam indicar pendências, inconsistências ou riscos no pedido.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-request-info"]',
        title: 'Dados Principais',
        content: 'Revise solicitante, departamento, empresa, planta, data necessária, fornecedor e grau de necessidade para confirmar o contexto do pedido.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-financial-context"]',
        title: 'Impacto Financeiro',
        content: 'Use o contexto financeiro para avaliar o impacto do pedido no orçamento e apoiar a decisão final.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-quotations"]',
        title: 'Cotações e Escolha do Fornecedor',
        content: 'Confirme se as cotações foram analisadas corretamente e se a cotação vencedora faz sentido para o pedido.',
        placement: 'top',
    },
    {
        target: '[data-tour="approval-drawer-documents"]',
        title: 'Documentos de Suporte',
        content: 'Verifique os documentos associados ao pedido, como cotações, proformas, anexos ou outros comprovativos necessários.',
        placement: 'top',
    },
    {
        target: '[data-tour="approval-drawer-workflow"]',
        title: 'Histórico do Fluxo',
        content: 'Consulte o histórico do pedido para entender as etapas anteriores, responsáveis, datas e decisões já tomadas.',
        placement: 'top',
    },
    {
        target: '[data-tour="approval-drawer-actions"]',
        title: 'Decisão Final',
        content: 'Use estas ações para aprovar, rejeitar ou devolver o pedido para reajuste. Esta decisão define se o pedido pode seguir para a próxima etapa do processo.',
        placement: 'top',
    },
];
