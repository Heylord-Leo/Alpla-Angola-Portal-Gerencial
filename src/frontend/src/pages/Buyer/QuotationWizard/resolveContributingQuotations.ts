// ─────────────────────────────────────────────────────────────────────────────
// Adjustment V2 Phase 4 — resolve the EXISTING quotation(s) that contribute to an ApprovalBatch, so
// "Gerenciar Cotações" can open the right one in EDIT mode for a Buyer commercial correction (e.g.
// PRICE_NEGOTIATION). A batch-committed item is not eligible for a NEW manual quotation, so editing
// the contributing quotation is the correct mechanism.
//
// Matching is STRICT: candidate.quotationId (candidate model) or the legacy winner quotation-item's
// parent quotation. NEVER by supplier name and NEVER by total. Deduplicated, order-preserving,
// deterministic (0 / 1 / many). Dependency-free so it runs under the repo's node-only vitest.
// ─────────────────────────────────────────────────────────────────────────────
import { ApprovalBatchSummary, SavedQuotationDto } from '../../../types';

export function resolveBatchContributingQuotations(
    batch: Pick<ApprovalBatchSummary, 'items'> | null | undefined,
    quotations: SavedQuotationDto[] | null | undefined,
): SavedQuotationDto[] {
    const qs = quotations || [];
    if (!batch || qs.length === 0) return [];

    const ids = new Set<string>();
    for (const item of batch.items || []) {
        const candidates = item.candidates || [];
        for (const c of candidates) {
            if (c.quotationId) ids.add(c.quotationId);
        }
        // Legacy batch item (no candidate rows): map the selected quotation ITEM to its parent
        // quotation — the only place a GUID→quotation link is followed, and strictly by id.
        if (candidates.length === 0 && item.selectedQuotationItemId) {
            const parent = qs.find(q => (q.items || []).some(it => it.id === item.selectedQuotationItemId));
            if (parent) ids.add(parent.id);
        }
    }

    // Return the actual quotation objects, preserving the request's quotation order; only those
    // still present in the request's quotation list (never a fabricated/guessed entry).
    return qs.filter(q => ids.has(q.id));
}

/**
 * EDIT-mode source for a quotation — the same rule the classic screen already uses at its per-row
 * edit entry. Purpose is EDIT, never source conversion: an OCR-origin quotation edits via 'UPLOAD',
 * everything else via 'MANUAL'.
 */
export function quotationEditMode(q: Pick<SavedQuotationDto, 'sourceType'>): 'UPLOAD' | 'MANUAL' {
    return q.sourceType === 'OCR' ? 'UPLOAD' : 'MANUAL';
}
