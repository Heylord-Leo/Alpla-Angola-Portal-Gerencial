/**
 * Frontend line-level reconciliation helpers (materiality vs the immutable OCR baseline) plus the
 * calculation signature used to detect a stale preview. Mirrors the backend materiality rules:
 * quantity/unit — exact/any change; price/discount/IVA — isolated line impact beyond AOA 2.00.
 * The BACKEND remains authoritative (QuotationReconciliationCalculator); this is UX gating only.
 */
export const LINE_RECONCILIATION_TOLERANCE = 2.0;

export function lineValue(qty: number, unitPrice: number, discount: number, ivaPercent: number): number {
    const net = Math.max(0, (qty || 0) * (unitPrice || 0) - (discount || 0));
    return net + net * (ivaPercent || 0) / 100;
}

/** Resolve a line's FINAL IVA percent from its selected rate id (the draft stores ivaRateId, not the
 * percent). Null id ⇒ 0% (matches the backend's "no rate ⇒ 0"). */
export function resolveIvaPercent(ivaRateId: number | null | undefined, ivaRates?: any[]): number {
    if (ivaRateId == null || !ivaRates) return 0;
    const rate = ivaRates.find(r => r.id === ivaRateId);
    return rate ? (rate.ratePercent ?? 0) : 0;
}

/** True when an OCR-baselined CONSIDERED line's current financial values differ materially from
 * the OCR baseline. A line with no baseline (legacy/manual) is never material.
 * `ivaRates` is required to evaluate IVA-only materiality (the draft stores ivaRateId, not %). */
export function hasMaterialOcrChange(item: any, ivaRates?: any[]): boolean {
    if (!item) return false;
    if (item.ocrOriginalLineTotal == null && item.ocrOriginalQuantity == null && item.ocrOriginalUnitPrice == null) return false;
    const oQ = item.ocrOriginalQuantity ?? 0, oP = item.ocrOriginalUnitPrice ?? 0, oD = item.ocrOriginalDiscountAmount ?? 0, oI = item.ocrOriginalIvaRatePercent ?? 0;
    const fQ = item.quantity ?? 0, fP = item.unitPrice ?? 0, fD = item.discountAmount ?? 0;
    if (fQ !== oQ) return true;
    if ((item.ocrOriginalUnitId ?? null) !== (item.unitId ?? null)) return true;
    // Isolated field impacts, each evaluated with the other fields at their FINAL values (canonical
    // morph order qty → price → discount → IVA), mirroring QuotationReconciliationCalculator.
    if (Math.abs(lineValue(fQ, fP, oD, oI) - lineValue(fQ, oP, oD, oI)) > LINE_RECONCILIATION_TOLERANCE) return true; // price-only
    if (Math.abs(lineValue(fQ, fP, fD, oI) - lineValue(fQ, fP, oD, oI)) > LINE_RECONCILIATION_TOLERANCE) return true; // discount-only
    // IVA-only impact = |value(fQ,fP,fD,finalIva) − value(fQ,fP,fD,originalIva)| > tolerance.
    const fI = resolveIvaPercent(item.ivaRateId, ivaRates);
    if (Math.abs(lineValue(fQ, fP, fD, fI) - lineValue(fQ, fP, fD, oI)) > LINE_RECONCILIATION_TOLERANCE) return true; // iva-only
    return false;
}

/** True when a unit that forbids decimal quantities is given a fractional final quantity (mirrors the
 * backend ValidateFinalQuantityPrecision invalid-input rule; decimal-safe via epsilon). */
export function isFractionalForIntegerUnit(item: any, units?: any[]): boolean {
    if (!item || item.unitId == null || !units) return false;
    const unit = units.find(u => u.id === item.unitId);
    if (!unit || unit.allowsDecimalQuantity !== false) return false;
    const q = item.quantity ?? 0;
    return Math.abs(q - Math.round(q)) > 1e-9;
}

/** Considered statuses that compose the quotation total. */
const CONSIDERED = ['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM'];

/** True when a considered, materially-changed line lacks a valid adjustment reason (min-length only
 * here; the shared validator provides the full quality check at the call sites). */
export function materialChangeLackingReason(item: any): boolean {
    if (!CONSIDERED.includes(item.reconciliationStatus)) return false;
    if (!hasMaterialOcrChange(item)) return false;
    const t = (item.lineAdjustmentJustification || '').trim();
    return t.length < 20;
}

/** Stable signature of every field that affects the reconciliation calculation. Any change makes a
 * previously-fetched preview stale. */
export function draftCalculationSignature(draft: any): string {
    if (!draft) return '';
    const items = (draft.items || []).map((i: any) => [
        i.reconciliationStatus, i.quantity, i.unitPrice, i.discountAmount, i.ivaRateId, i.unitId,
        i.mappedRequestLineItemId, i.lineAdjustmentJustification, i.reconciliationJustification,
        i.lineOrigin, i.ocrOriginalLineTotal, i.ocrOriginalQuantity, i.ocrOriginalUnitPrice
    ]);
    return JSON.stringify({ ocr: draft.ocrTotalAmount ?? null, disc: draft.discountAmount ?? 0, items });
}
