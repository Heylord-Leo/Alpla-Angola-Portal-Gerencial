import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle, Info, Plus } from 'lucide-react';
import { api } from '../../lib/api';
import {
    PaymentDocumentOcrState,
    PaymentSourceDocumentConflictDto,
    PaymentSourceDocumentDto,
    PaymentSourceDocumentsSummaryDto
} from '../../types/paymentSourceDocument';
import {
    ClassificationConflictState,
    EMPTY_CONFLICT
} from '../../lib/documentClassificationDecision';
import {
    collectValidationIssues,
    computeTotals,
    currencyConflictMessage,
    deriveCardStatus,
    duplicateBasicData,
    establishedCurrency,
    parseStoredClassification,
    toSavePayload,
    ValidationIssue
} from '../../lib/paymentSourceDocuments';
import { PaymentSourceDocumentCard } from './PaymentSourceDocumentCard';
import { DuplicateOverrideDialog } from './DuplicateOverrideDialog';
import { PaymentDocumentSummaryCard } from './PaymentDocumentSummaryCard';
import { AddPaymentDocumentChoice } from './AddPaymentDocumentChoice';
import { InfoModal } from '../ui/InfoModal';
import { FieldMessageIcon } from '../ui/FieldMessageIcon';
import { ConfirmationDialog } from '../common/ConfirmationDialog';
import { PaymentSupplierCreateModal } from './PaymentSupplierCreateModal';
import { SUPPLIER_CREATE_NOT_ALLOWED } from '../../lib/supplierQuickCreate';
import { formatCurrencyAO } from '../../lib/utils';

interface Props {
    requestId: string;
    /** DRAFT / AREA_ADJUSTMENT / FINAL_ADJUSTMENT enable editing; anything else is read-only. */
    readOnly?: boolean;
    plants: Array<{ id: number; name: string }>;
    currencies: Array<{ code: string; name: string }>;
    /** Mirrors the backend rule for `POST /lookups/suppliers/from-payment-ocr`. */
    canCreateSupplier?: boolean;
    /** Bubbles `canSubmit` so the parent can disable final submission. */
    onSummaryChange?: (summary: PaymentSourceDocumentsSummaryDto | null) => void;
    /** Opens the existing attachment picker; resolves with the new attachment id, or null. */
    onRequestAttachment?: (purpose: 'NEW' | 'REPLACE', documentId?: string) => Promise<string | null>;
    /** Renders the items belonging to one document. */
    renderItems?: (document: PaymentSourceDocumentDto) => React.ReactNode;
    /**
     * Bubbles whether a document is mid-edit, so submission can refuse rather than send an approver
     * a request whose contents were still being typed.
     */
    onEditingStateChange?: (state: { openSequence: number | null; unsavedSequences: number[] }) => void;
}

/** Everything held per document. Keyed by document id — never by array index. */
interface DocumentUiState {
    ocr: PaymentDocumentOcrState;
    conflict: ClassificationConflictState;
    saveError: string | null;
    isSaving: boolean;
    currencyError: string | null;
}

const EMPTY_UI: DocumentUiState = {
    ocr: { isProcessing: false, classification: null, error: null },
    conflict: EMPTY_CONFLICT,
    saveError: null,
    isSaving: false,
    currencyError: null
};

/**
 * The documents that originate a PAYMENT request.
 *
 * <p><b>State is a map keyed by document id.</b> Not an array of state, not an index — a removal
 * would silently reassign every subsequent document's OCR result and classification decision to the
 * wrong file. Nothing about a document's reading is held at request level.</p>
 *
 * <p>Cards are collapsed by default with one expanded, because a three-invoice request rendered as
 * one continuous form is unusable — and the collapsed header is built to answer "which one is still
 * missing something" without opening anything.</p>
 */
export function PaymentSourceDocumentCollection({
    requestId,
    readOnly = false,
    plants,
    currencies,
    canCreateSupplier = false,
    onSummaryChange,
    onRequestAttachment,
    renderItems,
    onEditingStateChange
}: Props) {
    const [summary, setSummary] = useState<PaymentSourceDocumentsSummaryDto | null>(null);
    const [documents, setDocuments] = useState<PaymentSourceDocumentDto[]>([]);
    const [uiState, setUiState] = useState<Record<string, DocumentUiState>>({});
    const [expandedId, setExpandedId] = useState<string | null>(null);
    const [chooserOpen, setChooserOpen] = useState(false);
    /** Documents edited but not yet saved. A pending edit is not part of the request until it is. */
    const [dirtyIds, setDirtyIds] = useState<string[]>([]);
    /** Supplier quick-create, scoped to the document that asked for it. */
    const [supplierCreate, setSupplierCreate] =
        useState<{ documentId: string; name: string; taxId: string } | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);
    /** A refresh with data already on screen. Distinct from the first load, which has none. */
    const [isRefreshing, setIsRefreshing] = useState(false);

    const [pendingRemoval, setPendingRemoval] = useState<PaymentSourceDocumentDto | null>(null);
    const [pendingReplace, setPendingReplace] = useState<PaymentSourceDocumentDto | null>(null);
    const [conflictDialog, setConflictDialog] = useState<PaymentSourceDocumentConflictDto | null>(null);
    /**
     * Duplicate hierarchy LEVEL 4: the backend refused the save because another document shares
     * this supplier reference and the content evidence cannot decide. Resolved by an explicit,
     * justified confirmation — never by asking the user to falsify the supplier's real reference.
     */
    const [duplicateOverride, setDuplicateOverride] =
        useState<{ documentId: string; detail: string; classification: string | null } | null>(null);
    const [showIssues, setShowIssues] = useState(false);

    /** Guards the Add button against a double click creating two documents. */
    const isAddingRef = useRef(false);
    const [isAdding, setIsAdding] = useState(false);

    const effectiveReadOnly = readOnly || summary?.canEditDocuments === false;

    // ── Load ────────────────────────────────────────────────────────────────────────────

    /**
     * The parent's callbacks, held in refs.
     *
     * <p>They must not take part in the identity of anything the load effect depends on. A parent
     * that passes an inline arrow — which is the natural thing to write — produces a new function
     * every render; if that identity reaches `reload`, the effect re-arms, fetches, sets state,
     * re-renders, and fetches again. That is not a hypothetical: it is exactly how this screen
     * ended up issuing dozens of concurrent GETs to `/source-documents`.</p>
     *
     * <p>Refs let the newest callback be used without ever being a dependency.</p>
     */
    const onSummaryChangeRef = useRef(onSummaryChange);
    onSummaryChangeRef.current = onSummaryChange;

    /** Only the newest load for the current request may write. */
    const loadTokenRef = useRef(0);
    const abortRef = useRef<AbortController | null>(null);

    const applySummary = useCallback((next: PaymentSourceDocumentsSummaryDto) => {
        setSummary(next);
        setDocuments(next.documents);

        // Seed per-document UI from what was persisted, without disturbing documents already open.
        setUiState(prev => {
            const merged: Record<string, DocumentUiState> = {};
            for (const d of next.documents) {
                const existing = prev[d.id];
                merged[d.id] = existing ?? {
                    ...EMPTY_UI,
                    ocr: {
                        isProcessing: false,
                        classification: parseStoredClassification(d),
                        error: null
                    },
                    conflict: {
                        hasConflict: d.classificationConflictAcknowledged,
                        isHighRisk: false,
                        acknowledged: d.classificationConflictAcknowledged,
                        justification: d.classificationJustification ?? ''
                    }
                };
            }
            return merged;
        });

        onSummaryChangeRef.current?.(next);
        // Depends on nothing: every value it touches is a setter or a ref, all stable for the life
        // of the component. That is what keeps `reload` stable too.
    }, []);

    /**
     * Fetches the summary for the current request.
     *
     * <p>Depends only on <c>requestId</c> — a scalar. Rendering, resizing, opening a modal or
     * editing a header field cannot change it, so none of them can cause a fetch.</p>
     *
     * <p>Existing data is kept on screen while a refresh is in flight. Clearing it first is what
     * made "nenhum documento" flash between responses; a refresh that has not answered yet is not
     * evidence that the documents are gone.</p>
     */
    const reload = useCallback(async () => {
        const token = ++loadTokenRef.current;

        abortRef.current?.abort();
        const controller = new AbortController();
        abortRef.current = controller;

        setIsRefreshing(true);

        try {
            const next = await api.requests.getSourceDocuments(requestId);

            // A response for a request we have moved on from, or one superseded by a newer load,
            // must not overwrite what is on screen.
            if (token !== loadTokenRef.current) return;

            applySummary(next);
            setLoadError(null);
        } catch {
            if (token !== loadTokenRef.current) return;
            setLoadError('Não foi possível carregar os documentos do pedido. Recarregue a página.');
        } finally {
            if (token === loadTokenRef.current) setIsRefreshing(false);
        }
    }, [requestId, applySummary]);

    useEffect(() => {
        void reload();
        return () => {
            // Nothing in flight may write after we leave this request.
            loadTokenRef.current++;
            abortRef.current?.abort();
        };
        // Deliberately keyed to the request, not to `reload`: see the note on the refs above.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [requestId]);

    // ── Per-document helpers ────────────────────────────────────────────────────────────

    const patchUi = (id: string, patch: Partial<DocumentUiState>) =>
        setUiState(prev => ({ ...prev, [id]: { ...(prev[id] ?? EMPTY_UI), ...patch } }));

    const patchDocument = (id: string, patch: Partial<PaymentSourceDocumentDto>) =>
        setDocuments(prev => prev.map(d => (d.id === id ? { ...d, ...patch } : d)));

    const lockedCurrency = useMemo(() => establishedCurrency(documents), [documents]);

    /** Persists one document. Only this card shows its own saving and error state. */
    const save = async (document: PaymentSourceDocumentDto, duplicateReason?: string) => {
        const ui = uiState[document.id] ?? EMPTY_UI;
        patchUi(document.id, { isSaving: true, saveError: null });

        try {
            const payload = toSavePayload(
                document, ui.ocr.classification, ui.conflict.acknowledged, ui.conflict.justification);
            if (duplicateReason) {
                payload.duplicateOverrideAcknowledged = true;
                payload.duplicateOverrideReason = duplicateReason;
            }

            const result = await api.requests.updateSourceDocument(requestId, document.id, payload);

            if ('status' in result && result.status === 409) {
                const code = (result as any).code as string | undefined;

                // LEVEL 4 — ambiguous duplicate: openable, justified override.
                if (code === 'DUPLICATE_AMBIGUOUS') {
                    setDuplicateOverride({
                        documentId: document.id,
                        detail: (result as any).detail ?? 'Outro documento partilha esta referência.',
                        classification: (result as any).classification ?? null
                    });
                    patchUi(document.id, { isSaving: false });
                    return;
                }

                // LEVEL 1/2 — proven duplicate: a hard refusal shown on the card, not a
                // concurrency situation to reload out of.
                if (code === 'DUPLICATE_SEMANTIC' || code === 'DUPLICATE_FILE_CROSS_REQUEST') {
                    patchUi(document.id, {
                        isSaving: false,
                        saveError: (result as any).detail ?? 'Documento duplicado.'
                    });
                    return;
                }

                setConflictDialog(result as PaymentSourceDocumentConflictDto);
                patchUi(document.id, { isSaving: false });
                return;
            }

            // Reconcile against the backend immediately — it decides totals and validation.
            await reload();
            setDirtyIds(prev => prev.filter(x => x !== document.id));
            patchUi(document.id, { isSaving: false, saveError: null });
        } catch (e: any) {
            patchUi(document.id, {
                isSaving: false,
                saveError: e?.message ?? 'Não foi possível guardar este documento.'
            });
        }
    };

    const handleFieldChange = (document: PaymentSourceDocumentDto, patch: Partial<PaymentSourceDocumentDto>) => {
        // One request, one currency — refused here so the user is not walked into a backend error.
        if (patch.currency && lockedCurrency && patch.currency !== lockedCurrency &&
            document.currency !== patch.currency) {
            patchUi(document.id, { currencyError: currencyConflictMessage(lockedCurrency, patch.currency) });
            return;
        }

        patchUi(document.id, { currencyError: null });
        patchDocument(document.id, patch);
        setDirtyIds(prev => (prev.includes(document.id) ? prev : [...prev, document.id]));
    };

    // Whether anything is mid-edit is the parent's business: it is what stands between a document
    // being typed and an approver receiving it.
    useEffect(() => {
        onEditingStateChange?.({
            openSequence: documents.find(d => d.id === expandedId)?.sequenceNumber ?? null,
            unsavedSequences: dirtyIds
                .map(id => documents.find(d => d.id === id)?.sequenceNumber)
                .filter((s): s is number => s != null)
        });
    }, [expandedId, dirtyIds, documents, onEditingStateChange]);

    // ── Add ─────────────────────────────────────────────────────────────────────────────

    const addDocument = async (basedOn?: PaymentSourceDocumentDto) => {
        if (isAddingRef.current) return;   // a double click must not create two documents
        isAddingRef.current = true;
        setIsAdding(true);

        try {
            const attachmentId = await onRequestAttachment?.('NEW');
            if (!attachmentId) return;

            const created = await api.requests.createSourceDocument(requestId, {
                ...(basedOn ? duplicateBasicData(basedOn) : {}),
                attachmentId,
                currency: basedOn?.currency ?? lockedCurrency ?? null
            });

            await reload();
            setExpandedId(created.id);

            // Move focus to the new card rather than leaving it at the bottom of the page.
            requestAnimationFrame(() => {
                document
                    .querySelector<HTMLElement>(`[data-document-id="${created.id}"] button`)
                    ?.focus();
            });
        } catch (e: any) {
            setLoadError(e?.message ?? 'Não foi possível adicionar o documento.');
        } finally {
            isAddingRef.current = false;
            setIsAdding(false);
        }
    };

    // ── Remove / replace ────────────────────────────────────────────────────────────────

    const confirmRemoval = async () => {
        const target = pendingRemoval;
        setPendingRemoval(null);
        if (!target) return;

        try {
            const result = await api.requests.removeSourceDocument(requestId, target.id, {
                rowVersion: target.rowVersion ?? null
            });

            if ('status' in result && (result as any).status === 409) {
                setConflictDialog(result as PaymentSourceDocumentConflictDto);
                return;
            }

            applySummary(result as PaymentSourceDocumentsSummaryDto);
            if (expandedId === target.id) setExpandedId(null);
        } catch (e: any) {
            patchUi(target.id, { saveError: e?.message ?? 'Não foi possível remover o documento.' });
        }
    };

    const confirmReplace = async () => {
        const target = pendingReplace;
        setPendingReplace(null);
        if (!target) return;

        // Named so the preflight can exclude it: a document cannot duplicate itself.
        const attachmentId = await onRequestAttachment?.('REPLACE', target.id);
        if (!attachmentId) return;

        patchUi(target.id, { isSaving: true, saveError: null });

        try {
            const result = await api.requests.updateSourceDocument(requestId, target.id, {
                attachmentId,
                rowVersion: target.rowVersion ?? null
            });

            if ('status' in result && (result as any).status === 409) {
                setConflictDialog(result as PaymentSourceDocumentConflictDto);
                return;
            }

            // The previous reading and decision belonged to the file that was replaced. The backend
            // has already discarded them; the UI must not keep showing them.
            patchUi(target.id, {
                ocr: { isProcessing: false, classification: null, error: null },
                conflict: EMPTY_CONFLICT,
                isSaving: false
            });

            await reload();
        } catch (e: any) {
            patchUi(target.id, { isSaving: false, saveError: e?.message ?? 'Não foi possível substituir o anexo.' });
        }
    };

    // ── Derived ─────────────────────────────────────────────────────────────────────────

    const totals = useMemo(() => computeTotals(documents), [documents]);
    const issues = useMemo(() => collectValidationIssues(summary), [summary]);

    const focusIssue = (issue: ValidationIssue) => {
        if (!issue.documentId) return;
        setExpandedId(issue.documentId);
        setShowIssues(false);
        requestAnimationFrame(() => {
            const card = document.querySelector<HTMLElement>(`[data-document-id="${issue.documentId}"]`);
            card?.scrollIntoView({ behavior: 'smooth', block: 'center' });
            card?.querySelector<HTMLElement>('button')?.focus();
        });
    };

    // ── Render ──────────────────────────────────────────────────────────────────────────

    return (
        <section style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            <header style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                <h3 style={{
                    margin: 0, fontSize: '0.8rem', fontWeight: 900, letterSpacing: '0.05em',
                    textTransform: 'uppercase', color: 'var(--color-text-muted)'
                }}>
                    Documentos do pedido
                </h3>

                {summary?.mixedTypeNotice && (
                    <FieldMessageIcon
                        severity="info"
                        tooltip="Documentos do mesmo fornecedor com tipos diferentes."
                        title="Tipos de documento diferentes"
                        maxWidth={560}
                    >
                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-main)' }}>
                            {summary.mixedTypeNotice}
                        </p>
                        <p style={{ margin: '10px 0 0', fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-muted)' }}>
                            Isto é permitido e não exige justificação. Cada documento gera o seu próprio
                            grupo e as suas próprias obrigações — nada é agrupado nem fundido.
                        </p>
                    </FieldMessageIcon>
                )}
            </header>

            {effectiveReadOnly && summary?.editBlockedReason && (
                <p style={{
                    margin: 0, padding: '8px 10px', borderRadius: '6px', fontSize: '0.78rem',
                    backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
                    color: 'var(--color-text-muted)'
                }}>
                    {summary.editBlockedReason}
                </p>
            )}

            {loadError && (
                <div role="alert" style={{
                    padding: '10px 12px', borderRadius: '6px', border: '1px solid #fca5a5',
                    backgroundColor: 'rgba(185,28,28,0.08)', color: '#b91c1c',
                    fontSize: '0.78rem', fontWeight: 600
                }}>
                    {loadError}
                </div>
            )}

            {/* §17 — the same progressive presentation as creation: everything settled shows as one
                compact line, and only the document being worked on is a form. A request holding
                three invoices must never be three open forms stacked down the page. */}
            {documents.map(d => {
                const ui = uiState[d.id] ?? EMPTY_UI;

                if (expandedId !== d.id) {
                    const status = deriveCardStatus(d, ui.ocr.isProcessing, ui.ocr.classification);

                    return (
                        <PaymentDocumentSummaryCard
                            key={d.id}                       // keyed by identity, never by index
                            document={{
                                sequence: d.sequenceNumber,
                                supplierName: d.supplierNameSnapshot ?? null,
                                documentNumber: d.documentNumber ?? null,
                                plantName: d.plantName ?? plants.find(p => p.id === d.plantId)?.name ?? null,
                                sourceDocumentType: (d.sourceDocumentType as string | null) ?? null,
                                grossAmount: d.grossAmount ?? null,
                                currency: d.currency ?? null,
                                itemCount: d.items.length
                            }}
                            lifecycle={{
                                state: d.isVoided ? 'REVIEW_REQUIRED'
                                    : status.status === 'OCR_PENDING' ? 'EXTRACTING'
                                    : status.status === 'READY' ? 'CONFIRMED' : 'REVIEW_REQUIRED',
                                label: status.label,
                                severity: status.severity
                            }}
                            issues={status.status === 'READY' ? [] : d.validationMessages}
                            onEdit={() => setExpandedId(d.id)}
                            onReplaceAttachment={() => setPendingReplace(d)}
                            onRemove={() => setPendingRemoval(d)}
                            readOnly={effectiveReadOnly}
                        />
                    );
                }

                return (
                    <PaymentSourceDocumentCard
                        key={d.id}
                        variant="editor"
                        document={d}
                        ocr={ui.ocr}
                        conflict={ui.conflict}
                        isExpanded
                        onToggle={() => setExpandedId(null)}
                        readOnly={effectiveReadOnly}
                        saveError={ui.saveError}
                        isSaving={ui.isSaving}
                        currencyLocked={lockedCurrency}
                        currencyError={ui.currencyError}
                        onFieldChange={patch => handleFieldChange(d, patch)}
                        onConflictChange={next => patchUi(d.id, { conflict: next })}
                        onReplaceAttachment={() => setPendingReplace(d)}
                        onRemove={() => setPendingRemoval(d)}
                        onDuplicate={() => void addDocument(d)}
                        showDuplicate={!effectiveReadOnly}
                        // Read-only requests never expose supplier creation: the document cannot be
                        // changed, so a supplier created for it would have nowhere to go.
                        onCreateSupplier={canCreateSupplier && !effectiveReadOnly
                            ? (name, taxId) => setSupplierCreate({ documentId: d.id, name, taxId })
                            : null}
                        supplierCreateDisabledReason={
                            effectiveReadOnly ? null
                                : canCreateSupplier ? null : SUPPLIER_CREATE_NOT_ALLOWED}
                        plants={plants}
                        currencies={currencies}
                        footer={!effectiveReadOnly ? (
                            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                                <button
                                    type="button"
                                    onClick={() => void save(d)}
                                    disabled={ui.isSaving}
                                    style={primaryButton}
                                >
                                    Guardar documento
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setExpandedId(null)}
                                    style={{
                                        padding: '8px 14px', borderRadius: '8px', cursor: 'pointer',
                                        border: '1px solid var(--color-border)',
                                        backgroundColor: 'var(--color-bg-page)',
                                        color: 'var(--color-text-main)', fontWeight: 700, fontSize: '0.8rem'
                                    }}
                                >
                                    Fechar
                                </button>
                            </div>
                        ) : (
                            <button
                                type="button"
                                onClick={() => setExpandedId(null)}
                                style={{
                                    alignSelf: 'flex-start', padding: '8px 14px', borderRadius: '8px',
                                    cursor: 'pointer', border: '1px solid var(--color-border)',
                                    backgroundColor: 'var(--color-bg-page)',
                                    color: 'var(--color-text-main)', fontWeight: 700, fontSize: '0.8rem'
                                }}
                            >
                                Fechar
                            </button>
                        )}
                    >
                        {renderItems?.(d)}
                    </PaymentSourceDocumentCard>
                );
            })}

            {!effectiveReadOnly && (
                <button
                    type="button"
                    onClick={() => setChooserOpen(true)}
                    disabled={isAdding}
                    style={{ ...primaryButton, alignSelf: 'flex-start', opacity: isAdding ? 0.6 : 1 }}
                >
                    <Plus size={14} /> {isAdding ? 'A adicionar…' : 'Adicionar outro documento'}
                </button>
            )}

            {/* The same shared quick-create as the creation screen and Quotation Management. */}
            <PaymentSupplierCreateModal
                isOpen={!!supplierCreate}
                onClose={() => setSupplierCreate(null)}
                prefill={{ name: supplierCreate?.name, taxId: supplierCreate?.taxId }}
                onCreated={supplier => {
                    const target = supplierCreate?.documentId;
                    if (!target) return;

                    // Applied to the document that asked, and marked dirty: the supplier exists in
                    // the master data now, but this document's link to it is not saved until the
                    // user saves the document.
                    patchDocument(target, {
                        supplierId: supplier.id,
                        supplierNameSnapshot: supplier.name
                    });
                    setDirtyIds(prev => (prev.includes(target) ? prev : [...prev, target]));
                }}
            />

            {chooserOpen && (
                <AddPaymentDocumentChoice
                    variant="modal"
                    sequence={documents.reduce((m, d) => Math.max(m, d.sequenceNumber), 0) + 1}
                    duplicateFrom={documents.length > 0
                        ? documents[documents.length - 1].sequenceNumber : null}
                    onChoose={method => {
                        setChooserOpen(false);
                        void addDocument(method === 'DUPLICATE'
                            ? documents[documents.length - 1] : undefined);
                    }}
                    onCancel={() => setChooserOpen(false)}
                />
            )}

            {/* ── Request-level summary ── */}
            <div style={{
                display: 'flex', gap: '16px', flexWrap: 'wrap', alignItems: 'center',
                padding: '10px 12px', borderRadius: '8px',
                backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)'
            }}>
                <Total label="Documentos" value={String(totals.activeCount)} />
                <Total label="Líquido" value={`${formatCurrencyAO(totals.net)} ${totals.currency ?? ''}`} />
                <Total label="IVA" value={`${formatCurrencyAO(totals.tax)} ${totals.currency ?? ''}`} />
                <Total
                    label="Total"
                    value={`${formatCurrencyAO(summary?.requestTotal ?? totals.gross)} ${totals.currency ?? ''}`}
                    strong
                />

                {issues.length > 0 && (
                    <button
                        type="button"
                        onClick={() => setShowIssues(true)}
                        style={{ ...linkButton, marginLeft: 'auto', color: '#b45309' }}
                    >
                        <AlertTriangle size={13} /> {issues.length} pendência(s)
                    </button>
                )}
            </div>

            {/* Every problem at once, each linking to the card that owns it. */}
            <InfoModal
                isOpen={showIssues}
                onClose={() => setShowIssues(false)}
                title="Pendências dos documentos"
                icon={<AlertTriangle size={18} />}
                tone="warning"
                maxWidth={640}
            >
                <p style={{ margin: '0 0 12px', fontSize: '0.8125rem', color: 'var(--color-text-muted)' }}>
                    O pedido só pode ser submetido depois de resolver todas as pendências abaixo.
                    Clique numa pendência para abrir o documento correspondente.
                </p>
                <ul style={{ margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                    {issues.map((issue, i) => (
                        <li key={`${issue.documentId ?? 'request'}-${i}`}>
                            {issue.documentId ? (
                                <button type="button" onClick={() => focusIssue(issue)} style={linkButton}>
                                    {issue.display}
                                </button>
                            ) : (
                                <span style={{ fontSize: '0.8rem' }}>{issue.display}</span>
                            )}
                        </li>
                    ))}
                </ul>
            </InfoModal>

            {/* ── Confirmations ── */}
            {pendingRemoval && (
                <ConfirmationDialog
                    title={`Remover Documento ${pendingRemoval.sequenceNumber}?`}
                    message={
                        'Os itens associados a este documento também serão removidos do pedido. ' +
                        'Se o pedido já foi submetido alguma vez, o documento é anulado em vez de apagado, ' +
                        'para que a decisão de classificação registada não se perca.'
                    }
                    confirmText="Remover documento"
                    cancelText="Manter"
                    variant="destructive"
                    onConfirm={() => void confirmRemoval()}
                    onCancel={() => setPendingRemoval(null)}
                />
            )}

            {pendingReplace && (
                <ConfirmationDialog
                    title={`Substituir o anexo do Documento ${pendingReplace.sequenceNumber}?`}
                    message={
                        'A leitura de OCR e a decisão de classificação atuais pertencem ao ficheiro que vai ' +
                        'substituir e serão descartadas. Se o novo documento gerar um conflito de ' +
                        'classificação, terá de o confirmar novamente — uma confirmação de uma leitura não ' +
                        'vale para outra.'
                    }
                    confirmText="Substituir anexo"
                    cancelText="Cancelar"
                    variant="warning"
                    onConfirm={() => void confirmReplace()}
                    onCancel={() => setPendingReplace(null)}
                />
            )}

            {/* Duplicate hierarchy LEVEL 4: proceed only with an explicit, audited justification. */}
            {duplicateOverride && (
                <DuplicateOverrideDialog
                    detail={duplicateOverride.detail}
                    classification={duplicateOverride.classification}
                    onCancel={() => setDuplicateOverride(null)}
                    onConfirm={reason => {
                        const target = documents.find(d => d.id === duplicateOverride.documentId);
                        setDuplicateOverride(null);
                        if (target) void save(target, reason);
                    }}
                />
            )}

            {/* Never auto-resubmits a human decision after reload. */}
            <InfoModal
                isOpen={!!conflictDialog}
                onClose={() => setConflictDialog(null)}
                title="Documento alterado entretanto"
                icon={<Info size={18} />}
                tone="warning"
                maxWidth={600}
                closeOnBackdrop={false}
                footer={
                    <>
                        <button type="button" onClick={() => setConflictDialog(null)} style={secondaryButton}>
                            Manter o que tenho no ecrã
                        </button>
                        <button
                            type="button"
                            onClick={() => { setConflictDialog(null); void reload(); }}
                            style={primaryButton}
                        >
                            Recarregar documento
                        </button>
                    </>
                }
            >
                <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-main)' }}>
                    {conflictDialog?.detail}
                </p>
                {conflictDialog?.current && (
                    <div style={{
                        marginTop: '12px', padding: '10px 12px', borderRadius: '8px',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)',
                        fontSize: '0.78rem', display: 'flex', flexDirection: 'column', gap: '4px'
                    }}>
                        <strong>Estado atual no servidor</strong>
                        <span>Documento {conflictDialog.current.sequenceNumber} · {conflictDialog.current.documentNumber ?? 'sem número'}</span>
                        <span>Fornecedor: {conflictDialog.current.supplierNameSnapshot ?? '—'}</span>
                        <span>Total: {formatCurrencyAO(conflictDialog.current.grossAmount ?? 0)} {conflictDialog.current.currency ?? ''}</span>
                    </div>
                )}
                <p style={{ margin: '12px 0 0', fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                    Se recarregar, qualquer confirmação de classificação que tenha feito terá de ser
                    decidida de novo — uma decisão sobre uma leitura não é automaticamente uma decisão
                    sobre outra.
                </p>
            </InfoModal>
        </section>
    );
}

function Total({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
    return (
        <span style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
            <span style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                {label}
            </span>
            <span style={{
                fontSize: strong ? '0.95rem' : '0.82rem',
                fontWeight: strong ? 800 : 600,
                color: 'var(--color-text-main)'
            }}>
                {value}
            </span>
        </span>
    );
}

const primaryButton: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: '6px',
    padding: '8px 14px', borderRadius: '8px', border: 'none', cursor: 'pointer',
    backgroundColor: 'var(--color-primary)', color: '#fff', fontWeight: 700, fontSize: '0.8rem'
};

const secondaryButton: React.CSSProperties = {
    padding: '8px 14px', borderRadius: '8px', cursor: 'pointer',
    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
    color: 'var(--color-text-main)', fontWeight: 600, fontSize: '0.8rem'
};

const linkButton: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: '4px',
    background: 'none', border: 'none', cursor: 'pointer', padding: 0,
    color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.78rem', textAlign: 'left'
};
