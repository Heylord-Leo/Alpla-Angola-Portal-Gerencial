import React, { useState, useEffect, useRef } from 'react';
import { useNavigate, useLocation, useSearchParams } from 'react-router-dom';
import { Save, X, Paperclip, Trash2, AlertTriangle, FileText, RefreshCw, UploadCloud, CheckCircle2, UserPlus, AlertCircle, Edit2, Plus } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { LookupDto, IvaRate, Unit, CurrencyDto, OcrDraft, OcrDraftItem } from '../../types';
import { useOcrProcessor } from '../../hooks/useOcrProcessor';
import { motion, AnimatePresence } from 'framer-motion';
import { RequestActionHeader, BreadcrumbItem } from './components/RequestActionHeader';
import { scrollToFirstError } from '../../lib/validation';
import { DateInput } from '../../components/DateInput';
import { computeFileHash, formatDateTime } from '../../lib/utils';


export function RequestCreate() {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const location = useLocation() as { state: { fromList?: string } | null };
    
    // Copy Mode Detection
    const copyFromId = searchParams.get('copyFrom');
    const isCopyMode = !!copyFromId;

    const [loading, setLoading] = useState(false);
    const [isTemplateLoading, setIsTemplateLoading] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    const [requestTypes, setRequestTypes] = useState<any[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [allowedPlantCodes, setAllowedPlantCodes] = useState<string[]>([]);
    const [isScopeLoading, setIsScopeLoading] = useState(true);
    const [attachments, setAttachments] = useState<File[]>([]);
    
    // Payment OCR States
    const [ivaRates, setIvaRates] = useState<IvaRate[]>([]);
    const [units, setUnits] = useState<Unit[]>([]);
    const [currencies, setCurrencies] = useState<CurrencyDto[]>([]);
    const [isOcrLoading, setIsOcrLoading] = useState(false);
    const [duplicateWarning, setDuplicateWarning] = useState<{ isOpen: boolean; requestNumber: string; uploadCallback: () => void; uploadedBy?: string; createdAtUtc?: string } | null>(null);
    const [paymentDraft, setPaymentDraft] = useState<OcrDraft | null>(null);
    const [ocrFile, setOcrFile] = useState<File | null>(null);
    const [isManualOcr, setIsManualOcr] = useState(false);
    const [quickSupplierModal, setQuickSupplierModal] = useState<{ show: boolean; initialName: string; initialTaxId: string }>({ show: false, initialName: '', initialTaxId: '' });
    const fileInputRef = useRef<HTMLInputElement>(null);
    const manualFileInputRef = useRef<HTMLInputElement>(null);

    const { mapOcrResultToDraft, calculateItemTotal, calculateDraftTotal } = useOcrProcessor(ivaRates, units, currencies, companies);

    const [formData, setFormData] = useState({
        title: '',
        description: '',
        requestTypeId: '', 
        needByDateUtc: '',
        needLevelId: '',
        estimatedTotalAmount: '',
        currencyId: 1,    // Internal implementation default
        departmentId: '',
        companyId: '',
        plantId: '',
        buyerId: '',
        areaApproverId: '',
        finalApproverId: ''
    });

    const initialFormDataRef = useRef(formData);

    // Navigation Away Protection
    useEffect(() => {
        const handleBeforeUnload = (e: BeforeUnloadEvent) => {
            const isDirty = JSON.stringify(formData) !== JSON.stringify(initialFormDataRef.current);
            if (isDirty && !loading) {
                e.preventDefault();
                e.returnValue = '';
            }
        };

        window.addEventListener('beforeunload', handleBeforeUnload);
        return () => window.removeEventListener('beforeunload', handleBeforeUnload);
    }, [formData, loading]);

    // Load Lookups
    useEffect(() => {
        async function loadLookups() {
            try {
                setIsScopeLoading(true);
                const [levelsData, departmentsData, companiesData, plantsData, rtData, meData, ivaData, unitsData, currenciesData] = await Promise.all([
                    api.lookups.getNeedLevels(true),
                    api.lookups.getDepartments(true),
                    api.lookups.getCompanies(true),
                    api.lookups.getPlants(undefined, true),
                    api.lookups.getRequestTypes(true),
                    api.users.me(),
                    api.lookups.getIvaRates(true),
                    api.lookups.getUnits(true),
                    api.lookups.getCurrencies(true)
                ]);
                setNeedLevels(levelsData);
                setDepartments(departmentsData);
                setCompanies(companiesData);
                setPlants(plantsData);
                setRequestTypes(rtData);
                setAllowedPlantCodes(meData.plants || []);
                setIvaRates(ivaData);
                setUnits(unitsData);
                setCurrencies(currenciesData);
            } catch (err) {
                console.error('Falha ao carregar dados auxiliares', err);
            } finally {
                setIsScopeLoading(false);
            }
        }
        loadLookups();
    }, []);

    // Handle Copy Mode Data Fetching
    useEffect(() => {
        if (!isCopyMode || isScopeLoading || plants.length === 0) return;

        async function loadTemplate() {
            try {
                setIsTemplateLoading(true);
                const data = await api.requests.getTemplate(copyFromId!);
                
                setFormData(prev => {
                    const next = {
                        ...prev,
                        title: `Cópia ${data.sourceRequestNumber} ${data.title}`,
                        description: data.description || '',
                        requestTypeId: data.requestTypeId ? String(data.requestTypeId) : '',
                        needLevelId: data.needLevelId ? String(data.needLevelId) : '',
                        departmentId: data.departmentId ? String(data.departmentId) : '',
                        companyId: data.companyId ? String(data.companyId) : '',
                        plantId: data.plantId ? String(data.plantId) : '',
                        buyerId: data.buyerId || '',
                        areaApproverId: data.areaApproverId || '',
                        finalApproverId: data.finalApproverId || '',
                    };
                    initialFormDataRef.current = next;
                    return next;
                });
            } catch (err: any) {
                setFeedback({ type: 'error', message: err.message || 'Falha ao carregar pedido para cópia.' });
            } finally {
                setIsTemplateLoading(false);
            }
        }

        loadTemplate();
    }, [isCopyMode, copyFromId, isScopeLoading, plants.length]);

    // Derived filtered data based on user scope
    const filteredPlants = plants.filter(p => allowedPlantCodes.includes(p.code));
    const filteredCompanies = companies.filter(c => 
        filteredPlants.some(p => p.companyId === c.id)
    );



    // Auto-selection of Company/Plant based on restricted scope
    useEffect(() => {
        if (isScopeLoading || isTemplateLoading || plants.length === 0 || companies.length === 0 || isCopyMode) return;

        setFormData(prev => {
            const next = { ...prev };
            
            if (filteredPlants.length === 1) {
                const soloPlant = filteredPlants[0];
                next.plantId = String(soloPlant.id);
                next.companyId = String(soloPlant.companyId);
            } 
            else if (filteredCompanies.length === 1 && !next.companyId) {
                next.companyId = String(filteredCompanies[0].id);
            }

            return next;
        });
    }, [isScopeLoading, isTemplateLoading, plants.length, companies.length, filteredPlants.length, filteredCompanies.length, isCopyMode]);

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

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files) {
            const newFiles = Array.from(e.target.files);
            setAttachments(prev => [...prev, ...newFiles]);
        }
    };

    const removeFile = (index: number) => {
        setAttachments(prev => prev.filter((_, i) => i !== index));
    };

    const _startManualOcr = (file: File) => {
        setOcrFile(file);
        setIsManualOcr(true);
        setFeedback({ type: 'error', message: null });
        setPaymentDraft({
            supplierId: null,
            supplierNameSnapshot: '',
            documentNumber: '',
            documentDate: '',
            currency: 'AOA', 
            discountAmount: 0,
            totalAmount: 0,
            items: []
        });
        if (manualFileInputRef.current) {
            manualFileInputRef.current.value = '';
        }
    };

    const handleManualOcrUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        setIsOcrLoading(true);
        try {
            const hash = await computeFileHash(file);
            const dupCheck = await api.attachments.checkDuplicate(hash);
            if (dupCheck.isDuplicate) {
                setDuplicateWarning({
                    isOpen: true,
                    requestNumber: dupCheck.requestNumber || 'Desconhecido',
                    uploadedBy: dupCheck.uploadedBy,
                    createdAtUtc: dupCheck.createdAtUtc,
                    uploadCallback: () => {
                        setDuplicateWarning(null);
                        _startManualOcr(file);
                    }
                });
                return;
            }
        } catch (err) {
            console.error("Duplicate check failed", err);
        } finally {
            setIsOcrLoading(false);
        }

        _startManualOcr(file);
    };

    const _startOcrExtract = async (file: File) => {
        setOcrFile(file);
        setIsManualOcr(false);
        setIsOcrLoading(true);
        setFeedback({ type: 'error', message: null });

        try {
            const result = await api.requests.directOcrExtract(file);
            const draft = await mapOcrResultToDraft(result);
            setPaymentDraft(draft);
            
            if (draft.companyId) {
                setFormData(prev => {
                    const next = { ...prev, companyId: String(draft.companyId) };
                    // We must also deduce plantId if possible from the matched company 
                    // (similar to what handleChange does for manual select)
                    next.plantId = '';
                    const plantsForCompany = filteredPlants.filter(p => p.companyId === draft.companyId);
                    if (plantsForCompany.length === 1) {
                        next.plantId = String(plantsForCompany[0].id);
                    }
                    return next;
                });
            }
            
            const extractedDate = draft.dueDate || draft.documentDate;
            if (extractedDate) {
                try {
                    const parsed = new Date(extractedDate);
                    if (!isNaN(parsed.getTime())) {
                        setFormData(prev => ({ ...prev, needByDateUtc: parsed.toISOString().split('T')[0] }));
                    }
                } catch (pe) {
                    // Ignore parsing errors
                }
            }
        } catch (err: any) {
            console.error("OCR Extraction failed", err);
            setFeedback({ type: 'error', message: err.message || 'Falha na extração OCR do documento.' });
        } finally {
            setIsOcrLoading(false);
            if (fileInputRef.current) {
                fileInputRef.current.value = '';
            }
        }
    };

    const handleOcrUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        
        setIsOcrLoading(true);
        try {
            const hash = await computeFileHash(file);
            const dupCheck = await api.attachments.checkDuplicate(hash);
            if (dupCheck.isDuplicate) {
                setDuplicateWarning({
                    isOpen: true,
                    requestNumber: dupCheck.requestNumber || 'Desconhecido',
                    uploadedBy: dupCheck.uploadedBy,
                    createdAtUtc: dupCheck.createdAtUtc,
                    uploadCallback: () => {
                        setDuplicateWarning(null);
                        _startOcrExtract(file);
                    }
                });
                return;
            }
        } catch (err) {
            console.error("Duplicate check failed", err);
        } finally {
            if (!duplicateWarning) { // Only set false if we are not presenting a modal
                setIsOcrLoading(false);
            }
        }

        _startOcrExtract(file);
    };

    const handleUpdateOcrDraft = (field: keyof OcrDraft, value: any) => {
        setPaymentDraft(prev => {
            if (!prev) return null;
            const next = { ...prev, [field]: value };
            if (field === 'discountAmount' || field === 'items') {
                next.totalAmount = calculateDraftTotal(next);
            }
            return next;
        });
    };

    const handleUpdateOcrItem = (index: number, field: keyof OcrDraftItem, value: any) => {
        setPaymentDraft(prev => {
            if (!prev) return null;
            const nextItems = [...prev.items];
            nextItems[index] = { ...nextItems[index], [field]: value };
            
            if (field === 'quantity' || field === 'unitPrice' || field === 'ivaRateId' || field === 'discountAmount') {
                nextItems[index].totalPrice = calculateItemTotal(nextItems[index]);
            }
            
            const next = { ...prev, items: nextItems };
            next.totalAmount = calculateDraftTotal(next);
            return next;
        });
    };

    const handleAddOcrItem = () => {
        setPaymentDraft(prev => {
            if (!prev) return null;
            const nextItems = [...prev.items, {
                lineNumber: prev.items.length + 1,
                description: '',
                quantity: 1,
                unitId: null,
                unit: '',
                unitPrice: 0,
                discountAmount: 0,
                ivaRateId: null,
                totalPrice: 0
            }];
            return { ...prev, items: nextItems };
        });
    };

    const handleRemoveOcrItem = (index: number) => {
        setPaymentDraft(prev => {
            if (!prev) return null;
            const nextItems = prev.items.filter((_, i) => i !== index);
            const next = { ...prev, items: nextItems };
            next.totalAmount = calculateDraftTotal(next);
            return next;
        });
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
        const { name, value } = e.target;
        setFormData(prev => {
            const next = { ...prev, [name]: value };
            if (name === 'companyId') {
                next.plantId = '';
                if (value) {
                    const plantsForCompany = filteredPlants.filter(p => p.companyId === Number(value));
                    if (plantsForCompany.length === 1) {
                        next.plantId = String(plantsForCompany[0].id);
                    }
                }
            }
            
            if (name === 'plantId' && value) {
                const selectedPlant = plants.find(p => p.id === Number(value));
                if (selectedPlant) {
                    next.companyId = String(selectedPlant.companyId);
                }
            }

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

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        const newErrors: Record<string, string[]> = {};
        if (!formData.requestTypeId) newErrors['RequestTypeId'] = ['O Tipo de Pedido é obrigatório.'];
        if (!formData.needLevelId) newErrors['NeedLevelId'] = ['O grau de necessidade é obrigatório.'];
        if (!formData.departmentId) newErrors['DepartmentId'] = ['O departamento é obrigatório.'];
        if (!formData.companyId) newErrors['CompanyId'] = ['A empresa é obrigatória.'];
        if (!formData.plantId) newErrors['PlantId'] = ['A planta é obrigatória.'];

        if (Number(formData.requestTypeId) === 1 || Number(formData.requestTypeId) === 2) {
            const isPayment = Number(formData.requestTypeId) === 2;
            if (!formData.needByDateUtc) {
                newErrors['NeedByDateUtc'] = [isPayment ? 'A data de vencimento é obrigatória para pedidos de Pagamento.' : 'A data Necessário Até é obrigatória para pedidos de Cotação.'];
            }
        }

        if (Object.keys(newErrors).length > 0) {
            setFieldErrors(newErrors);
            setFeedback({ type: 'error', message: 'Preencha todos os campos obrigatórios antes de continuar.' });
            scrollToFirstError(newErrors);
            return;
        }

        setLoading(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});

        const safeCurrencyId = Number(formData.currencyId) || 1;
        const safePlantId = Number(formData.plantId) || 0;

        const payload = {
            title: formData.title,
            description: Number(formData.requestTypeId) === 2 && paymentDraft && paymentDraft.discountAmount > 0
                ? `${formData.description}\n\n[Desconto OCR: ${paymentDraft.discountAmount.toLocaleString('pt-PT', { minimumFractionDigits: 2 })} ${paymentDraft.currency}]`
                : formData.description,
            requestTypeId: Number(formData.requestTypeId),
            needLevelId: Number(formData.needLevelId),
            currencyId: paymentDraft && Number(formData.requestTypeId) === 2 
                ? (currencies.find(c => c.code === paymentDraft.currency)?.id || safeCurrencyId)
                : safeCurrencyId,
            estimatedTotalAmount: paymentDraft && Number(formData.requestTypeId) === 2 
                ? paymentDraft.totalAmount 
                : (Number(formData.estimatedTotalAmount) || 0),
            departmentId: Number(formData.departmentId),
            companyId: Number(formData.companyId),
            plantId: safePlantId,
            capexOpexClassificationId: (formData as any).capexOpexClassificationId ? Number((formData as any).capexOpexClassificationId) : null,
            supplierId: Number(formData.requestTypeId) === 2 && paymentDraft?.supplierId 
                ? paymentDraft.supplierId 
                : ((formData as any).supplierId ? Number((formData as any).supplierId) : null),
            needByDateUtc: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) 
                ? new Date(formData.needByDateUtc).toISOString() 
                : null,
            buyerId: formData.buyerId || null,
            areaApproverId: formData.areaApproverId || null,
            finalApproverId: formData.finalApproverId || null,
            lineItems: Number(formData.requestTypeId) === 2 && paymentDraft ? paymentDraft.items.map((item, index) => ({
                lineNumber: index + 1,
                description: item.description,
                quantity: item.quantity,
                unitId: item.unitId,
                unit: item.unit,
                unitPrice: item.unitPrice,
                ivaRateId: item.ivaRateId,
                totalAmount: item.totalPrice,
                dueDate: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) 
                    ? new Date(formData.needByDateUtc).toISOString() 
                    : null,
                currencyId: currencies.find(c => c.code === paymentDraft.currency)?.id || safeCurrencyId,
                plantId: safePlantId,
                costCenterId: null,
                itemPriority: 'MEDIUM'
            })) : []
        };

        try {
            const result = await api.requests.create(payload);

            // 1. Upload generic supporting documents
            if (attachments.length > 0) {
                await api.attachments.upload(result.id, attachments, 'SUPPORTING');
            }

            // 2. Upload OCR file correctly classified (Proforma for Payment requests)
            if (ocrFile) {
                const targetType = Number(formData.requestTypeId) === 2 ? 'PROFORMA' : 'SUPPORTING';
                await api.attachments.upload(result.id, [ocrFile], targetType);
            }

            const isQuotation = Number(formData.requestTypeId) === 1;
            const fallbackSuccessMessage = isQuotation ? 'Pedido de Cotação criado com sucesso.' : 'Rascunho salvo com sucesso.';

            if (isQuotation) {
                const quotationSuccessMessage = 'Pedido de Cotação criado. Os itens serão inseridos na tela "Gestão de Cotações".';
                navigate(`/requests${location.state?.fromList || ''}`, {
                    replace: true,
                    state: { successMessage: quotationSuccessMessage }
                });
            } else {
                const isPayment = Number(formData.requestTypeId) === 2;
                const successMsg = isPayment ? 'Pedido de Pagamento gerado. Preencha os restantes dados e anexe a fatura/nota.' : fallbackSuccessMessage;

                navigate(`/requests/${result.id}/edit`, {
                    replace: true,
                    state: {
                        successMessage: successMsg,
                        fromList: location.state?.fromList
                    }
                });

            }
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                setFieldErrors(err.fieldErrors);
                setFeedback({ type: 'error', message: err.message || 'Existem campos preenchidos incorretamente.' });
                scrollToFirstError(err.fieldErrors);
            } else {
                setFeedback({ type: 'error', message: err.message || 'Erro ao criar o rascunho.' });
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }
        } finally {
            setLoading(false);
        }
    };

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        borderRadius: 'var(--radius-sm)',
        border: '1px solid var(--color-border-heavy)',
        boxShadow: 'var(--shadow-brutal)',
        fontSize: '0.875rem',
        fontFamily: 'var(--font-family-body)',
        color: 'var(--color-text-main)',
        backgroundColor: 'var(--color-bg-page)',
        marginTop: '8px',
        transition: 'all 0.2s ease',
        outline: 'none'
    };

    const labelStyle = {
        display: 'block',
        fontSize: '0.75rem',
        fontWeight: 600,
        textTransform: 'uppercase' as const,
        letterSpacing: '0.05em',
        color: 'var(--color-text-main)',
        marginBottom: '24px',
        position: 'relative' as const
    };

    const sectionTitleStyle = {
        fontSize: '1.1rem',
        fontWeight: 700,
        color: 'var(--color-primary)',
        borderBottom: '2px solid var(--color-border)',
        paddingBottom: '8px',
        marginBottom: '24px',
        marginTop: '16px',
        textTransform: 'uppercase' as const,
        letterSpacing: '0.05em'
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

    const headerProps = {
        breadcrumbs: [
            { label: 'Dashboard', to: '/' },
            { label: 'Pedidos', to: `/requests${location.state?.fromList || ''}` },
            { label: isCopyMode ? 'Cópia de Pedido' : 'Novo Rascunho' }
        ] as BreadcrumbItem[],
        title: isCopyMode ? 'Cópia de Pedido' : 'Novo Rascunho',
        secondaryActions: (
            <button
                type="button"
                onClick={() => navigate(`/requests${location.state?.fromList || ''}`)}
                style={{
                    height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid var(--color-border-heavy)',
                    backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                    fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.75rem', color: 'var(--color-text-main)'
                }}
            >
                <X size={14} /> {isCopyMode ? 'DESCARTAR CÓPIA' : 'CANCELAR'}
            </button>
        ),
        primaryActions: (
            <button
                onClick={handleSubmit}
                disabled={loading || isTemplateLoading || isOcrLoading}
                className="btn-primary"
                style={{ height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', borderRadius: 'var(--radius-sm)' }}
            >
                <Save size={14} /> 
                {loading ? 'GERANDO...' : (
                    Number(formData.requestTypeId) === 1 ? 'CRIAR PEDIDO' : 
                    Number(formData.requestTypeId) === 2 ? 'GERAR PEDIDO' : 
                    isCopyMode ? 'CRIAR RASCUNHO' : 'CRIAR RASCUNHO'
                )}
            </button>
        ),
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
            <RequestActionHeader {...headerProps} />

            {!isScopeLoading && allowedPlantCodes.length === 0 && (
                <div style={{
                    backgroundColor: '#FEF2F2', border: '2px solid #EF4444', padding: '24px', borderRadius: 'var(--radius-sm)',
                    boxShadow: 'var(--shadow-brutal)', display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'center', textAlign: 'center'
                }}>
                    <div style={{ color: '#EF4444', fontWeight: 800, fontSize: '1.25rem' }}>ACESSO RESTRITO</div>
                    <p style={{ color: 'var(--color-text-main)', fontSize: '0.875rem', maxWidth: '500px' }}>
                        O seu utilizador não possui nenhuma <strong>Planta</strong> atribuída ao seu âmbito de acesso. 
                        Por favor, contacte o Administrador do Sistema para configurar o seu perfil antes de tentar criar novos pedidos.
                    </p>
                    <button 
                        onClick={() => navigate('/')}
                        style={{
                            marginTop: '8px', padding: '8px 16px', backgroundColor: 'var(--color-text-main)', color: 'white',
                            border: 'none', borderRadius: 'var(--radius-sm)', fontWeight: 700, cursor: 'pointer'
                        }}
                    >
                        VOLTAR AO DASHBOARD
                    </button>
                </div>
            )}

            <form 
                onSubmit={handleSubmit} 
                style={{
                    display: allowedPlantCodes.length === 0 ? 'none' : 'flex',
                    flexDirection: 'column', gap: '32px', opacity: isScopeLoading ? 0.5 : 1, pointerEvents: isScopeLoading ? 'none' : 'auto'
                }}
            >
                <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-border-heavy)' }}>
                    <h2 style={sectionTitleStyle}>Dados Gerais do Pedido</h2>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                        <label style={labelStyle}>
                            Título do Pedido <span style={{ color: 'red' }}>*</span>
                            <input
                                required type="text" name="title" value={formData.title} onChange={handleChange}
                                placeholder="Ex: Aquisição de Laptops para TI" style={getInputStyle('Title')}
                            />
                            {renderFieldError('Title')}
                        </label>

                        <label style={labelStyle}>
                            Descrição ou Justificativa <span style={{ color: 'red' }}>*</span>
                            <textarea
                                required name="description" value={formData.description} onChange={handleChange} rows={4}
                                placeholder="Explique o motivo e os detalhes primários..." style={{ ...getInputStyle('Description'), resize: 'vertical' }}
                            />
                            {isCopyMode && (
                                <motion.div
                                    initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }}
                                    style={{
                                        marginTop: '12px', padding: '12px 16px', backgroundColor: '#FFFBEB',
                                        border: '2px solid #F59E0B', borderRadius: 'var(--radius-sm)', display: 'flex', alignItems: 'flex-start', gap: '12px'
                                    }}
                                >
                                    <AlertTriangle size={18} style={{ color: '#D97706', flexShrink: 0, marginTop: '2px' }} />
                                    <div style={{ fontSize: '0.8rem', color: '#92400E', fontWeight: 500, lineHeight: '1.4' }}>
                                        <strong>Atenção:</strong> Esta descrição foi copiada do pedido original. 
                                        Revise o conteúdo antes de submeter para confirmar que ele ainda corresponde à necessidade atual.
                                    </div>
                                </motion.div>
                            )}
                            {renderFieldError('Description')}
                        </label>

                        <div style={{ marginTop: '-8px', marginBottom: '8px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
                                <Paperclip size={16} style={{ color: 'var(--color-primary)' }} />
                                <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '0.025em' }}>
                                    Documentos de Apoio
                                </span>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 400 }}>(Opcional)</span>
                            </div>

                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'flex-start' }}>
                                <div style={{ 
                                    border: '1px solid var(--color-border-heavy)', padding: '8px 16px', textAlign: 'left', borderRadius: 'var(--radius-sm)',
                                    backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', position: 'relative', transition: 'all 0.2s ease',
                                    display: 'inline-flex', alignItems: 'center', gap: '8px', boxShadow: 'var(--shadow-sm)'
                                }}>
                                    <input 
                                        type="file" multiple onChange={handleFileChange}
                                        style={{ position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', opacity: 0, cursor: 'pointer' }}
                                    />
                                    <UploadCloud size={16} style={{ color: 'var(--color-text-main)' }} />
                                    <div style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-text-main)' }}>ADICIONAR DOCUMENTO</div>
                                </div>

                                {attachments.length > 0 && (
                                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: '8px', width: '100%' }}>
                                        {attachments.map((file, idx) => (
                                            <div key={idx} style={{ 
                                                display: 'flex', justifyContent: 'space-between', alignItems: 'center', 
                                                padding: '8px 12px', backgroundColor: 'white', border: '1px solid var(--color-border)', 
                                                borderRadius: 'var(--radius-sm)', boxShadow: '1px 1px 0px var(--color-border)'
                                            }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                                                    <Paperclip size={14} style={{ flexShrink: 0 }} />
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                        {file.name}
                                                    </span>
                                                </div>
                                                <button type="button" onClick={() => removeFile(idx)} style={{ color: '#EF4444', background: 'none', border: 'none', cursor: 'pointer', padding: '4px', flexShrink: 0 }}>
                                                    <Trash2 size={14} />
                                                </button>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>

                         <label style={labelStyle}>
                             Tipo de Pedido <span style={{ color: 'red' }}>*</span>
                             <select name="requestTypeId" value={formData.requestTypeId} onChange={handleChange} style={getInputStyle('RequestTypeId')}>
                                 <option value="">-- Selecione --</option>
                                 {requestTypes.filter(rt => rt.isActive).map(rt => (
                                     <option key={rt.id} value={rt.id}>{rt.name}</option>
                                 ))}
                             </select>
                             {renderFieldError('RequestTypeId')}
                         </label>

                         <AnimatePresence>
                             {Number(formData.requestTypeId) === 2 && (
                                 <motion.div
                                     initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                                     style={{ position: 'relative' }}
                                 >
                                     <div style={{ 
                                         marginBottom: '32px', padding: '24px', backgroundColor: '#fff', 
                                         border: '2px solid var(--color-primary)', borderRadius: 'var(--radius-sm)', boxShadow: 'var(--shadow-brutal-sm)',
                                         position: 'relative'
                                     }}>
                                         <AnimatePresence>
                                         {isOcrLoading && (
                                             <motion.div
                                                 key="ocr-loading-overlay"
                                                 initial={{ opacity: 0 }} 
                                                 animate={{ opacity: 1 }}
                                                 exit={{ opacity: 0 }}
                                                 transition={{ duration: 0.2 }}
                                                 style={{
                                                     position: 'absolute', top: 0, left: 0, right: 0, bottom: 0,
                                                     backgroundColor: 'rgba(255, 255, 255, 0.95)', zIndex: 20,
                                                     display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                                                     borderRadius: 'var(--radius-sm)', backdropFilter: 'blur(2px)'
                                                 }}
                                             >
                                                 <motion.div
                                                     animate={{ rotate: [0, 360] }}
                                                     transition={{ repeat: Infinity, ease: "linear", duration: 1.5 }}
                                                     style={{ marginBottom: '16px', display: 'flex' }}
                                                 >
                                                     <RefreshCw size={40} style={{ color: 'var(--color-primary)' }} />
                                                 </motion.div>
                                                 
                                                 <div style={{ fontWeight: 800, fontSize: '1rem', color: 'var(--color-primary)', letterSpacing: '0.05em' }}>
                                                     PROCESSANDO OCR...
                                                 </div>
                                                 
                                                 <div style={{ width: '140px', height: '4px', backgroundColor: 'var(--color-border)', borderRadius: '2px', overflow: 'hidden', margin: '16px 0' }}>
                                                     <motion.div
                                                         animate={{ x: ['-100%', '200%'] }}
                                                         transition={{ repeat: Infinity, ease: 'easeInOut', duration: 1.5 }}
                                                         style={{ width: '50%', height: '100%', backgroundColor: 'var(--color-primary)' }}
                                                     />
                                                 </div>

                                                 <div style={{ fontSize: '0.8rem', color: 'var(--color-text-main)', display: 'flex', gap: '2px' }}>
                                                     <span>Analisando documento, aguarde</span>
                                                     <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.5, 1] }}>.</motion.span>
                                                     <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.75, 1], delay: 0.2 }}>.</motion.span>
                                                     <motion.span animate={{ opacity: [0, 1, 0] }} transition={{ repeat: Infinity, duration: 1.5, times: [0, 0.5, 1], delay: 0.4 }}>.</motion.span>
                                                 </div>
                                             </motion.div>
                                         )}
                                         </AnimatePresence>

                                         <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '16px' }}>
                                             <div style={{ 
                                                 width: '40px', height: '40px', borderRadius: '50%', backgroundColor: 'var(--color-primary)', 
                                                 display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'white' 
                                             }}>
                                                 <FileText size={20} />
                                             </div>
                                             <div>
                                                 <h3 style={{ fontSize: '0.875rem', fontWeight: 800, textTransform: 'uppercase', margin: 0 }}>Input de Documento & Faturamento</h3>
                                                 <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', margin: '2px 0 0 0' }}>Anexe a fatura e insira os dados (OCR/Manual)</p>
                                             </div>
                                         </div>

                                         {!paymentDraft ? (
                                             <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
                                                 <label style={{ 
                                                     display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '12px', 
                                                     padding: '32px', border: '1px solid #3b82f6', borderRadius: 'var(--radius-md)',
                                                     cursor: isOcrLoading ? 'wait' : 'pointer', backgroundColor: '#eff6ff', transition: 'all 0.2s ease',
                                                     textAlign: 'center'
                                                 }}>
                                                     <input type="file" ref={fileInputRef} onChange={handleOcrUpload} disabled={isOcrLoading} style={{ display: 'none' }} />
                                                     {isOcrLoading ? (
                                                         <>
                                                             <div style={{ opacity: 0.3 }}>
                                                                 <UploadCloud size={32} style={{ color: '#2563eb' }} />
                                                             </div>
                                                             <span style={{ fontSize: '0.9rem', fontWeight: 800, color: '#1e40af' }}>PROCESSANDO...</span>
                                                         </>
                                                     ) : (
                                                         <>
                                                             <UploadCloud size={32} style={{ color: '#2563eb' }} />
                                                             <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '4px' }}>
                                                                <span style={{ fontSize: '0.9rem', fontWeight: 900, color: '#1e3a8a', letterSpacing: '0.025em', textTransform: 'uppercase' }}>IMPORTAR DOCUMENTO</span>
                                                                <span style={{ fontSize: '0.75rem', color: '#64748b' }}>Extrair dados de fatura PDF/Imagem usando OCR</span>
                                                             </div>
                                                         </>
                                                     )}
                                                 </label>

                                                 <label style={{ 
                                                     display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '12px', 
                                                     padding: '32px', border: '1px solid #d946ef', borderRadius: 'var(--radius-md)',
                                                     cursor: isOcrLoading ? 'not-allowed' : 'pointer', backgroundColor: '#fdf4ff', transition: 'all 0.2s ease',
                                                     opacity: isOcrLoading ? 0.5 : 1, textAlign: 'center'
                                                 }}>
                                                     <input type="file" ref={manualFileInputRef} onChange={handleManualOcrUpload} disabled={isOcrLoading} style={{ display: 'none' }} />
                                                     <Edit2 size={32} style={{ color: '#c026d3' }} />
                                                     <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '4px' }}>
                                                        <span style={{ fontSize: '0.9rem', fontWeight: 900, color: '#701a75', letterSpacing: '0.025em', textTransform: 'uppercase' }}>INSERIR MANUALMENTE</span>
                                                        <span style={{ fontSize: '0.75rem', color: '#64748b' }}>Preencher dados da fatura manualmente do zero e anexar</span>
                                                     </div>
                                                 </label>
                                             </div>
                                         ) : (
                                             <div 
                                                 id="ocr-success-container"
                                                 data-testid="ocr-success-container"
                                                 style={{ display: 'block' }}
                                             >
                                                 {isManualOcr ? (
                                                     <div style={{ 
                                                         display: 'flex', justifyContent: 'space-between', alignItems: 'center', 
                                                         padding: '12px 16px', backgroundColor: '#FDF4FF', border: '1px solid #F0ABFC', 
                                                         borderRadius: 'var(--radius-sm)', marginBottom: '16px' 
                                                     }}>
                                                         <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#86198F' }}>
                                                             <Edit2 size={16} />
                                                             <span style={{ fontSize: '0.8rem', fontWeight: 700 }}>INSCRIÇÃO MANUAL DA FATURA</span>
                                                             <span style={{ fontSize: '0.75rem', opacity: 0.8 }}>({ocrFile?.name})</span>
                                                         </div>
                                                         <button 
                                                            type="button"
                                                            onClick={() => { setPaymentDraft(null); setOcrFile(null); setIsManualOcr(false); }}
                                                            style={{ fontSize: '0.7rem', fontWeight: 800, color: '#86198F', background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline' }}
                                                         >
                                                             TROCAR ARQUIVO
                                                         </button>
                                                     </div>
                                                 ) : (
                                                     <div style={{ 
                                                         display: 'flex', justifyContent: 'space-between', alignItems: 'center', 
                                                         padding: '12px 16px', backgroundColor: '#F0FDF4', border: '1px solid #BBF7D0', 
                                                         borderRadius: 'var(--radius-sm)', marginBottom: '16px' 
                                                     }}>
                                                         <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#166534' }}>
                                                             <CheckCircle2 size={16} />
                                                             <span style={{ fontSize: '0.8rem', fontWeight: 700 }}>DADOS EXTRAÍDOS COM SUCESSO</span>
                                                             <span style={{ fontSize: '0.75rem', opacity: 0.8 }}>({ocrFile?.name})</span>
                                                         </div>
                                                         <button 
                                                            type="button"
                                                            onClick={() => { setPaymentDraft(null); setOcrFile(null); setIsManualOcr(false); }}
                                                            style={{ fontSize: '0.7rem', fontWeight: 800, color: '#166534', background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline' }}
                                                         >
                                                             TROCAR ARQUIVO
                                                         </button>
                                                     </div>
                                                 )}

                                                 <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px', marginBottom: '24px' }}>
                                                      <label style={{ ...labelStyle, marginBottom: 0, gridColumn: '1 / -1' }}>
                                                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                              <span>Fornecedor</span>
                                                              <button
                                                                  type="button"
                                                                  onClick={() => setQuickSupplierModal({
                                                                      show: true,
                                                                      initialName: paymentDraft.supplierNameSnapshot || '',
                                                                      initialTaxId: paymentDraft.supplierTaxId || ''
                                                                  })}
                                                                  style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', padding: 0, textDecoration: 'underline', display: 'flex', alignItems: 'center', gap: '4px' }}
                                                              >
                                                                  <UserPlus size={12} />
                                                                  + NOVO FORNECEDOR
                                                              </button>
                                                          </div>
                                                          <SupplierAutocomplete
                                                              initialName={paymentDraft.supplierNameSnapshot || ''}
                                                              initialPortalCode={paymentDraft.supplierPortalCode || ''}
                                                              isUnresolved={!paymentDraft.supplierId && !!paymentDraft.supplierNameSnapshot}
                                                              onChange={(id, name, portalCode) => {
                                                                  handleUpdateOcrDraft('supplierId', id);
                                                                  handleUpdateOcrDraft('supplierNameSnapshot', name);
                                                                  handleUpdateOcrDraft('supplierPortalCode', portalCode || '');
                                                                  clearFieldError('SupplierId');
                                                              }}
                                                              hasError={!paymentDraft.supplierId && !!paymentDraft.supplierNameSnapshot}
                                                              className="mt-1"
                                                          />
                                                          {!paymentDraft.supplierId && paymentDraft.supplierNameSnapshot && (
                                                              <motion.div 
                                                                  initial={{ opacity: 0, height: 0 }} 
                                                                  animate={{ opacity: 1, height: 'auto' }}
                                                                  style={{ marginTop: '8px', padding: '12px', backgroundColor: '#fff7ed', border: '2px solid #fdba74', borderRadius: 'var(--radius-sm)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: 'var(--shadow-brutal-sm)' }}
                                                              >
                                                                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                                                      <AlertCircle size={18} color="#c2410c" style={{ flexShrink: 0 }} />
                                                                      <div style={{ fontSize: '0.75rem', fontWeight: 700, color: '#9a3412', lineHeight: '1.4' }}>
                                                                          O fornecedor <strong>"{paymentDraft.supplierNameSnapshot}"</strong> foi extraído mas não existe no sistema.
                                                                      </div>
                                                                  </div>
                                                                  <button
                                                                      type="button"
                                                                      onClick={() => setQuickSupplierModal({
                                                                          show: true,
                                                                          initialName: paymentDraft.supplierNameSnapshot || '',
                                                                          initialTaxId: paymentDraft.supplierTaxId || ''
                                                                      })}
                                                                      style={{ 
                                                                          backgroundColor: '#f97316', color: '#fff', border: 'none', padding: '6px 12px', 
                                                                          borderRadius: 'var(--radius-sm)', fontSize: '0.7rem', fontWeight: 900, cursor: 'pointer',
                                                                          textTransform: 'uppercase', boxShadow: '2px 2px 0 #9a3412', flexShrink: 0, marginLeft: '12px'
                                                                      }}
                                                                  >
                                                                      CRIAR AGORA
                                                                  </button>
                                                              </motion.div>
                                                          )}
                                                      </label>
                                                     <label style={{ ...labelStyle, marginBottom: 0 }}>
                                                         Nº Documento
                                                         <input type="text" value={String(paymentDraft.documentNumber || '')} onChange={(e) => handleUpdateOcrDraft('documentNumber', e.target.value)} style={inputStyle} />
                                                     </label>
                                                     <label style={{ ...labelStyle, marginBottom: 0 }}>
                                                         Data
                                                         <input type="date" value={String(paymentDraft.documentDate || '')} onChange={(e) => handleUpdateOcrDraft('documentDate', e.target.value)} style={inputStyle} />
                                                     </label>
                                                     <label style={{ ...labelStyle, marginBottom: 0 }}>
                                                         Moeda
                                                         <select value={String(paymentDraft.currency || '')} onChange={(e) => handleUpdateOcrDraft('currency', e.target.value)} style={inputStyle}>
                                                             <option value="">--</option>
                                                             {currencies.map(c => <option key={c.id} value={c.code}>{c.code}</option>)}
                                                         </select>
                                                     </label>
                                                     <label style={{ ...labelStyle, marginBottom: 0 }}>
                                                         Total s/ IVA
                                                         <input type="number" value={(paymentDraft.items || []).reduce((sum, item) => sum + (((item?.quantity || 0) * (item?.unitPrice || 0)) - (item?.discountAmount || 0)), 0)} disabled style={{ ...inputStyle, backgroundColor: '#F9FAFB' }} />
                                                     </label>
                                                 </div>

                                                 <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                                     <h4 style={{ margin: 0, fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Itens da Fatura</h4>
                                                     <button 
                                                         type="button"
                                                         onClick={handleAddOcrItem}
                                                         style={{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.75rem', background: 'none', border: 'none', cursor: 'pointer' }}
                                                     >
                                                         <Plus size={16} /> ADICIONAR ITEM
                                                     </button>
                                                 </div>

                                                 <div style={{ overflowX: 'auto', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                                                     <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.75rem' }}>
                                                         <thead>
                                                             <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '1px solid var(--color-border)' }}>
                                                                 <th style={{ padding: '8px', textAlign: 'left', fontWeight: 800 }}>DESCRIÇÃO</th>
                                                                 <th style={{ padding: '8px', textAlign: 'center', width: '80px', fontWeight: 800 }}>UNID.</th>
                                                                 <th style={{ padding: '8px', textAlign: 'center', width: '80px', fontWeight: 800 }}>QTD</th>
                                                                 <th style={{ padding: '8px', textAlign: 'right', width: '100px', fontWeight: 800 }}>P. UNIT</th>
                                                                 <th style={{ padding: '8px', textAlign: 'center', width: '100px', fontWeight: 800 }}>IVA</th>
                                                                 <th style={{ padding: '8px', textAlign: 'right', width: '100px', fontWeight: 800 }}>DESC.</th>
                                                                 <th style={{ padding: '8px', textAlign: 'right', width: '100px', fontWeight: 800 }}>TOTAL</th>
                                                                 <th style={{ padding: '8px', textAlign: 'center', width: '40px' }}></th>
                                                             </tr>
                                                         </thead>
                                                         <tbody>
                                                             {!(paymentDraft.items && paymentDraft.items.length > 0) ? (
                                                                 <tr style={{ borderBottom: '1px solid var(--color-border-light)' }}>
                                                                     <td colSpan={8} style={{ padding: '32px 16px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                                                                         <span style={{ fontWeight: 800, display: 'block', marginBottom: '8px' }}>Nenhum item válido identificado no documento</span>
                                                                         <button
                                                                             type="button"
                                                                             onClick={handleAddOcrItem}
                                                                             style={{ 
                                                                                 display: 'inline-flex', alignItems: 'center', gap: '6px', 
                                                                                 backgroundColor: 'var(--color-primary)', color: 'white', 
                                                                                 border: 'none', padding: '8px 16px', borderRadius: '4px', 
                                                                                 fontWeight: 800, fontSize: '0.75rem', cursor: 'pointer' 
                                                                             }}
                                                                         >
                                                                             <Plus size={16} /> ADICIONAR LINHA MANUALMENTE
                                                                         </button>
                                                                     </td>
                                                                 </tr>
                                                             ) : (
                                                                 (paymentDraft.items || []).map((item, idx) => ({
                                                                     ...item,
                                                                     lineNumber: idx + 1
                                                                 })).map((item, idx) => (
                                                                     <tr key={idx} style={{ borderBottom: '1px solid var(--color-border-light)' }}>
                                                                         <td style={{ padding: '4px 8px' }}>
                                                                             <input type="text" value={String(item.description || '')} onChange={(e) => handleUpdateOcrItem(idx, 'description', e.target.value)} style={{ ...inputStyle, padding: '6px 8px', marginTop: 0 }} />
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px' }}>
                                                                             <select 
                                                                                 value={item.unitId || ''} 
                                                                                 onChange={(e) => {
                                                                                     const uId = Number(e.target.value);
                                                                                     const uMatch = units.find(x => x.id === uId);
                                                                                     setPaymentDraft(prev => {
                                                                                         if (!prev) return null;
                                                                                         const nextItems = [...prev.items];
                                                                                         nextItems[idx] = { ...nextItems[idx], unitId: uId, unit: uMatch ? uMatch.code : '' };
                                                                                         return { ...prev, items: nextItems };
                                                                                     });
                                                                                 }} 
                                                                                 style={{ ...inputStyle, padding: '6px 8px', marginTop: 0, textAlign: 'center' }}
                                                                             >
                                                                                 {units.map(u => <option key={u.id} value={u.id}>{u.code}</option>)}
                                                                             </select>
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px' }}>
                                                                             <input type="number" value={item.quantity || 0} onChange={(e) => handleUpdateOcrItem(idx, 'quantity', Number(e.target.value))} style={{ ...inputStyle, padding: '6px 8px', marginTop: 0, textAlign: 'center' }} />
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px' }}>
                                                                             <input type="number" value={item.unitPrice || 0} onChange={(e) => handleUpdateOcrItem(idx, 'unitPrice', Number(e.target.value))} style={{ ...inputStyle, padding: '6px 8px', marginTop: 0, textAlign: 'right' }} />
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px' }}>
                                                                             <select value={item.ivaRateId || ''} onChange={(e) => handleUpdateOcrItem(idx, 'ivaRateId', Number(e.target.value))} style={{ ...inputStyle, padding: '6px 8px', marginTop: 0 }}>
                                                                                 <option value="">0%</option>
                                                                                 {ivaRates.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
                                                                             </select>
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px' }}>
                                                                             <input
                                                                                 type="number"
                                                                                 value={item.discountAmount || 0}
                                                                                 onChange={(e) => handleUpdateOcrItem(idx, 'discountAmount', Number(e.target.value))}
                                                                                 style={{ ...inputStyle, padding: '6px 8px', marginTop: 0, textAlign: 'right', fontSize: '0.72rem' }}
                                                                                 min={0}
                                                                             />
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px', textAlign: 'right', fontWeight: 700 }}>
                                                                             {(Number(item.totalPrice) || 0).toLocaleString('pt-PT', { minimumFractionDigits: 2 })}
                                                                             {Number(item.discountAmount) > 0 && (
                                                                                 <div style={{ fontSize: '0.6rem', color: '#dc2626', fontWeight: 600 }}>
                                                                                     -{(Number(item.discountAmount)).toLocaleString('pt-PT', { minimumFractionDigits: 2 })}
                                                                                 </div>
                                                                             )}
                                                                         </td>
                                                                         <td style={{ padding: '4px 8px', textAlign: 'center' }}>
                                                                             <button type="button" onClick={() => handleRemoveOcrItem(idx)} style={{ color: '#EF4444', background: 'none', border: 'none', cursor: 'pointer' }}>
                                                                                 <Trash2 size={14} />
                                                                             </button>
                                                                         </td>
                                                                     </tr>
                                                                 ))
                                                             )}
                                                         </tbody>
                                                         <tfoot>
                                                             {(() => {
                                                                 const totalDiscount = (paymentDraft.items || []).reduce((sum, item) => sum + (Number(item.discountAmount) || 0), 0);
                                                                 return totalDiscount > 0 ? (
                                                                     <tr style={{ backgroundColor: '#fef2f2', borderBottom: '1px solid #fecaca' }}>
                                                                         <td colSpan={6} style={{ padding: '8px 16px', textAlign: 'right', fontSize: '0.75rem', fontWeight: 700, color: '#dc2626' }}>
                                                                             TOTAL ABATIMENTOS ({String(paymentDraft.currency || '')}):
                                                                         </td>
                                                                         <td style={{ padding: '8px 16px', textAlign: 'right', fontSize: '0.8rem', fontWeight: 800, color: '#dc2626' }}>
                                                                             -{totalDiscount.toLocaleString('pt-PT', { minimumFractionDigits: 2 })}
                                                                         </td>
                                                                         <td></td>
                                                                     </tr>
                                                                 ) : null;
                                                             })()}
                                                             <tr style={{ backgroundColor: '#F9FAFB', fontWeight: 800 }}>
                                                                 <td colSpan={6} style={{ padding: '12px 16px', textAlign: 'right' }}>TOTAL DO PEDIDO ({String(paymentDraft.currency || '')}):</td>
                                                                 <td style={{ padding: '12px 16px', textAlign: 'right', color: 'var(--color-primary)', fontSize: '0.85rem' }}>
                                                                     {(Number(paymentDraft.totalAmount) || 0).toLocaleString('pt-PT', { minimumFractionDigits: 2 })}
                                                                 </td>
                                                                 <td style={{ textAlign: 'center' }}>
                                                                    <button type="button" onClick={handleAddOcrItem} title="Adicionar Item" style={{ color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', padding: '4px 8px' }}>
                                                                        <Plus size={18} />
                                                                    </button>
                                                                 </td>
                                                             </tr>
                                                         </tfoot>
                                                     </table>
                                                 </div>
                                             </div>
                                         )}
                                     </div>
                                 </motion.div>
                             )}
                         </AnimatePresence>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '24px' }}>
                            <label style={labelStyle}>
                                Grau de Necessidade <span style={{ color: 'red' }}>*</span>
                                <select name="needLevelId" value={formData.needLevelId} onChange={handleChange} style={getInputStyle('NeedLevelId')}>
                                    <option value="">-- Selecione --</option>
                                    {needLevels.filter(nl => nl.isActive).map(nl => (
                                        <option key={nl.id} value={nl.id}>{nl.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('NeedLevelId')}
                            </label>

                            <AnimatePresence>
                                {(Number(formData.requestTypeId) === 1 || Number(formData.requestTypeId) === 2) && (
                                    <motion.div
                                        initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }}
                                        style={{ overflow: 'hidden' }}
                                    >
                                        <label style={labelStyle}>
                                            {Number(formData.requestTypeId) === 2 ? 'Data de vencimento' : 'Necessário até (Data limite)'} <span style={{ color: 'red' }}>*</span>
                                            <DateInput
                                                required name="needByDateUtc" value={formData.needByDateUtc}
                                                onChange={(val) => {
                                                    setFormData(prev => ({ ...prev, needByDateUtc: val }));
                                                    clearFieldError('NeedByDateUtc');
                                                }}
                                                hasError={!!getFieldErrors('NeedByDateUtc')}
                                                style={getInputStyle('NeedByDateUtc')}
                                            />
                                            {renderFieldError('NeedByDateUtc')}
                                            {!getFieldErrors('NeedByDateUtc') && formData.needByDateUtc && new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0) && (
                                                <div style={{ color: '#D97706', fontSize: '0.75rem', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600 }}>
                                                    <AlertTriangle size={12} />
                                                    {Number(formData.requestTypeId) === 2 ? 'O documento está vencido.' : 'A data selecionada está no passado.'}
                                                </div>
                                            )}
                                        </label>
                                    </motion.div>
                                )}
                            </AnimatePresence>

                            <label style={labelStyle}>
                                Departamento <span style={{ color: 'red' }}>*</span>
                                <select name="departmentId" value={formData.departmentId} onChange={handleChange} style={getInputStyle('DepartmentId')}>
                                    <option value="">-- Selecione --</option>
                                    {departments.filter(d => d.isActive).map(d => (
                                        <option key={d.id} value={d.id}>{d.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('DepartmentId')}
                            </label>

                             <label style={labelStyle}>
                                Empresa <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="companyId" value={formData.companyId} onChange={handleChange} style={getInputStyle('CompanyId')}
                                    disabled={filteredCompanies.length <= 1}
                                >
                                    <option value="">-- Selecione --</option>
                                    {filteredCompanies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                                </select>
                                {renderFieldError('CompanyId')}
                                {paymentDraft?.isCompanyOcrAutoFilled && (
                                    <motion.div 
                                        initial={{ opacity: 0, y: -5 }} animate={{ opacity: 1, y: 0 }}
                                        style={{ color: '#059669', fontSize: '0.75rem', marginTop: '6px', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '4px' }}
                                    >
                                        <CheckCircle2 size={14} />
                                        Preenchido automaticamente conforme identificado ({paymentDraft.extractedCompanyName})
                                    </motion.div>
                                )}
                            </label>

                            <label style={labelStyle}>
                                Planta <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="plantId" value={formData.plantId} onChange={handleChange} style={getInputStyle('PlantId')}
                                    disabled={filteredPlants.length <= 1 || (!!formData.companyId && filteredPlants.filter(p => Number(formData.companyId) === p.companyId).length <= 1)}
                                >
                                    <option value="">-- Selecione --</option>
                                    {filteredPlants
                                        .filter(p => p.isActive && (!formData.companyId || p.companyId === Number(formData.companyId)))
                                        .map(p => <option key={p.id} value={p.id}>{p.name}</option>)
                                    }
                                </select>
                                {renderFieldError('PlantId')}
                            </label>
                        </div>
                    </div>
                </section>
            </form>
            <QuickSupplierModal 
                isOpen={quickSupplierModal.show}
                onClose={() => setQuickSupplierModal({ show: false, initialName: '', initialTaxId: '' })}
                onSuccess={(s) => {
                    handleUpdateOcrDraft('supplierId', s.id);
                    handleUpdateOcrDraft('supplierNameSnapshot', s.name);
                    handleUpdateOcrDraft('supplierPortalCode', s.portalCode || '');
                    clearFieldError('SupplierId');
                }}
                initialName={quickSupplierModal.initialName}
                initialTaxId={quickSupplierModal.initialTaxId}
            />

            <AnimatePresence>
                {duplicateWarning?.isOpen && (
                    <div style={{ position: 'fixed', inset: 0, zIndex: 100, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '16px', backgroundColor: 'rgba(17, 24, 39, 0.5)', backdropFilter: 'blur(4px)' }}>
                        <motion.div
                            initial={{ opacity: 0, scale: 0.95 }}
                            animate={{ opacity: 1, scale: 1 }}
                            exit={{ opacity: 0, scale: 0.95 }}
                            style={{ backgroundColor: 'white', borderRadius: '12px', boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)', width: '100%', maxWidth: '448px', overflow: 'hidden', position: 'relative', border: '1px solid #e5e7eb' }}
                        >
                            <div style={{ padding: '24px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '48px', height: '48px', margin: '0 auto 16px', backgroundColor: '#fef3c7', borderRadius: '9999px' }}>
                                    <AlertTriangle size={24} color="#d97706" />
                                </div>
                                <h3 style={{ fontSize: '1.125rem', fontWeight: 600, textAlign: 'center', color: '#111827', marginBottom: '8px' }}>
                                    Documento Já Existente
                               </h3>
                                <p style={{ fontSize: '0.875rem', color: '#4b5563', textAlign: 'center', marginBottom: '16px' }}>
                                    Aviso de possível duplicidade: A extração binária (assinatura via ficheiro) detectou que este exato documento já foi carregado no sistema anteriormente.
                                </p>
                                
                                <div style={{ backgroundColor: '#f9fafb', padding: '16px', borderRadius: '8px', fontSize: '0.875rem', color: '#4b5563', marginBottom: '24px' }}>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Pedido Vinculado:</span> {duplicateWarning.requestNumber}</p>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Enviado por:</span> {duplicateWarning.uploadedBy || 'Desconhecido'}</p>
                                    <p><span style={{ fontWeight: 600, color: '#374151' }}>Enviado em:</span> {duplicateWarning.createdAtUtc ? formatDateTime(duplicateWarning.createdAtUtc) : '-'}</p>
                                </div>

                                <div style={{ display: 'flex', gap: '12px', marginTop: '24px' }}>
                                    <button
                                        type="button"
                                        onClick={() => setDuplicateWarning(null)}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: '#374151', backgroundColor: 'white', border: '1px solid #d1d5db', borderRadius: '8px', cursor: 'pointer' }}
                                    >
                                        Cancelar Envio
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => {
                                            if (duplicateWarning?.uploadCallback) {
                                                duplicateWarning.uploadCallback();
                                            }
                                        }}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: 'white', backgroundColor: '#d97706', border: 'none', borderRadius: '8px', cursor: 'pointer' }}
                                    >
                                        Estou Ciente, Prosseguir
                                    </button>
                                </div>
                            </div>
                        </motion.div>
                    </div>
                )}
            </AnimatePresence>
        </motion.div >
    );
}
