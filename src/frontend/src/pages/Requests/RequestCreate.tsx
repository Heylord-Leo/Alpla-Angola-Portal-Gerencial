import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate, useLocation, useSearchParams } from 'react-router-dom';
import { Save, X, Paperclip, Trash2, AlertTriangle, FileText, RefreshCw, UploadCloud, CheckCircle2, UserPlus, AlertCircle, Edit2, Plus, ArrowLeft } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { LookupDto, IvaRate, Unit, CurrencyDto, OcrDraft, OcrDraftItem, RequesterItem } from '../../types';
import { useOcrProcessor } from '../../hooks/useOcrProcessor';
import { motion, AnimatePresence } from 'framer-motion';
import { RequestActionHeader, BreadcrumbItem } from './components/RequestActionHeader';
import { scrollToFirstError } from '../../lib/validation';
import { DateInput } from '../../components/DateInput';
import { getMinNeedByDate, getMinLeadDays, isBeforeMinNeedByDate, getMinNeedByHint, getMinNeedByError, getNeedByAdjustmentNotice } from '../../lib/needByDate';
import { computeFileHash, formatDateTime } from '../../lib/utils';
import { CatalogItemAutocomplete } from '../../components/CatalogItemAutocomplete';
import { useCatalogItemReconciliation } from '../../hooks/useCatalogItemReconciliation';
import { CatalogItemReconciliationModal } from '../../components/CatalogItemReconciliationModal';
import { ReconciliationWarningDialog } from '../../components/ReconciliationWarningDialog';
import { ReconcilableItem, ItemResolution } from '../../types';
import { LiveGuideLauncher } from '../../features/guided-tour/live-guide/LiveGuideLauncher';
import { useLiveGuideRegistration } from '../../features/guided-tour/live-guide/LiveGuideProvider';
import { createRequestCreationGuide, type RequestFormValues } from '../../features/guided-tour/live-guide/guides/requestCreation.liveGuide';
import { SourceDocumentTypeField } from '../../components/requests/SourceDocumentTypeField';
import { PaymentSourceDocumentDraftCollection } from '../../components/requests/PaymentSourceDocumentDraftCollection';
import { usePaymentRequestCreation } from '../../hooks/usePaymentRequestCreation';
import { usePaymentDocumentOcr } from '../../hooks/usePaymentDocumentOcr';
import { PHASE_LABEL, TemporaryPaymentDocument } from '../../lib/paymentRequestCreation';
import {
    CONFLICT_JUSTIFICATION_MIN_LENGTH,
    buildClassificationPayload,
    evaluateClassificationConflict,
    type ClassificationConflictState
} from '../../lib/documentClassificationDecision';
import { useFeatureFlags } from '../../hooks/useFeatureFlags';
import { FieldMessageIcon } from '../../components/ui/FieldMessageIcon';


export function RequestCreate() {
    const navigate = useNavigate();
    // Post-Payment Completion (Release 2). Flags start off, so the form renders exactly as before
    // until the server confirms the feature is enabled.
    const { flags: featureFlags } = useFeatureFlags();
    const [searchParams] = useSearchParams();
    const location = useLocation() as { state: { fromList?: string } | null };
    
    // Copy Mode Detection
    const copyFromId = searchParams.get('copyFrom');
    const isCopyMode = !!copyFromId;

    const [loading, setLoading] = useState(false);
    const [isTemplateLoading, setIsTemplateLoading] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    // Per-row validation for the mandatory Quotation items (which fields are invalid on each row).
    const [itemRowErrors, setItemRowErrors] = useState<Record<number, { description?: boolean; quantity?: boolean; unitId?: boolean }>>({});
    // Transient red-pulse flag for the items section (auto-clears after ~5s → discreet error border remains).
    const [itemsPulse, setItemsPulse] = useState(false);
    const itemsPulseTimer = useRef<number | undefined>(undefined);
    const [requestTypes, setRequestTypes] = useState<any[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [allowedPlantCodes, setAllowedPlantCodes] = useState<string[]>([]);
    const [allowedDepartmentCodes, setAllowedDepartmentCodes] = useState<string[]>([]);
    const [isScopeLoading, setIsScopeLoading] = useState(true);
    const [scopeError, setScopeError] = useState<string | null>(null);
    const [lookupsError, setLookupsError] = useState<string | null>(null);
    const [attachments, setAttachments] = useState<File[]>([]);
    // Discreet notice shown when "Necessário até" is pushed forward to honour the need-level minimum
    const [needByAdjustmentNotice, setNeedByAdjustmentNotice] = useState<string | null>(null);

    // Payment OCR States
    const [ivaRates, setIvaRates] = useState<IvaRate[]>([]);
    const [units, setUnits] = useState<Unit[]>([]);
    const [currencies, setCurrencies] = useState<CurrencyDto[]>([]);
    const [isOcrLoading, setIsOcrLoading] = useState(false);
    const [duplicateWarning, setDuplicateWarning] = useState<{ isOpen: boolean; requestNumber: string; uploadCallback: () => void; uploadedBy?: string; createdAtUtc?: string } | null>(null);
    const [dupCountdown, setDupCountdown] = useState(0);

    // Countdown timer for duplicate warning confirm button safety delay
    useEffect(() => {
        if (!duplicateWarning?.isOpen) { setDupCountdown(0); return; }
        setDupCountdown(5);
        const interval = setInterval(() => {
            setDupCountdown(prev => {
                if (prev <= 1) { clearInterval(interval); return 0; }
                return prev - 1;
            });
        }, 1000);
        return () => clearInterval(interval);
    }, [duplicateWarning?.isOpen]);

    const [paymentDraft, setPaymentDraft] = useState<OcrDraft | null>(null);
    const [ocrFile, setOcrFile] = useState<File | null>(null);

    // Release 3: when multi-document is on, a PAYMENT request is composed as a COLLECTION of source
    // documents held client-side (each keyed by a temporary id) and flushed to the server in one
    // controlled pass after the draft exists. See usePaymentRequestCreation for the staging.
    const [tempDocuments, setTempDocuments] = useState<TemporaryPaymentDocument[]>([]);
    const creation = usePaymentRequestCreation();
    const documentOcr = usePaymentDocumentOcr();

    // Files chosen before the request exists. Keyed by a placeholder attachment id so the card can
    // show a filename immediately; the real upload happens in Stage C once there is a request to
    // upload against. The map is also what stops the same file being uploaded twice on a retry.
    const pendingFilesRef = useRef<Map<string, File>>(new Map());
    const uploadedIdsRef = useRef<Map<string, string>>(new Map());
    const sourceFileInputRef = useRef<HTMLInputElement>(null);
    const sourceFileResolverRef = useRef<((f: File | null) => void) | null>(null);

    /**
     * Opens the picker and resolves with a placeholder id the card can render straight away, plus
     * the File itself so OCR can read it immediately — before any request exists.
     */
    const pickSourceDocumentFile = useCallback(async () => {
        const file = await new Promise<File | null>(resolve => {
            sourceFileResolverRef.current = resolve;
            sourceFileInputRef.current?.click();
        });

        if (!file) return null;

        const placeholderId = `pending:${Date.now().toString(36)}:${file.name}`;
        pendingFilesRef.current.set(placeholderId, file);
        return { id: placeholderId, fileName: file.name, file };
    }, []);

    /**
     * Reads one document through POST /requests/direct-ocr, which needs no RequestId — so a
     * document can be read before anything is persisted, with no early attachment to reconcile
     * later. The result is stored under the document's own tempId.
     */
    const runDocumentOcr = useCallback(async (doc: TemporaryPaymentDocument) => {
        const placeholder = doc.attachmentId;
        const file = placeholder ? pendingFilesRef.current.get(placeholder) : undefined;
        if (file) documentOcr.registerFile(doc.tempId, file);

        const result = await documentOcr.run(doc);
        if (!result) return;

        // Merge back by tempId — never by position, and never touching another card.
        setTempDocuments(prev => prev.map(d =>
            d.tempId === doc.tempId
                ? { ...result.document, classification: result.document.classification }
                : d));
    }, [documentOcr]);

    const handleSourceFileChosen = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0] ?? null;
        e.target.value = '';
        const resolve = sourceFileResolverRef.current;
        sourceFileResolverRef.current = null;
        resolve?.(file);
    };

    /**
     * Stage C.0 — turn placeholder ids into real attachments, once the request exists.
     * An id already uploaded is reused rather than sent again, so a retry after a partial failure
     * never uploads the same invoice twice.
     */
    const materialiseAttachments = useCallback(async (
        requestId: string,
        documents: TemporaryPaymentDocument[]
    ): Promise<TemporaryPaymentDocument[]> => {
        const out: TemporaryPaymentDocument[] = [];

        for (const d of documents) {
            if (!d.attachmentId || !d.attachmentId.startsWith('pending:')) { out.push(d); continue; }

            const already = uploadedIdsRef.current.get(d.attachmentId);
            if (already) { out.push({ ...d, attachmentId: already }); continue; }

            const file = pendingFilesRef.current.get(d.attachmentId);
            if (!file) { out.push({ ...d, error: 'O ficheiro deste documento já não está disponível. Volte a anexá-lo.' }); continue; }

            try {
                const uploaded = await api.attachments.upload(requestId, [file], 'PAYMENT_SOURCE_DOCUMENT');
                const realId = Array.isArray(uploaded)
                    ? (uploaded[0]?.id ?? uploaded[0]?.attachmentId)
                    : (uploaded?.id ?? uploaded?.attachmentId);

                if (!realId) { out.push({ ...d, error: 'O anexo foi carregado mas o servidor não devolveu a sua identificação.' }); continue; }

                uploadedIdsRef.current.set(d.attachmentId, realId as string);
                out.push({ ...d, attachmentId: realId as string });
            } catch (err: any) {
                out.push({ ...d, error: err?.message ?? 'Não foi possível carregar o ficheiro deste documento.' });
            }
        }

        return out;
    }, []);
    const [isManualOcr, setIsManualOcr] = useState(false);
    const [quickSupplierModal, setQuickSupplierModal] = useState<{ show: boolean; initialName: string; initialTaxId: string }>({ show: false, initialName: '', initialTaxId: '' });
    const fileInputRef = useRef<HTMLInputElement>(null);
    const manualFileInputRef = useRef<HTMLInputElement>(null);

    // Requester Items State (for Quotation requests)
    const [requesterItems, setRequesterItems] = useState<RequesterItem[]>([]);

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
        finalApproverId: '',
        // Post-Payment Completion (Release 2 corrected): identity of the attached document.
        sourceDocumentType: ''
    });

    // Conflict between the user's classification and what the document extraction read.
    // Held separately from formData because it is evidence about the choice, not the choice.
    const [classificationConflict, setClassificationConflict] = useState<ClassificationConflictState>({
        hasConflict: false, isHighRisk: false, acknowledged: false, justification: ''
    });

    // Reconciliation Engine — unified for both payment and requester items
    const [showReconciliationWarning, setShowReconciliationWarning] = useState(false);
    const activeItems: ReconcilableItem[] = Number(formData.requestTypeId) === 2 && paymentDraft
        ? paymentDraft.items as any[]
        : requesterItems as any[];
    const reconciliation = useCatalogItemReconciliation(activeItems);

    const initialFormDataRef = useRef(formData);

    // ── Live Guide Registration ─────────────────────────────────────────
    const { registerGuideFactory, unregisterGuideFactory } = useLiveGuideRegistration();
    const formDataRef = useRef(formData);
    formDataRef.current = formData;

    const getFormValuesForGuide = useCallback((): RequestFormValues => ({
        title: formDataRef.current.title,
        description: formDataRef.current.description,
        requestTypeId: formDataRef.current.requestTypeId,
        needLevelId: formDataRef.current.needLevelId,
        needByDateUtc: formDataRef.current.needByDateUtc,
        departmentId: formDataRef.current.departmentId,
        companyId: formDataRef.current.companyId,
        plantId: formDataRef.current.plantId,
    }), []);

    useEffect(() => {
        registerGuideFactory('request-creation-live-guide', () =>
            createRequestCreationGuide(getFormValuesForGuide)
        );
        return () => unregisterGuideFactory('request-creation-live-guide');
    }, [registerGuideFactory, unregisterGuideFactory, getFormValuesForGuide]);

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

    // Load user scope (plants) independently from auxiliary lookups.
    // This prevents an unrelated lookup failure from hiding the user's plant access.
    useEffect(() => {
        async function loadScopeAndLookups() {
            setIsScopeLoading(true);
            setScopeError(null);
            setLookupsError(null);

            // 1. Load user profile (critical for scope determination)
            try {
                const meData = await api.users.me();
                const userPlants: string[] = meData.plants || [];
                const userDepartments: string[] = meData.departments || [];
                setAllowedPlantCodes(userPlants);
                setAllowedDepartmentCodes(userDepartments);
                console.info(`[RequestCreate] Profile loaded: ${userPlants.length} plant(s), ${userDepartments.length} department(s)`);
            } catch (err) {
                console.error('[RequestCreate] Failed to load user profile (/me)', err);
                setScopeError('Não foi possível carregar o seu perfil de acesso. Tente recarregar a página ou contacte o Administrador.');
            }

            // 2. Load auxiliary lookups (independent from scope)
            try {
                const [levelsData, departmentsData, companiesData, plantsData, rtData, ivaData, unitsData, currenciesData] = await Promise.all([
                    api.lookups.getNeedLevels(true),
                    api.lookups.getDepartments(true),
                    api.lookups.getCompanies(true),
                    api.lookups.getPlants(undefined, true),
                    api.lookups.getRequestTypes(true),
                    api.lookups.getIvaRates(true),
                    api.lookups.getUnits(true),
                    api.lookups.getCurrencies(true)
                ]);
                setNeedLevels(levelsData);
                setDepartments(departmentsData);
                setCompanies(companiesData);
                setPlants(plantsData);
                setRequestTypes(rtData);
                setIvaRates(ivaData);
                setUnits(unitsData);
                setCurrencies(currenciesData);
                console.info('[RequestCreate] Auxiliary lookups loaded successfully');
            } catch (err) {
                console.error('[RequestCreate] Failed to load auxiliary lookups', err);
                setLookupsError('Falha ao carregar dados auxiliares (tipos, departamentos, etc.). Tente recarregar a página.');
            }

            setIsScopeLoading(false);
        }
        loadScopeAndLookups();
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
    const filteredDepartments = departments.filter(d => allowedDepartmentCodes.includes(d.code));

    // Need-level lead time: the "Necessário até" date may not precede the minimum implied by the
    // selected Grau de Necessidade. Quotation only — on Payment requests the same field carries the
    // supplier invoice due date, which is legitimately allowed to be in the past.
    const isQuotationType = Number(formData.requestTypeId) === 1;
    const selectedNeedLevel = needLevels.find(nl => nl.id === Number(formData.needLevelId));
    const minNeedByDate = isQuotationType ? getMinNeedByDate(selectedNeedLevel?.code) : null;
    const minNeedByLeadDays = isQuotationType ? getMinLeadDays(selectedNeedLevel?.code) : null;
    const isBelowMinNeedByDate = isBeforeMinNeedByDate(formData.needByDateUtc, minNeedByDate);

    /**
     * Given the form values the user is moving to, returns the "Necessário até" date that honours the
     * need-level minimum: auto-filled when empty, pushed forward when it falls short, kept otherwise.
     */
    const resolveNeedByDate = (requestTypeId: string, needLevelId: string, currentNeedByDate: string) => {
        if (Number(requestTypeId) !== 1) return { needByDate: currentNeedByDate, notice: null as string | null };

        const level = needLevels.find(nl => nl.id === Number(needLevelId));
        const minDate = getMinNeedByDate(level?.code);
        if (!minDate || !level) return { needByDate: currentNeedByDate, notice: null as string | null };

        if (!currentNeedByDate) {
            return { needByDate: minDate, notice: null as string | null };
        }
        if (isBeforeMinNeedByDate(currentNeedByDate, minDate)) {
            return { needByDate: minDate, notice: getNeedByAdjustmentNotice(level.name, minDate) };
        }
        return { needByDate: currentNeedByDate, notice: null as string | null };
    };

    // Diagnostic log: plant + department scope filter result (non-sensitive)
    useEffect(() => {
        if (!isScopeLoading && plants.length > 0) {
            console.info(`[RequestCreate] Plant scope filter: ${filteredPlants.length} allowed out of ${plants.length} total`);
        }
        if (!isScopeLoading && departments.length > 0) {
            console.info(`[RequestCreate] Department scope filter: ${filteredDepartments.length} allowed out of ${departments.length} total`);
        }
    }, [isScopeLoading, plants.length, filteredPlants.length, departments.length, filteredDepartments.length]);



    // Auto-selection of Company/Plant/Department based on restricted scope
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

            // Auto-select department when only one is in scope.
            // (Fase B: o aprovador de área não é mais pré-nomeado — o roteamento é
            // resolvido pelo backend via DepartmentManagers no submit/decisão.)
            if (filteredDepartments.length === 1 && !next.departmentId) {
                next.departmentId = String(filteredDepartments[0].id);
            }

            return next;
        });
    }, [isScopeLoading, isTemplateLoading, plants.length, companies.length, filteredPlants.length, filteredCompanies.length, filteredDepartments.length, isCopyMode]);

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
            
            // If user manually changes the absolute discount amount, clear the reactive percentage
            if (field === 'discountAmount') {
                nextItems[index] = { ...nextItems[index], discountAmount: value, discountPercent: undefined };
            } else {
                nextItems[index] = { ...nextItems[index], [field]: value };
            }

            // If user manually edits the description, clear auto-match linkage
            // to prevent showing "Correspondência automática" for a modified description
            if (field === 'description') {
                nextItems[index].autoMatchStatus = null;
                nextItems[index].itemCatalogId = null;
                nextItems[index].itemCatalogCode = null;
            }
            
            // Reactive discount recalculation if percentage is locked in
            if ((field === 'quantity' || field === 'unitPrice') && nextItems[index].discountPercent !== undefined) {
                const qty = nextItems[index].quantity || 0;
                const price = nextItems[index].unitPrice || 0;
                const pct = nextItems[index].discountPercent!;
                const recalculatedDiscount = Math.round(qty * price * (pct / 100) * 100) / 100;
                nextItems[index].discountAmount = recalculatedDiscount;
            }

            if (field === 'quantity' || field === 'unitPrice' || field === 'ivaRateId' || field === 'discountAmount') {
                nextItems[index].totalPrice = calculateItemTotal(nextItems[index]);
            }
            
            const next = { ...prev, items: nextItems };
            next.totalAmount = calculateDraftTotal(next);
            return next;
        });
    };

    const handleCatalogSelectOcrItem = (index: number, description: string, catalogId: number | null, catalogCode: string | null, defaultUnitId: number | null) => {
        setPaymentDraft(prev => {
            if (!prev) return null;
            const nextItems = [...prev.items];
            nextItems[index] = {
                ...nextItems[index],
                description,
                itemCatalogId: catalogId,
                itemCatalogCode: catalogCode,
                unitId: defaultUnitId || nextItems[index].unitId,
                // Clear AUTO_MATCHED when user manually selects (it's now a manual selection)
                autoMatchStatus: catalogId ? null : 'NEEDS_REVIEW',
            };
            const next = { ...prev, items: nextItems };
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

        // Selecting/changing the need level (or switching request type) re-applies the minimum
        // lead time to "Necessário até": fill when empty, push forward when it falls short, keep otherwise.
        let resolvedNeedBy: { needByDate: string; notice: string | null } | null = null;
        if (name === 'needLevelId' || name === 'requestTypeId') {
            resolvedNeedBy = resolveNeedByDate(
                name === 'requestTypeId' ? value : formData.requestTypeId,
                name === 'needLevelId' ? value : formData.needLevelId,
                formData.needByDateUtc
            );
            setNeedByAdjustmentNotice(resolvedNeedBy.notice);
            if (resolvedNeedBy.needByDate !== formData.needByDateUtc) clearFieldError('NeedByDateUtc');
        }

        setFormData(prev => {
            const next = { ...prev, [name]: value };
            if (resolvedNeedBy) next.needByDateUtc = resolvedNeedBy.needByDate;
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

            // (Fase B: trocar o departamento não pré-nomeia aprovador de área —
            // o roteamento é resolvido pelo backend via DepartmentManagers.)

            return next;
        });
        clearFieldError(name);
    };

    // Requester Items Handlers (Quotation)
    const handleAddRequesterItem = () => {
        setRequesterItems(prev => [...prev, {
            lineNumber: prev.length + 1,
            description: '',
            quantity: 1,
            unitId: null,
            notes: '',
            itemCatalogId: null,
            itemCatalogCode: null
        }]);
    };

    const handleUpdateRequesterItem = (index: number, field: keyof RequesterItem, value: any) => {
        setRequesterItems(prev => {
            const next = [...prev];
            next[index] = { ...next[index], [field]: value };
            return next;
        });
        clearItemRowError(index, field as 'description' | 'quantity' | 'unitId');
    };

    // Clear a single invalid-field flag once the user starts fixing it (keeps highlights honest).
    const clearItemRowError = (index: number, field: 'description' | 'quantity' | 'unitId') => {
        setItemRowErrors(prev => {
            if (!prev[index] || !prev[index][field]) return prev;
            const nextRow = { ...prev[index] };
            delete nextRow[field];
            const next = { ...prev };
            if (Object.keys(nextRow).length === 0) delete next[index]; else next[index] = nextRow;
            return next;
        });
    };

    const handleRemoveRequesterItem = (index: number) => {
        setRequesterItems(prev => prev.filter((_, i) => i !== index));
    };

    const handleCatalogSelectRequesterItem = (index: number, description: string, catalogId: number | null, catalogCode: string | null, defaultUnitId: number | null) => {
        setRequesterItems(prev => {
            const next = [...prev];
            next[index] = {
                ...next[index],
                description,
                itemCatalogId: catalogId,
                itemCatalogCode: catalogCode,
                unitId: defaultUnitId || next[index].unitId
            };
            return next;
        });
        if (description && description.trim() !== '') clearItemRowError(index, 'description');
        if (defaultUnitId) clearItemRowError(index, 'unitId');
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        const newErrors: Record<string, string[]> = {};
        if (!formData.requestTypeId) newErrors['RequestTypeId'] = ['O Tipo de Pedido é obrigatório.'];
        if (!formData.needLevelId) newErrors['NeedLevelId'] = ['O grau de necessidade é obrigatório.'];
        if (!formData.departmentId) newErrors['DepartmentId'] = ['O departamento é obrigatório.'];
        if (!formData.companyId) newErrors['CompanyId'] = ['A empresa é obrigatória.'];
        if (!formData.plantId) newErrors['PlantId'] = ['A planta é obrigatória.'];

        // Post-Payment Completion (Release 2 corrected): when the classification contradicts what
        // the document extraction read, the user must acknowledge it — and for a high-risk conflict
        // (they chose a non-fiscal type for a document the evidence reads as fiscal, or the
        // extraction was confident) they must also write down why. A draft may still be saved
        // unclassified; this only guards a classification that disagrees with the evidence.
        if (Number(formData.requestTypeId) === 2 && featureFlags.postPaymentCompletionEnabled && formData.sourceDocumentType) {
            const evaluation = evaluateClassificationConflict(
                formData.sourceDocumentType, paymentDraft?.documentClassification ?? null);

            if (evaluation.hasConflict && !classificationConflict.acknowledged) {
                newErrors['sourceDocumentType'] = ['Confirme a classificação: ela difere da leitura do documento.'];
            } else if (evaluation.isHighRisk &&
                       classificationConflict.justification.trim().length < CONFLICT_JUSTIFICATION_MIN_LENGTH) {
                newErrors['sourceDocumentType'] = [
                    `Justifique a classificação (mínimo ${CONFLICT_JUSTIFICATION_MIN_LENGTH} caracteres).`];
            }
        }

        if (Number(formData.requestTypeId) === 1 || Number(formData.requestTypeId) === 2) {
            const isPayment = Number(formData.requestTypeId) === 2;
            if (!formData.needByDateUtc) {
                newErrors['NeedByDateUtc'] = [isPayment ? 'A data de vencimento é obrigatória para pedidos de Pagamento.' : 'A data Necessário Até é obrigatória para pedidos de Cotação.'];
            } else if (isBelowMinNeedByDate && selectedNeedLevel && minNeedByDate) {
                // Need-level minimum lead time (Quotation only) — mirrors the server-side rule.
                newErrors['NeedByDateUtc'] = [getMinNeedByError(selectedNeedLevel.name, minNeedByDate)];
            }
        }

        // Phase 2 — Mandatory items (QUOTATION): a Quotation is created already-submitted, so we block
        // before the POST. Mirror the payload filter (only description-non-empty rows are sent) AND the
        // authoritative backend rule: there must be >= 1 item and EVERY sent item must be valid
        // (quantity > 0 and a unit). A single invalid row blocks the submission.
        const rowErrors: Record<number, { description?: boolean; quantity?: boolean; unitId?: boolean }> = {};
        if (Number(formData.requestTypeId) === 1) {
            // A row is "started" if the user touched any of its meaningful fields.
            const startedRows = requesterItems
                .map((ri, idx) => ({ ri, idx }))
                .filter(({ ri }) => (!!ri.description && ri.description.trim() !== '') || Number(ri.quantity) > 0 || !!ri.unitId);

            if (startedRows.length === 0) {
                // Empty section — no rows to highlight, just the section-level message.
                newErrors['LineItems'] = ['Adicione pelo menos um item válido antes de continuar.'];
            } else {
                for (const { ri, idx } of startedRows) {
                    const e: { description?: boolean; quantity?: boolean; unitId?: boolean } = {};
                    if (!ri.description || ri.description.trim() === '') e.description = true;
                    if (!(Number(ri.quantity) > 0)) e.quantity = true;
                    if (!ri.unitId) e.unitId = true;
                    if (Object.keys(e).length > 0) rowErrors[idx] = e;
                }
                const anyFullyValid = startedRows.some(({ ri }) => !!ri.description && ri.description.trim() !== '' && Number(ri.quantity) > 0 && !!ri.unitId);
                if (Object.keys(rowErrors).length > 0 || !anyFullyValid) {
                    newErrors['LineItems'] = ['Corrija os itens destacados antes de continuar.'];
                }
            }
        }
        setItemRowErrors(rowErrors);

        if (Object.keys(newErrors).length > 0) {
            setFieldErrors(newErrors);
            setFeedback({ type: 'error', message: 'Preencha todos os campos obrigatórios antes de continuar.' });
            scrollToFirstError(newErrors);
            // Items section: pulse for ~5s then leave a discreet error border; focus the first fixable field.
            if (newErrors['LineItems']) {
                triggerItemsPulse();
                const firstBadRow = Object.keys(rowErrors).map(Number).sort((a, b) => a - b)[0];
                if (firstBadRow !== undefined) {
                    const field = rowErrors[firstBadRow].description ? 'description'
                        : rowErrors[firstBadRow].quantity ? 'quantity' : 'unitId';
                    setTimeout(() => {
                        const cell = document.querySelector(`[data-item-row="${firstBadRow}"][data-item-field="${field}"]`);
                        (cell?.querySelector('input, select') as HTMLElement | null)?.focus();
                    }, 550);
                }
            }
            return;
        }

        setLoading(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});

        // Reconciliation guardrail: check for unresolved catalog items before submission
        if (reconciliation.hasUnresolved && !showReconciliationWarning) {
            setShowReconciliationWarning(true);
            setLoading(false);
            return;
        }
        setShowReconciliationWarning(false);

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
            discountAmount: paymentDraft && Number(formData.requestTypeId) === 2 
                ? (paymentDraft.discountAmount || 0) 
                : 0,
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
            // areaApproverId removido (Fase B): o backend resolve o roteamento de área
            finalApproverId: formData.finalApproverId || null,
            // Post-Payment Completion (Release 2). PAYMENT only, and only when the feature is on.
            // A PAYMENT request is created as a DRAFT, so an empty value is legitimate here — the
            // mandatory rule applies at submission.
            sourceDocumentType: Number(formData.requestTypeId) === 2 && featureFlags.postPaymentCompletionEnabled
                ? (formData.sourceDocumentType || null)
                : null,
            // Classification evidence: what the extraction proposed, whether the user overrode it,
            // and why. Persisted so a disputed classification can always be explained afterwards.
            ...(Number(formData.requestTypeId) === 2 && featureFlags.postPaymentCompletionEnabled && formData.sourceDocumentType
                ? (() => {
                    const c = buildClassificationPayload(
                        formData.sourceDocumentType,
                        paymentDraft?.documentClassification,
                        classificationConflict);
                    return {
                        sourceDocumentTypeSource: c.source,
                        sourceDocumentTypeOcrSuggestion: c.suggestion,
                        sourceDocumentTypeOcrConfidence: c.confidence,
                        sourceDocumentTypeEvidenceJson: c.evidenceJson,
                        sourceDocumentTypeTitleFound: c.titleFound,
                        sourceDocumentTypeConflictingEvidenceJson: c.conflictingEvidenceJson,
                        sourceDocumentTypeSuggestionSource: c.suggestionSource,
                        sourceDocumentAttachmentId: paymentDraft?.proformaAttachmentId ?? null,
                        classificationConflictAcknowledged: c.acknowledged,
                        classificationJustification: c.justification
                    };
                })()
                : {}),
            lineItems: Number(formData.requestTypeId) === 2 && paymentDraft ? paymentDraft.items.map((item, index) => ({
                lineNumber: index + 1,
                description: item.description,
                quantity: item.quantity,
                unitId: item.unitId,
                unit: item.unit,
                unitPrice: item.unitPrice,
                discountAmount: item.discountAmount,
                ivaRateId: item.ivaRateId,
                totalAmount: item.totalPrice,
                dueDate: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime()) 
                    ? new Date(formData.needByDateUtc).toISOString() 
                    : null,
                currencyId: currencies.find(c => c.code === paymentDraft.currency)?.id || safeCurrencyId,
                plantId: safePlantId,
                costCenterId: null,
                itemPriority: 'MEDIUM',
                itemCatalogId: item.itemCatalogId || null
            })) : Number(formData.requestTypeId) === 1 && requesterItems.length > 0 ? requesterItems.filter(ri => ri.description.trim()).map((ri, index) => ({
                lineNumber: index + 1,
                description: ri.description,
                quantity: ri.quantity,
                unitId: ri.unitId,
                // The backend resolves the unit from its CODE (RequestLineItemDto.Unit) — send it so the
                // item is created with a unit and passes the Phase-2 mandatory-unit rule. (unitId alone
                // is ignored by the backend bulk-add / validation, which key off the code.)
                unit: units.find(u => u.id === ri.unitId)?.code,
                unitPrice: 0,
                discountAmount: 0,
                ivaRateId: null,
                totalAmount: 0,
                dueDate: formData.needByDateUtc && !isNaN(new Date(formData.needByDateUtc).getTime())
                    ? new Date(formData.needByDateUtc).toISOString()
                    : null,
                currencyId: safeCurrencyId,
                plantId: safePlantId,
                costCenterId: null,
                itemPriority: 'MEDIUM',
                notes: ri.notes || null,
                itemCatalogId: ri.itemCatalogId || null
            })) : []
        };

        const isMultiDocumentPayment =
            featureFlags.paymentMultiDocumentEnabled && Number(formData.requestTypeId) === 2;

        try {
            let createdRequestId: string;

            if (isMultiDocumentPayment) {
                // ── Stages B→C: create the draft once, then persist each document and its items.
                // Never the other way round: a request must not reach an approver describing
                // documents that were never saved.
                const run = await creation.persist(
                    () => payload,
                    tempDocuments,
                    setTempDocuments,
                    materialiseAttachments);

                if (!run.requestId) {
                    setFeedback({ type: 'error', message: creation.error ?? 'Não foi possível criar o pedido.' });
                    setLoading(false);
                    return;
                }

                createdRequestId = run.requestId;

                if (!run.allDocumentsPersisted) {
                    // The request exists and some documents are saved. Say so plainly rather than
                    // navigating away and letting the user believe everything went through.
                    setFeedback({
                        type: 'error',
                        message: 'O pedido foi criado, mas nem todos os documentos foram guardados. ' +
                                 'Corrija os documentos assinalados e tente novamente — os que já foram ' +
                                 'guardados não serão duplicados.'
                    });
                    setLoading(false);
                    return;
                }
            } else {
                const result = await api.requests.create(payload);
                createdRequestId = result.id;
            }

            const result = { id: createdRequestId };

            // 1. Upload generic supporting documents
            if (attachments.length > 0) {
                await api.attachments.upload(result.id, attachments, 'SUPPORTING');
            }

            // 2. Upload OCR file correctly classified (Proforma for Payment requests).
            //    Skipped for multi-document payments: the source documents own their own
            //    attachments, and re-uploading here would duplicate the same file.
            if (ocrFile && !isMultiDocumentPayment) {
                const targetType = Number(formData.requestTypeId) === 2 ? 'PROFORMA' : 'SUPPORTING';
                await api.attachments.upload(result.id, [ocrFile], targetType);
            }

            const isQuotation = Number(formData.requestTypeId) === 1;
            const fallbackSuccessMessage = isQuotation ? 'Pedido de Cotação criado com sucesso.' : 'Rascunho salvo com sucesso.';

            if (isQuotation) {
                const quotationSuccessMessage = 'Pedido de Cotação criado e enviado para a Gestão de Cotações.';
                navigate(`/requests${location.state?.fromList || ''}`, {
                    replace: true,
                    state: { successMessage: quotationSuccessMessage }
                });
            } else {
                const isPayment = Number(formData.requestTypeId) === 2;
                const successMsg = isPayment ? 'Confira os dados do pedido e clique em "Submeter" para enviar para aprovação.' : fallbackSuccessMessage;

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
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-md)',
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
        ...(getFieldErrors(fieldName) ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2', boxShadow: '0 0 0 3px rgba(239,68,68,0.2)' } : {})
    });

    // Fire the items-section red pulse for ~5s; the discreet error border persists afterwards.
    const triggerItemsPulse = () => {
        if (itemsPulseTimer.current) window.clearTimeout(itemsPulseTimer.current);
        setItemsPulse(true);
        itemsPulseTimer.current = window.setTimeout(() => setItemsPulse(false), 5000);
    };
    useEffect(() => () => { if (itemsPulseTimer.current) window.clearTimeout(itemsPulseTimer.current); }, []);

    // Red styling for an individual invalid item field.
    const itemFieldStyle = (rowIdx: number, field: 'description' | 'quantity' | 'unitId') =>
        (itemRowErrors[rowIdx]?.[field] ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2', boxShadow: '0 0 0 2px rgba(239,68,68,0.2)' } : {});

    const headerProps = {
        breadcrumbs: [
            { label: 'Dashboard', to: '/' },
            { label: 'Pedidos', to: `/requests${location.state?.fromList || ''}` },
            { label: isCopyMode ? 'Cópia de Pedido' : 'Novo Rascunho' }
        ] as BreadcrumbItem[],
        title: isCopyMode ? 'Cópia de Pedido' : 'Novo Rascunho',
        secondaryActions: (
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                {!isCopyMode && (
                    <LiveGuideLauncher guideId="request-creation-live-guide" label="Ajuda para criar pedido" />
                )}
                <button
                    type="button"
                    onClick={() => navigate(`/requests${location.state?.fromList || ''}`)}
                    style={{
                        height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '1px solid var(--color-border)',
                        backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                        fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.75rem', color: 'var(--color-text-main)'
                    }}
                >
                    <X size={14} /> {isCopyMode ? 'DESCARTAR CÓPIA' : 'CANCELAR'}
                </button>
            </div>
        ),
        primaryActions: (
            <button
                data-guide="request-submit"
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

            {/* Error State: Failed to load user profile */}
            {!isScopeLoading && scopeError && (
                <div style={{
                    backgroundColor: '#FEF2F2', border: '2px solid #EF4444', padding: '24px', borderRadius: 'var(--radius-sm)',
                    boxShadow: 'var(--shadow-md)', display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'center', textAlign: 'center'
                }}>
                    <AlertCircle size={32} style={{ color: '#EF4444' }} />
                    <div style={{ color: '#EF4444', fontWeight: 800, fontSize: '1.1rem' }}>ERRO AO CARREGAR PERFIL</div>
                    <p style={{ color: 'var(--color-text-main)', fontSize: '0.875rem', maxWidth: '500px' }}>
                        {scopeError}
                    </p>
                    <div style={{ display: 'flex', gap: '12px', marginTop: '8px' }}>
                        <button 
                            type="button"
                            onClick={() => window.location.reload()}
                            style={{
                                padding: '8px 16px', backgroundColor: 'var(--color-primary)', color: 'white',
                                border: 'none', borderRadius: 'var(--radius-sm)', fontWeight: 700, cursor: 'pointer'
                            }}
                        >
                            <RefreshCw size={14} style={{ marginRight: '6px', verticalAlign: 'middle' }} />
                            TENTAR NOVAMENTE
                        </button>
                        <button 
                            type="button"
                            onClick={() => navigate('/')}
                            style={{
                                padding: '8px 16px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)',
                                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', fontWeight: 700, cursor: 'pointer'
                            }}
                        >
                            VOLTAR AO DASHBOARD
                        </button>
                    </div>
                </div>
            )}

            {/* Error State: Profile loaded OK but user has zero plant assignments */}
            {!isScopeLoading && !scopeError && allowedPlantCodes.length === 0 && (
                <div style={{
                    backgroundColor: '#FEF2F2', border: '2px solid #EF4444', padding: '24px', borderRadius: 'var(--radius-sm)',
                    boxShadow: 'var(--shadow-md)', display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'center', textAlign: 'center'
                }}>
                    <div style={{ color: '#EF4444', fontWeight: 800, fontSize: '1.25rem' }}>ACESSO RESTRITO</div>
                    <p style={{ color: 'var(--color-text-main)', fontSize: '0.875rem', maxWidth: '500px' }}>
                        O seu utilizador não possui nenhuma <strong>Planta</strong> atribuída ao seu âmbito de acesso. 
                        Por favor, contacte o Administrador do Sistema para configurar o seu perfil antes de tentar criar novos pedidos.
                    </p>
                    <button 
                        type="button"
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

            {/* Warning State: Auxiliary lookups failed but scope loaded OK */}
            {!isScopeLoading && !scopeError && lookupsError && allowedPlantCodes.length > 0 && (
                <div style={{
                    backgroundColor: '#FFFBEB', border: '2px solid #F59E0B', padding: '16px 24px', borderRadius: 'var(--radius-sm)',
                    boxShadow: 'var(--shadow-md)', display: 'flex', alignItems: 'center', gap: '12px'
                }}>
                    <AlertTriangle size={24} style={{ color: '#D97706', flexShrink: 0 }} />
                    <div style={{ flex: 1 }}>
                        <div style={{ color: '#92400E', fontWeight: 700, fontSize: '0.875rem' }}>Falha ao carregar dados auxiliares</div>
                        <p style={{ color: '#92400E', fontSize: '0.8rem', margin: '4px 0 0' }}>
                            {lookupsError}
                        </p>
                    </div>
                    <button 
                        type="button"
                        onClick={() => window.location.reload()}
                        style={{
                            padding: '6px 12px', backgroundColor: '#D97706', color: 'white',
                            border: 'none', borderRadius: 'var(--radius-sm)', fontWeight: 700, cursor: 'pointer', fontSize: '0.75rem'
                        }}
                    >
                        <RefreshCw size={12} style={{ marginRight: '4px', verticalAlign: 'middle' }} />
                        RECARREGAR
                    </button>
                </div>
            )}

            {/* Picker for source-document files, held until the request exists (Stage C). */}
            <input
                ref={sourceFileInputRef}
                type="file"
                accept=".pdf,.png,.jpg,.jpeg"
                onChange={handleSourceFileChosen}
                style={{ display: 'none' }}
            />

            <form 
                data-guide="request-form"
                onSubmit={handleSubmit} 
                style={{
                    display: (scopeError || allowedPlantCodes.length === 0) ? 'none' : 'flex',
                    flexDirection: 'column', gap: '32px', opacity: isScopeLoading ? 0.5 : 1, pointerEvents: isScopeLoading ? 'none' : 'auto'
                }}
            >
                <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-md)', border: '1px solid var(--color-border)' }}>
                    <h2 style={sectionTitleStyle}>Dados Gerais do Pedido</h2>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                        <label data-guide="request-title" style={labelStyle}>
                            Título do Pedido <span style={{ color: 'red' }}>*</span>
                            <input
                                required type="text" name="title" value={formData.title} onChange={handleChange}
                                placeholder="Ex: Aquisição de Laptops para TI" style={getInputStyle('Title')}
                            />
                            {renderFieldError('Title')}
                        </label>

                        <label data-guide="request-description" style={labelStyle}>
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

                        <div data-guide="request-documents" style={{ marginTop: '-8px', marginBottom: '8px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
                                <Paperclip size={16} style={{ color: 'var(--color-primary)' }} />
                                <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '0.025em' }}>
                                    Documentos de Apoio
                                </span>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 400 }}>(Opcional)</span>
                            </div>

                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'flex-start' }}>
                                <div style={{ 
                                    border: '1px solid var(--color-border)', padding: '8px 16px', textAlign: 'left', borderRadius: 'var(--radius-sm)',
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
                                                borderRadius: 'var(--radius-sm)', boxShadow: 'var(--shadow-sm)'
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

                         <label data-guide="request-type" style={labelStyle}>
                             Tipo de Pedido <span style={{ color: 'red' }}>*</span>
                             <select name="requestTypeId" value={formData.requestTypeId} onChange={handleChange} style={getInputStyle('RequestTypeId')}>
                                 <option value="">-- Selecione --</option>
                                 {requestTypes.filter(rt => rt.isActive).map(rt => (
                                     <option key={rt.id} value={rt.id}>{rt.name}</option>
                                 ))}
                             </select>
                             {renderFieldError('RequestTypeId')}
                         </label>

                         {/* Quotation Requester Items Section */}
                         <AnimatePresence>
                             {Number(formData.requestTypeId) === 1 && (
                                 <motion.div
                                     initial={{ opacity: 0, height: 0, overflow: 'hidden' }}
                                     animate={{ opacity: 1, height: 'auto', transitionEnd: { overflow: 'visible' } }}
                                     exit={{ opacity: 0, height: 0, overflow: 'hidden' }}
                                     transition={{ duration: 0.3 }}
                                 >
                                     <div data-guide="request-quotation-items-section" data-field="LineItems" tabIndex={-1}
                                         className={itemsPulse ? 'error-pulse' : undefined}
                                         style={{
                                         marginBottom: '16px',
                                         padding: '24px',
                                         backgroundColor: getFieldErrors('LineItems') ? '#FEF6F6' : 'var(--color-bg-surface)',
                                         border: getFieldErrors('LineItems') ? '1px solid #EF4444' : '1px solid var(--color-border)',
                                         borderRadius: 'var(--radius-sm)',
                                         boxShadow: getFieldErrors('LineItems') && !itemsPulse ? 'var(--shadow-sm)' : (itemsPulse ? 'none' : 'var(--shadow-sm)'),
                                         outline: 'none',
                                         scrollMarginTop: '150px'
                                     }}>
                                         <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
                                             <div>
                                                 <h3 style={{ fontSize: '0.875rem', fontWeight: 800, textTransform: 'uppercase', margin: 0, color: 'var(--color-primary)' }}>
                                                     Itens Solicitados
                                                 </h3>
                                                 <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', margin: '2px 0 0 0' }}>
                                                     Adicione os itens que pretende solicitar (catálogo ou manual)
                                                 </p>
                                             </div>
                                             <button
                                                 type="button"
                                                 onClick={handleAddRequesterItem}
                                                 style={{
                                                     display: 'inline-flex', alignItems: 'center', gap: '6px',
                                                     backgroundColor: 'var(--color-primary)', color: 'white',
                                                     border: 'none', padding: '8px 16px', borderRadius: '4px',
                                                     fontWeight: 800, fontSize: '0.75rem', cursor: 'pointer',
                                                     textTransform: 'uppercase'
                                                 }}
                                             >
                                                 <Plus size={14} /> Adicionar Item
                                             </button>
                                         </div>

                                         {getFieldErrors('LineItems') && (
                                             <div style={{
                                                 display: 'flex', alignItems: 'flex-start', gap: '8px',
                                                 backgroundColor: '#FEE2E2', border: '1px solid #EF4444',
                                                 borderRadius: '4px', padding: '10px 12px', marginBottom: '16px',
                                                 color: '#B91C1C', fontSize: '0.75rem', fontWeight: 600
                                             }}>
                                                 <AlertCircle size={16} style={{ flexShrink: 0, marginTop: '1px' }} />
                                                 <span>{getFieldErrors('LineItems')![0]}</span>
                                             </div>
                                         )}

                                         {requesterItems.length === 0 ? (
                                             <div style={{
                                                 textAlign: 'center', padding: '24px', color: 'var(--color-text-muted)',
                                                 border: '1px dashed var(--color-border)', borderRadius: 'var(--radius-sm)',
                                                 fontSize: '0.8rem'
                                             }}>
                                                 <p style={{ marginBottom: '8px', fontWeight: 600 }}>Nenhum item adicionado ainda.</p>
                                                 <p style={{ fontSize: '0.75rem' }}>Clique em "Adicionar Item" para especificar os materiais ou serviços necessários.</p>
                                             </div>
                                         ) : (
                                             <div style={{ overflow: 'visible', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                                                 <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.75rem' }}>
                                                     <thead>
                                                         <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '1px solid var(--color-border)' }}>
                                                             <th style={{ padding: '8px', textAlign: 'center', width: '30px', fontWeight: 800 }}>#</th>
                                                             <th style={{ padding: '8px', textAlign: 'left', fontWeight: 800 }}>DESCRIÇÃO / ITEM DO CATÁLOGO</th>
                                                             <th style={{ padding: '8px', textAlign: 'center', width: '100px', fontWeight: 800 }}>UNID.</th>
                                                             <th style={{ padding: '8px', textAlign: 'center', width: '80px', fontWeight: 800 }}>QTD</th>
                                                             <th style={{ padding: '8px', textAlign: 'left', width: '180px', fontWeight: 800 }}>NOTAS</th>
                                                             <th style={{ padding: '8px', textAlign: 'center', width: '40px' }}></th>
                                                         </tr>
                                                     </thead>
                                                     <tbody>
                                                         {requesterItems.map((item, idx) => (
                                                             <tr key={idx} style={{ borderBottom: '1px solid var(--color-border-light)' }}>
                                                                 <td style={{ padding: '4px 8px', textAlign: 'center', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                                                                     {idx + 1}
                                                                 </td>
                                                                 <td data-item-row={idx} data-item-field="description" style={{ padding: '4px 8px', verticalAlign: 'top' }}>
                                                                     <CatalogItemAutocomplete
                                                                         value={item.itemCatalogCode ? `[${item.itemCatalogCode}] ${item.description}` : item.description}
                                                                         itemCatalogId={item.itemCatalogId}
                                                                         onChange={(desc, catId, catCode, defaultUnitId) => handleCatalogSelectRequesterItem(idx, desc, catId, catCode, defaultUnitId)}
                                                                         placeholder="Pesquisar item do catálogo ou digitar descrição..."
                                                                         style={{ padding: '6px 8px', marginTop: 0, ...itemFieldStyle(idx, 'description') }}
                                                                     />
                                                                     {itemRowErrors[idx]?.description && (
                                                                         <div style={{ color: '#B91C1C', fontSize: '0.68rem', fontWeight: 600, marginTop: '3px' }}>Informe a descrição.</div>
                                                                     )}
                                                                 </td>
                                                                 <td data-item-row={idx} data-item-field="unitId" style={{ padding: '4px 8px', verticalAlign: 'top' }}>
                                                                     <select
                                                                         value={item.unitId || ''}
                                                                         onChange={(e) => handleUpdateRequesterItem(idx, 'unitId', e.target.value ? Number(e.target.value) : null)}
                                                                         style={{ ...inputStyle, padding: '6px 8px', marginTop: 0, textAlign: 'center', ...itemFieldStyle(idx, 'unitId') }}
                                                                     >
                                                                         <option value="">—</option>
                                                                         {units.filter(u => u.isActive !== false || u.id === item.unitId).map(u => (
                                                                             <option key={u.id} value={u.id}>{u.code}</option>
                                                                         ))}
                                                                     </select>
                                                                     {itemRowErrors[idx]?.unitId && (
                                                                         <div style={{ color: '#B91C1C', fontSize: '0.68rem', fontWeight: 600, marginTop: '3px' }}>Selecione a unidade.</div>
                                                                     )}
                                                                 </td>
                                                                 <td data-item-row={idx} data-item-field="quantity" style={{ padding: '4px 8px', verticalAlign: 'top' }}>
                                                                     <input
                                                                         type="number"
                                                                         min={0}
                                                                         value={item.quantity}
                                                                         onChange={(e) => handleUpdateRequesterItem(idx, 'quantity', Number(e.target.value))}
                                                                         style={{ ...inputStyle, padding: '6px 8px', marginTop: 0, textAlign: 'center', ...itemFieldStyle(idx, 'quantity') }}
                                                                     />
                                                                     {itemRowErrors[idx]?.quantity && (
                                                                         <div style={{ color: '#B91C1C', fontSize: '0.68rem', fontWeight: 600, marginTop: '3px' }}>A quantidade deve ser maior que zero.</div>
                                                                     )}
                                                                 </td>
                                                                 <td style={{ padding: '4px 8px' }}>
                                                                     <input
                                                                         type="text"
                                                                         value={item.notes}
                                                                         onChange={(e) => handleUpdateRequesterItem(idx, 'notes', e.target.value)}
                                                                         placeholder="Observações..."
                                                                         style={{ ...inputStyle, padding: '6px 8px', marginTop: 0 }}
                                                                     />
                                                                 </td>
                                                                 <td style={{ padding: '4px 8px', textAlign: 'center' }}>
                                                                     <button type="button" onClick={() => handleRemoveRequesterItem(idx)} style={{ color: '#EF4444', background: 'none', border: 'none', cursor: 'pointer' }}>
                                                                         <Trash2 size={14} />
                                                                     </button>
                                                                 </td>
                                                             </tr>
                                                         ))}
                                                     </tbody>
                                                 </table>
                                             </div>
                                         )}
                                     </div>
                                 </motion.div>
                             )}
                         </AnimatePresence>

                         <AnimatePresence>
                             {Number(formData.requestTypeId) === 2 && (
                                 <motion.div
                                     initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                                     style={{ position: 'relative' }}
                                 >
                                     <div data-guide="request-payment-document-section" style={{ 
                                         marginBottom: '32px', padding: '24px', backgroundColor: 'var(--color-bg-surface)', 
                                         border: '1px solid var(--color-border)',  borderRadius: 'var(--radius-sm)', boxShadow: 'var(--shadow-sm)',
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
                                                          borderRadius: 'var(--radius-sm)', marginBottom: '16px',
                                                          boxShadow: '2px 2px 0 #F0ABFC'
                                                      }}>
                                                          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#86198F' }}>
                                                              <div style={{ backgroundColor: '#FAE8FF', padding: '6px', borderRadius: '50%', display: 'flex' }}>
                                                                <Edit2 size={16} />
                                                              </div>
                                                              <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                                <span style={{ fontSize: '0.8rem', fontWeight: 800, letterSpacing: '0.025em' }}>INSCRIÇÃO MANUAL DA FATURA</span>
                                                                <span style={{ fontSize: '0.7rem', opacity: 0.8, fontWeight: 600 }}>Arquivo: {ocrFile?.name}</span>
                                                              </div>
                                                          </div>
                                                          <button 
                                                             type="button"
                                                             onClick={() => { setPaymentDraft(null); setOcrFile(null); setIsManualOcr(false); }}
                                                             style={{ 
                                                                display: 'flex', alignItems: 'center', gap: '6px',
                                                                fontSize: '0.75rem', fontWeight: 900, color: '#86198F', 
                                                                backgroundColor: 'white', border: '2px solid #F0ABFC', 
                                                                padding: '6px 12px', borderRadius: '6px', cursor: 'pointer',
                                                                boxShadow: '2px 2px 0 #F0ABFC', transition: 'all 0.1s ease',
                                                                textTransform: 'uppercase'
                                                             }}
                                                             onMouseOver={(e) => { e.currentTarget.style.transform = 'translate(-1px, -1px)'; e.currentTarget.style.boxShadow = '3px 3px 0 #F0ABFC'; }}
                                                             onMouseOut={(e) => { e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = '2px 2px 0 #F0ABFC'; }}
                                                          >
                                                              <ArrowLeft size={14} /> VOLTAR / TROCAR DOCUMENTO
                                                          </button>
                                                      </div>
                                                  ) : (
                                                      <div style={{ 
                                                          display: 'flex', justifyContent: 'space-between', alignItems: 'center', 
                                                          padding: '12px 16px', backgroundColor: '#F0FDF4', border: '1px solid #BBF7D0', 
                                                          borderRadius: 'var(--radius-sm)', marginBottom: '16px',
                                                          boxShadow: '2px 2px 0 #BBF7D0'
                                                      }}>
                                                          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#166534' }}>
                                                              <div style={{ backgroundColor: '#DCFCE7', padding: '6px', borderRadius: '50%', display: 'flex' }}>
                                                                <CheckCircle2 size={16} />
                                                              </div>
                                                              <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                                <span style={{ fontSize: '0.8rem', fontWeight: 800, letterSpacing: '0.025em' }}>DADOS EXTRAÍDOS COM SUCESSO via OCR</span>
                                                                <span style={{ fontSize: '0.7rem', opacity: 0.8, fontWeight: 600 }}>Arquivo: {ocrFile?.name}</span>
                                                              </div>
                                                          </div>
                                                          <button 
                                                             type="button"
                                                             onClick={() => { setPaymentDraft(null); setOcrFile(null); setIsManualOcr(false); }}
                                                             style={{ 
                                                                display: 'flex', alignItems: 'center', gap: '6px',
                                                                fontSize: '0.75rem', fontWeight: 900, color: '#166534', 
                                                                backgroundColor: 'white', border: '2px solid #BBF7D0', 
                                                                padding: '6px 12px', borderRadius: '6px', cursor: 'pointer',
                                                                boxShadow: '2px 2px 0 #BBF7D0', transition: 'all 0.1s ease',
                                                                textTransform: 'uppercase'
                                                             }}
                                                             onMouseOver={(e) => { e.currentTarget.style.transform = 'translate(-1px, -1px)'; e.currentTarget.style.boxShadow = '3px 3px 0 #BBF7D0'; }}
                                                             onMouseOut={(e) => { e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = '2px 2px 0 #BBF7D0'; }}
                                                          >
                                                              <ArrowLeft size={14} /> VOLTAR / TROCAR DOCUMENTO
                                                          </button>
                                                      </div>
                                                  )}

                                                 <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px', marginBottom: '24px' }}>
                                                      <div style={{ ...labelStyle, marginBottom: 0, gridColumn: '1 / -1' }}>
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
                                                                  style={{ marginTop: '8px', padding: '12px', backgroundColor: '#fff7ed', border: '2px solid #fdba74', borderRadius: 'var(--radius-sm)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: 'var(--shadow-sm)' }}
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
                                                      </div>
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
                                                         <input type="number" value={(paymentDraft.items || []).reduce((sum, item) => sum + (((item?.quantity || 0) * (item?.unitPrice || 0)) - (item?.discountAmount || 0)), 0)} disabled style={{ ...inputStyle, backgroundColor: 'var(--color-bg-page)' }} />
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
                                                                    <th style={{ padding: '8px', width: '30px', textAlign: 'center' }}></th>
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
                                                                        <td colSpan={9} style={{ padding: '32px 16px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
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
                                                                        <tr key={idx} style={{ 
                                                                            borderBottom: '1px solid var(--color-border-light)', 
                                                                            backgroundColor: item.isChecked ? '#ECFDF5' : 'transparent',
                                                                            transition: 'background-color 0.2s ease'
                                                                        }}>
                                                                            <td style={{ padding: '4px 8px', textAlign: 'center' }}>
                                                                                <input 
                                                                                    type="checkbox" 
                                                                                    checked={item.isChecked || false} 
                                                                                    onChange={(e) => handleUpdateOcrItem(idx, 'isChecked', e.target.checked)}
                                                                                    style={{ cursor: 'pointer', width: '16px', height: '16px', accentColor: '#10B981', marginTop: '4px' }}
                                                                                />
                                                                            </td>
                                                                            <td style={{ padding: '4px 8px' }}>
                                                                                <CatalogItemAutocomplete
                                                                                    value={item.itemCatalogCode ? `[${item.itemCatalogCode}] ${item.description}` : (item.description || '')}
                                                                                    itemCatalogId={item.itemCatalogId || null}
                                                                                    onChange={(desc, catId, catCode, defaultUnitId) => handleCatalogSelectOcrItem(idx, desc, catId, catCode, defaultUnitId)}
                                                                                    placeholder="Pesquisar item do catálogo ou digitar descrição..."
                                                                                    style={{ padding: '6px 8px', marginTop: 0 }}
                                                                                />
                                                                                {/* Auto-match status badge */}
                                                                                {item.autoMatchStatus === 'AUTO_MATCHED' && (
                                                                                    <div style={{
                                                                                        display: 'flex', alignItems: 'center', gap: '4px',
                                                                                        fontSize: '0.65rem', fontWeight: 600, color: '#059669',
                                                                                        backgroundColor: '#ECFDF5', border: '1px solid #A7F3D0',
                                                                                        borderRadius: 'var(--radius-sm)', padding: '2px 6px', marginTop: '3px',
                                                                                        width: 'fit-content'
                                                                                    }}>
                                                                                        <CheckCircle2 size={11} />
                                                                                        Correspondência automática{item.itemCatalogCode ? ` — ${item.itemCatalogCode}` : ''}
                                                                                    </div>
                                                                                )}
                                                                                {item.autoMatchStatus === 'NEEDS_REVIEW' && !item.itemCatalogId && (
                                                                                    <div style={{
                                                                                        display: 'flex', alignItems: 'center', gap: '4px',
                                                                                        fontSize: '0.65rem', fontWeight: 600, color: '#D97706',
                                                                                        backgroundColor: '#FFFBEB', border: '1px solid #FDE68A',
                                                                                        borderRadius: 'var(--radius-sm)', padding: '2px 6px', marginTop: '3px',
                                                                                        width: 'fit-content'
                                                                                    }}>
                                                                                        <AlertCircle size={11} />
                                                                                        Item não catalogado — verifique manualmente
                                                                                    </div>
                                                                                )}
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
                                                                                 {units.filter(u => u.isActive !== false || u.id === item.unitId).map(u => <option key={u.id} value={u.id}>{u.code}</option>)}
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
                                                                            TOTAL ABATIMENTOS ITENS ({String(paymentDraft.currency || '')}):
                                                                        </td>
                                                                        <td style={{ padding: '8px 16px', textAlign: 'right', fontSize: '0.8rem', fontWeight: 800, color: '#dc2626' }}>
                                                                            -{totalDiscount.toLocaleString('pt-PT', { minimumFractionDigits: 2 })}
                                                                        </td>
                                                                        <td></td>
                                                                    </tr>
                                                                ) : null;
                                                            })()}
                                                            <tr style={{ backgroundColor: '#fef2f2', borderBottom: '1px solid #fecaca' }}>
                                                                <td colSpan={6} style={{ padding: '8px 16px', textAlign: 'right', fontSize: '0.75rem', fontWeight: 700, color: '#dc2626' }}>
                                                                    DESCONTO GLOBAL ({String(paymentDraft.currency || '')}):
                                                                </td>
                                                                <td style={{ padding: '4px 16px', textAlign: 'right' }}>
                                                                    <input 
                                                                        type="number" 
                                                                        min="0"
                                                                        step="0.01"
                                                                        value={paymentDraft.discountAmount || ''}
                                                                        onChange={(e) => handleUpdateOcrDraft('discountAmount', parseFloat(e.target.value) || 0)}
                                                                        style={{ width: '100px', padding: '4px', textAlign: 'right', border: '1px solid #fca5a5', borderRadius: '4px', fontSize: '0.85rem', fontWeight: 800, color: '#dc2626', backgroundColor: 'var(--color-bg-surface)', float: 'right' }}
                                                                    />
                                                                </td>
                                                                <td></td>
                                                            </tr>
                                                             <tr style={{ backgroundColor: 'var(--color-bg-page)', fontWeight: 800 }}>
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

                        {/* Release 3: PAYMENT composed as a collection of source documents. Each
                            card is the SAME component the edit screen uses — creation and editing
                            must not grow two visual implementations that drift apart. */}
                        {featureFlags.paymentMultiDocumentEnabled && Number(formData.requestTypeId) === 2 && (
                            <div style={{ marginBottom: '24px' }}>
                                <PaymentSourceDocumentDraftCollection
                                    documents={tempDocuments}
                                    onChange={setTempDocuments}
                                    suppliers={[]}
                                    plants={filteredPlants.map(p => ({ id: p.id, name: p.name }))}
                                    currencies={currencies.map(c => ({ code: c.code, name: c.symbol || c.code }))}
                                    disabled={creation.phase === 'CREATING_REQUEST' ||
                                              creation.phase === 'SAVING_DOCUMENTS' ||
                                              creation.phase === 'SAVING_ITEMS'}
                                    onPickFile={pickSourceDocumentFile}
                                    ocrStateFor={documentOcr.stateFor}
                                    discrepanciesFor={documentOcr.discrepanciesFor}
                                    onRunOcr={runDocumentOcr}
                                    onResetOcr={documentOcr.forget}
                                />

                                {creation.phase !== 'NOT_STARTED' && (
                                    <p style={{
                                        marginTop: '10px', fontSize: '0.8rem', fontWeight: 600,
                                        color: creation.phase === 'PARTIAL_FAILURE'
                                            ? '#b91c1c' : 'var(--color-text-muted)'
                                    }}>
                                        {PHASE_LABEL[creation.phase]}
                                    </p>
                                )}

                                {creation.failures.length > 0 && (
                                    <ul role="alert" style={{
                                        margin: '8px 0 0', paddingLeft: '18px',
                                        fontSize: '0.78rem', color: '#b91c1c', fontWeight: 600
                                    }}>
                                        {creation.failures.map(f => (
                                            <li key={f.tempId}>{f.label}: {f.message}</li>
                                        ))}
                                    </ul>
                                )}
                            </div>
                        )}

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '24px' }}>
                            {/* Post-Payment Completion (Release 2) — PAYMENT only, feature-gated.
                                Sits beside the other header fields so the classification is made
                                while the requester still has the document in front of them. */}
                            {/* Multi-document OFF: the single classification field, unchanged. When
                                the flag is ON the classification belongs to each document card
                                below, so this field would be a second, contradictory source. */}
                            {featureFlags.postPaymentCompletionEnabled &&
                             !featureFlags.paymentMultiDocumentEnabled &&
                             Number(formData.requestTypeId) === 2 && (
                                <SourceDocumentTypeField
                                    data-guide="request-source-document-type"
                                    context="PAYMENT_REQUEST"
                                    value={formData.sourceDocumentType}
                                    onChange={(val) => {
                                        setFormData(prev => ({ ...prev, sourceDocumentType: val }));
                                        clearFieldError('sourceDocumentType');
                                    }}
                                    ocr={paymentDraft?.documentClassification ?? null}
                                    conflict={classificationConflict}
                                    onConflictChange={setClassificationConflict}
                                    required={featureFlags.sourceDocumentTypeRequired}
                                    error={getFieldErrors('sourceDocumentType')?.[0] ?? null}
                                    labelStyle={labelStyle}
                                    inputStyle={getInputStyle('sourceDocumentType')}
                                />
                            )}

                            <label data-guide="request-need-level" style={labelStyle}>
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
                                        <label data-guide="request-needed-by" style={labelStyle}>
                                            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                                                {Number(formData.requestTypeId) === 2 ? 'Data de vencimento' : 'Necessário até (Data limite)'} <span style={{ color: 'red' }}>*</span>
                                                {/* Contextual, not corrective: the date is legal, it is simply already past.
                                                    An icon keeps the field's height fixed while the user edits the date. */}
                                                {!getFieldErrors('NeedByDateUtc') && !isBelowMinNeedByDate && formData.needByDateUtc && new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0) && (
                                                    <FieldMessageIcon
                                                        severity="warning"
                                                        tooltip={Number(formData.requestTypeId) === 2
                                                            ? 'O documento está vencido. Clique para saber o que isso significa.'
                                                            : 'A data selecionada está no passado. Clique para saber mais.'}
                                                        title={Number(formData.requestTypeId) === 2
                                                            ? 'O documento está vencido'
                                                            : 'A data selecionada está no passado'}
                                                        maxWidth={520}
                                                    >
                                                        <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-main)' }}>
                                                            {Number(formData.requestTypeId) === 2
                                                                ? 'A data de vencimento indicada já passou, pelo que o documento anexado está vencido. O pedido pode continuar a ser criado e submetido, mas poderá ser necessário obter um documento atualizado junto do fornecedor, e o pagamento pode implicar juros ou penalizações. Verifique a data no documento antes de prosseguir.'
                                                                : 'A data limite indicada já passou. O pedido pode continuar a ser criado, mas o prazo pedido não é realizável — confirme se a data está correta.'}
                                                        </p>
                                                    </FieldMessageIcon>
                                                )}
                                            </span>
                                            <DateInput
                                                required name="needByDateUtc" value={formData.needByDateUtc}
                                                min={minNeedByDate ?? undefined}
                                                onChange={(val) => {
                                                    setFormData(prev => ({ ...prev, needByDateUtc: val }));
                                                    clearFieldError('NeedByDateUtc');
                                                    setNeedByAdjustmentNotice(null);
                                                }}
                                                hasError={!!getFieldErrors('NeedByDateUtc') || isBelowMinNeedByDate}
                                                style={getInputStyle('NeedByDateUtc')}
                                            />
                                            {renderFieldError('NeedByDateUtc')}
                                            {!getFieldErrors('NeedByDateUtc') && isBelowMinNeedByDate && selectedNeedLevel && minNeedByDate && (
                                                <div style={{ color: '#EF4444', fontSize: '0.75rem', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600 }}>
                                                    <AlertCircle size={12} />
                                                    {getMinNeedByError(selectedNeedLevel.name, minNeedByDate)}
                                                </div>
                                            )}
                                            {!getFieldErrors('NeedByDateUtc') && !isBelowMinNeedByDate && needByAdjustmentNotice && (
                                                <div style={{ color: '#D97706', fontSize: '0.75rem', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600 }}>
                                                    <AlertTriangle size={12} />
                                                    {needByAdjustmentNotice}
                                                </div>
                                            )}
                                            {!getFieldErrors('NeedByDateUtc') && !isBelowMinNeedByDate && !needByAdjustmentNotice && minNeedByDate && minNeedByLeadDays !== null && selectedNeedLevel && (
                                                <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: '4px' }}>
                                                    {getMinNeedByHint(selectedNeedLevel.name, minNeedByDate, minNeedByLeadDays)}
                                                </div>
                                            )}
                                        </label>
                                    </motion.div>
                                )}
                            </AnimatePresence>

                            <label data-guide="request-department" style={labelStyle}>
                                Departamento <span style={{ color: 'red' }}>*</span>
                                <select name="departmentId" value={formData.departmentId} onChange={handleChange} style={getInputStyle('DepartmentId')}>
                                    <option value="">-- Selecione --</option>
                                    {filteredDepartments.filter(d => d.isActive).map(d => (
                                        <option key={d.id} value={d.id}>{d.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('DepartmentId')}
                            </label>

                             <label data-guide="request-company" style={labelStyle}>
                                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                                    Empresa <span style={{ color: 'red' }}>*</span>
                                    {/* Provenance, not a warning — and provenance never justifies moving the form. */}
                                    {paymentDraft?.isCompanyOcrAutoFilled && (
                                        <FieldMessageIcon
                                            severity="success"
                                            tooltip="Preenchido automaticamente a partir do documento. Clique para ver a origem."
                                            title="Empresa identificada automaticamente"
                                            maxWidth={520}
                                        >
                                            <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-main)' }}>
                                                A empresa foi preenchida automaticamente a partir do documento anexado, onde
                                                foi identificada como <strong>{paymentDraft.extractedCompanyName}</strong>.
                                            </p>
                                            <p style={{ margin: '10px 0 0', fontSize: '0.8125rem', lineHeight: 1.55, color: 'var(--color-text-muted)' }}>
                                                Trata-se de uma sugestão da leitura do documento, não de uma confirmação.
                                                Pode alterá-la se a empresa faturada for outra — o valor que ficar
                                                selecionado é o que será usado no pedido.
                                            </p>
                                        </FieldMessageIcon>
                                    )}
                                </span>
                                <select
                                    name="companyId" value={formData.companyId} onChange={handleChange} style={getInputStyle('CompanyId')}
                                    disabled={filteredCompanies.length <= 1}
                                >
                                    <option value="">-- Selecione --</option>
                                    {filteredCompanies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                                </select>
                                {renderFieldError('CompanyId')}
                            </label>

                            <label data-guide="request-plant" style={labelStyle}>
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
                mode="PAYMENT_OCR"
                extractedName={quickSupplierModal.initialName}
                extractedTaxId={quickSupplierModal.initialTaxId}
                onSuccess={(s) => {
                    handleUpdateOcrDraft('supplierId', s.id);
                    handleUpdateOcrDraft('supplierNameSnapshot', s.name);
                    handleUpdateOcrDraft('supplierPortalCode', s.portalCode || '');
                    handleUpdateOcrDraft('supplierRegistrationStatus', 'DRAFT');
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
                                
                                <div style={{ backgroundColor: 'var(--color-bg-page)', padding: '16px', borderRadius: '8px', fontSize: '0.875rem', color: '#4b5563', marginBottom: '24px' }}>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Pedido Vinculado:</span> {duplicateWarning.requestNumber}</p>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Enviado por:</span> {duplicateWarning.uploadedBy || 'Desconhecido'}</p>
                                    <p><span style={{ fontWeight: 600, color: '#374151' }}>Enviado em:</span> {duplicateWarning.createdAtUtc ? formatDateTime(duplicateWarning.createdAtUtc) : '-'}</p>
                                </div>

                                <div style={{ display: 'flex', gap: '12px', marginTop: '24px' }}>
                                    <button
                                        type="button"
                                        onClick={() => setDuplicateWarning(null)}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: '#374151', backgroundColor: 'white', border: '1px solid var(--color-border)', borderRadius: '8px', cursor: 'pointer' }}
                                    >
                                        Cancelar Envio
                                    </button>
                                    <button
                                        type="button"
                                        disabled={dupCountdown > 0}
                                        onClick={() => {
                                            if (duplicateWarning?.uploadCallback) {
                                                duplicateWarning.uploadCallback();
                                            }
                                        }}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: 'white', backgroundColor: '#d97706', border: 'none', borderRadius: '8px', cursor: dupCountdown > 0 ? 'not-allowed' : 'pointer', opacity: dupCountdown > 0 ? 0.6 : 1, transition: 'opacity 0.3s ease' }}
                                    >
                                        {dupCountdown > 0 ? `Estou Ciente, Prosseguir (${dupCountdown})` : 'Estou Ciente, Prosseguir'}
                                    </button>
                                </div>
                            </div>
                        </motion.div>
                    </div>
                )}
            </AnimatePresence>

            {/* Reconciliation Modal */}
            <CatalogItemReconciliationModal
                isOpen={reconciliation.isModalOpen}
                onClose={reconciliation.closeModal}
                classifiedItems={reconciliation.classifiedItems}
                onResolveAll={(resolutions: ItemResolution[]) => {
                    reconciliation.resolveAll(resolutions);
                    // Apply resolutions back to source items
                    resolutions.forEach(r => {
                        if (r.linkedCatalogId) {
                            if (Number(formData.requestTypeId) === 2 && paymentDraft) {
                                handleCatalogSelectOcrItem(
                                    r.itemIndex,
                                    r.linkedDescription || paymentDraft.items[r.itemIndex]?.description || '',
                                    r.linkedCatalogId,
                                    r.linkedCatalogCode || null,
                                    r.defaultUnitId || null
                                );
                            } else {
                                handleCatalogSelectRequesterItem(
                                    r.itemIndex,
                                    r.linkedDescription || requesterItems[r.itemIndex]?.description || '',
                                    r.linkedCatalogId,
                                    r.linkedCatalogCode || null,
                                    r.defaultUnitId || null
                                );
                            }
                        }
                    });
                }}
            />

            {/* Reconciliation Warning Dialog */}
            <ReconciliationWarningDialog
                isOpen={showReconciliationWarning}
                unresolvedCount={reconciliation.unresolvedCount}
                onReviewItems={() => {
                    setShowReconciliationWarning(false);
                    reconciliation.openModal();
                }}
                onCancel={() => {
                    setShowReconciliationWarning(false);
                    setLoading(false);
                }}
            />
        </motion.div >
    );
}
