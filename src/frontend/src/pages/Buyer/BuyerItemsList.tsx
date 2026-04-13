import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';

import { Link, useLocation, useSearchParams, useNavigate } from 'react-router-dom';
import { ChevronDown, ChevronRight, Plus, Upload, ExternalLink, Search, Filter, FileText, CheckCircle2, X, Pencil, Trash2, ArrowLeft, AlertCircle, RefreshCcw, Hash, Calendar, UserPlus, AlertTriangle, BookOpen } from 'lucide-react';
import { useAuth } from '../../features/auth/AuthContext';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO, formatDate, getUrgencyStyle, computeFileHash, formatDateTime } from '../../lib/utils';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { completeQuotationAction } from '../../lib/workflow';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { QuickCurrencyModal } from '../../components/Buyer/QuickCurrencyModal';
import { Tooltip } from '../../components/ui/Tooltip';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../../components/ui/DropdownPortal';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { SavedQuotationDto, IvaRate, Unit, OcrDraft, OcrDraftItem, ReconciliationBatchDto } from '../../types';
import { useOcrProcessor } from '../../hooks/useOcrProcessor';
import { ReconciliationPanel } from '../../components/Buyer/ReconciliationPanel';
import { RequestDrawerPresentation } from '../Requests/components/modern/RequestDrawerPresentation';


const ALLOWED_EXTENSIONS = ['pdf', 'jpg', 'jpeg', 'png', 'doc', 'docx', 'xls', 'xlsx'];
const ALLOWED_EXTENSIONS_MSG = "PDF, JPG, JPEG, PNG, DOC, DOCX, XLS e XLSX";
 
 // Step 9: Highlight animation
 const highlightStyles = `
 @keyframes sectionHighlight {
   0% { outline: 2px solid transparent; background-color: transparent; }
   15% { outline: 3px solid #ef4444; background-color: #fef2f2; }
   100% { outline: 2px solid transparent; background-color: transparent; }
 }
 .section-attention-highlight {
   animation: sectionHighlight 5s ease-out;
 }
 `;

// Interfaces for Lookups moved to types/index.ts

type QuotationDraftItem = OcrDraftItem;
type QuotationDraft = OcrDraft;


export function BuyerItemsList() {
    const [searchParams, setSearchParams] = useSearchParams();
    const navigate = useNavigate();

    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [statuses, setStatuses] = useState<any[]>([]);
    const mode = 'BUYER'; // Standardized as Buyer-only Workspace

    const [expandedRequests, setExpandedRequests] = useState<Set<string>>(new Set());
    const [deleteConfirm, setDeleteConfirm] = useState<{ attachmentId: string; fileName: string } | null>(null);
    const [addQuotationMode, setAddQuotationMode] = useState<Record<string, 'UPLOAD' | 'MANUAL' | null>>({});
    
    // Step 2 & 3: Quotation Flow States
    const [importSelectedFiles, setImportSelectedFiles] = useState<Record<string, File[]>>({});
    const [quotationFlowStep, setQuotationFlowStep] = useState<Record<string, 'SELECT_MODE' | 'EDIT_QUOTATION'>>({});
    const [ocrResults, setOcrResults] = useState<Record<string, any>>({});
    const [quotationDrafts, setQuotationDrafts] = useState<Record<string, QuotationDraft>>({});
    const [draftProformaFiles, setDraftProformaFiles] = useState<Record<string, File>>({});
    const [isSaving, setIsSaving] = useState(false);
    const [isProcessingOcr, setIsProcessingOcr] = useState<Record<string, boolean>>({});
    const [ocrErrors, setOcrErrors] = useState<Record<string, string | null>>({});
    const [reconciliationBatches, setReconciliationBatches] = useState<Record<string, ReconciliationBatchDto>>({});
    const [editingQuotationId, setEditingQuotationId] = useState<Record<string, string | null>>({});
    const [highlightedRequestId, setHighlightedRequestId] = useState<string | null>(null);
    const [formErrors, setFormErrors] = useState<Record<string, Record<string, string>>>({});
    

    // Approval Modal State
    const [showApprovalModal, setShowApprovalModal] = useState<{
        show: boolean,
        type: ApprovalActionType,
        requestId: string | null,
        itemId: string | null,
        itemDescription: string | null,
        newStatusCode: string | null,
        hasProforma: boolean,
        hasSupplier: boolean,
        itemsCount: number,
        isLastItem: boolean,
        quotationCount: number,
        hasCompleteQuotation: boolean
    }>({ 
        show: false, 
        type: null, 
        requestId: null, 
        itemId: null, 
        itemDescription: null, 
        newStatusCode: null, 
        hasProforma: false, 
        hasSupplier: false, 
        itemsCount: 0,
        isLastItem: false,
        quotationCount: 0,
        hasCompleteQuotation: false
    });
    const [approvalComment, setApprovalComment] = useState('');
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });

    // Quick Supplier Modal State
    const [quickSupplierModal, setQuickSupplierModal] = useState<{ show: boolean; requestId: string | null; initialName: string; initialTaxId: string }>({ show: false, requestId: null, initialName: '', initialTaxId: '' });
    
    // Quick Currency Modal State
    const [quickCurrencyModal, setQuickCurrencyModal] = useState<{ show: boolean; requestId: string | null; initialCode: string }>({ show: false, requestId: null, initialCode: '' });

    // Duplicate Supplier Flow State
    const [duplicateSupplierModal, setDuplicateSupplierModal] = useState<{ show: boolean, requestId: string, draft: QuotationDraft | null, duplicate: SavedQuotationDto | null }>({
        show: false, requestId: '', draft: null, duplicate: null
    });
    const [expandedQuotations, setExpandedQuotations] = useState<Record<string, boolean>>({});
    const [drawerRequestId, setDrawerRequestId] = useState<string | null>(null);
    const [showHelpModal, setShowHelpModal] = useState(false);
    const [fileDuplicateWarning, setFileDuplicateWarning] = useState<{
        isOpen: boolean;
        requestId: string;
        fileName: string;
        requestNumber: string;
        uploadedBy?: string;
        createdAtUtc?: string;
        uploadCallback: () => void;
    } | null>(null);

    // Financial Integrity Gate Modal State
    const [integrityModal, setIntegrityModal] = useState<{
        show: boolean;
        requestId: string;
        itemsCount: number;
        ocrOriginalTotal: number;
        quotationTotal: number;
        varianceAmount: number;
        variancePercent: number;
        toleranceApplied: number;
        unresolvedReconciliationCount: number;
        detail: string;
    } | null>(null);
    const [integrityJustification, setIntegrityJustification] = useState('');
    const [integrityOverrideLoading, setIntegrityOverrideLoading] = useState(false);

    // Lookups
    const [ivaRates, setIvaRates] = useState<IvaRate[]>([]);
    const [units, setUnits] = useState<Unit[]>([]);
    const [currencies, setCurrencies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);

    // Bind state to URL parameters
    const searchTerm = searchParams.get('search') || '';
    const itemStatus = searchParams.get('itemStatus') || '';
    const requestStatus = searchParams.get('requestStatus') || '';
    const owner = searchParams.get('owner') || 'todos';
    const page = Number(searchParams.get('page')) || 1;
    const pageSize = Number(searchParams.get('pageSize')) || 20;

    const [searchInput, setSearchInput] = useState(searchTerm);
    const [totalCount, setTotalCount] = useState(0);

    const { mapOcrResultToDraft, calculateItemTotal, calculateDraftTotal } = useOcrProcessor(ivaRates, units, currencies);
    const { user: currentUser } = useAuth();

    const location = useLocation();
    const locationState = location.state as { successMessage?: string, fromList?: string } | null;

    useEffect(() => {
        if (locationState?.successMessage) {
            setFeedback({ type: 'success', message: locationState.successMessage });
            // Clear location state
            window.history.replaceState({}, document.title)
        }

        const focusId = searchParams.get('highlightRequestId');
        if (focusId) {
            setHighlightedRequestId(focusId);
            setExpandedRequests(prev => {
                const newSet = new Set(prev);
                newSet.add(focusId);
                return newSet;
            });
            // Optionally, scroll into view after a short delay
            setTimeout(() => {
                const el = document.getElementById(`request-group-${focusId}`);
                if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }, 500);
        }
    }, [locationState, searchParams]);

    // Helper to safely update URL parameters
    const updateParams = (updates: Record<string, string | number | null>) => {
        setSearchParams(prev => {
            const next = new URLSearchParams(prev);
            Object.entries(updates).forEach(([key, value]) => {
                if (value === null || value === '') {
                    next.delete(key);
                } else {
                    next.set(key, String(value));
                }
            });
            return next;
        }, { replace: true });
    };

    // Debounce search input and update URL
    useEffect(() => {
        const handler = setTimeout(() => {
            if (searchTerm !== searchInput) {
                updateParams({ search: searchInput || null, page: 1 });
            }
        }, 500);
        return () => clearTimeout(handler);
    }, [searchInput, searchTerm]);

    const handleRequestStatusChange = (val: string) => updateParams({ requestStatus: val || null, page: 1 });
 
    // Initial Lookups (Request Statuses only)
    useEffect(() => {
        async function fetchLookups() {
            try {
                const [loadedStatuses, loadedIvaRates, loadedUnits, loadedCurrencies, loadedPlants] = await Promise.all([
                    api.lookups.getRequestStatuses(false),
                    api.lookups.getIvaRates(true),
                    api.lookups.getUnits(true),
                    api.lookups.getCurrencies(true),
                    api.lookups.getPlants(undefined, true)
                ]);
                setStatuses(loadedStatuses);
                setIvaRates(loadedIvaRates);
                setUnits(loadedUnits);
                setCurrencies(loadedCurrencies);
                setPlants(loadedPlants);
            } catch (err) {
                console.error("Failed to load lookups:", err);
            }
        }
        fetchLookups();
    }, []);


    const loadData = async () => {
        try {
            setLoading(true);
            const response = await api.lineItems.list(
                searchTerm, 
                itemStatus, 
                requestStatus, 
                undefined, 
                undefined, 
                { owner: owner === 'todos' ? undefined : owner }, 
                page, 
                pageSize
            );
            setItems(response.data || []);
            setTotalCount(response.totalCount || 0);
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro desconhecido' });
        } finally {
            setLoading(false);
        }
    };

    // Main Data Fetch
    useEffect(() => {
        loadData();
    }, [searchTerm, itemStatus, requestStatus, owner, page, pageSize]);

    // --- GROUPING & UI HELPERS ---

    const groupItemsByRequest = (items: any[]) => {
        const groups: Record<string, any> = {};
        items.forEach(item => {
            if (!groups[item.requestId]) {
                groups[item.requestId] = {
                    requestId: item.requestId,
                    requestNumber: item.requestNumber,
                    requestStatusName: item.requestStatusName,
                    requestStatusCode: item.requestStatusCode,
                    requesterName: item.requesterName,
                    plantId: item.plantId || item.requestPlantId,
                    plantName: item.plantName || item.requestPlantName,
                    departmentName: item.departmentName,
                    needByDateUtc: item.needByDateUtc,
                    proformaId: item.proformaId,
                    proformaFileName: item.proformaFileName,
                    proformaAttachments: item.proformaAttachments || (item.proformaId ? [{ id: item.proformaId, fileName: item.proformaFileName }] : []),
                    supportingAttachments: item.supportingAttachments || [],
                    requestSupplierId: item.requestSupplierId,
                    requestSupplierName: item.requestSupplierName,
                    requestSupplierCode: item.requestSupplierCode,
                    requestTypeCode: item.requestTypeCode,
                    requestCurrencyId: item.currencyId || 1, // Fallback to AOA if missing
                    companyId: item.companyId,
                    latestAdjustmentMessage: item.latestAdjustmentMessage,
                    latestAdjustmentActor: item.latestAdjustmentActor,
                    latestAdjustmentRole: item.latestAdjustmentRole,
                    latestAdjustmentDateUtc: item.latestAdjustmentDateUtc,
                    requestStatusBadgeColor: item.requestStatusBadgeColor,
                    buyerId: item.buyerId,
                    buyerName: item.buyerName,
                    requestTitle: item.requestTitle,
                    requestDescription: item.requestDescription,
                    quotations: item.quotations || [],
                    items: []
                };
            }
            // If the group exists, sync proformaAttachments from the latest item that has them
            if (item.proformaAttachments && item.proformaAttachments.length > 0 && groups[item.requestId].proformaAttachments.length === 0) {
                groups[item.requestId].proformaAttachments = item.proformaAttachments;
                groups[item.requestId].proformaId = item.proformaId;
                groups[item.requestId].proformaFileName = item.proformaFileName;
            }
            if (item.supportingAttachments && item.supportingAttachments.length > 0 && groups[item.requestId].supportingAttachments.length === 0) {
                groups[item.requestId].supportingAttachments = item.supportingAttachments;
            }
            // Only add real line items to the group's items array
            if (item.lineItemId) {
                groups[item.requestId].items.push(item);
            }
        });
        return Object.values(groups);
    };

    const toggleGroup = (requestId: string) => {
        setExpandedRequests(prev => {
            const next = new Set(prev);
            if (next.has(requestId)) next.delete(requestId);
            else next.add(requestId);
            return next;
        });
    };

    const getActionBadge = (statusCode: string) => {
        const badges: Record<string, { label: string; className: string }> = {
            'WAITING_QUOTATION': { label: 'AÇÃO NECESSÁRIA: COTAR', className: 'badge-danger' },
            'AREA_ADJUSTMENT': { label: 'AÇÃO NECESSÁRIA: REAJUSTAR', className: 'badge-orange' },
            'FINAL_ADJUSTMENT': { label: 'AÇÃO NECESSÁRIA: REAJUSTAR', className: 'badge-orange' },
            'PAYMENT_COMPLETED': { label: 'PAGAMENTO REALIZADO', className: 'badge-success' },
            'IN_FOLLOWUP': { label: 'EM ACOMPANHAMENTO', className: 'badge-info' }
        };
        return badges[statusCode] || { label: 'EM PROCESSO', className: 'badge-neutral' };
    };

    // Step 2 & 3: OCR Integration Handlers
    const handleFileSelectForImport = (requestId: string, files: File[]) => {
        setImportSelectedFiles(prev => ({ ...prev, [requestId]: files }));
        // setQuotationFlowStep(prev => ({ ...prev, [requestId]: 'UPLOAD' })); // Removed or handled elsewhere
        // Reset OCR results/errors when a new file is chosen
        setOcrResults(prev => { const n = { ...prev }; delete n[requestId]; return n; });
        setOcrErrors(prev => { const n = { ...prev }; delete n[requestId]; return n; });
    };

    const handleContinueWithDocument = async (requestId: string) => {
        const file = importSelectedFiles[requestId]?.[0];
        if (!file) return;

        setIsProcessingOcr(prev => ({ ...prev, [requestId]: true }));
        let hasDuplicateWarning = false;

        try {
            const hash = await computeFileHash(file);
            const dupCheck = await api.attachments.checkDuplicate(hash);
            
            if (dupCheck.isDuplicate) {
                hasDuplicateWarning = true;
                setFileDuplicateWarning({
                    isOpen: true,
                    requestId: requestId,
                    fileName: file.name,
                    requestNumber: dupCheck.requestNumber || 'Desconhecido',
                    uploadedBy: dupCheck.uploadedBy,
                    createdAtUtc: dupCheck.createdAtUtc,
                    uploadCallback: () => {
                        setFileDuplicateWarning(null);
                        handleImportFiles(requestId, [file]);
                    }
                });
                return;
            }
        } catch (err) {
            console.error("Duplicate check failed:", err);
        } finally {
            if (!hasDuplicateWarning) {
                setIsProcessingOcr(prev => ({ ...prev, [requestId]: false }));
            }
        }

        await handleImportFiles(requestId, [file]);
    };

    const handleResetToSelect = (requestId: string) => {
        setQuotationFlowStep(prev => ({ ...prev, [requestId]: 'SELECT_MODE' }));
        setImportSelectedFiles(prev => ({ ...prev, [requestId]: [] }));
        setOcrErrors(prev => ({ ...prev, [requestId]: null }));
        // Ensure we clear any edit session state when returning to selection
        setEditingQuotationId(prev => ({ ...prev, [requestId]: null }));
        
        setDraftProformaFiles(prev => {
            const next = { ...prev };
            delete next[requestId];
            return next;
        });
        
        setQuotationDrafts(prev => {
            const next = { ...prev };
            delete next[requestId];
            return next;
        });

        // Important: Always clear OCR batches and form errors so they don't leak back if reopened.
        setReconciliationBatches(prev => {
            const next = { ...prev };
            delete next[requestId];
            return next;
        });
        
        setFormErrors(prev => {
            const next = { ...prev };
            delete next[requestId];
            return next;
        });
    };

    const handleImportFiles = async (requestId: string, files: File[]) => {
        if (files.length === 0) return;

        // Preemptive Client-side Extension Validation
        const file = files[0];
        const ext = file.name.split('.').pop()?.toLowerCase() || '';
        if (!ALLOWED_EXTENSIONS.includes(ext)) {
            setFeedback({ 
                type: 'error', 
                message: `Arquivo não permitido. Formatos aceitos: ${ALLOWED_EXTENSIONS_MSG}.` 
            });
            return;
        }
        
        setIsProcessingOcr(prev => ({ ...prev, [requestId]: true }));
        setOcrErrors(prev => ({ ...prev, [requestId]: null }));
        // Ensure any previous edit state is cleared for a new OCR flow
        setEditingQuotationId(prev => ({ ...prev, [requestId]: null }));
        setDraftProformaFiles(prev => {
            const next = { ...prev };
            delete next[requestId];
            return next;
        });
        setImportSelectedFiles(prev => ({ ...prev, [requestId]: files }));

        try {
            // Part B: Persist the file first as PROFORMA attachment (using centralized utility)
            const uploadData = await api.attachments.upload(requestId, [files[0]], 'PROFORMA');
            const attachmentId = uploadData[0].id;

            // Step 3: Real OCR Call
            const result = await api.requests.ocrExtract(requestId, files[0]);
            setOcrResults(prev => ({ ...prev, [requestId]: result }));

            // Use shared OCR processor
            const initialDraft = await mapOcrResultToDraft(result, attachmentId);

            setQuotationDrafts(prev => ({ ...prev, [requestId]: initialDraft }));
            setQuotationFlowStep(prev => ({ ...prev, [requestId]: 'EDIT_QUOTATION' }));

            // Phase 2: Auto-load reconciliation after OCR
            try {
                const reconData = await api.requests.getReconciliation(requestId);
                if (reconData && reconData.records && reconData.records.length > 0) {
                    setReconciliationBatches(prev => ({ ...prev, [requestId]: reconData }));
                }
            } catch (reconErr) {
                console.warn('Failed to load reconciliation:', reconErr);
            }
        } catch (error: any) {
            setOcrErrors(prev => ({ ...prev, [requestId]: error.message }));
            setQuotationFlowStep(prev => ({ ...prev, [requestId]: 'EDIT_QUOTATION' }));
        } finally {
            setIsProcessingOcr(prev => ({ ...prev, [requestId]: false }));
        }
    };

    const handleUpdateQuotationHeader = (requestId: string, field: keyof QuotationDraft, value: any) => {
        setQuotationDrafts(prev => {
            const draft = { ...prev[requestId], [field]: value };
            // Clear extracted suggestion if a valid currency is manually picked
            if (field === 'currency' && value) {
                draft.extractedCurrency = undefined;
            }
            // Recalculate total when global discount changes
            if (field === 'discountAmount') {
                draft.totalAmount = calculateDraftTotal(draft);
            }
            return {
                ...prev,
                [requestId]: draft
            };
        });
    };

    const handleUpdateQuotationItem = (requestId: string, index: number, field: keyof QuotationDraftItem, value: any) => {
        setQuotationDrafts(prev => {
            const draft = { ...prev[requestId] };
            if (!draft.items) return prev;

            const updatedItems = [...draft.items];
            
            // If user manually changes the absolute discount amount, clear the reactive percentage
            if (field === 'discountAmount') {
                updatedItems[index] = { ...updatedItems[index], discountAmount: value, discountPercent: undefined };
            } else {
                updatedItems[index] = { ...updatedItems[index], [field]: value };
            }
            
            // Reactive discount recalculation if percentage is locked in
            if ((field === 'quantity' || field === 'unitPrice') && updatedItems[index].discountPercent !== undefined) {
                const qty = updatedItems[index].quantity || 0;
                const price = updatedItems[index].unitPrice || 0;
                const pct = updatedItems[index].discountPercent!;
                const recalculatedDiscount = Math.round(qty * price * (pct / 100) * 100) / 100;
                updatedItems[index].discountAmount = recalculatedDiscount;
            }
            
            // Recalculate deterministic line logic using shared hook logic
            updatedItems[index].totalPrice = calculateItemTotal(updatedItems[index]);

            draft.items = updatedItems;
            
            // Recalculate quotation total internally if needed
            draft.totalAmount = calculateDraftTotal(draft);
            
            return {
                ...prev,
                [requestId]: draft
            };
        });
    };

    // Global discount logic replaced by item-level discounts (Option A)

    // Local calculation logic moved to shared useOcrProcessor hook

    const handleAddQuotationItem = (requestId: string) => {
        setQuotationDrafts(prev => {
            const draft = prev[requestId] ? { ...prev[requestId] } : {
                supplierId: null,
                supplierNameSnapshot: '',
                documentNumber: '',
                documentDate: new Date().toISOString().split('T')[0],
                currency: '',
                discountAmount: 0,
                totalAmount: 0,
                items: []
            } as QuotationDraft;

            const nextLineNumber = (draft.items?.length || 0) + 1;
            
            // Default unit from request line item if possible
            const group = items.find(i => i.requestId === requestId);
            const originalItem = group?.items?.find((i: any) => i.lineNumber === nextLineNumber);

            const newItem: QuotationDraftItem = {
                lineNumber: nextLineNumber,
                description: '',
                quantity: 1,
                unitId: originalItem?.unitId || null,
                unitPrice: 0,
                ivaRateId: null,
                totalPrice: 0,
                discountAmount: 0
            };
            draft.items = [...(draft.items || []), newItem];
            return { ...prev, [requestId]: draft };
        });
    };

    const handleRemoveQuotationItem = (requestId: string, index: number) => {
        setQuotationDrafts(prev => {
            const draft = { ...prev[requestId] };
            if (!draft.items) return prev;

            const updatedItems = draft.items.filter((_, i) => i !== index);
            draft.totalAmount = updatedItems.reduce((sum, item) => sum + (item.totalPrice || 0), 0);
            draft.items = updatedItems;
            return { ...prev, [requestId]: draft };
        });
    };

    const handleRestoreOcrOriginal = async (requestId: string) => {
        const result = ocrResults[requestId];
        if (!result) return;

        const originalDraft = await mapOcrResultToDraft(result);
        setQuotationDrafts(prev => ({ ...prev, [requestId]: originalDraft }));
    };

    const handleSaveQuotation = async (requestId: string) => {
        const draft = quotationDrafts[requestId];
        if (!draft) return;
        
        if (draft.items.length === 0) {
            setFeedback({ type: 'error', message: 'A cotação deve conter pelo menos um item.' });
            return;
        }

        // Robust inline validation for header fields (Step 9.1)
        const errors: Record<string, string> = {};
        if (!draft.supplierId) errors.supplierId = 'Fornecedor é obrigatório.';
        if (!draft.documentNumber || !draft.documentNumber.trim()) errors.documentNumber = 'Número do documento é obrigatório.';
        if (!draft.documentDate) errors.documentDate = 'Data do documento é obrigatória.';
        if (!draft.currency) errors.currency = 'Moeda é obrigatória.';

        if (Object.keys(errors).length > 0) {
            setFormErrors(prev => ({ ...prev, [requestId]: errors }));
            setFeedback({ type: 'error', message: 'Por favor, corrija os erros destacados no formulário.' });
            return;
        }

        try {
            setIsSaving(true);
            const sourceType = addQuotationMode[requestId]; // 'UPLOAD' or 'MANUAL'
            const editId = editingQuotationId[requestId];
            
            // Step 5: Process Proforma Upload PRE-EMPTIVELY BEFORE SAVE
            let currentProformaId = draft.proformaAttachmentId;
            try {
                // If a new file is explicitly in the draft area, it takes precedence (replacement flow)
                let fileToUpload: File | null = null;
                if (draftProformaFiles[requestId]) {
                    fileToUpload = draftProformaFiles[requestId];
                } else if (sourceType === 'UPLOAD' && importSelectedFiles[requestId]?.[0] && !editId && !currentProformaId) {
                    // For direct OCR initial save (only if not already uploaded during extraction)
                    fileToUpload = importSelectedFiles[requestId][0];
                }

                if (fileToUpload) {
                    // Preemptive Client-side Extension Validation
                    const ext = fileToUpload.name.split('.').pop()?.toLowerCase() || '';
                    if (!ALLOWED_EXTENSIONS.includes(ext)) {
                        setFeedback({ 
                            type: 'error', 
                            message: `Arquivo não permitido. Formatos aceitos: ${ALLOWED_EXTENSIONS_MSG}.` 
                        });
                        setIsSaving(false);
                        return;
                    }

                    const uploaded = await api.attachments.upload(requestId, [fileToUpload], 'PROFORMA');
                    if (uploaded && uploaded.length > 0) {
                        currentProformaId = uploaded[0].id;
                    }
                }

                // Mandatory Validation: Must have EITHER a new upload OR an existing proforma ID
                if (!currentProformaId) {
                    setFeedback({ type: 'error', message: 'O documento Proforma é obrigatório para salvar a cotação.' });
                    setIsSaving(false);
                    return;
                }
            } catch (attError: any) {
                console.error("Failed to upload proforma:", attError);
                const errorMsg = attError.response?.data?.detail || attError.message || 'Falha ao processar o upload do documento.';
                setFeedback({ type: 'error', message: errorMsg });
                setIsSaving(false);
                return;
            }

            // Duplicate Supplier Validation Check
            const grouped = groupItemsByRequest(items);
            const group = grouped.find(g => g.requestId === requestId);
            const existingQuotations = group?.quotations || [];
            const duplicate = existingQuotations.find((q: SavedQuotationDto) => q.supplierId === draft.supplierId && q.id !== editId);
            if (duplicate) {
                // Halt save, open resolution modal
                setDuplicateSupplierModal({ show: true, requestId, draft: { ...draft, proformaAttachmentId: currentProformaId }, duplicate });
                setIsSaving(false);
                return;
            }
            
            // If we reached here, validations passed and it's not a duplicate.
            // Trigger Confirmation Modal before real save
            setShowApprovalModal({ 
                show: true, 
                type: sourceType === 'UPLOAD' ? 'SAVE_QUOTATION_OCR' : 'SAVE_QUOTATION_MANUAL', 
                requestId, 
                itemId: null,
                itemDescription: null,
                newStatusCode: null,
                hasProforma: false, 
                hasSupplier: true, 
                itemsCount: draft.items.length,
                isLastItem: false,
                quotationCount: 0,
                hasCompleteQuotation: false
            });
        } catch (error: any) {
            setFeedback({ type: 'error', message: error.message || 'Erro ao realizar validação.' });
        } finally {
            setIsSaving(false);
        }
    };

    const executeSaveQuotation = async (requestId: string) => {
        try {
            setIsSaving(true);
            const sourceType = addQuotationMode[requestId]; 
            const editId = editingQuotationId[requestId];
            const draft = quotationDrafts[requestId];
            const currentProformaId = draftProformaFiles[requestId] 
                ? (await api.attachments.upload(requestId, [draftProformaFiles[requestId]], 'PROFORMA')).id 
                : (draft.proformaAttachmentId || null);

            const payload = {
                supplierId: draft.supplierId,
                supplierNameSnapshot: draft.supplierNameSnapshot,
                documentNumber: draft.documentNumber,
                documentDate: draft.documentDate ? new Date(draft.documentDate).toISOString() : null,
                currency: draft.currency,
                discountAmount: draft.discountAmount || 0,
                totalAmount: draft.totalAmount, 
                sourceType: editId ? undefined : (sourceType === 'UPLOAD' ? 'OCR' : 'MANUAL'),
                sourceFileName: editId ? undefined : (sourceType === 'UPLOAD' ? (importSelectedFiles[requestId]?.[0]?.name || null) : null),
                proformaAttachmentId: currentProformaId,
                items: draft.items.map(item => ({
                    lineNumber: item.lineNumber,
                    description: item.description,
                    quantity: item.quantity,
                    unitId: item.unitId,
                    unitPrice: item.unitPrice,
                    discountAmount: item.discountAmount || 0,
                    ivaRateId: item.ivaRateId
                }))
            };
            
            // Clear errors before attempt
            setFormErrors(prev => ({ ...prev, [requestId]: {} }));

            if (editId) {
                await api.requests.updateQuotation(requestId, editId, payload);
                setFeedback({ type: 'success', message: 'Cotação atualizada com sucesso!' });
            } else {
                await api.requests.saveQuotation(requestId, payload);
                setFeedback({ type: 'success', message: 'Cotação salva com sucesso!' });
            }

            // Cleanup flow state on success
            setQuotationFlowStep(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setQuotationDrafts(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setAddQuotationMode(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setOcrResults(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setEditingQuotationId(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setDraftProformaFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setImportSelectedFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setReconciliationBatches(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            
            loadData();
        } catch (error: any) {
            setFeedback({ type: 'error', message: error.message || 'Erro ao salvar cotação.' });
        } finally {
            setIsSaving(false);
        }
    };


    const handleCancelQuotation = async (requestId: string) => {
        try {
            const draft = quotationDrafts[requestId];
            const isEditing = !!editingQuotationId[requestId];
            
            // If creating a NEW quotation from OCR, the attachment was pre-uploaded. Clean it up.
            if (!isEditing && draft?.proformaAttachmentId && addQuotationMode[requestId] === 'UPLOAD') {
                setIsSaving(true);
                try {
                    await api.attachments.delete(draft.proformaAttachmentId);
                } catch (e) {
                    console.warn("Failed to delete orphaned attachment on cancel", e);
                }
                setIsSaving(false);
            }

            // Cleanup flow state
            setQuotationFlowStep(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setQuotationDrafts(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setAddQuotationMode(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setOcrResults(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setEditingQuotationId(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setDraftProformaFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setImportSelectedFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setReconciliationBatches(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setFormErrors(prev => { const n = { ...prev }; delete n[requestId]; return n; });
        } catch (error) {
            console.error("Error cancelling quotation:", error);
            setIsSaving(false);
        }
    };


    const confirmDuplicateReplacement = async () => {
        const { requestId, draft, duplicate } = duplicateSupplierModal;
        if (!requestId || !draft || !duplicate) return;

        try {
            setIsSaving(true);
            const sourceType = addQuotationMode[requestId]; 
            const editId = editingQuotationId[requestId];

            const payload = {
                supplierId: draft.supplierId,
                supplierNameSnapshot: draft.supplierNameSnapshot,
                documentNumber: draft.documentNumber,
                documentDate: draft.documentDate ? new Date(draft.documentDate).toISOString() : null,
                currency: draft.currency,
                discountAmount: draft.discountAmount || 0,
                totalAmount: draft.totalAmount,
                sourceType: editId ? undefined : (sourceType === 'UPLOAD' ? 'OCR' : 'MANUAL'),
                sourceFileName: editId ? undefined : (sourceType === 'UPLOAD' ? (importSelectedFiles[requestId]?.[0]?.name || null) : null),
                proformaAttachmentId: draft.proformaAttachmentId,
                items: draft.items.map(item => ({
                    lineNumber: item.lineNumber,
                    description: item.description,
                    quantity: item.quantity,
                    unitId: item.unitId,
                    unitPrice: item.unitPrice,
                    ivaRateId: item.ivaRateId
                }))
            };

            // Issue the ATOMIC replace via the backend
            await api.requests.saveQuotation(requestId, payload, duplicate.id);
            setFeedback({ type: 'success', message: 'Cotação substituída com sucesso!' });

            // Cleanup
            setQuotationFlowStep(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setQuotationDrafts(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setAddQuotationMode(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setOcrResults(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setEditingQuotationId(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setDraftProformaFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setImportSelectedFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setReconciliationBatches(prev => { const n = { ...prev }; delete n[requestId]; return n; });
            setDuplicateSupplierModal({ show: false, requestId: '', draft: null, duplicate: null });
            
            loadData();
        } catch (error: any) {
             setFeedback({ type: 'error', message: error.message || 'Erro ao substituir cotação.' });
        } finally {
            setIsSaving(false);
        }
    };

    const cancelDuplicateFlow = () => {
        setDuplicateSupplierModal({ show: false, requestId: '', draft: null, duplicate: null });
    };



    const handleEditQuotation = (requestId: string, quotation: SavedQuotationDto) => {
        // Map saved quotation to draft structure
        const draft: QuotationDraft = {
            supplierId: quotation.supplierId || null,
            supplierNameSnapshot: quotation.supplierNameSnapshot,
            documentNumber: quotation.documentNumber || '',
            documentDate: quotation.documentDate ? quotation.documentDate.split('T')[0] : '',
            currency: quotation.currency,
            discountAmount: quotation.discountAmount || 0,
            totalAmount: quotation.totalAmount,
            proformaAttachmentId: quotation.proformaAttachmentId,
            items: quotation.items.map(item => ({
                lineNumber: item.lineNumber,
                description: item.description,
                quantity: item.quantity,
                unitId: item.unitId || null,
                unitPrice: item.unitPrice,
                ivaRateId: item.ivaRateId,
                totalPrice: item.lineTotal,
                discountAmount: 0
            }))
        };

        setQuotationDrafts(prev => ({ ...prev, [requestId]: draft }));
        setEditingQuotationId(prev => ({ ...prev, [requestId]: quotation.id }));
        setQuotationFlowStep(prev => ({ ...prev, [requestId]: 'EDIT_QUOTATION' }));
        setAddQuotationMode(prev => ({ ...prev, [requestId]: 'MANUAL' })); // For layout purposes
        
        // Scroll and highlight Section B (Step 9.2)
        setHighlightedRequestId(requestId);
        setTimeout(() => setHighlightedRequestId(null), 2500);

        setTimeout(() => {
            const sectionB = document.getElementById(`section-b-${requestId}`);
            if (sectionB) sectionB.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }, 100);
    };

    const handleDeleteQuotation = async (requestId: string, quotationId: string) => {
        // Use ApprovalModal for confirmation
        const qRecord = items.find(i => i.requestId === requestId)?.quotations?.find((q: any) => q.id === quotationId);
        
        setShowApprovalModal({ 
            show: true, 
            type: 'DELETE_QUOTATION' as any, // Ad-hoc type for this task
            requestId, 
            itemId: quotationId, // Use itemId to pass quotationId
            itemDescription: `Cotação ${qRecord?.supplierNameSnapshot || ''} (Doc: ${qRecord?.documentNumber || ''})`,
            newStatusCode: null,
            hasProforma: false, 
            hasSupplier: false, 
            itemsCount: 0,
            isLastItem: false,
            quotationCount: 0,
            hasCompleteQuotation: false
        });
    };

    const handleRemoveImportFile = (requestId: string) => {
        setImportSelectedFiles(prev => { const n = { ...prev }; delete n[requestId]; return n; });
        setQuotationFlowStep(prev => { const n = { ...prev }; delete n[requestId]; return n; });
        setOcrResults(prev => { const n = { ...prev }; delete n[requestId]; return n; });
        setOcrErrors(prev => { const n = { ...prev }; delete n[requestId]; return n; });
    };


    const handleDeleteProforma = async (attachmentId: string) => {
        // Trigger modal instead of native confirm
        const allItems = items;
        let fileName = '';
        for (const item of allItems) {
            const att = item.proformaAttachments?.find((a: any) => a.id === attachmentId);
            if (att) { fileName = att.fileName; break; }
        }
        setDeleteConfirm({ attachmentId, fileName });
    };

    const confirmDeleteProforma = async () => {
        if (!deleteConfirm) return;
        try {
            setIsSaving(true);
            await api.attachments.delete(deleteConfirm.attachmentId);
            setFeedback({ type: 'success', message: 'Documento removido com sucesso.' });
            const response = await api.lineItems.list(searchTerm, itemStatus, requestStatus, undefined, undefined, undefined, page, pageSize);
            setItems(response.data || []);
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao remover documento.' });
        } finally {
            setIsSaving(false);
            setDeleteConfirm(null);
        }
    };


    const handleUpdateGroupSupplier = async (requestId: string, supplierId: number | null, supplierName: string) => {
        try {
            setIsSaving(true);
            await api.requests.updateSupplier(requestId, supplierId);
            setItems(prev => prev.map(i => i.requestId === requestId ? { 
                ...i, 
                requestSupplierId: supplierId, 
                requestSupplierName: supplierName 
            } : i));
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao atualizar fornecedor do pedido.' });
        } finally {
            setIsSaving(false);
        }
    };

    const handleCompleteQuotation = (requestId: string, hasProforma: boolean, hasSupplier: boolean, itemsCount: number, quotationCount: number, hasCompleteQuotation: boolean) => {
        setShowApprovalModal({ 
            show: true, 
            type: 'COMPLETE_QUOTATION', 
            requestId, 
            itemId: null,
            itemDescription: null,
            newStatusCode: null,
            hasProforma, 
            hasSupplier, 
            itemsCount,
            isLastItem: false,
            quotationCount,
            hasCompleteQuotation
        });
    };

    const handleModalAction = async () => {
        const { requestId, itemId, newStatusCode, type, itemsCount } = showApprovalModal;
        if (!requestId && type !== 'ITEM_STATUS_CHANGE') return;

        try {
            setIsSaving(true);
            setModalFeedback({ type: 'error', message: null });

            if (type === 'COMPLETE_QUOTATION') {
                const completionResult = await completeQuotationAction(requestId!, itemsCount, approvalComment);
                
                // Financial Integrity Gate: check if backend returned integrity failure
                if (completionResult && 'integrityCheckFailed' in completionResult && completionResult.integrityCheckFailed) {
                    // Close the approval modal and show the integrity modal instead
                    setShowApprovalModal({ show: false, type: null, requestId: null, itemId: null, itemDescription: null, newStatusCode: null, hasProforma: false, hasSupplier: false, itemsCount: 0, isLastItem: false, quotationCount: 0, hasCompleteQuotation: false });
                    setApprovalComment('');
                    setIntegrityModal({
                        show: true,
                        requestId: requestId!,
                        itemsCount,
                        ocrOriginalTotal: completionResult.ocrOriginalTotal,
                        quotationTotal: completionResult.quotationTotal,
                        varianceAmount: completionResult.varianceAmount,
                        variancePercent: completionResult.variancePercent,
                        toleranceApplied: completionResult.toleranceApplied,
                        unresolvedReconciliationCount: completionResult.unresolvedReconciliationCount,
                        detail: completionResult.detail
                    });
                    setIsSaving(false);
                    return; // Do not proceed with normal flow
                }
                setFeedback({ type: 'success', message: 'Cotação concluída e enviada para aprovação.' });
            } else if (type === 'CANCEL_REQUEST' && requestId) {
                await api.requests.cancel(requestId, approvalComment);
                setFeedback({ type: 'success', message: 'Pedido cancelado com sucesso.' });
            } else if (type === 'ITEM_STATUS_CHANGE' && itemId && newStatusCode) {
                await api.lineItems.updateStatus(itemId, newStatusCode, approvalComment);
                setFeedback({ type: 'success', message: 'Status do item atualizado com sucesso.' });
            } else if (type === 'DELETE_ITEM' && requestId && itemId) {
                await api.requests.deleteLineItem(requestId, itemId);
                setFeedback({ type: 'success', message: 'Item excluído com sucesso.' });
            } else if (type === 'DELETE_QUOTATION' && requestId && itemId) {
                // itemId here is the quotationId
                await api.requests.deleteQuotation(requestId, itemId);
                setFeedback({ type: 'success', message: 'Cotação excluída com sucesso.' });
            } else if ((type === 'SAVE_QUOTATION_OCR' || type === 'SAVE_QUOTATION_MANUAL') && requestId) {
                await executeSaveQuotation(requestId);
            }
            
            setShowApprovalModal({ show: false, type: null, requestId: null, itemId: null, itemDescription: null, newStatusCode: null, hasProforma: false, hasSupplier: false, itemsCount: 0, isLastItem: false, quotationCount: 0, hasCompleteQuotation: false });
            setApprovalComment('');
            
            // Refresh data
            const response = await api.lineItems.list(searchTerm, itemStatus, requestStatus, undefined, undefined, undefined, page, pageSize);
            setItems(response.data || []);
        } catch (err: any) {
            setModalFeedback({ type: 'error', message: err.message || 'Erro ao concluir ação.' });
            if (err.message && err.message.toLowerCase().includes('proforma')) {
                setHighlightedRequestId(requestId);
                setTimeout(() => setHighlightedRequestId(null), 5000);
            }
        } finally {
            setIsSaving(false);
        }
    };

    // ═══════════════════════════════════════════════════════════════
    // FINANCIAL INTEGRITY GATE — Override Handler
    // ═══════════════════════════════════════════════════════════════
    const handleIntegrityOverride = async () => {
        if (!integrityModal || !integrityJustification.trim()) return;

        try {
            setIntegrityOverrideLoading(true);
            const result = await completeQuotationAction(
                integrityModal.requestId,
                integrityModal.itemsCount,
                '', // no additional comment
                {
                    financialIntegrityOverride: true,
                    overrideJustification: integrityJustification.trim()
                }
            );

            // Check if there's still an integrity failure (shouldn't happen with override, but safety check)
            if (result && 'integrityCheckFailed' in result && result.integrityCheckFailed) {
                setFeedback({ type: 'error', message: 'Override falhou. Verifique os dados e tente novamente.' });
                return;
            }

            setFeedback({ type: 'success', message: 'Cotação concluída com override financeiro e enviada para aprovação.' });
            setIntegrityModal(null);
            setIntegrityJustification('');

            // Refresh data
            const response = await api.lineItems.list(searchTerm, itemStatus, requestStatus, undefined, undefined, undefined, page, pageSize);
            setItems(response.data || []);
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao processar override financeiro.' });
        } finally {
            setIntegrityOverrideLoading(false);
        }
    };

    const handleAssignToMe = async (requestId: string) => {
        try {
            setIsSaving(true);
            await api.requests.assignBuyer(requestId);
            setFeedback({ type: 'success', message: 'Pedido atribuído a você com sucesso.' });
            loadData();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao atribuir pedido.' });
        } finally {
            setIsSaving(false);
        }
    };

    // --- RENDER HELPERS ---

    const groupedRequests = groupItemsByRequest(items);

    return (
        <PageContainer>
            <style>{highlightStyles}</style>

            {feedback.message && (
                <div style={{
                    position: 'sticky',
                    top: 'calc(var(--header-height) - 1rem)',
                    zIndex: 110,
                    backgroundColor: 'var(--color-bg-page)',
                    padding: '2rem 0 0 0',
                    margin: '-2rem 0 0 0',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '16px'
                }}>
                    <Feedback
                        type={feedback.type}
                        message={feedback.message}
                        onClose={() => setFeedback(prev => ({ ...prev, message: null }))}
                    />
                </div>
            )}

            <PageHeader
                title="Gestão de Cotações"
                subtitle="Visualize e gerencie os itens solicitados e suas cotações em um único workspace."
                actions={
                    <button
                        onClick={() => setShowHelpModal(true)}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            backgroundColor: '#FEF9C3',
                            color: '#854D0E',
                            border: '1px solid #FDE047',
                            padding: '8px 16px',
                            borderRadius: '6px',
                            fontWeight: 800,
                            fontSize: '0.8rem',
                            cursor: 'pointer',
                            transition: 'all 0.2s ease',
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em'
                        }}
                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#FEF08A'; e.currentTarget.style.color = '#713F12'; e.currentTarget.style.transform = 'translateY(-1px)'; }}
                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#FEF9C3'; e.currentTarget.style.color = '#854D0E'; e.currentTarget.style.transform = 'none'; }}
                    >
                        <BookOpen size={16} /> Manual de Cotação
                    </button>
                }
            />

            <AnimatePresence>
                {showHelpModal && (
                    <DropdownPortal>
                        <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(15, 23, 42, 0.4)', backdropFilter: 'blur(4px)', zIndex: 100000, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '20px' }}>
                            <motion.div
                                initial={{ opacity: 0, scale: 0.95, y: 20 }}
                                animate={{ opacity: 1, scale: 1, y: 0 }}
                                exit={{ opacity: 0, scale: 0.95, y: 20 }}
                                style={{ backgroundColor: 'white', borderRadius: '16px', width: '100%', maxWidth: '800px', maxHeight: '90vh', overflowY: 'auto', boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)', position: 'relative' }}
                            >
                                <div style={{ position: 'sticky', top: 0, backgroundColor: '#f8fafc', padding: '24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', zIndex: 1 }}>
                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'center' }}>
                                        <div style={{ padding: '12px', backgroundColor: '#fef3c7', color: '#d97706', borderRadius: '12px' }}>
                                            <BookOpen size={28} strokeWidth={2.5} />
                                        </div>
                                        <div>
                                            <h2 style={{ fontSize: '1.5rem', fontWeight: 900, color: '#0f172a', margin: 0, letterSpacing: '-0.02em' }}>Manual de Gestão de Cotações</h2>
                                            <p style={{ margin: '4px 0 0 0', color: '#64748b', fontSize: '0.95rem', fontWeight: 500 }}>Guia para processamento de faturas e reconciliação OCR.</p>
                                        </div>
                                    </div>
                                    <button onClick={() => setShowHelpModal(false)} style={{ background: 'white', border: '1px solid #e2e8f0', cursor: 'pointer', color: '#64748b', padding: '8px', borderRadius: '8px', transition: 'all 0.2s', display: 'flex' }} onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#f1f5f9'; e.currentTarget.style.color = '#0f172a'; }} onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'white'; e.currentTarget.style.color = '#64748b'; }}>
                                        <X size={20} strokeWidth={2.5} />
                                    </button>
                                </div>
                                <div style={{ padding: '32px' }}>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                                        {/* Step 1 */}
                                        <div style={{ display: 'flex', gap: '20px' }}>
                                            <div style={{ flexShrink: 0, width: '40px', height: '40px', borderRadius: '20px', backgroundColor: '#e0e7ff', color: '#4f46e5', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '1.2rem', fontWeight: 900 }}>1</div>
                                            <div>
                                                <h3 style={{ fontSize: '1.1rem', fontWeight: 800, color: '#1e293b', margin: '0 0 8px 0' }}>Receção e Atribuição do Pedido</h3>
                                                <p style={{ fontSize: '0.95rem', color: '#475569', lineHeight: '1.6', margin: 0 }}>Tudo começa quando um requisitante cria um pedido e este entra na fila de compras. O seu primeiro passo é abrir o pedido pendente e clicar no botão <strong>"Assumir Pedido"</strong> para se colocar como responsável pela tarefa. Certifique-se que os detalhes e quantidades solicitadas estão exatas antes de contactar o fornecedor.</p>
                                            </div>
                                        </div>

                                        {/* Step 2 */}
                                        <div style={{ display: 'flex', gap: '20px' }}>
                                            <div style={{ flexShrink: 0, width: '40px', height: '40px', borderRadius: '20px', backgroundColor: '#e0e7ff', color: '#4f46e5', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '1.2rem', fontWeight: 900 }}>2</div>
                                            <div>
                                                <h3 style={{ fontSize: '1.1rem', fontWeight: 800, color: '#1e293b', margin: '0 0 8px 0' }}>Anexar Cotação e Dados Financeiros</h3>
                                                <p style={{ fontSize: '0.95rem', color: '#475569', lineHeight: '1.6', margin: '0 0 12px 0' }}>Após negociar, clique em "Nova Cotação" e anexe a fatura/proforma correspondente. O OCR tentará extrair os dados. No entanto, é <strong>rigorosamente necessário e importante</strong> confirmar sempre manualmente se os itens extraídos, os valores unitários e o valor total correspondem ao papel. Se for um documento complexo, é sempre preferível descartar a extração clicando para <strong>inserir a cotação manualmente</strong>.</p>
                                            </div>
                                        </div>

                                        {/* Step 3 */}
                                        <div style={{ display: 'flex', gap: '20px' }}>
                                            <div style={{ flexShrink: 0, width: '40px', height: '40px', borderRadius: '20px', backgroundColor: '#e0e7ff', color: '#4f46e5', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '1.2rem', fontWeight: 900 }}>3</div>
                                            <div>
                                                <h3 style={{ fontSize: '1.1rem', fontWeight: 800, color: '#1e293b', margin: '0 0 8px 0' }}>Reconciliação e Correspondência</h3>
                                                <p style={{ fontSize: '0.95rem', color: '#475569', lineHeight: '1.6', margin: '0 0 12px 0' }}>O sistema cruza de imediato os itens originalmente solicitados com o ficheiro faturado, sendo possível 3 cenários:</p>
                                                <div style={{ display: 'grid', gap: '12px' }}>
                                                    <div style={{ padding: '12px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px' }}>
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                                            <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: '#059669' }}></div>
                                                            <span style={{ fontSize: '0.85rem', fontWeight: 800, color: '#059669', textTransform: 'uppercase' }}>Exact Match (Perfeito)</span>
                                                        </div>
                                                        <p style={{ margin: 0, fontSize: '0.85rem', color: '#64748b' }}>O item faturado e suas quantidades coincidem na perfeição com o item listado pelo requisitante no sistema.</p>
                                                    </div>
                                                    <div style={{ padding: '12px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px' }}>
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                                            <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: '#6366f1' }}></div>
                                                            <span style={{ fontSize: '0.85rem', fontWeight: 800, color: '#6366f1', textTransform: 'uppercase' }}>Item Extra (Fornecedor)</span>
                                                        </div>
                                                        <p style={{ margin: 0, fontSize: '0.85rem', color: '#64748b' }}>A fatura contém um item extra, como custos de frete adicionais. Como comprador, decide se elimina a linha extra ou permite a inclusão na despesa.</p>
                                                    </div>
                                                    <div style={{ padding: '12px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px' }}>
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                                            <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: '#eab308' }}></div>
                                                            <span style={{ fontSize: '0.85rem', fontWeight: 800, color: '#eab308', textTransform: 'uppercase' }}>Item Faltante</span>
                                                        </div>
                                                        <p style={{ margin: 0, fontSize: '0.85rem', color: '#64748b' }}>O fornecedor não incluiu um item pedido na fatura. O sistema prevê que os itens faltantes aguardem por posteriores orçamentos de outros fornecedores ou numa cotação sequencial e separada.</p>
                                                    </div>
                                                </div>
                                            </div>
                                        </div>

                                        {/* Step 4 */}
                                        <div style={{ display: 'flex', gap: '20px' }}>
                                            <div style={{ flexShrink: 0, width: '40px', height: '40px', borderRadius: '20px', backgroundColor: '#e0e7ff', color: '#4f46e5', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '1.2rem', fontWeight: 900 }}>4</div>
                                            <div>
                                                <h3 style={{ fontSize: '1.1rem', fontWeight: 800, color: '#1e293b', margin: '0 0 8px 0' }}>Progresso de Validação Visual</h3>
                                                <p style={{ fontSize: '0.95rem', color: '#475569', lineHeight: '1.6', margin: '0 0 12px 0' }}>Ao preencher uma ordem com dezenas de linhas, clique na <strong>checkbox circular</strong> à esquerda no final da conferência de cada linha de valor revisto. A linha fica imediatamente colorida de verde e atesta que não se perderá antes de terminar todas as linhas.</p>
                                            </div>
                                        </div>

                                        {/* Step 5 */}
                                        <div style={{ display: 'flex', gap: '20px' }}>
                                            <div style={{ flexShrink: 0, width: '40px', height: '40px', borderRadius: '20px', backgroundColor: '#22c55e', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '1.2rem', fontWeight: 900 }}>5</div>
                                            <div>
                                                <h3 style={{ fontSize: '1.1rem', fontWeight: 800, color: '#1e293b', margin: '0 0 8px 0' }}>Submeter para Aprovação</h3>
                                                <p style={{ fontSize: '0.95rem', color: '#475569', lineHeight: '1.6', margin: 0 }}>Terminada a gravação e correção de todos os preços e totais, submeta para a hierarquia. Note que na condição de comprador <strong>não é exigida a atribuição de Centros de Custo</strong> finais de despesa, pois esta é processada e auditada pelo seu <strong>Aprovador de Área</strong> aquando da passagem do pedido.</p>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </motion.div>
                        </div>
                    </DropdownPortal>
                )}
            </AnimatePresence>

            <SearchFilterBar
                searchPlaceholder="BUSCAR POR NÚMERO, TÍTULO, DESCRIÇÃO..."
                searchValue={searchInput}
                onSearchChange={setSearchInput}
                tabs={[
                    { id: 'todos', label: 'Todos' },
                    { id: 'unassigned', label: 'Não Atribuídos' },
                    { id: 'me', label: 'Meus Pedidos' },
                ]}
                activeTabId={owner || 'todos'}
                onTabChange={(id) => updateParams({ owner: id, page: 1 })}
                actions={
                    <select
                        style={{ padding: '8px 12px', border: '1px solid #e2e8f0', borderRadius: '8px', outline: 'none', background: '#fff', fontSize: '0.85rem', fontWeight: 600, color: '#475569' }}
                        value={requestStatus}
                        onChange={(e) => handleRequestStatusChange(e.target.value)}
                    >
                        <option value="">VER TODOS OS STATUS DO PEDIDO</option>
                        {statuses
                            .filter(s => ["WAITING_QUOTATION", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT", "PAYMENT_COMPLETED", "IN_FOLLOWUP"].includes(s.code))
                            .map(s => (
                                <option key={s.code} value={s.code}>{s.name}</option>
                            ))}
                    </select>
                }
            />

            {/* Grouped Area */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                {loading ? (
                    <div style={{ padding: '60px', textAlign: 'center', color: 'var(--color-primary)', fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.1em', border: '2px solid var(--color-primary)', backgroundColor: 'var(--color-bg-surface)' }}>
                        Carregando Cotações...
                    </div>
                ) : groupedRequests.length === 0 ? (
                    <div style={{ padding: '80px 20px', textAlign: 'center', color: 'var(--color-text-muted)', border: '4px dashed var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                        <FileText size={64} strokeWidth={1.5} style={{ opacity: 0.2, margin: '0 auto 24px', color: 'var(--color-primary)' }} />
                        <p style={{ fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-primary)' }}>Nenhuma cotação localizada.</p>
                    </div>
                ) : (
                    groupedRequests.map((group) => {
                        const isExpanded = expandedRequests.has(group.requestId);
                        const isAssignedToMe = group.buyerId === currentUser?.id;
                        const urgency = getUrgencyStyle(group.needByDateUtc, group.requestStatusCode);
                        const actionBadge = getActionBadge(group.requestStatusCode);
                        const isAdjustmentPhase = group.requestStatusCode === 'AREA_ADJUSTMENT' || group.requestStatusCode === 'FINAL_ADJUSTMENT';
                        const canMutateQuotation = ['DRAFT', 'WAITING_QUOTATION', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'].includes(group.requestStatusCode) && isAssignedToMe;

                        return (
                            <div key={group.requestId} id={`request-group-${group.requestId}`} className={highlightedRequestId === group.requestId ? 'section-attention-highlight' : ''} style={{
                                backgroundColor: 'var(--color-bg-surface)',
                                border: '2px solid var(--color-primary)',
                                boxShadow: 'var(--shadow-brutal)',
                                overflow: 'hidden'
                            }}>
                                {/* Request Header Row */}
                                <div
                                    onClick={() => toggleGroup(group.requestId)}
                                    style={{
                                        padding: '16px 24px',
                                        backgroundColor: 'var(--color-bg-page)',
                                        borderBottom: isExpanded ? '2px solid var(--color-primary)' : 'none',
                                        display: 'flex',
                                        justifyContent: 'space-between',
                                        alignItems: 'center',
                                        cursor: 'pointer',
                                        userSelect: 'none'
                                    }}
                                >
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '20px' }}>
                                        {urgency && (
                                            <Tooltip 
                                                variant="dark" 
                                                content={
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', whiteSpace: 'nowrap' }}>
                                                        <div style={{ width: '10px', height: '10px', backgroundColor: urgency.indicatorColor }} />
                                                        <span style={{ fontWeight: 800, fontSize: '0.75rem' }}>{urgency.description}</span>
                                                    </div>
                                                }
                                            >
                                                <div style={{ width: '4px', height: '32px', backgroundColor: urgency.indicatorColor, borderRadius: '2px', marginRight: '-8px' }} />
                                            </Tooltip>
                                        )}
                                        {isExpanded ? <ChevronDown size={24} /> : <ChevronRight size={24} />}
                                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                                            <span style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Pedido</span>
                                            <span style={{ fontSize: '1.25rem', fontWeight: 900, color: 'var(--color-primary)' }}>{group.requestNumber}</span>
                                        </div>
                                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                                            <span style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Status</span>
                                            <span className={`badge badge-sm badge-${
                                                 group.requestStatusBadgeColor === 'red' ? 'danger' :
                                                 group.requestStatusBadgeColor === 'yellow' ? 'warning' :
                                                 group.requestStatusBadgeColor === 'green' ? 'success' :
                                                 group.requestStatusBadgeColor || 'neutral'
                                             }`} style={{ alignSelf: 'flex-start', marginTop: '2px' }}>
                                                 {group.requestStatusName}
                                             </span>

                                        </div>
                                    </div>

                                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                                        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '2px' }}>
                                            <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Solicitante</span>
                                            <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{group.requesterName}</span>
                                        </div>
                                        {group.buyerName && (
                                            <>
                                                <div style={{ width: '1px', height: '24px', backgroundColor: 'var(--color-border)' }}></div>
                                                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Comprador</span>
                                                    <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-primary)' }}>{group.buyerName}</span>
                                                </div>
                                            </>
                                        )}
                                        <div style={{ width: '1px', height: '24px', backgroundColor: 'var(--color-border)' }}></div>
                                        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '2px' }}>
                                            <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Data Necessária</span>
                                            <span style={{ fontSize: '0.85rem', fontWeight: 700, color: urgency?.indicatorColor || '#f59e0b' }}>
                                                {formatDate(group.needByDateUtc)}
                                            </span>
                                        </div>
                                        {!group.buyerId && (
                                            <span className="badge" style={{ backgroundColor: '#fef3c7', color: '#b45309', border: '1px solid #fde68a', borderRadius: '4px', boxShadow: '2px 2px 0px rgba(0,0,0,0.05)' }}>
                                                Não Atribuído
                                            </span>
                                        )}
                                        <span className={`badge ${actionBadge.className}`} style={{
                                            borderRadius: '4px',
                                            boxShadow: '2px 2px 0px rgba(0,0,0,0.1)'
                                        }}>
                                            {actionBadge.label}
                                        </span>
                                        {group.buyerId !== currentUser?.id && (
                                            <button 
                                                onClick={(e) => { e.stopPropagation(); handleAssignToMe(group.requestId); }}
                                                disabled={isSaving}
                                                style={{ 
                                                    backgroundColor: 'var(--color-primary)', color: '#fff', border: 'none', borderRadius: '4px', padding: '6px 12px', fontSize: '0.75rem', fontWeight: 800, cursor: isSaving ? 'wait' : 'pointer', display: 'flex', alignItems: 'center', gap: '4px', textTransform: 'uppercase', boxShadow: '2px 2px 0px rgba(0,0,0,0.15)' 
                                                }}
                                                title={group.buyerId ? "Reatribuir este pedido para mim" : "Reivindicar pedido para mim"}
                                            >
                                                <UserPlus size={14} /> {group.buyerId ? "Assumir Pedido" : "Atribuir a Mim"}
                                            </button>
                                        )}
                                        <button
                                            type="button"
                                            onClick={(e) => {
                                                e.stopPropagation();
                                                setDrawerRequestId(group.requestId);
                                            }}
                                            style={{ color: 'var(--color-primary)', padding: '8px', background: 'none', border: 'none', cursor: 'pointer' }}
                                            title="Ver Detalhes do Pedido"
                                        >
                                            <ExternalLink size={20} />
                                        </button>
                                    </div>
                                </div>

                                {isExpanded && (
                                    <div style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '24px' }}>

                                        {isAdjustmentPhase && group.latestAdjustmentMessage && (
                                            <div style={{
                                                backgroundColor: '#fffbeb',
                                                border: '2px solid #fbbf24',
                                                borderRadius: 'var(--radius-sm)',
                                                padding: '16px',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                gap: '8px'
                                            }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                                    <span style={{ fontSize: '0.8rem', fontWeight: 900, color: '#b45309', textTransform: 'uppercase' }}>
                                                        REAJUSTE SOLICITADO
                                                    </span>
                                                    <span style={{ fontSize: '0.85rem', color: '#b45309' }}>
                                                        por <strong>{group.latestAdjustmentActor}</strong> ({group.latestAdjustmentRole}) em {formatDate(group.latestAdjustmentDateUtc)}
                                                    </span>
                                                </div>
                                                <div style={{ fontSize: '0.95rem', fontWeight: 600, color: '#92400e', whiteSpace: 'pre-wrap' }}>
                                                    "{group.latestAdjustmentMessage}"
                                                </div>
                                                <div style={{ fontSize: '0.75rem', color: '#b45309', marginTop: '4px' }}>
                                                    Corrija os dados solicitados nos documentos ou itens abaixo e resubmeta o pedido.
                                                </div>
                                            </div>
                                        )}

                                        {/* Row 1: Quotation Metadata Area */}
                                        <div style={{ 
                                            display: 'flex', 
                                            flexDirection: 'column',
                                            gap: '16px', 
                                            paddingBottom: '16px', 
                                            marginBottom: '16px', 
                                            borderBottom: '1px solid var(--color-border)' 
                                        }}>
                                            <div style={{ display: 'flex', gap: '24px', alignItems: 'flex-start' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Planta</span>
                                                    <span style={{ fontSize: '0.9rem', fontWeight: 700 }}>{group.plantName || plants.find(p => String(p.id) === String(group.plantId))?.name || '---'}</span>
                                                </div>
                                                <div style={{ width: '2px', height: '24px', backgroundColor: 'var(--color-border)', alignSelf: 'center' }}></div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Departamento</span>
                                                    <span style={{ fontSize: '0.9rem', fontWeight: 700 }}>{group.departmentName}</span>
                                                </div>
                                                <div style={{ width: '2px', height: '24px', backgroundColor: 'var(--color-border)', alignSelf: 'center' }}></div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', flex: 1 }}>
                                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Título do Pedido</span>
                                                    <span style={{ fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{group.requestTitle || '---'}</span>
                                                </div>
                                            </div>

                                            {(group.requestDescription || group.supportingAttachments.length > 0) && (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                                    {group.requestDescription && (
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                            <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Descrição / Notas do Pedido</span>
                                                            <span style={{ fontSize: '0.85rem', color: 'var(--color-text-body)', whiteSpace: 'pre-wrap', backgroundColor: 'var(--color-bg-page)', padding: '12px', borderRadius: '4px', border: '1px solid var(--color-border)' }}>
                                                                {group.requestDescription}
                                                            </span>
                                                        </div>
                                                    )}
                                                    
                                                    {group.supportingAttachments.length > 0 && (
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                            <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Documentos de Apoio Anexados</span>
                                                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: '12px' }}>
                                                                {group.supportingAttachments.map((att: any) => (
                                                                    <div key={att.id} style={{
                                                                        display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '12px 16px',
                                                                        backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '6px',
                                                                        boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                                                                    }}>
                                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', overflow: 'hidden' }}>
                                                                            <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '8px', borderRadius: '4px', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                                                                                <FileText size={20} color="#1d4ed8" />
                                                                            </div>
                                                                            <div style={{ display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
                                                                                <span style={{ fontWeight: 700, color: '#1e40af', fontSize: '0.85rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={att.fileName}>
                                                                                    {att.fileName}
                                                                                </span>
                                                                                <span style={{ fontSize: '0.7rem', color: '#3b82f6', fontWeight: 700 }}>DOC. APOIO (Solicitante)</span>
                                                                            </div>
                                                                        </div>
                                                                        <div style={{ display: 'flex', gap: '6px', marginLeft: '12px', flexShrink: 0 }}>
                                                                            <a
                                                                                href={`/api/v1/attachments/download/${att.id}`}
                                                                                target="_blank"
                                                                                rel="noopener noreferrer"
                                                                                style={{ color: '#1d4ed8', padding: '6px', border: '1px solid #93c5fd', borderRadius: '4px', display: 'flex', alignItems: 'center', backgroundColor: 'var(--color-bg-surface)', transition: 'all 0.2s' }}
                                                                                onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#dbeafe'; e.currentTarget.style.borderColor = '#60a5fa'; }}
                                                                                onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#fff'; e.currentTarget.style.borderColor = '#93c5fd'; }}
                                                                                title="Baixar Documento"
                                                                            >
                                                                                <ExternalLink size={16} />
                                                                            </a>
                                                                        </div>
                                                                    </div>
                                                                ))}
                                                            </div>
                                                        </div>
                                                    )}
                                                </div>
                                            )}
                                        </div>

                                        {/* SECTION A: Existing Quotations / Documents */}
                                        <div style={{
                                            padding: '24px',
                                            backgroundColor: 'var(--color-bg-page)',
                                            border: '2px solid var(--color-border-heavy)',
                                            borderRadius: 'var(--radius-sm)',
                                            display: 'flex',
                                            flexDirection: 'column',
                                            gap: '20px'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                <FileText size={18} /> SEÇÃO A: DOCUMENTOS E COTAÇÕES REGISTRADAS
                                            </div>

                                            {(group.quotations.length > 0 || group.proformaAttachments.length > 0) ? (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                                    {/* Real Quotations (New Step 7: Improved Visualization & Comparison) */}
                                                    {(() => {
                                                        // Subtle visual cue for lowest total in same currency (Step 7.3)
                                                        const lowestByCurrency: Record<string, number> = {};
                                                        group.quotations.forEach((q: SavedQuotationDto) => {
                                                            if (!lowestByCurrency[q.currency] || q.totalAmount < lowestByCurrency[q.currency]) {
                                                                lowestByCurrency[q.currency] = q.totalAmount;
                                                            }
                                                        });

                                                        return group.quotations.map((q: SavedQuotationDto) => {
                                                            const isExpanded = !!expandedQuotations[q.id];
                                                            const isLowest = group.quotations.length > 1 && q.totalAmount === lowestByCurrency[q.currency];

                                                            return (
                                                                <div key={q.id} style={{
                                                                    backgroundColor: q.isSelected ? '#eef2ff' : '#fff', 
                                                                    border: q.isSelected ? '2px solid #4f46e5' : isLowest ? '2px solid #10b981' : '2px solid #bae6fd', 
                                                                    borderRadius: '8px',
                                                                    display: 'flex', 
                                                                    flexDirection: 'column',
                                                                    boxShadow: q.isSelected ? '0 4px 12px rgba(79, 70, 229, 0.15)' : isLowest ? '0 4px 12px rgba(16, 185, 129, 0.1)' : '0 1px 3px rgba(0,0,0,0.05)',
                                                                    overflow: 'hidden',
                                                                    transition: 'all 0.2s ease',
                                                                    marginBottom: '12px'
                                                                }}>
                                                                    {/* Card Summary Row */}
                                                                    <div 
                                                                        onClick={() => setExpandedQuotations(prev => ({ ...prev, [q.id]: !prev[q.id] }))}
                                                                        style={{
                                                                            padding: '16px', 
                                                                            display: 'flex', 
                                                                            justifyContent: 'space-between', 
                                                                            alignItems: 'center',
                                                                            cursor: 'pointer',
                                                                            backgroundColor: isExpanded ? '#f0f9ff' : 'transparent'
                                                                        }}
                                                                    >
                                                                        <div style={{ display: 'flex', gap: '16px', alignItems: 'center', flex: 1 }}>
                                                                            <div style={{ backgroundColor: isLowest ? '#ecfdf5' : '#e0f2fe', padding: '10px', borderRadius: '8px', color: isLowest ? '#059669' : '#0369a1', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                                                                <FileText size={20} />
                                                                            </div>
                                                                            <div style={{ flex: 1 }}>
                                                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                                                    <div style={{ fontWeight: 900, fontSize: '1rem', color: q.isSelected ? '#3730a3' : isLowest ? '#065f46' : 'var(--color-primary)' }}>{q.supplierNameSnapshot}</div>
                                                                                    {q.isSelected && (
                                                                                        <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                                                                            Cotação Escolhida
                                                                                        </span>
                                                                                    )}
                                                                                    {isLowest && (
                                                                                        <span style={{ backgroundColor: '#10b981', color: '#fff', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                                                                            Menor Valor ({q.currency})
                                                                                        </span>
                                                                                    )}
                                                                                </div>
                                                                                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 700, display: 'flex', gap: '16px', marginTop: '2px' }}>
                                                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                                                        <Hash size={12} /> {q.documentNumber || 'S/N'}
                                                                                    </span>
                                                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                                                        <Calendar size={12} /> {q.documentDate ? formatDate(q.documentDate) : '—'}
                                                                                    </span>
                                                                                    <span style={{ color: '#0369a1', textTransform: 'uppercase' }}>
                                                                                        {q.sourceType === 'OCR' ? '⚡ OCR' : '✍️ MANUAL'}
                                                                                    </span>
                                                                                </div>
                                                                            </div>
                                                                        </div>

                                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '24px' }}>
                                                                            {/* Maintenance Actions (Step 8.1) */}
                                                                            {canMutateQuotation && mode === 'BUYER' && (
                                                                                <div style={{ display: 'flex', gap: '8px' }} onClick={(e) => e.stopPropagation()}>
                                                                                <button 
                                                                                    onClick={() => handleEditQuotation(group.requestId, q)}
                                                                                    style={{
                                                                                        display: 'flex', alignItems: 'center', gap: '4px', padding: '6px 12px',
                                                                                        backgroundColor: 'var(--color-bg-surface)', border: '1px solid #bae6fd', borderRadius: '4px',
                                                                                        color: '#0369a1', fontWeight: 800, fontSize: '0.7rem', cursor: 'pointer',
                                                                                        textTransform: 'uppercase', transition: 'all 0.1s'
                                                                                    }}
                                                                                    onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#f0f9ff'; e.currentTarget.style.borderColor = '#0ea5e9'; }}
                                                                                    onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#fff'; e.currentTarget.style.borderColor = '#bae6fd'; }}
                                                                                >
                                                                                    <Pencil size={12} /> Editar
                                                                                </button>
                                                                                <button 
                                                                                    onClick={() => handleDeleteQuotation(group.requestId, q.id)}
                                                                                    style={{
                                                                                        display: 'flex', alignItems: 'center', gap: '4px', padding: '6px 12px',
                                                                                        backgroundColor: 'var(--color-bg-surface)', border: '1px solid #fecaca', borderRadius: '4px',
                                                                                        color: '#dc2626', fontWeight: 800, fontSize: '0.7rem', cursor: 'pointer',
                                                                                        textTransform: 'uppercase', transition: 'all 0.1s'
                                                                                    }}
                                                                                    onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#fef2f2'; e.currentTarget.style.borderColor = '#ef4444'; }}
                                                                                    onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#fff'; e.currentTarget.style.borderColor = '#fecaca'; }}
                                                                                >
                                                                                    <Trash2 size={12} /> Excluir
                                                                                </button>
                                                                            </div>
                                                                        )}

                                                                        <div style={{ textAlign: 'right' }}>
                                                                            <div style={{ fontSize: '1.25rem', fontWeight: 900, color: isLowest ? '#059669' : '#0369a1', display: 'baseline', gap: '4px' }}>
                                                                                <span style={{ fontSize: '0.8rem', opacity: 0.8 }}>{q.currency}</span>
                                                                                    {formatCurrencyAO(q.totalAmount)}
                                                                                </div>
                                                                                <div style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 700 }}>{q.itemCount} ITENS • {formatDate(q.createdAtUtc)}</div>
                                                                            </div>
                                                                            <div style={{ color: 'var(--color-text-muted)', transform: isExpanded ? 'rotate(180deg)' : 'rotate(0deg)', transition: 'transform 0.2s' }}>
                                                                                <ChevronDown size={20} />
                                                                            </div>
                                                                        </div>
                                                                    </div>

                                                                    {/* Expanded Details Table (Step 7.2) */}
                                                                    {isExpanded && (
                                                                        <div style={{
                                                                            padding: '0 16px 16px 16px',
                                                                            borderTop: '1px solid #e0f2fe',
                                                                            backgroundColor: 'var(--color-bg-page)'
                                                                        }}>
                                                                            <div style={{ marginTop: '32px', border: '1px solid #e2e8f0', borderRadius: '6px', overflow: 'hidden', backgroundColor: 'var(--color-bg-surface)' }}>
                                                                                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
                                                                                    <thead>
                                                                                         <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                                                                                             <th style={{ padding: '12px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em' }}>Descrição do Item</th>
                                                                                            <th style={{ padding: '12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em', width: '60px' }}>Qtd</th>
                                                                                            <th style={{ padding: '12px', textAlign: 'center', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em', width: '60px' }}>Unid.</th>
                                                                                            <th style={{ padding: '12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em', width: '100px' }}>P. Unit</th>
                                                                                            <th style={{ padding: '12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em', width: '100px' }}>Desc. (AOA)</th>
                                                                                            <th style={{ padding: '12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em', width: '100px' }}>IVA</th>
                                                                                            <th style={{ padding: '12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.75rem', letterSpacing: '0.05em', width: '120px' }}>Total do Item</th>
                                                                                        </tr>
                                                                                    </thead>
                                                                                    <tbody>
                                                                                        {q.items && q.items.length > 0 ? q.items.map((item, idx) => (
                                                                                            <tr key={item.id || idx} style={{ borderBottom: '1px solid #f1f5f9' }}>
                                                                                                <td style={{ padding: '12px', fontWeight: 600, color: 'var(--color-primary)' }}>{item.description}</td>
                                                                                                <td style={{ padding: '12px', textAlign: 'right', fontWeight: 700 }}>{item.quantity}</td>
                                                                                                <td style={{ padding: '12px', textAlign: 'center', fontWeight: 700, color: 'var(--color-text-muted)' }}>{item.unitCode || '---'}</td>
                                                                                                <td style={{ padding: '12px', textAlign: 'right', fontWeight: 700 }}>{formatCurrencyAO(item.unitPrice)}</td>
                                                                                                <td style={{ padding: '12px', textAlign: 'right', fontWeight: 700, color: '#ef4444' }}>{item.discountAmount > 0 ? formatCurrencyAO(item.discountAmount) : '---'}</td>
                                                                                                <td style={{ padding: '12px', textAlign: 'right', fontWeight: 600, color: '#0369a1' }}>{item.ivaRatePercent > 0 ? `${item.ivaRatePercent}% (${formatCurrencyAO(item.ivaAmount)})` : 'Isento'}</td>
                                                                                                <td style={{ padding: '12px', textAlign: 'right', fontWeight: 900, backgroundColor: 'var(--color-bg-page)' }}>{formatCurrencyAO(item.lineTotal)}</td>
                                                                                            </tr>

                                                                                        )) : (
                                                                                            <tr>
                                                                                                <td colSpan={7} style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                                                                                    Nenhum item detalhado disponível.
                                                                                                </td>
                                                                                            </tr>
                                                                                        )}
                                                                                    </tbody>
                                                                                    <tfoot>
                                                                                        <tr style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                                                                            <td colSpan={6} style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>SUBTOTAL BRUTO:</td>
                                                                                            <td style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.8rem', color: 'var(--color-text-main)', fontWeight: 800 }}>{formatCurrencyAO(q.totalGrossAmount || 0)}</td>
                                                                                        </tr>
                                                                                        <tr style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                                                                            <td colSpan={6} style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>DESCONTOS GLOBAL:</td>
                                                                                            <td style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.8rem', color: '#dc2626', fontWeight: 800 }}>- {formatCurrencyAO(q.totalDiscountAmount || 0)}</td>
                                                                                        </tr>
                                                                                        <tr style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                                                                            <td colSpan={6} style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>BASE TRIBUTÁVEL:</td>
                                                                                            <td style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.8rem', color: 'var(--color-text-main)', fontWeight: 800 }}>{formatCurrencyAO(q.totalTaxableBase || 0)}</td>
                                                                                        </tr>
                                                                                        <tr style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                                                                            <td colSpan={6} style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>TOTAL IVA:</td>
                                                                                            <td style={{ padding: '8px 12px', textAlign: 'right', fontSize: '0.8rem', color: '#0284c7', fontWeight: 800 }}>{formatCurrencyAO(q.totalIvaAmount || 0)}</td>
                                                                                        </tr>
                                                                                        <tr style={{ backgroundColor: '#f1f5f9', fontWeight: 900, borderTop: '2px solid #cbd5e1' }}>
                                                                                            <td colSpan={6} style={{ padding: '12px', textAlign: 'right', textTransform: 'uppercase', fontSize: '0.75rem', color: 'var(--color-text-main)' }}>TOTAL DA COTAÇÃO ({q.currency}):</td>
                                                                                            <td style={{ padding: '12px', textAlign: 'right', fontSize: '1.1rem', color: 'var(--color-primary)' }}>{formatCurrencyAO(q.totalAmount)}</td>
                                                                                        </tr>
                                                                                    </tfoot>
                                                                                </table>
                                                                            </div>

                                                                            {q.sourceFileName && (
                                                                                <div style={{ marginTop: '12px', display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                                                                    <FileText size={14} />
                                                                                    <span>Arquivo de origem: <strong>{q.sourceFileName}</strong></span>
                                                                                </div>
                                                                            )}
                                                                        </div>
                                                                    )}
                                                                </div>
                                                            );
                                                        });
                                                    })()}

                                                    {/* Proforma Attachments (Legacy/Section A ref) */}
                                                    {group.proformaAttachments.length > 0 && (
                                                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))', gap: '12px', marginTop: group.quotations.length > 0 ? '8px' : 0 }}>
                                                            {group.proformaAttachments.map((att: any) => (
                                                                <div key={att.id} style={{
                                                                    display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '12px 16px',
                                                                    backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: '6px',
                                                                    boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                                                                }}>
                                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', overflow: 'hidden' }}>
                                                                        <div style={{ backgroundColor: '#f0fdf4', padding: '8px', borderRadius: '4px' }}>
                                                                            <FileText size={20} color="#166534" />
                                                                        </div>
                                                                        <div style={{ display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
                                                                            <span style={{ fontWeight: 700, color: 'var(--color-primary)', fontSize: '0.85rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={att.fileName}>
                                                                                {att.fileName}
                                                                            </span>
                                                                            <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>DOC. UPLOAD</span>
                                                                        </div>
                                                                    </div>
                                                                    <div style={{ display: 'flex', gap: '6px', marginLeft: '12px', flexShrink: 0 }}>
                                                                        <a
                                                                            href={`/api/v1/attachments/download/${att.id}`}
                                                                            target="_blank"
                                                                            rel="noopener noreferrer"
                                                                            style={{ color: 'var(--color-primary)', padding: '6px', border: '1px solid var(--color-border)', borderRadius: '4px', display: 'flex', alignItems: 'center', backgroundColor: 'var(--color-bg-surface)' }}
                                                                            title="Baixar Documento"
                                                                        >
                                                                            <ExternalLink size={16} />
                                                                        </a>
                                                                        {canMutateQuotation && mode === 'BUYER' && (
                                                                            <button
                                                                                onClick={() => handleDeleteProforma(att.id)}
                                                                                disabled={isSaving}
                                                                                style={{ background: '#fef2f2', border: '1px solid #fee2e2', color: '#ef4444', padding: '6px', borderRadius: '4px', cursor: isSaving ? 'wait' : 'pointer', display: 'flex', alignItems: 'center' }}
                                                                                title="Remover Documento"
                                                                            >
                                                                                <Trash2 size={16} />
                                                                            </button>
                                                                        )}
                                                                    </div>
                                                                </div>
                                                            ))}
                                                        </div>
                                                    )}


                                                </div>
                                            ) : (
                                                <div style={{ 
                                                    padding: '32px', 
                                                    textAlign: 'center', 
                                                    border: '2px dashed var(--color-border)', 
                                                    borderRadius: '8px',
                                                    backgroundColor: 'var(--color-bg-surface)'
                                                }}>
                                                    <p style={{ margin: 0, color: 'var(--color-text-muted)', fontSize: '0.9rem', fontWeight: 600 }}>
                                                        Nenhuma cotação ou documento registrado para este pedido.
                                                    </p>
                                                    <p style={{ margin: '4px 0 0', color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>
                                                        {isAssignedToMe ? 'Utilize a Seção B abaixo para adicionar a primeira cotação.' : 'Atribua o pedido a você para adicionar cotações e documentos.'}
                                                    </p>
                                                </div>
                                            )}
                                        </div>

                                        {/* SECTION B: Add New Quotation */}
                                        {canMutateQuotation && mode === 'BUYER' && (
                                            <div 
                                                id={`section-b-${group.requestId}`}
                                                className={highlightedRequestId === group.requestId ? 'section-attention-highlight' : ''}
                                                style={{
                                                    padding: '24px',
                                                    backgroundColor: 'var(--color-bg-surface)',
                                                    border: '2px solid var(--color-primary)',
                                                    borderRadius: 'var(--radius-sm)',
                                                    display: 'flex',
                                                    flexDirection: 'column',
                                                    gap: '20px',
                                                    boxShadow: 'var(--shadow-brutal)',
                                                    transition: 'all 0.3s ease'
                                                }}
                                            >
                                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                        <Plus size={18} /> SEÇÃO B: ADICIONAR NOVA COTAÇÃO
                                                    </div>
                                                    {addQuotationMode[group.requestId] && (
                                                        <button 
                                                            onClick={() => {
                                                                setAddQuotationMode(prev => ({ ...prev, [group.requestId]: null }));
                                                                handleResetToSelect(group.requestId);
                                                            }}
                                                            style={{ 
                                                                display: 'flex', alignItems: 'center', gap: '6px', 
                                                                backgroundColor: '#f1f5f9', border: '1px solid #cbd5e1', 
                                                                color: 'var(--color-text-main)', fontWeight: 800, fontSize: '0.75rem', cursor: 'pointer',
                                                                textTransform: 'uppercase', padding: '6px 12px', borderRadius: '4px'
                                                            }}
                                                        >
                                                            <ArrowLeft size={14} /> Voltar/Cancelar
                                                        </button>
                                                    )}
                                                </div>

                                                {!addQuotationMode[group.requestId] ? (
                                                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
                                                        <button
                                                            onClick={() => setAddQuotationMode(prev => ({ ...prev, [group.requestId]: 'UPLOAD' }))}
                                                            style={{
                                                                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '12px', padding: '32px 24px',
                                                                backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: '8px',
                                                                cursor: 'pointer', transition: 'all 0.2s', textAlign: 'center'
                                                            }}
                                                            onMouseOver={(e) => { e.currentTarget.style.borderColor = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = '#f0f9ff'; }}
                                                            onMouseOut={(e) => { e.currentTarget.style.borderColor = 'var(--color-border)'; e.currentTarget.style.backgroundColor = '#f8fafc'; }}
                                                        >
                                                            <Upload size={32} color="var(--color-primary)" />
                                                            <div>
                                                                <div style={{ fontWeight: 800, color: 'var(--color-text-main)', fontSize: '0.9rem', marginBottom: '4px' }}>IMPORTAR DOCUMENTO</div>
                                                                <p style={{ margin: 0, fontSize: '0.75rem', color: 'var(--color-text-muted)', lineHeight: 1.4 }}>Extrair dados de cotação PDF/Imagem usando OCR</p>
                                                            </div>
                                                        </button>

                                                        <button
                                                            onClick={() => {
                                                                const blankDraft: QuotationDraft = {
                                                                    supplierId: null,
                                                                    supplierNameSnapshot: '',
                                                                    documentNumber: '',
                                                                    documentDate: '',
                                                                    currency: '',
                                                                    discountAmount: 0,
                                                                    totalAmount: 0,
                                                                    items: []
                                                                };
                                                                setQuotationDrafts(prev => ({ ...prev, [group.requestId]: blankDraft }));
                                                                setQuotationFlowStep(prev => ({ ...prev, [group.requestId]: 'EDIT_QUOTATION' }));
                                                                setAddQuotationMode(prev => ({ ...prev, [group.requestId]: 'MANUAL' }));
                                                                // Crucial: Clear identity and proforma files for a new manual entry
                                                                setEditingQuotationId(prev => ({ ...prev, [group.requestId]: null }));
                                                                setDraftProformaFiles(prev => {
                                                                    const next = { ...prev };
                                                                    delete next[group.requestId];
                                                                    return next;
                                                                });
                                                            }}
                                                            style={{
                                                                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '12px', padding: '32px 24px',
                                                                backgroundColor: '#fdf4ff', border: '2px solid #f5d0fe', borderRadius: '8px',
                                                                cursor: 'pointer', transition: 'all 0.2s', textAlign: 'center'
                                                            }}
                                                            onMouseOver={(e) => { e.currentTarget.style.borderColor = '#d946ef'; e.currentTarget.style.backgroundColor = '#fae8ff'; }}
                                                            onMouseOut={(e) => { e.currentTarget.style.borderColor = '#f5d0fe'; e.currentTarget.style.backgroundColor = '#fdf4ff'; }}
                                                        >
                                                            <Pencil size={32} color="#d946ef" />
                                                            <div>
                                                                <div style={{ fontWeight: 800, color: 'var(--color-text-main)', fontSize: '0.9rem', marginBottom: '4px' }}>INSERIR MANUALMENTE</div>
                                                                <p style={{ margin: 0, fontSize: '0.75rem', color: 'var(--color-text-muted)', lineHeight: 1.4 }}>Preencher dados da cotação manualmente do zero</p>
                                                            </div>
                                                        </button>
                                                    </div>
                                                ) : (
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                                                        {addQuotationMode[group.requestId] === 'UPLOAD' && quotationFlowStep[group.requestId] !== 'EDIT_QUOTATION' && (
                                                            <div style={{ 
                                                                padding: '24px', backgroundColor: '#f0f9ff', border: '2px dashed #0ea5e9', borderRadius: '8px',
                                                                display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '16px'
                                                            }}>
                                                                <div style={{ textAlign: 'center' }}>
                                                                    <div style={{ fontWeight: 800, color: '#0369a1', fontSize: '0.90rem', textTransform: 'uppercase' }}>Importação de Documento (Proforma)</div>
                                                                    <p style={{ fontSize: '0.8rem', color: '#0c4a6e', margin: '4px 0 0' }}>Selecione o arquivo da cotação para iniciar.</p>
                                                                </div>
                                                                
                                                                {!importSelectedFiles[group.requestId] || importSelectedFiles[group.requestId].length === 0 ? (
                                                                    <label style={{
                                                                        display: 'inline-flex', alignItems: 'center', gap: '10px', backgroundColor: '#0ea5e9', color: '#fff',
                                                                        padding: '12px 24px', borderRadius: '6px', fontWeight: 800, fontSize: '0.85rem', cursor: 'pointer',
                                                                        boxShadow: '0 4px 6px -1px rgba(0,0,0,0.1)'
                                                                    }}>
                                                                        <Upload size={20} /> SELECIONAR ARQUIVO
                                                                        <input type="file" hidden onChange={(e) => { if (e.target.files && e.target.files.length > 0) { handleFileSelectForImport(group.requestId, Array.from(e.target.files)); e.target.value = ''; } }} />
                                                                    </label>
                                                                ) : (
                                                                    <div style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'center' }}>
                                                                        <div style={{ 
                                                                            display: 'flex', alignItems: 'center', gap: '12px', padding: '12px 16px', 
                                                                            backgroundColor: 'var(--color-bg-surface)', border: '1px solid #bae6fd', borderRadius: '6px', width: '100%', maxWidth: '400px'
                                                                        }}>
                                                                            <FileText size={20} color="#0284c7" />
                                                                            <span style={{ flex: 1, fontSize: '0.85rem', fontWeight: 700, color: '#0369a1', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                                                                {importSelectedFiles[group.requestId][0].name}
                                                                            </span>
                                                                            <div style={{ display: 'flex', gap: '4px' }}>
                                                                                <label style={{ color: '#0284c7', padding: '6px', cursor: 'pointer' }} title="Trocar Arquivo">
                                                                                    <Pencil size={16} />
                                                                                    <input type="file" hidden onChange={(e) => { if (e.target.files && e.target.files.length > 0) { handleFileSelectForImport(group.requestId, Array.from(e.target.files)); e.target.value = ''; } }} />
                                                                                </label>
                                                                                <button onClick={() => handleRemoveImportFile(group.requestId)} style={{ color: '#ef4444', padding: '6px', background: 'none', border: 'none', cursor: 'pointer' }} title="Remover Arquivo">
                                                                                    <Trash2 size={16} />
                                                                                </button>
                                                                            </div>
                                                                        </div>

                                                                        <button 
                                                                            onClick={() => handleContinueWithDocument(group.requestId)}
                                                                            disabled={isProcessingOcr[group.requestId]}
                                                                            style={{
                                                                                backgroundColor: 'var(--color-primary)', color: '#fff', padding: '12px 32px', borderRadius: '6px', 
                                                                                fontWeight: 900, fontSize: '0.9rem', border: 'none', cursor: isProcessingOcr[group.requestId] ? 'not-allowed' : 'pointer', 
                                                                                textTransform: 'uppercase', boxShadow: '0 4px 6px -1px rgba(0,0,0,0.1)', marginTop: '8px',
                                                                                display: 'flex', alignItems: 'center', gap: '8px', opacity: isProcessingOcr[group.requestId] ? 0.7 : 1
                                                                            }}
                                                                        >
                                                                            {isProcessingOcr[group.requestId] ? (
                                                                                <>Processando OCR...</>
                                                                            ) : (
                                                                                <>Iniciar OCR no documento <ChevronRight size={18} /></>
                                                                            )}
                                                                        </button>
                                                                    </div>
                                                                )}
                                                            </div>
                                                        )}

                                                        {/* EDITABLE DRAFT FLOW (OCR OR MANUAL) */}
                                                        {quotationFlowStep[group.requestId] === 'EDIT_QUOTATION' && (
                                                            <div className="quotation-review-area" style={{ border: '2px solid #e2e8f0', borderRadius: '8px', padding: '20px' }}>
                                                                <div className="review-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px', paddingBottom: '16px', borderBottom: '1px solid #e2e8f0' }}>
                                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                                                        <div style={{ 
                                                                            backgroundColor: addQuotationMode[group.requestId] === 'MANUAL' ? '#d946ef' : '#0ea5e9',
                                                                            color: '#fff', padding: '6px 12px', borderRadius: '4px', fontWeight: 900, fontSize: '0.7rem', textTransform: 'uppercase'
                                                                        }}>
                                                                            {addQuotationMode[group.requestId] === 'MANUAL' ? 'MODO MANUAL' : 'MODO OCR'}
                                                                        </div>
                                                                        <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                                                                            {editingQuotationId[group.requestId] ? 'Editar Cotação' : 'Registrar Nova Cotação'}
                                                                        </h3>
                                                                        {editingQuotationId[group.requestId] && (
                                                                            <span style={{ 
                                                                                backgroundColor: '#fef3c7', color: '#92400e', padding: '4px 10px', 
                                                                                borderRadius: '4px', fontSize: '0.65rem', fontWeight: 900, 
                                                                                border: '1px solid #fcd34d', textTransform: 'uppercase'
                                                                            }}>
                                                                                Modo: Editando Cotação
                                                                            </span>
                                                                        )}
                                                                    </div>
                                                                    
                                                                    <div className="review-actions" style={{ display: 'flex', gap: '12px' }}>
                                                                        {addQuotationMode[group.requestId] === 'UPLOAD' && (
                                                                            <button 
                                                                                onClick={() => handleRestoreOcrOriginal(group.requestId)}
                                                                                style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 16px', border: '1px solid #e2e8f0', borderRadius: '6px', fontSize: '0.8rem', fontWeight: 700, backgroundColor: 'var(--color-bg-surface)', cursor: 'pointer' }}
                                                                                title="Restaurar sugestões originais do OCR"
                                                                            >
                                                                                <RefreshCcw size={16} /> Restaurar OCR
                                                                            </button>
                                                                        )}
                                                                    </div>
                                                                </div>

                                                                {ocrErrors[group.requestId] && (
                                                                    <div style={{ backgroundColor: '#fef2f2', padding: '12px', borderRadius: '6px', border: '1px solid #fee2e2', color: '#b91c1c', fontSize: '0.8rem', fontWeight: 600, display: 'flex', gap: '10px', marginBottom: '20px' }}>
                                                                        <AlertCircle size={18} /> {ocrErrors[group.requestId]}
                                                                    </div>
                                                                )}

                                                                {!quotationDrafts[group.requestId] && isProcessingOcr[group.requestId] ? (
                                                                    <div style={{ padding: '40px', textAlign: 'center' }}>
                                                                        <div className="animate-spin" style={{ width: '32px', height: '32px', border: '4px solid #f1f5f9', borderTopColor: 'var(--color-primary)', borderRadius: '50%', margin: '0 auto 16px' }}></div>
                                                                        <span style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>Extraindo dados do documento...</span>
                                                                    </div>
                                                                ) : quotationDrafts[group.requestId] ? (
                                                                    <div className="quotation-form-body">
                                                                        {/* Phase 2: Reconciliation Panel */}
                                                                        {reconciliationBatches[group.requestId] && group.items.length > 0 && (
                                                                            <ReconciliationPanel
                                                                                requestId={group.requestId}
                                                                                batch={reconciliationBatches[group.requestId]}
                                                                                onBatchUpdated={(updated) => setReconciliationBatches(prev => ({ ...prev, [group.requestId]: updated }))}
                                                                            />
                                                                        )}
                                                                        <div className="form-header-grid" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '20px', marginBottom: '24px', backgroundColor: 'var(--color-bg-page)', padding: '20px', borderRadius: '8px' }}>
                                                                            <div style={{ gridColumn: 'span 3' }}>
                                                                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                                                                                    <label style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Fornecedor <span style={{ color: '#ef4444' }}>*</span></label>
                                                                                    <button
                                                                                        onClick={() => setQuickSupplierModal({
                                                                                            show: true,
                                                                                            requestId: group.requestId,
                                                                                            initialName: quotationDrafts[group.requestId].supplierNameSnapshot,
                                                                                            initialTaxId: quotationDrafts[group.requestId].supplierTaxId || ''
                                                                                        })}
                                                                                        style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', padding: 0, textDecoration: 'underline' }}
                                                                                    >
                                                                                        + NOVO FORNECEDOR
                                                                                    </button>
                                                                                </div>
                                                                                <SupplierAutocomplete
                                                                                    initialName={quotationDrafts[group.requestId].supplierNameSnapshot}
                                                                                    onChange={(id, name) => {
                                                                                        handleUpdateQuotationHeader(group.requestId, 'supplierId', id || null);
                                                                                        handleUpdateQuotationHeader(group.requestId, 'supplierNameSnapshot', name || '');
                                                                                    }}
                                                                                />
                                                                                {!quotationDrafts[group.requestId].supplierId && quotationDrafts[group.requestId].supplierNameSnapshot && (
                                                                                    <div style={{ marginTop: '8px', padding: '10px 12px', backgroundColor: '#fff7ed', border: '1px solid #ffedd5', borderRadius: '4px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                                                            <AlertCircle size={16} color="#c2410c" />
                                                                                            <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#9a3412' }}>
                                                                                                Fornecedor sugerido <strong>"{quotationDrafts[group.requestId].supplierNameSnapshot}"</strong> não encontrado no sistema.
                                                                                            </span>
                                                                                        </div>
                                                                                        <button
                                                                                            onClick={() => setQuickSupplierModal({
                                                                                                show: true,
                                                                                                requestId: group.requestId,
                                                                                                initialName: quotationDrafts[group.requestId].supplierNameSnapshot,
                                                                                                initialTaxId: quotationDrafts[group.requestId].supplierTaxId || ''
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
                                                                                {formErrors[group.requestId]?.supplierId && (
                                                                                    <div style={{ color: '#ef4444', fontSize: '0.75rem', fontWeight: 700, marginTop: '4px' }}>{formErrors[group.requestId].supplierId}</div>
                                                                                )}
                                                                            </div>
                                                                            <div>
                                                                                <label style={{ display: 'block', fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '6px' }}>Nº Documento <span style={{ color: '#ef4444' }}>*</span></label>
                                                                                <input 
                                                                                    className="form-input" 
                                                                                    value={quotationDrafts[group.requestId].documentNumber}
                                                                                    onChange={(e) => handleUpdateQuotationHeader(group.requestId, 'documentNumber', e.target.value)}
                                                                                    placeholder="Ex: INV-123"
                                                                                    style={{ width: '100%', padding: '10px', border: `1px solid ${formErrors[group.requestId]?.documentNumber ? '#ef4444' : 'var(--color-border)'}`, borderRadius: '4px', fontSize: '0.85rem' }}
                                                                                />
                                                                                {formErrors[group.requestId]?.documentNumber && (
                                                                                    <div style={{ color: '#ef4444', fontSize: '0.75rem', fontWeight: 700, marginTop: '4px' }}>{formErrors[group.requestId].documentNumber}</div>
                                                                                )}
                                                                            </div>
                                                                            <div style={{ gridColumn: 'span 2' }}>
                                                                                 <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-primary)', textTransform: 'uppercase', marginBottom: '8px' }}>
                                                                                     Documento da Cotação / Proforma
                                                                                     <span style={{ color: '#ef4444', fontWeight: 800, textTransform: 'none', marginLeft: '6px' }}>(Obrigatório para Salvar e Concluir)</span>
                                                                                 </label>
                                                                                 
                                                                                 {(() => {
                                                                                     const newFile = draftProformaFiles[group.requestId];
                                                                                     const existingId = quotationDrafts[group.requestId]?.proformaAttachmentId;
                                                                                     
                                                                                     // Case 1: New file selected (uploading)
                                                                                     if (newFile) {
                                                                                         return (
                                                                                             <div style={{ display: 'flex', alignItems: 'center', gap: '12px', padding: '8px 12px', backgroundColor: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '4px' }}>
                                                                                                 <FileText size={18} color="#166534" />
                                                                                                 <span style={{ fontSize: '0.8rem', fontWeight: 700, color: '#166534', flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                                                                     {newFile.name}
                                                                                                 </span>
                                                                                                 <button 
                                                                                                     onClick={() => setDraftProformaFiles(prev => { const n = { ...prev }; delete n[group.requestId]; return n; })}
                                                                                                     style={{ color: '#ef4444', background: 'none', border: 'none', cursor: 'pointer', padding: '4px' }}
                                                                                                     title="Remover Arquivo"
                                                                                                 >
                                                                                                     <Trash2 size={16} />
                                                                                                 </button>
                                                                                             </div>
                                                                                         );
                                                                                     }
                                                                                     
                                                                                     // Case 2: Existing proforma from DB (edit mode)
                                                                                     if (existingId) {
                                                                                         // Map existingId to attachment info if available in group.quotations or proformaAttachments
                                                                                         const attInfo = group.quotations.find((q: any) => q.proformaAttachmentId === existingId) || 
                                                                                                       group.proformaAttachments.find((a: any) => a.id === existingId);
                                                                                         
                                                                                         return (
                                                                                             <div style={{ display: 'flex', alignItems: 'center', gap: '12px', padding: '8px 12px', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '4px' }}>
                                                                                                 <FileText size={18} color="#1e40af" />
                                                                                                 <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
                                                                                                     <span style={{ fontSize: '0.75rem', fontWeight: 700, color: '#1e40af', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                                                                         {attInfo?.fileName || 'Documento Vinculado'}
                                                                                                     </span>
                                                                                                     <a 
                                                                                                         href={`/api/v1/attachments/download/${existingId}`} 
                                                                                                         target="_blank" 
                                                                                                         rel="noopener noreferrer"
                                                                                                         style={{ fontSize: '0.65rem', color: '#2563eb', fontWeight: 700, textDecoration: 'underline' }}
                                                                                                     >
                                                                                                         Ver documento atual
                                                                                                     </a>
                                                                                                 </div>
                                                                                                 <button 
                                                                                                     onClick={() => handleUpdateQuotationHeader(group.requestId, 'proformaAttachmentId', null)}
                                                                                                     style={{ display: 'flex', alignItems: 'center', gap: '4px', border: '1px solid #fee2e2', backgroundColor: 'var(--color-bg-surface)', color: '#ef4444', padding: '4px 8px', borderRadius: '4px', cursor: 'pointer', fontWeight: 800, fontSize: '0.65rem', textTransform: 'uppercase' }}
                                                                                                 >
                                                                                                     <Trash2 size={12} /> Remover e Trocar
                                                                                                 </button>
                                                                                             </div>
                                                                                         );
                                                                                     }
                                                                                     
                                                                                     // Case 3: No file (mandatory state)
                                                                                     return (
                                                                                         <label style={{
                                                                                             display: 'inline-flex', alignItems: 'center', gap: '8px', 
                                                                                             padding: '8px 16px', backgroundColor: '#fef2f2', color: '#b91c1c', 
                                                                                             border: '1px dashed #f87171', borderRadius: '4px', cursor: 'pointer',
                                                                                             fontSize: '0.8rem', fontWeight: 700
                                                                                         }}>
                                                                                             <Upload size={16} /> Selecionar Arquivo
                                                                                             <input 
                                                                                                 type="file" 
                                                                                                 hidden 
                                                                                                 onChange={(e) => { 
                                                                                                     if (e.target.files && e.target.files.length > 0) { 
                                                                                                         const file = e.target.files[0];
                                                                                                         setDraftProformaFiles(prev => ({ ...prev, [group.requestId]: file })); 
                                                                                                         e.target.value = ''; 
                                                                                                     } 
                                                                                                 }} 
                                                                                             />
                                                                                         </label>
                                                                                     );
                                                                                 })()}
                                                                            </div>
                                                                            <div style={{ gridColumn: '1 / -1', height: '1px', backgroundColor: 'var(--color-border)' }}></div>
                                                                            <div>
                                                                                <label style={{ display: 'block', fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '6px' }}>Data <span style={{ color: '#ef4444' }}>*</span></label>
                                                                                <input 
                                                                                    type="date"
                                                                                    className="form-input" 
                                                                                    value={quotationDrafts[group.requestId].documentDate}
                                                                                    onChange={(e) => handleUpdateQuotationHeader(group.requestId, 'documentDate', e.target.value)}
                                                                                    style={{ width: '100%', padding: '9px', border: `1px solid ${formErrors[group.requestId]?.documentDate ? '#ef4444' : 'var(--color-border)'}`, borderRadius: '4px', fontSize: '0.85rem' }}
                                                                                />
                                                                                {formErrors[group.requestId]?.documentDate && (
                                                                                    <div style={{ color: '#ef4444', fontSize: '0.75rem', fontWeight: 700, marginTop: '4px' }}>{formErrors[group.requestId].documentDate}</div>
                                                                                )}
                                                                            </div>
                                                                            <div>
                                                                                 <label style={{ display: 'block', fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '6px' }}>Moeda <span style={{ color: '#ef4444' }}>*</span></label>
                                                                                 <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                                                     <select 
                                                                                         className="form-input"
                                                                                         value={quotationDrafts[group.requestId].currency}
                                                                                         onChange={(e) => handleUpdateQuotationHeader(group.requestId, 'currency', e.target.value)}
                                                                                         style={{ 
                                                                                             width: '100%', 
                                                                                             padding: '10px', 
                                                                                             border: (formErrors[group.requestId]?.currency) ? '1px solid #ef4444' : (quotationDrafts[group.requestId].extractedCurrency && !quotationDrafts[group.requestId].currency ? '2px solid #f97316' : '1px solid var(--color-border)'), 
                                                                                             borderRadius: '4px', 
                                                                                             fontSize: '0.85rem' 
                                                                                         }}
                                                                                     >
                                                                                         <option value="">Selecione a moeda...</option>
                                                                                         {currencies.map(c => (
                                                                                             <option key={c.id} value={c.code}>{c.code} - {c.symbol}</option>
                                                                                         ))}
                                                                                     </select>
                                                                                     {quotationDrafts[group.requestId].extractedCurrency && !quotationDrafts[group.requestId].currency && (
                                                                                         <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' }}>
                                                                                             <span style={{ fontSize: '0.65rem', color: '#f97316', fontWeight: 700 }}>
                                                                                                 Sugestão: {quotationDrafts[group.requestId].extractedCurrency}
                                                                                             </span>
                                                                                             <button 
                                                                                                 onClick={() => setQuickCurrencyModal({ show: true, requestId: group.requestId, initialCode: quotationDrafts[group.requestId].extractedCurrency || '' })}
                                                                                                 style={{ 
                                                                                                     fontSize: '0.6rem', 
                                                                                                     fontWeight: 900, 
                                                                                                     color: '#fff', 
                                                                                                     backgroundColor: '#f97316', 
                                                                                                     border: 'none', 
                                                                                                     padding: '2px 6px', 
                                                                                                     borderRadius: '3px', 
                                                                                                     cursor: 'pointer',
                                                                                                     textTransform: 'uppercase'
                                                                                                 }}
                                                                                             >
                                                                                                 CRIAR AGORA
                                                                                             </button>
                                                                                         </div>
                                                                                     )}
                                                                                 </div>
                                                                                 {formErrors[group.requestId]?.currency && (
                                                                                     <div style={{ color: '#ef4444', fontSize: '0.75rem', fontWeight: 700, marginTop: '4px' }}>{formErrors[group.requestId].currency}</div>
                                                                                 )}
                                                                             </div>
                                                                        </div>

                                                                        <div className="form-items-area" style={{ marginBottom: '24px' }}>
                                                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                                                                <h4 style={{ margin: 0, fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Itens da Cotação</h4>
                                                                                <button 
                                                                                    onClick={() => handleAddQuotationItem(group.requestId)}
                                                                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.75rem', background: 'none', border: 'none', cursor: 'pointer' }}
                                                                                >
                                                                                    <Plus size={16} /> ADICIONAR ITEM
                                                                                </button>
                                                                            </div>

                                                                            <div className="draft-items-list" style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                                                                                {quotationDrafts[group.requestId].items.length === 0 ? (
                                                                                    <div style={{ padding: '32px', textAlign: 'center', backgroundColor: 'var(--color-bg-page)', border: '1px dashed #d1d5db', borderRadius: '8px' }}>
                                                                                        <Plus size={24} style={{ margin: '0 auto 8px', color: '#9ca3af' }} />
                                                                                        <p style={{ margin: 0, fontSize: '0.85rem', color: '#6b7280', fontWeight: 600 }}>Nenhum item adicionado à cotação.</p>
                                                                                        <button 
                                                                                            onClick={() => handleAddQuotationItem(group.requestId)}
                                                                                            style={{ marginTop: '12px', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline' }}
                                                                                        >
                                                                                            Clique para adicionar o primeiro item
                                                                                        </button>
                                                                                    </div>
                                                                                ) : (
                                                                                    <>
                                                                                        <div style={{ display: 'grid', gridTemplateColumns: '32px 40px minmax(150px, 1fr) 60px 80px 100px 100px 110px 120px 40px', gap: '8px', padding: '0 12px', marginBottom: '4px' }}>
                                                                                            <span></span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', textAlign: 'center' }}>#</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Descrição do Item</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', textAlign: 'right' }}>Qtd</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Unid</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', textAlign: 'right' }}>P. Unit</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', textAlign: 'right' }}>Desc. (AOA)</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Taxa IVA</span>
                                                                                            <span style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', textAlign: 'right' }}>Total ({quotationDrafts[group.requestId].currency})</span>
                                                                                            <span></span>
                                                                                        </div>
                                                                                        {quotationDrafts[group.requestId].items.map((item, idx) => (
                                                                                            <div key={idx} style={{ 
                                                                                                display: 'grid', gridTemplateColumns: '32px 40px minmax(150px, 1fr) 60px 80px 100px 100px 110px 120px 40px', gap: '8px', alignItems: 'center', padding: '12px', 
                                                                                                backgroundColor: item.isChecked ? '#ECFDF5' : 'var(--color-bg-surface)', 
                                                                                                border: item.isChecked ? '1px solid #10B981' : '1px solid #e2e8f0', 
                                                                                                borderRadius: '6px',
                                                                                                transition: 'all 0.2s ease'
                                                                                            }}>
                                                                                                <div style={{ display: 'flex', justifyContent: 'center' }}>
                                                                                                    <input 
                                                                                                        type="checkbox"
                                                                                                        checked={item.isChecked || false}
                                                                                                        onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'isChecked', e.target.checked)}
                                                                                                        title="Marque esta linha para sinalizar que você já validou este item contra o documento físico."
                                                                                                        style={{ width: '16px', height: '16px', cursor: 'pointer', accentColor: '#10B981' }}
                                                                                                    />
                                                                                                </div>
                                                                                                <div style={{ fontSize: '0.8rem', fontWeight: 800, color: 'var(--color-text-muted)', textAlign: 'center' }}>{item.lineNumber}</div>
                                                                                                <input 
                                                                                                    value={item.description}
                                                                                                    onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'description', e.target.value)}
                                                                                                    placeholder="Ex: Item de Teste"
                                                                                                    style={{ padding: '8px', border: '1px solid #e2e8f0', borderRadius: '4px', fontSize: '0.8rem' }}
                                                                                                />
                                                                                                <input 
                                                                                                    type="number"
                                                                                                    value={item.quantity || ''}
                                                                                                    onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'quantity', parseFloat(e.target.value) || 0)}
                                                                                                    style={{ padding: '8px', border: '1px solid #e2e8f0', borderRadius: '4px', fontSize: '0.8rem', textAlign: 'right', WebkitAppearance: 'none', MozAppearance: 'textfield' }}
                                                                                                />
                                                                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                                                                    <select
                                                                                                        value={item.unitId || ''}
                                                                                                        onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'unitId', e.target.value ? parseInt(e.target.value) : null)}
                                                                                                        style={{ padding: '8px', border: item.unit && !item.unitId ? '2px solid #f97316' : '1px solid #e2e8f0', borderRadius: '4px', fontSize: '0.75rem', backgroundColor: 'var(--color-bg-page)', width: '100%' }}
                                                                                                    >
                                                                                                        <option value="">...</option>
                                                                                                        {units.filter(u => u.isActive !== false || u.id === item.unitId).map(u => (
                                                                                                            <option key={u.id} value={u.id}>{u.code}</option>
                                                                                                        ))}
                                                                                                    </select>
                                                                                                    {item.unit && !item.unitId && (
                                                                                                        <span style={{ fontSize: '0.6rem', color: '#f97316', fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={`Extraído: ${item.unit}`}>
                                                                                                            Sugestão: {item.unit}
                                                                                                        </span>
                                                                                                    )}
                                                                                                </div>
                                                                                                <input 
                                                                                                    type="number"
                                                                                                    value={item.unitPrice || ''}
                                                                                                    onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'unitPrice', parseFloat(e.target.value) || 0)}
                                                                                                    style={{ padding: '8px', border: '1px solid #e2e8f0', borderRadius: '4px', fontSize: '0.8rem', textAlign: 'right', WebkitAppearance: 'none', MozAppearance: 'textfield' }}
                                                                                                />
                                                                                                <input 
                                                                                                    type="number"
                                                                                                    value={item.discountAmount || ''}
                                                                                                    onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'discountAmount', parseFloat(e.target.value) || 0)}
                                                                                                    style={{ padding: '8px', border: '1px solid #e2e8f0', borderRadius: '4px', fontSize: '0.8rem', textAlign: 'right', WebkitAppearance: 'none', MozAppearance: 'textfield' }}
                                                                                                />
                                                                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                                                                     <select
                                                                                                         value={item.ivaRateId || ''}
                                                                                                         onChange={(e) => handleUpdateQuotationItem(group.requestId, idx, 'ivaRateId', e.target.value ? parseInt(e.target.value) : null)}
                                                                                                         style={{
                                                                                                             padding: '8px',
                                                                                                             border: item.ivaUncertain
                                                                                                                 ? '2px solid #ef4444'
                                                                                                                 : (item.taxRate !== undefined && !item.ivaRateId ? '2px solid #f97316' : '1px solid #e2e8f0'),
                                                                                                             borderRadius: '4px',
                                                                                                             fontSize: '0.75rem',
                                                                                                             backgroundColor: item.ivaUncertain ? 'rgba(239,68,68,0.06)' : 'var(--color-bg-page)',
                                                                                                             width: '100%'
                                                                                                         }}
                                                                                                         title={item.ivaUncertain ? 'IVA não identificado pelo OCR. Verifique manualmente.' : undefined}
                                                                                                     >
                                                                                                         <option value="">Selecione IVA...</option>
                                                                                                         {ivaRates.filter(r => r.isActive).map(r => (
                                                                                                             <option key={r.id} value={r.id}>{r.name} ({r.ratePercent}%)</option>
                                                                                                         ))}
                                                                                                     </select>
                                                                                                     {item.ivaUncertain && (
                                                                                                         <span style={{ fontSize: '0.6rem', color: '#ef4444', fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', display: 'flex', alignItems: 'center', gap: '3px' }} title="O OCR não conseguiu identificar o IVA para este item. Verifique manualmente.">
                                                                                                             <AlertCircle size={10} style={{ flexShrink: 0 }} />
                                                                                                             IVA não identificado
                                                                                                         </span>
                                                                                                     )}
                                                                                                     {!item.ivaUncertain && item.taxRate !== undefined && !item.ivaRateId && (
                                                                                                         <span style={{ fontSize: '0.6rem', color: '#f97316', fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={`Extraído: ${item.taxRate}%`}>
                                                                                                             Sugestão: {item.taxRate}%
                                                                                                         </span>
                                                                                                     )}
                                                                                                 </div>
                                                                                                <div style={{ textAlign: 'right', fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                                                                                    {formatCurrencyAO(item.totalPrice)}
                                                                                                </div>
                                                                                                <button 
                                                                                                    onClick={() => handleRemoveQuotationItem(group.requestId, idx)}
                                                                                                    style={{ color: '#ef4444', background: 'none', border: 'none', cursor: 'pointer', display: 'flex', justifyContent: 'center' }}
                                                                                                >
                                                                                                    <X size={16} />
                                                                                                </button>
                                                                                            </div>
                                                                                        ))}
                                                                                    </>
                                                                                )}
                                                                            </div>
                                                                        </div>

                                                                        {/* IVA Uncertainty Contextual Banner */}
                                                                        {quotationDrafts[group.requestId]?.headerHasIva && quotationDrafts[group.requestId]?.items.some(i => i.ivaUncertain) && (
                                                                            <div style={{
                                                                                display: 'flex', alignItems: 'center', gap: '8px',
                                                                                padding: '10px 16px', borderRadius: '8px',
                                                                                background: 'rgba(239,68,68,0.06)',
                                                                                border: '1px solid rgba(239,68,68,0.2)',
                                                                                fontSize: '0.8rem', color: '#dc2626',
                                                                                marginBottom: '8px'
                                                                            }}>
                                                                                <AlertTriangle size={16} style={{ flexShrink: 0 }} />
                                                                                <span>
                                                                                    <strong>Atenção:</strong> O documento contém IVA nos totais, mas o IVA por item não foi identificado pelo OCR.
                                                                                    Verifique e corrija manualmente o campo IVA de cada item antes de salvar.
                                                                                </span>
                                                                            </div>
                                                                        )}

                                                                        <div className="form-footer" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', backgroundColor: '#f0f9ff', borderRadius: '8px', border: '1px solid #bae6fd' }}>
                                                                            <div style={{ display: 'flex', gap: '32px', flex: 1 }}>
                                                                                <div style={{ textAlign: 'left' }}>
                                                                                    <div style={{ fontSize: '0.7rem', fontWeight: 900, color: '#0369a1', textTransform: 'uppercase' }}>Subtotal Bruto</div>
                                                                                    <div style={{ fontSize: '1rem', fontWeight: 800, color: '#0c4a6e' }}>
                                                                                        {(() => {
                                                                                            const items = quotationDrafts[group.requestId]?.items || [];
                                                                                            const gross = items.reduce((sum, item) => sum + ((item.quantity||0) * (item.unitPrice||0)), 0);
                                                                                            return formatCurrencyAO(gross);
                                                                                        })()}
                                                                                    </div>
                                                                                </div>
                                                                                <div style={{ textAlign: 'left' }}>
                                                                                    <div style={{ fontSize: '0.7rem', fontWeight: 900, color: '#0369a1', textTransform: 'uppercase', marginBottom: '4px' }}>Valor Desc. Itens</div>
                                                                                    <div style={{ fontSize: '1rem', fontWeight: 800, color: '#dc2626' }}>
                                                                                        {(() => {
                                                                                            const items = quotationDrafts[group.requestId]?.items || [];
                                                                                            const totalDiscount = items.reduce((sum, item) => sum + (Number(item.discountAmount) || 0), 0);
                                                                                            return formatCurrencyAO(totalDiscount);
                                                                                        })()}
                                                                                    </div>
                                                                                </div>
                                                                                <div style={{ textAlign: 'left' }}>
                                                                                    <div style={{ fontSize: '0.7rem', fontWeight: 900, color: '#0369a1', textTransform: 'uppercase', marginBottom: '4px' }}>Desconto Global</div>
                                                                                    <input 
                                                                                        type="number" 
                                                                                        min="0"
                                                                                        step="0.01"
                                                                                        value={quotationDrafts[group.requestId]?.discountAmount || 0}
                                                                                        onChange={(e) => handleUpdateQuotationHeader(group.requestId, 'discountAmount', parseFloat(e.target.value) || 0)}
                                                                                        style={{ width: '120px', padding: '6px', border: '1px solid #e2e8f0', borderRadius: '4px', fontSize: '0.9rem', fontWeight: 600, color: '#dc2626', backgroundColor: 'var(--color-bg-surface)' }}
                                                                                    />
                                                                                </div>
                                                                                <div style={{ textAlign: 'left' }}>
                                                                                    <div style={{ fontSize: '0.7rem', fontWeight: 900, color: '#0369a1', textTransform: 'uppercase' }}>Valor Total IVA</div>
                                                                                    <div style={{ fontSize: '1rem', fontWeight: 800, color: '#0284c7' }}>
                                                                                        {(() => {
                                                                                            const draft = quotationDrafts[group.requestId];
                                                                                            const items = draft?.items || [];
                                                                                            const iva = items.reduce((sum, item) => {
                                                                                                const itemGross = (item.quantity||0) * (item.unitPrice||0);
                                                                                                const itemDiscount = Number(item.discountAmount) || 0;
                                                                                                const netSubtotal = Math.max(0, itemGross - itemDiscount);
                                                                                                const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
                                                                                                const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
                                                                                                return sum + Math.round(netSubtotal * (ivaPercent / 100) * 100) / 100;
                                                                                            }, 0);
                                                                                            return formatCurrencyAO(iva);
                                                                                        })()}
                                                                                    </div>
                                                                                </div>
                                                                                <div style={{ textAlign: 'right', flex: 1 }}>
                                                                                    <div style={{ fontSize: '0.7rem', fontWeight: 900, color: '#0369a1', textTransform: 'uppercase' }}>Valor Total da Cotação</div>
                                                                                    <div style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-primary)' }}>
                                                                                        {formatCurrencyAO(quotationDrafts[group.requestId].totalAmount)}
                                                                                    </div>
                                                                                </div>
                                                                            </div>
                                                                            <div style={{ display: 'flex', gap: '12px', marginLeft: '40px' }}>
                                                                                <button 
                                                                                    onClick={() => handleCancelQuotation(group.requestId)}
                                                                                    disabled={isSaving}
                                                                                    style={{ 
                                                                                        display: 'flex', 
                                                                                        alignItems: 'center', 
                                                                                        gap: '8px', 
                                                                                        backgroundColor: 'var(--color-bg-surface)', 
                                                                                        color: '#64748b', 
                                                                                        padding: '12px 24px', 
                                                                                        borderRadius: '6px', 
                                                                                        fontWeight: 800, 
                                                                                        fontSize: '0.9rem', 
                                                                                        border: '2px solid #e2e8f0', 
                                                                                        cursor: isSaving ? 'not-allowed' : 'pointer',
                                                                                        opacity: isSaving ? 0.6 : 1,
                                                                                        transition: 'all 0.2s'
                                                                                    }}
                                                                                >
                                                                                    <X size={20} /> CANCELAR
                                                                                </button>
                                                                                <button 
                                                                                     onClick={() => handleSaveQuotation(group.requestId)}
                                                                                     disabled={isSaving || !quotationDrafts[group.requestId].supplierId}
                                                                                     style={{ 
                                                                                         display: 'flex', 
                                                                                         alignItems: 'center', 
                                                                                         gap: '8px', 
                                                                                         backgroundColor: editingQuotationId[group.requestId] ? '#f97316' : '#059669', // Orange for edit (Step 9.5)
                                                                                         color: '#fff', 
                                                                                         padding: '12px 32px', 
                                                                                         borderRadius: '6px', 
                                                                                         fontWeight: 900, 
                                                                                         fontSize: '0.9rem', 
                                                                                         border: 'none', 
                                                                                         cursor: (isSaving || !quotationDrafts[group.requestId].supplierId) ? 'not-allowed' : 'pointer', 
                                                                                         opacity: (isSaving || !quotationDrafts[group.requestId].supplierId) ? 0.6 : 1, 
                                                                                         transition: 'all 0.2s',
                                                                                         boxShadow: '0 4px 6px -1px rgba(0,0,0,0.1)'
                                                                                     }}
                                                                                 >
                                                                                     {isSaving ? (
                                                                                         <>Processando...</>
                                                                                     ) : (
                                                                                         <>
                                                                                             <CheckCircle2 size={20} /> {editingQuotationId[group.requestId] ? 'ATUALIZAR COTAÇÃO' : 'SALVAR COTAÇÃO'}
                                                                                         </>
                                                                                     )}
                                                                                 </button>
                                                                            </div>
                                                                        </div>
                                                                        {!quotationDrafts[group.requestId].supplierId && (
                                                                            <p style={{ textAlign: 'right', fontSize: '0.75rem', color: '#b91c1c', fontWeight: 700, marginTop: '8px' }}>
                                                                                * Selecione um fornecedor para habilitar o salvamento.
                                                                            </p>
                                                                        )}
                                                                    </div>
                                                                ) : null}
                                                            </div>
                                                        )}
                                                    </div>
                                                )}
                                            </div>
                                        )}

                                        {/* Action Bar (Operational) */}
                                        <div style={{ 
                                            display: 'flex', 
                                            justifyContent: 'space-between', 
                                            alignItems: 'center',
                                            padding: '16px 24px',
                                            backgroundColor: group.requestStatusCode === 'WAITING_QUOTATION' ? '#fffbeb' : '#f1f5f9',
                                            border: '1px solid',
                                            borderColor: group.requestStatusCode === 'WAITING_QUOTATION' ? '#fef3c7' : 'var(--color-border)',
                                            borderRadius: '6px'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                                <div style={{ 
                                                    backgroundColor: group.requestStatusCode === 'WAITING_QUOTATION' ? '#f59e0b' : '#64748b',
                                                    color: '#fff',
                                                    padding: '4px 10px',
                                                    borderRadius: '4px',
                                                    fontSize: '0.7rem',
                                                    fontWeight: 900,
                                                    textTransform: 'uppercase'
                                                }}>
                                                    AÇÕES DO PEDIDO
                                                </div>
                                                <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                                    {group.requestStatusCode === 'WAITING_QUOTATION' 
                                                        ? 'Finalizar etapa de cotação?' 
                                                        : 'Status atual: ' + group.requestStatusName}
                                                </span>
                                            </div>

                                            <div style={{ display: 'flex', gap: '12px' }}>
                                                {!isAssignedToMe && (
                                                    <button 
                                                        onClick={(e) => { e.stopPropagation(); handleAssignToMe(group.requestId); }}
                                                        disabled={isSaving}
                                                        style={{ 
                                                            backgroundColor: 'var(--color-primary)', color: '#fff', border: 'none', borderRadius: '4px', padding: '10px 24px', fontSize: '0.85rem', fontWeight: 800, cursor: isSaving ? 'wait' : 'pointer', display: 'flex', alignItems: 'center', gap: '8px', textTransform: 'uppercase', boxShadow: '2px 2px 0px rgba(0,0,0,0.15)' 
                                                        }}
                                                        title="Reivindicar pedido para mim para editá-lo"
                                                    >
                                                        <UserPlus size={18} /> {group.buyerId ? "Assumir Pedido" : "Atribuir a Mim"}
                                                    </button>
                                                )}

                                                {group.requestStatusCode === 'WAITING_QUOTATION' && mode === 'BUYER' && isAssignedToMe && (
                                                    <>
                                                        <button
                                                            onClick={() => {
                                                                const anySupplierSet = !!group.requestSupplierId || group.items.every((item: any) => item.supplierId || item.supplierName);
                                                                const qCount = group.quotations.length;
                                                                const hasCompQ = group.quotations.some((q: SavedQuotationDto) => 
                                                                    (q.itemCount > 0) && (!!q.proformaAttachmentId) && (!!q.supplierId)
                                                                );
                                                                const totalItemsCount = group.items.length + group.quotations.reduce((acc: number, q: SavedQuotationDto) => acc + q.itemCount, 0);
                                                                handleCompleteQuotation(group.requestId, !!group.proformaId, anySupplierSet, totalItemsCount, qCount, hasCompQ);
                                                            }}
                                                            disabled={isSaving || !!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId]}
                                                            className={!!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId] ? "" : "btn-primary"}
                                                            style={{ 
                                                                display: 'flex', 
                                                                alignItems: 'center', 
                                                                gap: '8px', 
                                                                padding: '10px 24px', 
                                                                fontSize: '0.85rem',
                                                                backgroundColor: !!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId] ? '#e5e7eb' : '#059669',
                                                                border: !!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId] ? '2px solid #d1d5db' : '2px solid #047857',
                                                                color: !!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId] ? '#9ca3af' : '#ffffff',
                                                                cursor: !!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId] ? 'not-allowed' : 'pointer'
                                                            }}
                                                            title={!!addQuotationMode[group.requestId] || !!quotationFlowStep[group.requestId] ? "Salve ou cancele a cotação em andamento antes de concluir" : "Concluir etapa de cotação"}
                                                        >
                                                            <CheckCircle2 size={18} /> CONCLUIR COTAÇÃO
                                                        </button>

                                                        {!group.proformaId && 
                                                         !group.requestSupplierId && 
                                                         !group.items.some((item: any) => item.supplierName || (item.lineItemStatusCode && item.lineItemStatusCode !== 'WAITING_QUOTATION' && item.lineItemStatusCode !== 'PENDING')) && (
                                                            <button
                                                                onClick={() => setShowApprovalModal({ show: true, type: 'CANCEL_REQUEST', requestId: group.requestId, itemId: null, itemDescription: null, newStatusCode: null, hasProforma: false, hasSupplier: false, itemsCount: 0, isLastItem: false, quotationCount: 0, hasCompleteQuotation: false })}
                                                                disabled={isSaving}
                                                                style={{
                                                                    display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 24px', fontSize: '0.85rem',
                                                                    backgroundColor: 'transparent',
                                                                    border: '2px solid #ef4444',
                                                                    color: '#ef4444',
                                                                    borderRadius: 'var(--radius-sm)',
                                                                    fontWeight: 800,
                                                                    textTransform: 'uppercase',
                                                                    fontFamily: 'var(--font-family-display)',
                                                                    cursor: isSaving ? 'not-allowed' : 'pointer'
                                                                }}
                                                            >
                                                                <X size={18} /> CANCELAR PEDIDO
                                                            </button>
                                                        )}
                                                    </>
                                                )}

                                                {isAdjustmentPhase && mode === 'BUYER' && isAssignedToMe && (
                                                    <button
                                                        onClick={() => {
                                                            const qCount = group.quotations.length;
                                                            const hasCompQ = group.quotations.some((q: SavedQuotationDto) => 
                                                                (q.itemCount > 0) && (!!q.proformaAttachmentId) && (!!q.supplierId)
                                                            );
                                                            const totalItemsCount = group.items.length + group.quotations.reduce((acc: number, q: SavedQuotationDto) => acc + q.itemCount, 0);
                                                            handleCompleteQuotation(group.requestId, !!group.proformaId, !!group.requestSupplierId, totalItemsCount, qCount, hasCompQ);
                                                        }}
                                                        disabled={isSaving}
                                                        className="btn-primary"
                                                        style={{ 
                                                            display: 'flex', 
                                                            alignItems: 'center', 
                                                            gap: '8px', 
                                                            padding: '10px 24px', 
                                                            fontSize: '0.85rem',
                                                            backgroundColor: '#f59e0b',
                                                            border: '2px solid #d97706'
                                                        }}
                                                    >
                                                        <CheckCircle2 size={18} /> RESUBMETER PEDIDO
                                                    </button>
                                                )}
                                            </div>
                                        </div>

                                        {/* Supplier Card - Only for PAYMENT or if not QUOTATION */}
                                        {group.requestTypeCode !== 'QUOTATION' && (
                                            <div style={{
                                                padding: '16px',
                                                backgroundColor: 'var(--color-bg-surface)',
                                                border: '1px solid var(--color-border)',
                                                borderRadius: '6px',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                gap: '12px'
                                            }}>
                                                <div style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Fornecedor do Pedido (Geral)</div>
                                                <SupplierAutocomplete
                                                    onChange={(id, name) => handleUpdateGroupSupplier(group.requestId, id, name)}
                                                    initialName={group.requestSupplierName}
                                                    initialPortalCode={group.requestSupplierCode}
                                                    disabled={mode !== 'BUYER' || isSaving}
                                                />
                                                <p style={{ margin: 0, fontSize: '0.7rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                                    Este fornecedor será o destinatário principal do pedido na aprovação.
                                                </p>
                                            </div>
                                        )}

                                    </div>
                                )}
                            </div>
                        );
                    })
                )}
            </div>

            {/* Pagination Controls */}
            {
                !loading && items.length > 0 && (
                    <div style={{
                        marginTop: '24px',
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        padding: '16px',
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '2px solid var(--color-border)',
                        width: '100%',
                        minWidth: 0,
                        boxShadow: 'var(--shadow-brutal)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                            <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.9rem', textTransform: 'uppercase' }}>Itens por página:</span>
                            <select
                                value={pageSize}
                                onChange={(e) => {
                                    updateParams({ pageSize: Number(e.target.value), page: 1 });
                                }}
                                style={{
                                    padding: '8px 12px', border: '2px solid var(--color-primary)',
                                    backgroundColor: 'var(--color-bg-page)', outline: 'none',
                                    fontWeight: 700, color: 'var(--color-primary)', cursor: 'pointer'
                                }}
                            >
                                <option value={10}>10</option>
                                <option value={20}>20</option>
                                <option value={50}>50</option>
                                <option value={100}>100</option>
                            </select>
                        </div>

                        <div style={{ fontWeight: 700, color: 'var(--color-primary)', fontSize: '0.9rem' }}>
                            MOSTRANDO {((page - 1) * pageSize) + 1} - {Math.min(page * pageSize, totalCount)} DE {totalCount} RESULTADOS
                        </div>

                        <div style={{ display: 'flex', gap: '12px' }}>
                            <button
                                disabled={page === 1}
                                onClick={() => updateParams({ page: Math.max(1, page - 1) })}
                                style={{
                                    padding: '8px 20px', backgroundColor: page === 1 ? 'var(--color-bg-page)' : 'var(--color-bg-surface)',
                                    color: page === 1 ? 'var(--color-text-muted)' : 'var(--color-primary)',
                                    border: '2px solid', borderColor: page === 1 ? 'var(--color-border)' : 'var(--color-primary)',
                                    fontWeight: 800, cursor: page === 1 ? 'not-allowed' : 'pointer', transition: 'all 0.1s', textTransform: 'uppercase'
                                }}
                            >
                                <span style={{ opacity: page === 1 ? 0.5 : 1 }}>Anterior</span>
                            </button>
                            <button
                                disabled={page * pageSize >= totalCount}
                                onClick={() => updateParams({ page: page + 1 })}
                                style={{
                                    padding: '8px 20px', backgroundColor: page * pageSize >= totalCount ? 'var(--color-bg-page)' : 'var(--color-primary)',
                                    color: page * pageSize >= totalCount ? 'var(--color-text-muted)' : '#FFF',
                                    border: '2px solid', borderColor: page * pageSize >= totalCount ? 'var(--color-border)' : 'var(--color-primary)',
                                    fontWeight: 800, cursor: page * pageSize >= totalCount ? 'not-allowed' : 'pointer', transition: 'all 0.1s', textTransform: 'uppercase'
                                }}
                            >
                                <span style={{ opacity: page * pageSize >= totalCount ? 0.7 : 1 }}>Próximo</span>
                            </button>
                        </div>
                    </div>
                )
            }

            <ApprovalModal
                show={showApprovalModal.show}
                type={showApprovalModal.type}
                onClose={() => {
                    setShowApprovalModal({ 
                        show: false, type: null, requestId: null, itemId: null, itemDescription: null, 
                        newStatusCode: null, hasProforma: false, hasSupplier: false, itemsCount: 0, isLastItem: false,
                        quotationCount: 0, hasCompleteQuotation: false
                    });
                    setApprovalComment('');
                    setModalFeedback({ type: 'error', message: null });
                }}
                onConfirm={handleModalAction}
                comment={approvalComment}
                setComment={setApprovalComment}
                processing={isSaving}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback({ type: 'error', message: null })}
                isLastItem={showApprovalModal.isLastItem}
            />

            {/* Inline Delete Attachment Confirm Modal */}
            {deleteConfirm && (
                <DropdownPortal>
                    <div style={{
                        position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.55)',
                        display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: Z_INDEX.MODAL as any
                    }}>
                        <div style={{
                            backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-md)',
                            border: '2px solid var(--color-border-heavy)', boxShadow: 'var(--shadow-brutal)',
                            padding: '32px', maxWidth: '440px', width: '90%', display: 'flex', flexDirection: 'column', gap: '20px'
                        }}>
                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '16px' }}>
                                <div style={{ width: '44px', height: '44px', borderRadius: '10px', backgroundColor: '#fee2e2', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
                                    <Trash2 size={22} color="#ef4444" />
                                </div>
                                <div>
                                    <h3 style={{ margin: 0, fontFamily: 'var(--font-family-display)', fontWeight: 900, fontSize: '1.1rem', color: 'var(--color-text-main)' }}>Remover Documento</h3>
                                    <p style={{ margin: '8px 0 0 0', fontSize: '0.875rem', color: 'var(--color-text-muted)', lineHeight: '1.5' }}>
                                        Tem certeza que deseja remover <strong style={{ color: 'var(--color-text-main)' }}>"{deleteConfirm?.fileName}"</strong>?
                                    </p>
                                    <div style={{ marginTop: '12px', padding: '10px', backgroundColor: '#fff7ed', border: '1px solid #ffedd5', borderRadius: '4px', fontSize: '0.8rem', color: '#9a3412', fontWeight: 600 }}>
                                        <strong>IMPORTANTE:</strong> O documento proforma é obrigatório. Após a remoção, você deverá anexar um novo arquivo para poder salvar ou concluir a cotação.
                                    </div>
                                </div>
                            </div>
                            <div style={{ display: 'flex', gap: '12px', justifyContent: 'flex-end' }}>
                                <button
                                    onClick={() => setDeleteConfirm(null)}
                                    disabled={isSaving}
                                    style={{ padding: '10px 20px', border: '2px solid var(--color-border-heavy)', borderRadius: 'var(--radius-sm)', background: 'none', fontWeight: 700, cursor: 'pointer', fontSize: '0.875rem' }}
                                >
                                    Cancelar
                                </button>
                                <button
                                    onClick={confirmDeleteProforma}
                                    disabled={isSaving}
                                    style={{ padding: '10px 20px', backgroundColor: '#ef4444', color: '#fff', border: 'none', borderRadius: 'var(--radius-sm)', fontWeight: 700, cursor: isSaving ? 'wait' : 'pointer', fontSize: '0.875rem' }}
                                >
                                    {isSaving ? 'Removendo...' : 'Remover'}
                                </button>
                            </div>
                        </div>
                    </div>
                </DropdownPortal>
            )}
            {/* Quick Supplier Modal (Step 6) */}
            <QuickSupplierModal
                isOpen={quickSupplierModal.show}
                onClose={() => setQuickSupplierModal({ show: false, requestId: null, initialName: '', initialTaxId: '' })}
                initialName={quickSupplierModal.initialName}
                initialTaxId={quickSupplierModal.initialTaxId}
                onSuccess={(s) => {
                    if (quickSupplierModal.requestId) {
                        handleUpdateQuotationHeader(quickSupplierModal.requestId, 'supplierId', s.id);
                        handleUpdateQuotationHeader(quickSupplierModal.requestId, 'supplierNameSnapshot', s.name);
                    }
                }}
            />

            {/* Duplicate Supplier Resolution Modal (Step 9 Refresh) */}
            <AnimatePresence>
                {duplicateSupplierModal.show && (
                    <DropdownPortal>
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            exit={{ opacity: 0 }}
                            style={{
                                position: 'fixed',
                                top: 0,
                                left: 0,
                                right: 0,
                                bottom: 0,
                                backgroundColor: 'rgba(0,0,0,0.8)',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                zIndex: Z_INDEX.MODAL as any,
                                padding: '20px'
                            }}
                        >
                        <motion.div
                            initial={{ scale: 0.9, y: 20 }}
                            animate={{ scale: 1, y: 0 }}
                            style={{
                                backgroundColor: 'var(--color-bg-surface)',
                                padding: '40px',
                                borderRadius: 'var(--radius-md)',
                                maxWidth: '550px',
                                width: '100%',
                                border: '4px solid var(--color-border-heavy)',
                                boxShadow: 'var(--shadow-brutal)',
                                position: 'relative'
                            }}
                        >
                            <button 
                                onClick={cancelDuplicateFlow}
                                style={{
                                    position: 'absolute',
                                    top: '20px',
                                    right: '20px',
                                    background: 'none',
                                    border: 'none',
                                    cursor: 'pointer',
                                    color: 'var(--color-text-muted)'
                                }}
                            >
                                <X className="w-6 h-6" />
                            </button>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '24px' }}>
                                <AlertCircle style={{ width: '32px', height: '32px', color: '#f59e0b' }} />
                                <h2 style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', textTransform: 'uppercase', margin: 0, letterSpacing: '-0.02em' }}>
                                    Fornecedor Duplicado
                                </h2>
                            </div>

                            <div style={{ marginBottom: '24px' }}>
                                <p style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', marginBottom: '16px', lineHeight: 1.4 }}>
                                    Já existe uma cotação salva para <span style={{ color: 'var(--color-primary)' }}>{duplicateSupplierModal.draft?.supplierNameSnapshot || duplicateSupplierModal.duplicate?.supplierNameSnapshot}</span> neste pedido.
                                </p>

                                <div style={{ 
                                    backgroundColor: '#fffbeb', 
                                    border: '2px solid #fef3c7', 
                                    borderRadius: 'var(--radius-sm)', 
                                    padding: '20px',
                                    display: 'flex',
                                    justifyContent: 'space-between',
                                    alignItems: 'center'
                                }}>
                                    <div>
                                        <p style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: '#92400e', marginBottom: '4px' }}>Cotação Existente:</p>
                                        <p style={{ fontSize: '0.9rem', fontWeight: 700, color: '#b45309', margin: 0 }}>
                                            Doc: {duplicateSupplierModal.duplicate?.documentNumber || 'S/N'} • {duplicateSupplierModal.duplicate?.documentDate ? formatDate(duplicateSupplierModal.duplicate.documentDate) : 'N/A'}
                                        </p>
                                    </div>
                                    <div style={{ textAlign: 'right' }}>
                                        <p style={{ fontSize: '1.25rem', fontWeight: 900, color: '#92400e', margin: 0 }}>
                                            <span style={{ fontSize: '0.8rem', opacity: 0.7, marginRight: '4px' }}>{duplicateSupplierModal.duplicate?.currency || 'AOA'}</span>
                                            {formatCurrencyAO(duplicateSupplierModal.duplicate?.totalAmount || 0)}
                                        </p>
                                    </div>
                                </div>

                                <p style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', marginTop: '20px', fontWeight: 500, lineHeight: 1.5 }}>
                                    Você deseja <strong>substituir</strong> a cotação existente pela nova que você está criando? 
                                    A cotação anterior e seu arquivo proforma serão <strong>excluídos permanentemente</strong> para garantir a consistência do pedido.
                                </p>
                            </div>

                            <div style={{ display: 'flex', gap: '16px', marginTop: '12px' }}>
                                <button
                                    type="button"
                                    onClick={cancelDuplicateFlow}
                                    disabled={isSaving}
                                    style={{
                                        flex: 1, height: '48px', padding: '0 24px', background: 'none', border: '2px solid var(--color-border-heavy)',
                                        cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                        fontFamily: 'var(--font-family-display)', fontSize: '0.875rem'
                                    }}
                                >
                                    CANCELAR INCLUSÃO
                                </button>
                                <button
                                    type="button"
                                    onClick={confirmDuplicateReplacement}
                                    disabled={isSaving}
                                    style={{
                                        flex: 2, height: '48px', padding: '0 24px', backgroundColor: '#f59e0b', color: '#fff',
                                        border: 'none', cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                        boxShadow: '4px 4px 0px #b45309', fontFamily: 'var(--font-family-display)',
                                        fontSize: '0.875rem', opacity: isSaving ? 0.7 : 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px'
                                    }}
                                >
                                    {isSaving ? (
                                        <>
                                            <RefreshCcw className="w-4 h-4 animate-spin" />
                                            SUBSTITUINDO...
                                        </>
                                    ) : (
                                        'SUBSTITUIR COTAÇÃO'
                                    )}
                                </button>
                            </div>
                        </motion.div>
                    </motion.div>
                </DropdownPortal>
                )}
            </AnimatePresence>

            <AnimatePresence>
                {fileDuplicateWarning?.isOpen && (
                    <div style={{ position: 'fixed', inset: 0, zIndex: Z_INDEX.MODAL + 50, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '16px', backgroundColor: 'rgba(15, 23, 42, 0.6)', backdropFilter: 'blur(4px)' }}>
                        <motion.div
                            initial={{ opacity: 0, scale: 0.95, y: 10 }}
                            animate={{ opacity: 1, scale: 1, y: 0 }}
                            exit={{ opacity: 0, scale: 0.95, y: 10 }}
                            style={{ backgroundColor: 'white', borderRadius: '12px', boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.3)', width: '100%', maxWidth: '448px', overflow: 'hidden', position: 'relative', border: '1px solid #e2e8f0' }}
                        >
                            <div style={{ padding: '24px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '48px', height: '48px', margin: '0 auto 16px', backgroundColor: '#fff7ed', borderRadius: '50%' }}>
                                    <AlertTriangle size={24} color="#ea580c" />
                                </div>
                                <h3 style={{ fontSize: '1.25rem', fontWeight: 900, textAlign: 'center', color: '#0f172a', marginBottom: '8px', textTransform: 'uppercase', letterSpacing: '-0.01em' }}>
                                    Documento Já Existente
                                </h3>
                                <p style={{ fontSize: '0.875rem', color: '#64748b', textAlign: 'center', marginBottom: '20px', fontWeight: 500, lineHeight: 1.5 }}>
                                    Este orçamento/proforma já foi carregado no sistema anteriormente. Deseja prosseguir mesmo assim?
                                </p>
                                
                                <div style={{ backgroundColor: 'var(--color-bg-page)', padding: '16px', borderRadius: '8px', fontSize: '0.8rem', color: '#475569', border: '1px solid #e2e8f0', marginBottom: '24px' }}>
                                    <p style={{ marginBottom: '6px' }}><span style={{ fontWeight: 800, color: '#1e293b', textTransform: 'uppercase', fontSize: '0.7rem' }}>Arquivo:</span> {fileDuplicateWarning.fileName}</p>
                                    <p style={{ marginBottom: '6px' }}><span style={{ fontWeight: 800, color: '#1e293b', textTransform: 'uppercase', fontSize: '0.7rem' }}>Pedido Vinculado:</span> {fileDuplicateWarning.requestNumber}</p>
                                    <p style={{ marginBottom: '6px' }}><span style={{ fontWeight: 800, color: '#1e293b', textTransform: 'uppercase', fontSize: '0.7rem' }}>Enviado por:</span> {fileDuplicateWarning.uploadedBy || 'Desconhecido'}</p>
                                    <p style={{ margin: 0 }}><span style={{ fontWeight: 800, color: '#1e293b', textTransform: 'uppercase', fontSize: '0.7rem' }}>Enviado em:</span> {fileDuplicateWarning.createdAtUtc ? formatDateTime(fileDuplicateWarning.createdAtUtc) : '-'}</p>
                                </div>

                                <div style={{ display: 'flex', gap: '12px' }}>
                                    <button
                                        type="button"
                                        onClick={() => {
                                            setFileDuplicateWarning(null);
                                            setIsProcessingOcr(prev => ({ ...prev, [fileDuplicateWarning!.requestId]: false }));
                                        }}
                                        style={{ flex: 1, padding: '10px 16px', fontSize: '0.8rem', fontWeight: 800, color: '#475569', backgroundColor: 'white', border: '2px solid #e2e8f0', borderRadius: '6px', cursor: 'pointer', textTransform: 'uppercase' }}
                                    >
                                        Cancelar Envio
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => {
                                            if (fileDuplicateWarning?.uploadCallback) {
                                                fileDuplicateWarning.uploadCallback();
                                            }
                                        }}
                                        style={{ flex: 1.2, padding: '10px 16px', fontSize: '0.8rem', fontWeight: 800, color: 'white', backgroundColor: '#ea580c', border: 'none', borderRadius: '6px', cursor: 'pointer', textTransform: 'uppercase', boxShadow: '0 4px 0 #9a3412' }}
                                    >
                                        Estou Ciente, Prosseguir
                                    </button>
                                </div>
                            </div>
                        </motion.div>
                    </div>
                )}
            </AnimatePresence>

            <QuickSupplierModal 
                isOpen={quickSupplierModal.show}
                onClose={() => setQuickSupplierModal({ show: false, requestId: null, initialName: '', initialTaxId: '' })}
                onSuccess={(supplier: { id: number; name: string; taxId?: string }) => {
                    if (quickSupplierModal.requestId) {
                        handleUpdateQuotationHeader(quickSupplierModal.requestId, 'supplierId', supplier.id);
                        handleUpdateQuotationHeader(quickSupplierModal.requestId, 'supplierNameSnapshot', supplier.name);
                        handleUpdateQuotationHeader(quickSupplierModal.requestId, 'supplierTaxId', supplier.taxId || '');
                    }
                }}
                initialName={quickSupplierModal.initialName}
                initialTaxId={quickSupplierModal.initialTaxId}
            />

            <QuickCurrencyModal
                isOpen={quickCurrencyModal.show}
                onClose={() => setQuickCurrencyModal({ show: false, requestId: null, initialCode: '' })}
                onSuccess={(currency: any) => {
                    if (quickCurrencyModal.requestId) {
                        // Refresh local list
                        setCurrencies(prev => [...prev, currency]);
                        // Auto-select
                        handleUpdateQuotationHeader(quickCurrencyModal.requestId, 'currency', currency.code);
                    }
                }}
                initialCode={quickCurrencyModal.initialCode}
            />

            {/* Quick View Drawer */}
            <RequestDrawerPresentation
                isOpen={!!drawerRequestId}
                requestId={drawerRequestId}
                onClose={() => setDrawerRequestId(null)}
            />

            {/* ═══════════════════════════════════════════════════════════════ */}
            {/* FINANCIAL INTEGRITY GATE — Mismatch Modal                      */}
            {/* ═══════════════════════════════════════════════════════════════ */}
            <AnimatePresence>
                {integrityModal && integrityModal.show && (
                    <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        style={{
                            position: 'fixed', inset: 0,
                            backgroundColor: 'rgba(0,0,0,0.4)',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            zIndex: 10000, padding: '1rem'
                        }}
                        onClick={() => { setIntegrityModal(null); setIntegrityJustification(''); }}
                    >
                        <motion.div
                            initial={{ scale: 0.92, opacity: 0, y: 20 }}
                            animate={{ scale: 1, opacity: 1, y: 0 }}
                            exit={{ scale: 0.92, opacity: 0, y: 20 }}
                            transition={{ type: 'spring', damping: 25, stiffness: 400 }}
                            onClick={e => e.stopPropagation()}
                            style={{
                                background: '#ffffff',
                                border: '1px solid #e2e8f0',
                                borderRadius: '12px',
                                width: '100%', maxWidth: '560px',
                                overflow: 'hidden',
                                boxShadow: '0 20px 60px rgba(0,0,0,0.15), 0 0 0 1px rgba(0,0,0,0.05)'
                            }}
                        >
                            {/* Header */}
                            <div style={{
                                padding: '1.25rem 1.5rem',
                                borderBottom: '1px solid #e2e8f0',
                                display: 'flex', alignItems: 'center', gap: '0.75rem',
                                background: 'linear-gradient(135deg, rgba(234,179,8,0.08), rgba(234,88,12,0.04))'
                            }}>
                                <div style={{
                                    width: '40px', height: '40px', borderRadius: '10px',
                                    background: 'linear-gradient(135deg, #f59e0b, #d97706)',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                                    flexShrink: 0
                                }}>
                                    <AlertTriangle size={20} color="#fff" />
                                </div>
                                <div>
                                    <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: '#1e293b' }}>
                                        Verificação de Integridade Financeira
                                    </h3>
                                    <p style={{ margin: 0, fontSize: '0.8rem', color: '#64748b', marginTop: '2px' }}>
                                        O sistema detectou uma divergência entre os valores
                                    </p>
                                </div>
                                <button
                                    onClick={() => { setIntegrityModal(null); setIntegrityJustification(''); }}
                                    style={{
                                        marginLeft: 'auto', background: 'none', border: 'none', cursor: 'pointer',
                                        color: '#94a3b8', padding: '4px'
                                    }}
                                >
                                    <X size={18} />
                                </button>
                            </div>

                            {/* Body */}
                            <div style={{ padding: '1.25rem 1.5rem' }}>
                                {/* Explanation */}
                                <p style={{ fontSize: '0.85rem', color: '#475569', margin: '0 0 1rem 0', lineHeight: 1.5 }}>
                                    {integrityModal.detail}
                                </p>

                                {/* Comparison Table */}
                                <div style={{
                                    border: '1px solid #e2e8f0',
                                    borderRadius: '8px', overflow: 'hidden', marginBottom: '1rem'
                                }}>
                                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
                                        <thead>
                                            <tr style={{ background: '#f8fafc' }}>
                                                <th style={{ padding: '0.6rem 1rem', textAlign: 'left', color: '#64748b', fontWeight: 600, borderBottom: '1px solid #e2e8f0', textTransform: 'uppercase', fontSize: '0.75rem' }}>Referência</th>
                                                <th style={{ padding: '0.6rem 1rem', textAlign: 'right', color: '#64748b', fontWeight: 600, borderBottom: '1px solid #e2e8f0', textTransform: 'uppercase', fontSize: '0.75rem' }}>Valor</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            <tr>
                                                <td style={{ padding: '0.6rem 1rem', color: '#334155', borderBottom: '1px solid #f1f5f9' }}>
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                        <FileText size={14} style={{ color: '#3b82f6' }} />
                                                        Total OCR (Documento Original)
                                                    </span>
                                                </td>
                                                <td style={{ padding: '0.6rem 1rem', textAlign: 'right', fontFamily: 'monospace', fontWeight: 600, color: '#3b82f6', borderBottom: '1px solid #f1f5f9' }}>
                                                    {integrityModal.ocrOriginalTotal.toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                                </td>
                                            </tr>
                                            <tr>
                                                <td style={{ padding: '0.6rem 1rem', color: '#334155', borderBottom: '1px solid #f1f5f9' }}>
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                        <Hash size={14} style={{ color: '#8b5cf6' }} />
                                                        Total da Cotação (Sistema)
                                                    </span>
                                                </td>
                                                <td style={{ padding: '0.6rem 1rem', textAlign: 'right', fontFamily: 'monospace', fontWeight: 600, color: '#8b5cf6', borderBottom: '1px solid #f1f5f9' }}>
                                                    {integrityModal.quotationTotal.toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                                </td>
                                            </tr>
                                            <tr style={{ background: integrityModal.varianceAmount !== 0 ? '#fef2f2' : 'transparent' }}>
                                                <td style={{ padding: '0.6rem 1rem', color: '#334155' }}>
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                        <AlertTriangle size={14} style={{ color: '#ef4444' }} />
                                                        Variação
                                                    </span>
                                                </td>
                                                <td style={{ padding: '0.6rem 1rem', textAlign: 'right', fontFamily: 'monospace', fontWeight: 700, color: '#ef4444' }}>
                                                    {integrityModal.varianceAmount > 0 ? '+' : ''}{integrityModal.varianceAmount.toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                                    {' '}
                                                    <span style={{ fontSize: '0.75rem', color: '#f87171' }}>
                                                        ({integrityModal.variancePercent > 0 ? '+' : ''}{integrityModal.variancePercent.toFixed(2)}%)
                                                    </span>
                                                </td>
                                            </tr>
                                        </tbody>
                                    </table>
                                </div>

                                {/* Unresolved reconciliation warning */}
                                {integrityModal.unresolvedReconciliationCount > 0 && (
                                    <div style={{
                                        padding: '0.6rem 0.8rem', borderRadius: '8px',
                                        background: '#fffbeb',
                                        border: '1px solid #fde68a',
                                        fontSize: '0.8rem', color: '#92400e',
                                        display: 'flex', alignItems: 'center', gap: '0.5rem',
                                        marginBottom: '1rem'
                                    }}>
                                        <AlertCircle size={14} />
                                        {integrityModal.unresolvedReconciliationCount} item(ns) de reconciliação pendente(s) de revisão.
                                    </div>
                                )}

                                {/* Override justification */}
                                <div style={{ marginTop: '0.5rem' }}>
                                    <label style={{
                                        display: 'block', fontSize: '0.8rem', fontWeight: 600,
                                        color: '#475569', marginBottom: '0.4rem'
                                    }}>
                                        Justificação para prosseguir (obrigatório):
                                    </label>
                                    <textarea
                                        value={integrityJustification}
                                        onChange={e => setIntegrityJustification(e.target.value)}
                                        placeholder="Explique o motivo pelo qual deseja prosseguir com esta divergência financeira..."
                                        rows={3}
                                        style={{
                                            width: '100%', padding: '0.6rem 0.8rem',
                                            borderRadius: '8px',
                                            border: '1px solid #cbd5e1',
                                            background: '#f8fafc',
                                            color: '#1e293b',
                                            fontSize: '0.85rem', resize: 'vertical',
                                            fontFamily: 'inherit',
                                            boxSizing: 'border-box'
                                        }}
                                    />
                                </div>
                            </div>

                            {/* Footer Actions */}
                            <div style={{
                                padding: '1rem 1.5rem',
                                borderTop: '1px solid #e2e8f0',
                                display: 'flex', gap: '0.75rem', justifyContent: 'flex-end',
                                background: '#f8fafc'
                            }}>
                                <button
                                    onClick={() => { setIntegrityModal(null); setIntegrityJustification(''); }}
                                    style={{
                                        padding: '0.55rem 1.2rem', borderRadius: '8px',
                                        border: '1px solid #cbd5e1',
                                        background: '#ffffff',
                                        color: '#475569',
                                        fontSize: '0.85rem', fontWeight: 600, cursor: 'pointer',
                                        display: 'flex', alignItems: 'center', gap: '0.4rem'
                                    }}
                                >
                                    <ArrowLeft size={15} />
                                    Voltar e Corrigir
                                </button>
                                <button
                                    onClick={handleIntegrityOverride}
                                    disabled={!integrityJustification.trim() || integrityOverrideLoading}
                                    style={{
                                        padding: '0.55rem 1.2rem', borderRadius: '8px',
                                        border: 'none',
                                        background: !integrityJustification.trim() || integrityOverrideLoading
                                            ? '#f1f5f9'
                                            : 'linear-gradient(135deg, #f59e0b, #d97706)',
                                        color: !integrityJustification.trim() || integrityOverrideLoading
                                            ? '#94a3b8'
                                            : '#fff',
                                        fontSize: '0.85rem', fontWeight: 700, cursor: !integrityJustification.trim() || integrityOverrideLoading ? 'not-allowed' : 'pointer',
                                        display: 'flex', alignItems: 'center', gap: '0.4rem',
                                        transition: 'all 0.2s'
                                    }}
                                >
                                    {integrityOverrideLoading ? (
                                        <RefreshCcw size={15} style={{ animation: 'spin 1s linear infinite' }} />
                                    ) : (
                                        <CheckCircle2 size={15} />
                                    )}
                                    {integrityOverrideLoading ? 'Processando...' : 'Confirmar Override'}
                                </button>
                            </div>
                        </motion.div>
                    </motion.div>
                )}
            </AnimatePresence>
        </PageContainer>
    );
}
