// Shared eligibility helpers for the batch/lote partial-approval flow.
// Used by BuyerItemsList.tsx and PartialApprovalBatchModal.tsx to decide which
// quotation item candidates can be selected for a new ApprovalBatch.

// Batch statuses that still "hold" a quotation item — i.e. it is currently
// being reviewed/approved elsewhere and must not be offered again.
// CANCELLED and REJECTED release the item back to the pending pool, so they
// are intentionally excluded from this list.
const ACTIVE_OR_APPROVED_BATCH_STATUSES = [
    'WAITING_AREA_APPROVAL',
    'AREA_ADJUSTMENT',
    'WAITING_FINAL_APPROVAL',
    'FINAL_ADJUSTMENT',
    'APPROVED'
];

/**
 * Returns true when the given quotation item is free to be selected for a
 * new approval batch. It is NOT selectable only while it is referenced by a
 * batch that is still active or already approved; a cancelled or rejected
 * batch never blocks re-selection.
 */
export function isQuotationItemSelectableForApproval(quotationItemId: string, group: any): boolean {
    // Candidate model: a quotation item is "held" both when it is a batch item's winner
    // (legacy pointer / Area-decided) AND when it is one of the item's submitted CANDIDATES —
    // an option under review must not be offered again elsewhere.
    return !(group?.approvalBatches ?? []).some((batch: any) =>
        ACTIVE_OR_APPROVED_BATCH_STATUSES.includes(batch.status) &&
        (batch.items ?? []).some((item: any) =>
            item.selectedQuotationItemId === quotationItemId ||
            (item.candidates ?? []).some((c: any) => c.quotationItemId === quotationItemId))
    );
}

/**
 * Line-item lifecycle statuses that mean the item is already covered — in an
 * active batch, already approved, or formally handled as not-quoted — and
 * therefore must NOT be offered again for mapping/reconciliation in the
 * Buyer's quotation wizard. Mirrors the whitelist already used by
 * getEligibleItemsForPartialApproval: only `null`/`undefined` and
 * `QUOTATION_PENDING` are eligible, everything else is treated as covered.
 * Matches the backend's own guard in RequestsController.ProposeNotQuoted
 * ("Lifecycle guard: must be null or QUOTATION_PENDING").
 */
const ELIGIBLE_QUOTATION_LIFECYCLE_STATUSES = [null, undefined, 'QUOTATION_PENDING'];

/**
 * Returns true when a request line item is still open for new
 * cotação/mapeamento in the Buyer wizard — i.e. it has not already been sent
 * in a batch, approved, or formally proposed/accepted as not-quoted.
 */
export function isLineItemEligibleForQuotation(item: any): boolean {
    return ELIGIBLE_QUOTATION_LIFECYCLE_STATUSES.includes(item?.quotationLifecycleStatus);
}
