import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import chooserSrc from './QuotationEditChooserModal.tsx?raw';

// Phase 4 — the chooser shown when a batch is composed from more than one existing quotation. It is
// read-only except for the selection, renders business-readable data (supplier, document, total,
// item count), and never exposes a raw GUID.

describe('Phase 4 — QuotationEditChooserModal', () => {
    it('renders business-readable entries (supplier, document, total, item count)', () => {
        expect(chooserSrc).toMatch(/q\.supplierNameSnapshot/);
        expect(chooserSrc).toMatch(/q\.documentNumber/);
        expect(chooserSrc).toMatch(/fmtTotal\(q\)/);
        expect(chooserSrc).toMatch(/\(q\.items \|\| \[\]\)\.length/);
    });

    it('selecting an entry opens that quotation (delegates the chosen quotation up)', () => {
        expect(chooserSrc).toMatch(/onClick=\{\(\) => onSelect\(q\)\}/);
    });

    it('never renders a raw quotation GUID (id used only as the React key)', () => {
        // `key={q.id}` is allowed; a visible `{q.id}` text node is not.
        expect(chooserSrc).not.toMatch(/>\s*\{q\.id\}/);
        expect(chooserSrc).not.toMatch(/\{q\.id\}\s*</);
    });

    it('is read-only apart from the selection/open and close actions', () => {
        expect(chooserSrc).not.toMatch(/<input|<textarea|handleSave|api\./);
    });
});
