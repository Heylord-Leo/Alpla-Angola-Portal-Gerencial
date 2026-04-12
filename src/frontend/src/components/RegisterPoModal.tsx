import React, { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, FileText, X, AlertTriangle, CheckCircle, Search, Save } from 'lucide-react';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';
import { Z_INDEX } from '../constants/ui';
import { api } from '../lib/api';
import { formatCurrencyAO } from '../lib/utils';

interface RegisterPoModalProps {
    show: boolean;
    requestId: string;
    requestData: {
        totalAmount: number;
        supplierName: string;
        currencyCode: string;
    };
    onClose: () => void;
    onSuccess: (message: string) => void;
}

export function RegisterPoModal({ show, requestId, requestData, onClose, onSuccess }: RegisterPoModalProps) {
    const [file, setFile] = useState<File | null>(null);
    const [comment, setComment] = useState('');
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const fileInputRef = useRef<HTMLInputElement>(null);

    // OCR Validation State
    const [ocrLoading, setOcrLoading] = useState(false);
    const [ocrResult, setOcrResult] = useState<{
        hasMismatches: boolean;
        details: string[];
        extractedTotal?: number;
        extractedSupplier?: string;
    } | null>(null);
    const [overrideConfirmed, setOverrideConfirmed] = useState(false);

    // Reset state on open/close
    useEffect(() => {
        if (!show) {
            setFile(null);
            setComment('');
            setOcrResult(null);
            setOverrideConfirmed(false);
            setFeedback({ type: 'error', message: null });
        }
    }, [show]);

    if (!show) return null;

    // Helper: robust supplier name similarity (case-insensitive, accent-insensitive, token-based)
    const normalizeForComparison = (s: string) =>
        s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();

    const calculateSimilarity = (a: string, b: string) => {
        const na = normalizeForComparison(a);
        const nb = normalizeForComparison(b);

        // Exact match after normalization (covers pure case differences)
        const stripped1 = na.replace(/[^a-z0-9]/g, '');
        const stripped2 = nb.replace(/[^a-z0-9]/g, '');
        if (stripped1 === stripped2) return 1.0;
        if (stripped1.includes(stripped2) || stripped2.includes(stripped1)) return 0.9;

        // Token-based overlap: compare word-by-word
        const tokens1 = na.replace(/[^a-z0-9\s]/g, '').split(/\s+/).filter(t => t.length > 1);
        const tokens2 = nb.replace(/[^a-z0-9\s]/g, '').split(/\s+/).filter(t => t.length > 1);
        if (tokens1.length === 0 || tokens2.length === 0) return 0.0;

        const set2 = new Set(tokens2);
        const matchCount = tokens1.filter(t => set2.has(t)).length;
        return matchCount / Math.max(tokens1.length, tokens2.length);
    };

    const runOcrValidation = async (selectedFile: File) => {
        setOcrLoading(true);
        setOcrResult(null);
        setOverrideConfirmed(false);
        setFeedback({ type: 'error', message: null });

        try {
            // directOcrExtract handles generic layout detection & extraction
            // Response shape: { integration: { headerSuggestions: { supplierName: { value }, grandTotal: { value } } } }
            const ocrData = await api.requests.directOcrExtract(selectedFile);
            const suggestions = ocrData?.integration?.headerSuggestions;
            
            const extractedTotal = Number(suggestions?.grandTotal?.value) || 0;
            const extractedSupplier = suggestions?.supplierName?.value || '';

            const mismatches: string[] = [];
            let hasMismatches = false;

            // 1. Math check (tolerance 1 unit due to weird IVA roundings on ERPs)
            if (Math.abs(extractedTotal - requestData.totalAmount) > 1.0) {
                mismatches.push(`Total divergente: Identificado ${extractedTotal.toFixed(2)} (Esperado: ${requestData.totalAmount.toFixed(2)})`);
                hasMismatches = true;
            }

            // 2. Similarity supplier
            if (extractedSupplier) {
                const sim = calculateSimilarity(extractedSupplier, requestData.supplierName);
                if (sim < 0.6) {
                    mismatches.push(`Fornecedor divergente: Identificado "${extractedSupplier}" (Esperado: "${requestData.supplierName}")`);
                    hasMismatches = true;
                }
            } else {
                mismatches.push(`Fornecedor não identificado claramente no documento.`);
                hasMismatches = true;
            }

            setOcrResult({
                hasMismatches,
                details: mismatches,
                extractedTotal,
                extractedSupplier
            });

        } catch (err: any) {
             setOcrResult({
                hasMismatches: true,
                details: ["Falha ao extrair dados do OCR. Documento ilegível ou em formato não suportado."],
             });
        } finally {
            setOcrLoading(false);
        }
    };

    const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
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
            
            // Trigger OCR immediately
            await runOcrValidation(selectedFile);
        }
    };

    const handleConfirm = async () => {
        if (!file) {
            setFeedback({ type: 'error', message: 'É obrigatório anexar o documento de P.O em formato PDF.' });
            return;
        }

        if (ocrResult?.hasMismatches && !overrideConfirmed) {
            setFeedback({ type: 'error', message: 'Confirme estar ciente das divergências antes de registrar.' });
            return;
        }

        if (ocrResult?.hasMismatches && !comment.trim()) {
            setFeedback({ type: 'error', message: 'Um comentário justificativo é obrigatório quando há divergências.' });
            return;
        }

        setProcessing(true);
        setFeedback({ type: 'error', message: null });

        try {
            // 1. Upload the file first
            await api.attachments.upload(requestId, [file], 'PO');

            // 2. Register PO status transition
            const result = await api.requests.registerPo(requestId, {
                comment,
                hasMismatches: ocrResult?.hasMismatches || false,
                overrideConfirmed,
                mismatchDetails: ocrResult?.details ? ocrResult.details.join('; ') : ''
            });

            onSuccess(result.message || 'P.O registrada com sucesso!');
            
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Não foi possível registrar a P.O. Tente novamente.' });
            setProcessing(false);
        }
    };

    const handleClearFile = () => {
        setFile(null);
        setOcrResult(null);
        setOverrideConfirmed(false);
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
                            maxWidth: '700px',
                            width: '100%',
                            border: '4px solid var(--color-border-heavy)',
                            boxShadow: 'var(--shadow-brutal)',
                            maxHeight: '90vh',
                            overflowY: 'auto'
                        }}
                    >
                        <h2 style={{ fontSize: '1.5rem', fontWeight: 900, marginBottom: '8px', color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Emitir P.O Sistêmica
                        </h2>

                        <p style={{ marginBottom: '24px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.95rem' }}>
                            Anexe o PDF final gerado (ex: ERP Primavera) para verificação automática e registo na plataforma.
                        </p>

                        <div style={{ display: 'flex', gap: '16px', marginBottom: '24px', padding: '16px', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                            <div style={{ flex: 1 }}>
                                <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Valor Esperado</div>
                                <div style={{ fontSize: '1.1rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                    {formatCurrencyAO(requestData.totalAmount)} {requestData.currencyCode}
                                </div>
                            </div>
                            <div style={{ flex: 1 }}>
                                <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Fornecedor Esperado</div>
                                <div style={{ fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                    {requestData.supplierName}
                                </div>
                            </div>
                        </div>

                        <div style={{ marginBottom: '24px' }}>
                            <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Documento da P.O (Apenas PDF)
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
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                    <div style={{ 
                                        display: 'flex', alignItems: 'center', justifyContent: 'space-between', 
                                        padding: '16px 20px', backgroundColor: 'var(--color-bg-page)', 
                                        border: '2px solid var(--color-text-main)', borderRadius: 'var(--radius-md)'
                                    }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                            <FileText size={24} color="var(--color-text-main)" />
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

                                    {/* OCR Block */}
                                    {ocrLoading ? (
                                        <motion.div
                                            initial={{ opacity: 0 }}
                                            animate={{ opacity: 1 }}
                                            exit={{ opacity: 0 }}
                                            transition={{ duration: 0.2 }}
                                            style={{
                                                padding: '32px 24px',
                                                backgroundColor: '#f0f9ff',
                                                border: '2px solid var(--color-primary)',
                                                borderRadius: '8px',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                gap: '12px'
                                            }}
                                        >
                                            <motion.div
                                                animate={{ rotate: [0, 360] }}
                                                transition={{ repeat: Infinity, ease: "linear", duration: 1.5 }}
                                                style={{ display: 'flex' }}
                                            >
                                                <Search size={32} style={{ color: 'var(--color-primary)' }} />
                                            </motion.div>

                                            <span style={{ fontWeight: 800, fontSize: '0.9rem', color: 'var(--color-primary)', letterSpacing: '0.05em', textTransform: 'uppercase' }}>
                                                Validando com IA
                                            </span>

                                            <div style={{ width: '120px', height: '4px', backgroundColor: 'var(--color-border)', borderRadius: '2px', overflow: 'hidden' }}>
                                                <motion.div
                                                    animate={{ x: ['-100%', '200%'] }}
                                                    transition={{ repeat: Infinity, ease: 'easeInOut', duration: 1.5 }}
                                                    style={{ width: '50%', height: '100%', backgroundColor: 'var(--color-primary)' }}
                                                />
                                            </div>

                                            <div style={{ fontSize: '0.8rem', color: '#334155', fontWeight: 600, display: 'flex', gap: '2px' }}>
                                                <span>A validar documento com Inteligência Artificial</span>
                                                <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.5, 1] }}>.</motion.span>
                                                <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.75, 1], delay: 0.2 }}>.</motion.span>
                                                <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.5, 1], delay: 0.4 }}>.</motion.span>
                                            </div>
                                        </motion.div>
                                    ) : ocrResult ? (
                                        ocrResult.hasMismatches ? (
                                            <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} style={{ padding: '24px', backgroundColor: '#fff7ed', border: '2px solid #f97316', borderRadius: '8px' }}>
                                                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', marginBottom: '16px' }}>
                                                    <AlertTriangle size={24} color="#f97316" style={{ flexShrink: 0 }} />
                                                    <div>
                                                        <h4 style={{ margin: '0 0 4px 0', color: '#c2410c', fontSize: '1rem', fontWeight: 800 }}>Divergência Detectada</h4>
                                                        <p style={{ margin: 0, color: '#9a3412', fontSize: '0.85rem', fontWeight: 600 }}>Os dados extraídos do documento PDF não coincidem com o pedido atual:</p>
                                                    </div>
                                                </div>
                                                <ul style={{ margin: '0 0 20px 0', paddingLeft: '36px', color: '#9a3412', fontSize: '0.85rem', fontWeight: 700 }}>
                                                    {ocrResult.details.map((d, i) => <li key={i} style={{ marginBottom: '4px' }}>{d}</li>)}
                                                </ul>

                                                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#ffedd5', padding: '16px', borderRadius: '4px' }}>
                                                    <input 
                                                        type="checkbox" 
                                                        id="overrideConfirm"
                                                        checked={overrideConfirmed}
                                                        onChange={(e) => setOverrideConfirmed(e.target.checked)}
                                                        style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }}
                                                    />
                                                    <label htmlFor="overrideConfirm" style={{ cursor: 'pointer', color: '#9a3412', fontSize: '0.85rem', fontWeight: 700, lineHeight: 1.4 }}>
                                                        Estou ciente das divergências listadas acima e confirmo a emissão da P.O. sob minha responsabilidade. (Esta ação será registrada em sistema).
                                                    </label>
                                                </div>
                                            </motion.div>
                                        ) : (
                                            <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} style={{ padding: '20px', backgroundColor: '#f0fdf4', border: '2px solid #22c55e', borderRadius: '8px', display: 'flex', gap: '12px', alignItems: 'center' }}>
                                                <CheckCircle size={24} color="#16a34a" />
                                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                    <span style={{ fontWeight: 800, color: '#16a34a', fontSize: '0.95rem' }}>Documento Validado</span>
                                                    <span style={{ fontWeight: 600, color: '#15803d', fontSize: '0.8rem' }}>Os valores do PDF correspondem à cotação aprovada.</span>
                                                </div>
                                            </motion.div>
                                        )
                                    ) : null}
                                </div>
                            )}
                        </div>

                        <div style={{ marginBottom: '32px' }}>
                            <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Comentário {!ocrResult?.hasMismatches ? '(opcional)' : '(Obrigatório)'}
                            </label>
                            <textarea
                                value={comment}
                                onChange={(e) => setComment(e.target.value)}
                                placeholder={ocrResult?.hasMismatches ? "Justifique o motivo de ignorar a divergência..." : "Digite alguma observação sobre a emissão, se necessário..."}
                                rows={3}
                                style={{
                                    ...inputStyle,
                                    height: 'auto',
                                    padding: '16px',
                                    borderColor: ocrResult?.hasMismatches && !comment.trim() ? '#f97316' : 'var(--color-border)'
                                }}
                            />
                        </div>

                        <Feedback
                            type={feedback.type}
                            message={feedback.message}
                            onClose={() => setFeedback({ ...feedback, message: null })}
                        />

                        <div style={{ display: 'flex', gap: '16px', justifyContent: 'flex-end' }}>
                            <button
                                onClick={onClose}
                                disabled={processing}
                                style={{
                                    height: '48px', padding: '0 24px', background: 'none', border: '2px solid var(--color-border-heavy)',
                                    cursor: processing ? 'not-allowed' : 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem',
                                    opacity: processing ? 0.5 : 1
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={processing || !file || ocrLoading || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())}
                                onClick={handleConfirm}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: 'var(--color-primary)',
                                    color: '#fff',
                                    border: 'none',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px',
                                    cursor: (processing || !file || ocrLoading || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 'not-allowed' : 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: '4px 4px 0 var(--color-accent)',
                                    fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem',
                                    opacity: (processing || !file || ocrLoading || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 0.7 : 1
                                }}
                            >
                                <Save size={18} />
                                {processing ? 'PROCESSANDO...' : 'REGISTRAR P.O'}
                            </button>
                        </div>
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
