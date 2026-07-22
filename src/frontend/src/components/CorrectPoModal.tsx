import React, { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, FileText, X, AlertTriangle, Download, Trash2, Save, CheckCircle } from 'lucide-react';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';
import { Z_INDEX } from '../constants/ui';
import { api } from '../lib/api';
import { formatCurrencyAO } from '../lib/utils';
import { logger } from '../lib/logger';
import { RequestDetailsDto, RequestAttachmentDto } from '../types';
import {
    resolveExpectedSupplierName,
    resolveExpectedTotalAmount,
    resolveSupplierDisplay,
    extractOcrHeaderSuggestions,
    buildOcrMismatchResult,
    resolveTransportErrorDetails,
    CLIENT_PROCESSING_ERROR_MESSAGE,
} from '../lib/ocrPoValidation';

interface CorrectPoModalProps {
    show: boolean;
    requestId: string;
    poGroupId: string;
    onClose: () => void;
    onSuccess: (message: string) => void;
}

export function CorrectPoModal({ show, requestId, poGroupId, onClose, onSuccess }: CorrectPoModalProps) {
    const [loading, setLoading] = useState(true);
    const [requestData, setRequestData] = useState<RequestDetailsDto | null>(null);
    const [returnReason, setReturnReason] = useState<string>('');
    const [returnActor, setReturnActor] = useState<string>('');
    const [returnDate, setReturnDate] = useState<string>('');
    const [existingPo, setExistingPo] = useState<RequestAttachmentDto | null>(null);
    const [poRemoved, setPoRemoved] = useState(false);

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

    // B2P: Payment Condition State
    const [paymentCondition, setPaymentCondition] = useState<string>('');
    const [advancePercent, setAdvancePercent] = useState<number>(50);
    const [paymentConditionSource, setPaymentConditionSource] = useState<'OCR_DETECTED' | 'USER_SELECTED' | ''>('');
    const [ocrDetectedPaymentCondition, setOcrDetectedPaymentCondition] = useState<string | null>(null);

    // Load request details on open
    useEffect(() => {
        if (!show) {
            setLoading(true);
            setRequestData(null);
            setReturnReason('');
            setReturnActor('');
            setReturnDate('');
            setExistingPo(null);
            setPoRemoved(false);
            setFile(null);
            setComment('');
            setOcrResult(null);
            setOverrideConfirmed(false);
            setFeedback({ type: 'error', message: null });
            setPaymentCondition('');
            setAdvancePercent(50);
            setPaymentConditionSource('');
            setOcrDetectedPaymentCondition(null);
            return;
        }

        async function fetchData() {
            try {
                setLoading(true);
                const details = await api.requests.get(requestId);
                setRequestData(details);

                // Find the latest FINANCE_RETURN_ADJUSTMENT in status history
                const returnEvent = details.statusHistory
                    ?.filter((h: any) => h.actionTaken === 'FINANCE_RETURN_ADJUSTMENT')
                    .sort((a: any, b: any) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime())[0];

                if (returnEvent) {
                    // Strip the prefix for clean display
                    let reason = returnEvent.comment || '';
                    if (reason.startsWith('Devolvido por Finanças para ajuste: ')) {
                        reason = reason.substring('Devolvido por Finanças para ajuste: '.length);
                    }
                    setReturnReason(reason);
                    setReturnActor(returnEvent.actorName || '');
                    setReturnDate(returnEvent.createdAtUtc || '');
                }

                // Find the existing PO attachment for this group
                const poAttachment = details.attachments
                    ?.filter((a: RequestAttachmentDto) => a.attachmentTypeCode === 'PO' && a.requestPoGroupId === poGroupId)
                    .sort((a: RequestAttachmentDto, b: RequestAttachmentDto) => 
                        new Date(b.uploadedAtUtc).getTime() - new Date(a.uploadedAtUtc).getTime()
                    )[0] || null;
                setExistingPo(poAttachment);

                // Load existing payment condition
                if (details.paymentConditionCode) {
                    setPaymentCondition(details.paymentConditionCode);
                    setAdvancePercent(details.advancePaymentPercent ?? 50);
                    setPaymentConditionSource('USER_SELECTED');
                }

            } catch (err: any) {
                setFeedback({ type: 'error', message: 'Falha ao carregar detalhes do pedido.' });
            } finally {
                setLoading(false);
            }
        }

        fetchData();
    }, [show, requestId]);

    if (!show) return null;

    // Null-safe expected values - same pattern as RegisterPoModal.tsx. expectedSupplierName is the
    // COMPARISON value (null when unset); expectedSupplierDisplay is UI-only text.
    const totalAmount = resolveExpectedTotalAmount(requestData?.estimatedTotalAmount);
    const expectedSupplierName = resolveExpectedSupplierName(requestData?.supplierName);
    const expectedSupplierDisplay = resolveSupplierDisplay(expectedSupplierName);
    const currencyCode = requestData?.currencyCode || 'AOA';

    const runOcrValidation = async (selectedFile: File) => {
        setOcrLoading(true);
        setOcrResult(null);
        setOverrideConfirmed(false);
        setFeedback({ type: 'error', message: null });

        // Step 1: the API call itself. Failures here are transport/network/backend errors —
        // distinct from a client-side exception while processing a successful response.
        let ocrData: any;
        try {
            ocrData = await api.requests.directOcrExtract(selectedFile);
        } catch (err: any) {
            setOcrResult({ hasMismatches: true, details: resolveTransportErrorDetails(err) });
            logger.log({
                level: 'Error',
                eventType: 'API_REQUEST_FAILED',
                message: err?.detail || err?.message || 'directOcrExtract failed',
                componentKey: 'OcrDirectExtract',
                statusCode: err?.status,
                correlationId: err?.correlationId
            });
            setOcrLoading(false);
            return;
        }

        // Step 2: processing a successful (HTTP 200) response. Any exception here means the
        // extraction itself worked — it must never be reported as an unreadable/unsupported document.
        try {
            const { extractedTotal, extractedSupplier, paymentCondition: ocrPaymentCondition, advancePercent: ocrAdvancePercent } =
                extractOcrHeaderSuggestions(ocrData);

            const { hasMismatches, details } = buildOcrMismatchResult(extractedTotal, totalAmount, extractedSupplier, expectedSupplierName);

            setOcrResult({ hasMismatches, details, extractedTotal, extractedSupplier });

            // OCR Payment Condition Detection
            if (ocrPaymentCondition && ['POST_PAID', 'ADVANCE_FULL', 'ADVANCE_PARTIAL'].includes(ocrPaymentCondition)) {
                setPaymentCondition(ocrPaymentCondition);
                setPaymentConditionSource('OCR_DETECTED');
                setOcrDetectedPaymentCondition(ocrPaymentCondition);
                if (ocrPaymentCondition === 'ADVANCE_PARTIAL' && ocrAdvancePercent) {
                    setAdvancePercent(Math.max(1, Math.min(99, Math.round(Number(ocrAdvancePercent)))));
                }
            } else {
                setOcrDetectedPaymentCondition(null);
            }
        } catch (procErr: any) {
            setOcrResult({
                hasMismatches: true,
                details: [CLIENT_PROCESSING_ERROR_MESSAGE],
            });
            logger.log({
                level: 'Error',
                eventType: 'OCR_CLIENT_PROCESSING_ERROR',
                message: procErr?.message || 'Unknown client-side OCR processing error in CorrectPoModal',
                componentKey: 'OcrDirectExtract'
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
            if (selectedFile.size > 10 * 1024 * 1024) {
                setFeedback({ type: 'error', message: 'O arquivo não pode ser maior que 10MB.' });
                setFile(null);
                if (fileInputRef.current) fileInputRef.current.value = '';
                return;
            }
            setFile(selectedFile);
            setFeedback({ type: 'error', message: null });
            await runOcrValidation(selectedFile);
        }
    };

    const handleDownloadPo = async () => {
        if (!existingPo) return;
        try {
            await api.attachments.download(existingPo.id, existingPo.fileName);
        } catch {
            setFeedback({ type: 'error', message: 'Falha ao descarregar o documento.' });
        }
    };

    const handleRemovePo = async () => {
        if (!existingPo) return;
        try {
            await api.attachments.delete(existingPo.id);
            setPoRemoved(true);
            setExistingPo(null);
        } catch {
            setFeedback({ type: 'error', message: 'Falha ao remover o documento.' });
        }
    };

    const handleClearFile = () => {
        setFile(null);
        setOcrResult(null);
        setOverrideConfirmed(false);
        if (fileInputRef.current) fileInputRef.current.value = '';
    };

    const handleConfirm = async () => {
        if (!file) {
            setFeedback({ type: 'error', message: 'É obrigatório anexar o novo documento de P.O em formato PDF.' });
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
            // 1. Upload the new PO file
            await api.attachments.upload(requestId, [file], 'PO', poGroupId);

            // 2. Register PO (transitions back to PO_ISSUED)
            const result = await api.requests.registerPo(requestId, {
                poGroupId,
                comment: comment || 'P.O corrigida após devolução por Finanças.',
                hasMismatches: ocrResult?.hasMismatches || false,
                overrideConfirmed,
                mismatchDetails: ocrResult?.details ? ocrResult.details.join('; ') : '',
                paymentConditionCode: paymentCondition,
                advancePaymentPercent: paymentCondition === 'ADVANCE_PARTIAL' ? advancePercent : undefined,
                paymentConditionSource: paymentConditionSource || 'USER_SELECTED'
            });

            onSuccess(result.message || 'P.O corrigida e registrada com sucesso!');
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Não foi possível registrar a P.O. Tente novamente.' });
            setProcessing(false);
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
                            Correção de P.O
                        </h2>

                        <p style={{ marginBottom: '24px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.95rem' }}>
                            Este pedido foi devolvido por Finanças. Revise o motivo abaixo e submeta a P.O corrigida.
                        </p>

                        {loading ? (
                            <div style={{ padding: '60px 0', textAlign: 'center' }}>
                                <motion.div
                                    animate={{ rotate: [0, 360] }}
                                    transition={{ repeat: Infinity, ease: 'linear', duration: 1.5 }}
                                    style={{ display: 'inline-flex', marginBottom: '16px' }}
                                >
                                    <FileText size={32} style={{ color: 'var(--color-primary)' }} />
                                </motion.div>
                                <p style={{ fontWeight: 700, color: 'var(--color-text-muted)' }}>Carregando dados do pedido...</p>
                            </div>
                        ) : (
                            <>
                                {/* Finance Return Reason */}
                                {returnReason && (
                                    <div style={{
                                        marginBottom: '24px',
                                        padding: '20px',
                                        backgroundColor: '#fff7ed',
                                        border: '2px solid #f97316',
                                        borderRadius: 'var(--radius-sm)',
                                        borderLeft: '6px solid #ea580c',
                                    }}>
                                        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                                            <AlertTriangle size={22} color="#ea580c" style={{ flexShrink: 0, marginTop: '2px' }} />
                                            <div style={{ flex: 1 }}>
                                                <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: '#c2410c', marginBottom: '6px', letterSpacing: '0.05em' }}>
                                                    Motivo da Devolução por Finanças
                                                </div>
                                                <div style={{ fontSize: '0.95rem', fontWeight: 700, color: '#9a3412', lineHeight: 1.5 }}>
                                                    "{returnReason}"
                                                </div>
                                                <div style={{ marginTop: '10px', fontSize: '0.75rem', color: '#c2410c', fontWeight: 600 }}>
                                                    — {returnActor} em {returnDate ? new Date(returnDate).toLocaleDateString('pt-AO', { day: '2-digit', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '---'}
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                )}

                                {/* Expected Values */}
                                <div style={{ display: 'flex', gap: '16px', marginBottom: '24px', padding: '16px', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                                    <div style={{ flex: 1 }}>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Valor Esperado</div>
                                        <div style={{ fontSize: '1.1rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                            {formatCurrencyAO(totalAmount)} {currencyCode}
                                        </div>
                                    </div>
                                    <div style={{ flex: 1 }}>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Fornecedor Esperado</div>
                                        <div style={{ fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                            {expectedSupplierDisplay}
                                        </div>
                                    </div>
                                </div>

                                {/* Current P.O. Section */}
                                {existingPo && !poRemoved && (
                                    <div style={{ marginBottom: '24px' }}>
                                        <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                            P.O Atual (devolvida)
                                        </label>
                                        <div style={{
                                            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                                            padding: '16px 20px', backgroundColor: '#fff7ed',
                                            border: '2px solid #f97316', borderRadius: 'var(--radius-md)'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                                <FileText size={24} color="#ea580c" />
                                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                    <span style={{ fontWeight: 800, color: 'var(--color-text-main)', fontSize: '0.9rem' }}>{existingPo.fileName}</span>
                                                    <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>
                                                        {existingPo.fileSizeMBytes?.toFixed(2) || '---'} MB · Enviado por {existingPo.uploadedByName}
                                                    </span>
                                                </div>
                                            </div>
                                            <div style={{ display: 'flex', gap: '8px' }}>
                                                <button
                                                    onClick={handleDownloadPo}
                                                    title="Descarregar P.O atual"
                                                    style={{
                                                        background: 'none', border: '2px solid var(--color-border)', cursor: 'pointer',
                                                        padding: '8px', display: 'flex', borderRadius: 'var(--radius-sm)',
                                                        color: 'var(--color-primary)', transition: 'all 0.15s'
                                                    }}
                                                >
                                                    <Download size={18} />
                                                </button>
                                                <button
                                                    onClick={handleRemovePo}
                                                    title="Remover P.O atual"
                                                    style={{
                                                        background: 'none', border: '2px solid #fecaca', cursor: 'pointer',
                                                        padding: '8px', display: 'flex', borderRadius: 'var(--radius-sm)',
                                                        color: '#dc2626', transition: 'all 0.15s'
                                                    }}
                                                >
                                                    <Trash2 size={18} />
                                                </button>
                                            </div>
                                        </div>
                                    </div>
                                )}

                                {/* Upload New P.O. */}
                                <div style={{ marginBottom: '24px' }}>
                                    <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                        Nova P.O Corrigida (Apenas PDF)
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
                                                Clique para selecionar o PDF corrigido
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
                                                        padding: '40px 24px', backgroundColor: '#f8fafc',
                                                        border: '1.5px solid var(--color-border)', borderRadius: 'var(--radius-md)',
                                                        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '20px'
                                                    }}
                                                >
                                                    {/* Document Verification Animation */}
                                                    <div style={{ position: 'relative', width: '120px', height: '100px' }}>
                                                        {/* Background document page */}
                                                        <motion.div
                                                            animate={{ y: [0, -3, 0] }}
                                                            transition={{ repeat: Infinity, duration: 2.5, ease: 'easeInOut', delay: 0.3 }}
                                                            style={{
                                                                position: 'absolute', left: '8px', top: '12px',
                                                                width: '56px', height: '72px',
                                                                backgroundColor: '#e2e8f0', borderRadius: '4px',
                                                                border: '1px solid #cbd5e1', transform: 'rotate(-4deg)',
                                                            }}
                                                        >
                                                            <div style={{ padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: '5px' }}>
                                                                <div style={{ width: '80%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                                <div style={{ width: '60%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                                <div style={{ width: '90%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                                <div style={{ width: '45%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                            </div>
                                                        </motion.div>
                                                        {/* Front document page with scan line */}
                                                        <motion.div
                                                            animate={{ y: [0, -4, 0] }}
                                                            transition={{ repeat: Infinity, duration: 2.5, ease: 'easeInOut' }}
                                                            style={{
                                                                position: 'absolute', left: '18px', top: '6px',
                                                                width: '56px', height: '72px',
                                                                backgroundColor: 'white', borderRadius: '4px',
                                                                border: '1.5px solid #94a3b8', boxShadow: '0 2px 8px rgba(0,0,0,0.08)',
                                                                overflow: 'hidden',
                                                            }}
                                                        >
                                                            <div style={{ padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: '5px' }}>
                                                                <div style={{ width: '70%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                                <div style={{ width: '90%', height: '3px', backgroundColor: '#94a3b8', borderRadius: '2px' }} />
                                                                <div style={{ width: '55%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                                <div style={{ width: '75%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                                <div style={{ width: '40%', height: '3px', backgroundColor: '#94a3b8', borderRadius: '2px' }} />
                                                                <div style={{ width: '85%', height: '3px', backgroundColor: '#cbd5e1', borderRadius: '2px' }} />
                                                            </div>
                                                            <motion.div
                                                                animate={{ top: ['-10%', '110%'] }}
                                                                transition={{ repeat: Infinity, duration: 2, ease: 'easeInOut' }}
                                                                style={{ position: 'absolute', left: 0, right: 0, height: '8px', background: 'linear-gradient(180deg, transparent, rgba(37, 99, 235, 0.25), transparent)' }}
                                                            />
                                                        </motion.div>
                                                        {/* Magnifying glass */}
                                                        <motion.div
                                                            animate={{ x: [0, 12, 0, -8, 0], y: [0, 8, 16, 6, 0] }}
                                                            transition={{ repeat: Infinity, duration: 3, ease: 'easeInOut' }}
                                                            style={{ position: 'absolute', right: '6px', top: '4px', zIndex: 2 }}
                                                        >
                                                            <div style={{ width: '40px', height: '40px', borderRadius: '50%', border: '3px solid #2563EB', backgroundColor: 'rgba(37, 99, 235, 0.06)', position: 'relative', boxShadow: '0 2px 12px rgba(37, 99, 235, 0.2)' }}>
                                                                <div style={{ position: 'absolute', bottom: '-10px', right: '-8px', width: '4px', height: '16px', backgroundColor: '#2563EB', borderRadius: '2px', transform: 'rotate(-45deg)', transformOrigin: 'top center' }} />
                                                            </div>
                                                        </motion.div>
                                                        {/* Check pulse */}
                                                        <motion.div
                                                            animate={{ opacity: [0, 0, 1, 1, 0], scale: [0.5, 0.5, 1, 1, 0.5] }}
                                                            transition={{ repeat: Infinity, duration: 3, times: [0, 0.6, 0.7, 0.85, 1] }}
                                                            style={{ position: 'absolute', right: '0px', bottom: '0px', width: '22px', height: '22px', borderRadius: '50%', backgroundColor: '#16a34a', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 1px 6px rgba(22, 163, 74, 0.4)', zIndex: 3 }}
                                                        >
                                                            <CheckCircle size={14} color="white" strokeWidth={3} />
                                                        </motion.div>
                                                    </div>
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
                                                                id="correctPoOverrideConfirm"
                                                                checked={overrideConfirmed}
                                                                onChange={(e) => setOverrideConfirmed(e.target.checked)}
                                                                style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }}
                                                            />
                                                            <label htmlFor="correctPoOverrideConfirm" style={{ cursor: 'pointer', color: '#9a3412', fontSize: '0.85rem', fontWeight: 700, lineHeight: 1.4 }}>
                                                                Estou ciente das divergências listadas acima e confirmo a emissão da P.O corrigida sob minha responsabilidade.
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

                                {/* Comment */}

                                {/* ── B2P: Payment Condition Selector (required) ── */}
                                <div style={{ marginBottom: '24px', padding: '20px', backgroundColor: 'var(--color-bg-page)', border: !paymentCondition ? '2px solid #f97316' : '2px solid var(--color-border)', borderRadius: 'var(--radius-md)' }}>
                                    <label style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: !paymentCondition ? '#f97316' : 'var(--color-text-muted)' }}>
                                        Condição de Pagamento <span style={{ color: '#ef4444' }}>*</span>
                                        {ocrDetectedPaymentCondition && (
                                            <span style={{ fontSize: '0.65rem', padding: '2px 8px', borderRadius: '999px', backgroundColor: 'rgba(22, 163, 74, 0.1)', color: '#16a34a', border: '1px solid rgba(22, 163, 74, 0.3)', fontWeight: 700 }}>
                                                OCR Detectado
                                            </span>
                                        )}
                                    </label>
                                    {!paymentCondition && (
                                        <p style={{ fontSize: '0.75rem', color: '#f97316', marginBottom: '10px', fontWeight: 600 }}>
                                            Selecione a condição de pagamento antes de registrar a P.O.
                                        </p>
                                    )}
                                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                                        {[
                                            { code: 'POST_PAID', label: 'Pós-Pago' },
                                            { code: 'ADVANCE_FULL', label: '100% Antecipado' },
                                            { code: 'ADVANCE_PARTIAL', label: 'Antecipado Parcial' },
                                        ].map(opt => (
                                            <button
                                                key={opt.code}
                                                type="button"
                                                onClick={() => { setPaymentCondition(opt.code); setPaymentConditionSource('USER_SELECTED'); }}
                                                style={{
                                                    padding: '10px 18px',
                                                    borderRadius: 'var(--radius-sm)',
                                                    border: paymentCondition === opt.code ? '2px solid var(--color-primary)' : '1.5px solid var(--color-border)',
                                                    backgroundColor: paymentCondition === opt.code ? 'rgba(var(--color-primary-rgb), 0.08)' : 'var(--color-bg-surface)',
                                                    color: paymentCondition === opt.code ? 'var(--color-primary)' : 'var(--color-text-muted)',
                                                    fontWeight: 800,
                                                    fontSize: '0.8rem',
                                                    cursor: 'pointer',
                                                    transition: 'all 0.15s ease',
                                                    fontFamily: 'inherit',
                                                    position: 'relative',
                                                }}
                                            >
                                                {opt.label}
                                                {ocrDetectedPaymentCondition === opt.code && paymentCondition === opt.code && (
                                                    <span style={{ position: 'absolute', top: '-6px', right: '-6px', width: '14px', height: '14px', borderRadius: '50%', backgroundColor: '#16a34a', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                                        <CheckCircle size={10} color="white" strokeWidth={3} />
                                                    </span>
                                                )}
                                            </button>
                                        ))}
                                    </div>

                                    {paymentCondition === 'ADVANCE_PARTIAL' && (
                                        <div style={{ marginTop: '16px' }}>
                                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                                Percentual de Adiantamento: {advancePercent}%
                                            </label>
                                            <input
                                                type="range"
                                                min={1}
                                                max={99}
                                                value={advancePercent}
                                                onChange={(e) => setAdvancePercent(Number(e.target.value))}
                                                style={{ width: '100%', cursor: 'pointer' }}
                                            />
                                            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                                                <span>1%</span>
                                                <span style={{ color: 'var(--color-primary)', fontWeight: 900 }}>
                                                    {formatCurrencyAO(totalAmount * advancePercent / 100)} {currencyCode}
                                                </span>
                                                <span>99%</span>
                                            </div>
                                        </div>
                                    )}

                                    {paymentCondition === 'ADVANCE_FULL' && (
                                        <div style={{ marginTop: '12px', fontSize: '0.8rem', fontWeight: 700, color: '#d97706', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                            <AlertTriangle size={14} />
                                            O valor total de {formatCurrencyAO(totalAmount)} {currencyCode} será exigido como adiantamento.
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
                                        placeholder={ocrResult?.hasMismatches ? 'Justifique o motivo de ignorar a divergência...' : 'Descreva a correção realizada na P.O, se necessário...'}
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

                                {/* Actions */}
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
                                        disabled={processing || !file || ocrLoading || !paymentCondition || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())}
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
                                            cursor: (processing || !file || ocrLoading || !paymentCondition || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 'not-allowed' : 'pointer',
                                            fontWeight: 800,
                                            borderRadius: 'var(--radius-sm)',
                                            boxShadow: 'var(--shadow-md)',
                                            fontFamily: 'var(--font-family-display)',
                                            fontSize: '0.875rem',
                                            opacity: (processing || !file || ocrLoading || !paymentCondition || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 0.7 : 1
                                        }}
                                    >
                                        <Save size={18} />
                                        {processing ? 'PROCESSANDO...' : 'REGISTRAR P.O CORRIGIDA'}
                                    </button>
                                </div>
                            </>
                        )}
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
