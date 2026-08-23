import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import {
    resolveSingleGroupRowActions,
    resolveOperationalGroups,
    hasMultipleOperationalGroups,
    shouldShowSchedule,
    shouldShowPay,
    toggleSort,
    canScheduleGroupStatus,
    canPayGroupStatus,
    canReturnGroupStatus,
    resolveGroupFinanceButtons,
    resolveObligationRowFlags,
    resolveObligationActionPlan,
    obligationActionLabel,
    resolveNoteTooltip,
    FINANCE_DEFAULT_SORT,
    FINANCE_CLEAR_KEYS,
    FINANCE_SORT_OPTIONS,
    countAdvancedFilters,
    isAdvanceGroupStatus,
    resolveAttachmentUploadParams,
    resolveParentDisplayStatus,
    resolveScheduledPaymentDetails,
    resolveAdvancePaymentDetails,
    resolveSoleGroupActionLabels,
    canCancelScheduleGroupStatus,
    shouldShowCancelSchedule,
    formatBusinessDateOnly,
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

describe('shouldShowCancelSchedule (single-group kebab menu)', () => {
    test('renders when eligible AND resolvable group AND that group is currently scheduled', () => {
        const item = { availableFinanceActions: ['CANCEL_SCHEDULE'], poGroups: [{ id: 'g1', status: 'PAYMENT_SCHEDULED' }] };
        assert.equal(shouldShowCancelSchedule(item), true);
    });

    test('renders for an advance-scheduled group', () => {
        const item = { availableFinanceActions: ['CANCEL_SCHEDULE'], poGroups: [{ id: 'g1', status: 'ADVANCE_PAYMENT_SCHEDULED' }] };
        assert.equal(shouldShowCancelSchedule(item), true);
    });

    test('does not render when the resolved group itself is not in a scheduled status, even if the action code is present', () => {
        // Regression guard: eligibility must be double-gated by the group's OWN status, not just
        // the flat request-level action list, mirroring the multi-group cards' pattern.
        const item = { availableFinanceActions: ['CANCEL_SCHEDULE'], poGroups: [{ id: 'g1', status: 'PO_ISSUED' }] };
        assert.equal(shouldShowCancelSchedule(item), false);
    });

    test('does not render when not eligible, regardless of group status', () => {
        const item = { availableFinanceActions: [], poGroups: [{ id: 'g1', status: 'PAYMENT_SCHEDULED' }] };
        assert.equal(shouldShowCancelSchedule(item), false);
    });

    test('does not render when no group id is resolvable', () => {
        const item = { availableFinanceActions: ['CANCEL_SCHEDULE'], poGroups: [] };
        assert.equal(shouldShowCancelSchedule(item), false);
    });
});

describe('canScheduleGroupStatus / canPayGroupStatus / isAdvanceGroupStatus (per-group, multi-group sub-row)', () => {
    // Test matrix item 1: PO_ISSUED — can schedule and can pay; not advance.
    test('PO_ISSUED: schedulable, payable, not advance', () => {
        assert.equal(canScheduleGroupStatus('PO_ISSUED'), true);
        assert.equal(canPayGroupStatus('PO_ISSUED'), true);
        assert.equal(isAdvanceGroupStatus('PO_ISSUED'), false);
    });

    // Test matrix item 2: PAYMENT_REQUEST_SENT — matches backend eligibility (schedulable + payable, not advance).
    test('PAYMENT_REQUEST_SENT: schedulable, payable, not advance', () => {
        assert.equal(canScheduleGroupStatus('PAYMENT_REQUEST_SENT'), true);
        assert.equal(canPayGroupStatus('PAYMENT_REQUEST_SENT'), true);
        assert.equal(isAdvanceGroupStatus('PAYMENT_REQUEST_SENT'), false);
    });

    // Test matrix item 3: PAYMENT_SCHEDULED — can pay (not schedulable again, not advance).
    test('PAYMENT_SCHEDULED: payable, not schedulable, not advance', () => {
        assert.equal(canScheduleGroupStatus('PAYMENT_SCHEDULED'), false);
        assert.equal(canPayGroupStatus('PAYMENT_SCHEDULED'), true);
        assert.equal(isAdvanceGroupStatus('PAYMENT_SCHEDULED'), false);
    });

    // Test matrix item 4: ADVANCE_PAYMENT_REQUIRED — can schedule, can pay directly, is advance.
    test('ADVANCE_PAYMENT_REQUIRED: schedulable, payable, is advance', () => {
        assert.equal(canScheduleGroupStatus('ADVANCE_PAYMENT_REQUIRED'), true);
        assert.equal(canPayGroupStatus('ADVANCE_PAYMENT_REQUIRED'), true);
        assert.equal(isAdvanceGroupStatus('ADVANCE_PAYMENT_REQUIRED'), true);
    });

    // Test matrix item 5: ADVANCE_PAYMENT_SCHEDULED — can pay (not schedulable again), is advance.
    test('ADVANCE_PAYMENT_SCHEDULED: payable, not schedulable, is advance', () => {
        assert.equal(canScheduleGroupStatus('ADVANCE_PAYMENT_SCHEDULED'), false);
        assert.equal(canPayGroupStatus('ADVANCE_PAYMENT_SCHEDULED'), true);
        assert.equal(isAdvanceGroupStatus('ADVANCE_PAYMENT_SCHEDULED'), true);
    });

    // Test matrix item 6: terminal statuses — no schedule, no pay action.
    test('PAYMENT_COMPLETED / ADVANCE_PAYMENT_COMPLETED: no schedule, no pay', () => {
        for (const status of ['PAYMENT_COMPLETED', 'ADVANCE_PAYMENT_COMPLETED']) {
            assert.equal(canScheduleGroupStatus(status), false, status);
            assert.equal(canPayGroupStatus(status), false, status);
        }
    });

    // Test matrix item 11: dead/stale status strings (never valid RequestConstants values) no
    // longer influence eligibility — regression guard for the removed PO_CONFIRMED/PAYMENT_REQUIRED
    // inline strings.
    test('dead status strings (PO_CONFIRMED, PAYMENT_REQUIRED) are not eligible for anything', () => {
        for (const status of ['PO_CONFIRMED', 'PAYMENT_REQUIRED']) {
            assert.equal(canScheduleGroupStatus(status), false, status);
            assert.equal(canPayGroupStatus(status), false, status);
            assert.equal(isAdvanceGroupStatus(status), false, status);
        }
    });

    test('null/undefined/empty status is never eligible', () => {
        for (const status of [null, undefined, '']) {
            assert.equal(canScheduleGroupStatus(status), false);
            assert.equal(canPayGroupStatus(status), false);
            assert.equal(isAdvanceGroupStatus(status), false);
        }
    });
});

describe('canCancelScheduleGroupStatus (per-group, mirrors backend CanCancelSchedule)', () => {
    test('PAYMENT_SCHEDULED and ADVANCE_PAYMENT_SCHEDULED are cancellable', () => {
        assert.equal(canCancelScheduleGroupStatus('PAYMENT_SCHEDULED'), true);
        assert.equal(canCancelScheduleGroupStatus('ADVANCE_PAYMENT_SCHEDULED'), true);
    });

    test('every other status, including completed/terminal ones, is not cancellable', () => {
        for (const status of ['PO_ISSUED', 'PAYMENT_REQUEST_SENT', 'ADVANCE_PAYMENT_REQUIRED', 'PAYMENT_COMPLETED', 'ADVANCE_PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'COMPLETED', 'PENDING']) {
            assert.equal(canCancelScheduleGroupStatus(status), false, status);
        }
    });

    test('null/undefined/empty status is never eligible', () => {
        for (const status of [null, undefined, '']) {
            assert.equal(canCancelScheduleGroupStatus(status), false);
        }
    });
});

// 2026-07-25 Finance Payments UI regression: the single-group kebab menu's Schedule/Pay labels,
// derived from the resolved sole group's own status — same terminology the multi-group cards use.
describe('resolveSoleGroupActionLabels (single-group kebab menu terminology)', () => {
    test('normal group status -> "Agendar pagamento" / "Marcar como pago"', () => {
        const result = resolveSoleGroupActionLabels('PO_ISSUED');
        assert.equal(result.scheduleLabel, 'Agendar pagamento');
        assert.equal(result.payLabel, 'Marcar como pago');
    });

    test('advance group status -> "Agendar adiantamento" / "Registrar adiantamento pago"', () => {
        const result = resolveSoleGroupActionLabels('ADVANCE_PAYMENT_REQUIRED');
        assert.equal(result.scheduleLabel, 'Agendar adiantamento');
        assert.equal(result.payLabel, 'Registrar adiantamento pago');
    });

    test('ADVANCE_PAYMENT_SCHEDULED (still advance, only Pay remains eligible) -> advance-aware Pay label', () => {
        const result = resolveSoleGroupActionLabels('ADVANCE_PAYMENT_SCHEDULED');
        assert.equal(result.payLabel, 'Registrar adiantamento pago');
    });

    test('null/undefined status -> normal (non-advance) labels, never throws', () => {
        assert.doesNotThrow(() => resolveSoleGroupActionLabels(null));
        assert.equal(resolveSoleGroupActionLabels(null).scheduleLabel, 'Agendar pagamento');
        assert.equal(resolveSoleGroupActionLabels(undefined).payLabel, 'Marcar como pago');
    });

    test('PAYMENT_SCHEDULED -> "Cancelar agendamento"', () => {
        assert.equal(resolveSoleGroupActionLabels('PAYMENT_SCHEDULED').cancelScheduleLabel, 'Cancelar agendamento');
    });

    test('ADVANCE_PAYMENT_SCHEDULED -> "Cancelar agendamento de adiantamento"', () => {
        assert.equal(resolveSoleGroupActionLabels('ADVANCE_PAYMENT_SCHEDULED').cancelScheduleLabel, 'Cancelar agendamento de adiantamento');
    });
});

describe('formatBusinessDateOnly (timezone-proof calendar-date formatting)', () => {
    // 2026-07-26 regression: RequestPayment.ScheduledDateUtc round-trips through SQL Server's
    // datetime2 (no offset metadata) -> System.Text.Json emits it WITHOUT a trailing 'Z' -> a naive
    // `new Date(str)` on a datetime string without an offset is parsed as LOCAL time per ECMA-262
    // (unlike a date-ONLY string, which is parsed as UTC) -> combined with { timeZone: 'UTC' }
    // display formatting, this silently shifted the displayed calendar day by the browser's UTC
    // offset. Confirmed via reproduction: on a UTC+1 host, "2026-07-24T00:00:00" (no Z) rendered as
    // 23/07/2026 instead of the correct 24/07/2026 — the exact boundary reported by Finance.
    test('the exact 23/07-vs-24/07 regression boundary: a no-Z datetime string always yields the stored calendar day', () => {
        assert.equal(formatBusinessDateOnly('2026-07-24T00:00:00'), '24/07/2026');
    });

    test('a Z-suffixed datetime string yields the same calendar day (no double conversion)', () => {
        assert.equal(formatBusinessDateOnly('2026-07-24T00:00:00.000Z'), '24/07/2026');
    });

    test('a date-only string (no time component) yields the same calendar day', () => {
        assert.equal(formatBusinessDateOnly('2026-07-24'), '24/07/2026');
    });

    test('null/undefined/empty -> "---"', () => {
        assert.equal(formatBusinessDateOnly(null), '---');
        assert.equal(formatBusinessDateOnly(undefined), '---');
        assert.equal(formatBusinessDateOnly(''), '---');
    });

    test('malformed string -> "---", never throws', () => {
        assert.doesNotThrow(() => formatBusinessDateOnly('not-a-date'));
        assert.equal(formatBusinessDateOnly('not-a-date'), '---');
    });
});

describe('resolveAttachmentUploadParams (group-scoped attachment upload for SCHEDULE/PAY)', () => {
    const NCR_GROUP_ID = 'ncr-group-id';
    const ITEC_GROUP_ID = 'itec-group-id';

    // Test matrix item 12: normal payment upload passes the selected NCR group id.
    test('PAY with a file passes PAYMENT_PROOF and the selected (NCR) group id', () => {
        const result = resolveAttachmentUploadParams('PAY', true, NCR_GROUP_ID);
        assert.deepEqual(result, { typeCode: 'PAYMENT_PROOF', poGroupId: NCR_GROUP_ID });
    });

    // Test matrix item 13: advance payment upload passes the selected ITEC group id.
    test('PAY with a file passes PAYMENT_PROOF and the selected (ITEC) group id', () => {
        const result = resolveAttachmentUploadParams('PAY', true, ITEC_GROUP_ID);
        assert.deepEqual(result, { typeCode: 'PAYMENT_PROOF', poGroupId: ITEC_GROUP_ID });
    });

    // Test matrix item 14: normal scheduling upload passes the selected NCR group id.
    test('SCHEDULE with a file passes PAYMENT_SCHEDULE and the selected (NCR) group id', () => {
        const result = resolveAttachmentUploadParams('SCHEDULE', true, NCR_GROUP_ID);
        assert.deepEqual(result, { typeCode: 'PAYMENT_SCHEDULE', poGroupId: NCR_GROUP_ID });
    });

    // Test matrix item 15: advance scheduling upload passes the selected ITEC group id.
    test('SCHEDULE with a file passes PAYMENT_SCHEDULE and the selected (ITEC) group id', () => {
        const result = resolveAttachmentUploadParams('SCHEDULE', true, ITEC_GROUP_ID);
        assert.deepEqual(result, { typeCode: 'PAYMENT_SCHEDULE', poGroupId: ITEC_GROUP_ID });
    });

    // Test matrix item 16: scheduling without a file must not attempt an upload at all.
    test('SCHEDULE without a file returns null — no attachments.upload call should happen', () => {
        assert.equal(resolveAttachmentUploadParams('SCHEDULE', false, NCR_GROUP_ID), null);
    });

    test('PAY without a file returns null — no attachments.upload call should happen', () => {
        assert.equal(resolveAttachmentUploadParams('PAY', false, NCR_GROUP_ID), null);
    });

    // Test matrix item 17: normal and advance uploads keep the existing, shared type codes —
    // there is no separate ADVANCE_PAYMENT_PROOF/advance schedule type (see diagnosis: that
    // constant is dead code and ConfirmAdvancePayment explicitly expects PAYMENT_PROOF).
    test('PAY always resolves PAYMENT_PROOF regardless of which group is selected (normal or advance)', () => {
        assert.equal(resolveAttachmentUploadParams('PAY', true, NCR_GROUP_ID).typeCode, 'PAYMENT_PROOF');
        assert.equal(resolveAttachmentUploadParams('PAY', true, ITEC_GROUP_ID).typeCode, 'PAYMENT_PROOF');
    });

    test('SCHEDULE always resolves PAYMENT_SCHEDULE regardless of which group is selected (normal or advance)', () => {
        assert.equal(resolveAttachmentUploadParams('SCHEDULE', true, NCR_GROUP_ID).typeCode, 'PAYMENT_SCHEDULE');
        assert.equal(resolveAttachmentUploadParams('SCHEDULE', true, ITEC_GROUP_ID).typeCode, 'PAYMENT_SCHEDULE');
    });

    // Test matrix item 18 / diagnosis item 5: switching the acting group between calls must never
    // leak the previous group's id — the function is pure and stateless (no module-level mutable
    // state, no parent-status input to derive a wrong answer from), so consecutive calls with
    // different group ids must be fully independent. This is also the concrete regression guard
    // for the original bug: an NCR action must never resolve ITEC's id, or vice versa.
    test('consecutive calls for different groups never leak the previous group id', () => {
        const forNcr = resolveAttachmentUploadParams('PAY', true, NCR_GROUP_ID);
        const forItec = resolveAttachmentUploadParams('PAY', true, ITEC_GROUP_ID);
        const forNcrAgain = resolveAttachmentUploadParams('PAY', true, NCR_GROUP_ID);

        assert.equal(forNcr.poGroupId, NCR_GROUP_ID);
        assert.notEqual(forNcr.poGroupId, ITEC_GROUP_ID);
        assert.equal(forItec.poGroupId, ITEC_GROUP_ID);
        assert.notEqual(forItec.poGroupId, NCR_GROUP_ID);
        assert.equal(forNcrAgain.poGroupId, NCR_GROUP_ID);
    });

    // No group resolvable (defensive — should not happen once a modal is opened from a group
    // button, but the function must not throw and must not silently invent an id).
    test('null groupId resolves poGroupId to undefined, never a stale/guessed id', () => {
        const result = resolveAttachmentUploadParams('PAY', true, null);
        assert.equal(result.poGroupId, undefined);
    });

    // RETURN/NOTE never involve an attachment upload.
    test('RETURN and NOTE never resolve upload params, even with a file present', () => {
        assert.equal(resolveAttachmentUploadParams('RETURN', true, NCR_GROUP_ID), null);
        assert.equal(resolveAttachmentUploadParams('NOTE', true, NCR_GROUP_ID), null);
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

// Full bucket-resolution coverage (mixed/passthrough/single-bucket-different-codes/CANCELLED/
// fallback rules) now lives in requestGroupDisplayState.test.mjs, alongside the shared module's
// implementation — this is a re-export smoke test only, proving financePaymentsView.ts's
// backward-compatible re-export still points at a working implementation.
describe('resolveParentDisplayStatus (re-export smoke test)', () => {
    test('request 100 shape: NCR PAYMENT_SCHEDULED + ITEC ADVANCE_PAYMENT_COMPLETED -> "Pagamentos em andamento", not the raw aggregate', () => {
        const result = resolveParentDisplayStatus(
            [
                { id: 'ncr', status: 'PAYMENT_SCHEDULED' },
                { id: 'itec', status: 'ADVANCE_PAYMENT_COMPLETED' },
            ],
            'Adiantamento Realizado'
        );
        assert.equal(result.label, 'Pagamentos em andamento');
        assert.equal(result.bucket, 'MIXED');
    });

    test('no active groups -> preserves the existing safe fallback label', () => {
        const result = resolveParentDisplayStatus([], 'Aguardando Cotação');
        assert.equal(result.label, 'Aguardando Cotação');
        assert.equal(result.bucket, null);
    });
});

describe('resolveScheduledPaymentDetails', () => {
    test('resolves the FINAL_BALANCE payment scheduled date and planned amount', () => {
        const result = resolveScheduledPaymentDetails([
            { paymentType: 'ADVANCE', plannedAmount: 100, scheduledDateUtc: '2026-01-01T00:00:00Z' },
            { paymentType: 'FINAL_BALANCE', plannedAmount: 70341.42, scheduledDateUtc: '2026-08-05T00:00:00Z' },
        ]);
        assert.equal(result.scheduledDateUtc, '2026-08-05T00:00:00Z');
        assert.equal(result.plannedAmount, 70341.42);
    });

    test('no FINAL_BALANCE payment present -> nulls, no throw', () => {
        assert.doesNotThrow(() => resolveScheduledPaymentDetails([{ paymentType: 'ADVANCE', plannedAmount: 100 }]));
        const result = resolveScheduledPaymentDetails([{ paymentType: 'ADVANCE', plannedAmount: 100 }]);
        assert.equal(result.scheduledDateUtc, null);
        assert.equal(result.plannedAmount, null);
    });

    test('missing/undefined payments array -> nulls, no throw', () => {
        assert.doesNotThrow(() => resolveScheduledPaymentDetails(undefined));
        const result = resolveScheduledPaymentDetails(undefined);
        assert.equal(result.plannedAmount, null);
    });
});

describe('resolveAdvancePaymentDetails', () => {
    test('resolves the ADVANCE payment paid date, actual amount', () => {
        const result = resolveAdvancePaymentDetails([
            { paymentType: 'ADVANCE', plannedAmount: 275139.00, actualPaidAmount: 275139.00, paidDateUtc: '2026-07-10T00:00:00Z' },
            { paymentType: 'FINAL_BALANCE', plannedAmount: 0, scheduledDateUtc: null },
        ]);
        assert.equal(result.paidDateUtc, '2026-07-10T00:00:00Z');
        assert.equal(result.actualPaidAmount, 275139.00);
    });

    test('no ADVANCE payment present -> nulls, no throw', () => {
        const result = resolveAdvancePaymentDetails([{ paymentType: 'FINAL_BALANCE', plannedAmount: 100 }]);
        assert.equal(result.paidDateUtc, null);
        assert.equal(result.actualPaidAmount, null);
    });
});

describe('resolveGroupFinanceButtons (v2.230.0 per-group gating)', () => {
    test('prefers server financeActions — PO_ISSUED group exposes schedule/pay/return', () => {
        const r = resolveGroupFinanceButtons({ status: 'PO_ISSUED', financeActions: ['SCHEDULE', 'PAY', 'RETURN'] });
        assert.deepEqual(r, { schedule: true, pay: true, cancelSchedule: false, return: true });
    });

    test('paid sibling exposes no buttons (empty server array is authoritative)', () => {
        const r = resolveGroupFinanceButtons({ status: 'PAYMENT_COMPLETED', financeActions: [] });
        assert.deepEqual(r, { schedule: false, pay: false, cancelSchedule: false, return: false });
    });

    test('scheduled group exposes pay + cancel + return (not schedule)', () => {
        const r = resolveGroupFinanceButtons({ status: 'PAYMENT_SCHEDULED', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] });
        assert.deepEqual(r, { schedule: false, pay: true, cancelSchedule: true, return: true });
    });

    test('REQ-100 shape: each group judged independently — a paid sibling never suppresses the actionable one', () => {
        const paid = resolveGroupFinanceButtons({ status: 'PAYMENT_COMPLETED', financeActions: [] });
        const actionable = resolveGroupFinanceButtons({ status: 'PO_ISSUED', financeActions: ['SCHEDULE', 'PAY', 'RETURN'] });
        assert.equal(paid.pay, false);
        assert.equal(actionable.schedule, true);
        assert.equal(actionable.pay, true);
        assert.equal(actionable.return, true);
    });

    test('fallback to status when financeActions absent', () => {
        const r = resolveGroupFinanceButtons({ status: 'PO_ISSUED' });
        assert.equal(r.schedule, true);
        assert.equal(r.pay, true);
        assert.equal(r.return, true);
        assert.equal(r.cancelSchedule, false);
    });

    test('WAITING_PO group is never Finance-actionable (Buyer responsibility)', () => {
        const server = resolveGroupFinanceButtons({ status: 'WAITING_PO', financeActions: [] });
        const fallback = resolveGroupFinanceButtons({ status: 'WAITING_PO' });
        assert.deepEqual(server, { schedule: false, pay: false, cancelSchedule: false, return: false });
        assert.deepEqual(fallback, { schedule: false, pay: false, cancelSchedule: false, return: false });
    });
});

describe('canReturnGroupStatus', () => {
    test('PO_ISSUED and PAYMENT_SCHEDULED only', () => {
        assert.equal(canReturnGroupStatus('PO_ISSUED'), true);
        assert.equal(canReturnGroupStatus('PAYMENT_SCHEDULED'), true);
        assert.equal(canReturnGroupStatus('PAYMENT_COMPLETED'), false);
        assert.equal(canReturnGroupStatus('WAITING_PO'), false);
        assert.equal(canReturnGroupStatus(null), false);
    });
});

describe('resolveObligationRowFlags (Phase 4 row visual state)', () => {
    test('paid obligation is muted with no finance buttons', () => {
        const f = resolveObligationRowFlags({ actionClass: 'PAID_WAITING_RECEIVING', isOverdue: false });
        assert.deepEqual(f, { isPaid: true, isNoFinance: false, isOverdue: false, muted: true });
    });
    test('overdue NEEDS_PAYMENT flagged overdue, not muted', () => {
        const f = resolveObligationRowFlags({ actionClass: 'NEEDS_PAYMENT', isOverdue: true });
        assert.equal(f.isOverdue, true);
        assert.equal(f.muted, false);
        assert.equal(f.isPaid, false);
    });
    test('WAITING_PO (NO_FINANCE_ACTION) flagged as no-finance, not muted', () => {
        const f = resolveObligationRowFlags({ actionClass: 'NO_FINANCE_ACTION', isOverdue: false });
        assert.equal(f.isNoFinance, true);
        assert.equal(f.isPaid, false);
    });
    test('NEEDS_SCHEDULING is a normal actionable row', () => {
        const f = resolveObligationRowFlags({ actionClass: 'NEEDS_SCHEDULING', isOverdue: false });
        assert.deepEqual(f, { isPaid: false, isNoFinance: false, isOverdue: false, muted: false });
    });
    test('COMPLETED counts as paid/muted', () => {
        const f = resolveObligationRowFlags({ actionClass: 'COMPLETED', isOverdue: false });
        assert.equal(f.isPaid, true);
        assert.equal(f.muted, true);
    });
});

describe('resolveObligationActionPlan (Phase-4 action hierarchy: 1 primary + kebab)', () => {
    test('PO_ISSUED → primary Agendar; kebab Detalhes, Obs, Pagar, Devolver', () => {
        const p = resolveObligationActionPlan({ groupStatusCode: 'PO_ISSUED', financeActions: ['SCHEDULE', 'PAY', 'RETURN'] });
        assert.deepEqual(p.primary, { action: 'SCHEDULE', label: 'Agendar pagamento' });
        assert.deepEqual(p.menu, ['DETAILS', 'NOTE', 'PAY', 'RETURN']);
    });
    test('PAYMENT_SCHEDULED → primary Pagar; kebab Detalhes, Obs, Cancelar, Devolver', () => {
        const p = resolveObligationActionPlan({ groupStatusCode: 'PAYMENT_SCHEDULED', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] });
        assert.deepEqual(p.primary, { action: 'PAY', label: 'Pagar' });
        assert.deepEqual(p.menu, ['DETAILS', 'NOTE', 'CANCEL_SCHEDULE', 'RETURN']);
    });
    test('ADVANCE_PAYMENT_REQUIRED → primary Agendar adiantamento; kebab has Pagar adiantamento', () => {
        const p = resolveObligationActionPlan({ groupStatusCode: 'ADVANCE_PAYMENT_REQUIRED', financeActions: ['SCHEDULE', 'PAY'] });
        assert.deepEqual(p.primary, { action: 'SCHEDULE', label: 'Agendar adiantamento' });
        assert.deepEqual(p.menu, ['DETAILS', 'NOTE', 'PAY']);
        assert.equal(obligationActionLabel('PAY', true), 'Pagar adiantamento');
    });
    test('PAYMENT_COMPLETED → no primary; kebab only Detalhes + Adicionar observação', () => {
        const p = resolveObligationActionPlan({ groupStatusCode: 'PAYMENT_COMPLETED', financeActions: [] });
        assert.equal(p.primary, null);
        assert.deepEqual(p.menu, ['DETAILS', 'NOTE']);
    });
    test('WAITING_PO → no primary; kebab only Detalhes + Adicionar observação', () => {
        const p = resolveObligationActionPlan({ groupStatusCode: 'WAITING_PO', financeActions: [] });
        assert.equal(p.primary, null);
        assert.deepEqual(p.menu, ['DETAILS', 'NOTE']);
    });
    test('multi-group independence: paid sibling and actionable sibling produce different plans', () => {
        const paid = resolveObligationActionPlan({ groupStatusCode: 'PAYMENT_COMPLETED', financeActions: [] });
        const actionable = resolveObligationActionPlan({ groupStatusCode: 'PO_ISSUED', financeActions: ['SCHEDULE', 'PAY', 'RETURN'] });
        assert.equal(paid.primary, null);
        assert.equal(actionable.primary.action, 'SCHEDULE');
        assert.ok(actionable.menu.includes('RETURN'));
        assert.ok(!paid.menu.includes('RETURN'));
    });
    test('menu labels are advance-aware and correct', () => {
        assert.equal(obligationActionLabel('DETAILS', false), 'Detalhes');
        assert.equal(obligationActionLabel('NOTE', false), 'Adicionar observação');
        assert.equal(obligationActionLabel('CANCEL_SCHEDULE', false), 'Cancelar agendamento');
        assert.equal(obligationActionLabel('RETURN', false), 'Devolver para ajuste');
    });
});

describe('Phase-5 helpers (sort defaults, clear keys, note tooltip)', () => {
    test('default sort is newest', () => {
        assert.equal(FINANCE_DEFAULT_SORT, 'newest');
        assert.deepEqual(FINANCE_SORT_OPTIONS.map(o => o.value), ['newest', 'oldest']);
    });
    test('clear keys reset every filter AND sort (so sort returns to default)', () => {
        for (const k of ['search', 'actionClass', 'currencyCode', 'companyId', 'plantId', 'departmentId', 'actionableOnly', 'overdueOnly', 'dueTodayOnly', 'sortBy']) {
            assert.ok(FINANCE_CLEAR_KEYS.includes(k), `missing ${k}`);
        }
    });
    test('note tooltip hidden when no notes', () => {
        assert.equal(resolveNoteTooltip({ hasNotes: false, noteCount: 0 }), null);
        assert.equal(resolveNoteTooltip({ hasNotes: true, noteCount: 0, latestNoteText: null }), null);
    });
    test('single note → "Observação" + text, no extra', () => {
        const t = resolveNoteTooltip({ hasNotes: true, noteCount: 1, latestNoteText: 'Verificar câmbio' });
        assert.equal(t.title, 'Observação');
        assert.equal(t.body, 'Verificar câmbio');
        assert.equal(t.extra, null);
    });
    test('multiple notes → "Última observação" + latest + count of previous', () => {
        const t = resolveNoteTooltip({ hasNotes: true, noteCount: 3, latestNoteText: 'Última nota' });
        assert.equal(t.title, 'Última observação');
        assert.equal(t.body, 'Última nota');
        assert.equal(t.extra, '+2 observações anteriores');
    });
    test('exactly two notes → singular "anterior"', () => {
        const t = resolveNoteTooltip({ hasNotes: true, noteCount: 2, latestNoteText: 'X' });
        assert.equal(t.extra, '+1 observação anterior');
    });
});

describe('countAdvancedFilters (Mais filtros badge)', () => {
    test('none active → 0', () => {
        assert.equal(countAdvancedFilters({}), 0);
    });
    test('counts company, plant, department, currency, actionable, overdue', () => {
        assert.equal(countAdvancedFilters({ companyId: 1 }), 1);
        assert.equal(countAdvancedFilters({ companyId: 1, plantId: 2 }), 2);
        assert.equal(countAdvancedFilters({ companyId: 1, departmentId: 3, currencyCode: 'AOA' }), 3);
        assert.equal(countAdvancedFilters({ companyId: 1, plantId: 2, departmentId: 3, currencyCode: 'EUR', actionableOnly: true, overdueOnly: true }), 6);
    });
    test('does NOT count primary-toolbar controls (search/situação/ordenar are not passed here)', () => {
        // Only advanced fields are inputs; actionClass/search/sort are excluded by construction.
        assert.equal(countAdvancedFilters({ actionableOnly: false, overdueOnly: false }), 0);
    });
});
