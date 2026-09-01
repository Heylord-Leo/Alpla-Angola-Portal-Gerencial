import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import modalSrc from './BatchReworkModal.tsx?raw';

// Phase 4 (Revision provenance + selection safety): a quotation superseded by a revision
// (Quotation.RevisesQuotationId) must stay VISIBLE for audit but be non-selectable ("Versão anterior");
// the revised option is "Revisada"; deterministic preselection uses provenance only, never value/date.

describe('Phase 4 — superseded option is deterministic (provenance only)', () => {
    it('derives superseded/revision sets from revisesQuotationId — never price/date/supplier', () => {
        expect(modalSrc).toMatch(/supersededQuotationIds = new Set\(quotations\.map\(q => q\.revisesQuotationId\)\.filter\(Boolean\)/);
        expect(modalSrc).toMatch(/revisionQuotationIds = new Set\(quotations\.filter\(q => q\.revisesQuotationId\)\.map\(q => q\.id\)\)/);
        // Guard against value/date heuristics sneaking into option classification.
        expect(modalSrc).not.toMatch(/superseded = .*lineTotal|superseded = .*createdAt/);
    });
});

describe('Phase 4 — superseded option is visible but not selectable', () => {
    it('renders "Versão anterior" + an explanation, and keeps the option visible', () => {
        expect(modalSrc).toMatch(/Versão anterior/);
        expect(modalSrc).toMatch(/Esta cotação possui uma revisão mais recente\./);
    });
    it('the checkbox is disabled for a superseded option', () => {
        expect(modalSrc).toMatch(/disabled=\{superseded\}/);
    });
    it('toggling a superseded option is ignored (defense beyond the disabled input)', () => {
        expect(modalSrc).toMatch(/if \(opt\?\.superseded\) return item;/);
    });
    it('the revised option is labelled "Revisada"', () => {
        expect(modalSrc).toMatch(/Revisada/);
    });
});

describe('Phase 4 — deterministic preselection', () => {
    it('drops a superseded default and preselects the sole current revision (unambiguous only)', () => {
        expect(modalSrc).toMatch(/\.filter\(qid => \{ const o = options\.find\(op => op\.quotationItemId === qid\); return !\(o && o\.superseded\); \}\)/);
        expect(modalSrc).toMatch(/const leafRevisions = options\.filter\(o => o\.isRevision && !o\.superseded\);/);
        expect(modalSrc).toMatch(/if \(leafRevisions\.length === 1\) initialChecked\.push\(leafRevisions\[0\]\.quotationItemId\);/);
    });
});
