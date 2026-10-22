import { describe, it, expect, vi } from 'vitest';
import { buildTakeoverConfirmation, resolveAssignAction, runAssignRequest } from './workspaceAssignActions';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';

const R = { buyer: 'Buyer', systemAdministrator: 'System Administrator', localManager: 'Local Manager' };
const ME = 'u-leonardo', SAMUEL = 'u-samuel';
const q = (over: Partial<{ requestTypeCode: string; requestStatusCode: string; buyerId: string | null }> = {}) =>
    ({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: null, ...over });

describe('resolveAssignAction — mirrors AssignBuyer (self-assignment needs the Buyer role; takeover needs a coordinator)', () => {
    it('ordinary Buyer: claim when unassigned, nothing when assigned to someone else (server 409), nothing when already mine', () => {
        expect(resolveAssignAction(q(), { id: ME, roles: ['Buyer'] }, R).kind).toBe('CLAIM');
        const other = resolveAssignAction(q({ buyerId: SAMUEL }), { id: ME, roles: ['Buyer'] }, R);
        expect(other.kind).toBe('NONE'); expect(other.reason).toContain('coordenadores');
        expect(resolveAssignAction(q({ buyerId: ME }), { id: ME, roles: ['Buyer'] }, R).kind).toBe('NONE');
    });
    it('System Administrator / Local Manager WITH the Buyer role: claim when unassigned, takeover (with confirmation) when assigned to another', () => {
        expect(resolveAssignAction(q(), { id: ME, roles: ['System Administrator', 'Buyer'] }, R).kind).toBe('CLAIM');
        expect(resolveAssignAction(q({ buyerId: SAMUEL }), { id: ME, roles: ['System Administrator', 'Buyer'] }, R)).toMatchObject({ kind: 'TAKEOVER', label: 'Assumir pedido de outro comprador' });
        expect(resolveAssignAction(q({ buyerId: SAMUEL }), { id: ME, roles: ['Local Manager', 'Buyer'] }, R).kind).toBe('TAKEOVER');
    });
    it('System Administrator / Local Manager WITHOUT the Buyer role: nothing (server refuses self-assignment with 403)', () => {
        for (const roles of [['System Administrator'], ['Local Manager'], ['System Administrator', 'Local Manager']]) {
            expect(resolveAssignAction(q(), { id: ME, roles }, R).kind).toBe('NONE');
            expect(resolveAssignAction(q({ buyerId: SAMUEL }), { id: ME, roles }, R).reason).toContain('papel de Comprador');
        }
    });
    it('never for PAYMENT (group-level ownership), terminal statuses or an unauthenticated user', () => {
        expect(resolveAssignAction(q({ requestTypeCode: 'PAYMENT' }), { id: ME, roles: ['Buyer'] }, R).reason).toContain('grupo de correção');
        for (const s of ['CANCELLED', 'REJECTED', 'COMPLETED']) expect(resolveAssignAction(q({ requestStatusCode: s }), { id: ME, roles: ['Buyer'] }, R).kind).toBe('NONE');
        expect(resolveAssignAction(q(), null, R).kind).toBe('NONE');
        expect(resolveAssignAction(q(), { id: ME, roles: null }, R).kind).toBe('NONE');
    });
});

describe('takeover confirmation', () => {
    it('names the request and the current Buyer and states the effects and the audit/notification', () => {
        const c = buildTakeoverConfirmation({ requestNumber: 'REQ-05/10/2026-517', title: 'Peças', buyerName: 'Samuel Mpoco' });
        expect(c.title).toBe('Assumir pedido de outro comprador');
        expect(c.lines[0]).toContain('REQ-05/10/2026-517 — Peças');
        expect(c.lines[1]).toBe('Comprador atual: Samuel Mpoco.');
        expect(c.lines.some(l => l.includes('COMPRADOR_ATRIBUIDO') && l.includes('requisitante'))).toBe(true);
    });
});

describe('runAssignRequest — one submission at a time, server text surfaced, outcome words distinct from refresh problems', () => {
    function deps(impl: () => Promise<unknown>) {
        let busy = false;
        return { d: { assignBuyer: vi.fn(impl), isBusy: () => busy, setBusy: (b: boolean) => { busy = b; }, onSuccess: vi.fn(), onError: vi.fn() }, getBusy: () => busy };
    }
    it('success (claim and takeover wording), busy reset', async () => {
        const { d, getBusy } = deps(async () => undefined);
        expect(await runAssignRequest(d, 'req', 'CLAIM')).toBe('ASSIGNED');
        expect(d.onSuccess).toHaveBeenCalledWith('Pedido atribuído a si com sucesso.');
        expect(await runAssignRequest(d, 'req', 'TAKEOVER')).toBe('ASSIGNED');
        expect(d.onSuccess).toHaveBeenLastCalledWith('Pedido assumido: a responsabilidade passou para si.');
        expect(d.assignBuyer).toHaveBeenCalledWith('req'); expect(getBusy()).toBe(false);
    });
    it('server refusal (409 coordinator rule / 403 role / 404 scope): error surfaced verbatim, no success', async () => {
        const { d, getBusy } = deps(async () => { throw new Error('Este pedido já está atribuído a outro comprador. O reencaminhamento só é permitido por coordenadores.'); });
        expect(await runAssignRequest(d, 'req', 'CLAIM')).toBe('FAILED');
        expect(d.onError).toHaveBeenCalledWith('Este pedido já está atribuído a outro comprador. O reencaminhamento só é permitido por coordenadores.');
        expect(d.onSuccess).not.toHaveBeenCalled(); expect(getBusy()).toBe(false);
    });
    it('a second submission while one is in flight is skipped', async () => {
        let resolve!: () => void;
        const { d } = deps(() => new Promise<void>(r => { resolve = r; }));
        const first = runAssignRequest(d, 'req', 'CLAIM');
        expect(await runAssignRequest(d, 'req', 'CLAIM')).toBe('SKIPPED_BUSY');
        resolve(); await first;
        expect(d.assignBuyer).toHaveBeenCalledTimes(1);
    });
});

describe('workspace wiring (source-level — no DOM runtime in this suite)', () => {
    it('derives the action from the server-mirroring rule, confirms takeovers, and refreshes through refreshAfterMutation', () => {
        expect(workspaceSrc).toMatch(/resolveAssignAction\(\{ requestTypeCode: ws\?\.requestTypeCode, requestStatusCode: ws\?\.requestStatusCode, buyerId: ws\?\.buyerId \}, user, ASSIGN_ROLE_NAMES\)/);
        expect(workspaceSrc).toMatch(/assignAction\.kind === 'TAKEOVER' \? setAssignConfirm\(true\) : void performAssign\('CLAIM'\)/);
        expect(workspaceSrc).toMatch(/buildTakeoverConfirmation\(ws\)/);
        expect(workspaceSrc).toMatch(/runAssignRequest\(/);
        expect(workspaceSrc).toMatch(/onSuccess: \(msg\) => afterMutation\(msg\)/);
        expect(workspaceSrc).toMatch(/onError: \(msg\) => setActionError\(msg\)/);
        expect(workspaceSrc).toMatch(/disabled=\{assignBusy \|\| quotationBusy\}/);
    });
});
