import { describe, it, expect, beforeAll } from 'vitest';
import type {
  OperationInvoiceObligationDto,
  CompletionReadinessDto,
  CompletionReadinessGroupDto,
} from '../types/operationInvoice';

// v2.245.8 — pure presentation rules of the classification / completion flow, tested at the consumer
// boundary the drawer calls (OperationInvoiceSection, RequestEdit, RequestStatusActionPanels).
//
// operationInvoiceView.ts imports ApiError from lib/api, whose module graph touches browser storage at
// load time; this suite runs in the node environment (repository policy: no jsdom), so a minimal storage
// shim is installed BEFORE the module is imported dynamically. Nothing under test uses the shim.
type View = typeof import('./operationInvoiceView');
type ApiModule = typeof import('./api');
let view: View;
let ApiError: ApiModule['ApiError'];

beforeAll(async () => {
  const memoryStorage = () => {
    const map = new Map<string, string>();
    return {
      getItem: (k: string) => map.get(k) ?? null,
      setItem: (k: string, v: string) => { map.set(k, String(v)); },
      removeItem: (k: string) => { map.delete(k); },
      clear: () => map.clear(),
      key: (i: number) => Array.from(map.keys())[i] ?? null,
      get length() { return map.size; },
    };
  };
  const g = globalThis as any;
  g.localStorage ??= memoryStorage();
  g.sessionStorage ??= memoryStorage();
  view = await import('./operationInvoiceView');
  ApiError = (await import('./api')).ApiError;
});

const isClassificationPending: View['isClassificationPending'] = (o) => view.isClassificationPending(o);
const hasRegistrableObligation: View['hasRegistrableObligation'] = (o) => view.hasRegistrableObligation(o);
const isGroupAllocatable: View['isGroupAllocatable'] = (o) => view.isGroupAllocatable(o);
const isShortCloseProposable: View['isShortCloseProposable'] = (o) => view.isShortCloseProposable(o);
const legacyCompletionGuidance: View['legacyCompletionGuidance'] = (r, s) => view.legacyCompletionGuidance(r, s);
const mapOperationInvoiceError: View['mapOperationInvoiceError'] = (e) => view.mapOperationInvoiceError(e);
const blockingReasonText: View['blockingReasonText'] = (r) => view.blockingReasonText(r);

function obligation(over: Partial<OperationInvoiceObligationDto> = {}): OperationInvoiceObligationDto {
  return {
    groupId: 'g1', supplierId: 10, supplierName: 'Fornecedor', currency: 'AOA',
    sourceDocumentType: 'PROFORMA', requiresOperationInvoice: true,
    expectedAmount: 250000, expectedCurrency: 'AOA',
    validatedCoveredAmount: 0, pendingCoveredAmount: 0, remainingAmount: 250000, appliedTolerance: 250,
    coveragePercent: 0, allocations: [],
    derivedStatus: 'PENDING_UPLOAD', persistedStatus: 'PENDING_UPLOAD', statusDrift: false, closedShort: false,
    purchaseOrderNumber: 'PO-1', paymentSourceDocumentIds: [], lineItemCount: 2,
    reasonCode: 'AWAITING_OPERATION_INVOICE', explanation: '',
    ...over,
  };
}

/** The affected legacy shape exactly as the obligations endpoint returns it. */
const LEGACY_UNCLASSIFIED = obligation({
  sourceDocumentType: null, requiresOperationInvoice: true,
  expectedAmount: null, expectedCurrency: 'AOA', remainingAmount: null, coveragePercent: null,
  derivedStatus: 'UNCLASSIFIED', persistedStatus: 'UNCLASSIFIED', reasonCode: 'CLASSIFICATION_PENDING',
});

function group(over: Partial<CompletionReadinessGroupDto> = {}): CompletionReadinessGroupDto {
  return {
    groupId: 'g1', supplierName: 'Fornecedor', groupStatusCode: 'WAITING_RECEIPT',
    classified: true, poSatisfied: true, noBlockingCorrection: true, paymentSatisfied: true, receiptSatisfied: true,
    operationInvoiceSatisfied: false, closedShort: false, fiscalReceiptRequired: true, fiscalReceiptSatisfied: false,
    complete: false, blockingReasons: [], ...over,
  };
}

function readiness(groups: CompletionReadinessGroupDto[], lifecycle = false): CompletionReadinessDto {
  return {
    completionLifecycleEnabled: lifecycle, requestStatusCode: 'WAITING_RECEIPT',
    isCompletionReady: false, isCompleted: false, hasActiveReconciliation: false,
    totalGroupCount: groups.length, completedGroupCount: 0, blockingGroupCount: groups.length, groups,
  };
}

const CLASSIFICATION_PENDING = { code: 'CLASSIFICATION_PENDING', ownerCode: 'FINANCE_ADMIN' };
const INVOICE_PENDING = { code: 'OPERATION_INVOICE_PENDING', ownerCode: 'FINANCE' };
const FISCAL_PENDING = { code: 'FISCAL_RECEIPT_PENDING', ownerCode: 'FINANCE' };

describe('classification pending / registrable obligation', () => {
  it('a legacy UNCLASSIFIED group is classification-pending even though the read model derives it as "owed"', () => {
    expect(LEGACY_UNCLASSIFIED.requiresOperationInvoice).toBe(true); // fail-closed derivation
    expect(isClassificationPending(LEGACY_UNCLASSIFIED)).toBe(true);
    expect(isGroupAllocatable(LEGACY_UNCLASSIFIED)).toBe(false);
    expect(isShortCloseProposable(LEGACY_UNCLASSIFIED)).toBe(false);
  });

  it('a classified group is not pending; a null sourceDocumentType with a derived status still is', () => {
    expect(isClassificationPending(obligation())).toBe(false);
    expect(isClassificationPending(obligation({ sourceDocumentType: null }))).toBe(true);
  });

  it('"Registrar Fatura Final" exists only when some obligation could take the invoice', () => {
    expect(hasRegistrableObligation([LEGACY_UNCLASSIFIED])).toBe(false);
    expect(hasRegistrableObligation([])).toBe(false);
    expect(hasRegistrableObligation([obligation()])).toBe(true);
    expect(hasRegistrableObligation([LEGACY_UNCLASSIFIED, obligation({ groupId: 'g2' })])).toBe(true); // multi-group: one classified suffices
    expect(hasRegistrableObligation([obligation({ derivedStatus: 'NOT_REQUIRED', requiresOperationInvoice: false })])).toBe(false);
    expect(view.REGISTER_BLOCKED_BY_CLASSIFICATION).toMatch(/Classificar Documento de Origem/);
  });

  it('after classification the same obligation becomes allocatable and short-close-proposable', () => {
    const classified = obligation({ groupId: LEGACY_UNCLASSIFIED.groupId });
    expect(isClassificationPending(classified)).toBe(false);
    expect(hasRegistrableObligation([classified])).toBe(true);
    expect(isShortCloseProposable(classified)).toBe(true);
  });
});

describe('legacyCompletionGuidance — WAITING_RECEIPT under the legacy finalization path', () => {
  it('active supplier receipt + classification pending → classification is the next action and finalization is blocked', () => {
    const g = legacyCompletionGuidance(readiness([group({ classified: false, blockingReasons: [CLASSIFICATION_PENDING, INVOICE_PENDING] })]), 'WAITING_RECEIPT');
    expect(g).toEqual({
      responsible: 'Financeiro / Administração',
      nextAction: 'Classificar o documento de origem do grupo (Fatura Final — Cobertura) antes de finalizar',
      blocksLegacyFinalize: true,
    });
    expect(g!.responsible).toBe(view.COMPLETION_OWNER_LABELS.FINANCE_ADMIN);
    expect(blockingReasonText(CLASSIFICATION_PENDING)).toBe('Classificação pendente — Financeiro / Administração');
  });

  it('after classification the guidance advances to the final invoice and finalization is no longer blocked', () => {
    const g = legacyCompletionGuidance(readiness([group({ blockingReasons: [INVOICE_PENDING, FISCAL_PENDING] })]), 'WAITING_RECEIPT');
    expect(g).toEqual({
      responsible: 'Financeiro',
      nextAction: 'Registrar / validar a Fatura Final e finalizar o pedido',
      blocksLegacyFinalize: false,
    });
  });

  it('nothing pending on the invoice dimension → null (the projection wording stands)', () => {
    expect(legacyCompletionGuidance(readiness([group({ operationInvoiceSatisfied: true, blockingReasons: [FISCAL_PENDING] })]), 'WAITING_RECEIPT')).toBeNull();
  });

  it('never applies outside WAITING_RECEIPT, while the lifecycle is active, or without readiness', () => {
    const r = readiness([group({ classified: false, blockingReasons: [CLASSIFICATION_PENDING] })]);
    expect(legacyCompletionGuidance(r, 'IN_FOLLOWUP')).toBeNull();
    expect(legacyCompletionGuidance(r, 'PAYMENT_COMPLETED')).toBeNull();
    expect(legacyCompletionGuidance(readiness(r.groups, true), 'WAITING_RECEIPT')).toBeNull();
    expect(legacyCompletionGuidance(null, 'WAITING_RECEIPT')).toBeNull();
    expect(legacyCompletionGuidance(undefined, 'WAITING_RECEIPT')).toBeNull();
  });

  it('completed and cancelled groups are ignored (multi-group isolation)', () => {
    const done = group({ groupId: 'done', groupStatusCode: 'COMPLETED', classified: false, blockingReasons: [CLASSIFICATION_PENDING] });
    const cancelled = group({ groupId: 'x', groupStatusCode: 'CANCELLED', classified: false, blockingReasons: [CLASSIFICATION_PENDING] });
    expect(legacyCompletionGuidance(readiness([done, cancelled, group({ operationInvoiceSatisfied: true })]), 'WAITING_RECEIPT')).toBeNull();
    const open = group({ groupId: 'open', classified: false, blockingReasons: [CLASSIFICATION_PENDING] });
    expect(legacyCompletionGuidance(readiness([done, open]), 'WAITING_RECEIPT')!.blocksLegacyFinalize).toBe(true);
  });
});

describe('backend error mapping for the classification endpoint', () => {
  const apiError = (code: string, status = 409) =>
    new ApiError('x', status, undefined, undefined, code, undefined, { title: 't', detail: 'd', status, code });

  it('maps the typed codes to actionable Portuguese', () => {
    expect(mapOperationInvoiceError(apiError('OI_CLASSIFICATION_ALREADY_SET')).message).toMatch(/já tem o documento de origem classificado/);
    expect(mapOperationInvoiceError(apiError('OI_CLASSIFICATION_ACTIVITY_EXISTS')).message).toMatch(/faturas finais distribuídas/);
    expect(mapOperationInvoiceError(apiError('OI_CLASSIFICATION_NOT_ELIGIBLE')).message).toMatch(/não permite classificar/);
    expect(mapOperationInvoiceError(apiError('OPERATION_INVOICE_NO_OBLIGATION')).message).toMatch(/grupo classificado que exija Fatura Final/);
    expect(mapOperationInvoiceError(apiError('OPERATION_INVOICE_ATTACHMENT_CLAIMED')).code).toBe('OPERATION_INVOICE_ATTACHMENT_CLAIMED');
    expect(mapOperationInvoiceError(apiError('OPERATION_INVOICE_ATTACHMENT_NOT_RELEASABLE')).message).toMatch(/sem fatura associada/);
  });

  it('a concurrent classification is a reload situation', () => {
    const mapped = mapOperationInvoiceError(apiError('OI_CLASSIFICATION_CONCURRENCY'));
    expect(mapped.isConcurrency).toBe(true);
  });
});
