import React, { useMemo, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, Plus } from 'lucide-react';
import { PaymentSourceDocumentCard } from './PaymentSourceDocumentCard';
import { PaymentDocumentSummaryCard } from './PaymentDocumentSummaryCard';
import { PaymentDocumentsSummary } from './PaymentDocumentsSummary';
import { PaymentDocumentItemsEditor } from './PaymentDocumentItemsEditor';
import { AddPaymentDocumentChoice, AddDocumentMethod } from './AddPaymentDocumentChoice';
import { ConfirmationDialog } from '../common/ConfirmationDialog';
import { currencyConflictMessage } from '../../lib/paymentSourceDocuments';
import { ClassificationConflictState, EMPTY_CONFLICT } from '../../lib/documentClassificationDecision';
import { PaymentDocumentOcrState } from '../../types/paymentSourceDocument';
import { IvaRate, Unit } from '../../types';
import {
    ExtractionDiscrepancy,
    TemporaryPaymentDocument,
    TemporaryPaymentItem,
    asCardDocument,
    createTemporaryDocument,
    duplicateTemporaryBasics,
    temporaryEstablishedCurrency
} from '../../lib/paymentRequestCreation';
import {
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

    ocrStateFor: (tempId: string) => PaymentDocumentOcrState;
    discrepanciesFor: (tempId: string) => ExtractionDiscrepancy[];
    onRunOcr: (document: TemporaryPaymentDocument) => Promise<void>;
    onResetOcr: (tempId: string) => void;

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
    ocrStateFor,
    discrepanciesFor,
    onRunOcr,
    onResetOcr,
    disabled = false
}: Props) {
    const [chooserOpen, setChooserOpen] = useState(false);
    const [pendingRemoval, setPendingRemoval] = useState<TemporaryPaymentDocument | null>(null);
    const [pendingReplace, setPendingReplace] = useState<TemporaryPaymentDocument | null>(null);
    const [pendingSwitch, setPendingSwitch] = useState<TemporaryPaymentDocument | null>(null);
    const [currencyErrors, setCurrencyErrors] = useState<Record<string, string | null>>({});
    const [showBlockers, setShowBlockers] = useState(false);
    const isAddingRef = useRef(false);

    const active = documents.find(d => d.tempId === activeTempId) ?? null;
    const others = documents.filter(d => d.tempId !== activeTempId);
    const lockedCurrency = useMemo(() => temporaryEstablishedCurrency(documents), [documents]);
    const totals = useMemo(() => confirmedTotals(documents), [documents]);

    const patch = (tempId: string, changes: Partial<TemporaryPaymentDocument>) =>
        onChange(documents.map(d => (d.tempId === tempId ? { ...d, ...changes } : d)));

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

            const created: TemporaryPaymentDocument = {
                ...(basedOn ? duplicateTemporaryBasics(basedOn) : createTemporaryDocument()),
                localSequence: nextLocalSequence(documents),
                attachmentId: picked.id,
                attachmentFileName: picked.fileName,
                currency: basedOn?.currency ?? lockedCurrency ?? null
            };

            onChange([...documents, created]);
            onActiveChange(created.tempId);
            setShowBlockers(false);

            // Manual entry attaches the file but does not read it: the user is typing the data
            // precisely because the document is not machine-readable, and a failed reading would
            // only add an error banner to a form they never asked to have filled.
            if (method !== 'MANUAL') void onRunOcr(created);

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
        if (active && active.tempId !== target.tempId) { setPendingSwitch(target); return; }
        onActiveChange(target.tempId);
        setShowBlockers(false);
    };

    const requestAdd = () => {
        if (active) { setPendingSwitch(null); setShowBlockers(true); return; }
        setChooserOpen(true);
    };

    // ── Confirming ──────────────────────────────────────────────────────────────────────────

    const activeBlockers = active
        ? confirmationBlockers(active, ocrStateFor(active.tempId).isProcessing)
        : [];

    const confirmActive = () => {
        if (!active) return;
        if (activeBlockers.length > 0) { setShowBlockers(true); return; }

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
        onChange(documents.filter(d => d.tempId !== target.tempId));
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
            // A document whose file changed is no longer the document that was confirmed.
            confirmed: false
        };

        onChange(documents.map(d => (d.tempId === target.tempId ? replaced : d)));
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
                        disabled={disabled}
                    />
                );
            })}

            {/* The one document being worked on. */}
            {active && (
                <PaymentSourceDocumentCard
                    key={active.tempId}
                    variant="editor"
                    document={asCardDocument(active, active.localSequence)}
                    ocr={ocrStateFor(active.tempId)}
                    conflict={active.conflict}
                    isExpanded
                    onToggle={() => { /* the document being edited does not collapse */ }}
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

                                {activeBlockers.length > 0 && !showBlockers && (
                                    <button
                                        type="button"
                                        onClick={() => setShowBlockers(true)}
                                        style={{
                                            display: 'inline-flex', alignItems: 'center', gap: '5px',
                                            background: 'none', border: 'none', cursor: 'pointer',
                                            color: '#b45309', fontWeight: 700, fontSize: '0.75rem'
                                        }}
                                    >
                                        <AlertTriangle size={13} />
                                        {activeBlockers.length} pendência(s) neste documento
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
                    {!disabled && !!active.attachmentId &&
                     !ocrStateFor(active.tempId).isProcessing && (
                        <button type="button" onClick={() => void onRunOcr(active)} style={retryButton}>
                            {ocrStateFor(active.tempId).error
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

            {!active && documents.length > 0 && !disabled && (
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
                provisional={active ? {
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
