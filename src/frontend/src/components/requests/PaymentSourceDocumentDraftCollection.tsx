import React, { useMemo, useRef, useState } from 'react';
import { AlertTriangle, Plus } from 'lucide-react';
import { PaymentSourceDocumentCard } from './PaymentSourceDocumentCard';
import { FieldMessageIcon } from '../ui/FieldMessageIcon';
import { ConfirmationDialog } from '../common/ConfirmationDialog';
import { formatCurrencyAO } from '../../lib/utils';
import { currencyConflictMessage } from '../../lib/paymentSourceDocuments';
import { ClassificationConflictState } from '../../lib/documentClassificationDecision';
import { PaymentDocumentOcrState } from '../../types/paymentSourceDocument';
import { ExtractionDiscrepancy } from '../../lib/paymentRequestCreation';
import {
    TemporaryPaymentDocument,
    asCardDocument,
    createTemporaryDocument,
    duplicateTemporaryBasics,
    temporaryEstablishedCurrency,
    temporaryTotals
} from '../../lib/paymentRequestCreation';

interface Props {
    documents: TemporaryPaymentDocument[];
    onChange: (next: TemporaryPaymentDocument[]) => void;

    suppliers: Array<{ id: number; name: string; taxId?: string | null }>;
    plants: Array<{ id: number; name: string }>;
    currencies: Array<{ code: string; name: string }>;

    /** Picks a file and returns a placeholder id plus the File itself for OCR. */
    onPickFile: (purpose: 'NEW' | 'REPLACE') =>
        Promise<{ id: string; fileName: string; file: File } | null>;

    /** OCR state for one document, keyed by tempId by the caller. */
    ocrStateFor: (tempId: string) => PaymentDocumentOcrState;
    discrepanciesFor: (tempId: string) => ExtractionDiscrepancy[];
    /** Runs (or re-runs) OCR for one document. */
    onRunOcr: (document: TemporaryPaymentDocument) => Promise<void>;
    /** Clears everything the previous file produced, on replacement or removal. */
    onResetOcr: (tempId: string) => void;

    disabled?: boolean;
}

/**
 * The source-document collection <b>before the request exists</b>.
 *
 * <p>Renders the very same {@link PaymentSourceDocumentCard} as the persisted collection — creation
 * and editing must not grow two visual implementations that drift apart. Only the container differs,
 * because the persistence model genuinely differs: here there is no request to attach to yet, so
 * documents live in client state keyed by a temporary id and are flushed to the server in one
 * controlled pass afterwards.</p>
 *
 * <p>Keyed by <code>tempId</code>, never by array position. Removing the first document must not
 * hand its OCR reading and classification decision to the second.</p>
 */
export function PaymentSourceDocumentDraftCollection({
    documents,
    onChange,
    suppliers,
    plants,
    currencies,
    onPickFile,
    ocrStateFor,
    discrepanciesFor,
    onRunOcr,
    onResetOcr,
    disabled = false
}: Props) {
    const [expandedId, setExpandedId] = useState<string | null>(documents[0]?.tempId ?? null);
    const [pendingRemoval, setPendingRemoval] = useState<TemporaryPaymentDocument | null>(null);
    const [pendingReplace, setPendingReplace] = useState<TemporaryPaymentDocument | null>(null);
    const [currencyErrors, setCurrencyErrors] = useState<Record<string, string | null>>({});
    const isAddingRef = useRef(false);

    const lockedCurrency = useMemo(() => temporaryEstablishedCurrency(documents), [documents]);
    const totals = useMemo(() => temporaryTotals(documents), [documents]);

    const patch = (tempId: string, changes: Partial<TemporaryPaymentDocument>) =>
        onChange(documents.map(d => (d.tempId === tempId ? { ...d, ...changes } : d)));

    const addDocument = async (basedOn?: TemporaryPaymentDocument) => {
        if (isAddingRef.current || disabled) return;   // a double click must not create two cards
        isAddingRef.current = true;

        try {
            const picked = await onPickFile('NEW');
            if (!picked) return;

            const created: TemporaryPaymentDocument = {
                ...(basedOn ? duplicateTemporaryBasics(basedOn) : createTemporaryDocument()),
                attachmentId: picked.id,
                attachmentFileName: picked.fileName,
                currency: basedOn?.currency ?? lockedCurrency ?? null
            };

            onChange([...documents, created]);
            setExpandedId(created.tempId);

            // Read THIS document. Only its own card shows a processing state; a second card being
            // read at the same time is unaffected.
            void onRunOcr(created);

            requestAnimationFrame(() => {
                window.document
                    .querySelector<HTMLElement>(`[data-document-id="${created.tempId}"] button`)
                    ?.focus();
            });
        } finally {
            isAddingRef.current = false;
        }
    };

    const confirmRemoval = () => {
        const target = pendingRemoval;
        setPendingRemoval(null);
        if (!target) return;

        // Nothing is persisted yet, so removal is purely local — no void, no audit to preserve.
        onChange(documents.filter(d => d.tempId !== target.tempId));
        onResetOcr(target.tempId);
        if (expandedId === target.tempId) setExpandedId(null);
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
            conflict: { hasConflict: false, isHighRisk: false, acknowledged: false, justification: '' }
        };

        onChange(documents.map(d => (d.tempId === target.tempId ? replaced : d)));
        void onRunOcr(replaced);
    };

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

    return (
        <section style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            <h3 style={{
                margin: 0, fontSize: '0.8rem', fontWeight: 900, letterSpacing: '0.05em',
                textTransform: 'uppercase', color: 'var(--color-text-muted)'
            }}>
                Documentos do pedido
            </h3>

            {documents.length === 0 && (
                <p style={{
                    margin: 0, padding: '12px', borderRadius: '8px', fontSize: '0.8rem',
                    border: '1px dashed var(--color-border)', color: 'var(--color-text-muted)'
                }}>
                    Adicione pelo menos um documento de origem. Um pedido de pagamento pode conter
                    vários — por exemplo, a mesma prestação faturada separadamente para duas plantas.
                </p>
            )}

            {documents.map((d, index) => (
                <PaymentSourceDocumentCard
                    key={d.tempId}                        // identity, never position
                    document={asCardDocument(d, index + 1)}
                    ocr={ocrStateFor(d.tempId)}
                    conflict={d.conflict}
                    isExpanded={expandedId === d.tempId}
                    onToggle={() => setExpandedId(expandedId === d.tempId ? null : d.tempId)}
                    readOnly={disabled}
                    saveError={d.error}
                    isSaving={false}
                    currencyLocked={lockedCurrency}
                    currencyError={currencyErrors[d.tempId] ?? null}
                    onFieldChange={changes => handleFieldChange(d, changes as Record<string, unknown>)}
                    onConflictChange={(next: ClassificationConflictState) =>
                        patch(d.tempId, { conflict: next })}
                    onReplaceAttachment={() => setPendingReplace(d)}
                    onRemove={() => setPendingRemoval(d)}
                    onDuplicate={() => void addDocument(d)}
                    suppliers={suppliers}
                    plants={plants}
                    currencies={currencies}
                >
                    {ocrStateFor(d.tempId).error && !disabled && (
                        <button type="button" onClick={() => void onRunOcr(d)} style={retryButton}>
                            Tentar ler o documento novamente
                        </button>
                    )}

                    {/* A disagreement is reported, never silently applied — the user may have
                        corrected a misread number by hand. */}
                    {discrepanciesFor(d.tempId).length > 0 && (
                        <div style={{
                            padding: '8px 10px', borderRadius: '6px', fontSize: '0.75rem',
                            border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.06)',
                            color: '#b45309'
                        }}>
                            <strong>A leitura difere do que introduziu:</strong>
                            <ul style={{ margin: '4px 0 0', paddingLeft: '16px' }}>
                                {discrepanciesFor(d.tempId).map(x => (
                                    <li key={x.field}>
                                        {x.label}: manteve <strong>{x.userValue}</strong>,
                                        o documento indica <strong>{x.extractedValue}</strong>.
                                    </li>
                                ))}
                            </ul>
                        </div>
                    )}
                </PaymentSourceDocumentCard>
            ))}

            {!disabled && (
                <button
                    type="button"
                    onClick={() => void addDocument()}
                    style={{
                        display: 'inline-flex', alignItems: 'center', gap: '6px', alignSelf: 'flex-start',
                        padding: '8px 14px', borderRadius: '8px', border: 'none', cursor: 'pointer',
                        backgroundColor: 'var(--color-primary)', color: '#fff',
                        fontWeight: 700, fontSize: '0.8rem'
                    }}
                >
                    <Plus size={14} /> Adicionar outro documento
                </button>
            )}

            <div style={{
                display: 'flex', gap: '16px', flexWrap: 'wrap', alignItems: 'center',
                padding: '10px 12px', borderRadius: '8px',
                backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)'
            }}>
                <Total label="Documentos" value={String(totals.count)} />
                <Total label="Líquido" value={`${formatCurrencyAO(totals.net)} ${totals.currency ?? ''}`} />
                <Total label="IVA" value={`${formatCurrencyAO(totals.tax)} ${totals.currency ?? ''}`} />
                <Total label="Total" value={`${formatCurrencyAO(totals.gross)} ${totals.currency ?? ''}`} strong />

                {documents.length > 1 && (
                    <FieldMessageIcon
                        severity="info"
                        tooltip="O total do pedido é a soma dos documentos."
                        title="Total consolidado"
                        maxWidth={520}
                    >
                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55 }}>
                            O valor do pedido é a soma dos documentos ativos. Cada documento mantém a sua
                            própria classificação e gera o seu próprio acompanhamento depois do pagamento —
                            documentos de plantas ou tipos diferentes não são agrupados.
                        </p>
                    </FieldMessageIcon>
                )}
            </div>

            {pendingRemoval && (
                <ConfirmationDialog
                    title="Remover este documento?"
                    message={
                        'O documento e os seus dados serão descartados. Como o pedido ainda não foi ' +
                        'criado, nada é gravado no servidor.'
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
                        'vai substituir e serão descartadas. Se o novo documento gerar um conflito, terá ' +
                        'de o confirmar novamente.'
                    }
                    confirmText="Substituir anexo"
                    cancelText="Cancelar"
                    variant="warning"
                    onConfirm={() => void confirmReplace()}
                    onCancel={() => setPendingReplace(null)}
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
