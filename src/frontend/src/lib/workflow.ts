import { api } from './api';

/**
 * Shared logic for completing a quotation transition.
 * Encapsulates mandatory checks before calling the backend.
 * 
 * Returns the API response directly. The caller is responsible for
 * checking if the response has `integrityCheckFailed: true` and
 * showing the Financial Integrity Modal accordingly.
 */
export async function completeQuotationAction(
    requestId: string,
    itemCount: number,
    comment: string = '',
    overridePayload?: { financialIntegrityOverride: boolean; overrideJustification: string }
) {
    if (itemCount === 0) {
        throw new Error('O pedido deve conter pelo menos uma linha de item (via cotação vinculada ou itens diretos) para ser concluído.');
    }

    // Build payload
    const payload: { comment?: string; financialIntegrityOverride?: boolean; overrideJustification?: string } = { comment };
    if (overridePayload) {
        payload.financialIntegrityOverride = overridePayload.financialIntegrityOverride;
        payload.overrideJustification = overridePayload.overrideJustification;
    }

    // Execute API Call
    return await api.requests.completeQuotation(requestId, payload);
}
