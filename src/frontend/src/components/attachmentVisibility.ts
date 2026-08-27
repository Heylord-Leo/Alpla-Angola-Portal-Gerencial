// Phase 4B — attachment visibility invariant: any authorized attachment returned by the backend must be
// visible somewhere in the Request UI. Attachments whose type has a dedicated card render there; any other
// type (e.g. PAYMENT_SOURCE_DOCUMENT on a request not flagged multi-document, or a legacy/unknown type)
// falls into a read-only fallback bucket instead of being silently dropped.

/** Returns the attachments that have no dedicated card AND are not already shown in the source-doc section. */
export function selectUnmappedAttachments<T extends { id: string; attachmentTypeCode: string }>(
  attachments: T[],
  knownCardTypes: Iterable<string>,
  shownSourceAttachmentIds: Set<string>,
): T[] {
  const known = new Set(knownCardTypes);
  return attachments.filter(a => !known.has(a.attachmentTypeCode) && !shownSourceAttachmentIds.has(a.id));
}
