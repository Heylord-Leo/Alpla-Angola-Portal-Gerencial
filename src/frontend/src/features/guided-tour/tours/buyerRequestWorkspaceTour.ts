import type { TourStep } from '../guidedTourTypes';

/**
 * Buyer Request Workspace Page Tour (page-buyer-request-workspace)
 *
 * Route: /buyer/requests/:id (opened from Gestão de Cotações). Steps follow the screen layout:
 * 1. Header — request identity, operational state and next action
 * 1b. Assumir pedido — claim/takeover button in the header action group (rendered only when the server would accept it; skipped otherwise)
 * 2. Tabs — Itens · Cotações & Documentos · Lotes & Aprovações
 * 3. Quotations table — one row per quotation (supplier, document, date, total, state)
 * 4. Quotation actions — Editar / Excluir (visible only to the assigned Buyer while the request is in the
 *    quotation window; otherwise the step is skipped by filterActiveSteps)
 * 5. Classic tools link — the remaining actions not yet migrated
 *
 * Steps target stable wrappers only (never request numbers, supplier names or values). The quotations table and the
 * actions cell exist only on the "Cotações & Documentos" tab with at least one quotation; filterActiveSteps skips
 * them when absent, so the tour never shows a target-not-found error.
 */
export const BUYER_REQUEST_WORKSPACE_STEPS: TourStep[] = [
    {
        target: '[data-tour="buyer-workspace-header"]',
        title: 'Workspace do Pedido',
        content: 'Aqui vê a identificação do pedido, o estado operacional, a cobertura de itens e a próxima ação sugerida ao Comprador.',
        placement: 'bottom',
        skipBeacon: true,
    },
    {
        target: '[data-tour="buyer-workspace-assign"]',
        title: 'Assumir pedido',
        content: 'No grupo de ações do cabeçalho, ao lado de Solicitar cotação / Importar Cotação / Inserir Manualmente: quando o pedido não tem comprador atribuído, pode assumi-lo aqui. Um pedido já atribuído a outro comprador pode ser assumido por qualquer comprador do seu âmbito, com justificação obrigatória: a responsabilidade passa para si e o comprador anterior, o requisitante e os participantes do pedido são notificados por e-mail. O botão só aparece quando o servidor aceitaria a ação (não em pedidos de pagamento nem encerrados).',
        placement: 'bottom',
    },
    {
        target: '[data-tour="buyer-workspace-tabs"]',
        title: 'Separadores',
        content: 'Itens mostra a cobertura por item solicitado; Cotações & Documentos lista as cotações registadas; Lotes & Aprovações acompanha os lotes enviados.',
        placement: 'bottom',
    },
    {
        target: '[data-tour="buyer-workspace-quotations"]',
        title: 'Cotações & Documentos',
        content: 'Cada linha é uma cotação: fornecedor, número e data do documento, itens cotados, total por moeda e estado. Valores de moedas diferentes nunca são somados.',
        placement: 'top',
    },
    {
        target: '[data-tour="buyer-workspace-quotation-actions"]',
        title: 'Editar ou excluir uma cotação',
        content: 'Editar abre a cotação existente no assistente (mesma cotação, mesmo documento). Excluir remove a cotação estruturada e as suas linhas; o documento associado é marcado como removido. As ações só aparecem ao Comprador responsável enquanto o pedido está na fase de cotação e são recusadas se a cotação já integrar um lote de aprovação.',
        placement: 'left',
    },
    {
        target: '[data-tour="buyer-workspace-classic-link"]',
        title: 'Ferramentas clássicas',
        content: 'As ações ainda não migradas para o Workspace (remover proforma, reutilizar cotação, cancelar lote, cancelar pedido) continuam disponíveis na ferramenta clássica, já filtrada por este pedido.',
        placement: 'bottom',
    },
];
