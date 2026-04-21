/**
 * ContractOcrUploadZone
 *
 * A document upload + OCR trigger interaction panel for use at the top of
 * ContractCreate / ContractEdit. It is NOT a generic file uploader — it is
 * specifically scoped to the OCR workflow.
 *
 * Responsibilities:
 *   1. Show a drag-and-drop zone when no contract document has been uploaded yet.
 *   2. Once a document exists (contractHasDocument = true), show the OCR trigger button.
 *   3. Display OCR progress (polling state) while processing.
 *   4. Display success state (COMPLETED) with a "Re-executar" option.
 *   5. Display FAILED state with an error message and retry option.
 *   6. Handle re-trigger UX: ask for confirmation before discarding current results.
 *
 * File upload itself is delegated to the `onUploadFile` prop — the parent
 * (ContractCreate) owns the actual API call (upload via existing uploadContractDocument).
 * This component only emits events and renders state.
 *
 * Props contract is intentionally minimal. Batch 7 wires all the state in.
 */

import React, { useRef, useState } from 'react';
import { Upload, FileText, Cpu, CheckCircle, AlertCircle, RefreshCw, Loader2, X } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import type { OcrProcessingStatus } from '../../../types/contractOcr.types';

interface ContractOcrUploadZoneProps {
    /** True once the contract has at least one document uploaded */
    contractHasDocument: boolean;
    /** File name of the current primary document (for display purposes) */
    primaryDocumentName?: string;
    /** Current OCR pipeline status */
    ocrStatus: OcrProcessingStatus | null;
    /** Quality score from the completed extraction (0.0-1.0) */
    ocrQualityScore?: number | null;
    /** True when OCR is actively polling */
    isPolling: boolean;
    /** OCR error message (FAILED state) */
    ocrError: string | null;
    /** Called when the user drops or selects a file to upload */
    onUploadFile: (file: File) => void;
    /** Called when the user clicks "Iniciar OCR" or "Re-executar" */
    onTriggerOcr: () => void;
    /** Whether an upload is currently in-flight */
    isUploading?: boolean;
}

const ZONE_COLORS = {
    idle:        { bg: '#f8fafc', border: '#cbd5e1', text: '#475569' },
    hover:       { bg: '#eff6ff', border: '#3b82f6', text: '#1d4ed8' },
    hasDoc:      { bg: '#f0fdf4', border: '#86efac', text: '#15803d' },
    processing:  { bg: '#fffbeb', border: '#fcd34d', text: '#92400e' },
    completed:   { bg: '#f0fdf4', border: '#4ade80', text: '#15803d' },
    failed:      { bg: '#fef2f2', border: '#fca5a5', text: '#b91c1c' },
};

function qualityLabel(score: number): { label: string; color: string } {
    if (score >= 0.85) return { label: 'Excelente', color: '#059669' };
    if (score >= 0.65) return { label: 'Boa',       color: '#d97706' };
    return                { label: 'Parcial',       color: '#dc2626' };
}

export function ContractOcrUploadZone({
    contractHasDocument,
    primaryDocumentName,
    ocrStatus,
    ocrQualityScore,
    isPolling,
    ocrError,
    onUploadFile,
    onTriggerOcr,
    isUploading = false,
}: ContractOcrUploadZoneProps) {
    const fileInputRef = useRef<HTMLInputElement>(null);
    const [isDragging, setIsDragging] = useState(false);
    const [showRetriggerConfirm, setShowRetriggerConfirm] = useState(false);

    // ── Drag handlers ─────────────────────────────────────────────────────────
    const handleDragOver = (e: React.DragEvent) => {
        e.preventDefault();
        setIsDragging(true);
    };
    const handleDragLeave = () => setIsDragging(false);
    const handleDrop = (e: React.DragEvent) => {
        e.preventDefault();
        setIsDragging(false);
        const file = e.dataTransfer.files[0];
        if (file) onUploadFile(file);
    };
    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (file) onUploadFile(file);
        e.target.value = '';
    };

    // ── Re-trigger guard ──────────────────────────────────────────────────────
    const handleTriggerClick = () => {
        if (ocrStatus === 'COMPLETED') {
            setShowRetriggerConfirm(true);
        } else {
            onTriggerOcr();
        }
    };

    // ── Determine zone colour ─────────────────────────────────────────────────
    let zone = isDragging ? ZONE_COLORS.hover : ZONE_COLORS.idle;
    if (ocrStatus === 'PROCESSING' || ocrStatus === 'PENDING' || isPolling) zone = ZONE_COLORS.processing;
    else if (ocrStatus === 'COMPLETED')  zone = ZONE_COLORS.completed;
    else if (ocrStatus === 'FAILED')     zone = ZONE_COLORS.failed;
    else if (contractHasDocument)        zone = ZONE_COLORS.hasDoc;

    const isActive = ocrStatus === 'PENDING' || ocrStatus === 'PROCESSING' || isPolling;

    return (
        <div style={{ marginBottom: '0' }}>

            {/* ── Main zone ─────────────────────────────────────────────────── */}
            <motion.div
                layout
                style={{
                    border:          `2px dashed ${zone.border}`,
                    borderRadius:    'var(--radius-lg)',
                    backgroundColor: zone.bg,
                    padding:         '20px 24px',
                    transition:      'all 0.2s ease',
                    cursor:          !contractHasDocument && !isUploading ? 'pointer' : 'default',
                }}
                onClick={() => {
                    if (!contractHasDocument && !isUploading) fileInputRef.current?.click();
                }}
                onDragOver={!contractHasDocument ? handleDragOver : undefined}
                onDragLeave={!contractHasDocument ? handleDragLeave : undefined}
                onDrop={!contractHasDocument ? handleDrop : undefined}
            >
                <input
                    ref={fileInputRef}
                    type="file"
                    accept=".pdf,.docx,.doc,.jpg,.jpeg,.png"
                    style={{ display: 'none' }}
                    onChange={handleFileChange}
                />

                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '14px' }}>

                    {/* Icon col */}
                    <div style={{
                        width:           '40px',
                        height:          '40px',
                        borderRadius:    'var(--radius-md)',
                        backgroundColor: zone.border + '33',
                        display:         'flex',
                        alignItems:      'center',
                        justifyContent:  'center',
                        flexShrink:      0,
                    }}>
                        {isActive ? (
                            <Loader2 size={20} color={zone.text}
                                style={{ animation: 'spin 1.1s linear infinite' }} />
                        ) : ocrStatus === 'COMPLETED' ? (
                            <CheckCircle size={20} color={zone.text} />
                        ) : ocrStatus === 'FAILED' ? (
                            <AlertCircle size={20} color={zone.text} />
                        ) : contractHasDocument ? (
                            <FileText size={20} color={zone.text} />
                        ) : (
                            <Upload size={20} color={zone.text} />
                        )}
                    </div>

                    {/* Content col */}
                    <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{
                            fontSize:   '0.85rem',
                            fontWeight: 700,
                            color:      zone.text,
                            marginBottom: '3px',
                        }}>
                            {isUploading ? 'A carregar documento...' :
                             isActive    ? 'A processar documento OCR...' :
                             ocrStatus === 'COMPLETED' ? 'Documento analisado com OCR' :
                             ocrStatus === 'FAILED'    ? 'Processamento OCR falhou' :
                             contractHasDocument        ? 'Documento pronto para OCR' :
                             'Carregar documento do contrato'}
                        </div>

                        <div style={{ fontSize: '0.75rem', color: '#64748b' }}>
                            {isActive ? 'Os campos serão preenchidos automaticamente após a análise.' :
                             ocrStatus === 'COMPLETED' && ocrQualityScore != null ? (() => {
                                 const q = qualityLabel(ocrQualityScore);
                                 return (
                                     <span>
                                         Qualidade da extracção:{' '}
                                         <strong style={{ color: q.color }}>{q.label}</strong>
                                         {' '}({Math.round(ocrQualityScore * 100)}%)
                                         {primaryDocumentName && <> · {primaryDocumentName}</>}
                                     </span>
                                 );
                             })() :
                             ocrStatus === 'FAILED' ? ocrError || 'Tente novamente ou preencha o formulário manualmente.' :
                             contractHasDocument && primaryDocumentName ? primaryDocumentName :
                             'Arraste e solte ou clique para seleccionar (PDF, DOCX, JPG, PNG)'}
                        </div>
                    </div>

                    {/* Action col */}
                    <div style={{ flexShrink: 0, alignSelf: 'center' }}>
                        {contractHasDocument && !isActive && ocrStatus !== 'COMPLETED' && (
                            <button
                                type="button"
                                onClick={(e) => { e.stopPropagation(); onTriggerOcr(); }}
                                disabled={isUploading}
                                style={{
                                    display:         'flex',
                                    alignItems:      'center',
                                    gap:             '6px',
                                    padding:         '7px 14px',
                                    fontSize:        '0.8rem',
                                    fontWeight:      700,
                                    backgroundColor: 'var(--color-primary)',
                                    color:           'white',
                                    border:          'none',
                                    borderRadius:    'var(--radius-md)',
                                    cursor:          isUploading ? 'not-allowed' : 'pointer',
                                    opacity:         isUploading ? 0.6 : 1,
                                    transition:      'opacity 0.15s',
                                }}
                            >
                                <Cpu size={14} strokeWidth={2} />
                                Iniciar OCR
                            </button>
                        )}

                        {ocrStatus === 'COMPLETED' && (
                            <button
                                type="button"
                                onClick={(e) => { e.stopPropagation(); handleTriggerClick(); }}
                                style={{
                                    display:         'flex',
                                    alignItems:      'center',
                                    gap:             '5px',
                                    padding:         '5px 10px',
                                    fontSize:        '0.72rem',
                                    fontWeight:      600,
                                    backgroundColor: 'transparent',
                                    color:           '#64748b',
                                    border:          '1px solid #e2e8f0',
                                    borderRadius:    'var(--radius-sm)',
                                    cursor:          'pointer',
                                    transition:      'all 0.15s',
                                }}
                                onMouseOver={e => {
                                    e.currentTarget.style.borderColor = 'var(--color-primary)';
                                    e.currentTarget.style.color       = 'var(--color-primary)';
                                }}
                                onMouseOut={e => {
                                    e.currentTarget.style.borderColor = '#e2e8f0';
                                    e.currentTarget.style.color       = '#64748b';
                                }}
                            >
                                <RefreshCw size={12} strokeWidth={2} />
                                Re-executar
                            </button>
                        )}

                        {ocrStatus === 'FAILED' && (
                            <button
                                type="button"
                                onClick={(e) => { e.stopPropagation(); onTriggerOcr(); }}
                                style={{
                                    display:         'flex',
                                    alignItems:      'center',
                                    gap:             '5px',
                                    padding:         '6px 12px',
                                    fontSize:        '0.78rem',
                                    fontWeight:      700,
                                    backgroundColor: '#fee2e2',
                                    color:           '#b91c1c',
                                    border:          '1px solid #fca5a5',
                                    borderRadius:    'var(--radius-sm)',
                                    cursor:          'pointer',
                                }}
                            >
                                <RefreshCw size={13} strokeWidth={2} />
                                Tentar novamente
                            </button>
                        )}
                    </div>
                </div>
            </motion.div>

            {/* ── Re-trigger confirmation (inline, not modal) ────────────────── */}
            <AnimatePresence>
                {showRetriggerConfirm && (
                    <motion.div
                        initial={{ opacity: 0, y: -6 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -6 }}
                        style={{
                            marginTop:       '8px',
                            padding:         '10px 14px',
                            backgroundColor: '#fffbeb',
                            border:          '1px solid #fcd34d',
                            borderRadius:    'var(--radius-md)',
                            display:         'flex',
                            alignItems:      'center',
                            gap:             '10px',
                            fontSize:        '0.8rem',
                            color:           '#92400e',
                        }}
                    >
                        <AlertCircle size={16} style={{ flexShrink: 0 }} />
                        <span style={{ flex: 1 }}>
                            Isto irá limpar os resultados OCR actuais. Continuar?
                        </span>
                        <button
                            type="button"
                            onClick={() => {
                                setShowRetriggerConfirm(false);
                                onTriggerOcr();
                            }}
                            style={{
                                padding:         '3px 10px',
                                fontSize:        '0.75rem',
                                fontWeight:      700,
                                backgroundColor: '#d97706',
                                color:           'white',
                                border:          'none',
                                borderRadius:    'var(--radius-sm)',
                                cursor:          'pointer',
                            }}
                        >
                            Confirmar
                        </button>
                        <button
                            type="button"
                            onClick={() => setShowRetriggerConfirm(false)}
                            style={{
                                background: 'none',
                                border:     'none',
                                cursor:     'pointer',
                                color:      '#92400e',
                                padding:    '2px',
                                display:    'flex',
                            }}
                        >
                            <X size={16} />
                        </button>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
