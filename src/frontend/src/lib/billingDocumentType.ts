/**
 * Post-Payment Completion — billing document classification (Release 2).
 *
 * Single source of truth on the frontend for the two initial billing document types.
 * Mirrors `RequestConstants.BillingDocumentTypes` on the backend.
 *
 * There is deliberately **no default**: a request or quotation stays unclassified until a person
 * chooses. OCR may propose a value, but a proposal is never applied on the user's behalf.
 */

export const BILLING_DOCUMENT_TYPES = {
    PROFORMA: 'PROFORMA',
    FINAL_INVOICE: 'FINAL_INVOICE'
} as const;

export type BillingDocumentType = typeof BILLING_DOCUMENT_TYPES[keyof typeof BILLING_DOCUMENT_TYPES];

/** Options for a `<select>`, in the order the business reads them. */
export const BILLING_DOCUMENT_TYPE_OPTIONS: ReadonlyArray<{ value: BillingDocumentType; label: string }> = [
    { value: BILLING_DOCUMENT_TYPES.PROFORMA, label: 'Fatura Proforma' },
    { value: BILLING_DOCUMENT_TYPES.FINAL_INVOICE, label: 'Fatura Final' }
];

/** Portuguese label, or a neutral placeholder when the value is missing/unknown. */
export function billingDocumentTypeLabel(value?: string | null): string {
    const match = BILLING_DOCUMENT_TYPE_OPTIONS.find(o => o.value === value);
    return match ? match.label : 'Não classificado';
}

export function isBillingDocumentType(value?: string | null): value is BillingDocumentType {
    return value === BILLING_DOCUMENT_TYPES.PROFORMA || value === BILLING_DOCUMENT_TYPES.FINAL_INVOICE;
}

/**
 * Explains the downstream consequence of each choice, shown as inline help so the requester
 * understands they are committing to a later obligation — not just labelling a file.
 */
export function billingDocumentTypeHint(value?: string | null): string | null {
    switch (value) {
        case BILLING_DOCUMENT_TYPES.PROFORMA:
            return 'Será exigida uma Fatura Final após o pagamento.';
        case BILLING_DOCUMENT_TYPES.FINAL_INVOICE:
            return 'Não será exigida outra fatura após o pagamento.';
        default:
            return null;
    }
}

/**
 * Maps the Quotation Wizard's legacy UI value to the canonical domain value.
 * The wizard has used `'FINAL'` since before this feature existed; the domain uses
 * `'FINAL_INVOICE'`. Translating at the API boundary keeps the wizard's own state untouched.
 */
export function toCanonicalBillingDocumentType(value?: string | null): BillingDocumentType | null {
    if (!value) return null;
    const upper = value.trim().toUpperCase();
    if (upper === 'FINAL' || upper === BILLING_DOCUMENT_TYPES.FINAL_INVOICE) {
        return BILLING_DOCUMENT_TYPES.FINAL_INVOICE;
    }
    if (upper === BILLING_DOCUMENT_TYPES.PROFORMA) {
        return BILLING_DOCUMENT_TYPES.PROFORMA;
    }
    return null;
}
