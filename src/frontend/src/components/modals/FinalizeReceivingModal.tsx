import React, { useState } from 'react';
import { FileText, Upload, Trash2 } from 'lucide-react';
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

/**
 * v2.229.4 (REQ-17/08/2026-232) — the OPERATIONAL receiving confirmation.
 *
 * Three concepts this modal keeps strictly apart: (A) the Receiving user's explicit ATTESTATION
 * that goods/services were actually received or performed — the authoritative human
 * confirmation, mandatory, persisted verbatim into the ConfirmReceiving history; (B) OPTIONAL
 * supporting evidence (guia de entrega, relatório de serviço, termo de aceitação…) stored as
 * RECEIVING_EVIDENCE, group-linked; (C) the Finance-owned Recibo Fiscal, which this flow never
 * touches. No document is ever required to confirm receiving, and nothing here finalizes the
 * request — completion belongs to the Release 4 backend lifecycle.
 */
export const ATTESTATION_STATEMENT =
    'Atesto que os bens ou serviços deste grupo foram efetivamente recebidos ou executados.';

export const FinalizeReceivingModal: React.FC<FinalizeReceivingModalProps> = ({
    requestId,
    requestNumber,
    groupId,
    groupName,
    attachments: _attachments,
    show,
    onClose,
    onSuccess,
    isPartial = false
}) => {
    const [comment, setComment] = useState('');
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [evidenceFile, setEvidenceFile] = useState<File | null>(null);
    const [attested, setAttested] = useState(false);

    const handleConfirm = async () => {
        if (!attested) {
            setFeedback({ type: 'error', message: 'Confirme a declaração de recebimento/execução para prosseguir.' });
            return;
        }

        try {
            setProcessing(true);
            setFeedback({ type: 'success', message: null });

            // Optional supporting evidence — NEVER mandatory, NEVER a fiscal/legacy receipt.
            if (evidenceFile) {
                try {
                    await api.attachments.upload(requestId, [evidenceFile], 'RECEIVING_EVIDENCE', groupId);
                } catch (uploadErr: any) {
                    const errMsg = uploadErr instanceof Error ? uploadErr.message : (uploadErr?.response?.data?.message || 'Falha ao carregar o comprovativo.');
                    logger.error(`Erro ao carregar comprovativo de recebimento do pedido ${requestNumber} (${groupName || groupId}): ${errMsg}`, uploadErr, 'Global');
                    setFeedback({ type: 'error', message: errMsg });
                    setProcessing(false);
                    return;
                }
            }

            // The attestation statement is part of the audited history, verbatim and uneditable;
            // any user comment follows it explicitly.
            const historyComment = comment.trim()
                ? `${ATTESTATION_STATEMENT} Comentário: ${comment.trim()}`
                : ATTESTATION_STATEMENT;

            await api.requests.confirmReceiving(requestId, groupId, historyComment);

            setComment('');
            setEvidenceFile(null);
            setAttested(false);
            onSuccess('Recebimento confirmado com sucesso.');
        } catch (err: any) {
            const errorMessage = err instanceof Error ? err.message : (err?.response?.data?.message || 'Falha ao confirmar o recebimento.');
            logger.error(`Erro ao confirmar recebimento do pedido ${requestNumber}: ${errorMessage}`, err, 'Global');
            setFeedback({ type: 'error', message: errorMessage });
        } finally {
            setProcessing(false);
        }
    };

    const handleClose = () => {
        setFeedback({ type: 'success', message: null });
        setEvidenceFile(null);
        setComment('');
        setAttested(false);
        onClose();
    };

    return (
        <ApprovalModal
            show={show}
            type="CONFIRM_RECEIVING"
            onClose={handleClose}
            onConfirm={handleConfirm}
            comment={comment}
            setComment={setComment}
            processing={processing}
            feedback={feedback}
            onCloseFeedback={() => setFeedback({ ...feedback, message: null })}
            isPartial={isPartial}
            confirmDisabled={!attested}
        >
            {groupName && (
                <div style={{ marginBottom: '16px', padding: '8px', backgroundColor: '#EFF6FF', borderRadius: '4px', border: '1px solid #BFDBFE' }}>
                    <span style={{ fontSize: '0.75rem', fontWeight: 800, color: '#1E40AF', textTransform: 'uppercase' }}>Confirmando recebimento para:</span>
                    <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#1E3A8A' }}>{groupName}</div>
                </div>
            )}

            {/* ── Optional operational evidence — never a requirement ── */}
            <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', marginBottom: '6px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                    Comprovativo de recebimento/execução <span style={{ fontWeight: 600, textTransform: 'none' }}>(opcional)</span>
                </label>
                <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginBottom: '8px' }}>
                    Quando disponível, anexe uma guia de entrega, relatório de serviço, termo de
                    aceitação ou outro documento que comprove o recebimento ou execução.
                </div>
                {!evidenceFile ? (
                    <label style={{
                        display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                        padding: '14px', backgroundColor: '#F8FAFC', border: '2px dashed var(--color-border)',
                        borderRadius: 'var(--radius-sm)', cursor: 'pointer'
                    }}>
                        <Upload size={18} style={{ color: 'var(--color-primary)' }} />
                        <span style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                            Selecionar documento (opcional)
                        </span>
                        <input
                            type="file"
                            style={{ display: 'none' }}
                            onChange={(e) => {
                                if (e.target.files && e.target.files.length > 0) {
                                    setEvidenceFile(e.target.files[0]);
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
                            <span style={{ fontSize: '0.875rem', fontWeight: 700, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={evidenceFile.name}>
                                {evidenceFile.name}
                            </span>
                            <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>
                                {(evidenceFile.size / 1024 / 1024).toFixed(2)} MB
                            </span>
                        </div>
                        <button
                            onClick={() => setEvidenceFile(null)}
                            style={{ background: 'none', border: 'none', color: '#EF4444', cursor: 'pointer', padding: '4px' }}
                            title="Remover arquivo"
                        >
                            <Trash2 size={16} />
                        </button>
                    </div>
                )}
            </div>

            {/* ── Mandatory attestation — the authoritative human confirmation ── */}
            <label style={{
                display: 'flex', alignItems: 'flex-start', gap: '10px', padding: '12px',
                backgroundColor: attested ? '#F0FDF4' : '#FFFBEB',
                border: `1px solid ${attested ? '#86EFAC' : '#FCD34D'}`,
                borderRadius: 'var(--radius-sm)', cursor: 'pointer', marginBottom: '4px'
            }}>
                <input
                    type="checkbox"
                    checked={attested}
                    onChange={(e) => setAttested(e.target.checked)}
                    style={{ marginTop: '2px', width: '16px', height: '16px', cursor: 'pointer' }}
                />
                <span style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
                    {ATTESTATION_STATEMENT}
                </span>
            </label>
        </ApprovalModal>
    );
};
