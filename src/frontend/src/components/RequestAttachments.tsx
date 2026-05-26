import React, { useState } from 'react';
import { FileText, Upload, Download, Trash2, AlertCircle, CheckCircle } from 'lucide-react';
import { api } from '../lib/api';
import { formatDateTime } from '../lib/utils';

interface Attachment {
    id: string;
    fileName: string;
    fileExtension: string;
    fileSizeMBytes: number;
    attachmentTypeCode: string;
    uploadedAtUtc: string;
    uploadedByName: string;
}

interface RequestAttachmentsProps {
    requestId: string;
    attachments: Attachment[];
    canEdit: boolean;
    onRefresh: () => void;
    requestType?: string; // e.g., 'PAYMENT', 'QUOTATION'
    status?: string;      // e.g., 'DRAFT', 'PO_ISSUED'
    highlight?: boolean;
    id?: string;
}

const TYPE_LABELS: Record<string, string> = {
    'PROFORMA': 'Proforma',
    'PO': 'P.O',
    'PAYMENT_SCHEDULE': 'Cronograma de Pagamento',
    'PAYMENT_PROOF': 'Comprovante de Pagamento',
    'RECEIPT': 'Recibo',
    'SUPPORTING': 'Documentos de Apoio'
};

export const RequestAttachments: React.FC<RequestAttachmentsProps> = ({
    requestId,
    attachments,
    canEdit,
    onRefresh,
    requestType,
    status,
    highlight,
    id
}) => {
    const [uploading, setUploading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const isTypeUploadable = (typeCode: string) => {
        if (!status || !requestType) return false;

        switch (typeCode) {
            case 'PROFORMA':
                return ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT', 'WAITING_QUOTATION'].includes(status);
            case 'SUPPORTING':
                return ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT', 'WAITING_QUOTATION'].includes(status);
            case 'PO':
                return ['APPROVED', 'PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT'].includes(status);
            case 'PAYMENT_SCHEDULE':
                return ['PO_ISSUED', 'PAYMENT_SCHEDULED'].includes(status);
            case 'PAYMENT_PROOF':
                return ['PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT'].includes(status);
            case 'RECEIPT':
                return ['WAITING_RECEIPT'].includes(status);
            default:
                return false;
        }
    };

    const isTypeDeletable = (typeCode: string) => {
        if (!status) return false;

        switch (typeCode) {
            case 'PROFORMA':
                return ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT', 'WAITING_QUOTATION'].includes(status);
            case 'PO':
                return ['APPROVED'].includes(status);
            case 'PAYMENT_SCHEDULE':
                return ['PO_ISSUED', 'PAYMENT_SCHEDULED'].includes(status);
            case 'PAYMENT_PROOF':
                return ['PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT'].includes(status);
            case 'RECEIPT':
                return ['WAITING_RECEIPT'].includes(status);
            default:
                return ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT', 'WAITING_QUOTATION'].includes(status);
        }
    };

    const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>, typeCode: string) => {
        if (uploading) return;
        const selectedFiles = e.target.files;
        if (!selectedFiles || selectedFiles.length === 0) return;

        setUploading(true);
        setError(null);

        try {
            await api.attachments.upload(requestId, Array.from(selectedFiles), typeCode);
            onRefresh();
        } catch (err: any) {
            setError(err.message || 'Falha ao carregar arquivo.');
        } finally {
            setUploading(false);
            // Reset input so the same file can be selected again
            e.target.value = '';
        }
    };

    const handleDelete = async (id: string) => {
        if (!window.confirm('Tem certeza que deseja remover este documento?')) return;

        try {
            await api.attachments.delete(id);
            onRefresh();
        } catch (err: any) {
            alert(err.message || 'Falha ao remover arquivo.');
        }
    };

    const handleDownload = async (id: string, fileName: string) => {
        try {
            await api.attachments.download(id, fileName);
        } catch (err: any) {
            alert(err.message || 'Falha ao descarregar arquivo.');
        }
    };

    const activeAttachments = attachments.filter(a => !(a as any).isDeleted);

    return (
        <div id={id} data-field="Attachments" style={{
            backgroundColor: 'var(--color-bg-surface)',
            padding: '32px',
            borderRadius: 'var(--radius-md)',
            boxShadow: highlight ? '0 0 0 3px rgba(239,68,68,0.35), var(--shadow-md)' : 'var(--shadow-md)',
            border: '1px solid var(--color-border)',
            display: 'flex',
            flexDirection: 'column',
            gap: '24px',
            transition: 'box-shadow 0.5s ease'
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '2px solid var(--color-border)', paddingBottom: '8px' }}>
                <h2 style={{
                    fontSize: '1.25rem',
                    fontWeight: 900,
                    fontFamily: 'var(--font-family-display)',
                    margin: 0,
                    textTransform: 'uppercase',
                    letterSpacing: '-0.01em',
                    color: 'var(--color-text-main)'
                }}>Anexos do Pedido</h2>
                {uploading && <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-primary)' }}>Carregando...</span>}
            </div>

            {error && (
                <div style={{ padding: '12px', backgroundColor: '#fee2e2', color: '#b91c1c', borderRadius: '4px', display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', border: '1px solid #f87171' }}>
                    <AlertCircle size={16} />
                    {error}
                </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: '20px' }}>
                {Object.entries(TYPE_LABELS).map(([code, label]) => {
                    const typeAttachments = activeAttachments.filter(a => a.attachmentTypeCode === code);

                    // Logic: Some sections shouldn't even be shown if they don't apply to the request type
                    // After PO_ISSUED, both types follow the financial flow, so they stay visible
                    const isFinancialType = ['PAYMENT_SCHEDULE', 'PAYMENT_PROOF', 'RECEIPT'].includes(code);
                    if (isFinancialType && !['PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'COMPLETED', 'QUOTATION_COMPLETED'].includes(status || '')) {
                        return null;
                    }

                    const canUploadThisType = canEdit && isTypeUploadable(code);

                    return (
                        <div key={code} style={{
                            padding: '16px',
                            backgroundColor: 'var(--color-bg-page)',
                            borderRadius: 'var(--radius-sm)',
                            border: '2px solid var(--color-border)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '12px'
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                                    {label}
                                </span>
                                {canUploadThisType && (
                                    <label style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                                        <Upload size={14} />
                                        ADICIONAR
                                        <input 
                                            type="file" 
                                            multiple
                                            style={{ display: 'none' }} 
                                            onChange={(e) => handleUpload(e, code)} 
                                        />
                                    </label>
                                )}
                            </div>

                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                {typeAttachments.length === 0 ? (
                                    <span style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>Nenhum documento</span>
                                ) : (
                                    typeAttachments.map(a => (
                                        <div key={a.id} style={{
                                            display: 'flex',
                                            justifyContent: 'space-between',
                                            alignItems: 'center',
                                            padding: '8px 12px',
                                            backgroundColor: '#fff',
                                            borderRadius: '4px',
                                            border: '1px solid var(--color-border)'
                                        }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', flex: 1, overflow: 'hidden' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                    <FileText size={16} style={{ color: 'var(--color-text-muted)' }} />
                                                    <span style={{ fontSize: '0.875rem', fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', display: 'flex', alignItems: 'center', gap: '6px' }} title={a.fileName}>
                                                        {a.fileName}
                                                        <CheckCircle size={14} style={{ color: '#10B981' }} />
                                                    </span>
                                                </div>
                                                <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginLeft: '24px', marginTop: '2px' }}>
                                                    Enviado por {a.uploadedByName} em {formatDateTime(a.uploadedAtUtc)}
                                                </div>
                                            </div>
                                            <div style={{ display: 'flex', gap: '8px', marginLeft: '12px' }}>
                                                <button
                                                    onClick={() => handleDownload(a.id, a.fileName)}
                                                    style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer' }}
                                                    title="Descarregar"
                                                >
                                                    <Download size={16} />
                                                </button>
                                                {canEdit && isTypeDeletable(a.attachmentTypeCode) && (
                                                    <button
                                                        onClick={() => handleDelete(a.id)}
                                                        style={{ background: 'none', border: 'none', color: '#EF4444', cursor: 'pointer' }}
                                                        title="Remover"
                                                    >
                                                        <Trash2 size={16} />
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                    ))
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
};
