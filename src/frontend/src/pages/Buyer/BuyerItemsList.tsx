import { useEffect, useState, useRef, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { motion, AnimatePresence } from 'framer-motion';

import { useLocation, useSearchParams } from 'react-router-dom';
import { ChevronDown, ChevronRight, Plus, Upload, ExternalLink, FileText, CheckCircle2, X, Pencil, Trash2, ShieldCheck, AlertCircle, Hash, Calendar, UserPlus, AlertTriangle, BookOpen, MoreVertical, Package, PieChart, Layers, CheckSquare, History } from 'lucide-react';
import { useAuth } from '../../features/auth/AuthContext';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO, formatDate, getUrgencyStyle, formatDateTime, computeFileHash } from '../../lib/utils';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { QuickCurrencyModal } from '../../components/Buyer/QuickCurrencyModal';
import { QuotationReuseModal } from '../../components/Buyer/QuotationReuseModal';
import { QuotationWizardModal } from './QuotationWizard/QuotationWizardModal';
import { useQuotationWizardState } from './QuotationWizard/hooks/useQuotationWizardState';
import { PartialApprovalBatchModal } from './PartialApprovalBatchModal';
import { BatchReworkModal } from './BatchReworkModal';
import { CancelApprovalBatchModal } from './CancelApprovalBatchModal';
import { isQuotationItemSelectableForApproval, isLineItemEligibleForQuotation } from './batchEligibility';
import { getBuyerItemStatus } from './buyerItemStatus';
import { CloseNotQuotedModal } from './CloseNotQuotedModal';

import { Tooltip } from '../../components/ui/Tooltip';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../../components/ui/DropdownPortal';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { GuidedTourContextButton } from '../../features/guided-tour/GuidedTourContextButton';
import { LiveGuideLauncher } from '../../features/guided-tour/live-guide/LiveGuideLauncher';
import { useLiveGuideRegistration } from '../../features/guided-tour/live-guide/LiveGuideProvider';
import { createQuotationManagementGuide } from '../../features/guided-tour/live-guide/guides/quotationManagement.liveGuide';
import type { QuotationManagementState } from '../../features/guided-tour/live-guide/guides/quotationManagement.liveGuide';
import { SavedQuotationDto, IvaRate, Unit, OcrDraft, OcrDraftItem, ReconciliationBatchDto, FinancialIntegrityCheckFailedDto, AmbiguousSavePreAttemptSnapshot, ExtraItemDecisionPayload } from '../../types';
import { useOcrProcessor } from '../../hooks/useOcrProcessor';
import { RequestDrawerPresentation } from '../Requests/components/modern/RequestDrawerPresentation';
import { useTablePreferences } from '../../hooks/useTablePreferences';
// Manual quotation-entry seed fix (Option A): eligible requested items seed as priceable quotation
// rows (reconciliationStatus unset), not NOT_QUOTED. See manualQuotationDraft.ts.
import { buildManualQuotationDraftItems } from './QuotationWizard/manualQuotationDraft';
// Stage 2A-R: reusable stateless wizard-handler facade (single-source orchestration). It also owns the
// edit-draft rehydration (incl. parseClassificationEvidence) and the manual/OCR open logic.
import { createBuyerQuotationWizardController } from './QuotationWizard/buyerQuotationWizardController';


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

// Teams Chat inline button — compact icon to open a 1:1 chat with a user
const TeamsChatButton: React.FC<{ email?: string | null }> = ({ email }) => {
    if (!email) return null;
    const teamsUrl = `https://teams.microsoft.com/l/chat/0/0?users=${encodeURIComponent(email)}`;
    return (
        <a
            href={teamsUrl}
            target="_blank"
            rel="noopener noreferrer"
            title="Abrir chat no Teams"
            onClick={(e) => e.stopPropagation()}
            style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                marginLeft: '4px',
                width: '18px',
                height: '18px',
                borderRadius: '3px',
                opacity: 0.55,
                transition: 'opacity 0.15s ease, background-color 0.15s ease',
                verticalAlign: 'middle',
                flexShrink: 0,
            }}
            onMouseEnter={(e) => { e.currentTarget.style.opacity = '1'; e.currentTarget.style.backgroundColor = 'var(--color-bg-hover, rgba(0,0,0,0.06))'; }}
            onMouseLeave={(e) => { e.currentTarget.style.opacity = '0.55'; e.currentTarget.style.backgroundColor = 'transparent'; }}
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M16.5 3C17.88 3 19 4.12 19 5.5C19 6.88 17.88 8 16.5 8C16.21 8 15.93 7.95 15.67 7.87C16.14 7.21 16.42 6.39 16.42 5.5C16.42 4.61 16.14 3.79 15.67 3.13C15.93 3.05 16.21 3 16.5 3ZM12 3C13.38 3 14.5 4.12 14.5 5.5C14.5 6.88 13.38 8 12 8C10.62 8 9.5 6.88 9.5 5.5C9.5 4.12 10.62 3 12 3Z" fill="#464EB8" />
                <path d="M20 12V16.5C20 17.88 18.88 19 17.5 19H17V13C17 12.45 16.55 12 16 12H20Z" fill="#464EB8" opacity="0.6" />
                <path d="M16 13V19.5C16 20.33 15.33 21 14.5 21H5.5C4.67 21 4 20.33 4 19.5V13C4 12.45 4.45 12 5 12H15C15.55 12 16 12.45 16 13Z" fill="#464EB8" />
                <path d="M8.5 15.5H11.5" stroke="white" strokeWidth="1.2" strokeLinecap="round" />
                <path d="M10 14V17" stroke="white" strokeWidth="1.2" strokeLinecap="round" />
            </svg>
        </a>
    );
};

// Person Avatar — color-coded initials circle
const AVATAR_COLORS = [
    { bg: '#dbeafe', text: '#1d4ed8' },  // blue
    { bg: '#d1fae5', text: '#047857' },  // emerald
    { bg: '#ede9fe', text: '#6d28d9' },  // purple
    { bg: '#fef3c7', text: '#b45309' },  // amber
];
const PersonAvatar: React.FC<{ name?: string | null }> = ({ name }) => {
    if (!name) return null;
    const initials = name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
    const colorIdx = name.length % AVATAR_COLORS.length;
    const palette = AVATAR_COLORS[colorIdx];
    return (
        <div style={{
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            width: '24px', height: '24px', borderRadius: '50%', flexShrink: 0,
            backgroundColor: palette.bg, color: palette.text,
            fontSize: '10px', fontWeight: 700, lineHeight: 1,
        }} title={name}>
            {initials}
        </div>
    );
};

// Interfaces for Lookups moved to types/index.ts

type QuotationDraftItem = OcrDraftItem;
type QuotationDraft = OcrDraft;

const RequestGroupSkeleton: React.FC = () => {
    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: '16px',
            boxShadow: 'var(--shadow-sm)',
            overflow: 'hidden'
        }}>
            <div style={{
                cursor: 'default',
                padding: '24px',
                display: 'flex',
                alignItems: 'center',
                gap: '24px',
                animation: 'pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite'
            }}>
                <div style={{ flex: '1' }}>
                    <div style={{ width: '40%', height: '28px', marginBottom: '8px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                    <div style={{ width: '50%', height: '20px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                </div>
                <div style={{ flex: '2', display: 'flex', gap: '32px' }}>
                    <div>
                        <div style={{ width: '80px', height: '12px', marginBottom: '8px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                        <div style={{ width: '120px', height: '24px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                    </div>
                    <div>
                        <div style={{ width: '80px', height: '12px', marginBottom: '8px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                        <div style={{ width: '120px', height: '24px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                    </div>
                    <div>
                        <div style={{ width: '80px', height: '12px', marginBottom: '8px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                        <div style={{ width: '120px', height: '24px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
                    </div>
                </div>
                <div style={{ flex: '0 0 auto', width: '24px', height: '24px', backgroundColor: 'var(--color-border)', borderRadius: '4px' }}></div>
            </div>
            <style>{`
                @keyframes pulse {
                    0%, 100% { opacity: 1; }
                    50% { opacity: 0.5; }
                }
            `}</style>
        </div>
    );
};



export function BuyerItemsList() {
    const [searchParams, setSearchParams] = useSearchParams();


    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [statuses, setStatuses] = useState<any[]>([]);
    const mode = 'BUYER'; // Standardized as Buyer-only Workspace

    const [expandedRequests, setExpandedRequests] = useState<Set<string>>(new Set());
    const [kebabMenuOpen, setKebabMenuOpen] = useState<string | null>(null);
    const [deleteConfirm, setDeleteConfirm] = useState<{ attachmentId: string; fileName: string } | null>(null);
    

    // Step 2 & 3: Quotation Flow States
    const [importSelectedFiles, setImportSelectedFiles] = useState<Record<string, File[]>>({});
    const [ocrResults, setOcrResults] = useState<Record<string, any>>({});
    const [quotationDrafts, setQuotationDrafts] = useState<Record<string, QuotationDraft>>({});
    const [draftProformaFiles, setDraftProformaFiles] = useState<Record<string, File>>({});
    const [isSaving, setIsSaving] = useState(false);
    const [isProcessingOcr, setIsProcessingOcr] = useState<Record<string, boolean>>({});
    const [ocrErrors, setOcrErrors] = useState<Record<string, string | null>>({});
        const [editingQuotationId, setEditingQuotationId] = useState<Record<string, string | null>>({});
    const [highlightedRequestId, setHighlightedRequestId] = useState<string | null>(null);
    const [formErrors, setFormErrors] = useState<Record<string, Record<string, string>>>({});

    const [partialApprovalModal, setPartialApprovalModal] = useState<{
        show: boolean;
        group: any | null;
    }>({ show: false, group: null });

    const [batchReworkModal, setBatchReworkModal] = useState<{
        show: boolean;
        group: any | null;
        batch: any | null;
    }>({ show: false, group: null, batch: null });

    const [cancelApprovalModal, setCancelApprovalModal] = useState<{
        show: boolean;
        requestId: string;
        batchId: string;
        batchNumber: number;
    }>({ show: false, requestId: '', batchId: '', batchNumber: 0 });

    const [closeNotQuotedModal, setCloseNotQuotedModal] = useState<{
        show: boolean;
        requestId: string;
        lineItemId: string;
        itemDescription: string;
        isLastPendingItem: boolean;
    }>({ show: false, requestId: '', lineItemId: '', itemDescription: '', isLastPendingItem: false });


    // Approval Modal State
    const [showApprovalModal, setShowApprovalModal] = useState<{
        show: boolean,
        type: ApprovalActionType,
        requestId: string | null,
        itemId: string | null,
        itemDescription: string | null,
        newStatusCode: string | null,
        isLastItem: boolean
    }>({
        show: false,
        type: null,
        requestId: null,
        itemId: null,
        itemDescription: null,
        newStatusCode: null,
        isLastItem: false
    });
    const [approvalComment, setApprovalComment] = useState('');
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });

    // Quick Supplier Modal State
    const [quickSupplierModal, setQuickSupplierModal] = useState<{ show: boolean; requestId: string | null; initialName: string; initialTaxId: string }>({ show: false, requestId: null, initialName: '', initialTaxId: '' });

    // Quick Currency Modal State
    const [quickCurrencyModal, setQuickCurrencyModal] = useState<{ show: boolean; requestId: string | null; initialCode: string }>({ show: false, requestId: null, initialCode: '' });

    const [expandedQuotations, setExpandedQuotations] = useState<Record<string, boolean>>({});
    // Option C — explicit reuse of quotations used in a cancelled batch
    const [reuseModal, setReuseModal] = useState<{ requestId: string; quotation: SavedQuotationDto } | null>(null);
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
    const [dupCountdown, setDupCountdown] = useState(0);
    const duplicateWarningRef = useRef<HTMLDivElement | null>(null);
    const duplicateWarningReturnFocusRef = useRef<HTMLElement | null>(null);

    // Countdown timer for duplicate warning confirm button safety delay
    useEffect(() => {
        if (!fileDuplicateWarning?.isOpen) { setDupCountdown(0); return; }
        setDupCountdown(5);
        const interval = setInterval(() => {
            setDupCountdown(prev => {
                if (prev <= 1) { clearInterval(interval); return 0; }
                return prev - 1;
            });
        }, 1000);
        return () => clearInterval(interval);
    }, [fileDuplicateWarning?.isOpen]);

    // Focus management for the duplicate-warning modal (stacked above the Quotation Wizard):
    // capture whatever had focus in the wizard, move focus into the warning dialog once it
    // paints, and restore the wizard's focus when the warning closes — the wizard's own state
    // is untouched either way, only keyboard focus moves.
    useEffect(() => {
        if (fileDuplicateWarning?.isOpen) {
            duplicateWarningReturnFocusRef.current = document.activeElement as HTMLElement | null;
            const raf = requestAnimationFrame(() => {
                const first = duplicateWarningRef.current?.querySelector<HTMLElement>(
                    'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
                );
                first?.focus();
            });
            return () => cancelAnimationFrame(raf);
        }
        duplicateWarningReturnFocusRef.current?.focus();
        duplicateWarningReturnFocusRef.current = null;
    }, [fileDuplicateWarning?.isOpen]);

    // Minimal Tab trap — keeps keyboard focus inside the warning dialog while it's open, so the
    // wizard behind it (visually blocked by the backdrop already) also can't be reached by Tab.
    const handleDuplicateWarningKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
        if (e.key !== 'Tab') return;
        const focusable = duplicateWarningRef.current?.querySelectorAll<HTMLElement>(
            'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        );
        if (!focusable || focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    };

    // Lookups
    const [ivaRates, setIvaRates] = useState<IvaRate[]>([]);
    const [units, setUnits] = useState<Unit[]>([]);
    const [currencies, setCurrencies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);

    // --- Persistent Preferences (URL-sync pattern) ---
    const { preferences: savedPrefs, setPreferences: persistPrefs, resetPreferences } = useTablePreferences('buyer-items', {
        pageSize: 20,
    });

    // On mount: if URL has no filter params but we have saved preferences, hydrate URL
    const hasHydratedUrl = useRef(false);
    useEffect(() => {
        if (hasHydratedUrl.current) return;
        hasHydratedUrl.current = true;

        const hasExistingUrlFilters =
            searchParams.has('search') ||
            searchParams.has('itemStatus') ||
            searchParams.has('requestStatus') ||
            searchParams.has('owner') ||
            searchParams.has('pageSize');

        if (!hasExistingUrlFilters && savedPrefs.filters) {
            const p = new URLSearchParams(searchParams);
            let anySet = false;
            if (savedPrefs.search) { p.set('search', savedPrefs.search); anySet = true; }
            if (savedPrefs.filters.itemStatus) { p.set('itemStatus', savedPrefs.filters.itemStatus); anySet = true; }
            if (savedPrefs.filters.requestStatus) { p.set('requestStatus', savedPrefs.filters.requestStatus); anySet = true; }
            if (savedPrefs.filters.owner && savedPrefs.filters.owner !== 'todos') { p.set('owner', savedPrefs.filters.owner); anySet = true; }
            if (savedPrefs.pageSize && savedPrefs.pageSize !== 20) { p.set('pageSize', savedPrefs.pageSize.toString()); anySet = true; }
            if (anySet) {
                p.set('page', '1');
                setSearchParams(p, { replace: true });
            }
        }
    }, []); // Only on mount

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

    // --- Quotation Wizard Integration ---
    const quotationWizardState = useQuotationWizardState();
    const [wizardActiveRequest, setWizardActiveRequest] = useState<any | null>(null);
    const [temporaryWizardAttachmentIds, setTemporaryWizardAttachmentIds] = useState<string[]>([]);

    // Ambiguous-save pre-attempt snapshot (create path only). Owned HERE, not in the modal —
    // `wizardActiveRequest` is client-side state set once when the wizard opens (from the last
    // list load) and never refreshed, so it cannot be trusted as a "what exists right now"
    // baseline; the snapshot must come from a FRESH server read taken immediately before the
    // first create attempt. 'unavailable' means the fresh read itself failed — reconciliation is
    // then skipped for the rest of this submission (the normal save still proceeds unblocked).
    const preAttemptSnapshotRef = useRef<AmbiguousSavePreAttemptSnapshot | 'unavailable' | null>(null);

    // Stage 2A-R: the wizard orchestration handlers now live in ONE reusable, STATELESS facade. State
    // ownership stays HERE — the controller receives our state/setters/refs/callbacks and is recreated
    // each render, so its returned handlers behave exactly like the former inline ones. `onSaved` wraps
    // loadData() (defined below) so ordering is unaffected. See buyerQuotationWizardController.ts.
    const wizardController = createBuyerQuotationWizardController({
        quotationWizardState,
        wizardActiveRequest,
        setWizardActiveRequest,
        setIsSaving,
        setIsProcessingOcr,
        temporaryWizardAttachmentIds,
        setTemporaryWizardAttachmentIds,
        preAttemptSnapshotRef,
        mapOcrResultToDraft,
        onSaved: () => loadData(),
        onFeedback: setFeedback,
    });

    // Exact-file duplicate check (reconnects the existing, already-proven fileDuplicateWarning
    // flow — same computeFileHash()/checkDuplicate() sequence already used by RequestCreate.tsx)
    // runs BEFORE any upload or OCR call, so an accidental re-upload never wastes either.
    const handleUploadFileForWizard = async (file: File) => {
        if (!wizardActiveRequest) return;
        try {
            const hash = await computeFileHash(file);
            const dupCheck = await api.attachments.checkDuplicate(hash);
            if (dupCheck.isDuplicate) {
                setFileDuplicateWarning({
                    isOpen: true,
                    requestId: wizardActiveRequest.requestId,
                    fileName: file.name,
                    requestNumber: dupCheck.requestNumber || 'Desconhecido',
                    uploadedBy: dupCheck.uploadedBy,
                    createdAtUtc: dupCheck.createdAtUtc,
                    uploadCallback: () => {
                        setFileDuplicateWarning(null);
                        wizardController.startWizardUpload(file);
                    }
                });
                return;
            }
        } catch (err) {
            // Non-blocking: if the duplicate check itself fails (network hiccup, etc.), proceed
            // with the upload as normal rather than blocking the buyer on an unrelated failure.
            console.error('Duplicate check failed', err);
        }
        wizardController.startWizardUpload(file);
    };



    ;
    // ------------------------------------------

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
            // Sync to localStorage
            syncParamsToPrefs(next);
            return next;
        }, { replace: true });
    };

    const syncParamsToPrefs = (params: URLSearchParams) => {
        persistPrefs({
            search: params.get('search') || undefined,
            pageSize: Number(params.get('pageSize')) || 20,
            filters: {
                itemStatus: params.get('itemStatus') || undefined,
                requestStatus: params.get('requestStatus') || undefined,
                owner: (params.get('owner') && params.get('owner') !== 'todos') ? params.get('owner')! : undefined,
            },
        });
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
            setError(null);
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
            setError(err.message || 'Falha ao carregar as cotações. Por favor, tente novamente.');
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
                    requesterEmail: item.requesterEmail,
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
                    buyerEmail: item.buyerEmail,
                    areaApproverName: item.areaApproverName,
                    areaApproverEmail: item.areaApproverEmail,
                    finalApproverName: item.finalApproverName,
                    finalApproverEmail: item.finalApproverEmail,
                    requestTitle: item.requestTitle,
                    requestDescription: item.requestDescription,
                    quotations: item.quotations || [],
                    approvalBatches: item.approvalBatches || [],
                    items: [],
                    lineItems: []
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
            const itemId = item.lineItemId || item.id || item.requestLineItemId;
            if (itemId) {
                const alreadyExists = groups[item.requestId].items.some((i: any) => (i.lineItemId || i.id || i.requestLineItemId) === itemId);
                if (!alreadyExists) {
                    groups[item.requestId].items.push(item);
                    groups[item.requestId].lineItems.push({
                        id: itemId,
                        lineNumber: item.lineNumber,
                        description: item.itemDescription || item.description || item.productDescription || item.name || item.title,
                        quantity: item.quantity ?? item.qty ?? item.requestedQuantity,
                        unit: item.unitCode || item.unit || item.unitName,
                        unitId: item.unitId || null,
                        unitPrice: item.unitPrice,
                        totalAmount: item.total || (item.unitPrice * (item.quantity ?? 1)),
                        notes: item.notes,
                        itemCatalogId: item.itemCatalogId,
                        lineItemStatusCode: item.lineItemStatusCode,
                        quotationLifecycleStatus: item.quotationLifecycleStatus,
                    });
                }
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
    ;

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

    // Global discount logic replaced by item-level discounts (Option A)

    // Local calculation logic moved to shared useOcrProcessor hook


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
            isLastItem: false
        });
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


    const handleModalAction = async () => {
        const { requestId, itemId, newStatusCode, type } = showApprovalModal;
        if (!requestId && type !== 'ITEM_STATUS_CHANGE') return;

        try {
            setIsSaving(true);
            setModalFeedback({ type: 'error', message: null });

            if (type === 'CANCEL_REQUEST' && requestId) {
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
                /* legacy save removed */
            }

            setShowApprovalModal({ show: false, type: null, requestId: null, itemId: null, itemDescription: null, newStatusCode: null, isLastItem: false });
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

    const handleOpenPartialApproval = (group: any) => {
        setPartialApprovalModal({ show: true, group });
    };

    const handleOpenCancelApproval = (requestId: string, batchId: string, batchNumber: number) => {
        setCancelApprovalModal({ show: true, requestId, batchId, batchNumber });
    };

    const handleOpenBatchRework = (group: any, batch: any) => {
        setBatchReworkModal({ show: true, group, batch });
    };

    // Does NOT catch errors — PartialApprovalBatchModal awaits this and renders structured
    // 409/400 responses (pending decisions, locked reversal, invalid comment) inline itself;
    // swallowing them here would silently downgrade that to a generic page-level toast.
    // Candidate model: the payload carries candidate OPTIONS per item (no winner field exists).
    const handlePartialApprovalSubmit = async (
        submitData: import('../../types').BatchItemInput[],
        extraItemDecisions?: Record<string, ExtraItemDecisionPayload>
    ) => {
        if (!partialApprovalModal.group) return;
        await api.requests.createApprovalBatch(partialApprovalModal.group.requestId, submitData, undefined, extraItemDecisions);
        setPartialApprovalModal({ show: false, group: null });
        setFeedback({ type: 'success', message: 'Lote criado — opções enviadas para o Aprovador de Área.' });
        loadData();
    };

    const handleCancelSuccess = () => {
        setFeedback({ type: 'success', message: 'Lote de aprovação cancelado com sucesso.' });
        loadData();
    };

    const handleBatchReworkSuccess = (message: string) => {
        setBatchReworkModal({ show: false, group: null, batch: null });
        setFeedback({ type: 'success', message });
        loadData();
    };

    const calculateCoverage = (group: any) => {
        const activeItems = (group.lineItems || []).filter((item: any) =>
            item.lineItemStatusCode !== 'DELETED' && item.lineItemStatusCode !== 'CANCELLED'
        );
        const totalActive = activeItems.length;
        const quotations = group.quotations || [];

        // "Handled elsewhere" (not eligible for a new quotation) is not a single
        // bucket — BATCH_ASSIGNED/QUOTATION_APPROVED are genuinely resolved,
        // but NOT_QUOTED_PROPOSED is still awaiting a Requester/Area Approver
        // decision (it can bounce back to pending on reject) and must never be
        // reported as "fully quoted".
        let inBatchOrApproved = 0;
        let notQuotedProposed = 0;
        let notQuotedAccepted = 0;
        let closedNotQuoted = 0;
        let readyToSend = 0;
        let pending = 0;

        activeItems.forEach((reqItem: any) => {
            const lifecycle = reqItem.quotationLifecycleStatus;
            if (lifecycle === 'NOT_QUOTED_PROPOSED') { notQuotedProposed++; return; }
            if (lifecycle === 'NOT_QUOTED_ACCEPTED') { notQuotedAccepted++; return; }
            if (lifecycle === 'CLOSED_NOT_QUOTED') { closedNotQuoted++; return; }
            if (!isLineItemEligibleForQuotation(reqItem)) { inBatchOrApproved++; return; }

            const hasCandidate = quotations.some((q: any) =>
                (q.items || []).some((qi: any) =>
                    qi.mappedRequestLineItemId === reqItem.id &&
                    (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
                    isQuotationItemSelectableForApproval(qi.id, group)
                )
            );
            if (hasCandidate) readyToSend++;
            else pending++;
        });

        const handledElsewhere = inBatchOrApproved + notQuotedProposed + notQuotedAccepted + closedNotQuoted;
        const totalCovered = handledElsewhere + readyToSend;
        const totalNotCovered = pending;

        // AWAITING_DECISION takes priority over FULLY_QUOTED whenever nothing is
        // left to quote (pending === 0) but a not-quoted proposal is still open —
        // it can still bounce back to pending on reject, so it is never "done".
        let status: 'NOT_QUOTED' | 'PARTIALLY_QUOTED' | 'FULLY_QUOTED' | 'AWAITING_DECISION' = 'NOT_QUOTED';
        if (pending === 0 && notQuotedProposed > 0) {
            status = 'AWAITING_DECISION';
        } else if (pending === 0 && totalActive > 0) {
            status = 'FULLY_QUOTED';
        } else if (totalCovered > 0) {
            status = 'PARTIALLY_QUOTED';
        }

        return {
            totalActive,
            totalCovered,
            totalNotCovered,
            handledElsewhere,
            inBatchOrApproved,
            notQuotedProposed,
            notQuotedAccepted,
            closedNotQuoted,
            readyToSend,
            status
        };
    };

    const getEligibleItemsForPartialApproval = (group: any) => {
        const reqItems: any[] = group.lineItems || [];
        const quotations = group.quotations || [];
        const eligibleList: any[] = [];

        reqItems.forEach((reqItem: any) => {
            if (reqItem.lineItemStatusCode === 'DELETED' || reqItem.lineItemStatusCode === 'CANCELLED') return;

            const lifecycle = reqItem.quotationLifecycleStatus;
            if (lifecycle !== null && lifecycle !== 'QUOTATION_PENDING') return;

            if (
                lifecycle === 'BATCH_ASSIGNED' || 
                lifecycle === 'QUOTATION_APPROVED' || 
                lifecycle === 'NOT_QUOTED_PROPOSED' || 
                lifecycle === 'NOT_QUOTED_ACCEPTED'
            ) return;

            const hasCandidate = quotations.some((q: any) => 
                (q.items || []).some((qi: any) => 
                    qi.mappedRequestLineItemId === reqItem.id && 
                    (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
                    isQuotationItemSelectableForApproval(qi.id, group)
                )
            );

            if (hasCandidate) {
                eligibleList.push(reqItem);
            }
        });

        return eligibleList;
    };

    // --- RENDER HELPERS ---

    const groupedRequests = groupItemsByRequest(items);

    /**
     * Pre-tour preparation: automatically expand the first request
     * when the page tour starts and no request is currently expanded.
     * Listens for the 'guided-tour:prepare' CustomEvent dispatched by useGuidedTour.
     */
    useEffect(() => {
        const handleTourPrepare = (e: Event) => {
            const tourId = (e as CustomEvent).detail?.tourId;
            if (tourId !== 'page-buyer-items') return;

            // If a request is already expanded, nothing to do
            if (expandedRequests.size > 0) return;

            // If there are grouped requests, expand the first one
            if (groupedRequests.length > 0) {
                const firstRequestId = groupedRequests[0].requestId;
                setExpandedRequests(new Set([firstRequestId]));
            }
        };

        window.addEventListener('guided-tour:prepare', handleTourPrepare);
        return () => window.removeEventListener('guided-tour:prepare', handleTourPrepare);
    }, [expandedRequests, groupedRequests]);

    // ── Live Guide Registration ─────────────────────────────────────────
    const { registerGuideFactory, unregisterGuideFactory } = useLiveGuideRegistration();

    // Keep refs to avoid stale closures in the factory
    const groupedRequestsRef = useRef(groupedRequests);
    groupedRequestsRef.current = groupedRequests;
    const expandedRequestsRef = useRef(expandedRequests);
    expandedRequestsRef.current = expandedRequests;
    
    

    const getGuideState = useCallback((): QuotationManagementState => {
        const groups = groupedRequestsRef.current;
        const expanded = expandedRequestsRef.current;
        const firstGroup = groups.length > 0 ? groups[0] : null;
        return {
            hasVisibleGroups: groups.length > 0,
            isFirstGroupExpanded: firstGroup ? expanded.has(firstGroup.requestId) : false,
            isAssignedToMe: firstGroup ? firstGroup.buyerId === currentUser?.id : false,
            hasBuyerAssigned: firstGroup ? !!firstGroup.buyerId : false,
            hasQuotations: firstGroup ? firstGroup.quotations.length > 0 : false,
            isAddingQuotation: false,
            requestStatusCode: firstGroup ? firstGroup.requestStatusCode : '',
        };
    }, [currentUser?.id]);

    useEffect(() => {
        registerGuideFactory('quotation-management-live-guide', () =>
            createQuotationManagementGuide(getGuideState)
        );
        return () => unregisterGuideFactory('quotation-management-live-guide');
    }, [registerGuideFactory, unregisterGuideFactory, getGuideState]);

    return (
        <PageContainer>
            <div data-guide="qm-page">
            <style>{highlightStyles}</style>

            {feedback.message && (
                <div style={{
                    position: 'sticky',
                    top: 'calc(var(--header-height) + var(--env-banner-offset, 0px))',
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

            <div data-guide="qm-header">
            <PageHeader
                data-tour="buyer-items-header"
                title="Gestão de Cotações"
                subtitle="Visualize e gerencie os itens solicitados e suas cotações em um único workspace."
                actions={
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <GuidedTourContextButton tourId="page-buyer-items" label="Tour da Tela" />
                        <LiveGuideLauncher guideId="quotation-management-live-guide" />
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
                    </div>
                }
            />
            </div>

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

            <div data-tour="buyer-items-search" data-guide="qm-search">
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
                        style={{ padding: '8px 12px', border: '1px solid #e2e8f0', borderRadius: '12px', outline: 'none', background: '#fff', fontSize: '0.85rem', fontWeight: 500, color: '#475569', cursor: 'pointer', transition: 'border-color 0.15s ease, box-shadow 0.15s ease' }}
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
            </div>

            {/* Grouped Area */}
            <div data-tour="buyer-items-list" style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {loading ? (
                    <>
                        <RequestGroupSkeleton />
                        <RequestGroupSkeleton />
                        <RequestGroupSkeleton />
                        <RequestGroupSkeleton />
                    </>
                ) : error ? (
                    <div style={{ padding: '40px', textAlign: 'center', border: '1px solid var(--color-danger)', borderRadius: '16px', backgroundColor: 'var(--color-bg-surface)', boxShadow: 'var(--shadow-sm)' }}>
                        <h3 style={{ color: 'var(--color-danger)', marginBottom: '16px', fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Falha ao Carregar</h3>
                        <p style={{ color: 'var(--color-text-secondary)', marginBottom: '24px' }}>{error}</p>
                        <button className="btn btn-primary" onClick={() => loadData()}>
                            TENTAR NOVAMENTE
                        </button>
                    </div>
                ) : groupedRequests.length === 0 ? (
                    <div data-tour="buyer-items-empty-state" style={{ padding: '80px 20px', textAlign: 'center', border: '1px dashed var(--color-border)', borderRadius: '16px', backgroundColor: 'var(--color-bg-surface)', boxShadow: 'var(--shadow-sm)' }}>
                        <FileText size={64} strokeWidth={1.5} style={{ margin: '0 auto 24px', color: 'var(--color-primary)', opacity: 0.8 }} />
                        <h3 style={{ fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-primary)', marginBottom: '16px' }}>Nenhuma cotação localizada.</h3>

                        {(searchInput || requestStatus || itemStatus || owner !== 'todos') ? (
                            <>
                                <p style={{ color: 'var(--color-text-secondary)', marginBottom: '24px' }}>Tente limpar seus filtros para ver mais resultados.</p>
                                <div style={{ display: 'flex', gap: '16px', justifyContent: 'center' }}>
                                    {searchInput && (
                                        <button className="btn btn-secondary" onClick={() => { setSearchInput(''); updateParams({ search: null, page: 1 }); resetPreferences(); }}>
                                            LIMPAR BUSCA
                                        </button>
                                    )}
                                    {(requestStatus || itemStatus || owner !== 'todos') && (
                                        <button className="btn btn-secondary" onClick={() => { updateParams({ requestStatus: null, itemStatus: null, owner: null, page: 1 }); resetPreferences(); }}>
                                            LIMPAR FILTROS
                                        </button>
                                    )}
                                </div>
                            </>
                        ) : (
                            <>
                                <p style={{ color: 'var(--color-text-secondary)', marginBottom: '24px' }}>Você não tem cotações pendentes ou atribuídas a você no momento.</p>
                            </>
                        )}
                    </div>
                ) : (
                    groupedRequests.map((group, groupIndex) => {
                        const isExpanded = expandedRequests.has(group.requestId);
                        const isAssignedToMe = group.buyerId === currentUser?.id;
                        const urgency = getUrgencyStyle(group.needByDateUtc, group.requestStatusCode);
                        const actionBadge = getActionBadge(group.requestStatusCode);
                        const isAdjustmentPhase = group.requestStatusCode === 'AREA_ADJUSTMENT' || group.requestStatusCode === 'FINAL_ADJUSTMENT';
                        const canMutateQuotation = ['DRAFT', 'WAITING_QUOTATION', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'].includes(group.requestStatusCode) && isAssignedToMe;

                        return (
                            <div key={group.requestId} id={`request-group-${group.requestId}`} {...(groupIndex === 0 ? { 'data-guide': 'qm-request-card' } : {})} className={highlightedRequestId === group.requestId ? 'section-attention-highlight' : ''} style={{
                                backgroundColor: 'var(--color-bg-surface)',
                                border: '1px solid var(--color-border)',
                                borderRadius: '16px',
                                boxShadow: 'var(--shadow-sm)',
                                transition: 'box-shadow 0.2s ease, border-color 0.2s ease'
                            }}
                                onMouseEnter={(e) => { e.currentTarget.style.boxShadow = 'var(--shadow-md)'; e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.2)'; }}
                                onMouseLeave={(e) => { e.currentTarget.style.boxShadow = 'var(--shadow-sm)'; e.currentTarget.style.borderColor = 'var(--color-border)'; }}
                            >
                                {/* Request Header Row */}
                                <div
                                    onClick={() => toggleGroup(group.requestId)}
                                    style={{
                                        padding: '16px 20px',
                                        backgroundColor: isExpanded ? 'var(--color-bg-page)' : 'var(--color-bg-surface)',
                                        borderBottom: isExpanded ? '1px solid var(--color-border)' : 'none',
                                        display: 'flex',
                                        justifyContent: 'space-between',
                                        alignItems: 'center',
                                        cursor: 'pointer',
                                        userSelect: 'none',
                                        borderRadius: isExpanded ? '16px 16px 0 0' : '16px',
                                        transition: 'background-color 0.15s ease'
                                    }}
                                >
                                    {/* === LEFT ZONE: Chevron + Pedido + Status === */}
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: '220px' }}>
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
                                                <div style={{ width: '4px', height: '32px', backgroundColor: urgency.indicatorColor, borderRadius: '2px', marginRight: '-4px' }} />
                                            </Tooltip>
                                        )}
                                        <span {...(groupIndex === 0 ? { 'data-guide': 'qm-expand-request' } : {})} style={{ display: 'flex', alignItems: 'center' }}>
                                            {isExpanded ? <ChevronDown size={20} style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} /> : <ChevronRight size={20} style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} />}
                                        </span>
                                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                                            <span style={{ fontSize: '10px', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Pedido</span>
                                            <span style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--color-text-main)', whiteSpace: 'nowrap' }}>{group.requestNumber}</span>
                                            <span className={`badge badge-sm badge-${group.requestStatusBadgeColor === 'red' ? 'danger' :
                                                    group.requestStatusBadgeColor === 'yellow' ? 'warning' :
                                                        group.requestStatusBadgeColor === 'green' ? 'success' :
                                                            group.requestStatusBadgeColor || 'neutral'
                                                }`} style={{ alignSelf: 'flex-start', marginTop: '4px' }}>
                                                {group.requestStatusName}
                                            </span>
                                        </div>
                                    </div>

                                    {/* === CENTER ZONE: People (flex-1, spread) === */}
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '24px', flex: 1, justifyContent: 'center' }}>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                            <span style={{ fontSize: '10px', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Solicitante</span>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                <PersonAvatar name={group.requesterName} />
                                                <span style={{ fontSize: '0.85rem', fontWeight: 500, color: 'var(--color-text-main)', display: 'inline-flex', alignItems: 'center' }}>{group.requesterName}<TeamsChatButton email={group.requesterEmail} /></span>
                                            </div>
                                        </div>
                                        {group.buyerName && (
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <span style={{ fontSize: '10px', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Comprador</span>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                    <PersonAvatar name={group.buyerName} />
                                                    <span style={{ fontSize: '0.85rem', fontWeight: 500, color: 'var(--color-text-main)', display: 'inline-flex', alignItems: 'center' }}>{group.buyerName}<TeamsChatButton email={group.buyerEmail} /></span>
                                                </div>
                                            </div>
                                        )}
                                        {group.requestStatusCode !== 'WAITING_FINAL_APPROVAL' && group.areaApproverName && (
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <span style={{ fontSize: '10px', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Aprovador da Área</span>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                    <PersonAvatar name={group.areaApproverName} />
                                                    <span style={{ fontSize: '0.85rem', fontWeight: 500, color: 'var(--color-text-main)', display: 'inline-flex', alignItems: 'center' }}>{group.areaApproverName}<TeamsChatButton email={group.areaApproverEmail} /></span>
                                                </div>
                                            </div>
                                        )}
                                        {group.requestStatusCode === 'WAITING_FINAL_APPROVAL' && group.finalApproverName && (
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <span style={{ fontSize: '10px', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Aprovador Final</span>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                    <PersonAvatar name={group.finalApproverName} />
                                                    <span style={{ fontSize: '0.85rem', fontWeight: 500, color: 'var(--color-text-main)', display: 'inline-flex', alignItems: 'center' }}>{group.finalApproverName}<TeamsChatButton email={group.finalApproverEmail} /></span>
                                                </div>
                                            </div>
                                        )}
                                    </div>

                                    {/* === RIGHT ZONE: Date + Action + Cancel + ExternalLink === */}
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexShrink: 0 }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                            <Calendar size={16} style={{ color: urgency?.indicatorColor || '#f59e0b', flexShrink: 0 }} />
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                <span style={{ fontSize: '10px', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Data Necessária</span>
                                                <span style={{ fontSize: '0.85rem', fontWeight: 500, color: urgency?.indicatorColor || '#f59e0b', whiteSpace: 'nowrap' }}>
                                                    {formatDate(group.needByDateUtc)}
                                                </span>
                                            </div>
                                        </div>
                                        {!group.buyerId && (
                                            <span className="badge" style={{ backgroundColor: '#fef3c7', color: '#b45309', border: '1px solid #fde68a', borderRadius: '8px', fontSize: '0.75rem', fontWeight: 500, padding: '2px 10px' }}>
                                                Não Atribuído
                                            </span>
                                        )}
                                        <span className={`badge ${actionBadge.className}`} style={{
                                            borderRadius: '8px',
                                            fontSize: '0.75rem',
                                            fontWeight: 500,
                                            padding: '4px 14px'
                                        }}>
                                            {actionBadge.label}
                                        </span>
                                        {group.buyerId !== currentUser?.id && (
                                            <button
                                                {...(groupIndex === 0 ? { 'data-guide': 'qm-assign-btn' } : {})}
                                                onClick={(e) => { e.stopPropagation(); handleAssignToMe(group.requestId); }}
                                                disabled={isSaving}
                                                style={{
                                                    backgroundColor: 'var(--color-primary)', color: '#fff', border: 'none', borderRadius: '8px', padding: '6px 12px', fontSize: '0.75rem', fontWeight: 600, cursor: isSaving ? 'wait' : 'pointer', display: 'flex', alignItems: 'center', gap: '4px', textTransform: 'uppercase', boxShadow: 'var(--shadow-sm)', whiteSpace: 'nowrap'
                                                }}
                                                title={group.buyerId ? "Reatribuir este pedido para mim" : "Reivindicar pedido para mim"}
                                            >
                                                <UserPlus size={14} /> {group.buyerId ? "Assumir Pedido" : "Atribuir a Mim"}
                                            </button>
                                        )}
                                        {/* Kebab Menu */}
                                        <div style={{ position: 'relative' }}>
                                            <button
                                                type="button"
                                                onClick={(e) => {
                                                    e.stopPropagation();
                                                    setKebabMenuOpen(kebabMenuOpen === group.requestId ? null : group.requestId);
                                                }}
                                                style={{
                                                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                                                    padding: '6px',
                                                    backgroundColor: kebabMenuOpen === group.requestId ? 'rgba(var(--color-primary-rgb), 0.06)' : 'transparent',
                                                    border: 'none',
                                                    color: kebabMenuOpen === group.requestId ? 'var(--color-primary)' : 'var(--color-text-muted)',
                                                    borderRadius: '8px',
                                                    cursor: 'pointer',
                                                    transition: 'color 0.15s ease, background-color 0.15s ease'
                                                }}
                                                onMouseEnter={(e) => { if (kebabMenuOpen !== group.requestId) { e.currentTarget.style.color = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.06)'; } }}
                                                onMouseLeave={(e) => { if (kebabMenuOpen !== group.requestId) { e.currentTarget.style.color = 'var(--color-text-muted)'; e.currentTarget.style.backgroundColor = 'transparent'; } }}
                                                title="Mais opções"
                                            >
                                                <MoreVertical size={18} />
                                            </button>
                                            <AnimatePresence>
                                                {kebabMenuOpen === group.requestId && (
                                                    <>
                                                        {/* Backdrop to close */}
                                                        <div
                                                            style={{ position: 'fixed', inset: 0, zIndex: 50 }}
                                                            onClick={(e) => { e.stopPropagation(); setKebabMenuOpen(null); }}
                                                        />
                                                        <motion.div
                                                            initial={{ opacity: 0, scale: 0.95, y: -4 }}
                                                            animate={{ opacity: 1, scale: 1, y: 0 }}
                                                            exit={{ opacity: 0, scale: 0.95, y: -4 }}
                                                            transition={{ duration: 0.12, ease: 'easeOut' }}
                                                            style={{
                                                                position: 'absolute', right: 0, top: '100%', marginTop: '4px',
                                                                backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                                                                borderRadius: '10px', boxShadow: '0 4px 16px rgba(0,0,0,0.10)',
                                                                minWidth: '180px', zIndex: 51, overflow: 'hidden',
                                                                padding: '4px 0'
                                                            }}
                                                        >
                                                            <button
                                                                onClick={(e) => {
                                                                    e.stopPropagation();
                                                                    setKebabMenuOpen(null);
                                                                    setDrawerRequestId(group.requestId);
                                                                }}
                                                                style={{
                                                                    display: 'flex', alignItems: 'center', gap: '8px', width: '100%',
                                                                    padding: '10px 14px', background: 'none', border: 'none',
                                                                    fontSize: '0.85rem', fontWeight: 500, color: 'var(--color-text-main)',
                                                                    cursor: 'pointer', transition: 'background-color 0.1s ease'
                                                                }}
                                                                onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-neutral)'}
                                                                onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                                                            >
                                                                <ExternalLink size={15} style={{ color: 'var(--color-text-muted)' }} /> Ver Detalhes
                                                            </button>
                                                            {!group.proformaId &&
                                                                !group.requestSupplierId &&
                                                                !group.items.some((item: any) => item.supplierName || (item.lineItemStatusCode && item.lineItemStatusCode !== 'WAITING_QUOTATION' && item.lineItemStatusCode !== 'PENDING')) && (
                                                                    <button
                                                                        onClick={(e) => {
                                                                            e.stopPropagation();
                                                                            setKebabMenuOpen(null);
                                                                            setShowApprovalModal({ show: true, type: 'CANCEL_REQUEST', requestId: group.requestId, itemId: null, itemDescription: null, newStatusCode: null, isLastItem: false });
                                                                        }}
                                                                        disabled={isSaving}
                                                                        style={{
                                                                            display: 'flex', alignItems: 'center', gap: '8px', width: '100%',
                                                                            padding: '10px 14px', background: 'none', border: 'none',
                                                                            fontSize: '0.85rem', fontWeight: 500, color: '#ef4444',
                                                                            cursor: isSaving ? 'wait' : 'pointer', transition: 'background-color 0.1s ease'
                                                                        }}
                                                                        onMouseEnter={(e) => e.currentTarget.style.backgroundColor = '#fef2f2'}
                                                                        onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                                                                    >
                                                                        <X size={15} /> Cancelar Pedido
                                                                    </button>
                                                                )}
                                                        </motion.div>
                                                    </>
                                                )}
                                            </AnimatePresence>
                                        </div>
                                    </div>
                                </div>

                                {isExpanded && (
                                    <div data-tour="buyer-open-request" style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '24px' }}>
                                        {(() => {
                                            const coverage = calculateCoverage(group);
                                            const eligibleItems = getEligibleItemsForPartialApproval(group);
                                            const hasEligibleItems = eligibleItems.length > 0;

                                            return (
                                                <div style={{
                                                    display: 'flex',
                                                    alignItems: 'center',
                                                    justifyContent: 'space-between',
                                                    padding: '16px 20px',
                                                    backgroundColor: 'var(--color-bg-page)',
                                                    border: '1px solid var(--color-border)',
                                                    borderRadius: '12px',
                                                    boxShadow: 'var(--shadow-sm)'
                                                }}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                                                        <div style={{
                                                            display: 'flex',
                                                            alignItems: 'center',
                                                            justifyContent: 'center',
                                                            width: '40px',
                                                            height: '40px',
                                                            borderRadius: '50%',
                                                            backgroundColor: coverage.status === 'FULLY_QUOTED' ? '#e6f4ea' : coverage.status === 'AWAITING_DECISION' ? '#fef3c7' : coverage.status === 'PARTIALLY_QUOTED' ? '#fef7e0' : '#fce8e6',
                                                            color: coverage.status === 'FULLY_QUOTED' ? '#137333' : coverage.status === 'AWAITING_DECISION' ? '#92400e' : coverage.status === 'PARTIALLY_QUOTED' ? '#b06000' : '#c5221f'
                                                        }}>
                                                            <PieChart size={22} />
                                                        </div>
                                                        <div>
                                                            <h4 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                                                                Resumo de Cobertura das Cotações
                                                            </h4>
                                                            <div style={{ display: 'flex', gap: '12px', marginTop: '4px', fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, flexWrap: 'wrap' }}>
                                                                <span>Itens Solicitados: <strong style={{ color: 'var(--color-text-main)' }}>{coverage.totalActive}</strong></span>
                                                                {coverage.inBatchOrApproved > 0 && (<><span>•</span><span>Em lote/aprovação: <strong style={{ color: '#1a73e8' }}>{coverage.inBatchOrApproved}</strong></span></>)}
                                                                {coverage.notQuotedProposed > 0 && (<><span>•</span><span>Não cotado aguardando decisão: <strong style={{ color: '#b45309' }}>{coverage.notQuotedProposed}</strong></span></>)}
                                                                {coverage.notQuotedAccepted > 0 && (<><span>•</span><span>Não cotado aceito: <strong style={{ color: '#6b7280' }}>{coverage.notQuotedAccepted}</strong></span></>)}
                                                                {coverage.closedNotQuoted > 0 && (<><span>•</span><span>Encerrado sem cotação: <strong style={{ color: '#6b7280' }}>{coverage.closedNotQuoted}</strong></span></>)}
                                                                <span>•</span>
                                                                <span>Cotados (prontos p/ envio): <strong style={{ color: '#137333' }}>{coverage.readyToSend}</strong></span>
                                                                <span>•</span>
                                                                <span>Pendentes: <strong style={{ color: '#c5221f' }}>{coverage.totalNotCovered}</strong></span>
                                                            </div>
                                                        </div>
                                                    </div>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                                        <span className="badge" style={{
                                                            backgroundColor: coverage.status === 'FULLY_QUOTED' ? '#e6f4ea' : coverage.status === 'AWAITING_DECISION' ? '#fef3c7' : coverage.status === 'PARTIALLY_QUOTED' ? '#fef7e0' : '#fce8e6',
                                                            color: coverage.status === 'FULLY_QUOTED' ? '#137333' : coverage.status === 'AWAITING_DECISION' ? '#92400e' : coverage.status === 'PARTIALLY_QUOTED' ? '#b06000' : '#c5221f',
                                                            border: 'none',
                                                            fontWeight: 800,
                                                            fontSize: '0.75rem',
                                                            padding: '6px 12px',
                                                            borderRadius: '20px'
                                                        }}>
                                                            {coverage.status === 'FULLY_QUOTED' ? 'Totalmente Cotado' : coverage.status === 'AWAITING_DECISION' ? 'Aguardando Decisão' : coverage.status === 'PARTIALLY_QUOTED' ? 'Parcialmente Cotado' : 'Sem Cotação'}
                                                        </span>
                                                        {canMutateQuotation && mode === 'BUYER' && hasEligibleItems && (
                                                            <button
                                                                className="btn-primary"
                                                                onClick={(e) => {
                                                                    e.stopPropagation();
                                                                    handleOpenPartialApproval(group);
                                                                }}
                                                                style={{ gap: '8px', padding: '8px 16px', borderRadius: '8px', fontSize: '0.8rem' }}
                                                            >
                                                                <CheckSquare size={14} /> Avançar itens cobertos para aprovação
                                                            </button>
                                                        )}
                                                    </div>
                                                </div>
                                            );
                                        })()}

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
                                        <div {...(groupIndex === 0 ? { 'data-guide': 'qm-request-summary' } : {})} style={{
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

                                        {/* REQUESTED ITEMS SECTION */}
                                        <div data-tour="buyer-open-request-items" {...(groupIndex === 0 ? { 'data-guide': 'qm-items-section' } : {})} style={{
                                            padding: '24px',
                                            backgroundColor: 'var(--color-bg-page)',
                                            border: '1px solid var(--color-border)',
                                            borderRadius: 'var(--radius-sm)',
                                            display: 'flex',
                                            flexDirection: 'column',
                                            gap: '16px'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#7c3aed', fontWeight: 800, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                    <Package size={18} /> ITENS SOLICITADOS NO PEDIDO
                                                </div>
                                                {group.items.length > 0 && (
                                                    <span style={{
                                                        backgroundColor: '#ede9fe',
                                                        color: '#6d28d9',
                                                        fontSize: '0.7rem',
                                                        fontWeight: 800,
                                                        padding: '3px 10px',
                                                        borderRadius: '12px',
                                                        letterSpacing: '0.03em'
                                                    }}>
                                                        {group.items.length} {group.items.length === 1 ? 'item' : 'itens'}
                                                    </span>
                                                )}
                                            </div>

                                            {group.items.length > 0 ? (
                                                <div style={{ border: '1px solid var(--color-border)', borderRadius: '6px', overflow: 'hidden', backgroundColor: 'var(--color-bg-surface)' }}>
                                                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
                                                        <thead>
                                                            <tr style={{ backgroundColor: '#f5f3ff', borderBottom: '2px solid #e9d5ff' }}>
                                                                <th style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '40px' }}>#</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9' }}>Descrição</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '60px' }}>Qtd</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '60px' }}>Unid.</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '100px' }}>P. Unit. Est.</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '100px' }}>Total Est.</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '80px' }}>Prioridade</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '110px' }}>Tipo</th>
                                                                <th style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.7rem', letterSpacing: '0.05em', color: '#6d28d9', width: '180px' }}>Status</th>
                                                            </tr>
                                                        </thead>
                                                        <tbody>
                                                            {group.items.map((item: any, idx: number) => {
                                                                const isCatalog = !!item.itemCatalogId;
                                                                const priorityColors: Record<string, { bg: string; text: string; label: string }> = {
                                                                    'HIGH': { bg: '#fef2f2', text: '#dc2626', label: 'Alta' },
                                                                    'MEDIUM': { bg: '#fffbeb', text: '#d97706', label: 'Média' },
                                                                    'LOW': { bg: '#f0fdf4', text: '#16a34a', label: 'Baixa' }
                                                                };
                                                                const prio = priorityColors[item.itemPriority] || priorityColors['MEDIUM'];
                                                                const itemStatus = getBuyerItemStatus(item, group);
                                                                const isItemActionEligible = isLineItemEligibleForQuotation(item) && item.lineItemStatusCode !== 'CANCELLED' && item.lineItemStatusCode !== 'DELETED';
                                                                // Copy varies by context: closing the LAST pending item ends the
                                                                // quotation stage ("Encerrar sem cotação"), while with other items
                                                                // still pending the action only affects this one ("Desconsiderar item").
                                                                const pendingEligibleCount = group.items.filter((i: any) =>
                                                                    isLineItemEligibleForQuotation(i) && i.lineItemStatusCode !== 'CANCELLED' && i.lineItemStatusCode !== 'DELETED'
                                                                ).length;
                                                                const isLastPendingItem = isItemActionEligible && pendingEligibleCount === 1;

                                                                return (
                                                                    <tr key={item.lineItemId || idx} style={{
                                                                        borderBottom: idx < group.items.length - 1 ? '1px solid #f1f5f9' : 'none',
                                                                        backgroundColor: idx % 2 === 0 ? 'var(--color-bg-surface)' : 'var(--color-bg-page)'
                                                                    }}>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 800, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>
                                                                            {item.lineNumber}
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', fontWeight: 600, color: 'var(--color-text-main)' }}>
                                                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                                                <span>{item.itemDescription || '---'}</span>
                                                                                {item.costCenterName && (
                                                                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 500 }}>
                                                                                        CC: {item.costCenterCode ? `${item.costCenterCode} — ` : ''}{item.costCenterName}
                                                                                    </span>
                                                                                )}
                                                                            </div>
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 700 }}>
                                                                            {item.quantity}
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'center', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                                                                            {item.unitCode || '---'}
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 700 }}>
                                                                            {item.unitPrice > 0 ? formatCurrencyAO(item.unitPrice) : '---'}
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'right', fontWeight: 800, color: 'var(--color-text-main)' }}>
                                                                            {item.total > 0 ? formatCurrencyAO(item.total) : '---'}
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'center' }}>
                                                                            <span style={{
                                                                                display: 'inline-block',
                                                                                backgroundColor: prio.bg,
                                                                                color: prio.text,
                                                                                fontSize: '0.65rem',
                                                                                fontWeight: 800,
                                                                                padding: '2px 8px',
                                                                                borderRadius: '4px',
                                                                                textTransform: 'uppercase'
                                                                            }}>
                                                                                {prio.label}
                                                                            </span>
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'center' }}>
                                                                            <span style={{
                                                                                display: 'inline-flex',
                                                                                alignItems: 'center',
                                                                                gap: '4px',
                                                                                backgroundColor: isCatalog ? '#ecfdf5' : '#fffbeb',
                                                                                color: isCatalog ? '#059669' : '#d97706',
                                                                                fontSize: '0.65rem',
                                                                                fontWeight: 800,
                                                                                padding: '2px 8px',
                                                                                borderRadius: '4px',
                                                                                textTransform: 'uppercase',
                                                                                border: isCatalog ? '1px solid #a7f3d0' : '1px solid #fde68a'
                                                                            }}>
                                                                                {isCatalog ? '✓ Catálogo' : '✎ Manual'}
                                                                            </span>
                                                                        </td>
                                                                        <td style={{ padding: '10px 12px', textAlign: 'center' }}>
                                                                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px' }}>
                                                                                <span
                                                                                    title={itemStatus.hint}
                                                                                    style={{
                                                                                        display: 'inline-block',
                                                                                        backgroundColor: itemStatus.bg,
                                                                                        color: itemStatus.text,
                                                                                        border: `1px solid ${itemStatus.border}`,
                                                                                        fontSize: '0.65rem',
                                                                                        fontWeight: 800,
                                                                                        padding: '3px 8px',
                                                                                        borderRadius: '4px',
                                                                                        lineHeight: 1.3,
                                                                                        cursor: itemStatus.hint ? 'help' : 'default'
                                                                                    }}
                                                                                >
                                                                                    {itemStatus.label}
                                                                                </span>
                                                                                {canMutateQuotation && mode === 'BUYER' && isItemActionEligible && (
                                                                                    <button
                                                                                        onClick={() => setCloseNotQuotedModal({
                                                                                            show: true,
                                                                                            requestId: group.requestId,
                                                                                            lineItemId: item.lineItemId || item.id,
                                                                                            itemDescription: `Linha ${item.lineNumber} — ${item.itemDescription || item.description || ''}`,
                                                                                            isLastPendingItem
                                                                                        })}
                                                                                        title="O item deixará de ser considerado neste processo de cotação (exige motivo e justificativa)."
                                                                                        style={{
                                                                                            fontSize: '0.65rem',
                                                                                            fontWeight: 700,
                                                                                            color: '#6b7280',
                                                                                            backgroundColor: 'transparent',
                                                                                            border: '1px dashed #d1d5db',
                                                                                            borderRadius: '4px',
                                                                                            padding: '2px 8px',
                                                                                            cursor: 'pointer',
                                                                                            transition: 'all 0.15s'
                                                                                        }}
                                                                                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#f3f4f6'; e.currentTarget.style.color = '#374151'; }}
                                                                                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'transparent'; e.currentTarget.style.color = '#6b7280'; }}
                                                                                    >
                                                                                        {isLastPendingItem ? 'Encerrar sem cotação' : 'Desconsiderar item'}
                                                                                    </button>
                                                                                )}
                                                                            </div>
                                                                        </td>
                                                                    </tr>
                                                                );
                                                            })}
                                                        </tbody>
                                                    </table>
                                                </div>
                                            ) : (
                                                <div style={{
                                                    padding: '24px',
                                                    textAlign: 'center',
                                                    border: '2px dashed var(--color-border)',
                                                    borderRadius: '8px',
                                                    backgroundColor: 'var(--color-bg-surface)'
                                                }}>
                                                    <p style={{ margin: 0, color: 'var(--color-text-muted)', fontSize: '0.85rem', fontWeight: 600 }}>
                                                        Este pedido não possui itens detalhados (apenas dados de cabeçalho).
                                                    </p>
                                                </div>
                                            )}
                                        </div>

                                        {/* SECTION A: Existing Quotations / Documents */}
                                        <div data-tour="buyer-open-request-quotations" {...(groupIndex === 0 ? { 'data-guide': 'qm-docs-section' } : {})} style={{
                                            padding: '24px',
                                            backgroundColor: 'var(--color-bg-page)',
                                            border: '1px solid var(--color-border)',
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
                                                            // Option C: items of this quotation used in a CANCELLED batch
                                                            const reuseBlockedCount = (q.items || []).filter((qi: any) => qi.isReuseBlocked).length;
                                                            const reuseAuthorizedCount = (q.items || []).filter((qi: any) => qi.isReuseAuthorized).length;

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
                                                                                    {reuseBlockedCount > 0 && (
                                                                                        <>
                                                                                            <span style={{ backgroundColor: '#F59E0B', color: '#fff', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                                                                                Usada em lote cancelado
                                                                                            </span>
                                                                                            <span style={{ backgroundColor: '#FEF3C7', color: '#92400E', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase', border: '1px solid #F59E0B' }}>
                                                                                                Reuso requer confirmação
                                                                                            </span>
                                                                                        </>
                                                                                    )}
                                                                                    {reuseBlockedCount === 0 && reuseAuthorizedCount > 0 && (
                                                                                        <span style={{ backgroundColor: '#DCFCE7', color: '#166534', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase', border: '1px solid #86efac' }}>
                                                                                            Reuso autorizado ({reuseAuthorizedCount})
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
                                                                            {/* Option C — explicit reuse action for cancelled-batch quotations */}
                                                                            {mode === 'BUYER' && reuseBlockedCount > 0 && (
                                                                                <div onClick={(e) => e.stopPropagation()}>
                                                                                    <button
                                                                                        onClick={() => setReuseModal({ requestId: group.requestId, quotation: q })}
                                                                                        style={{
                                                                                            display: 'flex', alignItems: 'center', gap: '6px', padding: '6px 12px',
                                                                                            backgroundColor: '#D97706', border: 'none', borderRadius: '4px',
                                                                                            color: '#fff', fontWeight: 800, fontSize: '0.7rem',
                                                                                            textTransform: 'uppercase', cursor: 'pointer'
                                                                                        }}
                                                                                    >
                                                                                        <History size={12} /> Reutilizar cotação
                                                                                    </button>
                                                                                </div>
                                                                            )}

                                                                            {/* Maintenance Actions (Step 8.1) */}
                                                                            {canMutateQuotation && mode === 'BUYER' && (
                                                                                <div style={{ display: 'flex', gap: '8px' }} onClick={(e) => e.stopPropagation()}>
                                                                                    {(() => {
                                                                                        const isUsedInBatch = group.approvalBatches?.some((po: any) => po.items?.some((poi: any) => q.items?.some((qi: any) => qi.id === poi.selectedQuotationItemId || (poi.candidates || []).some((c: any) => c.quotationItemId === qi.id))));
                                                                                        if (isUsedInBatch) {
                                                                                            return (
                                                                                                <div style={{
                                                                                                    display: 'flex', alignItems: 'center', gap: '4px', padding: '6px 12px',
                                                                                                    backgroundColor: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '4px',
                                                                                                    color: '#166534', fontWeight: 800, fontSize: '0.7rem',
                                                                                                    textTransform: 'uppercase'
                                                                                                }}>
                                                                                                    <ShieldCheck size={12} /> Protegida por auditoria
                                                                                                </div>
                                                                                            );
                                                                                        }
                                                                                        return (
                                                                                            <>
                                                                                                <button
                                                                                        onClick={() => wizardController.handleOpenWizard(group, q.sourceType === 'OCR' ? 'UPLOAD' : 'MANUAL', q)}
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
                                                                                </>
                                                                                        );
                                                                                    })()}
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
        {...(groupIndex === 0 ? { 'data-guide': 'qm-add-quotation' } : {})}
        id={`section-b-${group.requestId}`}
        className={highlightedRequestId === group.requestId ? 'section-attention-highlight' : ''}
        style={{
            padding: '24px',
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-sm)',
            display: 'flex',
            flexDirection: 'column',
            gap: '20px',
            boxShadow: 'var(--shadow-md)',
            transition: 'all 0.3s ease'
        }}
    >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                <Plus size={18} /> SEÇÃO B: ADICIONAR NOVA COTAÇÃO
            </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
            <button
                onClick={() => wizardController.handleOpenWizard(group, 'UPLOAD')}
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
                onClick={() => wizardController.handleOpenWizard(group, 'MANUAL')}
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
    </div>
                                        )}

                                        {/* SECTION C: Approval Batches */}
                                        {group.approvalBatches && group.approvalBatches.length > 0 && (
                                            <div style={{
                                                padding: '24px',
                                                backgroundColor: 'var(--color-bg-page)',
                                                border: '1px solid var(--color-border)',
                                                borderRadius: 'var(--radius-sm)',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                gap: '20px',
                                                marginTop: '16px'
                                            }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                    <Layers size={18} /> LOTES DE APROVAÇÃO ENVIADOS
                                                </div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                                    {group.approvalBatches.map((batch: any) => {
                                                        const isReversible = ['WAITING_AREA_APPROVAL', 'AREA_ADJUSTMENT', 'WAITING_FINAL_APPROVAL', 'FINAL_ADJUSTMENT'].includes(batch.status);
                                                        const batchBadgeColor = 
                                                            batch.status === 'APPROVED' ? 'success' :
                                                            batch.status === 'REJECTED' ? 'danger' :
                                                            batch.status === 'CANCELLED' ? 'neutral' : 'warning';
                                                        
                                                        const batchStatusLabel = 
                                                            batch.status === 'WAITING_AREA_APPROVAL' ? 'Aguardando Aprovação da Área' :
                                                            batch.status === 'AREA_ADJUSTMENT' ? 'Ajuste da Área Requerido' :
                                                            batch.status === 'WAITING_FINAL_APPROVAL' ? 'Aguardando Aprovação Final' :
                                                            batch.status === 'FINAL_ADJUSTMENT' ? 'Ajuste Final Requerido' :
                                                            batch.status === 'APPROVED' ? 'Aprovado' :
                                                            batch.status === 'REJECTED' ? 'Rejeitado' :
                                                            batch.status === 'CANCELLED' ? 'Cancelado' : batch.status;

                                                        return (
                                                            <div key={batch.id} style={{
                                                                backgroundColor: 'var(--color-bg-surface)',
                                                                border: '1px solid var(--color-border)',
                                                                borderRadius: '8px',
                                                                padding: '16px',
                                                                display: 'flex',
                                                                justifyContent: 'space-between',
                                                                alignItems: 'center'
                                                            }}>
                                                                <div>
                                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                                                        <span style={{ fontWeight: 800, fontSize: '0.95rem', color: 'var(--color-primary)' }}>
                                                                            Lote #{batch.batchNumber}
                                                                        </span>
                                                                        <span className={`badge badge-sm badge-${batchBadgeColor}`}>
                                                                            {batchStatusLabel}
                                                                        </span>
                                                                    </div>
                                                                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'flex', gap: '12px' }}>
                                                                        <span>Criado em: {new Date(batch.createdAtUtc).toLocaleDateString('pt-PT')}</span>
                                                                        <span>•</span>
                                                                        <span>Itens: {batch.items?.length || 0}</span>
                                                                        {(() => {
                                                                            const optionCount = (batch.items || []).reduce((acc: number, bi: any) => acc + (bi.candidates?.length || 0), 0);
                                                                            return optionCount > 0 ? (<><span>•</span><span>Opções: {optionCount}</span></>) : null;
                                                                        })()}
                                                                    </div>
                                                                    {batch.comment && (
                                                                        <div style={{ fontSize: '0.8rem', color: 'var(--color-text-body)', marginTop: '8px', fontStyle: 'italic' }}>
                                                                            "{batch.comment}"
                                                                        </div>
                                                                    )}
                                                                </div>
                                                                <div style={{ display: 'flex', gap: '8px', flexShrink: 0 }}>
                                                                    {canMutateQuotation && mode === 'BUYER' && (batch.status === 'AREA_ADJUSTMENT' || batch.status === 'FINAL_ADJUSTMENT') && (
                                                                        <button
                                                                            onClick={(e) => {
                                                                                e.stopPropagation();
                                                                                handleOpenBatchRework(group, batch);
                                                                            }}
                                                                            style={{
                                                                                padding: '6px 12px',
                                                                                fontSize: '0.75rem',
                                                                                fontWeight: 700,
                                                                                color: '#0284c7',
                                                                                backgroundColor: '#e0f2fe',
                                                                                border: '1px solid #bae6fd',
                                                                                borderRadius: '4px',
                                                                                cursor: 'pointer',
                                                                                transition: 'all 0.2s'
                                                                            }}
                                                                            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#bae6fd'; }}
                                                                            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#e0f2fe'; }}
                                                                        >
                                                                            Revisar Lote para Reenvio
                                                                        </button>
                                                                    )}
                                                                    {canMutateQuotation && mode === 'BUYER' && isReversible && (
                                                                        <button
                                                                            onClick={(e) => {
                                                                                e.stopPropagation();
                                                                                handleOpenCancelApproval(group.requestId, batch.id, batch.batchNumber);
                                                                            }}
                                                                            style={{
                                                                                padding: '6px 12px',
                                                                                fontSize: '0.75rem',
                                                                                fontWeight: 700,
                                                                                color: '#dc2626',
                                                                                backgroundColor: '#fef2f2',
                                                                                border: '1px solid #fecaca',
                                                                                borderRadius: '4px',
                                                                                cursor: 'pointer',
                                                                                transition: 'all 0.2s'
                                                                            }}
                                                                            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#fee2e2'; }}
                                                                            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#fef2f2'; }}
                                                                        >
                                                                            Cancelar Lote
                                                                        </button>
                                                                    )}
                                                                </div>
                                                            </div>
                                                        );
                                                    })}
                                                </div>
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
                        boxShadow: 'var(--shadow-md)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                            <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.9rem', textTransform: 'uppercase' }}>Itens por página:</span>
                            <select
                                value={pageSize}
                                onChange={(e) => {
                                    updateParams({ pageSize: Number(e.target.value), page: 1 });
                                }}
                                style={{
                                    padding: '8px 12px', border: '1px solid var(--color-border)',
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
                        newStatusCode: null, isLastItem: false
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
                            border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-md)',
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
                                    style={{ padding: '10px 20px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', background: 'none', fontWeight: 700, cursor: 'pointer', fontSize: '0.875rem' }}
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

            {fileDuplicateWarning?.isOpen && createPortal(
                <AnimatePresence>
                    <div
                        ref={duplicateWarningRef}
                        onKeyDown={handleDuplicateWarningKeyDown}
                        role="dialog"
                        aria-modal="true"
                        aria-labelledby="duplicate-warning-title"
                        style={{ position: 'fixed', inset: 0, zIndex: `calc(${Z_INDEX.MODAL} + 50)` as any, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '16px', backgroundColor: 'rgba(15, 23, 42, 0.6)', backdropFilter: 'blur(4px)' }}
                    >
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
                                <h3 id="duplicate-warning-title" style={{ fontSize: '1.25rem', fontWeight: 900, textAlign: 'center', color: '#0f172a', marginBottom: '8px', textTransform: 'uppercase', letterSpacing: '-0.01em' }}>
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
                                        disabled={dupCountdown > 0}
                                        onClick={() => {
                                            if (fileDuplicateWarning?.uploadCallback) {
                                                fileDuplicateWarning.uploadCallback();
                                            }
                                        }}
                                        style={{ flex: 1.2, padding: '10px 16px', fontSize: '0.8rem', fontWeight: 800, color: 'white', backgroundColor: '#ea580c', border: 'none', borderRadius: '6px', cursor: dupCountdown > 0 ? 'not-allowed' : 'pointer', textTransform: 'uppercase', boxShadow: '0 4px 0 #9a3412', opacity: dupCountdown > 0 ? 0.6 : 1, transition: 'opacity 0.3s ease' }}
                                    >
                                        {dupCountdown > 0 ? `Estou Ciente, Prosseguir (${dupCountdown})` : 'Estou Ciente, Prosseguir'}
                                    </button>
                                </div>
                            </div>
                        </motion.div>
                    </div>
                </AnimatePresence>,
                document.body
            )}

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

            {/* Option C — explicit reuse of cancelled-batch quotations */}
            <QuotationReuseModal
                isOpen={!!reuseModal}
                requestId={reuseModal?.requestId || ''}
                quotation={reuseModal?.quotation || null}
                onClose={() => setReuseModal(null)}
                onAuthorized={() => { loadData(); }}
            />

            {/* Quick View Drawer */}
            <RequestDrawerPresentation
                isOpen={!!drawerRequestId}
                requestId={drawerRequestId}
                onClose={() => setDrawerRequestId(null)}
            />

            </div>

            <PartialApprovalBatchModal
                isOpen={partialApprovalModal.show}
                onClose={() => setPartialApprovalModal({ show: false, group: null })}
                group={partialApprovalModal.group}
                onSubmit={handlePartialApprovalSubmit}
            />

            <BatchReworkModal
                isOpen={batchReworkModal.show}
                onClose={() => setBatchReworkModal({ show: false, group: null, batch: null })}
                group={batchReworkModal.group}
                batch={batchReworkModal.batch}
                onSuccess={handleBatchReworkSuccess}
                // QF4: this classic screen IS the quotation-management surface — closing the modal
                // lands the buyer directly on the request's quotation tools.
                onManageQuotations={() => setBatchReworkModal({ show: false, group: null, batch: null })}
            />

            <CancelApprovalBatchModal
                isOpen={cancelApprovalModal.show}
                onClose={() => setCancelApprovalModal({ show: false, requestId: '', batchId: '', batchNumber: 0 })}
                requestId={cancelApprovalModal.requestId}
                batchId={cancelApprovalModal.batchId}
                batchNumber={cancelApprovalModal.batchNumber}
                onSuccess={handleCancelSuccess}
            />

            <CloseNotQuotedModal
                isOpen={closeNotQuotedModal.show}
                onClose={() => setCloseNotQuotedModal({ show: false, requestId: '', lineItemId: '', itemDescription: '', isLastPendingItem: false })}
                requestId={closeNotQuotedModal.requestId}
                lineItemId={closeNotQuotedModal.lineItemId}
                itemDescription={closeNotQuotedModal.itemDescription}
                isLastPendingItem={closeNotQuotedModal.isLastPendingItem}
                onSuccess={() => {
                    setFeedback({
                        type: 'success',
                        message: closeNotQuotedModal.isLastPendingItem
                            ? 'Item encerrado sem cotação. Não há mais pendências de cotação neste pedido; a decisão foi registrada no histórico.'
                            : 'Item desconsiderado neste processo de cotação. A decisão foi registrada no histórico do pedido.'
                    });
                    loadData();
                }}
            />

            <QuotationWizardModal
                request={wizardActiveRequest}
                wizardState={quotationWizardState}
                onSaveQuotation={wizardController.handleWizardSaveQuotation}
                onReconcilePreview={wizardController.handleReconcilePreview}
                isProcessingOcr={!!(wizardActiveRequest && isProcessingOcr[wizardActiveRequest.requestId])}
                onUploadFile={handleUploadFileForWizard}
                onCancelWizard={wizardController.onCancelWizard}
                onReplaceDocument={wizardController.handleReplaceDocumentForWizard}
                ivaRates={ivaRates}
                units={units}
                currencies={currencies}
                onRequestLineItemUpserted={wizardController.handleWizardLineItemUpserted}
            />

        </PageContainer>
    );
}
