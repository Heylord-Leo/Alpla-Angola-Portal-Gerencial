import { describe, it, expect } from 'vitest';
import {
  loadWorkflowProjection,
  shouldFetchWorkflowProjection,
  projectionOwnsGuidance,
  resolveProjectionGuidance,
  resolveHeaderGuidance,
  buildActiveFlows,
  isOperationalPanelStatus,
  OPERATIONAL_PANEL_STATUSES,
  PAYMENT_ACTION_GUIDANCE_STATUSES,
  RECEIVING_GUIDANCE_STATUSES,
  GUIDANCE_LOADING,
  PROJECTION_IDLE,
  type ProjectionLoad,
} from './workflowProjection';
import { getRequestGuidance } from './utils';
import { resolveObligationActionPlan, resolveGroupFinanceButtons, obligationActionLabel, isAdvanceGroupStatus, isAdvanceObligation } from './financePaymentsView';
import financeListSource from '../pages/Finance/FinancePaymentsList.tsx?raw';
import typesSource from '../types/index.ts?raw';
import type { RequestWorkflowProjection, WorkflowUnit } from '../types';
import panelSource from '../pages/Requests/components/RequestStatusActionPanels.tsx?raw';
import editSource from '../pages/Requests/RequestEdit.tsx?raw';
// operationInvoiceView imports the API client (browser-only logger) — pinned structurally, not executed.
import completionSource from './operationInvoiceView.ts?raw';

// v2.245.11 — PAYMENT-type requests at ADVANCE_PAYMENT_SCHEDULED: the details header / Quick View drawer /
// status panel must read "Financeiro / Confirmar o pagamento do adiantamento" (the backend projection's
// GroupGuidance wording), never the generic "Não definido / Aguardar atualização do sistema" default that
// the scalar map used to fall through to. Tested at the consumer boundary the view calls verbatim.

const RESPONSIBLE = 'Financeiro';
const NEXT_ACTION = 'Confirmar o pagamento do adiantamento';
const DEFAULT_RESPONSIBLE = 'Não definido';
const DEFAULT_ACTION = 'Aguardar atualização do sistema';

function groupUnit(statusCode: string, actionType: string, label: string, role = RESPONSIBLE, over: Partial<WorkflowUnit> = {}): WorkflowUnit {
  return {
    unitType: 'GROUP', unitId: 'g1', label: 'Grupo Fornecedor', supplierName: 'Fornecedor', totalAmount: 100, currencyCode: 'AOA',
    itemCount: 1, itemLineNumbers: [1], statusCode, statusLabel: 'Adiantamento Agendado', approvalState: 'COMPLETE', poState: 'ISSUED',
    paymentState: 'ADVANCE_IN_PROGRESS', receivingState: 'NOT_STARTED', completionState: 'NOT_STARTED', responsibleRole: role,
    nextAction: { unitType: 'GROUP', unitId: 'g1', unitLabel: 'Grupo Fornecedor', actionType, label, responsibleRole: role, priority: 41 },
    ...over,
  };
}

function projection(units: WorkflowUnit[], aggregate = 'ADVANCE_PAYMENT_SCHEDULED'): RequestWorkflowProjection {
  return {
    aggregateDisplay: { statusCode: aggregate, label: 'Adiantamento Agendado' },
    units,
    responsibilities: [{ role: RESPONSIBLE, unitCount: units.length }],
    nextActions: units.map(u => u.nextAction!).filter(Boolean),
    warnings: [],
  };
}

const loaded = (p: RequestWorkflowProjection): ProjectionLoad => ({ state: 'loaded', projection: p });
const LOADING: ProjectionLoad = { state: 'loading', projection: null };
const ERROR: ProjectionLoad = { state: 'error', projection: null };

// The exact server payload for REQ-16/09/2026-424 (PAYMENT, one group, 100% advance scheduled).
const ADVANCE_SCHEDULED = projection([groupUnit('ADVANCE_PAYMENT_SCHEDULED', 'CONFIRM_ADVANCE', NEXT_ACTION)]);

const resolve = (status: string, load: ProjectionLoad, requestTypeCode = 'PAYMENT') =>
  resolveHeaderGuidance({ status, requestTypeCode, load, release4Guidance: null, scalarGuidance: getRequestGuidance });

describe('fetch policy — PAYMENT requests in actionable payment states fetch the authoritative projection', () => {
  it('PAYMENT + ADVANCE_PAYMENT_SCHEDULED: exactly ONE fetch, loading → loaded with the payload', async () => {
    const calls: string[] = [];
    const states: ProjectionLoad[] = [];
    await loadWorkflowProjection(async (id) => { calls.push(id); return ADVANCE_SCHEDULED; },
      'req-424', 'PAYMENT', 'ADVANCE_PAYMENT_SCHEDULED', s => states.push(s));
    expect(calls).toEqual(['req-424']);
    expect(states.map(s => s.state)).toEqual(['loading', 'loaded']);
    expect(states[1].projection).toBe(ADVANCE_SCHEDULED);
  });

  it('the pinned actionable payment statuses (ADVANCE_PAYMENT_REQUIRED / ADVANCE_PAYMENT_SCHEDULED / PAYMENT_SCHEDULED) fetch and are projection-owned', () => {
    expect([...PAYMENT_ACTION_GUIDANCE_STATUSES]).toEqual(['ADVANCE_PAYMENT_REQUIRED', 'ADVANCE_PAYMENT_SCHEDULED', 'PAYMENT_SCHEDULED']);
    for (const status of PAYMENT_ACTION_GUIDANCE_STATUSES) {
      expect(shouldFetchWorkflowProjection('r', 'PAYMENT', status), status).toBe(true);
      expect(projectionOwnsGuidance('PAYMENT', status), status).toBe(true);
    }
    // The receiving-phase policy (v2.245.7) is untouched.
    for (const status of RECEIVING_GUIDANCE_STATUSES) {
      expect(shouldFetchWorkflowProjection('r', 'PAYMENT', status), status).toBe(true);
    }
    // Statuses outside both phases keep the type-specific scalar map (no fetch, not owned).
    for (const status of ['PO_ISSUED', 'APPROVED', 'DRAFT', 'WAITING_SUPPLIER_DELIVERY', 'ADVANCE_PAYMENT_COMPLETED', 'COMPLETED']) {
      expect(shouldFetchWorkflowProjection('r', 'PAYMENT', status), status).toBe(false);
      expect(projectionOwnsGuidance('PAYMENT', status), status).toBe(false);
    }
    expect(shouldFetchWorkflowProjection('', 'PAYMENT', 'ADVANCE_PAYMENT_SCHEDULED')).toBe(false);
  });
});

describe('header / drawer / panel guidance for PAYMENT + ADVANCE_PAYMENT_SCHEDULED', () => {
  it('loaded projection owns the header: "Financeiro / Confirmar o pagamento do adiantamento"', () => {
    const g = resolve('ADVANCE_PAYMENT_SCHEDULED', loaded(ADVANCE_SCHEDULED));
    expect(g).toEqual({ responsible: RESPONSIBLE, nextAction: NEXT_ACTION });
    expect(g!.loading).toBeUndefined();
    // the panel consumes the same single-unit resolution
    expect(resolveProjectionGuidance(loaded(ADVANCE_SCHEDULED), 'PAYMENT', 'ADVANCE_PAYMENT_SCHEDULED'))
      .toEqual({ guidance: { responsible: RESPONSIBLE, nextAction: NEXT_ACTION }, loading: false });
  });

  it('projection failure → the truthful scalar fallback with the SAME wording (never the generic default)', () => {
    const fallback = getRequestGuidance('ADVANCE_PAYMENT_SCHEDULED', 'PAYMENT');
    expect(fallback).toEqual({ responsible: RESPONSIBLE, nextAction: NEXT_ACTION });
    expect(resolve('ADVANCE_PAYMENT_SCHEDULED', ERROR)).toEqual(fallback);
    expect(resolve('ADVANCE_PAYMENT_SCHEDULED', PROJECTION_IDLE)).toEqual(fallback);
  });

  it('neither path renders "Não definido" / "Aguardar atualização do sistema"; loading shows the placeholder', () => {
    for (const load of [loaded(ADVANCE_SCHEDULED), ERROR, PROJECTION_IDLE]) {
      const g = resolve('ADVANCE_PAYMENT_SCHEDULED', load)!;
      expect(g.responsible).not.toBe(DEFAULT_RESPONSIBLE);
      expect(g.nextAction).not.toBe(DEFAULT_ACTION);
      expect(g.responsible).toBe(RESPONSIBLE);
    }
    const loading = resolve('ADVANCE_PAYMENT_SCHEDULED', LOADING)!;
    expect(loading).toBe(GUIDANCE_LOADING);
    expect(loading.nextAction).not.toBe(DEFAULT_ACTION);
    // no due-date wording in either source
    expect(NEXT_ACTION).not.toMatch(/venc|prazo|data/i);
    expect(getRequestGuidance('ADVANCE_PAYMENT_SCHEDULED', 'PAYMENT').nextAction).not.toMatch(/venc|prazo|data/i);
  });

  it('the other pinned payment statuses keep a non-default scalar fallback too (projection owns them when loaded)', () => {
    for (const status of PAYMENT_ACTION_GUIDANCE_STATUSES) {
      const g = getRequestGuidance(status, 'PAYMENT');
      expect(g.responsible, status).toBe(RESPONSIBLE);
      expect(g.nextAction, status).not.toBe(DEFAULT_ACTION);
    }
    const scheduled = projection([groupUnit('PAYMENT_SCHEDULED', 'COMPLETE_PAYMENT', 'Realizar o pagamento')], 'PAYMENT_SCHEDULED');
    expect(resolve('PAYMENT_SCHEDULED', loaded(scheduled))).toEqual({ responsible: RESPONSIBLE, nextAction: 'Realizar o pagamento' });
    const required = projection([groupUnit('ADVANCE_PAYMENT_REQUIRED', 'SCHEDULE_ADVANCE', 'Agendar o adiantamento')], 'ADVANCE_PAYMENT_REQUIRED');
    expect(resolve('ADVANCE_PAYMENT_REQUIRED', loaded(required))).toEqual({ responsible: RESPONSIBLE, nextAction: 'Agendar o adiantamento' });
  });

  it('the status/action panel renders for ADVANCE_PAYMENT_SCHEDULED (allow-list and its exported mirror agree)', () => {
    expect(isOperationalPanelStatus('ADVANCE_PAYMENT_SCHEDULED')).toBe(true);
    const match = panelSource.match(/canExecuteOperationalAction && \[([^\]]+)\]\.includes\(status \|\| ''\)/);
    expect(match, 'panel allow-list not found').toBeTruthy();
    const panelList = match![1].split(',').map(s => s.trim().replace(/^'|'$/g, '')).filter(Boolean);
    expect(panelList).toContain('ADVANCE_PAYMENT_SCHEDULED');
    expect(new Set(panelList)).toEqual(OPERATIONAL_PANEL_STATUSES);
  });

  it('the print view receives the projection in the same cases the header uses it (consistency across surfaces)', () => {
    expect(editSource).toMatch(/projection=\{\(requestTypeCode === 'QUOTATION' \|\| singleUnitGuidance\) \? workflowProjection : null\}/);
  });
});

describe('Finance obligations row for a PAYMENT-type scheduled advance', () => {
  it('exposes PAY as the primary action (advance label), no SCHEDULE, keeps CANCEL_SCHEDULE / RETURN in the menu', () => {
    // financeActions exactly as FinancePaymentEligibilityService.EvaluateGroupActions now returns for
    // PAYMENT + parent ADVANCE_PAYMENT_SCHEDULED + group ADVANCE_PAYMENT_SCHEDULED.
    const plan = resolveObligationActionPlan({ groupStatusCode: 'ADVANCE_PAYMENT_SCHEDULED', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] });
    expect(plan.primary).toEqual({ action: 'PAY', label: 'Pagar adiantamento' });
    expect(plan.menu).toEqual(['DETAILS', 'NOTE', 'CANCEL_SCHEDULE', 'RETURN']);
    expect(plan.menu).not.toContain('SCHEDULE');
    expect(isAdvanceGroupStatus('ADVANCE_PAYMENT_SCHEDULED')).toBe(true);
    expect(obligationActionLabel('PAY', true)).toBe('Pagar adiantamento');
    expect(resolveGroupFinanceButtons({ status: 'ADVANCE_PAYMENT_SCHEDULED', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] }))
      .toEqual({ schedule: false, pay: true, cancelSchedule: true, return: true });
  });

  it('the UI routes PAY by the server-authoritative paymentFlow: ADVANCE → confirmAdvancePayment, STANDARD → markAsPaid (never a label)', () => {
    // The exact obligation the server returns for REQ-16/09/2026-424 (paymentFlow: ADVANCE).
    const advance = { requestPoGroupId: 'g1', groupStatusCode: 'ADVANCE_PAYMENT_SCHEDULED', paymentFlow: 'ADVANCE', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] };
    const standard = { requestPoGroupId: 'g2', groupStatusCode: 'PAYMENT_SCHEDULED', paymentFlow: 'STANDARD', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] };
    expect(isAdvanceObligation(advance)).toBe(true);
    expect(isAdvanceObligation(standard)).toBe(false);
    // The DTO carries the field and the list page dispatches on it.
    expect(typesSource).toMatch(/paymentFlow\?: 'ADVANCE' \| 'STANDARD' \| string \| null;/);
    expect(financeListSource).toMatch(/const isAdvance = isAdvanceObligation\(actionModal\.obligation\);/);
    expect(financeListSource).toMatch(/if \(isAdvance\) await api\.requests\.confirmAdvancePayment\(/);
    expect(financeListSource).toMatch(/else await api\.finance\.markAsPaid\(/);
    expect(financeListSource).toMatch(/if \(isAdvance\) await api\.requests\.scheduleAdvancePayment\(/);
    expect(financeListSource).toMatch(/else await api\.finance\.schedulePayment\(/);
    expect(financeListSource).toMatch(/isAdvance=\{isAdvanceObligation\(actionModal\.obligation\)\}/);
    // no remaining label/status-only route decision in the list page
    expect(financeListSource).not.toMatch(/isAdvanceGroupStatus\(/);
  });

  it('before scheduling (ADVANCE_PAYMENT_REQUIRED) SCHEDULE is primary; after scheduling it must not remain', () => {
    const before = resolveObligationActionPlan({ groupStatusCode: 'ADVANCE_PAYMENT_REQUIRED', financeActions: ['SCHEDULE', 'RETURN'] });
    expect(before.primary).toEqual({ action: 'SCHEDULE', label: 'Agendar adiantamento' });
    const after = resolveObligationActionPlan({ groupStatusCode: 'ADVANCE_PAYMENT_SCHEDULED', financeActions: ['PAY', 'CANCEL_SCHEDULE', 'RETURN'] });
    expect(after.primary!.action).toBe('PAY');
  });
});

describe('multi-group and completed-advance shapes', () => {
  it('two scheduled-advance groups (REQ-27/08/2026-333): group-specific actions, never collapsed into one unit text; header falls back truthfully', () => {
    const multi = projection([
      groupUnit('ADVANCE_PAYMENT_SCHEDULED', 'CONFIRM_ADVANCE', NEXT_ACTION, RESPONSIBLE, { unitId: 'gA', label: 'Grupo FORNECEDOR A', supplierName: 'FORNECEDOR A',
        nextAction: { unitType: 'GROUP', unitId: 'gA', unitLabel: 'Grupo FORNECEDOR A', actionType: 'CONFIRM_ADVANCE', label: NEXT_ACTION, responsibleRole: RESPONSIBLE, priority: 41 } }),
      groupUnit('ADVANCE_PAYMENT_SCHEDULED', 'CONFIRM_ADVANCE', NEXT_ACTION, RESPONSIBLE, { unitId: 'gB', label: 'Grupo FORNECEDOR B', supplierName: 'FORNECEDOR B',
        nextAction: { unitType: 'GROUP', unitId: 'gB', unitLabel: 'Grupo FORNECEDOR B', actionType: 'CONFIRM_ADVANCE', label: NEXT_ACTION, responsibleRole: RESPONSIBLE, priority: 41 } }),
    ]);
    // one next action per group, addressed to its own unit
    expect(multi.nextActions.map(a => a.unitLabel)).toEqual(['Grupo FORNECEDOR A', 'Grupo FORNECEDOR B']);
    expect(new Set(multi.nextActions.map(a => a.unitId)).size).toBe(2);
    expect(buildActiveFlows(multi)).toEqual(['Financeiro: 2 grupos — adiantamento agendado']);
    // multi-unit rule unchanged: not reduced to a single-unit text → the truthful scalar fallback is rendered
    expect(resolveProjectionGuidance(loaded(multi), 'PAYMENT', 'ADVANCE_PAYMENT_SCHEDULED')).toEqual({ guidance: null, loading: false });
    expect(resolve('ADVANCE_PAYMENT_SCHEDULED', loaded(multi))).toEqual({ responsible: RESPONSIBLE, nextAction: NEXT_ACTION });
  });

  it('completion card ("Aguardando pagamento — Financeiro") and header agree on the responsible party', () => {
    // blockingReasonText(PAYMENT_PENDING / FINANCE) = `${label} — ${owner}` with these two tables.
    expect(completionSource).toMatch(/PAYMENT_PENDING: 'Aguardando pagamento'/);
    expect(completionSource).toMatch(/FINANCE: 'Financeiro'/);
    expect(completionSource).toMatch(/return `\$\{label\} — \$\{owner\}`;/);
    expect(resolve('ADVANCE_PAYMENT_SCHEDULED', loaded(ADVANCE_SCHEDULED))!.responsible).toBe('Financeiro');
    expect(getRequestGuidance('ADVANCE_PAYMENT_SCHEDULED', 'PAYMENT').responsible).toBe('Financeiro');
  });

  it('a completed advance no longer asks Finance (WAITING_SUPPLIER_DELIVERY / ADVANCE_PAYMENT_COMPLETED)', () => {
    const delivered = projection([groupUnit('WAITING_SUPPLIER_DELIVERY', 'CONFIRM_DELIVERY', 'Acompanhar a entrega do fornecedor', 'Comprador')], 'WAITING_SUPPLIER_DELIVERY');
    // outside the owned phases the type-specific scalar map stands (unchanged B2P wording), and it is not Finance
    expect(resolveProjectionGuidance(loaded(delivered), 'PAYMENT', 'WAITING_SUPPLIER_DELIVERY').guidance).toBeNull();
    expect(resolve('WAITING_SUPPLIER_DELIVERY', loaded(delivered))).toEqual(getRequestGuidance('WAITING_SUPPLIER_DELIVERY', 'PAYMENT'));
    expect(getRequestGuidance('WAITING_SUPPLIER_DELIVERY', 'PAYMENT').nextAction).not.toBe(NEXT_ACTION);
    expect(getRequestGuidance('ADVANCE_PAYMENT_COMPLETED', 'PAYMENT').nextAction).not.toBe(NEXT_ACTION);
    expect(getRequestGuidance('ADVANCE_PAYMENT_COMPLETED', 'PAYMENT').responsible).not.toBe(DEFAULT_RESPONSIBLE);
  });

  it('QUOTATION behaviour unchanged: projection owns every non-terminal status', () => {
    expect(resolve('ADVANCE_PAYMENT_SCHEDULED', loaded(ADVANCE_SCHEDULED), 'QUOTATION')).toEqual({ responsible: RESPONSIBLE, nextAction: NEXT_ACTION });
    expect(projectionOwnsGuidance('QUOTATION', 'ADVANCE_PAYMENT_SCHEDULED')).toBe(true);
    expect(projectionOwnsGuidance('QUOTATION', 'COMPLETED')).toBe(false);
  });
});
