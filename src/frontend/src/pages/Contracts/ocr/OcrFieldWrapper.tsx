/**
 * OcrFieldWrapper
 *
 * Conditionally wraps a form field with OCR visual treatment when an
 * OcrFieldState exists for that field. Renders the field unchanged when
 * ocrFieldState is undefined — zero impact on non-OCR fields.
 *
 * AUTO_FILL fields get:
 *   - Amber left border on the input container
 *   - A small "OCR" badge with confidence score
 *   - Confirmar / Limpar action buttons below the input
 *
 * SUGGESTION fields get:
 *   - An OcrSuggestionChip rendered below the empty input
 *   - No border change (field stays in its default "empty" appearance)
 *
 * Usage (Batch 7):
 *   <OcrFieldWrapper fieldName="EffectiveDateUtc" ocrState={ocr.fields} onConfirm={ocr.confirmField} onDiscard={ocr.discardField}>
 *     <DateInput ... />
 *   </OcrFieldWrapper>
 */

import React from 'react';
import { CheckCircle, X, Cpu } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import type { OcrState } from '../../../types/contractOcr.types';
import { OcrSuggestionChip } from './OcrSuggestionChip';

// ─── Colours (kept local — not worth a design token for OCR-only UI) ─────────
const OCR_AMBER = {
    border:     '#d97706',
    bg:         '#fffbeb',
    text:       '#92400e',
    badgeBg:    '#fef3c7',
    badgeText:  '#b45309',
    btnBg:      '#d97706',
    btnHover:   '#b45309',
};
const OCR_CONFIRMED = {
    border:     '#059669',
    bg:         '#f0fdf4',
    badgeBg:    '#d1fae5',
    badgeText:  '#065f46',
};

interface OcrFieldWrapperProps {
    fieldName: string;
    ocrState: OcrState;
    onConfirm: (fieldName: string, acceptedValue?: string) => Promise<void>;
    onDiscard: (fieldName: string) => Promise<void>;
    children: React.ReactNode;
    /** Override value to pass when confirming (e.g. current form input value) */
    currentValue?: string;
}

export function OcrFieldWrapper({
    fieldName,
    ocrState,
    onConfirm,
    onDiscard,
    children,
    currentValue,
}: OcrFieldWrapperProps) {
    const field = ocrState[fieldName];

    // ── No OCR data → render children completely untouched ────────────────────
    if (!field || field.discarded) {
        return <>{children}</>;
    }

    const isAutoFill   = field.displayHint === 'AUTO_FILL';
    const isSuggestion = field.displayHint === 'SUGGESTION';
    const isConfirmed  = field.confirmed;

    const pct = field.confidenceScore != null
        ? Math.round(field.confidenceScore * 100)
        : null;

    const colours = isConfirmed ? OCR_CONFIRMED : OCR_AMBER;

    return (
        <div style={{ position: 'relative' }}>

            {/* ── Confidence badge (top-right overlay, only for AUTO_FILL) ─── */}
            {isAutoFill && (
                <div style={{
                    position: 'absolute',
                    top: '-10px',
                    right: '0',
                    zIndex: 1,
                    display: 'flex',
                    alignItems: 'center',
                    gap: '4px',
                    padding: '2px 8px',
                    backgroundColor: isConfirmed ? OCR_CONFIRMED.badgeBg : OCR_AMBER.badgeBg,
                    color:           isConfirmed ? OCR_CONFIRMED.badgeText : OCR_AMBER.badgeText,
                    borderRadius:    '100px',
                    fontSize:        '0.6rem',
                    fontWeight:      800,
                    letterSpacing:   '0.06em',
                    textTransform:   'uppercase',
                    border: `1px solid ${isConfirmed ? OCR_CONFIRMED.border : OCR_AMBER.border}`,
                    pointerEvents:   'none',
                }}>
                    <Cpu size={9} strokeWidth={2.5} />
                    OCR {pct != null ? `${pct}%` : ''}
                    {isConfirmed && <CheckCircle size={9} strokeWidth={2.5} />}
                </div>
            )}

            {/* ── Input container — amber/green left border for AUTO_FILL ──── */}
            <div style={{
                borderLeft: isAutoFill
                    ? `3px solid ${colours.border}`
                    : 'none',
                borderRadius: isAutoFill ? '0 var(--radius-md) var(--radius-md) 0' : undefined,
                paddingLeft:  isAutoFill ? '8px' : undefined,
                backgroundColor: isAutoFill
                    ? (isConfirmed ? '#f0fdf420' : '#fffbeb20')
                    : 'transparent',
                transition: 'border-color 0.25s, background-color 0.25s',
            }}>
                {children}
            </div>

            {/* ── Action buttons (AUTO_FILL, not yet confirmed) ─────────────── */}
            <AnimatePresence>
                {isAutoFill && !isConfirmed && (
                    <motion.div
                        key="ocr-actions"
                        initial={{ opacity: 0, y: -4 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -4 }}
                        transition={{ duration: 0.15 }}
                        style={{
                            display:    'flex',
                            gap:        '6px',
                            marginTop:  '5px',
                            alignItems: 'center',
                        }}
                    >
                        <button
                            type="button"
                            onClick={() => onConfirm(fieldName, currentValue)}
                            style={{
                                display:        'flex',
                                alignItems:     'center',
                                gap:            '4px',
                                padding:        '3px 10px',
                                fontSize:       '0.7rem',
                                fontWeight:     700,
                                backgroundColor: OCR_AMBER.btnBg,
                                color:          'white',
                                border:         'none',
                                borderRadius:   'var(--radius-sm)',
                                cursor:         'pointer',
                                letterSpacing:  '0.03em',
                                transition:     'background-color 0.15s',
                            }}
                            onMouseOver={e => (e.currentTarget.style.backgroundColor = OCR_AMBER.btnHover)}
                            onMouseOut={e  => (e.currentTarget.style.backgroundColor = OCR_AMBER.btnBg)}
                        >
                            <CheckCircle size={11} strokeWidth={2.5} />
                            Confirmar
                        </button>

                        <button
                            type="button"
                            onClick={() => onDiscard(fieldName)}
                            style={{
                                display:        'flex',
                                alignItems:     'center',
                                gap:            '4px',
                                padding:        '3px 9px',
                                fontSize:       '0.7rem',
                                fontWeight:     600,
                                backgroundColor: 'transparent',
                                color:           OCR_AMBER.text,
                                border:         `1px solid ${OCR_AMBER.border}`,
                                borderRadius:   'var(--radius-sm)',
                                cursor:         'pointer',
                                transition:     'opacity 0.15s',
                            }}
                            onMouseOver={e => (e.currentTarget.style.opacity = '0.7')}
                            onMouseOut={e  => (e.currentTarget.style.opacity = '1')}
                        >
                            <X size={10} strokeWidth={2} />
                            Limpar
                        </button>

                        {field.rawValue && (
                            <span style={{
                                fontSize:  '0.65rem',
                                color:     'var(--color-text-muted)',
                                fontStyle: 'italic',
                                overflow:  'hidden',
                                textOverflow: 'ellipsis',
                                whiteSpace:   'nowrap',
                                maxWidth:     '180px',
                            }}>
                                OCR: "{field.rawValue}"
                            </span>
                        )}
                    </motion.div>
                )}

                {/* ── Confirmed tick ─────────────────────────────────────────── */}
                {isAutoFill && isConfirmed && (
                    <motion.div
                        key="ocr-confirmed"
                        initial={{ opacity: 0, scale: 0.85 }}
                        animate={{ opacity: 1, scale: 1 }}
                        exit={{ opacity: 0 }}
                        transition={{ duration: 0.2 }}
                        style={{
                            display:    'flex',
                            alignItems: 'center',
                            gap:        '4px',
                            marginTop:  '5px',
                            fontSize:   '0.7rem',
                            fontWeight: 600,
                            color:      OCR_CONFIRMED.badgeText,
                        }}
                    >
                        <CheckCircle size={12} strokeWidth={2.5} />
                        Confirmado pelo utilizador
                        <button
                            type="button"
                            onClick={() => onDiscard(fieldName)}
                            style={{
                                marginLeft:     '4px',
                                background:     'none',
                                border:         'none',
                                cursor:         'pointer',
                                color:          'var(--color-text-muted)',
                                padding:        '0 2px',
                                fontSize:       '0.65rem',
                                display:        'inline-flex',
                                alignItems:     'center',
                                gap:            '2px',
                            }}
                            title="Desfazer confirmação"
                        >
                            <X size={10} /> Desfazer
                        </button>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* ── Suggestion chip (SUGGESTION hint, no border treatment) ────── */}
            {isSuggestion && (
                <OcrSuggestionChip
                    field={field}
                    onApply={() => onConfirm(fieldName, field.normalisedValue ?? undefined)}
                    onDiscard={() => onDiscard(fieldName)}
                />
            )}
        </div>
    );
}
