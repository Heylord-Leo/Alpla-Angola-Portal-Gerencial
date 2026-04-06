import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate, useParams, useLocation, useSearchParams } from 'react-router-dom';
import { 
    Save, 
    X, 
    ShieldCheck, 
    ShieldAlert, 
    Edit2, 
    Trash2, 
    Send, 
    ArrowLeft, 
    Plus,
    FileText,
    Clock,
    ArrowRight,
    CheckCircle,
    Check,
    AlertCircle,
    AlertTriangle,
    UserPlus
} from 'lucide-react';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { formatCurrencyAO, getRequestGuidance, formatDateTime } from '../../lib/utils';
import { CurrencyDto, LookupDto, RequestStatusHistoryDto, RequestAttachmentDto, RequestLineItemDto, SavedQuotationDto } from '../../types';
import { DateInput } from '../../components/DateInput';
import { RequestAttachments } from '../../components/RequestAttachments';
import { motion, AnimatePresence } from 'framer-motion';
import { completeQuotationAction } from '../../lib/workflow';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { RequestLineItemForm } from '../../components/RequestLineItemForm';
import { RequestActionHeader, BreadcrumbItem, OperationalGuidance } from './components/RequestActionHeader';
import { RequestQuotations } from './components/RequestQuotations';
import { scrollToFirstError } from '../../lib/validation';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';

export function RequestEdit() {
    const navigate = useNavigate();
    const { id } = useParams<{ id: string }>();
    const location = useLocation();
    const [searchParams] = useSearchParams();

    const { user } = useAuth();
    
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
    
    // Form Data
    const [formData, setFormData] = useState({
        title: '',
        description: '',
        requestTypeId: 1,
        needLevelId: '',
        estimatedTotalAmount: '',
        currencyId: 1,
        departmentId: '',
        companyId: '',
        plantId: '',
        needByDateUtc: '',
        buyerId: '',
        capexOpexClassificationId: '',
        supplierId: '',
        areaApproverId: '',
        finalApproverId: ''
    });
    const [initialFormData, setInitialFormData] = useState<any>(null);
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

    // Master Data
    const [units, setUnits] = useState<LookupDto[]>([]);
    const [currencies, setCurrencies] = useState<CurrencyDto[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
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
        type: ApprovalActionType
    }>({ show: false, type: null });
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
    const isOperationalStage = ['APPROVED', 'PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'WAITING_QUOTATION'].includes(status || '');
    const isFinalizedStatus = ['COMPLETED', 'REJECTED', 'CANCELLED', 'QUOTATION_COMPLETED'].includes(status || '');

    const isDraftEditable = status === 'DRAFT' || isReworkStatus || isCopyMode;
    const isQuotationPartiallyEditable = status === 'WAITING_QUOTATION';
    const isFullyReadOnly = !isDraftEditable && !isQuotationPartiallyEditable;

    const hasSavedQuotations = quotations.length > 0;

    const canEditHeader = isDraftEditable || isQuotationPartiallyEditable;
    const canEditSupplier = isDraftEditable || (isQuotationPartiallyEditable && !hasSavedQuotations);
    const canEditItems = isDraftEditable || (isQuotationPartiallyEditable && !hasSavedQuotations);
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
                setFormData(prev => ({ ...prev, estimatedTotalAmount: newTotal.toString() }));
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
                setFormData(prev => ({ ...prev, estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0' }));
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
                setFormData(prev => ({ ...prev, estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0' }));
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
            navigate('/requests', { replace: true });
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
                setStatusHistory(data.statusHistory || []);
                setAttachments(data.attachments || []);
                setQuotations(data.quotations || []);
                setSelectedQuotationId(data.selectedQuotationId || null);
            }


            const form = {
                title: data.title || '',
                description: data.description || '',
                requestTypeId: data.requestTypeId || 1,
                needLevelId: data.needLevelId != null ? data.needLevelId.toString() : '',
                estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0',
                currencyId: data.currencyId ?? 1,
                departmentId: data.departmentId?.toString() || '',
                companyId: data.companyId?.toString() || '',
                plantId: data.plantId?.toString() || '',
                needByDateUtc: data.needByDateUtc ? data.needByDateUtc.substring(0, 10) : '',
                buyerId: data.buyerId || '',
                capexOpexClassificationId: data.capexOpexClassificationId != null ? data.capexOpexClassificationId.toString() : '',
                supplierId: data.supplierId?.toString() || '',
                areaApproverId: data.areaApproverId || '',
                finalApproverId: data.finalApproverId || ''
            };

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


            if (form.departmentId && !form.areaApproverId) {
                const dept = dData.find(d => d.id === Number(form.departmentId));
                if (dept && dept.responsibleUserId) {
                    form.areaApproverId = dept.responsibleUserId;
                }
            }

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

            // Auto-fill Area Approver from Department - DEC-082
            if (name === 'departmentId' && value) {
                const dept = departments.find(d => d.id === Number(value));
                if (dept && dept.responsibleUserId) {
                    next.areaApproverId = dept.responsibleUserId;
                }
            }

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
            departmentId: formData.departmentId ? Number(formData.departmentId) : 0,
            companyId: formData.companyId ? Number(formData.companyId) : 0,
            plantId: formData.plantId ? Number(formData.plantId) : null,
            capexOpexClassificationId: formData.capexOpexClassificationId ? Number(formData.capexOpexClassificationId) : null,
            supplierId: formData.supplierId ? Number(formData.supplierId) : null,
            needByDateUtc: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) 
                ? new Date(formData.needByDateUtc).toISOString() 
                : null,
            buyerId: formData.buyerId || null,
            areaApproverId: formData.areaApproverId || null,
            finalApproverId: formData.finalApproverId || null
        };

        setSaving(true);
        try {
            await api.requests.updateDraft(id!, payload);

            // Synchronize unified Due Date downwards to all existing line items
            if (!isCopyMode && lineItems.length > 0) {
                const globalDueDate = formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) 
                    ? new Date(formData.needByDateUtc).toISOString() 
                    : null;
                
                await Promise.all(lineItems.map(item => 
                    api.requests.updateLineItem(id!, item.id, {
                        ...item,
                        quantity: Number(item.quantity) || 0,
                        unitPrice: Number(item.unitPrice) || 0,
                        dueDate: globalDueDate
                    })
                ));
            }

            // Refetch to ensure frontend state (especially total) is perfectly in sync with backend logic
            const updated = await api.requests.get(id!);
            setLineItems(updated.lineItems || []);
            setFormData(prev => ({
                ...prev,
                estimatedTotalAmount: updated.estimatedTotalAmount?.toString() || '0'
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
                        message: id ? 'Pedido atualizado com sucesso.' : 'Rascunho salvo. Itens serão inseridos na tela "Gestão de Cotações".',
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

    const handleRequestAction = async (action: ApprovalActionType) => {        if (action === 'APPROVE' && status === 'WAITING_AREA_APPROVAL' && requestTypeCode === 'QUOTATION') {
            const selectedQ = quotations.find(q => q.isSelected);
            if (!selectedQ) {
                setModalFeedback({ type: 'error', message: 'É necessário selecionar uma cotação vencedora antes de aprovar.' });
                return;
            }
        }

        if (!id) return;

        if ((action === 'REJECT' || action === 'REQUEST_ADJUSTMENT') && !approvalComment.trim()) {
            setModalFeedback({
                type: 'error',
                message: action === 'REJECT' ? 'Informe o motivo da rejeição.' : 'Informe o motivo do reajuste.'
            });
            scrollToFirstError({ Comment: ['Requerido'] }, '.modal-content');
            return;
        }

        setModalFeedback({ type: 'error', message: null });

        // Mandatory Attachment Validations
        if (action === 'REGISTER_PO' && !hasAttachment('PO')) {
            setModalFeedback({ type: 'error', message: 'É necessário anexar a P.O antes de registrar.' });
            return;
        }
        if (action === 'SCHEDULE_PAYMENT' && !hasAttachment('PAYMENT_SCHEDULE')) {
            setModalFeedback({ type: 'error', message: 'É necessário anexar o Cronograma de Pagamento antes de agendar.' });
            return;
        }
        if (action === 'COMPLETE_PAYMENT' && !hasAttachment('PAYMENT_PROOF')) {
            setModalFeedback({ type: 'error', message: 'É necessário anexar o Comprovante de Pagamento antes de concluir.' });
            return;
        }
        if (action === 'COMPLETE_QUOTATION') {
            try {
                const totalItemsCount = lineItems.length + quotations.reduce((acc: number, q: SavedQuotationDto) => acc + q.itemCount, 0);
                await completeQuotationAction(id!, totalItemsCount, approvalComment);
                setApprovalProcessing(false);
                setFeedback({ type: 'success', message: 'Cotação concluída e enviada para aprovação.' });
                setShowApprovalModal({ show: false, type: null });
                setTimeout(() => navigate('/requests'), 2000);
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
                    ? await api.requests.approveArea(id, approvalComment, quotations.find(q => q.isSelected)?.id)
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
            } else if (action === 'REGISTER_PO') {
                result = await api.requests.registerPo(id, approvalComment);
            } else if (action === 'SCHEDULE_PAYMENT') {
                result = await api.requests.schedulePayment(id, approvalComment);
            } else if (action === 'COMPLETE_PAYMENT') {
                result = await api.requests.completePayment(id, approvalComment);
            } else if (action === 'MOVE_TO_RECEIPT') {
                result = await api.requests.moveToReceipt(id, approvalComment);
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

    const handleSubmitRequest = async () => {
        if (!id && !isCopyMode) return;

        // Rework Validation: Rely on mandatory field and attachment checks below

        // 1. Mandatory Content check
        // PAYMENT: Requires at least one item
        // QUOTATION: Requires at least one item OR one attachment
        if (requestTypeCode === 'PAYMENT' && lineItems.length === 0) {
            setFeedback({
                type: 'error',
                message: 'Para submeter, o pedido deve conter pelo menos um item.'
            });
            itemsSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
            setIsItemsHighlighted(true);
            setTimeout(() => setIsItemsHighlighted(false), 5000);
            return;
        }

        if (requestTypeCode === 'QUOTATION' && lineItems.length === 0 && attachments.length === 0) {
            setFeedback({
                type: 'error',
                message: 'O pedido de cotação deve conter pelo menos itens ou um anexo descritivo antes de ser submetido.'
            });
            setIsItemsHighlighted(true);
            setIsAttachmentsHighlighted(true);
            setTimeout(() => {
                setIsItemsHighlighted(false);
                setIsAttachmentsHighlighted(false);
            }, 5000);
            return;
        }

        // 1.1 Mandatory Proforma check (Only for PAYMENT types)
        if (requestTypeCode === 'PAYMENT' && !hasAttachment('PROFORMA')) {
            setFeedback({
                type: 'error',
                message: 'É necessário anexar a Proforma antes de submeter o pedido.'
            });
            // Scroll to attachments section and highlight
            const attachmentsSection = document.getElementById('attachments-section');
            if (attachmentsSection) {
                attachmentsSection.scrollIntoView({ behavior: 'smooth', block: 'center' });
            } else {
                window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' });
            }
            setIsAttachmentsHighlighted(true);
            setTimeout(() => setIsAttachmentsHighlighted(false), 5000);
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

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        borderRadius: 'var(--radius-md)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-sm)',
        transition: 'all 0.2s',
        backgroundColor: '#fcfcfc'
    };

    const sectionTitleStyle: React.CSSProperties = {
        fontSize: '0.85rem',
        fontWeight: 900,
        textTransform: 'uppercase',
        letterSpacing: '0.08em',
        color: 'var(--color-primary)',
        paddingBottom: '12px',
        borderBottom: '1px solid var(--color-border)',
        marginBottom: '24px',
        display: 'flex',
        alignItems: 'center',
        gap: '12px'
    };

    const labelStyle: React.CSSProperties = {
        display: 'flex',
        flexDirection: 'column',
        gap: '8px',
        fontSize: '0.7rem',
        fontWeight: 800,
        textTransform: 'uppercase',
        color: 'var(--color-text-muted)',
        letterSpacing: '0.02em'
    };

    const getFieldErrors = (fieldName: string) => {
        if (!fieldErrors) return null;
        const normalizedField = fieldName.toLowerCase();
        const key = Object.keys(fieldErrors).find(k => {
            const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
            return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
        });
        return key ? fieldErrors[key] : null;
    };

    const renderFieldError = (fieldName: string) => {
        const errors = getFieldErrors(fieldName);
        if (!errors) return null;
        return (
            <div style={{ color: '#EF4444', fontSize: '0.75rem', marginTop: '4px', position: 'absolute' }}>
                {errors[0]}
            </div>
        );
    };

    const getInputStyle = (fieldName: string) => ({
        ...inputStyle,
        ...(getFieldErrors(fieldName) ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2', boxShadow: '0 0 0 4px rgba(239, 68, 68, 0.1)' } : {})
    });


    if (loading) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1400px', margin: '0 auto' }}>
                <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div style={{ padding: '20px', textAlign: 'center', backgroundColor: '#fff', borderRadius: '8px', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}>
                    <div style={{ width: '40px', height: '40px', border: '3px solid var(--color-border)', borderTopColor: 'var(--color-primary)', borderRadius: '50%', animation: 'spin 1s linear infinite', margin: '0 auto 16px auto' }}></div>
                    <div style={{ fontWeight: 600, color: 'var(--color-text-main)' }}>Carregando detalhes do pedido...</div>
                </div>
            </div>
        );
    }

    // Defensive Guard: If data failed to load and we're not in copy mode, avoid white-screen crash
    // We check initialFormData as it's only populated after a successful loadData cycle.
    if (!loading && !initialFormData && !isCopyMode) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1400px', margin: '100px auto', textAlign: 'center' }}>
                <Feedback type={feedback.type} message={feedback.message || 'Erro ao carregar o pedido.'} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '1.25rem' }}>O pedido solicitado não pôde ser encontrado ou carregado.</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Isso pode ocorrer se o pedido foi excluído ou se há um problema de conexão.</div>
                <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', marginTop: '24px' }}>
                    <button onClick={() => window.location.reload()} className="btn-primary" style={{ padding: '10px 24px' }}>Tentar Novamente</button>
                    <button onClick={() => navigate('/requests')} className="btn-secondary" style={{ padding: '10px 24px' }}>Voltar para a Lista</button>
                </div>
            </div>
        );
    }

    const headerProps = {
        breadcrumbs: [
            { label: 'Dashboard', to: '/' },
            { label: 'Pedidos', to: `/requests${location.state?.fromList || ''}` },
            { label: status === 'DRAFT' ? 'Editar' : isReworkStatus ? 'Reajustar' : 'Visualizar' }
        ] as BreadcrumbItem[],
        title: status === 'DRAFT' ? 'Editar Pedido' : isReworkStatus ? 'Reajustar Pedido' : 'Pedido',
        requestNumber,
        statusBadge: statusFullName && (
            <span className={`badge badge-sm badge-${
                statusBadgeColor === 'red' ? 'danger' :
                statusBadgeColor === 'yellow' ? 'warning' :
                statusBadgeColor === 'green' ? 'success' :
                statusBadgeColor || 'neutral'
            }`} style={{ marginLeft: '8px' }}>
                {statusFullName}
            </span>
        ),
        contextBadges: (
            <>
                {isFullyReadOnly && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#f8fafc', color: '#64748b',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #e2e8f0',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldAlert size={12} /> MODO DE CONSULTA
                    </span>
                )}
                {isQuotationPartiallyEditable && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#e0f2fe', color: '#0369a1',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #bae6fd',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldCheck size={12} /> COTAÇÃO ATIVA
                    </span>
                )}
                {isReworkStatus && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#fff7ed', color: '#ea580c',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #ffedd5',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldAlert size={12} /> REAJUSTE NECESSÁRIO
                    </span>
                )}
            </>
        ),
        secondaryActions: (
            <>
                <button
                    type="button"
                    onClick={() => navigate(`/requests`)}
                    style={{
                        height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)',
                        backgroundColor: 'var(--color-bg-surface)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                        fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.7rem', color: 'var(--color-text-main)',
                        boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s'
                    }}
                >
                    {isCopyMode ? <><X size={14} /> DESCARTAR CÓPIA</> : (status === 'DRAFT' ? <><X size={14} /> CANCELAR</> : <><ArrowLeft size={14} /> VOLTAR</>)}
                </button>
            </>
        ),
        primaryActions: (
            <>
                {status === 'DRAFT' && !isCopyMode && (
                    <button
                        type="button"
                        onClick={() => handleDeleteRequest()}
                        disabled={saving}
                        style={{
                            height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid #EF4444',
                            backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.75rem'
                        }}
                    >
                        <Trash2 size={14} /> EXCLUIR
                    </button>
                )}
                {canCancelRequest && status !== 'DRAFT' && (
                    <button
                        type="button"
                        onClick={() => setShowApprovalModal({ show: true, type: 'CANCEL_REQUEST' })}
                        disabled={saving || submitting}
                        style={{
                            height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: '1px solid #EF4444',
                            backgroundColor: 'white', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.7rem',
                            boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s'
                        }}
                    >
                        <X size={14} /> CANCELAR PEDIDO
                    </button>
                )}
                {(isDraftEditable || isQuotationPartiallyEditable) && !isCopyMode && (
                    <button
                        onClick={handleSubmit}
                        disabled={saving}
                        className="btn-primary"
                        style={{ height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', borderRadius: 'var(--radius-sm)' }}
                    >
                        <Save size={14} /> {saving ? 'SALVANDO...' : 'SALVAR'}
                    </button>
                )}
                {isDraftEditable && (
                    <button
                        onClick={handleSubmitRequest}
                        disabled={submitting || saving}
                        className="btn-primary"
                        style={{
                            height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px',
                            fontSize: '0.75rem', backgroundColor: 'var(--color-primary)', borderRadius: 'var(--radius-sm)'
                        }}
                    >
                        <Send size={14} /> {submitting ? 'PROCESSANDO...' : (isReworkStatus ? 'REENVIAR' : 'SUBMETER')}
                    </button>
                )}
            </>
        ),
        operationalGuidance: (status ? getRequestGuidance(status, requestTypeCode || '') : null) as OperationalGuidance | null,
        feedback,
        onCloseFeedback: () => setFeedback(prev => ({ ...prev, message: null }))
    };

    return (

        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.4 }}
            style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', maxWidth: '1440px', margin: '0 auto', minWidth: 0 }}
        >

            {/* Sticky Header Unit - Feedback, Banners, and Main Action Header */}
            <RequestActionHeader {...headerProps}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {/* Area Approval Action Bar */}
                    {(requestTypeCode === 'PAYMENT' || requestTypeCode === 'QUOTATION') && status === 'WAITING_AREA_APPROVAL' && isAreaApprover && (
                        <div style={{
                            backgroundColor: 'white',
                            padding: '12px 16px',
                            borderRadius: '4px',
                            border: '1px solid var(--color-primary)',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            gap: '16px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <ShieldAlert size={18} color="var(--color-primary)" />
                                <span style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-text-main)', textTransform: 'uppercase' }}>APROVAÇÃO DA ÁREA</span>
                            </div>

                            <div style={{ display: 'flex', gap: '8px' }}>
                                <button
                                    onClick={() => setShowApprovalModal({ show: true, type: 'APPROVE' })}
                                    className="btn-success"
                                    style={{ height: '32px', padding: '0 12px', display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 800, fontSize: '0.7rem' }}
                                >
                                    <ShieldCheck size={14} /> APROVAR
                                </button>
                                {requestTypeCode !== 'PAYMENT' && (
                                    <button
                                        onClick={() => setShowApprovalModal({ show: true, type: 'REQUEST_ADJUSTMENT' })}
                                        style={{
                                            height: '32px', padding: '0 12px', background: 'none', border: '1px solid var(--color-primary)',
                                            color: 'var(--color-primary)', cursor: 'pointer', fontWeight: 800, borderRadius: '4px',
                                            fontFamily: 'var(--font-family-display)', fontSize: '0.7rem'
                                        }}
                                    >
                                        REAJUSTE
                                    </button>
                                )}
                                <button
                                    onClick={() => setShowApprovalModal({ show: true, type: 'REJECT' })}
                                    style={{
                                        height: '32px', padding: '0 12px', background: 'none', border: '1px solid #EF4444',
                                        color: '#EF4444', cursor: 'pointer', fontWeight: 800, borderRadius: '4px',
                                        fontFamily: 'var(--font-family-display)', fontSize: '0.7rem'
                                    }}
                                >
                                    REJEITAR
                                </button>
                            </div>
                        </div>
                    )}

                    {/* Final Approval Action Bar */}
                    {(requestTypeCode === 'PAYMENT' || requestTypeCode === 'QUOTATION') && status === 'WAITING_FINAL_APPROVAL' && isFinalApprover && (
                        <div style={{
                            backgroundColor: 'white',
                            padding: '12px 16px',
                            borderRadius: '4px',
                            border: '1px solid var(--color-status-green)',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            gap: '16px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <ShieldCheck size={18} color="var(--color-status-green)" />
                                <span style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-text-main)', textTransform: 'uppercase' }}>APROVAÇÃO FINAL</span>
                            </div>

                            <div style={{ display: 'flex', gap: '8px' }}>
                                <button
                                    onClick={() => {
                                        if (requestTypeCode === 'QUOTATION' && !selectedQuotationId) {
                                            setFeedback({ type: 'error', message: 'Selecione uma cotação vencedora antes de aprovar.' });
                                            return;
                                        }
                                        setShowApprovalModal({ show: true, type: 'APPROVE' });
                                    }}
                                    className="btn-success"
                                    disabled={requestTypeCode === 'QUOTATION' && !selectedQuotationId}
                                    style={{ 
                                        height: '32px', padding: '0 12px', display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 800, fontSize: '0.7rem',
                                        opacity: (requestTypeCode === 'QUOTATION' && !selectedQuotationId) ? 0.5 : 1,
                                        cursor: (requestTypeCode === 'QUOTATION' && !selectedQuotationId) ? 'not-allowed' : 'pointer'
                                    }}
                                    title={requestTypeCode === 'QUOTATION' && !selectedQuotationId ? 'Selecione uma cotação vencedora primeiro' : 'Aprovar Pedido'}
                                >
                                    <ShieldCheck size={14} /> APROVAR
                                </button>
                                {requestTypeCode !== 'PAYMENT' && (
                                    <button
                                        onClick={() => setShowApprovalModal({ show: true, type: 'REQUEST_ADJUSTMENT' })}
                                        style={{
                                            height: '32px', padding: '0 12px', background: 'none', border: '1px solid var(--color-primary)',
                                            color: 'var(--color-primary)', cursor: 'pointer', fontWeight: 800, borderRadius: '4px',
                                            fontFamily: 'var(--font-family-display)', fontSize: '0.7rem'
                                        }}
                                    >
                                        REAJUSTE
                                    </button>
                                )}
                                <button
                                    onClick={() => setShowApprovalModal({ show: true, type: 'REJECT' })}
                                    style={{
                                        height: '32px', padding: '0 12px', background: 'none', border: '1px solid #EF4444',
                                        color: '#EF4444', cursor: 'pointer', fontWeight: 800, borderRadius: '4px',
                                        fontFamily: 'var(--font-family-display)', fontSize: '0.7rem'
                                    }}
                                >
                                    REJEITAR
                                </button>
                            </div>
                        </div>
                    )}

                    {/* Procurement/Buyer Status Panel (Former Action Bar) */}
                    {canExecuteOperationalAction && ['APPROVED', 'PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT'].includes(status || '') && (
                        <div style={{
                            backgroundColor: 'white',
                            padding: '12px 24px',
                            borderRadius: '4px',
                            border: '1px solid var(--color-primary)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '4px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                             <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                <span style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    Status do Fluxo
                                </span>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end' }}>
                                <div style={{ display: 'flex', gap: '24px' }}>
                                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                                        <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Responsável atual</span>
                                        <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                            {getRequestGuidance(status || '', requestTypeCode).responsible}
                                        </span>
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                                        <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Próxima ação / Situação</span>
                                        <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                            {getRequestGuidance(status || '', requestTypeCode).nextAction}
                                        </span>
                                    </div>
                                </div>

                                {/* ADDED: Operational Actions Buttons (Only visible to BUYER mode) */}
                                {/* Buyer actions */}
                                {isBuyer && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        {status === 'APPROVED' && (
                                            <button 
                                                onClick={() => setShowApprovalModal({ show: true, type: 'REGISTER_PO' })}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                            >
                                                <FileText size={14} /> REGISTRAR P.O
                                            </button>
                                        )}
                                        {/* TRANSITIONAL FALLBACK: Removed direct access from Buyer to enforce Receiving ownership */}
                                    </div>
                                )}

                                {/* Receiving actions */}
                                {isReceiving && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        {(status === 'PAYMENT_COMPLETED' || status === 'WAITING_RECEIPT') && (
                                            <div style={{ display: 'flex', gap: '8px' }}>
                                                <button 
                                                    onClick={() => setShowApprovalModal({ show: true, type: 'MOVE_TO_RECEIPT' })}
                                                    className="btn-primary"
                                                    style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                                >
                                                    <ArrowRight size={14} /> MOVER PARA RECEBIMENTO
                                                </button>
                                                <button 
                                                    onClick={() => setShowApprovalModal({ show: true, type: 'FINALIZE' })}
                                                    className="btn-success"
                                                    style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                                >
                                                    <CheckCircle size={14} /> FINALIZAR PEDIDO
                                                </button>
                                            </div>
                                        )}
                                    </div>
                                )}

                                {/* Finance actions */}
                                {isFinance && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        {status === 'PO_ISSUED' && (
                                            <button 
                                                onClick={() => setShowApprovalModal({ show: true, type: 'SCHEDULE_PAYMENT' })}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                            >
                                                <Clock size={14} /> AGENDAR PAGAMENTO
                                            </button>
                                        )}
                                        {(status === 'PO_ISSUED' || status === 'PAYMENT_SCHEDULED') && (
                                            <button 
                                                onClick={() => setShowApprovalModal({ show: true, type: 'COMPLETE_PAYMENT' })}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                            >
                                                <Check size={14} /> CONFIRMAR PAGAMENTO
                                            </button>
                                        )}
                                    </div>
                                )}
                            </div>
                        </div>
                    )}

                    {/* Quotation Status Panel (Former Action Bar) - Isolated for QUOTATION workflow */}
                    {canExecuteOperationalAction && requestTypeCode === 'QUOTATION' && isQuotationPartiallyEditable && (
                        <div style={{
                            backgroundColor: 'white',
                            padding: '12px 24px',
                            borderRadius: '4px',
                            border: '1px solid var(--color-status-blue)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '4px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                             <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                <span style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-status-blue)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    Andamento do Pedido
                                </span>
                            </div>
                            <div style={{ display: 'flex', gap: '24px' }}>
                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Responsável atual</span>
                                    <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>Comprador</span>
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Próxima ação</span>
                                    <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>Pedido em tratamento do comprador</span>
                                </div>
                            </div>
                        </div>
                    )}
                </div>
            </RequestActionHeader>

            {/* Form Card */}
            <form onSubmit={handleSubmit} style={{
                display: 'flex',
                flexDirection: 'column',
                gap: '32px'
            }}>
                {isQuotationPartiallyEditable && (
                    <div style={{ 
                        margin: '0 0 24px', padding: '16px', borderRadius: 'var(--radius-lg)', 
                        backgroundColor: '#f0f9ff', border: '1px solid #0ea5e9',
                        display: 'flex', alignItems: 'center', gap: '16px'
                    }}>
                        <ShieldCheck size={24} color="#0ea5e9" />
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            <span style={{ fontWeight: 800, fontSize: '0.85rem', color: '#0369a1', textTransform: 'uppercase' }}>
                                Modo de Edição Parcial Ativo
                            </span>
                            <span style={{ fontSize: '0.8rem', color: '#0c4a6e' }}>
                                O pedido está em fase de cotação. Você pode ajustar o título, descrição e justificativa. 
                                {hasSavedQuotations ? ' Os itens e fornecedor estão bloqueados por existirem cotações salvas.' : ' Itens e fornecedor permanecem editáveis até que a primeira cotação seja registrada.'}
                            </span>
                        </div>
                    </div>
                )}

                <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-border-heavy)' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px', borderBottom: '3px solid var(--color-primary)', paddingBottom: '12px' }}>
                        <h2 style={{ ...sectionTitleStyle, borderBottom: 'none', marginBottom: 0, paddingBottom: 0, marginTop: 0, fontSize: '1.1rem', fontWeight: 900 }}>Dados Gerais do Pedido</h2>
                        {requestNumber && (
                            <div style={{
                                display: 'flex',
                                flexDirection: 'column',
                                alignItems: 'flex-end',
                                gap: '2px',
                                borderLeft: '2px solid var(--color-border)',
                                paddingLeft: '16px'
                            }}>
                                <span style={{ fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                                    Código de Rastreio
                                </span>
                                <span style={{
                                    fontSize: '1.4rem',
                                    fontWeight: 900,
                                    color: 'var(--color-primary)',
                                    fontFamily: 'var(--font-family-display)',
                                    lineHeight: 1
                                }}>
                                    {requestNumber}
                                </span>
                            </div>
                        )}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                        <label style={labelStyle}>
                            Título do Pedido <span style={{ color: 'red' }}>*</span>
                            <input
                                required
                                type="text"
                                name="title"
                                value={formData.title}
                                onChange={handleChange}
                                placeholder="Ex: Aquisição de Laptops para TI"
                                style={getInputStyle('Title')}
                                disabled={!canEditHeader}
                            />
                            {renderFieldError('Title')}
                        </label>

                        <label style={labelStyle}>
                            Descrição ou Justificativa <span style={{ color: 'red' }}>*</span>
                            <textarea
                                required
                                name="description"
                                value={formData.description}
                                onChange={handleChange}
                                rows={4}
                                placeholder="Explique o motivo e os detalhes primários..."
                                style={{ ...getInputStyle('Description'), resize: 'vertical' }}
                                disabled={!canEditHeader}
                            />
                            {renderFieldError('Description')}
                        </label>

                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                            <label style={labelStyle}>
                                Tipo de Pedido <span style={{ color: 'red' }}>*</span>
                                <select
                                    name="requestTypeId"
                                    value={formData.requestTypeId}
                                    onChange={handleChange}
                                    style={getInputStyle('RequestTypeId')}
                                    disabled={!canEditHeader || isQuotationPartiallyEditable || status !== 'DRAFT'}
                                >
                                    <option value={1}>COTAÇÃO (COM)</option>
                                    <option value={2}>PAGAMENTO (PAG)</option>
                                </select>
                                {renderFieldError('RequestTypeId')}
                            </label>

                            <label style={labelStyle}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                    <span>
                                        Fornecedor {Number(formData.requestTypeId) === 1 && <span style={{ color: 'red' }}>*</span>}
                                    </span>
                                    {canEditSupplier && (
                                        <button
                                            type="button"
                                            onClick={() => setQuickSupplierModal({
                                                show: true,
                                                initialName: supplierName,
                                                initialTaxId: '' // Persist if available in backend DTO later
                                            })}
                                            style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', padding: 0, textDecoration: 'underline', display: 'flex', alignItems: 'center', gap: '4px' }}
                                        >
                                            <UserPlus size={12} />
                                            + NOVO FORNECEDOR
                                        </button>
                                    )}
                                </div>
                                <SupplierAutocomplete
                                    initialName={supplierName}
                                    initialPortalCode={supplierPortalCode}
                                    onChange={(id, name) => {
                                        setFormData(prev => ({ ...prev, supplierId: id ? String(id) : '' }));
                                        setSupplierName(name);
                                        // Once selected, we don't strictly need to update supplierPortalCode state 
                                        // for the autocomplete itself as handles its own query, 
                                        // but it's good practice.
                                        setSupplierPortalCode('');
                                        clearFieldError('SupplierId');
                                    }}
                                    hasError={!!getFieldErrors('SupplierId')}
                                    className="mt-1"
                                    disabled={!canEditSupplier}
                                />
                                {canEditSupplier && !formData.supplierId && supplierName && (
                                    <div style={{ marginTop: '8px', padding: '10px 12px', backgroundColor: '#fff7ed', border: '1px solid #ffedd5', borderRadius: '4px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                            <AlertCircle size={16} color="#c2410c" />
                                            <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#9a3412' }}>
                                                Fornecedor sugerido <strong>"{supplierName}"</strong> não encontrado.
                                            </span>
                                        </div>
                                        <button
                                            type="button"
                                            onClick={() => setQuickSupplierModal({
                                                show: true,
                                                initialName: supplierName,
                                                initialTaxId: ''
                                            })}
                                            style={{ 
                                                backgroundColor: '#f97316', color: '#fff', border: 'none', padding: '4px 10px', 
                                                borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800, cursor: 'pointer',
                                                textTransform: 'uppercase'
                                            }}
                                        >
                                            CRIAR AGORA
                                        </button>
                                    </div>
                                )}
                                {isQuotationStage && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Em tratamento pelo comprador.
                                    </span>
                                )}
                                {renderFieldError('SupplierId')}
                            </label>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '24px' }}>
                            <label style={labelStyle}>
                                Grau de Necessidade <span style={{ color: 'red' }}>*</span>
                                <select name="needLevelId" value={formData.needLevelId} onChange={handleChange} style={getInputStyle('NeedLevelId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {needLevels.filter(nl => nl.isActive || Number(formData.needLevelId) === nl.id).map(nl => (
                                        <option key={nl.id} value={nl.id}>{nl.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('NeedLevelId')}
                            </label>

                            <AnimatePresence>
                                {(requestTypeCode === 'QUOTATION' || Number(formData.requestTypeId) === 1 || requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) && (
                                    <motion.div
                                        initial={{ opacity: 0, height: 0 }}
                                        animate={{ opacity: 1, height: 'auto' }}
                                        exit={{ opacity: 0, height: 0 }}
                                        style={{ overflow: 'hidden' }}
                                    >
                                        <label style={labelStyle}>
                                            {(requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) ? 'Data de vencimento' : 'Necessário até (Data limite)'} <span style={{ color: 'red' }}>*</span>
                                            <DateInput
                                                required
                                                name="needByDateUtc"
                                                value={formData.needByDateUtc}
                                                onChange={(val) => {
                                                    setFormData(prev => ({ ...prev, needByDateUtc: val }));
                                                    clearFieldError('NeedByDateUtc');
                                                }}
                                                hasError={!!getFieldErrors('NeedByDateUtc')}
                                                style={getInputStyle('NeedByDateUtc')}
                                                disabled={!canEditHeader}
                                            />
                                            {renderFieldError('NeedByDateUtc')}
                                            {!getFieldErrors('NeedByDateUtc') && formData.needByDateUtc && new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0) && (
                                                <div style={{ color: '#D97706', fontSize: '0.75rem', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600 }}>
                                                    <AlertTriangle size={12} />
                                                    {(requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) ? 'O documento está vencido.' : 'A data selecionada está no passado.'}
                                                </div>
                                            )}
                                        </label>
                                    </motion.div>
                                )}
                            </AnimatePresence>

                            <label style={labelStyle}>
                                Departamento <span style={{ color: 'red' }}>*</span>
                                <select name="departmentId" value={formData.departmentId} onChange={handleChange} style={getInputStyle('DepartmentId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {departments.filter(d => d.isActive || Number(formData.departmentId) === d.id).map(d => (
                                        <option key={d.id} value={d.id}>{d.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('DepartmentId')}
                            </label>

                            <label style={labelStyle}>
                                Empresa <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="companyId" 
                                    value={formData.companyId} 
                                    onChange={handleChange} 
                                    style={{
                                        ...getInputStyle('CompanyId'),
                                        ...(lineItems.length > 0 || isQuotationPartiallyEditable || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={!canEditHeader || isQuotationPartiallyEditable || lineItems.length > 0}
                                >
                                    <option value="">-- Selecione --</option>
                                    {companies.filter(c => c.isActive || Number(formData.companyId) === c.id).map(c => (
                                        <option key={c.id} value={c.id}>{c.name}</option>
                                    ))}
                                </select>
                                {lineItems.length > 0 && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Empresa bloqueada pois o pedido já possui itens.
                                    </span>
                                )}
                                {renderFieldError('CompanyId')}
                            </label>

                            <label style={labelStyle}>
                                Planta <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="plantId" 
                                    value={formData.plantId} 
                                    onChange={handleChange} 
                                    style={{
                                        ...getInputStyle('PlantId'),
                                        ...(!formData.companyId || isQuotationPartiallyEditable || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={!canEditHeader || isQuotationPartiallyEditable || !formData.companyId}
                                >
                                    <option value="">-- Selecione --</option>
                                    {plants.filter(p => (p.isActive && (!formData.companyId || p.companyId === Number(formData.companyId))) || formData.plantId === p.id.toString()).map(p => (
                                        <option key={p.id} value={p.id}>{p.name}</option>
                                    ))}
                                </select>
                                {!formData.companyId && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Selecione uma empresa primeiro.
                                    </span>
                                )}
                                {renderFieldError('PlantId')}
                            </label>
                        </div>

                    </div>
                </section>



                {/* Section C: Resumo Financeiro do Pedido */}
                <CollapsibleSection
                    title="Resumo Financeiro do Pedido"
                    count={0}
                    isOpen={sectionsOpen.finance}
                    onToggle={() => toggleSection('finance')}
                >
                    <div style={{ padding: '32px' }}>
                        <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '32px', alignItems: 'start' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', padding: '16px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                                    Valor Total Estimado
                                </span>
                                <span style={{ fontSize: '2rem', fontWeight: 800, color: 'var(--color-text-main)', fontFamily: 'var(--font-family-display)' }}>
                                    {formatCurrencyAO(Number(formData.estimatedTotalAmount) || 0)}
                                </span>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                    Este valor é recalculado automaticamente a partir dos itens inseridos ao recarregar a tela.
                                </span>
                            </div>

                            <label style={labelStyle}>
                                Moeda Principal do Pedido
                                <select
                                    name="currencyId"
                                    value={formData.currencyId}
                                    onChange={handleChange}
                                    style={{
                                        ...getInputStyle('CurrencyId'),
                                        ...(lineItems.length > 0 || !canEdit ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={lineItems.length > 0 || !canEditHeader}
                                >
                                    {currencies.map(c => (
                                        <option key={c.id} value={c.id}>{c.code} ({c.symbol})</option>
                                    ))}
                                </select>
                                {lineItems.length > 0 && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Moeda bloqueada pois o pedido já possui itens.
                                    </span>
                                )}
                                {renderFieldError('CurrencyId')}
                            </label>
                        </div>
                    </div>
                </CollapsibleSection>

            </form>

            {/* Line Items Section */}
            <CollapsibleSection
                title={selectedQuotationId ? 'Itens da Cotação Vencedora' : 'Itens do Pedido'}
                count={selectedQuotationId ? (quotations.find(q => q.id === selectedQuotationId)?.items.length || 0) : lineItems.length}
                isOpen={sectionsOpen.items}
                onToggle={() => toggleSection('items')}
            >
                <div
                    id="items-section-container"
                    ref={itemsSectionRef}
                    style={{
                        padding: '32px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '24px',
                        transition: 'box-shadow 0.5s ease',
                        boxShadow: isItemsHighlighted ? 'inset 0 0 0 4px #EF4444' : 'none'
                    }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                        <h2 id="items-section" style={{ ...sectionTitleStyle, borderBottom: 'none', marginBottom: 0, marginTop: 0 }}>
                            {selectedQuotationId ? 'Detalhamento da Proposta' : 'Itens da Requisição'}
                        </h2>
                        {selectedQuotationId && (
                            <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.75rem', fontWeight: 900, padding: '4px 12px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                Autoridade: Cotação Selecionada
                            </span>
                        )}
                        {canEditItems && !itemForm && !selectedQuotationId && (
                            <button
                                type="button"
                                onClick={() => setItemForm({ itemPriority: 'MEDIUM', quantity: 1, unit: '' })}
                                className="btn-primary"
                                style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
                            >
                                <Plus size={18} />
                                Adicionar Item
                            </button>
                        )}
                    </div>

                    {selectedQuotationId && (
                        <div style={{ backgroundColor: '#f0f4ff', padding: '16px', borderRadius: '8px', borderLeft: '4px solid #4f46e5', marginBottom: '16px' }}>
                            <p style={{ fontSize: '0.875rem', color: '#1e1b4b', margin: 0 }}>
                                <strong>Nota:</strong> Este pedido está vinculado à cotação vencedora. Os itens abaixo refletem a proposta comercial do fornecedor selecionado.
                            </p>
                        </div>
                    )}

                    <div style={{ overflowX: 'auto' }}>
                        <table className="alpla-table">
                            <thead>
                                <tr>
                                    <th>#</th>
                                    <th>Descrição</th>
                                    <th style={{ textAlign: 'center' }}>Unid.</th>
                                    <th style={{ textAlign: 'right' }}>Qtd</th>
                                    {!selectedQuotationId && <th>Planta</th>}
                                    {!selectedQuotationId && <th>IVA</th>}
                                    <th style={{ textAlign: 'right' }}>Preço Unit.</th>
                                    <th style={{ textAlign: 'right' }}>Total</th>
                                    {!selectedQuotationId && <th style={{ textAlign: 'center' }}>Ações</th>}
                                </tr>
                            </thead>
                            <tbody>
                                {selectedQuotationId ? (
                                    // PIVOT: Show items from the selected quotation
                                    (quotations.find(q => q.id === selectedQuotationId)?.items || []).map((item, idx) => (
                                        <tr key={item.id || idx}>
                                            <td>{idx + 1}</td>
                                            <td style={{ fontWeight: 600 }}>{item.description}</td>
                                            <td style={{ textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>{item.unitCode || '---'}</td>
                                            <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                            <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                            <td style={{ textAlign: 'right', fontWeight: 700 }}>{formatCurrencyAO(item.lineTotal)}</td>
                                        </tr>
                                    ))
                                ) : lineItems.length === 0 ? (
                                    <tr>
                                        <td colSpan={9} style={{ textAlign: 'center', padding: '24px', color: 'var(--color-text-muted)' }}>Nenhum item adicionado a este pedido.</td>
                                    </tr>
                                ) : (
                                    lineItems.map(item => (
                                        <tr key={item.id}>
                                            <td>{item.lineNumber}</td>
                                            <td>{item.description}</td>
                                            <td style={{ textAlign: 'center' }}>{item.unit || '---'}</td>
                                            <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                            <td>{item.plantName || '---'}</td>
                                            <td>
                                                <span style={{ fontSize: '0.75rem' }}>{item.ivaRatePercent != null ? `${item.ivaRatePercent}%` : '---'}</span>
                                            </td>
                                            <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                            <td style={{ textAlign: 'right', fontWeight: 600 }}>{formatCurrencyAO(item.totalAmount)}</td>
                                            <td style={{ padding: '8px', textAlign: 'center' }}>
                                                <div style={{ display: 'flex', gap: '8px', justifyContent: 'center' }}>
                                                    {canEditItems ? (
                                                        <>
                                                            <button onClick={() => setItemForm(item)} style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer' }} title="Editar"><Edit2 size={16} /></button>
                                                            <button onClick={() => handleDeleteItem(item.id)} style={{ background: 'none', border: 'none', color: '#EF4444', cursor: 'pointer' }} title="Excluir"><Trash2 size={16} /></button>
                                                        </>
                                                    ) : (
                                                        <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Somente leitura</span>
                                                    )}
                                                </div>
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>

                    {itemForm && (
                        <motion.div
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                        >
                            <RequestLineItemForm
                                itemForm={itemForm}
                                setItemForm={setItemForm}
                                onSaveItem={handleSaveItem}
                                itemSaving={itemSaving}
                                units={units as any}
                                fieldErrors={fieldErrors}
                                clearFieldError={clearFieldError}
                                companyId={formData.companyId ? Number(formData.companyId) : null}
                                plants={(plants || []).filter(p => !formData.companyId || Number(p.companyId) === Number(formData.companyId))}
                                requestTypeCode={requestTypeCode || undefined}
                                costCenters={costCenters}
                                ivaRates={ivaRates}
                            />
                        </motion.div>
                    )}
                </div>
            </CollapsibleSection>

            {/* Section: Cotações Salvas */}
            {requestTypeCode === 'QUOTATION' && status !== 'CANCELLED' && status !== 'REJECTED' && (
                <CollapsibleSection
                    title="Cotações Salvas"
                    count={quotations.length}
                    isOpen={sectionsOpen.quotations}
                    onToggle={() => toggleSection('quotations')}
                >
                    <div 
                        ref={quotationsSectionRef}
                        style={{ 
                            padding: '32px',
                            transition: 'box-shadow 0.6s ease-in-out, border-color 0.6s ease-in-out',
                            boxShadow: isQuotationsHighlighted ? 'inset 0 0 0 4px rgba(239, 68, 68, 0.2), 0 0 20px rgba(239, 68, 68, 0.15)' : 'none',
                            border: isQuotationsHighlighted ? '2px solid #EF4444' : 'none',
                            borderRadius: 'inherit'
                        }}
                    >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                            <h2 style={{ ...sectionTitleStyle, margin: 0 }}>
                                Cotações Recebidas
                            </h2>
                            {isFinalApprover && status === 'WAITING_FINAL_APPROVAL' && requestTypeCode === 'QUOTATION' && (
                                <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.7rem', fontWeight: 900, padding: '4px 10px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                    AÇÃO: SELECIONAR VENCEDORA
                                </span>
                            )}
                        </div>
                        
                        <RequestQuotations 
                            quotations={quotations}
                            isFinalApproverMode={!!(isFinalApprover || isAreaApprover)}
                            isDecisionStage={status === 'WAITING_FINAL_APPROVAL' || status === 'WAITING_AREA_APPROVAL'}
                            onSelectWinner={async (qid) => {
                                try {
                                    await api.requests.selectQuotation(id!, qid);
                                    setFeedback({ type: 'success', message: 'Cotação vencedora selecionada!' });
                                    loadData();
                                } catch (err: any) {
                                    setFeedback({ type: 'error', message: err.message });
                                }
                            }}
                        />
                    </div>
                </CollapsibleSection>
            )}

            {/* Section: Pedido Anexos */}
            <CollapsibleSection
                title="Anexos do Pedido"
                count={attachments.length}
                isOpen={sectionsOpen.attachments}
                onToggle={() => toggleSection('attachments')}
            >
                <div style={{ padding: '32px' }}>
                    <RequestAttachments
                        id="attachments-section"
                        highlight={isAttachmentsHighlighted}
                        requestId={id || ''}
                        attachments={attachments}
                        canEdit={canManageAttachments}
                        onRefresh={handleAttachmentRefresh}
                        requestType={requestTypeCode || undefined}
                        status={status || undefined}
                    />
                </div>
            </CollapsibleSection>

            {/* Section D: Histórico do Pedido */}
            <CollapsibleSection
                title="Histórico do Pedido"
                count={statusHistory.length}
                isOpen={sectionsOpen.history}
                onToggle={() => toggleSection('history')}
            >
                <div style={{
                    padding: '32px'
                }}>
                    {statusHistory.length === 0 ? (
                        <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)' }}>
                            Nenhum registro de histórico disponível para este pedido.
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                            {statusHistory.map((entry, idx) => (
                                <div key={entry.id} style={{
                                    display: 'grid',
                                    gridTemplateColumns: 'minmax(150px, 1fr) 2fr 3fr',
                                    gap: '24px',
                                    padding: '20px',
                                    backgroundColor: idx % 2 === 0 ? 'var(--color-bg-surface)' : 'var(--color-bg-page)',
                                    borderLeft: '4px solid var(--color-primary)',
                                    position: 'relative'
                                }}>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Data / Hora</div>
                                        <div style={{ fontSize: '0.85rem', fontWeight: 600 }}>
                                            {formatDateTime(entry.createdAtUtc)}
                                        </div>
                                    </div>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Ação / Novo Status</div>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                            <span style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                                {entry.actionTaken === 'CREATE' ? 'CRIAÇÃO' :
                                                    entry.actionTaken === 'UPDATE' ? 'ATUALIZAÇÃO' :
                                                        entry.actionTaken === 'SUBMIT' ? 'SUBMISSÃO' : 
                                                            entry.actionTaken === 'DOCUMENTO ADICIONADO' ? 'DOC. ANEXADO' :
                                                                entry.actionTaken === 'DOCUMENTO REMOVIDO' ? 'DOC. REMOVIDO' :
                                                                    entry.actionTaken === 'ITEM_ADICIONADO' ? 'ITEM ADICIONADO' :
                                                                        entry.actionTaken === 'ITEM_ALTERADO' ? 'ITEM ALTERADO' :
                                                                            entry.actionTaken === 'ITEM_REMOVIDO' ? 'ITEM REMOVIDO' :
                                                                                entry.actionTaken}
                                            </span>
                                            <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
                                                ➡ {entry.newStatusName}
                                            </span>
                                        </div>
                                    </div>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Responsável / Comentário</div>
                                        <div style={{ fontSize: '0.85rem' }}>
                                            <span style={{ fontWeight: 700, display: 'block', marginBottom: '4px' }}>{entry.actorName}</span>
                                            {entry.comment && (
                                                <span style={{ color: 'var(--color-text-muted)', fontStyle: 'italic', fontSize: '0.8rem' }}>"{entry.comment}"</span>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </CollapsibleSection>

            {/* Approval Modal */}
            <ApprovalModal
                selectedQuotationName={quotations.find(q => q.isSelected)?.supplierNameSnapshot}
                show={showApprovalModal.show}
                type={showApprovalModal.type}
                status={status}
                isReworkStatus={isReworkStatus}
                onClose={() => {
                    setShowApprovalModal({ show: false, type: null });
                    setApprovalComment('');
                    setModalFeedback({ type: 'error', message: null });
                }}
                onConfirm={(action) => handleRequestAction(action!)}
                comment={approvalComment}
                setComment={setApprovalComment}
                processing={approvalProcessing || saving || submitting}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback(prev => ({ ...prev, message: null }))}
            />

            <QuickSupplierModal 
                isOpen={quickSupplierModal.show}
                onClose={() => setQuickSupplierModal({ show: false, initialName: '', initialTaxId: '' })}
                onSuccess={(supplier: { id: number; name: string; taxId?: string }) => {
                    setFormData(prev => ({ ...prev, supplierId: String(supplier.id) }));
                    setSupplierName(supplier.name);
                    setSupplierPortalCode('');
                    clearFieldError('SupplierId');
                }}
                initialName={quickSupplierModal.initialName}
                initialTaxId={quickSupplierModal.initialTaxId}
            />

        </motion.div >
    );
}
