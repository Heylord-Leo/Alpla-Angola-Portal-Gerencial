import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { RequestDetailsDto, SavedQuotationDto, ApprovalIntelligenceDto } from '../../types';
import { ApprovalActionType } from '../../components/ApprovalModal';
import { X, ChevronRight, ChevronLeft, Check, Eye, Landmark, Scale, ClipboardCheck, Calculator, CheckCircle2 } from 'lucide-react';
import { ConfirmDiscardModal } from '../../components/ui/ConfirmDiscardModal';
import { WizardStepOverview } from './WizardStepOverview';
import { WizardStepAllocation, ItemAllocation, ItemAssignment } from './WizardStepAllocation';
import { WizardStepSelection } from './WizardStepSelection';
import { WizardStepBatchReview } from './WizardStepBatchReview';
import { WizardStepReview } from './WizardStepReview';
import { WizardStepBudget, AllocationReassignmentDto } from './WizardStepBudget';
import { useWizardValidation } from './hooks/useWizardValidation';
import { buildSelectionsPayload } from './candidateSelection';

interface ApprovalWizardModalProps {
    isOpen: boolean;
    onClose: () => void;
    request: RequestDetailsDto | null;
    quotations: SavedQuotationDto[];
    plants: { id: number, name: string }[];
    costCenters: { id: number, code: string, name: string, plantId?: number }[];
    onSubmitAction: (
        action: ApprovalActionType,
        awards: Record<string, string>,
        assignments: Record<string, ItemAssignment>,
        comment: string,
        budgetJustification?: string,
        reassignments?: AllocationReassignmentDto[],
        allocations?: Record<string, ItemAllocation[]>,
        extraItemDecisions?: Record<string, { decision: 'APPROVE' | 'REJECT' | 'ADJUST' | null; comment: string }>,
        /** Candidate model (Area batch approval): the winner decision per ApprovalBatchItem —
         * identity + optional justification only, never commercial values. */
        selections?: { approvalBatchItemId: string; selectedCandidateId: string; winnerSelectionJustification?: string }[]
    ) => Promise<boolean>;
    isSubmitting: boolean;
    onDownloadAttachment?: (attachmentId: string, fileName: string) => void;
    intelligence?: ApprovalIntelligenceDto | null;
    approvalStage?: 'AREA' | 'FINAL';
    activeBatch?: any | null;
}

type WizardStepKey = 'OVERVIEW' | 'ALLOCATION' | 'COMPARISON' | 'SINGLE_QUOTE_REVIEW' | 'BUDGET' | 'REVIEW';

export const ApprovalWizardModal: React.FC<ApprovalWizardModalProps> = ({
    isOpen,
    onClose,
    request,
    quotations,
    plants,
    costCenters,
    onSubmitAction,
    isSubmitting,
    onDownloadAttachment,
    intelligence,
    approvalStage,
    activeBatch
}) => {
    const [currentStep, setCurrentStep] = useState(1);
    const [itemAwards, setItemAwards] = useState<Record<string, string>>({});
    const [itemAssignments, setItemAssignments] = useState<Record<string, ItemAssignment>>({});
    const [itemAllocations, setItemAllocations] = useState<Record<string, ItemAllocation[]>>({});
    const [extraItemDecisions, setExtraItemDecisions] = useState<Record<string, { decision: 'APPROVE' | 'REJECT' | 'ADJUST' | null; comment: string }>>({});
    const [comment, setComment] = useState('');
    const [confirmReview, setConfirmReview] = useState(false);
    const [budgetJustification, setBudgetJustification] = useState('');
    const [isBudgetJustificationRequired, setIsBudgetJustificationRequired] = useState(false);
    const [stepValidationError, setStepValidationError] = useState<string | null>(null);
    const [reassignments, setReassignments] = useState<AllocationReassignmentDto[]>([]);
    const [budgetPreview, setBudgetPreview] = useState<any>(null);

    // ── Candidate model (Area winner selection) — LOCAL wizard state only; nothing persists
    // before the approval submit. Keyed by ApprovalBatchItemId. ──
    const [batchSelections, setBatchSelections] = useState<Record<string, string>>({});
    const [selectionJustifications, setSelectionJustifications] = useState<Record<string, string>>({});
    const [showSelectionValidation, setShowSelectionValidation] = useState(false);

    const handlePreviewLoaded = useCallback((preview: any) => {
        setBudgetPreview(preview);
    }, []);

    const handleJustificationRequiredChange = useCallback((required: boolean) => {
        setIsBudgetJustificationRequired(prev => {
            if (prev !== required) return required;
            return prev;
        });
    }, []);

    const isPayment = request?.requestTypeCode === 'PAYMENT';
    const isSingleQuotation = !isPayment && quotations.length === 1;
    const isBatchReviewMode = Boolean(activeBatch);
    // Candidate model: at least one batch item carries buyer-submitted candidate options —
    // the review step becomes the Area winner-selection step.
    const isCandidateBatch = Boolean(activeBatch?.items?.some((bi: any) => (bi.candidates?.length ?? 0) > 0));

    const dynamicSteps = useMemo(() => {
        const steps = [
            { key: 'OVERVIEW' as WizardStepKey, title: 'Visão Geral', icon: Eye },
            { key: 'ALLOCATION' as WizardStepKey, title: 'Atribuição Financeira', icon: Landmark }
        ];

        if (!isPayment) {
            if (isBatchReviewMode) {
                steps.push({ key: 'SINGLE_QUOTE_REVIEW' as WizardStepKey, title: isCandidateBatch ? 'Seleção do Vencedor' : 'Cotação Selecionada', icon: Scale });
            } else if (isSingleQuotation) {
                steps.push({ key: 'SINGLE_QUOTE_REVIEW' as WizardStepKey, title: 'Cotação Única', icon: Scale });
            } else {
                steps.push({ key: 'COMPARISON' as WizardStepKey, title: 'Comparação e Seleção', icon: Scale });
            }
        }

        steps.push({ key: 'BUDGET' as WizardStepKey, title: 'Disponibilidade Orçamental', icon: Calculator });
        steps.push({ key: 'REVIEW' as WizardStepKey, title: 'Revisão Final', icon: ClipboardCheck });

        return steps;
    }, [isPayment, isSingleQuotation, isBatchReviewMode, isCandidateBatch]);

    const totalSteps = dynamicSteps.length;
    const currentStepConfig = dynamicSteps[currentStep - 1] || dynamicSteps[0];

    // Track if user has made any changes for discard confirmation
    const [hasInteracted, setHasInteracted] = useState(false);
    const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);

    // Initialize state when modal opens or request changes
    useEffect(() => {
        if (isOpen && request) {
            const initialAwards: Record<string, string> = {};
            const initialAssignments: Record<string, ItemAssignment> = {};
            const initialAllocations: Record<string, ItemAllocation[]> = {};

            // Auto-selection for single quotation scenarios
            const autoSelect = !isPayment && quotations.length === 1;
            const singleQuotation = autoSelect ? quotations[0] : null;

            request.lineItems?.forEach((item: any) => {
                if (!item.isDeleted) {
                    if (item.selectedQuotationItemId) {
                        initialAwards[item.id] = item.selectedQuotationItemId;
                    } else if (autoSelect && singleQuotation && singleQuotation.items) {
                        const matchedItem = singleQuotation.items.find(qi => qi.mappedRequestLineItemId === item.id);
                        // Option C: never auto-select a cancelled-batch item without reuse authorization
                        if (matchedItem && !matchedItem.isReuseBlocked) {
                            initialAwards[item.id] = matchedItem.id;
                        }
                    }
                    
                    initialAssignments[item.id] = {
                        plantId: item.plantId,
                        costCenterId: item.costCenterId
                    };

                    if (item.allocations && item.allocations.length > 0) {
                        initialAllocations[item.id] = item.allocations.map((a: any) => ({
                            id: a.id || Math.random().toString(36).substring(7),
                            plantId: a.plantId,
                            costCenterId: a.costCenterId,
                            percentage: a.percentage
                        }));
                    } else if (item.plantId && item.costCenterId) {
                        initialAllocations[item.id] = [{
                            id: Math.random().toString(36).substring(7),
                            plantId: item.plantId,
                            costCenterId: item.costCenterId,
                            percentage: 100
                        }];
                    } else {
                        initialAllocations[item.id] = [];
                    }
                }
            });

            setItemAwards(initialAwards);
            setItemAssignments(initialAssignments);
            setItemAllocations(initialAllocations);
            setExtraItemDecisions({});
            setCurrentStep(1);
            setComment('');
            setConfirmReview(false);
            setBudgetJustification('');
            setIsBudgetJustificationRequired(false);
            setHasInteracted(false);
            setStepValidationError(null);
            setReassignments([]);
            // Candidate model: a (re)opened wizard NEVER restores a previous Area selection —
            // a returned batch must be decided again from scratch (history keeps the old decision).
            setBatchSelections({});
            setSelectionJustifications({});
            setShowSelectionValidation(false);
        }
    }, [isOpen, request]);

    const activeItems = request?.lineItems?.filter((i: any) => !i.isDeleted) || [];
    const extraItemsCount = useMemo(() => quotations.reduce((acc, q) => acc + (q.items?.filter(qi => qi.reconciliationStatus === 'EXTRA_ITEM').length || 0), 0), [quotations]);
    
    const approvedExtraItems = useMemo(() => {
        const approved: any[] = [];
        quotations.forEach(q => {
            q.items?.forEach(qi => {
                if (qi.reconciliationStatus === 'EXTRA_ITEM' && extraItemDecisions[qi.id]?.decision === 'APPROVE') {
                    approved.push({
                        id: qi.id, // Using quotation item id as line item id for allocation tracking
                        lineNumber: 900 + approved.length, // Fake line number
                        itemPriority: 'MEDIUM',
                        description: `[Item Adicional] ${qi.description}`,
                        quantity: qi.quantity,
                        unit: qi.unitCode || qi.unitName || 'UN',
                        unitPrice: qi.unitPrice,
                        totalAmount: qi.lineTotal,
                        supplierName: q.supplierNameSnapshot,
                        isExtraItem: true,
                        // Provide quotation item ID specifically for the backend to recognize it
                        selectedQuotationItemId: qi.id
                    });
                }
            });
        });
        return approved;
    }, [quotations, extraItemDecisions]);

    // --- Validation ---
    const {
        isStep2Valid,
        isStep3Valid,
        isStep4Valid,
        step2AssignedCount,
        step3AwardedCount
    } = useWizardValidation(
        activeItems,
        itemAllocations,
        itemAwards,
        budgetJustification,
        isBudgetJustificationRequired,
        isPayment,
        extraItemDecisions,
        extraItemsCount,
        approvedExtraItems,
        isBatchReviewMode,
        quotations,
        activeBatch,
        batchSelections,
        selectionJustifications
    );

    // Lock background document scroll while the approval wizard is open
    useEffect(() => {
        if (!isOpen) return;

        const previousOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';

        return () => {
            document.body.style.overflow = previousOverflow;
        };
    }, [isOpen]);

    // --- Close handling ---
    if (!isOpen || !request) return null;

    const handleCloseAttempt = () => {
        if (hasInteracted) {
            setShowDiscardConfirm(true);
        } else {
            onClose();
        }
    };

    const handleConfirmDiscard = () => {
        setShowDiscardConfirm(false);
        onClose();
    };

    const handleCancelDiscard = () => {
        setShowDiscardConfirm(false);
    };

    // --- Navigation ---
    const handleNext = () => {
        setStepValidationError(null);
        const currentKey = currentStepConfig.key;

        if (currentKey === 'ALLOCATION' && !isStep2Valid()) {
            setStepValidationError('Todos os itens devem ter Planta e Centro de Custo atribuídos para prosseguir.');
            return;
        }
        if (currentKey === 'COMPARISON' && !isStep3Valid()) {
            setStepValidationError('Todos os itens devem ter uma cotação vencedora selecionada para prosseguir.');
            return;
        }
        if (currentKey === 'COMPARISON' && !isStep2Valid()) {
            setStepValidationError('Você aprovou itens adicionais que não possuem alocação financeira. Volte à etapa de Atribuição Financeira para alocá-los.');
            return;
        }
        if (currentKey === 'SINGLE_QUOTE_REVIEW' && !isStep3Valid()) {
            if (isCandidateBatch) {
                // All-or-return: highlight undecided items inline, count them in the banner.
                setShowSelectionValidation(true);
                const batchItems: any[] = activeBatch?.items || [];
                const pendingCount = batchItems.filter((bi: any) =>
                    (bi.candidates?.length ?? 0) > 0 &&
                    (!batchSelections[bi.id] || !bi.candidates.some((c: any) => c.id === batchSelections[bi.id]))
                ).length;
                setStepValidationError(pendingCount > 0
                    ? `Selecione exatamente uma opção vencedora para cada item do lote. Decisões pendentes: ${pendingCount}. Se algum item não puder ser decidido, use "Solicitar Reajuste".`
                    : 'Existe uma escolha acima do menor valor sem justificativa válida (mín. 20 caracteres significativos).');
                return;
            }
            setStepValidationError('Não foi possível associar automaticamente todos os itens da cotação aos itens do pedido. Revise o mapeamento antes de continuar.');
            return;
        }
        if (currentKey === 'SINGLE_QUOTE_REVIEW' && !isStep2Valid()) {
            setStepValidationError('Você aprovou itens adicionais que não possuem alocação financeira. Volte à etapa de Atribuição Financeira para alocá-los.');
            return;
        }
        if (currentKey === 'BUDGET' && !isStep4Valid()) {
            setStepValidationError('O impacto orçamental é crítico. Forneça uma justificativa detalhada (mín. 20 caracteres) para prosseguir.');
            return;
        }

        setCurrentStep(prev => Math.min(prev + 1, totalSteps));
    };

    const handleBack = () => {
        setStepValidationError(null);
        setCurrentStep(prev => Math.max(prev - 1, 1));
    };

    // --- Submission ---
    const handleSubmit = async (action: ApprovalActionType) => {
        // All final actions require confirmation checkbox
        if (!confirmReview) {
            setStepValidationError('Marque a confirmação de revisão antes de prosseguir.');
            return;
        }

        if (action === 'APPROVE') {
            const hasInvalidAward = !isPayment && !isStep3Valid();
            if (!isStep2Valid() || hasInvalidAward || !isStep4Valid()) {
                if (isCandidateBatch && hasInvalidAward) setShowSelectionValidation(true);
                setStepValidationError('Não é possível aprovar. Existem pendências nas etapas anteriores.');
                return;
            }
        }

        if ((action === 'REJECT' || action === 'REQUEST_ADJUSTMENT') && !comment.trim()) {
            setStepValidationError('O comentário é obrigatório para Rejeição ou Solicitação de Reajuste.');
            return;
        }

        setStepValidationError(null);
        try {
            // Candidate model: the Area winner decision travels ONLY as identity + optional
            // justification — the backend re-validates and resolves the frozen snapshots.
            const selections = (action === 'APPROVE' && isCandidateBatch)
                ? buildSelectionsPayload(activeBatch?.items || [], batchSelections, selectionJustifications)
                : undefined;
            const success = await onSubmitAction(action, itemAwards, itemAssignments, comment, budgetJustification, reassignments, itemAllocations, extraItemDecisions, selections);
            if (success) {
                // Modal closes automatically on success via the parent
            }
        } catch (err) {
            setStepValidationError('Ocorreu um erro ao processar a solicitação.');
        }
    };

    // ── Candidate model: Area winner selection handlers (local state only). Selecting a winner
    // also mirrors it into itemAwards so downstream display steps (Allocation/Review) keep
    // resolving quotation data without change; the SUBMITTED authority is always Selections. ──
    const handleBatchCandidateSelect = (approvalBatchItemId: string, candidateId: string, requestLineItemId: string, quotationItemId: string) => {
        setHasInteracted(true);
        setBatchSelections(prev => ({ ...prev, [approvalBatchItemId]: candidateId }));
        setItemAwards(prev => ({ ...prev, [requestLineItemId]: quotationItemId }));
    };

    const handleSelectionJustificationChange = (approvalBatchItemId: string, text: string) => {
        setHasInteracted(true);
        setSelectionJustifications(prev => ({ ...prev, [approvalBatchItemId]: text }));
    };

    // --- Award helpers ---
    const handleAwardChange = (requestLineItemId: string, quotationItemId: string) => {
        setHasInteracted(true);
        setItemAwards(prev => ({ ...prev, [requestLineItemId]: quotationItemId }));
    };

    const handleExtraItemDecisionChange = (quotationItemId: string, decision: { decision: 'APPROVE' | 'REJECT' | 'ADJUST' | null; comment: string }) => {
        setHasInteracted(true);
        setExtraItemDecisions(prev => ({ ...prev, [quotationItemId]: decision }));
    };

    // Option C: a quotation item used in a CANCELLED batch without an active reuse authorization
    // is NOT a selectable candidate anywhere in this wizard (radio, select-all, best-price).
    // The backend annotation (isReuseBlocked) is authoritative; the API also rejects with 409.
    const isSelectableCandidate = (qi: any): boolean =>
        !!qi &&
        (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
        !qi.isReuseBlocked;

    const handleSelectAllFromQuotation = (quotationId: string) => {
        setHasInteracted(true);
        const quotation = quotations.find(q => q.id === quotationId);
        if (!quotation || !quotation.items) return;
        const newAwards = { ...itemAwards };
        activeItems.forEach((item: any) => {
            const qItem = quotation.items.find(qi => qi.mappedRequestLineItemId === item.id);
            if (isSelectableCandidate(qItem) && qItem!.unitPrice > 0) {
                newAwards[item.id] = qItem!.id;
            }
        });
        setItemAwards(newAwards);
    };

    const handleSelectLowestPrices = () => {
        setHasInteracted(true);
        const newAwards = { ...itemAwards };
        activeItems.forEach((item: any) => {
            let lowestPrice: number | null = null;
            let lowestQItemId: string | null = null;
            quotations.forEach(q => {
                const qItem = q.items?.find(qi => qi.mappedRequestLineItemId === item.id);
                if (isSelectableCandidate(qItem) && qItem!.unitPrice > 0) {
                    if (lowestPrice === null || qItem!.unitPrice < lowestPrice) {
                        lowestPrice = qItem!.unitPrice;
                        lowestQItemId = qItem!.id;
                    }
                }
            });
            if (lowestQItemId) {
                newAwards[item.id] = lowestQItemId;
            }
        });
        setItemAwards(newAwards);
    };

    const handleAllocationChange = (itemId: string, newAllocations: ItemAllocation[]) => {
        setHasInteracted(true);
        setItemAllocations(prev => ({ ...prev, [itemId]: newAllocations }));
    };

    const handleBulkFillAllocations = (sourceItemId: string) => {
        setHasInteracted(true);
        const sourceAllocations = itemAllocations[sourceItemId];
        if (!sourceAllocations || sourceAllocations.length === 0) return;

        const newMap = { ...itemAllocations };
        activeItems.forEach((item: any) => {
            const currentAllocations = newMap[item.id] || [];
            const isAssigned = currentAllocations.length > 0 && 
                currentAllocations.reduce((sum, a) => sum + (a.percentage || 0), 0) === 100 && 
                currentAllocations.every(a => a.plantId && a.costCenterId && a.percentage > 0);
            
            if (!isAssigned) {
                newMap[item.id] = sourceAllocations.map(a => ({
                    ...a,
                    id: Math.random().toString(36).substring(7)
                }));
            }
        });
        setItemAllocations(newMap);
    };

    return (
        <>
        <div
            /* Backdrop does not close — only X button triggers close */
            style={{
                position: 'fixed',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.6)',
                backdropFilter: 'blur(4px)',
                zIndex: 9999,
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                padding: '24px'
            }}
        >
            <div
                className="wizard-modal-container"
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#F3F4F6',
                    borderRadius: '12px',
                    width: '100%',
                    maxWidth: 'min(1200px, calc(100vw - 48px))',
                    display: 'flex',
                    flexDirection: 'column',
                    boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                    overflow: 'hidden'
                }}
            >
                {/* ═══════════════ STICKY HEADER ═══════════════ */}
                <div style={{
                    padding: '16px 24px',
                    backgroundColor: '#FFFFFF',
                    borderBottom: '1px solid #E5E7EB',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '16px',
                    flexShrink: 0
                }}>
                    {/* Top row: Title + Close */}
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                            <div style={{
                                width: '40px', height: '40px', borderRadius: '8px',
                                backgroundColor: '#EFF6FF',
                                display: 'flex', alignItems: 'center', justifyContent: 'center',
                                color: '#2563EB', fontWeight: 700, fontSize: '0.75rem'
                            }}>
                                {request.requestNumber?.split('-').pop() || '?'}
                            </div>
                            <div>
                                <h2 style={{ fontSize: '1.125rem', fontWeight: 700, color: '#111827', margin: 0 }}>
                                    Aprovação de Área — {request.requestNumber}
                                </h2>
                            </div>
                        </div>
                        <button
                            onClick={handleCloseAttempt}
                            style={{
                                background: 'none', border: 'none', cursor: 'pointer', padding: '8px',
                                color: '#6B7280', borderRadius: '50%', display: 'flex', alignItems: 'center',
                                transition: 'all 0.15s'
                            }}
                            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#F3F4F6'; e.currentTarget.style.color = '#111827'; }}
                            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'transparent'; e.currentTarget.style.color = '#6B7280'; }}
                            title="Fechar"
                        >
                            <X size={22} />
                        </button>
                    </div>

                    {/* Stepper */}
                    <div style={{ display: 'flex', alignItems: 'center', width: '100%' }}>
                        {dynamicSteps.map((step: { key: WizardStepKey; title: string; icon: any }, idx: number) => {
                            const isCompleted = currentStep > idx + 1;
                            const isCurrent = currentStep === idx + 1;
                            const StepIcon = step.icon;

                            const isStepValid = (key: WizardStepKey): boolean => {
                                switch (key) {
                                    case 'OVERVIEW': return true;
                                    case 'ALLOCATION': return isStep2Valid();
                                    case 'COMPARISON':
                                    case 'SINGLE_QUOTE_REVIEW': return isStep3Valid();
                                    case 'BUDGET': return isStep4Valid();
                                    case 'REVIEW': return confirmReview;
                                    default: return false;
                                }
                            };
                            
                            const valid = isStepValid(step.key);

                            return (
                                <React.Fragment key={step.key}>
                                    <div 
                                        style={{ 
                                            display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px',
                                            width: '80px', position: 'relative', zIndex: 10,
                                            cursor: isCompleted || valid ? 'pointer' : 'default',
                                            opacity: (isCompleted || isCurrent || valid) ? 1 : 0.6,
                                            transition: 'opacity 0.2s'
                                        }}
                                        onClick={() => {
                                            if (isCompleted || valid) setCurrentStep(idx + 1);
                                        }}
                                    >
                                        <div style={{
                                            width: '36px', height: '36px', borderRadius: '50%',
                                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            backgroundColor: isCompleted ? '#10B981' : isCurrent ? '#2563EB' : '#E5E7EB',
                                            color: isCompleted || isCurrent ? '#FFFFFF' : '#9CA3AF',
                                            fontWeight: 600, fontSize: '0.875rem',
                                            transition: 'all 0.3s ease',
                                            boxShadow: isCurrent ? '0 0 0 4px rgba(37, 99, 235, 0.15)' : 'none'
                                        }}>
                                            {isCompleted ? <Check size={16} strokeWidth={3} /> : <StepIcon size={16} />}
                                        </div>
                                        <span style={{
                                            fontSize: '0.6875rem', fontWeight: isCurrent ? 700 : 500,
                                            color: isCurrent ? '#111827' : (isCompleted || valid) ? '#374151' : '#9CA3AF',
                                            textAlign: 'center', lineHeight: 1.2,
                                            display: 'flex', alignItems: 'center', gap: '4px', justifyContent: 'center'
                                        }}>
                                            {step.title}
                                            {valid ? (
                                                <CheckCircle2 size={12} color="#10B981" />
                                            ) : (
                                                <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: '#D1D5DB' }} />
                                            )}
                                        </span>
                                    </div>
                                    {idx < dynamicSteps.length - 1 && (
                                        <div style={{
                                            height: '2px', flex: '1',
                                            backgroundColor: isCompleted ? '#10B981' : '#E5E7EB',
                                            margin: '0 -16px 22px -16px',
                                            transition: 'background-color 0.3s'
                                        }} />
                                    )}
                                </React.Fragment>
                            );
                        })}
                    </div>
                </div>

                {/* ═══════════════ SCROLLABLE BODY ═══════════════ */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '28px 36px', backgroundColor: '#F9FAFB' }}>
                    {/* Step validation error banner */}
                    {stepValidationError && (
                        <div style={{
                            marginBottom: '20px', padding: '12px 16px',
                            backgroundColor: '#FEF2F2', border: '1px solid #FECACA', borderRadius: '8px',
                            color: '#991B1B', fontSize: '0.875rem', fontWeight: 600,
                            display: 'flex', alignItems: 'center', gap: '8px'
                        }}>
                            <span style={{ fontSize: '1.1rem' }}>⚠</span>
                            {stepValidationError}
                        </div>
                    )}

                    {currentStepConfig.key === 'OVERVIEW' && (
                        <WizardStepOverview
                            request={request}
                            onDownloadAttachment={onDownloadAttachment}
                            intelligence={intelligence}
                            approvalStage={approvalStage}
                        />
                    )}

                    {currentStepConfig.key === 'ALLOCATION' && (
                        <WizardStepAllocation
                            request={request}
                            itemAllocations={itemAllocations}
                            onChangeAllocations={handleAllocationChange}
                            onBulkFill={handleBulkFillAllocations}
                            plants={plants}
                            costCenters={costCenters}
                            assignedCount={step2AssignedCount}
                            totalCount={activeItems.length + approvedExtraItems.length}
                            itemAwards={itemAwards}
                            itemAssignments={itemAssignments}
                            extraItems={approvedExtraItems}
                        />
                    )}

                    {(currentStepConfig.key === 'COMPARISON' || currentStepConfig.key === 'SINGLE_QUOTE_REVIEW') && (
                        isBatchReviewMode ? (
                            <WizardStepBatchReview
                                request={request}
                                quotations={quotations}
                                activeBatch={activeBatch}
                                onDownloadAttachment={onDownloadAttachment}
                                selections={batchSelections}
                                selectionJustifications={selectionJustifications}
                                onSelectCandidate={handleBatchCandidateSelect}
                                onJustificationChange={handleSelectionJustificationChange}
                                showSelectionValidation={showSelectionValidation}
                            />
                        ) : (
                            <WizardStepSelection
                                request={request}
                                quotations={quotations}
                                itemAwards={itemAwards}
                                onChangeAward={handleAwardChange}
                                onSelectAll={handleSelectAllFromQuotation}
                                onSelectLowestPrices={handleSelectLowestPrices}
                                awardedCount={step3AwardedCount}
                                totalCount={activeItems.length}
                                isSingleQuotation={isSingleQuotation}
                                extraItemDecisions={extraItemDecisions}
                                onChangeExtraItemDecision={handleExtraItemDecisionChange}
                            />
                        )
                    )}

                    {currentStepConfig.key === 'BUDGET' && (
                        <WizardStepBudget
                            requestId={request.id}
                            batchId={activeBatch?.id ?? null}
                            batchSelections={isCandidateBatch
                                ? Object.entries(batchSelections).map(([approvalBatchItemId, selectedCandidateId]) => ({ approvalBatchItemId, selectedCandidateId }))
                                : undefined}
                            itemAwards={itemAwards}
                            itemAssignments={itemAssignments}
                            itemAllocations={itemAllocations}
                            onChangeItemAllocations={(allocs) => { setItemAllocations(allocs as Record<string, ItemAllocation[]>); setHasInteracted(true); }}
                            budgetJustification={budgetJustification}
                            onChangeBudgetJustification={(val) => { setBudgetJustification(val); setHasInteracted(true); }}
                            onJustificationRequiredChange={handleJustificationRequiredChange}
                            onPreviewLoaded={handlePreviewLoaded}
                            reassignments={reassignments}
                            onChangeReassignments={(vals) => { setReassignments(vals); setHasInteracted(true); }}
                            onChangeItemAssignments={(assignments) => { setItemAssignments(assignments); setHasInteracted(true); }}
                            extraItemDecisions={extraItemDecisions}
                        />
                    )}

                    {currentStepConfig.key === 'REVIEW' && (
                        <WizardStepReview
                            request={request}
                            quotations={quotations}
                            itemAwards={itemAwards}
                            itemAssignments={itemAssignments}
                            itemAllocations={itemAllocations}
                            comment={comment}
                            onChangeComment={(c) => { setComment(c); setHasInteracted(true); }}
                            plants={plants}
                            costCenters={costCenters}
                            confirmReview={confirmReview}
                            onChangeConfirmReview={(v) => { setConfirmReview(v); setHasInteracted(true); }}
                            isStep2Valid={isStep2Valid()}
                            isStep3Valid={isStep3Valid()}
                            isStep4Valid={isStep4Valid()}
                            extraItems={approvedExtraItems}
                            budgetJustification={budgetJustification}
                            budgetPreview={budgetPreview}
                        />
                    )}
                </div>

                {/* ═══════════════ STICKY FOOTER ═══════════════ */}
                <div style={{
                    padding: '14px 24px',
                    backgroundColor: '#FFFFFF',
                    borderTop: '1px solid #E5E7EB',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    flexShrink: 0
                }}>
                    {/* Left: Back */}
                    <button
                        onClick={handleBack}
                        disabled={currentStep === 1 || isSubmitting}
                        style={{
                            padding: '10px 20px', borderRadius: '8px',
                            display: 'flex', alignItems: 'center', gap: '8px',
                            backgroundColor: '#FFFFFF', border: '1px solid #D1D5DB',
                            color: currentStep === 1 ? '#D1D5DB' : '#374151',
                            cursor: currentStep === 1 || isSubmitting ? 'not-allowed' : 'pointer',
                            fontWeight: 600, fontSize: '0.875rem',
                            transition: 'all 0.15s'
                        }}
                    >
                        <ChevronLeft size={18} /> Voltar
                    </button>

                    {/* Right: Actions */}
                    <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                        {/* Step counter */}
                        <span style={{ fontSize: '0.75rem', color: '#9CA3AF', fontWeight: 500, marginRight: '8px' }}>
                            Etapa {currentStep} de {totalSteps}
                        </span>

                        {currentStep === totalSteps ? (
                            <>
                                <button
                                    onClick={() => handleSubmit('REJECT')}
                                    disabled={isSubmitting || !confirmReview}
                                    style={{
                                        padding: '10px 20px', borderRadius: '8px',
                                        backgroundColor: '#FEF2F2', border: '1px solid #FCA5A5', color: '#DC2626',
                                        cursor: isSubmitting || !confirmReview ? 'not-allowed' : 'pointer',
                                        fontWeight: 600, fontSize: '0.8125rem', transition: 'all 0.15s',
                                        opacity: !confirmReview ? 0.5 : 1
                                    }}
                                >
                                    Rejeitar
                                </button>
                                {request?.requestTypeCode !== 'PAYMENT' && (
                                <button
                                    onClick={() => handleSubmit('REQUEST_ADJUSTMENT')}
                                    disabled={isSubmitting || !confirmReview}
                                    style={{
                                        padding: '10px 20px', borderRadius: '8px',
                                        backgroundColor: '#FFFBEB', border: '1px solid #FCD34D', color: '#D97706',
                                        cursor: isSubmitting || !confirmReview ? 'not-allowed' : 'pointer',
                                        fontWeight: 600, fontSize: '0.8125rem', transition: 'all 0.15s',
                                        opacity: !confirmReview ? 0.5 : 1
                                    }}
                                >
                                    Solicitar Reajuste
                                </button>
                                )}
                                <button
                                    onClick={() => handleSubmit('APPROVE')}
                                    disabled={isSubmitting || !isStep2Valid() || !isStep3Valid() || !isStep4Valid() || !confirmReview}
                                    style={{
                                        padding: '10px 28px', borderRadius: '8px',
                                        backgroundColor: '#10B981', border: 'none', color: '#FFFFFF',
                                        cursor: isSubmitting || !isStep2Valid() || !isStep3Valid() || !isStep4Valid() || !confirmReview ? 'not-allowed' : 'pointer',
                                        fontWeight: 700, fontSize: '0.875rem',
                                        opacity: isSubmitting || !isStep2Valid() || !isStep3Valid() || !isStep4Valid() || !confirmReview ? 0.5 : 1,
                                        transition: 'all 0.15s',
                                        boxShadow: '0 2px 8px rgba(16, 185, 129, 0.3)'
                                    }}
                                >
                                    {isSubmitting ? 'Processando...' : '👍 Aprovar Pedido'}
                                </button>
                            </>
                        ) : (
                            <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                                {(['COMPARISON', 'SINGLE_QUOTE_REVIEW'].includes(currentStepConfig.key)) && approvedExtraItems.length > 0 && !isStep2Valid() && (
                                    <span style={{ color: '#DC2626', fontSize: '0.8125rem', fontWeight: 600 }}>
                                        ⚠️ Existem itens adicionais aprovados sem alocação financeira. (Volte à Etapa 2)
                                    </span>
                                )}
                                <button
                                    onClick={handleNext}
                                    style={{
                                        padding: '10px 28px', borderRadius: '8px',
                                        display: 'flex', alignItems: 'center', gap: '8px',
                                        backgroundColor: '#2563EB', border: 'none', color: '#FFFFFF',
                                        cursor: 'pointer', fontWeight: 600, fontSize: '0.875rem',
                                        transition: 'all 0.15s',
                                        boxShadow: '0 2px 8px rgba(37, 99, 235, 0.25)'
                                    }}
                                >
                                    Próximo <ChevronRight size={18} />
                                </button>
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
        <ConfirmDiscardModal
            isOpen={showDiscardConfirm}
            onConfirm={handleConfirmDiscard}
            onCancel={handleCancelDiscard}
        />
        </>
    );
};
