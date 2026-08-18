import React, { useState, useEffect, useMemo } from 'react';
import { X, AlertTriangle, CheckCircle, RefreshCw, Info } from 'lucide-react';
import { formatCurrencyAO } from '../../lib/utils';
import { api } from '../../lib/api';
import {
    RequestLineItemDto, SavedQuotationDto, ExtraItemDecisionState, ExtraItemDecisionPayload,
    ApprovalBatchSummary, ApprovalBatchItemCandidate, BatchItemInput
} from '../../types';
import { computeRelevantExtraLines, parseExtraItemDecisionError } from './batchExtraItemsLogic';
import { BatchExtraItemsDecisionPanel } from './BatchExtraItemsDecisionPanel';
import { validateReconciliationJustification } from '../../lib/reconciliationJustificationValidator';

/** One selectable option row in the rework screen. `frozen` = values come from the batch's
 * persisted candidate SNAPSHOT (never refreshed from the live quotation); otherwise the option is
 * not yet in the batch and shows current quotation values (the backend snapshots it on save). */
interface ReworkOption {
    quotationItemId: string;
    quotationId: string;
    supplierName: string;
    description: string;
    lineTotal: number;
    currency: string;
    reconciliationStatus: string;
    frozen: boolean;
}

interface ReworkItem {
    reqItem: RequestLineItemDto;
    options: ReworkOption[];
    kept: boolean;
    checkedIds: string[];
    buyerNotes: Record<string, string>;
    /** Pre-candidate-model item (no candidate rows, buyer-selected winner). Saving converts it. */
    isLegacy: boolean;
}

// Batch statuses that "hold" a quotation item — mirrors batchEligibility.ts's
// ACTIVE_OR_APPROVED_BATCH_STATUSES, but the batch currently being reworked is deliberately
// excluded from this check (its own current candidates must remain selectable).
const ACTIVE_OR_APPROVED_BATCH_STATUSES = ['WAITING_AREA_APPROVAL', 'AREA_ADJUSTMENT', 'WAITING_FINAL_APPROVAL', 'FINAL_ADJUSTMENT', 'APPROVED'];

function isSelectableExcludingOwnBatch(quotationItemId: string, group: any, ownBatchId: string): boolean {
    const referencedElsewhere = (group?.approvalBatches || []).some((b: any) =>
        b.id !== ownBatchId &&
        ACTIVE_OR_APPROVED_BATCH_STATUSES.includes(b.status) &&
        (b.items || []).some((bi: any) =>
            bi.selectedQuotationItemId === quotationItemId ||
            (bi.candidates || []).some((c: any) => c.quotationItemId === quotationItemId))
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
 * FINAL_ADJUSTMENT). Candidate model: the buyer edits the OPTION set (add/remove candidates,
 * BuyerNotes, keep/drop items) — never a winner; a returned batch re-enters area approval with no
 * pre-decided winner. Persisted candidates display their FROZEN snapshot values; newly added
 * options are snapshotted server-side on save. Editing a legacy (pre-candidate) batch explicitly
 * converts it to the candidate model.
 */
export const BatchReworkModal: React.FC<BatchReworkModalProps> = ({ isOpen, onClose, group, batch, onSuccess }) => {
    const [reworkItems, setReworkItems] = useState<ReworkItem[]>([]);
    const [extraItemDecisions, setExtraItemDecisions] = useState<Record<string, ExtraItemDecisionState>>({});
    const [phase, setPhase] = useState<SubmitPhase>('idle');
    const [submitError, setSubmitError] = useState<string | null>(null);
    const [pendingItemsError, setPendingItemsError] = useState<{ description: string; supplierName?: string | null }[] | null>(null);
    const [lockedItemId, setLockedItemId] = useState<string | null>(null);
    const [lockedReason, setLockedReason] = useState<string | null>(null);
    const [fieldErrorItemId, setFieldErrorItemId] = useState<string | null>(null);
    const [fieldErrorMessage, setFieldErrorMessage] = useState<string | null>(null);
    const [showValidation, setShowValidation] = useState(false);

    const hasLegacyItems = reworkItems.some(i => i.isLegacy);

    useEffect(() => {
        if (!isOpen || !group || !batch) return;

        const allLineItems: RequestLineItemDto[] = group.items || group.lineItems || group.requestLineItems || [];
        const quotations: SavedQuotationDto[] = group.quotations || [];
        const allQuotationItems = quotations.flatMap(q => (q.items || []).map(qi => ({ ...qi, quotationId: q.id, supplierName: q.supplierNameSnapshot })));

        const newItems: ReworkItem[] = [];

        batch.items.forEach(batchItem => {
            const reqItem = allLineItems.find(li => li.id === batchItem.requestLineItemId);
            if (!reqItem) return; // defensive — should always be found

            // Buyer-included EXTRA_ITEM lines are governed by the extra-items panel below (their
            // single fixed option re-enters the payload automatically) — not candidate-editable.
            if (reqItem.creationOrigin === 'BUYER_EXTRA_ITEM_INCLUDED') return;

            const snapshotCandidates: ApprovalBatchItemCandidate[] = batchItem.candidates || [];
            const isLegacy = snapshotCandidates.length === 0 && !!batchItem.selectedQuotationItemId;

            const options: ReworkOption[] = [];

            // Persisted candidates first — FROZEN snapshot values, never live ones.
            snapshotCandidates.forEach(c => {
                options.push({
                    quotationItemId: c.quotationItemId,
                    quotationId: c.quotationId,
                    supplierName: c.supplierName,
                    description: c.description,
                    lineTotal: c.lineTotal,
                    currency: c.currency,
                    reconciliationStatus: c.reconciliationStatus || 'MAPPED',
                    frozen: true
                });
            });

            // Legacy item: its historical buyer-selected winner becomes the seed option (live
            // lookup — no snapshot exists; NEVER fabricate additional candidates for it).
            if (isLegacy) {
                const currentQi = allQuotationItems.find(qi => qi.id === batchItem.selectedQuotationItemId);
                if (currentQi) {
                    options.push({
                        quotationItemId: currentQi.id,
                        quotationId: currentQi.quotationId,
                        supplierName: currentQi.supplierName || 'Fornecedor',
                        description: currentQi.description,
                        lineTotal: currentQi.lineTotal || 0,
                        currency: currentQi.currencyCode || 'AOA',
                        reconciliationStatus: currentQi.reconciliationStatus || 'MAPPED',
                        frozen: false
                    });
                }
            }

            // Other eligible quotation lines mapped to this item — addable options (snapshotted
            // by the backend if the buyer checks them and saves).
            quotations.forEach(quotation => {
                (quotation.items || []).forEach(qi => {
                    if (qi.mappedRequestLineItemId === reqItem.id &&
                        (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
                        isSelectableExcludingOwnBatch(qi.id, group, batch.id) &&
                        !options.some(o => o.quotationItemId === qi.id)) {
                        options.push({
                            quotationItemId: qi.id,
                            quotationId: quotation.id,
                            supplierName: quotation.supplierNameSnapshot || 'Fornecedor',
                            description: qi.description,
                            lineTotal: qi.lineTotal || 0,
                            currency: qi.currencyCode || quotation.currency || 'AOA',
                            reconciliationStatus: qi.reconciliationStatus,
                            frozen: false
                        });
                    }
                });
            });

            const buyerNotes: Record<string, string> = {};
            snapshotCandidates.forEach(c => { if (c.buyerNote) buyerNotes[c.quotationItemId] = c.buyerNote; });

            newItems.push({
                reqItem,
                options,
                kept: true,
                checkedIds: isLegacy
                    ? (batchItem.selectedQuotationItemId ? [batchItem.selectedQuotationItemId] : [])
                    : snapshotCandidates.map(c => c.quotationItemId),
                buyerNotes,
                isLegacy
            });
        });

        setReworkItems(newItems);

        // Structural pre-population of extra-item decisions: an included extra is a batch item
        // whose generated line has CreationOrigin BUYER_EXTRA_ITEM_INCLUDED; its quotation line is
        // the single candidate (candidate model) or the legacy winner pointer.
        const decisions: Record<string, ExtraItemDecisionState> = {};
        batch.items.forEach(batchItem => {
            const generatedLineItem = allLineItems.find(li => li.id === batchItem.requestLineItemId);
            if (generatedLineItem?.creationOrigin !== 'BUYER_EXTRA_ITEM_INCLUDED') return;
            const extraQuotationItemId = batchItem.candidates?.[0]?.quotationItemId ?? batchItem.selectedQuotationItemId;
            if (extraQuotationItemId) {
                decisions[extraQuotationItemId] = { decision: 'INCLUDE', comment: '' };
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
        setShowValidation(false);
    }, [isOpen, group, batch]);

    // Contributing quotations = every quotation carrying at least one CHECKED option of a KEPT item.
    const relevantExtraLines = useMemo(() => {
        if (!group) return [];
        const selections = reworkItems
            .filter(item => item.kept)
            .flatMap(item => item.options
                .filter(o => item.checkedIds.includes(o.quotationItemId))
                .map(o => ({ quotationItemId: o.quotationItemId, quotationId: o.quotationId })));
        return computeRelevantExtraLines(selections, group.quotations || []);
    }, [reworkItems, group]);
    const relevantExtraLineIds = useMemo(() => relevantExtraLines.map(l => l.quotationItemId).sort().join('|'), [relevantExtraLines]);

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

    const handleToggleOption = (reqItemId: string, quotationItemId: string) => {
        setReworkItems(prev => prev.map(item => {
            if (item.reqItem.id !== reqItemId) return item;
            const checked = item.checkedIds.includes(quotationItemId);
            return {
                ...item,
                checkedIds: checked
                    ? item.checkedIds.filter(id => id !== quotationItemId)
                    : [...item.checkedIds, quotationItemId]
            };
        }));
    };

    const handleToggleKept = (reqItemId: string) => {
        setReworkItems(prev => prev.map(item => item.reqItem.id === reqItemId ? { ...item, kept: !item.kept } : item));
    };

    const handleBuyerNoteChange = (reqItemId: string, quotationItemId: string, note: string) => {
        setReworkItems(prev => prev.map(item =>
            item.reqItem.id === reqItemId
                ? { ...item, buyerNotes: { ...item.buyerNotes, [quotationItemId]: note } }
                : item
        ));
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

    const keptItems = reworkItems.filter(i => i.kept);
    const invalidItems = keptItems.filter(i => i.checkedIds.length === 0);
    const isValid = keptItems.length > 0 && invalidItems.length === 0 && isExtraItemsValid;

    const buildPayload = () => {
        const items: BatchItemInput[] = keptItems.map(item => ({
            requestLineItemId: String(item.reqItem.id),
            candidates: item.checkedIds.map(quotationItemId => {
                const note = (item.buyerNotes[quotationItemId] || '').trim();
                return note
                    ? { quotationItemId: String(quotationItemId), buyerNote: note }
                    : { quotationItemId: String(quotationItemId) };
            })
        }));

        // Included EXTRA_ITEM lines re-enter the payload as their own single fixed candidate —
        // they are governed by the panel decisions, not by the candidate-edit UI above.
        const allLineItems: RequestLineItemDto[] = group?.items || group?.lineItems || group?.requestLineItems || [];
        (batch?.items || []).forEach(batchItem => {
            const generatedLineItem = allLineItems.find(li => li.id === batchItem.requestLineItemId);
            if (generatedLineItem?.creationOrigin !== 'BUYER_EXTRA_ITEM_INCLUDED') return;
            const extraQuotationItemId = batchItem.candidates?.[0]?.quotationItemId ?? batchItem.selectedQuotationItemId;
            if (!extraQuotationItemId) return;
            const state = extraItemDecisions[extraQuotationItemId];
            if (state?.decision === 'EXCLUDE') return; // reversal handled server-side by the decision
            items.push({
                requestLineItemId: String(batchItem.requestLineItemId),
                candidates: [{ quotationItemId: String(extraQuotationItemId) }]
            });
        });

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

        if (keptItems.length === 0) {
            setSubmitError('O lote deve manter pelo menos um item.');
            return;
        }
        if (invalidItems.length > 0) {
            setShowValidation(true);
            setSubmitError('Selecione pelo menos uma opção de cotação para cada item mantido no lote.');
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
                    backgroundColor: '#FFFFFF', borderRadius: '12px', width: '100%', maxWidth: '760px',
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
                                Revise as opções de cotação e os itens adicionais antes de reenviar. O vencedor será escolhido pelo Aprovador de Área.
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

                    {hasLegacyItems && (
                        <div style={{ display: 'flex', gap: '10px', alignItems: 'flex-start', backgroundColor: '#fffbeb', border: '1px solid #fcd34d', borderRadius: '8px', padding: '12px 16px' }}>
                            <Info size={18} color="#d97706" style={{ flexShrink: 0, marginTop: '2px' }} />
                            <p style={{ margin: 0, fontSize: '0.8125rem', color: '#92400e', lineHeight: 1.5 }}>
                                <strong>Lote do modelo anterior:</strong> o vencedor foi definido pelo comprador na criação. Ao salvar correções, o lote será convertido para o novo modelo de opções — a(s) cotação(ões) marcadas passam a ser opções e o <strong>Aprovador de Área</strong> fará a escolha do vencedor.
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
                            Itens do Lote ({reworkItems.length})
                        </h3>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                            {reworkItems.map(item => {
                                const itemInvalid = showValidation && item.kept && item.checkedIds.length === 0;
                                return (
                                    <div key={item.reqItem.id} style={{ border: itemInvalid ? '1px solid #dc2626' : '1px solid var(--color-border)', borderRadius: '8px', padding: '16px', backgroundColor: item.kept ? 'var(--color-bg-surface)' : '#f9fafb', opacity: item.kept ? 1 : 0.75 }}>
                                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                                            <div>
                                                <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>
                                                    Linha {item.reqItem.lineNumber} &mdash; Qtd: {item.reqItem.quantity} {item.reqItem.unit}
                                                </div>
                                                <div style={{ marginTop: '6px', fontSize: '0.85rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                                    {item.reqItem.description}
                                                </div>
                                            </div>
                                            <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', flexShrink: 0, fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                                                <input type="checkbox" checked={item.kept} onChange={() => handleToggleKept(item.reqItem.id)} style={{ cursor: 'pointer' }} />
                                                Manter no lote
                                            </label>
                                        </div>
                                        {item.kept ? (
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                <div style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                                                    Opções a Enviar para Aprovação
                                                </div>
                                                {itemInvalid && (
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '8px 12px', fontSize: '0.8rem', color: '#991b1b', fontWeight: 600 }}>
                                                        <AlertTriangle size={14} /> Selecione pelo menos uma opção de cotação para este item (ou remova-o do lote).
                                                    </div>
                                                )}
                                                {item.options.map(option => {
                                                    const checked = item.checkedIds.includes(option.quotationItemId);
                                                    return (
                                                        <div key={option.quotationItemId} style={{ border: checked ? '1px solid #3b82f6' : '1px solid var(--color-border)', borderRadius: '6px', backgroundColor: checked ? '#eff6ff' : 'transparent' }}>
                                                            <label style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', padding: '12px', cursor: 'pointer' }}>
                                                                <input type="checkbox" checked={checked} onChange={() => handleToggleOption(item.reqItem.id, option.quotationItemId)} style={{ cursor: 'pointer', marginTop: '4px' }} />
                                                                <div style={{ flex: 1 }}>
                                                                    <div style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text-main)', display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                                                                        {option.description}
                                                                        {option.reconciliationStatus === 'SUBSTITUTE' && <span style={{ padding: '2px 6px', backgroundColor: '#fef9c3', color: '#854d0e', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px' }}>Substituto</span>}
                                                                        {option.frozen && <span style={{ padding: '2px 6px', backgroundColor: '#f0f9ff', color: '#0369a1', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px', border: '1px solid #bae6fd' }}>Valores congelados no envio</span>}
                                                                    </div>
                                                                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '4px' }}>Fornecedor: <strong>{option.supplierName}</strong></div>
                                                                </div>
                                                                <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>{formatCurrencyAO(option.lineTotal)}</div>
                                                            </label>
                                                            {checked && (
                                                                <div style={{ padding: '0 12px 12px 40px' }}>
                                                                    <input
                                                                        type="text"
                                                                        value={item.buyerNotes[option.quotationItemId] || ''}
                                                                        onChange={(e) => handleBuyerNoteChange(item.reqItem.id, option.quotationItemId, e.target.value)}
                                                                        placeholder="Observação do Comprador (opcional)"
                                                                        maxLength={1000}
                                                                        style={{ width: '100%', padding: '6px 10px', fontSize: '0.8rem', border: '1px solid var(--color-border)', borderRadius: '4px', backgroundColor: '#fff' }}
                                                                    />
                                                                </div>
                                                            )}
                                                        </div>
                                                    );
                                                })}
                                            </div>
                                        ) : (
                                            <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                                Será removido do lote ao salvar — o item volta para a sua fila de cotação.
                                            </div>
                                        )}
                                    </div>
                                );
                            })}
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
