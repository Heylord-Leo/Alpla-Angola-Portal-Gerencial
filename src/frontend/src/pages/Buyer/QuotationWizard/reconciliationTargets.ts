// ─────────────────────────────────────────────────────────────────────────────
// Adjustment V2 Phase 4 — reconciliation target set. The Step-3 "Itens Solicitados" panel (and the
// mapping dropdown) must show every request item that is EITHER still open for a new quotation OR
// ALREADY linked by the current quotation draft. The second clause is what keeps an EDIT of a
// contributing quotation working when its mapped request item is already committed to the batch
// (BATCH_ASSIGNED / QUOTATION_APPROVED) — the existing reconciliation relationship stays visible.
//
// STRICT scope: inclusion of a non-eligible item requires that THIS draft already maps it. An item
// committed to another batch that this quotation does not link is neither eligible nor mapped here,
// so it stays excluded — no weakening of duplicate-batch protection, no supplier/total/description
// guessing, no lifecycle mutation. For a NEW quotation (no mapped ids) the union collapses to the
// eligible set, so NEW behavior is unchanged. Dependency-injected eligibility so batchEligibility's
// global rule is reused verbatim, never forked. Pure + node-vitest friendly.
// ─────────────────────────────────────────────────────────────────────────────

/** The request line ids the current draft already maps (reconciliation relationship, any status). */
export function draftMappedRequestItemIds(draftItems: { mappedRequestLineItemId?: string | null }[] | null | undefined): Set<string> {
    const ids = new Set<string>();
    for (const i of draftItems || []) {
        if (i.mappedRequestLineItemId) ids.add(i.mappedRequestLineItemId);
    }
    return ids;
}

/**
 * Request items to offer as reconciliation targets: eligible-for-new-quotation UNION already-linked
 * by the current draft. Order-preserving; never duplicates (a globally-eligible item that is also
 * mapped appears once). `isEligible` is the caller's `isLineItemEligibleForQuotation` — injected so
 * the single global rule is reused, not copied.
 */
export function reconciliationRequestItems<T extends { id: string }>(
    lineItems: T[] | null | undefined,
    mappedRequestItemIds: Set<string>,
    isEligible: (li: T) => boolean,
): T[] {
    return (lineItems || []).filter(li => isEligible(li) || mappedRequestItemIds.has(li.id));
}
