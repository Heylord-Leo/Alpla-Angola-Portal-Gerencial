import { describe, it, expect } from 'vitest';
import {
    evaluateQuotationLinePricing, describeQuotationPricingState, readLinePricingConflict, QUOTATION_PRICING_NOT_LINE_RESOLVED,
    resolveOcrLineDiscount, resolveOcrHeaderDiscount, allocateAmountProportionally, allocateGlobalDiscountToLines, lineNet,
    applyGlobalDiscountAllocationToDraft, computeDraftQuotationTotal, recomputeDraftLineTotal, describeAllocationRefusal,
    type AllocatableDraftLine, type DraftPricingLike,
} from './quotationPricing';

// v2.249.6 — REQ-21/10/2026-595 (LUANDAY proforma): 150 CX × 1.390,00 (desc. 37.530,00), 100 CX × 1.885,00 (desc. 30.160,00), IVA 5 %.
// Before discounts 397.000,00; discounts 67.690,00; taxable 329.310,00; IVA 16.465,50; payable 345.775,50.

const req595Persisted = {
    discountAmount: 67_690, totalAmount: 345_775.50,
    items: [{ lineTotal: 218_925, reconciliationStatus: 'MAPPED' }, { lineTotal: 197_925, reconciliationStatus: 'MAPPED' }],
};
const req595PerLine = {
    discountAmount: 0, totalAmount: 345_775.50,
    items: [{ lineTotal: 179_518.50, reconciliationStatus: 'MAPPED' }, { lineTotal: 166_257, reconciliationStatus: 'MAPPED' }],
};

describe('evaluateQuotationLinePricing — mirror of QuotationLinePricingPolicy', () => {
    it('REQ-595 persisted shape: global discount unallocated, lines sum to the undiscounted 416.850,00', () => {
        const e = evaluateQuotationLinePricing(req595Persisted);
        expect(e.isLineResolved).toBe(false);
        expect(e.reasonCode).toBe('GLOBAL_DISCOUNT_UNALLOCATED');
        expect(e.consideredLineTotalSum).toBe(416_850);
        expect(e.difference).toBe(71_074.5);
        expect(e.source).toBe('CLIENT');
    });

    it('REQ-595 per-line shape is line-resolved', () => {
        const e = evaluateQuotationLinePricing(req595PerLine);
        expect(e.isLineResolved).toBe(true);
        expect(e.reasonCode).toBeNull();
        expect(e.difference).toBe(0);
    });

    it('prefers the server evaluation when the DTO carries it', () => {
        const e = evaluateQuotationLinePricing({ ...req595PerLine, isLinePricingResolved: false, linePricingReasonCode: 'LINE_TOTAL_MISMATCH', consideredLineTotalSum: 1 });
        expect(e.source).toBe('SERVER');
        expect(e.isLineResolved).toBe(false);
        expect(e.reasonCode).toBe('LINE_TOTAL_MISMATCH');
        expect(e.consideredLineTotalSum).toBe(1);
    });

    it('excludes IGNORED / NOT_QUOTED rows and flags a legacy line-sum mismatch beyond 1,00', () => {
        const e = evaluateQuotationLinePricing({ discountAmount: 0, totalAmount: 300, items: [
            { lineTotal: 100, reconciliationStatus: 'MAPPED' }, { lineTotal: 200, reconciliationStatus: 'SUBSTITUTE' },
            { lineTotal: 999, reconciliationStatus: 'IGNORED' }, { lineTotal: 0, reconciliationStatus: 'NOT_QUOTED' }, { lineTotal: 50, reconciliationStatus: 'EXTRA_ITEM' },
        ] });
        expect(e.consideredLineTotalSum).toBe(350);
        expect(e.reasonCode).toBe('LINE_TOTAL_MISMATCH');
        expect(evaluateQuotationLinePricing({ discountAmount: 0, totalAmount: 1000, items: [{ lineTotal: 1000.99 }] }).isLineResolved).toBe(true);
        expect(evaluateQuotationLinePricing({ discountAmount: 0, totalAmount: 0, items: [{ lineTotal: 253_080 }] }).isLineResolved).toBe(true); // total never computed
    });

    it('classifies every evaluation: LINE_RESOLVED, ZERO_VALUE, LEGACY_TOTAL_NOT_COMPUTED (resolved but distinct) and the two unresolved kinds', () => {
        expect(evaluateQuotationLinePricing(req595PerLine).kind).toBe('LINE_RESOLVED');
        expect(evaluateQuotationLinePricing(req595Persisted).kind).toBe('GLOBAL_DISCOUNT_UNALLOCATED');
        expect(evaluateQuotationLinePricing({ discountAmount: 0, totalAmount: 1_404_060, items: [{ lineTotal: 1_508_220 }] }).kind).toBe('LINE_TOTAL_MISMATCH');
        const legacy = evaluateQuotationLinePricing({ discountAmount: 0, totalAmount: 0, items: [{ lineTotal: 253_080 }] });
        expect(legacy).toMatchObject({ isLineResolved: true, kind: 'LEGACY_TOTAL_NOT_COMPUTED', consideredLineTotalSum: 253_080 });
        const zero = evaluateQuotationLinePricing({ discountAmount: 0, totalAmount: 0, items: [{ lineTotal: 0 }, { lineTotal: 99, reconciliationStatus: 'IGNORED' }] });
        expect(zero).toMatchObject({ isLineResolved: true, kind: 'ZERO_VALUE' });
        // A zero total with a global discount is unresolved, never "legacy".
        expect(evaluateQuotationLinePricing({ discountAmount: 5, totalAmount: 0, items: [{ lineTotal: 253_080 }] }).kind).toBe('GLOBAL_DISCOUNT_UNALLOCATED');
        // The server kind wins when the DTO carries it.
        expect(evaluateQuotationLinePricing({ ...req595PerLine, isLinePricingResolved: true, linePricingKind: 'LEGACY_TOTAL_NOT_COMPUTED' }).kind).toBe('LEGACY_TOTAL_NOT_COMPUTED');
    });
});

describe('describeQuotationPricingState — the chip and its sentence', () => {
    it('is null for a line-resolved quotation', () => {
        expect(describeQuotationPricingState(req595PerLine, 'AOA')).toBeNull();
    });
    it('names the global discount, the card total and the line sum, and points at the explicit allocation', () => {
        const st = describeQuotationPricingState(req595Persisted, 'AOA')!;
        expect(st.label).toBe('Desconto global não distribuído');
        expect(st.detail).toContain('67');
        expect(st.detail).toContain('345');
        expect(st.detail).toContain('416');
        expect(st.detail).toContain('Distribuir pelas linhas');
        expect(st.detail).toContain('Não pode ser enviada para aprovação');
    });
    it('describes a legacy mismatch as a save-to-recalculate case', () => {
        const st = describeQuotationPricingState({ discountAmount: 0, totalAmount: 1_404_060, items: [{ lineTotal: 1_508_220 }] }, 'AOA')!;
        expect(st.label).toBe('Totais por linha divergentes');
        expect(st.detail).toContain('Editar');
    });
});

describe('readLinePricingConflict — the batch endpoint 409', () => {
    it('reads the structured quotations payload', () => {
        const err = { status: 409, errorCode: QUOTATION_PRICING_NOT_LINE_RESOLVED, details: { code: QUOTATION_PRICING_NOT_LINE_RESOLVED, detail: 'Cotação LUANDAY …', quotations: [
            { quotationId: 'q1', supplierName: 'LUANDAY', reasonCode: 'GLOBAL_DISCOUNT_UNALLOCATED', globalDiscount: 67690, quotationTotal: 345775.5, consideredLineTotalSum: 416850 },
        ] } };
        const c = readLinePricingConflict(err)!;
        expect(c.detail).toContain('LUANDAY');
        expect(c.quotations[0]).toMatchObject({ quotationId: 'q1', reasonCode: 'GLOBAL_DISCOUNT_UNALLOCATED', globalDiscount: 67690 });
    });
    it('is null for any other error', () => {
        expect(readLinePricingConflict({ status: 409, details: { code: 'QUOTATION_REUSE_NOT_AUTHORIZED' } })).toBeNull();
        expect(readLinePricingConflict(new Error('x'))).toBeNull();
    });
});

describe('resolveOcrLineDiscount — never drops a printed discount silently', () => {
    const line1 = { quantity: 150, unitPrice: 1390, discountPercent: 0 }; // gross 208.500,00

    it('keeps the extracted discount when the document line total agrees (net 170.970,00)', () => {
        const r = resolveOcrLineDiscount({ ...line1, discountAmount: 37_530, totalPrice: 170_970 });
        expect(r).toMatchObject({ discountAmount: 37_530, discountUncertain: false, strategy: 'KEPT' });
        expect(r.discountPercent).toBe(18);
    });

    it('recovers the discount from the printed net line total when the provider extracted none (old Strategy A, now flagged)', () => {
        const r = resolveOcrLineDiscount({ ...line1, discountAmount: 0, totalPrice: 170_970 });
        expect(r).toMatchObject({ discountAmount: 37_530, discountUncertain: true, strategy: 'IMPLIED_FROM_LINE_TOTAL', impliedDiscount: 37_530 });
    });

    it('KEEPS an extracted discount that the printed line total contradicts (gross read into totalPrice) and asks for confirmation — the old code zeroed it', () => {
        const r = resolveOcrLineDiscount({ ...line1, discountAmount: 37_530, totalPrice: 208_500 });
        expect(r.discountAmount).toBe(37_530);
        expect(r.discountUncertain).toBe(true);
        expect(r.impliedDiscount).toBe(0);
        expect(r.strategy).toBe('KEPT');
    });

    it('a VAT-inclusive line total (179.518,50) does not rewrite the discount either', () => {
        const r = resolveOcrLineDiscount({ ...line1, discountAmount: 37_530, totalPrice: 179_518.5 });
        expect(r.discountAmount).toBe(37_530);
        expect(r.discountUncertain).toBe(true);
    });

    it('no discount, line total equals gross → no discount, not uncertain', () => {
        const r = resolveOcrLineDiscount({ ...line1, discountAmount: 0, totalPrice: 208_500 });
        expect(r).toMatchObject({ discountAmount: 0, discountUncertain: false, strategy: 'NONE' });
    });

    it('percent only (no line total): amount derived from the percent; a contradicting amount is flagged', () => {
        expect(resolveOcrLineDiscount({ quantity: 1, unitPrice: 510_000, discountAmount: 0, discountPercent: 7, totalPrice: 0 }))
            .toMatchObject({ discountAmount: 35_700, discountPercent: 7, discountUncertain: false, strategy: 'FROM_PERCENT' });
        expect(resolveOcrLineDiscount({ quantity: 1, unitPrice: 510_000, discountAmount: 1_000, discountPercent: 7, totalPrice: 0 }))
            .toMatchObject({ discountAmount: 35_700, discountUncertain: true });
    });
});

describe('resolveOcrHeaderDiscount — the summary "Desconto Comercial" is counted once', () => {
    it('equal to the sum of the line discounts → aggregate, no global discount (REQ-595)', () => {
        const r = resolveOcrHeaderDiscount(67_690, [37_530, 30_160]);
        expect(r.kind).toBe('AGGREGATE_OF_LINES');
        expect(r.globalDiscount).toBe(0);
        expect(r.note).toContain('soma dos descontos das linhas');
    });
    it('within 1,00 of the sum still counts as the aggregate (per-line rounding)', () => {
        expect(resolveOcrHeaderDiscount(67_690.99, [37_530, 30_160]).kind).toBe('AGGREGATE_OF_LINES');
        expect(resolveOcrHeaderDiscount(67_692, [37_530, 30_160]).kind).toBe('DOCUMENT_LEVEL');
    });
    it('lines without discount → document-level discount kept on the header, with the allocation warning', () => {
        const r = resolveOcrHeaderDiscount(67_690, [0, 0]);
        expect(r.kind).toBe('DOCUMENT_LEVEL');
        expect(r.globalDiscount).toBe(67_690);
        expect(r.note).toContain('distribuído pelas linhas');
    });
    it('no header discount → nothing', () => {
        expect(resolveOcrHeaderDiscount(0, [37_530])).toMatchObject({ kind: 'NONE', globalDiscount: 0, note: null });
    });
});

describe('allocateAmountProportionally — largest remainder, exact sum', () => {
    it('splits 67.690,00 over the REQ-595 nets and adds up exactly', () => {
        const shares = allocateAmountProportionally(67_690, [208_500, 188_500]);
        expect(shares[0] + shares[1]).toBeCloseTo(67_690, 10);
        expect(shares).toEqual([35_550.04, 32_139.96]);   // 208.500 / 397.000 and 188.500 / 397.000 of 67.690,00
    });
    it('three equal bases with an amount not divisible by 3 → cents go to the first by remainder order', () => {
        const shares = allocateAmountProportionally(1, [1, 1, 1]);
        expect(shares.reduce((a, b) => a + b, 0)).toBeCloseTo(1, 10);
        expect(shares).toEqual([0.34, 0.33, 0.33]);
    });
    it('zero bases get nothing; no bases / no amount → zeros', () => {
        expect(allocateAmountProportionally(10, [0, 5, 0])).toEqual([0, 10, 0]);
        expect(allocateAmountProportionally(0, [1, 2])).toEqual([0, 0]);
        expect(allocateAmountProportionally(10, [])).toEqual([]);
    });
});

describe('allocateGlobalDiscountToLines — the explicit "Distribuir pelas linhas"', () => {
    const lines: AllocatableDraftLine[] = [
        { quantity: 150, unitPrice: 1390, discountAmount: 0, reconciliationStatus: 'MAPPED', lineAdjustmentJustification: null },
        { quantity: 100, unitPrice: 1885, discountAmount: 0, reconciliationStatus: 'MAPPED', lineAdjustmentJustification: null },
        { quantity: 1, unitPrice: 999, discountAmount: 0, reconciliationStatus: 'IGNORED', lineAdjustmentJustification: null },
    ];

    it('adds proportional shares to the considered lines only, exact total, with a pre-filled editable reason', () => {
        const r = allocateGlobalDiscountToLines(lines, 67_690, 'AOA');
        expect(r.allocated).toBe(67_690);
        expect(r.allocatedLineCount).toBe(2);
        expect((r.items[0].discountAmount ?? 0) + (r.items[1].discountAmount ?? 0)).toBeCloseTo(67_690, 10);
        expect(r.items[2].discountAmount).toBe(0);                       // IGNORED never receives a share
        expect(r.items[0].lineAdjustmentJustification).toContain('distribuído proporcionalmente');
        expect(r.items[0].lineAdjustmentJustification!.length).toBeGreaterThanOrEqual(20);
        expect(r.items[0].discountPercent).toBeCloseTo(17.05, 2);
    });

    it('keeps an existing per-line reason and adds to an existing per-line discount', () => {
        const r = allocateGlobalDiscountToLines([{ quantity: 1, unitPrice: 100, discountAmount: 10, lineAdjustmentJustification: 'Motivo já registado pelo comprador.' }, { quantity: 1, unitPrice: 100, discountAmount: 0 }], 18, 'AOA');
        expect(r.shares).toEqual([8.53, 9.47]);                          // nets 90 / 100
        expect(r.items[0].discountAmount).toBe(18.53);
        expect(r.items[0].lineAdjustmentJustification).toBe('Motivo já registado pelo comprador.');
    });

    it('a zero global discount changes nothing (NOTHING_TO_ALLOCATE, same line objects)', () => {
        const r = allocateGlobalDiscountToLines(lines, 0, 'AOA');
        expect(r.items).toBe(lines);
        expect(r.allocatedLineCount).toBe(0);
        expect(r.refusal).toBe('NOTHING_TO_ALLOCATE');
    });

    it('the proportional allocation does NOT reproduce printed per-line discounts: LUANDAY gives 35.550,04 / 32.139,96, the document prints 37.530,00 / 30.160,00', () => {
        // Same total (345.775,50) — different line values, and the line values are what approval and the P.O. group freeze.
        const r = allocateGlobalDiscountToLines(lines.slice(0, 2), 67_690, 'AOA');
        expect(r.shares).toEqual([35_550.04, 32_139.96]);
        const rates = [{ id: 5, ratePercent: 5 }];
        const withIva = r.items.map(i => recomputeDraftLineTotal({ ...i, ivaRateId: 5 }, rates));
        expect(withIva).toEqual([181_597.46, 164_178.04]);                  // ≠ 179.518,50 / 166.257,00 printed on the document
        expect(withIva[0] + withIva[1]).toBeCloseTo(345_775.5, 10);          // the total alone cannot tell the two apart
        const printed = [{ ...lines[0], discountAmount: 37_530, ivaRateId: 5 }, { ...lines[1], discountAmount: 30_160, ivaRateId: 5 }].map(i => recomputeDraftLineTotal(i, rates));
        expect(printed).toEqual([179_518.5, 166_257]);
    });

    it('lineNet is gross minus the line discount, never negative', () => {
        expect(lineNet({ quantity: 150, unitPrice: 1390, discountAmount: 37_530 })).toBe(170_970);
        expect(lineNet({ quantity: 1, unitPrice: 10, discountAmount: 50 })).toBe(0);
    });
});

describe('allocation boundaries — never negative, never twice, never on ignored / not-quoted / zero-value lines', () => {
    const IVA = [{ id: 5, ratePercent: 5 }, { id: 14, ratePercent: 14 }];
    // Line A 10 × 1.000,00 (own discount 500,00, IVA 14 %) net 9.500; line B 3 × 2.000,00 (IVA 5 %) net 6.000; line C 1 × 0,00 (zero-value).
    const mixed = (discountAmount: number): DraftPricingLike => ({ currency: 'AOA', discountAmount, totalAmount: 0, globalDiscountKind: 'DOCUMENT_LEVEL', items: [
        { quantity: 10, unitPrice: 1_000, discountAmount: 500, ivaRateId: 14, reconciliationStatus: 'MAPPED' },
        { quantity: 3, unitPrice: 2_000, discountAmount: 0, ivaRateId: 5, reconciliationStatus: 'MAPPED' },
        { quantity: 1, unitPrice: 0, discountAmount: 0, ivaRateId: 14, reconciliationStatus: 'MAPPED' },
    ] });

    it('mixed VAT + existing line discount + zero-value line: shares by net, line totals recomputed, Σ lines equals the global-discount total (16.024,84)', () => {
        const before = computeDraftQuotationTotal(mixed(1_000), IVA);
        expect(before).toBe(16_024.84);                                        // taxable 14.500 + IVA 1.630 × 14.500/15.500
        const o = applyGlobalDiscountAllocationToDraft(mixed(1_000), IVA);
        expect(o.refusal).toBeNull();
        expect(o.allocatedLineCount).toBe(2);
        expect(o.draft.items.map(i => i.discountAmount)).toEqual([1_112.9, 387.1, 0]);
        expect(o.draft.items.map(i => i.totalPrice)).toEqual([10_131.29, 5_893.55, 0]);
        expect(o.draft.discountAmount).toBe(0);
        expect(o.draft.globalDiscountKind).toBe('NONE');
        expect(o.draft.totalAmount).toBe(16_024.84);
        expect(o.draft.items.reduce((s, i) => s + (i.totalPrice ?? 0), 0)).toBeCloseTo(16_024.84, 10);
        expect(o.note).toContain('distribuído por 2 linha(s)');
        expect(o.note).toContain('substitua estes valores pelos impressos');
    });

    it('a repeat press finds nothing to allocate and returns the SAME draft (the discount is never applied twice)', () => {
        const first = applyGlobalDiscountAllocationToDraft(mixed(1_000), IVA);
        const second = applyGlobalDiscountAllocationToDraft(first.draft, IVA);
        expect(second.refusal).toBe('NOTHING_TO_ALLOCATE');
        expect(second.draft).toBe(first.draft);
        expect(second.draft.items.map(i => i.discountAmount)).toEqual([1_112.9, 387.1, 0]);
        expect(second.allocatedLineCount).toBe(0);
    });

    it('an amount above the eligible net is refused, nothing changes and the note says why (no line may go negative)', () => {
        const draft = mixed(15_500.01);                                        // eligible net = 9.500 + 6.000 = 15.500
        const o = applyGlobalDiscountAllocationToDraft(draft, IVA);
        expect(o.refusal).toBe('EXCEEDS_ELIGIBLE_NET');
        expect(o.draft.items).toBe(draft.items);
        expect(o.draft.discountAmount).toBe(15_500.01);
        expect(o.draft.globalDiscountKind).toBe('DOCUMENT_LEVEL');
        expect(o.draft.globalDiscountNote).toContain('excede o valor líquido');
        expect(o.draft.globalDiscountNote).toContain('negativo');
        // Exactly the eligible net is the boundary: every eligible line goes to net 0 (a 100 % discount), never below.
        const edge = applyGlobalDiscountAllocationToDraft(mixed(15_500), IVA);
        expect(edge.refusal).toBeNull();
        expect(edge.draft.items.map(i => i.discountAmount)).toEqual([10_000, 6_000, 0]);
        expect(edge.draft.items.map(i => i.totalPrice)).toEqual([0, 0, 0]);
        expect(edge.draft.totalAmount).toBe(0);
    });

    it('IGNORED / NOT_QUOTED and zero-value lines never receive a share; with no eligible line the allocation is refused', () => {
        const draft: DraftPricingLike = { currency: 'AOA', discountAmount: 100, totalAmount: 0, items: [
            { quantity: 2, unitPrice: 300, discountAmount: 0, ivaRateId: 14, reconciliationStatus: 'IGNORED' },
            { quantity: 1, unitPrice: 400, discountAmount: 0, ivaRateId: 5, reconciliationStatus: 'NOT_QUOTED' },
            { quantity: 5, unitPrice: 0, discountAmount: 0, ivaRateId: 5, reconciliationStatus: 'MAPPED' },
        ] };
        const o = applyGlobalDiscountAllocationToDraft(draft, IVA);
        expect(o.refusal).toBe('NO_ELIGIBLE_LINES');
        expect(o.draft.items).toBe(draft.items);
        expect(o.draft.discountAmount).toBe(100);
        expect(describeAllocationRefusal('NO_ELIGIBLE_LINES', 100, 0, 'AOA')).toContain('ignoradas / não cotadas');
        // The same lines plus one considered line: only that line receives the whole amount.
        const withEligible: DraftPricingLike = { ...draft, items: [...draft.items, { quantity: 1, unitPrice: 1_000, discountAmount: 0, ivaRateId: 5, reconciliationStatus: 'SUBSTITUTE' }] };
        const o2 = applyGlobalDiscountAllocationToDraft(withEligible, IVA);
        expect(o2.refusal).toBeNull();
        expect(o2.draft.items.map(i => i.discountAmount)).toEqual([0, 0, 0, 100]);
        expect(o2.allocatedLineCount).toBe(1);
    });

    it('rounding remainders: exact cents, cents go to the largest fractions, and no share ever exceeds its line net', () => {
        expect(allocateAmountProportionally(0.01, [1, 1, 1])).toEqual([0.01, 0, 0]);
        expect(allocateAmountProportionally(100, [33.33, 33.33, 33.34])).toEqual([33.33, 33.33, 33.34]);
        expect(allocateAmountProportionally(0.99, [0.01, 0.01, 0.98])).toEqual([0.01, 0.01, 0.97]);
        expect(allocateAmountProportionally(1, [0.01, 0.01, 0.98])).toEqual([0.01, 0.01, 0.98]);
        for (const [amount, nets] of [[0.99, [0.01, 0.01, 0.98]], [10, [3.33, 3.33, 3.34]], [67_690, [208_500, 188_500]], [1_000, [9_500, 6_000, 0]]] as Array<[number, number[]]>) {
            const shares = allocateAmountProportionally(amount, nets);
            expect(shares.reduce((a, b) => a + b, 0)).toBeCloseTo(amount, 10);
            shares.forEach((s, i) => expect(s).toBeLessThanOrEqual(nets[i]));
        }
    });

    it('recomputeDraftLineTotal / computeDraftQuotationTotal follow the server arithmetic (Round2 per line, IVA on the net, never negative)', () => {
        expect(recomputeDraftLineTotal({ quantity: 150, unitPrice: 1_390, discountAmount: 37_530, ivaRateId: 5 }, IVA)).toBe(179_518.5);
        expect(recomputeDraftLineTotal({ quantity: 1, unitPrice: 10, discountAmount: 50, ivaRateId: 14 }, IVA)).toBe(0);
        expect(recomputeDraftLineTotal({ quantity: 3, unitPrice: 1_333.33, discountAmount: 0, ivaRateId: 14 }, IVA)).toBe(4_559.99);  // 3.999,99 + 560,00
        const d: DraftPricingLike = { discountAmount: 0, totalAmount: 0, items: [
            { quantity: 150, unitPrice: 1_390, discountAmount: 37_530, ivaRateId: 5 }, { quantity: 100, unitPrice: 1_885, discountAmount: 30_160, ivaRateId: 5 },
        ] };
        expect(computeDraftQuotationTotal(d, IVA)).toBe(345_775.5);
        expect(computeDraftQuotationTotal({ ...d, discountAmount: 999_999 }, IVA)).toBe(0);
    });
});
