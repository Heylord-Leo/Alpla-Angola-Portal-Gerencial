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

/** Phase-5 obligations-list defaults/keys, shared with the page so tests can assert them. */
export const FINANCE_DEFAULT_SORT = 'newest';
export const FINANCE_SORT_OPTIONS = [
    { value: 'newest', label: 'Mais recentes' },
    { value: 'oldest', label: 'Mais antigos' },
] as const;
/** Every filter/sort URL param cleared by "Limpar" (sort then resets to the default newest-first). */
export const FINANCE_CLEAR_KEYS = ['search', 'actionClass', 'currencyCode', 'companyId', 'plantId', 'departmentId', 'actionableOnly', 'overdueOnly', 'dueTodayOnly', 'sortBy'] as const;

/** Number of ADVANCED filters currently active (drives the "Mais filtros (N)" badge). Search,
 *  Situação financeira and Ordenar live in the primary toolbar and are NOT counted here. */
export function countAdvancedFilters(f: {
    companyId?: number | string | null; plantId?: number | string | null; departmentId?: number | string | null;
    currencyCode?: string | null; actionableOnly?: boolean; overdueOnly?: boolean;
}): number {
    let n = 0;
    if (f.companyId) n++;
    if (f.plantId) n++;
    if (f.departmentId) n++;
    if (f.currencyCode) n++;
    if (f.actionableOnly) n++;
    if (f.overdueOnly) n++;
    return n;
}

/**
 * Compact tooltip content for a container's Finance note indicator. Returns null when there are no
 * notes (so the icon is hidden). One tooltip per request container (notes are request-level), so a
 * multi-group request never shows a duplicated note icon.
 */
export function resolveNoteTooltip(container: { hasNotes?: boolean; noteCount?: number; latestNoteText?: string | null }):
    { title: string; body: string; extra: string | null } | null {
    if (!container.hasNotes || !container.latestNoteText) return null;
    const count = container.noteCount ?? 1;
    return {
        title: count > 1 ? 'Última observação' : 'Observação',
        body: container.latestNoteText,
        extra: count > 1 ? `+${count - 1} ${count - 1 === 1 ? 'observação anterior' : 'observações anteriores'}` : null,
    };
}

export type ObligationActionCode = 'SCHEDULE' | 'PAY' | 'CANCEL_SCHEDULE' | 'RETURN' | 'NOTE' | 'DETAILS';
export interface ObligationActionPlan {
    /** The single inline primary Finance mutation (or null when there is none, e.g. paid/WAITING_PO). */
    primary: { action: 'SCHEDULE' | 'PAY'; label: string } | null;
    /** Kebab menu items, in the canonical order: Detalhes, Adicionar observação, secondary finance
     *  actions, correction/destructive last. */
    menu: ObligationActionCode[];
}

/**
 * Action hierarchy for one obligation (Phase-4 polish): exactly ONE primary inline button, every
 * other authorized action in the kebab. Derived solely from the backend-authorized financeActions
 * (never invents an action) plus the group's advance/normal flavor for labels.
 */
export function resolveObligationActionPlan(o: { groupStatusCode?: string | null; financeActions?: string[] | null }): ObligationActionPlan {
    const acts = o.financeActions ?? [];
    const advance = isAdvanceGroupStatus(o.groupStatusCode);

    let primary: ObligationActionPlan['primary'] = null;
    if (acts.includes('SCHEDULE')) primary = { action: 'SCHEDULE', label: advance ? 'Agendar adiantamento' : 'Agendar pagamento' };
    else if (acts.includes('PAY')) primary = { action: 'PAY', label: advance ? 'Pagar adiantamento' : 'Pagar' };

    const menu: ObligationActionCode[] = ['DETAILS', 'NOTE'];
    if (acts.includes('PAY') && primary?.action !== 'PAY') menu.push('PAY');   // Pagar available but not primary (PO_ISSUED / advance)
    if (acts.includes('CANCEL_SCHEDULE')) menu.push('CANCEL_SCHEDULE');
    if (acts.includes('RETURN')) menu.push('RETURN');                          // correction — always last
    return { primary, menu };
}

/** PT label for an obligation action, advance-aware. */
export function obligationActionLabel(code: ObligationActionCode, advance: boolean): string {
    switch (code) {
        case 'DETAILS': return 'Detalhes';
        case 'NOTE': return 'Adicionar observação';
        case 'PAY': return advance ? 'Pagar adiantamento' : 'Pagar';
        case 'SCHEDULE': return advance ? 'Agendar adiantamento' : 'Agendar pagamento';
        case 'CANCEL_SCHEDULE': return 'Cancelar agendamento';
        case 'RETURN': return 'Devolver para ajuste';
    }
}

/**
 * Visual flags for one Finance obligation row (Phase 4): paid/post-payment rows are de-emphasized
 * with no mutation buttons, NO_FINANCE_ACTION rows show a Buyer/waiting message, overdue rows get
 * the red urgency treatment. Derived purely from the obligation's action class + overdue flag.
 */
export function resolveObligationRowFlags(o: { actionClass?: string | null; isOverdue?: boolean | null }): {
    isPaid: boolean; isNoFinance: boolean; isOverdue: boolean; muted: boolean;
} {
    const isPaid = o.actionClass === 'PAID_WAITING_RECEIVING' || o.actionClass === 'COMPLETED';
    const isNoFinance = o.actionClass === 'NO_FINANCE_ACTION';
    const isOverdue = !!o.isOverdue;
    return { isPaid, isNoFinance, isOverdue, muted: isPaid };
}

/** Mirrors FinancePaymentEligibilityService.CanReturnGroup's ReturnableGroupStatuses. */
const RETURNABLE_GROUP_STATUSES = ['PO_ISSUED', 'PAYMENT_SCHEDULED'];
export function canReturnGroupStatus(status: string | null | undefined): boolean {
    return status != null && RETURNABLE_GROUP_STATUSES.includes(status);
}

/**
 * The Finance buttons a single multi-group card should render, gated on THIS group only.
 * Prefers the backend-computed `financeActions` (authoritative per-group action list from
 * FinancePaymentEligibilityService.EvaluateGroupActions); falls back to the mirrored local status
 * predicates when the field is absent (older payloads / safety). A paid sibling group can never
 * affect this result — that is the request-100 fix.
 */
export function resolveGroupFinanceButtons(group: { status?: string | null; financeActions?: string[] | null }): {
    schedule: boolean; pay: boolean; cancelSchedule: boolean; return: boolean;
} {
    const actions = group.financeActions;
    if (Array.isArray(actions)) {
        // Server truth (an empty array is a legitimate "no actions" answer).
        return {
            schedule: actions.includes('SCHEDULE'),
            pay: actions.includes('PAY'),
            cancelSchedule: actions.includes('CANCEL_SCHEDULE'),
            return: actions.includes('RETURN'),
        };
    }
    // Fallback: derive from the group's own status (mirrors the backend lists).
    return {
        schedule: canScheduleGroupStatus(group.status),
        pay: canPayGroupStatus(group.status),
        cancelSchedule: canCancelScheduleGroupStatus(group.status),
        return: canReturnGroupStatus(group.status),
    };
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
