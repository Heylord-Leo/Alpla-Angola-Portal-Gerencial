/**
 * Adjustment V2 (Phase 3) — the frontend mirror of the approved structured reason catalog and the
 * pure payload/validation logic for the approver "Solicitar Reajuste" step. Labels match the
 * backend AdjustmentEventLabels catalog (design §2). Raw codes are never shown to users — the UI
 * renders `label` only.
 */

export type AdjustmentReasonOwner = 'BUYER' | 'REQUESTER';

export interface AdjustmentReasonDef {
    code: string;
    label: string;
    owner: AdjustmentReasonOwner;
    /** Reasons that are meaningless without a specific item (design §2 "required"). */
    itemRequired: boolean;
}

/** The closed catalog (order = display order; buyer group first, then requester). */
export const ADJUSTMENT_REASONS: readonly AdjustmentReasonDef[] = [
    // Buyer-owned
    { code: 'PRICE_NEGOTIATION', label: 'Preço / negociação', owner: 'BUYER', itemRequired: false },
    { code: 'NEW_QUOTATION', label: 'Solicitar nova cotação', owner: 'BUYER', itemRequired: false },
    { code: 'SUPPLIER', label: 'Fornecedor', owner: 'BUYER', itemRequired: false },
    { code: 'SUPPLIER_DELIVERY_TIME', label: 'Prazo de entrega do fornecedor', owner: 'BUYER', itemRequired: false },
    { code: 'PAYMENT_TERMS', label: 'Condição de pagamento', owner: 'BUYER', itemRequired: false },
    { code: 'DOCUMENTATION', label: 'Documentação / Proforma', owner: 'BUYER', itemRequired: false },
    { code: 'BATCH_COMPOSITION', label: 'Composição do lote', owner: 'BUYER', itemRequired: false },
    { code: 'EXTRA_QUOTATION_ITEM', label: 'Item adicional da cotação', owner: 'BUYER', itemRequired: false },
    { code: 'OTHER', label: 'Outro', owner: 'BUYER', itemRequired: false },
    // Requester-first
    { code: 'REQUESTED_QUANTITY', label: 'Quantidade solicitada', owner: 'REQUESTER', itemRequired: true },
    { code: 'SPECIFICATION', label: 'Descrição / especificação', owner: 'REQUESTER', itemRequired: true },
    { code: 'REQUESTED_UNIT', label: 'Unidade de medida', owner: 'REQUESTER', itemRequired: true },
    { code: 'NEEDED_BY_DATE', label: 'Data necessária', owner: 'REQUESTER', itemRequired: false },
    { code: 'MISSING_ITEM', label: 'Item faltante no pedido', owner: 'REQUESTER', itemRequired: false },
    { code: 'REMOVE_REQUEST_ITEM', label: 'Remover item do pedido', owner: 'REQUESTER', itemRequired: true },
];

const BY_CODE = new Map(ADJUSTMENT_REASONS.map(r => [r.code, r]));

export function reasonLabel(code: string): string {
    return BY_CODE.get(code)?.label ?? code;
}

export function isItemRequired(code: string): boolean {
    return BY_CODE.get(code)?.itemRequired ?? false;
}

export interface AdjustmentReasonPayload {
    reasonCode: string;
    requestLineItemId?: string | null;
}

export interface AdjustmentSubmitPayload {
    comment: string;
    wholeBatch: boolean;
    reasons: AdjustmentReasonPayload[];
}

/**
 * Maps the approver's raw selection into the backend contract. Item-required reasons are emitted
 * once per selected item; the remaining reasons stay whole-lot (null item). `wholeBatch` is true
 * only when no item-required reason forces item scoping — mirroring the server's validation so the
 * request can never be rejected for an inconsistency the UI allowed.
 */
export function buildAdjustmentReasons(
    selectedCodes: readonly string[],
    selectedItemIds: readonly string[],
): { wholeBatch: boolean; reasons: AdjustmentReasonPayload[] } {
    const usesItems = selectedCodes.some(isItemRequired);
    const reasons: AdjustmentReasonPayload[] = [];
    for (const code of selectedCodes) {
        if (isItemRequired(code)) {
            for (const id of selectedItemIds) reasons.push({ reasonCode: code, requestLineItemId: id });
        } else {
            reasons.push({ reasonCode: code, requestLineItemId: null });
        }
    }
    return { wholeBatch: !usesItems, reasons };
}

// ── Friendly display labels for the read-only batch-details surface (Phase 3, batch details) ──
// Kept in this single frontend adjustment vocabulary module; never expose raw enum codes to users.

/** ApprovalBatch.Status → friendly Portuguese label. */
const BATCH_STATUS_LABELS: Record<string, string> = {
    WAITING_AREA_APPROVAL: 'Aguardando Aprovação da Área',
    AREA_ADJUSTMENT: 'Reajuste solicitado na Aprovação da Área',
    WAITING_FINAL_APPROVAL: 'Aguardando Aprovação Final',
    FINAL_ADJUSTMENT: 'Reajuste solicitado na Aprovação Final',
    APPROVED: 'Aprovado',
    REJECTED: 'Rejeitado',
    CANCELLED: 'Cancelado',
};

export function batchStatusLabel(status: string): string {
    return BATCH_STATUS_LABELS[status] ?? status;
}

/** Adjustment-cycle state → friendly label. */
const CYCLE_STATE_LABELS: Record<string, string> = {
    WAITING_BUYER: 'Aguardando ação do Comprador',
    WAITING_REQUESTER: 'Aguardando ação do Solicitante',
    RESUBMITTED: 'Reenviado para aprovação',
    CANCELLED: 'Cancelado',
};

export function cycleStateLabel(status: string): string {
    return CYCLE_STATE_LABELS[status] ?? status;
}

/** SourceStage → friendly origin label. */
export function sourceStageLabel(stage: string): string {
    if (stage === 'FINAL') return 'Aprovação Final';
    if (stage === 'AREA') return 'Aprovação de Área';
    return stage;
}

/**
 * Business-readable label for one affected request line item in the adjustment picker — never a
 * bare "Item" when a line number / catalog code / description is available, and never a GUID.
 * Format: "#<LineNumber> — <ItemCode> — <Description>", collapsing whatever parts are absent.
 */
export function affectedItemLabel(item: {
    lineNumber?: number | null;
    itemCatalogCode?: string | null;
    description?: string | null;
}): string {
    const parts: string[] = [];
    if (item.lineNumber != null) parts.push(`#${item.lineNumber}`);
    if (item.itemCatalogCode) parts.push(item.itemCatalogCode);
    if (item.description) parts.push(item.description);
    if (parts.length > 0) return parts.join(' — ');
    return item.lineNumber != null ? `Item #${item.lineNumber}` : 'Item';
}

/** The business actor currently responsible, derived from the cycle state (friendly, safe). */
export function cycleResponsibleLabel(status: string): string {
    if (status === 'WAITING_BUYER') return 'Comprador';
    if (status === 'WAITING_REQUESTER') return 'Solicitante';
    return '—';
}

/** Returns a user-facing error string, or null when the selection is valid to submit. */
export function validateAdjustmentSelection(
    selectedCodes: readonly string[],
    selectedItemIds: readonly string[],
    comment: string,
): string | null {
    if (!comment.trim()) return 'O comentário do reajuste é obrigatório.';
    if (selectedCodes.length === 0) return 'Selecione ao menos um motivo para o reajuste.';
    if (selectedCodes.some(isItemRequired) && selectedItemIds.length === 0)
        return 'Selecione ao menos um item para os motivos que exigem item específico.';
    return null;
}
