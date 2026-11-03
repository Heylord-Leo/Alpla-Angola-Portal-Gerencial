import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guards (no RTL) for the v2.249.8 validity-date provenance in the quotation wizard.
import hook from '../../../hooks/useOcrProcessor.ts?raw';
import state from './hooks/useQuotationWizardState.ts?raw';
import docStep from './WizardStepDocumentsOcr.tsx?raw';
import finalReview from './WizardStepFinalReview.tsx?raw';
import saveLogic from './quotationSaveLogic.ts?raw';
import controller from './buyerQuotationWizardController.ts?raw';

describe('Quotation wizard — validity date ("Validade da cotação") provenance and scope (v2.249.8)', () => {
    it('the OCR processor normalises dates through the pure lib and records EXTRACTED provenance only when the extraction returned a value', () => {
        expect(hook).toMatch(/import \{ resolveOcrDateAlias \} from '\.\.\/lib\/ocrDateAlias';/);
        expect(hook).toMatch(/const resolveDateAlias = resolveOcrDateAlias;/);
        expect(hook).toMatch(/dueDate: resolveDateAlias\(getSafeValue<string>\(suggestions\?\.dueDate, ''\)\),/);
        expect(hook).toMatch(/dueDateSource: resolveDateAlias\(getSafeValue<string>\(suggestions\?\.dueDate, ''\)\) \? 'EXTRACTED' : undefined,/);
        // No default, no clock, no Date round-trip for the validity date.
        expect(hook).not.toMatch(/dueDate: new Date\(/);
        expect(hook).not.toMatch(/dueDate: .*toISOString/);
    });
    it('a Buyer edit of the validity date is recorded as MANUAL; clearing it drops the provenance', () => {
        expect(state).toMatch(/if \(field === 'dueDate'\) next\.dueDateSource = value \? 'MANUAL' : undefined;/);
    });
    it('the document step labels the field as the quotation validity and shows its provenance + scope', () => {
        expect(docStep).toMatch(/Validade da Cotação \(Data de Vencimento\)/);
        expect(docStep).toMatch(/data-testid="quotation-due-date-source"/);
        expect(docStep).toMatch(/Lida do documento pela extração — confirme com a validade impressa\./);
        expect(docStep).toMatch(/Introduzida manualmente\./);
        expect(docStep).toMatch(/Informativa: não fica guardada com a cotação\./);
    });
    it('the final review shows the value verbatim (no date conversion) with its provenance and scope', () => {
        expect(finalReview).toMatch(/>Validade da cotação<\/p>/);
        expect(finalReview).toMatch(/data-testid="final-review-due-date"[^>]*>\{draft\.dueDate \|\| 'Não especificada'\}/);
        expect(finalReview).toMatch(/describeQuotationDueDateSource\(draft\.dueDateSource\)/);
        expect(finalReview).toMatch(/não fica guardada com a cotação/);
        expect(finalReview).not.toMatch(/Data Validade/);
    });
    it('confirmed scope: the validity date is NOT part of the save/preview payload and is NOT rehydrated on edit (wizard-only value)', () => {
        expect(saveLogic).not.toMatch(/dueDate/);
        expect(controller).not.toMatch(/dueDate/);
    });
});
