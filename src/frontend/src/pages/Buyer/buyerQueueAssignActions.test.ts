import { describe, it, expect, vi } from 'vitest';
import { buildRequestRowKebab, resolveQueueAssignAction } from './buyerQueueAssignActions';

const ROLE_NAMES = { buyer: 'Buyer', systemAdministrator: 'System Administrator', localManager: 'Local Manager' };
const base = [{ label: 'Ver detalhes', onClick: () => {} }, { label: 'Adicionar observação', onClick: () => {} }];
const me = { id: 'u-me', roles: ['Buyer'] };

describe('queue kebab — same eligibility policy as the Workspace, appended after the standard options', () => {
    it('TAKEOVER for a QUOTATION request owned by another Buyer; the option calls the takeover handler only', () => {
        const d = resolveQueueAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: 'u-other' }, me, ROLE_NAMES);
        const onClaim = vi.fn(); const onTakeOver = vi.fn();
        const kebab = buildRequestRowKebab(base, d, { onClaim, onTakeOver });
        expect(kebab.map(o => o.label)).toEqual(['Ver detalhes', 'Adicionar observação', 'Assumir pedido de outro comprador']);
        kebab[2].onClick();
        expect(onTakeOver).toHaveBeenCalledTimes(1); expect(onClaim).not.toHaveBeenCalled();
    });
    it('CLAIM for an unassigned request uses the existing claim handler', () => {
        const d = resolveQueueAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: null }, me, ROLE_NAMES);
        const onClaim = vi.fn(); const onTakeOver = vi.fn();
        const kebab = buildRequestRowKebab(base, d, { onClaim, onTakeOver });
        expect(kebab[2].label).toBe('Assumir pedido');
        kebab[2].onClick();
        expect(onClaim).toHaveBeenCalledTimes(1); expect(onTakeOver).not.toHaveBeenCalled();
    });
    it('nothing is added for the current owner, PAYMENT rows, terminal statuses or non-Buyers', () => {
        const cases = [
            resolveQueueAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: 'u-me' }, me, ROLE_NAMES),
            resolveQueueAssignAction({ requestTypeCode: 'PAYMENT', requestStatusCode: 'WAITING_PO_CORRECTION', buyerId: 'u-other' }, me, ROLE_NAMES),
            resolveQueueAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'CANCELLED', buyerId: 'u-other' }, me, ROLE_NAMES),
            resolveQueueAssignAction({ requestTypeCode: 'QUOTATION', requestStatusCode: 'WAITING_QUOTATION', buyerId: 'u-other' }, { id: 'u-me', roles: ['Requester'] }, ROLE_NAMES),
        ];
        for (const d of cases) {
            expect(d.kind).toBe('NONE');
            expect(buildRequestRowKebab(base, d, { onClaim: () => {}, onTakeOver: () => {} })).toBe(base);
        }
    });
});
