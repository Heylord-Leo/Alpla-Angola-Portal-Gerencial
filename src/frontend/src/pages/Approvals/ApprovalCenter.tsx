import React, { useEffect, useState, useCallback } from 'react';
import { useLocation } from 'react-router-dom';
import { Z_INDEX } from '../../constants/ui';
import { api } from '../../lib/api';
import { RequestListItemDto, RequestDetailsDto, PendingApprovalsResponseDto, ItemIntelligenceDto } from '../../types';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { ApprovalDetailPanel } from './ApprovalDetailPanel';
import { DetailedHistoryPanel } from './components/DetailedHistoryPanel';
import { AlertCircle, Building2, User, Landmark, ShieldCheck, Inbox, ChevronRight, FileText as FileContract, Info } from 'lucide-react';
import { formatDate, formatCurrencyAO, getUrgencyStyle } from '../../lib/utils';
import { motion, AnimatePresence } from 'framer-motion';
import { DropdownPortal } from '../../components/ui/DropdownPortal';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { QueueSummary } from './components/QueueSummary';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { Tooltip } from '../../components/ui/Tooltip';
import {
    ContractApprovalItem,
    PendingContractApprovalsResponse,
    fetchPendingContractApprovals,
    contractTechnicalApprove,
    contractTechnicalReturn,
    contractFinalApprove,
    contractFinalReturn
} from '../../lib/contractsApi';
import { ContractApprovalPanel } from './components/ContractApprovalPanel';

// --- Types ---

type ApprovalStage = 'AREA' | 'FINAL';
type SortMode = 'default' | 'oldest' | 'value_desc' | 'value_asc';

const HIGH_VALUE_THRESHOLD = 500000;

// --- Component ---

export function ApprovalCenter() {
    const { user, isAdmin } = useAuth();
    const location = useLocation();

    const [flashedRequestId, setFlashedRequestId] = useState<string | null>(location.state?.flashRequestId || null);

    useEffect(() => {
        if (flashedRequestId) {
            window.history.replaceState({}, document.title);
            const timer = setTimeout(() => {
                setFlashedRequestId(null);
            }, 5000);
            return () => clearTimeout(timer);
        }
    }, [flashedRequestId]);

    const isAreaApprover = isAdmin || (user?.roles?.includes(ROLES.AREA_APPROVER) ?? false);
    const isFinalApprover = isAdmin || (user?.roles?.includes(ROLES.FINAL_APPROVER) ?? false);

    // Queue state
    const [loading, setLoading] = useState(true);
    const [data, setData] = useState<PendingApprovalsResponseDto | null>(null);
    const [contractApprovals, setContractApprovals] = useState<PendingContractApprovalsResponse | null>(null);

    // Selection state
    const [selectedRequestId, setSelectedRequestId] = useState<string | null>(null);
    const [selectedApprovalStage, setSelectedApprovalStage] = useState<ApprovalStage | null>(null);
    const [isPanelOpen, setIsPanelOpen] = useState(false);
    const [selectedDetailedItem, setSelectedDetailedItem] = useState<ItemIntelligenceDto | null>(null);

    // Contract selection state (DEC-118)
    const [selectedContractId, setSelectedContractId] = useState<string | null>(null);
    const [selectedContractStage, setSelectedContractStage] = useState<'TECHNICAL' | 'FINAL' | null>(null);
    const [isContractPanelOpen, setIsContractPanelOpen] = useState(false);

    // Detail state
    const [detailLoading, setDetailLoading] = useState(false);
    const [detailData, setDetailData] = useState<RequestDetailsDto | null>(null);

    // --- Triage State (Phase 4) ---
    const [sortMode, setSortMode] = useState<SortMode>('default');
    const [activeFilters, setActiveFilters] = useState<string[]>([]);



    // --- Filtered & Sorted Data ---
    const filteredAndSortedData = React.useMemo(() => {
        if (!data) return { area: [], final: [], flat: [] };

        const applyFilters = (requests: RequestListItemDto[]) => {
            return requests.filter(req => {
                if (activeFilters.includes('urgent')) {
                    const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);
                    if (!urgency || urgency.priority < 2) return false;
                }
                if (activeFilters.includes('high_value')) {
                    if ((req.estimatedTotalAmount || 0) < HIGH_VALUE_THRESHOLD) return false;
                }
                if (activeFilters.includes('has_alert')) {
                    if (req.requestTypeCode === 'QUOTATION' && req.selectedQuotationId) return false;
                    if (req.requestTypeCode !== 'QUOTATION') return false;
                }
                return true;
            });
        };

        const applySort = (requests: RequestListItemDto[]) => {
            const list = [...requests];
            switch (sortMode) {
                case 'oldest':
                    return list.sort((a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime());
                case 'value_desc':
                    return list.sort((a, b) => (b.estimatedTotalAmount || 0) - (a.estimatedTotalAmount || 0));
                case 'value_asc':
                    return list.sort((a, b) => (a.estimatedTotalAmount || 0) - (b.estimatedTotalAmount || 0));
                default:
                    return list; // Backend default
            }
        };

        const areaRaw = data.areaApprovals || [];
        const finalRaw = data.finalApprovals || [];

        const areaProcessed = activeFilters.includes('final_only') ? [] : applySort(applyFilters(areaRaw));
        const finalProcessed = activeFilters.includes('area_only') ? [] : applySort(applyFilters(finalRaw));

        return {
            area: areaProcessed,
            final: finalProcessed,
            flat: [...areaProcessed, ...finalProcessed]
        };
    }, [data, sortMode, activeFilters]);

    const { area: areaApprovals, final: finalApprovals, flat: flatQueue } = filteredAndSortedData;

    // --- Navigation Logic ---
    const currentIndex = flatQueue.findIndex(r => r.id === selectedRequestId);
    
    const handleNext = () => {
        if (currentIndex < flatQueue.length - 1) {
            const next = flatQueue[currentIndex + 1];
            // Determine if next is in area or final (must check processed lists)
            const isArea = areaApprovals.some(r => r.id === next.id);
            setSelectedRequestId(next.id);
            setSelectedApprovalStage(isArea ? 'AREA' : 'FINAL');
            setSelectedDetailedItem(null);
        }
    };

    const handlePrev = () => {
        if (currentIndex > 0) {
            const prev = flatQueue[currentIndex - 1];
            const isArea = areaApprovals.some(r => r.id === prev.id);
            setSelectedRequestId(prev.id);
            setSelectedApprovalStage(isArea ? 'AREA' : 'FINAL');
            setSelectedDetailedItem(null);
        }
    };

    // Keyboard navigation
    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            if (!isPanelOpen || detailLoading) return;
            
            // Safeguard: do not navigate if user is typing in an input/textarea
            const activeTag = document.activeElement?.tagName.toLowerCase();
            if (activeTag === 'input' || activeTag === 'textarea' || document.activeElement?.getAttribute('contenteditable') === 'true') {
                return;
            }

            if (e.key === 'ArrowRight') {
                handleNext();
            } else if (e.key === 'ArrowLeft') {
                handlePrev();
            }
        };

        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [isPanelOpen, detailLoading, handleNext, handlePrev]);

    // --- Drawer Resizing ---
    const [drawerWidth, setDrawerWidth] = useState(() => {
        const saved = localStorage.getItem('approvalDrawerWidth');
        if (saved) {
            const parsed = parseInt(saved, 10);
            if (!isNaN(parsed) && parsed >= 520) return parsed;
        }
        return 640; // Default
    });
    const [isResizing, setIsResizing] = useState(false);

    const handleMouseDown = useCallback((e: React.MouseEvent) => {
        if (window.innerWidth <= 768) return;
        setIsResizing(true);
        e.preventDefault();
    }, []);

    const handleMouseMove = useCallback((e: MouseEvent) => {
        if (!isResizing) return;
        
        const newWidth = window.innerWidth - e.clientX;
        const minWidth = 520;
        const maxWidth = window.innerWidth * 0.9;
        
        const clampedWidth = Math.max(minWidth, Math.min(newWidth, maxWidth));
        setDrawerWidth(clampedWidth);
    }, [isResizing]);

    const handleMouseUp = useCallback(() => {
        if (isResizing) {
            setIsResizing(false);
            localStorage.setItem('approvalDrawerWidth', drawerWidth.toString());
        }
    }, [isResizing, drawerWidth]);

    useEffect(() => {
        if (isResizing) {
            window.addEventListener('mousemove', handleMouseMove);
            window.addEventListener('mouseup', handleMouseUp);
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
        } else {
            window.removeEventListener('mousemove', handleMouseMove);
            window.removeEventListener('mouseup', handleMouseUp);
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
        }
        return () => {
            window.removeEventListener('mousemove', handleMouseMove);
            window.removeEventListener('mouseup', handleMouseUp);
        };
    }, [isResizing, handleMouseMove, handleMouseUp]);

    // Feedback state
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    // --- Data Loading ---

    const loadQueue = useCallback(async () => {
        try {
            const [requestsResponse, contractsResponse] = await Promise.all([
                api.requests.getPendingApprovals(),
                fetchPendingContractApprovals().catch(() => null)
            ]);
            setData(requestsResponse);
            setContractApprovals(contractsResponse);
        } catch (error) {
            console.error('Failed to load pending approvals:', error);
        } finally {
            setLoading(false);
        }
    }, []);

    const loadDetailData = useCallback(async (requestId: string) => {
        setDetailLoading(true);
        try {
            const details = await api.requests.get(requestId);
            setDetailData(details);
        } catch (err: any) {
            console.error('Failed to load request details:', err);
            setFeedback({ type: 'error', message: 'Falha ao carregar detalhes do pedido.' });
            setSelectedRequestId(null);
            setIsPanelOpen(false);
            setDetailData(null);
        } finally {
            setDetailLoading(false);
        }
    }, []);

    // Refresh detail data without resetting selection (for winner selection updates)
    const refreshDetailData = useCallback(async () => {
        if (!selectedRequestId) return;
        try {
            const details = await api.requests.get(selectedRequestId);
            setDetailData(details);
        } catch (err: any) {
            console.error('Failed to refresh detail data:', err);
        }
    }, [selectedRequestId]);

    useEffect(() => {
        loadQueue();
    }, [loadQueue]);

    // Load detail when selection changes
    useEffect(() => {
        if (selectedRequestId && isPanelOpen) {
            loadDetailData(selectedRequestId);
        }
    }, [selectedRequestId, isPanelOpen, loadDetailData]);

    // --- Handlers ---

    const handleRowSelect = (id: string, stage: ApprovalStage) => {
        setSelectedRequestId(id);
        setSelectedApprovalStage(stage);
        setIsPanelOpen(true);
        setDetailData(null); // Clear old detail while new one loads
        setSelectedDetailedItem(null); // Reset drill-down
    };

    const handleCloseDetail = () => {
        setIsPanelOpen(false);
        setSelectedRequestId(null);
        setSelectedApprovalStage(null);
        setDetailData(null);
        setSelectedDetailedItem(null);
        // Also close contract panel if open
        setIsContractPanelOpen(false);
        setSelectedContractId(null);
        setSelectedContractStage(null);
    };

    const handleContractRowSelect = (id: string, stage: 'TECHNICAL' | 'FINAL') => {
        // Close request panel first
        setIsPanelOpen(false);
        setSelectedRequestId(null);
        setSelectedContractId(id);
        setSelectedContractStage(stage);
        setIsContractPanelOpen(true);
    };

    const handleCloseContractPanel = () => {
        setIsContractPanelOpen(false);
        setSelectedContractId(null);
        setSelectedContractStage(null);
    };

    const handleContractActionCompleted = async (successMessage: string) => {
        setFeedback({ type: 'success', message: successMessage });
        handleCloseContractPanel();
        loadQueue();
    };

    const handleActionCompleted = async (successMessage: string) => {
        setFeedback({ type: 'success', message: successMessage });

        // Refresh the queue
        try {
            const newData = await api.requests.getPendingApprovals();
            // We need to find the "next" item based on the active filters and sort
            // Since state updates are async, we re-apply the logic to the new data
            
            const areaFiltered = activeFilters.includes('final_only') ? [] : (newData.areaApprovals || []).filter(req => {
                if (activeFilters.includes('urgent')) {
                    const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);
                    if (!urgency || urgency.priority < 2) return false;
                }
                if (activeFilters.includes('high_value')) {
                    if ((req.estimatedTotalAmount || 0) < HIGH_VALUE_THRESHOLD) return false;
                }
                if (activeFilters.includes('has_alert')) {
                    if (req.requestTypeCode === 'QUOTATION' && req.selectedQuotationId) return false;
                    if (req.requestTypeCode !== 'QUOTATION') return false;
                }
                return true;
            });

            const finalFiltered = activeFilters.includes('area_only') ? [] : (newData.finalApprovals || []).filter(req => {
                if (activeFilters.includes('urgent')) {
                    const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);
                    if (!urgency || urgency.priority < 2) return false;
                }
                if (activeFilters.includes('high_value')) {
                    if ((req.estimatedTotalAmount || 0) < HIGH_VALUE_THRESHOLD) return false;
                }
                if (activeFilters.includes('has_alert')) {
                    if (req.requestTypeCode === 'QUOTATION' && req.selectedQuotationId) return false;
                    if (req.requestTypeCode !== 'QUOTATION') return false;
                }
                return true;
            });

            const sortList = (list: RequestListItemDto[]) => {
                const res = [...list];
                switch (sortMode) {
                    case 'oldest': return res.sort((a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime());
                    case 'value_desc': return res.sort((a, b) => (b.estimatedTotalAmount || 0) - (a.estimatedTotalAmount || 0));
                    case 'value_asc': return res.sort((a, b) => (a.estimatedTotalAmount || 0) - (b.estimatedTotalAmount || 0));
                    default: return res;
                }
            };

            const newSortedArea = sortList(areaFiltered);
            const newSortedFinal = sortList(finalFiltered);
            const newFlatQueue = [...newSortedArea, ...newSortedFinal];

            setData(newData); // Update the main data state
            
            if (newFlatQueue.length > 0) {
                // Try to pick the item that now occupies the same index
                const nextItem = newFlatQueue[currentIndex] || newFlatQueue[newFlatQueue.length - 1];
                if (nextItem) {
                    const isNewArea = newSortedArea.some(r => r.id === nextItem.id);
                    setSelectedRequestId(nextItem.id);
                    setSelectedApprovalStage(isNewArea ? 'AREA' : 'FINAL');
                    setSelectedDetailedItem(null);
                    // Detail data will be updated by the useEffect watching selectedRequestId
                    return;
                }
            }
            
            // If we fall through, close detail
            handleCloseDetail();
        } catch (err) {
            console.error('Failed to refresh queue after action:', err);
            handleCloseDetail();
        }
    };

    const handleSeedData = async () => {
        try {
            await api.dev.seedIntelligence();
            setFeedback({ type: 'success', message: 'INTELIGÊNCIA: Dados de teste semeados com sucesso.' });
            loadQueue();
        } catch (err: any) {
            console.error("Seed error:", err);
            setFeedback({ type: 'error', message: `Falha ao semear dados: ${err.message || 'Erro de comunicação.'}` });
        }
    };

    const handleCleanupData = async () => {
        try {
            await api.dev.cleanupIntelligence();
            setFeedback({ type: 'success', message: 'INTELIGÊNCIA: Dados de teste removidos para limpeza.' });
            loadQueue();
            handleCloseDetail();
        } catch (err: any) {
            console.error("Cleanup error:", err);
            setFeedback({ type: 'error', message: `Falha ao limpar dados: ${err.message || 'Erro de comunicação.'}` });
        }
    };

    const [isHoveringHandle, setIsHoveringHandle] = useState(false);

    // --- Render ---

    if (loading) {
        return (
            <div style={{ padding: '60px', textAlign: 'center', color: 'var(--color-primary)', fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                A Carregar Fila de Aprovações...
            </div>
        );
    }

    const totalPending = (data?.areaApprovals?.length || 0) + (data?.finalApprovals?.length || 0);

    return (
        <PageContainer>
            <style>{`
                @keyframes flashRedAlert {
                    0% { background-color: transparent; }
                    10% { background-color: rgba(239, 68, 68, 0.2); }
                    50% { background-color: rgba(239, 68, 68, 0.05); }
                    90% { background-color: rgba(239, 68, 68, 0.2); }
                    100% { background-color: transparent; }
                }
                .flash-red-row {
                    animation: flashRedAlert 5s ease-out;
                }
            `}</style>
            {/* Feedback */}
            {feedback.message && (
                <div style={{ position: 'sticky', top: 'calc(var(--header-height) - 1rem)', zIndex: 10, backgroundColor: 'var(--color-bg-surface)', padding: '2rem 0 0 0', margin: '-2rem 0 0 0', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                </div>
            )}

            <PageHeader
                title="Centro de Aprovações"
                subtitle="Workspace centralizado para decisões e aprovações de Procurement."
            />

            {/* Queue Summary — KPI Cards (Phase 4) */}
            <QueueSummary
                areaApprovals={data?.areaApprovals || []}
                finalApprovals={data?.finalApprovals || []}
                pendingContracts={
                    contractApprovals
                        ? (contractApprovals.technicalApprovals.length + contractApprovals.finalApprovals.length)
                        : undefined
                }
            />

            {/* Triage Controls — Finance-style tab bar */}
            <SearchFilterBar
                searchValue=""
                onSearchChange={() => {}}
                tabs={[
                    { id: 'sort_default', label: '⏰ Urgência' },
                    { id: 'sort_oldest',  label: '🕐 Mais Antigo' },
                    { id: 'sort_value',   label: '📈 Maior Valor' },
                    { id: 'filter_urgent',     label: 'Urgentes' },
                    { id: 'filter_high_value', label: 'Alto Valor' },
                    { id: 'filter_has_alert',  label: 'Com Alerta' },
                    { id: 'filter_area_only',  label: 'Apenas Área' },
                    { id: 'filter_final_only', label: 'Apenas Final' },
                ]}
                activeTabId={
                    activeFilters.length === 1 ? `filter_${activeFilters[0]}`
                    : sortMode !== 'default'   ? `sort_${sortMode === 'oldest' ? 'oldest' : 'value'}`
                    : 'sort_default'
                }
                onTabChange={(id) => {
                    if (id.startsWith('sort_')) {
                        setActiveFilters([]);
                        const modeMap: Record<string, SortMode> = { sort_default: 'default', sort_oldest: 'oldest', sort_value: 'value_desc' };
                        setSortMode(modeMap[id] ?? 'default');
                    } else {
                        const filterId = id.replace('filter_', '');
                        setSortMode('default');
                        setActiveFilters(prev =>
                            prev.includes(filterId) ? prev.filter(f => f !== filterId) : [filterId]
                        );
                    }
                }}
            />

            {/* DEV TOOLS — visually subordinate in dev only */}
            {import.meta.env.DEV && (
                <div style={{ display: 'flex', gap: '12px', alignItems: 'center', padding: '8px 12px', border: '1px dashed var(--color-border)', backgroundColor: 'var(--color-bg-page)' }}>
                    <span style={{ fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em', opacity: 0.6 }}>Dev</span>
                    <button onClick={handleSeedData} style={{ background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', fontWeight: 700, fontSize: '0.7rem', textTransform: 'uppercase', textDecoration: 'underline' }}>Semear Inteligência</button>
                    <button onClick={handleCleanupData} style={{ background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', fontWeight: 700, fontSize: '0.7rem', textTransform: 'uppercase', textDecoration: 'underline' }}>Limpar Dados</button>
                </div>
            )}

            {/* QUEUE SECTIONS (Master) */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                {isAreaApprover && (
                    <div style={{
                        backgroundColor: '#eff6ff',
                        border: '1px solid #bfdbfe',
                        borderRadius: '12px',
                        padding: '24px',
                    }}>
                        <ApprovalQueueSection
                            title="Aguardando minha aprovação de área"
                            icon={<Building2 size={20} />}
                            tooltip="Pedidos que aguardam a sua validação de área. Clique num cartão para abrir o painel de detalhe e escolher Aprovar ou Devolver. A aprovação de área é a primeira etapa do fluxo e confirma que o pedido está alinhado com a necessidade do departamento."
                            requests={areaApprovals}
                            showCostCenter={true}
                            selectedId={selectedRequestId}
                            flashedId={flashedRequestId}
                            onRowSelect={(id) => handleRowSelect(id, 'AREA')}
                        />
                    </div>
                )}

                {isFinalApprover && (
                    <div style={{
                        backgroundColor: '#f0fdf4',
                        border: '1px solid #bbf7d0',
                        borderRadius: '12px',
                        padding: '24px',
                    }}>
                        <ApprovalQueueSection
                            title="Aguardando minha aprovação final"
                            icon={<ShieldCheck size={20} />}
                            tooltip="Pedidos que já passaram pela aprovação de área e aguardam a sua decisão final. Esta é a última etapa antes do pedido ser encaminhado para Procurement. Clique num cartão para analisar os detalhes e aprovar ou devolver para revisão."
                            requests={finalApprovals}
                            selectedId={selectedRequestId}
                            flashedId={flashedRequestId}
                            onRowSelect={(id) => handleRowSelect(id, 'FINAL')}
                        />
                    </div>
                )}

                {/* ── Contracts Section (DEC-118) ── */}
                {(isAreaApprover || isFinalApprover) && contractApprovals &&
                    (contractApprovals.technicalApprovals.length > 0 || contractApprovals.finalApprovals.length > 0) && (
                        <div style={{
                            backgroundColor: '#faf5ff',
                            border: '1px solid #ddd6fe',
                            borderRadius: '12px',
                            padding: '24px',
                        }}>
                            <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                {/* Section header — same pattern as request sections */}
                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', borderLeft: '4px solid #7c3aed', paddingLeft: '16px' }}>
                                    <span style={{ color: '#7c3aed' }}><FileContract size={20} /></span>
                                    <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-main)', letterSpacing: '-0.02em' }}>
                                        Contratos Pendentes
                                    </h2>
                                    <Tooltip
                                        variant="light"
                                        side="bottom"
                                        align="start"
                                        content={
                                            <div style={{ fontSize: '0.82rem', lineHeight: 1.6 }}>
                                                <strong style={{ display: 'block', marginBottom: 6, color: '#7c3aed' }}>Aprovação de Contratos</strong>
                                                Contratos que aguardam a sua validação no fluxo de dois passos:
                                                <ul style={{ margin: '8px 0 0', paddingLeft: 18, display: 'flex', flexDirection: 'column', gap: 4 }}>
                                                    <li><strong>Revisão Técnica</strong> — Validar conceção, cláusulas e conformidade do contrato.</li>
                                                    <li><strong>Aprovação Final</strong> — Autorizar a entrada em vigor do contrato na empresa.</li>
                                                </ul>
                                                <span style={{ display: 'block', marginTop: 8, color: '#64748b', fontStyle: 'italic' }}>Clique num cartão para abrir o painel de decisão.</span>
                                            </div>
                                        }
                                    >
                                        <Info size={16} style={{ color: '#7c3aed', cursor: 'help', flexShrink: 0 }} />
                                    </Tooltip>
                                    <span style={{ marginLeft: 'auto', backgroundColor: '#7c3aed', color: 'white', padding: '2px 10px', fontWeight: 800, fontSize: '0.75rem' }}>
                                        {contractApprovals.technicalApprovals.length + contractApprovals.finalApprovals.length}
                                    </span>
                                </div>

                                {contractApprovals.technicalApprovals.length > 0 && (
                                    <ContractQueueTable
                                        title="Revisão Técnica"
                                        stageColor="#0369a1"
                                        contracts={contractApprovals.technicalApprovals}
                                        selectedId={selectedContractId}
                                        onRowSelect={(id) => handleContractRowSelect(id, 'TECHNICAL')}
                                    />
                                )}

                                {contractApprovals.finalApprovals.length > 0 && (
                                    <ContractQueueTable
                                        title="Aprovação Final"
                                        stageColor="#7c3aed"
                                        contracts={contractApprovals.finalApprovals}
                                        selectedId={selectedContractId}
                                        onRowSelect={(id) => handleContractRowSelect(id, 'FINAL')}
                                    />
                                )}
                            </section>
                        </div>
                    )
                }
            </div>

            {/* No permissions message */}
            {!isAreaApprover && !isFinalApprover && (
                <div style={{ padding: '80px 20px', textAlign: 'center', color: 'var(--color-text-muted)', border: '4px dashed var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                    <AlertCircle size={64} strokeWidth={1.5} style={{ opacity: 0.2, margin: '0 auto 24px', color: 'var(--color-primary)' }} />
                    <p style={{ fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-main)' }}>Sem Permissões de Aprovação</p>
                    <p style={{ color: 'var(--color-text-muted)', fontWeight: 600 }}>Você não possui perfil de aprovador no sistema.</p>
                </div>
            )}

            {/* All-clear state */}
            {!selectedRequestId && totalPending === 0 && (isAreaApprover || isFinalApprover) && (
                <div style={{ padding: '80px 20px', textAlign: 'center', color: 'var(--color-text-muted)', border: '4px dashed var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                    <Inbox size={64} strokeWidth={1.5} style={{ opacity: 0.2, margin: '0 auto 24px', color: 'var(--color-primary)' }} />
                    <p style={{ fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-main)' }}>Tudo em dia!</p>
                    <p style={{ color: 'var(--color-text-muted)', fontWeight: 600 }}>Não há pedidos pendentes de aprovação no momento.</p>
                </div>
            )}

            {/* ============================== */}
            {/* SIDE PANEL (Drawer)            */}
            {/* ============================== */}
            <AnimatePresence>
                {isPanelOpen && (
                    <DropdownPortal>
                        {/* Overlay */}
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            exit={{ opacity: 0 }}
                            onClick={handleCloseDetail}
                            style={{
                                position: 'fixed',
                                inset: 0,
                                background: 'rgba(0, 0, 0, 0.4)',
                                backdropFilter: 'blur(4px)',
                                zIndex: 1000,
                                cursor: 'pointer'
                            }}
                        />

                        {/* Drawer */}
                        <motion.div
                            initial={{ x: '100%' }}
                            animate={{ x: 0 }}
                            exit={{ x: '100%' }}
                            transition={{ type: 'spring', damping: 25, stiffness: 200 }}
                            style={{
                                position: 'fixed',
                                top: 0,
                                right: 0,
                                bottom: 0,
                                width: '100%',
                                maxWidth: '100%',
                                // Use dynamic width only on desktop
                                ...(window.innerWidth > 768 ? {
                                    width: `${drawerWidth}px`,
                                    maxWidth: '90vw'
                                } : {}),
                                backgroundColor: 'white',
                                borderLeft: '6px solid var(--color-primary)',
                                boxShadow: '-8px 0 32px rgba(0,0,0,0.2)',
                                zIndex: Z_INDEX.DRAWER as any,
                                display: 'flex',
                                flexDirection: 'column'
                            }}
                        >
                            {/* Resize Handle (Desktop Only) */}
                            {window.innerWidth > 768 && (
                                <div
                                    onMouseDown={handleMouseDown}
                                    onMouseEnter={() => setIsHoveringHandle(true)}
                                    onMouseLeave={() => setIsHoveringHandle(false)}
                                    style={{
                                        position: 'absolute',
                                        left: '-3px',
                                        top: 0,
                                        bottom: 0,
                                        width: '12px',
                                        cursor: 'col-resize',
                                        zIndex: `calc(${Z_INDEX.DRAWER} + 1)` as any,
                                        display: 'flex',
                                        justifyContent: 'center',
                                        transition: 'all 0.2s ease'
                                    }}
                                >
                                    {/* Subtle visual indicator on hover or during resize */}
                                    <div style={{ 
                                        width: '2px', 
                                        height: '100%', 
                                        backgroundColor: isResizing || isHoveringHandle ? 'var(--color-primary)' : 'transparent',
                                        opacity: isResizing ? 1 : isHoveringHandle ? 0.5 : 0,
                                        transition: 'all 0.2s ease',
                                        boxShadow: isResizing ? '0 0 8px var(--color-primary)' : 'none'
                                    }} />
                                </div>
                            )}


                            {/* Drawer Content */}
                            <div style={{ flex: 1, overflowY: 'auto', position: 'relative' }}>
                                {/* Main Detail Panel with potential dimming */}
                                <div style={{ 
                                    flex: 1, 
                                    opacity: selectedDetailedItem ? 0.3 : 1, 
                                    filter: selectedDetailedItem ? 'blur(2px)' : 'none',
                                    transition: 'all 0.3s ease',
                                    pointerEvents: selectedDetailedItem ? 'none' : 'auto'
                                }}>
                                    {detailLoading ? (
                                        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', padding: '80px 24px', gap: '12px' }}>
                                            <div style={{ width: '32px', height: '32px', border: '3px solid var(--color-border)', borderTopColor: 'var(--color-primary)', borderRadius: '50%', animation: 'spin 0.8s linear infinite' }} />
                                            <span style={{ fontWeight: 700, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.1em' }}>A Carregar Detalhes...</span>
                                        </div>
                                    ) : detailData ? (
                                        <div style={{ padding: 0 }}>
                                            <ApprovalDetailPanel
                                                key={detailData.id}
                                                data={detailData}
                                                approvalStage={selectedApprovalStage || 'AREA'}
                                                isAreaApprover={isAreaApprover}
                                                isFinalApprover={isFinalApprover}
                                                onActionCompleted={handleActionCompleted}
                                                onClose={handleCloseDetail}
                                                onDataRefresh={refreshDetailData}
                                                isDrawerContext={true}
                                                onDrillDown={(item) => setSelectedDetailedItem(item)}
                                                onNext={handleNext}
                                                onPrev={handlePrev}
                                                currentIndex={currentIndex}
                                                totalCount={flatQueue.length}
                                            />
                                        </div>
                                    ) : null}
                                </div>

                                {/* Secondary Sliding Panel (Drill-down) */}
                                <AnimatePresence>
                                    {selectedDetailedItem && (
                                        <motion.div
                                            initial={{ x: '100%' }}
                                            animate={{ x: 0 }}
                                            exit={{ x: '100%' }}
                                            transition={{ type: 'spring', damping: 25, stiffness: 200 }}
                                            style={{
                                                position: 'absolute',
                                                top: 0,
                                                right: 0,
                                                bottom: 0,
                                                width: '100%',
                                                zIndex: 150,
                                                backgroundColor: 'white',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                boxShadow: '-10px 0 30px rgba(0,0,0,0.15)',
                                                borderLeft: '4px solid black'
                                            }}
                                        >
                                            <DetailedHistoryPanel 
                                                item={selectedDetailedItem}
                                                requestId={selectedRequestId!}
                                                onClose={() => setSelectedDetailedItem(null)}
                                            />
                                        </motion.div>
                                    )}
                                </AnimatePresence>
                            </div>
                        </motion.div>
                    </DropdownPortal>
                )}
            </AnimatePresence>

            {/* ============================== */}
            {/* CONTRACT PANEL (DEC-118)       */}
            {/* ============================== */}
            <AnimatePresence>
                {isContractPanelOpen && selectedContractId && selectedContractStage && (() => {
                    const allContracts = [
                        ...(contractApprovals?.technicalApprovals ?? []),
                        ...(contractApprovals?.finalApprovals ?? [])
                    ];
                    const contract = allContracts.find(c => c.id === selectedContractId);
                    if (!contract) return null;
                    return (
                        <DropdownPortal>
                            <motion.div
                                initial={{ opacity: 0 }}
                                animate={{ opacity: 1 }}
                                exit={{ opacity: 0 }}
                                onClick={handleCloseContractPanel}
                                style={{
                                    position: 'fixed', inset: 0,
                                    background: 'rgba(0, 0, 0, 0.4)',
                                    backdropFilter: 'blur(4px)',
                                    zIndex: 1000, cursor: 'pointer'
                                }}
                            />
                            <motion.div
                                initial={{ x: '100%' }}
                                animate={{ x: 0 }}
                                exit={{ x: '100%' }}
                                transition={{ type: 'spring', damping: 25, stiffness: 200 }}
                                style={{
                                    position: 'fixed', top: 0, right: 0, bottom: 0,
                                    width: '100%', maxWidth: '100%',
                                    ...(window.innerWidth > 768 ? { width: '560px', maxWidth: '90vw' } : {}),
                                    backgroundColor: 'white',
                                    borderLeft: `6px solid ${selectedContractStage === 'TECHNICAL' ? '#0369a1' : '#7c3aed'}`,
                                    boxShadow: '-8px 0 32px rgba(0,0,0,0.2)',
                                    zIndex: Z_INDEX.DRAWER as any,
                                    display: 'flex', flexDirection: 'column'
                                }}
                            >
                                <ContractApprovalPanel
                                    contract={contract}
                                    stage={selectedContractStage}
                                    onApprove={async (comment) => {
                                        if (selectedContractStage === 'TECHNICAL') {
                                            await contractTechnicalApprove(contract.id, comment);
                                        } else {
                                            await contractFinalApprove(contract.id, comment);
                                        }
                                        await handleContractActionCompleted(
                                            selectedContractStage === 'TECHNICAL'
                                                ? `Aprovação técnica do contrato ${contract.contractNumber} concluída.`
                                                : `Contrato ${contract.contractNumber} aprovado e ativado com sucesso.`
                                        );
                                    }}
                                    onReturn={async (comment) => {
                                        if (selectedContractStage === 'TECHNICAL') {
                                            await contractTechnicalReturn(contract.id, comment);
                                        } else {
                                            await contractFinalReturn(contract.id, comment);
                                        }
                                        await handleContractActionCompleted(
                                            `Contrato ${contract.contractNumber} devolvido ao rascunho.`
                                        );
                                    }}
                                    onClose={handleCloseContractPanel}
                                />
                            </motion.div>
                        </DropdownPortal>
                    );
                })()}
            </AnimatePresence>
        </PageContainer>
    );
}

// ====================================
// CONTRACT QUEUE CARDS (DEC-118)
// ====================================

interface ContractQueueTableProps {
    title: string;
    stageColor: string;
    contracts: ContractApprovalItem[];
    selectedId: string | null;
    onRowSelect: (id: string) => void;
}

function ContractQueueTable({ title, stageColor, contracts, selectedId, onRowSelect }: ContractQueueTableProps) {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {/* Sub-group label */}
            <div style={{
                fontSize: 10, fontWeight: 800, textTransform: 'uppercase',
                letterSpacing: '0.08em', color: stageColor,
                paddingLeft: 4, marginBottom: 2,
                display: 'flex', alignItems: 'center', gap: 6
            }}>
                <span style={{ width: 8, height: 8, borderRadius: '50%', background: stageColor, display: 'inline-block', flexShrink: 0 }} />
                {title}
            </div>

            {contracts.map((c, i) => {
                const isSelected = selectedId === c.id;
                const counterparty = c.supplierName ?? c.counterpartyName ?? '—';
                return (
                    <motion.div
                        key={c.id}
                        initial={{ opacity: 0, y: 8 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ delay: i * 0.04, duration: 0.2 }}
                        onClick={() => onRowSelect(c.id)}
                        style={{
                            display: 'grid',
                            gridTemplateColumns: '1fr 1fr auto',
                            gap: '12px',
                            alignItems: 'center',
                            padding: '14px 18px',
                            borderRadius: 'var(--radius-lg, 8px)',
                            backgroundColor: isSelected ? `${stageColor}10` : 'var(--color-bg-surface)',
                            border: `1px solid ${isSelected ? stageColor : 'var(--color-border)'}`,
                            borderLeft: `5px solid ${stageColor}`,
                            cursor: 'pointer',
                            boxShadow: isSelected ? `0 0 0 1px ${stageColor}40, var(--shadow-sm)` : 'var(--shadow-sm)',
                            transition: 'all 0.15s ease',
                            userSelect: 'none',
                        }}
                        whileHover={{ y: -1, boxShadow: `0 4px 12px ${stageColor}25, var(--shadow-sm)` } as any}
                    >
                        {/* Left: contract ref + title */}
                        <div style={{ minWidth: 0 }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                                <span style={{ fontWeight: 800, fontSize: '0.9rem', color: stageColor }}>{c.contractNumber}</span>
                                <span style={{
                                    fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase',
                                    padding: '1px 7px', borderRadius: 4,
                                    background: `${stageColor}18`, color: stageColor,
                                    letterSpacing: '0.04em', whiteSpace: 'nowrap'
                                }}>{title}</span>
                            </div>
                            <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: 2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 280 }}>
                                {c.title}
                            </div>
                        </div>

                        {/* Mid: counterparty + dept */}
                        <div style={{ minWidth: 0 }}>
                            <div style={{ fontWeight: 700, fontSize: '0.85rem', color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {counterparty}
                            </div>
                            <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                                {c.departmentName}{c.companyName ? ` · ${c.companyName}` : ''}
                            </div>
                        </div>

                        {/* Right: value */}
                        <div style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                            <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>{c.currencyCode || 'AOA'}</div>
                            <div style={{ fontWeight: 800, fontSize: '0.95rem', color: 'var(--color-text-main)' }}>
                                {c.totalContractValue != null ? formatCurrencyAO(c.totalContractValue, c.currencyCode) : '—'}
                            </div>
                        </div>
                    </motion.div>
                );
            })}
        </div>
    );
}

interface ApprovalQueueSectionProps {
    title: string;
    icon: React.ReactNode;
    tooltip?: string;
    requests: RequestListItemDto[];
    showCostCenter?: boolean;
    selectedId: string | null;
    flashedId?: string | null;
    onRowSelect: (id: string) => void;
}

function ApprovalQueueSection({ title, icon, tooltip, requests, showCostCenter, selectedId, flashedId, onRowSelect }: ApprovalQueueSectionProps) {
    if (requests.length === 0) {
        return (
            <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', borderLeft: '4px solid var(--color-border)', paddingLeft: '16px' }}>
                    <span style={{ color: 'var(--color-text-muted)' }}>{icon}</span>
                    <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '-0.02em' }}>{title}</h2>
                    {tooltip && (
                        <Tooltip variant="light" side="bottom" align="start" content={
                            <span style={{ fontSize: '0.82rem', lineHeight: 1.6 }}>{tooltip}</span>
                        }>
                            <Info size={15} style={{ color: 'var(--color-text-muted)', cursor: 'help', flexShrink: 0 }} />
                        </Tooltip>
                    )}
                </div>
                <div style={{ padding: '40px 20px', textAlign: 'center', color: 'var(--color-text-muted)', border: '2px dashed var(--color-border)', backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg, 8px)' }}>
                    <p style={{ fontWeight: 700, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Nenhum pedido pendente nesta fila.</p>
                </div>
            </section>
        );
    }

    return (
        <section style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            {/* Section header */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', borderLeft: '4px solid var(--color-primary)', paddingLeft: '16px' }}>
                <span style={{ color: 'var(--color-primary)' }}>{icon}</span>
                <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-main)', letterSpacing: '-0.02em' }}>{title}</h2>
                {tooltip && (
                    <Tooltip variant="light" side="bottom" align="start" content={
                        <span style={{ fontSize: '0.82rem', lineHeight: 1.6 }}>{tooltip}</span>
                    }>
                        <Info size={15} style={{ color: 'var(--color-primary)', cursor: 'help', flexShrink: 0, opacity: 0.7 }} />
                    </Tooltip>
                )}
                <span style={{ marginLeft: 'auto', backgroundColor: 'var(--color-primary)', color: 'white', padding: '2px 10px', fontWeight: 800, fontSize: '0.75rem' }}>
                    {requests.length}
                </span>
            </div>

            {/* Card list */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {requests.map((req, i) => {
                    const isSelected = selectedId === req.id;
                    const isFlashed = flashedId === req.id;
                    const hasQuotationAlert = req.requestTypeCode === 'QUOTATION' && !req.selectedQuotationId;

                    return (
                        <motion.div
                            key={req.id}
                            initial={{ opacity: 0, y: 8 }}
                            animate={{ opacity: 1, y: 0 }}
                            transition={{ delay: i * 0.04, duration: 0.2 }}
                            onClick={() => onRowSelect(req.id)}
                            className={isFlashed ? 'flash-red-row' : ''}
                            style={{
                                display: 'grid',
                                gridTemplateColumns: '200px 1fr auto',
                                gap: '16px',
                                alignItems: 'center',
                                padding: '14px 20px',
                                borderRadius: 'var(--radius-lg, 8px)',
                                backgroundColor: isSelected ? 'rgba(var(--color-primary-rgb), 0.04)' : 'var(--color-bg-surface)',
                                border: `1px solid ${isSelected ? 'var(--color-primary)' : 'var(--color-border)'}`,
                                borderLeft: `5px solid ${isSelected ? 'var(--color-primary)' : 'transparent'}`,
                                cursor: 'pointer',
                                boxShadow: isSelected ? '0 0 0 1px var(--color-primary), var(--shadow-sm)' : 'var(--shadow-sm)',
                                transition: 'all 0.15s ease',
                                userSelect: 'none',
                                position: 'relative',
                            }}
                            whileHover={{ y: -1, boxShadow: '0 4px 12px rgba(0,0,0,0.08)' } as any}
                        >
                            {/* Left: request number + date */}
                            <div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                    {isSelected && <ChevronRight size={14} style={{ color: 'var(--color-primary)', flexShrink: 0 }} />}
                                    <span style={{ fontWeight: 800, fontSize: '0.9rem', color: 'var(--color-primary)' }}>{req.requestNumber}</span>
                                    {hasQuotationAlert && (
                                        <Tooltip variant="dark" content={<span style={{ fontWeight: 700, fontSize: '0.75rem' }}>Requer atenção na análise</span>}>
                                            <AlertCircle size={13} strokeWidth={2.5} style={{ color: '#f43f5e', flexShrink: 0 }} />
                                        </Tooltip>
                                    )}
                                </div>
                                <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600, marginTop: 3 }}>{formatDate(req.createdAtUtc)}</div>
                                {/* Type badge */}
                                <span style={{ display: 'inline-block', marginTop: 6, fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', padding: '2px 8px', borderRadius: 4, border: '1px solid var(--color-border)', color: 'var(--color-text-muted)' }}>
                                    {req.requestTypeCode}
                                </span>
                            </div>

                            {/* Mid: requester + dept + cost center */}
                            <div style={{ minWidth: 0 }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 700, fontSize: '0.88rem', color: 'var(--color-text-main)' }}>
                                    <User size={13} style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} />
                                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{req.requesterName}</span>
                                </div>
                                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase', marginTop: 2 }}>
                                    {req.departmentName || '---'}
                                </div>
                                {showCostCenter && req.costCenterCode && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: 5, marginTop: 4 }}>
                                        <Landmark size={11} style={{ color: 'var(--color-primary)', flexShrink: 0 }} />
                                        <span style={{ fontWeight: 700, fontSize: '0.75rem', color: 'var(--color-text-main)' }}>{req.costCenterCode}</span>
                                    </div>
                                )}
                            </div>

                            {/* Right: value + status */}
                            <div style={{ textAlign: 'right', display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8 }}>
                                <div>
                                    <div style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>{req.currencyCode || 'AOA'}</div>
                                    <div style={{ fontWeight: 800, fontSize: '1rem', color: 'var(--color-text-main)', whiteSpace: 'nowrap' }}>
                                        {formatCurrencyAO(req.estimatedTotalAmount)}
                                    </div>
                                </div>
                                <StatusBadge code={req.statusCode} name={req.statusName} color={req.statusBadgeColor} />
                            </div>
                        </motion.div>
                    );
                })}
            </div>
        </section>
    );
}

export default ApprovalCenter;
