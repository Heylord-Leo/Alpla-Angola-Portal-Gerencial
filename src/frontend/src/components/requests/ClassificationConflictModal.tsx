import React, { useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { InfoModal } from '../ui/InfoModal';
import { documentTypeLabel, normalizeDocumentType } from '../../lib/sourceDocumentType';
import {
    CONFLICT_JUSTIFICATION_MIN_LENGTH,
    ClassificationConflictState,
    ConflictEvaluation,
    OcrDocumentClassification,
    canConfirmConflict,
    conflictBlockingReason
} from '../../lib/documentClassificationDecision';
import { ClassificationEvidence, FiscalBadge } from './OcrClassificationModal';

interface Props {
    isOpen: boolean;
    /** The type the user just picked. NOT yet the field's value — that is the whole point. */
    pendingType: string;
    /** The value the field will fall back to if this is cancelled. */
    previousType: string;
    ocr: OcrDocumentClassification;
    evaluation: ConflictEvaluation;
    /** Pre-fills when re-opening an already-confirmed conflict. */
    initial?: ClassificationConflictState;
    onConfirm: (state: ClassificationConflictState) => void;
    onCancel: () => void;
}

const secondaryButton: React.CSSProperties = {
    padding: '9px 16px', borderRadius: '8px', fontWeight: 600, fontSize: '0.8125rem',
    cursor: 'pointer', backgroundColor: 'var(--color-bg-surface)',
    border: '1px solid var(--color-border)', color: 'var(--color-text-main)'
};

/**
 * The moment a user contradicts the document.
 *
 * <p>It opens by itself, because a warning the user can ignore is a warning that gets ignored — and
 * the defect that started all of this was an `FT` invoice accepted as a "Fatura Proforma" with a red
 * box sitting unread beneath the field. More importantly, <b>the selection is not applied until this
 * is confirmed.</b> Cancelling restores the previous value, so the only way to end up with a
 * contradicting classification is to have deliberately said so.</p>
 *
 * <p>Escape and the close button are cancel, not dismiss. There is no path through this dialog that
 * silently keeps the new value.</p>
 */
export function ClassificationConflictModal({
    isOpen,
    pendingType,
    previousType,
    ocr,
    evaluation,
    initial,
    onConfirm,
    onCancel
}: Props) {
    const [acknowledged, setAcknowledged] = useState(initial?.acknowledged ?? false);
    const [justification, setJustification] = useState(initial?.justification ?? '');
    const [showValidation, setShowValidation] = useState(false);

    // Each new conflict is a fresh decision — never inherit the answer given for a different one.
    useEffect(() => {
        if (!isOpen) return;
        setAcknowledged(initial?.acknowledged ?? false);
        setJustification(initial?.justification ?? '');
        setShowValidation(false);
    }, [isOpen, pendingType, initial?.acknowledged, initial?.justification]);

    const canConfirm = canConfirmConflict(evaluation, acknowledged, justification);
    const blockingReason = conflictBlockingReason(evaluation, acknowledged, justification);
    const written = justification.trim().length;
    const suggestion = normalizeDocumentType(ocr.suggestedType);

    const confirm = () => {
        if (!canConfirm) { setShowValidation(true); return; }
        onConfirm({
            hasConflict: true,
            isHighRisk: evaluation.isHighRisk,
            acknowledged: true,
            justification: justification.trim()
        });
    };

    return (
        <InfoModal
            isOpen={isOpen}
            onClose={onCancel}
            title="A classificação selecionada contradiz o documento"
            icon={<AlertTriangle size={18} />}
            tone="danger"
            maxWidth={640}
            closeOnBackdrop={false}
            footer={
                <>
                    <button type="button" onClick={onCancel} style={secondaryButton}>
                        Cancelar alteração
                    </button>
                    <button
                        type="button"
                        onClick={confirm}
                        aria-disabled={!canConfirm}
                        style={{
                            padding: '9px 16px', borderRadius: '8px', fontWeight: 700,
                            fontSize: '0.8125rem', border: 'none',
                            cursor: canConfirm ? 'pointer' : 'not-allowed',
                            backgroundColor: canConfirm ? '#b91c1c' : '#cbd5e1',
                            color: canConfirm ? '#ffffff' : '#64748b'
                        }}
                    >
                        Confirmar classificação e registar justificativa
                    </button>
                </>
            }
        >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                {/* The comparison, side by side, so the disagreement is the first thing read. */}
                <div style={{
                    display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '10px'
                }}>
                    <div style={{
                        padding: '10px 12px', borderRadius: '8px',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)'
                    }}>
                        <div style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                            Identificado pelo OCR
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', marginTop: '4px' }}>
                            <strong style={{ fontSize: '0.9rem', color: 'var(--color-text-main)' }}>
                                {documentTypeLabel(suggestion)}
                            </strong>
                            <FiscalBadge type={suggestion} />
                        </div>
                    </div>

                    <div style={{
                        padding: '10px 12px', borderRadius: '8px',
                        border: '1px solid #fca5a5', backgroundColor: 'rgba(185, 28, 28, 0.06)'
                    }}>
                        <div style={{ fontSize: '0.7rem', fontWeight: 700, color: '#b91c1c', textTransform: 'uppercase' }}>
                            Selecionado pelo utilizador
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', marginTop: '4px' }}>
                            <strong style={{ fontSize: '0.9rem', color: 'var(--color-text-main)' }}>
                                {documentTypeLabel(pendingType)}
                            </strong>
                            <FiscalBadge type={pendingType} />
                        </div>
                    </div>
                </div>

                <ClassificationEvidence ocr={ocr} />

                <p style={{
                    margin: 0, padding: '10px 12px', fontSize: '0.78rem', lineHeight: 1.55,
                    borderRadius: '8px', border: '1px solid var(--color-border)',
                    backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)'
                }}>
                    A classificação não é uma etiqueta: <strong>determina as obrigações que o Portal
                    irá exigir</strong> — factura da operação, comprovativo de pagamento, regularização
                    de adiantamento ou revisão do Financeiro. Classificar o documento de forma
                    diferente da leitura altera essas obrigações, pelo que a decisão fica registada
                    para auditoria com o seu nome, a data e o motivo indicado.
                </p>

                <label style={{
                    display: 'flex', alignItems: 'flex-start', gap: '8px',
                    fontSize: '0.8rem', fontWeight: 600, cursor: 'pointer', color: 'var(--color-text-main)'
                }}>
                    <input
                        type="checkbox"
                        checked={acknowledged}
                        onChange={e => setAcknowledged(e.target.checked)}
                        style={{ marginTop: '2px' }}
                    />
                    Confirmo que a classificação que selecionei está correta, apesar da leitura do documento.
                </label>

                <div>
                    <label
                        htmlFor="classification-justification"
                        style={{
                            display: 'block', fontSize: '0.75rem', fontWeight: 700,
                            color: 'var(--color-text-main)', marginBottom: '4px'
                        }}
                    >
                        Justificativa {evaluation.isHighRisk && <span style={{ color: '#b91c1c' }}>*</span>}
                    </label>
                    <textarea
                        id="classification-justification"
                        value={justification}
                        onChange={e => setJustification(e.target.value)}
                        rows={3}
                        placeholder={`Explique por que motivo esta classificação está correta (mínimo ${CONFLICT_JUSTIFICATION_MIN_LENGTH} caracteres).`}
                        style={{
                            width: '100%', boxSizing: 'border-box', padding: '8px 10px',
                            fontSize: '0.8rem', borderRadius: '8px',
                            border: `1px solid ${showValidation && blockingReason ? '#fca5a5' : 'var(--color-border)'}`,
                            backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                            fontFamily: 'var(--font-family-body)', resize: 'vertical'
                        }}
                    />
                    <div style={{
                        display: 'flex', justifyContent: 'space-between', gap: '10px',
                        marginTop: '3px', fontSize: '0.7rem'
                    }}>
                        <span style={{ color: 'var(--color-text-muted)' }}>
                            Será registada para auditoria.
                        </span>
                        <span style={{
                            fontWeight: 700,
                            color: written >= CONFLICT_JUSTIFICATION_MIN_LENGTH ? '#15803d' : 'var(--color-text-muted)'
                        }}>
                            {written}/{CONFLICT_JUSTIFICATION_MIN_LENGTH}
                        </span>
                    </div>
                </div>

                {showValidation && blockingReason && (
                    <div role="alert" style={{
                        fontSize: '0.78rem', fontWeight: 600, color: '#b91c1c',
                        display: 'flex', alignItems: 'center', gap: '6px'
                    }}>
                        <AlertTriangle size={14} />
                        {blockingReason}
                    </div>
                )}

                {previousType && (
                    <p style={{ margin: 0, fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                        Se cancelar, o campo volta a <strong>{documentTypeLabel(previousType)}</strong>.
                    </p>
                )}
            </div>
        </InfoModal>
    );
}
