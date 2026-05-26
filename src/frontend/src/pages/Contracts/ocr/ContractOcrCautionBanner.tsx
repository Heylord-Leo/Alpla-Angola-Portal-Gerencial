/**
 * ContractOcrCautionBanner
 *
 * Inline warning banners shown inside the form, not in the summary panel.
 * Two distinct banner types, each rendered independently:
 *
 *   1. UNCONFIRMED_AT_SUBMIT — shown when the user tries to submit and
 *      there are still unconfirmed AUTO_FILL fields in the gated set.
 *      This is the "exclude + warn" modal replacement for non-modal contexts.
 *
 *   2. CONFLICTS_DETECTED — shown when the backend flags conflictsDetected = true
 *      on the extraction record. Prompts the user to review fields carefully.
 *
 *   3. PARTIAL_EXTRACTION — shown when isPartial = true.
 *      Informs the user that some fields may be missing and manual entry is needed.
 *
 * These banners are dismissible. They are NOT blocking — the form can still
 * be submitted. The banner is a warning signal only.
 *
 * Used in Batch 7 by ContractCreate, rendered:
 *   - UNCONFIRMED_AT_SUBMIT: just before the submit button, shown dynamically
 *   - CONFLICTS_DETECTED + PARTIAL_EXTRACTION: below the OCR upload zone
 */

import React, { useState } from 'react';
import { AlertTriangle, X, Info } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

// ─── Banner types ─────────────────────────────────────────────────────────────

export type OcrCautionBannerVariant =
    | 'unconfirmed_at_submit'
    | 'conflicts_detected'
    | 'partial_extraction';

const BANNER_CONFIG: Record<OcrCautionBannerVariant, {
    bg:     string;
    border: string;
    text:   string;
    icon:   React.FC<{ size: number; style?: React.CSSProperties }>;
    title:  string;
    body:   string;
}> = {
    unconfirmed_at_submit: {
        bg:     '#fffbeb',
        border: '#fcd34d',
        text:   '#92400e',
        icon:   ({ size, style }) => <AlertTriangle size={size} style={style} />,
        title:  'Campos OCR não confirmados',
        body:   'Os valores extraídos pelo OCR nos campos destacados não foram confirmados e serão excluídos ao guardar. Confirme ou limpe esses campos antes de guardar para garantir que todos os dados são correctos.',
    },
    conflicts_detected: {
        bg:     '#fef2f2',
        border: '#fca5a5',
        text:   '#991b1b',
        icon:   ({ size, style }) => <AlertTriangle size={size} style={style} />,
        title:  'Conflitos detectados na extracção OCR',
        body:   'O OCR detectou valores potencialmente conflituantes no documento. Reveja cada campo cuidadosamente antes de guardar.',
    },
    partial_extraction: {
        bg:     '#eff6ff',
        border: '#bfdbfe',
        text:   '#1d4ed8',
        icon:   ({ size, style }) => <Info size={size} style={style} />,
        title:  'Extracção parcial',
        body:   'O OCR não conseguiu extrair todos os campos. Preencha manualmente os campos em falta.',
    },
};

interface ContractOcrCautionBannerProps {
    variant: OcrCautionBannerVariant;
    isVisible: boolean;
    /** Optional list of field labels shown for unconfirmed_at_submit variant */
    unconfirmedFieldLabels?: string[];
    /** Allow the parent to react when the banner is dismissed */
    onDismiss?: () => void;
}

const FIELD_LABELS: Record<string, string> = {
    EffectiveDateUtc:   'Data de Início',
    ExpirationDateUtc:  'Data de Validade',
    SignedAtUtc:        'Data de Assinatura',
    TotalContractValue: 'Valor Total',
    CurrencyId:         'Moeda',
};

export function ContractOcrCautionBanner({
    variant,
    isVisible,
    unconfirmedFieldLabels,
    onDismiss,
}: ContractOcrCautionBannerProps) {
    const [dismissed, setDismissed] = useState(false);

    const show = isVisible && !dismissed;

    const handleDismiss = () => {
        setDismissed(true);
        onDismiss?.();
    };

    // Reset dismissed state when visibility toggles back to true
    React.useEffect(() => {
        if (isVisible) setDismissed(false);
    }, [isVisible]);

    const cfg = BANNER_CONFIG[variant];
    const Icon = cfg.icon;

    return (
        <AnimatePresence>
            {show && (
                <motion.div
                    key={`ocr-caution-${variant}`}
                    initial={{ opacity: 0, y: -6 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -6 }}
                    transition={{ duration: 0.18 }}
                    role="alert"
                    style={{
                        display:         'flex',
                        gap:             '10px',
                        padding:         '12px 14px',
                        backgroundColor: cfg.bg,
                        border:          `1px solid ${cfg.border}`,
                        borderLeft:      `4px solid ${cfg.border}`,
                        borderRadius:    'var(--radius-md)',
                        color:           cfg.text,
                    }}
                >
                    <Icon size={17} style={{ flexShrink: 0, marginTop: '2px' }} />

                    <div style={{ flex: 1 }}>
                        <div style={{ fontSize: '0.8rem', fontWeight: 700, marginBottom: '3px' }}>
                            {cfg.title}
                        </div>
                        <div style={{ fontSize: '0.75rem', lineHeight: 1.5 }}>
                            {cfg.body}
                        </div>

                        {/* Field list for unconfirmed_at_submit */}
                        {variant === 'unconfirmed_at_submit' && unconfirmedFieldLabels && unconfirmedFieldLabels.length > 0 && (
                            <div style={{
                                display:    'flex',
                                flexWrap:   'wrap',
                                gap:        '5px',
                                marginTop:  '8px',
                            }}>
                                {unconfirmedFieldLabels.map(name => (
                                    <span key={name} style={{
                                        padding:         '2px 8px',
                                        fontSize:        '0.67rem',
                                        fontWeight:      700,
                                        backgroundColor: '#fef3c7',
                                        color:           '#92400e',
                                        border:          '1px solid #fcd34d',
                                        borderRadius:    '100px',
                                    }}>
                                        {FIELD_LABELS[name] ?? name}
                                    </span>
                                ))}
                            </div>
                        )}
                    </div>

                    <button
                        type="button"
                        onClick={handleDismiss}
                        aria-label="Fechar aviso"
                        style={{
                            background:   'none',
                            border:       'none',
                            cursor:       'pointer',
                            color:        cfg.text,
                            opacity:      0.6,
                            padding:      '2px',
                            display:      'flex',
                            alignItems:   'flex-start',
                            flexShrink:   0,
                            transition:   'opacity 0.15s',
                        }}
                        onMouseOver={e => (e.currentTarget.style.opacity = '1')}
                        onMouseOut={e  => (e.currentTarget.style.opacity = '0.6')}
                    >
                        <X size={16} />
                    </button>
                </motion.div>
            )}
        </AnimatePresence>
    );
}
