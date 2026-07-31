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
    /** Short consequence shown under the option, so the choice is made with eyes open. */
    hint: string;
    isFiscal: boolean;
    /** Rendered with a "revisão do Financeiro" marker. */
    requiresReview: boolean;
    /** Legitimate but atypical in this context — offered, but flagged. */
    unusual?: boolean;
}

const ALL: Record<SourceDocumentType, Omit<DocumentTypeOption, 'value'>> = {
    ESTIMATE: {
        label: 'Orçamento / Cotação',
        hint: 'Documento não fiscal. Não autoriza pagamento.',
        isFiscal: false,
        requiresReview: false
    },
    PROFORMA: {
        label: 'Factura Pró-forma',
        hint: 'Documento não fiscal. Será exigida a factura da operação após o pagamento.',
        isFiscal: false,
        requiresReview: false
    },
    ADVANCE_INVOICE: {
        label: 'Factura de Adiantamento',
        hint: 'Documento fiscal de adiantamento. Exigirá factura da operação, Nota de Crédito e validação do Financeiro.',
        isFiscal: true,
        requiresReview: true
    },
    INVOICE: {
        label: 'Factura',
        hint: 'Documento fiscal da operação. Exigirá comprovativo de pagamento.',
        isFiscal: true,
        requiresReview: false
    },
    INVOICE_RECEIPT: {
        label: 'Factura-Recibo',
        hint: 'Documento fiscal que já comprova o pagamento. Não exige recibo separado.',
        isFiscal: true,
        requiresReview: true
    },
    OTHER: {
        label: 'Outro documento',
        hint: 'Será enviado para revisão do Financeiro antes de prosseguir.',
        isFiscal: false,
        requiresReview: true
    },
    UNCLASSIFIED: {
        label: 'Não classificado',
        hint: 'Selecione o tipo de documento para prosseguir.',
        isFiscal: false,
        requiresReview: true
    }
};

function option(value: SourceDocumentType, unusual = false): DocumentTypeOption {
    return { value, ...ALL[value], unusual };
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

export function documentTypeHint(value?: string | null): string | null {
    const key = normalizeDocumentType(value);
    return key && key !== SOURCE_DOCUMENT_TYPES.UNCLASSIFIED ? ALL[key].hint : null;
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

/**
 * Plain-language preview of what will still be required. Shown next to the field so the requester
 * sees that the choice commits the request to future work, not just labels a file.
 */
export function obligationPreview(value?: string | null): string[] {
    const key = normalizeDocumentType(value);
    if (!key || key === SOURCE_DOCUMENT_TYPES.UNCLASSIFIED) return [];

    const out: string[] = ['Recebimento operacional'];

    switch (key) {
        case SOURCE_DOCUMENT_TYPES.PROFORMA:
        case SOURCE_DOCUMENT_TYPES.ESTIMATE:
            out.push('Factura da operação', 'Recibo/comprovativo de pagamento');
            break;
        case SOURCE_DOCUMENT_TYPES.ADVANCE_INVOICE:
            out.push('Factura da operação', 'Nota de Crédito (regularização do adiantamento)',
                     'Comprovativo de pagamento', 'Validação do Financeiro');
            break;
        case SOURCE_DOCUMENT_TYPES.INVOICE:
            out.push('Recibo/comprovativo de pagamento');
            break;
        case SOURCE_DOCUMENT_TYPES.INVOICE_RECEIPT:
            // Nothing further: the document already evidences the operation and its payment.
            break;
        case SOURCE_DOCUMENT_TYPES.OTHER:
            out.push('Revisão do Financeiro');
            break;
    }

    return out;
}
