import React, { useState } from 'react';
import { AlertCircle, FileText } from 'lucide-react';
import { DocumentTypeInfoTrigger } from './DocumentTypeInfoModal';
import { OcrClassificationModalBody } from './OcrClassificationModal';
import { ClassificationConflictModal } from './ClassificationConflictModal';
import { FieldMessageIcon } from '../ui/FieldMessageIcon';
import {
    DocumentUsageContext,
    documentTypeLabel,
    documentTypeOptionsFor,
    isFiscalDocument,
    isSelectableDocumentType,
    normalizeDocumentType
} from '../../lib/sourceDocumentType';
import {
    ClassificationConflictState,
    EMPTY_CONFLICT,
    OcrDocumentClassification,
    evaluateClassificationConflict,
    isUserResolvedIndeterminate
} from '../../lib/documentClassificationDecision';

// Re-exported so existing importers keep working while the logic lives in the shared lib.
export type { OcrDocumentClassification, ClassificationConflictState };
export { evaluateClassificationConflict };
export { CONFLICT_JUSTIFICATION_MIN_LENGTH } from '../../lib/documentClassificationDecision';

interface Props {
    context: DocumentUsageContext;
    value: string;
    /** Called only with a COMMITTED value — a conflicting choice arrives here after confirmation. */
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

/**
 * "Tipo de documento anexado" — the identity of the document the supplier issued.
 *
 * <p>The field describes the artefact, not the workflow. There is no default and no auto-selection,
 * so an unclassified request is always someone not deciding — never the system deciding for them.</p>
 *
 * <p><b>Nothing contextual is rendered inline.</b> The label carries at most three icons — what the
 * field means, what the document was read as, and whether the current choice contradicts that — and
 * each opens a modal. The field's height is therefore constant, whatever the Portal has to say.</p>
 *
 * <p><b>A conflicting selection is pending, not applied.</b> Picking a type that contradicts the
 * reading opens the conflict modal and leaves the field on its previous value until the user
 * confirms. Cancelling restores it. This is what makes "the dropdown cannot silently change the
 * classification after OCR has formed a suggestion" true rather than merely intended.</p>
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
    const suggestion = normalizeDocumentType(ocr?.suggestedType);
    const evaluation = evaluateClassificationConflict(value, ocr);
    // The user named a type the reading could not determine (OTHER / UNCLASSIFIED / nothing).
    const userResolvedIndeterminate = isUserResolvedIndeterminate(value, ocr);
    const confidencePct = ocr?.confidence != null ? Math.round(ocr.confidence * 100) : null;

    /** The choice awaiting confirmation. While set, the select shows it but `value` is unchanged. */
    const [pendingType, setPendingType] = useState<string | null>(null);

    const pendingEvaluation = evaluateClassificationConflict(pendingType, ocr);

    const handleSelect = (next: string) => {
        const conflictOnNext = evaluateClassificationConflict(next, ocr);

        if (!conflictOnNext.hasConflict) {
            setPendingType(null);
            // Leaving a conflict behind clears the answer given for it — a confirmation belongs to
            // one specific contradiction, never to whatever is chosen afterwards.
            if (conflict?.hasConflict) onConflictChange?.(EMPTY_CONFLICT);
            onChange(next);
            return;
        }

        setPendingType(next);
    };

    const confirmPending = (state: ClassificationConflictState) => {
        const confirmed = pendingType;
        setPendingType(null);
        if (confirmed === null) return;
        onConflictChange?.(state);
        onChange(confirmed);
    };

    /** Escape, "Cancelar alteração" and the close button all land here. */
    const cancelPending = () => setPendingType(null);

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

    /**
     * The label's icon strip. At most three, in a fixed order — meaning, reading, disagreement —
     * so their position is predictable and the row never changes height.
     */
    const icons = (
        <>
            <DocumentTypeInfoTrigger context={context} />

            {suggestion && (
                <FieldMessageIcon
                    severity="info"
                    tooltip="Clique para consultar a classificação sugerida pelo OCR."
                    ariaLabel="Ver a classificação sugerida pelo OCR"
                    title={ocr?.isFallback
                        ? 'Sugestão de classificação (não verificada)'
                        : `O documento parece ser: ${documentTypeLabel(suggestion)}`}
                >
                    <OcrClassificationModalBody ocr={ocr!} />
                </FieldMessageIcon>
            )}

            {/* The reading found no specific type and the user supplied one. Neutral, not a
                warning: nothing was contradicted, and a red icon here would accuse the user of
                overriding a decision the system never made. */}
            {userResolvedIndeterminate && (
                <FieldMessageIcon
                    severity="success"
                    tooltip="A classificação foi definida por si; o OCR não identificou um tipo específico."
                    ariaLabel="Ver como esta classificação foi definida"
                    title="Classificação definida pelo utilizador"
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-main)' }}>
                            A leitura automática não identificou um tipo específico. A classificação
                            <strong> {documentTypeLabel(selected)}</strong> foi definida por si.
                        </p>
                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-muted)' }}>
                            Não é uma divergência e não exige justificativa — a escolha fica registada no
                            histórico do pedido, com a indicação de que não foi lida do documento.
                        </p>
                        {ocr && <OcrClassificationModalBody ocr={ocr} />}
                    </div>
                </FieldMessageIcon>
            )}

            {evaluation.hasConflict && (
                <FieldMessageIcon
                    severity={evaluation.isHighRisk ? 'error' : 'warning'}
                    tooltip={evaluation.isHighRisk
                        ? 'A classificação selecionada contradiz o documento. Clique para rever.'
                        : 'A classificação selecionada difere da leitura do documento. Clique para rever.'}
                    ariaLabel="Rever a divergência de classificação"
                    title={evaluation.isHighRisk
                        ? 'A classificação selecionada contradiz o documento'
                        : 'A classificação selecionada difere da leitura do documento'}
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-main)' }}>
                            Selecionou <strong>{documentTypeLabel(selected)}</strong>, mas o documento
                            indica <strong>{documentTypeLabel(suggestion)}</strong>
                            {confidencePct != null && ` (confiança ${confidencePct}%)`}.
                            {conflict?.acknowledged && ' Esta divergência já foi confirmada.'}
                        </p>
                        {ocr && <OcrClassificationModalBody ocr={ocr} />}
                        {conflict?.justification && (
                            <p style={{
                                margin: 0, padding: '10px 12px', fontSize: '0.78rem', lineHeight: 1.55,
                                borderRadius: '8px', border: '1px solid var(--color-border)',
                                backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)'
                            }}>
                                <strong>Justificativa registada:</strong> {conflict.justification}
                            </p>
                        )}
                    </div>
                </FieldMessageIcon>
            )}
        </>
    );

    if (readOnly) {
        return (
            <div data-guide={dataGuide} style={labelStyle} className={labelClassName}>
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                    Tipo de documento anexado
                    {icons}
                </span>
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
            </div>
        );
    }

    return (
        <label data-guide={dataGuide} style={labelStyle} className={labelClassName}>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                Tipo de documento anexado {required && <span style={{ color: 'red' }}>*</span>}
                {icons}
            </span>

            <select
                name="sourceDocumentType"
                // While a conflict is pending the select shows the attempted choice, so the dialog
                // is visibly about what was just picked — but `value` has not moved.
                value={pendingType ?? selected ?? ''}
                onChange={e => handleSelect(e.target.value)}
                style={inputStyle}
                className={inputClassName}
            >
                <option value="">-- Selecione --</option>
                {/* Labels name the document and nothing else. The review requirement is a derived
                    obligation (DocumentObligationResolver), enforced in validation and surfaced in
                    the Finance queue — encoding it in the option text made the list read like a
                    warning instead of a list of documents. */}
                {options.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
            </select>

            {/* The only inline message left: a validation error the user must fix right now. */}
            {error && (
                <div style={{
                    color: '#EF4444', fontSize: '0.75rem', marginTop: '4px',
                    display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600
                }}>
                    <AlertCircle size={12} />
                    {error}
                </div>
            )}

            {pendingType && ocr && (
                <ClassificationConflictModal
                    isOpen
                    pendingType={pendingType}
                    previousType={selected ?? ''}
                    ocr={ocr}
                    evaluation={pendingEvaluation}
                    initial={conflict}
                    onConfirm={confirmPending}
                    onCancel={cancelPending}
                />
            )}
        </label>
    );
}

export { isSelectableDocumentType };
