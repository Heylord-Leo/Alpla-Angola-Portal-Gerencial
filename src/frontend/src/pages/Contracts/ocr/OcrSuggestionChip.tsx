/**
 * OcrSuggestionChip
 *
 * Rendered below an empty form field when displayHint === 'SUGGESTION'.
 * The chip shows the OCR-suggested value with Aplicar / Ignorar actions.
 *
 * Clicking "Aplicar":
 *   - For most text fields: calls onApply (which confirms the value via the hook)
 *   - For the SupplierMatch field: handled separately in Batch 7 — the chip
 *     pre-populates the SupplierAutocomplete search box rather than setting supplierId.
 *     The `isSupplierField` prop signals this case and replaces the button label with
 *     "Usar como pesquisa" and disables the chip's own confirmation flow.
 *
 * The chip is hidden once the field has been confirmed or discarded.
 */

import { Sparkles, Check, X } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import type { OcrFieldState } from '../../../types/contractOcr.types';

interface OcrSuggestionChipProps {
    field: OcrFieldState;
    onApply: () => void;
    onDiscard: () => void;
    /**
     * When true, the chip is for the SupplierMatch field.
     * "Aplicar" becomes "Usar como pesquisa" — signals the parent to
     * pre-fill the autocomplete input, not resolve a supplierId.
     */
    isSupplierField?: boolean;
}

const CHIP_BLUE = {
    bg:         '#EFF6FF',
    border:     '#BFDBFE',
    text:       '#1D4ED8',
    btnBg:      '#2563EB',
    btnHover:   '#1D4ED8',
    mutedText:  '#64748B',
};

export function OcrSuggestionChip({
    field,
    onApply,
    onDiscard,
    isSupplierField = false,
}: OcrSuggestionChipProps) {
    // Already acted on — render nothing
    if (field.confirmed || field.discarded) return null;
    if (!field.normalisedValue && !field.rawValue) return null;

    const displayValue = field.normalisedValue ?? field.rawValue ?? '';
    const pct = field.confidenceScore != null
        ? Math.round(field.confidenceScore * 100)
        : null;

    return (
        <AnimatePresence>
            <motion.div
                key="suggestion-chip"
                initial={{ opacity: 0, y: -6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.18 }}
                style={{
                    display:         'flex',
                    alignItems:      'center',
                    gap:             '8px',
                    marginTop:       '6px',
                    padding:         '6px 10px',
                    backgroundColor: CHIP_BLUE.bg,
                    border:          `1px solid ${CHIP_BLUE.border}`,
                    borderRadius:    'var(--radius-sm)',
                    flexWrap:        'wrap',
                }}
            >
                {/* Icon + label */}
                <Sparkles size={13} color={CHIP_BLUE.text} strokeWidth={2} style={{ flexShrink: 0 }} />

                <span style={{
                    fontSize:     '0.72rem',
                    fontWeight:   600,
                    color:        CHIP_BLUE.text,
                    flexShrink:   0,
                }}>
                    OCR{pct != null ? ` (${pct}%)` : ''}:
                </span>

                {/* Suggested value */}
                <span
                    title={displayValue}
                    style={{
                        fontSize:      '0.75rem',
                        color:         '#1e293b',
                        fontWeight:    500,
                        flex:          1,
                        overflow:      'hidden',
                        textOverflow:  'ellipsis',
                        whiteSpace:    'nowrap',
                        maxWidth:      '260px',
                    }}
                >
                    {displayValue}
                </span>

                {/* Actions */}
                <div style={{ display: 'flex', gap: '5px', flexShrink: 0 }}>
                    <button
                        type="button"
                        onClick={onApply}
                        style={{
                            display:         'flex',
                            alignItems:      'center',
                            gap:             '3px',
                            padding:         '2px 8px',
                            fontSize:        '0.68rem',
                            fontWeight:      700,
                            backgroundColor: CHIP_BLUE.btnBg,
                            color:           'white',
                            border:          'none',
                            borderRadius:    'var(--radius-sm)',
                            cursor:          'pointer',
                            letterSpacing:   '0.02em',
                            transition:      'background-color 0.15s',
                            whiteSpace:      'nowrap',
                        }}
                        onMouseOver={e => (e.currentTarget.style.backgroundColor = CHIP_BLUE.btnHover)}
                        onMouseOut={e  => (e.currentTarget.style.backgroundColor = CHIP_BLUE.btnBg)}
                    >
                        <Check size={10} strokeWidth={2.5} />
                        {isSupplierField ? 'Usar como pesquisa' : 'Aplicar'}
                    </button>

                    <button
                        type="button"
                        onClick={onDiscard}
                        style={{
                            display:         'flex',
                            alignItems:      'center',
                            gap:             '3px',
                            padding:         '2px 7px',
                            fontSize:        '0.68rem',
                            fontWeight:      600,
                            backgroundColor: 'transparent',
                            color:           CHIP_BLUE.mutedText,
                            border:          `1px solid ${CHIP_BLUE.border}`,
                            borderRadius:    'var(--radius-sm)',
                            cursor:          'pointer',
                            transition:      'opacity 0.15s',
                            whiteSpace:      'nowrap',
                        }}
                        onMouseOver={e => (e.currentTarget.style.opacity = '0.6')}
                        onMouseOut={e  => (e.currentTarget.style.opacity = '1')}
                    >
                        <X size={10} strokeWidth={2} />
                        Ignorar
                    </button>
                </div>
            </motion.div>
        </AnimatePresence>
    );
}
