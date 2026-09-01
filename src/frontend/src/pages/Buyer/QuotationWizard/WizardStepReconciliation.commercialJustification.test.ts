import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import reconSrc from './WizardStepReconciliation.tsx?raw';
import controllerSrc from './buyerQuotationWizardController.ts?raw';

// Phase 4 (Commercial change justification): the line material-change section must be SOURCE-NEUTRAL
// (an OcrOriginal* baseline exists for manual quotations too) and offer a truthful commercial reason,
// while keeping the OCR reason and the existing validation + persistence untouched.

describe('Phase 4 — source-neutral material-change wording', () => {
    it('1. header no longer says "documento OCR" — it compares against the original quotation', () => {
        expect(reconSrc).toMatch(/Alterações em relação à cotação original/);
        expect(reconSrc).not.toMatch(/Alteração em relação ao documento OCR/);
    });
    it('2. quantity label is source-neutral', () => {
        expect(reconSrc).toMatch(/Qtd\. original:/);
        expect(reconSrc).not.toMatch(/Qtd OCR:/);
    });
    it('3. price label is source-neutral', () => {
        expect(reconSrc).toMatch(/Preço original:/);
        expect(reconSrc).not.toMatch(/Preço OCR:/);
    });
    it('4. discount label is source-neutral', () => {
        expect(reconSrc).toMatch(/Desc\. original:/);
        expect(reconSrc).not.toMatch(/Desc\. OCR:/);
    });
    it('shows original → current, still reading the OcrOriginal* baseline fields (unchanged storage)', () => {
        expect(reconSrc).toMatch(/\{fmtNum\(quoteItem\.ocrOriginalUnitPrice\)\}<\/strong> → <strong>\{fmtNum\(quoteItem\.unitPrice\)\}/);
    });
});

describe('Phase 4 — justification catalog', () => {
    it('5. offers a truthful commercial reason', () => {
        expect(reconSrc).toMatch(/'Preço renegociado com o fornecedor'/);
    });
    it('6. keeps the OCR reason available for real OCR corrections', () => {
        expect(reconSrc).toMatch(/'Erro na extração pelo OCR'/);
    });
    it('7. does NOT auto-select the commercial reason — selection is pure text match on the draft value', () => {
        expect(reconSrc).toMatch(/const selected = !isOther && adjText === label/);
        // No code forces the first suggestion as a default value.
        expect(reconSrc).not.toMatch(/lineAdjustmentJustification:\s*LINE_ADJUSTMENT_SUGGESTIONS/);
        expect(reconSrc).not.toMatch(/LINE_ADJUSTMENT_SUGGESTIONS\[0\]/);
    });
});

describe('Phase 4 — persistence & validation untouched', () => {
    it('8. EDIT mode loads the existing persisted line justification (no silent overwrite/clear)', () => {
        expect(controllerSrc).toMatch(/lineAdjustmentJustification: item\.lineAdjustmentJustification \|\| null/);
    });
    it('9. line-level validation requirement is unchanged (still validateReconciliationJustification)', () => {
        expect(reconSrc).toMatch(/validateReconciliationJustification\(adjText\)/);
        expect(reconSrc).toMatch(/hasMaterialOcrChange\(quoteItem, ivaRates\)/);
    });
});
