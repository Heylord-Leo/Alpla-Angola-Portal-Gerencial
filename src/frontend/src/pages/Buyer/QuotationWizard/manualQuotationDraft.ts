// ─────────────────────────────────────────────────────────────────────────────
// Manual quotation-entry seed (defect fix, Option A + shape follow-up). Extracted from
// BuyerItemsList.handleOpenWizard purely so it can be unit-tested. Behavior: the eligible requested
// items are seeded as quotation rows AVAILABLE FOR PRICING — reconciliationStatus is intentionally
// LEFT UNSET, matching the posture of OCR-derived items (which reach reconciliation with no status and
// auto-suggest a mapping to the matching requested line). They are NOT pre-marked NOT_QUOTED, which
// previously excluded them from the reconciliation supplier panel (realItems = items where
// reconciliationStatus !== 'NOT_QUOTED'). If the supplier did not quote a given item, the Buyer marks
// it via the EXISTING "Não cotado nesta cotação" control — unchanged here.
//
// INPUT CONTRACT: pass the NORMALIZED request line items (group.lineItems), whose `id` / `description`
// / `quantity` / `unitId` satisfy the OcrDraftItem contract (`description: string`, `quantity: number`
// are REQUIRED and consumed by reconciliation, e.g. the auto-suggest's `description.trim()`). Do NOT
// pass the raw list rows (group.items), whose description lives under `itemDescription` — that left
// `description` undefined and crashed reconciliation. `description` is required, so it is not defaulted
// here: an undefined description signals a wrong input source, which must be corrected, not masked.
// ─────────────────────────────────────────────────────────────────────────────
import { OcrDraftItem } from '../../../types';
import { isLineItemEligibleForQuotation } from '../batchEligibility';

export function buildManualQuotationDraftItems(lineItems: any[]): OcrDraftItem[] {
    return (lineItems || [])
        .filter(isLineItemEligibleForQuotation)
        .map((i: any) => ({
            mappedRequestLineItemId: i.id,
            lineNumber: 0,
            description: i.description,
            quantity: i.quantity,
            unitId: i.unitId || null,
            unitPrice: 0,
            ivaRateId: null,
            totalPrice: 0,
            discountAmount: 0,
            itemCatalogId: null,
            // reconciliationStatus intentionally UNSET — see file header (Option A).
        }));
}
