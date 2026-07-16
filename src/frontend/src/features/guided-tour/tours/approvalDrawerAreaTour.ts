import type { TourStep } from '../guidedTourTypes';

/**
 * Approval Drawer — Area Approval Tour
 *
 * Reflects the batch/partial-approval model: the area drawer is INFORMATIVE
 * only — every operational action (financial allocation, batch item review,
 * budget check, approve/reject/adjust) happens inside the Approval Wizard,
 * opened via the "Revisar Pedido" button.
 *
 * All targets use stable `data-tour` attributes.
 * Missing targets are skipped gracefully by `filterActiveSteps()` — e.g. the
 * partial-batch banner step only shows when the request has an active batch.
 */
export const APPROVAL_DRAWER_AREA_STEPS: TourStep[] = [
    {
        target: '[data-tour="approval-drawer-header"]',
        title: 'Resumo do Pedido',
        content: 'Aqui você vê o número do pedido, o estado atual, o tipo e o valor total. Esta tela é informativa — a aprovação em si será feita no Wizard, aberto pelo botão "Revisar Pedido".',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-batch-banner"]',
        title: 'Lote de Aprovação Parcial',
        content: 'Este pedido pode ainda estar em cotação, mas este lote parcial já está aguardando a sua aprovação. O status geral do pedido e o status do lote podem ser diferentes — isso é esperado. O Wizard revisará apenas os itens deste lote; itens pendentes fora do lote continuam com o comprador e não bloqueiam esta aprovação.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-alerts"]',
        title: 'Alerta de Preços',
        content: 'Quando algum item está com preço acima da média histórica, um alerta informativo aparece aqui. Ele não bloqueia a aprovação — use-o como contexto para questionar valores dentro do Wizard.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-request-info"]',
        title: 'Informações do Pedido',
        content: 'Consulte os dados principais do pedido, como solicitante, departamento, empresa, planta, data necessária, fornecedor e grau de necessidade.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-financial-context"]',
        title: 'Contexto Financeiro',
        content: 'Use o contexto financeiro para entender o histórico de pagamentos deste departamento/fornecedor e apoiar a sua decisão.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-financial-allocation"]',
        title: 'Inteligência para Decisão',
        content: 'Estatísticas de preço histórico por item, cruzadas com o ERP. Itens marcados a vermelho estão acima do preço médio pago — leve essa informação para a revisão no Wizard.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="approval-drawer-actions"]',
        title: 'Revisar Pedido',
        content: 'Clique em "Revisar Pedido" para abrir o Wizard de Aprovação. É nele que você fará a atribuição financeira (planta e centro de custo), a revisão dos itens do lote, a análise orçamental e a decisão final: aprovar, rejeitar ou solicitar reajuste.',
        placement: 'top',
    },
];
