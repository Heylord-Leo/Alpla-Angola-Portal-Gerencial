/**
 * Pure, testable logic for the Finance > Payments page (FinancePaymentsList.tsx):
 * row-action visibility and the sort-toggle state machine. Extracted so eligibility rendering
 * and sort toggling can be unit-tested without a component-rendering test runner, and so the
 * component itself does not re-implement business eligibility rules independently of the
 * backend's AvailableFinanceActions (see IFinancePaymentEligibilityService on the backend).
 */

export interface FinancePoGroupSummary {
    id: string;
    status?: string | null;
}

export interface FinanceRowActionSource {
    availableFinanceActions: string[];
    poGroups?: FinancePoGroupSummary[] | null;
}

export interface FinanceRowActions {
    /** Backend eligibility, exactly as reported — never re-derived from poGroups length. */
    canSchedule: boolean;
    canPay: boolean;
    /** The single operational group id to act on, or null when it cannot be unambiguously determined. */
    groupId: string | null;
}

const CANCELLED_GROUP_STATUS = 'CANCELLED';

/**
 * PoGroups includes CANCELLED groups (kept in the DTO for historical display only — see DEC-149).
 * They must never count as "operational": never inflate a multi-group determination, never get
 * auto-selected as the acting group, and never suppress a valid single-group row action. Every
 * other status — including legacy pre-finance-status values like PENDING — remains operational and
 * resolvable, which is required for the confirmed legacy-PAYMENT-group bug fix. This does NOT
 * reintroduce the old broad finance-status filter: it excludes exactly one status (CANCELLED),
 * not a whitelist of "known-good" statuses.
 */
export function resolveOperationalGroups(poGroups?: FinancePoGroupSummary[] | null): FinancePoGroupSummary[] {
    return (poGroups || []).filter(g => g.status !== CANCELLED_GROUP_STATUS);
}

/** Whether the row has more than one operational (non-CANCELLED) group — drives the multi-group sub-row UI. */
export function hasMultipleOperationalGroups(poGroups?: FinancePoGroupSummary[] | null): boolean {
    return resolveOperationalGroups(poGroups).length > 1;
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
