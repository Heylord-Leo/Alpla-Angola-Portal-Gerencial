import React from 'react';
import { AlertCircle, AlertTriangle, CheckCircle2, FileText, Info } from 'lucide-react';
import {
    DocumentUsageContext,
    documentTypeHint,
    documentTypeLabel,
    documentTypeOptionsFor,
    isFiscalDocument,
    isSelectableDocumentType,
    normalizeDocumentType,
    obligationPreview
} from '../../lib/sourceDocumentType';

/** What document extraction proposed, with the evidence behind it. Never applied automatically. */
export interface OcrDocumentClassification {
    suggestedType?: string | null;
    confidence?: number | null;
    titleFound?: string | null;
    supportingEvidence?: string[];
    conflictingEvidence?: string[];
    fiscalMarkers?: string[];
    nonFiscalMarkers?: string[];
    indicatesFiscalDocument?: boolean;
}

export interface ClassificationConflictState {
    hasConflict: boolean;
    isHighRisk: boolean;
    acknowledged: boolean;
    justification: string;
}

interface Props {
    context: DocumentUsageContext;
    value: string;
    onChange: (value: string) => void;
    ocr?: OcrDocumentClassification | null;
    conflict?: ClassificationConflictState;
    onConflictChange?: (next: ClassificationConflictState) => void;
    readOnly?: boolean;
    required?: boolean;
    error?: string | null;
    labelStyle?: React.CSSProperties;
    inputStyle?: React.CSSProperties;
    labelClassName?: string;
    inputClassName?: string;
    'data-guide'?: string;
}

/** Minimum characters for the written reason when a high-risk conflict is overridden. */
export const CONFLICT_JUSTIFICATION_MIN_LENGTH = 20;

/**
 * Evaluates a user selection against the extraction's opinion.
 *
 * The rule that matters: choosing a NON-FISCAL type for a document the evidence reads as FISCAL is
 * always high-risk, whatever the numeric confidence. That is exactly the observed defect — an `FT`
 * invoice classified as "Fatura Proforma" — and it is the direction that understates fiscal
 * reality, so it never gets the benefit of the doubt.
 */
export function evaluateClassificationConflict(
    selected: string,
    ocr?: OcrDocumentClassification | null
): { hasConflict: boolean; isHighRisk: boolean } {
    const suggestion = normalizeDocumentType(ocr?.suggestedType);
    const choice = normalizeDocumentType(selected);

    if (!suggestion || !choice || suggestion === choice) {
        return { hasConflict: false, isHighRisk: false };
    }

    const confidence = ocr?.confidence ?? 0;
    const understatesFiscalReality = !!ocr?.indicatesFiscalDocument && !isFiscalDocument(choice);

    return { hasConflict: true, isHighRisk: understatesFiscalReality || confidence >= 0.7 };
}

/**
 * "Tipo de documento anexado" — the identity of the document the supplier issued.
 *
 * The field describes the artefact, not the workflow: the obligations it triggers are shown as a
 * consequence, never as the thing being chosen. There is no default and no auto-selection, so an
 * unclassified request is always someone not deciding — never the system deciding for them.
 */
export function SourceDocumentTypeField({
    context,
    value,
    onChange,
    ocr,
    conflict,
    onConflictChange,
    readOnly = false,
    required = false,
    error,
    labelStyle,
    inputStyle,
    labelClassName,
    inputClassName,
    'data-guide': dataGuide
}: Props) {
    const options = documentTypeOptionsFor(context);
    const selected = normalizeDocumentType(value);
    const hint = documentTypeHint(value);
    const obligations = obligationPreview(value);
    const suggestion = normalizeDocumentType(ocr?.suggestedType);
    const evaluation = evaluateClassificationConflict(value, ocr);
    const confidencePct = ocr?.confidence != null ? Math.round(ocr.confidence * 100) : null;

    const fiscalBadge = (type: string | null) => {
        if (!type) return null;
        const fiscal = isFiscalDocument(type);
        return (
            <span style={{
                fontSize: '0.68rem', fontWeight: 700, padding: '2px 8px', borderRadius: '999px',
                backgroundColor: fiscal ? '#dcfce7' : '#f1f5f9',
                color: fiscal ? '#15803d' : '#475569',
                border: `1px solid ${fiscal ? '#86efac' : '#cbd5e1'}`
            }}>
                {fiscal ? 'Documento fiscal' : 'Não fiscal'}
            </span>
        );
    };

    if (readOnly) {
        return (
            <div data-guide={dataGuide} style={labelStyle} className={labelClassName}>
                Tipo de documento anexado
                <div style={{
                    marginTop: '8px', padding: '10px 12px', borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)',
                    display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap'
                }}>
                    <FileText size={14} color="var(--color-text-muted)" />
                    <span style={{
                        fontSize: '0.875rem', fontWeight: 600,
                        color: selected ? 'var(--color-text-main)' : 'var(--color-text-muted)'
                    }}>
                        {documentTypeLabel(value)}
                    </span>
                    {fiscalBadge(selected)}
                </div>
                {hint && (
                    <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: '4px' }}>{hint}</div>
                )}
            </div>
        );
    }

    return (
        <label data-guide={dataGuide} style={labelStyle} className={labelClassName}>
            Tipo de documento anexado {required && <span style={{ color: 'red' }}>*</span>}

            <select
                name="sourceDocumentType"
                value={selected ?? ''}
                onChange={e => onChange(e.target.value)}
                style={inputStyle}
                className={inputClassName}
            >
                <option value="">-- Selecione --</option>
                {options.map(opt => (
                    <option key={opt.value} value={opt.value}>
                        {opt.label}
                        {opt.unusual ? ' (invulgar — revisão)' : ''}
                        {!opt.unusual && opt.requiresReview ? ' (revisão do Financeiro)' : ''}
                    </option>
                ))}
            </select>

            {/* OCR proposal — shown for confirmation, never written into the selection. */}
            {suggestion && !selected && (
                <div style={{
                    marginTop: '6px', padding: '8px 10px', borderRadius: 'var(--radius-sm)',
                    backgroundColor: '#eff6ff', border: '1px solid #93c5fd'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                        <Info size={14} color="#1d4ed8" />
                        <span style={{ fontSize: '0.75rem', color: '#1e40af', fontWeight: 600 }}>
                            O documento parece ser: {documentTypeLabel(suggestion)}
                            {confidencePct != null && ` (confiança ${confidencePct}%)`}. Confirme ou corrija.
                        </span>
                    </div>
                    {ocr?.titleFound && (
                        <div style={{ fontSize: '0.72rem', color: '#1e40af', marginTop: '4px' }}>
                            Título lido no documento: <strong>{ocr.titleFound}</strong>
                        </div>
                    )}
                </div>
            )}

            {/* Selection agrees with the evidence. */}
            {suggestion && selected && !evaluation.hasConflict && (
                <div style={{
                    marginTop: '6px', display: 'flex', alignItems: 'center', gap: '6px',
                    fontSize: '0.72rem', color: '#15803d', fontWeight: 600
                }}>
                    <CheckCircle2 size={13} />
                    Confirmado: coincide com a leitura do documento.
                </div>
            )}

            {/* Conflict. High risk demands an explicit reason before the form may proceed. */}
            {evaluation.hasConflict && (
                <div style={{
                    marginTop: '6px', padding: '10px 12px', borderRadius: 'var(--radius-sm)',
                    backgroundColor: evaluation.isHighRisk ? '#fef2f2' : '#fffbeb',
                    border: `1px solid ${evaluation.isHighRisk ? '#fca5a5' : '#fcd34d'}`
                }}>
                    <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                        <AlertTriangle size={15} color={evaluation.isHighRisk ? '#b91c1c' : '#b45309'} style={{ marginTop: 2 }} />
                        <div style={{ flex: 1 }}>
                            <div style={{
                                fontSize: '0.78rem', fontWeight: 700,
                                color: evaluation.isHighRisk ? '#991b1b' : '#92400e'
                            }}>
                                {evaluation.isHighRisk
                                    ? 'A sua seleção contradiz o documento'
                                    : 'A sua seleção difere da leitura do documento'}
                            </div>
                            <div style={{
                                fontSize: '0.74rem', marginTop: '3px',
                                color: evaluation.isHighRisk ? '#991b1b' : '#92400e'
                            }}>
                                Selecionou <strong>{documentTypeLabel(selected)}</strong>, mas o documento indica{' '}
                                <strong>{documentTypeLabel(suggestion)}</strong>
                                {confidencePct != null && ` (confiança ${confidencePct}%)`}.
                            </div>

                            {ocr?.titleFound && (
                                <div style={{ fontSize: '0.72rem', marginTop: '4px', color: '#7f1d1d' }}>
                                    Título lido: <strong>{ocr.titleFound}</strong>
                                </div>
                            )}
                            {!!ocr?.fiscalMarkers?.length && (
                                <div style={{ fontSize: '0.72rem', marginTop: '2px', color: '#7f1d1d' }}>
                                    Marcas fiscais encontradas: {ocr.fiscalMarkers.join('; ')}
                                </div>
                            )}
                            {!!ocr?.nonFiscalMarkers?.length && (
                                <div style={{ fontSize: '0.72rem', marginTop: '2px', color: '#7f1d1d' }}>
                                    Marcas de documento não fiscal: {ocr.nonFiscalMarkers.join('; ')}
                                </div>
                            )}
                            {!!ocr?.supportingEvidence?.length && (
                                <div style={{ fontSize: '0.72rem', marginTop: '2px', color: '#7f1d1d' }}>
                                    Evidência: {ocr.supportingEvidence.slice(0, 3).join('; ')}
                                </div>
                            )}

                            {onConflictChange && (
                                <label style={{
                                    display: 'flex', alignItems: 'center', gap: '6px', marginTop: '8px',
                                    fontSize: '0.74rem', fontWeight: 600, cursor: 'pointer',
                                    color: evaluation.isHighRisk ? '#991b1b' : '#92400e'
                                }}>
                                    <input
                                        type="checkbox"
                                        checked={conflict?.acknowledged ?? false}
                                        onChange={e => onConflictChange({
                                            hasConflict: true,
                                            isHighRisk: evaluation.isHighRisk,
                                            acknowledged: e.target.checked,
                                            justification: conflict?.justification ?? ''
                                        })}
                                    />
                                    Confirmo que a minha seleção está correta apesar da leitura do documento.
                                </label>
                            )}

                            {evaluation.isHighRisk && onConflictChange && (
                                <div style={{ marginTop: '6px' }}>
                                    <textarea
                                        value={conflict?.justification ?? ''}
                                        onChange={e => onConflictChange({
                                            hasConflict: true,
                                            isHighRisk: true,
                                            acknowledged: conflict?.acknowledged ?? false,
                                            justification: e.target.value
                                        })}
                                        placeholder={`Explique por que motivo a classificação está correta (mínimo ${CONFLICT_JUSTIFICATION_MIN_LENGTH} caracteres).`}
                                        rows={2}
                                        style={{
                                            width: '100%', padding: '8px', fontSize: '0.75rem',
                                            borderRadius: 'var(--radius-sm)', border: '1px solid #fca5a5',
                                            backgroundColor: 'var(--color-bg-surface)',
                                            color: 'var(--color-text-main)', fontFamily: 'var(--font-family-body)'
                                        }}
                                    />
                                    <div style={{ fontSize: '0.68rem', color: '#991b1b', marginTop: '2px' }}>
                                        {(conflict?.justification ?? '').trim().length}/{CONFLICT_JUSTIFICATION_MIN_LENGTH} caracteres — será registado para auditoria.
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
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
                <div style={{
                    color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: '4px',
                    display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap'
                }}>
                    {fiscalBadge(selected)}
                    <span>{hint}</span>
                </div>
            )}

            {/* What this choice commits the request to — shown so the consequence is visible now. */}
            {isSelectableDocumentType(value) && obligations.length > 0 && (
                <div style={{
                    marginTop: '6px', padding: '8px 10px', borderRadius: 'var(--radius-sm)',
                    backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)'
                }}>
                    <div style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', marginBottom: '3px' }}>
                        Com esta escolha, será ainda necessário:
                    </div>
                    <ul style={{ margin: 0, paddingLeft: '16px', fontSize: '0.72rem', color: 'var(--color-text-main)' }}>
                        {obligations.map(o => <li key={o}>{o}</li>)}
                    </ul>
                </div>
            )}
        </label>
    );
}
