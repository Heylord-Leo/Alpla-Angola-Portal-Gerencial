// ─────────────────────────────────────────────────────────────────────────────
// Phase 1 P.O. group cancellation / return to quotation (docs/PO_GROUP_CANCELLATION.md) — pure,
// framework-free labels, validation and wording shared by the Buyer dialog, the Workspace "Grupos P.O."
// tab, the Finance confirmation panel and the request page. Mirrors PoGroupCancellationConstants.cs
// (labels are copied verbatim; the backend remains the authority for every rule).
//
// Wording rule: the Portal NEVER claims it cancelled or verified an ECF in Primavera. Every sentence about
// the external document is "o comprador declara" / "Finanças confirma".
// ─────────────────────────────────────────────────────────────────────────────
import type { LastFinanceReturnDto, PriorEcfConfirmationDto } from '../types';

export const MIN_JUSTIFICATION_LENGTH = 20;
export const MAX_JUSTIFICATION_LENGTH = 1000;
export const MAX_ECF_REFERENCE_LENGTH = 100;

// ── Kinds ──
export const CANCELLATION_KIND_LABELS: Record<string, string> = {
    RESTART_QUOTATION: 'Devolver itens à cotação',
    CANCEL_ITEMS: 'Encerrar itens (cancelar a necessidade)',
};
export const CANCELLATION_KIND_EXPLANATIONS: Record<string, string> = {
    RESTART_QUOTATION: 'Os itens voltam à fase de cotação. Será necessário um NOVO lote e NOVAS aprovações de área e final antes de uma nova P.O.',
    CANCEL_ITEMS: 'Os itens ficam encerrados após a aprovação: nada será comprado para eles neste pedido.',
};
export function cancellationKindLabel(kind?: string | null): string {
    return (kind && CANCELLATION_KIND_LABELS[kind]) || kind || '—';
}

// ── Reason codes (D10) — labels mirror PoGroupCancellationConstants.ReasonCodes.Labels ──
export const CANCELLATION_REASON_LABELS: Record<string, string> = {
    SUPPLIER_CANCELLED: 'Fornecedor cancelou / não consegue fornecer',
    ORDER_CANCELLED_BY_COMPANY: 'Necessidade cancelada pela empresa',
    PRICE_OR_TERMS_CHANGED: 'Preço ou condições alterados — nova cotação necessária',
    WRONG_SUPPLIER_OR_ITEM: 'Fornecedor ou item errado na aprovação',
    DUPLICATE_ORDER: 'Encomenda duplicada',
    FINANCE_RETURN: 'Devolvido por Finanças sem correção possível',
    OTHER: 'Outro (explicar)',
};
export const CANCELLATION_REASON_CODES = Object.keys(CANCELLATION_REASON_LABELS);
export function cancellationReasonLabel(code?: string | null): string {
    return (code && CANCELLATION_REASON_LABELS[code]) || code || '—';
}

// ── ECF statuses — the Buyer's HUMAN declaration, never a Portal verification ──
export const ECF_STATUS_LABELS: Record<string, string> = {
    NOT_APPLICABLE: 'Não aplicável (nunca houve P.O. registada)',
    PENDING_EXTERNAL: 'O comprador declara que a ECF ainda não foi anulada',
    DECLARED_CANCELLED: 'O comprador declara que a ECF já foi anulada fora do Portal',
    UNKNOWN: 'O comprador declara não saber o estado da ECF',
};
/** Short option labels for the declaration radio. */
export const ECF_STATUS_OPTION_LABELS: Record<string, string> = {
    PENDING_EXTERNAL: 'Ainda não foi anulada',
    DECLARED_CANCELLED: 'Já foi anulada fora do Portal',
    UNKNOWN: 'Não sei',
};
export const ECF_DECLARABLE_STATUSES = ['PENDING_EXTERNAL', 'DECLARED_CANCELLED', 'UNKNOWN'];
export function ecfStatusLabel(status?: string | null): string {
    return (status && ECF_STATUS_LABELS[status]) || status || '—';
}

// ── Finance confirmation statuses (D4/D12) ──
export const FINANCE_CONFIRMATION_STATUS_LABELS: Record<string, string> = {
    NOT_REQUIRED: 'Confirmação de Finanças não necessária',
    PENDING: 'Aguarda confirmação de Finanças',
    CONFIRMED: 'Anulação confirmada por Finanças',
    REJECTED: 'Declaração rejeitada por Finanças',
};
export function financeStatusLabel(status?: string | null): string {
    return (status && FINANCE_CONFIRMATION_STATUS_LABELS[status]) || status || '—';
}

// ── Finance return kinds (RequestPoGroup.LastFinanceReturnKind; NULL = legacy, never inferred) ──
export const FINANCE_RETURN_KIND_LABELS: Record<string, string> = {
    PO_CORRECTION: 'Corrigir P.O.',
    ORDER_CANCELLATION_REQUESTED: 'Cancelamento solicitado — não corrigir',
};
export const FINANCE_RETURN_KIND_UNCLASSIFIED_LABEL = 'Tipo não classificado (devolução anterior à versão)';
export function financeReturnKindLabel(kind?: string | null): string {
    return (kind && FINANCE_RETURN_KIND_LABELS[kind]) || FINANCE_RETURN_KIND_UNCLASSIFIED_LABEL;
}

// ── Finance return reason codes (optional structured reason; free text stays mandatory) ──
export const FINANCE_RETURN_REASON_LABELS: Record<string, string> = {
    WRONG_AMOUNT: 'Valor da P.O incorreto',
    WRONG_SUPPLIER: 'Fornecedor incorreto',
    DOCUMENT_ILLEGIBLE: 'Documento ilegível / incompleto',
    PAYMENT_CONDITIONS: 'Condições de pagamento a corrigir',
    ORDER_CANCELLED: 'Pedido/encomenda cancelado',
    SUPPLIER_CANCELLED: 'Fornecedor cancelou',
    OTHER: 'Outro',
};
export const FINANCE_RETURN_REASON_CODES = Object.keys(FINANCE_RETURN_REASON_LABELS);
export function financeReturnReasonLabel(code?: string | null): string {
    return (code && FINANCE_RETURN_REASON_LABELS[code]) || code || '—';
}

// ── History actions written by the feature (RequestStatusHistory.ActionTaken) ──
export const PO_CANCELLATION_HISTORY_LABELS: Record<string, string> = {
    PO_GROUP_CANCELLED: 'P.O. cancelada',
    PO_GROUP_ITEMS_RELEASED: 'Itens devolvidos à cotação',
    PO_GROUP_ITEMS_CLOSED: 'Itens encerrados após aprovação',
    PO_CANCELLATION_ECF_DECLARED: 'Declaração do comprador sobre a ECF',
    PO_CANCELLATION_CONFIRMED: 'Anulação da ECF confirmada por Finanças',
    PO_CANCELLATION_REJECTED: 'Declaração da ECF rejeitada por Finanças',
    PO_CANCELLATION_NOTIFICATION_RESENT: 'Notificação de cancelamento reenviada',
    PO_NUMBER_REUSED_AFTER_CANCELLATION: 'Número de P.O. reutilizado após cancelamento',
};

/** Group status label for a CANCELLED group (the shared GROUP_STATUS_LABELS map has no entry for it). */
export const CANCELLED_GROUP_STATUS_LABEL = 'Cancelado';
export const CANCELLED_GROUP_CHIP_LABEL = 'P.O. cancelada';

// ── Form model + validation (client mirror of the write rules; the server re-validates) ──
export interface CancelPoGroupForm {
    kind: string;
    reasonCode: string;
    reason: string;
    ecfStatus: string;
    ecfReference: string;
    ecfDeclarationNote: string;
}

export const EMPTY_CANCEL_FORM: CancelPoGroupForm = { kind: '', reasonCode: '', reason: '', ecfStatus: '', ecfReference: '', ecfDeclarationNote: '' };

export interface CancelFormError { field: keyof CancelPoGroupForm; message: string; }

export const CANCEL_FORM_MESSAGES = {
    kindRequired: 'Escolha o que acontece aos itens (devolver à cotação ou encerrar).',
    reasonCodeRequired: 'Escolha o motivo do cancelamento.',
    reasonTooShort: `A explicação é obrigatória (mínimo ${MIN_JUSTIFICATION_LENGTH} caracteres).`,
    reasonTooLong: `A explicação não pode exceder ${MAX_JUSTIFICATION_LENGTH} caracteres.`,
    ecfStatusRequired: 'Declare o estado da ECF (este grupo já teve P.O. registada).',
    ecfReferenceRequired: 'Indique a referência da ECF anulada (ex.: ECF10 2026/364).',
    ecfReferenceTooLong: `A referência da ECF não pode exceder ${MAX_ECF_REFERENCE_LENGTH} caracteres.`,
} as const;

export function validateCancelPoGroupForm(form: CancelPoGroupForm, opts: { financeConfirmationWouldBeRequired: boolean }): CancelFormError[] {
    const errors: CancelFormError[] = [];
    if (form.kind !== 'RESTART_QUOTATION' && form.kind !== 'CANCEL_ITEMS') errors.push({ field: 'kind', message: CANCEL_FORM_MESSAGES.kindRequired });
    if (!form.reasonCode || !CANCELLATION_REASON_LABELS[form.reasonCode]) errors.push({ field: 'reasonCode', message: CANCEL_FORM_MESSAGES.reasonCodeRequired });
    const reason = (form.reason ?? '').trim();
    if (reason.length < MIN_JUSTIFICATION_LENGTH) errors.push({ field: 'reason', message: CANCEL_FORM_MESSAGES.reasonTooShort });
    else if (reason.length > MAX_JUSTIFICATION_LENGTH) errors.push({ field: 'reason', message: CANCEL_FORM_MESSAGES.reasonTooLong });
    if (opts.financeConfirmationWouldBeRequired) {
        errors.push(...validateEcfDeclaration({ ecfStatus: form.ecfStatus, ecfReference: form.ecfReference }));
    }
    return errors;
}

/** Validation of the ECF declaration alone (shared by the cancel dialog and the "Atualizar declaração ECF" dialog). */
export function validateEcfDeclaration(decl: { ecfStatus: string; ecfReference: string }): CancelFormError[] {
    const errors: CancelFormError[] = [];
    if (!ECF_DECLARABLE_STATUSES.includes(decl.ecfStatus)) {
        errors.push({ field: 'ecfStatus', message: CANCEL_FORM_MESSAGES.ecfStatusRequired });
        return errors;
    }
    const reference = (decl.ecfReference ?? '').trim();
    if (decl.ecfStatus === 'DECLARED_CANCELLED' && reference.length === 0) errors.push({ field: 'ecfReference', message: CANCEL_FORM_MESSAGES.ecfReferenceRequired });
    else if (reference.length > MAX_ECF_REFERENCE_LENGTH) errors.push({ field: 'ecfReference', message: CANCEL_FORM_MESSAGES.ecfReferenceTooLong });
    return errors;
}

// ── Consequences shown before confirmation (what the server WILL do; never "did") ──
export interface CancellationConsequenceGroup {
    purchaseOrderNumber?: string | null;
    paymentConditionCode?: string | null;
    financeConfirmationWouldBeRequired: boolean;
    lineNumbers?: number[];
}

export function describeCancellationConsequences(form: Pick<CancelPoGroupForm, 'kind'>, group: CancellationConsequenceGroup): string[] {
    const lines: string[] = [];
    const items = group.lineNumbers && group.lineNumbers.length > 0 ? ` (${group.lineNumbers.map(n => `#${n}`).join(', ')})` : '';
    if (form.kind === 'CANCEL_ITEMS') lines.push(`Os itens deste grupo${items} ficam encerrados após a aprovação — nada será comprado para eles.`);
    else lines.push(`Os itens deste grupo${items} voltam à cotação e exigirão um NOVO lote com novas aprovações de área e final.`);
    if (group.purchaseOrderNumber) lines.push(`O documento da P.O. ${group.purchaseOrderNumber} registado no Portal fica marcado como "Sem efeito".`);
    else lines.push('Nenhum documento de P.O. está registado neste grupo; não há documento a marcar como "Sem efeito".');
    const cond = (group.paymentConditionCode || '').toUpperCase();
    if (cond === 'ADVANCE_FULL' || cond === 'ADVANCE_PARTIAL') lines.push('O adiantamento previsto (ainda não pago) deste grupo é cancelado.');
    if (group.financeConfirmationWouldBeRequired) {
        lines.push('A sua declaração sobre a ECF fica registada como declaração do comprador; Finanças confirma separadamente a anulação do documento externo. O Portal não verifica o Primavera.');
        lines.push('O registo de uma P.O. de substituição para estes itens fica bloqueado até Finanças confirmar a anulação da ECF anterior.');
    } else {
        lines.push('Não é necessária confirmação de Finanças: este grupo nunca teve P.O. registada.');
    }
    return lines;
}

// ── Display helpers ──
/** "Devolvido por Finanças — {kindLabel} — Motivo: {notes}". NULL kind → the unclassified label (never inferred from text). */
export function lastFinanceReturnLabel(dto: LastFinanceReturnDto | null | undefined): string | null {
    if (!dto) return null;
    const kind = dto.kind ? financeReturnKindLabel(dto.kind) : FINANCE_RETURN_KIND_UNCLASSIFIED_LABEL;
    const notes = (dto.notes ?? '').trim();
    return `Devolvido por Finanças — ${kind} — Motivo: ${notes.length > 0 ? notes : '—'}`;
}

/** Banner text for a group whose replacement P.O. is blocked by an unconfirmed prior cancellation (D4). */
export function priorEcfBlockMessage(dto: PriorEcfConfirmationDto | null | undefined): string | null {
    if (!dto || !dto.blocked) return null;
    const numbers = (dto.cancellations || []).map(c => c.previousPoNumber).filter((n): n is string => !!n && n.trim().length > 0);
    const ref = numbers.length > 0 ? ` (${numbers.join(', ')})` : '';
    const head = `Registo da nova P.O. bloqueado até Finanças confirmar a anulação da ECF anterior${ref}.`;
    const detail = (dto.message ?? '').trim();
    return detail.length > 0 ? `${head} ${detail}` : head;
}

/** Message for the Finance panel when nobody in the plant could be notified. */
export const NO_FINANCE_RECIPIENTS_WARNING = 'Nenhum utilizador Finanças da planta foi notificado.';

// ── D13 notification visibility: Finance e-mail policy outcome + per-recipient outbox delivery ──
// Policy: plant-scoped Finance users ALWAYS receive the e-mail for this event (independent of the company AP switch);
// the AP mailbox is additional. The outcome describes what the Portal QUEUED; delivery is the worker's later fact.
export interface FinanceEmailPolicyInput {
    financeEmailPolicyOutcome?: string | null;
    financeRecipientsResolvedCount?: number;
    financeRecipientsWithEmailCount?: number;
    financeEmailsQueuedCount?: number;
}
export interface FinanceEmailPolicyDescription {
    /** 'ok' = queued; 'warning' = partial/no e-mail; 'info' = in-app only. */
    severity: 'ok' | 'warning' | 'info';
    text: string;
    /** The resend action deserves prominence (nothing/partially queued). */
    needsAttention: boolean;
}

export function describeFinanceEmailPolicy(dto: FinanceEmailPolicyInput | null | undefined): FinanceEmailPolicyDescription {
    const outcome = dto?.financeEmailPolicyOutcome ?? null;
    const resolved = dto?.financeRecipientsResolvedCount ?? 0;
    const withEmail = dto?.financeRecipientsWithEmailCount ?? 0;
    const queued = dto?.financeEmailsQueuedCount ?? 0;
    switch (outcome) {
        case 'QUEUED':
            return { severity: 'ok', needsAttention: false, text: `E-mails enfileirados para Finanças da planta: ${queued} (${withEmail} de ${resolved} utilizador(es) com e-mail). Os utilizadores Finanças da planta recebem sempre este e-mail; a caixa de Contas a Pagar é adicional.` };
        case 'NO_ELIGIBLE_FINANCE_USERS':
            return { severity: 'warning', needsAttention: true, text: 'Nenhum utilizador Finanças elegível na planta: nenhum e-mail foi enfileirado. Atribua um utilizador Finanças à planta e reenvie a notificação.' };
        case 'ELIGIBLE_USERS_WITHOUT_EMAIL':
            return { severity: 'warning', needsAttention: true, text: `${resolved} utilizador(es) Finanças elegível(is) sem e-mail registado: só a notificação na aplicação foi criada. Complete o e-mail dos utilizadores e reenvie a notificação.` };
        case 'NO_PLANT_IN_APP_ONLY':
            return { severity: 'info', needsAttention: true, text: 'Pedido sem planta: não é possível resolver destinatários Finanças por e-mail; apenas a notificação na aplicação foi criada.' };
        default:
            return { severity: 'info', needsAttention: resolved === 0, text: resolved === 0 ? NO_FINANCE_RECIPIENTS_WARNING : `Notificação registada para ${resolved} destinatário(s) Finanças (política de e-mail não classificada).` };
    }
}

export interface DeliveryChip { label: string; color: string; background: string; border: string; }
export const DELIVERY_STATUS_LABELS: Record<string, string> = {
    PENDING: 'Pendente', PROCESSING: 'A enviar', SENT: 'Enviado', FAILED: 'Falhou — nova tentativa', DEAD_LETTER: 'Não entregue', EXPIRED: 'Expirado',
};
/** Status chip presentation: SENT green, PENDING/PROCESSING neutral, FAILED amber (retry), DEAD_LETTER red (lastError). */
export function deliveryChip(status?: string | null): DeliveryChip {
    switch (status) {
        case 'SENT': return { label: DELIVERY_STATUS_LABELS.SENT, color: '#166534', background: '#f0fdf4', border: '#bbf7d0' };
        case 'FAILED': return { label: DELIVERY_STATUS_LABELS.FAILED, color: '#92400E', background: '#FFFBEB', border: '#FCD34D' };
        case 'DEAD_LETTER': return { label: DELIVERY_STATUS_LABELS.DEAD_LETTER, color: '#991B1B', background: '#FEF2F2', border: '#FECACA' };
        case 'EXPIRED': return { label: DELIVERY_STATUS_LABELS.EXPIRED, color: '#991B1B', background: '#FEF2F2', border: '#FECACA' };
        case 'PENDING': case 'PROCESSING': return { label: DELIVERY_STATUS_LABELS[status], color: '#475569', background: '#f8fafc', border: '#e2e8f0' };
        default: return { label: status || '—', color: '#475569', background: '#f8fafc', border: '#e2e8f0' };
    }
}

/** True when at least one recipient is FAILED/DEAD_LETTER/EXPIRED — the resend action should be prominent. */
export function hasDeliveryFailures(delivery: { status?: string | null }[] | null | undefined): boolean {
    return (delivery || []).some(d => d.status === 'FAILED' || d.status === 'DEAD_LETTER' || d.status === 'EXPIRED');
}

/** Resend prominence: any delivery failure OR a policy outcome other than QUEUED. */
export function resendNeedsAttention(dto: FinanceEmailPolicyInput & { delivery?: { status?: string | null }[] | null } | null | undefined): boolean {
    if (!dto) return false;
    return hasDeliveryFailures(dto.delivery) || describeFinanceEmailPolicy(dto).needsAttention;
}

/** Finance rejection note validation (mandatory, ≥ MIN_JUSTIFICATION_LENGTH). */
export function validateFinanceRejectionNote(note: string): string | null {
    if ((note ?? '').trim().length < MIN_JUSTIFICATION_LENGTH) return `A justificação da rejeição é obrigatória (mínimo ${MIN_JUSTIFICATION_LENGTH} caracteres).`;
    return null;
}

/** Explanation of a Buyer declaration as a short sentence for cards (status + reference + who/when are rendered by the caller). */
export function describeBuyerDeclaration(decl: { ecfStatus?: string | null; ecfReference?: string | null }): string {
    const base = ecfStatusLabel(decl.ecfStatus);
    const ref = (decl.ecfReference ?? '').trim();
    return ref.length > 0 ? `${base} — referência ${ref}` : base;
}
