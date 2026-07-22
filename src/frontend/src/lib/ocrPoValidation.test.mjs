import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import {
    GENERIC_OCR_FAILURE_MESSAGE,
    CLIENT_PROCESSING_ERROR_MESSAGE,
    NOT_DEFINED_SUPPLIER_DISPLAY,
    calculateSimilarity,
    resolveExpectedSupplierName,
    resolveSupplierDisplay,
    resolveExpectedTotalAmount,
    extractOcrHeaderSuggestions,
    buildOcrMismatchResult,
    resolveTransportErrorDetails,
} from './ocrPoValidation.ts';

/**
 * Exact HTTP 200 fixture from the confirmed failed PO OCR execution (Primavera/MUSOLAND PO,
 * REQ Servicos Ministerio 2026/0041). Matches OcrExtractionResultDto's real JSON shape:
 * integration.headerSuggestions.<field>.value, with unset optional fields serialized as null.
 */
const CONFIRMED_HTTP_200_FIXTURE = {
    success: true,
    status: { code: 'ok', qualityScore: 0.95 },
    requiredFieldsMissing: [],
    integration: {
        headerSuggestions: {
            supplierName: { value: 'ALPLA ANGOLA SOPRO - IND. E COM. GERAL (SU), LDA', status: 'recommended' },
            billedCompany: { value: 'MUSOLAND - Mundo de Soluções em Luanda', status: 'recommended' },
            documentNumber: { value: 'PO Serviços Ministério 2026/0041', status: 'recommended' },
            documentDate: null,
            dueDate: null,
            currency: { value: 'AKZ', status: 'recommended' },
            grandTotal: { value: 855000.00, status: 'recommended' },
            discountAmount: { value: 0, status: 'recommended' },
            vendorAddress: null,
            vendorContactName: null,
            vendorContactEmail: null,
            vendorContactPhone: null,
            vendorIban: null,
            vendorBankAccount: null,
            vendorSwift: null,
            vendorPaymentTerms: null,
            paymentCondition: { value: 'ADVANCE_FULL', status: 'recommended' },
            paymentConditionRawText: {
                value: '100% antecipado antes da execução/libertação final do serviço.',
                status: 'informational',
            },
            paymentConditionAdvancePercent: null,
        },
        lineItemSuggestions: [
            { description: 'Serviço de Manutenção', quantity: 1, unit: 'UN', unitPrice: 855000, discountAmount: 0, discountPercent: 0, totalPrice: 855000, taxRate: null, status: 'suggested' },
        ],
        contractSuggestions: null,
        lineItemsRequireReview: true,
        reviewRequired: true,
        recommendedAutofillFields: null,
    },
    metadata: { provider: 'OPENAI', promptTokens: 0, completionTokens: 0, totalTokens: 0, pagesProcessed: 0, chunkCount: 0 },
};

describe('resolveExpectedSupplierName — now a comparison value (string | null), never a placeholder', () => {
    test('null does not crash and resolves to null (not a placeholder string)', () => {
        assert.doesNotThrow(() => resolveExpectedSupplierName(null));
        assert.equal(resolveExpectedSupplierName(null), null);
    });

    test('undefined does not crash and resolves to null', () => {
        assert.doesNotThrow(() => resolveExpectedSupplierName(undefined));
        assert.equal(resolveExpectedSupplierName(undefined), null);
    });

    test('empty string resolves to null', () => {
        assert.equal(resolveExpectedSupplierName(''), null);
    });

    test('whitespace-only string resolves to null', () => {
        assert.equal(resolveExpectedSupplierName('   '), null);
    });

    test('a real value is trimmed and preserved', () => {
        assert.equal(resolveExpectedSupplierName('  MUSOLAND  '), 'MUSOLAND');
    });
});

describe('resolveSupplierDisplay — UI-only text, separate from the comparison value', () => {
    test('null comparison value displays as "Não definido"', () => {
        assert.equal(resolveSupplierDisplay(null), 'Não definido');
        assert.equal(resolveSupplierDisplay(null), NOT_DEFINED_SUPPLIER_DISPLAY);
    });

    test('a real comparison value is displayed as-is', () => {
        assert.equal(resolveSupplierDisplay('MUSOLAND'), 'MUSOLAND');
    });
});

describe('resolveExpectedTotalAmount', () => {
    test('null/undefined/non-number values fall back to 0 without throwing', () => {
        assert.equal(resolveExpectedTotalAmount(null), 0);
        assert.equal(resolveExpectedTotalAmount(undefined), 0);
        assert.equal(resolveExpectedTotalAmount('855000'), 0); // string is not `number` typeof — intentional, matches typeof-guard semantics
    });

    test('a real number is preserved', () => {
        assert.equal(resolveExpectedTotalAmount(855000), 855000);
    });
});

describe('extractOcrHeaderSuggestions — confirmed HTTP 200 fixture', () => {
    test('does not throw on the exact confirmed payload', () => {
        assert.doesNotThrow(() => extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE));
    });

    test('total 855000 is processed', () => {
        const { extractedTotal } = extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE);
        assert.equal(extractedTotal, 855000);
    });

    test('PO number is obtained from documentNumber (no purchaseOrderNumber field exists)', () => {
        const { extractedPoNumber } = extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE);
        assert.equal(extractedPoNumber, 'PO Serviços Ministério 2026/0041');
    });

    test('paymentCondition = ADVANCE_FULL is recognized', () => {
        const { paymentCondition } = extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE);
        assert.equal(paymentCondition, 'ADVANCE_FULL');
    });

    test('null paymentConditionAdvancePercent is accepted (undefined, not a crash)', () => {
        const { advancePercent } = extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE);
        assert.equal(advancePercent, undefined);
    });

    test('all-null optional fields (dueDate, vendorContactName/Email, vendorIban, vendorBankAccount, vendorSwift, recommendedAutofillFields) do not cause a crash', () => {
        // These fields are not read by extractOcrHeaderSuggestions at all — asserting the whole
        // call still succeeds end-to-end is the meaningful regression guard here.
        assert.doesNotThrow(() => extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE));
    });
});

describe('calculateSimilarity — nullable, never a numeric score when not comparable', () => {
    test("calculateSimilarity('MUSOLAND', null) is null", () => {
        assert.equal(calculateSimilarity('MUSOLAND', null), null);
    });

    test("calculateSimilarity('MUSOLAND', '') is null", () => {
        assert.equal(calculateSimilarity('MUSOLAND', ''), null);
    });

    test("calculateSimilarity('', 'MUSOLAND') is null", () => {
        assert.equal(calculateSimilarity('', 'MUSOLAND'), null);
    });

    test('undefined on either side is null', () => {
        assert.equal(calculateSimilarity(undefined, 'MUSOLAND'), null);
        assert.equal(calculateSimilarity('MUSOLAND', undefined), null);
    });

    test('whitespace-only on either side is null (trims to empty)', () => {
        assert.equal(calculateSimilarity('   ', 'MUSOLAND'), null);
        assert.equal(calculateSimilarity('MUSOLAND', '   '), null);
    });

    test('both values empty/blank is null, not a match', () => {
        assert.equal(calculateSimilarity('', ''), null);
        assert.equal(calculateSimilarity(null, null), null);
        assert.equal(calculateSimilarity(undefined, undefined), null);
    });

    test('two placeholder-like strings with no alphanumeric content (e.g. the old \'---\' placeholder) never match, even though both are non-blank raw strings', () => {
        // Regression guard: calculateSimilarity must not rely on `.includes('')`. Passing the old
        // placeholder text explicitly (as if a caller still used it) must not produce a match.
        assert.equal(calculateSimilarity('---', '---'), null);
        assert.equal(calculateSimilarity('MUSOLAND', '---'), null);
        assert.equal(calculateSimilarity('---', 'MUSOLAND'), null);
    });

    test('valid identical supplier names score 1.0 (case/accent-insensitive)', () => {
        assert.equal(calculateSimilarity('MUSOLAND', 'MUSOLAND'), 1.0);
        assert.equal(calculateSimilarity('MUSOLAND', 'musoland'), 1.0);
        assert.equal(calculateSimilarity('Solucoes', 'Soluções'), 1.0);
    });

    test('valid slightly different supplier names score high but not necessarily 1.0', () => {
        const sim = calculateSimilarity('MUSOLAND LDA', 'MUSOLAND, LDA.');
        assert.ok(sim !== null && sim >= 0.9, `expected a high similarity score, got: ${sim}`);
    });

    test('ALPLA vs MUSOLAND scores low (genuinely different suppliers)', () => {
        const sim = calculateSimilarity('ALPLA ANGOLA SOPRO - IND. E COM. GERAL (SU), LDA', 'MUSOLAND - Mundo de Soluções em Luanda');
        assert.ok(sim !== null && sim < 0.6, `expected a low similarity score, got: ${sim}`);
    });
});

describe('buildOcrMismatchResult — confirmed HTTP 200 fixture', () => {
    const { extractedTotal, extractedSupplier } = extractOcrHeaderSuggestions(CONFIRMED_HTTP_200_FIXTURE);

    test('supplier mismatch is shown explicitly (extracted ALPLA vs expected MUSOLAND) — divergence, not a crash', () => {
        const expectedSupplierName = resolveExpectedSupplierName('MUSOLAND - Mundo de Soluções em Luanda');
        const expectedTotalAmount = resolveExpectedTotalAmount(855000);

        const result = buildOcrMismatchResult(extractedTotal, expectedTotalAmount, extractedSupplier, expectedSupplierName);

        assert.equal(result.hasMismatches, true);
        assert.ok(result.details.some(d => d.includes('ALPLA ANGOLA SOPRO') && d.includes('MUSOLAND')),
            `expected a supplier-divergence detail mentioning both names, got: ${JSON.stringify(result.details)}`);
    });

    test('the generic unreadable-document message is never produced by mismatch processing', () => {
        const expectedSupplierName = resolveExpectedSupplierName('MUSOLAND - Mundo de Soluções em Luanda');
        const expectedTotalAmount = resolveExpectedTotalAmount(855000);
        const result = buildOcrMismatchResult(extractedTotal, expectedTotalAmount, extractedSupplier, expectedSupplierName);

        assert.ok(!result.details.some(d => d === GENERIC_OCR_FAILURE_MESSAGE));
    });

    test('missing expected supplier (null) does not throw, and does NOT report a match', () => {
        // This is the regression test for BOTH confirmed defects:
        // 1. requestData.supplierName null/undefined must never crash (original bug).
        // 2. A missing expected supplier must never be silently treated as a match (follow-up fix —
        //    previously '---' scored 0.9 via the `includes('')` pitfall).
        const expectedSupplierName = resolveExpectedSupplierName(null); // null, not '---'
        assert.doesNotThrow(() => buildOcrMismatchResult(extractedTotal, 855000, extractedSupplier, expectedSupplierName));

        const result = buildOcrMismatchResult(extractedTotal, 855000, extractedSupplier, expectedSupplierName);
        assert.equal(result.hasMismatches, true, 'a missing expected supplier must be flagged, never treated as validated');
    });

    test('missing expected supplier produces the specific "not comparable" message, showing both the extracted value and "Não definido"', () => {
        const expectedSupplierName = resolveExpectedSupplierName(undefined);
        const result = buildOcrMismatchResult(extractedTotal, 855000, extractedSupplier, expectedSupplierName);

        assert.ok(result.details.some(d => d === 'Não foi possível comparar o fornecedor extraído porque o grupo não possui fornecedor esperado definido.'),
            `expected the not-comparable message, got: ${JSON.stringify(result.details)}`);
        assert.ok(result.details.some(d => d.includes(extractedSupplier)),
            `expected the extracted supplier value to be shown, got: ${JSON.stringify(result.details)}`);
        assert.ok(result.details.some(d => d.includes('Não definido')),
            `expected "Não definido" to be shown as the expected supplier, got: ${JSON.stringify(result.details)}`);
    });

    test('empty extracted supplier (OCR could not identify one) is reported distinctly from a missing expected supplier', () => {
        const result = buildOcrMismatchResult(0, 0, '', resolveExpectedSupplierName('MUSOLAND'));
        assert.equal(result.hasMismatches, true);
        assert.ok(result.details.some(d => d === 'Fornecedor não identificado claramente no documento.'));
    });
});

describe('resolveTransportErrorDetails', () => {
    test('uses err.detail when present', () => {
        const details = resolveTransportErrorDetails({ detail: 'Backend rejected the request', status: 400 });
        assert.equal(details[0], 'Backend rejected the request');
    });

    test('falls back to err.message when detail is absent', () => {
        const details = resolveTransportErrorDetails({ message: 'Network error' });
        assert.equal(details[0], 'Network error');
    });

    test('falls back to the generic message only when neither detail nor message exist', () => {
        const details = resolveTransportErrorDetails({});
        assert.equal(details[0], GENERIC_OCR_FAILURE_MESSAGE);
    });

    test('correlationId is appended as a second detail line when available', () => {
        const details = resolveTransportErrorDetails({ detail: 'Backend error', correlationId: 'abc-123' });
        assert.equal(details.length, 2);
        assert.ok(details[1].includes('abc-123'));
    });

    test('no correlationId line is added when correlationId is absent', () => {
        const details = resolveTransportErrorDetails({ detail: 'Backend error' });
        assert.equal(details.length, 1);
    });
});

describe('message constants', () => {
    test('the client-processing message is distinct from the generic OCR-failure message', () => {
        assert.notEqual(CLIENT_PROCESSING_ERROR_MESSAGE, GENERIC_OCR_FAILURE_MESSAGE);
        assert.equal(CLIENT_PROCESSING_ERROR_MESSAGE, 'Os dados foram extraídos do documento, mas ocorreu um erro ao processar o resultado.');
    });
});
