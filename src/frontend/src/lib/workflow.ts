import { api } from './api';

/**
 * Shared logic for completing a quotation transition.
 * Encapsulates mandatory checks before calling the backend.
 */
export async function completeQuotationAction(
    requestId: string,
    itemCount: number,
    comment: string = ''
) {
    if (itemCount === 0) {
        throw new Error('O pedido deve conter pelo menos uma linha de item (via cotação vinculada ou itens diretos) para ser concluído.');
    }

    // 2. Execute API Call
    return await api.requests.completeQuotation(requestId, comment);
}
