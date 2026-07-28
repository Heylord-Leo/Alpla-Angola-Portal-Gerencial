import React, { useState, useEffect, useMemo } from 'react';
import { X, AlertTriangle, CheckCircle, RefreshCw, Info } from 'lucide-react';
import { formatCurrencyAO } from '../../lib/utils';
import { api } from '../../lib/api';
import {
    RequestLineItemDto, SavedQuotationDto, ExtraItemDecisionState, ExtraItemDecisionPayload,
    ApprovalBatchSummary
} from '../../types';
import { computeRelevantExtraLines, parseExtraItemDecisionError } from './batchExtraItemsLogic';
import { BatchExtraItemsDecisionPanel } from './BatchExtraItemsDecisionPanel';
import { validateReconciliationJustification } from '../../lib/reconciliationJustificationValidator';

interface Candidate {
    quotationItemId: string;
    quotationId: string;
    supplierName: string;
    description: string;
    unitPrice: number;
    reconciliationStatus: string;
}

interface BatchLineItem {
    reqItem: RequestLineItemDto;
    candidates: Candidate[];
    selectedCandidateId: string | null;
}

// Batch statuses that "hold" a quotation item — mirrors batchEligibility.ts's
// ACTIVE_OR_APPROVED_BATCH_STATUSES, but the batch currently being reworked is deliberately
// excluded from this check (its own current winners must remain selectable candidates).
const ACTIVE_OR_APPROVED_BATCH_STATUSES = ['WAITING_AREA_APPROVAL', 'AREA_ADJUSTMENT', 'WAITING_FINAL_APPROVAL', 'FINAL_ADJUSTMENT', 'APPROVED'];

function isSelectableExcludingOwnBatch(quotationItemId: string, group: any, ownBatchId: string): boolean {
    const referencedElsewhere = (group?.approvalBatches || []).some((b: any) =>
        b.id !== ownBatchId &&
        ACTIVE_OR_APPROVED_BATCH_STATUSES.includes(b.status) &&
        (b.items || []).some((bi: any) => bi.selectedQuotationItemId === quotationItemId)
    );
    return !referencedElsewhere;
}

interface BatchReworkModalProps {
    isOpen: boolean;
    onClose: () => void;
    group: any;
    batch: ApprovalBatchSummary | null;
    /** Called after a fully successful update + resubmit — parent should refetch and close. */
    onSuccess: (message: string) => void;
}

// 'savedNotResubmitted': the update succeeded (corrections ARE persisted) but the resubmit call
// that immediately follows it failed — must never be described as a save failure.
type SubmitPhase = 'idle' | 'submitting' | 'savedNotResubmitted' | 'resubmitOnlyFailed';

/**
 * Buyer's rework screen for a batch the Area or Final Approver returned (AREA_ADJUSTMENT /
 * FINAL_ADJUSTMENT). Did not exist before Phase 3 — UpdateBatch/ResubmitBatch had zero frontend
 * callers (confirmed in Phase 1's investigation). Reuses the same winner-selection UI and the
 * shared BatchExtraItemsDecisionPanel as batch creation, pre-populated from the batch's current,
 * persisted state — never starts from a blank slate.
 */
export const BatchReworkModal: React.FC<BatchReworkModalProps> = ({ isOpen, onClose, group, batch, onSuccess }) => {
    const [lineItems, setLineItems] = useState<BatchLineItem[]>([]);
    const [extraItemDecisions, setExtraItemDecisions] = useState<Record<string, ExtraItemDecisionState>>({});
    const [phase, setPhase] = useState<SubmitPhase>('idle');
    const [submitError, setSubmitError] = useState<string | null>(null);
    const [pendingItemsError, setPendingItemsError] = useState<{ description: string; supplierName?: string | null }[] | null>(null);
    const [lockedItemId, setLockedItemId] = useState<string | null>(null);
    const [lockedReason, setLockedReason] = useState<string | null>(null);
    const [fieldErrorItemId, setFieldErrorItemId] = useState<string | null>(null);
    const [fieldErrorMessage, setFieldErrorMessage] = useState<string | null>(null);

    // Pre-population — structural, per the reviewed plan: a currently-included extra is
    // identified primarily via ApprovalBatchItem.SelectedQuotationItemId → the originating
    // QuotationItem, confirmed by RequestLineItem.CreationOrigin === BUYER_EXTRA_ITEM_INCLUDED.
    // excludedExtraItems / unresolvedLegacyLines come straight from the batch DTO.
    useEffect(() => {
        if (!isOpen || !group || !batch) return;

        const allLineItems: RequestLineItemDto[] = group.items || group.lineItems || group.requestLineItems || [];
        const quotations: SavedQuotationDto[] = group.quotations || [];
        const allQuotationItems = quotations.flatMap(q => (q.items || []).map(qi => ({ ...qi, quotationId: q.id, supplierName: q.supplierNameSnapshot })));

        const newLineItems: BatchLineItem[] = [];

        batch.items.forEach(batchItem => {
            const reqItem = allLineItems.find(li => li.id === batchItem.requestLineItemId);
            if (!reqItem) return; // defensive — should always be found

            const candidates: Candidate[] = [];
            quotations.forEach(quotation => {
                (quotation.items || []).forEach(qi => {
                    if (qi.mappedRequestLineItemId === reqItem.id &&
                        (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
                        isSelectableExcludingOwnBatch(qi.id, group, batch.id)) {
                        candidates.push({
                            quotationItemId: qi.id,
                            quotationId: quotation.id,
                            supplierName: quotation.supplierNameSnapshot || 'Fornecedor',
                            description: qi.description,
                            unitPrice: qi.unitPrice || 0,
                            reconciliationStatus: qi.reconciliationStatus
                        });
                    }
                });
            });

            // The batch's current winner must always be present as a candidate, even in the rare
            // case reconciliation data changed since the batch was created.
            if (!candidates.some(c => c.quotationItemId === batchItem.selectedQuotationItemId)) {
                const currentQi = allQuotationItems.find(qi => qi.id === batchItem.selectedQuotationItemId);
                if (currentQi) {
                    candidates.push({
                        quotationItemId: currentQi.id,
                        quotationId: currentQi.quotationId,
                        supplierName: currentQi.supplierName || 'Fornecedor',
                        description: currentQi.description,
                        unitPrice: currentQi.unitPrice || 0,
                        reconciliationStatus: currentQi.reconciliationStatus || 'MAPPED'
                    });
                }
            }

            newLineItems.push({ reqItem, candidates, selectedCandidateId: batchItem.selectedQuotationItemId });
        });

        setLineItems(newLineItems);

        // Structural pre-population of extra-item decisions.
        const decisions: Record<string, ExtraItemDecisionState> = {};
        batch.items.forEach(batchItem => {
            const qi = allQuotationItems.find(q => q.id === batchItem.selectedQuotationItemId);
            if (qi?.reconciliationStatus !== 'EXTRA_ITEM') return;
            const generatedLineItem = allLineItems.find(li => li.id === batchItem.requestLineItemId);
            if (generatedLineItem?.creationOrigin === 'BUYER_EXTRA_ITEM_INCLUDED') {
                decisions[qi.id] = { decision: 'INCLUDE', comment: '' };
            }
        });
        (batch.excludedExtraItems || []).forEach(item => {
            decisions[item.quotationItemId] = { decision: 'EXCLUDE', comment: item.comment || '' };
        });
        (batch.unresolvedLegacyLines || []).forEach(item => {
            decisions[item.quotationItemId] = { decision: null, comment: '' };
        });
        setExtraItemDecisions(decisions);

        setPhase('idle');
        setSubmitError(null);
        setPendingItemsError(null);
        setLockedItemId(null);
        setLockedReason(null);
        setFieldErrorItemId(null);
        setFieldErrorMessage(null);
    }, [isOpen, group, batch]);

    const relevantExtraLines = useMemo(() => {
        if (!group) return [];
        const selections = lineItems.map(item => {
            const candidate = item.candidates.find(c => c.quotationItemId === item.selectedCandidateId);
            return { quotationItemId: item.selectedCandidateId, quotationId: candidate?.quotationId || null };
        });
        return computeRelevantExtraLines(selections, group.quotations || []);
    }, [lineItems, group]);
    const relevantExtraLineIds = useMemo(() => relevantExtraLines.map(l => l.quotationItemId).sort().join('|'), [relevantExtraLines]);

    // Same reconciliation rule as batch creation: preserve decisions for lines still relevant
    // (including the structural pre-population from persisted state, set above), default any
    // newly-relevant EXTRA_ITEM line to INCLUDE (review-first — a valid default always exists),
    // drop stale decisions for lines that no longer contribute.
    useEffect(() => {
        setExtraItemDecisions(prev => {
            const next: Record<string, ExtraItemDecisionState> = {};
            relevantExtraLines.forEach(line => {
                next[line.quotationItemId] = prev[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
            });
            return next;
        });
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [relevantExtraLineIds]);

    const handleSelectCandidate = (reqItemId: string, candidateId: string) => {
        setLineItems(prev => prev.map(item => item.reqItem.id === reqItemId ? { ...item, selectedCandidateId: candidateId } : item));
    };

    const handleChangeExtraItemDecision = (quotationItemId: string, decision: ExtraItemDecisionState) => {
        if (lockedItemId === quotationItemId) { setLockedItemId(null); setLockedReason(null); }
        if (fieldErrorItemId === quotationItemId) { setFieldErrorItemId(null); setFieldErrorMessage(null); }
        setExtraItemDecisions(prev => ({ ...prev, [quotationItemId]: decision }));
    };

    const isExtraItemsValid = relevantExtraLines.every(line => {
        // A freshly-relevant line always defaults to INCLUDE (review-first). A pre-existing
        // UnresolvedLegacyLine is seeded with decision:null and stays blocking until the buyer
        // explicitly resolves it — this is the one case still requiring an explicit choice.
        const state = extraItemDecisions[line.quotationItemId];
        if (!state || state.decision === null) return false;
        if (state.decision === 'EXCLUDE') return validateReconciliationJustification(state.comment).isValid;
        return true;
    });
    const isValid = lineItems.every(item => item.selectedCandidateId !== null) && isExtraItemsValid;

    const buildPayload = () => {
        const items = lineItems.map(item => ({
            requestLineItemId: String(item.reqItem.id),
            selectedQuotationItemId: String(item.selectedCandidateId)
        }));
        const extraItemDecisionsPayload: Record<string, ExtraItemDecisionPayload> = {};
        relevantExtraLines.forEach(line => {
            const state = extraItemDecisions[line.quotationItemId];
            if (state?.decision) {
                extraItemDecisionsPayload[line.quotationItemId] = { decision: state.decision, comment: state.comment || undefined };
            }
        });
        return { items, extraItemDecisions: Object.keys(extraItemDecisionsPayload).length > 0 ? extraItemDecisionsPayload : undefined };
    };

    const applyErrorResponse = (err: any) => {
        const parsed = parseExtraItemDecisionError(err, relevantExtraLines);
        setPendingItemsError(null);
        setLockedItemId(null);
        setLockedReason(null);
        setFieldErrorItemId(null);
        setFieldErrorMessage(null);
        setSubmitError(null);

        if (parsed.kind === 'pending') {
            setPendingItemsError(parsed.pendingItems || []);
        } else if (parsed.kind === 'locked') {
            setLockedItemId(parsed.lockedQuotationItemId || null);
            setLockedReason(parsed.lockedReason || null);
        } else if (parsed.genericMatchedQuotationItemId) {
            setFieldErrorItemId(parsed.genericMatchedQuotationItemId);
            setFieldErrorMessage(parsed.message || null);
        } else {
            setSubmitError(parsed.message || 'Ocorreu um erro ao processar a solicitação.');
        }
    };

    const handleSaveAndResubmit = async () => {
        if (!group || !batch) return;
        setSubmitError(null);
        setPendingItemsError(null);
        setLockedItemId(null);
        setLockedReason(null);
        setFieldErrorItemId(null);
        setFieldErrorMessage(null);

        if (!lineItems.every(item => item.selectedCandidateId !== null)) {
            setSubmitError('Por favor, selecione uma cotação vencedora para cada item.');
            return;
        }
        if (!isExtraItemsValid) {
            setSubmitError('Decida (incluir ou não incluir) todos os itens adicionais pendentes da cotação antes de continuar.');
            return;
        }

        setPhase('submitting');
        const { items, extraItemDecisions: payload } = buildPayload();

        try {
            await api.requests.updateApprovalBatch(group.requestId, batch.id, items, undefined, payload);
        } catch (err) {
            applyErrorResponse(err);
            setPhase('idle');
            return;
        }

        try {
            await api.requests.resubmitApprovalBatch(group.requestId, batch.id);
            setPhase('idle');
            onSuccess('Correções salvas e lote reenviado para aprovação com sucesso.');
        } catch (err: any) {
            // Corrections ARE persisted at this point — never claim total failure, and never let
            // the buyer accidentally resubmit the whole edit again from this state.
            setPhase('savedNotResubmitted');
            setSubmitError(err?.message || 'As correções foram salvas, mas o reenvio para aprovação falhou.');
        }
    };

    const handleResubmitOnly = async () => {
        if (!group || !batch) return;
        setSubmitError(null);
        setPhase('submitting');
        try {
            await api.requests.resubmitApprovalBatch(group.requestId, batch.id);
            setPhase('idle');
            onSuccess('Lote reenviado para aprovação com sucesso.');
        } catch (err: any) {
            setPhase('resubmitOnlyFailed');
            setSubmitError(err?.message || 'Falha ao reenviar o lote para aprovação.');
        }
    };

    if (!isOpen || !batch) return null;
    const isSubmitting = phase === 'submitting';
    const cameFromFinalAdjustment = batch.status === 'FINAL_ADJUSTMENT';

    return (
        <div
            style={{
                position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.7)', backdropFilter: 'blur(4px)', zIndex: 10000,
                display: 'flex', justifyContent: 'center', alignItems: 'flex-start', padding: '40px 24px', overflowY: 'auto'
            }}
            onClick={onClose}
        >
            <div
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#FFFFFF', borderRadius: '12px', width: '100%', maxWidth: '740px',
                    boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                    display: 'flex', flexDirection: 'column', maxHeight: 'calc(100vh - 80px)'
                }}
            >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 24px', borderBottom: '1px solid var(--color-border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <div style={{ backgroundColor: '#fef3c7', padding: '8px', borderRadius: '8px' }}>
                            <RefreshCw size={24} color="#d97706" />
                        </div>
                        <div>
                            <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                Corrigir Lote #{batch.batchNumber}
                            </h2>
                            <p style={{ margin: 0, fontSize: '0.85rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                Revise os vencedores e os itens adicionais antes de reenviar para aprovação
                            </p>
                        </div>
                    </div>
                    <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', color: 'var(--color-text-muted)' }}>
                        <X size={20} />
                    </button>
                </div>

                <div style={{ padding: '24px', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    {cameFromFinalAdjustment && (
                        <div style={{ display: 'flex', gap: '10px', alignItems: 'flex-start', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px', padding: '12px 16px' }}>
                            <Info size={18} color="#2563eb" style={{ flexShrink: 0, marginTop: '2px' }} />
                            <p style={{ margin: 0, fontSize: '0.8125rem', color: '#1e3a8a', lineHeight: 1.5 }}>
                                Este lote foi devolvido pela Aprovação Final. Como a composição do lote pode ser alterada aqui, após o reenvio ele retornará primeiro à Aprovação da Área.
                            </p>
                        </div>
                    )}

                    {batch.comment && (
                        <div style={{ backgroundColor: '#fffbeb', border: '1px solid #fcd34d', borderRadius: '8px', padding: '12px 16px', fontSize: '0.8125rem', color: '#92400e' }}>
                            <strong>Motivo do reajuste:</strong> {batch.comment}
                        </div>
                    )}

                    <div>
                        <h3 style={{ margin: '0 0 12px 0', fontSize: '1rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                            Itens do Lote ({lineItems.length})
                        </h3>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                            {lineItems.map(item => (
                                <div key={item.reqItem.id} style={{ border: '1px solid var(--color-border)', borderRadius: '8px', padding: '16px', backgroundColor: 'var(--color-bg-surface)' }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                                        <div>
                                            <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>
                                                Linha {item.reqItem.lineNumber} &mdash; Qtd: {item.reqItem.quantity} {item.reqItem.unit}
                                            </div>
                                            <div style={{ marginTop: '6px', fontSize: '0.85rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                                {item.reqItem.description}
                                            </div>
                                        </div>
                                        {item.selectedCandidateId ? <CheckCircle size={20} color="#059669" style={{ flexShrink: 0 }} /> : <AlertTriangle size={20} color="#d97706" style={{ flexShrink: 0 }} />}
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                        {item.candidates.map(candidate => (
                                            <label key={candidate.quotationItemId} style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', padding: '12px', border: item.selectedCandidateId === candidate.quotationItemId ? '1px solid #3b82f6' : '1px solid var(--color-border)', borderRadius: '6px', backgroundColor: item.selectedCandidateId === candidate.quotationItemId ? '#eff6ff' : 'transparent', cursor: 'pointer' }}>
                                                <input type="radio" name={`rework-candidate-${item.reqItem.id}`} checked={item.selectedCandidateId === candidate.quotationItemId} onChange={() => handleSelectCandidate(item.reqItem.id, candidate.quotationItemId)} style={{ cursor: 'pointer', marginTop: '4px' }} />
                                                <div style={{ flex: 1 }}>
                                                    <div style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text-main)', display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                                                        {candidate.description}
                                                        {candidate.reconciliationStatus === 'SUBSTITUTE' && <span style={{ padding: '2px 6px', backgroundColor: '#fef9c3', color: '#854d0e', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px' }}>Substituto</span>}
                                                    </div>
                                                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '4px' }}>Fornecedor: <strong>{candidate.supplierName}</strong></div>
                                                </div>
                                                <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>{formatCurrencyAO(candidate.unitPrice)}</div>
                                            </label>
                                        ))}
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>

                    <BatchExtraItemsDecisionPanel
                        lines={relevantExtraLines}
                        decisions={extraItemDecisions}
                        onChangeDecision={handleChangeExtraItemDecision}
                        lockedItemId={lockedItemId}
                        lockedReason={lockedReason}
                        onDismissLocked={() => { setLockedItemId(null); setLockedReason(null); }}
                        fieldErrorItemId={fieldErrorItemId}
                        fieldErrorMessage={fieldErrorMessage}
                    />
                </div>

                {pendingItemsError && (
                    <div style={{ padding: '14px 24px', backgroundColor: 'var(--color-status-red-surface, #fef2f2)', borderTop: '1px solid var(--color-status-red, #dc2626)', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-status-red, #dc2626)', fontWeight: 700, fontSize: '0.85rem' }}>
                            <AlertTriangle size={16} /> Itens adicionais pendentes de decisão
                        </div>
                        <ul style={{ margin: 0, paddingLeft: '20px', fontSize: '0.8125rem', color: '#991b1b' }}>
                            {pendingItemsError.map((p, i) => <li key={i}>{p.description}{p.supplierName ? ` — ${p.supplierName}` : ''}</li>)}
                        </ul>
                    </div>
                )}

                {phase === 'savedNotResubmitted' && (
                    <div style={{ padding: '14px 24px', backgroundColor: '#fffbeb', borderTop: '1px solid #fcd34d', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#92400e', fontWeight: 700, fontSize: '0.85rem' }}>
                            <CheckCircle size={16} /> Correções salvas — lote ainda não reenviado
                        </div>
                        <p style={{ margin: 0, fontSize: '0.8125rem', color: '#92400e' }}>
                            Suas alterações foram salvas com sucesso, mas o reenvio para aprovação falhou: {submitError}
                        </p>
                        <div>
                            <button onClick={handleResubmitOnly} disabled={isSubmitting} style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: '#d97706', color: '#fff', fontWeight: 600, cursor: isSubmitting ? 'not-allowed' : 'pointer' }}>
                                {isSubmitting ? 'Reenviando...' : 'Tentar Reenviar Novamente'}
                            </button>
                        </div>
                    </div>
                )}

                {phase === 'resubmitOnlyFailed' && (
                    <div style={{ padding: '14px 24px', backgroundColor: 'var(--color-status-red-surface, #fef2f2)', borderTop: '1px solid var(--color-status-red, #dc2626)', color: '#991b1b', fontSize: '0.8125rem', fontWeight: 600 }}>
                        {submitError}
                    </div>
                )}

                {submitError && phase !== 'savedNotResubmitted' && phase !== 'resubmitOnlyFailed' && (
                    <div style={{ padding: '12px 24px', backgroundColor: '#fef2f2', borderTop: '1px solid #fecaca', color: '#dc2626', fontSize: '0.85rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <AlertTriangle size={16} /> {submitError}
                    </div>
                )}

                <div style={{ padding: '16px 24px', borderTop: '1px solid var(--color-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', backgroundColor: 'var(--color-bg-surface)', borderBottomLeftRadius: '12px', borderBottomRightRadius: '12px' }}>
                    <button onClick={onClose} style={{ padding: '10px 20px', backgroundColor: 'white', border: '1px solid var(--color-border)', borderRadius: '8px', fontWeight: 600, color: 'var(--color-text-muted)', cursor: 'pointer' }}>
                        Fechar
                    </button>
                    {phase === 'savedNotResubmitted' ? null : (
                        <div style={{ display: 'flex', gap: '10px' }}>
                            <button
                                onClick={handleResubmitOnly}
                                disabled={isSubmitting}
                                style={{ padding: '10px 16px', backgroundColor: 'white', border: '1px solid var(--color-border)', borderRadius: '8px', fontWeight: 600, color: 'var(--color-text-main)', cursor: isSubmitting ? 'not-allowed' : 'pointer' }}
                            >
                                Reenviar sem alterações
                            </button>
                            <button
                                onClick={handleSaveAndResubmit}
                                disabled={!isValid || isSubmitting}
                                style={{ padding: '10px 20px', backgroundColor: isValid && !isSubmitting ? 'var(--color-primary)' : 'var(--color-border)', border: 'none', borderRadius: '8px', fontWeight: 600, color: 'white', cursor: isValid && !isSubmitting ? 'pointer' : 'not-allowed' }}
                            >
                                {isSubmitting ? 'Enviando...' : 'Salvar Correções e Reenviar'}
                            </button>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};
