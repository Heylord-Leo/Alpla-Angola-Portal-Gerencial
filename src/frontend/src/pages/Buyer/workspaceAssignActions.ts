/**
 * "Assumir pedido" in the Buyer Request Workspace and the classic quotation list — framework-free decision +
 * orchestration, unit-tested. The server remains the authority; the UI only hides what the server would refuse.
 *
 * Backend rules mirrored (docs/BUYER_WORKSPACE_QUOTATION_ACTIONS.md §6/§8):
 * - CLAIM (`POST …/assign-buyer`, no body): unassigned QUOTATION request, actor holds the Buyer ROLE — a System
 *   Administrator or Local Manager without it is refused (403), whatever their other privileges;
 * - TAKEOVER (`POST …/take-over`): QUOTATION request assigned to ANOTHER Buyer, actor holds the Buyer role (any
 *   Buyer — coordinator privileges are neither required nor sufficient), non-terminal status, mandatory trimmed
 *   justification ≤ 500 characters, and the owner the actor confirmed (`expectedBuyerId`) — a stale confirmation is
 *   refused (409 TAKEOVER_STALE / TAKEOVER_CONCURRENT) and never overwrites a newer owner;
 * - access scope (company/plant/department) is enforced server-side (404 outside it);
 * - PAYMENT requests are excluded: their ownership is the correction GROUP's PoResponsibleBuyerId (queue row
 *   "Atribuir a Mim"), never request-level ownership.
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

/** Statuses in which neither claim nor takeover applies (mirrors BuyerTakeOverPolicy.TerminalStatuses). */
export const TERMINAL_REQUEST_STATUSES: readonly string[] = ['CANCELLED', 'REJECTED', 'COMPLETED', 'PAYMENT_COMPLETED'];

export const TAKEOVER_JUSTIFICATION_MAX_LENGTH = 500;

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
        return { kind: 'NONE', reason: 'Apenas o papel de Comprador pode assumir a responsabilidade por um pedido (regra do servidor, também para administradores e gestores locais).' };
    }
    if (!ws.buyerId) return { kind: 'CLAIM', label: 'Assumir pedido', reason: 'Pedido sem comprador atribuído.' };
    return { kind: 'TAKEOVER', label: 'Assumir pedido de outro comprador', reason: 'Comprador: pode assumir o pedido de outro comprador com justificação; o comprador anterior, o requisitante e os participantes são notificados.' };
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
            'A transferência fica registada no histórico do pedido (COMPRADOR_TRANSFERIDO) e são enviados e-mails ao comprador anterior, a si, ao requisitante e aos participantes do pedido (aprovadores, P.O, finanças e receção envolvidos).',
            'Se outra pessoa assumir o pedido entretanto, esta confirmação é recusada e nada é alterado.',
        ],
    };
}

export interface JustificationValidation { ok: boolean; message?: string; }

/** Mirrors the server rule: trimmed, non-empty, at most TAKEOVER_JUSTIFICATION_MAX_LENGTH characters. */
export function validateTakeoverJustification(raw: string): JustificationValidation {
    const trimmed = (raw ?? '').trim();
    if (!trimmed) return { ok: false, message: 'Indique a justificação para assumir o pedido de outro comprador.' };
    if (trimmed.length > TAKEOVER_JUSTIFICATION_MAX_LENGTH) return { ok: false, message: `A justificação não pode exceder ${TAKEOVER_JUSTIFICATION_MAX_LENGTH} caracteres.` };
    return { ok: true };
}

export interface TakeOverPayload { justification: string; expectedBuyerId: string; }

export interface AssignRequestDeps {
    assignBuyer: (requestId: string) => Promise<unknown>;
    takeOver: (requestId: string, payload: TakeOverPayload) => Promise<unknown>;
    isBusy: () => boolean;
    setBusy: (busy: boolean) => void;
    onSuccess: (message: string) => void;
    onError: (message: string) => void;
}

export type AssignRequestOutcome = 'ASSIGNED' | 'SKIPPED_BUSY' | 'FAILED' | 'INVALID';

/**
 * One submission at a time; the server's refusal text (400/403/404/409) is surfaced verbatim. CLAIM posts no body;
 * TAKEOVER posts the trimmed justification and the owner the actor confirmed (`expectedBuyerId`, never omitted).
 */
export async function runAssignRequest(
    deps: AssignRequestDeps,
    requestId: string,
    kind: Exclude<AssignActionKind, 'NONE'>,
    takeover?: { justification: string; expectedBuyerId?: string | null },
): Promise<AssignRequestOutcome> {
    if (kind === 'TAKEOVER') {
        const v = validateTakeoverJustification(takeover?.justification ?? '');
        if (!v.ok) { deps.onError(v.message!); return 'INVALID'; }
        if (!takeover?.expectedBuyerId) { deps.onError('O pedido não tem comprador atribuído: use "Assumir pedido" (atribuição). Atualize a página.'); return 'INVALID'; }
    }
    if (deps.isBusy()) return 'SKIPPED_BUSY';
    deps.setBusy(true);
    try {
        if (kind === 'TAKEOVER') {
            await deps.takeOver(requestId, { justification: takeover!.justification.trim(), expectedBuyerId: takeover!.expectedBuyerId! });
            deps.onSuccess('Pedido assumido: a responsabilidade passou para si. O comprador anterior, o requisitante e os participantes do pedido serão notificados.');
        } else {
            await deps.assignBuyer(requestId);
            deps.onSuccess('Pedido atribuído a si com sucesso.');
        }
        return 'ASSIGNED';
    } catch (e: any) {
        deps.onError(e?.message || 'Não foi possível assumir o pedido. Tente novamente.');
        return 'FAILED';
    } finally {
        deps.setBusy(false);
    }
}
