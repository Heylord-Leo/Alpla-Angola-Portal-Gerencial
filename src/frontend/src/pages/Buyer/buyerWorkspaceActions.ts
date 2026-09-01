// ─────────────────────────────────────────────────────────────────────────────
// Buyer Workspace header-action derivation. The server (BuyerQueueProjectionBuilder) is the single
// source of truth and may return MORE THAN ONE actionable next-action — notably PartialCoverage returns
// both ADD_QUOTATION and SUBMIT_BATCH. The header must expose each independently, with NO ordering
// dependency and NO frontend eligibility recomputation. Pure + node-vitest friendly.
// ─────────────────────────────────────────────────────────────────────────────

export interface WorkspaceNextAction {
    code: string;
    actionable: boolean;
    label?: string;
}

export interface WorkspaceHeaderActions {
    hasSubmitBatch: boolean;
    hasAddQuotation: boolean;
    hasResolveAdjustment: boolean;
    submitBatchLabel?: string;
    addQuotationLabel?: string;
    resolveAdjustmentLabel?: string;
}

/** Derive which header actions are available from the server's next-action list — by code + actionable
 * flag only (position-independent). RESOLVE_ADJUSTMENT is reported so the caller can keep it as the
 * special/primary flow; the server already suppresses SUBMIT_BATCH/ADD_QUOTATION while it is present. */
export function deriveWorkspaceHeaderActions(nextActions: WorkspaceNextAction[] | null | undefined): WorkspaceHeaderActions {
    const list = nextActions || [];
    const find = (code: string) => list.find(a => a.actionable && a.code === code);
    const submit = find('SUBMIT_BATCH');
    const addQuotation = find('ADD_QUOTATION');
    const resolveAdjustment = find('RESOLVE_ADJUSTMENT');
    return {
        hasSubmitBatch: !!submit,
        hasAddQuotation: !!addQuotation,
        hasResolveAdjustment: !!resolveAdjustment,
        submitBatchLabel: submit?.label,
        addQuotationLabel: addQuotation?.label,
        resolveAdjustmentLabel: resolveAdjustment?.label,
    };
}
