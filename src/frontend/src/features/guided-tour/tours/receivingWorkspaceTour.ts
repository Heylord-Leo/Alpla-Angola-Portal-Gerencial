import type { TourStep } from '../guidedTourTypes';

/**
 * Receiving Workspace Page Tour (page-receiving-workspace)
 *
 * Focused on how to use the Workspace de Recebimento.
 * Steps follow the operational flow:
 * 1. Page header — workspace context
 * 2. Info banner — explains the workspace purpose
 * 3. Search — find requests by number/title
 * 4. Pedidos aguardando recebimento — pending stage
 * 5. Pedidos em acompanhamento — in-progress / partial receipts
 * 6. Pedidos recebidos — completed stage
 *
 * All section steps target the wrapper div (always rendered),
 * so they appear even when the section count is 0.
 * filterActiveSteps will skip any target not in the DOM.
 */
export const RECEIVING_WORKSPACE_STEPS: TourStep[] = [
    // 1. Page header
    {
        target: '[data-tour="receiving-header"]',
        title: 'Workspace de Recebimento',
        content: 'Neste workspace você acompanha a entrada de materiais e serviços, desde pedidos aguardando recebimento até os já finalizados.',
        placement: 'bottom',
        skipBeacon: true,
    },
    // 2. Info banner
    {
        target: '[data-tour="receiving-info"]',
        title: 'Nota Informativa',
        content: 'Este aviso explica que o workspace organiza os pedidos por estágio operacional: aguardando recebimento, em acompanhamento e recebidos.',
        placement: 'bottom',
    },
    // 3. Search bar
    {
        target: '[data-tour="receiving-search"]',
        title: 'Pesquisa',
        content: 'Use a barra de pesquisa para localizar pedidos pelo número, título ou empresa.',
        placement: 'bottom',
    },
    // 4. Pending section
    {
        target: '[data-tour="receiving-pending"]',
        title: 'Pedidos Pendentes',
        content: 'Esta seção mostra os pedidos que aguardam recebimento. Clique em "Receber" para iniciar a operação de entrada.',
        placement: 'top',
    },
    // 5. In-progress / followup section
    {
        target: '[data-tour="receiving-in-progress"]',
        title: 'Pedidos em Acompanhamento',
        content: 'Esta seção mostra os pedidos cujo recebimento já foi iniciado, mas ainda não foi totalmente concluído. Use esta área para acompanhar recebimentos parciais, pendências e pedidos que ainda precisam de conferência.',
        placement: 'top',
    },
    // 6. Completed section
    {
        target: '[data-tour="receiving-completed"]',
        title: 'Pedidos Recebidos',
        content: 'Aqui ficam os pedidos já finalizados. Use "Visualizar" para consultar o histórico de recebimento.',
        placement: 'top',
    },
];
