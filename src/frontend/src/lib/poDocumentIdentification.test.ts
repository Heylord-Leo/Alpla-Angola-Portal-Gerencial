import { describe, it, expect } from 'vitest';
import {
    NO_EXTRACTION_IDENTIFICATION, readPoDocumentIdentification, readDetectedDocumentNumber,
    initialPoNumberField, applyExtractedNumber, userEditsPoNumber, useSuggestedPoNumber, clearExtractedNumber,
    poNumberSourceLabel, poNumberChanged, describePoDocumentReview, numberDecisionPending, keepCurrentPoNumber, type PoDocumentIdentification,
} from './poDocumentIdentification';

// v2.249.4 — the shared P.O. document identification (both wizards) and the number field state machine.

function serverVerdict(over: Partial<any> = {}) {
    return { integration: { headerSuggestions: {
        documentNumber: { value: 'ECF11 2026/421' }, currencyCode: { value: 'AOA' },
        documentTitle: { value: 'PO Serviços ECF11 2026/421' },
        referencedDocumentNumbers: ['ECF10 2026/364'],
        documentClassification: { suggestedType: 'OTHER', confidence: 0.4, titleFound: 'PO Serviços ECF11 2026/421' },
        purchaseOrderIdentification: {
            verdict: 'IDENTIFIED_PO', reasonCode: 'PO_IDENTIFIED', reason: 'Ordem de Compra identificada: ECF11 2026/421 (ECF11 — Serviços).',
            family: 'ECF11', familyLabel: 'Serviços', reference: 'ECF11 2026/421', canonical: 'ECF11-2026-421',
            identityCandidates: [{ reference: 'ECF11 2026/421', canonical: 'ECF11-2026-421', family: 'ECF11', familyLabel: 'Serviços', source: 'DOCUMENT_NUMBER' }],
            bodyReferences: [{ reference: 'ECF10 2026/364', canonical: 'ECF10-2026-364', family: 'ECF10', familyLabel: 'Material diverso / escritório', source: 'BODY_REFERENCE' }],
            ...over,
        },
    } } };
}

describe('readPoDocumentIdentification — the server verdict is the source of truth', () => {
    it('reads IDENTIFIED_PO with family, label, number and body references', () => {
        const id = readPoDocumentIdentification(serverVerdict());
        expect(id.verdict).toBe('IDENTIFIED_PO');
        expect(id.family).toBe('ECF11'); expect(id.familyLabel).toBe('Serviços'); expect(id.reference).toBe('ECF11 2026/421');
        expect(id.bodyReferences.map(b => b.reference)).toEqual(['ECF10 2026/364']);
        expect(id.isBlocked).toBe(false); expect(id.requiresExplicitConfirmation).toBe(false);
    });
    it('IDENTIFIED_OTHER_TYPE is blocked; UNCERTAIN requires the explicit confirmation', () => {
        expect(readPoDocumentIdentification(serverVerdict({ verdict: 'IDENTIFIED_OTHER_TYPE', otherTypeLabel: 'Cotação / Factura pró-forma' })).isBlocked).toBe(true);
        const u = readPoDocumentIdentification(serverVerdict({ verdict: 'UNCERTAIN', reasonCode: 'UNRECOGNISED_IDENTIFIER', reference: null }));
        expect(u.requiresExplicitConfirmation).toBe(true); expect(u.isBlocked).toBe(false);
    });
    it('an unknown verdict string degrades to UNCERTAIN, never to a success', () => {
        expect(readPoDocumentIdentification(serverVerdict({ verdict: 'SOMETHING_NEW' })).verdict).toBe('UNCERTAIN');
    });
    it('without a server verdict: a parsed ECF reference is a P.O., anything else is UNCERTAIN (never "validated")', () => {
        expect(readPoDocumentIdentification({ integration: { headerSuggestions: { documentNumber: { value: 'ECF10 2026/219' } } } }).verdict).toBe('IDENTIFIED_PO');
        const u = readPoDocumentIdentification({ integration: { headerSuggestions: { documentNumber: { value: 'ECF99 2026/9003' } } } });
        expect(u.verdict).toBe('UNCERTAIN'); expect(u.reasonCode).toBe('NO_IDENTIFIER');
        expect(readPoDocumentIdentification(null).verdict).toBe('UNCERTAIN');
    });
});

describe('readDetectedDocumentNumber — the raw number for review; no extraction facts are ever sent to the server', () => {
    it('returns the raw document number, or null', () => {
        expect(readDetectedDocumentNumber(serverVerdict())).toBe('ECF11 2026/421');
        expect(readDetectedDocumentNumber({ integration: { headerSuggestions: { documentNumber: { value: ' ECF99 2026/9003 ' } } } })).toBe('ECF99 2026/9003');
        expect(readDetectedDocumentNumber({})).toBeNull();
    });
});

describe('P.O. number field — extraction fills an empty field, never a manual or current value', () => {
    const identified: PoDocumentIdentification = readPoDocumentIdentification(serverVerdict());

    it('registration: empty → EXTRACTED; typing → MANUAL; a new file clears an extracted value but keeps a manual one', () => {
        let f = initialPoNumberField(null);
        expect(f.source).toBe('EMPTY');
        f = applyExtractedNumber(f, identified, 'ECF11 2026/421', []);
        expect(f).toMatchObject({ value: 'ECF11 2026/421', source: 'EXTRACTED', suggestion: 'ECF11 2026/421', suggestionFamilyLabel: 'Serviços' });
        f = userEditsPoNumber(f, 'ECF11 2026/422');
        expect(f.source).toBe('MANUAL');
        f = applyExtractedNumber(f, identified, 'ECF11 2026/421', []);
        expect(f.value).toBe('ECF11 2026/422');                       // never overwritten
        expect(f.suggestion).toBe('ECF11 2026/421');                  // offered beside the field
        f = useSuggestedPoNumber(f);
        expect(f).toMatchObject({ value: 'ECF11 2026/421', source: 'EXTRACTED' });
        f = clearExtractedNumber(f);
        expect(f).toMatchObject({ value: '', source: 'EMPTY', suggestion: null });
        const manual = clearExtractedNumber(userEditsPoNumber(initialPoNumberField(null), 'ECF10 2026/7'));
        expect(manual).toMatchObject({ value: 'ECF10 2026/7', source: 'MANUAL' });
    });

    it('correction: starts as CURRENT; extraction never replaces it silently, only suggests; typing the current number back is CURRENT again', () => {
        const current = 'TEST-MAQSIS-2026-9003';
        let f = initialPoNumberField(current);
        expect(f).toMatchObject({ value: current, source: 'CURRENT' });
        f = applyExtractedNumber(f, identified, 'ECF11 2026/421', []);
        expect(f.value).toBe(current); expect(f.source).toBe('CURRENT'); expect(f.suggestion).toBe('ECF11 2026/421');
        expect(poNumberChanged(current, f.value)).toBe(false);
        f = useSuggestedPoNumber(f);
        expect(f.source).toBe('EXTRACTED'); expect(poNumberChanged(current, f.value)).toBe(true);
        f = userEditsPoNumber(f, ' test-maqsis-2026-9003 ', current);
        expect(f.source).toBe('MANUAL');
        f = userEditsPoNumber(f, current, current);
        expect(f.source).toBe('CURRENT');
        expect(clearExtractedNumber(useSuggestedPoNumber(applyExtractedNumber(initialPoNumberField(current), identified, null, [])), current)).toMatchObject({ value: current, source: 'CURRENT' });
    });

    it('a NIF-shaped document number never fills the field', () => {
        const f = applyExtractedNumber(initialPoNumberField(null), readPoDocumentIdentification({ integration: { headerSuggestions: { documentNumber: { value: '5001713205' } } } }), '5001713205', ['5001713205']);
        expect(f.value).toBe(''); expect(f.suggestion).toBeNull();
    });

    it('labels', () => {
        expect(poNumberSourceLabel('EXTRACTED')).toBe('Extraído do documento');
        expect(poNumberSourceLabel('MANUAL')).toBe('Introduzido manualmente');
        expect(poNumberSourceLabel('CURRENT')).toBe('Número atual (sem alteração)');
    });
});

describe('describePoDocumentReview — never an unconditional "Documento Validado"', () => {
    const identified = readPoDocumentIdentification(serverVerdict());
    it('success only for an identified P.O. without business mismatches', () => {
        expect(describePoDocumentReview(identified, false).tone).toBe('success');
        const withMismatch = describePoDocumentReview(identified, true);
        expect(withMismatch.tone).toBe('warning'); expect(withMismatch.needsTypeConfirmation).toBe(false);
    });
    it('UNCERTAIN (synthetic ECF99 / no extraction) is a warning that needs the explicit type confirmation', () => {
        const r = describePoDocumentReview(NO_EXTRACTION_IDENTIFICATION, false);
        expect(r.tone).toBe('warning'); expect(r.needsTypeConfirmation).toBe(true);
        expect(describePoDocumentReview(null, false).needsTypeConfirmation).toBe(true);
    });
    it('a quotation / proforma / invoice is blocked', () => {
        const r = describePoDocumentReview(readPoDocumentIdentification(serverVerdict({ verdict: 'IDENTIFIED_OTHER_TYPE', otherTypeLabel: 'Cotação / Factura pró-forma', reason: 'não pode ser registado como P.O.' })), false);
        expect(r.tone).toBe('blocked'); expect(r.title).toContain('Cotação / Factura pró-forma');
    });
});

describe('correction number decision (REQ-628: current TEST-MAQSIS-2026-9003, replacement PDF reads ECF99 2026/9003)', () => {
    const current = 'TEST-MAQSIS-2026-9003';
    const uncertainEcf99 = readPoDocumentIdentification({ integration: { headerSuggestions: { documentNumber: { value: 'ECF99 2026/9003' }, purchaseOrderIdentification: { verdict: 'UNCERTAIN', reasonCode: 'UNRECOGNISED_IDENTIFIER', reason: 'série não reconhecida', identityCandidates: [], bodyReferences: [] } } } });
    it('the detected number is offered even though the ECF99 series is not recognised, and the decision is pending while the field still holds the current number', () => {
        const f = applyExtractedNumber(initialPoNumberField(current), uncertainEcf99, 'ECF99 2026/9003', []);
        expect(f.value).toBe(current); expect(f.source).toBe('CURRENT');
        expect(f.suggestion).toBe('ECF99 2026/9003'); expect(f.suggestionFamilyLabel).toBeNull();   // visible for review, unrecognised series
        expect(numberDecisionPending(f, current)).toBe(true);
        expect(uncertainEcf99.requiresExplicitConfirmation).toBe(true);                             // the type still needs the explicit confirmation
    });
    it('resolved by using the detected number, by typing another number, or by explicitly keeping the current one', () => {
        const f = applyExtractedNumber(initialPoNumberField(current), uncertainEcf99, 'ECF99 2026/9003', []);
        expect(numberDecisionPending(useSuggestedPoNumber(f), current)).toBe(false);
        expect(useSuggestedPoNumber(f)).toMatchObject({ value: 'ECF99 2026/9003', source: 'EXTRACTED' });
        expect(numberDecisionPending(userEditsPoNumber(f, 'ECF10 2026/9003', current), current)).toBe(false);
        const kept = keepCurrentPoNumber(f, current);
        expect(kept).toMatchObject({ value: current, source: 'CURRENT', currentConfirmed: true });
        expect(numberDecisionPending(kept, current)).toBe(false);
    });
    it('typing the current number back is not a decision; a new file reopens it; no decision when the document reads the same number or nothing', () => {
        const f = applyExtractedNumber(initialPoNumberField(current), uncertainEcf99, 'ECF99 2026/9003', []);
        expect(numberDecisionPending(userEditsPoNumber(useSuggestedPoNumber(f), current, current), current)).toBe(true);
        expect(numberDecisionPending(clearExtractedNumber(keepCurrentPoNumber(f, current), current), current)).toBe(false);   // no suggestion any more
        expect(numberDecisionPending(applyExtractedNumber(clearExtractedNumber(keepCurrentPoNumber(f, current), current), uncertainEcf99, 'ECF99 2026/9003', []), current)).toBe(true); // reopened
        expect(numberDecisionPending(applyExtractedNumber(initialPoNumberField(current), null, current, []), current)).toBe(false);
        expect(numberDecisionPending(applyExtractedNumber(initialPoNumberField(current), null, null, []), current)).toBe(false);
        expect(numberDecisionPending(applyExtractedNumber(initialPoNumberField(null), uncertainEcf99, 'ECF99 2026/9003', []), null)).toBe(false);   // registration: no current number
    });
});
