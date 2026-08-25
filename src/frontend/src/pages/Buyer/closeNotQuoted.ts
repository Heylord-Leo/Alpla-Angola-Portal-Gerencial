// Phase 3E.1 — pure validation contract for the "Desconsiderar item" / close-not-quoted modal, shared by
// the classic screen and the Workspace (both mount the SAME CloseNotQuotedModal). Mirrors the server rule
// (reason required + justification ≥ 20 chars). Eligibility of WHICH items may be closed is server-computed
// (item.canCloseNotQuoted); this only governs the modal form.

export const MIN_CLOSE_JUSTIFICATION_LENGTH = 20;

/** A close-not-quoted submission is valid when a reason is chosen and the justification meets the minimum. */
export function isCloseNotQuotedValid(reason: string, justification: string): boolean {
  return !!reason && justification.trim().length >= MIN_CLOSE_JUSTIFICATION_LENGTH;
}

/** Copy hint: closing the request's single remaining pending item ends the quotation stage. */
export function isLastPendingItem(pendingCount: number): boolean {
  return pendingCount === 1;
}
