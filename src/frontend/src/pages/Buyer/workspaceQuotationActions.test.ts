import { describe, it, expect, vi } from 'vitest';
import {
    QUOTATION_MUTABLE_REQUEST_STATUSES,
    buildDeleteQuotationConfirmation,
    canManageWorkspaceQuotations,
    describeWorkspaceQuotation,
    runDeleteQuotation,
} from './workspaceQuotationActions';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import hostSrc from './QuotationWizard/hooks/useWorkspaceWizardHost.ts?raw';
import type { BuyerWorkspaceQuotation } from '../../types/buyerWorkspace';

const q = (over: Partial<BuyerWorkspaceQuotation> = {}): BuyerWorkspaceQuotation => ({
    id: 'bc0ee2e8-156a-4b97-aeee-195980b2ddc4', supplierId: 7, supplierName: 'Socimorcasal Angola', documentNumber: 'OR 3602/10/CM',
    documentDate: '2026-10-15T00:00:00Z', itemsQuotedCount: 4, currency: 'AOA', totalAmount: 239099.04, documentCount: 1, isSelected: false, ...over,
});

describe('canManageWorkspaceQuotations — classic parity: quotation window AND assigned Buyer', () => {
    const ME = 'u-samuel', OTHER = 'u-celestina';
    it('allows the assigned Buyer only while the request is in the quotation window', () => {
        for (const s of QUOTATION_MUTABLE_REQUEST_STATUSES) expect(canManageWorkspaceQuotations(s, ['Buyer'], 'Buyer', ME, ME)).toBe(true);
        for (const s of ['FINAL_APPROVED', 'PO_ISSUED', 'WAITING_PO_CORRECTION', 'WAITING_AREA_APPROVAL', 'COMPLETED', 'CANCELLED'])
            expect(canManageWorkspaceQuotations(s, ['Buyer'], 'Buyer', ME, ME)).toBe(false);
    });
    it('denies another Buyer, an unassigned request, and users without the Buyer role (same as the classic list)', () => {
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Buyer'], 'Buyer', OTHER, ME)).toBe(false);      // assigned to someone else
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Buyer'], 'Buyer', null, ME)).toBe(false);       // unassigned → claim first
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Finance'], 'Buyer', ME, ME)).toBe(false);
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', undefined, 'Buyer', ME, ME)).toBe(false);
        expect(canManageWorkspaceQuotations(undefined, ['Buyer'], 'Buyer', ME, ME)).toBe(false);
    });
    it('System Administrator and Local Manager see the buttons only when they are themselves the assigned Buyer with the Buyer role (no broadening)', () => {
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['System Administrator'], 'Buyer', OTHER, ME)).toBe(false);
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['System Administrator'], 'Buyer', ME, ME)).toBe(false);       // admin without Buyer role
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Local Manager'], 'Buyer', ME, ME)).toBe(false);
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['System Administrator', 'Buyer'], 'Buyer', ME, ME)).toBe(true);  // combined roles, assigned
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Local Manager', 'Buyer'], 'Buyer', OTHER, ME)).toBe(false);     // combined roles, not assigned
    });
});

describe('identification and confirmation text', () => {
    it('identifies supplier, document and date', () => {
        expect(describeWorkspaceQuotation(q())).toBe('Socimorcasal Angola · Doc OR 3602/10/CM · 15/10/2026');
        expect(describeWorkspaceQuotation(q({ supplierName: null, documentNumber: null, documentDate: null }))).toBe('Fornecedor não identificado · Doc S/N · sem data');
    });
    it('states the endpoint consequences: hard delete of lines, linked document marked removed, batch refusal, audit', () => {
        const c = buildDeleteQuotationConfirmation(q());
        expect(c.title).toBe('Excluir cotação');
        expect(c.lines[0]).toContain('Socimorcasal Angola · Doc OR 3602/10/CM · 15/10/2026');
        expect(c.lines[1]).toContain('4 linha(s) serão removidas definitivamente');
        expect(c.lines.some(l => l.includes('documento associado') && l.includes('marcado como removido'))).toBe(true);
        expect(c.lines.some(l => l.includes('lote de aprovação') && l.includes('histórico'))).toBe(true);
        expect(c.lines.some(l => l.includes('selecionada'))).toBe(false);
    });
    it('does not promise a document removal when none is linked; flags a selected quotation', () => {
        const c = buildDeleteQuotationConfirmation(q({ documentCount: 0, isSelected: true }));
        expect(c.lines.some(l => l.includes('não tem documento associado'))).toBe(true);
        expect(c.lines.some(l => l.includes('marcado como removido'))).toBe(false);
        expect(c.lines.some(l => l.includes('selecionada'))).toBe(true);
    });
});

describe('runDeleteQuotation — one submission at a time, server text surfaced', () => {
    function deps(impl: () => Promise<void>) {
        let busy = false; const log: string[] = [];
        return {
            d: {
                deleteQuotation: vi.fn(impl),
                isBusy: () => busy,
                setBusy: (b: boolean) => { busy = b; log.push(`busy=${b}`); },
                onSuccess: vi.fn((m: string) => log.push(`ok:${m}`)),
                onError: vi.fn((m: string) => log.push(`err:${m}`)),
            }, log, getBusy: () => busy,
        };
    }
    it('success: calls the API once, reports success, resets busy', async () => {
        const { d, log, getBusy } = deps(async () => {});
        expect(await runDeleteQuotation(d, 'req', q())).toBe('DELETED');
        expect(d.deleteQuotation).toHaveBeenCalledWith('req', 'bc0ee2e8-156a-4b97-aeee-195980b2ddc4');
        expect(d.onSuccess).toHaveBeenCalledWith('Cotação excluída: Socimorcasal Angola · Doc OR 3602/10/CM · 15/10/2026.');
        expect(log).toEqual(['busy=true', expect.stringMatching(/^ok:/), 'busy=false']);
        expect(getBusy()).toBe(false);
    });
    it('refused by the server (409 batch reference / window): error surfaced verbatim, busy reset, no success', async () => {
        const { d, getBusy } = deps(async () => { throw new Error('Esta cotação já foi utilizada em um lote de aprovação e não pode ser excluída.'); });
        expect(await runDeleteQuotation(d, 'req', q())).toBe('FAILED');
        expect(d.onError).toHaveBeenCalledWith('Esta cotação já foi utilizada em um lote de aprovação e não pode ser excluída.');
        expect(d.onSuccess).not.toHaveBeenCalled();
        expect(getBusy()).toBe(false);
    });
    it('a second submission while one is in flight is skipped without calling the API', async () => {
        let resolve!: () => void;
        const { d } = deps(() => new Promise<void>(r => { resolve = r; }));
        const first = runDeleteQuotation(d, 'req', q());
        expect(await runDeleteQuotation(d, 'req', q())).toBe('SKIPPED_BUSY');
        resolve();
        expect(await first).toBe('DELETED');
        expect(d.deleteQuotation).toHaveBeenCalledTimes(1);
    });
});

describe('workspace wiring (source-level — no DOM runtime in this suite)', () => {
    it('the wizard host exposes an in-place EDIT that reuses the shared controller without a rework batch', () => {
        expect(hostSrc).toMatch(/const openEditQuotation = async \(requestId: string, quotationId: string\)/);
        expect(hostSrc).toMatch(/controller\.handleOpenWizard\(toWizardActiveRequest\(request, requestId\), quotationEditMode\(quotation\), quotation\);/);
        expect(hostSrc).toMatch(/openEditQuotation,\n\s+openReviseQuotation,/);
    });
    it('the tab shows Editar/Excluir only when the window+role allow, routes delete through the orchestration and refreshes the projection', () => {
        // v2.249.2 — the server window (scalar OR item-level) is passed as the last argument.
        expect(workspaceSrc).toMatch(/canManageWorkspaceQuotations\(ws\?\.requestStatusCode, user\?\.roles, ROLES\.BUYER, ws\?\.buyerId, user\?\.id, ws\?\.quotationWindowOpen\)/);
        expect(workspaceSrc).toMatch(/wizardHost\.openEditQuotation\(ws\.requestId, q\.id\)/);
        expect(workspaceSrc).toMatch(/runDeleteQuotation\(/);
        expect(workspaceSrc).toMatch(/onSuccess: \(msg\) => afterMutation\(msg\)/);           // flash + refreshAfterMutation: coverage, counts, next actions
        // A reload failure after a successful mutation is reported as such (no page swap, no retry prompt) — for delete AND edit.
        expect(workspaceSrc).toMatch(/const afterMutation = \(msg: string\) => \{ setActiveHost\(null\); flash\(msg\); void refreshAfterMutation\(\); \};/);
        expect(workspaceSrc).toMatch(/onSaved: \(\) => refreshAfterMutation\(\)/);
        expect(workspaceSrc).toMatch(/catch \{ setActionError\(REFRESH_AFTER_MUTATION_FAILED\); \}/);
        // Edit opening is guarded against double clicks and against a concurrent delete.
        expect(workspaceSrc).toMatch(/if \(!ws \|\| quotationBusyRef\.current\) return;[\s\S]*?await wizardHost\.openEditQuotation\(ws\.requestId, q\.id\)/);
        expect(workspaceSrc).toMatch(/onError: \(msg\) => setActionError\(msg\)/);
        expect(workspaceSrc).toMatch(/variant="destructive"/);
        expect(workspaceSrc).toMatch(/buildDeleteQuotationConfirmation\(deleteTarget\)/);
        expect(workspaceSrc).toMatch(/disabled=\{busy \|\| !st\.editable\} onClick=\{\(\) => onEdit\(q\)\}/); // §7: per-quotation server lock
        expect(workspaceSrc).toMatch(/disabled=\{busy \|\| !st\.deletable\} onClick=\{\(\) => onDelete\(q\)\}/);
    });
});

// v2.249.2 — the server's quotation window (scalar OR item-level) replaces the scalar mirror when provided.
describe('canManageWorkspaceQuotations — server quotationWindowOpen (v2.249.2)', () => {
    const ME = 'u-samuel';
    it('an open item-level window enables Editar/Excluir even when the scalar is past the P.O. gate', () => {
        expect(canManageWorkspaceQuotations('PO_PARTIALLY_UPLOADED', ['Buyer'], 'Buyer', ME, ME, true)).toBe(true);
        expect(canManageWorkspaceQuotations('PO_ISSUED', ['Buyer'], 'Buyer', ME, ME, true)).toBe(true);
    });
    it('a closed server window disables them even inside the scalar window; undefined keeps the scalar mirror', () => {
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Buyer'], 'Buyer', ME, ME, false)).toBe(false);
        expect(canManageWorkspaceQuotations('WAITING_QUOTATION', ['Buyer'], 'Buyer', ME, ME, undefined)).toBe(true);
        expect(canManageWorkspaceQuotations('PO_ISSUED', ['Buyer'], 'Buyer', ME, ME, null)).toBe(false);
    });
    it('the window never overrides role/ownership', () => {
        expect(canManageWorkspaceQuotations('PO_PARTIALLY_UPLOADED', ['Buyer'], 'Buyer', 'u-other', ME, true)).toBe(false);
        expect(canManageWorkspaceQuotations('PO_PARTIALLY_UPLOADED', ['Finance'], 'Buyer', ME, ME, true)).toBe(false);
    });
});
