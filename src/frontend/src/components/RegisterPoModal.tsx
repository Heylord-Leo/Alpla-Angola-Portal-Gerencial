import React, { useState, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, FileText, X } from 'lucide-react';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';
import { Z_INDEX } from '../constants/ui';
import { api } from '../lib/api';

interface RegisterPoModalProps {
    show: boolean;
    requestId: string;
    onClose: () => void;
    onSuccess: (message: string) => void;
}

export function RegisterPoModal({ show, requestId, onClose, onSuccess }: RegisterPoModalProps) {
    const [file, setFile] = useState<File | null>(null);
    const [comment, setComment] = useState('');
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const fileInputRef = useRef<HTMLInputElement>(null);

    if (!show) return null;

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files.length > 0) {
            const selectedFile = e.target.files[0];
            const ext = selectedFile.name.split('.').pop()?.toLowerCase();
            if (ext !== 'pdf') {
                setFeedback({ type: 'error', message: 'Apenas arquivos PDF são permitidos para envio de P.O.' });
                setFile(null);
                if (fileInputRef.current) fileInputRef.current.value = '';
                return;
            }
            if (selectedFile.size > 10 * 1024 * 1024) { // 10MB limit
                setFeedback({ type: 'error', message: 'O arquivo não pode ser maior que 10MB.' });
                setFile(null);
                if (fileInputRef.current) fileInputRef.current.value = '';
                return;
            }
            setFile(selectedFile);
            setFeedback({ type: 'error', message: null });
        }
    };

    const handleConfirm = async () => {
        if (!file) {
            setFeedback({ type: 'error', message: 'É obrigatório anexar o documento de P.O em formato PDF.' });
            return;
        }

        setProcessing(true);
        setFeedback({ type: 'error', message: null });

        try {
            // 1. Upload the file first
            await api.attachments.upload(requestId, [file], 'PO');

            // 2. Register PO status transition
            const result = await api.requests.registerPo(requestId, comment);

            onSuccess(result.message || 'P.O registrada com sucesso!');
            
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Não foi possível registrar a P.O. Tente novamente.' });
            setProcessing(false);
        }
    };

    const handleClearFile = () => {
        setFile(null);
        if (fileInputRef.current) {
            fileInputRef.current.value = '';
        }
    };

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
                        zIndex: Z_INDEX.MODAL as any,
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
                        <h2 style={{ fontSize: '1.5rem', fontWeight: 900, marginBottom: '16px', color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Emitir P.O Sistêmica
                        </h2>

                        <p style={{ marginBottom: '24px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.95rem' }}>
                            O pedido encontra-se <strong>APROVADO</strong>. Para concluir a emissão da Purchase Order, faça o upload do documento final assinado e confirme o registro. O documento será associado permanentemente ao pedido e o status será atualizado.
                        </p>

                        <div style={{ marginBottom: '24px' }}>
                            <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Documento da P.O (Obrigatório, apenas PDF)
                            </label>
                            
                            {!file ? (
                                <div 
                                    style={{
                                        border: '2px dashed var(--color-primary)',
                                        borderRadius: 'var(--radius-md)',
                                        padding: '32px',
                                        textAlign: 'center',
                                        backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)',
                                        cursor: 'pointer',
                                        transition: 'all 0.2s ease',
                                        position: 'relative'
                                    }}
                                    onClick={() => fileInputRef.current?.click()}
                                >
                                    <input 
                                        type="file" 
                                        accept=".pdf"
                                        onChange={handleFileChange}
                                        style={{ display: 'none' }}
                                        ref={fileInputRef}
                                    />
                                    <Upload size={32} color="var(--color-primary)" style={{ margin: '0 auto 16px', opacity: 0.8 }} />
                                    <div style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '1rem', marginBottom: '4px' }}>
                                        Clique para selecionar o PDF
                                    </div>
                                    <div style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>
                                        Limite de 10MB
                                    </div>
                                </div>
                            ) : (
                                <div style={{ 
                                    display: 'flex', alignItems: 'center', justifyContent: 'space-between', 
                                    padding: '16px 20px', backgroundColor: 'var(--color-bg-page)', 
                                    border: '2px solid var(--color-status-success)', borderRadius: 'var(--radius-md)'
                                }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                        <FileText size={24} color="var(--color-status-success)" />
                                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                                            <span style={{ fontWeight: 800, color: 'var(--color-text-main)', fontSize: '0.9rem' }}>{file.name}</span>
                                            <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>{(file.size / 1024 / 1024).toFixed(2)} MB</span>
                                        </div>
                                    </div>
                                    <button 
                                        onClick={handleClearFile}
                                        style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', display: 'flex', color: 'var(--color-status-red)' }}
                                    >
                                        <X size={20} />
                                    </button>
                                </div>
                            )}
                        </div>

                        <div style={{ marginBottom: '32px' }}>
                            <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Comentário (opcional)
                            </label>
                            <textarea
                                value={comment}
                                onChange={(e) => setComment(e.target.value)}
                                placeholder="Digite alguma observação sobre a emissão, se necessário..."
                                rows={3}
                                style={{
                                    ...inputStyle,
                                    height: 'auto',
                                    padding: '16px'
                                }}
                            />
                        </div>

                        <Feedback
                            type={feedback.type}
                            message={feedback.message}
                            onClose={() => setFeedback({ ...feedback, message: null })}
                        />

                        <div style={{ display: 'flex', gap: '16px', justifyContent: 'center' }}>
                            <button
                                onClick={onClose}
                                disabled={processing}
                                style={{
                                    height: '48px', padding: '0 32px', background: 'none', border: '2px solid var(--color-border-heavy)',
                                    cursor: processing ? 'not-allowed' : 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem',
                                    opacity: processing ? 0.5 : 1
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={processing || !file}
                                onClick={handleConfirm}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: 'var(--color-primary)',
                                    color: '#fff',
                                    border: 'none',
                                    cursor: processing || !file ? 'not-allowed' : 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: '4px 4px 0 var(--color-accent)',
                                    fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem',
                                    opacity: processing || !file ? 0.7 : 1
                                }}
                            >
                                {processing ? 'PROCESSANDO...' : 'REGISTRAR P.O'}
                            </button>
                        </div>
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
