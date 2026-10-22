import { describe, it, expect } from 'vitest';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import queueSrc from './BuyerQueueList.tsx?raw';
import itemsTourSrc from '../../features/guided-tour/tours/buyerItemsPageTour.ts?raw';

/**
 * Placement of the claim/takeover action (source-level — no DOM runtime in this suite; visual wrapping must be checked
 * in a browser). Workspace: inside the upper-right header action group, never inside the "Comprador" Meta cell whose
 * value box clips (REQ-271). Queue: inside each row's kebab through the shared policy and dialog.
 */
describe('Workspace header placement', () => {
    const groupStart = workspaceSrc.indexOf('data-tour="buyer-workspace-actions"');
    const groupEnd = workspaceSrc.indexOf('{/* Meta grid */}');
    it('the action group exists, wraps, and contains the assign button before the quotation actions', () => {
        expect(groupStart).toBeGreaterThan(-1); expect(groupEnd).toBeGreaterThan(groupStart);
        const group = workspaceSrc.slice(groupStart, groupEnd);
        expect(group).toMatch(/flexWrap: 'wrap'/);
        const assignAt = group.indexOf('data-tour="buyer-workspace-assign"');
        expect(assignAt).toBeGreaterThan(-1);
        expect(assignAt).toBeLessThan(group.indexOf('Solicitar cotação'));
        expect(group).toContain('Importar Cotação'); expect(group).toContain('Inserir Manualmente');
    });
    it('the "Comprador" metadata cell shows only the Buyer name', () => {
        const meta = workspaceSrc.match(/<Meta icon=\{<UserIcon size=\{13\} \/>\} label="Comprador">([^]*?)<\/Meta>/);
        expect(meta).not.toBeNull();
        expect(meta![1]).toContain("ws.buyerName || 'Não atribuído'");
        expect(meta![1]).not.toContain('<button');
        expect(workspaceSrc).not.toContain('BuyerOwnerField');
    });
    it('the button keeps the shared decision, confirmation and busy guard', () => {
        const group = workspaceSrc.slice(groupStart, groupEnd);
        expect(group).toMatch(/assignAction\.kind !== 'NONE'/);
        expect(group).toMatch(/assignAction\.kind === 'TAKEOVER' \? setAssignConfirm\(true\) : void performAssign\('CLAIM'\)/);
        expect(group).toMatch(/disabled=\{assignBusy \|\| quotationBusy\}/);
    });
});

describe('Quotation queue kebab placement', () => {
    it('row kebab is built with the shared policy and the takeover opens the shared dialog', () => {
        expect(queueSrc).toMatch(/const kebab = buildRequestRowKebab\(\[/);
        expect(queueSrc).toMatch(/assignDecision=\{resolveQueueAssignAction\(item, user, ASSIGN_ROLE_NAMES\)\}/);
        expect(queueSrc).toMatch(/onTakeOver=\{\(\) => setTakeOverItem\(item\)\}/);
        expect(queueSrc).toMatch(/<TakeOverRequestDialog/);
        expect(queueSrc).toMatch(/data-tour="buyer-queue-row-actions"/);
    });
    it('submission reuses runAssignRequest with the displayed owner; success refreshes queue + summary; failed refresh is distinct; cancel just closes', () => {
        expect(queueSrc).toMatch(/takeOver: \(id, payload\) => api\.requests\.takeOverRequest\(id, payload\)/);
        expect(queueSrc).toMatch(/expectedBuyerId: item\.buyerId/);
        expect(queueSrc).toMatch(/onSuccess: \(msg\) => \{ setNotice\(msg\); void refreshAfterMutation\(\); \}/);
        expect(queueSrc).toMatch(/catch \{ setError\(QUEUE_REFRESH_AFTER_MUTATION_FAILED\); \}/);
        expect(queueSrc).toMatch(/isBusy: \(\) => takeOverBusyRef\.current \|\| busy/);
        expect(queueSrc).toMatch(/onCancel=\{\(\) => setTakeOverItem\(null\)\}/);
        expect(queueSrc).toMatch(/if \(outcome !== 'INVALID'\) setTakeOverItem\(null\)/);
    });
    it('queue tour has a step on the row actions anchor mentioning the takeover', () => {
        expect(itemsTourSrc).toContain('[data-tour="buyer-queue-row-actions"]');
        expect(itemsTourSrc).toContain('Assumir pedido de outro comprador');
    });
});
