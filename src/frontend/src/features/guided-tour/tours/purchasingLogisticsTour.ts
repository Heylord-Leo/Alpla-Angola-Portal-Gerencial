import type { TourStep } from '../guidedTourTypes';

/**
 * Compras & Logística — Module Tour
 * 
 * Explains the module's cockpit structure and sub-modules:
 * 1. Module menu entry (sidebar)
 * 2. Cockpit overview (page header)
 * 3. Pedidos menu shortcut
 * 4. KPI Cards (indicators)
 * 5. Pontos de Atenção (attention items)
 * 6. Ações Rápidas (quick actions)
 * 7. Manual de Operação (operational guidance)
 * 8. Gestão de Cotações (buyer items menu)
 * 9. Recebimento (receiving menu)
 * 
 * Steps whose target is not in the DOM (e.g. RBAC-hidden menus)
 * are filtered out automatically by filterActiveSteps.
 */
export const PURCHASING_LOGISTICS_STEPS: TourStep[] = [
    {
        target: '[data-tour="purchasing-logistics"]',
        title: 'Compras & Logística',
        content: 'Este módulo reúne as principais atividades relacionadas a pedidos, cotações, compras, logística e recebimentos.',
        placement: 'right',
        skipBeacon: true,
    },
    {
        target: '[data-tour="purchasing-overview"]',
        title: 'Cockpit do Módulo',
        content: 'Esta é a visão geral do módulo. Aqui você acompanha indicadores, pendências e atalhos operacionais do processo de compras.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="purchase-requests-menu"]',
        title: 'Pedidos',
        content: 'Use esta opção para criar, acompanhar e gerir pedidos de compra, pagamento ou cotação. Aqui você acompanha o estado do pedido e continua o fluxo conforme sua responsabilidade.',
        placement: 'right',
    },
    {
        target: '[data-tour="purchasing-kpi-cards"]',
        title: 'Cards Principais',
        content: 'Indicadores resumidos do módulo: total de pedidos abertos, em cotação, aguardando aprovação ou pagamento, e recebimentos pendentes.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="purchasing-attention-points"]',
        title: 'Pontos de Atenção',
        content: 'Esta área destaca itens que exigem atenção imediata: pedidos urgentes, aprovações pendentes, pagamentos em atraso ou gargalos operacionais.',
        placement: 'top',
    },
    {
        target: '[data-tour="purchasing-quick-actions"]',
        title: 'Ações Rápidas',
        content: 'Atalhos para as operações mais frequentes: criar novo pedido, acessar cotações, recebimentos ou pedidos ativos.',
        placement: 'left',
    },
    {
        target: '[data-tour="purchasing-operation-manual"]',
        title: 'Manual de Operação',
        content: 'Orientação operacional sobre como utilizar o módulo: regras de priorização, processos internos e instruções de uso do Cockpit.',
        placement: 'left',
    },
    {
        target: '[data-tour="buyer-items-menu"]',
        title: 'Gestão de Cotações',
        content: 'Nesta área, o comprador acompanha itens aguardando cotação, regista fornecedores, compara propostas e conduz o processo de compra.',
        placement: 'right',
    },
    {
        target: '[data-tour="receiving-menu"]',
        title: 'Recebimento',
        content: 'Aqui são acompanhados os itens pendentes de recebimento, recebimentos parciais e documentos de entrada de materiais ou serviços.',
        placement: 'right',
    },
];
