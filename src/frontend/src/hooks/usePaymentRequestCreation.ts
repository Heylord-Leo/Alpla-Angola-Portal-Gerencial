import { useCallback, useRef, useState } from 'react';
import { ApiError, api } from '../lib/api';
import {
    CreationPhase,
    TemporaryPaymentDocument,
    allocateRoundingResidual,
    applyDocumentResult,
    derivePhase,
    pendingDocuments,
    summariseFailures,
    toCreatePayload,
    unpersistableDocuments
} from '../lib/paymentRequestCreation';
import { PaymentSourceDocumentDto } from '../types/paymentSourceDocument';

export interface CreationRunResult {
    ok: boolean;
    requestId: string | null;
    /** True only when every persistable document reached the server. */
    allDocumentsPersisted: boolean;

    /**
     * Why request creation failed, carried ON THE RESULT because the caller acts on it in the
     * same tick — hook state set during the run is not visible to the closure that invoked it,
     * which is exactly how the backend's explanation was being replaced by a generic toast.
     */
    error?: string | null;
    /** Backend field-validation dictionary (ValidationProblemDetails.errors), when one came back. */
    fieldErrors?: Record<string, string[]> | null;
}

/**
 * Drives the multi-call creation of a PAYMENT request that carries several source documents.
 *
 * <p>The request must exist before a document can attach to it, so creation is a sequence rather
 * than a single POST — and a sequence that can fail halfway. Two properties make that safe:</p>
 *
 * <ul>
 *   <li><b>The request is created exactly once.</b> Its id is held in a ref and reused by every
 *   retry, so a failure at document 2 never produces a second request.</li>
 *   <li><b>Each document remembers whether it was persisted.</b> A retry skips what already
 *   succeeded rather than creating it twice, and the backend's attachment-scoped idempotency is the
 *   second line of defence if the client's memory is ever lost.</li>
 * </ul>
 *
 * <p>Nothing here submits the request for approval. Persisting documents and asking for approval are
 * separate acts, and doing the second before the first has finished is how a request reaches an
 * approver describing documents that were never saved.</p>
 */
export function usePaymentRequestCreation() {
    const [phase, setPhase] = useState<CreationPhase>('NOT_STARTED');
    const [failures, setFailures] = useState<Array<{ tempId: string; label: string; message: string }>>([]);
    const [error, setError] = useState<string | null>(null);

    /** Survives retries: the request is created once and only once. */
    const requestIdRef = useRef<string | null>(null);
    const isRunningRef = useRef(false);

    const reset = useCallback(() => {
        requestIdRef.current = null;
        setPhase('NOT_STARTED');
        setFailures([]);
        setError(null);
    }, []);

    /**
     * Stage B → C. Creates the draft if it does not exist yet, then persists every document that
     * has an attachment and has not already been saved.
     *
     * <p>Returns the updated documents so the caller can hold the temp→persisted mapping. Documents
     * are never reordered: the map is by <code>tempId</code>.</p>
     */
    const persist = useCallback(async (
        buildRequestPayload: () => unknown,
        documents: TemporaryPaymentDocument[],
        onDocumentsChanged: (next: TemporaryPaymentDocument[]) => void,
        /**
         * Turns placeholder attachment ids into real ones, now that a request exists to upload
         * against. Idempotent by contract: an id already uploaded must be reused, never re-sent.
         */
        materialiseAttachments?: (
            requestId: string, docs: TemporaryPaymentDocument[]
        ) => Promise<TemporaryPaymentDocument[]>,
        /**
         * Duplicate hierarchy LEVEL 4: the backend refused this document because another one
         * shares its supplier reference and the content evidence cannot decide. Resolves with the
         * user's written reason (≥ 20 chars) to retry once with the audited override, or null to
         * leave the document failed with the backend's explanation.
         */
        onAmbiguousDuplicate?: (
            document: TemporaryPaymentDocument, detail: string
        ) => Promise<string | null>
    ): Promise<CreationRunResult> => {
        if (isRunningRef.current) {
            return { ok: false, requestId: requestIdRef.current, allDocumentsPersisted: false };
        }
        isRunningRef.current = true;
        setError(null);

        let working = documents;

        try {
            // ── Stage B: the request, exactly once ──
            if (!requestIdRef.current) {
                setPhase('CREATING_REQUEST');
                try {
                    const created = await api.requests.create(buildRequestPayload());
                    requestIdRef.current = created.id;
                } catch (e: any) {
                    setPhase('PARTIAL_FAILURE');
                    const message = e?.message ?? 'Não foi possível criar o pedido.';
                    setError(message);
                    return {
                        ok: false,
                        requestId: null,
                        allDocumentsPersisted: false,
                        error: message,
                        // Field-level detail (e.g. "O título é obrigatório.") must reach the form,
                        // not die inside a generic toast. The backend already said what is wrong.
                        fieldErrors: e instanceof ApiError && e.fieldErrors ? e.fieldErrors : null
                    };
                }
            }

            const requestId = requestIdRef.current!;

            // ── Stage C.0: upload the files that were chosen before the request existed ──
            if (materialiseAttachments) {
                working = await materialiseAttachments(requestId, working);
                onDocumentsChanged(working);
            }

            // ── Stage C: one document at a time, so a failure names the document ──
            setPhase('SAVING_DOCUMENTS');

            for (const document of pendingDocuments(working)) {
                try {
                    const persisted: PaymentSourceDocumentDto =
                        await api.requests.createSourceDocument(requestId, toCreatePayload(document));

                    working = applyDocumentResult(working, document.tempId, { persisted });
                } catch (e: any) {
                    // LEVEL 4 ambiguous duplicate: ask the user once, retry once with the audited
                    // override. Any other failure — including a declined override — records the
                    // backend's explanation on the document.
                    let resolved = false;
                    if (e instanceof ApiError && e.errorCode === 'DUPLICATE_AMBIGUOUS' && onAmbiguousDuplicate) {
                        const reason = await onAmbiguousDuplicate(document, e.message);
                        if (reason) {
                            try {
                                const persisted: PaymentSourceDocumentDto =
                                    await api.requests.createSourceDocument(requestId, {
                                        ...toCreatePayload(document),
                                        duplicateOverrideAcknowledged: true,
                                        duplicateOverrideReason: reason
                                    });
                                working = applyDocumentResult(working, document.tempId, { persisted });
                                resolved = true;
                            } catch (retryError: any) {
                                working = applyDocumentResult(working, document.tempId, {
                                    error: retryError?.message ?? 'Não foi possível guardar este documento.'
                                });
                                resolved = true;
                            }
                        }
                    }

                    if (!resolved) {
                        // Document 1 keeps its persisted id — a later failure never undoes earlier work.
                        working = applyDocumentResult(working, document.tempId, {
                            error: e?.message ?? 'Não foi possível guardar este documento.'
                        });
                    }
                }

                onDocumentsChanged(working);
            }

            // ── Stage C.2: items, now that their owner has a real id ──
            const withItems = working.filter(d => d.persistedId && d.items.length > 0);
            if (withItems.length > 0) {
                setPhase('SAVING_ITEMS');

                for (const document of withItems) {
                    // v2.229.10 monetary reconciliation (MODEL 2): what is PERSISTED is the
                    // reconciled attribution — the document's cent-level rounding residual lands
                    // on its last eligible line, so Σ(persisted item totals) equals the declared
                    // document gross exactly. Client state stays canonical; the same pure rule
                    // recomputes the same allocation on a retry, so a partially-persisted
                    // document resumes with identical values.
                    const reconciledTotals = new Map(
                        allocateRoundingResidual(document.items, document.grossAmount)
                            .items.map(i => [i.tempId, i.totalAmount]));

                    for (const item of document.items.filter(i => !i.persistedId)) {
                        try {
                            const created = await api.requests.createLineItem(requestId, {
                                description: item.description,
                                quantity: item.quantity,
                                unitId: item.unitId,
                                // The backend resolves the unit from its CODE, so both are sent.
                                unit: item.unitCode ?? undefined,
                                unitPrice: item.unitPrice,
                                discountAmount: item.discountAmount,
                                ivaRateId: item.ivaRateId,
                                totalAmount: reconciledTotals.get(item.tempId) ?? item.totalAmount,
                                itemCatalogId: item.itemCatalogId,
                                // Mandatory for every PAYMENT item, and taken from the document the
                                // item belongs to — not from the request. In a request holding two
                                // invoices due on different dates, the request-level date is the
                                // right answer for at most one of them.
                                dueDate: document.dueDate
                                    ? new Date(document.dueDate).toISOString()
                                    : undefined,
                                plantId: document.plantId,
                                // The mapping that matters: the item names its OWN document's
                                // persisted id, resolved after that document was created.
                                paymentSourceDocumentId: document.persistedId
                            });

                            item.persistedId = created?.itemId ?? created?.id ?? null;
                        } catch (e: any) {
                            working = applyDocumentResult(working, document.tempId, {
                                error: e?.message ?? 'O documento foi guardado, mas um item falhou.'
                            });
                        }
                    }
                }

                onDocumentsChanged(working);
            }

            const nextPhase = derivePhase(working);
            setPhase(nextPhase);
            setFailures(summariseFailures(working));

            return {
                ok: nextPhase === 'COMPLETE',
                requestId,
                allDocumentsPersisted: pendingDocuments(working).length === 0 &&
                                       summariseFailures(working).length === 0
            };
        } finally {
            isRunningRef.current = false;
        }
    }, []);

    /** Documents the user must still give a file to before anything can be saved. */
    const blockingDocuments = useCallback(
        (documents: TemporaryPaymentDocument[]) => unpersistableDocuments(documents), []);

    return {
        phase,
        failures,
        error,
        requestId: requestIdRef.current,
        isRunning: isRunningRef.current,
        persist,
        reset,
        blockingDocuments
    };
}
