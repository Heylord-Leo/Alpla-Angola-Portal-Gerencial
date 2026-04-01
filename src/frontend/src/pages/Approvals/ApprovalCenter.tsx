import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../lib/api';
import { RequestListItemDto, RequestDetailsDto, PendingApprovalsResponseDto } from '../../types';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { ApprovalDetailPanel } from './ApprovalDetailPanel';
import { Loader2, CheckCircle, AlertCircle, Building2, User, Landmark, ShieldCheck, Inbox, X, ChevronRight } from 'lucide-react';
import { formatDate } from '../../lib/utils';
import { motion, AnimatePresence } from 'framer-motion';
import { DropdownPortal } from '../../components/ui/DropdownPortal';

// --- Types ---

type ApprovalStage = 'AREA' | 'FINAL';

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

    // Detail state
    const [detailLoading, setDetailLoading] = useState(false);
    const [detailData, setDetailData] = useState<RequestDetailsDto | null>(null);

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
            setData(newData);

            // Determine next selection
            const areaIds = new Set((newData.areaApprovals || []).map(r => r.id));
            const finalIds = new Set((newData.finalApprovals || []).map(r => r.id));

            // Check if the actioned request is still in any queue (status changed but still pending my action)
            if (selectedRequestId && (areaIds.has(selectedRequestId) || finalIds.has(selectedRequestId))) {
                const isNowArea = areaIds.has(selectedRequestId);
                setSelectedApprovalStage(isNowArea ? 'AREA' : 'FINAL');
                await loadDetailData(selectedRequestId);
            } else {
                // Request left the queue — find next item in the SAME queue first
                const sameQueue = selectedApprovalStage === 'AREA'
                    ? (newData.areaApprovals || [])
                    : (newData.finalApprovals || []);
                
                const otherQueue = selectedApprovalStage === 'AREA'
                    ? (newData.finalApprovals || [])
                    : (newData.areaApprovals || []);
                
                const otherStage: ApprovalStage = selectedApprovalStage === 'AREA' ? 'FINAL' : 'AREA';

                if (sameQueue.length > 0) {
                    setSelectedRequestId(sameQueue[0].id);
                    // Stage remains the same
                } else if (otherQueue.length > 0) {
                    setSelectedRequestId(otherQueue[0].id);
                    setSelectedApprovalStage(otherStage);
                } else {
                    // Both queues empty — close panel
                    handleCloseDetail();
                }
            }
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
            <div className="flex items-center justify-center min-h-[400px]">
                <Loader2 className="w-8 h-8 animate-spin text-blue-600" />
            </div>
        );
    }

    const totalPending = (data?.areaApprovals?.length || 0) + (data?.finalApprovals?.length || 0);

    return (
        <div className="p-6 max-w-7xl mx-auto space-y-8">
            {/* Page Feedback */}
            <Feedback
                type={feedback.type}
                message={feedback.message}
                onClose={() => setFeedback(prev => ({ ...prev, message: null }))}
            />

            {/* Page Header */}
            <header className="flex flex-col gap-2 border-b-4 border-black pb-4 mb-6">
                <div className="flex items-center gap-3">
                    <CheckCircle className="w-8 h-8 text-blue-600" />
                    <h1 className="text-4xl font-black uppercase tracking-tighter italic">Centro de Aprovações</h1>
                    {totalPending > 0 && (
                        <span style={{
                            backgroundColor: 'var(--color-primary)',
                            color: 'white',
                            padding: '4px 12px',
                            fontWeight: 900,
                            fontSize: '0.85rem',
                            borderRadius: '0',
                            boxShadow: '3px 3px 0 black'
                        }}>
                            {totalPending} PENDENTE{totalPending > 1 ? 'S' : ''}
                        </span>
                    )}
                </div>
                <p className="text-zinc-600 font-medium">Workspace centralizado para decisões e aprovações de Procurement.</p>
            </header>

            {/* DEV TOOLS (Only in Local Development) */}
            {import.meta.env.DEV && (
                <div style={{
                    display: 'flex', gap: '12px', padding: '16px',
                    backgroundColor: '#fffbeb', border: '2px solid #fcd34d',
                    marginBottom: '32px', boxShadow: '4px 4px 0px 0px rgba(0,0,0,0.05)'
                }}>
                    <div style={{
                        backgroundColor: '#f59e0b', color: 'white',
                        padding: '2px 8px', fontWeight: 900, fontSize: '0.65rem',
                        textTransform: 'uppercase', alignSelf: 'center'
                    }}>
                        Dev Tools
                    </div>
                    <button 
                        onClick={handleSeedData}
                        style={{
                            backgroundColor: 'black', color: 'white', border: 'none',
                            padding: '6px 12px', fontWeight: 800, fontSize: '0.75rem',
                            textTransform: 'uppercase', cursor: 'pointer'
                        }}
                    >
                        Semear Dados Inteligência
                    </button>
                    <button 
                        onClick={handleCleanupData}
                        style={{
                            backgroundColor: 'transparent', color: 'black', border: '2px solid black',
                            padding: '4px 10px', fontWeight: 800, fontSize: '0.75rem',
                            textTransform: 'uppercase', cursor: 'pointer'
                        }}
                    >
                        Limpar Dados Teste
                    </button>
                </div>
            )}

            {/* QUEUE SECTIONS (Master) */}
            <div className="space-y-12">
                {isAreaApprover && (
                    <ApprovalQueueSection
                        title="Aguardando minha aprovação de área"
                        icon={<Building2 className="w-6 h-6" />}
                        requests={data?.areaApprovals ?? []}
                        showCostCenter={true}
                        selectedId={selectedRequestId}
                        onRowSelect={(id) => handleRowSelect(id, 'AREA')}
                    />
                )}

                {isFinalApprover && (
                    <ApprovalQueueSection
                        title="Aguardando minha aprovação final"
                        icon={<ShieldCheck className="w-6 h-6" />}
                        requests={data?.finalApprovals ?? []}
                        selectedId={selectedRequestId}
                        onRowSelect={(id) => handleRowSelect(id, 'FINAL')}
                    />
                )}
            </div>

            {/* No permissions message */}
            {!isAreaApprover && !isFinalApprover && (
                <div className="p-12 text-center border-4 border-dashed border-zinc-200 rounded-lg">
                    <AlertCircle className="w-12 h-12 text-zinc-300 mx-auto mb-4" />
                    <h3 className="text-xl font-bold text-zinc-500">Sem Permissões de Aprovação</h3>
                    <p className="text-zinc-400">Você não possui permissões para acessar as filas de aprovação.</p>
                </div>
            )}

            {/* All-clear state */}
            {!selectedRequestId && totalPending === 0 && (isAreaApprover || isFinalApprover) && (
                <div style={{
                    padding: '48px',
                    textAlign: 'center',
                    border: '4px solid black',
                    backgroundColor: '#f0fdf4',
                    boxShadow: '8px 8px 0px 0px rgba(0,0,0,1)'
                }}>
                    <Inbox style={{ width: '48px', height: '48px', color: '#22c55e', margin: '0 auto 16px' }} />
                    <h3 style={{ fontWeight: 900, fontSize: '1.2rem', textTransform: 'uppercase', color: 'var(--color-text-main)', marginBottom: '8px' }}>
                        Tudo em dia!
                    </h3>
                    <p style={{ fontWeight: 600, color: 'var(--color-text-muted)' }}>
                        Não há pedidos pendentes de aprovação no momento.
                    </p>
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
                                zIndex: 1001,
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
                                        zIndex: 1002,
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

                            {/* Drawer Header */}
                            <div style={{
                                padding: '24px 32px',
                                backgroundColor: 'var(--color-bg-page)',
                                borderBottom: '4px solid black',
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center'
                            }}>
                                <div>
                                    <div className="flex items-center gap-2 text-[10px] font-black uppercase text-zinc-400 mb-1 tracking-widest">
                                        PEDIDO REVISÃO <ChevronRight size={10} /> {selectedApprovalStage === 'AREA' ? 'APROVAÇÃO ÁREA' : 'APROVAÇÃO FINAL'}
                                    </div>
                                    <h2 style={{ fontSize: '1.5rem', fontWeight: 900, textTransform: 'uppercase', margin: 0, color: 'var(--color-primary)', fontStyle: 'italic' }}>
                                        {detailData?.requestNumber || '...'}
                                    </h2>
                                </div>
                                <button 
                                    onClick={handleCloseDetail} 
                                    className="p-2 border-2 border-black hover:bg-black hover:text-white transition-colors"
                                >
                                    <X size={24} />
                                </button>
                            </div>

                            {/* Drawer Content */}
                            <div style={{ flex: 1, overflowY: 'auto' }}>
                                {detailLoading ? (
                                    <div className="flex flex-col items-center justify-center p-24 gap-4">
                                        <Loader2 className="w-8 h-8 animate-spin text-blue-600" />
                                        <span className="font-black text-xs uppercase text-zinc-400">Carregando detalhes...</span>
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
                                        />
                                    </div>
                                ) : null}
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
            <section className="space-y-4">
                <div className="flex items-center gap-3 border-l-8 border-zinc-300 pl-4">
                    <span className="p-2 bg-zinc-100 rounded text-zinc-500">{icon}</span>
                    <h2 className="text-2xl font-black uppercase italic tracking-tight text-zinc-400">{title}</h2>
                </div>
                <div className="p-8 text-center border-4 border-black bg-zinc-50 shadow-[8px_8px_0px_0px_rgba(0,0,0,1)]">
                    <p className="text-zinc-500 font-bold uppercase tracking-widest text-sm">Nenhum pedido pendente nesta fila.</p>
                </div>
            </section>
        );
    }

    return (
        <section className="space-y-4">
            <div className="flex items-center gap-3 border-l-8 border-blue-600 pl-4">
                <span className="p-2 bg-blue-50 rounded text-blue-600">{icon}</span>
                <h2 className="text-2xl font-black uppercase italic tracking-tight">{title}</h2>
                <span className="bg-black text-white px-3 py-0.5 font-bold text-sm rounded-full ml-auto">
                    {requests.length}
                </span>
            </div>

            <div className="overflow-hidden border-4 border-black shadow-[8px_8px_0px_0px_rgba(0,0,0,1)] bg-white">
                <table className="w-full text-left border-collapse">
                    <thead className="bg-black text-white uppercase text-xs font-black tracking-widest border-b-4 border-black">
                        <tr>
                            <th className="p-4 border-r-2 border-zinc-800">Pedido</th>
                            <th className="p-4 border-r-2 border-zinc-800">Solicitante / Depto</th>
                            {showCostCenter && <th className="p-4 border-r-2 border-zinc-800">Centro de Custo</th>}
                            <th className="p-4 border-r-2 border-zinc-800 text-center">Tipo</th>
                            <th className="p-4 border-r-2 border-zinc-800 text-right">Valor</th>
                            <th className="p-4">Status</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y-4 divide-black">
                        {requests.map((req) => {
                            const isSelected = selectedId === req.id;

                            return (
                                <tr
                                    key={req.id}
                                    onClick={() => onRowSelect(req.id)}
                                    className={`group cursor-pointer transition-all ${
                                        isSelected
                                            ? 'bg-blue-50'
                                            : 'hover:bg-yellow-50'
                                    }`}
                                    style={isSelected ? {
                                        borderLeft: '12px solid var(--color-primary)',
                                        backgroundColor: '#eff6ff'
                                    } : {}}
                                >
                                    <td className="p-4 border-r-2 border-black font-black italic text-lg leading-tight w-40 relative">
                                        {req.requestNumber}
                                        <div className="text-[10px] text-zinc-400 mt-1 not-italic font-medium">
                                            {formatDate(req.createdAtUtc)}
                                        </div>
                                        {isSelected && (
                                            <div className="absolute right-2 top-1/2 -translate-y-1/2">
                                                <ChevronRight className="text-blue-600 w-6 h-6" />
                                            </div>
                                        )}
                                    </td>
                                    <td className="p-4 border-r-2 border-black">
                                        <div className="font-bold flex items-center gap-1.5 leading-tight">
                                            <User className="w-3.5 h-3.5 shrink-0 text-zinc-400" />
                                            {req.requesterName}
                                        </div>
                                        <div className="text-xs text-zinc-500 font-medium uppercase mt-1">
                                            {req.departmentName || '---'}
                                        </div>
                                    </td>
                                    {showCostCenter && (
                                        <td className="p-4 border-r-2 border-black">
                                            {req.costCenterCode ? (
                                                <div className="flex items-center gap-1.5 bg-zinc-100 px-2 py-1 border-2 border-black w-fit">
                                                    <Landmark className="w-3.5 h-3.5 text-blue-600" />
                                                    <span className="font-black text-xs">{req.costCenterCode}</span>
                                                </div>
                                            ) : (
                                                <span className="text-zinc-300 italic text-xs">---</span>
                                            )}
                                        </td>
                                    )}
                                    <td className="p-4 border-r-2 border-black text-center">
                                        <div className="font-black text-[10px] border-2 border-black bg-zinc-50 inline-block px-2 py-0.5 uppercase">
                                            {req.requestTypeCode}
                                        </div>
                                    </td>
                                    <td className="p-4 border-r-2 border-black text-right whitespace-nowrap">
                                        <div className="text-xs font-black text-zinc-500 mb-0.5">{req.currencyCode || 'AKZ'}</div>
                                        <div className="text-xl font-black leading-none">
                                            {req.estimatedTotalAmount.toLocaleString('pt-PT', { minimumFractionDigits: 2 })}
                                        </div>
                                    </td>
                                    <td className="p-4">
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
