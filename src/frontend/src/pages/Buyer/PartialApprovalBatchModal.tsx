import React, { useState, useEffect, useMemo, useRef } from 'react';
import { X, AlertTriangle, Package, Send } from 'lucide-react';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestLineItemDto, SavedQuotationDto, ExtraItemDecisionState, ExtraItemDecisionPayload, BatchItemInput } from '../../types';
import { isQuotationItemSelectableForApproval } from './batchEligibility';
import { BatchExtraItemsDecisionPanel } from './BatchExtraItemsDecisionPanel';
import { computeRelevantExtraLines, parseExtraItemDecisionError } from './batchExtraItemsLogic';
import { validateReconciliationJustification } from '../../lib/reconciliationJustificationValidator';

/** One selectable quotation OPTION for a requested item. Values shown here are the current
 * quotation data for display only — the backend freezes its own authoritative snapshot at
 * submission; the POST payload carries candidate IDENTITY + optional BuyerNote, nothing else. */
interface CandidateOption {
    quotationItemId: string;
    quotationId: string;
    supplierName: string;
    description: string;
    quantity: number;
    unitLabel: string | null;
    unitPrice: number;
    discountAmount: number;
    ivaAmount: number;
    ivaRatePercent: number;
    grossSubtotal: number;
    lineTotal: number;
    currency: string;
    reconciliationStatus: string;
    reconciliationJustification: string | null;
    lineAdjustmentJustification: string | null;
    documentNumber: string | null;
    documentDate: string | null;
}

interface EligibleItem {
    reqItem: RequestLineItemDto;
    candidates: CandidateOption[];
    /** Partial-batch inclusion: excluded items stay in the buyer queue untouched. */
    included: boolean;
    /** Candidate model: the buyer CHECKS one or more options — never a winner. */
    checkedIds: string[];
    /** Optional per-candidate "Observação do Comprador". Never auto-populated. */
    buyerNotes: Record<string, string>;
}

interface PartialApprovalBatchModalProps {
    isOpen: boolean;
    onClose: () => void;
    group: any;
    /** Must throw (not swallow) on failure so the modal can render the error inline —
     * BuyerItemsList.handlePartialApprovalSubmit only handles the success side-effects. */
    onSubmit: (
        items: BatchItemInput[],
        extraItemDecisions?: Record<string, ExtraItemDecisionPayload>
    ) => Promise<void>;
}

const getRequestItemDescription = (item: any) =>
    item.description ||
    item.itemDescription ||
    item.productDescription ||
    item.name ||
    item.title ||
    item.requestedDescription ||
    'Descrição do item não disponível';

const formatDocumentDate = (iso: string | null) => {
    if (!iso) return null;
    const d = new Date(iso);
    return isNaN(d.getTime()) ? null : d.toLocaleDateString('pt-PT');
};

export const PartialApprovalBatchModal: React.FC<PartialApprovalBatchModalProps> = ({
    isOpen,
    onClose,
    group,
    onSubmit
}) => {
    const [eligibleItems, setEligibleItems] = useState<EligibleItem[]>([]);
    const [pendingItems, setPendingItems] = useState<RequestLineItemDto[]>([]);
    const [extraItemDecisions, setExtraItemDecisions] = useState<Record<string, ExtraItemDecisionState>>({});
    const [submitError, setSubmitError] = useState<string | null>(null);
    const [pendingItemsError, setPendingItemsError] = useState<{ description: string; supplierName?: string | null }[] | null>(null);
    const [lockedItemId, setLockedItemId] = useState<string | null>(null);
    const [lockedReason, setLockedReason] = useState<string | null>(null);
    const [fieldErrorItemId, setFieldErrorItemId] = useState<string | null>(null);
    const [fieldErrorMessage, setFieldErrorMessage] = useState<string | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);
    /** Inline validation is only shown after a submit attempt (not while composing). */
    const [showValidation, setShowValidation] = useState(false);
    const itemRefs = useRef<Record<string, HTMLDivElement | null>>({});

    useEffect(() => {
        if (!isOpen || !group) return;

        const reqItems: RequestLineItemDto[] = group.items || group.lineItems || group.requestLineItems || [];
        const quotations: SavedQuotationDto[] = group.quotations || [];

        const newEligibleItems: EligibleItem[] = [];
        const newPendingItems: RequestLineItemDto[] = [];

        reqItems.forEach(reqItem => {
            const reqItemId = reqItem.id || (reqItem as any).lineItemId || (reqItem as any).requestLineItemId;
            const normalizedReqItem = { ...reqItem, id: reqItemId };

            if (normalizedReqItem.lineItemStatusCode === 'DELETED' || normalizedReqItem.lineItemStatusCode === 'CANCELLED') return;
            if (normalizedReqItem.quotationLifecycleStatus && normalizedReqItem.quotationLifecycleStatus !== 'QUOTATION_PENDING') {
                return;
            }

            const candidates: CandidateOption[] = [];
            quotations.forEach(quotation => {
                const qItems = quotation.items || [];
                qItems.forEach(qi => {
                    if (qi.mappedRequestLineItemId === reqItemId &&
                        (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
                        isQuotationItemSelectableForApproval(qi.id, group)) {
                        candidates.push({
                            quotationItemId: qi.id,
                            quotationId: quotation.id,
                            supplierName: quotation.supplierNameSnapshot || 'Fornecedor',
                            description: qi.description,
                            quantity: qi.quantity || 0,
                            unitLabel: qi.unitCode || qi.unitName || null,
                            unitPrice: qi.unitPrice || 0,
                            discountAmount: qi.discountAmount || 0,
                            ivaAmount: qi.ivaAmount || 0,
                            ivaRatePercent: qi.ivaRatePercent || 0,
                            grossSubtotal: qi.grossSubtotal || 0,
                            lineTotal: qi.lineTotal || 0,
                            currency: qi.currencyCode || quotation.currency || 'AOA',
                            reconciliationStatus: qi.reconciliationStatus,
                            reconciliationJustification: qi.reconciliationJustification || null,
                            lineAdjustmentJustification: qi.lineAdjustmentJustification || null,
                            documentNumber: quotation.documentNumber || null,
                            documentDate: quotation.documentDate || null
                        });
                    }
                });
            });

            if (candidates.length > 0) {
                // Default: PRECHECK ALL eligible options (approved rule) — the workflow's purpose
                // is to hand alternatives to the Area Approver, and accidentally omitting a valid
                // quote is worse than sending both. Everything stays visibly editable.
                newEligibleItems.push({
                    reqItem: normalizedReqItem,
                    candidates,
                    included: true,
                    checkedIds: candidates.map(c => c.quotationItemId),
                    buyerNotes: {}
                });
            } else {
                newPendingItems.push(normalizedReqItem);
            }
        });

        setEligibleItems(newEligibleItems);
        setPendingItems(newPendingItems);
        setExtraItemDecisions({});
        setSubmitError(null);
        setPendingItemsError(null);
        setLockedItemId(null);
        setLockedReason(null);
        setFieldErrorItemId(null);
        setFieldErrorMessage(null);
        setShowValidation(false);

    }, [isOpen, group]);

    // Contributing quotations = every quotation carrying at least one CHECKED candidate of an
    // INCLUDED item (mirrors the backend's candidate-based contributing rule).
    const relevantExtraLines = useMemo(() => {
        const selections = eligibleItems
            .filter(item => item.included)
            .flatMap(item => item.candidates
                .filter(c => item.checkedIds.includes(c.quotationItemId))
                .map(c => ({ quotationItemId: c.quotationItemId, quotationId: c.quotationId })));
        return computeRelevantExtraLines(selections, group?.quotations || []);
    }, [eligibleItems, group]);
    const relevantExtraLineIds = useMemo(() => relevantExtraLines.map(l => l.quotationItemId).sort().join('|'), [relevantExtraLines]);

    // ── Pre-decision batch summary ──
    // There is NO winner and NO batch total before the Area decision. The only monetary facts a
    // buyer may see are the commercial RANGE of the checked options (min/max possible combination)
    // — and only when a single currency is involved. Never a "total considerado".
    const batchSummary = useMemo(() => {
        const included = eligibleItems.filter(i => i.included);
        const checkedCandidates = included.flatMap(item =>
            item.candidates.filter(c => item.checkedIds.includes(c.quotationItemId)));

        const supplierNames = new Set(checkedCandidates.map(c => c.supplierName));
        const currencies = new Set(checkedCandidates.map(c => c.currency));

        const includedExtras = relevantExtraLines.filter(line => {
            const state = extraItemDecisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
            return state.decision !== 'EXCLUDE';
        });

        let minCombination: number | null = null;
        let maxCombination: number | null = null;
        const everyItemHasChecked = included.length > 0 && included.every(i => i.checkedIds.length > 0);
        if (everyItemHasChecked && currencies.size === 1) {
            let min = 0, max = 0;
            included.forEach(item => {
                const totals = item.candidates
                    .filter(c => item.checkedIds.includes(c.quotationItemId))
                    .map(c => c.lineTotal);
                min += Math.min(...totals);
                max += Math.max(...totals);
            });
            // Included EXTRA_ITEM lines are fixed (single-option) — they raise both bounds.
            includedExtras.forEach(line => { min += line.lineTotal; max += line.lineTotal; });
            minCombination = min;
            maxCombination = max;
        }

        return {
            includedItemCount: included.length,
            optionCount: checkedCandidates.length,
            supplierCount: supplierNames.size,
            includedExtraCount: includedExtras.length,
            mixedCurrencies: currencies.size > 1,
            minCombination,
            maxCombination
        };
    }, [eligibleItems, relevantExtraLines, extraItemDecisions]);

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

    const handleToggleCandidate = (reqItemId: string, candidateId: string) => {
        setEligibleItems(prev => prev.map(item => {
            if (item.reqItem.id !== reqItemId) return item;
            const checked = item.checkedIds.includes(candidateId);
            return {
                ...item,
                checkedIds: checked
                    ? item.checkedIds.filter(id => id !== candidateId)
                    : [...item.checkedIds, candidateId]
            };
        }));
    };

    const handleToggleIncluded = (reqItemId: string) => {
        setEligibleItems(prev => prev.map(item =>
            item.reqItem.id === reqItemId ? { ...item, included: !item.included } : item
        ));
    };

    const handleBuyerNoteChange = (reqItemId: string, candidateId: string, note: string) => {
        setEligibleItems(prev => prev.map(item =>
            item.reqItem.id === reqItemId
                ? { ...item, buyerNotes: { ...item.buyerNotes, [candidateId]: note } }
                : item
        ));
    };

    const handleChangeExtraItemDecision = (quotationItemId: string, decision: ExtraItemDecisionState) => {
        if (lockedItemId === quotationItemId) { setLockedItemId(null); setLockedReason(null); }
        if (fieldErrorItemId === quotationItemId) { setFieldErrorItemId(null); setFieldErrorMessage(null); }
        setExtraItemDecisions(prev => ({ ...prev, [quotationItemId]: decision }));
    };

    const isExtraItemsValid = relevantExtraLines.every(line => {
        const state = extraItemDecisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
        if (state.decision === 'EXCLUDE') return validateReconciliationJustification(state.comment).isValid;
        return true;
    });

    const includedItems = eligibleItems.filter(i => i.included);
    const invalidItems = includedItems.filter(i => i.checkedIds.length === 0);

    const handleSubmit = async () => {
        setSubmitError(null);
        setPendingItemsError(null);
        setLockedItemId(null);
        setLockedReason(null);
        setFieldErrorItemId(null);
        setFieldErrorMessage(null);

        if (includedItems.length === 0) {
            setSubmitError('Inclua pelo menos um item solicitado no lote.');
            return;
        }

        if (invalidItems.length > 0) {
            // Inline validation on the offending item, plus scroll/focus to the first one.
            setShowValidation(true);
            const firstInvalidId = invalidItems[0].reqItem.id;
            itemRefs.current[firstInvalidId]?.scrollIntoView({ behavior: 'smooth', block: 'center' });
            return;
        }

        if (!isExtraItemsValid) {
            setSubmitError('Revise o motivo de remoção de algum item adicional antes de continuar.');
            return;
        }

        // Candidate model payload: identity + optional note only. No winner, no financial values
        // — the backend snapshots the authoritative commercial facts server-side.
        const submitData: BatchItemInput[] = includedItems.map(item => ({
            requestLineItemId: String(item.reqItem.id),
            candidates: item.checkedIds.map(candidateId => {
                const note = (item.buyerNotes[candidateId] || '').trim();
                return note
                    ? { quotationItemId: String(candidateId), buyerNote: note }
                    : { quotationItemId: String(candidateId) };
            })
        }));

        const extraItemDecisionsPayload: Record<string, ExtraItemDecisionPayload> = {};
        relevantExtraLines.forEach(line => {
            const state = extraItemDecisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
            extraItemDecisionsPayload[line.quotationItemId] = { decision: state.decision || 'INCLUDE', comment: state.comment || undefined };
        });

        setIsSubmitting(true);
        try {
            await onSubmit(submitData, Object.keys(extraItemDecisionsPayload).length > 0 ? extraItemDecisionsPayload : undefined);
        } catch (err) {
            const parsed = parseExtraItemDecisionError(err, relevantExtraLines);
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
        } finally {
            setIsSubmitting(false);
        }
    };

    if (!isOpen) return null;

    return (
        <div
            style={{
                position: 'fixed',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.7)',
                backdropFilter: 'blur(4px)',
                zIndex: 10000,
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'flex-start',
                padding: '40px 24px',
                overflowY: 'auto'
            }}
            onClick={onClose}
        >
            <div
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#FFFFFF',
                    borderRadius: '12px',
                    width: '100%',
                    maxWidth: '760px',
                    boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                    display: 'flex',
                    flexDirection: 'column',
                    maxHeight: 'calc(100vh - 80px)'
                }}
            >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 24px', borderBottom: '1px solid var(--color-border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <div style={{ backgroundColor: '#e0f2fe', padding: '8px', borderRadius: '8px' }}>
                            <Package size={24} color="#0284c7" />
                        </div>
                        <div>
                            <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                Enviar Cotações para Aprovação
                            </h2>
                            <p style={{ margin: 0, fontSize: '0.85rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                Selecione os itens e as opções de cotação que serão apresentadas ao Aprovador de Área. O vencedor será escolhido na aprovação.
                            </p>
                        </div>
                    </div>
                    <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', color: 'var(--color-text-muted)' }}>
                        <X size={20} />
                    </button>
                </div>

                <div style={{ padding: '24px', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    <div>
                        <h3 style={{ margin: '0 0 12px 0', fontSize: '1rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                            Itens com cotação ({eligibleItems.length})
                        </h3>
                        {eligibleItems.length === 0 ? (
                            <p style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>Nenhum item elegível para envio à aprovação.</p>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                {eligibleItems.map(item => {
                                    const itemInvalid = showValidation && item.included && item.checkedIds.length === 0;
                                    // "MENOR VALOR" badge — per currency within this item; informational only.
                                    const lowestByCurrency: Record<string, number> = {};
                                    item.candidates.forEach(c => {
                                        if (lowestByCurrency[c.currency] === undefined || c.lineTotal < lowestByCurrency[c.currency]) {
                                            lowestByCurrency[c.currency] = c.lineTotal;
                                        }
                                    });
                                    return (
                                        <div
                                            key={item.reqItem.id}
                                            ref={el => { itemRefs.current[item.reqItem.id] = el; }}
                                            style={{
                                                border: itemInvalid ? '1px solid #dc2626' : '1px solid var(--color-border)',
                                                borderRadius: '8px', padding: '16px',
                                                backgroundColor: item.included ? 'var(--color-bg-surface)' : '#f9fafb',
                                                opacity: item.included ? 1 : 0.75
                                            }}
                                        >
                                            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                                                <div>
                                                    <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>
                                                        Linha {item.reqItem.lineNumber} &mdash; Qtd solicitada: {item.reqItem.quantity} {item.reqItem.unit}
                                                    </div>
                                                    <div style={{ marginTop: '6px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                                                        <div style={{ marginBottom: '2px' }}>Item solicitado:</div>
                                                        <div style={{ fontWeight: 600, color: 'var(--color-text-main)', wordBreak: 'break-word' }}>
                                                            {getRequestItemDescription(item.reqItem)}
                                                        </div>
                                                    </div>
                                                </div>
                                                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', flexShrink: 0, fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-muted)' }} onClick={(e) => e.stopPropagation()}>
                                                    <input
                                                        type="checkbox"
                                                        checked={item.included}
                                                        onChange={() => handleToggleIncluded(item.reqItem.id)}
                                                        style={{ cursor: 'pointer' }}
                                                    />
                                                    Incluir no lote
                                                </label>
                                            </div>

                                            {item.included && (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                    <div style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                                                        Opções a Enviar para Aprovação
                                                    </div>
                                                    {itemInvalid && (
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '8px 12px', fontSize: '0.8rem', color: '#991b1b', fontWeight: 600 }}>
                                                            <AlertTriangle size={14} /> Selecione pelo menos uma opção de cotação para este item (ou remova-o do lote).
                                                        </div>
                                                    )}
                                                    {item.candidates.map(candidate => {
                                                        const checked = item.checkedIds.includes(candidate.quotationItemId);
                                                        const isLowest = candidate.lineTotal === lowestByCurrency[candidate.currency];
                                                        const quantityDiffers = item.reqItem.quantity != null
                                                            && Number(candidate.quantity) !== Number(item.reqItem.quantity);
                                                        const docDate = formatDocumentDate(candidate.documentDate);
                                                        return (
                                                            <div key={candidate.quotationItemId} style={{ border: checked ? '1px solid #3b82f6' : '1px solid var(--color-border)', borderRadius: '6px', backgroundColor: checked ? '#eff6ff' : 'transparent' }}>
                                                                <label style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', padding: '12px', cursor: 'pointer' }}>
                                                                    <input
                                                                        type="checkbox"
                                                                        checked={checked}
                                                                        onChange={() => handleToggleCandidate(item.reqItem.id, candidate.quotationItemId)}
                                                                        style={{ cursor: 'pointer', marginTop: '4px' }}
                                                                    />
                                                                    <div style={{ flex: 1 }}>
                                                                        <div style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text-main)', wordBreak: 'break-word', display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                                                                            {candidate.description}
                                                                            {isLowest && (
                                                                                <span style={{ padding: '2px 6px', backgroundColor: '#ecfdf5', color: '#047857', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px', whiteSpace: 'nowrap', border: '1px solid #a7f3d0' }}>MENOR VALOR</span>
                                                                            )}
                                                                            {candidate.reconciliationStatus === 'SUBSTITUTE' && (
                                                                                <span style={{ padding: '2px 6px', backgroundColor: '#fef9c3', color: '#854d0e', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px', whiteSpace: 'nowrap' }}>Substituto</span>
                                                                            )}
                                                                            {quantityDiffers && (
                                                                                <span style={{ padding: '2px 6px', backgroundColor: '#fff7ed', color: '#9a3412', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px', whiteSpace: 'nowrap', border: '1px solid #fed7aa' }}>Qtd difere do pedido</span>
                                                                            )}
                                                                        </div>
                                                                        <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '6px' }}>
                                                                            Fornecedor: <span style={{ fontWeight: 600 }}>{candidate.supplierName}</span>
                                                                            {candidate.documentNumber && (
                                                                                <span> &middot; Doc: <span style={{ fontWeight: 600 }}>{candidate.documentNumber}</span>{docDate ? ` (${docDate})` : ''}</span>
                                                                            )}
                                                                        </div>
                                                                        {(candidate.lineAdjustmentJustification || (candidate.reconciliationStatus === 'SUBSTITUTE' && candidate.reconciliationJustification)) && (
                                                                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '6px', marginTop: '6px', fontSize: '0.75rem', color: '#92400e', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '4px', padding: '6px 8px' }}>
                                                                                <AlertTriangle size={12} style={{ marginTop: '1px', flexShrink: 0 }} />
                                                                                <span>{candidate.lineAdjustmentJustification || candidate.reconciliationJustification}</span>
                                                                            </div>
                                                                        )}
                                                                    </div>
                                                                    <div style={{ textAlign: 'right', flexShrink: 0, minWidth: '170px' }}>
                                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Qtd cotada: <strong style={{ color: 'var(--color-text-main)' }}>{candidate.quantity}{candidate.unitLabel ? ` ${candidate.unitLabel}` : ''}</strong></div>
                                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Preço unitário s/ IVA: {formatCurrencyAO(candidate.unitPrice)}</div>
                                                                        {candidate.discountAmount > 0 && (
                                                                            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Desconto: {formatCurrencyAO(candidate.discountAmount)}</div>
                                                                        )}
                                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>IVA ({candidate.ivaRatePercent}%): {formatCurrencyAO(candidate.ivaAmount)}</div>
                                                                        <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)', marginTop: '2px' }}>Total da linha c/ IVA: {formatCurrencyAO(candidate.lineTotal)}</div>
                                                                    </div>
                                                                </label>
                                                                {checked && (
                                                                    <div style={{ padding: '0 12px 12px 40px' }}>
                                                                        <input
                                                                            type="text"
                                                                            value={item.buyerNotes[candidate.quotationItemId] || ''}
                                                                            onChange={(e) => handleBuyerNoteChange(item.reqItem.id, candidate.quotationItemId, e.target.value)}
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
                                            )}
                                            {!item.included && (
                                                <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                                    Fora deste lote — o item continuará na sua fila com as cotações registradas.
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        )}
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

                    {pendingItems.length > 0 && (
                        <div>
                            <h3 style={{ margin: '0 0 12px 0', fontSize: '1rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                                Itens Restantes ({pendingItems.length})
                            </h3>
                            <div style={{ backgroundColor: '#fef2f2', border: '1px solid #fecaca', padding: '12px 16px', borderRadius: '8px', display: 'flex', alignItems: 'flex-start', gap: '12px', marginBottom: '16px' }}>
                                <AlertTriangle size={20} color="#dc2626" style={{ marginTop: '2px' }} />
                                <div>
                                    <div style={{ fontWeight: 800, fontSize: '0.85rem', color: '#991b1b' }}>Os itens não incluídos continuarão na sua fila aguardando novas cotações.</div>
                                </div>
                            </div>
                            <div style={{ border: '1px solid var(--color-border)', borderRadius: '8px', overflow: 'hidden' }}>
                                {pendingItems.map((item, index) => (
                                    <div key={item.id} style={{ padding: '12px 16px', backgroundColor: 'var(--color-bg-page)', borderBottom: index < pendingItems.length - 1 ? '1px solid var(--color-border)' : 'none', display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                        <div>
                                            <div style={{ fontWeight: 700, fontSize: '0.85rem' }}>
                                                Linha {item.lineNumber} &mdash; Qtd: {item.quantity} {item.unit}
                                            </div>
                                            <div style={{ marginTop: '6px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                                                <div style={{ marginBottom: '2px' }}>Item solicitado:</div>
                                                <div style={{ fontWeight: 600, color: 'var(--color-text-main)', wordBreak: 'break-word' }}>
                                                    {getRequestItemDescription(item)}
                                                </div>
                                            </div>
                                        </div>
                                        <span style={{ fontSize: '0.75rem', fontWeight: 700, color: '#dc2626', backgroundColor: '#fef2f2', padding: '4px 8px', borderRadius: '4px', border: '1px solid #fecaca', flexShrink: 0, marginTop: '4px' }}>
                                            Pendente de cotação
                                        </span>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                </div>

                {eligibleItems.length > 0 && (
                    <div style={{ padding: '16px 24px', borderTop: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                        <h3 style={{ margin: '0 0 10px 0', fontSize: '0.95rem', fontWeight: 700, color: 'var(--color-primary)' }}>
                            Resumo do lote
                        </h3>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', fontSize: '0.85rem' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>Itens selecionados</span>
                                <strong style={{ color: 'var(--color-text-main)' }}>{batchSummary.includedItemCount}</strong>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>Opções enviadas</span>
                                <strong style={{ color: 'var(--color-text-main)' }}>{batchSummary.optionCount}</strong>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>Fornecedores</span>
                                <strong style={{ color: 'var(--color-text-main)' }}>{batchSummary.supplierCount}</strong>
                            </div>
                            {batchSummary.includedExtraCount > 0 && (
                                <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                    <span>Itens adicionais incluídos</span>
                                    <strong style={{ color: 'var(--color-text-main)' }}>{batchSummary.includedExtraCount}</strong>
                                </div>
                            )}
                            {batchSummary.mixedCurrencies ? (
                                <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)', paddingTop: '6px', borderTop: '1px dashed var(--color-border)' }}>
                                    <span>Faixa comercial</span>
                                    <strong style={{ color: 'var(--color-text-main)' }}>Múltiplas moedas — não agregada</strong>
                                </div>
                            ) : (batchSummary.minCombination !== null && batchSummary.maxCombination !== null) && (
                                <div style={{ paddingTop: '6px', borderTop: '1px dashed var(--color-border)' }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                        <span>Menor combinação possível</span>
                                        <strong style={{ color: 'var(--color-text-main)' }}>{formatCurrencyAO(batchSummary.minCombination)}</strong>
                                    </div>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                        <span>Maior combinação possível</span>
                                        <strong style={{ color: 'var(--color-text-main)' }}>{formatCurrencyAO(batchSummary.maxCombination)}</strong>
                                    </div>
                                </div>
                            )}
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '8px', borderTop: '1px dashed var(--color-border)' }}>
                                <span style={{ fontWeight: 700, fontSize: '0.95rem', color: 'var(--color-primary)' }}>Total aprovado</span>
                                <strong style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>A definir pelo Aprovador de Área</strong>
                            </div>
                        </div>
                    </div>
                )}

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
                {submitError && (
                    <div style={{ padding: '12px 24px', backgroundColor: '#fef2f2', borderTop: '1px solid #fecaca', color: '#dc2626', fontSize: '0.85rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <AlertTriangle size={16} />
                        {submitError}
                    </div>
                )}
                <div style={{ padding: '16px 24px', borderTop: '1px solid var(--color-border)', display: 'flex', justifyContent: 'flex-end', gap: '12px', backgroundColor: 'var(--color-bg-surface)', borderBottomLeftRadius: '12px', borderBottomRightRadius: '12px' }}>
                    <button onClick={onClose} style={{ padding: '10px 20px', backgroundColor: 'white', border: '1px solid var(--color-border)', borderRadius: '8px', fontWeight: 600, color: 'var(--color-text-muted)', cursor: 'pointer' }}>
                        Cancelar
                    </button>
                    <button onClick={handleSubmit} disabled={includedItems.length === 0 || isSubmitting} style={{ padding: '10px 20px', backgroundColor: includedItems.length > 0 && !isSubmitting ? 'var(--color-primary)' : 'var(--color-border)', border: 'none', borderRadius: '8px', fontWeight: 600, color: 'white', cursor: includedItems.length > 0 && !isSubmitting ? 'pointer' : 'not-allowed', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Send size={16} />
                        {isSubmitting ? 'Enviando...' : 'Enviar opções para aprovação'}
                    </button>
                </div>
            </div>
        </div>
    );
};
