import React, { useEffect, useState, useCallback } from 'react';
import { Z_INDEX } from '../../constants/ui';
import { api } from '../../lib/api';
import { RequestListItemDto, RequestDetailsDto, PendingApprovalsResponseDto, ItemIntelligenceDto } from '../../types';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { ApprovalDetailPanel } from './ApprovalDetailPanel';
import { DetailedHistoryPanel } from './components/DetailedHistoryPanel';
import { AlertCircle, Building2, User, Landmark, ShieldCheck, Inbox, ChevronRight, History, Clock, TrendingUp } from 'lucide-react';
import { formatDate, formatCurrencyAO, getUrgencyStyle } from '../../lib/utils';
import { motion, AnimatePresence } from 'framer-motion';
import { DropdownPortal } from '../../components/ui/DropdownPortal';
import { QueueSummary } from './components/QueueSummary';
import { Tooltip } from '../../components/ui/Tooltip';

// --- Types ---

type ApprovalStage = 'AREA' | 'FINAL';
type SortMode = 'default' | 'oldest' | 'value_desc' | 'value_asc';

const HIGH_VALUE_THRESHOLD = 500000;

// --- Component ---

export function ApprovalCenter() {
    const { user, isAdmin } = useAuth();

    const isAreaApprover = isAdmin || (user?.roles?.includes(ROLES.AREA_APPROVER) ?? false);
    const isFinalApprover = isAdmin || (user?.roles?.includes(ROLES.FINAL_APPROVER) ?? false);

    // Queue state
    const [loading, setLoading] = useState(true);
    const [data, setData] = useState<PendingApprovalsResponseDto | null>(null);

    // Selection state
    const [selectedRequestId, setSelectedRequestId] = useState<string | null>(null);
    const [selectedApprovalStage, setSelectedApprovalStage] = useState<ApprovalStage | null>(null);
    const [isPanelOpen, setIsPanelOpen] = useState(false);
    const [selectedDetailedItem, setSelectedDetailedItem] = useState<ItemIntelligenceDto | null>(null);

    // Detail state
    const [detailLoading, setDetailLoading] = useState(false);
    const [detailData, setDetailData] = useState<RequestDetailsDto | null>(null);

    // --- Triage State (Phase 4) ---
    const [sortMode, setSortMode] = useState<SortMode>('default');
    const [activeFilters, setActiveFilters] = useState<string[]>([]);

    const toggleFilter = (filter: string) => {
        setActiveFilters(prev => 
            prev.includes(filter) ? prev.filter(f => f !== filter) : [...prev, filter]
        );
    };

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
        }
    };

    const handlePrev = () => {
        if (currentIndex > 0) {
            const prev = flatQueue[currentIndex - 1];
            const isArea = areaApprovals.some(r => r.id === prev.id);
            setSelectedRequestId(prev.id);
            setSelectedApprovalStage(isArea ? 'AREA' : 'FINAL');
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
            const response = await api.requests.getPendingApprovals();
            setData(response);
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
    };

    const handleCloseDetail = () => {
        setIsPanelOpen(false);
        setSelectedRequestId(null);
        setSelectedApprovalStage(null);
        setDetailData(null);
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
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', minWidth: 0 }}>
            {/* Feedback */}
            {feedback.message && (
                <div style={{ position: 'sticky', top: 'calc(var(--header-height) - 1rem)', zIndex: 10, backgroundColor: 'var(--color-bg-surface)', padding: '2rem 0 0 0', margin: '-2rem 0 0 0', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                </div>
            )}

            {/* Page Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', width: '100%', minWidth: 0 }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Centro de Aprovações</h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' }}>Workspace centralizado para decisões e aprovações de Procurement.</p>
                </div>
                {totalPending > 0 && (
                    <span style={{ backgroundColor: 'var(--color-primary)', color: 'white', padding: '6px 16px', fontWeight: 800, fontSize: '0.8rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                        {totalPending} PENDENTE{totalPending > 1 ? 'S' : ''}
                    </span>
                )}
            </div>

            {/* Queue Summary — KPI Cards (Phase 4) */}
            <QueueSummary areaApprovals={data?.areaApprovals || []} finalApprovals={data?.finalApprovals || []} />

            {/* Triage Controls — Filter Hub */}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                padding: '16px',
                boxShadow: 'var(--shadow-brutal)',
                border: '2px solid var(--color-primary)',
                display: 'flex',
                flexWrap: 'wrap',
                gap: '16px',
                alignItems: 'center'
            }}>
                {/* Sort controls */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <span style={{ fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>Ordenar:</span>
                    <div style={{ display: 'flex', gap: '8px' }}>
                        {[
                            { id: 'default', label: 'Urgência', icon: <Clock size={12} /> },
                            { id: 'oldest', label: 'Mais Antigo', icon: <History size={12} /> },
                            { id: 'value_desc', label: 'Maior Valor', icon: <TrendingUp size={12} /> }
                        ].map(btn => (
                            <button
                                key={btn.id}
                                onClick={() => setSortMode(btn.id as SortMode)}
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '6px',
                                    padding: '4px 12px',
                                    fontSize: '0.75rem',
                                    fontWeight: 700,
                                    textTransform: 'uppercase',
                                    border: `2px solid ${sortMode === btn.id ? 'var(--color-primary)' : 'var(--color-border)'}`,
                                    backgroundColor: sortMode === btn.id ? 'var(--color-primary)' : 'var(--color-bg-page)',
                                    color: sortMode === btn.id ? 'var(--color-bg-surface)' : 'var(--color-text-main)',
                                    cursor: 'pointer',
                                    transition: 'all 0.1s'
                                }}
                            >
                                {btn.icon} {btn.label}
                            </button>
                        ))}
                    </div>
                </div>

                <div style={{ width: '1px', height: '28px', backgroundColor: 'var(--color-border)' }} />

                {/* Filter chips */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
                    <span style={{ fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>Filtrar:</span>
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                        {[
                            { id: 'urgent', label: 'Urgentes' },
                            { id: 'high_value', label: 'Alto Valor' },
                            { id: 'has_alert', label: 'Com Alerta' },
                            { id: 'area_only', label: 'Apenas Área' },
                            { id: 'final_only', label: 'Apenas Final' }
                        ].map(filter => {
                            const active = activeFilters.includes(filter.id);
                            return (
                                <button
                                    key={filter.id}
                                    onClick={() => toggleFilter(filter.id)}
                                    style={{
                                        padding: '4px 12px',
                                        fontSize: '0.75rem',
                                        fontWeight: 700,
                                        textTransform: 'uppercase',
                                        border: `2px solid ${active ? 'var(--color-primary)' : 'var(--color-border)'}`,
                                        backgroundColor: active ? 'var(--color-primary)' : 'var(--color-bg-page)',
                                        color: active ? 'var(--color-bg-surface)' : 'var(--color-text-main)',
                                        cursor: 'pointer',
                                        transition: 'all 0.1s'
                                    }}
                                >
                                    {filter.label}
                                </button>
                            );
                        })}
                        {activeFilters.length > 0 && (
                            <button
                                onClick={() => setActiveFilters([])}
                                style={{
                                    background: 'none',
                                    border: 'none',
                                    color: 'var(--color-text-muted)',
                                    cursor: 'pointer',
                                    fontWeight: 700,
                                    textTransform: 'uppercase',
                                    fontSize: '0.75rem',
                                    whiteSpace: 'nowrap'
                                }}
                            >
                                Limpar Filtros
                            </button>
                        )}
                    </div>
                </div>
            </div>

            {/* DEV TOOLS — visually subordinate in dev only */}
            {import.meta.env.DEV && (
                <div style={{ display: 'flex', gap: '12px', alignItems: 'center', padding: '8px 12px', border: '1px dashed var(--color-border)', backgroundColor: 'var(--color-bg-page)' }}>
                    <span style={{ fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em', opacity: 0.6 }}>Dev</span>
                    <button onClick={handleSeedData} style={{ background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', fontWeight: 700, fontSize: '0.7rem', textTransform: 'uppercase', textDecoration: 'underline' }}>Semear Inteligência</button>
                    <button onClick={handleCleanupData} style={{ background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', fontWeight: 700, fontSize: '0.7rem', textTransform: 'uppercase', textDecoration: 'underline' }}>Limpar Dados</button>
                </div>
            )}

            {/* QUEUE SECTIONS (Master) */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '40px' }}>
                {isAreaApprover && (
                    <ApprovalQueueSection
                        title="Aguardando minha aprovação de área"
                        icon={<Building2 size={20} />}
                        requests={areaApprovals}
                        showCostCenter={true}
                        selectedId={selectedRequestId}
                        onRowSelect={(id) => handleRowSelect(id, 'AREA')}
                    />
                )}

                {isFinalApprover && (
                    <ApprovalQueueSection
                        title="Aguardando minha aprovação final"
                        icon={<ShieldCheck size={20} />}
                        requests={finalApprovals}
                        selectedId={selectedRequestId}
                        onRowSelect={(id) => handleRowSelect(id, 'FINAL')}
                    />
                )}
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
                                        zIndex: (Z_INDEX.DRAWER + 1) as any,
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
                                        <div className="p-0">
                                            <ApprovalDetailPanel
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
        </div>
    );
}

// ====================================
// QUEUE SECTION SUB-COMPONENT
// ====================================

interface ApprovalQueueSectionProps {
    title: string;
    icon: React.ReactNode;
    requests: RequestListItemDto[];
    showCostCenter?: boolean;
    selectedId: string | null;
    onRowSelect: (id: string) => void;
}

function ApprovalQueueSection({ title, icon, requests, showCostCenter, selectedId, onRowSelect }: ApprovalQueueSectionProps) {
    if (requests.length === 0) {
        return (
            <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', borderLeft: '4px solid var(--color-border)', paddingLeft: '16px' }}>
                    <span style={{ color: 'var(--color-text-muted)' }}>{icon}</span>
                    <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '-0.02em' }}>{title}</h2>
                </div>
                <div style={{ padding: '40px 20px', textAlign: 'center', color: 'var(--color-text-muted)', border: '2px dashed var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                    <p style={{ fontWeight: 700, fontSize: '0.85rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Nenhum pedido pendente nesta fila.</p>
                </div>
            </section>
        );
    }

    return (
        <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            {/* Section title aligned with Pedidos pattern */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', borderLeft: '4px solid var(--color-primary)', paddingLeft: '16px' }}>
                <span style={{ color: 'var(--color-primary)' }}>{icon}</span>
                <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-main)', letterSpacing: '-0.02em' }}>{title}</h2>
                <span style={{
                    marginLeft: 'auto',
                    backgroundColor: 'var(--color-primary)',
                    color: 'white',
                    padding: '2px 10px',
                    fontWeight: 800,
                    fontSize: '0.75rem'
                }}>
                    {requests.length}
                </span>
            </div>

            {/* Table — matches Pedidos border/shadow pattern */}
            <div style={{ overflowX: 'auto', width: '100%', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)' }}>
                <table style={{ border: 'none', boxShadow: 'none', minWidth: '900px' }}>
                    <thead>
                        <tr>
                            <th>Pedido</th>
                            <th>Solicitante / Depto</th>
                            {showCostCenter && <th>Centro de Custo</th>}
                            <th style={{ textAlign: 'center' }}>Tipo</th>
                            <th style={{ textAlign: 'right' }}>Valor</th>
                            <th>Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        {requests.map((req) => {
                            const isSelected = selectedId === req.id;

                            return (
                                <tr
                                    key={req.id}
                                    className="hoverable-row"
                                    onClick={() => onRowSelect(req.id)}
                                    style={{
                                        cursor: 'pointer',
                                        borderLeft: isSelected ? '6px solid var(--color-primary)' : 'none',
                                        backgroundColor: isSelected ? 'rgba(var(--color-primary-rgb), 0.03)' : undefined
                                    }}
                                >
                                    <td style={{ fontWeight: 800, color: 'var(--color-primary)' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                            {isSelected && <ChevronRight size={16} style={{ color: 'var(--color-primary)', flexShrink: 0 }} />}
                                            <div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                    {req.requestNumber}
                                                    {req.requestTypeCode === 'QUOTATION' && !req.selectedQuotationId && (
                                                        <Tooltip
                                                            variant="dark"
                                                            content={
                                                                <span style={{ fontWeight: 700, fontSize: '0.75rem' }}>Requer atenção na análise</span>
                                                            }
                                                        >
                                                            <AlertCircle size={14} strokeWidth={2.5} style={{ color: '#f43f5e', flexShrink: 0, cursor: 'default' }} />
                                                        </Tooltip>
                                                    )}
                                                </div>
                                                <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600, marginTop: '2px' }}>{formatDate(req.createdAtUtc)}</div>
                                            </div>
                                        </div>
                                    </td>
                                    <td>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                            <span style={{ fontWeight: 700, color: 'var(--color-text-main)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                <User size={13} style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} />
                                                {req.requesterName}
                                            </span>
                                            <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase' }}>{req.departmentName || '---'}</span>
                                        </div>
                                    </td>
                                    {showCostCenter && (
                                        <td>
                                            {req.costCenterCode ? (
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                    <Landmark size={13} style={{ color: 'var(--color-primary)', flexShrink: 0 }} />
                                                    <span style={{ fontWeight: 700, fontSize: '0.8rem', color: 'var(--color-text-main)' }}>{req.costCenterCode}</span>
                                                </div>
                                            ) : (
                                                <span style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem', fontStyle: 'italic' }}>---</span>
                                            )}
                                        </td>
                                    )}
                                    <td style={{ textAlign: 'center' }}>
                                        <span style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', border: '1px solid var(--color-border)', padding: '2px 8px', display: 'inline-block' }}>
                                            {req.requestTypeCode}
                                        </span>
                                    </td>
                                    <td style={{ textAlign: 'right', fontWeight: 800 }}>
                                        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '2px' }}>
                                            <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>{req.currencyCode || 'AOA'}</span>
                                            <span>{formatCurrencyAO(req.estimatedTotalAmount)}</span>
                                        </div>
                                    </td>
                                    <td>
                                        <StatusBadge code={req.statusCode} name={req.statusName} color={req.statusBadgeColor} />
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
        </section>
    );
}

export default ApprovalCenter;
