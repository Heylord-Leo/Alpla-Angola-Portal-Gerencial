import { api } from './api';

/**
 * Shared logic for completing a quotation transition.
 * Encapsulates mandatory checks before calling the backend.
 */
export async function completeQuotationAction(
    requestId: string,
    hasProforma: boolean,
    hasSupplier: boolean,
    itemCount: number,
    comment: string = ''
) {
    if (itemCount === 0) {
        throw new Error('O pedido deve conter pelo menos uma linha de item para ser concluído.');
    }

    if (!hasSupplier) {
        throw new Error('É necessário selecionar um fornecedor antes de concluir a cotação.');
    }

    if (!hasProforma) {
        throw new Error('É necessário anexar a Proforma antes de concluir a cotação.');
    }

    // 2. Execute API Call
    return await api.requests.completeQuotation(requestId, comment);
}
