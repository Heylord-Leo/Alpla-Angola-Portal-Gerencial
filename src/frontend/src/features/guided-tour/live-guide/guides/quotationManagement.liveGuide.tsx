import type { LiveGuideStep, LiveGuideDefinition } from '../liveGuideTypes';

/**
 * Quotation Management Live Guide
 *
 * Guides the buyer step-by-step through the quotation management workspace.
 * Uses a factory function pattern to receive a state getter,
 * avoiding tight coupling to React hooks or component internals.
 *
 * Target: /buyer/items (Buyer Workspace / Gestão de Cotações screen)
 *
 * This guide is ASSISTIVE (not mandatory). All steps use `requiredAction: 'none'`
 * because the buyer workspace has complex state dependencies and the guide
 * explains the workflow without forcing real actions.
 */

/** Minimal state shape needed for conditional step logic */
export interface QuotationManagementState {
    /** Whether the first request group is currently expanded */
    isFirstGroupExpanded: boolean;
    /** Whether the first group is assigned to the current user */
    isAssignedToMe: boolean;
    /** Whether there is at least one saved quotation on the first group */
    hasQuotations: boolean;
    /** Whether a quotation add mode (UPLOAD or MANUAL) is active */
    isAddingQuotation: boolean;
    /** Request status code of the first group */
    requestStatusCode: string;
    /** Whether there are any visible request groups on the page */
    hasVisibleGroups: boolean;
    /** Whether the first group has a buyer assigned (any buyer, not just current) */
    hasBuyerAssigned: boolean;
}

/**
 * Helper: check if a DOM element exists for a given data-guide attribute.
 */
function guideTargetExists(attr: string): boolean {
    return !!document.querySelector(`[data-guide="${attr}"]`);
}

/**
 * Creates the live guide step definitions with conditional logic.
 *
 * Conditions read the state getter snapshot at evaluation time,
 * with DOM existence checks as fallback safety to avoid crashes
 * when elements are not rendered.
 *
 * @param getState — callback that returns the current workspace state snapshot
 */
export function createQuotationManagementSteps(
    getState: () => QuotationManagementState
): LiveGuideStep[] {
    return [
        // ── Step 1: Introduction (centered — no spotlight) ──
        {
            id: 'intro',
            target: '[data-guide="qm-page"]',
            title: 'Bem-vindo à Gestão de Cotações',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                    <p style={{ margin: 0 }}>
                        Este guia vai ajudá-lo a entender a tela de <strong>Gestão de Cotações</strong>,
                        o workspace principal do comprador.
                    </p>
                    <p style={{ margin: 0 }}>
                        Cada passo explica uma secção do workspace: cabeçalho, filtros,
                        cartões de pedido, atribuição, itens, documentos e ações de cotação.
                    </p>
                    {!getState().hasVisibleGroups && (
                        <p style={{
                            margin: '4px 0 0 0',
                            padding: '8px 12px',
                            backgroundColor: '#FEF3C7',
                            border: '1px solid #FDE68A',
                            borderRadius: '6px',
                            fontSize: '0.82rem',
                            color: '#92400E',
                            fontWeight: 600,
                        }}>
                            ⚠ Não existem pedidos visíveis neste momento. Alguns passos operacionais
                            serão ignorados automaticamente. Para uma demonstração completa, certifique-se
                            de que existe pelo menos um pedido em cotação visível.
                        </p>
                    )}
                </div>
            ),
            placement: 'center',
            requiredAction: 'none',
            allowSkip: false,
        },

        // ── Step 2: Page Header ──
        {
            id: 'header',
            target: '[data-guide="qm-header"]',
            title: 'Cabeçalho e Ações',
            content:
                'O cabeçalho mostra o título da tela e as ações globais. ' +
                'Aqui pode encontrar o botão "Tour da Tela" (explicação visual dos elementos), ' +
                'o "Manual de Cotação" (referência de procedimentos) e este "Guia ao Vivo" (assistência passo a passo).',
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: false,
        },

        // ── Step 3: Search & Filters ──
        {
            id: 'search-filters',
            target: '[data-guide="qm-search"]',
            title: 'Busca e Filtros',
            content:
                'Use a barra de busca para localizar pedidos por número, título ou descrição. ' +
                'As abas permitem filtrar: "Todos" mostra todos os pedidos, "Não Atribuídos" filtra os que aguardam atribuição, ' +
                'e "Meus Pedidos" mostra apenas os pedidos atribuídos a você.\n\n' +
                'O seletor de status permite filtrar por etapa do fluxo (Aguardando Cotação, Reajuste de Área, etc.).',
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: false,
        },

        // ── Step 4: Request Card ──
        {
            id: 'request-card',
            target: '[data-guide="qm-request-card"]',
            title: 'Cartão do Pedido',
            content:
                'Cada pedido aparece como um cartão de trabalho. ' +
                'O cartão mostra: número do pedido, status, solicitante, comprador atribuído, data necessária e ação pendente.\n\n' +
                'Use o botão de expansão (seta) para abrir os detalhes e visualizar ' +
                'resumo, itens, documentos, cotações e ações disponíveis.',
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => getState().hasVisibleGroups && guideTargetExists('qm-request-card'),
            fallbackContent: 'Não existem pedidos visíveis neste momento. Este passo será ignorado.',
        },

        // ── Step 5: Expand/Collapse Button ──
        {
            id: 'expand-request',
            target: '[data-guide="qm-expand-request"]',
            title: 'Abrir Detalhes do Pedido',
            content:
                'Use este botão para abrir ou ocultar os detalhes do pedido. ' +
                'Ao expandir, você verá o resumo, itens solicitados, documentos, cotações e ações disponíveis.\n\n' +
                'Clique no botão de seta para alternar entre o estado aberto e fechado.',
            placement: 'right',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => getState().hasVisibleGroups && guideTargetExists('qm-expand-request'),
            fallbackContent: 'O botão de expansão não está visível no momento.',
        },

        // ── Step 6: Assign Button ──
        {
            id: 'assign-button',
            target: '[data-guide="qm-assign-btn"]',
            title: 'Atribuição do Pedido',
            content: (() => {
                const state = getState();
                if (state.isAssignedToMe) {
                    return 'Este pedido já está atribuído a você. ' +
                        'Como comprador responsável, pode registrar cotações, anexar documentos e submeter para aprovação.';
                }
                if (state.hasBuyerAssigned) {
                    return (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <p style={{ margin: 0 }}>
                                Este pedido já está atribuído a outro comprador.
                                Dependendo das regras de negócio, poderá usar o botão "Assumir Pedido"
                                para reatribuir a responsabilidade para si.
                            </p>
                            <p style={{ margin: 0, fontSize: '0.82rem', color: 'var(--color-text-muted, #6b7280)', fontStyle: 'italic' }}>
                                Algumas ações podem estar bloqueadas enquanto o pedido não estiver atribuído a você.
                            </p>
                        </div>
                    );
                }
                return 'Clique "Atribuir a Mim" para reivindicar este pedido como seu. ' +
                    'Somente o comprador atribuído pode registrar cotações, anexar documentos e submeter o pedido para aprovação.';
            })(),
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => getState().hasVisibleGroups && guideTargetExists('qm-assign-btn'),
            fallbackContent: 'O botão de atribuição não está visível — o pedido pode já estar atribuído a você.',
        },

        // ── Step 7: Request Summary (expanded) ──
        {
            id: 'request-summary',
            target: '[data-guide="qm-request-summary"]',
            title: 'Resumo do Pedido',
            content:
                'Aqui encontra as informações gerais do pedido: planta, departamento, título e descrição. ' +
                'Se o solicitante anexou documentos de apoio (especificações, fotos, referências), eles também aparecem nesta secção.',
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => getState().isFirstGroupExpanded && guideTargetExists('qm-request-summary'),
            fallbackContent: 'Expanda um pedido para ver o resumo completo.',
        },

        // ── Step 8: Items Section (expanded) ──
        {
            id: 'items-section',
            target: '[data-guide="qm-items-section"]',
            title: 'Itens Solicitados',
            content:
                'Esta tabela lista os itens, materiais ou serviços que o solicitante precisa. ' +
                'Verifique as descrições, quantidades, unidades e se os itens são de catálogo ou manuais.\n\n' +
                'Use esta informação como base para contactar fornecedores e obter cotações.',
            placement: 'top',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => getState().isFirstGroupExpanded && guideTargetExists('qm-items-section'),
            fallbackContent: 'Expanda um pedido para ver os itens solicitados.',
        },

        // ── Step 9: Documents & Quotations Section (expanded) ──
        {
            id: 'documents-section',
            target: '[data-guide="qm-docs-section"]',
            title: 'Documentos e Cotações Registradas',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <p style={{ margin: 0 }}>
                        A <strong>Seção A</strong> mostra todas as cotações já registradas e os documentos anexados (proformas, faturas).
                    </p>
                    <p style={{ margin: 0 }}>
                        Cada cotação exibe: fornecedor, número do documento, data, valor total e método de entrada (OCR ou Manual).
                    </p>
                    <p style={{ margin: 0, fontSize: '0.82rem', color: 'var(--color-text-muted, #6b7280)' }}>
                        Quando existem múltiplas cotações, o sistema destaca automaticamente a de menor valor para facilitar a comparação.
                    </p>
                </div>
            ),
            placement: 'top',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => getState().isFirstGroupExpanded && guideTargetExists('qm-docs-section'),
            fallbackContent: 'Expanda um pedido para ver os documentos e cotações.',
        },

        // ── Step 10: Add Quotation Section (expanded + assigned + mutable status) ──
        {
            id: 'add-quotation',
            target: '[data-guide="qm-add-quotation"]',
            title: 'Adicionar Nova Cotação',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <p style={{ margin: 0 }}>
                        A <strong>Seção B</strong> permite registrar uma nova cotação de duas formas:
                    </p>
                    <div>
                        <strong style={{ color: 'var(--color-primary, #2563eb)' }}>Importar Documento</strong>
                        <p style={{ margin: '2px 0 6px 0' }}>
                            Selecione um PDF ou imagem da cotação/proforma. O sistema usará OCR para extrair os dados automaticamente.
                            Depois, revise e corrija os valores antes de salvar.
                        </p>
                    </div>
                    <div style={{ borderTop: '1px solid var(--color-border, #e5e7eb)', paddingTop: '6px' }}>
                        <strong style={{ color: '#d946ef' }}>Inserir Manualmente</strong>
                        <p style={{ margin: '2px 0 0 0' }}>
                            Preencha os dados da cotação diretamente: fornecedor, número do documento, moeda, itens e valores.
                            Use quando o documento é complexo demais para OCR ou quando não existe documento digital.
                        </p>
                    </div>
                </div>
            ),
            placement: 'top',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => {
                const s = getState();
                return s.isFirstGroupExpanded
                    && s.isAssignedToMe
                    && ['WAITING_QUOTATION', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'].includes(s.requestStatusCode)
                    && guideTargetExists('qm-add-quotation');
            },
            fallbackContent:
                'A secção de adicionar cotação não está disponível. ' +
                'Certifique-se de que o pedido está atribuído a você e em status adequado (Aguardando Cotação).',
        },

        // ── Step 11: Complete Quotation (expanded + has quotations + not editing) ──
        {
            id: 'complete-quotation',
            target: '[data-guide="qm-complete-btn"]',
            title: 'Concluir Cotação',
            content:
                'Quando todas as cotações estiverem registradas e revisadas, clique "Concluir Cotação" ' +
                'para submeter o pedido para a próxima etapa de aprovação.\n\n' +
                'Se existir uma cotação em edição (rascunho aberto), finalize ou cancele antes de concluir.',
            placement: 'top',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => {
                const s = getState();
                return s.isFirstGroupExpanded
                    && s.hasQuotations
                    && !s.isAddingQuotation
                    && s.requestStatusCode === 'WAITING_QUOTATION'
                    && guideTargetExists('qm-complete-btn');
            },
            fallbackContent:
                'O botão "Concluir Cotação" aparece quando existe pelo menos uma cotação salva e nenhum rascunho em edição.',
        },
    ];
}

/**
 * Factory: creates the full LiveGuideDefinition for the Quotation Management screen.
 *
 * @param getState — callback that returns the current workspace state snapshot
 */
export function createQuotationManagementGuide(
    getState: () => QuotationManagementState
): LiveGuideDefinition {
    return {
        id: 'quotation-management-live-guide',
        type: 'live-guide',
        module: 'buyer',
        route: '/buyer/items',
        title: 'Guia — Gestão de Cotações',
        description: 'Ajuda passo a passo para gerir pedidos e registrar cotações no workspace do comprador.',
        version: '1.0.0',
        enabled: true,
        steps: createQuotationManagementSteps(getState),
    };
}
