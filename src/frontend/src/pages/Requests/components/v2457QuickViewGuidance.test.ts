import { describe, it, expect } from 'vitest';
// v2.245.7 — structural guards proving the Request Details / Quick View CONSUMER is wired to the
// authoritative projection loader + precedence rule (whose behavior is unit-tested at the boundary in
// src/lib/workflowProjectionGuidance.test.ts). Node-env vitest (no jsdom/RTL in this repository).
import edit from '../RequestEdit.tsx?raw';
import header from './RequestActionHeader.tsx?raw';
import panel from './RequestStatusActionPanels.tsx?raw';
import drawer from './modern/RequestDrawerPresentation.tsx?raw';
import print from './print/requestPrintModel.ts?raw';
import receivingOp from '../../Receiving/ReceivingOperation.tsx?raw';

describe('Quick View and full page share ONE details component (one projection fetch per rendered view)', () => {
  it('the drawer (Quick View) renders RequestEdit — the same component as the /requests/:id page', () => {
    expect(drawer).toMatch(/<RequestEdit requestId=\{requestId\} onClose=\{onClose\} \/>/);
  });

  it('RequestEdit requests the projection through the loader in a single effect (no per-type fetch gate, no second call site)', () => {
    expect(edit).toMatch(/loadWorkflowProjection\(api\.requests\.getWorkflowProjection, id, requestTypeCode, status,/);
    expect(edit).not.toMatch(/requestTypeCode !== 'QUOTATION'\) \{ setWorkflowProjection\(null\); return; \}/);
    // exactly one place calls the projection endpoint in the details view
    expect(edit.match(/getWorkflowProjection/g)?.length).toBe(1);
    expect(edit).toMatch(/\}, \[id, requestTypeCode, status\]\);/);
  });

  it('the fetch is cancellable; the projection object is exposed when loaded (last loaded kept during a reload, cleared on error/idle)', () => {
    expect(edit).toMatch(/load => \{ if \(!cancelled\) setProjectionLoad\(load\); \}, \(\) => cancelled\)/);
    expect(edit).toMatch(/const workflowProjection = projectionLoad\.state === 'loaded' \? projectionLoad\.projection\s*\n?\s*: projectionLoad\.state === 'loading' \? lastLoadedProjectionRef\.current : null;/);
    expect(edit).toMatch(/else if \(projectionLoad\.state !== 'loading'\) lastLoadedProjectionRef\.current = null;/);
  });

  it('the guidance pair follows the LIVE load state (placeholder while loading), not the cached projection', () => {
    expect(edit).toMatch(/resolveProjectionGuidance\(projectionLoad, requestTypeCode, status\)/);
    expect(edit).toMatch(/load: projectionLoad,/);
  });
});

describe('precedence: the header strip and the status panel consume the same rule', () => {
  it('RequestEdit derives the header guidance through resolveHeaderGuidance (Release-4 → loading → projection → scalar)', () => {
    expect(edit).toMatch(/operationalGuidance: resolveHeaderGuidance\(\{\s*status, requestTypeCode, load: projectionLoad, release4Guidance, scalarGuidance: getRequestGuidance,\s*\}\)/);
    // the old inline chain that let the scalar map win for non-QUOTATION requests is gone
    expect(edit).not.toMatch(/\|\| singleUnitGuidance\s+\/\/ projection truth/);
  });

  it('RequestEdit derives the panel guidance + loading flag from resolveProjectionGuidance and passes both', () => {
    expect(edit).toMatch(/const \{ guidance: singleUnitGuidance, loading: singleUnitGuidanceLoading \} = useMemo\(\s*\(\) => resolveProjectionGuidance\(projectionLoad, requestTypeCode, status\)/);
    expect(edit).toMatch(/singleUnitGuidance=\{singleUnitGuidance\}/);
    expect(edit).toMatch(/guidanceLoading=\{singleUnitGuidanceLoading\}/);
  });

  it('the panel prefers projection guidance over the generic map and shows the placeholder while loading', () => {
    expect(panel).toMatch(/guidanceLoading\s*\?\s*GUIDANCE_LOADING\s*:\s*\(singleUnitGuidance \?\? getRequestGuidance\(status \|\| '', requestTypeCode\)\)/);
    expect(panel).toMatch(/import \{ GUIDANCE_LOADING \} from '\.\.\/\.\.\/\.\.\/lib\/workflowProjection'/);
  });

  it('the header strip renders a muted placeholder (aria-busy) instead of a generic pair while loading', () => {
    expect(header).toMatch(/aria-busy=\{operationalGuidance\.loading \? true : undefined\}/);
    expect(header).toMatch(/operationalGuidance\.loading \? \(/);
    expect(header).toMatch(/loading\?: boolean;/);
  });

  it('QUOTATION-only presentation is preserved: multi-unit header, badge override and panel status stay gated on QUOTATION', () => {
    expect(edit).toMatch(/if \(requestTypeCode !== 'QUOTATION'\) return null;\s*\n\s*if \(!workflowProjection \|\| workflowProjection\.units\.length <= 1\) return null;/);
    expect(edit).toMatch(/requestTypeCode === 'QUOTATION' \? resolveDrawerBadgeOverride\(workflowProjection, status\) : null/);
    expect(edit).toMatch(/\(requestTypeCode === 'QUOTATION' && workflowProjection\)\s*\n?\s*\? effectivePanelStatus/);
  });

  it('completion-readiness is NOT used as a receiving-completeness signal (it gates only the Release-4 WAITING_RECEIPT guidance)', () => {
    expect(edit).toMatch(/release4Guidance = \(release4LegacyFinalizeSuppressed && completionReadiness\)/);
    expect(edit).not.toMatch(/completionReadiness\.complete/);
    expect(edit).not.toMatch(/lineItems\.every\(/);
  });
});

describe('regressions kept from v2.245.5 / v2.245.6', () => {
  it('adjustment toast rule still drives the receiving operation feedback', () => {
    expect(receivingOp).toMatch(/message: receiptSubmitSuccessMessage\(previousReceivedQty, receivedQty\)/);
  });
  it('RECEIVING_REOPENED keeps its history/print label', () => {
    expect(print).toMatch(/RECEIVING_REOPENED: 'Recebimento reaberto para correção'/);
  });
  it('the receiving operation page still renders its own confirmation hint (unchanged)', () => {
    expect(receivingOp).toMatch(/Recebimento completo — confirme o recebimento\./);
  });
});
