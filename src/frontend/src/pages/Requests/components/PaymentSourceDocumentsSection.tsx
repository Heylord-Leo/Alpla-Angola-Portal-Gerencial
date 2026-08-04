import React, { useCallback, useRef, useState } from 'react';
import { api } from '../../../lib/api';
import { PaymentSourceDocumentCollection } from '../../../components/requests/PaymentSourceDocumentCollection';
import { PaymentSourceDocumentsSummaryDto } from '../../../types/paymentSourceDocument';
import { CollapsibleSection } from '../../../components/ui/CollapsibleSection';

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
    onEditingStateChange
}: Props) {
    const fileInputRef = useRef<HTMLInputElement>(null);
    const resolverRef = useRef<((id: string | null) => void) | null>(null);
    const [uploadError, setUploadError] = useState<string | null>(null);

    const isPayment = requestTypeCode === 'PAYMENT';
    const readOnly = !EDITABLE_STATUSES.includes(statusCode ?? '');

    /**
     * Opens the file picker and resolves with the new attachment id. Promise-shaped so the
     * collection can await it inline instead of threading callbacks through three components.
     */
    const requestAttachment = useCallback((): Promise<string | null> => {
        setUploadError(null);
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

        try {
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

            <PaymentSourceDocumentCollection
                requestId={requestId}
                readOnly={readOnly}
                plants={plants}
                currencies={currencies}
                onEditingStateChange={onEditingStateChange}
                onSummaryChange={onSummaryChange}
                onRequestAttachment={requestAttachment}
            />
        </CollapsibleSection>
    );
}
