/**
 * v2.230.0 — pure helpers for the Multi-Group Request Workflow projection
 * (GET /requests/{id}/workflow-projection). Display logic only — never used for
 * permissions/eligibility. Mirrors label vocabulary from the backend
 * RequestWorkflowProjectionBuilder; the builder is authoritative (labels arrive
 * server-computed), these helpers only cover the list rows where the full
 * projection is not fetched.
 */

import type { RequestWorkflowProjection, WorkflowUnit } from '../types';

/** PT labels for display-only aggregate codes (never persisted; list rows fall back here). */
const DISPLAY_ONLY_LABELS: Record<string, string> = {
    MIXED_PROCESSING: 'Processamento Parcial',
    PARTIALLY_PO_ISSUED: 'P.O Parcialmente Registrada',
    PARTIALLY_APPROVED: 'Parcialmente Aprovado',
    PARTIALLY_IN_APPROVAL: 'Parcialmente em Aprovação',
    QUOTATION_IN_APPROVAL: 'Cotação em Aprovação',
    QUOTATION_IN_PROGRESS: 'Cotação em Andamento',
    FULLY_APPROVED: 'Aprovado',
    APPROVED_WITH_CLOSURES: 'Aprovado (com encerramentos)',
    FULLY_COMPLETED: 'Finalizado',
    COMPLETED_WITH_CLOSURES: 'Finalizado (com encerramentos)',
};

/**
 * Aggregate badge label for a LIST row. Multi-unit rows prefer the display-state
 * label (e.g. "Processamento Parcial"); single-unit rows always keep the current
 * fallback (statusName / group-aware override) — the compatibility rule.
 */
export function resolveListAggregateLabel(
    activeUnitCount: number | undefined,
    displayWorkflowState: string | null | undefined,
    fallbackLabel: string,
): string {
    if ((activeUnitCount ?? 0) <= 1) return fallbackLabel;
    if (!displayWorkflowState) return fallbackLabel;
    return DISPLAY_ONLY_LABELS[displayWorkflowState] ?? fallbackLabel;
}

/**
 * Role-keyed rollup for the detail header's "FLUXOS ATIVOS" strip, e.g.
 * ["Compras: 1 grupo aguardando P.O.", "Financeiro: 1 grupo com P.O. emitida"].
 */
export function buildActiveFlows(projection: RequestWorkflowProjection): string[] {
    const byRole = new Map<string, WorkflowUnit[]>();
    for (const unit of projection.units) {
        if (!unit.nextAction) continue;
        const list = byRole.get(unit.responsibleRole) ?? [];
        list.push(unit);
        byRole.set(unit.responsibleRole, list);
    }
    const flows: string[] = [];
    for (const [role, units] of byRole) {
        const noun = units[0].unitType === 'BATCH' ? 'lote' : 'grupo';
        const nounPlural = units[0].unitType === 'BATCH' ? 'lotes' : 'grupos';
        const stateLabel = units[0].statusLabel.toLowerCase();
        flows.push(units.length === 1
            ? `${role}: 1 ${noun} — ${stateLabel}`
            : `${role}: ${units.length} ${nounPlural} — ${summarizeStates(units)}`);
    }
    return flows;
}

function summarizeStates(units: WorkflowUnit[]): string {
    const labels = Array.from(new Set(units.map(u => u.statusLabel.toLowerCase())));
    return labels.join(', ');
}

/** True when the projection represents more than one active operational unit. */
export function isMultiUnit(projection: RequestWorkflowProjection | null | undefined): boolean {
    return (projection?.units.length ?? 0) > 1;
}

/**
 * Requests-list expanded row, > 3 lots: which single lot starts expanded. The first lot that
 * still has work (any non-completed step) wins — a completed "Lote #1" never dominates while
 * another lot requires action. All completed: the first lot.
 */
export function defaultExpandedLotIndex(lots: { steps: { state: string }[] }[]): number {
    const active = lots.findIndex(lot => lot.steps.some(s => s.state !== 'completed'));
    return active >= 0 ? active : 0;
}

/** Lot header title — real domain identity only: "Lote #N · Supplier" when a real batch
 *  number exists; otherwise the unit label ("Grupo X"). Never fabricates a lot number. */
export function lotHeaderTitle(lot: { lotNumber?: number | null; supplierName?: string | null; label: string }): string {
    if (lot.lotNumber != null) {
        return lot.supplierName ? `Lote #${lot.lotNumber} · ${lot.supplierName}` : `Lote #${lot.lotNumber}`;
    }
    return lot.label;
}

const TERMINAL_SCALARS = ['CANCELLED', 'REJECTED', 'COMPLETED'];

/**
 * Drawer status-badge override (mirror of the backend ResolveSingleUnitBadgeOverride):
 * a non-terminal single-unit request whose persisted scalar lags the unit lifecycle shows
 * the unit's truthful label. Display only — null means "keep the persisted status name".
 */
export function resolveDrawerBadgeOverride(
    projection: RequestWorkflowProjection | null | undefined,
    scalarStatusCode: string | null | undefined,
): { code: string; label: string } | null {
    if (!projection || projection.units.length !== 1) return null;
    if (!scalarStatusCode || TERMINAL_SCALARS.includes(scalarStatusCode)) return null;
    const unit = projection.units[0];
    if (unit.statusCode === scalarStatusCode) return null;
    return { code: unit.statusCode, label: unit.statusLabel };
}

/**
 * Drawer guidance (Responsável / Próxima ação) derived from the projection for a single-unit
 * QUOTATION request. For healthy requests the strings are identical to the legacy
 * getRequestGuidance map (the backend reuses them); stale-scalar cases get the unit's truth.
 * Null → caller keeps the legacy scalar guidance (class-A/unit-less, terminal, multi-unit).
 */
export function resolveSingleUnitGuidance(
    projection: RequestWorkflowProjection | null | undefined,
    scalarStatusCode: string | null | undefined,
): { responsible: string; nextAction: string } | null {
    if (!projection || projection.units.length !== 1) return null;
    if (!scalarStatusCode || TERMINAL_SCALARS.includes(scalarStatusCode)) return null;
    const unit = projection.units[0];
    return {
        responsible: unit.responsibleRole,
        nextAction: unit.nextAction?.label ?? 'Sem ação pendente',
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// v2.245.7 — Request Details / Quick View guidance consumption.
//
// The header's "Responsável / Próxima ação" pair is derived from the AUTHORITATIVE projection
// (GET /requests/{id}/workflow-projection) whose receiving guidance is computed server-side from the
// receipt facts (v2.245.3 / v2.245.6). Until v2.245.7 the details view fetched that projection ONLY for
// QUOTATION requests, so every other request type — the PAYMENT groups of the receiving flow included —
// fell through to the generic status-only map (lib/utils getRequestGuidance) and a fully received,
// unconfirmed IN_FOLLOWUP group kept reading "Resolver itens pendentes…".
//
// Policy (single source of truth, no second receiving rulebook in the frontend):
//  • QUOTATION requests: unchanged — the projection drives guidance for every non-terminal status.
//  • Every other request type: the projection drives guidance while the request/unit is in a
//    RECEIVING-PHASE status, where the projection carries receipt-fact-specific wording. Outside that
//    phase the legacy scalar map stands (strings there are type-specific and intentionally kept).
//  • While the projection is loading, a placeholder is shown — never a known-generic text that is
//    replaced later (no flash of wrong guidance).
//  • If the fetch fails, the conservative legacy scalar map is used and the view keeps working.
// ─────────────────────────────────────────────────────────────────────────────

export type ProjectionLoadState = 'idle' | 'loading' | 'loaded' | 'error';

export interface ProjectionLoad {
    state: ProjectionLoadState;
    projection: RequestWorkflowProjection | null;
}

export const PROJECTION_IDLE: ProjectionLoad = { state: 'idle', projection: null };

/**
 * Receiving-phase statuses (request scalar AND group unit) whose projection guidance depends on the
 * receipt facts. For these the legacy map and the projection use the SAME strings except the
 * receipt-fact-specific "Recebimento completo — confirmar recebimento", which only the projection knows.
 */
export const RECEIVING_GUIDANCE_STATUSES: readonly string[] = [
    'PAYMENT_COMPLETED', 'IN_FOLLOWUP', 'WAITING_RECEIPT', 'WAITING_FISCAL_RECEIPT',
];

const PROJECTION_IRRELEVANT_SCALARS = ['DRAFT', ...TERMINAL_SCALARS];

/** Whether the details view needs the projection for its guidance (one fetch per view; none when irrelevant). */
export function shouldFetchWorkflowProjection(
    requestId: string | null | undefined,
    requestTypeCode: string | null | undefined,
    scalarStatusCode: string | null | undefined,
): boolean {
    if (!requestId) return false;
    if (requestTypeCode === 'QUOTATION') return true;
    return !!scalarStatusCode && RECEIVING_GUIDANCE_STATUSES.includes(scalarStatusCode);
}

/**
 * The details view's projection loader (framework-free so it is testable at the consumer boundary).
 * Emits `loading` → `loaded` | `error`; emits `idle` (no fetch) when the projection is not needed.
 * A failed fetch never throws — the caller falls back to the legacy scalar guidance.
 */
export async function loadWorkflowProjection(
    fetcher: (requestId: string) => Promise<RequestWorkflowProjection>,
    requestId: string | null | undefined,
    requestTypeCode: string | null | undefined,
    scalarStatusCode: string | null | undefined,
    emit: (load: ProjectionLoad) => void,
    isCancelled: () => boolean = () => false,
): Promise<void> {
    if (!shouldFetchWorkflowProjection(requestId, requestTypeCode, scalarStatusCode)) {
        emit(PROJECTION_IDLE);
        return;
    }
    emit({ state: 'loading', projection: null });
    try {
        const projection = await fetcher(requestId as string);
        if (!isCancelled()) emit({ state: 'loaded', projection });
    } catch {
        if (!isCancelled()) emit({ state: 'error', projection: null });
    }
}

export interface HeaderGuidance {
    responsible: string;
    nextAction: string;
    /** True while the authoritative projection is still loading — render a placeholder, not text. */
    loading?: boolean;
}

export const GUIDANCE_LOADING: HeaderGuidance = {
    responsible: '—',
    nextAction: 'Carregando próxima ação...',
    loading: true,
};

/** True when, for this request type/status, the projection (not the scalar map) owns the guidance. */
export function projectionOwnsGuidance(
    requestTypeCode: string | null | undefined,
    scalarStatusCode: string | null | undefined,
): boolean {
    if (!scalarStatusCode || PROJECTION_IRRELEVANT_SCALARS.includes(scalarStatusCode)) return false;
    if (requestTypeCode === 'QUOTATION') return true;
    return RECEIVING_GUIDANCE_STATUSES.includes(scalarStatusCode);
}

/**
 * Projection-derived single-unit guidance for the details header, scoped by the policy above.
 * `guidance` is null when the scalar map must be used (multi-unit, unit-less, not owned, error/idle);
 * `loading` is true only while a fetch that WOULD own the guidance is in flight.
 */
export function resolveProjectionGuidance(
    load: ProjectionLoad,
    requestTypeCode: string | null | undefined,
    scalarStatusCode: string | null | undefined,
): { guidance: { responsible: string; nextAction: string } | null; loading: boolean } {
    if (!projectionOwnsGuidance(requestTypeCode, scalarStatusCode)) return { guidance: null, loading: false };
    if (load.state === 'loading') return { guidance: null, loading: true };
    if (load.state !== 'loaded' || !load.projection) return { guidance: null, loading: false };

    const single = resolveSingleUnitGuidance(load.projection, scalarStatusCode);
    if (!single) return { guidance: null, loading: false };
    if (requestTypeCode !== 'QUOTATION') {
        // Non-QUOTATION: only an operational GROUP unit in the receiving phase is projection-owned.
        const unit = load.projection.units[0];
        if (unit.unitType !== 'GROUP' || !RECEIVING_GUIDANCE_STATUSES.includes(unit.statusCode)) {
            return { guidance: null, loading: false };
        }
    }
    return { guidance: single, loading: false };
}

/**
 * The single precedence rule for the header strip and the status panel:
 *   1. Release-4 completion guidance (WAITING_RECEIPT under the active completion lifecycle) — most specific;
 *   2. loading placeholder while the owning projection is in flight (no flash of generic text);
 *   3. the projection's single-unit guidance;
 *   4. the legacy scalar map (conservative fallback: idle, error, multi-unit, unit-less, not owned).
 */
export function resolveHeaderGuidance(input: {
    status: string | null | undefined;
    requestTypeCode: string | null | undefined;
    load: ProjectionLoad;
    release4Guidance?: { responsible: string; nextAction: string } | null;
    scalarGuidance: (status: string, requestTypeCode: string) => { responsible: string; nextAction: string };
}): HeaderGuidance | null {
    const { status, requestTypeCode, load, release4Guidance, scalarGuidance } = input;
    if (!status) return null;
    if (status === 'WAITING_RECEIPT' && release4Guidance) return release4Guidance;
    const { guidance, loading } = resolveProjectionGuidance(load, requestTypeCode, status);
    if (loading) return GUIDANCE_LOADING;
    return guidance ?? scalarGuidance(status, requestTypeCode || '');
}

/**
 * Operational panel statuses — the request-level status codes for which the Procurement/Buyer
 * action panel in RequestStatusActionPanels is allowed to render. Kept in sync with the
 * allow-list at the top of that panel (RequestStatusActionPanels.tsx line ~140). Exported so the
 * multi-unit derivation below — and its tests — can assert that a derived result is "operational"
 * (i.e. the panel will actually render for it).
 */
export const OPERATIONAL_PANEL_STATUSES = new Set<string>([
    'APPROVED', 'QUOTATION_COMPLETED', 'PO_REQUESTED', 'PO_PARTIALLY_UPLOADED', 'PO_ISSUED',
    'WAITING_PO_CORRECTION', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT',
    'ADVANCE_PAYMENT_REQUIRED', 'ADVANCE_PAYMENT_COMPLETED', 'WAITING_SUPPLIER_DELIVERY',
    'WAITING_RECONCILIATION',
]);

/** True when the panel's operational allow-list would render for this request-level status. */
export function isOperationalPanelStatus(code: string): boolean {
    return OPERATIONAL_PANEL_STATUSES.has(code);
}

/** A single operational unit's lifecycle mapped onto the request-level panel vocabulary
 *  (identity for post-PO states; WAITING_PO → PO_REQUESTED, PENDING → WAITING_FINAL_APPROVAL). */
function mapUnitStatusToPanel(unitStatusCode: string): string {
    const map: Record<string, string> = {
        WAITING_PO: 'PO_REQUESTED',
        PENDING: 'WAITING_FINAL_APPROVAL',
    };
    return map[unitStatusCode] ?? unitStatusCode;
}

/**
 * Representative operational panel status for a MULTI-unit request whose persisted scalar is a
 * valid non-operational request-level state while some of its groups are already operational (e.g.
 * a partial-quotation QUOTATION correctly still WAITING_QUOTATION for its unquoted items, whose
 * already-approved groups are WAITING_PO). Precedence mirrors exactly what the
 * RequestStatusActionPanels inner gates need to surface the right per-group buttons — REGISTER_PO
 * needs PO_REQUESTED / PO_PARTIALLY_UPLOADED; CORRECT_PO needs WAITING_PO_CORRECTION /
 * PO_PARTIALLY_UPLOADED:
 *
 *  - a group still WAITING_PO, alongside a group that already crossed the PO gate OR a group in
 *    correction  → PO_PARTIALLY_UPLOADED  (the only status that shows both Register and Correct)
 *  - groups WAITING_PO with nothing issued and no correction  → PO_REQUESTED
 *  - no WAITING_PO but a group in correction  → WAITING_PO_CORRECTION
 *  - no actionable Buyer PO work in any group  → null (caller keeps the persisted scalar)
 *
 * Only GROUP units are considered; BATCH units and PENDING groups are pre-PO (neither actionable
 * nor "past the gate") and never force an operational status on their own.
 */
function deriveMultiUnitPanelStatus(units: readonly WorkflowUnit[]): string | null {
    const groups = units.filter(u => u.unitType === 'GROUP');
    const hasWaitingPo = groups.some(u => u.statusCode === 'WAITING_PO');
    const hasCorrection = groups.some(u => u.statusCode === 'WAITING_PO_CORRECTION');
    const hasPastPoGroup = groups.some(u =>
        u.statusCode !== 'WAITING_PO' &&
        u.statusCode !== 'WAITING_PO_CORRECTION' &&
        u.statusCode !== 'PENDING');

    if (hasWaitingPo) {
        return (hasPastPoGroup || hasCorrection) ? 'PO_PARTIALLY_UPLOADED' : 'PO_REQUESTED';
    }
    if (hasCorrection) return 'WAITING_PO_CORRECTION';
    return null;
}

/**
 * Effective status for the drawer ACTION PANELS of a QUOTATION request: panels are keyed on
 * request-level status codes, so the operational unit lifecycle maps onto the equivalent panel
 * vocabulary.
 *
 * Single-unit: the unit's lifecycle maps directly (identity for post-PO states; WAITING_PO →
 * PO_REQUESTED, PENDING → WAITING_FINAL_APPROVAL).
 *
 * Multi-unit: the persisted scalar governs the panel UNLESS it is a valid NON-operational
 * request-level state (e.g. WAITING_QUOTATION on a partial quotation) while one or more approved
 * groups are already operational. Only then is a representative operational status derived from
 * the groups so the group-aware panel can render; when the scalar is already operational it is
 * preserved unchanged. The per-group Register-PO / Correct-PO buttons remain group-filtered inside
 * the panels — the effective status only decides whether the operational panel is allowed to open.
 *
 * Falls back to the persisted scalar for class-A (unit-less), terminal scalars, or a multi-unit
 * request with no actionable Buyer PO work.
 */
export function effectivePanelStatus(
    projection: RequestWorkflowProjection | null | undefined,
    scalarStatusCode: string,
): string {
    if (!projection || projection.units.length === 0) return scalarStatusCode;
    if (TERMINAL_SCALARS.includes(scalarStatusCode)) return scalarStatusCode;

    if (projection.units.length === 1) {
        return mapUnitStatusToPanel(projection.units[0].statusCode);
    }

    // Multi-unit: keep an already-operational scalar; only rescue a lagging non-operational one.
    if (isOperationalPanelStatus(scalarStatusCode)) return scalarStatusCode;
    return deriveMultiUnitPanelStatus(projection.units) ?? scalarStatusCode;
}
