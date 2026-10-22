import { describe, it, expect, vi } from 'vitest';
import {
    TAKEOVER_JUSTIFICATION_MAX_LENGTH, TERMINAL_REQUEST_STATUSES, buildTakeoverConfirmation, resolveAssignAction, runAssignRequest, validateTakeoverJustification,
} from './workspaceAssignActions';
import tourSrc from '../../features/guided-tour/tours/buyerRequestWorkspaceTour.ts?raw';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import classicSrc from './BuyerItemsList.tsx?raw';
import dialogSrc from '../../components/Buyer/TakeOverRequestDialog.tsx?raw';

const ROLE_NAMES = { buyer: 'Buyer', systemAdministrator: 'System Administrator', localManager: 'Local Manager' };
const me = 'u-me';
const other = 'u-other';
const user = (roles: string[]) => ({ id: me, roles });
const ws = (over: Partial<{ requestTypeCode: string | null; requestStatusCode: string | null; buyerId: string | null }> = {}) =>
    ({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: null, ...over });

describe('resolveAssignAction — mirrors assign-buyer (claim) and take-over (§8) server rules', () => {
    it('unassigned QUOTATION request + Buyer role → CLAIM', () => {
        expect(resolveAssignAction(ws(), user(['Buyer']), ROLE_NAMES).kind).toBe('CLAIM');
    });
    it('assigned to another Buyer + ordinary Buyer role → TAKEOVER (any Buyer, no coordinator privilege needed)', () => {
        const d = resolveAssignAction(ws({ buyerId: other }), user(['Buyer']), ROLE_NAMES);
        expect(d.kind).toBe('TAKEOVER'); expect(d.label).toBe('Assumir pedido de outro comprador');
    });
    it('coordinators WITHOUT the Buyer role get nothing, on unassigned and assigned requests', () => {
        for (const roles of [['System Administrator'], ['Local Manager'], ['System Administrator', 'Local Manager']]) {
            expect(resolveAssignAction(ws(), user(roles), ROLE_NAMES).kind).toBe('NONE');
            expect(resolveAssignAction(ws({ buyerId: other }), user(roles), ROLE_NAMES).kind).toBe('NONE');
        }
    });
    it('coordinators WITH the Buyer role behave like any Buyer', () => {
        expect(resolveAssignAction(ws({ buyerId: other }), user(['Local Manager', 'Buyer']), ROLE_NAMES).kind).toBe('TAKEOVER');
        expect(resolveAssignAction(ws(), user(['System Administrator', 'Buyer']), ROLE_NAMES).kind).toBe('CLAIM');
    });
    it('already mine → nothing', () => {
        expect(resolveAssignAction(ws({ buyerId: me }), user(['Buyer']), ROLE_NAMES).kind).toBe('NONE');
    });
    it('PAYMENT requests never offer request-level assignment', () => {
        expect(resolveAssignAction(ws({ requestTypeCode: 'PAYMENT' }), user(['Buyer']), ROLE_NAMES).reason).toContain('grupo de correção');
        expect(resolveAssignAction(ws({ requestTypeCode: undefined as any }), user(['Buyer']), ROLE_NAMES).kind).toBe('NONE');
    });
    it('terminal statuses (incl. PAYMENT_COMPLETED) hide the action for claim and takeover', () => {
        expect(TERMINAL_REQUEST_STATUSES).toEqual(['CANCELLED', 'REJECTED', 'COMPLETED', 'PAYMENT_COMPLETED']);
        for (const st of TERMINAL_REQUEST_STATUSES) {
            expect(resolveAssignAction(ws({ requestStatusCode: st }), user(['Buyer']), ROLE_NAMES).kind).toBe('NONE');
            expect(resolveAssignAction(ws({ requestStatusCode: st, buyerId: other }), user(['Buyer']), ROLE_NAMES).kind).toBe('NONE');
        }
        expect(resolveAssignAction(ws({ requestStatusCode: 'WAITING_AREA_APPROVAL', buyerId: other }), user(['Buyer']), ROLE_NAMES).kind).toBe('TAKEOVER');
    });
    it('no session → nothing', () => {
        expect(resolveAssignAction(ws(), null, ROLE_NAMES).kind).toBe('NONE');
        expect(resolveAssignAction(ws(), { id: me, roles: null }, ROLE_NAMES).kind).toBe('NONE');
    });
});

describe('takeover confirmation and justification', () => {
    it('names the request and the current Buyer and states the transfer, the audit and the e-mails', () => {
        const c = buildTakeoverConfirmation({ requestNumber: 'REQ-20/08/2026-271', title: 'Peças', buyerName: 'Samuel Mpoco' });
        expect(c.lines[0]).toContain('REQ-20/08/2026-271 — Peças');
        expect(c.lines[1]).toBe('Comprador atual: Samuel Mpoco.');
        expect(c.lines.join(' ')).toContain('COMPRADOR_TRANSFERIDO');
        expect(c.lines.join(' ')).toContain('e-mails ao comprador anterior');
        expect(c.lines.join(' ')).toContain('recusada e nada é alterado');
        expect(buildTakeoverConfirmation({ requestNumber: 'X' }).lines[1]).toBe('Comprador atual: não identificado.');
    });
    it('validates the justification like the server: trimmed, non-empty, ≤ 500', () => {
        expect(validateTakeoverJustification('').ok).toBe(false);
        expect(validateTakeoverJustification('   \n ').ok).toBe(false);
        expect(validateTakeoverJustification('x'.repeat(TAKEOVER_JUSTIFICATION_MAX_LENGTH + 1)).ok).toBe(false);
        expect(validateTakeoverJustification('  ' + 'x'.repeat(TAKEOVER_JUSTIFICATION_MAX_LENGTH) + '  ').ok).toBe(true);
        expect(validateTakeoverJustification(' ok ').ok).toBe(true);
    });
});

describe('runAssignRequest — one submission at a time, server text surfaced, takeover payload', () => {
    const deps = (over: Partial<Parameters<typeof runAssignRequest>[0]> = {}) => {
        let busy = false;
        return {
            assignBuyer: vi.fn(async () => ({})),
            takeOver: vi.fn(async () => ({})),
            isBusy: () => busy,
            setBusy: (b: boolean) => { busy = b; },
            onSuccess: vi.fn(),
            onError: vi.fn(),
            ...over,
        };
    };
    it('CLAIM posts no body and reports success', async () => {
        const d = deps();
        expect(await runAssignRequest(d, 'r1', 'CLAIM')).toBe('ASSIGNED');
        expect(d.assignBuyer).toHaveBeenCalledWith('r1'); expect(d.takeOver).not.toHaveBeenCalled();
        expect(d.onSuccess).toHaveBeenCalledWith('Pedido atribuído a si com sucesso.');
    });
    it('TAKEOVER posts the trimmed justification and the confirmed owner', async () => {
        const d = deps();
        expect(await runAssignRequest(d, 'r1', 'TAKEOVER', { justification: '  motivo  ', expectedBuyerId: other })).toBe('ASSIGNED');
        expect(d.takeOver).toHaveBeenCalledWith('r1', { justification: 'motivo', expectedBuyerId: other });
        expect(d.onSuccess).toHaveBeenCalledWith(expect.stringContaining('serão notificados'));
    });
    it('TAKEOVER without a valid justification or without the confirmed owner is refused client-side and never calls the API', async () => {
        const d = deps();
        expect(await runAssignRequest(d, 'r1', 'TAKEOVER', { justification: '   ', expectedBuyerId: other })).toBe('INVALID');
        expect(await runAssignRequest(d, 'r1', 'TAKEOVER', { justification: 'ok', expectedBuyerId: null })).toBe('INVALID');
        expect(d.takeOver).not.toHaveBeenCalled(); expect(d.onError).toHaveBeenCalledTimes(2);
    });
    it('busy → skipped without a call; server refusal → verbatim error', async () => {
        const busy = deps({ isBusy: () => true });
        expect(await runAssignRequest(busy, 'r1', 'TAKEOVER', { justification: 'ok', expectedBuyerId: other })).toBe('SKIPPED_BUSY');
        expect(busy.takeOver).not.toHaveBeenCalled();
        const refused = deps({ takeOver: vi.fn(async () => { throw new Error('O pedido já está atribuído a Outro, não ao comprador que confirmou.'); }) });
        expect(await runAssignRequest(refused, 'r1', 'TAKEOVER', { justification: 'ok', expectedBuyerId: other })).toBe('FAILED');
        expect(refused.onError).toHaveBeenCalledWith('O pedido já está atribuído a Outro, não ao comprador que confirmou.');
    });
});

describe('wiring (source-level — no DOM runtime in this suite)', () => {
    it('workspace: dialog with justification, expectedBuyerId from the projection, re-read after success, dialog kept open on client-side validation failure', () => {
        expect(workspaceSrc).toMatch(/<TakeOverRequestDialog/);
        expect(workspaceSrc).toMatch(/takeOver: \(id, payload\) => api\.requests\.takeOverRequest\(id, payload\)/);
        expect(workspaceSrc).toMatch(/expectedBuyerId: ws\.buyerId/);
        expect(workspaceSrc).toMatch(/onSuccess: \(msg\) => afterMutation\(msg\)/);
        expect(workspaceSrc).toMatch(/if \(outcome !== 'INVALID'\) setAssignConfirm\(false\)/);
        expect(workspaceSrc).toMatch(/isBusy: \(\) => assignBusyRef\.current \|\| quotationBusyRef\.current/);
        expect(workspaceSrc).toMatch(/onCancel=\{\(\) => setAssignConfirm\(false\)\}/);       // cancel closes without any call
        expect(workspaceSrc).toMatch(/busy=\{assignBusy\}/);                                  // busy state disables confirm
        expect(workspaceSrc).toMatch(/catch \{ setActionError\(REFRESH_AFTER_MUTATION_FAILED\); \}/);   // failed re-read after success stays distinct
    });
    it('classic list: same decision helper, hidden when NONE, takeover through the shared dialog with expectedBuyerId, reload on success', () => {
        expect(classicSrc).toMatch(/resolveAssignAction\(\{ requestTypeCode: group\.requestTypeCode, requestStatusCode: group\.requestStatusCode, buyerId: group\.buyerId \}, currentUser, ASSIGN_ROLE_NAMES\)/);
        expect(classicSrc).toMatch(/if \(assign\.kind === 'NONE'\) return null/);
        expect(classicSrc).toMatch(/setTakeOverTarget\(group\)/);
        expect(classicSrc).toMatch(/expectedBuyerId: group\.buyerId/);
        expect(classicSrc).toMatch(/onSuccess: \(msg\) => \{ setFeedback\(\{ type: 'success', message: msg \}\); loadData\(\); \}/);
        expect(classicSrc).toMatch(/<TakeOverRequestDialog/);
        expect(classicSrc).toMatch(/onCancel=\{\(\) => setTakeOverTarget\(null\)\}/);
        expect(classicSrc).toMatch(/busy=\{isSaving\}/);
    });
    it('dialog: shared confirmation lines, textarea bound to the max length, confirm disabled while invalid or busy', () => {
        expect(dialogSrc).toMatch(/buildTakeoverConfirmation\(request\)/);
        expect(dialogSrc).toMatch(/maxLength=\{TAKEOVER_JUSTIFICATION_MAX_LENGTH\}/);
        expect(dialogSrc).toMatch(/const canConfirm = validation\.ok && !busy/);
        expect(dialogSrc).toMatch(/onConfirm\(justification\.trim\(\)\)/);
    });
    it('guided tour step describes the open takeover, the justification and the notifications', () => {
        expect(tourSrc).toMatch(/\[data-tour="buyer-workspace-assign"\]/);
        expect(tourSrc).toContain('qualquer comprador do seu âmbito, com justificação obrigatória');
        expect(tourSrc).toContain('notificados por e-mail');
    });
});
