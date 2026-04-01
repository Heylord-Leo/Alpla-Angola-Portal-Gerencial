import { motion, AnimatePresence } from 'framer-motion';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';

export type ApprovalActionType = 
    | 'APPROVE' 
    | 'REJECT' 
    | 'REQUEST_ADJUSTMENT' 
    | 'SAVE' 
    | 'SUBMIT' 
    | 'DELETE' 
    | 'DELETE_ITEM' 
    | 'DELETE_QUOTATION'
    | 'REGISTER_PO' 
    | 'SCHEDULE_PAYMENT' 
    | 'COMPLETE_PAYMENT' 
    | 'MOVE_TO_RECEIPT' 
    | 'FINALIZE' 
    | 'COMPLETE_QUOTATION'
    | 'ITEM_STATUS_CHANGE'
    | 'CANCEL_REQUEST'
    | 'DUPLICATE_REQUEST'
    | 'SAVE_QUOTATION_OCR'
    | 'SAVE_QUOTATION_MANUAL'
    | null;

interface ApprovalModalProps {
    show: boolean;
    type: ApprovalActionType;
    status?: string | null;
    isReworkStatus?: boolean;
    onClose: () => void;
    onConfirm: (action: ApprovalActionType) => void;
    comment: string;
    setComment: (comment: string) => void;
    processing: boolean;
    feedback: { type: FeedbackType; message: string | null };
    onCloseFeedback: () => void;
    isLastItem?: boolean;
    isPartial?: boolean;
    selectedQuotationName?: string | null;
}

export function ApprovalModal({
    show,
    type,
    status,
    isReworkStatus,
    onClose,
    onConfirm,
    comment,
    setComment,
    processing,
    feedback,
    onCloseFeedback,
    isLastItem,
    isPartial,
    selectedQuotationName
}: ApprovalModalProps) {
    if (!show) return null;

    const getTitle = () => {
        switch (type) {
            case 'APPROVE': return 'Confirmar Aprovação';
            case 'REJECT': return 'Confirmar Rejeição';
            case 'SAVE': return 'Confirmar salvamento';
            case 'SUBMIT': return isReworkStatus ? 'Confirmar Reenvio' : 'Confirmar Envio';
            case 'DELETE':
            case 'DELETE_ITEM':
            case 'DELETE_QUOTATION': return 'Confirmar exclusão';
            case 'REGISTER_PO': return 'Registrar P.O';
            case 'SCHEDULE_PAYMENT': return 'Agendar Pagamento';
            case 'COMPLETE_PAYMENT': return 'Confirmar Pagamento';
            case 'MOVE_TO_RECEIPT': return 'Mover para Recibo';
            case 'FINALIZE': return 'Finalizar Pedido';
            case 'COMPLETE_QUOTATION': return 'Concluir Cotação';
            case 'ITEM_STATUS_CHANGE': return 'Confirmar Alteração de Status';
            case 'CANCEL_REQUEST': return 'Cancelar Pedido';
            case 'DUPLICATE_REQUEST': return 'Duplicar Pedido';
            case 'SAVE_QUOTATION_OCR':
            case 'SAVE_QUOTATION_MANUAL': return 'Confirmar salvamento';
            default: return '';
        }
    };

    const getDescription = () => {
        switch (type) {
            case 'APPROVE':
                const baseDesc = status === 'WAITING_AREA_APPROVAL' 
                    ? 'Deseja aprovar este pedido e enviá-lo para a fila de aprovação final?' 
                    : 'Deseja realizar a aprovação final deste pedido?';
                return selectedQuotationName 
                    ? `${baseDesc}\n\nVencedora selecionada: ${selectedQuotationName}`
                    : baseDesc;
            case 'REJECT': return 'Tem certeza que deseja rejeitar este pedido? Esta ação é irreversível.';
            case 'SAVE': return 'Deseja salvar as alterações realizadas neste pedido?';
            case 'SUBMIT': return isReworkStatus ? 'Deseja reenviar este pedido para o fluxo de aprovação?' : 'Deseja enviar este pedido para o fluxo de aprovação?';
            case 'DELETE': return 'Tem certeza de que deseja excluir este rascunho? Esta ação não poderá ser desfeita.';
            case 'DELETE_ITEM': return 'Tem certeza que deseja excluir este item?';
            case 'DELETE_QUOTATION': return 'Tem certeza que deseja excluir esta cotação?';
            case 'REGISTER_PO': return 'Deseja confirmar que a P.O deste pedido foi emitida?';
            case 'SCHEDULE_PAYMENT': return 'Deseja confirmar que o pagamento deste pedido foi agendado?';
            case 'COMPLETE_PAYMENT': return 'Deseja confirmar que o pagamento deste pedido foi realizado?';
            case 'MOVE_TO_RECEIPT': return 'Deseja mover este pedido para a fase de aguardando recibo?';
            case 'FINALIZE': 
                return isPartial 
                    ? 'Atenção: Existem itens pendentes. Ao finalizar, o pedido será movido para o estágio de "Acompanhamento" operacional.'
                    : 'Deseja confirmar o recebimento e finalizar este pedido permanentemente?';
            case 'COMPLETE_QUOTATION': return 'Deseja confirmar que o processo de cotação foi concluído e enviar para aprovação?';
            case 'REQUEST_ADJUSTMENT': return 'Informe o que precisa ser ajustado no pedido pelo solicitante.';
            case 'ITEM_STATUS_CHANGE': 
                return isLastItem 
                    ? 'Este é o último item pendente do pedido. Ao confirmar, todos os itens estarão recebidos e o pedido será finalizado.' 
                    : 'Deseja confirmar a alteração de status deste item? Esta ação requer uma observação obrigatória.';
            case 'CANCEL_REQUEST': return 'Tem certeza que deseja cancelar este pedido? Informe o motivo abaixo.';
            case 'DUPLICATE_REQUEST': return 'Deseja criar uma cópia deste pedido? Um novo rascunho será gerado com os mesmos dados básicos e itens, sem copiar o histórico ou anexos.';
            case 'SAVE_QUOTATION_OCR': return 'A extração de informações via OCR não é 100% precisa. Você verificou todas as informações?';
            case 'SAVE_QUOTATION_MANUAL': return 'Tem certeza de que todas as informações inseridas estão corretas?';
            default: return '';
        }
    };

    const isCommentRequired = type === 'REJECT' || type === 'REQUEST_ADJUSTMENT' || type === 'ITEM_STATUS_CHANGE' || type === 'CANCEL_REQUEST';
    const showCommentField = [
        'APPROVE', 'REJECT', 'REQUEST_ADJUSTMENT', 'REGISTER_PO', 
        'SCHEDULE_PAYMENT', 'COMPLETE_PAYMENT', 'MOVE_TO_RECEIPT', 
        'FINALIZE', 'COMPLETE_QUOTATION', 'ITEM_STATUS_CHANGE', 'CANCEL_REQUEST'
    ].includes(type || '');

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        backgroundColor: 'var(--color-bg-page)',
        border: '2px solid var(--color-border)',
        borderRadius: 'var(--radius-sm)',
        fontSize: '0.875rem',
        fontWeight: 600,
        color: 'var(--color-text-main)',
        transition: 'all 0.2s ease',
        fontFamily: 'inherit'
    };

    return (
        <DropdownPortal>
            <AnimatePresence>
                <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    style={{
                        position: 'fixed',
                        top: 0,
                        left: 0,
                        right: 0,
                        bottom: 0,
                        backgroundColor: 'rgba(0,0,0,0.8)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        zIndex: 1000,
                        padding: '20px'
                    }}
                >
                    <motion.div
                        initial={{ scale: 0.9, y: 20 }}
                        animate={{ scale: 1, y: 0 }}
                        className="modal-content"
                        style={{
                            backgroundColor: 'var(--color-bg-surface)',
                            padding: '40px',
                            borderRadius: 'var(--radius-md)',
                            maxWidth: '600px',
                            width: '100%',
                            border: '4px solid var(--color-border-heavy)',
                            boxShadow: 'var(--shadow-brutal)'
                        }}
                    >
                        <h2 style={{ fontSize: '1.5rem', fontWeight: 900, marginBottom: '24px', color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            {getTitle()}
                        </h2>

                        <p style={{ marginBottom: '24px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.95rem' }}>
                            {getDescription()}
                        </p>

                        {showCommentField && (
                            <div style={{ marginBottom: '32px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    {isCommentRequired ? 'Motivo / Observações (obrigatório)' : 'Comentário (opcional)'}
                                </label>
                                <textarea
                                    name="comment"
                                    value={comment}
                                    onChange={(e) => setComment(e.target.value)}
                                    placeholder="Digite aqui..."
                                    rows={4}
                                    style={{
                                        ...inputStyle,
                                        height: 'auto',
                                        padding: '16px'
                                    }}
                                />
                            </div>
                        )}

                        <Feedback
                            type={feedback.type}
                            message={feedback.message}
                            onClose={onCloseFeedback}
                        />

                        <div style={{ display: 'flex', gap: '16px', justifyContent: 'center' }}>
                            <button
                                onClick={onClose}
                                style={{
                                    height: '48px', padding: '0 32px', background: 'none', border: '2px solid var(--color-border-heavy)',
                                    cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem'
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={processing}
                                onClick={() => onConfirm(type)}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: (type === 'REJECT' || type === 'DELETE' || type === 'DELETE_ITEM' || type === 'DELETE_QUOTATION' || type === 'CANCEL_REQUEST') ? 'var(--color-status-red)' : 'var(--color-primary)',
                                    color: '#fff',
                                    border: 'none',
                                    cursor: 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: '4px 4px 0 var(--color-accent)',
                                    fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem',
                                    opacity: processing ? 0.7 : 1
                                }}
                            >
                                {processing ? 'PROCESSANDO...' : (isLastItem && type === 'ITEM_STATUS_CHANGE' ? 'OK, ENCERRAR PEDIDO, TODOS ITENS RECEBIDOS' : 'CONFIRMAR')}
                            </button>
                        </div>
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
