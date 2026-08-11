import { useState, useEffect, useMemo } from 'react';
import {
    User, Building2, Factory,
    Calendar, Landmark, Download,
    Paperclip, AlertCircle, List,
    MessageSquare, Users, History as HistoryIcon, DollarSign,
    Target, TrendingUp, ArrowRightLeft, AlertTriangle, ShieldCheck,
    BookOpen, X, Compass, Layers
} from 'lucide-react';
import { RequestDetailsDto, ApprovalIntelligenceDto, FinalApprovalLotView } from '../../types';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { Tooltip } from '../../components/ui/Tooltip';
import { api } from '../../lib/api';
import { formatDate, formatCurrencyAO } from '../../lib/utils';
import { motion } from 'framer-motion';
import { useGuidedTourContext } from '../../features/guided-tour/GuidedTourProvider';
import type { TourId } from '../../features/guided-tour/guidedTourTypes';
import { ApprovalWizardModal } from './ApprovalWizardModal';
import { ItemAssignment } from './WizardStepAllocation';
import { AllocationReassignmentDto } from './WizardStepBudget';


// Decision specific components
import { DecisionHeader } from './components/DecisionHeader';
import { DecisionSummaryGrid } from './components/DecisionSummaryGrid';
import { DecisionSection } from './components/DecisionSection';
import { DecisionQuotationCard } from './components/DecisionQuotationCard';
import { DecisionTimeline } from './components/DecisionTimeline';
import { DecisionInsightsPanel } from './components/DecisionInsightsPanel';
import { DecisionFinancialTrendLine } from './components/DecisionFinancialTrendLine';
import { ItemAwardMatrix } from './components/ItemAwardMatrix';
import { AwardSummary } from './components/AwardSummary';

// --- Constants ---

const ATTACHMENT_TYPE_LABELS: Record<string, string> = {
    'PROFORMA': 'Proforma',
    'PO': 'P.O (Ordem de Compra)',
    'PAYMENT_SCHEDULE': 'Cronograma de Pagamento',
    'PAYMENT_PROOF': 'Comprovante de Pagamento',
    'GENERAL': 'Documento Geral',
    'INVOICE': 'Fatura',
    'RECEIPT': 'Recibo',
};

// Mirrors the labels used in Buyer/BuyerItemsList.tsx for ApprovalBatch.Status,
// so the same batch status reads identically for Buyer and Approver.
const BATCH_STATUS_LABELS: Record<string, string> = {
    'WAITING_AREA_APPROVAL': 'Aguardando Aprovação da Área',
    'AREA_ADJUSTMENT': 'Ajuste da Área Requerido',
    'WAITING_FINAL_APPROVAL': 'Aguardando Aprovação Final',
    'FINAL_ADJUSTMENT': 'Ajuste Final Requerido',
    'APPROVED': 'Aprovado',
    'REJECTED': 'Rejeitado',
    'CANCELLED': 'Cancelado',
};

// --- Interfaces ---

export interface ApprovalDetailPanelProps {
    data: RequestDetailsDto;
    approvalStage: 'AREA' | 'FINAL';
    /** Explicit actionable batch id carried from the clicked queue card. When set, the drawer selects
     *  EXACTLY this batch (never re-guesses by status), guaranteeing card ⇄ drawer parity for requests
     *  with multiple simultaneous batches. Null for PAYMENT / legacy request-level actions. */
    activeBatchId?: string | null;
    isAreaApprover: boolean;
    isFinalApprover: boolean;
    onActionCompleted: (successMessage: string) => void;
    onClose: () => void;
    onDataRefresh: () => Promise<void>;
    isDrawerContext?: boolean;
    onDrillDown?: (item: any) => void;
    // Navigation props
    onNext?: () => void;
    onPrev?: () => void;
    currentIndex?: number;
    totalCount?: number;
}

// --- Component ---

export function ApprovalDetailPanel({
    data,
    approvalStage,
    activeBatchId,
    isAreaApprover,
    onActionCompleted,
    onClose,
    onDataRefresh,
    onDrillDown,
    onNext,
    onPrev,
    currentIndex,
    totalCount
}: ApprovalDetailPanelProps) {

    const stageBatchStatus = approvalStage === 'AREA' ? 'WAITING_AREA_APPROVAL' : 'WAITING_FINAL_APPROVAL';

    // Select EXACTLY the batch the card carried (identity parity). Only fall back to a status-based
    // match when no id was carried (legacy callers / request-level PAYMENT actions with no batch).
    const activeBatch = activeBatchId
        ? data.approvalBatches?.find((b: any) => b.id === activeBatchId)
        : data.approvalBatches?.find((b: any) => b.status === stageBatchStatus);

    // Invariant: a card that passed a batch id MUST resolve that exact batch under this stage.
    // A mismatch means the card and drawer disagreed — surface a diagnostic rather than silently
    // opening a different lot (the original REQ-132 defect).
    const batchIdentityError = activeBatchId != null && (!activeBatch || activeBatch.status !== stageBatchStatus)
        ? `Inconsistência de lote: o cartão indicava o lote ${activeBatchId}, mas o painel não o encontrou nesta etapa (${approvalStage}). Recarregue a fila.`
        : null;

    const activeItems = activeBatch
        ? (data.lineItems || []).filter(item =>
            activeBatch.items?.some((bi: any) => bi.requestLineItemId === item.id)
          )
        : (data.lineItems || []);

    // Batch-scoped view of the request for the Approval Wizard. The wizard and
    // every step inside it (Allocation, Selection, Review, Overview) derive their
    // item lists straight from request.lineItems, so when an active batch exists
    // we hand it a request whose lineItems are ONLY the batch's items — items
    // outside the batch (pending / NOT_QUOTED_PROPOSED) must neither appear nor
    // block. We also stamp each item with the buyer-selected winner from
    // ApprovalBatchItem (the source of truth in the batch model) so awards come
    // pre-filled. Memoized because the wizard's init effect keys on the request
    // object identity — a fresh object every render would wipe its state.
    const wizardRequest = useMemo(() => {
        if (!activeBatch) return data;
        const batchLineItems = (data.lineItems || [])
            .filter(item => activeBatch.items?.some((bi: any) => bi.requestLineItemId === item.id))
            .map(item => {
                const batchItem = activeBatch.items?.find((bi: any) => bi.requestLineItemId === item.id);
                return batchItem?.selectedQuotationItemId
                    ? { ...item, selectedQuotationItemId: batchItem.selectedQuotationItemId }
                    : item;
            });
        return { ...data, lineItems: batchLineItems };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [data, approvalStage]);

    // Effective amount of the active batch — the header must show the batch
    // value, not the request's EstimatedTotalAmount (0 for quotation requests
    // created without prices). Prefers the immutable final-approval snapshot;
    // before that, sums the buyer-selected winning quotation items (their
    // lineTotals are available via data.quotations).
    const activeBatchTotal = useMemo(() => {
        if (!activeBatch) return null;
        if (activeBatch.approvedTotalAmount && activeBatch.approvedTotalAmount > 0) {
            return activeBatch.approvedTotalAmount as number;
        }
        const lineTotalByQuotationItemId = new Map<string, number>();
        (data.quotations || []).forEach(q =>
            (q.items || []).forEach(qi => lineTotalByQuotationItemId.set(qi.id, qi.lineTotal || 0))
        );
        const sum = (activeBatch.items || []).reduce(
            (acc: number, bi: any) => acc + (lineTotalByQuotationItemId.get(bi.selectedQuotationItemId) || 0),
            0
        );
        return sum > 0 ? sum : null;
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [data, approvalStage]);

    // Normalized, lot-aware Final Approval view model — backend-computed and authoritative for
    // item line totals, lot total, supplier resolution and included-vs-ignored separation. All
    // Final Approval displays read from here rather than the request-level estimate/supplier,
    // which lag behind the batch (0 total, blank supplier for quotation requests).
    const lotView: FinalApprovalLotView | null = (activeBatch as any)?.lotView ?? null;

    // Fast lookup of a lot item by its RequestLineItemId — feeds the per-item total resolution.
    const lotItemByLineId = useMemo(() => {
        const map = new Map<string, FinalApprovalLotView['includedItems'][number]>();
        (lotView?.includedItems || []).forEach(li => map.set(li.requestLineItemId, li));
        return map;
    }, [lotView]);

    // The set of quotation-item ids that belong to the current lot (buyer-selected winners) and,
    // separately, the IGNORED lines with their audit reason — used to make "Cotações Salvas"
    // lot-aware (included vs. not-included-in-this-lot) instead of mixing every quotation row.
    const lotIncludedQuotationItemIds = useMemo(() => {
        const set = new Set<string>();
        (activeBatch?.items || []).forEach((bi: any) => {
            if (bi.selectedQuotationItemId) set.add(bi.selectedQuotationItemId);
        });
        return set;
    }, [activeBatch]);

    const lotIgnoredReasonByQuotationItemId = useMemo(() => {
        const map = new Map<string, string | null | undefined>();
        (lotView?.ignoredLines || []).forEach(line =>
            map.set(line.quotationItemId, line.reconciliationJustification)
        );
        return map;
    }, [lotView]);

    // Resolve the authoritative displayed total for one line item. For a lot item we use its
    // selected-quotation line total; a null there means "unresolved winner" — we surface a warning
    // rather than silently rendering 0. Items with no lot entry (no batch / outside the lot) keep
    // their own value, preserving legitimate zeros for genuinely unpriced requests.
    const resolveItemTotal = (item: any): { text: string; warning: boolean } => {
        const lotItem = lotItemByLineId.get(item.id);
        if (lotItem) {
            if (lotItem.lineTotal == null) {
                return { text: 'Valor indisponível', warning: true };
            }
            return { text: formatCurrencyAO(lotItem.lineTotal), warning: false };
        }
        return { text: formatCurrencyAO(item.totalAmount), warning: false };
    };

    // Guided tour context
    const { startTour } = useGuidedTourContext();
    const drawerTourId: TourId = approvalStage === 'FINAL' ? 'drawer-approval-final' : 'drawer-approval-area';

    // Approval modal state
    const [showApprovalModal, setShowApprovalModal] = useState<{
        show: boolean;
        type: ApprovalActionType;
    }>({ show: false, type: null });
    const [approvalComment, setApprovalComment] = useState('');
    const [approvalProcessing, setApprovalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    
    // Internal processing state for winner selection
    const [quotationProcessingId, setQuotationProcessingId] = useState<string | null>(null);

    // UX Tracking
    const [highlightSection, setHighlightSection] = useState(false);
    const [highlightFields, setHighlightFields] = useState(false);
    const [highlightQuotationSection, setHighlightQuotationSection] = useState(false);
    const [expandedQuotationId, setExpandedQuotationId] = useState<string | null>(null);

    // Phase 3A: Intelligence
    const [intelligence, setIntelligence] = useState<ApprovalIntelligenceDto | null>(null);
    const [loadingIntelligence, setLoadingIntelligence] = useState(false);
    const [showHelp, setShowHelp] = useState(false);

    // Business Control: Cost Center & Plant Selection (DEC-085/DEC-099/DEC-103)
    const [costCenters, setCostCenters] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [itemAssignments, setItemAssignments] = useState<Record<string, { plantId: number | null, costCenterId: number | null }>>({});
    const [itemAwards, setItemAwards] = useState<Record<string, string>>({});
    const [activeInsightItemId, setActiveInsightItemId] = useState<string | null>(null);
    const [activeAssignmentItemId, setActiveAssignmentItemId] = useState<string | null>(null);
    const [viewMode, setViewMode] = useState<'CARDS' | 'LIST'>(
        (activeItems && activeItems.length > 5) ? 'LIST' : 'CARDS'
    );
    const [insightSearchQ, setInsightSearchQ] = useState('');
    const [isWizardOpen, setIsWizardOpen] = useState(false);

    const isQuotation = data.requestTypeCode === 'QUOTATION';
    const isPayment = data.requestTypeCode === 'PAYMENT';

    const hasApprovalBatches = Array.isArray(data.approvalBatches) && data.approvalBatches.length > 0;
    const hasSelectedQuotationItem = (data.lineItems || []).some(
        (item: any) => Boolean(item.selectedQuotationItemId)
    );
    const cameFromLegacyCompleteQuotation = (data.statusHistory || []).some(
        (history: any) => history.actionTaken === 'COMPLETE_QUOTATION'
    );

    const isLegacyQuotationApproval =
        isQuotation &&
        data.statusCode === 'WAITING_AREA_APPROVAL' &&
        !hasApprovalBatches &&
        !hasSelectedQuotationItem &&
        cameFromLegacyCompleteQuotation;

    const showAreaAdjustment =
        !isPayment && (Boolean(activeBatch) || isLegacyQuotationApproval);

    const showAreaReject =
        Boolean(activeBatch) || isPayment || isLegacyQuotationApproval;

    // Partial/batch approval means the request's own aggregate status (data.statusCode)
    // can lag behind reality — e.g. it can still read WAITING_QUOTATION while one of its
    // batches is already WAITING_AREA_APPROVAL, because other line items are still
    // unresolved (pending quotation or a not-quoted proposal awaiting decision). The
    // drawer must treat "area approval stage" as true whenever there IS an active batch
    // to review, not only when the whole request's status says so.
    const isAreaApprovalStage = approvalStage === 'AREA' && (
        !!activeBatch ||
        data.statusCode === 'WAITING_AREA_APPROVAL' ||
        data.statusCode === 'WAITING_COST_CENTER' ||
        isPayment
    );
    const isFinalApprovalStage = data.statusCode === 'WAITING_FINAL_APPROVAL';

    useEffect(() => {
        if (data.id) {
            fetchIntelligence();
        }
    }, [data.id]);

    useEffect(() => {
        if (approvalStage === 'AREA' && data.companyId) {
            // Fetch all CCs and Plants for the company to support item-level re-assignment
            fetchCostCenters(data.companyId);
            fetchPlants(data.companyId);
        }
    }, [approvalStage, data.companyId]);

    // Initialize item assignments and awards from existing data
    useEffect(() => {
        if (activeItems) {
            const initialMap: Record<string, { plantId: number | null, costCenterId: number | null }> = {};
            const initialAwards: Record<string, string> = {};
            activeItems.forEach(item => {
                initialMap[item.id] = {
                    plantId: item.plantId || null,
                    costCenterId: item.costCenterId || null
                };
                if (item.selectedQuotationItemId) {
                    initialAwards[item.id] = item.selectedQuotationItemId;
                }
            });
            setItemAssignments(initialMap);
            setItemAwards(initialAwards);
            if (activeItems.length > 0) {
                if (!activeInsightItemId) setActiveInsightItemId(activeItems[0].id);
                if (!activeAssignmentItemId) {
                    const firstPending = activeItems.find(i => !initialMap[i.id].plantId || !initialMap[i.id].costCenterId);
                    setActiveAssignmentItemId(firstPending ? firstPending.id : activeItems[0].id);
                }
            }
        }
    }, [data.id, activeItems]);

    const fetchIntelligence = async () => {
        setLoadingIntelligence(true);
        try {
            const intel = await api.approvals.getIntelligence(data.id);
            setIntelligence(intel);
        } catch (err) {
            console.error('Failed to fetch approval intelligence:', err);
        } finally {
            setLoadingIntelligence(false);
        }
    };

    const fetchCostCenters = async (companyId: number) => {
        try {
            const list = await api.lookups.getCostCenters(false, undefined, companyId);
            setCostCenters(list);
        } catch (err) {
            console.error('Failed to fetch cost centers:', err);
        }
    };

    const fetchPlants = async (companyId: number) => {
        try {
            const list = await api.lookups.getPlants(companyId, false);
            setPlants(list);
        } catch (err) {
            console.error('Failed to fetch plants:', err);
        }
    };

    // --- Computed ---
    const selectedQuotation = data.quotations?.find(q => q.isSelected);

    // DEC-099/DEC-103: Cost Center & Plant Validation Logic
    const itemsMissingPlant = activeItems.filter(item => !itemAssignments[item.id]?.plantId);
    const itemsMissingCC = activeItems.filter(item => !itemAssignments[item.id]?.costCenterId);
    
    const countMissingPlant = itemsMissingPlant.length;
    const countMissingCC = itemsMissingCC.length;
    
    const allAssigned = activeItems.length > 0 && countMissingPlant === 0 && countMissingCC === 0;
    
    // A simplified helper to find if all items currently share the same assignment (for summary display)
    const currentPairs = Object.values(itemAssignments).filter(v => v.plantId && v.costCenterId);
    const uniquePlants = Array.from(new Set(currentPairs.map(p => p.plantId)));
    const uniqueCCs = Array.from(new Set(currentPairs.map(p => p.costCenterId)));
    
    const isUnifiedAssignment = activeItems.length > 0 && allAssigned && uniquePlants.length === 1 && uniqueCCs.length === 1;
    const unifiedCC = isUnifiedAssignment ? costCenters.find(cc => cc.id === uniqueCCs[0]) : null;

    const handleBulkFill = (itemId: string) => {
        const source = itemAssignments[itemId];
        if (!source || !source.plantId || !source.costCenterId) return;

        const newMap = { ...itemAssignments };
        activeItems.forEach(item => {
            // Safer Helper: Apply only to items that are still pending
            if (!newMap[item.id]?.plantId || !newMap[item.id]?.costCenterId) {
                newMap[item.id] = {
                    plantId: source.plantId,
                    costCenterId: source.costCenterId
                };
            }
        });
        setItemAssignments(newMap);
    };

    const canBulkFill = (itemId: string): boolean => {
        const source = itemAssignments[itemId];
        if (!source || !source.plantId || !source.costCenterId) return false;

        // True if there is at least one OTHER pending item
        return activeItems.some(item => item.id !== itemId && (!itemAssignments[item.id]?.plantId || !itemAssignments[item.id]?.costCenterId));
    };

    // Winner selection is handled via ItemAwardMatrix for AREA stage. Legacy card-level selection is disabled.
    const canSelectWinner = false;

    // const allItemsAwarded = activeItems.length > 0 && activeItems.every(i => !!itemAwards[i.id] || !!i.selectedQuotationItemId);

    // Approve is blocked if winner not selected for every item (Quotation, Area Approval) OR any Item Assignment is missing (Area Approval)
    // const isApproveBlocked = (isQuotation && approvalStage === 'AREA' && isAreaApprovalStage && !allItemsAwarded) || (approvalStage === 'AREA' && isAreaApprovalStage && !allAssigned);

    // REQUEST_ADJUSTMENT is hidden for PAYMENT requests
    const showAdjustmentAction = !isPayment;

    // Find lowest amount for visual highlights
    const lowestByCurrency: Record<string, number> = {};
    if (isQuotation && data.quotations) {
        data.quotations.forEach(q => {
            if (!lowestByCurrency[q.currency] || q.totalAmount < lowestByCurrency[q.currency]) {
                lowestByCurrency[q.currency] = q.totalAmount;
            }
        });
    }

    // Intelligence summary flags
    const intelItemsWithHistory = intelligence?.items?.filter(i => i.hasHistory) || [];
    // Show warning only when at least one item has price strictly above historical average
    const itemsAboveAvg = intelItemsWithHistory.filter(i => i.currentUnitPrice > (i.averageHistoricalPrice || 0));
    const hasItemAboveAvg = itemsAboveAvg.length > 0;

    // --- Handlers ---
    // (handleQuotationWarningClick / handleAllocationWarningClick were removed
    // along with the operational warning banners — the area drawer is
    // informative only; allocation/selection issues surface inside the Wizard.)

    const handleSelectWinner = async (quotationId: string) => {
        setQuotationProcessingId(quotationId);
        try {
            await api.requests.selectQuotation(data.id, quotationId);
            await onDataRefresh();
        } catch (err: any) {
            console.error('Failed to select winner:', err);
        } finally {
            setQuotationProcessingId(null);
        }
    };

    const handleDownloadAttachment = async (attachmentId: string, fileName: string) => {
        try {
            await api.attachments.download(attachmentId, fileName);
        } catch (err: any) {
            console.error('Failed to download attachment:', err);
        }
    };

    const handleWizardSubmit = async (
        action: ApprovalActionType,
        awards: Record<string, string>,
        assignments: Record<string, ItemAssignment>,
        comment: string,
        budgetJustification?: string,
        reassignments?: AllocationReassignmentDto[],
        allocations?: Record<string, any[]>,
        extraItemDecisions?: Record<string, { decision: 'APPROVE' | 'REJECT' | 'ADJUST' | null; comment: string }>,
        selections?: { approvalBatchItemId: string; selectedCandidateId: string; winnerSelectionJustification?: string }[]
    ): Promise<boolean> => {
        setApprovalProcessing(true);
        try {
            let result;
            const isArea = isAreaApprovalStage;

            if (action === 'APPROVE') {
                result = activeBatch
                    ? (isArea
                        ? await api.requests.approveBatchArea(data.id, activeBatch.id, comment, awards, assignments, budgetJustification, reassignments, allocations, extraItemDecisions, selections)
                        : await api.requests.approveBatchFinal(data.id, activeBatch.id, comment))
                    : (isArea
                        ? await api.requests.approveArea(data.id, comment, awards, assignments, budgetJustification, reassignments, allocations, extraItemDecisions)
                        : await api.requests.approveFinal(data.id, comment));
            } else if (action === 'REJECT') {
                result = activeBatch
                    ? (isArea
                        ? await api.requests.rejectBatchArea(data.id, activeBatch.id, comment)
                        : await api.requests.rejectBatchFinal(data.id, activeBatch.id, comment))
                    : (isArea
                        ? await api.requests.rejectArea(data.id, comment)
                        : await api.requests.rejectFinal(data.id, comment));
            } else if (action === 'REQUEST_ADJUSTMENT') {
                result = activeBatch
                    ? (isArea
                        ? await api.requests.requestAdjustmentBatchArea(data.id, activeBatch.id, comment)
                        : await api.requests.requestAdjustmentBatchFinal(data.id, activeBatch.id, comment))
                    : (isArea
                        ? await api.requests.requestAdjustmentArea(data.id, comment)
                        : await api.requests.requestAdjustmentFinal(data.id, comment));
            } else {
                throw new Error('Ação inválida.');
            }

            setIsWizardOpen(false);
            onActionCompleted(result.message || 'Ação concluída com sucesso.');
            return true;
        } catch (err: any) {
            // Structured concurrency conflict (another approver/operation changed the request):
            // close the wizard and refresh the queue/detail instead of a technical alert. The
            // user must review the fresh data — never auto-retry the approval.
            const isConcurrencyConflict = err?.status === 409 &&
                (err?.errorCode === 'APPROVAL_CONCURRENCY_CONFLICT' || err?.details?.code === 'APPROVAL_CONCURRENCY_CONFLICT');
            if (isConcurrencyConflict) {
                setIsWizardOpen(false);
                setShowApprovalModal({ show: false, type: null });
                onActionCompleted('O pedido foi alterado por outra operação. Os dados foram atualizados — verifique e tente novamente.');
                return false;
            }

            let errorMsg = err.message || 'Não foi possível concluir a ação. Tente novamente.';
            if (err.fieldErrors) {
                const details = Object.entries(err.fieldErrors as Record<string, string[]>)
                    .map(([, msgs]) => msgs.join(', '))
                    .filter(Boolean);
                if (details.length > 0) {
                    errorMsg = details.join('. ');
                }
            }
            alert(errorMsg);
            return false;
        } finally {
            setApprovalProcessing(false);
        }
    };

    // --- Render Helpers ---

    // Supplier header: on Final Approval prefer the lot's resolved supplier (from the batch's
    // winning quotation items), never the obsolete request-level SupplierName (blank for quotation
    // requests). Fall back to the request supplier only when there is no lot resolution at all, so
    // a populated supplier group is never silently reduced to "---".
    const supplierSummaryLabel = lotView?.supplierLabel ? lotView.supplierHeading : 'Fornecedor Atual';
    const supplierSummaryValue = lotView?.supplierLabel ?? data.supplierName;

    // Plant header names the REQUESTING plant. The financial-allocation plant (per item) can
    // legitimately differ and is labeled separately in the items section, so we disambiguate here.
    const plantSummaryLabel = isFinalApprovalStage ? 'Planta Solicitante' : 'Planta';

    const summaryItems = [
        { label: 'Solicitante', value: data.requesterName, icon: <User size={12} /> },
        { label: 'Departamento', value: data.departmentName, icon: <Building2 size={12} /> },
        { label: 'Empresa', value: data.companyName },
        { label: plantSummaryLabel, value: data.plantName, icon: <Factory size={12} /> },
        { label: 'Necessário Até', value: data.needByDateUtc ? formatDate(data.needByDateUtc) : '---', icon: <Calendar size={12} /> },
        { label: supplierSummaryLabel, value: supplierSummaryValue },
        { 
            label: 'Atribuição Financeira', 
            value: (approvalStage === 'AREA' && isAreaApprovalStage) 
                ? (isUnifiedAssignment && unifiedCC ? `[${unifiedCC.code}] ${unifiedCC.name}` : 'Múltiplos / Pendente')
                : data.costCenterCode || (activeItems?.[0]?.costCenterCode ? `[${activeItems[0].costCenterCode}]` : '---'), 
            icon: <Landmark size={12} /> 
        },
        { 
            label: 'Grau Necessidade', 
            value: <span style={{ color: '#0a2540' }}>{data.needLevelName}</span> 
        }
    ];

    return (
        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.3 }}
            style={{ 
                backgroundColor: 'var(--color-bg-page)', 
                minHeight: '100%', display: 'flex', flexDirection: 'column', position: 'relative'
            }}
        >
            {/* Batch-identity diagnostic — the card carried a batch the drawer could not resolve. */}
            {batchIdentityError && (
                <div style={{ margin: '12px 16px 0', padding: '12px 16px', borderRadius: 8, backgroundColor: '#fef2f2', border: '1px solid #fecaca', color: '#b91c1c', fontWeight: 700, fontSize: '0.8rem', display: 'flex', alignItems: 'center', gap: 8 }}>
                    <AlertTriangle size={16} style={{ flexShrink: 0 }} />
                    <span>{batchIdentityError}</span>
                </div>
            )}

            {/* 1. DECISION HEADER (Top Navigation & Hero) */}
            <div data-tour="approval-drawer-header">
                <DecisionHeader 
                    requestNumber={data.requestNumber || ''}
                    requestTypeCode={data.requestTypeCode || ''}
                    statusCode={data.statusCode || ''}
                    statusName={data.statusName || ''}
                    statusBadgeColor={data.statusBadgeColor || ''}
                    totalAmount={lotView?.lotTotal ?? activeBatchTotal ?? data.estimatedTotalAmount}
                    totalAmountOverrideLabel={(() => {
                        // Candidate model: before the Area winner decision there IS no batch
                        // commercial truth — never fall back to 0 or the request estimate.
                        const candidateItems = (activeBatch?.items || []).filter((bi: any) => (bi.candidates?.length ?? 0) > 0);
                        const pending = candidateItems.length > 0 && candidateItems.some((bi: any) => !bi.selectedCandidateId);
                        return pending ? 'A definir pelo Aprovador de Área' : null;
                    })()}
                    currencyCode={data.currencyCode || ''}
                    approvalStage={approvalStage}
                    onClose={onClose}
                    onOpenRequest={() => window.open(`/requests/${data.id}`, '_blank')}
                    onNext={onNext}
                    onPrev={onPrev}
                    currentIndex={currentIndex}
                    totalCount={totalCount}
                />
            </div>

            <div style={{ maxWidth: '80rem', margin: '0 auto', width: '100%', padding: '16px 24px 96px 24px' }}>

                {/* --- PARTIAL APPROVAL BANNER --- */}
                {activeBatch && (
                    <div data-tour="approval-drawer-batch-banner" style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '16px',
                        padding: '16px 20px',
                        backgroundColor: '#eff6ff',
                        border: '1px solid #bfdbfe',
                        borderRadius: '12px',
                        marginBottom: '16px',
                        boxShadow: 'var(--shadow-sm)'
                    }}>
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            width: '36px',
                            height: '36px',
                            borderRadius: '50%',
                            backgroundColor: '#dbeafe',
                            color: '#1d4ed8'
                        }}>
                            <Layers size={18} />
                        </div>
                        <div>
                            <h4 style={{ margin: 0, fontSize: '0.85rem', fontWeight: 800, color: '#1e3a8a', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                Lote de Aprovação Parcial (Lote #{activeBatch.batchNumber})
                            </h4>
                            <p style={{ margin: '2px 0 0 0', fontSize: '0.75rem', color: '#1e40af', lineHeight: 1.4, fontWeight: 500 }}>
                                Apenas os itens pertencentes a este lote estão visíveis e incluídos nesta ação de aprovação. Os itens restantes do pedido continuam pendentes com o comprador.
                            </p>
                            {(() => {
                                // Candidate model: the Buyer submitted OPTIONS — the winner is chosen
                                // by the Area Approver inside the wizard, never pre-decided here.
                                const candidateItems = (activeBatch.items || []).filter((bi: any) => (bi.candidates?.length ?? 0) > 0);
                                if (candidateItems.length === 0) return null;
                                const optionCount = candidateItems.reduce((acc: number, bi: any) => acc + bi.candidates.length, 0);
                                const undecidedCount = candidateItems.filter((bi: any) => !bi.selectedCandidateId).length;
                                return (
                                    <p style={{ margin: '6px 0 0 0', fontSize: '0.72rem', color: '#1e40af', lineHeight: 1.5 }}>
                                        <strong>Opções enviadas pelo Comprador:</strong> {optionCount} para {candidateItems.length} item(ns).{' '}
                                        {undecidedCount > 0
                                            ? <>Vencedores a definir pelo Aprovador de Área — compare e selecione no Assistente de Revisão.</>
                                            : <>Vencedores selecionados pelo Aprovador de Área.</>}
                                    </p>
                                );
                            })()}
                            {activeBatch.status !== data.statusCode && (
                                <p style={{ margin: '8px 0 0 0', fontSize: '0.72rem', color: '#1e40af', lineHeight: 1.5 }}>
                                    <strong>Pedido (status geral):</strong> {data.statusName || data.statusCode}
                                    {' · '}
                                    <strong>Lote #{activeBatch.batchNumber}:</strong> {BATCH_STATUS_LABELS[activeBatch.status] || activeBatch.status}
                                </p>
                            )}
                        </div>
                    </div>
                )}

                {/* NOTE: the Area Approver's budget justification is rendered inside
                     "Inteligência para Decisão > Disponibilidade Orçamental" (via
                     DecisionInsightsPanel props below), next to the budget KPIs it
                     refers to — not as a standalone banner here. */}

                {/* --- ACTION BAR --- */}
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginBottom: '16px' }}>
                    <button
                        data-tour="approval-drawer-tour-button"
                        onClick={() => startTour(drawerTourId)}
                        title="Tour da Aprovação"
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                            padding: '8px 16px',
                            backgroundColor: 'rgba(var(--color-primary-rgb), 0.06)',
                            color: 'var(--color-primary)',
                            border: '1px solid rgba(var(--color-primary-rgb), 0.15)',
                            borderRadius: '8px',
                            fontWeight: 700,
                            fontSize: '13px',
                            cursor: 'pointer',
                            transition: 'all 0.2s',
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em',
                            fontFamily: 'var(--font-family-display)',
                            whiteSpace: 'nowrap',
                        }}
                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.12)'; e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.25)'; e.currentTarget.style.transform = 'translateY(-1px)'; }}
                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.06)'; e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.15)'; e.currentTarget.style.transform = 'translateY(0)'; }}
                    >
                        <Compass size={14} strokeWidth={2.5} /> Tour da Aprovação
                    </button>
                    <button
                        data-tour="approval-drawer-manual-button"
                        onClick={() => setShowHelp(true)}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '8px 16px',
                            backgroundColor: '#FEF9C3',
                            color: '#854D0E',
                            border: '1px solid #FDE047',
                            borderRadius: '8px',
                            fontWeight: 700,
                            fontSize: '13px',
                            cursor: 'pointer',
                            boxShadow: 'var(--shadow-sm)',
                            transition: 'all 0.2s',
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em'
                        }}
                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#FEF08A'; e.currentTarget.style.color = '#713F12'; e.currentTarget.style.transform = 'translateY(-1px)'; }}
                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#FEF9C3'; e.currentTarget.style.color = '#854D0E'; e.currentTarget.style.transform = 'none'; }}
                    >
                        <BookOpen size={16} /> Manual de Aprovação
                    </button>
                </div>

                {/* --- INTELLIGENCE ALERTS (wrapper for tour) --- */}
                {hasItemAboveAvg && (
                    <div data-tour="approval-drawer-alerts">
                {/* Price warning banner: shown ONLY when items are above historical average */}
                {hasItemAboveAvg && (
                    <div style={{
                        marginBottom: '24px', width: '100%', 
                        backgroundColor: '#FEF9C3', 
                        border: '1px solid #FEF08A', 
                        borderRadius: 'var(--radius-lg)', padding: '16px',
                        display: 'flex', gap: '16px', boxShadow: 'var(--shadow-sm)', alignItems: 'flex-start'
                    }}>
                        <AlertTriangle color="#A16207" style={{ marginTop: '2px', flexShrink: 0 }} size={20} />
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            <span style={{ color: '#854D0E', fontWeight: 700, marginBottom: '4px' }}>
                                Atenção ao Histórico de Preços
                            </span>
                            <span style={{ color: '#A16207', fontSize: '0.875rem', lineHeight: 1.2 }}>
                                {itemsAboveAvg.length === 1
                                    ? '1 item deste pedido está com preço acima da média histórica.'
                                    : `${itemsAboveAvg.length} itens deste pedido estão com preço acima da média histórica.`}
                            </span>
                        </div>
                    </div>
                )}

                {/* --- BLOCKING ALERTS ---
                     The old "Atribuição de Itens Obrigatória" banner (single-winner-per-
                     request model: data.quotations.find(q => q.isSelected)) was removed.
                     It doesn't apply to the batch/partial-approval model — a batch can
                     legitimately cover only some of the request's items, and winners are
                     now awarded per item (ItemAwardMatrix), not as one quotation for the
                     whole request. Blocking the drawer on this obsolete check no longer
                     makes sense now that the real review happens per-batch in the Wizard. */}

                {/* NOTE: the "Pendência de Alocação" blocking alert was removed. The
                     area-approval drawer is informative only — plant/cost-center
                     allocation is handled inside the Approval Wizard ("Revisar Pedido"),
                     which validates it in its own Atribuição Financeira step. */}
                    </div>
                )}

                {/* 2. RESUMO PARA DECISÃO (Always Open Grid Context) */}
                <div data-tour="approval-drawer-request-info" style={{ marginBottom: '32px' }}>
                    <DecisionSummaryGrid items={summaryItems} />
                </div>

                {/* NOTE: the "Itens Propostos como Não Cotado" decision panel (Fase 3) was
                     deliberately removed from this drawer. Product direction: the approval
                     drawer is being phased out as an operational surface in favor of the
                     Approval Batch Wizard, and a not-quoted decision is not a batch action —
                     it must not gate or appear inside batch approval at all. The component
                     itself (NotQuotedDecisionPanel/NotQuotedDecisionModal) is kept and still
                     used from the Requester's RequestEdit.tsx; only this integration point
                     was removed. Where this decision should ultimately live (buyer flow vs.
                     a dedicated pendencies screen) is still to be decided. */}

                {/* 2.5. CONTEXTO FINANCEIRO (Gráfico de Tendência) */}
                <div data-tour="approval-drawer-financial-context" style={{ marginBottom: '32px' }}>
                    <DecisionSection 
                        title="Contexto Financeiro Visual" 
                        icon={<TrendingUp size={16} style={{ color: 'black' }} />}
                        isCollapsible={true}
                        defaultOpen={true}
                    >
                        <div style={{ padding: '24px' }}>
                            <DecisionFinancialTrendLine requestId={data.id} />
                        </div>
                    </DecisionSection>
                </div>

                {/* 2.6. COTAÇÕES SALVAS — hidden on the AREA drawer: quotation review,
                     per-item winner display (ItemAwardMatrix / "Resumo da Atribuição")
                     and everything operational now lives in the Approval Wizard. The
                     section is preserved for FINAL approval, whose AwardSummary still
                     depends on it. */}
                {isQuotation && !isAreaApprovalStage && (
                    <motion.div
                        data-tour="approval-drawer-quotations"
                        id="cotacoes-salvas-section"
                        animate={
                            highlightQuotationSection
                                ? {
                                      boxShadow: ['0 0 0px 0px transparent', '0 0 15px 5px rgba(239, 68, 68, 0.4)', '0 0 0px 0px transparent'],
                                      transition: { duration: 1, repeat: 4 }
                                  }
                                : { boxShadow: '0 0 0px 0px transparent' }
                        }
                        style={{ borderRadius: 'var(--radius-lg)' }}
                    >
                        <DecisionSection 
                            title="Cotações Salvas" 
                            icon={<DollarSign size={16} />}
                            count={data.quotations?.length || 0}
                            isCollapsible={false}
                        >
                            {(data.quotations?.length || 0) === 0 ? (
                                <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                                    Nenhuma cotação registrada.
                                </div>
                            ) : (
                                <div>
                                    {data.quotations?.map(q => (
                                        <DecisionQuotationCard
                                            key={q.id}
                                            quotation={q}
                                            isLowest={data.quotations.length > 1 && q.totalAmount === lowestByCurrency[q.currency]}
                                            canSelectWinner={canSelectWinner}
                                            onSelectWinner={handleSelectWinner}
                                            isProcessing={quotationProcessingId === q.id}
                                            isExpanded={expandedQuotationId === q.id}
                                            onToggleExpand={(id) => setExpandedQuotationId(prev => prev === id ? null : id)}
                                            lotIncludedItemIds={lotView ? lotIncludedQuotationItemIds : undefined}
                                            lotIgnoredReasonById={lotView ? lotIgnoredReasonByQuotationItemId : undefined}
                                        />
                                    ))}
                                </div>
                            )}

                            {isQuotation && approvalStage === 'AREA' && isAreaApprovalStage && data.quotations && data.quotations.length > 0 && (
                                <div style={{ marginTop: '24px' }}>
                                    <ItemAwardMatrix
                                        items={activeItems}
                                        quotations={data.quotations}
                                        itemAwards={itemAwards}
                                        onAwardChange={(lineItemId, quotationItemId) => {
                                            setItemAwards(prev => ({
                                                ...prev,
                                                [lineItemId]: quotationItemId
                                            }));
                                        }}
                                        onSelectAll={(quotationId) => {
                                            const quotation = data.quotations?.find(q => q.id === quotationId);
                                            if (!quotation || !quotation.items) return;
                                            const newAwards = { ...itemAwards };
                                            activeItems.forEach(item => {
                                                // Match by mapping (not lineNumber) and skip reuse-blocked
                                                // items (Option C) — bulk select must never pick them.
                                                const qItem = quotation.items.find(qi => qi.mappedRequestLineItemId === item.id);
                                                if (qItem && !qItem.isReuseBlocked
                                                    && (qItem.reconciliationStatus === 'MAPPED' || qItem.reconciliationStatus === 'SUBSTITUTE')) {
                                                    newAwards[item.id] = qItem.id;
                                                }
                                            });
                                            setItemAwards(newAwards);
                                        }}
                                    />
                                </div>
                            )}

                            {isQuotation && approvalStage === 'FINAL' && isFinalApprovalStage && (
                                <div style={{ marginTop: '24px' }}>
                                    <AwardSummary
                                        poGroups={data.poGroups || []}
                                        quotations={data.quotations || []}
                                        attachments={data.attachments || []}
                                    />
                                </div>
                            )}
                        </DecisionSection>
                    </motion.div>
                )}

                {/* 3. INTELIGÊNCIA PARA DECISÃO (Phase 4 - Horizontal Navigation) */}
                <div data-tour="approval-drawer-financial-allocation">
                <DecisionSection 
                    title="Inteligência para Decisão" 
                    icon={<TrendingUp size={16} style={{ color: 'black' }} />}
                    isCollapsible={false}
                >
                    {loadingIntelligence ? (
                        <div style={{ padding: '40px 24px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '16px' }}>
                                <div style={{ width: '40px', height: '40px', border: '4px solid var(--color-text-main)', borderTopColor: 'transparent', borderRadius: '50%', animation: 'spin 1s linear infinite' }} />
                                <span style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.2em', color: 'var(--color-text-main)' }}>Analizando Histórico...</span>
                            </div>
                        </div>
                    ) : intelligence ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            {/* Horizontal Tab Strip for Item Selection */}
                            {activeItems.length > 1 && (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', paddingBottom: '0', marginBottom: '16px', borderBottom: '1px solid var(--color-border)', width: '100%' }}>
                                    {activeItems.length > 5 && (
                                        <div style={{ padding: '0 4px', display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between' }}>
                                             <div style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>
                                                Analisar Item:
                                             </div>
                                             <div style={{ position: 'relative', width: '250px' }}>
                                                 <Target size={14} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'gray' }} />
                                                 <input 
                                                    type="text" 
                                                    placeholder="Buscar item..."
                                                    value={insightSearchQ}
                                                    onChange={(e) => setInsightSearchQ(e.target.value)}
                                                    style={{ width: '100%', fontSize: '0.75rem', padding: '6px 12px 6px 36px', border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-sm)', outline: 'none', fontWeight: 700, color: 'var(--color-text-main)' }}
                                                 />
                                             </div>
                                        </div>
                                    )}
                                    {activeItems.length <= 5 && (
                                        <div style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', padding: '0 4px' }}>
                                            Analisar Item:
                                        </div>
                                    )}
                                    <div style={{ display: 'flex', alignItems: 'flex-end', gap: '8px', overflowX: 'auto', scrollBehavior: 'smooth' }}>
                                        {activeItems.filter(item => item.description.toLowerCase().includes(insightSearchQ.toLowerCase())).map((item) => {
                                            const isActive = activeInsightItemId === item.id;
                                            
                                            // Status cue logic
                                            const assignment = itemAssignments[item.id];
                                            const isAssigned = assignment?.plantId && assignment?.costCenterId;
                                            
                                            return (
                                                <button
                                                    key={item.id}
                                                    onClick={() => setActiveInsightItemId(item.id)}
                                                    style={{
                                                        flexShrink: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', padding: '0 16px 12px 16px', transition: 'all 0.2s', outline: 'none', cursor: 'pointer', background: 'none', border: 'none', borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                                                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)', minWidth: '160px', maxWidth: '220px'
                                                    }}
                                                >
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', width: '100%', marginBottom: '4px' }}>
                                                        <span style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                            Item #{item.lineNumber}
                                                        </span>
                                                        <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: isAssigned ? 'var(--color-status-green)' : 'var(--color-status-red)' }} />
                                                    </div>
                                                    <div style={{ fontSize: '0.75rem', width: '100%', textAlign: 'left', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', transition: 'opacity 0.2s', fontWeight: isActive ? 900 : 700, opacity: isActive ? 1 : 0.7 }}>
                                                        {item.description}
                                                    </div>
                                                </button>
                                            );
                                        })}
                                    </div>
                                </div>
                            )}

                            <DecisionInsightsPanel 
                                intelligence={{
                                    ...intelligence,
                                    // Only pass the intelligence for the selected item to focus the UI
                                    items: intelligence.items?.filter(i => {
                                        const originalItem = activeItems.find(ai => ai.id === activeInsightItemId);
                                        return i.description === originalItem?.description;
                                    }) || []
                                }} 
                                approvalStage={approvalStage}
                                onDrillDown={onDrillDown}
                                requestData={{
                                    description: data.description,
                                    supplierName: data.supplierName,
                                    costCenterCode: data.costCenterCode,
                                    requestTypeCode: data.requestTypeCode,
                                    hasQuotations: (data.quotations?.length || 0) > 0
                                }}
                                isSingleItemFocus={activeItems.length > 1}
                                budgetJustification={approvalStage === 'FINAL' ? activeBatch?.budgetJustification : undefined}
                                budgetJustificationAuthor={approvalStage === 'FINAL' ? activeBatch?.updatedByUserName : undefined}
                                budgetJustificationDate={approvalStage === 'FINAL' && activeBatch?.updatedAtUtc ? formatDate(activeBatch.updatedAtUtc) : undefined}
                                batchChecklist={approvalStage === 'FINAL' && activeBatch ? {
                                    batchNumber: activeBatch.batchNumber,
                                    itemCount: activeBatch.items?.length || 0,
                                    areaApproved: ['WAITING_FINAL_APPROVAL', 'APPROVED'].includes(activeBatch.status),
                                    winnersDefined: (activeBatch.items || []).every((bi: any) => !!bi.selectedQuotationItemId),
                                    allocationDefined: activeItems.length > 0 && activeItems.every((li: any) =>
                                        (li.allocations && li.allocations.length > 0 && li.allocations.every((a: any) => a.costCenterId)) || !!li.costCenterId
                                    ),
                                    budgetJustificationRegistered: !!activeBatch.budgetJustification
                                } : undefined}
                            />
                        </div>
                    ) : (
                        <div style={{ padding: '32px', textAlign: 'center', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)' }}>
                             <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '12px' }}>
                                <Target style={{ color: 'var(--color-border-heavy)' }} size={32} />
                                <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Dados de inteligência não disponíveis</span>
                             </div>
                        </div>
                    )}
                </DecisionSection>
                </div>

                {/* 4. ITENS DO PEDIDO (Adaptive Navigation) — hidden on the AREA drawer:
                     batch items are reviewed/allocated inside the Approval Wizard, and
                     items outside the batch must not appear as decision surface here.
                     Kept for FINAL approval (read-only Planta/C.Custo context). */}
                {!isAreaApprovalStage && (
                <motion.div
                    data-tour="approval-drawer-items"
                    id="itens-do-pedido-section"
                    animate={
                        highlightSection
                            ? {
                                  boxShadow: ['0 0 0px 0px transparent', '0 0 15px 5px rgba(239, 68, 68, 0.4)', '0 0 0px 0px transparent'],
                                  transition: { duration: 1, repeat: 4 }
                              }
                            : { boxShadow: '0 0 0px 0px transparent' }
                    }
                    style={{ borderRadius: 'var(--radius-lg)' }}
                >
                    <DecisionSection 
                        title="Itens do pedido" 
                        icon={<List size={16} />}
                        count={activeItems.length}
                        isCollapsible={true}
                        defaultOpen={true}
                        noPadding={viewMode === 'LIST'}
                        headerRight={
                        <div style={{ display: 'flex', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-md)', padding: '4px', marginLeft: 'auto', flexShrink: 0 }}>
                            <button
                                onClick={(e) => { e.stopPropagation(); setViewMode('CARDS'); }}
                                style={{
                                    padding: '6px 12px', borderRadius: '4px', fontSize: '0.625rem', textTransform: 'uppercase', fontWeight: 700, letterSpacing: '0.05em', transition: 'all 0.2s', border: 'none',
                                    ...(viewMode === 'CARDS' ? { backgroundColor: 'white', boxShadow: 'var(--shadow-sm)', color: 'var(--color-text-main)' } : { backgroundColor: 'transparent', color: 'var(--color-text-muted)', cursor: 'pointer' })
                                }}
                            >
                                Cards
                            </button>
                            <button
                                onClick={(e) => { e.stopPropagation(); setViewMode('LIST'); }}
                                style={{
                                    padding: '6px 12px', borderRadius: '4px', fontSize: '0.625rem', textTransform: 'uppercase', fontWeight: 700, letterSpacing: '0.05em', transition: 'all 0.2s', border: 'none',
                                    ...(viewMode === 'LIST' ? { backgroundColor: 'white', boxShadow: 'var(--shadow-sm)', color: 'var(--color-text-main)' } : { backgroundColor: 'transparent', color: 'var(--color-text-muted)', cursor: 'pointer' })
                                }}
                            >
                                Lista
                            </button>
                        </div>
                    }
                >
                    {activeItems.length === 0 ? (
                        <div style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                            Nenhum item encontrado.
                        </div>
                    ) : viewMode === 'CARDS' ? (
                        /* --- CARDS MODE --- */
                        <div style={{ display: 'flex', gap: '16px', overflowX: 'auto', paddingBottom: '16px', paddingTop: '4px', scrollBehavior: 'smooth' }}>
                            {activeItems.map((item) => {
                                const assignment = itemAssignments[item.id] || { plantId: null, costCenterId: null };
                                const isResolved = assignment.plantId && assignment.costCenterId;

                                return (
                                    <div 
                                        key={item.id}
                                        style={{
                                            flexShrink: 0, width: '340px', display: 'flex', flexDirection: 'column', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', overflow: 'hidden', boxShadow: 'var(--shadow-sm)', transition: 'box-shadow 0.2s'
                                        }}
                                    >
                                        <div style={{ padding: '20px', display: 'flex', flexDirection: 'column', flexGrow: 1 }}>
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                    <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', marginBottom: '4px' }}>
                                                        Item #{item.lineNumber}
                                                    </span>
                                                    <span style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', lineHeight: 1.2 }}>
                                                        {item.description}
                                                    </span>
                                                </div>
                                                <div style={{ width: '12px', height: '12px', borderRadius: '50%', marginTop: '4px', flexShrink: 0, backgroundColor: isResolved ? 'var(--color-status-green)' : 'var(--color-status-red)' }} />
                                            </div>

                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: '24px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.625rem' }}>Quantidade</span>
                                                    <span style={{ fontWeight: 900, color: 'var(--color-text-main)', fontSize: '0.875rem' }}>{item.quantity} {item.unit || 'UN'}</span>
                                                </div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', alignItems: 'flex-end' }}>
                                                    <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.625rem' }}>Total</span>
                                                    {(() => {
                                                        const t = resolveItemTotal(item);
                                                        return (
                                                            <span style={{ fontWeight: 900, color: t.warning ? 'var(--color-status-orange)' : 'var(--color-text-main)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                                {t.warning && <AlertTriangle size={12} />}{t.text}
                                                            </span>
                                                        );
                                                    })()}
                                                </div>
                                            </div>

                                            {approvalStage === 'AREA' && isAreaApprovalStage ? (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                        <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Planta <span style={{ color: 'var(--color-status-red)' }}>*</span></label>
                                                        <select
                                                            value={assignment.plantId || ''}
                                                            onChange={(e) => setItemAssignments(prev => ({ 
                                                                ...prev, [item.id]: { plantId: parseInt(e.target.value) || null, costCenterId: null } 
                                                            }))}
                                                            style={{ 
                                                                width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                border: (highlightFields && !assignment.plantId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                borderRadius: 'var(--radius-md)', padding: '10px', outline: 'none' 
                                                            }}
                                                        >
                                                            <option value="">Selecionar...</option>
                                                            {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                                        </select>
                                                    </div>
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                        <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Centro de Custo <span style={{ color: 'var(--color-status-red)' }}>*</span></label>
                                                        <select
                                                            value={assignment.costCenterId || ''}
                                                            disabled={!assignment.plantId}
                                                            onChange={(e) => setItemAssignments(prev => ({ 
                                                                ...prev, [item.id]: { ...prev[item.id], costCenterId: parseInt(e.target.value) || null } 
                                                            }))}
                                                            style={{ 
                                                                width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                border: (highlightFields && !assignment.costCenterId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                borderRadius: 'var(--radius-md)', padding: '10px', outline: 'none', opacity: assignment.plantId ? 1 : 0.5 
                                                            }}
                                                        >
                                                            <option value="">{assignment.plantId ? 'Selecionar...' : 'Exige Planta'}</option>
                                                            {costCenters.filter(cc => cc.plantId === assignment.plantId).map(cc => (
                                                                <option key={cc.id} value={cc.id}>[{cc.code}] {cc.name}</option>
                                                            ))}
                                                        </select>
                                                    </div>
                                                    {canBulkFill(item.id) && (() => {
                                                        const pendingCount = activeItems.filter(i => i.id !== item.id && (!itemAssignments[i.id]?.plantId || !itemAssignments[i.id]?.costCenterId)).length;
                                                        return (
                                                            <Tooltip content="Aplica a mesma Planta e Centro de Custo aos demais itens desta lista com status pendente" variant="dark" side="top">
                                                                <button 
                                                                    onClick={() => handleBulkFill(item.id)}
                                                                    style={{ marginTop: '8px', width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', padding: '8px 12px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-muted)', borderRadius: 'var(--radius-md)', fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', border: 'none', cursor: 'pointer' }}
                                                                >
                                                                    Aplicar a {pendingCount} pendentes
                                                                </button>
                                                            </Tooltip>
                                                        );
                                                    })()}
                                                </div>
                                            ) : (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.75rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)' }}>Planta (Alocação)</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.plantName || '---'}</span>
                                                    </div>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.75rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)' }}>Centro de Custo</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.costCenterCode ? `[${item.costCenterCode}]` : '---'}</span>
                                                    </div>
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    ) : (
                        /* --- LIST MODE (Master-Detail Replacement) --- */
                        <div style={{ display: 'flex', flexDirection: 'column', borderTop: '1px solid var(--color-border)' }}>
                            {activeItems.map((item) => {
                                const assignment = itemAssignments[item.id] || { plantId: null, costCenterId: null };
                                const isResolved = assignment.plantId && assignment.costCenterId;

                                return (
                                    <div key={item.id} style={{ padding: '24px', display: 'flex', gap: '24px', borderBottom: '1px solid var(--color-border)', flexWrap: 'wrap' }}>
                                        <div style={{ flex: '1 1 0%', display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '8px' }}>
                                                <div style={{ width: '10px', height: '10px', borderRadius: '50%', flexShrink: 0, backgroundColor: isResolved ? 'var(--color-status-green)' : 'var(--color-status-red)' }} />
                                                <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Item #{item.lineNumber}</span>
                                            </div>
                                            <h4 style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', marginBottom: '12px', paddingRight: '16px' }}>{item.description}</h4>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '24px', marginTop: 'auto' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.5625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Quantidade</span>
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 900, color: 'var(--color-text-main)' }}>{item.quantity} {item.unit || 'UN'}</span>
                                                </div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.5625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>{lotItemByLineId.has(item.id) ? 'Total Aprovado' : 'Total Estimado'}</span>
                                                    {(() => {
                                                        const t = resolveItemTotal(item);
                                                        return (
                                                            <span style={{ fontSize: '0.75rem', fontWeight: 900, color: t.warning ? 'var(--color-status-orange)' : 'var(--color-text-main)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                                {t.warning && <AlertTriangle size={12} />}{t.text}
                                                            </span>
                                                        );
                                                    })()}
                                                </div>
                                            </div>
                                        </div>

                                        <div style={{ width: '100%', maxWidth: '400px', flexShrink: 0, backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', padding: '16px', boxShadow: 'var(--shadow-sm)', position: 'relative' }}>
                                            {approvalStage === 'AREA' && isAreaApprovalStage ? (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                            <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Planta</label>
                                                            <select
                                                                value={assignment.plantId || ''}
                                                                onChange={(e) => setItemAssignments(prev => ({ 
                                                                    ...prev, [item.id]: { plantId: parseInt(e.target.value) || null, costCenterId: null } 
                                                                }))}
                                                                style={{ 
                                                                    width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                    border: (highlightFields && !assignment.plantId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                    borderRadius: 'var(--radius-md)', padding: '8px', outline: 'none' 
                                                                }}
                                                            >
                                                                <option value="">Selecionar...</option>
                                                                {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                                            </select>
                                                        </div>
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                            <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>C. Custo</label>
                                                            <select
                                                                value={assignment.costCenterId || ''}
                                                                disabled={!assignment.plantId}
                                                                onChange={(e) => setItemAssignments(prev => ({ 
                                                                    ...prev, [item.id]: { ...prev[item.id], costCenterId: parseInt(e.target.value) || null } 
                                                                }))}
                                                                style={{ 
                                                                    width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                    border: (highlightFields && !assignment.costCenterId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                    borderRadius: 'var(--radius-md)', padding: '8px', outline: 'none', opacity: assignment.plantId ? 1 : 0.5 
                                                                }}
                                                            >
                                                                <option value="">{assignment.plantId ? 'Selecionar...' : '---'}</option>
                                                                {costCenters.filter(cc => cc.plantId === assignment.plantId).map(cc => (
                                                                    <option key={cc.id} value={cc.id}>[{cc.code}] {cc.name}</option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                    </div>
                                                    {canBulkFill(item.id) && (() => {
                                                        const pendingCount = activeItems.filter(i => i.id !== item.id && (!itemAssignments[i.id]?.plantId || !itemAssignments[i.id]?.costCenterId)).length;
                                                        return (
                                                            <Tooltip content="Aplica a mesma Planta e Centro de Custo aos demais itens desta lista com status pendente" variant="dark" side="top">
                                                                <button 
                                                                    onClick={() => handleBulkFill(item.id)}
                                                                    style={{ width: '100%', marginTop: '4px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', padding: '6px 12px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-muted)', borderRadius: 'var(--radius-sm)', fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', transition: 'all 0.2s', border: 'none', cursor: 'pointer' }}
                                                                >
                                                                    Aplicar aos {pendingCount} compatíveis
                                                                </button>
                                                            </Tooltip>
                                                        );
                                                    })()}
                                                </div>
                                            ) : (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', justifyContent: 'center', height: '100%' }}>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>Planta (Alocação)</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.plantName || '---'}</span>
                                                    </div>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>C. Custo</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.costCenterCode ? `[${item.costCenterCode}] ${item.costCenterName}` : '---'}</span>
                                                    </div>
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </DecisionSection>
                </motion.div>
                )}

                {/* 5. JUSTIFICATIVA / OBSERVAÇÕES (Always Open if present) */}
                {data.description && (
                    <DecisionSection 
                        title="Justificativa / Observações" 
                        icon={<MessageSquare size={16} />}
                        isCollapsible={false}
                    >
                        <p style={{ margin: 0, fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', lineHeight: 1.6, padding: '20px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', whiteSpace: 'pre-wrap' }}>
                            {data.description}
                        </p>
                    </DecisionSection>
                )}


                {/* 7. RESUMO FINANCEIRO (Collapsible) */}
                <DecisionSection 
                    title="Resumo financeiro" 
                    icon={<DollarSign size={16} />}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', padding: '24px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)' }}>
                        {lotView ? (
                            <>
                                {/* Primary amount at Final Approval is the approved LOT total — never the
                                     request's estimate (0 for quotation requests). The original estimate,
                                     when meaningful, is kept only as secondary context. */}
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Total Aprovado Neste Lote</span>
                                    <span style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', letterSpacing: '-0.02em' }}>
                                        {formatCurrencyAO(lotView.lotTotal)} <span style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', fontWeight: 700, marginLeft: '4px' }}>{lotView.currencyCode || data.currencyCode}</span>
                                    </span>
                                </div>
                                {data.estimatedTotalAmount > 0 && (
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid var(--color-border)', paddingTop: '16px' }}>
                                        <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Valor Estimado Original</span>
                                        <span style={{ fontSize: '0.875rem', fontWeight: 800, color: 'var(--color-text-muted)' }}>
                                            {formatCurrencyAO(data.estimatedTotalAmount)} {data.currencyCode}
                                        </span>
                                    </div>
                                )}
                                {lotView.hasMonetaryInconsistency && (
                                    <div style={{ display: 'flex', alignItems: 'flex-start', gap: '10px', padding: '12px 14px', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderLeft: '4px solid #f59e0b', borderRadius: 'var(--radius-md)' }}>
                                        <AlertTriangle size={16} style={{ color: '#b45309', flexShrink: 0, marginTop: '2px' }} />
                                        <span style={{ fontSize: '0.8rem', fontWeight: 700, color: '#78350f' }}>
                                            O total aprovado registrado não coincide com a soma dos itens do lote. Verifique os valores antes de decidir.
                                        </span>
                                    </div>
                                )}
                            </>
                        ) : (
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Custo Estimado Total</span>
                                <span style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', letterSpacing: '-0.02em' }}>
                                    {formatCurrencyAO(data.estimatedTotalAmount)} <span style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', fontWeight: 700, marginLeft: '4px' }}>{data.currencyCode}</span>
                                </span>
                            </div>
                        )}
                        {data.supplierPortalCode && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid var(--color-border)', paddingTop: '16px' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Código do Fornecedor</span>
                                <span style={{ fontSize: '0.875rem', fontWeight: 900, fontFamily: 'monospace', backgroundColor: 'var(--color-bg-page)', padding: '4px 10px', borderRadius: 'var(--radius-sm)', color: 'var(--color-text-main)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}>{data.supplierPortalCode}</span>
                            </div>
                        )}
                    </div>
                </DecisionSection>

                {/* 8. ANEXOS (Collapsible) */}
                <div data-tour="approval-drawer-documents">
                <DecisionSection 
                    title="Anexos" 
                    icon={<Paperclip size={16} />}
                    count={data.attachments?.length || 0}
                    isCollapsible={true}
                    defaultOpen={false}
                    noPadding={true}
                >
                    {(data.attachments?.length || 0) === 0 ? (
                        <div style={{ padding: '32px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                            Nenhum anexo registrado.
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            {data.attachments.map((att) => (
                                <div key={att.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '20px', borderBottom: '1px solid var(--color-border)' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px', flex: 1, minWidth: 0 }}>
                                        <div style={{ width: '40px', height: '40px', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-sm)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, border: '1px solid var(--color-border)' }}>
                                            <Paperclip size={18} color="var(--color-text-muted)" />
                                        </div>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', marginBottom: '4px' }}>
                                                {att.fileName}
                                            </div>
                                            <div style={{ fontSize: '0.625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                {ATTACHMENT_TYPE_LABELS[att.attachmentTypeCode] || att.attachmentTypeCode} • Por {att.uploadedByName}
                                            </div>
                                        </div>
                                    </div>
                                    <button
                                        onClick={() => handleDownloadAttachment(att.id, att.fileName)}
                                        style={{ marginLeft: '16px', padding: '8px', backgroundColor: 'white', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', color: 'var(--color-text-muted)', boxShadow: 'var(--shadow-sm)', cursor: 'pointer', flexShrink: 0 }}
                                    >
                                        <Download size={16} />
                                    </button>
                                </div>
                            ))}
                        </div>
                    )}
                </DecisionSection>
                </div>

                {/* 9. PARTICIPANTES DO FLUXO (Collapsible) */}
                <DecisionSection 
                    title="Participantes do fluxo" 
                    icon={<Users size={16} />}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        {[
                            { label: 'Solicitante', name: data.requesterName, role: 'Buyer/User' },
                            { label: 'Comprador Atribuído', name: data.buyerName, role: 'Procurement' },
                            {
                                // Fase B: antes da decisão mostra os managers elegíveis; depois, o decisor real.
                                label: 'Aprovador da Área',
                                name: data.areaApproverName
                                    || (data.eligibleAreaManagerNames && data.eligibleAreaManagerNames.length > 0
                                        ? `Pendente — ${data.eligibleAreaManagerNames.length} responsáve${data.eligibleAreaManagerNames.length > 1 ? 'is' : 'l'}: ${data.eligibleAreaManagerNames.join(', ')}`
                                        : null),
                                role: 'Area Manager'
                            },
                            { label: 'Aprovador Final', name: data.finalApproverName, role: 'C-Level / Admin' }
                        ].map((p, idx) => (
                            <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '16px', padding: '16px', backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)' }}>
                                <div style={{ width: '40px', height: '40px', borderRadius: '50%', backgroundColor: 'var(--color-text-main)', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.875rem', fontWeight: 900, flexShrink: 0 }}>
                                    {p.name?.substring(0, 2).toUpperCase() || '??'}
                                </div>
                                <div style={{ minWidth: 0 }}>
                                    <div style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.name || 'Pendente'}</div>
                                    <div style={{ fontSize: '0.625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginTop: '4px' }}>{p.label}</div>
                                </div>
                            </div>
                        ))}
                    </div>
                </DecisionSection>

                {/* 10. HISTÓRICO DO PEDIDO (Collapsible) */}
                <div data-tour="approval-drawer-workflow">
                <DecisionSection 
                    title="Histórico do pedido" 
                    icon={<HistoryIcon size={16} />}
                    count={data.statusHistory?.length || 0}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <DecisionTimeline entries={data.statusHistory} />
                </DecisionSection>
                </div>
            </div>

            {/* Sticky Action Footer
                 Area-approval stage: primary action is the wizard; secondary
                 quick-actions (Reject / Adjustment) are shown when scope is
                 clear — i.e. an activeBatch exists (QUOTATION) or the request
                 is a PAYMENT (which never has batches, so request-level is
                 correct). QUOTATION without an active batch falls back to
                 wizard-only to prevent accidental request-level operations.
                 Final-approval stage keeps the original three-button footer. */}
            <div data-tour="approval-drawer-actions" style={{ position: 'sticky', bottom: 0, zIndex: 50, backgroundColor: 'var(--color-bg-page)', borderTop: '1px solid var(--color-border)', padding: '16px 24px', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: '12px', flexWrap: 'wrap', boxShadow: '0 -4px 12px -2px rgba(0,0,0,0.08)', width: '100%' }}>
                {isAreaApprovalStage ? (
                    <>
                        {/* Guard G1: Only show quick-actions when scope is deterministic (active batch, payment, or legacy quotation without batch) */}
                        {showAdjustmentAction && showAreaAdjustment && (
                            <button
                                onClick={() => setShowApprovalModal({ show: true, type: 'REQUEST_ADJUSTMENT' })}
                                disabled={approvalProcessing}
                                style={{
                                    padding: '12px 24px',
                                    backgroundColor: 'rgba(217, 119, 6, 0.06)',
                                    color: '#92400E',
                                    fontWeight: 800,
                                    border: '1.5px solid rgba(217, 119, 6, 0.3)',
                                    borderRadius: 'var(--radius-lg)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.05em',
                                    fontSize: '0.75rem',
                                    cursor: approvalProcessing ? 'not-allowed' : 'pointer',
                                    transition: 'all 0.2s ease',
                                    opacity: approvalProcessing ? 0.6 : 1
                                }}
                                onMouseEnter={(e) => { if (!approvalProcessing) { e.currentTarget.style.backgroundColor = 'rgba(217, 119, 6, 0.12)'; e.currentTarget.style.borderColor = 'rgba(217, 119, 6, 0.5)'; e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 2px 8px rgba(217, 119, 6, 0.15)'; } }}
                                onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'rgba(217, 119, 6, 0.06)'; e.currentTarget.style.borderColor = 'rgba(217, 119, 6, 0.3)'; e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = 'none'; }}
                            >
                                <ArrowRightLeft size={16} /> Reajuste
                            </button>
                        )}
                        {showAreaReject && (
                            <button
                                onClick={() => setShowApprovalModal({ show: true, type: 'REJECT' })}
                                disabled={approvalProcessing}
                                style={{
                                    padding: '12px 24px',
                                    backgroundColor: 'rgba(239, 68, 68, 0.06)',
                                    color: '#991B1B',
                                    fontWeight: 800,
                                    border: '1.5px solid rgba(239, 68, 68, 0.25)',
                                    borderRadius: 'var(--radius-lg)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.05em',
                                    fontSize: '0.75rem',
                                    cursor: approvalProcessing ? 'not-allowed' : 'pointer',
                                    transition: 'all 0.2s ease',
                                    opacity: approvalProcessing ? 0.6 : 1
                                }}
                                onMouseEnter={(e) => { if (!approvalProcessing) { e.currentTarget.style.backgroundColor = 'rgba(239, 68, 68, 0.12)'; e.currentTarget.style.borderColor = 'rgba(239, 68, 68, 0.45)'; e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 2px 8px rgba(239, 68, 68, 0.15)'; } }}
                                onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'rgba(239, 68, 68, 0.06)'; e.currentTarget.style.borderColor = 'rgba(239, 68, 68, 0.25)'; e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = 'none'; }}
                            >
                                <AlertTriangle size={16} /> Rejeitar
                            </button>
                        )}
                        <button
                            onClick={() => setIsWizardOpen(true)}
                            disabled={approvalProcessing}
                            style={{
                                padding: '12px 32px',
                                borderRadius: 'var(--radius-lg)',
                                fontWeight: 800,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                textTransform: 'uppercase',
                                letterSpacing: '0.05em',
                                fontSize: '0.75rem',
                                transition: 'all 0.2s ease',
                                border: 'none',
                                backgroundColor: '#16A34A',
                                color: 'white',
                                cursor: approvalProcessing ? 'not-allowed' : 'pointer',
                                boxShadow: '0 2px 8px rgba(22, 163, 74, 0.3)',
                                opacity: approvalProcessing ? 0.6 : 1
                            }}
                            onMouseEnter={(e) => { if (!approvalProcessing) { e.currentTarget.style.backgroundColor = '#15803D'; e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 4px 14px rgba(22, 163, 74, 0.4)'; } }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = '#16A34A'; e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = '0 2px 8px rgba(22, 163, 74, 0.3)'; }}
                        >
                            <ShieldCheck size={16} /> Revisar Pedido
                        </button>
                    </>
                ) : (
                    <>
                        {showAdjustmentAction && (
                            <button
                                onClick={() => setShowApprovalModal({ show: true, type: 'REQUEST_ADJUSTMENT' })}
                                style={{
                                    padding: '12px 24px',
                                    backgroundColor: 'rgba(217, 119, 6, 0.06)',
                                    color: '#92400E',
                                    fontWeight: 800,
                                    border: '1.5px solid rgba(217, 119, 6, 0.3)',
                                    borderRadius: 'var(--radius-lg)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.05em',
                                    fontSize: '0.75rem',
                                    cursor: 'pointer',
                                    transition: 'all 0.2s ease'
                                }}
                                onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'rgba(217, 119, 6, 0.12)'; e.currentTarget.style.borderColor = 'rgba(217, 119, 6, 0.5)'; e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 2px 8px rgba(217, 119, 6, 0.15)'; }}
                                onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'rgba(217, 119, 6, 0.06)'; e.currentTarget.style.borderColor = 'rgba(217, 119, 6, 0.3)'; e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = 'none'; }}
                            >
                                <ArrowRightLeft size={16} /> Reajuste
                            </button>
                        )}
                        <button
                            onClick={() => setShowApprovalModal({ show: true, type: 'REJECT' })}
                            style={{
                                padding: '12px 24px',
                                backgroundColor: 'rgba(239, 68, 68, 0.06)',
                                color: '#991B1B',
                                fontWeight: 800,
                                border: '1.5px solid rgba(239, 68, 68, 0.25)',
                                borderRadius: 'var(--radius-lg)',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                textTransform: 'uppercase',
                                letterSpacing: '0.05em',
                                fontSize: '0.75rem',
                                cursor: 'pointer',
                                transition: 'all 0.2s ease'
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'rgba(239, 68, 68, 0.12)'; e.currentTarget.style.borderColor = 'rgba(239, 68, 68, 0.45)'; e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 2px 8px rgba(239, 68, 68, 0.15)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'rgba(239, 68, 68, 0.06)'; e.currentTarget.style.borderColor = 'rgba(239, 68, 68, 0.25)'; e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = 'none'; }}
                        >
                            <AlertTriangle size={16} /> Rejeitar
                        </button>
                        <button
                            onClick={() => setShowApprovalModal({ show: true, type: 'APPROVE' })}
                            style={{
                                padding: '12px 32px',
                                borderRadius: 'var(--radius-lg)',
                                fontWeight: 800,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                textTransform: 'uppercase',
                                letterSpacing: '0.05em',
                                fontSize: '0.75rem',
                                transition: 'all 0.2s ease',
                                border: 'none',
                                backgroundColor: '#16A34A',
                                color: 'white',
                                cursor: 'pointer',
                                boxShadow: '0 2px 8px rgba(22, 163, 74, 0.3)'
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = '#15803D'; e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 4px 14px rgba(22, 163, 74, 0.4)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = '#16A34A'; e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = '0 2px 8px rgba(22, 163, 74, 0.3)'; }}
                        >
                            <ShieldCheck size={16} /> Aprovar
                        </button>
                    </>
                )}
            </div>

            <ApprovalModal
                show={showApprovalModal.show}
                type={showApprovalModal.type}
                status={data.statusCode || ''}
                onClose={() => {
                    setShowApprovalModal({ show: false, type: null });
                    setApprovalComment('');
                    setModalFeedback({ type: 'error', message: null });
                }}
                onConfirm={(action) => handleWizardSubmit(action, itemAwards, itemAssignments, approvalComment)}
                comment={approvalComment}
                setComment={setApprovalComment}
                processing={approvalProcessing}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback({ type: 'error', message: null })}
                selectedQuotationName={selectedQuotation?.supplierNameSnapshot || null}
                batchNumber={activeBatch?.batchNumber ?? null}
                batchItemCount={activeBatch?.items?.length ?? null}
                isLegacyQuotationApproval={isLegacyQuotationApproval}
                isDecidedCandidateBatch={Boolean(activeBatch?.items?.some((bi: any) => (bi.candidates?.length ?? 0) > 0 && bi.selectedCandidateId))}
            />

            {/* WIZARD MODAL (For Area Approval Quotes) */}
            <ApprovalWizardModal
                isOpen={isWizardOpen}
                onClose={() => setIsWizardOpen(false)}
                request={wizardRequest}
                quotations={data.quotations || []}
                plants={plants}
                costCenters={costCenters}
                onSubmitAction={handleWizardSubmit}
                isSubmitting={approvalProcessing}
                onDownloadAttachment={handleDownloadAttachment}
                intelligence={intelligence}
                approvalStage={approvalStage}
                activeBatch={activeBatch}
            />

            {/* HELP OVERLAY (MODAL) */}
            {showHelp && (
                <div 
                    onClick={() => setShowHelp(false)}
                    style={{ 
                    position: 'fixed', top: 0, left: 0, width: '100vw', height: '100vh', 
                    backgroundColor: 'rgba(15, 23, 42, 0.7)', zIndex: 9999, 
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    backdropFilter: 'blur(4px)'
                }}>
                    <div 
                        onClick={(e) => e.stopPropagation()}
                        style={{
                        backgroundColor: 'var(--color-bg-page)', border: '4px solid #0f172a',
                        width: '90%', maxWidth: '750px', maxHeight: '90vh', overflowY: 'auto',
                        boxShadow: '16px 16px 0 #0f172a', padding: '32px', position: 'relative'
                    }}>
                        <button 
                            onClick={() => setShowHelp(false)}
                            style={{
                                position: 'absolute', top: '16px', right: '16px', backgroundColor: 'transparent',
                                border: 'none', cursor: 'pointer', color: '#64748b'
                            }}
                            onMouseOver={(e) => e.currentTarget.style.color = '#0f172a'}
                            onMouseOut={(e) => e.currentTarget.style.color = '#64748b'}
                        >
                            <X size={32} />
                        </button>
                        
                        <h2 style={{ fontSize: '1.75em', fontWeight: 900, textTransform: 'uppercase', marginBottom: '8px', borderBottom: '4px solid #e2e8f0', paddingBottom: '16px' }}>
                            Manual de Decisão
                        </h2>
                        <p style={{ fontWeight: 500, fontSize: '15px', color: '#475569', marginBottom: '32px' }}>
                            Este guia esclarece os componentes analíticos do Centro de Aprovações para agilizar e padronizar sua tomada de decisão.
                        </p>

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            
                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                <div style={{ backgroundColor: '#e0f2fe', padding: '12px', color: '#0284c7', border: '2px solid #0284c7' }}><TrendingUp size={24} /></div>
                                <div>
                                    <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Contexto Financeiro Visual</h4>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>O que é:</strong> O gráfico de tendência avalia o fluxo de caixa histórico do <strong>departamento solicitante</strong> para o <strong>mesmo fornecedor</strong> nos últimos meses e semanas.
                                    </p>
                                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                        <em>Por que é importante:</em> Permite visualizar picos anormais de gastos ou compras recorrentes, ajudando a identificar se este departamento já comprou demais desta mesma entidade recentemente ou se há um possível fracionamento orçamentário.
                                    </p>
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                <div style={{ backgroundColor: '#fce7f3', padding: '12px', color: '#db2777', border: '2px solid #db2777' }}><Target size={24} /></div>
                                <div>
                                    <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Inteligência para Decisão</h4>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>O que é:</strong> Um motor preditivo que cruza os itens sendo comprados neste exato pedido contra o nosso banco de dados unificado de histórico de aquisições do sistema Primavera ERP.
                                    </p>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>Como funciona:</strong> Ao trocar de "Item" na régua de cima, o painel muda para lhe mostrar as estatísticas globais e departamentais do item alvo. Se a bolinha ao lado do item estiver <strong>vermelha</strong>, isto indica que o preço deste fornecedor está consideravelmente acima do histórico que pagamos nos últimos meses, exigindo forte questionamento.
                                    </p>
                                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                        <em>Abas Disponíveis:</em> Você pode alternar entre a <strong>Visão Financeira</strong> (para ver o preço médio histórico por toda a Alpla) e <strong>Visão Departamental</strong> (para ver o impacto isolado no seu departamento ou orçamento local).
                                    </p>
                                </div>
                            </div>

                            {isAreaApprovalStage ? (
                                <>
                                    {/* Area approval: the drawer is informative only — allocation,
                                         batch review and the decision all happen inside the Wizard. */}
                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <div style={{ backgroundColor: '#dcfce7', padding: '12px', color: '#16a34a', border: '2px solid #16a34a' }}><ShieldCheck size={24} /></div>
                                        <div>
                                            <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Revisar Pedido — o Wizard de Aprovação</h4>
                                            <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                                Na aprovação de área, este painel serve apenas como <strong>contexto</strong>. Para aprovar, rejeitar ou solicitar reajuste, clique em <strong>Revisar Pedido</strong> e siga o Wizard. É nele que você fará a <strong>atribuição financeira</strong> (Planta e Centro de Custo por item, com distribuição rápida em massa), a <strong>revisão dos itens do lote</strong>, a <strong>análise orçamental</strong> e a <strong>decisão final</strong>.
                                            </p>
                                            <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                                <em>Importante:</em> O Wizard considera somente os itens do lote enviado para aprovação. Itens pendentes fora do lote continuam sob responsabilidade do comprador e não bloqueiam a aprovação deste lote.
                                            </p>
                                        </div>
                                    </div>

                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <div style={{ backgroundColor: '#eff6ff', padding: '12px', color: '#1d4ed8', border: '2px solid #1d4ed8' }}><Layers size={24} /></div>
                                        <div>
                                            <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Fluxo por Lote Parcial</h4>
                                            <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                                Um pedido pode continuar em cotação enquanto um lote parcial de itens já está em aprovação. <strong>Isso é esperado</strong> — o status geral do pedido e o status do lote podem ser diferentes, e o banner no topo do painel mostra os dois.
                                            </p>
                                            <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                                <em>Itens sem cotação:</em> Itens desconsiderados/encerrados sem cotação são tratados no fluxo do comprador, não na aprovação de área.
                                            </p>
                                        </div>
                                    </div>

                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <div style={{ backgroundColor: '#f3f4f6', padding: '12px', color: '#4b5563', border: '2px solid #4b5563' }}><ShieldCheck size={24} /></div>
                                        <div>
                                            <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Passo a Passo da Aprovação (Checklist)</h4>
                                            <ol style={{ margin: '8px 0', paddingLeft: '20px', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                                <li style={{ marginBottom: '8px' }}><strong>1. Resumo e Entendimento:</strong> Revise neste painel quem solicitou, os níveis de urgência e a secção de Justificativas.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>2. Contexto:</strong> Use o Contexto Financeiro Visual e a Inteligência para Decisão para identificar anomalias de preço ou gastos recorrentes.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>3. Lote Parcial:</strong> Se houver banner de lote, lembre-se: a revisão cobre apenas os itens desse lote.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>4. Revisar Pedido:</strong> Clique no botão no rodapé para abrir o Wizard de Aprovação.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>5. No Wizard:</strong> Faça a atribuição financeira (Planta e Centro de Custo), revise itens e valores do lote e verifique a disponibilidade orçamental.</li>
                                                <li><strong>6. Decisão:</strong> Conclua no Wizard com Aprovar, Rejeitar ou Solicitar Reajuste. Para rejeição ou reajuste, o comentário é obrigatório.</li>
                                            </ol>
                                        </div>
                                    </div>
                                </>
                            ) : (
                                <>
                                    {/* Final approval: the Final Approver reviews the Area's decision —
                                         winners, values and allocation are already set and read-only here. */}
                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <div style={{ backgroundColor: '#dcfce7', padding: '12px', color: '#16a34a', border: '2px solid #16a34a' }}><ShieldCheck size={24} /></div>
                                        <div>
                                            <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Revisão da Decisão da Área</h4>
                                            <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                                Como Aprovador Final, o seu papel é <strong>revisar a decisão tomada pela área</strong>: confira o fornecedor vencedor e os itens aprovados (secções "Cotações Salvas" e "Itens do pedido"), os <strong>valores finais</strong> no Resumo Financeiro e a <strong>atribuição financeira</strong> (Planta e Centro de Custo) já definida por item. Estes dados são apresentados apenas para leitura — a escolha da área não é editada aqui.
                                            </p>
                                            <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                                <em>Lote parcial:</em> Quando houver banner de lote no topo, a sua decisão vale apenas para os itens desse lote. Itens pendentes fora do lote continuam com o comprador.
                                            </p>
                                        </div>
                                    </div>

                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <div style={{ backgroundColor: '#fef3c7', padding: '12px', color: '#d97706', border: '2px solid #d97706' }}><AlertTriangle size={24} /></div>
                                        <div>
                                            <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Justificativa Orçamental</h4>
                                            <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                                Se a área aprovou o lote com um centro de custo <strong>crítico, esgotado ou sem orçamento</strong>, foi obrigada a registrar uma justificativa. Ela aparece dentro de <strong>Inteligência para Decisão → Disponibilidade Orçamental</strong>, junto aos indicadores de orçamento a que se refere.
                                            </p>
                                            <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                                <em>Importante:</em> Leia essa justificativa antes de decidir — ela explica por que a área optou por prosseguir apesar do alerta orçamental.
                                            </p>
                                        </div>
                                    </div>

                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <div style={{ backgroundColor: '#f3f4f6', padding: '12px', color: '#4b5563', border: '2px solid #4b5563' }}><ShieldCheck size={24} /></div>
                                        <div>
                                            <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Passo a Passo da Aprovação Final (Checklist)</h4>
                                            <ol style={{ margin: '8px 0', paddingLeft: '20px', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                                <li style={{ marginBottom: '8px' }}><strong>1. Resumo e Entendimento:</strong> Revise quem solicitou, os níveis de urgência e a secção de Justificativas do pedido.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>2. Decisão da Área:</strong> Confira o fornecedor vencedor e os itens aprovados pela área.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>3. Valores Finais:</strong> Verifique o Resumo Financeiro e compare com o Contexto Financeiro Visual em busca de padrões anormais.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>4. Atribuição Financeira:</strong> Confirme que cada item possui Planta e Centro de Custo coerentes com o orçamento da área.</li>
                                                <li style={{ marginBottom: '8px' }}><strong>5. Alertas Orçamentais:</strong> Se houver justificativa orçamental da área (em Inteligência para Decisão → Disponibilidade Orçamental), leia-a antes de decidir.</li>
                                                <li><strong>6. Decisão:</strong> Use a barra inferior para Aprovar, Rejeitar ou Solicitar Reajuste. Para rejeição ou reajuste, o comentário é obrigatório.</li>
                                            </ol>
                                        </div>
                                    </div>
                                </>
                            )}

                        </div>
                    </div>
                </div>
            )}
        </motion.div>
    );
}
