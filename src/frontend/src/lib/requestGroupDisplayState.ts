/**
 * Pure, frontend-only "group-aware display state" logic, shared between the Finance workspace
 * (FinancePaymentsList.tsx, via financePaymentsView.ts's re-export) and the Requests Explorer /
 * Request drawer. Mirrors the backend's RequestGroupDisplayStateCalculator
 * (AlplaPortal.Domain.Services) exactly — same bucket statuses, same label strings. The two are
 * independently maintained (C#/TypeScript, no shared codegen); keep them in sync by updating both
 * together and re-running both test suites (financePaymentsView.test.mjs's
 * "resolveParentDisplayStatus" describe block + RequestGroupDisplayStateCalculatorTests.cs), which
 * assert against this same literal spec on each side.
 *
 * Never used for permissions/eligibility — display labels only.
 */

export interface DisplayGroupSummary {
    id: string;
    status?: string | null;
}

const CANCELLED_GROUP_STATUS = 'CANCELLED';

/**
 * Excludes CANCELLED groups (kept in DTOs for historical display only). Every other status —
 * including legacy pre-finance-status values — remains operational.
 */
export function resolveOperationalGroups<T extends DisplayGroupSummary>(groups?: T[] | null): T[] {
    return (groups || []).filter(g => g.status !== CANCELLED_GROUP_STATUS);
}

/** Whether the row has more than one operational (non-CANCELLED) group. */
export function hasMultipleOperationalGroups(groups?: DisplayGroupSummary[] | null): boolean {
    return resolveOperationalGroups(groups).length > 1;
}

export type DisplayBucket = 'WAITING_ACTION' | 'SCHEDULED' | 'ADVANCE_PAID' | 'PAID_OR_POST_PAYMENT' | 'COMPLETED';

const DISPLAY_BUCKET_STATUSES: Record<DisplayBucket, string[]> = {
    WAITING_ACTION: ['PO_ISSUED', 'PAYMENT_REQUEST_SENT', 'ADVANCE_PAYMENT_REQUIRED'],
    SCHEDULED: ['PAYMENT_SCHEDULED', 'ADVANCE_PAYMENT_SCHEDULED'],
    ADVANCE_PAID: ['ADVANCE_PAYMENT_COMPLETED', 'WAITING_SUPPLIER_DELIVERY'],
    PAID_OR_POST_PAYMENT: ['PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'WAITING_RECONCILIATION', 'IN_FOLLOWUP'],
    COMPLETED: ['COMPLETED'],
};

// Used only when active groups differ in literal status code but share one bucket (e.g. one
// PO_ISSUED + one PAYMENT_REQUEST_SENT, both WAITING_ACTION) — the persisted status name reflects
// only one of them in that case, so it can't be reused as-is. Approved label set.
const DISPLAY_BUCKET_LABELS: Record<DisplayBucket, string> = {
    WAITING_ACTION: 'Aguardando processamento financeiro',
    SCHEDULED: 'Pagamentos agendados',
    ADVANCE_PAID: 'Adiantamentos realizados',
    PAID_OR_POST_PAYMENT: 'Pagamentos concluídos',
    COMPLETED: 'Concluído',
};

const MIXED_DISPLAY_LABEL = 'Pagamentos em andamento';

function resolveGroupDisplayBucket(status: string | null | undefined): DisplayBucket | null {
    if (!status) return null;
    const buckets = Object.keys(DISPLAY_BUCKET_STATUSES) as DisplayBucket[];
    return buckets.find(bucket => DISPLAY_BUCKET_STATUSES[bucket].includes(status)) ?? null;
}

export interface GroupAwareDisplayStatus {
    label: string;
    /** 'MIXED' when active groups span different buckets; null when no bucket could be resolved
     *  (no operational groups, an unrecognized group status, or every group sharing the exact same
     *  status code — in all three cases the caller's fallbackLabel, normally the persisted
     *  Request.Status name, is returned unchanged since it is already accurate). */
    bucket: DisplayBucket | 'MIXED' | null;
}

/**
 * Pure, frontend-only display label for a request's group-aware aggregate state. Deliberately
 * independent of Request.Status/StatusName — the persisted aggregate (StatusAggregationService's
 * "furthest behind group wins", see RequestStatusCalculator) is correct as a legacy compatibility
 * field, but reads as misleading once a QUOTATION request's groups sit on structurally different
 * tracks: e.g. NCR PAYMENT_SCHEDULED + ITEC ADVANCE_PAYMENT_COMPLETED persists as
 * "ADVANCE_PAYMENT_COMPLETED" even though the request as a whole is still mid-flight. Never
 * persisted, never sent back to any endpoint — display only.
 *
 * Resolution order (mirrors RequestGroupDisplayStateCalculator.Resolve exactly):
 *  1. No operational groups, or an unrecognized group status -> fallbackLabel, bucket null.
 *  2. Every operational group shares the exact same status code -> fallbackLabel unchanged (it is
 *     already accurate for that one code), bucket set to the resolved bucket for callers that only
 *     need the bucket, not a different label.
 *  3. Groups differ in code but share one bucket -> that bucket's generic label.
 *  4. Groups span multiple buckets -> "Pagamentos em andamento".
 */
export function resolveParentDisplayStatus(
    poGroups: DisplayGroupSummary[] | null | undefined,
    fallbackLabel: string
): GroupAwareDisplayStatus {
    const operational = resolveOperationalGroups(poGroups);
    if (operational.length === 0) {
        return { label: fallbackLabel, bucket: null };
    }

    const buckets = operational.map(g => resolveGroupDisplayBucket(g.status));
    if (buckets.some(b => b === null)) {
        return { label: fallbackLabel, bucket: null };
    }

    const resolvedBuckets = buckets as DisplayBucket[];
    const distinctBuckets = new Set(resolvedBuckets);
    if (distinctBuckets.size > 1) {
        return { label: MIXED_DISPLAY_LABEL, bucket: 'MIXED' };
    }

    const distinctStatuses = new Set(operational.map(g => g.status));
    if (distinctStatuses.size === 1) {
        return { label: fallbackLabel, bucket: resolvedBuckets[0] };
    }

    const bucket = resolvedBuckets[0];
    return { label: DISPLAY_BUCKET_LABELS[bucket], bucket };
}

/**
 * Friendly per-group status labels — the single source of truth for both the Finance group cards
 * (FinancePaymentsList.tsx) and the Request drawer's group summary. Do not duplicate this map
 * elsewhere; falls back to the raw code for any status not yet mapped, so an unmapped/legacy
 * status never renders as blank.
 */
export const GROUP_STATUS_LABELS: Record<string, string> = {
    PO_ISSUED: 'P.O. Emitida',
    PAYMENT_REQUEST_SENT: 'Pedido de Pagamento Enviado',
    ADVANCE_PAYMENT_REQUIRED: 'Adiantamento Pendente',
    PAYMENT_SCHEDULED: 'Pagamento agendado',
    ADVANCE_PAYMENT_SCHEDULED: 'Adiantamento agendado',
    ADVANCE_PAYMENT_COMPLETED: 'Adiantamento realizado',
    WAITING_SUPPLIER_DELIVERY: 'Aguardando Entrega',
    PAYMENT_COMPLETED: 'Pago',
    WAITING_RECEIPT: 'Aguardando Recibo',
    WAITING_RECONCILIATION: 'Em Reconciliação',
    IN_FOLLOWUP: 'Em Acompanhamento',
    COMPLETED: 'Concluído',
};

/** Friendly per-group label, falling back to the raw status code if unmapped (never blank). */
export function resolveGroupStatusLabel(status: string | null | undefined): string {
    if (!status) return '---';
    return GROUP_STATUS_LABELS[status] ?? status;
}

const UNDEFINED_STATUS_LABEL = 'Status não definido';

/**
 * Safe display-label fallback chain for a request status badge: displayStatusName ->
 * statusName -> GROUP_STATUS_LABELS (a genuine translation only — never its own raw-code
 * passthrough) -> "Status não definido". Never returns a manufactured
 * "status.replace(/_/g, ' ')"-style string — the raw technical code may still drive styling
 * (a separate concern, e.g. RequestsTableWidget's STATUS_THEME lookup), but must never be the
 * visible label text.
 */
export function resolveSafeStatusLabel(
    statusCode: string | null | undefined,
    statusName: string | null | undefined,
    displayStatusName?: string | null
): string {
    if (displayStatusName) return displayStatusName;
    if (statusName) return statusName;
    if (statusCode) {
        const groupLabel = resolveGroupStatusLabel(statusCode);
        if (groupLabel !== statusCode) return groupLabel;
    }
    return UNDEFINED_STATUS_LABEL;
}
