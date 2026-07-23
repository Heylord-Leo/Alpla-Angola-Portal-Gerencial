import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import {
    resolveOperationalGroups,
    hasMultipleOperationalGroups,
    resolveParentDisplayStatus,
    resolveGroupStatusLabel,
    resolveSafeStatusLabel,
    GROUP_STATUS_LABELS,
} from './requestGroupDisplayState.ts';

// Mirrors AlplaPortal.Domain.Services.RequestGroupDisplayStateCalculatorTests.cs — same bucket
// statuses, same label strings, same resolution order. Update both sides together.
describe('resolveParentDisplayStatus', () => {
    test('request 100 shape: NCR PAYMENT_SCHEDULED + ITEC ADVANCE_PAYMENT_COMPLETED -> "Pagamentos em andamento", not the raw persisted aggregate', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'ncr', status: 'PAYMENT_SCHEDULED' },
                { id: 'itec', status: 'ADVANCE_PAYMENT_COMPLETED' },
            ],
            'Adiantamento Realizado' // the raw persisted Request.Status.Name for this scenario
        );
        assert.equal(result.label, 'Pagamentos em andamento');
        assert.notEqual(result.label, 'Adiantamento Realizado');
        assert.equal(result.bucket, 'MIXED');
    });

    test('same exact PAYMENT_SCHEDULED code across every group -> persisted Portuguese status name is preserved, not a generic bucket label', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'PAYMENT_SCHEDULED' },
                { id: 'g2', status: 'PAYMENT_SCHEDULED' },
            ],
            'Pagamento Agendado (nome persistido)'
        );
        assert.equal(result.label, 'Pagamento Agendado (nome persistido)');
        assert.equal(result.bucket, 'SCHEDULED');
    });

    test('same exact COMPLETED code across every group -> persisted status name preserved', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'COMPLETED' },
                { id: 'g2', status: 'COMPLETED' },
            ],
            'Concluído (nome persistido)'
        );
        assert.equal(result.label, 'Concluído (nome persistido)');
        assert.equal(result.bucket, 'COMPLETED');
    });

    test('different codes in the same SCHEDULED bucket -> "Pagamentos agendados"', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'PAYMENT_SCHEDULED' },
                { id: 'g2', status: 'ADVANCE_PAYMENT_SCHEDULED' },
            ],
            'fallback'
        );
        assert.equal(result.label, 'Pagamentos agendados');
        assert.equal(result.bucket, 'SCHEDULED');
    });

    test('different codes in PAID_OR_POST_PAYMENT bucket -> "Pagamentos concluídos"', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'PAYMENT_COMPLETED' },
                { id: 'g2', status: 'WAITING_RECEIPT' },
            ],
            'fallback'
        );
        assert.equal(result.label, 'Pagamentos concluídos');
        assert.equal(result.bucket, 'PAID_OR_POST_PAYMENT');
    });

    test('different codes in WAITING_ACTION bucket -> "Aguardando processamento financeiro"', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'PO_ISSUED' },
                { id: 'g2', status: 'PAYMENT_REQUEST_SENT' },
            ],
            'fallback'
        );
        assert.equal(result.label, 'Aguardando processamento financeiro');
        assert.equal(result.bucket, 'WAITING_ACTION');
    });

    test('different codes in ADVANCE_PAID bucket -> "Adiantamentos realizados"', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'ADVANCE_PAYMENT_COMPLETED' },
                { id: 'g2', status: 'WAITING_SUPPLIER_DELIVERY' },
            ],
            'fallback'
        );
        assert.equal(result.label, 'Adiantamentos realizados');
        assert.equal(result.bucket, 'ADVANCE_PAID');
    });

    test('one PAYMENT_COMPLETED + one PAYMENT_SCHEDULED -> mixed "Pagamentos em andamento"', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'PAYMENT_COMPLETED' },
                { id: 'g2', status: 'PAYMENT_SCHEDULED' },
            ],
            'fallback'
        );
        assert.equal(result.label, 'Pagamentos em andamento');
        assert.equal(result.bucket, 'MIXED');
    });

    test('CANCELLED groups are excluded from bucket computation', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'g1', status: 'PAYMENT_SCHEDULED' },
                { id: 'g2', status: 'CANCELLED' },
            ],
            'fallback'
        );
        // Only the one non-CANCELLED group counts -> single group, single code -> passthrough.
        assert.equal(result.label, 'fallback');
        assert.equal(result.bucket, 'SCHEDULED');
    });

    test('single-group request preserves the persisted status name (no override needed)', () => {
        const result = resolveParentDisplayStatus([{ id: 'g1', status: 'ADVANCE_PAYMENT_COMPLETED' }], 'Adiantamento Realizado');
        assert.equal(result.label, 'Adiantamento Realizado');
        assert.equal(result.bucket, 'ADVANCE_PAID');
    });

    test('no active groups -> preserves the existing safe fallback label', () => {
        const result = resolveParentDisplayStatus([], 'Aguardando Cotação');
        assert.equal(result.label, 'Aguardando Cotação');
        assert.equal(result.bucket, null);

        const resultAllCancelled = resolveParentDisplayStatus([{ id: 'g1', status: 'CANCELLED' }], 'Aguardando Cotação');
        assert.equal(resultAllCancelled.label, 'Aguardando Cotação');
        assert.equal(resultAllCancelled.bucket, null);

        const resultUndefined = resolveParentDisplayStatus(undefined, 'Aguardando Cotação');
        assert.equal(resultUndefined.label, 'Aguardando Cotação');
    });

    test('unrecognized group status falls back rather than guessing a label', () => {
        const result = resolveParentDisplayStatus([{ id: 'g1', status: 'SOME_FUTURE_STATUS' }], 'fallback');
        assert.equal(result.label, 'fallback');
        assert.equal(result.bucket, null);
    });
});

describe('resolveOperationalGroups / hasMultipleOperationalGroups', () => {
    test('excludes CANCELLED, keeps everything else', () => {
        const groups = resolveOperationalGroups([
            { id: 'g1', status: 'PO_ISSUED' },
            { id: 'g2', status: 'CANCELLED' },
        ]);
        assert.deepEqual(groups.map(g => g.id), ['g1']);
    });

    test('hasMultipleOperationalGroups counts only non-CANCELLED groups', () => {
        assert.equal(hasMultipleOperationalGroups([{ id: 'g1', status: 'PO_ISSUED' }, { id: 'g2', status: 'CANCELLED' }]), false);
        assert.equal(hasMultipleOperationalGroups([{ id: 'g1', status: 'PO_ISSUED' }, { id: 'g2', status: 'PAYMENT_SCHEDULED' }]), true);
    });
});

describe('resolveGroupStatusLabel', () => {
    test('known status returns its friendly label', () => {
        assert.equal(resolveGroupStatusLabel('ADVANCE_PAYMENT_COMPLETED'), 'Adiantamento realizado');
    });

    test('null/undefined returns a placeholder, never throws', () => {
        assert.equal(resolveGroupStatusLabel(null), '---');
        assert.equal(resolveGroupStatusLabel(undefined), '---');
    });

    test('every GROUP_STATUS_LABELS entry maps to a non-empty, non-underscore string', () => {
        for (const [code, label] of Object.entries(GROUP_STATUS_LABELS)) {
            assert.ok(label.length > 0, `${code} has an empty label`);
            assert.ok(!label.includes('_'), `${code}'s label "${label}" still contains a raw-code underscore`);
        }
    });
});

describe('resolveSafeStatusLabel', () => {
    test('both displayStatusName and statusName null/empty, and the code has no group-label translation -> "Status não definido", never the raw code', () => {
        assert.equal(resolveSafeStatusLabel('SOME_UNKNOWN_FUTURE_CODE', null, null), 'Status não definido');
        assert.equal(resolveSafeStatusLabel('SOME_UNKNOWN_FUTURE_CODE', '', undefined), 'Status não definido');
    });

    test('both names absent, but statusCode has a genuine group-label translation -> that translation is used, not "Status não definido"', () => {
        assert.equal(resolveSafeStatusLabel('ADVANCE_PAYMENT_COMPLETED', null, null), 'Adiantamento realizado');
    });

    test('no statusCode either -> "Status não definido"', () => {
        assert.equal(resolveSafeStatusLabel(null, null, null), 'Status não definido');
    });

    test('unknown technical code with no name data -> never renders underscore-to-space English text', () => {
        const result = resolveSafeStatusLabel('SOME_UNKNOWN_FUTURE_CODE', null, null);
        assert.equal(result, 'Status não definido');
        assert.ok(!result.includes('_'));
        assert.notEqual(result, 'SOME UNKNOWN FUTURE CODE');
    });

    test('displayStatusName takes priority over statusName', () => {
        assert.equal(resolveSafeStatusLabel('X', 'Nome Persistido', 'Pagamentos em andamento'), 'Pagamentos em andamento');
    });

    test('statusName used when displayStatusName is absent', () => {
        assert.equal(resolveSafeStatusLabel('ADVANCE_PAYMENT_COMPLETED', 'Adiantamento Realizado', null), 'Adiantamento Realizado');
    });

    test('falls back to the group-status label resolver when only statusCode is known and it maps to a genuine translation', () => {
        assert.equal(resolveSafeStatusLabel('PAYMENT_SCHEDULED', null, null), 'Pagamento agendado');
    });

    test('request 100: Explorer row renders "Pagamentos em andamento" via displayStatusName', () => {
        const result = resolveSafeStatusLabel('ADVANCE_PAYMENT_COMPLETED', 'Adiantamento Realizado', 'Pagamentos em andamento');
        assert.equal(result, 'Pagamentos em andamento');
    });
});
