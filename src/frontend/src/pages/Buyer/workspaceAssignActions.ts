/**
 * "Assumir pedido" in the Buyer Request Workspace — framework-free decision + orchestration, unit-tested.
 *
 * The decision mirrors the backend rules of `POST /api/v1/requests/{id}/assign-buyer` (RequestsController.AssignBuyer)
 * for QUOTATION requests, which stay the authority:
 * - self-assignment (no targetUserId) requires the Buyer ROLE — a System Administrator or Local Manager without it is
 *   refused with 403, whatever their other privileges;
 * - a request already assigned to someone else can be taken over ONLY by a coordinator (System Administrator or Local
 *   Manager); an ordinary Buyer is refused with 409 ("O reencaminhamento só é permitido por coordenadores");
 * - the access scope (company/plant/department) is enforced server-side (404 outside it).
 * The classic list shows "Assumir Pedido" to every non-owner and lets the server refuse; the Workspace shows the action
 * only when the server would accept it, and asks for confirmation before a takeover.
 *
 * PAYMENT requests are deliberately excluded: their ownership is the correction GROUP's PoResponsibleBuyerId (claim and
 * coordinator transfer rules of §G.20.1), handled by the queue row's "Atribuir a Mim" — never request-level ownership.
 */
export type AssignActionKind = 'CLAIM' | 'TAKEOVER' | 'NONE';

export interface AssignActionDecision {
    kind: AssignActionKind;
    /** Button label when kind !== 'NONE'. */
    label?: string;
    /** Why no action is offered (diagnostic; also shown as a title where useful). */
    reason: string;
}

export interface AssignRoleNames { buyer: string; systemAdministrator: string; localManager: string; }

export const TERMINAL_REQUEST_STATUSES: readonly string[] = ['CANCELLED', 'REJECTED', 'COMPLETED'];

export function resolveAssignAction(
    ws: { requestTypeCode?: string | null; requestStatusCode?: string | null; buyerId?: string | null },
    user: { id?: string | null; roles?: readonly string[] | null } | null | undefined,
    roleNames: AssignRoleNames,
): AssignActionDecision {
    if (!user?.id || !user.roles) return { kind: 'NONE', reason: 'Utilizador não autenticado.' };
    if (ws.requestTypeCode !== 'QUOTATION') {
        return { kind: 'NONE', reason: 'Pedidos de pagamento têm a responsabilidade por grupo de correção de P.O (fila de cotações → "Atribuir a Mim"), não por pedido.' };
    }
    if (!ws.requestStatusCode || TERMINAL_REQUEST_STATUSES.includes(ws.requestStatusCode)) {
        return { kind: 'NONE', reason: 'Pedido encerrado: a atribuição de comprador já não se aplica.' };
    }
    if (ws.buyerId && ws.buyerId === user.id) return { kind: 'NONE', reason: 'Este pedido já está atribuído a si.' };
    if (!user.roles.includes(roleNames.buyer)) {
        return { kind: 'NONE', reason: 'Apenas o papel de Comprador pode assumir a responsabilidade por um pedido (regra do servidor, também para administradores).' };
    }
    if (!ws.buyerId) return { kind: 'CLAIM', label: 'Assumir pedido', reason: 'Pedido sem comprador atribuído.' };
    const isCoordinator = user.roles.includes(roleNames.systemAdministrator) || user.roles.includes(roleNames.localManager);
    if (isCoordinator) {
        return { kind: 'TAKEOVER', label: 'Assumir pedido de outro comprador', reason: 'Coordenador (Administrador do Sistema / Gestor Local) com o papel de Comprador.' };
    }
    return { kind: 'NONE', reason: 'Pedido atribuído a outro comprador: o reencaminhamento só é permitido por coordenadores (regra do servidor).' };
}

export interface TakeoverConfirmation { title: string; lines: string[]; }

/** Confirmation shown before a takeover: names the request and the current Buyer, and states the real effects. */
export function buildTakeoverConfirmation(ws: { requestNumber: string; title?: string | null; buyerName?: string | null }): TakeoverConfirmation {
    return {
        title: 'Assumir pedido de outro comprador',
        lines: [
            `Assumir o pedido ${ws.requestNumber}${ws.title ? ` — ${ws.title}` : ''}?`,
            `Comprador atual: ${ws.buyerName || 'não identificado'}.`,
            'A responsabilidade passa para si: as ações de cotação (Editar/Excluir, novas cotações e envio para aprovação) deixam de estar disponíveis ao comprador atual.',
            'A alteração fica registada no histórico do pedido (COMPRADOR_ATRIBUIDO) e o requisitante é notificado, como na ferramenta clássica.',
        ],
    };
}

export interface AssignRequestDeps {
    assignBuyer: (requestId: string) => Promise<unknown>;
    isBusy: () => boolean;
    setBusy: (busy: boolean) => void;
    onSuccess: (message: string) => void;
    onError: (message: string) => void;
}

export type AssignRequestOutcome = 'ASSIGNED' | 'SKIPPED_BUSY' | 'FAILED';

/** One submission at a time; the server's refusal text (403/404/409) is surfaced verbatim. Self-assignment only (no targetUserId). */
export async function runAssignRequest(deps: AssignRequestDeps, requestId: string, kind: Exclude<AssignActionKind, 'NONE'>): Promise<AssignRequestOutcome> {
    if (deps.isBusy()) return 'SKIPPED_BUSY';
    deps.setBusy(true);
    try {
        await deps.assignBuyer(requestId);
        deps.onSuccess(kind === 'TAKEOVER' ? 'Pedido assumido: a responsabilidade passou para si.' : 'Pedido atribuído a si com sucesso.');
        return 'ASSIGNED';
    } catch (e: any) {
        deps.onError(e?.message || 'Não foi possível assumir o pedido. Tente novamente.');
        return 'FAILED';
    } finally {
        deps.setBusy(false);
    }
}
