import React, { useState, useEffect, useMemo } from 'react';
import { X, AlertTriangle, CheckCircle, Package } from 'lucide-react';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestLineItemDto, SavedQuotationDto, ExtraItemDecisionState, ExtraItemDecisionPayload } from '../../types';
import { isQuotationItemSelectableForApproval } from './batchEligibility';
import { BatchExtraItemsDecisionPanel } from './BatchExtraItemsDecisionPanel';
import { computeRelevantExtraLines, parseExtraItemDecisionError } from './batchExtraItemsLogic';
import { validateReconciliationJustification } from '../../lib/reconciliationJustificationValidator';

interface Candidate {
    quotationItemId: string;
    quotationId: string;
    supplierName: string;
    description: string;
    // Persisted, backend-authoritative monetary fields (copied verbatim from SavedQuotationItemDto —
    // never recomputed in the frontend). quantity is the QUOTATION-line quantity, not the requested qty.
    quantity: number;
    unitPrice: number;
    /** null = the authoritative value was absent from the API payload (never silently coerced to 0);
     * a real stored 0 stays 0. See the lot-summary missing-field guard. */
    taxableBase: number | null;
    ivaAmount: number;
    ivaRatePercent: number;
    lineTotal: number;
    currency: string;
    reconciliationStatus: string;
}

interface EligibleItem {
    reqItem: RequestLineItemDto;
    candidates: Candidate[];
    selectedCandidateId: string | null;
}

interface PartialApprovalBatchModalProps {
    isOpen: boolean;
    onClose: () => void;
    group: any;
    /** Must throw (not swallow) on failure so the modal can render the error inline —
     * BuyerItemsList.handlePartialApprovalSubmit only handles the success side-effects. */
    onSubmit: (
        items: { requestLineItemId: string, selectedQuotationItemId: string }[],
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

            const candidates: Candidate[] = [];
            quotations.forEach(quotation => {
                const qItems = quotation.items || [];
                qItems.forEach(qi => {
                    if (qi.mappedRequestLineItemId === reqItemId &&
                        (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
                        isQuotationItemSelectableForApproval(qi.id, group)) {
                        if (import.meta.env.DEV) {
                            console.log(`[PartialApproval] Match found! Normalized RequestItemId: ${reqItemId} === Mapped: ${qi.mappedRequestLineItemId}`);
                        }
                        candidates.push({
                            quotationItemId: qi.id,
                            quotationId: quotation.id,
                            supplierName: quotation.supplierNameSnapshot || 'Fornecedor',
                            description: qi.description,
                            quantity: qi.quantity || 0,
                            unitPrice: qi.unitPrice || 0,
                            taxableBase: qi.taxableBase ?? null,
                            ivaAmount: qi.ivaAmount || 0,
                            ivaRatePercent: qi.ivaRatePercent || 0,
                            lineTotal: qi.lineTotal || 0,
                            currency: qi.currencyCode || 'AOA',
                            reconciliationStatus: qi.reconciliationStatus
                        });
                    }
                });
            });

            if (candidates.length > 0) {
                const selectedCandidateId = candidates.length === 1 ? candidates[0].quotationItemId : null;
                newEligibleItems.push({
                    reqItem: normalizedReqItem,
                    candidates,
                    selectedCandidateId
                });
            } else {
                if (import.meta.env.DEV) {
                    console.log(`[PartialApproval] Item ${normalizedReqItem.lineNumber} (${reqItemId}) remains pending`);
                }
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

    }, [isOpen, group]);

    const relevantExtraLines = useMemo(() => {
        const selections = eligibleItems.map(item => {
            const candidate = item.candidates.find(c => c.quotationItemId === item.selectedCandidateId);
            return { quotationItemId: item.selectedCandidateId, quotationId: candidate?.quotationId || null };
        });
        return computeRelevantExtraLines(selections, group?.quotations || []);
    }, [eligibleItems, group]);
    const relevantExtraLineIds = useMemo(() => relevantExtraLines.map(l => l.quotationItemId).sort().join('|'), [relevantExtraLines]);

    // Dynamic "Resumo financeiro do lote". Sums ONLY the persisted, backend-authoritative fields
    // (taxableBase / ivaAmount / lineTotal) of the currently-included set — never re-derived from
    // quantity/price/discount/IVA rate. Included set = each selected eligible winner + each
    // EXTRA_ITEM whose decision is not EXCLUDE. Recomputes on winner selection, eligible-item set,
    // relevant-extra set, and include/exclude decision changes.
    const lotSummary = useMemo(() => {
        let subtotalSemIva = 0, ivaTotal = 0, totalComIva = 0, selectedRequestedCount = 0, includedExtraCount = 0;
        // Distinguish a genuinely-missing authoritative field (null — API omitted taxableBase) from a
        // real stored zero: a missing value must never be silently summed as 0 and shown as valid.
        let missingTaxableBaseCount = 0;

        const addTaxableBase = (value: number | null) => {
            if (value == null) { missingTaxableBaseCount++; return; }
            subtotalSemIva += value;
        };

        eligibleItems.forEach(item => {
            const candidate = item.candidates.find(c => c.quotationItemId === item.selectedCandidateId);
            if (!candidate) return;
            selectedRequestedCount++;
            addTaxableBase(candidate.taxableBase);
            ivaTotal += candidate.ivaAmount;
            totalComIva += candidate.lineTotal;
        });

        relevantExtraLines.forEach(line => {
            const state = extraItemDecisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
            if (state.decision === 'EXCLUDE') return;
            includedExtraCount++;
            addTaxableBase(line.taxableBase);
            ivaTotal += line.ivaAmount;
            totalComIva += line.lineTotal;
        });

        return { subtotalSemIva, ivaTotal, totalComIva, selectedRequestedCount, includedExtraCount, hasMissingTaxableBase: missingTaxableBaseCount > 0 };
    }, [eligibleItems, relevantExtraLines, extraItemDecisions]);

    // Reconcile decision state whenever the set of relevant EXTRA_ITEM lines changes (winner
    // selection changed contributing quotations): preserve decisions for lines still relevant,
    // drop decisions for lines no longer relevant. EXTRA_ITEM lines default to INCLUDE
    // (review-first: a valid default always exists, the buyer only overrides).
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
        setEligibleItems(prev => prev.map(item =>
            item.reqItem.id === reqItemId ? { ...item, selectedCandidateId: candidateId } : item
        ));
    };

    const handleChangeExtraItemDecision = (quotationItemId: string, decision: ExtraItemDecisionState) => {
        if (lockedItemId === quotationItemId) { setLockedItemId(null); setLockedReason(null); }
        if (fieldErrorItemId === quotationItemId) { setFieldErrorItemId(null); setFieldErrorMessage(null); }
        setExtraItemDecisions(prev => ({ ...prev, [quotationItemId]: decision }));
    };

    const isExtraItemsValid = relevantExtraLines.every(line => {
        // Every EXTRA_ITEM line always has a valid default (INCLUDE) — the modal is submit-ready
        // instantly; only an explicit EXCLUDE override requires a comment.
        const state = extraItemDecisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
        if (state.decision === 'EXCLUDE') return validateReconciliationJustification(state.comment).isValid;
        return true;
    });

    const isValid = eligibleItems.every(item => item.selectedCandidateId !== null) && isExtraItemsValid && !lotSummary.hasMissingTaxableBase;

    const handleSubmit = async () => {
        setSubmitError(null);
        setPendingItemsError(null);
        setLockedItemId(null);
        setLockedReason(null);
        setFieldErrorItemId(null);
        setFieldErrorMessage(null);

        if (eligibleItems.length === 0) {
            setSubmitError('Nenhum item elegível para aprovação parcial.');
            return;
        }

        if (!eligibleItems.every(item => item.selectedCandidateId !== null)) {
            setSubmitError('Por favor, selecione uma cotação para cada item.');
            return;
        }

        if (!isExtraItemsValid) {
            setSubmitError('Revise o motivo de remoção de algum item adicional antes de continuar.');
            return;
        }

        if (lotSummary.hasMissingTaxableBase) {
            setSubmitError('O valor tributável (subtotal sem IVA) de um ou mais itens não foi retornado pelo servidor. Atualize a página para recarregar os dados da cotação antes de confirmar o lote.');
            return;
        }

        const submitData = [];

        for (const item of eligibleItems) {
            // Safely normalize to string before validating
            const reqItemId = item.reqItem.id ? String(item.reqItem.id) : '';
            const quotationItemId = item.selectedCandidateId ? String(item.selectedCandidateId) : '';

            if (import.meta.env.DEV) {
                console.log(`[PartialApproval] Processing Payload Entry - RequestItemId: ${reqItemId}, QuotationItemId: ${quotationItemId}`);
            }

            if (!reqItemId || reqItemId.trim() === '' || !quotationItemId || quotationItemId.trim() === '') {
                setSubmitError(`Payload inválido: IDs ausentes na linha ${item.reqItem.lineNumber}`);
                return; // Return safely without throwing an uncaught error
            }

            submitData.push({
                requestLineItemId: reqItemId,
                selectedQuotationItemId: quotationItemId
            });
        }

        const extraItemDecisionsPayload: Record<string, ExtraItemDecisionPayload> = {};
        relevantExtraLines.forEach(line => {
            const state = extraItemDecisions[line.quotationItemId] || { decision: 'INCLUDE', comment: '' };
            extraItemDecisionsPayload[line.quotationItemId] = { decision: state.decision || 'INCLUDE', comment: state.comment || undefined };
        });

        if (import.meta.env.DEV) {
            console.log('[PartialApproval] Final POST Payload:', JSON.stringify({ items: submitData, extraItemDecisions: extraItemDecisionsPayload }, null, 2));
        }

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
                    maxWidth: '700px',
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
                                Aprovação Parcial de Cotações
                            </h2>
                            <p style={{ margin: 0, fontSize: '0.85rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                Selecione os itens que deseja avançar para aprovação
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
                            Itens a enviar no Lote ({eligibleItems.length})
                        </h3>
                        {eligibleItems.length === 0 ? (
                            <p style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>Nenhum item elegível para aprovação parcial.</p>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                {eligibleItems.map(item => (
                                    <div key={item.reqItem.id} style={{ border: '1px solid var(--color-border)', borderRadius: '8px', padding: '16px', backgroundColor: 'var(--color-bg-surface)' }}>
                                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                                            <div>
                                                <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>
                                                    Linha {item.reqItem.lineNumber} &mdash; Qtd: {item.reqItem.quantity} {item.reqItem.unit}
                                                </div>
                                                <div style={{ marginTop: '6px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                                                    <div style={{ marginBottom: '2px' }}>Item solicitado:</div>
                                                    <div style={{ fontWeight: 600, color: 'var(--color-text-main)', wordBreak: 'break-word' }}>
                                                        {getRequestItemDescription(item.reqItem)}
                                                    </div>
                                                </div>
                                            </div>
                                            {item.selectedCandidateId ? (
                                                <CheckCircle size={20} color="#059669" style={{ flexShrink: 0 }} />
                                            ) : (
                                                <AlertTriangle size={20} color="#d97706" style={{ flexShrink: 0 }} />
                                            )}
                                        </div>

                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                            <div style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                                                Vencedor Selecionado
                                            </div>
                                            {item.candidates.map(candidate => (
                                                <label key={candidate.quotationItemId} style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', padding: '12px', border: item.selectedCandidateId === candidate.quotationItemId ? '1px solid #3b82f6' : '1px solid var(--color-border)', borderRadius: '6px', backgroundColor: item.selectedCandidateId === candidate.quotationItemId ? '#eff6ff' : 'transparent', cursor: 'pointer' }}>
                                                    <input
                                                        type="radio"
                                                        name={`candidate-${item.reqItem.id}`}
                                                        checked={item.selectedCandidateId === candidate.quotationItemId}
                                                        onChange={() => handleSelectCandidate(item.reqItem.id, candidate.quotationItemId)}
                                                        style={{ cursor: 'pointer', marginTop: '4px' }}
                                                    />
                                                    <div style={{ flex: 1 }}>
                                                        <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginBottom: '2px' }}>
                                                            Item na cotação/proforma:
                                                        </div>
                                                        <div style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text-main)', wordBreak: 'break-word', display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                                                            {candidate.description}
                                                            {candidate.reconciliationStatus === 'SUBSTITUTE' && (
                                                                <span style={{ padding: '2px 6px', backgroundColor: '#fef9c3', color: '#854d0e', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px', whiteSpace: 'nowrap' }}>Substituto</span>
                                                            )}
                                                            {candidate.reconciliationStatus === 'MAPPED' && (
                                                                <span style={{ padding: '2px 6px', backgroundColor: '#f0fdf4', color: '#166534', fontSize: '0.7rem', fontWeight: 700, borderRadius: '4px', whiteSpace: 'nowrap' }}>Mapeado</span>
                                                            )}
                                                        </div>
                                                        <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '6px' }}>
                                                            Fornecedor: <span style={{ fontWeight: 600 }}>{candidate.supplierName}</span>
                                                        </div>
                                                    </div>
                                                    <div style={{ textAlign: 'right', flexShrink: 0, minWidth: '160px' }}>
                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Qtd: <strong style={{ color: 'var(--color-text-main)' }}>{candidate.quantity}</strong></div>
                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Preço unitário s/ IVA: {formatCurrencyAO(candidate.unitPrice)}</div>
                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>IVA ({candidate.ivaRatePercent}%): {formatCurrencyAO(candidate.ivaAmount)}</div>
                                                        <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)', marginTop: '2px' }}>Total da linha c/ IVA: {formatCurrencyAO(candidate.lineTotal)}</div>
                                                    </div>
                                                </label>
                                            ))}
                                        </div>
                                    </div>
                                ))}
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
                            Resumo financeiro do lote
                        </h3>
                        {lotSummary.hasMissingTaxableBase && (
                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '10px 12px', marginBottom: '10px' }}>
                                <AlertTriangle size={16} color="#dc2626" style={{ marginTop: '1px', flexShrink: 0 }} />
                                <span style={{ fontSize: '0.8rem', color: '#991b1b', fontWeight: 600 }}>
                                    Subtotal sem IVA indisponível: um ou mais itens incluídos não retornaram o valor tributável do servidor. Atualize a página para recarregar os dados da cotação antes de confirmar.
                                </span>
                            </div>
                        )}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', fontSize: '0.85rem' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>Itens solicitados selecionados</span>
                                <strong style={{ color: 'var(--color-text-main)' }}>{lotSummary.selectedRequestedCount}</strong>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>Itens adicionais incluídos</span>
                                <strong style={{ color: 'var(--color-text-main)' }}>{lotSummary.includedExtraCount}</strong>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>Subtotal sem IVA</span>
                                {lotSummary.hasMissingTaxableBase ? (
                                    <strong style={{ color: '#dc2626' }}>Indisponível</strong>
                                ) : (
                                    <strong style={{ color: 'var(--color-text-main)' }}>{formatCurrencyAO(lotSummary.subtotalSemIva)}</strong>
                                )}
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--color-text-muted)' }}>
                                <span>IVA total</span>
                                <strong style={{ color: 'var(--color-text-main)' }}>{formatCurrencyAO(lotSummary.ivaTotal)}</strong>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '8px', borderTop: '1px dashed var(--color-border)' }}>
                                <span style={{ fontWeight: 700, fontSize: '1rem', color: 'var(--color-primary)' }}>Total considerado neste lote</span>
                                <strong style={{ fontWeight: 800, fontSize: '1rem', color: 'var(--color-primary)' }}>{formatCurrencyAO(lotSummary.totalComIva)}</strong>
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
                    <button onClick={handleSubmit} disabled={!isValid || eligibleItems.length === 0 || isSubmitting} style={{ padding: '10px 20px', backgroundColor: isValid && eligibleItems.length > 0 && !isSubmitting ? 'var(--color-primary)' : 'var(--color-border)', border: 'none', borderRadius: '8px', fontWeight: 600, color: 'white', cursor: isValid && eligibleItems.length > 0 && !isSubmitting ? 'pointer' : 'not-allowed', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        {isSubmitting ? 'Enviando...' : 'Confirmar e Enviar para Aprovação'}
                    </button>
                </div>
            </div>
        </div>
    );
};
