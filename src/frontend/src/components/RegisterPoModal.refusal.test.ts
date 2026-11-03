import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guards for the v2.249.6 refusal presentation of both P.O. wizards (no RTL).
// REQ-21/10/2026-595: a proforma correctly refused as a P.O. still showed the divergence-acceptance checkbox and demanded a
// justification; the wizard also pre-selected "Pós-Pago" from the proforma's "C001 - pagamento imediato s/desconto".
import register from './RegisterPoModal.tsx?raw';
import correct from './CorrectPoModal.tsx?raw';
import submission from '../lib/correctPoSubmission.ts?raw';
import ocrLib from '../lib/ocrPoValidation.ts?raw';

describe('P.O. wizards — a definitively refused document shows divergences as diagnostics only', () => {
    it.each([['RegisterPoModal', register], ['CorrectPoModal', correct]])('%s derives the controls from the shared rule and renders the acceptance checkbox only in OVERRIDE mode', (_name, src) => {
        expect(src).toMatch(/describeDivergenceControls\(identification, !!documentBlock, !!ocrResult\?\.hasMismatches \|\| !!backendOcrMismatch\)/);
        expect(src).toMatch(/divergenceControls\.mode === 'OVERRIDE' \? \(/);
        expect(src).toMatch(/data-testid="divergence-diagnostic-only"/);
        expect(src).toMatch(/Escolha outro ficheiro\./);
        // The refused state swaps the block title/subtitle for the diagnostic wording.
        expect(src).toMatch(/documentRefused \? divergenceControls\.diagnosticTitle : 'Divergência Detectada'/);
        expect(src).toMatch(/documentRefused \? divergenceControls\.diagnosticNote : 'Os dados extraídos do documento PDF não coincidem com o pedido atual:'/);
    });

    it('RegisterPoModal: the backend-mismatch acceptance checkbox is also gated, the comment is optional while refused, and the refusal is decided before any divergence message', () => {
        expect(register).toMatch(/\{divergenceControls\.mode === 'OVERRIDE' && \(\s*<div[^>]*>\s*<input\s*type="checkbox"\s*id="overrideConfirmBackend"/);
        expect(register).toMatch(/Comentário \{\(divergenceControls\.commentRequired \|\| \(!documentRefused && identification\?\.requiresExplicitConfirmation\)\) \? '\(Obrigatório\)' : '\(opcional\)'\}/);
        expect(register).toMatch(/placeholder=\{divergenceControls\.commentRequired \? "Justifique o motivo de ignorar a divergência\.\.\."/);
        // handleConfirm: documentBlock / isBlocked check precedes the "Confirme estar ciente das divergências" check.
        const refusal = register.indexOf("if (documentBlock || identification?.isBlocked) {\n            setFeedback({ type: 'error', message: 'O documento foi identificado como cotação");
        const divergence = register.indexOf("if (ocrResult?.hasMismatches && !overrideConfirmed) {");
        expect(refusal).toBeGreaterThan(0);
        expect(refusal).toBeLessThan(divergence);
        // The submit gate still refuses the file (unchanged v2.249.4 rule).
        expect(register).toMatch(/\|\| !!identification\?\.isBlocked/);
    });

    it('CorrectPoModal: the justification policy receives the refused state and the submission guard refuses the document before any upload', () => {
        expect(correct).toMatch(/resolveJustificationPolicy\(requestData, poGroupId, !!ocrResult\?\.hasMismatches, !!identification\?\.requiresExplicitConfirmation, documentRefused\)/);
        expect(submission).toMatch(/if \(documentRefused\) return \{ required: false, source: 'DOCUMENT_REFUSED' \};/);
        expect(submission).toMatch(/if \(input\.identification\?\.isBlocked\) return CORRECT_PO_MESSAGES\.documentBlocked;/);
        expect(correct).toMatch(/\|\| !!identification\?\.isBlocked/);
    });

    it.each([['RegisterPoModal', register], ['CorrectPoModal', correct]])('%s resets the refusal and stale extraction state when another file is selected', (_name, src) => {
        // runOcrValidation (new file) clears the verdict, the server block, the acceptance and the payment hint; the clear-file handler too.
        expect(src).toMatch(/setIdentification\(null\); setDetectedDocumentNumber\(null\); setDocumentTypeConfirmed\(false\);/);
        expect(src).toMatch(/setDocumentBlock\(null\)/);
        expect(src).toMatch(/setOverrideConfirmed\(false\);/);
        expect(src).toMatch(/setPaymentConditionHint\(paymentProposal\.hint\)/);
    });
});

describe('P.O. wizards — payment condition proposed by the extraction', () => {
    it('both wizards route the OCR classification through resolveOcrPaymentConditionProposal with the verdict, status and raw text', () => {
        for (const src of [register, correct]) {
            expect(src).toMatch(/resolveOcrPaymentConditionProposal\(\s*\{ code: ocrPaymentCondition, status: ocrPaymentConditionStatus, rawText: ocrPaymentConditionRawText, advancePercent: ocrAdvancePercent \},\s*id\.isBlocked\)/);
            expect(src).toMatch(/if \(paymentProposal\.apply && paymentProposal\.code\) \{/);
            // A stale OCR pre-selection from the previous file is cleared when the new proposal is not applied.
            expect(src).toMatch(/if \(paymentConditionSource === 'OCR_DETECTED'\) \{ setPaymentCondition\(''\); setPaymentConditionSource\(''\); \}/);
            expect(src).toMatch(/data-testid="payment-condition-hint"/);
            // The old unconditional pre-selection is gone.
            expect(src).not.toMatch(/if \(ocrPaymentCondition && \['POST_PAID', 'ADVANCE_FULL', 'ADVANCE_PARTIAL'\]\.includes\(ocrPaymentCondition\)\)/);
        }
    });

    it('the header reader exposes the classification status and the raw payment-terms text', () => {
        expect(ocrLib).toMatch(/paymentConditionStatus: suggestions\?\.paymentCondition\?\.status,/);
        expect(ocrLib).toMatch(/paymentConditionRawText: suggestions\?\.paymentConditionRawText\?\.value,/);
    });
});
