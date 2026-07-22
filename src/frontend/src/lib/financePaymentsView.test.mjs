import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import {
    resolveSingleGroupRowActions,
    resolveOperationalGroups,
    hasMultipleOperationalGroups,
    shouldShowSchedule,
    shouldShowPay,
    toggleSort,
} from './financePaymentsView.ts';

describe('resolveSingleGroupRowActions', () => {
    test('reads eligibility only from availableFinanceActions, never from poGroups.length', () => {
        // The confirmed bug scenario: PAY is eligible per the backend even though poGroups is
        // empty (legacy group stuck at PENDING, filtered out of the DTO's PoGroups array).
        const result = resolveSingleGroupRowActions({ availableFinanceActions: ['PAY', 'ADD_NOTE'], poGroups: [] });
        assert.equal(result.canPay, true);
        assert.equal(result.canSchedule, false);
        assert.equal(result.groupId, null); // zero groups — no id to act on
    });

    test('resolves the single group id when exactly one group is present', () => {
        const result = resolveSingleGroupRowActions({ availableFinanceActions: ['SCHEDULE', 'PAY'], poGroups: [{ id: 'g1' }] });
        assert.equal(result.canSchedule, true);
        assert.equal(result.canPay, true);
        assert.equal(result.groupId, 'g1');
    });

    test('does not resolve a single group id when multiple groups are present (multi-group UX is separate)', () => {
        const result = resolveSingleGroupRowActions({ availableFinanceActions: ['SCHEDULE'], poGroups: [{ id: 'g1' }, { id: 'g2' }] });
        assert.equal(result.groupId, null);
    });

    test('handles missing/undefined poGroups without throwing', () => {
        assert.doesNotThrow(() => resolveSingleGroupRowActions({ availableFinanceActions: [] }));
        const result = resolveSingleGroupRowActions({ availableFinanceActions: [] });
        assert.equal(result.groupId, null);
        assert.equal(result.canSchedule, false);
        assert.equal(result.canPay, false);
    });
});

describe('CANCELLED-group exclusion (operational group count)', () => {
    // Scenario (a): one operational group + one CANCELLED group — treated as a single operational
    // group; normal top-level action resolution uses the operational group, not the cancelled one.
    test('one operational group + one CANCELLED group resolves to the operational group', () => {
        const item = {
            availableFinanceActions: ['PAY'],
            poGroups: [
                { id: 'cancelled-1', status: 'CANCELLED' },
                { id: 'operational-1', status: 'PO_ISSUED' },
            ],
        };
        assert.equal(hasMultipleOperationalGroups(item.poGroups), false);
        const result = resolveSingleGroupRowActions(item);
        assert.equal(result.groupId, 'operational-1');
        assert.equal(result.canPay, true);
        assert.equal(shouldShowPay(item), true);
    });

    // Scenario (b): only a CANCELLED group — no executable group is resolved, so no payment/
    // schedule action renders even though the backend reported them eligible (parent-status-driven).
    test('only one CANCELLED group resolves no executable group and renders no action', () => {
        const item = {
            availableFinanceActions: ['SCHEDULE', 'PAY'],
            poGroups: [{ id: 'cancelled-1', status: 'CANCELLED' }],
        };
        assert.equal(hasMultipleOperationalGroups(item.poGroups), false);
        const result = resolveSingleGroupRowActions(item);
        assert.equal(result.groupId, null);
        assert.equal(shouldShowSchedule(item), false);
        assert.equal(shouldShowPay(item), false);
    });

    // Scenario (c): one legacy PENDING PAYMENT group — PENDING is not CANCELLED, so it stays
    // operational and resolvable; this is the confirmed bug fix this helper must never regress.
    test('one legacy PENDING PAYMENT group remains resolvable for PAY; SCHEDULE follows availableFinanceActions', () => {
        const item = {
            availableFinanceActions: ['PAY'], // backend correctly omits SCHEDULE for a PENDING group
            poGroups: [{ id: 'legacy-pending-1', status: 'PENDING' }],
        };
        assert.equal(hasMultipleOperationalGroups(item.poGroups), false);
        const result = resolveSingleGroupRowActions(item);
        assert.equal(result.groupId, 'legacy-pending-1');
        assert.equal(shouldShowPay(item), true);
        assert.equal(shouldShowSchedule(item), false);
    });

    test('resolveOperationalGroups excludes CANCELLED but keeps every other status, including PENDING', () => {
        const groups = [
            { id: 'a', status: 'CANCELLED' },
            { id: 'b', status: 'PENDING' },
            { id: 'c', status: 'PO_ISSUED' },
        ];
        const operational = resolveOperationalGroups(groups);
        assert.deepEqual(operational.map(g => g.id), ['b', 'c']);
    });

    test('two operational groups (no CANCELLED) still count as multiple — unaffected by this change', () => {
        const groups = [{ id: 'a', status: 'PO_ISSUED' }, { id: 'b', status: 'WAITING_PO' }];
        assert.equal(hasMultipleOperationalGroups(groups), true);
    });

    test('missing status on a group entry is treated as operational (not CANCELLED)', () => {
        const groups = [{ id: 'a' }];
        assert.equal(resolveOperationalGroups(groups).length, 1);
    });
});

describe('shouldShowSchedule / shouldShowPay', () => {
    test('SCHEDULE renders when eligible AND a group id is resolvable', () => {
        assert.equal(shouldShowSchedule({ availableFinanceActions: ['SCHEDULE'], poGroups: [{ id: 'g1' }] }), true);
    });

    test('SCHEDULE does not render when eligible but no group id is resolvable (zero groups)', () => {
        // Eligibility says yes, but there is nothing to execute against — an execution/input
        // problem, not a different eligibility policy (per the design requirement).
        assert.equal(shouldShowSchedule({ availableFinanceActions: ['SCHEDULE'], poGroups: [] }), false);
    });

    test('PAY renders when eligible AND a group id is resolvable, even with a legacy group status', () => {
        // The confirmed fix: PAY must render for a PAYMENT-type request whose only group is
        // stuck at a pre-finance status, because the backend's own rule is parent-status-driven —
        // as long as backend correctly listed PAY in availableFinanceActions and a group exists.
        assert.equal(shouldShowPay({ availableFinanceActions: ['PAY'], poGroups: [{ id: 'g1' }] }), true);
    });

    test('PAY does not render when not eligible, regardless of groups', () => {
        assert.equal(shouldShowPay({ availableFinanceActions: ['SCHEDULE'], poGroups: [{ id: 'g1' }] }), false);
    });

    test('neither renders when availableFinanceActions is empty', () => {
        assert.equal(shouldShowSchedule({ availableFinanceActions: [], poGroups: [{ id: 'g1' }] }), false);
        assert.equal(shouldShowPay({ availableFinanceActions: [], poGroups: [{ id: 'g1' }] }), false);
    });
});

describe('toggleSort', () => {
    test('first click on a column activates it ascending', () => {
        const result = toggleSort({ key: null, direction: 'asc' }, 'suppliername');
        assert.deepEqual(result, { key: 'suppliername', direction: 'asc' });
    });

    test('second click on the same ascending column switches to descending', () => {
        const result = toggleSort({ key: 'suppliername', direction: 'asc' }, 'suppliername');
        assert.deepEqual(result, { key: 'suppliername', direction: 'desc' });
    });

    test('clicking a different column activates it ascending, replacing the previous sort', () => {
        const result = toggleSort({ key: 'suppliername', direction: 'desc' }, 'amount');
        assert.deepEqual(result, { key: 'amount', direction: 'asc' });
    });

    test('clicking the same column a third time (currently descending) resets to ascending', () => {
        const result = toggleSort({ key: 'amount', direction: 'desc' }, 'amount');
        assert.deepEqual(result, { key: 'amount', direction: 'asc' });
    });

    test('only one active sort column at a time', () => {
        let config = { key: null, direction: 'asc' };
        config = toggleSort(config, 'requestnumber');
        config = toggleSort(config, 'statuscode');
        assert.equal(config.key, 'statuscode');
    });
});
