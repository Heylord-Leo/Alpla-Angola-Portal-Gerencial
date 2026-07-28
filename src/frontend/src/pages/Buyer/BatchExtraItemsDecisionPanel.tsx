import React from 'react';
import { Plus, XCircle, AlertCircle, Lock } from 'lucide-react';
import { formatCurrencyAO } from '../../lib/utils';
import { ExtraItemDecisionState } from '../../types';
import { validateReconciliationJustification } from '../../lib/reconciliationJustificationValidator';

export interface ExtraItemLine {
    quotationItemId: string;
    quotationId: string;
    description: string;
    // Persisted, backend-authoritative monetary fields (copied verbatim from SavedQuotationItemDto —
    // never recomputed in the frontend).
    quantity: number;
    unitPrice: number;
    /** null = the authoritative value was absent from the API payload (never silently coerced to 0);
     * a real stored 0 stays 0. See the lot-summary missing-field guard. */
    taxableBase: number | null;
    ivaAmount: number;
    ivaRatePercent: number;
    lineTotal: number;
    supplierName: string;
    reconciliationJustification: string | null;
}

interface BatchExtraItemsDecisionPanelProps {
    /** Genuine EXTRA_ITEM lines — every one already defaults to INCLUDE in the parent's decisions
     * map before this panel ever renders (review-first: the buyer confirms/overrides, never starts
     * from a forced blank choice). */
    lines: ExtraItemLine[];
    decisions: Record<string, ExtraItemDecisionState>;
    onChangeDecision: (quotationItemId: string, decision: ExtraItemDecisionState) => void;
    /** Set when a reversal (INCLUDE→EXCLUDE) attempt on this specific line was rejected by the
     * backend with EXTRA_ITEM_INCLUSION_LOCKED — shown inline under that one line only. */
    lockedItemId?: string | null;
    lockedReason?: string | null;
    onDismissLocked?: () => void;
    /** Server-side validation error (e.g. invalid EXCLUDE comment) mapped back to a specific
     * line — a best-effort mapping, not always resolvable (see parseExtraItemDecisionError). */
    fieldErrorItemId?: string | null;
    fieldErrorMessage?: string | null;
}

const baseBtnStyle: React.CSSProperties = {
    padding: '6px 12px', fontSize: '0.8125rem', fontWeight: 600, borderRadius: '6px',
    cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px', border: '1px solid transparent'
};
const unselectedBtnStyle: React.CSSProperties = { ...baseBtnStyle, border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-muted)' };

/**
 * Buyer's batch-composition REVIEW for genuine EXTRA_ITEM quotation lines — shared by batch
 * creation (PartialApprovalBatchModal) and batch rework (BatchReworkModal), so the UI can never
 * diverge between the two entry points, mirroring the shared backend service. Every EXTRA_ITEM
 * line here already has a resolved reconciliationJustification from reconciliation and already
 * defaults to INCLUDE — this panel is a confirmation surface, not a second reconciliation: the
 * buyer only interacts to override a default (remove from batch).
 */
export const BatchExtraItemsDecisionPanel: React.FC<BatchExtraItemsDecisionPanelProps> = ({
    lines,
    decisions,
    onChangeDecision,
    lockedItemId,
    lockedReason,
    onDismissLocked,
    fieldErrorItemId,
    fieldErrorMessage
}) => {
    if (lines.length === 0) return null;

    return (
        <div>
            <h3 style={{ margin: '0 0 4px 0', fontSize: '1rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                Itens Adicionais da Cotação ({lines.length})
            </h3>
            <p style={{ margin: '0 0 12px 0', fontSize: '0.8125rem', color: 'var(--color-text-muted)' }}>
                Estas linhas foram classificadas como itens adicionais durante a reconciliação da cotação e já estão incluídas neste lote por padrão. Remova alguma apenas se não quiser adquiri-la agora.
            </p>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {lines.map(line => {
                    const state: ExtraItemDecisionState = decisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
                    const isLocked = lockedItemId === line.quotationItemId;
                    const hasFieldError = fieldErrorItemId === line.quotationItemId;
                    const excludeValidation = state.decision === 'EXCLUDE' ? validateReconciliationJustification(state.comment) : null;
                    const showExcludeError = state.decision === 'EXCLUDE' && state.comment.trim().length > 0 && excludeValidation && !excludeValidation.isValid;
                    const isIncluded = state.decision !== 'EXCLUDE';

                    return (
                        <div key={line.quotationItemId} style={{
                            border: (isLocked || hasFieldError) ? '1px solid var(--color-status-red, #dc2626)' : '1px solid var(--color-border)',
                            borderRadius: '8px', padding: '16px', backgroundColor: 'var(--color-bg-surface)'
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px', marginBottom: '10px' }}>
                                <div>
                                    <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-text-main)', wordBreak: 'break-word' }}>
                                        {line.description}
                                    </div>
                                    <div style={{ fontSize: '0.8125rem', color: 'var(--color-text-muted)', marginTop: '4px' }}>
                                        Fornecedor: <strong>{line.supplierName}</strong>
                                    </div>
                                </div>
                                <div style={{ textAlign: 'right', flexShrink: 0, minWidth: '160px' }}>
                                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Qtd: <strong style={{ color: 'var(--color-text-main)' }}>{line.quantity}</strong></div>
                                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Preço unitário s/ IVA: {formatCurrencyAO(line.unitPrice)}</div>
                                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>IVA ({line.ivaRatePercent}%): {formatCurrencyAO(line.ivaAmount)}</div>
                                    <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)', marginTop: '2px' }}>Total da linha c/ IVA: {formatCurrencyAO(line.lineTotal)}</div>
                                </div>
                            </div>

                            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '1px dashed var(--color-border)', borderRadius: '4px', padding: '8px 10px', marginBottom: '10px' }}>
                                Classificado como item adicional durante a reconciliação{' '}
                                {line.reconciliationJustification && (
                                    <>· <strong>Motivo:</strong> {line.reconciliationJustification}</>
                                )}
                            </div>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: isIncluded ? 0 : '10px' }}>
                                <span style={{
                                    fontSize: '0.75rem', fontWeight: 700, padding: '4px 10px', borderRadius: '14px',
                                    backgroundColor: isIncluded ? 'rgba(5,150,105,0.12)' : 'rgba(107,114,128,0.12)',
                                    color: isIncluded ? '#059669' : '#6b7280'
                                }}>
                                    {isIncluded ? 'Incluído neste lote' : 'Removido deste lote'}
                                </span>
                                {isIncluded ? (
                                    <button
                                        type="button"
                                        onClick={() => onChangeDecision(line.quotationItemId, { decision: 'EXCLUDE', comment: state.comment })}
                                        style={unselectedBtnStyle}
                                    >
                                        <XCircle size={14} /> Remover deste lote
                                    </button>
                                ) : (
                                    <button
                                        type="button"
                                        onClick={() => onChangeDecision(line.quotationItemId, { decision: 'INCLUDE', comment: state.comment })}
                                        style={{ ...baseBtnStyle, backgroundColor: '#059669', color: '#fff' }}
                                    >
                                        <Plus size={14} /> Manter incluído
                                    </button>
                                )}
                            </div>

                            {state.decision === 'EXCLUDE' && (
                                <div>
                                    <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, color: 'var(--color-text-main)', marginBottom: '6px' }}>
                                        Motivo da remoção *
                                    </label>
                                    <textarea
                                        value={state.comment}
                                        onChange={(e) => onChangeDecision(line.quotationItemId, { decision: 'EXCLUDE', comment: e.target.value })}
                                        placeholder="Ex.: item não será adquirido neste momento, orçamento não contempla esta linha..."
                                        rows={2}
                                        style={{
                                            width: '100%', fontSize: '0.8125rem', padding: '8px 10px',
                                            border: (showExcludeError || hasFieldError) ? '1px solid var(--color-status-red, #dc2626)' : '1px solid var(--color-border)',
                                            borderRadius: '4px', fontFamily: 'inherit', resize: 'vertical', boxSizing: 'border-box'
                                        }}
                                    />
                                    {showExcludeError && (
                                        <span style={{ fontSize: '0.75rem', color: 'var(--color-status-red, #dc2626)', marginTop: '4px', display: 'block' }}>
                                            {excludeValidation!.error}
                                        </span>
                                    )}
                                    {!showExcludeError && hasFieldError && fieldErrorMessage && (
                                        <span style={{ fontSize: '0.75rem', color: 'var(--color-status-red, #dc2626)', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                            <AlertCircle size={12} /> {fieldErrorMessage}
                                        </span>
                                    )}
                                </div>
                            )}

                            {hasFieldError && state.decision !== 'EXCLUDE' && fieldErrorMessage && (
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-status-red, #dc2626)', marginTop: '8px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                    <AlertCircle size={12} /> {fieldErrorMessage}
                                </span>
                            )}

                            {isLocked && (
                                <div style={{ marginTop: '10px', backgroundColor: 'var(--color-status-red-surface, #fef2f2)', border: '1px solid var(--color-status-red, #dc2626)', borderRadius: '6px', padding: '10px 12px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-status-red, #dc2626)', fontWeight: 700, fontSize: '0.8125rem' }}>
                                        <Lock size={14} /> Não é possível remover este item
                                    </div>
                                    <p style={{ margin: 0, fontSize: '0.8125rem', color: 'var(--color-text-main)' }}>
                                        {lockedReason || 'Este item já possui vínculos que impedem a remoção (alocação financeira, recebimento ou outro lote).'}
                                    </p>
                                    <div>
                                        <button
                                            type="button"
                                            onClick={() => {
                                                onChangeDecision(line.quotationItemId, { decision: 'INCLUDE', comment: state.comment });
                                                onDismissLocked?.();
                                            }}
                                            style={{ ...baseBtnStyle, backgroundColor: '#fff', border: '1px solid var(--color-status-red, #dc2626)', color: 'var(--color-status-red, #dc2626)' }}
                                        >
                                            Manter incluído
                                        </button>
                                    </div>
                                </div>
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
};
