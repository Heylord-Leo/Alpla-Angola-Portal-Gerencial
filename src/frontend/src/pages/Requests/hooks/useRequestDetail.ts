
import { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate, useParams, useLocation, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../../../lib/api';
import { useAuth } from '../../../features/auth/AuthContext';
import { ROLES } from '../../../constants/roles';
import { FeedbackType } from '../../../components/ui/Feedback';
import { ApprovalActionType } from '../../../components/ApprovalModal';
import { scrollToFirstError } from '../../../lib/validation';
import { completeQuotationAction } from '../../../lib/workflow';
import { isFiscalDocument, isSelectableDocumentType } from '../../../lib/sourceDocumentType';
import {
    CONFLICT_JUSTIFICATION_MIN_LENGTH,
    ClassificationConflictState,
    EMPTY_CONFLICT,
    OcrDocumentClassification,
    buildClassificationPayload,
    canConfirmConflict,
    evaluateClassificationConflict
} from '../../../lib/documentClassificationDecision';

/** Rehydrates the stored evidence blob, tolerating anything that is not the shape we wrote. */
function parseClassificationEvidence(json?: string | null): Partial<OcrDocumentClassification> {
    if (!json) return {};
    try {
        const parsed = JSON.parse(json);
        return parsed && typeof parsed === 'object' ? parsed as Partial<OcrDocumentClassification> : {};
    } catch {
        return {};
    }
}
import { useFeatureFlags } from '../../../hooks/useFeatureFlags';
import { useCatalogItemReconciliation } from '../../../hooks/useCatalogItemReconciliation';
import {
    DocumentScopedItem,
    documentLabelsByIndex,
    equivalentUnresolvedIndexes,
    flattenPersistedLineItems,
    propagateEquivalentResolutions,
    unresolvedItems
} from '../../../lib/multiDocumentCatalogReconciliation';
import { CurrencyDto, ItemResolution, LookupDto, RequestStatusHistoryDto, RequestAttachmentDto, RequestLineItemDto, SavedQuotationDto } from '../../../types';

export function useRequestDetail({ id: propsId, onClose }: { id?: string, onClose?: () => void } = {}) {
    const navigate = useNavigate();
    const params = useParams<{ id: string }>();
    const id = propsId || params.id;
    const location = useLocation();
    const [searchParams] = useSearchParams();

    const { user } = useAuth();
    // Post-Payment Completion (Release 2). Defaults to every flag off, so the screen renders its
    // pre-feature layout until the server says otherwise.
    const { flags: featureFlags } = useFeatureFlags();
    
    // Derived Configuration
    const copyFromId = searchParams.get('copyFrom');
    const isCopyMode = !!copyFromId && !id;

    // --- State Declarations ---

    // UI/Loading State
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [submitting, setSubmitting] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    
    // Status & Identity
    const [status, setStatus] = useState<string | null>(null);
    const [statusFullName, setStatusFullName] = useState<string | null>(null);
    const [statusBadgeColor, setStatusBadgeColor] = useState<string | null>(null);
    const [requestTypeCode, setRequestTypeCode] = useState<string | null>(null);
    const [requestNumber, setRequestNumber] = useState<string | null>(null);
    const [requesterId, setRequesterId] = useState<string | null>(null);
    
    // Form Data
    const [formData, setFormData] = useState({
        title: '',
        description: '',
        requestTypeId: 1,
        needLevelId: '',
        estimatedTotalAmount: '',
        discountAmount: '',
        currencyId: 1,
        departmentId: '',
        companyId: '',
        plantId: '',
        needByDateUtc: '',
        buyerId: '',
        capexOpexClassificationId: '',
        supplierId: '',
        areaApproverId: '',
        finalApproverId: '',
        // Post-Payment Completion (Release 2): PROFORMA | FINAL_INVOICE | '' (unclassified).
        sourceDocumentType: ''
    });
    const [initialFormData, setInitialFormData] = useState<any>(null);
    // What the document was read as, and the answer given to any contradiction. Restored from the
    // saved request so an edit judges the classification against the same evidence as creation did.
    const [documentClassification, setDocumentClassification] =
        useState<OcrDocumentClassification | null>(null);
    const [classificationConflict, setClassificationConflict] =
        useState<ClassificationConflictState>(EMPTY_CONFLICT);
    const [supplierName, setSupplierName] = useState('');
    const [supplierPortalCode, setSupplierPortalCode] = useState('');

    // Items & Quotations
    const [lineItems, setLineItems] = useState<RequestLineItemDto[]>([]);
    const [itemForm, setItemForm] = useState<Partial<RequestLineItemDto> | null>(null);
    const [itemSaving, setItemSaving] = useState(false);
    const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
    const [statusHistory, setStatusHistory] = useState<RequestStatusHistoryDto[]>([]);
    const [attachments, setAttachments] = useState<RequestAttachmentDto[]>([]);
    const [quotations, setQuotations] = useState<SavedQuotationDto[]>([]);
    const [selectedQuotationId, setSelectedQuotationId] = useState<string | null>(null);
    const [poGroups, setPoGroups] = useState<any[]>([]);

    // Master Data
    const [units, setUnits] = useState<LookupDto[]>([]);
    const [currencies, setCurrencies] = useState<CurrencyDto[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
    /**
     * This request keeps its document identity on its PaymentSourceDocuments, not on the header.
     *
     * <p>Set by the review screen once the source-document summary arrives, from the persisted
     * discriminator rather than a row count. While true, `Request.SourceDocumentType` and
     * `Request.SupplierId` are compatibility echoes and must not gate anything: the authoritative
     * classification lives on each document and is validated there, by the backend, per document.</p>
     */
    const [usesMultiSourceDocuments, setUsesMultiSourceDocuments] = useState(false);
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<LookupDto[]>([]);
    const [costCenters, setCostCenters] = useState<LookupDto[]>([]);
    const [ivaRates, setIvaRates] = useState<LookupDto[]>([]);


    // Layout State
    const [sectionsOpen, setSectionsOpen] = useState({
        finance: false,
        items: false,
        quotations: false,
        attachments: false,
        history: false
    });
    const [isItemsHighlighted, setIsItemsHighlighted] = useState(false);
    const [isAttachmentsHighlighted, setIsAttachmentsHighlighted] = useState(false);
    const [isQuotationsHighlighted, setIsQuotationsHighlighted] = useState(false);

    // Refs
    const itemsSectionRef = useRef<HTMLDivElement>(null);
    const quotationsSectionRef = useRef<HTMLDivElement>(null);
    const hasGuidedAttentionRun = useRef<string | null>(null);

    // Modals
    const [showApprovalModal, setShowApprovalModal] = useState<{
        show: boolean,
        type: ApprovalActionType,
        groupId?: string
    }>({ show: false, type: null });
    const [poGroupIdForUpload, setPoGroupIdForUpload] = useState<string | null>(null);
    const [showCorrectPoModal, setShowCorrectPoModal] = useState(false);
    const [showReconciliationModal, setShowReconciliationModal] = useState(false);
    const [approvalComment, setApprovalComment] = useState('');
    const [approvalProcessing, setApprovalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });

    // Quick Supplier Modal State
    const [quickSupplierModal, setQuickSupplierModal] = useState<{ show: boolean; initialName: string; initialTaxId: string }>({ show: false, initialName: '', initialTaxId: '' });

    // --- Derived Operational Logic ---

    // Permissions based on real roles
    const isBuyer = user?.roles.includes(ROLES.BUYER);
    const isAreaApprover = user?.roles.includes(ROLES.AREA_APPROVER);
    const isFinalApprover = user?.roles.includes(ROLES.FINAL_APPROVER);
    const isFinance = user?.roles.includes(ROLES.FINANCE);
    const isReceiving = user?.roles.includes(ROLES.RECEIVING);
    
    const isReworkStatus = status === 'AREA_ADJUSTMENT' || status === 'FINAL_ADJUSTMENT';
    const isQuotationStage = status === 'WAITING_QUOTATION';
    const isOperationalStage = ['APPROVED', 'QUOTATION_COMPLETED', 'PO_REQUESTED', 'PO_PARTIALLY_UPLOADED', 'PO_ISSUED', 'WAITING_PO_CORRECTION', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'WAITING_QUOTATION'].includes(status || '');
    const isFinalizedStatus = ['COMPLETED', 'REJECTED', 'CANCELLED'].includes(status || '');

    const isDraftEditable = status === 'DRAFT' || isReworkStatus || isCopyMode;
    const isQuotationPartiallyEditable = status === 'WAITING_QUOTATION';
    const isFullyReadOnly = !isDraftEditable && !isQuotationPartiallyEditable;

    const hasSavedQuotations = quotations.length > 0;
    
    // Creator-only check: only the requester can edit if it's not a new/copy request
    const isCreator = user?.id === requesterId;
    const canEditOriginalData = !id || isCopyMode || status === 'DRAFT' || isCreator;

    const canEditHeader = (isDraftEditable || isQuotationPartiallyEditable) && canEditOriginalData;
    const canEditSupplier = (isDraftEditable || (isQuotationPartiallyEditable && !hasSavedQuotations)) && canEditOriginalData;
    const canEditItems = (isDraftEditable || (isQuotationPartiallyEditable && !hasSavedQuotations)) && canEditOriginalData;
    const canManageAttachments = status !== null && !isFinalizedStatus && !isCopyMode;
    const canExecuteOperationalAction = (isOperationalStage || isQuotationStage) && !isCopyMode;
    const canEdit = canEditItems;

    const canCancelRequest = useMemo(() => {
        if (!status) return false;
        if (status === 'CANCELLED' || status === 'COMPLETED' || status === 'REJECTED') return false;

        if (requestTypeCode === 'QUOTATION') {
            if (status !== 'DRAFT' && status !== 'WAITING_QUOTATION') return false;
            
            if (status === 'WAITING_QUOTATION') {
                const hasSupplier = !!formData.supplierId;
                const hasProforma = attachments.some(a => a.attachmentTypeCode === 'PROFORMA' && !(a as any).isDeleted);
                const hasItemProcessing = lineItems.some(item => 
                    item.supplierName || 
                    (item.lineItemStatusCode && item.lineItemStatusCode !== 'WAITING_QUOTATION' && item.lineItemStatusCode !== 'PENDING')
                );
                
                if (hasSupplier || hasProforma || hasItemProcessing) return false;
            }
            return true;
        } else if (requestTypeCode === 'PAYMENT') {
            const allowedStatuses = ['DRAFT', 'WAITING_AREA_APPROVAL', 'AREA_ADJUSTMENT', 'WAITING_FINAL_APPROVAL', 'FINAL_ADJUSTMENT', 'WAITING_COST_CENTER', 'APPROVED'];
            if (!allowedStatuses.includes(status)) return false;
            
            const hasOperationalDocs = attachments.some(a => 
                (a.attachmentTypeCode === 'PO' || 
                 a.attachmentTypeCode === 'PAYMENT_SCHEDULE' || 
                 a.attachmentTypeCode === 'PAYMENT_PROOF') && !(a as any).isDeleted
            );
            
            if (hasOperationalDocs) return false;
            return true;
        }
        return false;
    }, [status, requestTypeCode, formData.supplierId, attachments, lineItems]);

    // --- Hooks & Effects ---

    // Navigation Away Protection - DEC-082
    useEffect(() => {
        const isHeaderDirty = initialFormData && JSON.stringify(formData) !== JSON.stringify(initialFormData);
        const isItemsDirty = lineItems.length > 0 && isCopyMode; // In copy mode, having items is "dirty" vs nothing
        const hasUnsavedChanges = isHeaderDirty || isItemsDirty;

        const handleBeforeUnload = (e: BeforeUnloadEvent) => {
            if (hasUnsavedChanges && !saving && !submitting) {
                e.preventDefault();
                e.returnValue = ''; // Required for most browsers
            }
        };

        window.addEventListener('beforeunload', handleBeforeUnload);
        return () => window.removeEventListener('beforeunload', handleBeforeUnload);
    }, [formData, initialFormData, lineItems, isCopyMode, saving, submitting]);

    // --- Page Initialization & State Cleanup ---
    // Ref-guarded state consumption prevents race conditions and infinite navigate loops
    const hasConsentedState = useRef(false);

    useEffect(() => {
        // Only process persistent state from location ONCE per mount
        if (hasConsentedState.current) {
            return;
        }
        
        hasConsentedState.current = true;

        const focus = searchParams.get('focus');
        let shouldReplaceState = false;
        let newState = { ...(location.state || {}) } as any;
        let newSearchParams = new URLSearchParams(searchParams);

        // 1. Process Success Message (Post-Create/Post-Edit)
        if (location.state?.successMessage) {
            setFeedback({ type: 'success', message: location.state.successMessage });
            delete newState.successMessage;
            shouldReplaceState = true;
        }

        // 2. Process Highlight Focus (Post-Create redirect or explicit focus param)
        if (location.state?.successMessage || focus === 'items') {
            if (focus === 'items') {
                newSearchParams.delete('focus');
                shouldReplaceState = true;
            }

            const timer = setTimeout(() => {
                if (itemsSectionRef.current) {
                    itemsSectionRef.current.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    setIsItemsHighlighted(true);
                    setTimeout(() => setIsItemsHighlighted(false), 5000);
                }
            }, 500);
            return () => clearTimeout(timer);
        }

        // 3. Clear transient state from History to avoid replay on refresh
        if (shouldReplaceState) {
            // Delay navigation slightly to ensure hydration stability
            setTimeout(() => {
                navigate({
                    pathname: location.pathname,
                    search: newSearchParams.toString()
                }, { 
                    replace: true, 
                    state: newState 
                });
            }, 0);
        }
    }, [location.pathname, location.search, location.state, navigate, searchParams]);


    // Guided Attention for specific statuses
    useEffect(() => {
        let highlightTimeout: any;
        let scrollTimeout: any;

        if (!loading && status === 'WAITING_AREA_APPROVAL' && requestTypeCode === 'QUOTATION' && id && hasGuidedAttentionRun.current !== id) {
            hasGuidedAttentionRun.current = id;
            setSectionsOpen(prev => ({ ...prev, quotations: true }));

            scrollTimeout = setTimeout(() => {
                if (quotationsSectionRef.current) {
                    const yOffset = -220; 
                    const y = quotationsSectionRef.current.getBoundingClientRect().top + window.pageYOffset + yOffset;
                    window.scrollTo({ top: y, behavior: 'smooth' });

                    setIsQuotationsHighlighted(true);
                    highlightTimeout = setTimeout(() => {
                        setIsQuotationsHighlighted(false);
                    }, 4000);
                }
            }, 600);
        }

        return () => {
            if (highlightTimeout) clearTimeout(highlightTimeout);
            if (scrollTimeout) clearTimeout(scrollTimeout);
        };
    }, [loading, status, id, requestTypeCode]);

    // --- Action Handlers ---

    const toggleSection = (section: keyof typeof sectionsOpen) => {
        setSectionsOpen(prev => ({ ...prev, [section]: !prev[section] }));
    };

    const hasAttachment = (typeCode: string) => {
        return attachments.some(a => a.attachmentTypeCode === typeCode && !(a as any).isDeleted);
    };

    const handleAttachmentRefresh = useCallback(() => {
        loadData();
    }, []);

    const handleSaveItem = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!itemForm) return;

        if (!id && !isCopyMode) return;

        setItemSaving(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});

        try {
            const selectedUnit = units.find(u => u.code === itemForm.unit);

            if (selectedUnit && !selectedUnit.allowsDecimalQuantity) {
                const qtyVal = Number(itemForm.quantity);
                if (!Number.isInteger(qtyVal)) {
                    setFieldErrors({ Quantity: [`A unidade '${selectedUnit.code}' não permite quantidades fracionadas(decimais).`] });
                    setItemSaving(false);
                    return;
                }
            }

            if (!selectedUnit) {
                setFieldErrors(prev => ({ ...prev, UnitId: ['Informe a unidade do item.'] }));
                setItemSaving(false);
                return;
            }

            if (!itemForm.costCenterId) {
                setFieldErrors(prev => ({ ...prev, CostCenterId: ['Informe o centro de custo.'] }));
                setItemSaving(false);
                return;
            }

            if (!itemForm.ivaRateId) {
                setFieldErrors(prev => ({ ...prev, IvaRateId: ['Informe a taxa IVA.'] }));
                setItemSaving(false);
                return;
            }

            const payload = {
                ...itemForm,
                quantity: Number(itemForm.quantity),
                unitPrice: Number(itemForm.unitPrice),
                unitId: selectedUnit.id,
                costCenterId: Number(itemForm.costCenterId),
                ivaRateId: Number(itemForm.ivaRateId),
                dueDate: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) ? new Date(formData.needByDateUtc).toISOString() : (itemForm.dueDate || null),
                currencyId: Number(formData.currencyId)
            };

            if (isCopyMode) {
                let nextItems = [...lineItems];
                const tempId = itemForm.id || `temp-${Date.now()}`;
                
                if (itemForm.id) {
                    nextItems = nextItems.map(it => it.id === itemForm.id ? { ...it, ...payload } : it);
                } else {
                    nextItems.push({ ...payload, id: tempId } as any);
                }
                
                setLineItems(nextItems);
                const newTotal = nextItems.reduce((acc, it) => acc + (Number(it.quantity) * Number(it.unitPrice)), 0);
                setFormData(prev => ({ ...prev, estimatedTotalAmount: newTotal.toString(), discountAmount: '0' }));
                setItemForm(null);
                setFeedback({ type: 'success', message: 'Item atualizado (em memória).' });
            } else {
                if (itemForm.id) {
                    await api.requests.updateLineItem(id!, itemForm.id, payload);
                } else {
                    await api.requests.createLineItem(id!, payload);
                }
                const data = await api.requests.get(id!);
                setLineItems(data.lineItems || []);
                setFormData(prev => ({ ...prev, estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0', discountAmount: data.discountAmount?.toString() || '0' }));
                setItemForm(null);
                setFeedback({ type: 'success', message: 'Item salvo com sucesso.' });
            }
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                setFieldErrors(err.fieldErrors);
                setFeedback({ type: 'error', message: err.message || 'Existem campos inválidos no item.' });
            } else {
                setFeedback({ type: 'error', message: err.message || 'Erro ao salvar o item.' });
            }
        } finally {
            setItemSaving(false);
        }
    };

    const handleDeleteItem = async (itemId: string) => {
        if (!id && !isCopyMode) return;
        setPendingDeleteId(itemId);
        setShowApprovalModal({ show: true, type: 'DELETE_ITEM' });
    };

    const executeDeleteItem = async () => {
        if (!pendingDeleteId) return;
        if (isCopyMode) {
            const nextItems = lineItems.filter(it => it.id !== pendingDeleteId);
            setLineItems(nextItems);
            const newTotal = nextItems.reduce((acc, it) => acc + (Number(it.quantity) * Number(it.unitPrice)), 0);
            setFormData(prev => ({ ...prev, estimatedTotalAmount: newTotal.toString() }));
            setFeedback({ type: 'success', message: 'Item removido da cópia.' });
            setPendingDeleteId(null);
        } else {
            if (!id) return;
            try {
                await api.requests.deleteLineItem(id, pendingDeleteId);
                const data = await api.requests.get(id);
                setLineItems(data.lineItems || []);
                setFormData(prev => ({ ...prev, estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0', discountAmount: data.discountAmount?.toString() || '0' }));
                setFeedback({ type: 'success', message: 'Item excluído com sucesso.' });
                setPendingDeleteId(null);
            } catch (err: any) {
                setFeedback({ type: 'error', message: err.message || 'Erro ao excluir o item.' });
            }
        }
    };

    const handleDeleteRequest = async (e?: React.MouseEvent) => {
        if (e) {
            e.preventDefault();
            e.stopPropagation();
        }
        if (isCopyMode) {
            onClose ? onClose() : navigate('/requests', { replace: true });
            return;
        }
        if (!id) {
            setFeedback({ type: 'error', message: 'ID do pedido não localizado.' });
            return;
        }
        setShowApprovalModal({ show: true, type: 'DELETE' });
    };

    const executeDeleteRequest = async () => {
        setSaving(true);
        setFeedback({ type: 'info', message: 'Excluindo rascunho...' });
        try {
            await api.requests.remove(id!);
            navigate('/requests', {
                replace: true,
                state: { successMessage: 'Rascunho excluído com sucesso.' }
            });
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao excluir o rascunho.' });
            setSaving(false);
        }
    };

    const loadData = useCallback(async () => {
        if (!id && !isCopyMode) return;
        try {
            setLoading(true);
            let data: any;

            
            if (isCopyMode) {
                data = await api.requests.getTemplate(copyFromId!);
                setStatus('COPY_MODE'); // Virtual status
                setStatusFullName('Novo Pedido (Cópia)');
                setStatusBadgeColor('var(--color-status-draft-bg)');
                setLineItems(data.lineItems || []);
                setRequestTypeCode(data.requestTypeId === 2 ? 'PAYMENT' : 'QUOTATION');
                setRequestNumber(null);
                setRequesterId(null);
                setStatusHistory([]);
                setAttachments([]);
                setQuotations([]);
                setSelectedQuotationId(null);
            } else {
                data = await api.requests.get(id!);
                setStatus(data.statusCode);
                setStatusFullName(data.statusName);
                setStatusBadgeColor(data.statusBadgeColor);
                setLineItems(data.lineItems || []);
                setRequestTypeCode(data.requestTypeCode);
                setRequestNumber(data.requestNumber || null);
                setRequesterId(data.requesterId || null);
                setStatusHistory(data.statusHistory || []);
                setAttachments(data.attachments || []);
                setQuotations(data.quotations || []);
                setSelectedQuotationId(data.selectedQuotationId || null);
                setPoGroups(data.poGroups || []);
            }


            const form = {
                title: data.title || '',
                description: data.description || '',
                requestTypeId: data.requestTypeId || 1,
                needLevelId: data.needLevelId != null ? data.needLevelId.toString() : '',
                estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0',
                discountAmount: data.discountAmount?.toString() || '0',
                currencyId: data.currencyId ?? 1,
                departmentId: data.departmentId?.toString() || '',
                companyId: data.companyId?.toString() || '',
                plantId: data.plantId?.toString() || '',
                needByDateUtc: data.needByDateUtc ? data.needByDateUtc.substring(0, 10) : '',
                buyerId: data.buyerId || '',
                capexOpexClassificationId: data.capexOpexClassificationId != null ? data.capexOpexClassificationId.toString() : '',
                supplierId: data.supplierId?.toString() || '',
                areaApproverId: data.areaApproverId || '',
                finalApproverId: data.finalApproverId || '',
                sourceDocumentType: data.sourceDocumentType || ''
            };

            // The reading this classification was judged against, restored so the edit screen can
            // show the same suggestion and detect the same contradiction as the create screen.
            setDocumentClassification(data.sourceDocumentTypeOcrSuggestion
                ? {
                    suggestedType: data.sourceDocumentTypeOcrSuggestion,
                    confidence: data.sourceDocumentTypeOcrConfidence ?? null,
                    indicatesFiscalDocument: isFiscalDocument(data.sourceDocumentTypeOcrSuggestion),
                    ...parseClassificationEvidence(data.sourceDocumentTypeEvidenceJson)
                }
                : null);
            setClassificationConflict({
                hasConflict: !!data.classificationConflictAcknowledged,
                isHighRisk: false,
                acknowledged: !!data.classificationConflictAcknowledged,
                justification: data.classificationJustification || ''
            });

            if (!isCopyMode) {
                setSupplierName(data.supplierName || '');
                setSupplierPortalCode(data.supplierPortalCode || '');
            }

            const [uData, cData, nData, dData, compData, plantData, ccData, ivaData] = await Promise.all([
                api.lookups.getUnits(true),
                api.lookups.getCurrencies(true),
                api.lookups.getNeedLevels(true),
                api.lookups.getDepartments(true),
                api.lookups.getCompanies(true),
                api.lookups.getPlants(undefined, true),
                api.lookups.getCostCenters(true),
                api.lookups.getIvaRates(true)
            ]);
            
            setUnits(uData);
            setCurrencies(cData);
            setNeedLevels(nData);
            setDepartments(dData);
            setCompanies(compData); 
            setPlants(plantData);
            setCostCenters(ccData);
            setIvaRates(ivaData);


            // (Fase B: o aprovador de área não é mais pré-nomeado a partir do
            // responsável do departamento — roteamento via DepartmentManagers.)
            setFormData(form);
            setInitialFormData(form);
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Falha ao carregar pedido.' });
        } finally {
            setLoading(false);
        }

    }, [id, isCopyMode, copyFromId]);

    useEffect(() => {
        loadData();
    }, [loadData]);


    const clearFieldError = (fieldName: string) => {
        setFieldErrors(prev => {
            const next = { ...prev };
            const normalizedField = fieldName.toLowerCase();
            const key = Object.keys(next).find(k => {
                const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
                return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
            });
            if (key) delete next[key];
            return next;
        });
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
        const { name, value } = e.target;
        setFormData(prev => {
            const next = { ...prev, [name]: value };

            // (Fase B: trocar o departamento não pré-nomeia aprovador de área.)
            return next;
        });
        clearFieldError(name);
    };

    const handleSubmit = async (e?: React.FormEvent, forceFeedback: boolean = false): Promise<boolean> => {
        if (e) e.preventDefault();
        if (!id && !isCopyMode) return false;

        // Manual Validation for Required Fields
        const newErrors: Record<string, string[]> = {};
        if (!formData.requestTypeId) newErrors['RequestTypeId'] = ['O Tipo de Pedido é obrigatório.'];


        // New required checks
        if (!formData.needLevelId) newErrors['NeedLevelId'] = ['O grau de necessidade é obrigatório.'];
        if (!formData.departmentId) newErrors['DepartmentId'] = ['O departamento é obrigatório.'];
        if (!formData.companyId) newErrors['CompanyId'] = ['A empresa é obrigatória.'];
        if (!formData.plantId) newErrors['PlantId'] = ['A planta é obrigatória.'];
        // Conditional Supplier Validation: ID 2 is PAYMENT
        const isPayment = Number(formData.requestTypeId) === 2 || requestTypeCode === 'PAYMENT';
        if (!formData.needByDateUtc) {
            newErrors['NeedByDateUtc'] = [isPayment ? 'A data de vencimento é obrigatória.' : 'A data Necessário Até é obrigatória.'];
        }

        // Conditional Supplier Validation: ID 2 is PAYMENT
        // RELAXED: Supplier can be added via Proforma OCR in document-first flow
        // if (Number(formData.requestTypeId) === 2 && !formData.supplierId) {
        //     newErrors['SupplierId'] = ['O fornecedor é obrigatório para pedidos de Pagamento.'];
        // }

        if (Object.keys(newErrors).length > 0) {
            setFieldErrors(newErrors);
            setFeedback({ type: 'error', message: 'Preencha todos os campos obrigatórios antes de continuar.' });
            scrollToFirstError(newErrors);
            return false;
        }

        // Safety Check: Active Item Form
        if (itemForm) {
            setFeedback({ type: 'error', message: 'Você possui um item em edição não salvo. Salve ou cancele a edição do item antes de salvar o pedido.' });
            itemsSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
            return false;
        }

        // Optimization: For manual saves, prevent no-op API calls
        const isHeaderDirty = initialFormData && JSON.stringify(formData) !== JSON.stringify(initialFormData);
        
        if (e && !isHeaderDirty) {
            setFeedback({ type: 'info', message: 'Nenhuma alteração detectada para salvar.' });
            return true;
        }

        // Standardized Modal Confirmation
        if (e && !showApprovalModal.show) {
            setShowApprovalModal({ show: true, type: 'SAVE' });
            return false;
        }

        // If forceFeedback is true (modal confirmation), we proceed even if e is dummy or null
        // because the dirty check was already verified at the moment the modal was opened.

        if (isCopyMode) {
            // No persistence for Copy Mode until SUBMIT
            return true;
        }

        const payload = {
            title: formData.title,
            description: formData.description,
            requestTypeId: Number(formData.requestTypeId),
            needLevelId: formData.needLevelId ? Number(formData.needLevelId) : null,
            currencyId: formData.currencyId ? Number(formData.currencyId) : null,
            estimatedTotalAmount: Number(formData.estimatedTotalAmount) || 0,
            discountAmount: Number(formData.discountAmount) || 0,
            departmentId: formData.departmentId ? Number(formData.departmentId) : 0,
            companyId: formData.companyId ? Number(formData.companyId) : 0,
            plantId: formData.plantId ? Number(formData.plantId) : null,
            capexOpexClassificationId: formData.capexOpexClassificationId ? Number(formData.capexOpexClassificationId) : null,
            supplierId: formData.supplierId ? Number(formData.supplierId) : null,
            needByDateUtc: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) 
                ? new Date(formData.needByDateUtc).toISOString() 
                : null,
            buyerId: formData.buyerId || null,
            // areaApproverId removido (Fase B): o backend resolve o roteamento de área
            finalApproverId: formData.finalApproverId || null,
            // Post-Payment Completion (Release 2). Empty string means "not chosen" and is sent as
            // null — the backend accepts that on a draft and blocks it at submission instead.
            sourceDocumentType: formData.sourceDocumentType || null,
            // The classification and the reasoning behind it travel together — the backend
            // re-derives whether this was an override and refuses an unconfirmed one.
            ...(() => {
                const c = buildClassificationPayload(
                    formData.sourceDocumentType, documentClassification, classificationConflict);
                return {
                    sourceDocumentTypeSource: c.source,
                    sourceDocumentTypeOcrSuggestion: c.suggestion,
                    sourceDocumentTypeOcrConfidence: c.confidence,
                    sourceDocumentTypeEvidenceJson: c.evidenceJson,
                    sourceDocumentTypeTitleFound: c.titleFound,
                    sourceDocumentTypeConflictingEvidenceJson: c.conflictingEvidenceJson,
                    sourceDocumentTypeSuggestionSource: c.suggestionSource,
                    classificationConflictAcknowledged: c.acknowledged,
                    classificationJustification: c.justification
                };
            })()
        };

        setSaving(true);
        try {
            await api.requests.updateDraft(id!, payload);

            // Due-date propagation to line items is now handled server-side inside UpdateRequestDraft
            // to ensure atomicity — all changes commit or fail together within a single transaction.
            // Previously, a separate Promise.all(updateLineItem) call here caused the IVA save bug:
            // header changes were committed, but line item updates could fail IVA validation,
            // resulting in partial persistence and misleading error messages.

            // Refetch to ensure frontend state (especially total) is perfectly in sync with backend logic
            const updated = await api.requests.get(id!);
            setLineItems(updated.lineItems || []);
            setFormData(prev => ({
                ...prev,
                estimatedTotalAmount: updated.estimatedTotalAmount?.toString() || '0',
                discountAmount: updated.discountAmount?.toString() || '0'
            }));

            // Only show success if this was a manual save (has e or forceFeedback)
            if (e || forceFeedback) {

                const isPayment = Number(formData.requestTypeId) === 2 || requestTypeCode === 'PAYMENT';
                const hasNoItems = lineItems.length === 0;

                if (isPayment && hasNoItems) {
                    setFeedback({
                        type: 'success',
                        message: id ? 'Pedido atualizado. Adicione os itens para poder submeter.' : 'Rascunho salvo. Adicione os itens para poder submeter o pedido.'
                    });
                    // Auto-scroll and highlight the line items section
                    setTimeout(() => {
                        itemsSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                        setIsItemsHighlighted(true);
                        setTimeout(() => setIsItemsHighlighted(false), 5000); // 5 seconds highlight
                    }, 500);
                } else if (!isPayment && hasNoItems) {
                    setFeedback({
                        type: 'success',
                        message: id ? 'Pedido atualizado com sucesso.' : 'Rascunho salvo com sucesso.',
                    });
                } else {
                    setFeedback({ type: 'success', message: 'Pedido atualizado com sucesso.' });
                }
            }
            return true;
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                // Collect detailed field errors for better visibility
                const errorMessages = Object.entries(err.fieldErrors)
                    .map(([_field, msgs]) => `${msgs.join(', ')}`)
                    .filter(msg => msg.length > 0);

                const finalMessage = errorMessages.length > 0 
                    ? `Erro de validação: ${errorMessages.join('. ')}`
                    : (err.message || 'Existem campos preenchidos incorretamente.');

                setFieldErrors(err.fieldErrors);
                setFeedback({ 
                    type: 'error', 
                    message: finalMessage
                });
                scrollToFirstError(err.fieldErrors);
                const errorKeys = Object.keys(err.fieldErrors).map(k => k.toLowerCase());
                if (errorKeys.some(k => k.includes('proforma') || k.includes('attachment'))) {
                    setIsAttachmentsHighlighted(true);
                    setTimeout(() => setIsAttachmentsHighlighted(false), 5000);
                }
                if (errorKeys.some(k => k.includes('lineitems'))) {
                    setIsItemsHighlighted(true);
                    setTimeout(() => setIsItemsHighlighted(false), 5000);
                }
            } else {
                setFeedback({ type: 'error', message: err.message || 'Erro ao atualizar o rascunho.' });
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }
            return false;
        } finally {
            setSaving(false);
        }
    };

    const handleRequestAction = async (action: ApprovalActionType) => {        if (!id) return;

        const actionRequiresComment = action === 'REJECT' || action === 'REQUEST_ADJUSTMENT' || action === 'CANCEL_REQUEST' || action === 'ITEM_STATUS_CHANGE';

        if (actionRequiresComment && !approvalComment.trim()) {
            let errorMsg = 'Motivo / Observações são obrigatórios.';
            if (action === 'REJECT') errorMsg = 'Informe o motivo da rejeição.';
            else if (action === 'REQUEST_ADJUSTMENT') errorMsg = 'Informe o motivo do reajuste.';
            else if (action === 'CANCEL_REQUEST') errorMsg = 'Informe o motivo do cancelamento.';
            
            setModalFeedback({
                type: 'error',
                message: errorMsg
            });
            scrollToFirstError({ Comment: ['Requerido'] }, '.modal-content');
            return;
        }

        setModalFeedback({ type: 'error', message: null });

        // Mandatory Attachment Validations
        if (action === 'CONFIRM_ADVANCE' && !hasAttachment('PAYMENT_PROOF')) {
            setModalFeedback({ type: 'error', message: 'É necessário anexar o Comprovante de Pagamento antes de confirmar.' });
            return;
        }
        if (action === 'COMPLETE_QUOTATION') {
            try {
                const totalItemsCount = lineItems.length + quotations.reduce((acc: number, q: SavedQuotationDto) => acc + q.itemCount, 0);
                const completionResult = await completeQuotationAction(id!, totalItemsCount, approvalComment);
                
                // Financial Integrity Gate: surface mismatch in modal feedback
                if (completionResult && 'integrityCheckFailed' in completionResult && completionResult.integrityCheckFailed) {
                    setModalFeedback({ 
                        type: 'error', 
                        message: `⚠️ Integridade Financeira: ${completionResult.detail} Utilize a tela "Gestão de Cotações" para corrigir ou confirmar o override.`
                    });
                    setApprovalProcessing(false);
                    return;
                }
                setApprovalProcessing(false);
                setFeedback({ type: 'success', message: 'Cotação concluída e enviada para aprovação.' });
                setShowApprovalModal({ show: false, type: null });
                setTimeout(() => onClose ? onClose() : navigate('/requests'), 2000);
                return;
            } catch (err: any) {
                setModalFeedback({ type: 'error', message: err.message });
                setApprovalProcessing(false);
                return;
            }
        }

        setApprovalProcessing(true);
        try {
            let result;
            const isAreaApproval = status === 'WAITING_AREA_APPROVAL';

            if (action === 'APPROVE') {
                result = isAreaApproval
                    ? await api.requests.approveArea(id, approvalComment, undefined)
                    : await api.requests.approveFinal(id, approvalComment);
            } else if (action === 'REJECT') {
                result = isAreaApproval
                    ? await api.requests.rejectArea(id, approvalComment)
                    : await api.requests.rejectFinal(id, approvalComment);
            } else if (action === 'REQUEST_ADJUSTMENT') {
                result = isAreaApproval
                    ? await api.requests.requestAdjustmentArea(id, approvalComment)
                    : await api.requests.requestAdjustmentFinal(id, approvalComment);
            } else if (action === 'SAVE') {
                const success = await handleSubmit(undefined, true);
                if (success) {
                    setShowApprovalModal({ show: false, type: null });
                }
                return;
            } else if (action === 'SUBMIT') {
                await executeSubmit();
                setShowApprovalModal({ show: false, type: null });
                return;
            } else if (action === 'DELETE') {
                await executeDeleteRequest();
                setShowApprovalModal({ show: false, type: null });
                return;
            } else if (action === 'DELETE_ITEM') {
                await executeDeleteItem();
                setShowApprovalModal({ show: false, type: null });
                return;
            } else if (action === 'SCHEDULE_ADVANCE') {
                const groupId = poGroups?.[0]?.id || '';
                result = await api.requests.scheduleAdvancePayment(id, { requestPoGroupId: groupId, scheduledDate: new Date().toISOString(), comment: approvalComment });
            } else if (action === 'CONFIRM_ADVANCE') {
                // Pass estimated total as fallback. A dedicated UI can be added later if partial advance amount needs to be specific.
                const groupId = poGroups?.[0]?.id || '';
                result = await api.requests.confirmAdvancePayment(id, { requestPoGroupId: groupId, actualPaidAmount: Number(formData.estimatedTotalAmount) || 0, paidDate: new Date().toISOString(), comment: approvalComment });
            } else if (action === 'CONFIRM_DELIVERY') {
                result = await api.requests.confirmDelivery(id, approvalComment);
            } else if (action === 'MOVE_TO_RECEIPT') {
                if (!showApprovalModal.groupId) throw new Error("Grupo P.O. não especificado.");
                result = await api.requests.moveToReceipt(id, showApprovalModal.groupId, approvalComment);
            } else if (action === 'FINALIZE') {
                result = await api.requests.finalize(id, approvalComment);
            } else if (action === 'CANCEL_REQUEST') {
                result = await api.requests.cancel(id, approvalComment);
            } else {
                throw new Error('Ação inválida.');
            }

            setFeedback({ type: 'success', message: result.message });
            setShowApprovalModal({ show: false, type: null });
            setApprovalComment('');
            setModalFeedback({ type: 'error', message: null });

            // Reload request data to reflect status change and history
            const data = await api.requests.get(id);
            setStatus(data.statusCode);
            setStatusFullName(data.statusName);
            setStatusBadgeColor(data.statusBadgeColor);
            setStatusHistory(data.statusHistory || []);

        } catch (err: any) {
            setModalFeedback({ type: 'error', message: err.message || 'Não foi possível concluir a ação. Tente novamente.' });
        } finally {
            setApprovalProcessing(false);
        }
    };

    // ── Catalogue reconciliation for a SAVED multi-document PAYMENT draft ──
    //
    // A draft can gain a document, a line or an edited description after it was created, and none of
    // those went past the catalogue on the way in. The request already existing is not a reason to
    // skip the check — it is only a reason to run it here rather than before creation.
    //
    // Scoped to multi-document PAYMENT on purpose. Legacy PAYMENT and QUOTATION editing never ran
    // catalogue reconciliation at submission and are left exactly as they are.
    const [showCatalogReconciliationWarning, setShowCatalogReconciliationWarning] = useState(false);

    const catalogScopedItems: DocumentScopedItem[] = useMemo(
        () => (requestTypeCode === 'PAYMENT' && usesMultiSourceDocuments)
            ? flattenPersistedLineItems(lineItems as any)
            : [],
        [requestTypeCode, usesMultiSourceDocuments, lineItems]);

    const catalogReconciliation = useCatalogItemReconciliation(catalogScopedItems);

    /**
     * Unresolved lines according to the SERVER's copy, not the modal's memory.
     *
     * <p>Resolutions are held by array index, and the index of a line changes when a document is
     * removed. Deciding the gate from what was actually saved means a stale answer can never wave a
     * genuinely unmatched item through.</p>
     */
    const catalogUnresolved = useMemo(
        () => unresolvedItems(catalogScopedItems), [catalogScopedItems]);

    // Undefined, not an empty object, when this request is not multi-document PAYMENT — the modal
    // treats "labels supplied" as the switch for its Documento column, and `{}` is truthy.
    const catalogDocumentLabels = useMemo(
        () => catalogScopedItems.length > 0 ? documentLabelsByIndex(catalogScopedItems) : undefined,
        [catalogScopedItems]);

    const catalogEquivalentIndexesOf = useCallback(
        (index: number) => equivalentUnresolvedIndexes(catalogScopedItems, index),
        [catalogScopedItems]);

    /**
     * Records the answers, one focused call per line.
     *
     * <p>Uses the catalogue-link endpoint rather than the ordinary item update: only
     * <c>ItemCatalogId</c> changes, so the line keeps its source document, quantity, unit, price,
     * discount, IVA and total. Re-running it with the same answers changes nothing and writes no
     * history, so pressing Submeter twice cannot duplicate anything.</p>
     */
    const applyCatalogResolutions = useCallback(async (resolutions: ItemResolution[]) => {
        if (!id) return;

        // One answer settles every equivalent line, so the same unknown item appearing on two
        // invoices does not become two pending catalogue entries.
        const effective = propagateEquivalentResolutions(catalogScopedItems, resolutions);
        catalogReconciliation.resolveAll(effective);

        try {
            for (const resolution of effective) {
                const target = catalogScopedItems[resolution.itemIndex];
                if (!target || !resolution.linkedCatalogId) continue;

                await api.requests.setLineItemCatalogLink(
                    id, target.itemTempId, resolution.linkedCatalogId);
            }

            const data = await api.requests.get(id);
            setLineItems(data.lineItems || []);
        } catch (err: any) {
            setFeedback({
                type: 'error',
                message: err?.message || 'Falha ao vincular os itens ao catálogo.'
            });
        }
    }, [id, catalogScopedItems, catalogReconciliation]);

    const handleSubmitRequest = async () => {
        if (!id && !isCopyMode) return;

        // Rework Validation: Rely on mandatory field and attachment checks below

        // 1. Mandatory Content check
        // PAYMENT: Requires at least one item
        // QUOTATION: Requires at least one item OR one attachment
        if (requestTypeCode === 'PAYMENT') {
            const activePaymentItems = lineItems.filter(it => !(it as any).isDeleted);
            const noItems = activePaymentItems.length === 0;
            // Phase 2 — each active line must be individually valid; a zero/invalid line must not be
            // masked by another valid line (mirrors the authoritative backend Submit check).
            const hasInvalidItem = activePaymentItems.some(it =>
                !it.description || it.description.trim() === '' ||
                Number(it.quantity) <= 0 ||
                !it.unit ||
                Number(it.totalAmount) <= 0
            );
            if (noItems || hasInvalidItem) {
                setFeedback({
                    type: 'error',
                    message: noItems
                        ? 'Para submeter, o pedido deve conter pelo menos um item.'
                        : 'Cada item do pagamento deve ter descrição, quantidade maior que zero, unidade e valor de linha maior que zero.'
                });
                itemsSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                setIsItemsHighlighted(true);
                setTimeout(() => setIsItemsHighlighted(false), 5000);
                return;
            }
        }

        if (requestTypeCode === 'QUOTATION') {
            // Phase 2 — a Quotation can only be submitted with at least one VALID item; an attachment no
            // longer substitutes items. Mirrors the authoritative backend Submit rule.
            const activeQuotationItems = lineItems.filter(it => !(it as any).isDeleted);
            const noItems = activeQuotationItems.length === 0;
            const hasInvalidItem = activeQuotationItems.some(it =>
                !it.description || it.description.trim() === '' ||
                Number(it.quantity) <= 0 ||
                !it.unit
            );
            if (noItems || hasInvalidItem) {
                setFeedback({
                    type: 'error',
                    message: 'Adicione pelo menos um item válido antes de submeter a solicitação de Cotação.'
                });
                setIsItemsHighlighted(true);
                setTimeout(() => setIsItemsHighlighted(false), 5000);
                return;
            }
        }

        // 1.05 Post-Payment Completion (Release 2) — the billing document type becomes mandatory at
        // submission, never while the request is still a draft. Mirrors the authoritative backend
        // rule in SubmitRequest; the server re-checks it regardless of what the UI allows.
        if (requestTypeCode === 'PAYMENT' && !usesMultiSourceDocuments &&
            featureFlags.sourceDocumentTypeRequired &&
            !isSelectableDocumentType(formData.sourceDocumentType)) {
            setFeedback({
                type: 'error',
                message: 'Selecione o Tipo de documento anexado antes de submeter o pedido.'
            });
            window.scrollTo({ top: 0, behavior: 'smooth' });
            return;
        }

        // 1.06 A classification that contradicts the document reading must have been confirmed —
        // and justified where the risk warrants it. The backend rejects an unconfirmed one with a
        // 400; refusing here turns that into an explanation instead of a failed save.
        // Likewise the header-level conflict: on a multi-document request the reading and the
        // decision belong to a document, and each document carries its own answer.
        if (requestTypeCode === 'PAYMENT' && !usesMultiSourceDocuments &&
            featureFlags.postPaymentCompletionEnabled) {
            const evaluation = evaluateClassificationConflict(
                formData.sourceDocumentType, documentClassification);

            if (!canConfirmConflict(evaluation, classificationConflict.acknowledged,
                                    classificationConflict.justification)) {
                setFeedback({
                    type: 'error',
                    message: classificationConflict.acknowledged
                        ? `Justifique a classificação divergente (mínimo ${CONFLICT_JUSTIFICATION_MIN_LENGTH} caracteres) antes de submeter.`
                        : 'A classificação selecionada contradiz o documento. Confirme a divergência no aviso junto ao campo antes de submeter.'
                });
                window.scrollTo({ top: 0, behavior: 'smooth' });
                return;
            }
        }

        // 1.1 Mandatory Proforma check — LEGACY single-document PAYMENT only.
        // Under the multi-document model the commercial document IS the PaymentSourceDocument, whose
        // attachment is typed PAYMENT_SOURCE_DOCUMENT. The legacy slot is never filled by that flow,
        // so this guard would refuse every multi-document request and ask for the same invoice twice.
        if (requestTypeCode === 'PAYMENT' && !usesMultiSourceDocuments && !hasAttachment('PROFORMA')) {
            setFeedback({
                type: 'error',
                message: 'É necessário anexar a Proforma antes de submeter o pedido.'
            });
            
            // Expand the attachments section
            setSectionsOpen(prev => ({ ...prev, attachments: true }));

            // Scroll to attachments section and highlight
            // We use a small timeout to allow the section to expand in the DOM before scrolling
            setTimeout(() => {
                const attachmentsSection = document.getElementById('attachments-section');
                if (attachmentsSection) {
                    attachmentsSection.scrollIntoView({ behavior: 'smooth', block: 'center' });
                } else {
                    window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' });
                }
                setIsAttachmentsHighlighted(true);
                setTimeout(() => setIsAttachmentsHighlighted(false), 5000);
            }, 100);
            return;
        }

        // 1.2 Catalogue reconciliation — multi-document PAYMENT only.
        // Runs after the request and document rules, before anything is saved or submitted. Lines
        // already linked to a catalogue item are not asked about again.
        if (requestTypeCode === 'PAYMENT' && usesMultiSourceDocuments && catalogUnresolved.length > 0) {
            setShowCatalogReconciliationWarning(true);
            return;
        }

        // 2. Perform Auto-Save (includes header validation)
        const saveSuccessful = await handleSubmit();
        if (!saveSuccessful) return;

        // 3. Confirm Submission (Standardized Modal)
        if (!showApprovalModal.show) {
            setShowApprovalModal({ show: true, type: 'SUBMIT' });
            return;
        }
    };

    const executeSubmit = async () => {
        setSubmitting(true);
        setFeedback({ type: 'info', message: isCopyMode ? 'Criando pedido da cópia...' : (isReworkStatus ? 'Reenviando pedido...' : 'Submetendo pedido...') });

        try {
            let result;
            if (isCopyMode) {
                // Atomic creation with bulk items
                const payload = {
                    ...formData,
                    requestTypeId: Number(formData.requestTypeId),
                    lineItems: lineItems.map(it => ({
                        ...it,
                        id: undefined // Don't send temp/old IDs
                    }))
                };
                result = await api.requests.create(payload);
                // The backend CreateRequest now supports bulk items and returns full details
                // We navigate to /requests table as requested in copy flow
            } else {
                result = await api.requests.submit(id!);
            }

            const successMessage = result?.message || (requestTypeCode === 'QUOTATION'
                ? 'Pedido enviado para cotação com sucesso.'
                : 'Pedido enviado para aprovação da área com sucesso.');

            navigate('/requests', {
                replace: true,
                state: { successMessage }
            });

        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao submeter o pedido.' });
            window.scrollTo({ top: 0, behavior: 'smooth' });
        } finally {
            setSubmitting(false);
        }
    };

    

    return {
        usesMultiSourceDocuments,
        setUsesMultiSourceDocuments,
        id,
        isCopyMode,
        copyFromId,
        loading,
        saving,
        submitting,
        feedback,
        setFeedback,
        fieldErrors,
        setFieldErrors,
        status,
        setStatus,
        statusFullName,
        setStatusFullName,
        statusBadgeColor,
        setStatusBadgeColor,
        requestTypeCode,
        setRequestTypeCode,
        requestNumber,
        setRequestNumber,
        formData,
        setFormData,
        documentClassification,
        classificationConflict,
        setClassificationConflict,
        initialFormData,
        // Post-Payment Completion (Release 2): drives whether the classification field renders.
        featureFlags,
        supplierName,
        setSupplierName,
        supplierPortalCode,
        setSupplierPortalCode,
        lineItems,
        setLineItems,
        itemForm,
        setItemForm,
        itemSaving,
        pendingDeleteId,
        setPendingDeleteId,
        statusHistory,
        setStatusHistory,
        attachments,
        setAttachments,
        quotations,
        setQuotations,
        selectedQuotationId,
        setSelectedQuotationId,
        units,
        currencies,
        needLevels,
        departments,
        companies,
        plants,
        costCenters,
        ivaRates,
        sectionsOpen,
        setSectionsOpen,
        toggleSection,
        isItemsHighlighted,
        setIsItemsHighlighted,
        isAttachmentsHighlighted,
        setIsAttachmentsHighlighted,
        isQuotationsHighlighted,
        setIsQuotationsHighlighted,
        itemsSectionRef,
        quotationsSectionRef,
        showApprovalModal,
        setShowApprovalModal,
        poGroupIdForUpload,
        setPoGroupIdForUpload,
        showCorrectPoModal,
        setShowCorrectPoModal,
        showReconciliationModal,
        setShowReconciliationModal,
        approvalComment,
        setApprovalComment,
        approvalProcessing,
        modalFeedback,
        setModalFeedback,
        quickSupplierModal,
        setQuickSupplierModal,
        isBuyer,
        isCreator,
        isAreaApprover,
        isFinalApprover,
        isFinance,
        isReceiving,
        isReworkStatus,
        isQuotationStage,
        isOperationalStage,
        isFinalizedStatus,
        isDraftEditable,
        isQuotationPartiallyEditable,
        isFullyReadOnly,
        hasSavedQuotations,
        canEditHeader,
        canEditSupplier,
        canEditItems,
        canManageAttachments,
        canExecuteOperationalAction,
        canEdit,
        canCancelRequest,
        handleChange,
        clearFieldError,
        handleSubmit,
        handleRequestAction,
        handleSubmitRequest,
        // Catalogue reconciliation, multi-document PAYMENT drafts only.
        showCatalogReconciliationWarning,
        setShowCatalogReconciliationWarning,
        catalogReconciliation,
        catalogUnresolved,
        catalogDocumentLabels,
        catalogEquivalentIndexesOf,
        applyCatalogResolutions,
        handleSaveItem,
        handleDeleteItem,
        executeDeleteItem,
        handleDeleteRequest,
        executeDeleteRequest,
        handleAttachmentRefresh,
        loadData,
        navigate,
        location,
        searchParams,
        poGroups
    };
}
