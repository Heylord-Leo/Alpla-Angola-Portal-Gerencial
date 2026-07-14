import React, { useState } from 'react';
import { FileText, CheckCircle, Upload, Trash2 } from 'lucide-react';
import { api } from '../../lib/api';
import { logger } from '../../lib/logger';
import { ApprovalModal } from '../ApprovalModal';
import { FeedbackType } from '../ui/Feedback';
import { RequestAttachmentDto } from '../../types';

interface FinalizeReceivingModalProps {
    requestId: string;
    requestNumber: string;
    groupId: string;
    groupName?: string;
    attachments: RequestAttachmentDto[];
    show: boolean;
    onClose: () => void;
    onSuccess: (message?: string) => void;
    isPartial?: boolean;
}

export const FinalizeReceivingModal: React.FC<FinalizeReceivingModalProps> = ({
    requestId,
    requestNumber,
    groupId,
    groupName,
    attachments,
    show,
    onClose,
    onSuccess,
    isPartial = false
}) => {
    const [finalizeComment, setFinalizeComment] = useState('');
    const [finalizeProcessing, setFinalizeProcessing] = useState(false);
    const [finalizeFeedback, setFinalizeFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [finalizeReceiptFile, setFinalizeReceiptFile] = useState<File | null>(null);

    const handleConfirmFinalize = async () => {
        // Frontend validation: Must have a RECEIPT attachment or selected file
        const hasReceipt = attachments?.some(
            (a: any) => a.attachmentTypeCode === 'RECEIPT' && !a.isDeleted
        );

        if (!hasReceipt && !finalizeReceiptFile) {
            setFinalizeFeedback({
                type: 'error',
                message: 'Antes de finalizar, anexe o recibo/documento fiscal.'
            });
            return;
        }

        try {
            setFinalizeProcessing(true);
            setFinalizeFeedback({ type: 'success', message: null });

            if (!hasReceipt && finalizeReceiptFile) {
                try {
                    await api.attachments.upload(requestId, [finalizeReceiptFile], 'RECEIPT', groupId);
                } catch (uploadErr: any) {
                    const errMsg = uploadErr instanceof Error ? uploadErr.message : (uploadErr?.response?.data?.message || 'Falha ao carregar recibo.');
                    logger.error(`Erro ao carregar recibo final do pedido ${requestNumber} (${groupName || groupId}): ${errMsg}`, uploadErr, 'Global');
                    setFinalizeFeedback({ type: 'error', message: errMsg });
                    setFinalizeProcessing(false);
                    return;
                }
            }

            await api.requests.confirmReceiving(requestId, groupId, finalizeComment);
            
            // Clean state and trigger success
            setFinalizeComment('');
            setFinalizeReceiptFile(null);
            onSuccess('Recebimento finalizado com sucesso.');
        } catch (err: any) {
            console.error('Error confirming receiving:', err);
            const errorMessage = err instanceof Error ? err.message : (err?.response?.data?.message || 'Falha ao finalizar pedido.');
            
            logger.error(`Erro ao finalizar pedido ${requestNumber}: ${errorMessage}`, err, 'Global');
            setFinalizeFeedback({ type: 'error', message: errorMessage });
        } finally {
            setFinalizeProcessing(false);
        }
    };

    const handleClose = () => {
        setFinalizeFeedback({ type: 'success', message: null });
        setFinalizeReceiptFile(null);
        setFinalizeComment('');
        onClose();
    };

    return (
        <ApprovalModal
            show={show}
            type="FINALIZE"
            onClose={handleClose}
            onConfirm={handleConfirmFinalize}
            comment={finalizeComment}
            setComment={setFinalizeComment}
            processing={finalizeProcessing}
            feedback={finalizeFeedback}
            onCloseFeedback={() => setFinalizeFeedback({ ...finalizeFeedback, message: null })}
            isPartial={isPartial}
        >
            {groupName && (
                <div style={{ marginBottom: '16px', padding: '8px', backgroundColor: '#EFF6FF', borderRadius: '4px', border: '1px solid #BFDBFE' }}>
                    <span style={{ fontSize: '0.75rem', fontWeight: 800, color: '#1E40AF', textTransform: 'uppercase' }}>Confirmando recebimento para:</span>
                    <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#1E3A8A' }}>{groupName}</div>
                </div>
            )}
            {(() => {
                const hasReceipt = attachments?.some(
                    (a: any) => a.attachmentTypeCode === 'RECEIPT' && !a.isDeleted && a.requestPoGroupId === groupId
                );
                
                if (hasReceipt) {
                    const receipt = attachments!.find((a: any) => a.attachmentTypeCode === 'RECEIPT' && !a.isDeleted && a.requestPoGroupId === groupId);
                    return (
                        <div>
                            <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Recibo / Documento Fiscal
                            </label>
                            <div style={{
                                display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 16px',
                                backgroundColor: '#F8FAFC', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)'
                            }}>
                                <FileText size={18} style={{ color: '#10B981' }} />
                                <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={receipt?.fileName}>
                                    {receipt?.fileName}
                                </span>
                                <CheckCircle size={16} style={{ color: '#10B981' }} />
                            </div>
                        </div>
                    );
                }
                
                return (
                    <div>
                        <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                            Anexar Recibo do Fornecedor (obrigatório)
                        </label>
                        {!finalizeReceiptFile ? (
                            <label style={{
                                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                                gap: '12px', padding: '24px', backgroundColor: '#F8FAFC', border: '2px dashed var(--color-border)',
                                borderRadius: 'var(--radius-sm)', cursor: 'pointer', transition: 'all 0.2s ease'
                            }}
                            onMouseOver={(e) => e.currentTarget.style.borderColor = 'var(--color-primary)'}
                            onMouseOut={(e) => e.currentTarget.style.borderColor = 'var(--color-border)'}
                            >
                                <Upload size={24} style={{ color: 'var(--color-primary)' }} />
                                <span style={{ fontSize: '0.875rem', fontWeight: 700, color: 'var(--color-primary)' }}>Clique para selecionar o recibo</span>
                                <input 
                                    type="file" 
                                    style={{ display: 'none' }} 
                                    onChange={(e) => {
                                        if (e.target.files && e.target.files.length > 0) {
                                            setFinalizeReceiptFile(e.target.files[0]);
                                        }
                                    }}
                                />
                            </label>
                        ) : (
                            <div style={{
                                display: 'flex', alignItems: 'center', gap: '12px', padding: '12px 16px',
                                backgroundColor: '#F8FAFC', border: '1px solid var(--color-primary)', borderRadius: 'var(--radius-sm)'
                            }}>
                                <FileText size={18} style={{ color: 'var(--color-primary)' }} />
                                <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontSize: '0.875rem', fontWeight: 700, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={finalizeReceiptFile.name}>
                                        {finalizeReceiptFile.name}
                                    </span>
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>
                                        {(finalizeReceiptFile.size / 1024 / 1024).toFixed(2)} MB
                                    </span>
                                </div>
                                <button
                                    onClick={() => setFinalizeReceiptFile(null)}
                                    style={{ background: 'none', border: 'none', color: '#EF4444', cursor: 'pointer', padding: '4px' }}
                                    title="Remover arquivo"
                                >
                                    <Trash2 size={16} />
                                </button>
                            </div>
                        )}
                    </div>
                );
            })()}
        </ApprovalModal>
    );
};
