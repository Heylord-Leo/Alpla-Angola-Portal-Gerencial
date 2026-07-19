import React, { useEffect, useState } from 'react';
import { OcrDraft, RequestDetailsDto } from '../../../types';
import { UseQuotationWizardStateReturn } from './hooks/useQuotationWizardState';
import { CheckCircle, AlertCircle, HelpCircle, Link as LinkIcon, Plus, XCircle, Search, RefreshCw, Lock, FilePlus } from 'lucide-react';
import { isLineItemEligibleForQuotation } from '../batchEligibility';
import { AddRequestedItemModal } from './AddRequestedItemModal';

interface WizardStepReconciliationProps {
    draft: OcrDraft | null;
    request: RequestDetailsDto | null;
    wizardState: UseQuotationWizardStateReturn;
    ivaRates: any[];
    units?: any[];
    /** Upsert a requested line item created by the from-proforma workaround into the active request. */
    onRequestLineItemUpserted?: (item: any) => void;
}

const ACTIVE_BATCH_STATUSES = ['WAITING_AREA_APPROVAL', 'AREA_ADJUSTMENT', 'WAITING_FINAL_APPROVAL', 'FINAL_ADJUSTMENT'];

export const WizardStepReconciliation: React.FC<WizardStepReconciliationProps> = ({
    draft,
    request,
    wizardState,
    ivaRates,
    units = [],
    onRequestLineItemUpserted
}) => {
    // Which OCR draft line currently has the "add as requested item" form open (index into draft.items).
    const [addFormDraftIndex, setAddFormDraftIndex] = useState<number | null>(null);

    if (!draft || !request) return null;

    const { updateDraftItemFields, toggleNotQuotedPlaceholder } = wizardState;

    // Backend-enforced scope, mirrored in the UI: only QUOTATION in WAITING_QUOTATION with no active batch.
    const req: any = request;
    const hasActiveBatch = (req.approvalBatches || []).some((b: any) => ACTIVE_BATCH_STATUSES.includes(b.status));
    const canAddRequestedItem =
        req.requestTypeCode === 'QUOTATION' &&
        req.requestStatusCode === 'WAITING_QUOTATION' &&
        !hasActiveBatch;

    // Whether the request TRULY has no items — the signal that gates the "add as requested item" action.
    // The backend list already excludes soft-deleted items (LineItemsController: !li.IsDeleted), so any
    // line present here counts as an existing item, INCLUDING cancelled ones and non-eligible statuses
    // (BATCH_ASSIGNED / QUOTATION_APPROVED / CLOSED_NOT_QUOTED). This is intentionally DIFFERENT from
    // `eligibleRequestItems` (quotation-eligibility only): the button must not reappear just because all
    // existing items became ineligible for the current quotation. Conservative: any non-deleted line blocks it.
    const existingRequestItems = (req.lineItems || []).filter((li: any) => !li.isDeleted);

    // Called after the backend creates (or idempotently/duplicate-returns) a requested item:
    // upsert it into the request locally (no refetch), then auto-map the originating OCR line.
    const handleRequestedItemResolved = (draftIndex: number, item: any) => {
        if (!item || !item.id) return;
        onRequestLineItemUpserted?.(item);
        updateDraftItemFields(draftIndex, {
            reconciliationStatus: 'MAPPED',
            mappedRequestLineItemId: item.id,
            isAutoSuggested: false
        } as any, ivaRates);
        setAddFormDraftIndex(null);
    };

    const realItems = draft.items.filter((i: any) => i.reconciliationStatus !== 'NOT_QUOTED');
    const mappedIds = new Set(draft.items.map((i: any) => i.mappedRequestLineItemId).filter(Boolean));

    // Only items still open for quotation belong in this wizard's active flow —
    // items already in a batch, approved, or formally proposed/accepted as
    // not-quoted are excluded (they are handled elsewhere and must not be
    // re-mapped or re-declared as not-quoted here).
    const eligibleRequestItems = (request.lineItems || []).filter(isLineItemEligibleForQuotation);
    const coveredElsewhereCount = (request.lineItems?.length || 0) - eligibleRequestItems.length;
    const inBatchOrApprovedCount = (request.lineItems || []).filter((i: any) =>
        ['BATCH_ASSIGNED', 'QUOTATION_APPROVED'].includes(i.quotationLifecycleStatus)
    ).length;
    // Kept apart on purpose: PROPOSED is still awaiting a Requester/Area Approver
    // decision (can bounce back to pending on reject) while ACCEPTED is final —
    // lumping them together hides that a decision is still pending.
    const notQuotedProposedCount = (request.lineItems || []).filter((i: any) =>
        i.quotationLifecycleStatus === 'NOT_QUOTED_PROPOSED'
    ).length;
    const notQuotedAcceptedCount = (request.lineItems || []).filter((i: any) =>
        i.quotationLifecycleStatus === 'NOT_QUOTED_ACCEPTED'
    ).length;

    // Auto-suggest logic on mount
    useEffect(() => {
        if (!draft || !request) return;

        realItems.forEach((quoteItem: any) => {
            if (!quoteItem.reconciliationStatus && !quoteItem.isAutoSuggested) {
                const reqItem = eligibleRequestItems.find((r: any) =>
                    r.description.trim().toLowerCase() === quoteItem.description.trim().toLowerCase()
                );
                
                if (reqItem && !mappedIds.has(reqItem.id)) {
                    const itemIndex = draft.items.findIndex((i: any) => i === quoteItem);
                    if (itemIndex !== -1) {
                        updateDraftItemFields(itemIndex, { 
                            reconciliationStatus: 'MAPPED', 
                            mappedRequestLineItemId: reqItem.id,
                            isAutoSuggested: true
                        });
                        mappedIds.add(reqItem.id);
                    }
                }
            }
        });
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const getStatusCounts = () => {
        let mapped = 0, substitute = 0, extra = 0, ignored = 0, notQuoted = 0, pending = 0;
        
        realItems.forEach((item: any) => {
            if (item.reconciliationStatus === 'MAPPED') mapped++;
            else if (item.reconciliationStatus === 'SUBSTITUTE') substitute++;
            else if (item.reconciliationStatus === 'EXTRA_ITEM') extra++;
            else if (item.reconciliationStatus === 'IGNORED') ignored++;
            else pending++;
        });

        draft.items.forEach((item: any) => {
            if (item.reconciliationStatus === 'NOT_QUOTED') notQuoted++;
        });

        const pendingReq = eligibleRequestItems.filter((reqItem: any) => {
            const isCovered = draft.items.some((i: any) => 
                (i.reconciliationStatus === 'MAPPED' || i.reconciliationStatus === 'SUBSTITUTE' || i.reconciliationStatus === 'NOT_QUOTED') &&
                i.mappedRequestLineItemId === reqItem.id
            );
            return !isCovered;
        }).length || 0;

        return { mapped, substitute, extra, ignored, notQuoted, pendingOcr: pending, pendingReq };
    };

    const counts = getStatusCounts();

    const handleStatusChange = (idx: number, newStatus: string) => {
        const itemIndex = draft.items.findIndex((i: any) => i === realItems[idx]);
        if (itemIndex === -1) return;

        const updates: any = { reconciliationStatus: newStatus, isAutoSuggested: false };
        
        if (newStatus === 'MAPPED') {
            updates.reconciliationJustification = null;
        } else if (newStatus === 'EXTRA_ITEM' || newStatus === 'IGNORED') {
            updates.mappedRequestLineItemId = null;
        }
        
        updateDraftItemFields(itemIndex, updates, ivaRates);
    };

    const handleJustificationChange = (idx: number, text: string) => {
        const itemIndex = draft.items.findIndex((i: any) => i === realItems[idx]);
        if (itemIndex === -1) return;
        updateDraftItemFields(itemIndex, { reconciliationJustification: text, isAutoSuggested: false }, ivaRates);
    };

    const handleNotQuotedJustificationChange = (reqItemId: string, text: string) => {
        const itemIndex = draft.items.findIndex((i: any) => i.reconciliationStatus === 'NOT_QUOTED' && i.mappedRequestLineItemId === reqItemId);
        if (itemIndex === -1) return;
        updateDraftItemFields(itemIndex, { reconciliationJustification: text }, ivaRates);
    };

    const handleMappingChange = (idx: number, mappedId: string | null) => {
        const itemIndex = draft.items.findIndex((i: any) => i === realItems[idx]);
        if (itemIndex === -1) return;

        const reqItem = request.lineItems?.find((r: any) => r.id === mappedId);
        if (reqItem) {
            const hasNotQuoted = draft.items.some((i: any) => i.reconciliationStatus === 'NOT_QUOTED' && i.mappedRequestLineItemId === mappedId);
            if (hasNotQuoted) {
                toggleNotQuotedPlaceholder(reqItem);
            }
        }

        updateDraftItemFields(itemIndex, { mappedRequestLineItemId: mappedId, isAutoSuggested: false }, ivaRates);
    };

    const getBadgeStyle = (status: string) => {
        switch (status) {
            case 'MAPPED': return { bg: 'rgba(16, 185, 129, 0.1)', text: '#10B981', label: 'Corresponde ao item', icon: <CheckCircle size={14} /> };
            case 'SUBSTITUTE': return { bg: 'rgba(59, 130, 246, 0.1)', text: '#3B82F6', label: 'Item substituto', icon: <RefreshCw size={14} /> };
            case 'EXTRA_ITEM': return { bg: 'rgba(59, 130, 246, 0.1)', text: '#3B82F6', label: 'Item adicional', icon: <Plus size={14} /> };
            case 'IGNORED': return { bg: 'rgba(148, 163, 184, 0.1)', text: '#94A3B8', label: 'Linha ignorada', icon: <XCircle size={14} /> };
            case 'NOT_QUOTED': return { bg: 'rgba(245, 158, 11, 0.1)', text: '#F59E0B', label: 'Não cotado nesta cotação', icon: <AlertCircle size={14} /> };
            default: return { bg: 'rgba(239, 68, 68, 0.1)', text: '#EF4444', label: 'Pendente', icon: <HelpCircle size={14} /> };
        }
    };

    return (
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--spacing-lg, 24px)" }}>
            {/* Header Summary */}
            <div style={{ backgroundColor: "var(--color-bg-surface, #fff)", border: "1px solid var(--color-border)", borderRadius: "12px", padding: "16px", boxShadow: "0 1px 2px 0 rgb(0 0 0 / 0.05)" }}>
                <h2 style={{ fontSize: "20px", fontFamily: "'Segoe UI', 'Helvetica Neue', Arial, sans-serif", fontWeight: 600, color: "#1e293b", margin: "0 0 16px 0" }}>
                    Conferência dos Itens da Cotação
                </h2>
                <div style={{ display: "flex", flexWrap: "wrap", gap: "12px", fontSize: "0.875rem", fontFamily: "'Helvetica', 'Segoe UI', Arial, sans-serif" }}>
                    <span style={{ display: "flex", alignItems: "center", gap: "6px", padding: "4px 10px", borderRadius: "16px", backgroundColor: "rgba(16, 185, 129, 0.1)", color: "#10B981", fontWeight: 500 }}>
                        {counts.mapped} vinculados
                    </span>
                    <span style={{ display: "flex", alignItems: "center", gap: "6px", padding: "4px 10px", borderRadius: "16px", backgroundColor: "rgba(59, 130, 246, 0.1)", color: "#3B82F6", fontWeight: 500 }}>
                        {counts.substitute} substitutos
                    </span>
                    <span style={{ display: "flex", alignItems: "center", gap: "6px", padding: "4px 10px", borderRadius: "16px", backgroundColor: "rgba(59, 130, 246, 0.1)", color: "#3B82F6", fontWeight: 500 }}>
                        {counts.extra} adicionais
                    </span>
                    <span style={{ display: "flex", alignItems: "center", gap: "6px", padding: "4px 10px", borderRadius: "16px", backgroundColor: "rgba(148, 163, 184, 0.1)", color: "#94A3B8", fontWeight: 500 }}>
                        {counts.ignored} ignorados
                    </span>
                    <span style={{ display: "flex", alignItems: "center", gap: "6px", padding: "4px 10px", borderRadius: "16px", backgroundColor: "rgba(245, 158, 11, 0.1)", color: "#F59E0B", fontWeight: 500 }}>
                        {counts.notQuoted} não cotados
                    </span>
                    <span style={{ display: "flex", alignItems: "center", gap: "6px", padding: "4px 10px", borderRadius: "16px", backgroundColor: (counts.pendingOcr > 0 || counts.pendingReq > 0) ? "rgba(245, 158, 11, 0.1)" : "rgba(148, 163, 184, 0.1)", color: (counts.pendingOcr > 0 || counts.pendingReq > 0) ? "#F59E0B" : "#94A3B8", fontWeight: 600 }}>
                        {counts.pendingOcr + counts.pendingReq} pendentes
                    </span>
                </div>
                {coveredElsewhereCount > 0 && (
                    <div style={{ marginTop: "12px", display: "flex", alignItems: "flex-start", gap: "6px", fontSize: "0.8rem", color: "#64748b" }}>
                        <Lock size={13} style={{ marginTop: "2px", flexShrink: 0 }} />
                        <span>
                            {coveredElsewhereCount} item(ns) não {coveredElsewhereCount === 1 ? 'aparece' : 'aparecem'} aqui por já {coveredElsewhereCount === 1 ? 'estar tratado' : 'estarem tratados'}:
                            {[
                                inBatchOrApprovedCount > 0 ? `${inBatchOrApprovedCount} em lote de aprovação ativo/aprovado` : null,
                                notQuotedProposedCount > 0 ? `${notQuotedProposedCount} não cotado(s) aguardando decisão do requisitante/aprovador` : null,
                                notQuotedAcceptedCount > 0 ? `${notQuotedAcceptedCount} já aceito(s) como não cotado` : null
                            ].filter(Boolean).join('; ')}.
                        </span>
                    </div>
                )}
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(450px, 1fr))", gap: "24px" }}>
                {/* Panel 1: OCR Items */}
                <div style={{ backgroundColor: "#fff", border: "1px solid var(--color-border)", borderRadius: "12px", overflow: "hidden", display: "flex", flexDirection: "column", boxShadow: "0 1px 2px 0 rgb(0 0 0 / 0.05)" }}>
                    <div style={{ backgroundColor: "#f8fafc", padding: "16px", borderBottom: "1px solid var(--color-border)" }}>
                        <h4 style={{ margin: 0, fontSize: "1rem", fontFamily: "'Segoe UI', 'Helvetica Neue', Arial, sans-serif", fontWeight: 600, color: "#1e293b" }}>Itens da Cotação do Fornecedor</h4>
                        <p style={{ margin: "4px 0 0 0", fontSize: "0.875rem", color: "#64748b" }}>O que cada item deste fornecedor representa?</p>
                    </div>
                    <div style={{ padding: "16px", display: "flex", flexDirection: "column", gap: "16px", maxHeight: "600px", overflowY: "auto" }}>
                        {realItems.map((quoteItem: any, idx: number) => {
                            const isIgnored = quoteItem.reconciliationStatus === "IGNORED";
                            // Ignoring a document line WITH value excludes it from the integrity
                            // baseline, so that exclusion needs its own justification (zero-value
                            // lines are exempt).
                            const ignoredWithValue = isIgnored && (quoteItem.totalPrice ?? 0) > 0;
                            const showMapping = quoteItem.reconciliationStatus === "MAPPED" || quoteItem.reconciliationStatus === "SUBSTITUTE";
                            const showJustification = quoteItem.reconciliationStatus === "SUBSTITUTE" || quoteItem.reconciliationStatus === "EXTRA_ITEM" || ignoredWithValue;
                            const isJustificationRequired = quoteItem.reconciliationStatus === "SUBSTITUTE" || quoteItem.reconciliationStatus === "EXTRA_ITEM" || ignoredWithValue;
                            const missingMapping = showMapping && !quoteItem.mappedRequestLineItemId;
                            const missingJustification = isJustificationRequired && (!quoteItem.reconciliationJustification || !quoteItem.reconciliationJustification.trim());

                            const baseBtnStyle = { padding: "6px 12px", fontSize: "0.875rem", fontWeight: 500, borderRadius: "4px", cursor: "pointer", display: "flex", alignItems: "center", gap: "6px" };
                            const unselectedBtnStyle = { ...baseBtnStyle, border: "1px solid #E2E8F0", backgroundColor: "#F8FAFC", color: "#64748B" };

                            return (
                                <div key={idx} style={{ 
                                    padding: "16px", 
                                    border: "1px solid var(--color-border)", 
                                    borderRadius: "8px", 
                                    backgroundColor: isIgnored ? "#f9fafb" : "#fff",
                                    opacity: isIgnored ? 0.7 : 1,
                                    transition: "all 0.2s ease",
                                    boxShadow: "0 1px 2px 0 rgb(0 0 0 / 0.05)"
                                }}>
                                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "16px" }}>
                                        <div>
                                            <div style={{ fontSize: "1rem", fontFamily: "'Helvetica', 'Segoe UI', Arial, sans-serif", fontWeight: 500, color: isIgnored ? "#94a3b8" : "#1e293b" }}>
                                                {quoteItem.description}
                                            </div>
                                            <div style={{ fontSize: "0.875rem", color: "#64748b", marginTop: "4px" }}>
                                                Qtd: {quoteItem.quantity} {quoteItem.unitId ? "un" : ""} • Preço: {quoteItem.unitPrice}
                                            </div>
                                        </div>
                                        {quoteItem.isAutoSuggested && (
                                            <span style={{ fontSize: "0.75rem", padding: "4px 8px", backgroundColor: "rgba(59, 130, 246, 0.1)", color: "#3B82F6", borderRadius: "4px", display: "flex", alignItems: "center", gap: "4px", fontWeight: 500 }}>
                                                <Search size={12} /> Sugestão automática
                                            </span>
                                        )}
                                    </div>

                                    <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                                        {/* Action Buttons */}
                                        <div style={{ display: "flex", flexWrap: "wrap", gap: "8px" }}>
                                            <button 
                                                onClick={() => handleStatusChange(idx, "MAPPED")}
                                                style={quoteItem.reconciliationStatus === "MAPPED" ? { ...baseBtnStyle, border: "1px solid #10B981", backgroundColor: "#10B981", color: "#fff" } : unselectedBtnStyle}
                                            >
                                                <LinkIcon size={14} /> Vincular
                                            </button>
                                            <button 
                                                onClick={() => handleStatusChange(idx, "SUBSTITUTE")}
                                                style={quoteItem.reconciliationStatus === "SUBSTITUTE" ? { ...baseBtnStyle, border: "1px solid #3B82F6", backgroundColor: "#3B82F6", color: "#fff" } : unselectedBtnStyle}
                                            >
                                                <RefreshCw size={14} /> Substituto
                                            </button>
                                            <button 
                                                onClick={() => handleStatusChange(idx, "EXTRA_ITEM")}
                                                style={quoteItem.reconciliationStatus === "EXTRA_ITEM" ? { ...baseBtnStyle, border: "1px solid #3B82F6", backgroundColor: "#3B82F6", color: "#fff" } : unselectedBtnStyle}
                                            >
                                                <Plus size={14} /> Item adicional
                                            </button>
                                            <button
                                                onClick={() => handleStatusChange(idx, "IGNORED")}
                                                style={quoteItem.reconciliationStatus === "IGNORED" ? { ...baseBtnStyle, border: "1px solid #94A3B8", backgroundColor: "#94A3B8", color: "#fff" } : unselectedBtnStyle}
                                            >
                                                <XCircle size={14} /> Ignorar linha
                                            </button>
                                        </div>

                                        {/* Distinct action: create a REQUESTED item from this proforma line (not EXTRA_ITEM).
                                            Shown ONLY while the request truly has NO items (recovery case for item-less requests).
                                            Uses `existingRequestItems` (any non-deleted line, regardless of quotation-eligibility),
                                            NOT `eligibleRequestItems` — so it never reappears just because existing items became
                                            ineligible. As soon as the first item exists, this disappears on every proforma line. */}
                                        {canAddRequestedItem && existingRequestItems.length === 0 && (
                                            <div style={{ borderTop: "1px dashed #E2E8F0", paddingTop: "10px" }}>
                                                <button
                                                    onClick={() => {
                                                        const di = draft.items.findIndex((i: any) => i === quoteItem);
                                                        if (di !== -1) setAddFormDraftIndex(di);
                                                    }}
                                                    style={{ ...baseBtnStyle, border: "1px solid var(--color-primary)", backgroundColor: "#fff", color: "var(--color-primary)", fontWeight: 600 }}
                                                    title="Cria um novo item solicitado no pedido a partir desta linha (para itens omitidos pelo requisitante). Diferente de 'Item adicional'."
                                                >
                                                    <FilePlus size={14} /> Adicionar como item solicitado
                                                </button>
                                                <div style={{ fontSize: "0.75rem", color: "#64748b", marginTop: "6px" }}>
                                                    Use quando este item deveria constar no pedido original, mas foi omitido. Não confundir com "Item adicional" (proposta sujeita à aprovação).
                                                </div>
                                            </div>
                                        )}

                                        {/* Validation Help Text for Missing Status */}
                                        {!quoteItem.reconciliationStatus && (
                                            <span style={{ fontSize: "0.875rem", color: "#EF4444", display: "flex", alignItems: "center", gap: "6px", fontWeight: 500 }}>
                                                <AlertCircle size={14} /> Selecione uma ação para todos os itens da cotação.
                                            </span>
                                        )}

                                        {showMapping && (
                                            <div style={{ backgroundColor: "#f9fafb", padding: "12px", borderRadius: "4px", border: missingMapping ? "1px solid #EF4444" : "1px solid transparent" }}>
                                                <label style={{ display: "block", fontSize: "0.875rem", fontWeight: 500, color: "#1e293b", marginBottom: "8px" }}>Selecione o item solicitado correspondente:</label>
                                                <select 
                                                    style={{ width: "100%", fontSize: "0.875rem", padding: "10px", border: "1px solid #E2E8F0", borderRadius: "4px", backgroundColor: "#fff", color: "#1e293b", outlineColor: "#004C90" }}
                                                    value={quoteItem.mappedRequestLineItemId || ""}
                                                    onChange={(e) => handleMappingChange(idx, e.target.value || null)}
                                                >
                                                    <option value="">-- Selecione um item --</option>
                                                    {eligibleRequestItems.filter((reqItem: any) =>
                                                        reqItem.id === quoteItem.mappedRequestLineItemId || !mappedIds.has(reqItem.id)
                                                    ).map((reqItem: any) => (
                                                        <option key={reqItem.id} value={reqItem.id}>
                                                            {reqItem.lineNumber} - {reqItem.description} (Qtd: {reqItem.quantity})
                                                        </option>
                                                    ))}
                                                </select>
                                                {missingMapping && <span style={{ fontSize: "0.75rem", color: "#EF4444", marginTop: "6px", display: "flex", alignItems: "center", gap: "4px" }}><AlertCircle size={12} /> Mapeamento obrigatório para este status.</span>}
                                            </div>
                                        )}

                                        {showJustification && (
                                            <div>
                                                <label style={{ display: "block", fontSize: "0.875rem", fontWeight: "var(--font-weight-medium, 500)", color: "var(--color-text-main)", marginBottom: "8px" }}>Justificativa {isJustificationRequired ? "*" : "(Opcional)"}</label>
                                                <input 
                                                    type="text"
                                                    style={{ width: "100%", fontSize: "0.875rem", padding: "10px", border: missingJustification ? "1px solid var(--color-status-red)" : "1px solid var(--color-border)", borderRadius: "var(--radius-sm, 4px)", fontFamily: "var(--font-family-body)" }}
                                                    placeholder="Informe o motivo..."
                                                    value={quoteItem.reconciliationJustification || ""}
                                                    onChange={(e) => handleJustificationChange(idx, e.target.value)}
                                                />
                                                {missingJustification && <span style={{ fontSize: "0.75rem", color: "var(--color-status-red, #dc2626)", marginTop: "6px", display: "flex", alignItems: "center", gap: "4px" }}><AlertCircle size={12} /> Informe a justificativa para este item.</span>}
                                            </div>
                                        )}
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                </div>

                {/* Panel 2: Request Items Coverage */}
                <div style={{ backgroundColor: "#fff", border: "1px solid var(--color-border)", borderRadius: "12px", overflow: "hidden", display: "flex", flexDirection: "column", boxShadow: "0 1px 2px 0 rgb(0 0 0 / 0.05)" }}>
                    <div style={{ backgroundColor: "#f8fafc", padding: "16px", borderBottom: "1px solid var(--color-border)" }}>
                        <h4 style={{ margin: 0, fontSize: "1rem", fontFamily: "'Segoe UI', 'Helvetica Neue', Arial, sans-serif", fontWeight: 600, color: "#1e293b" }}>Itens Solicitados</h4>
                        <p style={{ margin: "4px 0 0 0", fontSize: "0.875rem", color: "#64748b" }}>Cobertura da solicitação original</p>
                    </div>
                    <div style={{ padding: "16px", display: "flex", flexDirection: "column", gap: "16px", maxHeight: "600px", overflowY: "auto" }}>
                        {eligibleRequestItems.map((reqItem: any, idx: number) => {
                            const notQuotedItem = draft.items.find((i: any) => i.reconciliationStatus === "NOT_QUOTED" && i.mappedRequestLineItemId === reqItem.id);
                            const isNotQuoted = !!notQuotedItem;
                            const mappingInfo = draft.items.find((i: any) => (i.reconciliationStatus === "MAPPED" || i.reconciliationStatus === "SUBSTITUTE") && i.mappedRequestLineItemId === reqItem.id);

                            let currentStatus = "PENDING";
                            if (isNotQuoted) currentStatus = "NOT_QUOTED";
                            else if (mappingInfo) currentStatus = mappingInfo.reconciliationStatus || 'MAPPED';

                            const style = getBadgeStyle(currentStatus);

                            return (
                                <div key={idx} style={{ padding: "16px", border: "1px solid var(--color-border)", borderRadius: "8px", display: "flex", flexWrap: "wrap", justifyContent: "space-between", alignItems: "center", backgroundColor: currentStatus === "PENDING" ? "#fff" : "#f9fafb", borderLeft: currentStatus === "PENDING" ? "4px solid #EF4444" : "1px solid var(--color-border)", boxShadow: "0 1px 2px 0 rgb(0 0 0 / 0.05)" }}>
                                    <div style={{ flex: 1, paddingRight: "16px" }}>
                                        <div style={{ fontSize: "1rem", fontWeight: 500, fontFamily: "'Helvetica', 'Segoe UI', Arial, sans-serif", color: "#1e293b" }}>{reqItem.lineNumber} - {reqItem.description}</div>
                                        <div style={{ fontSize: "0.875rem", color: "#64748b", marginTop: "4px" }}>
                                            Qtd Solicitada: {reqItem.quantity} {reqItem.unitId ? "un" : ""}
                                        </div>
                                        
                                        {mappingInfo && (
                                            <div style={{ marginTop: "8px", fontSize: "0.875rem", color: "#1e293b", display: "flex", alignItems: "center", gap: "6px", backgroundColor: "#fff", padding: "8px", borderRadius: "4px", border: "1px dashed #E2E8F0" }}>
                                                <LinkIcon size={14} color="#94A3B8" />
                                                <span style={{ color: "#94A3B8" }}>Vinculado a:</span> {mappingInfo.description}
                                            </div>
                                        )}
                                        {currentStatus === "PENDING" && (
                                            <div style={{ marginTop: "8px", fontSize: "0.875rem", color: "#EF4444", display: "flex", alignItems: "center", gap: "6px", fontWeight: 500 }}>
                                                <AlertCircle size={14} /> Marque os itens solicitados não cotados antes de avançar.
                                            </div>
                                        )}
                                    </div>
                                    <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: "12px" }}>
                                        <span style={{ fontSize: "0.75rem", padding: "6px 12px", borderRadius: "16px", backgroundColor: style.bg, color: style.text, fontWeight: 600, whiteSpace: "nowrap", display: "flex", alignItems: "center", gap: "6px" }}>
                                            {style.icon} {style.label}
                                        </span>
                                        {currentStatus === "PENDING" && (
                                            <button 
                                                onClick={() => toggleNotQuotedPlaceholder(reqItem)}
                                                style={{ padding: "8px 16px", fontSize: "0.875rem", backgroundColor: "#fff", border: "1px solid #E2E8F0", borderRadius: "4px", cursor: "pointer", color: "#F59E0B", fontWeight: 500, transition: "all 0.2s", display: "flex", alignItems: "center", gap: "6px" }}
                                            >
                                                Não cotado nesta cotação
                                            </button>
                                        )}
                                        {currentStatus === "NOT_QUOTED" && (
                                            <button
                                                onClick={() => toggleNotQuotedPlaceholder(reqItem)}
                                                style={{ padding: "6px 12px", fontSize: "0.875rem", backgroundColor: "#fff", border: "1px solid #E2E8F0", borderRadius: "4px", cursor: "pointer", color: "#64748B", fontWeight: 500 }}
                                            >
                                                Desfazer
                                            </button>
                                        )}
                                    </div>
                                    {isNotQuoted && (
                                        <div style={{ flexBasis: "100%", marginTop: "12px" }}>
                                            <label style={{ display: "block", fontSize: "0.875rem", fontWeight: 500, color: "#1e293b", marginBottom: "8px" }}>
                                                Observação (opcional)
                                            </label>
                                            <textarea
                                                value={notQuotedItem.reconciliationJustification || ""}
                                                onChange={(e) => handleNotQuotedJustificationChange(reqItem.id, e.target.value)}
                                                placeholder="Ex: fornecedor informou não trabalhar com este item..."
                                                rows={2}
                                                style={{
                                                    width: "100%",
                                                    fontSize: "0.875rem",
                                                    padding: "10px",
                                                    border: "1px solid #E2E8F0",
                                                    borderRadius: "4px",
                                                    fontFamily: "inherit",
                                                    resize: "vertical",
                                                    boxSizing: "border-box"
                                                }}
                                            />
                                            <span style={{ fontSize: "0.75rem", color: "#64748b", marginTop: "6px", display: "block" }}>
                                                Marca apenas que este fornecedor não cotou o item — ele continua pendente no pedido. Para removê-lo do processo, use "Encerrar sem cotação" na tabela de itens do pedido.
                                            </span>
                                        </div>
                                    )}
                                </div>
                            );
                        })}
                    </div>
                </div>
            </div>

            {addFormDraftIndex !== null && draft.items[addFormDraftIndex] && (
                <AddRequestedItemModal
                    requestId={req.requestId}
                    sourceProformaAttachmentId={draft.proformaAttachmentId ?? null}
                    units={units}
                    initial={{
                        description: draft.items[addFormDraftIndex].description || '',
                        quantity: draft.items[addFormDraftIndex].quantity || 1,
                        unitId: draft.items[addFormDraftIndex].unitId ?? null,
                        itemCatalogId: (draft.items[addFormDraftIndex] as any).itemCatalogId ?? null,
                    }}
                    onClose={() => setAddFormDraftIndex(null)}
                    onResolved={(item) => handleRequestedItemResolved(addFormDraftIndex, item)}
                />
            )}
        </div>
    );
};
