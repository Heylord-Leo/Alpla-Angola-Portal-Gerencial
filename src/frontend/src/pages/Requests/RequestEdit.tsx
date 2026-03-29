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
    Check
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { formatCurrencyAO, getRequestGuidance, formatDateTime } from '../../lib/utils';
import { CurrencyDto, LookupDto, UserDto, RequestStatusHistoryDto, RequestAttachmentDto, RequestLineItemDto, SavedQuotationDto } from '../../types';
import { DateInput } from '../../components/DateInput';
import { RequestAttachments } from '../../components/RequestAttachments';
import { motion } from 'framer-motion';
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

    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [status, setStatus] = useState<string | null>(null);
    const [statusFullName, setStatusFullName] = useState<string | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    const itemsSectionRef = useRef<HTMLDivElement>(null);
    const [isItemsHighlighted, setIsItemsHighlighted] = useState(false);
    const [isAttachmentsHighlighted, setIsAttachmentsHighlighted] = useState(false);

    const [sectionsOpen, setSectionsOpen] = useState({
        participants: false,
        finance: false,
        items: true,
        quotations: false,
        attachments: false,
        history: false
    });

    const quotationsSectionRef = useRef<HTMLDivElement>(null);

    const toggleSection = (section: keyof typeof sectionsOpen) => {
        setSectionsOpen(prev => ({ ...prev, [section]: !prev[section] }));
    };
    const [requestTypeCode, setRequestTypeCode] = useState<string | null>(null);
    const [requestNumber, setRequestNumber] = useState<string | null>(null);
    const [initialFormData, setInitialFormData] = useState<any>(null); // This holds the object literal used in the form

    const { user } = useAuth();

    // Permissions based on real roles
    const isBuyer = user?.roles.includes(ROLES.BUYER);
    const isAreaApprover = user?.roles.includes(ROLES.AREA_APPROVER);
    const isFinalApprover = user?.roles.includes(ROLES.FINAL_APPROVER);
    const isFinance = user?.roles.includes(ROLES.FINANCE);
    const isReceiving = user?.roles.includes(ROLES.RECEIVING);

    // Approval & Operational State
    const [showApprovalModal, setShowApprovalModal] = useState<{
        show: boolean,
        type: ApprovalActionType
    }>({ show: false, type: null });
    const [approvalComment, setApprovalComment] = useState('');
    const [approvalProcessing, setApprovalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const isReworkStatus = status === 'AREA_ADJUSTMENT' || status === 'FINAL_ADJUSTMENT';
    const isQuotationStage = status === 'WAITING_QUOTATION';
    const isOperationalStage = ['APPROVED', 'PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'WAITING_QUOTATION'].includes(status || '');
    const isFinalizedStatus = ['COMPLETED', 'REJECTED', 'CANCELLED', 'QUOTATION_COMPLETED'].includes(status || '');

    const isDraftEditable = status === 'DRAFT' || isReworkStatus;
    const isQuotationPartiallyEditable = status === 'WAITING_QUOTATION';
    const isFullyReadOnly = !isDraftEditable && !isQuotationPartiallyEditable;

    // Precision booleans for operational gating


    const canEditHeader = status === 'DRAFT' || isReworkStatus;
    const canEditSupplier = status === 'DRAFT' || isReworkStatus;
    const canEditItems = status === 'DRAFT' || isReworkStatus;
    const canManageAttachments = status !== null && !isFinalizedStatus;
    const canExecuteOperationalAction = isOperationalStage || isQuotationStage;

    // canEdit legacy flag (primarily controls items and save button accessibility)
    const canEdit = canEditItems;

    useEffect(() => {
        const focus = searchParams.get('focus');

        if (location.state?.successMessage) {
            setFeedback({ type: 'success', message: location.state.successMessage });
            // Replace state so refresh doesn't trigger it again
            window.history.replaceState({}, document.title);
        }

        if (location.state?.successMessage || focus === 'items') {
            // Auto-scroll and highlight the line items section
            setTimeout(() => {
                itemsSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                setIsItemsHighlighted(true);
                setTimeout(() => setIsItemsHighlighted(false), 5000); // 5 seconds highlight

                // If query param exists, remove it without refreshing to keep URL clean
                if (focus === 'items') {
                    const newUrl = window.location.pathname;
                    window.history.replaceState({}, document.title, newUrl);
                }
            }, 300); // slight delay to ensure UI rendered
        }
    }, [location.state, location.search, searchParams]);

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
    const [supplierName, setSupplierName] = useState('');
    const [supplierPortalCode, setSupplierPortalCode] = useState('');

    const [lineItems, setLineItems] = useState<RequestLineItemDto[]>([]);
    const [itemForm, setItemForm] = useState<Partial<RequestLineItemDto> | null>(null);
    const [itemSaving, setItemSaving] = useState(false);
    const [statusHistory, setStatusHistory] = useState<RequestStatusHistoryDto[]>([]);
    const [attachments, setAttachments] = useState<RequestAttachmentDto[]>([]);
    const [quotations, setQuotations] = useState<SavedQuotationDto[]>([]);
    const [selectedQuotationId, setSelectedQuotationId] = useState<string | null>(null);
    const [costCenters, setCostCenters] = useState<LookupDto[]>([]);

    const hasAttachment = (typeCode: string) => {
        return attachments.some(a => a.attachmentTypeCode === typeCode && !(a as any).isDeleted);
    };

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

    // Master Data
    const [units, setUnits] = useState<LookupDto[]>([]);
    const [currencies, setCurrencies] = useState<CurrencyDto[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<LookupDto[]>([]);
    const [users, setUsers] = useState<UserDto[]>([]);

    const handleSaveItem = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!id || !itemForm) return;
        setItemSaving(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});
        try {
            // Find the ID of the selected unit from its code to map it back to the backend UnitId integer
            const selectedUnit = units.find(u => u.code === itemForm.unit);

            // Unit Decimal Validation
            if (selectedUnit && !selectedUnit.allowsDecimalQuantity) {
                // If it has a decimal point or comma, and the fractional part is not strictly .00, reject it.
                // An easier check is just ensuring the float parses equal to the int
                const qtyVal = Number(itemForm.quantity);
                if (!Number.isInteger(qtyVal)) {
                    setFieldErrors({
                        Quantity: [`A unidade '${selectedUnit.code}' não permite quantidades fracionadas(decimais).`]
                    });
                    setItemSaving(false);
                    return;
                }
            }

            const validPriorities = ['HIGH', 'MEDIUM', 'LOW'];
            const itemPriority = (itemForm.itemPriority || '').toUpperCase();

            if (!itemPriority || !validPriorities.includes(itemPriority)) {
                setFieldErrors(prev => ({ ...prev, ItemPriority: ['Informe a prioridade do item.'] }));
                setFeedback({ type: 'error', message: 'Informe a prioridade do item.' });
                setItemSaving(false);
                return;
            }

            if (!selectedUnit) {
                setFieldErrors(prev => ({ ...prev, UnitId: ['Informe a unidade do item.'] }));
                setFeedback({ type: 'error', message: 'Informe a unidade do item.' });
                setItemSaving(false);
                return;
            }

            const payload = {
                ...itemForm,
                itemPriority: itemPriority,
                quantity: Number(itemForm.quantity),
                unitPrice: Number(itemForm.unitPrice),
                unitId: selectedUnit.id,
                currencyId: Number(formData.currencyId) // Enforce header currency
            };
            if (itemForm.id) {
                await api.requests.updateLineItem(id, itemForm.id, payload);
            } else {
                await api.requests.createLineItem(id, payload);
            }
            // Refetch to cleanly pick up the server's new total & list
            const data = await api.requests.get(id);
            setLineItems(data.lineItems || []);
            setFormData(prev => ({ ...prev, estimatedTotalAmount: data.estimatedTotalAmount?.toString() || '0' }));
            setItemForm(null);
            setFeedback({ type: 'success', message: 'Item salvo com sucesso.' });
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                setFieldErrors(err.fieldErrors);
                setFeedback({ type: 'error', message: err.message || 'Existem campos inválidos no item.' });
                scrollToFirstError(err.fieldErrors, '#items-section-container');
                setIsItemsHighlighted(true);
                setTimeout(() => setIsItemsHighlighted(false), 5000);
            } else {
                setFeedback({ type: 'error', message: err.message || 'Erro ao salvar o item.' });
            }
        } finally {
            setItemSaving(false);
        }
    };

    const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

    const handleDeleteItem = async (itemId: string) => {
        if (!id) return;
        setPendingDeleteId(itemId);
        setShowApprovalModal({ show: true, type: 'DELETE_ITEM' });
    };

    const executeDeleteItem = async () => {
        if (!id || !pendingDeleteId) return;
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
    };

    const handleDeleteRequest = async (e?: React.MouseEvent) => {
        if (e) {
            e.preventDefault();
            e.stopPropagation();
        }

        if (!id) {
            setFeedback({ type: 'error', message: 'ID do pedido não localizado.' });
            return;
        }

        // Standardized Modal Confirmation
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
        if (!id) return;
        try {
            setLoading(true);
            const data = await api.requests.get(id);

            setStatus(data.statusCode);
            setStatusFullName(data.statusName);
            setLineItems(data.lineItems || []);
            setRequestTypeCode(data.requestTypeCode);
            setRequestNumber(data.requestNumber || null);
            setStatusHistory(data.statusHistory || []);
            setAttachments(data.attachments || []);
            setQuotations(data.quotations || []);
            setSelectedQuotationId(data.selectedQuotationId || null);

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
            setFormData(form);
            setInitialFormData(form);
            setSupplierName(data.supplierName || '');
            setSupplierPortalCode(data.supplierPortalCode || '');

            const [uData, cData, nData, dData, compData, plantData, ccData, usersData] = await Promise.all([
                api.lookups.getUnits(true),
                api.lookups.getCurrencies(true),
                api.lookups.getNeedLevels(true),
                api.lookups.getDepartments(true),
                api.lookups.getCompanies(true),
                api.lookups.getPlants(undefined, true),
                api.lookups.getCostCenters(true),
                api.users.list()
            ]);
            setUnits(uData);
            setCurrencies(cData);
            setNeedLevels(nData);
            setDepartments(dData);
            setCompanies(compData); 
            setPlants(plantData);
            setCostCenters(ccData);
            setUsers(usersData);

        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Falha ao carregar pedido.' });
        } finally {
            setLoading(false);
        }
    }, [id]);

    // Handle Scroll Focus from Search Params
    useEffect(() => {
        const focus = searchParams.get('focus');
        if (!loading && focus) {
            if (focus === 'items') {
                setSectionsOpen(prev => ({ ...prev, items: true }));
                setTimeout(() => {
                    itemsSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    setIsItemsHighlighted(true);
                    setTimeout(() => setIsItemsHighlighted(false), 3000);
                }, 500);
            }
        }
    }, [loading, searchParams]);

    const handleAttachmentRefresh = useCallback(() => {
        loadData();
    }, [loadData]);


    useEffect(() => {
        loadData();
    }, [loadData]);

    const today = new Date().toISOString().split('T')[0];

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
        setFormData(prev => ({ ...prev, [name]: value }));
        clearFieldError(name);
    };

    const handleSubmit = async (e?: React.FormEvent): Promise<boolean> => {
        if (e) e.preventDefault();
        if (!id) return false;

        // Manual Validation for Required Fields
        const newErrors: Record<string, string[]> = {};
        if (!formData.requestTypeId) newErrors['RequestTypeId'] = ['O Tipo de Pedido é obrigatório.'];
        if (!formData.buyerId) newErrors['BuyerId'] = ['O Comprador Atribuído é obrigatório.'];
        if (!formData.areaApproverId) newErrors['AreaApproverId'] = ['O Aprovador de Área é obrigatório.'];
        if (!formData.finalApproverId) newErrors['FinalApproverId'] = ['O Aprovador Final é obrigatório.'];

        // New required checks
        if (!formData.needLevelId) newErrors['NeedLevelId'] = ['O grau de necessidade é obrigatório.'];
        if (!formData.departmentId) newErrors['DepartmentId'] = ['O departamento é obrigatório.'];
        if (!formData.companyId) newErrors['CompanyId'] = ['A empresa é obrigatória.'];
        if (!formData.plantId) newErrors['PlantId'] = ['A planta é obrigatória.'];
        // Conditional Supplier Validation: ID 2 is PAYMENT
        const isPayment = Number(formData.requestTypeId) === 2 || requestTypeCode === 'PAYMENT';
        if (!isPayment && !formData.needByDateUtc) {
            newErrors['NeedByDateUtc'] = ['A data Necessário Até é obrigatória.'];
        } else if (formData.needByDateUtc && formData.needByDateUtc < today) {
            newErrors['NeedByDateUtc'] = ['A data Necessário Até não pode ser no passado.'];
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

        setSaving(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});

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
            needByDateUtc: formData.needByDateUtc ? new Date(formData.needByDateUtc).toISOString() : null,
            buyerId: formData.buyerId || null,
            areaApproverId: formData.areaApproverId || null,
            finalApproverId: formData.finalApproverId || null
        };

        try {
            await api.requests.updateDraft(id, payload);

            // Refetch to ensure frontend state (especially total) is perfectly in sync with backend logic
            const updated = await api.requests.get(id);
            setLineItems(updated.lineItems || []);
            setFormData(prev => ({
                ...prev,
                estimatedTotalAmount: updated.estimatedTotalAmount?.toString() || '0'
            }));

            // Only show success if this was a manual save (has e)
            if (e) {

                const isPayment = Number(formData.requestTypeId) === 2 || requestTypeCode === 'PAYMENT';
                const hasNoItems = lineItems.length === 0;

                if (isPayment && hasNoItems) {
                    setFeedback({
                        type: 'success',
                        message: 'Rascunho salvo. Adicione os itens para poder submeter o pedido.'
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
                        message: 'Rascunho salvo. Itens serão inseridos na tela "Gestão de Cotações".',
                    });
                } else {
                    setFeedback({ type: 'success', message: 'Rascunho salvo com sucesso.' });
                }
            }
            return true;
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                setFieldErrors(err.fieldErrors);
                setFeedback({ type: 'error', message: err.message || 'Existem campos preenchidos incorretamente.' });
                scrollToFirstError(err.fieldErrors);
                
                // Highlight specific sections if relevant errors found
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
                const hasProforma = attachments.some(a => a.attachmentTypeCode === 'PROFORMA');
                const hasSupplier = !!formData.supplierId;
                const itemsCount = lineItems.length;
                await completeQuotationAction(id, hasProforma, hasSupplier, itemsCount, approvalComment);
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
                await handleSubmit();
                setShowApprovalModal({ show: false, type: null });
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
            setStatusHistory(data.statusHistory || []);

        } catch (err: any) {
            setModalFeedback({ type: 'error', message: err.message || 'Não foi possível concluir a ação. Tente novamente.' });
        } finally {
            setApprovalProcessing(false);
        }
    };

    const handleSubmitRequest = async () => {
        if (!id) return;

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
        setFeedback({ type: 'info', message: isReworkStatus ? 'Reenviando pedido...' : 'Submetendo pedido...' });

        try {
            const result = await api.requests.submit(id!);

            const successMessage = result.message || (requestTypeCode === 'QUOTATION'
                ? 'Pedido enviado para cotação com sucesso.'
                : 'Pedido enviado para aprovação da área com sucesso.');

            // Redirect to list as per requirement
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
        borderRadius: 'var(--radius-sm)',
        border: '1px solid var(--color-border-heavy)',
        boxShadow: 'var(--shadow-brutal)'
    };

    const sectionTitleStyle: React.CSSProperties = {
        fontSize: '1rem',
        fontWeight: 900,
        textTransform: 'uppercase',
        letterSpacing: '0.05em',
        color: 'var(--color-text-main)',
        paddingBottom: '12px',
        borderBottom: '2px solid var(--color-border)',
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
        ...(getFieldErrors(fieldName) ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2', boxShadow: '3px 3px 0px #EF4444' } : {})
    });

    if (loading) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1000px', margin: '0 auto' }}>
                <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div>Carregando detalhes...</div>
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
            <span style={{
                marginLeft: '8px', padding: '2px 8px', backgroundColor: '#f0f7ff',
                color: '#0369a1', borderRadius: '4px', fontSize: '0.65rem', border: '1px solid #e0f2fe'
            }}>
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
                    onClick={() => navigate(`/requests${location.state?.fromList || ''}`)}
                    style={{
                        height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid var(--color-border-heavy)',
                        backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                        fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.75rem', color: 'var(--color-text-main)'
                    }}
                >
                    {status === 'DRAFT' ? <><X size={14} /> CANCELAR</> : <><ArrowLeft size={14} /> VOLTAR</>}
                </button>
            </>
        ),
        primaryActions: (
            <>
                {status === 'DRAFT' && (
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
                            height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid #EF4444',
                            backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.75rem'
                        }}
                    >
                        <X size={14} /> CANCELAR PEDIDO
                    </button>
                )}
                {(isDraftEditable || isQuotationPartiallyEditable) && (
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
            style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1000px', margin: '0 auto' }}
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


                {/* Section A: Dados Gerais do Pedido */}
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
                                    disabled={!canEditHeader || status !== 'DRAFT'}
                                >
                                    <option value={1}>COTAÇÃO (COM)</option>
                                    <option value={2}>PAGAMENTO (PAG)</option>
                                </select>
                                {renderFieldError('RequestTypeId')}
                            </label>

                            <label style={labelStyle}>
                                Fornecedor {Number(formData.requestTypeId) === 2 && <span style={{ color: 'red' }}>*</span>}
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
                                        ...(lineItems.length > 0 || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={!canEditHeader || lineItems.length > 0}
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
                                        ...(!formData.companyId || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={!canEditHeader || !formData.companyId}
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

                        <label style={labelStyle}>
                            Necessário até (Data limite) <span style={{ color: 'red' }}>*</span>
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
                        </label>
                    </div>
                </section>

                {/* Section B: Participantes do Fluxo */}
                <CollapsibleSection
                    title="Participantes do Fluxo"
                    count={3}
                    isOpen={sectionsOpen.participants}
                    onToggle={() => toggleSection('participants')}
                >
                    <div style={{ padding: '32px' }}>
                        <p style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', marginBottom: '24px' }}>
                            Nota: O Solicitante será preenchido automaticamente com o seu usuário logado atual.
                        </p>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '20px' }}>
                            <label style={labelStyle}>
                                Comprador Atribuído <span style={{ color: 'red' }}>*</span>
                                <select name="buyerId" value={formData.buyerId} onChange={handleChange} style={getInputStyle('BuyerId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {users.map(u => (
                                        <option key={u.id} value={u.id}>{u.fullName}</option>
                                    ))}
                                </select>
                                {renderFieldError('BuyerId')}
                            </label>

                            <label style={labelStyle}>
                                Aprovador de Área <span style={{ color: 'red' }}>*</span>
                                <select name="areaApproverId" value={formData.areaApproverId} onChange={handleChange} style={getInputStyle('AreaApproverId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {users.map(u => (
                                        <option key={u.id} value={u.id}>{u.fullName}</option>
                                    ))}
                                </select>
                                {renderFieldError('AreaApproverId')}
                            </label>

                            <label style={labelStyle}>
                                Aprovador Final <span style={{ color: 'red' }}>*</span>
                                <select name="finalApproverId" value={formData.finalApproverId} onChange={handleChange} style={getInputStyle('FinalApproverId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {users.map(u => (
                                        <option key={u.id} value={u.id}>{u.fullName}</option>
                                    ))}
                                </select>
                                {renderFieldError('FinalApproverId')}
                            </label>
                        </div>
                    </div>
                </CollapsibleSection>

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
                                    {!selectedQuotationId && <th>Prioridade</th>}
                                    <th>Descrição</th>
                                    <th style={{ textAlign: 'center' }}>Unid.</th>
                                    <th style={{ textAlign: 'right' }}>Qtd</th>
                                    {!selectedQuotationId && <th>Planta</th>}
                                    {!selectedQuotationId && <th>Centro Custo</th>}
                                    <th style={{ textAlign: 'right' }}>Preço Unit.</th>
                                    <th style={{ textAlign: 'right' }}>Total</th>
                                    {!selectedQuotationId && <th>Status</th>}
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
                                        <td colSpan={11} style={{ textAlign: 'center', padding: '24px', color: 'var(--color-text-muted)' }}>Nenhum item adicionado a este pedido.</td>
                                    </tr>
                                ) : (
                                    lineItems.map(item => (
                                        <tr key={item.id}>
                                            <td>{item.lineNumber}</td>
                                            <td>
                                                {/* Business priority badge */}
                                                {(() => {
                                                    const priorityMap: Record<string, { label: string; color: string; bg: string }> = {
                                                        HIGH: { label: 'Alta', color: '#dc2626', bg: '#fee2e2' },
                                                        MEDIUM: { label: 'Média', color: '#d97706', bg: '#fef3c7' },
                                                        LOW: { label: 'Baixa', color: '#16a34a', bg: '#dcfce7' }
                                                    };
                                                    const p = priorityMap[item.itemPriority] || priorityMap['MEDIUM'];

                                                    return (
                                                        <span style={{ display: 'inline-block', padding: '2px 8px', borderRadius: '999px', fontSize: '0.7rem', fontWeight: 700, color: p.color, backgroundColor: p.bg }}>
                                                            {p.label}
                                                        </span>
                                                    );
                                                })()}
                                            </td>
                                            <td>{item.description}</td>
                                            <td style={{ textAlign: 'center' }}>{item.unit || '---'}</td>
                                            <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                            <td>{item.plantName || '---'}</td>
                                            <td>
                                                {isAreaApprover && status === 'WAITING_AREA_APPROVAL' ? (
                                                    <select
                                                        value={item.costCenterId || ''}
                                                        onChange={async (e) => {
                                                            const ccId = e.target.value ? Number(e.target.value) : null;
                                                            try {
                                                                await api.lineItems.updateCostCenter(item.id, ccId);
                                                                setFeedback({ type: 'success', message: 'Centro de custo atualizado.' });
                                                                loadData();
                                                            } catch (err: any) {
                                                                setFeedback({ type: 'error', message: err.message });
                                                            }
                                                        }}
                                                        style={{ padding: '4px', borderRadius: '4px', border: '1px solid #4f46e5', fontSize: '0.75rem', fontWeight: 600 }}
                                                    >
                                                        <option value="">-- Selecione --</option>
                                                        {costCenters.map(cc => (
                                                            <option key={cc.id} value={cc.id}>{cc.code} - {cc.name}</option>
                                                        ))}
                                                    </select>
                                                ) : (
                                                    <span style={{ fontSize: '0.75rem' }}>{item.costCenterCode || '---'}</span>
                                                )}
                                            </td>
                                            <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                            <td style={{ textAlign: 'right', fontWeight: 600 }}>{formatCurrencyAO(item.totalAmount)}</td>
                                            <td>
                                                {/* Item status badge — read-only for requester */}
                                                {item.lineItemStatusName ? (
                                                    <span style={{
                                                        display: 'inline-block', padding: '2px 8px', borderRadius: '999px',
                                                        fontSize: '0.7rem', fontWeight: 700, whiteSpace: 'nowrap',
                                                        backgroundColor: item.lineItemStatusBadgeColor === 'blue' ? '#dbeafe' :
                                                            item.lineItemStatusBadgeColor === 'yellow' ? '#fef3c7' :
                                                                item.lineItemStatusBadgeColor === 'green' ? '#dcfce7' :
                                                                    item.lineItemStatusBadgeColor === 'red' ? '#fee2e2' :
                                                                        item.lineItemStatusBadgeColor === 'indigo' ? '#e0e7ff' :
                                                                            item.lineItemStatusBadgeColor === 'orange' ? '#ffedd5' :
                                                                                item.lineItemStatusBadgeColor === 'cyan' ? '#cffafe' :
                                                                                    '#f3f4f6',
                                                        color: item.lineItemStatusBadgeColor === 'blue' ? '#1d4ed8' :
                                                            item.lineItemStatusBadgeColor === 'yellow' ? '#92400e' :
                                                                item.lineItemStatusBadgeColor === 'green' ? '#15803d' :
                                                                item.lineItemStatusBadgeColor === 'red' ? '#dc2626' :
                                                                    item.lineItemStatusBadgeColor === 'indigo' ? '#4338ca' :
                                                                        item.lineItemStatusBadgeColor === 'orange' ? '#c2410c' :
                                                                            item.lineItemStatusBadgeColor === 'cyan' ? '#0e7490' :
                                                                                '#374151'
                                                    }}>
                                                        {item.lineItemStatusName}
                                                    </span>
                                                ) : (
                                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>—</span>
                                                )}
                                            </td>
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
                                currencies={currencies}
                                currencyId={Number(formData.currencyId)}
                                fieldErrors={fieldErrors}
                                clearFieldError={clearFieldError}
                                companyId={formData.companyId ? Number(formData.companyId) : null}
                                plants={(plants || []).filter(p => !formData.companyId || Number(p.companyId) === Number(formData.companyId))}
                            />
                        </motion.div>
                    )}
                </div>
            </CollapsibleSection>

            {/* Section: Cotações Salvas */}
            {(requestTypeCode === 'QUOTATION' || requestTypeCode === 'PAYMENT') && status !== 'CANCELLED' && status !== 'REJECTED' && (
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
                            transition: 'box-shadow 0.5s ease',
                            boxShadow: 'none'
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
                        requestId={id!}
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

        </motion.div >
    );
}
