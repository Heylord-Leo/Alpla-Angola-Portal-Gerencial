import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import {
    resolveListAggregateLabel,
    buildActiveFlows,
    isMultiUnit,
    defaultExpandedLotIndex,
    lotHeaderTitle,
    resolveDrawerBadgeOverride,
    resolveSingleUnitGuidance,
    effectivePanelStatus,
    isOperationalPanelStatus,
} from './workflowProjection.ts';

// v2.230.0 — pure display helpers for the Multi-Group Request Workflow projection.
// The backend RequestWorkflowProjectionBuilder is authoritative for labels; these tests pin the
// list-row fallback behavior (where the full projection is not fetched) and the header rollup.

describe('resolveListAggregateLabel', () => {
    test('single-unit rows ALWAYS keep the fallback label (compatibility rule)', () => {
        assert.equal(resolveListAggregateLabel(1, 'MIXED_PROCESSING', 'Aprovado'), 'Aprovado');
        assert.equal(resolveListAggregateLabel(0, 'MIXED_PROCESSING', 'Aprovado'), 'Aprovado');
        assert.equal(resolveListAggregateLabel(undefined, 'MIXED_PROCESSING', 'Aprovado'), 'Aprovado');
    });

    test('multi-unit rows prefer the display-state label', () => {
        assert.equal(resolveListAggregateLabel(2, 'MIXED_PROCESSING', 'P.O Emitida'), 'Processamento Parcial');
        assert.equal(resolveListAggregateLabel(3, 'PARTIALLY_PO_ISSUED', 'Aprovado'), 'P.O Parcialmente Registrada');
    });

    test('multi-unit rows with a persisted (non display-only) code keep the fallback', () => {
        assert.equal(resolveListAggregateLabel(2, 'PO_ISSUED', 'P.O Emitida'), 'P.O Emitida');
        assert.equal(resolveListAggregateLabel(2, null, 'P.O Emitida'), 'P.O Emitida');
    });
});

describe('buildActiveFlows', () => {
    const unit = (overrides) => ({
        unitType: 'GROUP',
        unitId: 'u1',
        label: 'Grupo A',
        totalAmount: 0,
        itemCount: 0,
        itemLineNumbers: [],
        statusCode: 'PO_ISSUED',
        statusLabel: 'P.O Emitida',
        approvalState: 'COMPLETE',
        poState: 'ISSUED',
        paymentState: 'PENDING',
        receivingState: 'NOT_STARTED',
        completionState: 'NOT_STARTED',
        responsibleRole: 'Financeiro',
        nextAction: { unitType: 'GROUP', unitId: 'u1', unitLabel: 'Grupo A', actionType: 'SCHEDULE_PAYMENT', label: 'Pagar', responsibleRole: 'Financeiro', priority: 50 },
        ...overrides,
    });

    test('rolls units up by role', () => {
        const flows = buildActiveFlows({
            aggregateDisplay: { statusCode: 'MIXED_PROCESSING', label: 'Processamento Parcial' },
            units: [
                unit({}),
                unit({ unitId: 'u2', unitType: 'BATCH', label: 'Lote #2', statusCode: 'WAITING_FINAL_APPROVAL', statusLabel: 'Aguardando Aprovação Final', responsibleRole: 'Aprovador Final', nextAction: { unitType: 'BATCH', unitId: 'u2', unitLabel: 'Lote #2', actionType: 'FINAL_APPROVE', label: 'Aprovar', responsibleRole: 'Aprovador Final', priority: 15 } }),
            ],
            responsibilities: [],
            nextActions: [],
            warnings: [],
        });
        assert.equal(flows.length, 2);
        assert.ok(flows.some(f => f.startsWith('Financeiro: 1 grupo — ')));
        assert.ok(flows.some(f => f.startsWith('Aprovador Final: 1 lote — ')));
    });

    test('units without a next action never contribute a flow', () => {
        const flows = buildActiveFlows({
            aggregateDisplay: { statusCode: 'FULLY_COMPLETED', label: 'Finalizado' },
            units: [unit({ nextAction: null, responsibleRole: 'Sem ação' })],
            responsibilities: [],
            nextActions: [],
            warnings: [],
        });
        assert.equal(flows.length, 0);
    });
});

describe('isMultiUnit', () => {
    test('true only above one active unit', () => {
        assert.equal(isMultiUnit(null), false);
        assert.equal(isMultiUnit({ units: [] }), false);
        assert.equal(isMultiUnit({ units: [{}] }), false);
        assert.equal(isMultiUnit({ units: [{}, {}] }), true);
    });
});

describe('lot timeline helpers (Requests-list expanded row)', () => {
    const lot = (states, extra = {}) => ({ steps: states.map(s => ({ state: s })), label: 'Grupo X', ...extra });

    test('defaultExpandedLotIndex picks the first lot still requiring work', () => {
        const lots = [
            lot(['completed', 'completed']),          // done
            lot(['completed', 'current']),            // active — should win
            lot(['completed', 'pending']),
        ];
        assert.equal(defaultExpandedLotIndex(lots), 1);
    });

    test('all lots completed: first lot is the default', () => {
        assert.equal(defaultExpandedLotIndex([lot(['completed']), lot(['completed'])]), 0);
    });

    test('lotHeaderTitle uses the REAL lot number when present', () => {
        assert.equal(lotHeaderTitle({ lotNumber: 2, supplierName: 'TDA-COMERCIO', label: 'Grupo TDA-COMERCIO' }), 'Lote #2 · TDA-COMERCIO');
        assert.equal(lotHeaderTitle({ lotNumber: 3, supplierName: null, label: 'Lote #3' }), 'Lote #3');
    });

    test('lotHeaderTitle never fabricates a lot number for batchless groups', () => {
        assert.equal(lotHeaderTitle({ lotNumber: null, supplierName: 'Gasp Transportes', label: 'Grupo Gasp Transportes' }), 'Grupo Gasp Transportes');
    });
});

describe('drawer projection helpers (single-unit historical compatibility)', () => {
    const projection = (units) => ({ aggregateDisplay: { statusCode: 'X', label: 'X' }, units, responsibilities: [], nextActions: [], warnings: [] });
    const unit = (statusCode, statusLabel, role = 'Financeiro', actionLabel = 'Pagar ou agendar o pagamento') => ({
        unitType: 'GROUP', unitId: 'u1', label: 'Grupo X', totalAmount: 0, itemCount: 0, itemLineNumbers: [],
        statusCode, statusLabel, approvalState: 'COMPLETE', poState: 'ISSUED', paymentState: 'PENDING',
        receivingState: 'NOT_STARTED', completionState: 'NOT_STARTED', responsibleRole: role,
        nextAction: actionLabel ? { unitType: 'GROUP', unitId: 'u1', unitLabel: 'Grupo X', actionType: 'A', label: actionLabel, responsibleRole: role, priority: 1 } : null,
    });

    test('stale APPROVED scalar + PO_ISSUED unit: badge + guidance come from the unit', () => {
        const p = projection([unit('PO_ISSUED', 'P.O Emitida')]);
        assert.deepEqual(resolveDrawerBadgeOverride(p, 'APPROVED'), { code: 'PO_ISSUED', label: 'P.O Emitida' });
        assert.deepEqual(resolveSingleUnitGuidance(p, 'APPROVED'), { responsible: 'Financeiro', nextAction: 'Pagar ou agendar o pagamento' });
    });

    test('healthy single unit agreeing with the scalar: no badge override, guidance still projection-driven', () => {
        const p = projection([unit('PO_ISSUED', 'P.O Emitida')]);
        assert.equal(resolveDrawerBadgeOverride(p, 'PO_ISSUED'), null);
        assert.deepEqual(resolveSingleUnitGuidance(p, 'PO_ISSUED'), { responsible: 'Financeiro', nextAction: 'Pagar ou agendar o pagamento' });
    });

    test('class-A unitless request: legacy fallback everywhere', () => {
        const p = projection([]);
        assert.equal(resolveDrawerBadgeOverride(p, 'WAITING_QUOTATION'), null);
        assert.equal(resolveSingleUnitGuidance(p, 'WAITING_QUOTATION'), null);
        assert.equal(effectivePanelStatus(p, 'WAITING_QUOTATION'), 'WAITING_QUOTATION');
    });

    test('terminal scalars stay authoritative', () => {
        const p = projection([unit('PO_ISSUED', 'P.O Emitida')]);
        for (const t of ['CANCELLED', 'REJECTED', 'COMPLETED']) {
            assert.equal(resolveDrawerBadgeOverride(p, t), null);
            assert.equal(resolveSingleUnitGuidance(p, t), null);
            assert.equal(effectivePanelStatus(p, t), t);
        }
    });

    test('effectivePanelStatus maps the unit lifecycle onto panel vocabulary', () => {
        assert.equal(effectivePanelStatus(projection([unit('WAITING_PO', 'Aguardando P.O.')]), 'APPROVED'), 'PO_REQUESTED');
        assert.equal(effectivePanelStatus(projection([unit('PENDING', 'Aguardando Ativação')]), 'APPROVED'), 'WAITING_FINAL_APPROVAL');
        assert.equal(effectivePanelStatus(projection([unit('PO_ISSUED', 'P.O Emitida')]), 'APPROVED'), 'PO_ISSUED'); // identity
        assert.equal(effectivePanelStatus(null, 'APPROVED'), 'APPROVED'); // no projection loaded
        assert.equal(effectivePanelStatus(projection([unit('A', 'a'), unit('B', 'b')]), 'PO_PARTIALLY_UPLOADED'), 'PO_PARTIALLY_UPLOADED'); // multi-unit keeps scalar
    });
});

describe('effectivePanelStatus — multi-group QUOTATION with a lagging scalar (REQ-234 class)', () => {
    const projection = (units) => ({ aggregateDisplay: { statusCode: 'X', label: 'X' }, units, responsibilities: [], nextActions: [], warnings: [] });
    const group = (statusCode) => ({
        unitType: 'GROUP', unitId: 'g-' + statusCode, label: 'Grupo ' + statusCode, totalAmount: 0, itemCount: 0,
        itemLineNumbers: [], statusCode, statusLabel: statusCode, approvalState: 'COMPLETE', poState: 'PENDING',
        paymentState: 'NOT_STARTED', receivingState: 'NOT_STARTED', completionState: 'NOT_STARTED', responsibleRole: 'Comprador',
        nextAction: null,
    });
    const batch = (statusCode) => ({ ...group(statusCode), unitType: 'BATCH', unitId: 'b-' + statusCode });

    // CASE A — two WAITING_PO groups + stale WAITING_QUOTATION scalar → PO_REQUESTED (Register P.O renders).
    test('CASE A: WAITING_PO + WAITING_PO → PO_REQUESTED', () => {
        const eff = effectivePanelStatus(projection([group('WAITING_PO'), group('WAITING_PO')]), 'WAITING_QUOTATION');
        assert.equal(eff, 'PO_REQUESTED');
        assert.equal(isOperationalPanelStatus(eff), true);
    });

    // CASE B — one PO already issued, one still waiting → PO_PARTIALLY_UPLOADED (only remaining group registers).
    test('CASE B: PO_ISSUED + WAITING_PO → PO_PARTIALLY_UPLOADED', () => {
        const eff = effectivePanelStatus(projection([group('PO_ISSUED'), group('WAITING_PO')]), 'WAITING_QUOTATION');
        assert.equal(eff, 'PO_PARTIALLY_UPLOADED');
        assert.equal(isOperationalPanelStatus(eff), true);
    });

    // CASE C — a group returned for correction alongside an issued one → WAITING_PO_CORRECTION.
    test('CASE C: PO_ISSUED + WAITING_PO_CORRECTION → WAITING_PO_CORRECTION', () => {
        const eff = effectivePanelStatus(projection([group('PO_ISSUED'), group('WAITING_PO_CORRECTION')]), 'WAITING_QUOTATION');
        assert.equal(eff, 'WAITING_PO_CORRECTION');
        assert.equal(isOperationalPanelStatus(eff), true);
    });

    // Mixed Register + Correct: only PO_PARTIALLY_UPLOADED satisfies both inner button gates.
    test('WAITING_PO + WAITING_PO_CORRECTION → PO_PARTIALLY_UPLOADED (shows both actions)', () => {
        const eff = effectivePanelStatus(projection([group('WAITING_PO'), group('WAITING_PO_CORRECTION')]), 'WAITING_QUOTATION');
        assert.equal(eff, 'PO_PARTIALLY_UPLOADED');
        assert.equal(isOperationalPanelStatus(eff), true);
    });

    // CASE D — an already-operational multi-unit scalar is preserved unchanged (no regression).
    test('CASE D: operational scalar preserved for multi-unit', () => {
        assert.equal(effectivePanelStatus(projection([group('PO_ISSUED'), group('WAITING_PO')]), 'PO_PARTIALLY_UPLOADED'), 'PO_PARTIALLY_UPLOADED');
        assert.equal(effectivePanelStatus(projection([group('WAITING_PO'), group('WAITING_PO')]), 'PO_REQUESTED'), 'PO_REQUESTED');
    });

    // CASE E — no actionable Buyer PO work: keep the persisted (non-operational) scalar.
    test('CASE E: no actionable group keeps the scalar', () => {
        assert.equal(effectivePanelStatus(projection([group('PO_ISSUED'), group('PO_ISSUED')]), 'WAITING_QUOTATION'), 'WAITING_QUOTATION');
        assert.equal(effectivePanelStatus(projection([batch('WAITING_AREA_APPROVAL'), batch('WAITING_AREA_APPROVAL')]), 'WAITING_QUOTATION'), 'WAITING_QUOTATION');
    });

    // Pre-PO PENDING groups never fabricate an operational status.
    test('multi PENDING groups with a lagging scalar stay non-operational', () => {
        assert.equal(effectivePanelStatus(projection([group('PENDING'), group('PENDING')]), 'WAITING_QUOTATION'), 'WAITING_QUOTATION');
    });

    // BATCH units alongside a WAITING_PO group: the batch does not count as "past the PO gate".
    test('BATCH unit does not force PO_PARTIALLY_UPLOADED', () => {
        const eff = effectivePanelStatus(projection([batch('WAITING_AREA_APPROVAL'), group('WAITING_PO')]), 'WAITING_QUOTATION');
        assert.equal(eff, 'PO_REQUESTED');
    });

    // Terminal scalar still wins even with lagging-looking groups.
    test('terminal scalar authoritative for multi-unit', () => {
        assert.equal(effectivePanelStatus(projection([group('WAITING_PO'), group('WAITING_PO')]), 'CANCELLED'), 'CANCELLED');
    });

    // The single-unit path is unchanged by the refactor.
    test('single-unit lagging scalar still maps from the unit', () => {
        assert.equal(effectivePanelStatus(projection([group('WAITING_PO')]), 'WAITING_QUOTATION'), 'PO_REQUESTED');
    });
});
