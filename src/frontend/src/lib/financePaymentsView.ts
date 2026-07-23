/**
 * Pure, testable logic for the Finance > Payments page (FinancePaymentsList.tsx):
 * row-action visibility and the sort-toggle state machine. Extracted so eligibility rendering
 * and sort toggling can be unit-tested without a component-rendering test runner, and so the
 * component itself does not re-implement business eligibility rules independently of the
 * backend's AvailableFinanceActions (see IFinancePaymentEligibilityService on the backend).
 *
 * Group-aware display-state logic (bucket resolution, parent label, per-group friendly labels) now
 * lives in requestGroupDisplayState.ts, shared with the Requests Explorer / Request drawer — this
 * file re-exports it so existing imports here keep working.
 */
import type { DisplayGroupSummary, DisplayBucket, GroupAwareDisplayStatus } from './requestGroupDisplayState.ts';
import { resolveOperationalGroups, hasMultipleOperationalGroups, resolveParentDisplayStatus, resolveGroupStatusLabel, GROUP_STATUS_LABELS } from './requestGroupDisplayState.ts';

export type { DisplayGroupSummary, DisplayBucket, GroupAwareDisplayStatus };
export { resolveOperationalGroups, hasMultipleOperationalGroups, resolveParentDisplayStatus, resolveGroupStatusLabel, GROUP_STATUS_LABELS };

/** Alias kept for the existing call sites in this file — identical shape to DisplayGroupSummary. */
export type FinancePoGroupSummary = DisplayGroupSummary;
/** Alias kept for backward compatibility with existing imports. */
export type FinanceDisplayBucket = DisplayBucket;
/** Alias kept for backward compatibility with existing imports. */
export type FinanceParentDisplayStatus = GroupAwareDisplayStatus;

export interface FinanceRowActionSource {
    availableFinanceActions: string[];
    poGroups?: FinancePoGroupSummary[] | null;
}

export interface FinanceRowActions {
    /** Backend eligibility, exactly as reported — never re-derived from poGroups length. */
    canSchedule: boolean;
    canPay: boolean;
    canCancelSchedule: boolean;
    /** The single operational group id to act on, or null when it cannot be unambiguously determined. */
    groupId: string | null;
}

/**
 * Resolves the single-group, top-level row actions (used when the row has exactly one operational
 * PoGroup; rows with multiple operational groups keep using their existing per-group sub-row UI,
 * which already reads each group's own status directly and is unaffected by this helper).
 *
 * canSchedule/canPay come ONLY from availableFinanceActions — never from poGroups length. A
 * missing groupId (no operational group present) is treated as an execution/input problem: the
 * action is still reported as eligible, but the caller must not render a button it cannot execute
 * without a group id.
 */
export function resolveSingleGroupRowActions(item: FinanceRowActionSource): FinanceRowActions {
    const actions = item.availableFinanceActions || [];
    const operationalGroups = resolveOperationalGroups(item.poGroups);
    const groupId = operationalGroups.length === 1 ? operationalGroups[0].id : null;

    return {
        canSchedule: actions.includes('SCHEDULE'),
        canPay: actions.includes('PAY'),
        canCancelSchedule: actions.includes('CANCEL_SCHEDULE'),
        groupId,
    };
}

/** Whether the SCHEDULE button should actually render — eligible AND a group id is executable. */
export function shouldShowSchedule(item: FinanceRowActionSource): boolean {
    const { canSchedule, groupId } = resolveSingleGroupRowActions(item);
    return canSchedule && groupId !== null;
}

/** Whether the PAY button should actually render — eligible AND a group id is executable. */
export function shouldShowPay(item: FinanceRowActionSource): boolean {
    const { canPay, groupId } = resolveSingleGroupRowActions(item);
    return canPay && groupId !== null;
}

/**
 * Whether the CANCEL_SCHEDULE button should actually render for the single-group row — eligible
 * at the request level AND a group id is executable AND that resolved group's OWN status is
 * currently scheduled (mirrors canCancelScheduleGroupStatus, same double-gate pattern the
 * multi-group cards use for SCHEDULE/PAY, so a sibling group's status can never leak in — though
 * for the single-operational-group row there is by definition no sibling).
 */
export function shouldShowCancelSchedule(item: FinanceRowActionSource): boolean {
    const { canCancelSchedule, groupId } = resolveSingleGroupRowActions(item);
    if (!canCancelSchedule || groupId === null) return false;
    const group = (item.poGroups || []).find(g => g.id === groupId);
    return canCancelScheduleGroupStatus(group?.status);
}

/**
 * Per-group eligibility for the multi-group sub-row (FinancePaymentsList.tsx's "Pagamentos por
 * Fornecedor" panel). Unlike resolveSingleGroupRowActions (which reads the request-level
 * availableFinanceActions), these read a single RequestPoGroup's own status directly — required
 * because a request-level flag or a sibling group's status must never determine whether THIS
 * group's action renders or which backend endpoint it calls (the confirmed request-100 bug: the
 * ITEC group being ADVANCE_PAYMENT_REQUIRED must never affect the NCR group, and vice versa).
 *
 * Mirrors FinancePaymentEligibilityService's canonical lists on the backend exactly — update both
 * sides together if the backend guard changes.
 */
const SCHEDULABLE_GROUP_STATUSES = ['PO_ISSUED', 'PAYMENT_REQUEST_SENT', 'ADVANCE_PAYMENT_REQUIRED'];
const PAYABLE_GROUP_STATUSES = ['PO_ISSUED', 'PAYMENT_REQUEST_SENT', 'PAYMENT_SCHEDULED', 'ADVANCE_PAYMENT_REQUIRED', 'ADVANCE_PAYMENT_SCHEDULED'];
const ADVANCE_GROUP_STATUSES = ['ADVANCE_PAYMENT_REQUIRED', 'ADVANCE_PAYMENT_SCHEDULED'];
/** Mirrors FinancePaymentEligibilityService.CanCancelSchedule's CancellableScheduledGroupStatuses. */
const CANCELLABLE_SCHEDULE_GROUP_STATUSES = ['PAYMENT_SCHEDULED', 'ADVANCE_PAYMENT_SCHEDULED'];

/** Mirrors FinancePaymentEligibilityService.CanSchedule's SchedulableGroupStatuses. */
export function canScheduleGroupStatus(status: string | null | undefined): boolean {
    return status != null && SCHEDULABLE_GROUP_STATUSES.includes(status);
}

/** Mirrors FinancePaymentEligibilityService.CanPay's PayableGroupStatusesForQuotation. */
export function canPayGroupStatus(status: string | null | undefined): boolean {
    return status != null && PAYABLE_GROUP_STATUSES.includes(status);
}

/**
 * Whether this specific group's direct-payment action must route through the advance-payment
 * endpoints (confirmAdvancePayment) instead of the normal payment endpoint (markAsPaid).
 * Group-scoped by design — never derive this from the parent request's aggregated statusCode.
 */
export function isAdvanceGroupStatus(status: string | null | undefined): boolean {
    return status != null && ADVANCE_GROUP_STATUSES.includes(status);
}

/**
 * Whether THIS group's own status makes it eligible for "Cancelar agendamento" — mirrors
 * FinancePaymentEligibilityService.CanCancelSchedule exactly (group-status-only, no type
 * branching: PAYMENT_SCHEDULED/ADVANCE_PAYMENT_SCHEDULED are always genuinely-written values).
 * Group-scoped by design — a sibling group's status must never affect this.
 */
export function canCancelScheduleGroupStatus(status: string | null | undefined): boolean {
    return status != null && CANCELLABLE_SCHEDULE_GROUP_STATUSES.includes(status);
}

export interface SoleGroupActionLabels {
    scheduleLabel: string;
    payLabel: string;
    cancelScheduleLabel: string;
}

/**
 * Friendly Schedule/Pay/Cancel-schedule labels for the single-group row's kebab menu, derived from
 * that one resolved group's own status — never the parent request's aggregated statusCode. Mirrors
 * the multi-group "Pagamentos por Fornecedor" cards' existing isAdvanceGroupStatus ternary exactly,
 * so both surfaces use identical terminology for the same group state.
 */
export function resolveSoleGroupActionLabels(status: string | null | undefined): SoleGroupActionLabels {
    const advance = isAdvanceGroupStatus(status);
    return {
        scheduleLabel: advance ? 'Agendar adiantamento' : 'Agendar pagamento',
        payLabel: advance ? 'Registrar adiantamento pago' : 'Marcar como pago',
        cancelScheduleLabel: advance ? 'Cancelar agendamento de adiantamento' : 'Cancelar agendamento',
    };
}

export interface AttachmentUploadParams {
    typeCode: string;
    poGroupId?: string;
}

/**
 * Resolves the attachment-type code and RequestPoGroupId for the optional file upload that may
 * precede a Finance SCHEDULE/PAY action. Returns null when no upload should be attempted — either
 * no file was selected, or the action doesn't involve an attachment (RETURN/NOTE). Pure and
 * stateless: the caller supplies the acting group's id fresh on every call (from actionModal.groupId,
 * itself replaced as a whole object on every group selection), so there is no risk of one group's
 * upload leaking a sibling's id — see FinancePaymentsList.handleConfirmAction.
 */
export function resolveAttachmentUploadParams(
    action: string | null,
    hasFile: boolean,
    groupId: string | null
): AttachmentUploadParams | null {
    if (!hasFile) return null;
    if (action === 'SCHEDULE') return { typeCode: 'PAYMENT_SCHEDULE', poGroupId: groupId ?? undefined };
    if (action === 'PAY') return { typeCode: 'PAYMENT_PROOF', poGroupId: groupId ?? undefined };
    return null;
}

export interface FinancePaymentSummary {
    paymentType: string;
    plannedAmount: number;
    actualPaidAmount?: number | null;
    scheduledDateUtc?: string | null;
    paidDateUtc?: string | null;
}

/**
 * Resolves the FinalBalance RequestPayment's scheduled date/planned amount for a
 * PAYMENT_SCHEDULED group card. Returns nulls when no matching payment exists (rather than
 * throwing) — the card should render its badge even if the payment row is somehow missing.
 */
export function resolveScheduledPaymentDetails(
    payments: FinancePaymentSummary[] | null | undefined
): { scheduledDateUtc: string | null; plannedAmount: number | null } {
    const payment = (payments || []).find(p => p.paymentType === 'FINAL_BALANCE');
    return {
        scheduledDateUtc: payment?.scheduledDateUtc ?? null,
        plannedAmount: payment ? payment.plannedAmount : null,
    };
}

/**
 * Canonical, timezone-proof formatter for date-only business values (e.g. RequestPayment.
 * ScheduledDateUtc) received as an ISO date/datetime string. Deliberately never constructs a
 * `Date` object: after a round-trip through SQL Server's `datetime2` column type (no offset
 * metadata) and System.Text.Json's default converter, the JSON string arrives WITHOUT a trailing
 * 'Z' (e.g. "2026-07-24T00:00:00") — `new Date(...)` on such a string is parsed as LOCAL time per
 * the ECMA-262 date-time grammar (unlike a date-ONLY "2026-07-24" string, which IS parsed as UTC).
 * Combined with `{ timeZone: 'UTC' }` display formatting, that local/UTC mismatch silently shifts
 * the displayed calendar day by the browser's UTC offset (confirmed: 24/07 stored -> 23/07 shown
 * on a UTC+1 host). Extracting the Y-M-D digits directly from the string sidesteps timezone
 * interpretation entirely, so the displayed calendar day can never shift. Use this for every
 * calendar-date (not exact-instant) display — e.g. the cancel-schedule modal's "previously
 * scheduled" summary — so it always agrees with the backend's own un-converted history formatting.
 */
export function formatBusinessDateOnly(isoDateTime: string | null | undefined): string {
    if (!isoDateTime) return '---';
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDateTime);
    if (!match) return '---';
    const [, year, month, day] = match;
    return `${day}/${month}/${year}`;
}

/**
 * Resolves the Advance RequestPayment's paid date/actual amount for an ADVANCE_PAYMENT_COMPLETED
 * group card. Returns nulls when no matching payment exists.
 */
export function resolveAdvancePaymentDetails(
    payments: FinancePaymentSummary[] | null | undefined
): { paidDateUtc: string | null; actualPaidAmount: number | null } {
    const payment = (payments || []).find(p => p.paymentType === 'ADVANCE');
    return {
        paidDateUtc: payment?.paidDateUtc ?? null,
        actualPaidAmount: payment?.actualPaidAmount ?? null,
    };
}

export interface SortConfig {
    key: string | null;
    direction: 'asc' | 'desc';
}

/**
 * Same toggle state machine as RequestsDashboard.handleSort: clicking the currently-ascending
 * column switches it to descending; clicking any other column (or the same column while
 * descending) activates that column ascending. Exactly one active sort column at a time.
 */
export function toggleSort(current: SortConfig, key: string): SortConfig {
    if (current.key === key && current.direction === 'asc') {
        return { key, direction: 'desc' };
    }
    return { key, direction: 'asc' };
}
