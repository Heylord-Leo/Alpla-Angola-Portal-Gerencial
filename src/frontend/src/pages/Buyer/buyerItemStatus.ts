// Per-item status derivation for the "Itens Solicitados no Pedido" table in the
// Buyer workspace. Combines RequestLineItem.QuotationLifecycleStatus with the
// request's approvalBatches and saved quotations so the buyer can see at a
// glance where each individual item stands in the partial/batch approval flow.
import { isQuotationItemSelectableForApproval } from './batchEligibility';

export const BATCH_STATUS_LABELS: Record<string, string> = {
    'WAITING_AREA_APPROVAL': 'Aguardando Aprovação da Área',
    'AREA_ADJUSTMENT': 'Ajuste da Área Requerido',
    'WAITING_FINAL_APPROVAL': 'Aguardando Aprovação Final',
    'FINAL_ADJUSTMENT': 'Ajuste Final Requerido',
    'APPROVED': 'Aprovado',
    'REJECTED': 'Rejeitado',
    'CANCELLED': 'Cancelado',
};

const ACTIVE_OR_APPROVED_BATCH_STATUSES = [
    'WAITING_AREA_APPROVAL',
    'AREA_ADJUSTMENT',
    'WAITING_FINAL_APPROVAL',
    'FINAL_ADJUSTMENT',
    'APPROVED',
];

export interface BuyerItemStatusBadge {
    label: string;
    /** Longer explanation shown as a tooltip (title attribute). */
    hint?: string;
    bg: string;
    text: string;
    border: string;
}

/**
 * Derives the display status of a single request line item for the buyer.
 * `item` may be a raw list item (id in `lineItemId`) or a normalized one (`id`).
 * `group` is the grouped request built by groupItemsByRequest (needs
 * `approvalBatches` and `quotations`).
 */
export function getBuyerItemStatus(item: any, group: any): BuyerItemStatusBadge {
    const itemId = item.lineItemId || item.id || item.requestLineItemId;
    const lifecycle = item.quotationLifecycleStatus;

    if (item.lineItemStatusCode === 'CANCELLED' || item.lineItemStatusCode === 'DELETED') {
        return { label: 'Cancelado', bg: '#f3f4f6', text: '#6b7280', border: '#e5e7eb' };
    }

    if (lifecycle === 'BATCH_ASSIGNED' || lifecycle === 'QUOTATION_APPROVED') {
        const batch = group?.approvalBatches?.find((b: any) =>
            ACTIVE_OR_APPROVED_BATCH_STATUSES.includes(b.status) &&
            b.items?.some((bi: any) => bi.requestLineItemId === itemId)
        );
        if (lifecycle === 'QUOTATION_APPROVED') {
            return {
                label: batch ? `Aprovado (Lote #${batch.batchNumber})` : 'Aprovado',
                bg: '#f0fdf4', text: '#15803d', border: '#bbf7d0'
            };
        }
        const batchStatusLabel = batch ? (BATCH_STATUS_LABELS[batch.status] || batch.status) : 'Em aprovação';
        return {
            label: batch ? `Lote #${batch.batchNumber} — ${batchStatusLabel}` : 'Em aprovação',
            bg: '#eff6ff', text: '#1d4ed8', border: '#bfdbfe'
        };
    }

    if (lifecycle === 'NOT_QUOTED_PROPOSED') {
        return {
            label: 'Aguardando decisão de não cotado',
            hint: 'A decisão cabe ao requisitante/aprovador de área — nenhuma ação do comprador é necessária neste momento.',
            bg: '#fef3c7', text: '#92400e', border: '#fde68a'
        };
    }

    if (lifecycle === 'NOT_QUOTED_ACCEPTED') {
        return {
            label: 'Não cotado aceito',
            bg: '#f3f4f6', text: '#6b7280', border: '#e5e7eb'
        };
    }

    if (lifecycle === 'CLOSED_NOT_QUOTED') {
        return {
            label: 'Encerrado sem cotação',
            hint: item.notQuotedJustification || 'Item encerrado sem cotação pelo comprador. Detalhes no histórico do pedido.',
            bg: '#f3f4f6', text: '#6b7280', border: '#e5e7eb'
        };
    }

    // null / QUOTATION_PENDING: eligible for quotation — distinguish "already
    // quoted, waiting to be sent in a batch" from "no valid quotation yet".
    const hasCandidate = (group?.quotations || []).some((q: any) =>
        (q.items || []).some((qi: any) =>
            qi.mappedRequestLineItemId === itemId &&
            (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
            isQuotationItemSelectableForApproval(qi.id, group)
        )
    );

    if (hasCandidate) {
        return {
            label: 'Cotado — pronto para envio',
            bg: '#eff6ff', text: '#1d4ed8', border: '#bfdbfe'
        };
    }

    return {
        label: 'Pendente de cotação',
        bg: '#fef2f2', text: '#b91c1c', border: '#fecaca'
    };
}
