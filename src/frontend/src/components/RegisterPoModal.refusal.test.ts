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
        // v2.249.8 — the reviewed set (reviewMismatches) carries the extraction-time divergences plus the number divergence of the current input.
        expect(src).toMatch(/describeDivergenceControls\(identification, !!documentBlock, !!reviewMismatches\?\.hasMismatches \|\| !!backendOcrMismatch\)/);
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
        const divergence = register.indexOf("if (reviewMismatches?.hasMismatches && !overrideConfirmed) {");
        expect(refusal).toBeGreaterThan(0);
        expect(refusal).toBeLessThan(divergence);
        // The submit gate still refuses the file (unchanged v2.249.4 rule).
        expect(register).toMatch(/\|\| !!identification\?\.isBlocked/);
    });

    it('CorrectPoModal: the justification policy receives the refused state and the submission guard refuses the document before any upload', () => {
        expect(correct).toMatch(/resolveJustificationPolicy\(requestData, poGroupId, !!reviewMismatches\?\.hasMismatches, !!identification\?\.requiresExplicitConfirmation, documentRefused\)/);
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

// ── v2.249.8 — REQ-03/11/2026-630 finding: the typed number differed from the identified ECF10 2026/9630 ("AAAAAA") and nothing warned ──
import blocks from './po/PoDocumentReviewBlocks.tsx?raw';

describe('P.O. wizards — the typed number vs the document number is evaluated from the current input (v2.249.8)', () => {
    it.each([['RegisterPoModal', register, 'purchaseOrderNumber'], ['CorrectPoModal', correct, 'poNumber.value']])('%s derives the number divergence on every render and merges it into the reviewed set', (_name, src, typed) => {
        const typedRe = typed.replace('.', '\\.');
        expect(src).toMatch(new RegExp(`const numberDivergence = describePoNumberDivergence\\(${typedRe}, identification, detectedDocumentNumber\\);`));
        expect(src).toMatch(/const reviewMismatches = withNumberDivergence\(ocrResult, numberDivergence\);/);
        // The divergence panel renders the reviewed set (so the number line appears with the acknowledgement checkbox) …
        expect(src).toMatch(/\) : reviewMismatches \? \(\s*reviewMismatches\.hasMismatches \? \(/);
        expect(src).toMatch(/\{reviewMismatches\.details\.map\(\(d, i\) =>/);
        // … the number block shows the inline warning, and the verdict block keeps identification separate from the number.
        expect(src).toMatch(/<PoNumberFieldBlock[^]*?divergence=\{numberDivergence\}/);
        expect(src).toMatch(/<PoDocumentVerdictBlock identification=\{identification\}[^]*?numberDivergence=\{numberDivergence\} \/>/);
        // The extraction-time set is no longer what gates the acknowledgement.
        expect(src).not.toMatch(/\(!!ocrResult\?\.hasMismatches && \(!overrideConfirmed/);
        expect(src).not.toMatch(/\(!!ocrResult\?\.hasMismatches && !overrideConfirmed\)/);
    });
    it('RegisterPoModal submits the reviewed set and gates the button, the pre-submit checks and the mandatory comment on it', () => {
        expect(register).toMatch(/hasMismatches: reviewMismatches\?\.hasMismatches \|\| false,/);
        expect(register).toMatch(/mismatchDetails: reviewMismatches\?\.details \? reviewMismatches\.details\.join\('; '\) : '',/);
        expect(register).toMatch(/\|\| \(!!reviewMismatches\?\.hasMismatches && \(!overrideConfirmed \|\| !comment\.trim\(\)\)\)/);
        expect(register).toMatch(/if \(reviewMismatches\?\.hasMismatches && !comment\.trim\(\)\) \{/);
        // The business facts of the extraction (supplier / total) still come from the extraction-time result.
        expect(register).toMatch(/extractedSupplierName: ocrResult\?\.extractedSupplier,/);
    });
    it('the shared blocks render the inline warning with both numbers and the "Usar número extraído" shortcut, and the verdict note', () => {
        expect(blocks).toMatch(/\{divergence\?\.divergent && !decisionPending && \(/);
        expect(blocks).toMatch(/data-testid="po-number-divergence"/);
        expect(blocks).toMatch(/data-testid="po-number-divergence-typed"/);
        expect(blocks).toMatch(/data-testid="po-number-divergence-document"/);
        expect(blocks).toMatch(/data-testid="po-number-divergence-use-extracted"[^]*?onClick=\{onUseSuggestion\}/);
        expect(blocks).toMatch(/Número introduzido diferente do número do documento/);
        expect(blocks).toMatch(/data-testid="po-document-number-divergence"/);
        // The plain suggestion row yields to the warning while divergent.
        expect(blocks).toMatch(/\{field\.suggestion && suggestionDiffers && !suggestionDiffersFromCurrent && !divergence\?\.divergent && \(/);
    });
});
