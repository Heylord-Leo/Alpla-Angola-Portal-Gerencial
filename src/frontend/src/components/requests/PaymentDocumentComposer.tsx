import React, { useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, Plus } from 'lucide-react';
import { api } from '../../lib/api';
import { formatCurrencyAO } from '../../lib/utils';
import { PaymentSourceDocumentCard } from './PaymentSourceDocumentCard';
import { PaymentDocumentSummaryCard } from './PaymentDocumentSummaryCard';
import { PaymentDocumentsSummary } from './PaymentDocumentsSummary';
import { PaymentDocumentItemsEditor } from './PaymentDocumentItemsEditor';
import { AddPaymentDocumentChoice, AddDocumentMethod } from './AddPaymentDocumentChoice';
import {
    PaymentDocumentExtractionError,
    PaymentDocumentExtractionLoading,
    useFocusOnce
} from './PaymentDocumentExtractionState';
import { ConfirmationDialog } from '../common/ConfirmationDialog';
import { PaymentSupplierCreateModal, SupplierPrefill } from './PaymentSupplierCreateModal';
import { SUPPLIER_CREATE_NOT_ALLOWED } from '../../lib/supplierQuickCreate';
import { currencyConflictMessage } from '../../lib/paymentSourceDocuments';
import { ClassificationConflictState, EMPTY_CONFLICT } from '../../lib/documentClassificationDecision';
import { PaymentDocumentOcrState } from '../../types/paymentSourceDocument';
import { IvaRate, Unit } from '../../types';
import {
    CANDIDATE_FIELD_LABELS,
    ExtractionDiscrepancy,
    SourceDocumentCandidatesResult,
    TemporaryPaymentDocument,
    TemporaryPaymentItem,
    asCardDocument,
    createTemporaryDocument,
    duplicateTemporaryBasics,
    temporaryEstablishedCurrency
} from '../../lib/paymentRequestCreation';
import {
    blockerField,
    confirmationBlockers,
    confirmedTotals,
    documentLifecycle,
    nextLocalSequence
} from '../../lib/paymentDocumentComposition';

interface Props {
    documents: TemporaryPaymentDocument[];
    onChange: (next: TemporaryPaymentDocument[]) => void;

    /** The document open in the editor, owned by the parent so submission can see it. */
    activeTempId: string | null;
    onActiveChange: (tempId: string | null) => void;

    plants: Array<{ id: number; name: string }>;
    currencies: Array<{ code: string; name: string }>;
    units: Unit[];
    ivaRates: IvaRate[];

    /** Picks a file and returns a placeholder id plus the File itself for OCR. */
    onPickFile: (purpose: 'NEW' | 'REPLACE') =>
        Promise<{ id: string; fileName: string; file: File } | null>;

    /** Mirrors the backend rule for `POST /lookups/suppliers/from-payment-ocr`. */
    canCreateSupplier: boolean;

    ocrStateFor: (tempId: string) => PaymentDocumentOcrState;
    discrepanciesFor: (tempId: string) => ExtractionDiscrepancy[];
    onRunOcr: (document: TemporaryPaymentDocument) => Promise<void>;
    onResetOcr: (tempId: string) => void;

    /** The selected company's registered NIF, for the customer-NIF discrepancy notice. */
    selectedCompanyTaxId?: string | null;

    disabled?: boolean;
}

/**
 * A PAYMENT request's source documents, composed one at a time.
 *
 * <p>The screen shows exactly one thing at a time: the choice of how to start a document, or the
 * document being worked on. Everything already dealt with sits above it as a one-line summary. What
 * it deliberately never shows is an empty document card next to a form the user has already filled —
 * that is what made the previous screen read as "enter the same invoice twice".</p>
 *
 * <p>The multi-document model underneath is unchanged: each document keeps its own reading, its own
 * classification decision, its own items and its own identity. Only the presentation is
 * progressive.</p>
 */
export function PaymentDocumentComposer({
    documents,
    onChange,
    activeTempId,
    onActiveChange,
    plants,
    currencies,
    units,
    ivaRates,
    onPickFile,
    canCreateSupplier,
    ocrStateFor,
    discrepanciesFor,
    onRunOcr,
    onResetOcr,
    selectedCompanyTaxId = null,
    disabled = false
}: Props) {
    const [chooserOpen, setChooserOpen] = useState(false);
    const [pendingRemoval, setPendingRemoval] = useState<TemporaryPaymentDocument | null>(null);
    const [pendingReplace, setPendingReplace] = useState<TemporaryPaymentDocument | null>(null);
    const [pendingSwitch, setPendingSwitch] = useState<TemporaryPaymentDocument | null>(null);
    const [currencyErrors, setCurrencyErrors] = useState<Record<string, string | null>>({});
    const [showBlockers, setShowBlockers] = useState(false);
    /**
     * Supplier quick-create, scoped to the document that asked for it.
     *
     * <p>The tempId is held with the request so the created supplier is applied to the document the
     * user opened it from — creating a supplier while composing Documento 2 must not touch
     * Documento 1.</p>
     */
    const [supplierCreate, setSupplierCreate] =
        useState<{ tempId: string; prefill: SupplierPrefill } | null>(null);
    const isAddingRef = useRef(false);

    /**
     * Review-time candidate matching (v2.229.10 L4 flow), per document. Advisory: the same rule
     * engine runs authoritatively at persistence — this exists so the user learns about a probable
     * duplicate while reviewing, not after the whole OCR-and-review effort.
     */
    const [candidateResults, setCandidateResults] =
        useState<Record<string, SourceDocumentCandidatesResult | null>>({});
    const candidateTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    const active = documents.find(d => d.tempId === activeTempId) ?? null;
    const others = documents.filter(d => d.tempId !== activeTempId);
    const activeOcr = active ? ocrStateFor(active.tempId) : { isProcessing: false, error: null, classification: null };

    /** A document mid-read is not a document anyone may act on — including by leaving it. */
    const isExtracting = !!active && active.entryMode === 'PENDING_OCR';

    // Focus the review area once, when the reading lands. Once, because a document that finishes
    // reading while the user has moved on must not pull the caret out of the field they are in.
    useFocusOnce(active && active.entryMode === 'REVIEW' ? active.tempId : null, true);

    // Candidate preflight: re-asked (debounced) whenever the identity-relevant fields of the
    // active document settle. Failures are silent — this is a courtesy; persistence re-checks.
    //
    // Two value sets travel together and must never collapse: the ACCEPTED draft values (what the
    // user kept — "manteve X") and the SOURCE evidence (what the document says — "o documento
    // indica Y", held in the extraction-discrepancy state). Matching is about the PAPER, so the
    // evidence is sent alongside and wins server-side; nothing here rewrites the draft.
    const activeForCandidates = active && active.entryMode !== 'PENDING_OCR' ? active : null;
    const activeDiscrepancies = activeForCandidates ? discrepanciesFor(activeForCandidates.tempId) : [];
    const sourceEvidence = (field: string): string | null =>
        activeDiscrepancies.find(d => d.field === field)?.extractedValue ?? null;
    const sourceNumber = (field: string): number | null => {
        const raw = sourceEvidence(field);
        if (raw == null) return null;
        const n = Number(raw);
        return Number.isFinite(n) ? n : null;
    };

    const candidateKey = activeForCandidates ? [
        activeForCandidates.tempId,
        activeForCandidates.documentNumber ?? '',
        activeForCandidates.documentSeries ?? '',
        activeForCandidates.supplierId ?? '',
        activeForCandidates.supplierNameSnapshot ?? '',
        activeForCandidates.supplierTaxIdSnapshot ?? '',
        activeForCandidates.documentDate ?? '',
        activeForCandidates.currency ?? '',
        activeForCandidates.grossAmount ?? '',
        activeDiscrepancies.map(d => `${d.field}=${d.extractedValue}`).join(',')
    ].join('|') : null;

    useEffect(() => {
        if (candidateTimerRef.current) clearTimeout(candidateTimerRef.current);
        if (!activeForCandidates || !activeForCandidates.documentNumber?.trim()) return;

        const doc = activeForCandidates;
        candidateTimerRef.current = setTimeout(async () => {
            try {
                const result = await api.paymentSourceDocuments.matchCandidates({
                    companyId: null,
                    supplierId: doc.supplierId,
                    supplierName: doc.supplierNameSnapshot,
                    supplierTaxId: doc.supplierTaxIdSnapshot,
                    documentNumber: doc.documentNumber,
                    documentSeries: doc.documentSeries,
                    documentDate: doc.documentDate,
                    currency: doc.currency,
                    grossAmount: doc.grossAmount,
                    ocrDocumentNumber: sourceEvidence('documentNumber'),
                    ocrDocumentDate: sourceEvidence('documentDate'),
                    ocrCurrency: sourceEvidence('currency'),
                    ocrGrossAmount: sourceNumber('grossAmount')
                });
                setCandidateResults(prev => ({ ...prev, [doc.tempId]: result }));
            } catch {
                // Advisory only. The persistence guard remains the enforcement.
            }
        }, 600);

        return () => {
            if (candidateTimerRef.current) clearTimeout(candidateTimerRef.current);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [candidateKey]);
    const lockedCurrency = useMemo(() => temporaryEstablishedCurrency(documents), [documents]);
    const totals = useMemo(() => confirmedTotals(documents), [documents]);

    /**
     * The latest document array, surviving multiple updates inside ONE event.
     *
     * <p>`documents` is a prop, so two synchronous updates in the same handler both read the array
     * of the render they were created in — and the second silently discards the first. That is
     * exactly what the classification-conflict confirmation does: it stores the acknowledgement
     * (`onConflictChange`) and then commits the type (`onChange`), and the type update was erasing
     * the acknowledgement the user had just given. Every mutation goes through this ref so each one
     * builds on the previous, whatever the render timing.</p>
     */
    const documentsRef = useRef(documents);
    documentsRef.current = documents;

    const commit = (next: TemporaryPaymentDocument[]) => {
        documentsRef.current = next;
        onChange(next);
    };

    const patch = (tempId: string, changes: Partial<TemporaryPaymentDocument>) =>
        commit(documentsRef.current.map(d => (d.tempId === tempId ? { ...d, ...changes } : d)));

    // ── Adding ──────────────────────────────────────────────────────────────────────────────

    const startDocument = async (method: AddDocumentMethod) => {
        if (isAddingRef.current || disabled) return;   // a double click must not create two documents
        isAddingRef.current = true;
        setChooserOpen(false);

        try {
            const basedOn = method === 'DUPLICATE'
                ? [...documents].reverse().find(d => d.confirmed) ?? null
                : null;

            const picked = await onPickFile('NEW');
            if (!picked) return;

            // Manual entry attaches the file but does not read it: the user is typing the data
            // precisely because the document is not machine-readable, and a failed reading would
            // only add an error banner to a form they never asked to have filled.
            const readsFile = method !== 'MANUAL';

            const created: TemporaryPaymentDocument = {
                ...(basedOn ? duplicateTemporaryBasics(basedOn) : createTemporaryDocument()),
                localSequence: nextLocalSequence(documents),
                // Set BEFORE the document is rendered, so the loading view is what appears first.
                // Deriving this from "a request is in flight" would leave one render in which
                // nothing is loading and nothing has been read — the empty editor flash.
                entryMode: readsFile ? 'PENDING_OCR' : 'MANUAL',
                attachmentId: picked.id,
                attachmentFileName: picked.fileName,
                currency: basedOn?.currency ?? lockedCurrency ?? null
            };

            commit([...documentsRef.current, created]);
            onActiveChange(created.tempId);
            setShowBlockers(false);

            if (readsFile) {
                void onRunOcr(created);
                return;   // focus belongs to the review area, once the reading lands
            }

            requestAnimationFrame(() => {
                window.document
                    .querySelector<HTMLElement>(`[data-document-id="${created.tempId}"] input`)
                    ?.focus();
            });
        } finally {
            isAddingRef.current = false;
        }
    };

    /** §6: switching editors must never abandon unsaved changes silently. */
    const requestEdit = (target: TemporaryPaymentDocument) => {
        // Nothing may be opened while a document is being read: the reading is about to write into
        // that document, and moving away mid-flight is how a result lands on the wrong card.
        if (isExtracting) return;
        if (active && active.tempId !== target.tempId) { setPendingSwitch(target); return; }
        onActiveChange(target.tempId);
        setShowBlockers(false);
    };

    const requestAdd = () => {
        if (isExtracting) return;
        if (active) { setPendingSwitch(null); setShowBlockers(true); return; }
        setChooserOpen(true);
    };

    // ── Confirming ──────────────────────────────────────────────────────────────────────────

    const activeBlockers = active
        ? confirmationBlockers(active, ocrStateFor(active.tempId).isProcessing)
        : [];

    /** Reveals the pending issues and takes the user to the first field that is missing. */
    const revealBlockers = (blockers: string[]) => {
        setShowBlockers(true);
        if (!active) return;

        const field = blockers.map(blockerField).find((f): f is string => !!f);
        if (!field) return;

        requestAnimationFrame(() => {
            const scope = window.document
                .querySelector<HTMLElement>(`[data-document-id="${active.tempId}"]`);
            const target = scope?.querySelector<HTMLElement>(`[data-field="${field}"]`)
                ?? scope?.querySelector<HTMLElement>(`[data-document-${field}] input, [data-document-${field}] select`);

            target?.scrollIntoView({ behavior: 'smooth', block: 'center' });
            target?.focus();
        });
    };

    const confirmActive = () => {
        if (!active) return;

        // Re-evaluated here, not trusted from the button's disabled state. A document may only
        // enter CONFIRMED when the pure rule says so — `disabled` is a hint to the user, never the
        // guarantee, and an incomplete document that slipped through becomes a request that fails
        // halfway through being saved.
        const blockers = confirmationBlockers(active, ocrStateFor(active.tempId).isProcessing);
        if (blockers.length > 0) { revealBlockers(blockers); return; }

        patch(active.tempId, { confirmed: true });
        onActiveChange(null);
        setShowBlockers(false);
    };

    // ── Removal and replacement ─────────────────────────────────────────────────────────────

    const confirmRemoval = () => {
        const target = pendingRemoval;
        setPendingRemoval(null);
        if (!target) return;

        // Nothing is persisted yet, so removal is purely local — no void, no audit to preserve.
        // The remaining documents keep the numbers they already had.
        commit(documentsRef.current.filter(d => d.tempId !== target.tempId));
        onResetOcr(target.tempId);
        if (activeTempId === target.tempId) onActiveChange(null);
    };

    const confirmReplace = async () => {
        const target = pendingReplace;
        setPendingReplace(null);
        if (!target) return;

        const picked = await onPickFile('REPLACE');
        if (!picked) return;

        // The previous reading and decision belonged to the file being replaced. Nothing is
        // persisted yet, so the temporary decision is simply discarded — there is no audit to keep.
        onResetOcr(target.tempId);

        const replaced: TemporaryPaymentDocument = {
            ...target,
            attachmentId: picked.id,
            attachmentFileName: picked.fileName,
            classification: null,
            conflict: EMPTY_CONFLICT,
            // A document whose file changed is no longer the document that was confirmed, and the
            // previous document's fields must not stay on screen while the new file is being read.
            confirmed: false,
            entryMode: 'PENDING_OCR'
        };

        commit(documentsRef.current.map(d => (d.tempId === target.tempId ? replaced : d)));
        onActiveChange(replaced.tempId);
        void onRunOcr(replaced);
    };

    // ── Field editing ───────────────────────────────────────────────────────────────────────

    const handleFieldChange = (document: TemporaryPaymentDocument, changes: Record<string, unknown>) => {
        const nextCurrency = changes.currency as string | undefined;

        if (nextCurrency && lockedCurrency && nextCurrency !== lockedCurrency &&
            document.currency !== nextCurrency) {
            setCurrencyErrors(prev => ({
                ...prev,
                [document.tempId]: currencyConflictMessage(lockedCurrency, nextCurrency)
            }));
            return;
        }

        setCurrencyErrors(prev => ({ ...prev, [document.tempId]: null }));
        patch(document.tempId, changes as Partial<TemporaryPaymentDocument>);
    };

    const setItems = (document: TemporaryPaymentDocument, items: TemporaryPaymentItem[]) =>
        patch(document.tempId, { items });

    // ── Render ──────────────────────────────────────────────────────────────────────────────

    const lastConfirmed = [...documents].reverse().find(d => d.confirmed) ?? null;

    return (
        <section
            data-guide="request-payment-documents"
            style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}
        >
            {documents.length > 0 && (
                <h3 style={{
                    margin: 0, fontSize: '0.8rem', fontWeight: 900, letterSpacing: '0.05em',
                    textTransform: 'uppercase', color: 'var(--color-text-muted)'
                }}>
                    Documentos do pedido
                </h3>
            )}

            {/* Everything already dealt with, one line each. */}
            {others.map(d => {
                const lifecycle = documentLifecycle(
                    d, ocrStateFor(d.tempId).isProcessing, !!ocrStateFor(d.tempId).error, false);

                return (
                    <PaymentDocumentSummaryCard
                        key={d.tempId}
                        document={{
                            sequence: d.localSequence,
                            supplierName: d.supplierNameSnapshot,
                            documentNumber: d.documentNumber,
                            plantName: plants.find(p => p.id === d.plantId)?.name ?? null,
                            sourceDocumentType: d.sourceDocumentType,
                            grossAmount: d.grossAmount,
                            currency: d.currency,
                            itemCount: d.items.length
                        }}
                        lifecycle={lifecycle}
                        issues={lifecycle.state === 'CONFIRMED' ? [] : confirmationBlockers(d, false)}
                        onEdit={() => requestEdit(d)}
                        onReplaceAttachment={() => setPendingReplace(d)}
                        onRemove={() => setPendingRemoval(d)}
                        // Confirmed documents stay readable while another is read; they just
                        // cannot be acted on until that reading settles.
                        disabled={disabled || isExtracting}
                    />
                );
            })}

            {/* A document being read owns the whole document area: no fields, no item table, no
                confirm button, nothing to change until the values arrive. */}
            {active && active.entryMode === 'PENDING_OCR' && !activeOcr.error && (
                <PaymentDocumentExtractionLoading
                    fileName={active.attachmentFileName}
                    sequence={active.localSequence}
                />
            )}

            {active && active.entryMode === 'PENDING_OCR' && !!activeOcr.error && (
                <PaymentDocumentExtractionError
                    fileName={active.attachmentFileName}
                    sequence={active.localSequence}
                    message={activeOcr.error}
                    onRetry={() => void onRunOcr(active)}
                    onEnterManually={() => {
                        // An explicit change of intent: the document stops being a failed reading
                        // and becomes one the user is filling in. Nothing the failed attempt
                        // produced is carried over.
                        onResetOcr(active.tempId);
                        patch(active.tempId, {
                            entryMode: 'MANUAL',
                            classification: null,
                            conflict: EMPTY_CONFLICT
                        });
                    }}
                    onChooseAnotherFile={() => setPendingReplace(active)}
                    onRemove={() => setPendingRemoval(active)}
                />
            )}

            {/* The one document being worked on. */}
            {active && active.entryMode !== 'PENDING_OCR' && (
                <PaymentSourceDocumentCard
                    key={active.tempId}
                    variant="editor"
                    document={asCardDocument(active, active.localSequence)}
                    ocr={ocrStateFor(active.tempId)}
                    conflict={active.conflict}
                    isExpanded
                    onToggle={() => { /* the document being edited does not collapse */ }}
                    statusOverride={(() => {
                        const state = ocrStateFor(active.tempId);
                        const lifecycle = documentLifecycle(
                            active, state.isProcessing, !!state.error, true);
                        return {
                            label: lifecycle.label,
                            severity: lifecycle.severity,
                            isExtracting: lifecycle.state === 'EXTRACTING'
                        };
                    })()}
                    readOnly={disabled}
                    saveError={active.error}
                    isSaving={false}
                    currencyLocked={lockedCurrency}
                    currencyError={currencyErrors[active.tempId] ?? null}
                    onFieldChange={changes =>
                        handleFieldChange(active, changes as Record<string, unknown>)}
                    onConflictChange={(next: ClassificationConflictState) =>
                        patch(active.tempId, { conflict: next })}
                    onReplaceAttachment={() => setPendingReplace(active)}
                    onRemove={() => setPendingRemoval(active)}
                    onDuplicate={() => { /* duplication starts a NEW document, from the chooser */ }}
                    showDuplicate={false}
                    onCreateSupplier={canCreateSupplier && !disabled
                        ? (name, taxId) => setSupplierCreate({
                            tempId: active.tempId,
                            prefill: {
                                name, taxId,
                                address: active.supplierExtraction?.address,
                                paymentTerms: active.supplierExtraction?.paymentTerms,
                                contactName: active.supplierExtraction?.contactName,
                                email: active.supplierExtraction?.email,
                                phone: active.supplierExtraction?.phone,
                                bankIban: active.supplierExtraction?.bankIban,
                                bankAccountNumber: active.supplierExtraction?.bankAccountNumber,
                                bankSwift: active.supplierExtraction?.bankSwift
                            }
                          })
                        : null}
                    supplierCreateDisabledReason={canCreateSupplier ? null : SUPPLIER_CREATE_NOT_ALLOWED}
                    showValidationErrors={showBlockers}
                    plants={plants}
                    currencies={currencies}
                    footer={
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            {showBlockers && activeBlockers.length > 0 && (
                                <ul role="alert" style={{
                                    margin: 0, padding: '8px 10px 8px 28px', borderRadius: '6px',
                                    fontSize: '0.75rem', fontWeight: 600, color: '#b45309',
                                    border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.06)'
                                }}>
                                    {activeBlockers.map(b => <li key={b}>{b}</li>)}
                                </ul>
                            )}

                            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', alignItems: 'center' }}>
                                <button
                                    type="button"
                                    onClick={confirmActive}
                                    disabled={disabled || activeBlockers.length > 0}
                                    onMouseEnter={() => {
                                        if (activeBlockers.length > 0) setShowBlockers(true);
                                    }}
                                    style={{
                                        display: 'inline-flex', alignItems: 'center', gap: '6px',
                                        padding: '10px 18px', borderRadius: '8px', border: 'none',
                                        backgroundColor: activeBlockers.length > 0
                                            ? 'var(--color-border)' : 'var(--color-primary)',
                                        color: activeBlockers.length > 0
                                            ? 'var(--color-text-muted)' : '#fff',
                                        fontWeight: 800, fontSize: '0.82rem',
                                        cursor: activeBlockers.length > 0 ? 'not-allowed' : 'pointer'
                                    }}
                                >
                                    <CheckCircle2 size={15} /> Confirmar e adicionar documento
                                </button>

                                {activeBlockers.length > 0 && (
                                    <button
                                        type="button"
                                        onClick={() => revealBlockers(activeBlockers)}
                                        style={{
                                            display: 'inline-flex', alignItems: 'center', gap: '5px',
                                            background: 'none', border: 'none', cursor: 'pointer',
                                            color: '#b45309', fontWeight: 700, fontSize: '0.75rem'
                                        }}
                                    >
                                        <AlertTriangle size={13} />
                                        {activeBlockers.length === 1
                                            ? '1 pendência neste documento'
                                            : `${activeBlockers.length} pendências neste documento`}
                                    </button>
                                )}
                            </div>
                        </div>
                    }
                >
                    {/* This document's own lines, inside this document's editor. */}
                    <PaymentDocumentItemsEditor
                        items={active.items}
                        onChange={items => setItems(active, items)}
                        units={units}
                        ivaRates={ivaRates}
                        currency={active.currency}
                        documentTotal={active.grossAmount}
                        readOnly={disabled}
                    />

                    {/* Manual entry never reads the file, so the option has to stay reachable —
                        otherwise choosing "inserir manualmente" is a one-way door. */}
                    {!disabled && !!active.attachmentId && !activeOcr.isProcessing && (
                        <button
                            type="button"
                            onClick={() => {
                                // A re-read blocks the same way a first read does — the values are
                                // about to change, so the form must not be editable meanwhile.
                                patch(active.tempId, { entryMode: 'PENDING_OCR' });
                                void onRunOcr(active);
                            }}
                            style={retryButton}
                        >
                            {activeOcr.error
                                ? 'Tentar ler o documento novamente'
                                : active.classification
                                    ? 'Ler o documento novamente'
                                    : 'Ler este documento com OCR'}
                        </button>
                    )}

                    {/* A disagreement is reported, never silently applied — the user may have
                        corrected a misread number by hand. */}
                    {discrepanciesFor(active.tempId).length > 0 && (
                        <div style={{
                            padding: '8px 10px', borderRadius: '6px', fontSize: '0.75rem',
                            border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.06)',
                            color: '#b45309'
                        }}>
                            <strong>A leitura difere do que introduziu:</strong>
                            <ul style={{ margin: '4px 0 0', paddingLeft: '16px' }}>
                                {discrepanciesFor(active.tempId).map(x => (
                                    <li key={x.field}>
                                        {x.label}: manteve <strong>{x.userValue}</strong>,
                                        o documento indica <strong>{x.extractedValue}</strong>.
                                    </li>
                                ))}
                            </ul>
                        </div>
                    )}

                    {/* A probable EXISTING supplier the matcher found (same name, different NIF).
                        Reviewed, never auto-applied — and "Criar fornecedor" stays available for
                        the genuinely-new case. */}
                    {active.probableSupplier && !active.supplierId && (
                        <div style={{
                            padding: '10px 12px', borderRadius: '6px', fontSize: '0.78rem',
                            border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.06)',
                            color: 'var(--color-text-main)',
                            display: 'flex', flexDirection: 'column', gap: '8px'
                        }}>
                            <strong style={{ color: '#b45309' }}>
                                Possível fornecedor existente
                            </strong>
                            <span>
                                <strong>{active.probableSupplier.name}</strong> já existe no Portal,
                                mas o NIF lido no documento difere do NIF registado.
                            </span>
                            <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                Documento: <strong>{active.supplierTaxIdSnapshot ?? 'NIF não lido pelo OCR'}</strong>
                                {' '}· Fornecedor registado: <strong>{active.probableSupplier.taxId ?? '—'}</strong>
                            </span>
                            <div>
                                <button
                                    type="button"
                                    onClick={() => patch(active.tempId, {
                                        supplierId: active.probableSupplier!.id,
                                        supplierNameSnapshot: active.probableSupplier!.name
                                        // The documentary NIF snapshot stays as read — it is
                                        // evidence about the document, not master data.
                                    })}
                                    style={{
                                        padding: '6px 12px', borderRadius: '6px', cursor: 'pointer',
                                        border: '1px solid var(--color-primary)', backgroundColor: 'transparent',
                                        color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.75rem'
                                    }}
                                >
                                    Usar fornecedor existente
                                </button>
                            </div>
                        </div>
                    )}

                    {/* Customer-NIF discrepancy: display-only evidence (nothing is persisted). */}
                    {(() => {
                        const norm = (v?: string | null) =>
                            (v ?? '').replace(/[^0-9A-Za-z]/g, '').toUpperCase();
                        return selectedCompanyTaxId && active.billedCompanyTaxId &&
                            norm(selectedCompanyTaxId) !== norm(active.billedCompanyTaxId) ? (
                            <p style={{
                                margin: 0, padding: '8px 10px', borderRadius: '6px', fontSize: '0.75rem',
                                fontWeight: 600, color: '#b45309',
                                border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.06)'
                            }}>
                                O NIF do cliente lido no documento
                                ({active.billedCompanyTaxId}) não corresponde ao NIF da empresa
                                selecionada ({selectedCompanyTaxId}). Verifique se o documento
                                pertence a esta empresa.
                            </p>
                        ) : null;
                    })()}

                    {/* Candidate matching (L4 flow): why the Portal thinks this document may
                        already exist — or is merely related. Conflicting fields highlighted. */}
                    {(() => {
                        const result = candidateResults[active.tempId];
                        const top = result?.candidates?.[0];
                        if (!top) return null;

                        const strong = top.classification === 'STRONG_BUSINESS_DUPLICATE'
                            || top.classification === 'SEMANTIC_DUPLICATE';
                        const related = top.classification === 'RELATED_DOCUMENT';
                        const border = strong ? '#fca5a5' : related ? 'var(--color-border)' : '#fcd34d';
                        const bg = strong ? 'rgba(185,28,28,0.06)'
                            : related ? 'var(--color-bg-page)' : 'rgba(180,83,9,0.06)';
                        const titleColor = strong ? '#b91c1c' : related ? 'var(--color-text-muted)' : '#b45309';
                        const title = strong
                            ? 'Provável documento duplicado'
                            : related ? 'Documento relacionado encontrado' : 'Possível documento correspondente';

                        // The "uploaded" column shows what the DOCUMENT says (source evidence
                        // preferred over the accepted draft value) — the same values the
                        // classification was computed from. The "manteve X / o documento indica Y"
                        // panel above explains any divergence from the draft.
                        const evGross = sourceNumber('grossAmount');
                        const uploadedValues: Record<string, string> = {
                            SUPPLIER: active.supplierNameSnapshot ?? '—',
                            SUPPLIER_NAME: active.supplierNameSnapshot ?? '—',
                            SUPPLIER_NIF: active.supplierTaxIdSnapshot ?? '—',
                            DOCUMENT_NUMBER: sourceEvidence('documentNumber') ?? active.documentNumber ?? '—',
                            DOCUMENT_DATE: sourceEvidence('documentDate') ?? active.documentDate ?? '—',
                            CURRENCY: sourceEvidence('currency') ?? active.currency ?? '—',
                            GROSS_AMOUNT: evGross != null
                                ? formatCurrencyAO(evGross)
                                : active.grossAmount != null ? formatCurrencyAO(active.grossAmount) : '—'
                        };
                        const existingValues: Record<string, string> = {
                            SUPPLIER: top.existing?.supplierName ?? '—',
                            SUPPLIER_NAME: top.existing?.supplierName ?? '—',
                            SUPPLIER_NIF: top.existing?.supplierTaxId ?? '—',
                            DOCUMENT_NUMBER: top.existing?.documentNumber ?? '—',
                            DOCUMENT_DATE: top.existing?.documentDate?.substring(0, 10) ?? '—',
                            CURRENCY: top.existing?.currency ?? '—',
                            GROSS_AMOUNT: top.existing?.grossAmount != null
                                ? formatCurrencyAO(top.existing.grossAmount) : '—'
                        };
                        const rows = ['SUPPLIER', 'SUPPLIER_NIF', 'DOCUMENT_NUMBER',
                            'DOCUMENT_DATE', 'CURRENCY', 'GROSS_AMOUNT'];

                        return (
                            <div style={{
                                padding: '10px 12px', borderRadius: '6px', fontSize: '0.78rem',
                                border: `1px solid ${border}`, backgroundColor: bg,
                                display: 'flex', flexDirection: 'column', gap: '8px'
                            }}>
                                <strong style={{ color: titleColor }}>{title}</strong>
                                <span style={{ color: 'var(--color-text-main)' }}>
                                    {top.requestNumber
                                        ? <>Documento {top.sequenceNumber} do pedido <strong>{top.requestNumber}</strong></>
                                        : 'Um documento de outro pedido (fora do seu âmbito de acesso)'}
                                    {top.reason ? <> — {top.reason}.</> : '.'}
                                </span>
                                {top.existing && (
                                    <div style={{ overflowX: 'auto' }}>
                                        <table style={{ borderCollapse: 'collapse', fontSize: '0.75rem', minWidth: '380px' }}>
                                            <thead>
                                                <tr style={{ color: 'var(--color-text-muted)', fontWeight: 700 }}>
                                                    <td style={{ padding: '2px 10px 2px 0' }} />
                                                    <td style={{ padding: '2px 10px' }}>Documento existente</td>
                                                    <td style={{ padding: '2px 10px' }}>Documento carregado</td>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {rows.map(f => {
                                                    const conflict = top.conflictingFields.includes(f)
                                                        || (f === 'SUPPLIER' && top.conflictingFields.includes('SUPPLIER_NIF'));
                                                    return (
                                                        <tr key={f} style={conflict
                                                            ? { color: '#b91c1c', fontWeight: 700 }
                                                            : undefined}>
                                                            <td style={{ padding: '2px 10px 2px 0', fontWeight: 700 }}>
                                                                {CANDIDATE_FIELD_LABELS[f] ?? f}
                                                            </td>
                                                            <td style={{ padding: '2px 10px' }}>{existingValues[f]}</td>
                                                            <td style={{ padding: '2px 10px' }}>{uploadedValues[f]}</td>
                                                        </tr>
                                                    );
                                                })}
                                            </tbody>
                                        </table>
                                    </div>
                                )}
                                {!related && (
                                    <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        Ao gerar o pedido, será pedida uma confirmação justificada
                                        caso mantenha este documento.
                                    </span>
                                )}
                                {result!.candidates.length > 1 && (
                                    <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        +{result!.candidates.length - 1} outro(s) documento(s) com a mesma referência.
                                    </span>
                                )}
                            </div>
                        );
                    })()}
                </PaymentSourceDocumentCard>
            )}

            {/* The first document IS the screen; every one after it is an explicit decision. */}
            {!active && documents.length === 0 && !disabled && (
                <AddPaymentDocumentChoice
                    variant="panel"
                    sequence={1}
                    onChoose={method => void startDocument(method)}
                />
            )}

            {!active && documents.length > 0 && !disabled && !isExtracting && (
                <button
                    type="button"
                    onClick={requestAdd}
                    style={{
                        display: 'inline-flex', alignItems: 'center', gap: '6px', alignSelf: 'flex-start',
                        padding: '8px 14px', borderRadius: '8px', border: '1px solid var(--color-primary)',
                        cursor: 'pointer', backgroundColor: 'transparent',
                        color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.8rem'
                    }}
                >
                    <Plus size={14} /> Adicionar outro documento
                </button>
            )}

            {chooserOpen && (
                <AddPaymentDocumentChoice
                    variant="modal"
                    sequence={nextLocalSequence(documents)}
                    duplicateFrom={lastConfirmed?.localSequence ?? null}
                    onChoose={method => void startDocument(method)}
                    onCancel={() => setChooserOpen(false)}
                />
            )}

            <PaymentDocumentsSummary
                totals={totals}
                // Nothing provisional to report while a document is still being read — its values
                // do not exist yet, and "0,00" would be one of them.
                provisional={active && !isExtracting ? {
                    sequence: active.localSequence,
                    gross: active.grossAmount ?? 0,
                    currency: active.currency
                } : null}
            />

            {pendingRemoval && (
                <ConfirmationDialog
                    title={`Remover o Documento ${pendingRemoval.localSequence}?`}
                    message={
                        'O documento e os seus dados serão descartados. Como o pedido ainda não foi ' +
                        'criado, nada é gravado no servidor. Os restantes documentos mantêm a ' +
                        'numeração que já têm.'
                    }
                    confirmText="Remover"
                    cancelText="Manter"
                    variant="destructive"
                    onConfirm={confirmRemoval}
                    onCancel={() => setPendingRemoval(null)}
                />
            )}

            {pendingReplace && (
                <ConfirmationDialog
                    title="Substituir o anexo?"
                    message={
                        'A leitura de OCR e a decisão de classificação atuais pertencem ao ficheiro que ' +
                        'vai substituir e serão descartadas. O documento volta a "Em revisão" e terá de ' +
                        'ser confirmado outra vez.'
                    }
                    confirmText="Substituir anexo"
                    cancelText="Cancelar"
                    variant="warning"
                    onConfirm={() => void confirmReplace()}
                    onCancel={() => setPendingReplace(null)}
                />
            )}

            {/* The SAME quick-create Quotation Management uses, in its PAYMENT_OCR mode: the
                supplier lands in the one authoritative supplier source, immediately searchable
                everywhere. Nothing here is payment-specific except which document gets the id. */}
            <PaymentSupplierCreateModal
                isOpen={!!supplierCreate}
                onClose={() => setSupplierCreate(null)}
                prefill={supplierCreate?.prefill ?? {}}
                onCreated={supplier => {
                    const target = supplierCreate?.tempId;
                    if (!target) return;

                    // Applied by tempId to the document that asked, and to no other. The extracted
                    // NIF snapshot is kept: it is the evidence the document itself carried, and the
                    // registered supplier's own record is a separate fact.
                    patch(target, {
                        supplierId: supplier.id,
                        supplierNameSnapshot: supplier.name
                    });
                }}
            />

            {pendingSwitch && active && (
                <ConfirmationDialog
                    title={`Documento ${active.localSequence} está em revisão`}
                    message={
                        `Confirme o Documento ${active.localSequence} antes de abrir o Documento ` +
                        `${pendingSwitch.localSequence}, ou descarte as alterações em curso.`
                    }
                    confirmText="Confirmar e mudar"
                    cancelText="Continuar a editar"
                    variant="warning"
                    onConfirm={() => {
                        const target = pendingSwitch;
                        setPendingSwitch(null);
                        if (activeBlockers.length > 0) { setShowBlockers(true); return; }
                        patch(active.tempId, { confirmed: true });
                        onActiveChange(target.tempId);
                    }}
                    onCancel={() => setPendingSwitch(null)}
                />
            )}
        </section>
    );
}

const retryButton: React.CSSProperties = {
    alignSelf: 'flex-start', padding: '6px 12px', borderRadius: '6px', cursor: 'pointer',
    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
    color: 'var(--color-text-main)', fontWeight: 600, fontSize: '0.75rem'
};
