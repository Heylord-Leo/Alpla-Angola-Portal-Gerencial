import { describe, it, expect } from 'vitest';
import {
    MIN_JUSTIFICATION_LENGTH, MAX_JUSTIFICATION_LENGTH, EMPTY_CANCEL_FORM, CANCEL_FORM_MESSAGES,
    validateCancelPoGroupForm, validateEcfDeclaration, describeCancellationConsequences,
    ecfStatusLabel, financeStatusLabel, financeReturnKindLabel, financeReturnReasonLabel, cancellationReasonLabel, cancellationKindLabel,
    lastFinanceReturnLabel, priorEcfBlockMessage, validateFinanceRejectionNote, describeBuyerDeclaration,
    CANCELLATION_REASON_LABELS, FINANCE_RETURN_REASON_LABELS, PO_CANCELLATION_HISTORY_LABELS, FINANCE_RETURN_KIND_UNCLASSIFIED_LABEL,
} from './poGroupCancellation';

const valid = { ...EMPTY_CANCEL_FORM, kind: 'RESTART_QUOTATION', reasonCode: 'SUPPLIER_CANCELLED', reason: 'O fornecedor informou que não consegue entregar.' };

describe('label maps mirror PoGroupCancellationConstants.cs', () => {
    it('reason codes', () => {
        expect(Object.keys(CANCELLATION_REASON_LABELS)).toEqual(['SUPPLIER_CANCELLED', 'ORDER_CANCELLED_BY_COMPANY', 'PRICE_OR_TERMS_CHANGED', 'WRONG_SUPPLIER_OR_ITEM', 'DUPLICATE_ORDER', 'FINANCE_RETURN', 'OTHER']);
        expect(cancellationReasonLabel('FINANCE_RETURN')).toBe('Devolvido por Finanças sem correção possível');
        expect(cancellationReasonLabel('OTHER')).toBe('Outro (explicar)');
        expect(cancellationReasonLabel('BOGUS')).toBe('BOGUS');
        expect(cancellationReasonLabel(null)).toBe('—');
    });
    it('kinds, ECF statuses and Finance statuses use declaration wording (never a Portal verification)', () => {
        expect(cancellationKindLabel('RESTART_QUOTATION')).toBe('Devolver itens à cotação');
        expect(cancellationKindLabel('CANCEL_ITEMS')).toContain('Encerrar itens');
        expect(ecfStatusLabel('DECLARED_CANCELLED')).toContain('O comprador declara');
        expect(ecfStatusLabel('PENDING_EXTERNAL')).toContain('O comprador declara');
        expect(ecfStatusLabel('UNKNOWN')).toContain('O comprador declara');
        expect(ecfStatusLabel('NOT_APPLICABLE')).toContain('Não aplicável');
        expect(financeStatusLabel('CONFIRMED')).toBe('Anulação confirmada por Finanças');
        expect(financeStatusLabel('REJECTED')).toBe('Declaração rejeitada por Finanças');
        expect(financeStatusLabel('PENDING')).toBe('Aguarda confirmação de Finanças');
        expect(financeStatusLabel('NOT_REQUIRED')).toBe('Confirmação de Finanças não necessária');
    });
    it('Finance return kinds + reason codes', () => {
        expect(financeReturnKindLabel('PO_CORRECTION')).toBe('Corrigir P.O.');
        expect(financeReturnKindLabel('ORDER_CANCELLATION_REQUESTED')).toBe('Cancelamento solicitado — não corrigir');
        expect(financeReturnKindLabel(null)).toBe(FINANCE_RETURN_KIND_UNCLASSIFIED_LABEL);
        expect(financeReturnKindLabel(undefined)).toBe('Tipo não classificado (devolução anterior à versão)');
        expect(Object.keys(FINANCE_RETURN_REASON_LABELS)).toEqual(['WRONG_AMOUNT', 'WRONG_SUPPLIER', 'DOCUMENT_ILLEGIBLE', 'PAYMENT_CONDITIONS', 'ORDER_CANCELLED', 'SUPPLIER_CANCELLED', 'OTHER']);
        expect(financeReturnReasonLabel('WRONG_AMOUNT')).toBe('Valor da P.O incorreto');
    });
    it('history action labels for the eight new actions', () => {
        expect(PO_CANCELLATION_HISTORY_LABELS).toEqual({
            PO_GROUP_CANCELLED: 'P.O. cancelada',
            PO_GROUP_ITEMS_RELEASED: 'Itens devolvidos à cotação',
            PO_GROUP_ITEMS_CLOSED: 'Itens encerrados após aprovação',
            PO_CANCELLATION_ECF_DECLARED: 'Declaração do comprador sobre a ECF',
            PO_CANCELLATION_CONFIRMED: 'Anulação da ECF confirmada por Finanças',
            PO_CANCELLATION_REJECTED: 'Declaração da ECF rejeitada por Finanças',
            PO_CANCELLATION_NOTIFICATION_RESENT: 'Notificação de cancelamento reenviada',
            PO_NUMBER_REUSED_AFTER_CANCELLATION: 'Número de P.O. reutilizado após cancelamento',
        });
    });
});

describe('validateCancelPoGroupForm', () => {
    it('MIN_JUSTIFICATION_LENGTH is 20 (backend constant)', () => {
        expect(MIN_JUSTIFICATION_LENGTH).toBe(20);
        expect(MAX_JUSTIFICATION_LENGTH).toBe(1000);
    });
    it('passes a complete form without ECF when Finance confirmation is not required', () => {
        expect(validateCancelPoGroupForm(valid, { financeConfirmationWouldBeRequired: false })).toEqual([]);
    });
    it('requires kind, reason code and a 20–1000 char explanation', () => {
        const errs = validateCancelPoGroupForm(EMPTY_CANCEL_FORM, { financeConfirmationWouldBeRequired: false });
        expect(errs.map(e => e.field)).toEqual(['kind', 'reasonCode', 'reason']);
        expect(errs.find(e => e.field === 'reason')!.message).toBe(CANCEL_FORM_MESSAGES.reasonTooShort);
        expect(validateCancelPoGroupForm({ ...valid, kind: 'SOMETHING' }, { financeConfirmationWouldBeRequired: false }).map(e => e.field)).toEqual(['kind']);
        expect(validateCancelPoGroupForm({ ...valid, reasonCode: 'NOPE' }, { financeConfirmationWouldBeRequired: false }).map(e => e.field)).toEqual(['reasonCode']);
        expect(validateCancelPoGroupForm({ ...valid, reason: '                    x' }, { financeConfirmationWouldBeRequired: false }).map(e => e.field)).toEqual(['reason']); // trimmed
        expect(validateCancelPoGroupForm({ ...valid, reason: 'a'.repeat(1001) }, { financeConfirmationWouldBeRequired: false })[0].message).toBe(CANCEL_FORM_MESSAGES.reasonTooLong);
        expect(validateCancelPoGroupForm({ ...valid, reason: 'a'.repeat(20) }, { financeConfirmationWouldBeRequired: false })).toEqual([]);
    });
    it('when ECF declaration is required: status mandatory; reference mandatory only for DECLARED_CANCELLED', () => {
        expect(validateCancelPoGroupForm(valid, { financeConfirmationWouldBeRequired: true }).map(e => e.field)).toEqual(['ecfStatus']);
        expect(validateCancelPoGroupForm({ ...valid, ecfStatus: 'NOT_APPLICABLE' }, { financeConfirmationWouldBeRequired: true }).map(e => e.field)).toEqual(['ecfStatus']);
        expect(validateCancelPoGroupForm({ ...valid, ecfStatus: 'PENDING_EXTERNAL' }, { financeConfirmationWouldBeRequired: true })).toEqual([]);
        expect(validateCancelPoGroupForm({ ...valid, ecfStatus: 'UNKNOWN' }, { financeConfirmationWouldBeRequired: true })).toEqual([]);
        expect(validateCancelPoGroupForm({ ...valid, ecfStatus: 'DECLARED_CANCELLED' }, { financeConfirmationWouldBeRequired: true }).map(e => e.field)).toEqual(['ecfReference']);
        expect(validateCancelPoGroupForm({ ...valid, ecfStatus: 'DECLARED_CANCELLED', ecfReference: '   ' }, { financeConfirmationWouldBeRequired: true }).map(e => e.field)).toEqual(['ecfReference']);
        expect(validateCancelPoGroupForm({ ...valid, ecfStatus: 'DECLARED_CANCELLED', ecfReference: 'ECF10 2026/364' }, { financeConfirmationWouldBeRequired: true })).toEqual([]);
        expect(validateEcfDeclaration({ ecfStatus: 'PENDING_EXTERNAL', ecfReference: 'x'.repeat(101) }).map(e => e.field)).toEqual(['ecfReference']);
    });
});

describe('describeCancellationConsequences', () => {
    const group = { purchaseOrderNumber: 'ECF10 2026/364', paymentConditionCode: 'ADVANCE_PARTIAL', financeConfirmationWouldBeRequired: true, lineNumbers: [2, 5] };
    it('RESTART_QUOTATION: items back to quotation + NEW approvals; document "Sem efeito"; advance cancelled; Finance confirmation + replacement blocked', () => {
        const lines = describeCancellationConsequences({ kind: 'RESTART_QUOTATION' }, group);
        expect(lines[0]).toContain('#2, #5');
        expect(lines[0]).toContain('voltam à cotação');
        expect(lines[0]).toContain('novas aprovações de área e final');
        expect(lines.some(l => l.includes('ECF10 2026/364') && l.includes('"Sem efeito"'))).toBe(true);
        expect(lines.some(l => l.includes('adiantamento previsto'))).toBe(true);
        expect(lines.some(l => l.includes('declaração do comprador') && l.includes('Finanças confirma'))).toBe(true);
        expect(lines.some(l => l.includes('bloqueado até Finanças confirmar'))).toBe(true);
        expect(lines.join(' ')).not.toMatch(/o Portal (anulou|cancelou|verificou)/i);
    });
    it('CANCEL_ITEMS: items closed; no advance line for POST_PAID; no Finance confirmation when the group never had a P.O.', () => {
        const lines = describeCancellationConsequences({ kind: 'CANCEL_ITEMS' }, { purchaseOrderNumber: null, paymentConditionCode: 'POST_PAID', financeConfirmationWouldBeRequired: false, lineNumbers: [] });
        expect(lines[0]).toContain('ficam encerrados');
        expect(lines[0]).not.toContain('#');
        expect(lines.some(l => l.includes('Nenhum documento de P.O.'))).toBe(true);
        expect(lines.some(l => l.includes('adiantamento'))).toBe(false);
        expect(lines.some(l => l.includes('Não é necessária confirmação de Finanças'))).toBe(true);
        expect(lines.some(l => l.includes('bloqueado'))).toBe(false);
    });
});

describe('display helpers', () => {
    it('lastFinanceReturnLabel never infers a kind from the text', () => {
        expect(lastFinanceReturnLabel({ kind: null, kindLabel: 'x', notes: 'Pedido cancelado, não corrigir', source: 'HISTORY' }))
            .toBe('Devolvido por Finanças — Tipo não classificado (devolução anterior à versão) — Motivo: Pedido cancelado, não corrigir');
        expect(lastFinanceReturnLabel({ kind: 'ORDER_CANCELLATION_REQUESTED', kindLabel: 'x', notes: 'cancelar', source: 'COLUMNS' }))
            .toBe('Devolvido por Finanças — Cancelamento solicitado — não corrigir — Motivo: cancelar');
        expect(lastFinanceReturnLabel({ kind: 'PO_CORRECTION', kindLabel: 'x', notes: '  ', source: 'COLUMNS' })).toBe('Devolvido por Finanças — Corrigir P.O. — Motivo: —');
        expect(lastFinanceReturnLabel(null)).toBeNull();
    });
    it('priorEcfBlockMessage lists previous P.O. numbers and appends the server message', () => {
        expect(priorEcfBlockMessage({ blocked: false, cancellations: [], message: 'ignored' })).toBeNull();
        expect(priorEcfBlockMessage(null)).toBeNull();
        const msg = priorEcfBlockMessage({ blocked: true, message: 'Aguarda Finanças.', cancellations: [
            { cancellationId: 'c1', previousPoGroupId: 'g1', previousPoNumber: 'ECF10 2026/364', financeConfirmationStatus: 'PENDING', ecfStatus: 'PENDING_EXTERNAL', lineNumbers: [1] },
            { cancellationId: 'c2', previousPoGroupId: 'g2', previousPoNumber: null, financeConfirmationStatus: 'REJECTED', ecfStatus: 'UNKNOWN', lineNumbers: [2] },
        ] });
        expect(msg).toBe('Registo da nova P.O. bloqueado até Finanças confirmar a anulação da ECF anterior (ECF10 2026/364). Aguarda Finanças.');
        expect(priorEcfBlockMessage({ blocked: true, cancellations: [], message: null })).toBe('Registo da nova P.O. bloqueado até Finanças confirmar a anulação da ECF anterior.');
    });
    it('Finance rejection note is mandatory (≥ 20) and the declaration summary includes the reference', () => {
        expect(validateFinanceRejectionNote('curto')).toMatch(/mínimo 20/);
        expect(validateFinanceRejectionNote('A referência indicada não existe no Primavera.')).toBeNull();
        expect(describeBuyerDeclaration({ ecfStatus: 'DECLARED_CANCELLED', ecfReference: 'ECF10 2026/364' })).toBe('O comprador declara que a ECF já foi anulada fora do Portal — referência ECF10 2026/364');
        expect(describeBuyerDeclaration({ ecfStatus: 'UNKNOWN', ecfReference: '' })).toBe('O comprador declara não saber o estado da ECF');
    });
});

// ── D13 notification visibility: Finance e-mail policy + per-recipient delivery ──
import { describeFinanceEmailPolicy, deliveryChip, hasDeliveryFailures, resendNeedsAttention, NO_FINANCE_RECIPIENTS_WARNING } from './poGroupCancellation';

describe('describeFinanceEmailPolicy', () => {
    it('QUEUED → ok, counts, states the plant-users-always + AP-additional policy, no attention', () => {
        const d = describeFinanceEmailPolicy({ financeEmailPolicyOutcome: 'QUEUED', financeRecipientsResolvedCount: 3, financeRecipientsWithEmailCount: 2, financeEmailsQueuedCount: 2 });
        expect(d.severity).toBe('ok');
        expect(d.needsAttention).toBe(false);
        expect(d.text).toContain('enfileirados para Finanças da planta: 2');
        expect(d.text).toContain('2 de 3');
        expect(d.text).toContain('recebem sempre');
        expect(d.text).toContain('Contas a Pagar é adicional');
    });
    it('distinguishes no eligible users / eligible without e-mail / no plant (in-app only), all needing attention', () => {
        const none = describeFinanceEmailPolicy({ financeEmailPolicyOutcome: 'NO_ELIGIBLE_FINANCE_USERS', financeRecipientsResolvedCount: 0 });
        expect(none.severity).toBe('warning'); expect(none.needsAttention).toBe(true); expect(none.text).toContain('Nenhum utilizador Finanças elegível');
        const noEmail = describeFinanceEmailPolicy({ financeEmailPolicyOutcome: 'ELIGIBLE_USERS_WITHOUT_EMAIL', financeRecipientsResolvedCount: 2, financeRecipientsWithEmailCount: 0 });
        expect(noEmail.severity).toBe('warning'); expect(noEmail.needsAttention).toBe(true); expect(noEmail.text).toContain('2 utilizador(es) Finanças elegível(is) sem e-mail');
        const noPlant = describeFinanceEmailPolicy({ financeEmailPolicyOutcome: 'NO_PLANT_IN_APP_ONLY' });
        expect(noPlant.severity).toBe('info'); expect(noPlant.needsAttention).toBe(true); expect(noPlant.text).toContain('sem planta'); expect(noPlant.text).toContain('apenas a notificação na aplicação');
    });
    it('null/unknown outcome (legacy rows) falls back to the resolved count; zero → the recipients warning', () => {
        expect(describeFinanceEmailPolicy({ financeEmailPolicyOutcome: null, financeRecipientsResolvedCount: 0 })).toEqual({ severity: 'info', needsAttention: true, text: NO_FINANCE_RECIPIENTS_WARNING });
        const legacy = describeFinanceEmailPolicy({ financeEmailPolicyOutcome: undefined, financeRecipientsResolvedCount: 2 });
        expect(legacy.needsAttention).toBe(false); expect(legacy.text).toContain('2 destinatário(s)');
        expect(describeFinanceEmailPolicy(null).needsAttention).toBe(true);
    });
});

describe('deliveryChip / resend attention', () => {
    it('maps statuses: SENT green, PENDING/PROCESSING neutral, FAILED amber, DEAD_LETTER red', () => {
        expect(deliveryChip('SENT')).toMatchObject({ label: 'Enviado', color: '#166534' });
        expect(deliveryChip('PENDING')).toMatchObject({ label: 'Pendente', color: '#475569' });
        expect(deliveryChip('PROCESSING')).toMatchObject({ label: 'A enviar', color: '#475569' });
        expect(deliveryChip('FAILED')).toMatchObject({ label: 'Falhou — nova tentativa', color: '#92400E' });
        expect(deliveryChip('DEAD_LETTER')).toMatchObject({ label: 'Não entregue', color: '#991B1B' });
        expect(deliveryChip('EXPIRED').color).toBe('#991B1B');
        expect(deliveryChip('WHATEVER').label).toBe('WHATEVER');
        expect(deliveryChip(null).label).toBe('—');
    });
    it('resend is prominent on any FAILED/DEAD_LETTER/EXPIRED delivery or a non-QUEUED outcome', () => {
        const sent = [{ status: 'SENT' }, { status: 'PENDING' }];
        expect(hasDeliveryFailures(sent)).toBe(false);
        expect(hasDeliveryFailures([{ status: 'SENT' }, { status: 'DEAD_LETTER' }])).toBe(true);
        expect(hasDeliveryFailures([{ status: 'FAILED' }])).toBe(true);
        expect(hasDeliveryFailures(null)).toBe(false);
        expect(resendNeedsAttention({ financeEmailPolicyOutcome: 'QUEUED', financeRecipientsResolvedCount: 1, financeRecipientsWithEmailCount: 1, financeEmailsQueuedCount: 1, delivery: sent })).toBe(false);
        expect(resendNeedsAttention({ financeEmailPolicyOutcome: 'QUEUED', financeRecipientsResolvedCount: 1, financeRecipientsWithEmailCount: 1, financeEmailsQueuedCount: 1, delivery: [{ status: 'FAILED' }] })).toBe(true);
        expect(resendNeedsAttention({ financeEmailPolicyOutcome: 'ELIGIBLE_USERS_WITHOUT_EMAIL', financeRecipientsResolvedCount: 1, delivery: [] })).toBe(true);
        expect(resendNeedsAttention(null)).toBe(false);
    });
});

// v2.249.2 — Buyer guidance by the STORED return kind (legacy null → neutral; nothing inferred from reason text).
import { financeReturnGuidance, FINANCE_RETURN_KIND_GUIDANCE_UNCLASSIFIED } from './poGroupCancellation';
describe('financeReturnGuidance (v2.249.2)', () => {
    it('names the correction for PO_CORRECTION and the cancellation advisory for ORDER_CANCELLATION_REQUESTED, keeping both outcomes available', () => {
        expect(financeReturnGuidance('PO_CORRECTION')).toContain('Corrigir P.O.');
        expect(financeReturnGuidance('PO_CORRECTION')).toContain('pode cancelá-la');
        expect(financeReturnGuidance('ORDER_CANCELLATION_REQUESTED')).toContain('não a corrija');
        expect(financeReturnGuidance('ORDER_CANCELLATION_REQUESTED')).toContain('Cancelar P.O.');
        expect(financeReturnGuidance('ORDER_CANCELLATION_REQUESTED')).toContain('A correção continua disponível');
    });
    it('legacy / unknown kinds stay neutral', () => {
        expect(financeReturnGuidance(null)).toBe(FINANCE_RETURN_KIND_GUIDANCE_UNCLASSIFIED);
        expect(financeReturnGuidance(undefined)).toBe(FINANCE_RETURN_KIND_GUIDANCE_UNCLASSIFIED);
        expect(financeReturnGuidance('SOMETHING_ELSE')).toBe(FINANCE_RETURN_KIND_GUIDANCE_UNCLASSIFIED);
        expect(FINANCE_RETURN_KIND_GUIDANCE_UNCLASSIFIED).not.toMatch(/cancelamento solicitado|Corrigir P\.O\. e volte/);
    });
});
