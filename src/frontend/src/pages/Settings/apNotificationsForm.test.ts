import { describe, expect, it } from 'vitest';
import { defaultApForm, formFromConfig, toApConfigPayload, validateApForm, type ApConfig } from './apNotificationsForm';

const existing: ApConfig = {
    id: 7, companyId: 1, companyName: 'AlplaPLASTICO', email: 'alpla-plasticos-accounts@alpla.com',
    ccEmails: 'aovia-treasury@alpla.com', label: null, isActive: true,
    notifyOnScheduled: true, notifyOnCompleted: true, notifyOnPoRegistered: false, notifyFinanceUsersByEmail: false,
    createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z'
};

describe('Accounts Payable e-mail form (Master Data)', () => {
    it('new configuration defaults: scheduling/completion ON, P.O.-registered and Finance e-mail OFF', () => {
        const f = defaultApForm();
        expect(f.notifyOnScheduled).toBe(true);
        expect(f.notifyOnCompleted).toBe(true);
        expect(f.notifyOnPoRegistered).toBe(false);
        expect(f.notifyFinanceUsersByEmail).toBe(false);
    });

    it('displays an existing configuration, preserving To/CC and the existing toggles, with the new switches as stored', () => {
        const f = formFromConfig({ ...existing, notifyOnPoRegistered: true });
        expect(f.email).toBe('alpla-plasticos-accounts@alpla.com');
        expect(f.ccEmails).toBe('aovia-treasury@alpla.com');
        expect(f.notifyOnScheduled).toBe(true);
        expect(f.notifyOnCompleted).toBe(true);
        expect(f.notifyOnPoRegistered).toBe(true);
        expect(f.notifyFinanceUsersByEmail).toBe(false);
    });

    it('treats payloads from an older API (flags missing) as OFF, never as ON', () => {
        const legacy = { ...existing } as Partial<ApConfig> as ApConfig;
        delete (legacy as Partial<ApConfig>).notifyOnPoRegistered;
        delete (legacy as Partial<ApConfig>).notifyFinanceUsersByEmail;
        const f = formFromConfig(legacy);
        expect(f.notifyOnPoRegistered).toBe(false);
        expect(f.notifyFinanceUsersByEmail).toBe(false);
    });

    it('edits the two switches independently and sends them on update without changing the other fields', () => {
        const f = { ...formFromConfig(existing), notifyFinanceUsersByEmail: true };
        const p = toApConfigPayload(f, 'update');
        expect(p).toEqual({
            email: 'alpla-plasticos-accounts@alpla.com', ccEmails: 'aovia-treasury@alpla.com', label: null,
            notifyOnScheduled: true, notifyOnCompleted: true, notifyOnPoRegistered: false, notifyFinanceUsersByEmail: true
        });
        expect('companyId' in p).toBe(false);

        const p2 = toApConfigPayload({ ...f, notifyOnPoRegistered: true, notifyFinanceUsersByEmail: false }, 'update');
        expect(p2.notifyOnPoRegistered).toBe(true);
        expect(p2.notifyFinanceUsersByEmail).toBe(false);
    });

    it('create sends the company and explicit false for both new switches by default', () => {
        const p = toApConfigPayload({ ...defaultApForm(), companyId: 2, email: ' ap@alpla.com ' }, 'create');
        expect(p.companyId).toBe(2);
        expect(p.email).toBe('ap@alpla.com');
        expect(p.notifyOnPoRegistered).toBe(false);
        expect(p.notifyFinanceUsersByEmail).toBe(false);
    });

    it('validation is unchanged by the new switches', () => {
        expect(validateApForm({ ...defaultApForm(), companyId: 1, email: 'ap@alpla.com', notifyOnPoRegistered: true }, 'create')).toBeNull();
        expect(validateApForm({ ...defaultApForm(), companyId: 0, email: 'ap@alpla.com' }, 'create')).toBe('Selecione uma empresa.');
        expect(validateApForm({ ...defaultApForm(), email: 'nope' }, 'update')).toBe('O e-mail principal não tem um formato válido.');
        expect(validateApForm({ ...defaultApForm(), email: 'ap@alpla.com', ccEmails: 'a@x;b' }, 'update')).toBe("O endereço CC 'b' não é um e-mail válido.");
    });
});
