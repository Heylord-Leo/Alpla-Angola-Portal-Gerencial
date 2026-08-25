// ─────────────────────────────────────────────────────────────────────────────
// Shared quotation save/preview logic (Phase 3C.1 host extraction). MOVED verbatim out of
// BuyerItemsList so the classic screen and the Buyer Workspace share ONE implementation of the
// save-payload builder and the ambiguous-save read-back reconciliation. NO behavior change — this is
// the same code, relocated. Consumed by useBuyerQuotationWizard (and imported back by BuyerItemsList).
// ─────────────────────────────────────────────────────────────────────────────
import { api } from '../../../lib/api';
import { SavedQuotationDto, OcrDraft, AmbiguousSavePreAttemptSnapshot } from '../../../types';
import { ClassificationConflictState, EMPTY_CONFLICT, buildClassificationPayload } from '../../../lib/documentClassificationDecision';
import { normalizeDocumentType } from '../../../lib/sourceDocumentType';

// ── Ambiguous-save read-back reconciliation (SaveQuotation create path only) ───────────────
//
// A network error / 5xx / timeout on SaveQuotation does not prove the create failed: EF Core's
// SaveChangesAsync wraps the write in one atomic transaction, and a client-side timeout can fire
// after the server has already committed. This is a FRONTEND-ONLY, BEST-EFFORT heuristic to
// detect that case — it is not a substitute for true idempotency (no CreationIdempotencyKey
// exists on Quotation yet; see the separate recommendation to mirror RequestLineItem's existing
// CreationIdempotencyKey pattern). It can only reduce false "save failed" reports; it cannot
// eliminate the ambiguity with certainty.
export const AMBIGUOUS_SAVE_CLOCK_SKEW_TOLERANCE_MS = 60_000;

/**
 * Best-effort match for a quotation created by an ambiguous SaveQuotation attempt.
 *
 * IMPORTANT: a request may legitimately hold MULTIPLE quotations from the same supplier (the
 * one-quotation-per-supplier restriction was removed from the backend — see SaveQuotation).
 * This function must never assume "same supplier" implies "the one I'm looking for" — supplier
 * match narrows the candidate set, it does not by itself confirm identity.
 *
 * Matching rules (in order):
 *  1. Must belong to the request being edited (implicit — `quotations` is that request's list).
 *  2. Must NOT be in the pre-attempt snapshot (i.e. must be new since the attempt started).
 *  3. supplierId must match exactly.
 *  4. createdAtUtc must be at/after (attemptStartedAtUtc - clock-skew tolerance).
 *  5. If the draft has a proformaAttachmentId, it must match EXACTLY — this is the strongest
 *     signal available (a stable per-upload GUID) and, when present, is treated as decisive: no
 *     fallback to the weaker heuristic below, to avoid matching an unrelated same-supplier
 *     quotation from a different document.
 *  6. When proformaAttachmentId is absent (manual/no-attachment drafts), fall back to a WEAKER
 *     heuristic: supplier + snapshot-exclusion + recency (1-4 above) narrow the candidates, then
 *     totalAmount/documentNumber are used only as corroboration — never as the sole identity
 *     criterion. If exactly one candidate remains with no corroboration, it is accepted only
 *     because it is the sole survivor of ALL the filters above (new, this supplier, this time
 *     window) — not because "one supplier = one quotation" is assumed anywhere. If multiple
 *     uncorroborated candidates remain (now a realistic case, since a supplier may have several
 *     concurrent quotations), the match is deliberately refused rather than guessed — the caller
 *     falls back to showing the original error and letting the buyer retry/check manually.
 */
export function findAmbiguousSaveMatch(
    quotations: SavedQuotationDto[],
    snapshot: AmbiguousSavePreAttemptSnapshot,
    draft: OcrDraft
): SavedQuotationDto | null {
    const attemptStartMs = new Date(snapshot.attemptStartedAtUtc).getTime() - AMBIGUOUS_SAVE_CLOCK_SKEW_TOLERANCE_MS;

    const candidates = quotations.filter(q =>
        !snapshot.existingQuotationIds.has(q.id) &&
        q.supplierId === draft.supplierId &&
        new Date(q.createdAtUtc as any).getTime() >= attemptStartMs
    );

    if (candidates.length === 0) return null;

    if (draft.proformaAttachmentId) {
        return candidates.find(q => q.proformaAttachmentId === draft.proformaAttachmentId) || null;
    }

    const corroborated = candidates.find(q =>
        (typeof q.totalAmount === 'number' && Math.abs(q.totalAmount - (draft.totalAmount || 0)) < 0.01) ||
        (!!draft.documentNumber && !!q.documentNumber && q.documentNumber.trim() === draft.documentNumber.trim())
    );
    if (corroborated) return corroborated;

    return candidates.length === 1 ? candidates[0] : null;
}

/**
 * Re-fetches the request and looks for a quotation matching the ambiguous attempt. Never throws:
 * a failure to confirm is reported the same as "no match found" — callers must not assume
 * success without a positive match.
 */
export async function tryReconcileAmbiguousSave(
    requestId: string,
    draft: OcrDraft,
    snapshot: AmbiguousSavePreAttemptSnapshot
): Promise<SavedQuotationDto | null> {
    try {
        const freshRequest = await api.requests.get(requestId);
        return findAmbiguousSaveMatch(freshRequest.quotations || [], snapshot, draft);
    } catch {
        return null;
    }
}

/** Single source of truth for the quotation save/preview wire payload — used by BOTH the save call
 * and the authoritative reconcile-preview so the two never diverge for the same draft. */
export function buildQuotationPayload(draft: OcrDraft, conflict: ClassificationConflictState = EMPTY_CONFLICT) {
    // The classification and the reasoning behind it travel together — the backend re-derives
    // whether this was an override and refuses an unconfirmed one.
    const classification = buildClassificationPayload(
        draft.documentType, draft.documentClassification, conflict);

    return {
        source: 'OCR',
        supplierId: draft.supplierId,
        supplierNameSnapshot: draft.supplierNameSnapshot,
        documentNumber: draft.documentNumber,
        documentDate: draft.documentDate ? new Date(draft.documentDate).toISOString() : undefined,
        // Post-Payment Completion (Release 2): the wizard has always collected "Tipo de Documento
        // da Cotação" but the value was dropped before reaching the API. It is now persisted, and
        // the winning quotation's value becomes the PO group's Final Invoice obligation.
        // Mapped to the canonical domain value ('FINAL' → 'FINAL_INVOICE').
        documentType: normalizeDocumentType(draft.documentType),
        documentTypeSource: classification.source,
        documentTypeOcrSuggestion: classification.suggestion,
        documentTypeOcrConfidence: classification.confidence,
        documentTypeEvidenceJson: classification.evidenceJson,
        documentTypeTitleFound: classification.titleFound,
        documentTypeConflictingEvidenceJson: classification.conflictingEvidenceJson,
        documentTypeSuggestionSource: classification.suggestionSource,
        classificationConflictAcknowledged: classification.acknowledged,
        classificationJustification: classification.justification,
        currency: draft.currency || 'AOA',
        discountAmount: draft.discountAmount || 0,
        totalAmount: draft.totalAmount || 0,
        proformaAttachmentId: draft.proformaAttachmentId,
        items: draft.items.filter(i => ['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM', 'NOT_QUOTED', 'IGNORED'].includes(i.reconciliationStatus as string)).map((i, idx) => ({
            mappedRequestLineItemId: i.mappedRequestLineItemId,
            lineNumber: idx + 1,
            description: i.description || '',
            quantity: i.quantity || 0,
            unitPrice: i.unitPrice || 0,
            discountAmount: i.discountAmount || 0,
            ivaRateId: i.ivaRateId || null || 1,
            unitId: i.unitId || null,
            lineTotal: i.totalPrice || 0,
            itemCatalogId: i.itemCatalogId || null,
            reconciliationStatus: i.reconciliationStatus,
            reconciliationJustification: i.reconciliationJustification || null,
            originalReconciliationJustification: i.reconciliationJustification || null,
            // ── Financial Reconciliation: OCR-original baseline + line adjustment reason ──
            lineOrigin: i.lineOrigin || (i.ocrOriginalLineTotal != null ? 'OCR' : 'MANUAL'),
            ocrOriginalQuantity: i.ocrOriginalQuantity ?? null,
            ocrOriginalUnitPrice: i.ocrOriginalUnitPrice ?? null,
            ocrOriginalDiscountAmount: i.ocrOriginalDiscountAmount ?? null,
            ocrOriginalIvaRatePercent: i.ocrOriginalIvaRatePercent ?? null,
            ocrOriginalUnitText: i.ocrOriginalUnitText ?? null,
            ocrOriginalUnitId: i.ocrOriginalUnitId ?? null,
            ocrOriginalLineTotal: i.ocrOriginalLineTotal ?? null,
            lineAdjustmentJustification: i.lineAdjustmentJustification || null
        }))
    };
}
