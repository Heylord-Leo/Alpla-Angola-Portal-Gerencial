import { useCallback, useRef, useState } from 'react';
import { api } from '../lib/api';
import { PaymentDocumentOcrState } from '../types/paymentSourceDocument';
import { OcrDraft } from '../types';
import {
    OCR_MODULE_REQUESTS,
    OcrExtractionEnvelope,
    extractionFailureMessage,
    isSuccessfulExtraction
} from '../types/ocrExtraction';
import {
    ExtractionDiscrepancy,
    TemporaryPaymentDocument,
    extractDocumentFields,
    fromOcrDraft,
    mergeExtraction
} from '../lib/paymentRequestCreation';

const IDLE: PaymentDocumentOcrState = { isProcessing: false, classification: null, error: null };

export interface DocumentOcrRunResult {
    document: TemporaryPaymentDocument;
    discrepancies: ExtractionDiscrepancy[];
}

/**
 * OCR for the creation screen, held <b>per document</b>.
 *
 * <p>The state is a map keyed by <code>tempId</code>. Reading Documento 2 must not clear, replace or
 * even briefly disturb Documento 1's result — with a single request-level variable it would, which
 * is precisely the defect this whole design exists to prevent.</p>
 *
 * <p>It calls <code>POST /api/v1/requests/direct-ocr</code>, which takes a raw file and needs no
 * request. That is what makes reading a document possible <b>before</b> anything is persisted: no
 * lazy draft, no attachment created early, and no second attachment record to reconcile later. The
 * same file is uploaded exactly once, afterwards, when the request exists.</p>
 *
 * <p>Nothing here selects a document type. The extraction produces a proposal with its evidence;
 * the user confirms or corrects it through the same Release 2 field the edit screen uses.</p>
 *
 * @param mapResult <c>useOcrProcessor.mapOcrResultToDraft</c>. Passing it in is what makes a
 * document in a collection read exactly like a document in the single-document editor — same unit
 * aliases, same IVA matching, same discount cross-validation, same backend-authoritative supplier
 * match. Omitting it falls back to header fields only, with no lines and no supplier resolution.
 */
export function usePaymentDocumentOcr(
    mapResult?: (raw: OcrExtractionEnvelope) => Promise<OcrDraft>
) {
    const [states, setStates] = useState<Record<string, PaymentDocumentOcrState>>({});
    const [discrepancies, setDiscrepancies] = useState<Record<string, ExtractionDiscrepancy[]>>({});

    /** Files kept so a retry re-reads the same document instead of asking for it again. */
    const filesRef = useRef<Map<string, File>>(new Map());
    const inFlightRef = useRef<Set<string>>(new Set());

    const stateFor = useCallback(
        (tempId: string): PaymentDocumentOcrState => states[tempId] ?? IDLE, [states]);

    const patch = (tempId: string, next: Partial<PaymentDocumentOcrState>) =>
        setStates(prev => ({ ...prev, [tempId]: { ...(prev[tempId] ?? IDLE), ...next } }));

    /** Remembers the file behind a document, so OCR can be retried without re-picking it. */
    const registerFile = useCallback((tempId: string, file: File) => {
        filesRef.current.set(tempId, file);
    }, []);

    /**
     * Reads one document. Returns the merged document so the caller can store it; the OCR state
     * itself lives here, keyed by id.
     */
    const run = useCallback(async (
        document: TemporaryPaymentDocument,
        supplierLookup?: (name: string, taxId: string | null) => { id: number; name: string; taxId?: string | null } | null
    ): Promise<DocumentOcrRunResult | null> => {
        const file = filesRef.current.get(document.tempId);
        if (!file) {
            patch(document.tempId, {
                isProcessing: false,
                error: 'O ficheiro deste documento já não está disponível. Volte a anexá-lo.'
            });
            return null;
        }

        // One read at a time per document; two cards may read concurrently without interfering.
        if (inFlightRef.current.has(document.tempId)) return null;
        inFlightRef.current.add(document.tempId);

        patch(document.tempId, { isProcessing: true, error: null });

        try {
            // The module allowlist key, not a free-text hint: an unconfigured value makes
            // DocumentExtractionService refuse the extraction before any provider is called.
            const result: OcrExtractionEnvelope =
                await api.requests.directOcrExtract(file, OCR_MODULE_REQUESTS);

            // HTTP 200 is not success. A blocked module, a disabled provider, an unreadable file
            // and a timeout all return 200 with success:false, every field null and zero tokens —
            // which, taken at face value, produces an empty editor that claims it was read.
            if (!isSuccessfulExtraction(result)) {
                patch(document.tempId, {
                    isProcessing: false,
                    // The fallback classifier can still guess a type from the FILENAME alone. That
                    // guess describes the name of the file, not the document, and must not be
                    // presented as a reading — so nothing is kept from a failed extraction.
                    classification: null,
                    error: extractionFailureMessage(result)
                });
                setDiscrepancies(prev => ({ ...prev, [document.tempId]: [] }));
                return null;
            }

            const extracted = mapResult
                ? fromOcrDraft(await mapResult(result), result)
                : extractDocumentFields(result);
            const merged = mergeExtraction(document, extracted, supplierLookup);

            patch(document.tempId, {
                isProcessing: false,
                classification: extracted.classification,
                error: null
            });
            setDiscrepancies(prev => ({ ...prev, [document.tempId]: merged.discrepancies }));

            return { document: merged.document, discrepancies: merged.discrepancies };
        } catch (e: any) {
            // Local to this card, and persistent: a failed read must stay on screen. Manual entry
            // remains open, so an unreadable scan can never strand a document.
            patch(document.tempId, {
                isProcessing: false,
                error: (e?.message ?? 'Não foi possível ler este documento.') +
                       ' Pode tentar novamente ou preencher os dados manualmente.'
            });
            return null;
        } finally {
            inFlightRef.current.delete(document.tempId);
        }
    }, [mapResult]);

    /** Everything this document's previous file produced. Used when the attachment is replaced. */
    const reset = useCallback((tempId: string) => {
        filesRef.current.delete(tempId);
        setStates(prev => ({ ...prev, [tempId]: IDLE }));
        setDiscrepancies(prev => ({ ...prev, [tempId]: [] }));
    }, []);

    const forget = useCallback((tempId: string) => {
        filesRef.current.delete(tempId);
        setStates(prev => { const n = { ...prev }; delete n[tempId]; return n; });
        setDiscrepancies(prev => { const n = { ...prev }; delete n[tempId]; return n; });
    }, []);

    return {
        stateFor,
        discrepanciesFor: (tempId: string) => discrepancies[tempId] ?? [],
        registerFile,
        run,
        reset,
        forget
    };
}
