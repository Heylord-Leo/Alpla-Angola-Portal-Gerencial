import React, { useCallback, useRef, useState } from 'react';
import { api } from '../../../lib/api';
import { PaymentSourceDocumentCollection } from '../../../components/requests/PaymentSourceDocumentCollection';
import { PaymentSourceDocumentsSummaryDto } from '../../../types/paymentSourceDocument';
import { CollapsibleSection } from '../../../components/ui/CollapsibleSection';
import { ConfirmationDialog } from '../../../components/common/ConfirmationDialog';
import { computeFileHash, formatDateTime } from '../../../lib/utils';

interface Props {
    requestId: string;
    requestTypeCode: string | null;
    statusCode: string | null;
    /** Release 3 flag. False renders nothing at all — the pre-feature layout is untouched. */
    multiDocumentEnabled: boolean;
    /**
     * Whether this request already has source documents. A request created before Release 3 has
     * none, keeps the legacy single-document rendering, and must never be shown this section.
     * Nothing is synthesized on the client.
     */
    hasSourceDocuments: boolean;

    plants: Array<{ id: number; name: string }>;
    currencies: Array<{ code: string; name: string }>;

    isOpen: boolean;
    onToggle: () => void;
    onSummaryChange?: (summary: PaymentSourceDocumentsSummaryDto | null) => void;
    /** Active document count, for the section header badge. */
    documentCount: number;
    /** Mirrors the backend rule for supplier quick-create from a Payment flow. */
    canCreateSupplier?: boolean;
    /** Bubbles whether a document is mid-edit, so submission can refuse. */
    onEditingStateChange?: (state: { openSequence: number | null; unsavedSequences: number[] }) => void;
}

/** Statuses in which the documents may be changed. Mirrors PaymentSourceDocumentPolicy. */
const EDITABLE_STATUSES = ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'];

/**
 * The PAYMENT source-document collection, with the two gates that decide whether it appears at all.
 *
 * <p><b>Flag off → nothing renders.</b> The screen keeps exactly the layout it had before the
 * feature existed.</p>
 *
 * <p><b>No source-document rows → nothing renders.</b> A request created before Release 3 has none
 * and continues through the legacy single-document path. The client never synthesizes documents to
 * make an old request look new; that would invent facts nobody recorded.</p>
 */
export function PaymentSourceDocumentsSection({
    requestId,
    requestTypeCode,
    statusCode,
    multiDocumentEnabled,
    hasSourceDocuments,
    plants,
    currencies,
    isOpen,
    onToggle,
    onSummaryChange,
    documentCount,
    canCreateSupplier = false,
    onEditingStateChange
}: Props) {
    const fileInputRef = useRef<HTMLInputElement>(null);
    const resolverRef = useRef<((id: string | null) => void) | null>(null);
    const [uploadError, setUploadError] = useState<string | null>(null);
    /** Guards against a rapid second selection starting a second preflight, upload or reading. */
    const isCheckingRef = useRef(false);
    const replacingDocumentIdRef = useRef<string | null>(null);

    /** The same file is already on this request. A hard block — no override, no justification. */
    const [sameRequestDuplicate, setSameRequestDuplicate] = useState<{
        fileName: string;
        sequence: number;
        documentNumber: string | null;
        supplierName: string | null;
        documentId: string | null;
    } | null>(null);

    /** The same file belongs to another request. Warned, per the existing policy. */
    const [crossRequestDuplicate, setCrossRequestDuplicate] = useState<{
        fileName: string;
        requestNumber: string | null;
        uploadedBy: string | null;
        createdAtUtc: string | null;
        onDecision: (accepted: boolean) => void;
    } | null>(null);

    /**
     * The same file is an ACTIVE source document of another LIVE request — a debt already in
     * flight (duplicate hierarchy LEVEL 1, cross-request). Hard block: no override exists for
     * paying the same file twice.
     */
    const [crossRequestBlock, setCrossRequestBlock] = useState<{
        fileName: string;
        requestNumber: string | null;
    } | null>(null);

    const isPayment = requestTypeCode === 'PAYMENT';
    const readOnly = !EDITABLE_STATUSES.includes(statusCode ?? '');

    /**
     * The review screen presents documents; the composer changes them.
     *
     * <p>Editing starts behind one explicit action rather than being permanently available, so the
     * review screen stays a review — every document and item change happens inside the controlled
     * composer, where the confirmation rules apply.</p>
     */
    const [composerOpen, setComposerOpen] = useState(false);
    const editing = composerOpen && !readOnly;

    /**
     * Opens the file picker and resolves with the new attachment id. Promise-shaped so the
     * collection can await it inline instead of threading callbacks through three components.
     */
    const requestAttachment = useCallback((purpose?: 'NEW' | 'REPLACE', documentId?: string): Promise<string | null> => {
        setUploadError(null);
        // Remembered for the preflight: a document being replaced cannot conflict with itself.
        replacingDocumentIdRef.current = purpose === 'REPLACE' ? documentId ?? null : null;
        return new Promise<string | null>(resolve => {
            resolverRef.current = resolve;
            fileInputRef.current?.click();
        });
    }, []);

    const handleFileChosen = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        e.target.value = '';                       // allow re-picking the same file after a failure
        const resolve = resolverRef.current;
        resolverRef.current = null;

        if (!file) { resolve?.(null); return; }

        // A rapid second selection must not start a second preflight, a second upload or a second
        // reading of the same document.
        if (isCheckingRef.current) { resolve?.(null); return; }
        isCheckingRef.current = true;

        const replacingDocumentId = replacingDocumentIdRef.current;
        replacingDocumentIdRef.current = null;

        try {
            // ── Preflight, before the file is uploaded or read ──
            // Without it the answer arrives from the create endpoint as a 409, i.e. after OCR, after
            // the user corrected every extracted field and confirmed the document.
            try {
                const hash = await computeFileHash(file);
                const verdict = await api.requests.checkSourceDocumentDuplicate(requestId, {
                    contentHash: hash,
                    candidateFileName: file.name,
                    replacingDocumentId
                });

                if (verdict.outcome === 'BLOCK' && verdict.duplicateScope === 'OTHER_REQUEST') {
                    // Registered as an active source document of another live request: a debt
                    // already in flight. Nothing to acknowledge — the block is the answer.
                    setCrossRequestBlock({
                        fileName: file.name,
                        requestNumber: verdict.requestNumber ?? null
                    });
                    resolve?.(null);
                    return;
                }

                if (verdict.outcome === 'BLOCK') {
                    // Nothing is uploaded, nothing is read and nothing existing is disturbed —
                    // resolving null leaves a replacement's current attachment exactly as it was.
                    setSameRequestDuplicate({
                        fileName: file.name,
                        sequence: verdict.conflictingSequenceNumber ?? 0,
                        documentNumber: verdict.conflictingDocumentNumber ?? null,
                        supplierName: verdict.supplierName ?? null,
                        documentId: verdict.conflictingDocumentId ?? null
                    });
                    resolve?.(null);
                    return;
                }

                if (verdict.outcome === 'WARN') {
                    const accepted = await new Promise<boolean>(done => {
                        setCrossRequestDuplicate({
                            fileName: file.name,
                            requestNumber: verdict.requestNumber ?? null,
                            uploadedBy: verdict.uploadedBy ?? null,
                            createdAtUtc: verdict.createdAtUtc ?? null,
                            onDecision: done
                        });
                    });
                    setCrossRequestDuplicate(null);
                    if (!accepted) { resolve?.(null); return; }
                }
            } catch {
                // The preflight is a courtesy. If it cannot answer, the user is not stopped — the
                // create endpoint still refuses a duplicate with a 409.
            }

            const uploaded = await api.attachments.upload(requestId, [file], 'PAYMENT_SOURCE_DOCUMENT');
            const attachmentId = Array.isArray(uploaded)
                ? uploaded[0]?.id ?? uploaded[0]?.attachmentId
                : uploaded?.id ?? uploaded?.attachmentId;

            if (!attachmentId) {
                setUploadError('O anexo foi carregado mas o servidor não devolveu a sua identificação.');
                resolve?.(null);
                return;
            }

            resolve?.(attachmentId as string);
        } catch (err: any) {
            setUploadError(err?.message ?? 'Não foi possível carregar o ficheiro.');
            resolve?.(null);
        } finally {
            isCheckingRef.current = false;
        }
    };

    // Both gates, stated once.
    if (!isPayment || !multiDocumentEnabled) return null;
    if (!hasSourceDocuments && readOnly) return null;

    return (
        <CollapsibleSection
            title="Documentos do Pedido"
            count={documentCount}
            isOpen={isOpen}
            onToggle={onToggle}
        >
            <input
                ref={fileInputRef}
                type="file"
                accept=".pdf,.png,.jpg,.jpeg"
                onChange={handleFileChosen}
                style={{ display: 'none' }}
            />

            {uploadError && (
                <div role="alert" style={{
                    marginBottom: '10px', padding: '8px 10px', borderRadius: '6px',
                    border: '1px solid #fca5a5', backgroundColor: 'rgba(185,28,28,0.08)',
                    color: '#b91c1c', fontSize: '0.78rem', fontWeight: 600
                }}>
                    {uploadError}
                </div>
            )}

            {!readOnly && (
                <div style={{ marginBottom: '14px' }}>
                    <button
                        type="button"
                        onClick={() => setComposerOpen(o => !o)}
                        style={{
                            display: 'inline-flex', alignItems: 'center', gap: '6px',
                            padding: '9px 16px', borderRadius: 'var(--radius-sm)', cursor: 'pointer',
                            border: `1px solid var(--color-primary)`,
                            backgroundColor: editing ? 'var(--color-primary)' : 'transparent',
                            color: editing ? '#fff' : 'var(--color-primary)',
                            fontWeight: 800, fontSize: '0.8rem'
                        }}
                    >
                        {editing ? 'Concluir edição dos documentos' : 'Editar documentos do pedido'}
                    </button>
                    {!editing && (
                        <p style={{
                            margin: '8px 0 0', fontSize: '0.76rem', color: 'var(--color-text-muted)'
                        }}>
                            Os documentos e os seus itens só podem ser alterados aqui — as regras de
                            confirmação aplicam-se dentro deste editor.
                        </p>
                    )}
                </div>
            )}

            <PaymentSourceDocumentCollection
                requestId={requestId}
                readOnly={readOnly || !editing}
                plants={plants}
                currencies={currencies}
                canCreateSupplier={canCreateSupplier}
                onEditingStateChange={onEditingStateChange}
                onSummaryChange={onSummaryChange}
                onRequestAttachment={requestAttachment}
            />

            {/* The same file, already on this request. Nothing to weigh up, so nothing to confirm. */}
            {sameRequestDuplicate && (
                <ConfirmationDialog
                    title="Ficheiro já incluído neste pedido"
                    message={
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                            <span>
                                Este ficheiro já está associado ao{' '}
                                <strong>Documento {sameRequestDuplicate.sequence}</strong> deste
                                pedido e não pode ser adicionado novamente.
                            </span>
                            <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                Ficheiro: <strong>{sameRequestDuplicate.fileName}</strong>
                                {sameRequestDuplicate.documentNumber
                                    ? <> · Nº <strong>{sameRequestDuplicate.documentNumber}</strong></>
                                    : null}
                                {sameRequestDuplicate.supplierName
                                    ? <> · <strong>{sameRequestDuplicate.supplierName}</strong></>
                                    : null}
                            </span>
                            <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                Se este é realmente outro documento, verifique se não anexou o ficheiro
                                errado.
                            </span>
                        </div>
                    }
                    confirmText={`Ver Documento ${sameRequestDuplicate.sequence}`}
                    cancelText="Fechar"
                    variant="warning"
                    onConfirm={() => {
                        const id = sameRequestDuplicate.documentId;
                        setSameRequestDuplicate(null);
                        if (!id) return;
                        requestAnimationFrame(() => {
                            const card = document.querySelector<HTMLElement>(`[data-document-id="${id}"]`)
                                ?? document.querySelector<HTMLElement>(`[data-document-sequence="${sameRequestDuplicate.sequence}"]`);
                            card?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                        });
                    }}
                    onCancel={() => setSameRequestDuplicate(null)}
                />
            )}

            {/* The same file, already an active source document of another LIVE request. */}
            {crossRequestBlock && (
                <ConfirmationDialog
                    title="Ficheiro já registado noutro pedido em curso"
                    message={
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                            <span>
                                O ficheiro <strong>{crossRequestBlock.fileName}</strong> já está
                                registado como documento de origem
                                {crossRequestBlock.requestNumber
                                    ? <> do pedido <strong>{crossRequestBlock.requestNumber}</strong></>
                                    : ' de outro pedido'}{' '}
                                ainda em curso e não pode originar dois pagamentos.
                            </span>
                            <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                Se o outro pedido estiver errado, cancele-o ou anule lá o documento
                                antes de o registar aqui.
                            </span>
                        </div>
                    }
                    confirmText="Entendi"
                    cancelText="Fechar"
                    variant="warning"
                    onConfirm={() => setCrossRequestBlock(null)}
                    onCancel={() => setCrossRequestBlock(null)}
                />
            )}

            {/* The same file, on ANOTHER request. Allowed deliberately — the existing policy. */}
            {crossRequestDuplicate && (
                <ConfirmationDialog
                    title="Este ficheiro já foi utilizado noutro pedido"
                    message={
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                            <span>
                                O ficheiro <strong>{crossRequestDuplicate.fileName}</strong> já está
                                associado a outro pedido
                                {crossRequestDuplicate.requestNumber
                                    ? <> (<strong>{crossRequestDuplicate.requestNumber}</strong>)</>
                                    : null}.
                            </span>
                            {(crossRequestDuplicate.uploadedBy || crossRequestDuplicate.createdAtUtc) && (
                                <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                    {crossRequestDuplicate.uploadedBy
                                        ? <>Carregado por <strong>{crossRequestDuplicate.uploadedBy}</strong></>
                                        : null}
                                    {crossRequestDuplicate.createdAtUtc
                                        ? <> em {formatDateTime(crossRequestDuplicate.createdAtUtc)}</>
                                        : null}
                                </span>
                            )}
                            <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                Continue apenas se tiver a certeza de que este documento deve ser pago
                                também neste pedido.
                            </span>
                        </div>
                    }
                    confirmText="Estou ciente, prosseguir"
                    cancelText="Cancelar"
                    variant="warning"
                    onConfirm={() => crossRequestDuplicate.onDecision(true)}
                    onCancel={() => crossRequestDuplicate.onDecision(false)}
                />
            )}
        </CollapsibleSection>
    );
}
