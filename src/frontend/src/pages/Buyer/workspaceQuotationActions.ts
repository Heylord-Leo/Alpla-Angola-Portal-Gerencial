import type { BuyerWorkspaceQuotation } from '../../types/buyerWorkspace';

/**
 * "Cotações & Documentos" actions in the Buyer Workspace (Editar / Excluir) — framework-free so the decisions and the
 * delete orchestration are unit-tested. The backend stays the authority: `PUT/DELETE …/quotations/{id}` enforce the
 * request's quotation window (RequestWorkflowHelper.CanMutateQuotation), the access scope and the approval-batch
 * references (409). The UI mirrors only the window and the Buyer role to decide whether to show the buttons.
 */

/** Mirror of RequestWorkflowHelper.CanMutateQuotation (backend is authoritative). */
export const QUOTATION_MUTABLE_REQUEST_STATUSES: readonly string[] = ['DRAFT', 'WAITING_QUOTATION', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'];

/**
 * Same visibility rule as the classic list's Edit/Delete (`BuyerItemsList`: `canMutateQuotation = window &&
 * group.buyerId === currentUser.id`): the request must be in the quotation window AND assigned to the current user.
 * An unassigned request, another Buyer, a Local Manager or a System Administrator who is not the assigned Buyer see no
 * buttons — exactly as in the classic tool. The Buyer role is additionally required because the Workspace route also
 * admits Local Managers and administrators. Backend authorization is unchanged by this rule.
 */
export function canManageWorkspaceQuotations(
    requestStatusCode: string | null | undefined,
    roles: readonly string[] | null | undefined,
    buyerRole: string,
    requestBuyerId: string | null | undefined,
    currentUserId: string | null | undefined,
): boolean {
    if (!requestStatusCode || !roles || !roles.includes(buyerRole)) return false;
    if (!requestBuyerId || !currentUserId || requestBuyerId !== currentUserId) return false;
    return QUOTATION_MUTABLE_REQUEST_STATUSES.includes(requestStatusCode);
}

/** Message for a reload failure AFTER a successful mutation: distinct from a mutation failure, never a retry prompt. */
export const REFRESH_AFTER_MUTATION_FAILED =
    'A ação foi concluída com sucesso, mas não foi possível atualizar o workspace. Atualize a página para ver o estado atual; não repita a ação.';

export function formatQuotationDate(iso?: string | null): string {
    if (!iso) return 'sem data';
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? 'sem data' : d.toLocaleDateString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

/** "Fornecedor · Doc N · dd/mm/yyyy" — the identification shown in button titles and the confirmation. */
export function describeWorkspaceQuotation(q: Pick<BuyerWorkspaceQuotation, 'supplierName' | 'documentNumber' | 'documentDate'>): string {
    return `${q.supplierName || 'Fornecedor não identificado'} · Doc ${q.documentNumber || 'S/N'} · ${formatQuotationDate(q.documentDate)}`;
}

export interface DeleteQuotationConfirmation { title: string; lines: string[]; }

/**
 * Explains what DELETE …/quotations/{id} actually does: removes the structured quotation and all its lines
 * (hard delete), marks the linked document as removed (soft delete + "DOCUMENTO_REMOVIDO" history) when one is
 * linked, writes a "QUOTATION_DELETED" history row, and is refused when the quotation is referenced by an
 * approval batch or the request is outside the quotation window.
 */
export function buildDeleteQuotationConfirmation(q: BuyerWorkspaceQuotation): DeleteQuotationConfirmation {
    const lines = [
        `Excluir a cotação ${describeWorkspaceQuotation(q)}?`,
        `A cotação estruturada e as suas ${q.itemsQuotedCount} linha(s) serão removidas definitivamente; esta ação não pode ser anulada.`,
    ];
    if (q.documentCount > 0) {
        lines.push('O documento associado (proforma/cotação) será marcado como removido automaticamente e deixará de aparecer nos documentos do pedido.');
    } else {
        lines.push('Esta cotação não tem documento associado; apenas o registo estruturado será removido.');
    }
    lines.push('A exclusão é recusada se a cotação já foi utilizada num lote de aprovação ou se o pedido já saiu da fase de cotação. A ação fica registada no histórico do pedido.');
    if (q.isSelected) lines.push('Atenção: esta cotação está marcada como selecionada.');
    return { title: 'Excluir cotação', lines };
}

export interface DeleteQuotationDeps {
    deleteQuotation: (requestId: string, quotationId: string) => Promise<void>;
    isBusy: () => boolean;
    setBusy: (busy: boolean) => void;
    onSuccess: (message: string) => void;
    onError: (message: string) => void;
}

export type DeleteQuotationOutcome = 'DELETED' | 'SKIPPED_BUSY' | 'FAILED';

/** One submission at a time; the server's refusal text (409/404) is surfaced verbatim. */
export async function runDeleteQuotation(deps: DeleteQuotationDeps, requestId: string, q: Pick<BuyerWorkspaceQuotation, 'id' | 'supplierName' | 'documentNumber' | 'documentDate'>): Promise<DeleteQuotationOutcome> {
    if (deps.isBusy()) return 'SKIPPED_BUSY';
    deps.setBusy(true);
    try {
        await deps.deleteQuotation(requestId, q.id);
        deps.onSuccess(`Cotação excluída: ${describeWorkspaceQuotation(q)}.`);
        return 'DELETED';
    } catch (e: any) {
        deps.onError(e?.message || 'Não foi possível excluir a cotação. Tente novamente.');
        return 'FAILED';
    } finally {
        deps.setBusy(false);
    }
}

export interface QuotationActionState { editable: boolean; deletable: boolean; locked: boolean; lockLabel?: string; lockReason?: string; }

/**
 * §7 — per-quotation state for Editar/Excluir. `canManage` is the request-level rule (window + assigned Buyer); the
 * per-quotation lock is the SERVER's decision (`isSubmittedForApproval`), never recomputed client-side, so classic and
 * Workspace agree with PUT/DELETE. A locked quotation shows the lock with the server reason instead of the buttons.
 */
export function resolveQuotationActionState(q: Pick<BuyerWorkspaceQuotation, 'isSubmittedForApproval' | 'submittedBatchNumber' | 'submissionLockReason'>, canManage: boolean): QuotationActionState {
    if (!canManage) return { editable: false, deletable: false, locked: false };
    if (q.isSubmittedForApproval) {
        return {
            editable: false, deletable: false, locked: true,
            lockLabel: q.submittedBatchNumber != null ? `Submetida (Lote #${q.submittedBatchNumber})` : 'Submetida para aprovação',
            lockReason: q.submissionLockReason || 'Cotação já submetida para aprovação: edição e exclusão diretas bloqueadas; use a revisão/reajuste.',
        };
    }
    return { editable: true, deletable: true, locked: false };
}
