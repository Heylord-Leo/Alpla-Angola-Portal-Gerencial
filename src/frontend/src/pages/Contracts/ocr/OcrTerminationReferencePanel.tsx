/**
 * OcrTerminationReferencePanel
 *
 * A dedicated read-only panel for the TerminationClauses, SignatoryNames,
 * and ExternalContractReference REFERENCE_ONLY fields extracted by OCR.
 *
 * Placement: rendered above or below the "Cláusulas Legais" section in
 * ContractCreate — distinct from ContractOcrSummaryPanel which is a sidebar.
 * This panel lives inline in the form, giving the user direct access to the
 * extracted legal text while filling in the governing law and termination fields.
 *
 * It does NOT apply values to any form field. It is purely informational.
 * The user reads the extracted text and manually types relevant parts into the form.
 *
 * Rendered only when at least one REFERENCE_ONLY field has extracted content.
 */

import { useState } from 'react';
import { BookOpen, ChevronDown, ChevronUp, Copy, Check } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import type { OcrExtractedField } from '../../../types/contractOcr.types';

const FIELD_LABELS: Record<string, string> = {
    TerminationClauses:         'Cláusulas de Rescisão (OCR)',
    SignatoryNames:             'Signatários identificados (OCR)',
    ExternalContractReference:  'Referência externa do contrato (OCR)',
};

interface FieldBlockProps {
    field: OcrExtractedField;
}

function FieldBlock({ field }: FieldBlockProps) {
    const [expanded,   setExpanded]   = useState(false);
    const [copied,     setCopied]     = useState(false);

    const value = field.normalisedValue ?? field.rawExtractedValue ?? '';
    if (!value) return null;

    const isLong = value.length > 300;

    const handleCopy = async () => {
        try {
            await navigator.clipboard.writeText(value);
            setCopied(true);
            setTimeout(() => setCopied(false), 2000);
        } catch {
            // clipboard unavailable — silently ignore
        }
    };

    return (
        <div style={{
            borderTop:   '1px solid #f1f5f9',
            paddingTop:  '12px',
            marginTop:   '12px',
        }}>
            {/* Field header */}
            <div style={{
                display:       'flex',
                alignItems:    'center',
                justifyContent: 'space-between',
                marginBottom:  '8px',
            }}>
                <span style={{
                    fontSize:      '0.7rem',
                    fontWeight:    800,
                    color:         '#475569',
                    textTransform: 'uppercase',
                    letterSpacing: '0.07em',
                }}>
                    {FIELD_LABELS[field.fieldName] ?? field.fieldName}
                </span>

                <button
                    type="button"
                    onClick={handleCopy}
                    title="Copiar para a área de transferência"
                    style={{
                        display:     'flex',
                        alignItems:  'center',
                        gap:         '4px',
                        padding:     '2px 8px',
                        fontSize:    '0.65rem',
                        fontWeight:  600,
                        background:  'none',
                        border:      '1px solid #e2e8f0',
                        borderRadius: 'var(--radius-sm)',
                        cursor:      'pointer',
                        color:       copied ? '#059669' : '#64748b',
                        transition:  'all 0.15s',
                    }}
                    onMouseOver={e => !copied && (e.currentTarget.style.borderColor = 'var(--color-primary)')}
                    onMouseOut={e  => !copied && (e.currentTarget.style.borderColor = '#e2e8f0')}
                >
                    {copied
                        ? <><Check size={10} strokeWidth={2.5} /> Copiado</>
                        : <><Copy size={10} strokeWidth={2} /> Copiar</>
                    }
                </button>
            </div>

            {/* Content */}
            <div style={{
                fontSize:            '0.82rem',
                color:               '#334155',
                lineHeight:          1.65,
                backgroundColor:     '#f8fafc',
                padding:             '10px 12px',
                borderRadius:        'var(--radius-sm)',
                border:              '1px solid #e2e8f0',
                overflow:            isLong && !expanded ? 'hidden' : undefined,
                display:             isLong && !expanded ? '-webkit-box' : 'block',
                WebkitLineClamp:     isLong && !expanded ? 5 : undefined,
                WebkitBoxOrient:     isLong && !expanded ? 'vertical' : undefined,
                whiteSpace:          'pre-wrap',
                wordBreak:           'break-word',
                maxHeight:           isLong && !expanded ? '120px' : undefined,
            }}>
                {value}
            </div>

            {/* Expand/collapse for long text */}
            {isLong && (
                <button
                    type="button"
                    onClick={() => setExpanded(v => !v)}
                    style={{
                        marginTop:   '6px',
                        background:  'none',
                        border:      'none',
                        cursor:      'pointer',
                        fontSize:    '0.7rem',
                        fontWeight:  600,
                        color:       'var(--color-primary)',
                        padding:     '0',
                        display:     'flex',
                        alignItems:  'center',
                        gap:         '3px',
                    }}
                >
                    {expanded
                        ? <><ChevronUp size={12} /> Mostrar menos</>
                        : <><ChevronDown size={12} /> Mostrar texto completo</>
                    }
                </button>
            )}
        </div>
    );
}

// ─── Main Panel ───────────────────────────────────────────────────────────────

interface OcrTerminationReferencePanelProps {
    /** All REFERENCE_ONLY fields from the extraction. This component filters to the relevant subset. */
    referenceFields: OcrExtractedField[];
}

const SHOWN_FIELDS = new Set(['TerminationClauses', 'SignatoryNames', 'ExternalContractReference']);

export function OcrTerminationReferencePanel({ referenceFields }: OcrTerminationReferencePanelProps) {
    const [open, setOpen] = useState(true);

    const relevant = referenceFields.filter(f =>
        SHOWN_FIELDS.has(f.fieldName) &&
        (f.normalisedValue || f.rawExtractedValue),
    );

    if (relevant.length === 0) return null;

    return (
        <div style={{
            backgroundColor: '#fafafa',
            border:          '1px solid #e2e8f0',
            borderLeft:      '3px solid #94a3b8',
            borderRadius:    'var(--radius-md)',
            overflow:        'hidden',
        }}>
            {/* Header */}
            <button
                type="button"
                onClick={() => setOpen(v => !v)}
                style={{
                    width:           '100%',
                    display:         'flex',
                    alignItems:      'center',
                    justifyContent:  'space-between',
                    padding:         '10px 14px',
                    background:      'transparent',
                    border:          'none',
                    borderBottom:    open ? '1px solid #e2e8f0' : 'none',
                    cursor:          'pointer',
                    textAlign:       'left',
                    transition:      'background 0.15s',
                }}
                onMouseOver={e => (e.currentTarget.style.backgroundColor = '#f1f5f9')}
                onMouseOut={e  => (e.currentTarget.style.backgroundColor = 'transparent')}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <BookOpen size={14} color="#64748b" strokeWidth={2} />
                    <span style={{
                        fontSize:      '0.75rem',
                        fontWeight:    700,
                        color:         '#475569',
                        textTransform: 'uppercase',
                        letterSpacing: '0.06em',
                    }}>
                        Informação legal extraída por OCR
                    </span>
                    <span style={{
                        fontSize:        '0.62rem',
                        fontWeight:      700,
                        color:           '#64748b',
                        backgroundColor: '#e2e8f0',
                        padding:         '1px 7px',
                        borderRadius:    '100px',
                    }}>
                        Apenas leitura
                    </span>
                </div>
                {open
                    ? <ChevronUp size={15} color="#94a3b8" />
                    : <ChevronDown size={15} color="#94a3b8" />
                }
            </button>

            {/* Content */}
            <AnimatePresence initial={false}>
                {open && (
                    <motion.div
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ duration: 0.2 }}
                        style={{ overflow: 'hidden' }}
                    >
                        <div style={{ padding: '10px 14px 14px' }}>
                            <p style={{
                                fontSize:    '0.75rem',
                                color:       '#64748b',
                                margin:      '0 0 4px 0',
                                lineHeight:  1.5,
                            }}>
                                O texto abaixo foi extraído do documento por OCR para referência.
                                Não é aplicado automaticamente ao formulário — use-o para preencher
                                manualmente os campos de cláusulas legais, se necessário.
                            </p>

                            {relevant.map(f => (
                                <FieldBlock key={f.fieldName} field={f} />
                            ))}
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
