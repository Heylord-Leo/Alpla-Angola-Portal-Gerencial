/**
 * useContractOcr — Phase 1 Contract OCR hook
 *
 * Responsibilities:
 *   1. Trigger OCR extraction for a contract (triggerOcr)
 *   2. Poll GET /ocr-status every 3 s until COMPLETED or FAILED
 *   3. Fetch GET /ocr-fields once on COMPLETED
 *   4. Build and maintain OcrState (keyed by fieldName)
 *   5. Expose per-field actions: confirm, discard, applysuggestion, edit
 *   6. Persist decisions via POST /ocr-confirm after each user action
 *   7. Expose a buildSafePayloadMask() helper for use in handleSubmit
 *
 * Usage (ContractCreate / ContractEdit — Batch 7):
 *
 *   const ocr = useContractOcr(contractId);
 *
 *   // Trigger:
 *   await ocr.triggerOcr();
 *
 *   // Check state:
 *   ocr.isPolling         → true while PENDING/PROCESSING
 *   ocr.status            → OcrProcessingStatus | null
 *   ocr.fields            → OcrState (keyed by fieldName)
 *   ocr.referenceFields   → OcrExtractedField[] for the summary panel
 *   ocr.error             → string | null
 *   ocr.qualityScore      → number | null
 *   ocr.isPartial         → boolean
 *   ocr.conflictsDetected → boolean
 *
 *   // Per-field user actions (each calls confirmOcrFields internally):
 *   ocr.confirmField(fieldName, acceptedValue?)
 *   ocr.discardField(fieldName)
 *   ocr.applySuggestion(fieldName)   // SUGGESTION → moves to confirmed AUTO_FILL state
 *   ocr.editField(fieldName, value)  // records an edit before confirm
 *
 *   // For confirmAll button:
 *   ocr.confirmAll()
 *
 *   // In handleSubmit — get the set of unconfirmed gated fields:
 *   const unconfirmed = ocr.getUnconfirmedGatedFields();
 *
 *   // Cleanup:
 *   ocr.reset()
 */

import { useRef, useState, useCallback, useEffect } from 'react';
import {
    triggerContractOcr,
    getOcrStatus,
    getOcrFields,
    confirmOcrFields,
} from '../lib/contractsApi';
import type {
    OcrProcessingStatus,
    OcrExtractedField,
    OcrState,
    OcrFieldConfirmItem,
} from '../types/contractOcr.types';
import { OCR_GATED_FIELDS } from '../types/contractOcr.types';

// ─── Constants ────────────────────────────────────────────────────────────────

/** Polling interval in ms */
const POLL_INTERVAL_MS = 3_000;

/** Terminal states — stop polling when reached */
const TERMINAL_STATUSES: OcrProcessingStatus[] = ['COMPLETED', 'FAILED'];

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Convert the OcrFieldsResponse field list into an OcrState keyed by fieldName */
function buildOcrState(fields: OcrExtractedField[]): OcrState {
    const state: OcrState = {};
    for (const f of fields) {
        if (f.displayHint === 'REFERENCE_ONLY') continue; // handled separately
        state[f.fieldName] = {
            fieldId:        f.id,
            fieldName:      f.fieldName,
            displayHint:    f.displayHint,
            normalisedValue: f.normalisedValue,
            rawValue:       f.rawExtractedValue,
            confidenceScore: f.confidenceScore,
            confirmed:      f.confirmedByUser,
            discarded:      f.discardedByUser,
            overriddenValue: f.finalSavedValue !== f.normalisedValue && f.finalSavedValue !== null
                ? f.finalSavedValue
                : undefined,
        };
    }
    return state;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseContractOcrReturn {
    // State
    status:            OcrProcessingStatus | null;
    isPolling:         boolean;
    fields:            OcrState;
    /** REFERENCE_ONLY fields — rendered in the summary panel, not form fields */
    referenceFields:   OcrExtractedField[];
    qualityScore:      number | null;
    isPartial:         boolean;
    conflictsDetected: boolean;
    error:             string | null;
    hasOcrData:        boolean;

    // Actions
    triggerOcr:        () => Promise<void>;
    confirmField:      (fieldName: string, acceptedValue?: string) => Promise<void>;
    discardField:      (fieldName: string) => Promise<void>;
    /**
     * Apply a SUGGESTION: moves the field into a "ready-to-confirm" accepted state.
     * The value moves to overriddenValue so the user can still edit before final Confirmar.
     * Does NOT call confirmOcrFields — the user must still click Confirmar.
     */
    applySuggestion:   (fieldName: string) => void;
    /**
     * Record an in-progress edit made by the user before confirming.
     * Updates overriddenValue in local state only — no API call.
     */
    editField:         (fieldName: string, value: string) => void;
    /** Confirm all unconfirmed, non-discarded AUTO_FILL fields at once. */
    confirmAll:        () => Promise<void>;
    /** Returns fieldNames that are AUTO_FILL, unconfirmed, and in OCR_GATED_FIELDS. */
    getUnconfirmedGatedFields: () => string[];
    reset:             () => void;
}

export function useContractOcr(contractId: string | null | undefined): UseContractOcrReturn {
    const [status,            setStatus]            = useState<OcrProcessingStatus | null>(null);
    const [isPolling,         setIsPolling]         = useState(false);
    const [fields,            setFields]            = useState<OcrState>({});
    const [referenceFields,   setReferenceFields]   = useState<OcrExtractedField[]>([]);
    const [qualityScore,      setQualityScore]      = useState<number | null>(null);
    const [isPartial,         setIsPartial]         = useState(false);
    const [conflictsDetected, setConflictsDetected] = useState(false);
    const [error,             setError]             = useState<string | null>(null);

    const pollTimerRef      = useRef<ReturnType<typeof setTimeout> | null>(null);
    const isMountedRef      = useRef(true);

    // Track the extractionRecordId used for confirm calls
    const extractionRecordIdRef = useRef<string | null>(null);

    // ── Cleanup on unmount ────────────────────────────────────────────────────
    useEffect(() => {
        isMountedRef.current = true;
        return () => {
            isMountedRef.current = false;
            if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
        };
    }, []);

    // ── Field fetcher (called once after COMPLETED) ───────────────────────────
    const fetchFields = useCallback(async (cId: string) => {
        try {
            const res = await getOcrFields(cId);
            if (!isMountedRef.current) return;

            extractionRecordIdRef.current = res.extractionRecordId;
            setQualityScore(res.qualityScore);
            setIsPartial(res.isPartial);
            setConflictsDetected(res.conflictsDetected);
            setFields(buildOcrState(res.fields));
            setReferenceFields(res.fields.filter(f => f.displayHint === 'REFERENCE_ONLY'));
        } catch (err) {
            if (!isMountedRef.current) return;
            const msg = err instanceof Error ? err.message : String(err);
            setError(`Erro ao carregar campos OCR: ${msg}`);
        }
    }, []);

    // ── Polling logic ─────────────────────────────────────────────────────────
    const schedulePoll = useCallback((cId: string) => {
        pollTimerRef.current = setTimeout(async () => {
            if (!isMountedRef.current) return;
            try {
                const res = await getOcrStatus(cId);
                if (!isMountedRef.current) return;

                setStatus(res.ocrStatus);

                if (res.ocrStatus && TERMINAL_STATUSES.includes(res.ocrStatus)) {
                    setIsPolling(false);
                    if (res.ocrStatus === 'COMPLETED') {
                        await fetchFields(cId);
                    } else {
                        // FAILED
                        setError(res.latestRecord?.errorMessage ?? 'O processamento OCR falhou.');
                    }
                } else {
                    // Still in progress — schedule next tick
                    schedulePoll(cId);
                }
            } catch (err) {
                if (!isMountedRef.current) return;
                setIsPolling(false);
                const msg = err instanceof Error ? err.message : String(err);
                setError(`Erro ao verificar estado OCR: ${msg}`);
            }
        }, POLL_INTERVAL_MS);
    }, [fetchFields]);

    // ── triggerOcr ────────────────────────────────────────────────────────────
    const triggerOcr = useCallback(async () => {
        if (!contractId) {
            setError('ID do contrato não disponível.');
            return;
        }
        if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
        setError(null);
        setIsPolling(true);
        setStatus('PENDING');
        setFields({});
        setReferenceFields([]);
        extractionRecordIdRef.current = null;

        try {
            await triggerContractOcr(contractId);
            if (!isMountedRef.current) return;
            schedulePoll(contractId);
        } catch (err) {
            if (!isMountedRef.current) return;
            setIsPolling(false);
            setStatus(null);
            const msg = err instanceof Error ? err.message : String(err);
            setError(`Não foi possível iniciar o OCR: ${msg}`);
        }
    }, [contractId, schedulePoll]);

    // ── Internal confirm helper ───────────────────────────────────────────────
    const persistConfirmDecisions = useCallback(
        async (items: OcrFieldConfirmItem[]) => {
            if (!contractId || !items.length) return;
            try {
                await confirmOcrFields(contractId, { fields: items });
            } catch (err) {
                // Non-fatal — local state already updated. Log to console only.
                console.error('[useContractOcr] confirmOcrFields failed:', err);
            }
        },
        [contractId],
    );

    // ── confirmField ──────────────────────────────────────────────────────────
    const confirmField = useCallback(
        async (fieldName: string, acceptedValue?: string) => {
            const current = fields[fieldName];
            if (!current || current.discarded) return;

            const finalValue = acceptedValue ?? current.overriddenValue ?? current.normalisedValue ?? undefined;
            const wasOverridden = acceptedValue !== undefined
                ? acceptedValue !== current.normalisedValue
                : !!current.overriddenValue;

            // Optimistic local update
            setFields(prev => ({
                ...prev,
                [fieldName]: {
                    ...prev[fieldName],
                    confirmed:      true,
                    overriddenValue: finalValue !== current.normalisedValue ? finalValue : prev[fieldName].overriddenValue,
                },
            }));

            await persistConfirmDecisions([{
                fieldId:      current.fieldId,
                discarded:    false,
                acceptedValue: finalValue,
                wasOverridden,
            }]);
        },
        [fields, persistConfirmDecisions],
    );

    // ── discardField ──────────────────────────────────────────────────────────
    const discardField = useCallback(
        async (fieldName: string) => {
            const current = fields[fieldName];
            if (!current) return;

            setFields(prev => ({
                ...prev,
                [fieldName]: { ...prev[fieldName], discarded: true, confirmed: false },
            }));

            await persistConfirmDecisions([{
                fieldId:   current.fieldId,
                discarded: true,
                wasOverridden: false,
            }]);
        },
        [fields, persistConfirmDecisions],
    );

    // ── applySuggestion ───────────────────────────────────────────────────────
    // Moves SUGGESTION field to applied state (value visible in chip + form).
    // Does NOT persist — user must still click Confirmar.
    const applySuggestion = useCallback((fieldName: string) => {
        setFields(prev => {
            const f = prev[fieldName];
            if (!f || f.displayHint !== 'SUGGESTION') return prev;
            return {
                ...prev,
                [fieldName]: {
                    ...f,
                    displayHint: 'AUTO_FILL' as const,  // treat as pending confirmation
                    confirmed:   false,
                },
            };
        });
    }, []);

    // ── editField ─────────────────────────────────────────────────────────────
    const editField = useCallback((fieldName: string, value: string) => {
        setFields(prev => {
            if (!prev[fieldName]) return prev;
            return {
                ...prev,
                [fieldName]: { ...prev[fieldName], overriddenValue: value, confirmed: false },
            };
        });
    }, []);

    // ── confirmAll ────────────────────────────────────────────────────────────
    const confirmAll = useCallback(async () => {
        const items: OcrFieldConfirmItem[] = [];

        setFields(prev => {
            const next = { ...prev };
            for (const [name, f] of Object.entries(next)) {
                if (f.confirmed || f.discarded) continue;
                if (f.displayHint !== 'AUTO_FILL') continue;

                const finalValue = f.overriddenValue ?? f.normalisedValue ?? undefined;
                const wasOverridden = !!f.overriddenValue;

                items.push({
                    fieldId:      f.fieldId,
                    discarded:    false,
                    acceptedValue: finalValue,
                    wasOverridden,
                });

                next[name] = { ...f, confirmed: true };
            }
            return next;
        });

        await persistConfirmDecisions(items);
    }, [persistConfirmDecisions]);

    // ── getUnconfirmedGatedFields ─────────────────────────────────────────────
    const getUnconfirmedGatedFields = useCallback((): string[] => {
        return Object.values(fields)
            .filter(
                f =>
                    f.displayHint === 'AUTO_FILL' &&
                    !f.confirmed &&
                    !f.discarded &&
                    OCR_GATED_FIELDS.has(f.fieldName),
            )
            .map(f => f.fieldName);
    }, [fields]);

    // ── reset ─────────────────────────────────────────────────────────────────
    const reset = useCallback(() => {
        if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
        setStatus(null);
        setIsPolling(false);
        setFields({});
        setReferenceFields([]);
        setQualityScore(null);
        setIsPartial(false);
        setConflictsDetected(false);
        setError(null);
        extractionRecordIdRef.current = null;
    }, []);

    return {
        status,
        isPolling,
        fields,
        referenceFields,
        qualityScore,
        isPartial,
        conflictsDetected,
        error,
        hasOcrData: status === 'COMPLETED' && Object.keys(fields).length > 0,
        triggerOcr,
        confirmField,
        discardField,
        applySuggestion,
        editField,
        confirmAll,
        getUnconfirmedGatedFields,
        reset,
    };
}
