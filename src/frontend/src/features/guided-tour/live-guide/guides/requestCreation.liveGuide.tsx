import type { LiveGuideStep, LiveGuideDefinition } from '../liveGuideTypes';

/**
 * Request Creation Live Guide
 *
 * Guides the user step-by-step through creating a new request draft.
 * Uses a factory function pattern to receive a form state getter,
 * avoiding tight coupling to React hooks or component internals.
 *
 * Target: /requests/new (New Request Draft / Novo Rascunho screen)
 */

/** Minimal form state shape needed for validation */
export interface RequestFormValues {
    title: string;
    description: string;
    requestTypeId: string;
    needLevelId: string;
    needByDateUtc: string;
    departmentId: string;
    companyId: string;
    plantId: string;
}

/**
 * Helper: read the value of an input/select/textarea inside a data-guide target.
 * Returns the trimmed string value, or '' if not found.
 */
function readGuideTargetValue(dataGuideAttr: string): string {
    const container = document.querySelector(`[data-guide="${dataGuideAttr}"]`);
    if (!container) return '';
    const el = container.querySelector('input, select, textarea') as
        HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | null;
    return el ? el.value.trim() : '';
}

/**
 * Helper: read the current requestTypeId directly from the DOM select element.
 * This is the primary method for condition evaluation to avoid stale-closure risks.
 */
function readRequestTypeFromDOM(): string {
    return readGuideTargetValue('request-type');
}

/**
 * Creates the live guide step definitions with validation functions.
 *
 * Validation reads the DOM value directly from the data-guide target element,
 * with a secondary check via the form state getter. This eliminates
 * stale-closure risks entirely.
 *
 * @param getFormValues — callback that returns the current form state snapshot
 */
export function createRequestCreationSteps(
    getFormValues: () => RequestFormValues
): LiveGuideStep[] {
    return [
        // ── Step: Introduction (centered modal — no spotlight, no anchor) ──
        {
            id: 'intro',
            target: '[data-guide="request-form"]',
            title: 'Guia de Criação de Pedido',
            content:
                'Este guia vai ajudá-lo a criar um novo pedido passo a passo. ' +
                'Preencha cada campo conforme a orientação. O guia só seguirá quando a informação necessária estiver preenchida.',
            placement: 'center',
            requiredAction: 'none',
            allowSkip: false,
        },

        // ── Step: Título do Pedido ──
        {
            id: 'request-title',
            target: '[data-guide="request-title"]',
            title: 'Título do Pedido',
            content:
                'Informe um título curto e claro para identificar o pedido. ' +
                "Exemplo: 'Aquisição de laptops para TI', 'Pagamento de fornecedor' ou 'Compra de material de manutenção'.",
            placement: 'bottom',
            requiredAction: 'input',
            validate: () => readGuideTargetValue('request-title').length > 0
                || getFormValues().title.trim().length > 0,
            validationMessage: 'Preencha o título do pedido para continuar.',
            allowSkip: false,
        },

        // ── Step: Descrição ou Justificativa ──
        {
            id: 'request-description',
            target: '[data-guide="request-description"]',
            title: 'Descrição ou Justificativa',
            content:
                'Explique o motivo do pedido e os detalhes principais. ' +
                'Esta informação ajuda os aprovadores, compradores e financeiro a entenderem a necessidade.',
            placement: 'bottom',
            requiredAction: 'input',
            validate: () => readGuideTargetValue('request-description').length > 0
                || getFormValues().description.trim().length > 0,
            validationMessage: 'Preencha a descrição ou justificativa para continuar.',
            allowSkip: false,
        },

        // ── Step: Documentos de Apoio (optional) ──
        {
            id: 'request-documents',
            target: '[data-guide="request-documents"]',
            title: 'Documentos de Apoio',
            content:
                'Aqui você pode anexar documentos que ajudem o comprador a encontrar ou validar aquilo que está sendo solicitado, ' +
                'como especificações técnicas, fotos, referências, propostas preliminares ou e-mails de apoio.\n\n' +
                'Documentos fiscais ou faturas finais devem ser inseridos na etapa correta do fluxo de OCR, quando aplicável.',
            placement: 'bottom',
            requiredAction: 'upload',
            allowSkip: true,
            fallbackContent: 'A área de documentos não está visível no momento. Pode prosseguir.',
        },

        // ── Step: Tipo de Pedido (rich formatted content) ──
        {
            id: 'request-type',
            target: '[data-guide="request-type"]',
            title: 'Tipo de Pedido',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                    <p style={{ margin: 0 }}>Escolha o tipo de pedido conforme a situação:</p>

                    <div>
                        <strong style={{ color: 'var(--color-primary, #2563eb)' }}>Cotação</strong>
                        <p style={{ margin: '2px 0 4px 0' }}>
                            Use quando ainda não existe fornecedor definido, preço final ou documento formal para pagamento.
                        </p>
                        <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted, #6b7280)', paddingLeft: '8px' }}>
                            <p style={{ margin: '0 0 1px 0', fontWeight: 600, fontSize: '0.72rem', textTransform: 'uppercase', letterSpacing: '0.03em' }}>Exemplos:</p>
                            <ul style={{ margin: '0', paddingLeft: '16px', listStyleType: 'disc' }}>
                                <li>compra de equipamento sem fornecedor definido;</li>
                                <li>pesquisa de preço para material;</li>
                                <li>comparação entre fornecedores;</li>
                                <li>pedido baseado apenas numa especificação técnica.</li>
                            </ul>
                        </div>
                    </div>

                    <div style={{ borderTop: '1px solid var(--color-border, #e5e7eb)', paddingTop: '8px' }}>
                        <strong style={{ color: 'var(--color-primary, #2563eb)' }}>Pagamento</strong>
                        <p style={{ margin: '2px 0 4px 0' }}>
                            Use quando já existe uma obrigação ou documento para pagar, normalmente com fornecedor identificado e documento de suporte.
                        </p>
                        <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted, #6b7280)', paddingLeft: '8px' }}>
                            <p style={{ margin: '0 0 1px 0', fontWeight: 600, fontSize: '0.72rem', textTransform: 'uppercase', letterSpacing: '0.03em' }}>Exemplos:</p>
                            <ul style={{ margin: '0', paddingLeft: '16px', listStyleType: 'disc' }}>
                                <li>pagamento de uma proforma já recebida;</li>
                                <li>serviço já acordado;</li>
                                <li>fornecedor já definido;</li>
                                <li>pedido que precisa seguir para validação/aprovação de pagamento.</li>
                            </ul>
                        </div>
                    </div>
                </div>
            ),
            placement: 'bottom',
            requiredAction: 'select',
            validate: () => readGuideTargetValue('request-type').length > 0
                || getFormValues().requestTypeId !== '',
            validationMessage: 'Selecione o tipo de pedido para continuar.',
            allowSkip: false,
        },

        // ── Step: Cotação → Itens Solicitados ──
        // Visible only when Cotação (id=1) is selected.
        {
            id: 'request-quotation-items',
            target: '[data-guide="request-quotation-items-section"]',
            title: 'Itens Solicitados',
            content:
                'Como este pedido é do tipo Cotação, você deve informar os itens, materiais ou serviços que pretende solicitar.\n\n' +
                "Clique em 'Adicionar Item' para especificar o que precisa ser comprado ou cotado. " +
                'Quanto mais clara for a descrição dos itens, mais fácil será para o comprador procurar fornecedores, preços e alternativas.',
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => {
                const domVal = readRequestTypeFromDOM();
                if (domVal === '1') return true;
                if (Number(getFormValues().requestTypeId) === 1) return true;
                return false;
            },
        },

        // ── Step: Pagamento → Input de Documento & Faturamento ──
        // Visible only when Pagamento (id=2) is selected.
        {
            id: 'request-payment-document',
            target: '[data-guide="request-payment-document-section"]',
            title: 'Input de Documento & Faturamento',
            content:
                'Como este pedido é do tipo Pagamento, você deve iniciar o preenchimento dos dados de faturamento.\n\n' +
                'Você pode importar um documento para extração automática via OCR ou inserir as informações manualmente.\n\n' +
                "Use 'Importar Documento' quando tiver um documento PDF ou imagem para análise. " +
                "Use 'Inserir Manualmente' quando preferir preencher os dados diretamente no sistema.",
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => {
                const domVal = readRequestTypeFromDOM();
                if (domVal === '2') return true;
                if (Number(getFormValues().requestTypeId) === 2) return true;
                return false;
            },
        },

        // ── Step: Tipo de Documento de Faturação (Pagamento, quando a funcionalidade está ativa) ──
        // A condição observa o próprio campo no DOM: enquanto a funcionalidade estiver desativada o
        // campo não é renderizado e o passo desaparece sozinho, sem duplicar aqui a feature flag.
        {
            id: 'request-source-document-type',
            target: '[data-guide="request-source-document-type"]',
            title: 'Tipo de Documento de Faturação',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                    <p style={{ margin: 0 }}>
                        Selecione o tipo de documento que originou este pedido. Esta escolha define o que
                        será exigido depois do pagamento — não é apenas uma etiqueta.
                    </p>

                    <div>
                        <strong style={{ color: 'var(--color-warning, #d97706)' }}>Fatura Proforma</strong>
                        <p style={{ margin: '2px 0 6px 0', fontSize: '0.85rem' }}>
                            O fornecedor enviou uma proforma. Depois do pagamento <strong>será exigida a
                            Fatura Final</strong> para concluir o pedido.
                        </p>
                    </div>

                    <div style={{ borderTop: '1px solid var(--color-border, #e5e7eb)', paddingTop: '6px' }}>
                        <strong style={{ color: 'var(--color-primary, #2563eb)' }}>Fatura Final</strong>
                        <p style={{ margin: '2px 0 0 0', fontSize: '0.85rem' }}>
                            O fornecedor já enviou a fatura definitiva. <strong>Não será exigida outra
                            fatura</strong> depois do pagamento.
                        </p>
                    </div>

                    <p style={{ margin: 0, fontSize: '0.8rem', color: 'var(--color-text-muted, #6b7280)' }}>
                        Pode guardar o rascunho sem escolher, mas a seleção é obrigatória para submeter o pedido.
                    </p>
                </div>
            ),
            placement: 'bottom',
            requiredAction: 'none',
            allowSkip: true,
            condition: () => !!document.querySelector('[data-guide="request-source-document-type"]'),
        },

        // ── Step: Grau de Necessidade ──
        {
            id: 'request-need-level',
            target: '[data-guide="request-need-level"]',
            title: 'Grau de Necessidade',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                    <p style={{ margin: 0 }}>
                        Selecione o grau de necessidade do pedido. Esta informação ajuda a equipe de compras e aprovação a entender a urgência.
                    </p>
                    <p style={{ margin: 0, fontWeight: 600, fontSize: '0.82rem' }}>Use como referência:</p>

                    <div>
                        <strong style={{ color: 'var(--color-danger, #dc2626)' }}>Crítico</strong>
                        <p style={{ margin: '2px 0 6px 0', fontSize: '0.85rem' }}>
                            Quando a falta do item ou serviço pode parar uma operação, afetar produção, segurança, entrega ao cliente ou causar impacto imediato no negócio.
                        </p>
                    </div>

                    <div style={{ borderTop: '1px solid var(--color-border, #e5e7eb)', paddingTop: '6px' }}>
                        <strong style={{ color: 'var(--color-warning, #d97706)' }}>Urgente</strong>
                        <p style={{ margin: '2px 0 6px 0', fontSize: '0.85rem' }}>
                            Quando o pedido é importante e deve ser tratado com prioridade, mas ainda não representa uma parada imediata.
                        </p>
                    </div>

                    <div style={{ borderTop: '1px solid var(--color-border, #e5e7eb)', paddingTop: '6px' }}>
                        <strong style={{ color: 'var(--color-primary, #2563eb)' }}>Normal</strong>
                        <p style={{ margin: '2px 0 6px 0', fontSize: '0.85rem' }}>
                            Quando o pedido é necessário para manter a operação ou planejamento normal, mas existe algum tempo para tratamento.
                        </p>
                    </div>

                    <div style={{ borderTop: '1px solid var(--color-border, #e5e7eb)', paddingTop: '6px' }}>
                        <strong style={{ color: 'var(--color-text-muted, #6b7280)' }}>Baixo</strong>
                        <p style={{ margin: '2px 0 0 0', fontSize: '0.85rem' }}>
                            Quando o pedido não é urgente e pode seguir o fluxo normal de análise, compra e aprovação.
                        </p>
                    </div>
                </div>
            ),
            placement: 'bottom',
            requiredAction: 'select',
            validate: () => readGuideTargetValue('request-need-level').length > 0
                || getFormValues().needLevelId !== '',
            validationMessage: 'Selecione o grau de necessidade para continuar.',
            allowSkip: false,
        },

        // ── Step: Necessário até / Data Limite (conditional — appears when type is selected) ──
        {
            id: 'request-needed-by',
            target: '[data-guide="request-needed-by"]',
            title: 'Data Limite',
            content:
                'Informe a data limite em que este pedido será necessário. Esta informação ajuda a equipe de compras, ' +
                'aprovação e financeiro a priorizar o atendimento.\n\n' +
                'Exemplo: se o material precisa estar disponível até o fim do mês, selecione essa data como referência.',
            placement: 'bottom',
            requiredAction: 'input',
            validate: () => readGuideTargetValue('request-needed-by').length > 0
                || getFormValues().needByDateUtc !== '',
            validationMessage: 'Informe a data limite para continuar.',
            allowSkip: false,
            condition: () => {
                const domVal = readRequestTypeFromDOM();
                if (domVal === '1' || domVal === '2') return true;
                const formVal = Number(getFormValues().requestTypeId);
                return formVal === 1 || formVal === 2;
            },
        },

        // ── Step: Departamento ──
        {
            id: 'request-department',
            target: '[data-guide="request-department"]',
            title: 'Departamento',
            content: (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <p style={{ margin: 0 }}>
                        Selecione o departamento responsável pelo pedido.
                    </p>
                    <p style={{ margin: 0, fontSize: '0.85rem' }}>
                        A lista mostra apenas os departamentos que fazem parte do seu escopo de acesso.
                        Isso garante que você consiga acompanhar o pedido depois de criado e evita pedidos vinculados a áreas que você não tem permissão para visualizar.
                    </p>
                    <p style={{ margin: 0, fontSize: '0.82rem', color: 'var(--color-text-muted, #6b7280)', fontStyle: 'italic' }}>
                        Se o departamento necessário não aparecer na lista, solicite a revisão do seu escopo de acesso ao administrador.
                    </p>
                </div>
            ),
            placement: 'bottom',
            requiredAction: 'select',
            validate: () => readGuideTargetValue('request-department').length > 0
                || getFormValues().departmentId !== '',
            validationMessage: 'Selecione o departamento para continuar.',
            allowSkip: false,
        },

        // ── Step: Empresa ──
        {
            id: 'request-company',
            target: '[data-guide="request-company"]',
            title: 'Empresa',
            content:
                'Selecione a empresa relacionada ao pedido. Exemplo: AlplaPLASTICO ou AlplaSOPRO.',
            placement: 'top',
            requiredAction: 'select',
            validate: () => readGuideTargetValue('request-company').length > 0
                || getFormValues().companyId !== '',
            validationMessage: 'Selecione a empresa para continuar.',
            allowSkip: false,
        },

        // ── Step: Planta ──
        {
            id: 'request-plant',
            target: '[data-guide="request-plant"]',
            title: 'Planta',
            content:
                'Selecione a planta onde o pedido será utilizado. ' +
                'A planta influencia filtros, centro de custo, aprovações e relatórios.',
            placement: 'top',
            requiredAction: 'select',
            validate: () => readGuideTargetValue('request-plant').length > 0
                || getFormValues().plantId !== '',
            validationMessage: 'Selecione a planta para continuar.',
            allowSkip: false,
        },

        // ── Step: Criar Rascunho ──
        {
            id: 'request-submit',
            target: '[data-guide="request-submit"]',
            title: 'Criar Rascunho',
            content:
                "Depois de preencher os dados obrigatórios, clique em 'Criar Rascunho'. " +
                'O pedido ainda não será submetido para aprovação. ' +
                'Ele será salvo como rascunho para que você possa revisar, adicionar itens e documentos antes de enviar.',
            placement: 'top',
            requiredAction: 'none',
            allowSkip: false,
        },
    ];
}

/**
 * Factory: creates the full LiveGuideDefinition for the Request Creation screen.
 *
 * @param getFormValues — callback that returns the current form state snapshot
 */
export function createRequestCreationGuide(
    getFormValues: () => RequestFormValues
): LiveGuideDefinition {
    return {
        id: 'request-creation-live-guide',
        type: 'live-guide',
        module: 'requests',
        route: '/requests/new',
        title: 'Guia — Criar Pedido',
        description: 'Ajuda passo a passo para criar um novo pedido de compra.',
        version: '1.4.0',
        enabled: true,
        steps: createRequestCreationSteps(getFormValues),
    };
}
