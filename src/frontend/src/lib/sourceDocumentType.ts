/**
 * Document classification — Release 2 corrected.
 *
 * The taxonomy describes **what the supplier actually issued**, under Angola's Regime Jurídico das
 * Facturas (Presidential Decree 71/25). It is deliberately NOT a description of the workflow:
 * what remains owed is derived separately, so that a Factura de Adiantamento (fiscal, yet still
 * owing an operation invoice) and a Factura-Recibo (owing no separate receipt) are both
 * representable.
 *
 * Mirrors `RequestConstants.SourceDocumentTypes` and `DocumentObligationResolver` on the backend.
 * The backend re-derives and re-validates everything here — this exists so the UI can offer the
 * right options and explain the consequences, never to decide them.
 */

export const SOURCE_DOCUMENT_TYPES = {
    ESTIMATE: 'ESTIMATE',
    PROFORMA: 'PROFORMA',
    ADVANCE_INVOICE: 'ADVANCE_INVOICE',
    INVOICE: 'INVOICE',
    INVOICE_RECEIPT: 'INVOICE_RECEIPT',
    OTHER: 'OTHER',
    UNCLASSIFIED: 'UNCLASSIFIED'
} as const;

export type SourceDocumentType = typeof SOURCE_DOCUMENT_TYPES[keyof typeof SOURCE_DOCUMENT_TYPES];

/** Where the document is being presented. Availability and obligations both depend on it. */
export type DocumentUsageContext = 'PAYMENT_REQUEST' | 'QUOTATION_MANAGEMENT' | 'POST_PAYMENT_EVIDENCE';

export interface DocumentTypeOption {
    value: SourceDocumentType;
    label: string;
    isFiscal: boolean;
    /** Routed to the Finance review queue by the backend resolver. */
    requiresReview: boolean;
    /** Legitimate but atypical in this context — offered, but flagged. */
    unusual?: boolean;
}

/**
 * The two halves of the explanation, kept apart on purpose.
 *
 * `whatItIs` describes the artefact the supplier issued; `whatComesNext` describes what the Portal
 * will require because of it. Collapsing them into one sentence is how the identity of a document
 * and the obligations it triggers got confused in the first place.
 */
interface DocumentTypeDescription {
    whatItIs: string;
    whatComesNext: string;
}

const ALL: Record<SourceDocumentType, Omit<DocumentTypeOption, 'value'> & DocumentTypeDescription> = {
    ESTIMATE: {
        label: 'Orçamento / Cotação',
        isFiscal: false,
        requiresReview: false,
        whatItIs: 'Documento não fiscal com a proposta de preços do fornecedor.',
        whatComesNext: 'Não autoriza pagamento. Se a proposta avançar, serão exigidas a factura da operação e o comprovativo/recibo de pagamento.'
    },
    PROFORMA: {
        label: 'Factura Pró-forma',
        isFiscal: false,
        requiresReview: false,
        whatItIs: 'Documento não fiscal usado antes do pagamento.',
        whatComesNext: 'Depois será exigida a factura da operação e o comprovativo/recibo de pagamento.'
    },
    ADVANCE_INVOICE: {
        label: 'Factura de Adiantamento',
        isFiscal: true,
        requiresReview: true,
        whatItIs: 'Documento fiscal relativo a um adiantamento.',
        whatComesNext: 'Depois serão exigidas a factura da operação, a regularização/Nota de Crédito quando aplicável, o comprovativo de pagamento e a validação do Financeiro.'
    },
    INVOICE: {
        label: 'Factura',
        isFiscal: true,
        requiresReview: false,
        whatItIs: 'Documento fiscal da operação.',
        whatComesNext: 'Depois serão exigidos apenas o comprovativo/recibo de pagamento e o recebimento operacional.'
    },
    INVOICE_RECEIPT: {
        label: 'Factura-Recibo',
        isFiscal: true,
        requiresReview: true,
        whatItIs: 'Documento fiscal que já comprova o pagamento da operação.',
        whatComesNext: 'Não exige recibo separado. Resta apenas o recebimento operacional.'
    },
    OTHER: {
        label: 'Outro documento',
        isFiscal: false,
        requiresReview: true,
        whatItIs: 'Documento que não se enquadra claramente nas categorias acima.',
        whatComesNext: 'Exige revisão do Financeiro antes de prosseguir.'
    },
    UNCLASSIFIED: {
        label: 'Não classificado',
        isFiscal: false,
        requiresReview: true,
        whatItIs: 'Nenhum tipo foi ainda escolhido.',
        whatComesNext: 'Selecione o tipo de documento para prosseguir.'
    }
};

/** Wording that changes with the context, because the same document plays a different role. */
const CONTEXT_WORDING: Partial<Record<DocumentUsageContext, Partial<Record<SourceDocumentType, Partial<DocumentTypeDescription>>>>> = {
    QUOTATION_MANAGEMENT: {
        ESTIMATE: {
            whatComesNext: 'É o documento típico desta fase. Não autoriza pagamento por si só.'
        },
        PROFORMA: {
            whatComesNext: 'Se a cotação for escolhida e originar um pagamento, serão depois exigidas a factura da operação e o comprovativo/recibo de pagamento.'
        },
        ADVANCE_INVOICE: {
            whatItIs: 'Documento fiscal relativo a um adiantamento — invulgar nesta fase.'
        }
    }
};

function option(value: SourceDocumentType, unusual = false): DocumentTypeOption {
    const { label, isFiscal, requiresReview } = ALL[value];
    return { value, label, isFiscal, requiresReview, unusual };
}

/**
 * Options offered for each context.
 *
 * A Factura-Recibo is absent from both origin contexts on purpose: it states that the operation
 * and its full payment already happened, so offering it as the origin of a payable request would
 * invite the Portal to pay the same thing twice. An Orçamento is absent from Payment because a
 * non-fiscal document cannot authorize payment on its own.
 */
export function documentTypeOptionsFor(context: DocumentUsageContext): DocumentTypeOption[] {
    switch (context) {
        case 'PAYMENT_REQUEST':
            return [
                option(SOURCE_DOCUMENT_TYPES.PROFORMA),
                option(SOURCE_DOCUMENT_TYPES.ADVANCE_INVOICE),
                option(SOURCE_DOCUMENT_TYPES.INVOICE),
                option(SOURCE_DOCUMENT_TYPES.OTHER)
            ];
        case 'QUOTATION_MANAGEMENT':
            return [
                option(SOURCE_DOCUMENT_TYPES.ESTIMATE),
                option(SOURCE_DOCUMENT_TYPES.PROFORMA),
                option(SOURCE_DOCUMENT_TYPES.INVOICE),
                option(SOURCE_DOCUMENT_TYPES.ADVANCE_INVOICE, true),
                option(SOURCE_DOCUMENT_TYPES.OTHER)
            ];
        case 'POST_PAYMENT_EVIDENCE':
            return [
                option(SOURCE_DOCUMENT_TYPES.INVOICE),
                option(SOURCE_DOCUMENT_TYPES.INVOICE_RECEIPT),
                option(SOURCE_DOCUMENT_TYPES.OTHER)
            ];
    }
}

export function documentTypeLabel(value?: string | null): string {
    const key = normalizeDocumentType(value);
    return key ? ALL[key].label : ALL.UNCLASSIFIED.label;
}

export interface DocumentTypeExplanation extends DocumentTypeDescription {
    value: SourceDocumentType;
    label: string;
    isFiscal: boolean;
}

/**
 * What the "?" modal explains, one entry per option actually offered in this context.
 *
 * Derived from {@link documentTypeOptionsFor} rather than from its own list, so the modal can never
 * describe a document the dropdown does not offer — or omit one it does.
 */
export function documentTypeExplanations(context: DocumentUsageContext): DocumentTypeExplanation[] {
    return documentTypeOptionsFor(context).map(opt => {
        const base = ALL[opt.value];
        const override = CONTEXT_WORDING[context]?.[opt.value];
        return {
            value: opt.value,
            label: base.label,
            isFiscal: base.isFiscal,
            whatItIs: override?.whatItIs ?? base.whatItIs,
            whatComesNext: override?.whatComesNext ?? base.whatComesNext
        };
    });
}

/** Why the field exists at all — the opening paragraph of the modal. */
export function documentTypeFieldPurpose(context: DocumentUsageContext): string {
    switch (context) {
        case 'PAYMENT_REQUEST':
            return 'Este campo identifica o tipo de documento que está a anexar ao pedido. '
                + 'A classificação não altera o valor nem o fornecedor — determina o que o Portal irá exigir '
                + 'mais à frente no processo: factura da operação, comprovativo de pagamento, regularização de '
                + 'adiantamento ou revisão do Financeiro.';
        case 'QUOTATION_MANAGEMENT':
            return 'Este campo identifica o tipo de documento que o fornecedor emitiu e que está a anexar à cotação. '
                + 'A classificação acompanha a cotação: se esta for escolhida e originar um pagamento, determina o '
                + 'que o Portal irá exigir mais à frente no processo.';
        case 'POST_PAYMENT_EVIDENCE':
            return 'Este campo identifica o tipo de documento que está a anexar como comprovativo. '
                + 'A classificação determina que obrigações do pedido ficam encerradas e quais permanecem em aberto.';
    }
}

export function isFiscalDocument(value?: string | null): boolean {
    const key = normalizeDocumentType(value);
    return !!key && ALL[key].isFiscal;
}

/**
 * Canonical form. Accepts the two superseded codes so a stale value renders instead of vanishing:
 * `FINAL_INVOICE` was the old binary code, and `FINAL` was the Quotation Wizard's local value.
 */
export function normalizeDocumentType(value?: string | null): SourceDocumentType | null {
    if (!value) return null;
    const upper = value.trim().toUpperCase();
    if (upper === 'FINAL_INVOICE' || upper === 'FINAL') return SOURCE_DOCUMENT_TYPES.INVOICE;
    return (upper in ALL) ? (upper as SourceDocumentType) : null;
}

export function isSelectableDocumentType(value?: string | null): boolean {
    const key = normalizeDocumentType(value);
    return !!key && key !== SOURCE_DOCUMENT_TYPES.UNCLASSIFIED;
}

/** True when the type may originate a payable request — mirrors the backend resolver. */
export function canInitiatePayment(value?: string | null): boolean {
    const key = normalizeDocumentType(value);
    return key === SOURCE_DOCUMENT_TYPES.PROFORMA
        || key === SOURCE_DOCUMENT_TYPES.ADVANCE_INVOICE
        || key === SOURCE_DOCUMENT_TYPES.INVOICE;
}

