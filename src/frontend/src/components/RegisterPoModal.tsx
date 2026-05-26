import React, { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, FileText, X, AlertTriangle, CheckCircle, Save, ShieldAlert, ShieldCheck } from 'lucide-react';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';
import { Z_INDEX } from '../constants/ui';
import { api } from '../lib/api';
import { formatCurrencyAO } from '../lib/utils';

interface SupplierRegistrationCheck {
    id: number;
    name: string;
    registrationStatus: string;
    allowed: boolean;
    warning: boolean;
    blocked: boolean;
    message: string;
}

interface RegisterPoModalProps {
    show: boolean;
    requestId: string;
    supplierId?: number | null;
    requestData: {
        totalAmount: number;
        supplierName: string;
        currencyCode: string;
    };
    onClose: () => void;
    onSuccess: (message: string) => void;
}

export function RegisterPoModal({ show, requestId, supplierId, requestData, onClose, onSuccess }: RegisterPoModalProps) {
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

    // Supplier Registration Guard
    const [regCheck, setRegCheck] = useState<SupplierRegistrationCheck | null>(null);
    const [regCheckLoading, setRegCheckLoading] = useState(false);

    // Reset state on open/close
    useEffect(() => {
        if (!show) {
            setFile(null);
            setComment('');
            setOcrResult(null);
            setOverrideConfirmed(false);
            setFeedback({ type: 'error', message: null });
            setRegCheck(null);
        }
    }, [show]);

    // Check supplier registration status when modal opens
    useEffect(() => {
        if (show && supplierId && supplierId > 0) {
            setRegCheckLoading(true);
            api.lookups.checkSupplierRegistration(supplierId, 'po')
                .then((data: SupplierRegistrationCheck) => setRegCheck(data))
                .catch(() => setRegCheck(null))
                .finally(() => setRegCheckLoading(false));
        } else if (show) {
            setRegCheck(null);
            setRegCheckLoading(false);
        }
    }, [show, supplierId]);

    if (!show) return null;

    const isBlocked = regCheck?.blocked === true;
    const isWarning = regCheck?.warning === true && !isBlocked;

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
                            border: '1px solid var(--color-border)',
                            boxShadow: 'var(--shadow-lg)',
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

                        {/* Supplier Registration Guard */}
                        {regCheckLoading && (
                            <div style={{
                                padding: '14px 18px', marginBottom: '20px',
                                backgroundColor: '#f8fafc', border: '1.5px solid var(--color-border)',
                                borderRadius: 'var(--radius-md)', display: 'flex', alignItems: 'center', gap: '12px'
                            }}>
                                <div style={{ width: '18px', height: '18px', borderRadius: '50%', border: '2.5px solid #94a3b8', borderTopColor: 'transparent', animation: 'spin 0.8s linear infinite' }} />
                                <span style={{ fontWeight: 700, fontSize: '0.85rem', color: '#64748b' }}>A verificar estado do fornecedor...</span>
                            </div>
                        )}

                        {isBlocked && (
                            <motion.div
                                initial={{ opacity: 0, y: -8 }}
                                animate={{ opacity: 1, y: 0 }}
                                style={{
                                    padding: '20px 22px', marginBottom: '20px',
                                    backgroundColor: '#fef2f2', border: '2px solid #ef4444',
                                    borderRadius: 'var(--radius-md)'
                                }}
                            >
                                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                                    <ShieldAlert size={24} color="#dc2626" style={{ flexShrink: 0, marginTop: '2px' }} />
                                    <div>
                                        <h4 style={{ margin: '0 0 6px 0', color: '#991b1b', fontSize: '0.95rem', fontWeight: 800 }}>
                                            Emissão de P.O Bloqueada
                                        </h4>
                                        <p style={{ margin: '0 0 10px 0', color: '#b91c1c', fontSize: '0.85rem', fontWeight: 600, lineHeight: 1.5 }}>
                                            {regCheck?.message}
                                        </p>
                                        <div style={{
                                            display: 'inline-flex', alignItems: 'center', gap: '8px',
                                            padding: '6px 14px', borderRadius: '100px',
                                            backgroundColor: '#fee2e2', fontSize: '0.75rem',
                                            fontWeight: 800, color: '#991b1b', textTransform: 'uppercase'
                                        }}>
                                            Estado: {regCheck?.registrationStatus}
                                        </div>
                                    </div>
                                </div>
                            </motion.div>
                        )}

                        {isWarning && (
                            <motion.div
                                initial={{ opacity: 0, y: -8 }}
                                animate={{ opacity: 1, y: 0 }}
                                style={{
                                    padding: '16px 18px', marginBottom: '20px',
                                    backgroundColor: '#fffbeb', border: '1.5px solid #f59e0b',
                                    borderRadius: 'var(--radius-md)', display: 'flex', gap: '12px', alignItems: 'flex-start'
                                }}
                            >
                                <ShieldCheck size={22} color="#d97706" style={{ flexShrink: 0, marginTop: '2px' }} />
                                <div>
                                    <h4 style={{ margin: '0 0 4px 0', color: '#92400e', fontSize: '0.875rem', fontWeight: 800 }}>
                                        Fornecedor em Aprovação
                                    </h4>
                                    <p style={{ margin: 0, color: '#78350f', fontSize: '0.825rem', fontWeight: 600, lineHeight: 1.5 }}>
                                        {regCheck?.message || 'A ficha do fornecedor ainda não está totalmente ativa. A emissão será permitida, mas recomenda-se aguardar a aprovação.'}
                                    </p>
                                </div>
                            </motion.div>
                        )}

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
                                            transition={{ duration: 0.3 }}
                                            style={{
                                                padding: '40px 24px',
                                                backgroundColor: '#f8fafc',
                                                border: '1.5px solid var(--color-border)',
                                                borderRadius: 'var(--radius-md)',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                gap: '20px'
                                            }}
                                        >
                                            {/* Document Verification Animation */}
                                            <div style={{ position: 'relative', width: '120px', height: '100px' }}>
                                                {/* Background document pages (stacked) */}
                                                <motion.div
                                                    animate={{ y: [0, -3, 0] }}
                                                    transition={{ repeat: Infinity, duration: 2.5, ease: 'easeInOut', delay: 0.3 }}
                                                    style={{
                                                        position: 'absolute', left: '8px', top: '12px',
                                                        width: '56px', height: '72px',
                                                        backgroundColor: '#e2e8f0', borderRadius: '4px',
                                                        border: '1px solid #cbd5e1',
                                                        transform: 'rotate(-4deg)',
                                                    }}
                                                >
                                                    {/* Fake text lines */}
                                                    <div style={{ padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: '5px' }}>
                                                        <div style={{ width: '80%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                        <div style={{ width: '60%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                        <div style={{ width: '90%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                        <div style={{ width: '45%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                    </div>
                                                </motion.div>

                                                {/* Front document page */}
                                                <motion.div
                                                    animate={{ y: [0, -4, 0] }}
                                                    transition={{ repeat: Infinity, duration: 2.5, ease: 'easeInOut' }}
                                                    style={{
                                                        position: 'absolute', left: '18px', top: '6px',
                                                        width: '56px', height: '72px',
                                                        backgroundColor: 'white', borderRadius: '4px',
                                                        border: '1.5px solid #94a3b8',
                                                        boxShadow: '0 2px 8px rgba(0,0,0,0.08)',
                                                        overflow: 'hidden',
                                                    }}
                                                >
                                                    {/* Fake text lines */}
                                                    <div style={{ padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: '5px' }}>
                                                        <div style={{ width: '70%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                        <div style={{ width: '90%', height: '3px', backgroundColor: '#94a3b8', borderRadius: '2px' }} />
                                                        <div style={{ width: '55%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                        <div style={{ width: '75%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                        <div style={{ width: '40%', height: '3px', backgroundColor: '#94a3b8', borderRadius: '2px' }} />
                                                        <div style={{ width: '85%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                    </div>
                                                    {/* Scan line sweeping down the document */}
                                                    <motion.div
                                                        animate={{ top: ['-10%', '110%'] }}
                                                        transition={{ repeat: Infinity, duration: 2, ease: 'easeInOut' }}
                                                        style={{
                                                            position: 'absolute', left: 0, right: 0,
                                                            height: '8px',
                                                            background: 'linear-gradient(180deg, transparent, rgba(37, 99, 235, 0.25), transparent)',
                                                        }}
                                                    />
                                                </motion.div>

                                                {/* Magnifying glass */}
                                                <motion.div
                                                    animate={{ 
                                                        x: [0, 12, 0, -8, 0],
                                                        y: [0, 8, 16, 6, 0],
                                                    }}
                                                    transition={{ repeat: Infinity, duration: 3, ease: 'easeInOut' }}
                                                    style={{
                                                        position: 'absolute', right: '6px', top: '4px',
                                                        zIndex: 2,
                                                    }}
                                                >
                                                    <div style={{
                                                        width: '40px', height: '40px',
                                                        borderRadius: '50%',
                                                        border: '3px solid #2563EB',
                                                        backgroundColor: 'rgba(37, 99, 235, 0.06)',
                                                        position: 'relative',
                                                        boxShadow: '0 2px 12px rgba(37, 99, 235, 0.2)',
                                                    }}>
                                                        {/* Magnifying glass handle */}
                                                        <div style={{
                                                            position: 'absolute', bottom: '-10px', right: '-8px',
                                                            width: '4px', height: '16px',
                                                            backgroundColor: '#2563EB',
                                                            borderRadius: '2px',
                                                            transform: 'rotate(-45deg)',
                                                            transformOrigin: 'top center',
                                                        }} />
                                                    </div>
                                                </motion.div>

                                                {/* Subtle check pulse (appears periodically) */}
                                                <motion.div
                                                    animate={{ opacity: [0, 0, 1, 1, 0], scale: [0.5, 0.5, 1, 1, 0.5] }}
                                                    transition={{ repeat: Infinity, duration: 3, times: [0, 0.6, 0.7, 0.85, 1] }}
                                                    style={{
                                                        position: 'absolute', right: '0px', bottom: '0px',
                                                        width: '22px', height: '22px',
                                                        borderRadius: '50%',
                                                        backgroundColor: '#16a34a',
                                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                                        boxShadow: '0 1px 6px rgba(22, 163, 74, 0.4)',
                                                        zIndex: 3,
                                                    }}
                                                >
                                                    <CheckCircle size={14} color="white" strokeWidth={3} />
                                                </motion.div>
                                            </div>

                                            {/* Labels */}
                                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px' }}>
                                                <span style={{ fontWeight: 900, fontSize: '0.8rem', color: '#1e293b', letterSpacing: '0.12em', textTransform: 'uppercase' }}>
                                                    Validando Documento
                                                </span>
                                                <div style={{ fontSize: '0.8rem', color: '#64748b', fontWeight: 600, display: 'flex', gap: '2px' }}>
                                                    <span>A verificar documento com Inteligência Artificial</span>
                                                    <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.5, 1] }}>.</motion.span>
                                                    <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.75, 1], delay: 0.2 }}>.</motion.span>
                                                    <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.5, 1], delay: 0.4 }}>.</motion.span>
                                                </div>
                                            </div>

                                            {/* Progress bar */}
                                            <div style={{ width: '160px', height: '3px', backgroundColor: '#e2e8f0', borderRadius: '2px', overflow: 'hidden' }}>
                                                <motion.div
                                                    animate={{ x: ['-100%', '250%'] }}
                                                    transition={{ repeat: Infinity, ease: 'easeInOut', duration: 1.8 }}
                                                    style={{ width: '40%', height: '100%', backgroundColor: '#2563EB', borderRadius: '2px' }}
                                                />
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
                                    height: '48px', padding: '0 24px', background: 'none', border: '1px solid var(--color-border)',
                                    cursor: processing ? 'not-allowed' : 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem',
                                    opacity: processing ? 0.5 : 1
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={isBlocked || processing || !file || ocrLoading || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())}
                                onClick={handleConfirm}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: isBlocked ? '#94a3b8' : 'var(--color-primary)',
                                    color: '#fff',
                                    border: 'none',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px',
                                    cursor: (isBlocked || processing || !file || ocrLoading || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 'not-allowed' : 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: isBlocked ? 'none' : 'var(--shadow-md)',
                                    fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem',
                                    opacity: (isBlocked || processing || !file || ocrLoading || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 0.7 : 1
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
