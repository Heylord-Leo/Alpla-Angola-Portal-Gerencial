import React, { useState, useEffect } from 'react';
import { X, AlertTriangle, CheckCircle, Package } from 'lucide-react';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestLineItemDto, SavedQuotationDto } from '../../types';
import { isQuotationItemSelectableForApproval } from './batchEligibility';

interface Candidate {
    quotationItemId: string;
    quotationId: string;
    supplierName: string;
    description: string;
    unitPrice: number;
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
    onSubmit: (items: { requestLineItemId: string, selectedQuotationItemId: string }[]) => void;
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
    const [submitError, setSubmitError] = useState<string | null>(null);

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
                            unitPrice: qi.unitPrice || 0,
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

    }, [isOpen, group]);



    const handleSelectCandidate = (reqItemId: string, candidateId: string) => {
        setEligibleItems(prev => prev.map(item =>
            item.reqItem.id === reqItemId ? { ...item, selectedCandidateId: candidateId } : item
        ));
    };


    const isValid = eligibleItems.every(item => item.selectedCandidateId !== null);

    const handleSubmit = () => {
        setSubmitError(null);
        
        if (eligibleItems.length === 0) {
            setSubmitError('Nenhum item elegível para aprovação parcial.');
            return;
        }

        if (!isValid) {
            setSubmitError('Por favor, selecione uma cotação para cada item.');
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

        if (import.meta.env.DEV) {
            console.log('[PartialApproval] Final POST Payload:', JSON.stringify(submitData, null, 2));
        }
        onSubmit(submitData);
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
                                                    <div style={{ textAlign: 'right', flexShrink: 0 }}>
                                                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Valor:</div>
                                                        <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>
                                                            {formatCurrencyAO(candidate.unitPrice)}
                                                        </div>
                                                    </div>
                                                </label>
                                            ))}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>

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
                    <button onClick={handleSubmit} disabled={!isValid || eligibleItems.length === 0} style={{ padding: '10px 20px', backgroundColor: isValid && eligibleItems.length > 0 ? 'var(--color-primary)' : 'var(--color-border)', border: 'none', borderRadius: '8px', fontWeight: 600, color: 'white', cursor: isValid && eligibleItems.length > 0 ? 'pointer' : 'not-allowed', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        Confirmar e Enviar para Aprovação
                    </button>
                </div>
            </div>
        </div>
    );
};
