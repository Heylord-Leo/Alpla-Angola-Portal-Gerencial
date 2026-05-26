import type { TourStep } from '../guidedTourTypes';

/**
 * Buyer Items Page Tour (page-buyer-items)
 *
 * Focused on how to use the Gestão de Cotações workspace.
 * Steps follow the natural visual scan:
 * 1. Page header — context and available actions
 * 2. Search & filters — find requests by status, owner, or text
 * 3. Request list — the work queue of requests grouped by request
 * 4. Opened request — the expanded request details (conditional)
 * 5. Requested items — items inside the opened request (conditional)
 * 6. Quotations / documents — existing quotations section (conditional)
 * 7. Empty state — shown only when no requests exist (conditional)
 *
 * Steps 4–7 are conditional on DOM state and will be filtered out
 * automatically by filterActiveSteps if their targets are absent.
 */
export const BUYER_ITEMS_PAGE_STEPS: TourStep[] = [
    // 1. Page header
    {
        target: '[data-tour="buyer-items-header"]',
        title: 'Gestão de Cotações',
        content: 'Neste workspace o comprador visualiza e gere os itens solicitados, organiza cotações e acompanha o processo de compra.',
        placement: 'bottom',
        skipBeacon: true,
    },
    // 2. Search & filters
    {
        target: '[data-tour="buyer-items-search"]',
        title: 'Pesquisa e Filtros',
        content: 'Use a barra de pesquisa e os filtros para encontrar itens específicos por status, responsável ou termo de busca.',
        placement: 'bottom',
    },
    // 3. Request list / work queue
    {
        target: '[data-tour="buyer-items-list"]',
        title: 'Lista de Pedidos e Itens',
        content: 'Os pedidos são agrupados por solicitação. Clique na linha de um pedido para expandir e ver os detalhes, itens, cotações cadastradas, documentos e ações disponíveis.',
        placement: 'top',
    },
    // 4. Opened/expanded request details (visible only when a request is expanded)
    {
        target: '[data-tour="buyer-open-request"]',
        title: 'Pedido Aberto',
        content: 'Este bloco mostra o pedido selecionado com as principais informações para cotação: dados do pedido, solicitante, comprador, aprovador, data necessária, itens solicitados, cotações registradas e documentos associados.',
        placement: 'top',
    },
    // 5. Requested items section (inside the opened request)
    {
        target: '[data-tour="buyer-open-request-items"]',
        title: 'Itens Solicitados',
        content: 'Aqui estão os itens do pedido original: descrição, quantidade, unidade e valores estimados. Use esta tabela para conferir o que foi solicitado antes de registar as cotações.',
        placement: 'top',
    },
    // 6. Quotations / documents section (inside the opened request)
    {
        target: '[data-tour="buyer-open-request-quotations"]',
        title: 'Cotações e Documentos',
        content: 'Nesta seção ficam as cotações já registradas e os documentos proforma anexados. Compare fornecedores, verifique valores e identifique a melhor proposta para o pedido.',
        placement: 'top',
    },
    // 7. Empty state (visible only when no requests exist)
    {
        target: '[data-tour="buyer-items-empty-state"]',
        title: 'Sem Pedidos Disponíveis',
        content: 'Quando não houver pedidos disponíveis para cotação, esta área ficará vazia. Assim que novos pedidos forem atribuídos, eles aparecerão aqui para análise.',
        placement: 'bottom',
    },
];
