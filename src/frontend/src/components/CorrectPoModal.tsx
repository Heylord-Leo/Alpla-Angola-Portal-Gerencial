import React, { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Upload, FileText, X, AlertTriangle, Download, Trash2, Save, CheckCircle, ChevronDown, ChevronRight } from 'lucide-react';
import { parseFinanceReturnComment } from '../lib/financeReturnReason';
import { Feedback, FeedbackType } from './ui/Feedback';
import { DropdownPortal } from './ui/DropdownPortal';
import { Z_INDEX } from '../constants/ui';
import { api } from '../lib/api';
import { formatCurrencyAO } from '../lib/utils';
import { logger } from '../lib/logger';
import { RequestDetailsDto, RequestAttachmentDto } from '../types';
import { resolveJustificationPolicy, submitCorrectPo, validateCorrectPoSubmission } from '../lib/correctPoSubmission';
import { classifyRegisterPoError, validatePreviousPoReuse, isDocumentBlockingKind, REGISTER_PO_ERROR_MESSAGES } from '../lib/registerPoErrors';
import {
    PoDocumentIdentification, PoNumberField, NO_EXTRACTION_IDENTIFICATION,
    readPoDocumentIdentification, readDetectedDocumentNumber, initialPoNumberField, applyExtractedNumber, userEditsPoNumber,
    useSuggestedPoNumber, clearExtractedNumber, numberDecisionPending, keepCurrentPoNumber,
} from '../lib/poDocumentIdentification';
import { PoNumberFieldBlock, PoDocumentVerdictBlock } from './po/PoDocumentReviewBlocks';
import { MIN_JUSTIFICATION_LENGTH } from '../lib/poGroupCancellation';
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
    const [showReturnTechnical, setShowReturnTechnical] = useState(false);
    const [existingPo, setExistingPo] = useState<RequestAttachmentDto | null>(null);
    const [poRemoved, setPoRemoved] = useState(false);

    const [file, setFile] = useState<File | null>(null);
    const [comment, setComment] = useState('');
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    // Phase 1 P.O. cancellation: 409 PRIOR_ECF_CONFIRMATION_PENDING (upload or register) → blocking notice, no retry;
    // 400 PREVIOUS_PO_NUMBER_REUSED → warning + confirmation + explanation (≥ 20), then resubmit the SAME payload.
    const [priorEcfBlock, setPriorEcfBlock] = useState<string | null>(null);
    const [previousPoReuse, setPreviousPoReuse] = useState<{ message: string } | null>(null);
    const [reuseConfirmed, setReuseConfirmed] = useState(false);
    const [reuseComment, setReuseComment] = useState('');
    const uploadedFileRef = useRef<File | null>(null);
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

    // v2.249.4 — the P.O. NUMBER the correction confirms or replaces (current vs proposed, never silently retained), the
    // DOCUMENT verdict (server rule, re-enforced by register-po), the explicit type confirmation, and the server branches
    // the old modal never handled (DUPLICATE_PO, OCR_MISMATCH, document blocks). The uploaded file id lets a refused
    // register-po be retried without a second upload.
    const [poNumber, setPoNumber] = useState<PoNumberField>(initialPoNumberField(null));
    const [identification, setIdentification] = useState<PoDocumentIdentification | null>(null);
    const [detectedDocumentNumber, setDetectedDocumentNumber] = useState<string | null>(null);
    const [documentTypeConfirmed, setDocumentTypeConfirmed] = useState(false);
    const [backendOcrMismatch, setBackendOcrMismatch] = useState<{ details: string } | null>(null);
    const [duplicateWarning, setDuplicateWarning] = useState<string | null>(null);
    const [overrideDuplicateConfirmed, setOverrideDuplicateConfirmed] = useState(false);
    const [duplicateOverrideComment, setDuplicateOverrideComment] = useState('');
    const [documentBlock, setDocumentBlock] = useState<string | null>(null);
    const uploadedAttachmentIdRef = useRef<string | null>(null);

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
            setPoNumber(initialPoNumberField(null));
            setIdentification(null); setDetectedDocumentNumber(null); setDocumentTypeConfirmed(false);
            setBackendOcrMismatch(null); setDuplicateWarning(null); setOverrideDuplicateConfirmed(false); setDuplicateOverrideComment('');
            setDocumentBlock(null); uploadedAttachmentIdRef.current = null;
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

                // Find the FINANCE_RETURN_ADJUSTMENT for THIS group (comment carries "GroupId: <id>"),
                // falling back to the latest return event overall. Keep the RAW comment — the orange
                // block parses it (parseFinanceReturnComment) so the human message is surfaced above the
                // technical context; group-specific selection avoids showing a sibling group's reason.
                const returns = (details.statusHistory ?? [])
                    .filter((h: any) => h.actionTaken === 'FINANCE_RETURN_ADJUSTMENT')
                    .sort((a: any, b: any) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime());
                const returnEvent = returns.find((h: any) => (h.comment || '').includes(poGroupId)) ?? returns[0];

                if (returnEvent) {
                    setReturnReason(returnEvent.comment || '');
                    setReturnActor(returnEvent.actorName || '');
                    setReturnDate(returnEvent.createdAtUtc || '');
                }

                // v2.249.4 — the CURRENT P.O. document of the group is the server's choice (currentPoAttachmentId: the file
                // registered last; earlier files are superseded). Fallback for pre-version data: newest live P.O. file.
                const group = details.poGroups?.find(g => g.id === poGroupId) ?? null;
                const groupPoFiles = (details.attachments ?? [])
                    .filter((a: RequestAttachmentDto) => a.attachmentTypeCode === 'PO' && a.requestPoGroupId === poGroupId && !a.voidedAtUtc);
                const poAttachment = (group?.currentPoAttachmentId && groupPoFiles.find((a: RequestAttachmentDto) => a.id === group.currentPoAttachmentId))
                    || groupPoFiles.filter((a: RequestAttachmentDto) => !a.supersededAtUtc)
                        .sort((a: RequestAttachmentDto, b: RequestAttachmentDto) => new Date(b.uploadedAtUtc).getTime() - new Date(a.uploadedAtUtc).getTime())[0]
                    || null;
                setExistingPo(poAttachment);
                // The number the group has today is the starting point of the proposed number (source CURRENT).
                setPoNumber(initialPoNumberField(group?.purchaseOrderNumber));

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

    // v2.242.0 — GROUP-scoped expected context. Resolve the SELECTED RequestPoGroup and read its
    // TotalAmount / Supplier / Currency. NEVER the request-scalar fields (estimatedTotalAmount /
    // supplierName / currencyCode), which are 0 / null for a multi-group request and produced the
    // "0,00 / Não definido" defect + a wrong OCR divergence baseline. expectedSupplierName is the
    // COMPARISON value (null when unset); expectedSupplierDisplay is UI-only text.
    const selectedGroup = requestData?.poGroups?.find(g => g.id === poGroupId) ?? null;
    // A poGroupId that resolves to no group is a hard error — never fall back to 0 / "Não definido".
    const groupMissing = !loading && !!requestData && !!poGroupId && !selectedGroup;
    // The detail fetch failed: no group context and no server decision → the form is not offered at all.
    const detailsUnavailable = !loading && !requestData;
    // docs §G.19.3 — whether the Buyer's justification is mandatory is the BACKEND's decision for the legacy
    // request-level Finance return (requestData.legacyReturnCorrection, same predicate RegisterPo enforces) and,
    // as before, an unresolved OCR divergence. Current-code corrections keep the comment optional.
    const justification = resolveJustificationPolicy(requestData, poGroupId, !!ocrResult?.hasMismatches, !!identification?.requiresExplicitConfirmation);
    const totalAmount = resolveExpectedTotalAmount(selectedGroup?.totalAmount);
    const expectedSupplierName = resolveExpectedSupplierName(selectedGroup?.supplierNameSnapshot);
    const expectedSupplierDisplay = resolveSupplierDisplay(expectedSupplierName);
    const currencyCode = selectedGroup?.currencyCode || 'AOA';

    const runOcrValidation = async (selectedFile: File) => {
        if (groupMissing) return; // never compare against a missing group's 0/null baseline
        setOcrLoading(true);
        setOcrResult(null);
        setOverrideConfirmed(false);
        setFeedback({ type: 'error', message: null });
        // v2.249.4 — a new file: the previous extraction (verdict, suggested number, confirmations, server answers) is stale.
        setIdentification(null); setDetectedDocumentNumber(null); setDocumentTypeConfirmed(false);
        setBackendOcrMismatch(null); setDuplicateWarning(null); setDocumentBlock(null);
        setPoNumber(f => clearExtractedNumber(f, selectedGroup?.purchaseOrderNumber));

        // Step 1: the API call itself. Failures here are transport/network/backend errors —
        // distinct from a client-side exception while processing a successful response.
        let ocrData: any;
        try {
            // v2.249.4 — request-scoped, server-persisted extraction: the evidence register-po will read for this file's bytes.
            ocrData = await api.requests.poDocumentExtract(requestId, selectedFile);
        } catch (err: any) {
            setOcrResult({ hasMismatches: true, details: resolveTransportErrorDetails(err) });
            // v2.249.4 — no extraction: the document is UNCERTAIN (explicit confirmation + manual number), never "validated".
            setIdentification(NO_EXTRACTION_IDENTIFICATION);
            setDetectedDocumentNumber(null);
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
            const { extractedTotal, extractedSupplier, extractedSupplierNif, extractedPoNumber, extractedPoReference,
                paymentCondition: ocrPaymentCondition, advancePercent: ocrAdvancePercent } =
                extractOcrHeaderSuggestions(ocrData);

            // v2.249.4 — document identity first: the server verdict and the facts it will re-evaluate; then the number the
            // document carries, offered next to the current number (filled only into an empty field, never over it).
            const id = readPoDocumentIdentification(ocrData);
            setIdentification(id);
            setDetectedDocumentNumber(readDetectedDocumentNumber(ocrData));
            setPoNumber(f => applyExtractedNumber(f, id, extractedPoReference || extractedPoNumber, [extractedSupplierNif]));

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
        uploadedFileRef.current = null;
        uploadedAttachmentIdRef.current = null;
        setIdentification(null); setDetectedDocumentNumber(null); setDocumentTypeConfirmed(false);
        setBackendOcrMismatch(null); setDuplicateWarning(null); setOverrideDuplicateConfirmed(false); setDuplicateOverrideComment(''); setDocumentBlock(null);
        setPoNumber(f => clearExtractedNumber(f, selectedGroup?.purchaseOrderNumber));
        if (fileInputRef.current) fileInputRef.current.value = '';
    };

    const handleConfirm = async () => {
        // docs §G.19.3 — validation, payload and submission live in lib/correctPoSubmission (unit-tested on the
        // real payload path). The modal never fabricates a justification: a blank comment is sent as absent, and
        // when the backend declared it mandatory the submission is refused here, before any upload.
        // Phase 1 P.O. cancellation: a blocked replacement is never retried; a number reuse needs confirmation + explanation.
        if (priorEcfBlock) return;
        if (previousPoReuse) {
            const reuseError = validatePreviousPoReuse(reuseConfirmed, reuseComment);
            if (reuseError) { setFeedback({ type: 'error', message: reuseError }); return; }
        }
        // v2.249.4 — any refusal AFTER the upload (reuse, duplicate, OCR mismatch, type confirmation) keeps the uploaded file:
        // the resubmission skips the upload and names it (poAttachmentId), so no duplicate P.O. file is ever created.
        const reuseUpload = uploadedAttachmentIdRef.current != null && uploadedFileRef.current === file;
        const submission = {
            requestId, poGroupId, detailsUnavailable, groupMissing, file, comment, ocrResult, overrideConfirmed,
            paymentCondition, advancePercent, paymentConditionSource, justification,
            previousPoNumberReuseComment: previousPoReuse ? reuseComment : undefined,
            skipUpload: reuseUpload,
            uploadedAttachmentId: reuseUpload ? uploadedAttachmentIdRef.current : null,
            purchaseOrderNumber: poNumber.value, purchaseOrderNumberSource: poNumber.source,
            identification, documentTypeConfirmed, numberDecisionPending: numberDecisionPending(poNumber, selectedGroup?.purchaseOrderNumber),
            overrideDuplicateConfirmed: !!duplicateWarning && overrideDuplicateConfirmed, duplicateOverrideComment,
        };
        const validationError = validateCorrectPoSubmission(submission);
        if (validationError) {
            setFeedback({ type: 'error', message: validationError });
            return;
        }

        setProcessing(true);
        setFeedback({ type: 'error', message: null });

        try {
            // 1. Upload the new PO file, 2. register the P.O. (correction) — same order as before.
            const outcome = await submitCorrectPo(
                { upload: api.attachments.upload, registerPo: api.requests.registerPo, onUploaded: id => { uploadedAttachmentIdRef.current = id; uploadedFileRef.current = file; } },
                submission
            );
            if (!outcome.ok) {
                setFeedback({ type: 'error', message: outcome.error });
                setProcessing(false);
                return;
            }
            onSuccess(outcome.message);
        } catch (err: any) {
            const decision = classifyRegisterPoError(err);
            if (decision.kind === 'PRIOR_ECF_CONFIRMATION_PENDING') setPriorEcfBlock(decision.message);
            else if (decision.kind === 'PREVIOUS_PO_NUMBER_REUSED') {
                // Only register-po answers with this code, and the upload precedes it: this file is already stored.
                uploadedFileRef.current = file;
                setPreviousPoReuse({ message: decision.message });
            }
            // v2.249.4 — the server branches the old modal silently turned into a generic error.
            else if (decision.kind === 'DUPLICATE_PO') setDuplicateWarning(decision.message);
            else if (decision.kind === 'OCR_MISMATCH') setBackendOcrMismatch({ details: decision.message });
            else if (isDocumentBlockingKind(decision.kind)) setDocumentBlock(decision.message);
            else if (decision.kind === 'DOCUMENT_TYPE_CONFIRMATION_REQUIRED') { setIdentification(prev => prev ?? NO_EXTRACTION_IDENTIFICATION); setFeedback({ type: 'error', message: decision.message }); }
            else setFeedback({ type: 'error', message: decision.message });
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

    // v2.249.4 — one gate for the submit button: the server rules mirrored for UX (the server stays authoritative).
    const submitDisabled = !!priorEcfBlock || !!documentBlock || processing || !file || ocrLoading || !paymentCondition
        || !poNumber.value.trim()
        || numberDecisionPending(poNumber, selectedGroup?.purchaseOrderNumber)
        || !!identification?.isBlocked
        || (!!identification?.requiresExplicitConfirmation && !documentTypeConfirmed)
        || (!!ocrResult?.hasMismatches && !overrideConfirmed)
        || (!!backendOcrMismatch && !overrideConfirmed)
        || (!!duplicateWarning && (!overrideDuplicateConfirmed || !duplicateOverrideComment.trim()))
        || (justification.required && !comment.trim());

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
                        ) : (detailsUnavailable || groupMissing) ? (
                            <div style={{ padding: '40px 20px', textAlign: 'center' }}>
                                <AlertTriangle size={32} style={{ color: 'var(--color-status-red)', marginBottom: '12px' }} />
                                <p style={{ fontWeight: 700, color: 'var(--color-status-red)', maxWidth: '460px', margin: '0 auto', lineHeight: 1.5 }}>
                                    {detailsUnavailable
                                        ? 'Não foi possível carregar os dados do pedido. Atualize a página e tente novamente.'
                                        : 'Não foi possível carregar os dados do grupo P.O. selecionado. Atualize a página e tente novamente.'}
                                </p>
                            </div>
                        ) : (
                            <>
                                {/* Finance Return Reason — the human message is surfaced ABOVE the
                                    system/technical context (parseFinanceReturnComment); actor/date are
                                    secondary; the technical bracket collapses behind "Ver detalhes técnicos". */}
                                {returnReason && (() => {
                                    const parsed = parseFinanceReturnComment(returnReason);
                                    return (
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
                                            <div style={{ flex: 1, minWidth: 0 }}>
                                                <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: '#c2410c', marginBottom: '8px', letterSpacing: '0.05em' }}>
                                                    Motivo da Devolução por Finanças
                                                </div>
                                                <div style={{ fontSize: '0.68rem', fontWeight: 700, textTransform: 'uppercase', color: '#c2410c', letterSpacing: '0.04em', opacity: 0.85 }}>
                                                    Mensagem de Finanças
                                                </div>
                                                <div style={{ fontSize: '1.05rem', fontWeight: 800, color: '#9a3412', lineHeight: 1.45, marginTop: '2px' }}>
                                                    {parsed.message}
                                                </div>
                                                {(returnActor || returnDate) && (
                                                    <div style={{ marginTop: '10px', fontSize: '0.75rem', color: '#c2410c', fontWeight: 600 }}>
                                                        Devolvido por {returnActor || '—'}{returnDate ? ` · ${new Date(returnDate).toLocaleDateString('pt-AO', { day: '2-digit', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' })}` : ''}
                                                    </div>
                                                )}
                                                {parsed.technical && (
                                                    <div style={{ marginTop: '12px' }}>
                                                        <button
                                                            type="button"
                                                            onClick={() => setShowReturnTechnical(v => !v)}
                                                            aria-expanded={showReturnTechnical}
                                                            style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', background: 'transparent', border: 'none', cursor: 'pointer', padding: 0, fontSize: '0.72rem', fontWeight: 700, color: '#c2410c' }}
                                                        >
                                                            {showReturnTechnical ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                                                            Ver detalhes técnicos
                                                        </button>
                                                        {showReturnTechnical && (
                                                            <div style={{ marginTop: '6px', fontSize: '0.72rem', color: '#7c2d12', lineHeight: 1.5, whiteSpace: 'pre-wrap', wordBreak: 'break-word', opacity: 0.9 }}>
                                                                {parsed.technical}
                                                            </div>
                                                        )}
                                                    </div>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                    );
                                })()}

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
                                            P.O atual (devolvida){selectedGroup?.purchaseOrderNumber ? ` — nº ${selectedGroup.purchaseOrderNumber}` : ''} · será substituída pelo novo documento
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

                                            {/* v2.249.4 — document verdict (identified / uncertain + confirmation / other type = blocked) */}
                                            {!ocrLoading && (ocrResult || identification) && (
                                                <PoDocumentVerdictBlock identification={identification} hasBusinessMismatches={!!ocrResult?.hasMismatches || !!backendOcrMismatch} typeConfirmed={documentTypeConfirmed} onTypeConfirmedChange={setDocumentTypeConfirmed} idPrefix="correct" detectedNumber={detectedDocumentNumber} />
                                            )}
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
                                                ) : null /* v2.249.4 — the verdict block above replaces the unconditional "Documento Validado" */
                                            ) : null}
                                        </div>
                                    )}
                                </div>

                                {/* ── v2.249.4: the P.O. number — current vs proposed; extracted / manual provenance; server branches ── */}
                                <PoNumberFieldBlock
                                    field={poNumber}
                                    currentNumber={selectedGroup?.purchaseOrderNumber}
                                    inputId="correctPoNumber"
                                    onChange={v => { setPoNumber(f => userEditsPoNumber(f, v, selectedGroup?.purchaseOrderNumber)); setDuplicateWarning(null); setPreviousPoReuse(null); setReuseConfirmed(false); setReuseComment(''); }}
                                    onUseSuggestion={() => { setPoNumber(f => useSuggestedPoNumber(f)); setDuplicateWarning(null); setPreviousPoReuse(null); setReuseConfirmed(false); setReuseComment(''); }}
                                    onKeepCurrent={() => { setPoNumber(f => keepCurrentPoNumber(f, selectedGroup?.purchaseOrderNumber)); setDuplicateWarning(null); setPreviousPoReuse(null); setReuseConfirmed(false); setReuseComment(''); }}
                                >
                                    {duplicateWarning && (
                                        <div data-testid="correct-po-duplicate" style={{ marginTop: '12px', padding: '16px', backgroundColor: '#fef2f2', border: '2px solid #ef4444', borderRadius: '8px' }}>
                                            <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', marginBottom: '12px' }}>
                                                <AlertTriangle size={20} color="#ef4444" style={{ flexShrink: 0 }} />
                                                <div>
                                                    <h4 style={{ margin: '0 0 4px 0', color: '#b91c1c', fontSize: '0.9rem', fontWeight: 800 }}>P.O Duplicada (SISTEMA CENTRAL)</h4>
                                                    <p style={{ margin: 0, color: '#991b1b', fontSize: '0.8rem', fontWeight: 600 }}>{duplicateWarning}</p>
                                                </div>
                                            </div>
                                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#fee2e2', padding: '12px', borderRadius: '4px', marginBottom: '12px' }}>
                                                <input type="checkbox" id="correctPoOverrideDuplicate" checked={overrideDuplicateConfirmed} onChange={(e) => setOverrideDuplicateConfirmed(e.target.checked)} style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }} />
                                                <label htmlFor="correctPoOverrideDuplicate" style={{ cursor: 'pointer', color: '#991b1b', fontSize: '0.8rem', fontWeight: 700, lineHeight: 1.4 }}>
                                                    Confirmar uso do número de P.O duplicado sob minha responsabilidade.
                                                </label>
                                            </div>
                                            {overrideDuplicateConfirmed && (
                                                <textarea value={duplicateOverrideComment} onChange={(e) => setDuplicateOverrideComment(e.target.value)} placeholder="Justificativa obrigatória para P.O duplicada..." rows={2} style={{ ...inputStyle, resize: 'none', borderColor: '#ef4444' }} />
                                            )}
                                        </div>
                                    )}
                                    {backendOcrMismatch && (
                                        <div data-testid="correct-po-backend-mismatch" style={{ marginTop: '12px', padding: '16px', backgroundColor: '#fff7ed', border: '2px solid #ef4444', borderRadius: '8px' }}>
                                            <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', marginBottom: '12px' }}>
                                                <AlertTriangle size={20} color="#ef4444" style={{ flexShrink: 0 }} />
                                                <div>
                                                    <h4 style={{ margin: '0 0 4px 0', color: '#b91c1c', fontSize: '0.9rem', fontWeight: 800 }}>Divergência Detectada (SISTEMA CENTRAL)</h4>
                                                    <p style={{ margin: 0, color: '#991b1b', fontSize: '0.8rem', fontWeight: 600 }}>{backendOcrMismatch.details}</p>
                                                </div>
                                            </div>
                                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#fee2e2', padding: '12px', borderRadius: '4px' }}>
                                                <input type="checkbox" id="correctPoOverrideBackend" checked={overrideConfirmed} onChange={(e) => setOverrideConfirmed(e.target.checked)} style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }} />
                                                <label htmlFor="correctPoOverrideBackend" style={{ cursor: 'pointer', color: '#991b1b', fontSize: '0.8rem', fontWeight: 700, lineHeight: 1.4 }}>
                                                    Estou ciente das divergências reportadas pelo sistema central e confirmo a correção sob minha responsabilidade (justificativa obrigatória no comentário).
                                                </label>
                                            </div>
                                        </div>
                                    )}
                                </PoNumberFieldBlock>

                                {documentBlock && (
                                    <div role="alert" data-testid="correct-po-document-block" style={{ marginBottom: '24px', padding: '16px', backgroundColor: '#fef2f2', border: '2px solid #ef4444', borderRadius: '8px', display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                                        <AlertTriangle size={20} color="#ef4444" style={{ flexShrink: 0 }} />
                                        <div>
                                            <h4 style={{ margin: '0 0 4px 0', color: '#b91c1c', fontSize: '0.9rem', fontWeight: 800 }}>Documento recusado como P.O.</h4>
                                            <p style={{ margin: 0, color: '#991b1b', fontSize: '0.8rem', fontWeight: 600 }}>{documentBlock}</p>
                                            <p style={{ margin: '6px 0 0', color: '#991b1b', fontSize: '0.75rem' }}>Remova este ficheiro e carregue a Ordem de Compra emitida pelo ERP. O ficheiro recusado não é registado como P.O.</p>
                                        </div>
                                    </div>
                                )}

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
                                        Comentário {justification.required ? '(Obrigatório)' : '(opcional)'}
                                    </label>
                                    {justification.source === 'LEGACY_RETURN' && (
                                        <div style={{ marginBottom: '10px', fontSize: '0.78rem', fontWeight: 600, color: '#d97706', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                            <AlertTriangle size={14} />
                                            Esta P.O foi devolvida por Finanças: a justificativa do Comprador é obrigatória e ficará registada no histórico.
                                        </div>
                                    )}
                                    <textarea
                                        value={comment}
                                        onChange={(e) => setComment(e.target.value)}
                                        placeholder={justification.source === 'OCR_MISMATCH'
                                            ? 'Justifique o motivo de ignorar a divergência...'
                                            : justification.source === 'DOCUMENT_TYPE'
                                                ? 'Justifique porque este documento é uma Ordem de Compra apesar de não ter sido identificado automaticamente...'
                                            : justification.source === 'LEGACY_RETURN'
                                                ? 'Descreva a correção realizada na P.O devolvida por Finanças...'
                                                : 'Descreva a correção realizada na P.O, se necessário...'}
                                        rows={3}
                                        style={{
                                            ...inputStyle,
                                            height: 'auto',
                                            padding: '16px',
                                            borderColor: justification.required && !comment.trim() ? '#f97316' : 'var(--color-border)'
                                        }}
                                    />
                                </div>

                                {/* Phase 1 P.O. cancellation (D15): the corrected P.O. reuses the number of a previous CANCELLED P.O. */}
                                {previousPoReuse && (
                                    <div data-tour="correct-po-previous-number-reuse" style={{ marginBottom: '24px', padding: '16px', backgroundColor: '#fffbeb', border: '2px solid #f59e0b', borderRadius: '8px' }}>
                                        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start', marginBottom: '12px' }}>
                                            <AlertTriangle size={20} color="#d97706" style={{ flexShrink: 0 }} />
                                            <div>
                                                <h4 style={{ margin: '0 0 4px 0', color: '#92400e', fontSize: '0.9rem', fontWeight: 800 }}>{REGISTER_PO_ERROR_MESSAGES.reuseWarningTitle}</h4>
                                                <p style={{ margin: 0, color: '#92400e', fontSize: '0.8rem', fontWeight: 600 }}>{previousPoReuse.message}</p>
                                            </div>
                                        </div>
                                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', backgroundColor: '#fef3c7', padding: '12px', borderRadius: '4px', marginBottom: '12px' }}>
                                            <input type="checkbox" id="correctPoPreviousReuseConfirm" checked={reuseConfirmed} onChange={(e) => setReuseConfirmed(e.target.checked)} style={{ marginTop: '2px', cursor: 'pointer', width: '16px', height: '16px' }} />
                                            <label htmlFor="correctPoPreviousReuseConfirm" style={{ cursor: 'pointer', color: '#92400e', fontSize: '0.8rem', fontWeight: 700, lineHeight: 1.4 }}>
                                                {REGISTER_PO_ERROR_MESSAGES.reuseCheckbox}
                                            </label>
                                        </div>
                                        <textarea value={reuseComment} onChange={(e) => setReuseComment(e.target.value)} rows={2}
                                            placeholder={`Explique porque a nova P.O. reutiliza o número da P.O. cancelada (mínimo ${MIN_JUSTIFICATION_LENGTH} caracteres)...`}
                                            style={{ ...inputStyle, resize: 'none', borderColor: '#f59e0b' }} />
                                        <div style={{ marginTop: '6px', fontSize: '0.7rem', fontWeight: 600, color: reuseComment.trim().length < MIN_JUSTIFICATION_LENGTH ? '#b45309' : 'var(--color-text-muted)' }}>
                                            {reuseComment.trim().length}/{MIN_JUSTIFICATION_LENGTH} caracteres mínimos
                                        </div>
                                    </div>
                                )}
                                {/* Phase 1 P.O. cancellation (D4): blocked until Finance confirms the previous ECF annulment — no retry. */}
                                {priorEcfBlock && (
                                    <div role="alert" data-tour="correct-po-prior-ecf-block" style={{ marginBottom: '24px', padding: '16px', backgroundColor: '#fef2f2', border: '2px solid #ef4444', borderRadius: '8px', display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                                        <AlertTriangle size={20} color="#ef4444" style={{ flexShrink: 0 }} />
                                        <div>
                                            <h4 style={{ margin: '0 0 4px 0', color: '#b91c1c', fontSize: '0.9rem', fontWeight: 800 }}>Registo da nova P.O. bloqueado</h4>
                                            <p style={{ margin: 0, color: '#991b1b', fontSize: '0.8rem', fontWeight: 600 }}>{priorEcfBlock}</p>
                                            <p style={{ margin: '6px 0 0', color: '#991b1b', fontSize: '0.75rem' }}>Finanças tem de confirmar a anulação da ECF anterior antes de qualquer nova P.O. para estes itens. Feche esta janela; não é possível tentar novamente aqui.</p>
                                        </div>
                                    </div>
                                )}

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
                                        disabled={submitDisabled}
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
                                            cursor: submitDisabled ? 'not-allowed' : 'pointer',
                                            fontWeight: 800,
                                            borderRadius: 'var(--radius-sm)',
                                            boxShadow: 'var(--shadow-md)',
                                            fontFamily: 'var(--font-family-display)',
                                            fontSize: '0.875rem',
                                            opacity: submitDisabled ? 0.7 : 1
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
