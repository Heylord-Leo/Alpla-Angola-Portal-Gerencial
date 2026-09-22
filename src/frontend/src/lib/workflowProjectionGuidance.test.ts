import { describe, it, expect } from 'vitest';
import {
  loadWorkflowProjection,
  shouldFetchWorkflowProjection,
  projectionOwnsGuidance,
  resolveProjectionGuidance,
  resolveHeaderGuidance,
  GUIDANCE_LOADING,
  PROJECTION_IDLE,
  RECEIVING_GUIDANCE_STATUSES,
  resolveSingleUnitGuidance,
  resolveDrawerBadgeOverride,
  effectivePanelStatus,
  type ProjectionLoad,
} from './workflowProjection';
import { getRequestGuidance } from './utils';
import type { RequestWorkflowProjection, WorkflowUnit } from '../types';

// v2.245.7 — Request Details / Quick View guidance consumption, tested at the CONSUMER boundary the view
// calls verbatim: the loader that decides/executes the `workflow-projection` fetch and the single
// precedence rule that turns { scalar status, projection load, Release-4 guidance } into the rendered
// "Responsável / Próxima ação". Payloads below mirror the backend RequestWorkflowProjectionBuilder
// output (camelCase) for the TEST scenario: a PAYMENT request, one IN_FOLLOWUP group, 2/2 received.

const CONFIRM_LABEL = 'Recebimento completo — confirmar recebimento';
const PENDING_LABEL = 'Resolver itens pendentes e confirmar recebimento';
const RECEIVE_LABEL = 'Mover para fase de recebimento e conferir itens';
const ATTACH_LABEL = 'Anexar recibo do fornecedor e finalizar pedido';

function groupUnit(statusCode: string, actionType: string, label: string, role = 'Recebimento', over: Partial<WorkflowUnit> = {}): WorkflowUnit {
  return {
    unitType: 'GROUP', unitId: 'g1', label: 'Grupo Fornecedor', supplierName: 'Fornecedor', totalAmount: 100, currencyCode: 'AOA',
    itemCount: 2, itemLineNumbers: [1, 2], statusCode, statusLabel: statusCode, approvalState: 'COMPLETE', poState: 'ISSUED',
    paymentState: 'COMPLETE', receivingState: 'IN_PROGRESS', completionState: 'NOT_STARTED', responsibleRole: role,
    nextAction: { unitType: 'GROUP', unitId: 'g1', unitLabel: 'Grupo Fornecedor', actionType, label, responsibleRole: role, priority: 67 },
    ...over,
  };
}

function projection(units: WorkflowUnit[], aggregate = 'IN_FOLLOWUP'): RequestWorkflowProjection {
  return {
    aggregateDisplay: { statusCode: aggregate, label: aggregate },
    units,
    responsibilities: units.map(u => ({ role: u.responsibleRole, unitCount: 1 })),
    nextActions: units.map(u => u.nextAction!).filter(Boolean),
    warnings: [],
  };
}

const loaded = (p: RequestWorkflowProjection): ProjectionLoad => ({ state: 'loaded', projection: p });
const LOADING: ProjectionLoad = { state: 'loading', projection: null };
const ERROR: ProjectionLoad = { state: 'error', projection: null };

// The exact server payloads for the TEST scenario and its neighbours.
const IN_FOLLOWUP_ALL_RECEIVED = projection([groupUnit('IN_FOLLOWUP', 'CONFIRM_RECEIVING', CONFIRM_LABEL)]);
const IN_FOLLOWUP_ONE_PENDING = projection([groupUnit('IN_FOLLOWUP', 'RESOLVE_FOLLOWUP', PENDING_LABEL)]);
const PAYMENT_COMPLETED_ALL_RECEIVED = projection([groupUnit('PAYMENT_COMPLETED', 'CONFIRM_RECEIVING', CONFIRM_LABEL)], 'PAYMENT_COMPLETED');
const PAYMENT_COMPLETED_PENDING = projection([groupUnit('PAYMENT_COMPLETED', 'RECEIVE', RECEIVE_LABEL)], 'PAYMENT_COMPLETED');
const WAITING_RECEIPT_CONFIRMED = projection([groupUnit('WAITING_RECEIPT', 'ATTACH_RECEIPT', ATTACH_LABEL, 'Financeiro', { receivingState: 'COMPLETE' })], 'WAITING_RECEIPT');

describe('loadWorkflowProjection — the details view requests the projection for receiving states of every type', () => {
  it('PAYMENT + IN_FOLLOWUP (the TEST scenario): exactly ONE fetch, loading → loaded with the payload', async () => {
    const calls: string[] = [];
    const states: ProjectionLoad[] = [];
    await loadWorkflowProjection(async (id) => { calls.push(id); return IN_FOLLOWUP_ALL_RECEIVED; },
      'request-a', 'PAYMENT', 'IN_FOLLOWUP', s => states.push(s));
    expect(calls).toEqual(['request-a']);
    expect(states.map(s => s.state)).toEqual(['loading', 'loaded']);
    expect(states[1].projection).toBe(IN_FOLLOWUP_ALL_RECEIVED);
  });

  it('fetches for every receiving-phase status of a non-QUOTATION request', async () => {
    for (const status of RECEIVING_GUIDANCE_STATUSES) {
      let calls = 0;
      await loadWorkflowProjection(async () => { calls++; return IN_FOLLOWUP_ALL_RECEIVED; }, 'r', 'PAYMENT', status, () => {});
      expect(calls, status).toBe(1);
    }
  });

  it('QUOTATION requests keep fetching for every status (v2.230.0 behavior unchanged)', async () => {
    let calls = 0;
    await loadWorkflowProjection(async () => { calls++; return IN_FOLLOWUP_ALL_RECEIVED; }, 'r', 'QUOTATION', 'APPROVED', () => {});
    expect(calls).toBe(1);
  });

  it('does NOT fetch when the projection cannot own the guidance (no id / non-receiving status of a PAYMENT) → idle', async () => {
    let calls = 0;
    const states: ProjectionLoad[] = [];
    await loadWorkflowProjection(async () => { calls++; return IN_FOLLOWUP_ALL_RECEIVED; }, null, 'PAYMENT', 'IN_FOLLOWUP', s => states.push(s));
    await loadWorkflowProjection(async () => { calls++; return IN_FOLLOWUP_ALL_RECEIVED; }, 'r', 'PAYMENT', 'DRAFT', s => states.push(s));
    await loadWorkflowProjection(async () => { calls++; return IN_FOLLOWUP_ALL_RECEIVED; }, 'r', 'PAYMENT', 'PO_ISSUED', s => states.push(s));
    expect(calls).toBe(0);
    expect(states).toEqual([PROJECTION_IDLE, PROJECTION_IDLE, PROJECTION_IDLE]);
  });

  it('a failed fetch resolves (never rejects) and emits error — the view falls back, it does not crash', async () => {
    const states: ProjectionLoad[] = [];
    await expect(loadWorkflowProjection(async () => { throw new Error('boom'); }, 'r', 'PAYMENT', 'IN_FOLLOWUP', s => states.push(s)))
      .resolves.toBeUndefined();
    expect(states.map(s => s.state)).toEqual(['loading', 'error']);
    expect(states[1].projection).toBeNull();
  });

  it('a cancelled load (view closed / deps changed) emits nothing after cancellation', async () => {
    const states: ProjectionLoad[] = [];
    let cancelled = false;
    const p = loadWorkflowProjection(async () => { cancelled = true; return IN_FOLLOWUP_ALL_RECEIVED; }, 'r', 'PAYMENT', 'IN_FOLLOWUP', s => states.push(s), () => cancelled);
    await p;
    expect(states.map(s => s.state)).toEqual(['loading']);
  });

  it('shouldFetchWorkflowProjection mirrors the loader policy', () => {
    expect(shouldFetchWorkflowProjection('r', 'PAYMENT', 'IN_FOLLOWUP')).toBe(true);
    expect(shouldFetchWorkflowProjection('r', 'PAYMENT', 'PAYMENT_COMPLETED')).toBe(true);
    expect(shouldFetchWorkflowProjection('r', 'PAYMENT', 'WAITING_RECEIPT')).toBe(true);
    expect(shouldFetchWorkflowProjection('r', 'QUOTATION', 'DRAFT')).toBe(true);
    expect(shouldFetchWorkflowProjection('r', 'PAYMENT', 'DRAFT')).toBe(false);
    expect(shouldFetchWorkflowProjection('r', 'PAYMENT', 'PO_ISSUED')).toBe(false);
    expect(shouldFetchWorkflowProjection('', 'QUOTATION', 'APPROVED')).toBe(false);
  });
});

describe('resolveHeaderGuidance — what the Quick View / full page header and status panel render', () => {
  const resolve = (status: string, load: ProjectionLoad, requestTypeCode = 'PAYMENT', release4Guidance: any = null) =>
    resolveHeaderGuidance({ status, requestTypeCode, load, release4Guidance, scalarGuidance: getRequestGuidance });

  it('1. IN_FOLLOWUP, one group, all items received → confirmation guidance, pending text absent', () => {
    const g = resolve('IN_FOLLOWUP', loaded(IN_FOLLOWUP_ALL_RECEIVED));
    expect(g).toEqual({ responsible: 'Recebimento', nextAction: CONFIRM_LABEL });
    expect(g!.nextAction).not.toContain('pendentes');
    expect(g!.loading).toBeUndefined();
  });

  it('2. IN_FOLLOWUP with one incomplete item → pending-item guidance remains', () => {
    expect(resolve('IN_FOLLOWUP', loaded(IN_FOLLOWUP_ONE_PENDING)))
      .toEqual({ responsible: 'Recebimento', nextAction: PENDING_LABEL });
  });

  it('3. PAYMENT_COMPLETED with all items received → projection confirmation guidance (not "mover para recebimento")', () => {
    expect(resolve('PAYMENT_COMPLETED', loaded(PAYMENT_COMPLETED_ALL_RECEIVED)))
      .toEqual({ responsible: 'Recebimento', nextAction: CONFIRM_LABEL });
    expect(resolve('PAYMENT_COMPLETED', loaded(PAYMENT_COMPLETED_PENDING)))
      .toEqual({ responsible: 'Recebimento', nextAction: RECEIVE_LABEL });
  });

  it('4. WAITING_RECEIPT (confirmed) → post-confirmation Finance guidance, never editable/pending receiving', () => {
    const g = resolve('WAITING_RECEIPT', loaded(WAITING_RECEIPT_CONFIRMED));
    expect(g).toEqual({ responsible: 'Financeiro', nextAction: ATTACH_LABEL });
    expect(g!.nextAction).not.toMatch(/pendentes|confirmar recebimento/);
    // Release-4 completion guidance stays the most specific source for WAITING_RECEIPT (unchanged precedence)
    const r4 = { responsible: 'Financeiro', nextAction: 'Classificar o documento de origem' };
    expect(resolve('WAITING_RECEIPT', loaded(WAITING_RECEIPT_CONFIRMED), 'PAYMENT', r4)).toBe(r4);
  });

  it('5. precedence: the generic IN_FOLLOWUP scalar map says "pendentes", the loaded projection wins', () => {
    expect(getRequestGuidance('IN_FOLLOWUP', 'PAYMENT').nextAction).toBe(PENDING_LABEL); // what the map would say
    expect(resolve('IN_FOLLOWUP', loaded(IN_FOLLOWUP_ALL_RECEIVED))!.nextAction).toBe(CONFIRM_LABEL);
  });

  it('6. loading: the placeholder is rendered — the generic text is never flashed before the projection resolves', () => {
    const g = resolve('IN_FOLLOWUP', LOADING);
    expect(g).toBe(GUIDANCE_LOADING);
    expect(g!.loading).toBe(true);
    expect(g!.nextAction).not.toBe(PENDING_LABEL);
    expect(g!.nextAction).toBe('Carregando próxima ação...');
    // QUOTATION requests get the same no-flash behavior for any non-terminal status
    expect(resolve('APPROVED', LOADING, 'QUOTATION')).toBe(GUIDANCE_LOADING);
  });

  it('7. projection failure: the conservative legacy scalar guidance is rendered (documented fallback), no crash', () => {
    expect(resolve('IN_FOLLOWUP', ERROR)).toEqual(getRequestGuidance('IN_FOLLOWUP', 'PAYMENT'));
    expect(resolve('PAYMENT_COMPLETED', ERROR)).toEqual(getRequestGuidance('PAYMENT_COMPLETED', 'PAYMENT'));
    expect(resolve('APPROVED', ERROR, 'QUOTATION')).toEqual(getRequestGuidance('APPROVED', 'QUOTATION'));
  });

  it('idle (projection not needed) → legacy scalar guidance, no placeholder', () => {
    expect(resolve('PO_ISSUED', PROJECTION_IDLE)).toEqual(getRequestGuidance('PO_ISSUED', 'PAYMENT'));
    expect(resolve('DRAFT', PROJECTION_IDLE)).toEqual(getRequestGuidance('DRAFT', 'PAYMENT'));
    expect(resolve('DRAFT', LOADING)).toEqual(getRequestGuidance('DRAFT', 'PAYMENT')); // never "loading" for DRAFT
  });

  it('no status → nothing to render', () => {
    expect(resolve('', loaded(IN_FOLLOWUP_ALL_RECEIVED))).toBeNull();
  });
});

describe('multi-group / scope preservation (the projection builder owns group semantics)', () => {
  it('multiple groups with mixed statuses → not reduced to a single-unit text; legacy scalar fallback (multi-unit rule unchanged)', () => {
    const multi = projection([
      groupUnit('WAITING_RECEIPT', 'ATTACH_RECEIPT', ATTACH_LABEL, 'Financeiro'),
      { ...groupUnit('IN_FOLLOWUP', 'RESOLVE_FOLLOWUP', PENDING_LABEL), unitId: 'g2' },
    ]);
    const r = resolveProjectionGuidance(loaded(multi), 'PAYMENT', 'IN_FOLLOWUP');
    expect(r).toEqual({ guidance: null, loading: false });
    expect(resolveHeaderGuidance({ status: 'IN_FOLLOWUP', requestTypeCode: 'PAYMENT', load: loaded(multi), scalarGuidance: getRequestGuidance }))
      .toEqual(getRequestGuidance('IN_FOLLOWUP', 'PAYMENT'));
  });

  it('non-QUOTATION: only an operational GROUP unit in the receiving phase is projection-owned (batches / other phases keep the type-specific map)', () => {
    const batch: WorkflowUnit = { ...groupUnit('FINAL_ADJUSTMENT', 'RESOLVE_ADJUSTMENT', 'Revisar o lote', 'Comprador'), unitType: 'BATCH', batchNumber: 1 };
    expect(resolveProjectionGuidance(loaded(projection([batch])), 'PAYMENT', 'IN_FOLLOWUP').guidance).toBeNull();
    const lagging = projection([groupUnit('PO_ISSUED', 'SCHEDULE_PAYMENT', 'Pagar ou agendar o pagamento', 'Financeiro')]);
    expect(resolveProjectionGuidance(loaded(lagging), 'PAYMENT', 'IN_FOLLOWUP').guidance).toBeNull();
    expect(projectionOwnsGuidance('PAYMENT', 'WAITING_SUPPLIER_DELIVERY')).toBe(false); // B2P wording stays type-specific
    expect(projectionOwnsGuidance('PAYMENT', 'IN_FOLLOWUP')).toBe(true);
    expect(projectionOwnsGuidance('QUOTATION', 'WAITING_SUPPLIER_DELIVERY')).toBe(true); // QUOTATION unchanged
    expect(projectionOwnsGuidance('QUOTATION', 'COMPLETED')).toBe(false);
  });

  it('terminal scalars stay authoritative for every type', () => {
    for (const t of ['CANCELLED', 'REJECTED', 'COMPLETED']) {
      expect(resolveProjectionGuidance(loaded(IN_FOLLOWUP_ALL_RECEIVED), 'PAYMENT', t)).toEqual({ guidance: null, loading: false });
      expect(resolveProjectionGuidance(LOADING, 'QUOTATION', t)).toEqual({ guidance: null, loading: false });
    }
  });

  it('existing v2.230.0 mappings are untouched (regression)', () => {
    const p = projection([groupUnit('PO_ISSUED', 'SCHEDULE_PAYMENT', 'Pagar ou agendar o pagamento', 'Financeiro')]);
    expect(resolveSingleUnitGuidance(p, 'APPROVED')).toEqual({ responsible: 'Financeiro', nextAction: 'Pagar ou agendar o pagamento' });
    expect(resolveDrawerBadgeOverride(p, 'APPROVED')).toEqual({ code: 'PO_ISSUED', label: 'PO_ISSUED' });
    expect(effectivePanelStatus(p, 'APPROVED')).toBe('PO_ISSUED');
  });
});
