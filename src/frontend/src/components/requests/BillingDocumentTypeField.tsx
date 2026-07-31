import React from 'react';
import { AlertCircle, Info } from 'lucide-react';
import {
    BILLING_DOCUMENT_TYPE_OPTIONS,
    billingDocumentTypeHint,
    billingDocumentTypeLabel,
    isBillingDocumentType
} from '../../lib/billingDocumentType';

interface Props {
    value: string;
    onChange: (value: string) => void;
    /** Read-only rendering — used once the request has been submitted and the choice is locked. */
    readOnly?: boolean;
    /** Marks the field visually as required. Mandatory only at submission, never on a draft. */
    required?: boolean;
    /** Validation message to show under the field. */
    error?: string | null;
    /**
     * A value proposed by document extraction. Rendered as a banner the user must act on — it is
     * never written into `value` automatically. No extraction path emits this today; the prop
     * exists so a future OCR suggestion can only ever arrive as a proposal.
     */
    suggestion?: string | null;
    onAcceptSuggestion?: (value: string) => void;
    /** RequestCreate styles inline; RequestEdit uses CSS-module class names. Both are supported. */
    labelStyle?: React.CSSProperties;
    inputStyle?: React.CSSProperties;
    labelClassName?: string;
    inputClassName?: string;
    'data-guide'?: string;
}

/**
 * "Tipo de Documento de Faturação" — the billing document that originated a PAYMENT request.
 *
 * The choice is not cosmetic: PROFORMA commits the request to producing a Final Invoice after
 * payment, FINAL_INVOICE does not. That consequence is spelled out inline rather than left for the
 * requester to infer.
 *
 * There is **no default and no auto-selection**. The placeholder stays selected until a person
 * chooses, so an unclassified request is always the result of someone not deciding — never of the
 * system deciding for them.
 */
export function BillingDocumentTypeField({
    value,
    onChange,
    readOnly = false,
    required = false,
    error,
    suggestion,
    onAcceptSuggestion,
    labelStyle,
    inputStyle,
    labelClassName,
    inputClassName,
    'data-guide': dataGuide
}: Props) {
    const hint = billingDocumentTypeHint(value);
    const showSuggestion = !readOnly && !!suggestion && !value && isBillingDocumentType(suggestion);

    if (readOnly) {
        return (
            <div data-guide={dataGuide} style={labelStyle} className={labelClassName}>
                Tipo de Documento de Faturação
                <div
                    style={{
                        marginTop: '8px',
                        padding: '10px 12px',
                        borderRadius: 'var(--radius-sm)',
                        border: '1px solid var(--color-border)',
                        backgroundColor: 'var(--color-bg-page)',
                        fontSize: '0.875rem',
                        fontWeight: 600,
                        color: value ? 'var(--color-text-main)' : 'var(--color-text-muted)'
                    }}
                >
                    {billingDocumentTypeLabel(value)}
                </div>
                {hint && (
                    <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: '4px' }}>
                        {hint}
                    </div>
                )}
            </div>
        );
    }

    return (
        <label data-guide={dataGuide} style={labelStyle} className={labelClassName}>
            Tipo de Documento de Faturação {required && <span style={{ color: 'red' }}>*</span>}
            <select
                name="billingDocumentType"
                value={value}
                onChange={e => onChange(e.target.value)}
                style={inputStyle}
                className={inputClassName}
            >
                <option value="">-- Selecione --</option>
                {BILLING_DOCUMENT_TYPE_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
            </select>

            {showSuggestion && (
                <div
                    style={{
                        marginTop: '6px', padding: '8px 10px', borderRadius: 'var(--radius-sm)',
                        backgroundColor: '#fffbeb', border: '1px solid #fcd34d',
                        display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap'
                    }}
                >
                    <Info size={14} color="#b45309" />
                    <span style={{ fontSize: '0.75rem', color: '#92400e', fontWeight: 600 }}>
                        O OCR sugere: {billingDocumentTypeLabel(suggestion)}. Confirme a seleção.
                    </span>
                    {onAcceptSuggestion && (
                        <button
                            type="button"
                            onClick={() => onAcceptSuggestion(suggestion!)}
                            style={{
                                fontSize: '0.72rem', fontWeight: 700, padding: '4px 10px',
                                borderRadius: 'var(--radius-sm)', border: '1px solid #b45309',
                                background: 'transparent', color: '#92400e', cursor: 'pointer'
                            }}
                        >
                            Confirmar
                        </button>
                    )}
                </div>
            )}

            {error && (
                <div style={{
                    color: '#EF4444', fontSize: '0.75rem', marginTop: '4px',
                    display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600
                }}>
                    <AlertCircle size={12} />
                    {error}
                </div>
            )}

            {!error && hint && (
                <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: '4px' }}>
                    {hint}
                </div>
            )}
        </label>
    );
}
