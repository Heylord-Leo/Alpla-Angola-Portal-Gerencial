import React, { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, FileText, X, AlertTriangle, CheckCircle, Save, ShieldAlert, ShieldCheck } from 'lucide-react';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';
import { Z_INDEX } from '../constants/ui';
import { api } from '../lib/api';
import { formatCurrencyAO } from '../lib/utils';
import { logger } from '../lib/logger';
import {
    resolveExpectedSupplierName,
    resolveExpectedTotalAmount,
    resolveSupplierDisplay,
    extractOcrHeaderSuggestions,
    buildOcrMismatchResult,
    resolveTransportErrorDetails,
    CLIENT_PROCESSING_ERROR_MESSAGE,
    PRIMAVERA_FAMILY_LABELS,
    PrimaveraPoParse,
    looksLikeNif,
    mentionsPrimaveraFamilyWithoutReference,
    parsePrimaveraPoReference,
    resolveAutoFillPoNumber,
} from '../lib/ocrPoValidation';

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
    poGroupId: string;
    supplierId?: number | null;
    requestData: {
        totalAmount?: number | null;
        supplierName?: string | null;
        currencyCode: string;
    };
    onClose: () => void;
    onSuccess: (message: string) => void;
}

export function RegisterPoModal({ show, requestId, poGroupId, supplierId, requestData, onClose, onSuccess }: RegisterPoModalProps) {
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

    // PO Number State
    const [purchaseOrderNumber, setPurchaseOrderNumber] = useState('');

    // Backend Overrides State
    const [backendOcrMismatch, setBackendOcrMismatch] = useState<{ details: string } | null>(null);
    /** Positively identified Primavera PO reference (deterministic parse), for review display. */
    const [detectedPoReference, setDetectedPoReference] = useState<PrimaveraPoParse | null>(null);
    const [duplicateWarning, setDuplicateWarning] = useState<string | null>(null);
    const [overrideDuplicateConfirmed, setOverrideDuplicateConfirmed] = useState(false);
    const [duplicateOverrideComment, setDuplicateOverrideComment] = useState('');

    // Supplier Registration Guard
    const [regCheck, setRegCheck] = useState<SupplierRegistrationCheck | null>(null);
    const [regCheckLoading, setRegCheckLoading] = useState(false);

    // B2P: Payment Condition State (no default — buyer must explicitly select)
    const [paymentCondition, setPaymentCondition] = useState<string>('');
    const [advancePercent, setAdvancePercent] = useState<number>(50);
    const [paymentConditionSource, setPaymentConditionSource] = useState<'OCR_DETECTED' | 'USER_SELECTED' | ''>('');
    const [ocrDetectedPaymentCondition, setOcrDetectedPaymentCondition] = useState<string | null>(null);

    // Reset state on open/close
    useEffect(() => {
        if (!show) {
            setFile(null);
            setComment('');
            setOcrResult(null);
            setOverrideConfirmed(false);
            setPurchaseOrderNumber('');
            setBackendOcrMismatch(null);
            setDuplicateWarning(null);
            setOverrideDuplicateConfirmed(false);
            setDuplicateOverrideComment('');
            setFeedback({ type: 'error', message: null });
            setRegCheck(null);
            setPaymentCondition('');
            setAdvancePercent(50);
            setPaymentConditionSource('');
            setOcrDetectedPaymentCondition(null);
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

    // Null-safe expected values: requestData.supplierName/totalAmount can be null/undefined at
    // runtime (RequestPoGroupDto.supplierNameSnapshot is nullable). expectedSupplierName is the
    // COMPARISON value (null when unset — never a placeholder string, so it can never be fed into
    // calculateSimilarity as if it were a real name); expectedSupplierDisplay is UI-only text.
    const expectedSupplierName = resolveExpectedSupplierName(requestData?.supplierName);
    const expectedSupplierDisplay = resolveSupplierDisplay(expectedSupplierName);
    const expectedTotalAmount = resolveExpectedTotalAmount(requestData?.totalAmount);

    const runOcrValidation = async (selectedFile: File) => {
        setOcrLoading(true);
        setOcrResult(null);
        setOverrideConfirmed(false);
        setFeedback({ type: 'error', message: null });

        // Step 1: the API call itself. Failures here are transport/network/backend errors —
        // distinct from a client-side exception while processing a successful response.
        let ocrData: any;
        try {
            // directOcrExtract handles generic layout detection & extraction
            // Response shape: { integration: { headerSuggestions: { supplierName: { value }, grandTotal: { value } } } }
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
            const { extractedTotal, extractedSupplier, extractedSupplierNif, extractedPoNumber,
                extractedPoReference, paymentCondition: ocrPaymentCondition, advancePercent: ocrAdvancePercent } =
                extractOcrHeaderSuggestions(ocrData);

            // ── Primavera-aware PO number (positive identification, never numeric guessing) ──
            // The old behavior auto-filled the raw generic documentNumber, which on Primavera
            // POs was frequently the SUPPLIER NIF (e.g. 5001713205) — poisoning the field and
            // firing false duplicates. Now: a positively parsed ECF/ECF10/ECF11 reference (or a
            // letter-bearing supplier reference) may auto-fill; fiscal numbers never do.
            const knownNifs = [extractedSupplierNif];
            const autoFill = resolveAutoFillPoNumber(extractedPoReference || extractedPoNumber, knownNifs);
            const detectedParse = autoFill?.parse
                ?? parsePrimaveraPoReference(extractedPoReference || extractedPoNumber);

            if (autoFill && !purchaseOrderNumber) {
                setPurchaseOrderNumber(autoFill.value);
            }
            setDetectedPoReference(detectedParse);

            const poWarnings: string[] = [];
            if (!autoFill && extractedPoNumber && !purchaseOrderNumber) {
                poWarnings.push(
                    looksLikeNif(extractedPoNumber)
                        ? `O número extraído (${extractedPoNumber}) parece ser um NIF e foi ignorado. ` +
                          'Verifique a referência da P.O no documento (formato ECF/ECF10/ECF11 AAAA/NNN) e insira-a manualmente.'
                        : `Não foi possível identificar a referência da P.O automaticamente. Insira-a manualmente.`);
            }
            if (mentionsPrimaveraFamilyWithoutReference(extractedPoNumber) && !detectedParse) {
                poWarnings.push('O documento parece ser uma P.O Primavera, mas a referência completa ' +
                    '(família + ano/sequência) não foi lida. Revise e insira manualmente.');
            }

            const mismatchBase = buildOcrMismatchResult(extractedTotal, expectedTotalAmount, extractedSupplier, expectedSupplierName);
            const hasMismatches = mismatchBase.hasMismatches || poWarnings.length > 0;
            const details = [...poWarnings, ...mismatchBase.details];

            setOcrResult({
                hasMismatches,
                details,
                extractedTotal,
                extractedSupplier
            });

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
                message: procErr?.message || 'Unknown client-side OCR processing error in RegisterPoModal',
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

        if (!purchaseOrderNumber.trim()) {
            setFeedback({ type: 'error', message: 'O número da P.O é obrigatório.' });
            return;
        }

        // Frontend check
        if (ocrResult?.hasMismatches && !overrideConfirmed) {
            setFeedback({ type: 'error', message: 'Confirme estar ciente das divergências (avaliação prévia) antes de registrar.' });
            return;
        }
        if (ocrResult?.hasMismatches && !comment.trim()) {
            setFeedback({ type: 'error', message: 'Um comentário justificativo é obrigatório quando há divergências (avaliação prévia).' });
            return;
        }

        // Backend blocks (Duplicate PO)
        if (duplicateWarning && !overrideDuplicateConfirmed) {
            setFeedback({ type: 'error', message: 'Confirme estar ciente da duplicidade de P.O antes de continuar.' });
            return;
        }
        if (duplicateWarning && overrideDuplicateConfirmed && !duplicateOverrideComment.trim()) {
            setFeedback({ type: 'error', message: 'Um comentário justificativo é obrigatório para registrar P.O duplicada.' });
            return;
        }

        // Backend blocks (OCR)
        if (backendOcrMismatch && !overrideConfirmed) {
            setFeedback({ type: 'error', message: 'Confirme estar ciente das divergências detectadas pelo sistema central.' });
            return;
        }
        if (backendOcrMismatch && overrideConfirmed && !comment.trim()) {
            setFeedback({ type: 'error', message: 'Um comentário justificativo é obrigatório para as divergências do sistema central.' });
            return;
        }

        setProcessing(true);
        setFeedback({ type: 'error', message: null });
        setBackendOcrMismatch(null);
        setDuplicateWarning(null);

        try {
            // 1. Upload the file first
            await api.attachments.upload(requestId, [file], 'PO', poGroupId);

            // 2. Register PO status transition (with B2P payment condition)
            const result = await api.requests.registerPo(requestId, {
                poGroupId,
                comment,
                hasMismatches: ocrResult?.hasMismatches || false,
                overrideConfirmed,
                mismatchDetails: ocrResult?.details ? ocrResult.details.join('; ') : '',
                paymentConditionCode: paymentCondition,
                advancePaymentPercent: paymentCondition === 'ADVANCE_PARTIAL' ? advancePercent : undefined,
                paymentConditionSource: paymentConditionSource || 'USER_SELECTED',
                purchaseOrderNumber: purchaseOrderNumber.trim(),
                extractedSupplierName: ocrResult?.extractedSupplier,
                extractedTotalAmount: ocrResult?.extractedTotal,
                overrideDuplicateConfirmed,
                duplicateOverrideComment: duplicateOverrideComment.trim()
            });

            onSuccess(result.message || 'P.O registrada com sucesso!');
            
        } catch (err: any) {
            setProcessing(false);
            
            if (err.title === 'DUPLICATE_PO') {
                setDuplicateWarning(err.detail || 'Número de P.O já existente.');
                return;
            }
            if (err.title === 'OCR_MISMATCH') {
                setBackendOcrMismatch({ details: err.detail || 'Divergências validadas pelo sistema central.' });
                return;
            }

            setFeedback({ type: 'error', message: err.message || 'Não foi possível registrar a P.O. Tente novamente.' });
        }
    };

    const handleClearFile = () => {
        setFile(null);
        setOcrResult(null);
        setOverrideConfirmed(false);
        setBackendOcrMismatch(null);
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
                                    {formatCurrencyAO(expectedTotalAmount)} {requestData.currencyCode}
                                </div>
                            </div>
                            <div style={{ flex: 1 }}>
                                <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Fornecedor Esperado</div>
                                <div style={{ fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                    {expectedSupplierDisplay}
                                </div>
                            </div>
                        </div>

                        <div style={{ marginBottom: '20px' }}>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Número da P.O <span style={{ color: '#ef4444' }}>*</span>
                            </label>
                            <input
                                type="text"
                                value={purchaseOrderNumber}
                                onChange={(e) => {
                                    setPurchaseOrderNumber(e.target.value);
                                    setDuplicateWarning(null); // clear warning if user changes PO number
                                }}
                                placeholder="Ex: ECF11 2026/421"
                                style={inputStyle}
                            />
                            {detectedPoReference && (
                                <p style={{
                                    margin: '6px 0 0', fontSize: '0.75rem',
                                    color: 'var(--color-text-muted)', fontWeight: 600
                                }}>
                                    P.O Primavera detectada: <strong>{detectedPoReference.display}</strong>
                                    {' '}— família <strong>{detectedPoReference.family}</strong>
                                    {PRIMAVERA_FAMILY_LABELS[detectedPoReference.family]
                                        ? <> ({PRIMAVERA_FAMILY_LABELS[detectedPoReference.family]})</>
                                        : null}. Revise antes de registrar.
                                </p>
                            )}
                            {duplicateWarning && (
                                <motion.div initial={{ opacity: 0, y: 5 }} animate={{ opacity: 1, y: 0 }} style={{ marginTop: '12px', padding: '16px', backgroundColor: '#fef2f2', border: '2px solid #ef4444', borderRadius: '8px' }}>
                                    <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', marginBottom: '12px' }}>
                                        <AlertTriangle size={20} color="#ef4444" style={{ flexShrink: 0 }} />
                                        <div>
                                            <h4 style={{ margin: '0 0 4px 0', color: '#b91c1c', fontSize: '0.9rem', fontWeight: 800 }}>P.O Duplicada (SISTEMA CENTRAL)</h4>
                                            <p style={{ margin: 0, color: '#991b1b', fontSize: '0.8rem', fontWeight: 600 }}>{duplicateWarning}</p>
                                        </div>
                                    </div>
                                    <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#fee2e2', padding: '12px', borderRadius: '4px', marginBottom: '12px' }}>
                                        <input 
                                            type="checkbox" 
                                            id="overrideDuplicateConfirm"
                                            checked={overrideDuplicateConfirmed}
                                            onChange={(e) => setOverrideDuplicateConfirmed(e.target.checked)}
                                            style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }}
                                        />
                                        <label htmlFor="overrideDuplicateConfirm" style={{ cursor: 'pointer', color: '#991b1b', fontSize: '0.8rem', fontWeight: 700, lineHeight: 1.4 }}>
                                            Confirmar uso do número de P.O duplicado sob minha responsabilidade.
                                        </label>
                                    </div>
                                    {overrideDuplicateConfirmed && (
                                        <textarea
                                            value={duplicateOverrideComment}
                                            onChange={(e) => setDuplicateOverrideComment(e.target.value)}
                                            placeholder="Justificativa obrigatória para P.O duplicada..."
                                            rows={2}
                                            style={{ ...inputStyle, resize: 'none', borderColor: '#ef4444' }}
                                        />
                                    )}
                                </motion.div>
                            )}
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
                                    ) : backendOcrMismatch ? (
                                        <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} style={{ padding: '24px', backgroundColor: '#fff7ed', border: '2px solid #ef4444', borderRadius: '8px' }}>
                                            <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', marginBottom: '16px' }}>
                                                <ShieldAlert size={24} color="#ef4444" style={{ flexShrink: 0 }} />
                                                <div>
                                                    <h4 style={{ margin: '0 0 4px 0', color: '#b91c1c', fontSize: '1rem', fontWeight: 800 }}>Divergência Detectada (SISTEMA CENTRAL)</h4>
                                                    <p style={{ margin: 0, color: '#991b1b', fontSize: '0.85rem', fontWeight: 600 }}>O sistema central rejeitou a validação da P.O pelo(s) motivo(s):</p>
                                                </div>
                                            </div>
                                            <p style={{ margin: '0 0 20px 0', color: '#991b1b', fontSize: '0.85rem', fontWeight: 700 }}>
                                                {backendOcrMismatch.details}
                                            </p>
                                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#fee2e2', padding: '16px', borderRadius: '4px' }}>
                                                <input 
                                                    type="checkbox" 
                                                    id="overrideConfirmBackend"
                                                    checked={overrideConfirmed}
                                                    onChange={(e) => setOverrideConfirmed(e.target.checked)}
                                                    style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }}
                                                />
                                                <label htmlFor="overrideConfirmBackend" style={{ cursor: 'pointer', color: '#991b1b', fontSize: '0.85rem', fontWeight: 700, lineHeight: 1.4 }}>
                                                    Estou ciente das divergências reportadas e confirmo a emissão sob minha responsabilidade.
                                                </label>
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

                        {/* ── B2P: Payment Condition Selector (required — no default) ── */}
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
                                            {formatCurrencyAO(expectedTotalAmount * advancePercent / 100)} {requestData.currencyCode}
                                        </span>
                                        <span>99%</span>
                                    </div>
                                </div>
                            )}

                            {paymentCondition === 'ADVANCE_FULL' && (
                                <div style={{ marginTop: '12px', fontSize: '0.8rem', fontWeight: 700, color: '#d97706', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <AlertTriangle size={14} />
                                    O valor total de {formatCurrencyAO(expectedTotalAmount)} {requestData.currencyCode} será exigido como adiantamento.
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
                                disabled={isBlocked || processing || !file || ocrLoading || !paymentCondition || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())}
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
                                    cursor: (isBlocked || processing || !file || ocrLoading || !paymentCondition || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 'not-allowed' : 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: isBlocked ? 'none' : 'var(--shadow-md)',
                                    fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem',
                                    opacity: (isBlocked || processing || !file || ocrLoading || !paymentCondition || (ocrResult?.hasMismatches && !overrideConfirmed) || (ocrResult?.hasMismatches && !comment.trim())) ? 0.7 : 1
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
