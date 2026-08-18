import type { TourStep } from '../guidedTourTypes';

/**
 * Approvals Center Page Tour (page-approvals-center)
 *
 * Explains the operational approval workflow for end users.
 * Steps follow the visual layout:
 * 1. Page header — workspace context
 * 2. KPI cards — pending counts, total value, urgency, alerts
 * 3. Filter tabs — triage controls
 * 4. Area approval queue — area-level decisions
 * 5. Final approval queue — final-level decisions
 * 6. Request card — individual pending request
 * 7. Empty state — shown when no approvals are pending
 *
 * Conditional steps:
 * - Area queue (step 4) only shows if the user has area approver role
 * - Final queue (step 5) only shows if the user has final approver role
 * - Request card (step 6) only shows if at least one card exists
 * - Empty state (step 7) only shows when queues are empty
 *
 * All handled by filterActiveSteps — missing DOM targets are silently skipped.
 */
export const APPROVALS_CENTER_STEPS: TourStep[] = [
    // 1. Page header
    {
        target: '[data-tour="approvals-header"]',
        title: 'Centro de Aprovações',
        content: 'Este workspace centraliza os pedidos que precisam de análise e decisão dentro do fluxo de Procurement.',
        placement: 'bottom',
        skipBeacon: true,
    },
    // 2. KPI cards
    {
        target: '[data-tour="approvals-kpi-cards"]',
        title: 'Indicadores de Aprovação',
        content: 'Estes cards resumem a quantidade de pedidos pendentes, valor total em fila, pedidos urgentes e pedidos com alertas.',
        placement: 'bottom',
    },
    // 3. Filter tabs
    {
        target: '[data-tour="approvals-filter-tabs"]',
        title: 'Filtros Rápidos',
        content: 'Use estes filtros para priorizar a fila por urgência, pedidos mais antigos, maior valor, alertas ou tipo de aprovação.',
        placement: 'bottom',
    },
    // 4. Area approval queue (conditional — only for area approvers)
    {
        target: '[data-tour="approvals-area-queue"]',
        title: 'Aprovação de Área',
        content: 'Esta fila mostra os pedidos que aguardam a sua decisão como aprovador de área.',
        placement: 'top',
    },
    // 5. Final approval queue (conditional — only for final approvers)
    {
        target: '[data-tour="approvals-final-queue"]',
        title: 'Aprovação Final',
        content: 'Esta fila mostra os pedidos que aguardam validação final antes de seguir para as próximas etapas do processo.',
        placement: 'top',
    },
    // 6. Request card (conditional — only if at least one card exists)
    {
        target: '[data-tour="approvals-request-card"]',
        title: 'Pedido em Aprovação',
        content: 'Cada card representa um pedido pendente. Aqui você vê informações essenciais como número do pedido, data, solicitante, departamento, valor e status atual. Lotes de cotação aguardando a seleção do vencedor pelo Aprovador de Área mostram "A definir pelo Aprovador de Área" no lugar do valor. Clique no pedido para abrir os detalhes e tomar a decisão de aprovar ou rejeitar.',
        placement: 'bottom',
    },
    // 7. Empty state (conditional — only when queues are empty)
    {
        target: '[data-tour="approvals-empty-state"]',
        title: 'Sem Pedidos Pendentes',
        content: 'Quando não houver pedidos aguardando sua decisão, as filas ficarão vazias. Novos pedidos aparecerão aqui automaticamente quando exigirem aprovação.',
        placement: 'top',
    },
];
