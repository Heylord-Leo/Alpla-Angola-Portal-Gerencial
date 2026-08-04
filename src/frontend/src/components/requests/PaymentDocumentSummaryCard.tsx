import React from 'react';
import {
    AlertCircle, AlertTriangle, CheckCircle2, FileText, Loader2, Pencil, RefreshCw, Trash2
} from 'lucide-react';
import { DocumentLifecycleInfo } from '../../lib/paymentDocumentComposition';
import { documentTypeLabel } from '../../lib/sourceDocumentType';
import { formatCurrencyAO } from '../../lib/utils';

export interface SummaryCardDocument {
    sequence: number;
    supplierName: string | null;
    documentNumber: string | null;
    plantName: string | null;
    sourceDocumentType: string | null;
    grossAmount: number | null;
    currency: string | null;
    itemCount: number;
}

interface Props {
    document: SummaryCardDocument;
    lifecycle: DocumentLifecycleInfo;
    /** Why this document is not settled, when it is not. Shown, never hidden behind a hover. */
    issues?: string[];
    onEdit: () => void;
    onReplaceAttachment: () => void;
    onRemove: () => void;
    readOnly?: boolean;
    disabled?: boolean;
}

const SEVERITY_COLOR: Record<string, string> = {
    success: '#15803d',
    warning: '#b45309',
    error: '#b91c1c',
    muted: '#64748b'
};

/**
 * A document the user has already dealt with, reduced to one line the eye can scan.
 *
 * <p>The point of collapsing is that a request holding three invoices must not be three open forms.
 * The point of the header is that collapsing must not cost the user the ability to tell the
 * documents apart — supplier, number, plant, type and total are all here, so nothing has to be
 * expanded just to find out which document it is.</p>
 */
export function PaymentDocumentSummaryCard({
    document,
    lifecycle,
    issues = [],
    onEdit,
    onReplaceAttachment,
    onRemove,
    readOnly = false,
    disabled = false
}: Props) {
    const accent = SEVERITY_COLOR[lifecycle.severity];

    return (
        <div
            data-document-sequence={document.sequence}
            style={{
                border: '1px solid var(--color-border)',
                borderLeft: `3px solid ${accent}`,
                borderRadius: 'var(--radius-sm, 8px)',
                backgroundColor: 'var(--color-bg-surface)',
                padding: '10px 12px',
                display: 'flex', flexDirection: 'column', gap: '8px'
            }}
        >
            <div style={{
                display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap'
            }}>
                <span style={{ fontWeight: 800, fontSize: '0.85rem', whiteSpace: 'nowrap' }}>
                    Documento {document.sequence}
                </span>

                <span style={{
                    display: 'inline-flex', alignItems: 'center', gap: '4px',
                    padding: '2px 8px', borderRadius: '999px',
                    fontSize: '0.68rem', fontWeight: 800, whiteSpace: 'nowrap',
                    color: accent, border: `1px solid ${accent}`,
                    backgroundColor: 'transparent'
                }}>
                    {lifecycle.state === 'EXTRACTING' && <Loader2 size={12} className="spin" />}
                    {lifecycle.state === 'CONFIRMED' && <CheckCircle2 size={12} />}
                    {lifecycle.state === 'REVIEW_REQUIRED' && <AlertTriangle size={12} />}
                    {lifecycle.state === 'EDITING' && <Pencil size={12} />}
                    {lifecycle.state === 'ERROR' && <AlertCircle size={12} />}
                    {lifecycle.label}
                </span>

                <span style={{ flex: 1 }} />

                <span style={{ fontWeight: 800, fontSize: '0.9rem', whiteSpace: 'nowrap' }}>
                    {formatCurrencyAO(document.grossAmount ?? 0)} {document.currency ?? ''}
                </span>
            </div>

            {/* Identity, so the documents can be told apart without opening any of them. */}
            <div style={{
                display: 'flex', flexDirection: 'column', gap: '2px',
                fontSize: '0.78rem', color: 'var(--color-text-muted)', overflowWrap: 'anywhere'
            }}>
                <strong style={{ color: 'var(--color-text-main)', fontWeight: 700 }}>
                    {document.supplierName || 'Fornecedor por indicar'}
                </strong>
                <span>{document.documentNumber || 'Sem número'}</span>
                <span>
                    {document.sourceDocumentType
                        ? documentTypeLabel(document.sourceDocumentType)
                        : 'Tipo por classificar'}
                    {document.plantName ? ` · ${document.plantName}` : ''}
                </span>
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                    <FileText size={12} /> {document.itemCount} item(ns)
                </span>
            </div>

            {issues.length > 0 && (
                <ul style={{
                    margin: 0, paddingLeft: '18px', fontSize: '0.73rem',
                    color: '#b45309', fontWeight: 600
                }}>
                    {issues.map(i => <li key={i}>{i}</li>)}
                </ul>
            )}

            {!readOnly && (
                <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
                    <button type="button" onClick={onEdit} disabled={disabled} style={linkButton}>
                        <Pencil size={13} /> Ver / editar
                    </button>
                    <button
                        type="button"
                        onClick={onReplaceAttachment}
                        disabled={disabled}
                        style={linkButton}
                    >
                        <RefreshCw size={13} /> Substituir anexo
                    </button>
                    <button
                        type="button"
                        onClick={onRemove}
                        disabled={disabled}
                        style={{ ...linkButton, color: '#b91c1c' }}
                    >
                        <Trash2 size={13} /> Remover
                    </button>
                </div>
            )}
        </div>
    );
}

const linkButton: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: '4px',
    background: 'none', border: 'none', cursor: 'pointer',
    color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.75rem', padding: 0
};
