import { useEffect, useState, useCallback, useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Package } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestListItemDto } from '../../types';
import type { ReceivingQueueRowDto, ReceivingBucket } from '../../types/dashboardV2';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { StandardTable, TableEmptyState } from '../../components/ui/StandardTable';
import { GuidedTourContextButton } from '../../features/guided-tour/GuidedTourContextButton';

// Canonical drill-down buckets accepted from the Dashboard V2 Receiving section. Kept in sync with
// ReceivingActionEvaluator.Buckets (backend) and receivingWorkspaceHref (dashboardV2View).
const CANONICAL_BUCKETS: ReceivingBucket[] = ['READY_FOR_RECEIPT', 'WAITING_RECEIPT', 'IN_FOLLOWUP', 'WAITING_SUPPLIER_DELIVERY'];

/**
 * Public workspace entry point. When opened with Dashboard V2 canonical params
 * (?queue=actionable or ?receivingBucket=<bucket>) it renders the exact group-level queue from
 * GET /api/v1/receiving/queue so counts reconcile with the dashboard. Without those params it
 * preserves the historical request-scalar workspace unchanged. The switch is by URL only; each
 * branch owns its own hooks (no conditional hooks in this wrapper).
 */
export function ReceivingWorkspace() {
    const [params] = useSearchParams();
    const queue = params.get('queue');
    const bucketParam = params.get('receivingBucket');
    const bucket = CANONICAL_BUCKETS.includes(bucketParam as ReceivingBucket) ? (bucketParam as ReceivingBucket) : null;
    const isCanonical = queue === 'actionable' || bucket !== null;

    if (isCanonical) {
        return <ReceivingCanonicalWorkspace bucket={bucket} />;
    }
    return <ReceivingScalarWorkspace />;
}

function ReceivingScalarWorkspace() {
    const { user: currentUser } = useAuth();
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [searchInput, setSearchInput] = useState('');
    const [totalCount, setTotalCount] = useState(0);

    // Group toggle states
    const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({
        delivery: true,
        pending: true,
        waiting_document: false,
        followup: false,
        received: false
    });

    useEffect(() => {
        // Enforce RECEIVING mode when entering this workspace
        localStorage.setItem('user_mode', 'RECEIVING');
    }, []);

    const loadData = useCallback(async () => {
        try {
            setLoading(true);
            
            // 1. Fetch available statuses to get IDs
            const statuses = await api.lookups.getRequestStatuses(false);
            const targetStatusCodes = [
                'PAYMENT_COMPLETED', 'PAG_REALIZADO', 
                'WAITING_RECEIPT', 'AG_RECIBO', 
                'IN_FOLLOWUP', 
                'COMPLETED', 'FINALIZADO',
                'WAITING_SUPPLIER_DELIVERY'  // B2P: advance payment flow
            ];
            const targetStatusIds = statuses
                .filter(s => targetStatusCodes.includes(s.code))
                .map(s => String(s.id))
                .join(',');

            // 2. Fetch available types to get IDs for QUOTATION and PAYMENT
            const types = await api.lookups.getRequestTypes();
            const targetTypeCodes = ['QUOTATION', 'PAYMENT'];
            const targetTypeIds = types
                .filter(t => targetTypeCodes.includes(t.code))
                .map(t => String(t.id))
                .join(',');

            if (!targetStatusIds || !targetTypeIds) {
                setRequests([]);
                setTotalCount(0);
                return;
            }

            // 3. Scope Filtering Logic
            let plantIdsString = '';
            let departmentIdsString = '';
            
            const isSystemAdmin = currentUser?.roles.includes(ROLES.SYSTEM_ADMINISTRATOR);
            
            if (!isSystemAdmin) {
                const [allPlants, allDepts] = await Promise.all([
                    api.lookups.getPlants(),
                    api.lookups.getDepartments()
                ]);
                
                const filteredPlantIds = allPlants
                    .filter((p: any) => currentUser?.plants?.includes(p.code))
                    .map((p: any) => String(p.id));
                
                const filteredDeptIds = allDepts
                    .filter((d: any) => currentUser?.departments?.includes(d.code))
                    .map((d: any) => String(d.id));
                
                plantIdsString = filteredPlantIds.join(',');
                departmentIdsString = filteredDeptIds.join(',');
            }

            // 4. Fetch requests using the IDs
            const data = await api.requests.list(
                searchInput, 
                { 
                    statusIds: targetStatusIds,
                    typeIds: targetTypeIds,
                    plantIds: plantIdsString || undefined,
                    departmentIds: departmentIdsString || undefined,
                    isAttention: false
                }, 
                1, 
                100 // Increased limit to ensure grouped visibility
            );

            // Correct mapping for PagedResult structure
            setRequests(data.pagedResult.items || []);
            setTotalCount(data.pagedResult.totalCount || 0);
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao carregar workspace' });
        } finally {
            setLoading(false);
        }
    }, [searchInput]);

    useEffect(() => {
        const handler = setTimeout(() => {
            loadData();
        }, 300);
        return () => clearTimeout(handler);
    }, [loadData]);

    const groups = useMemo(() => {
        return {
            delivery: requests.filter(r => r.statusCode === 'WAITING_SUPPLIER_DELIVERY'),
            pending: requests.filter(r => r.statusCode === 'PAYMENT_COMPLETED' || r.statusCode === 'PAG_REALIZADO'),
            waiting_document: requests.filter(r => r.statusCode === 'WAITING_RECEIPT' || r.statusCode === 'AG_RECIBO'),
            followup: requests.filter(r => r.statusCode === 'IN_FOLLOWUP'),
            received: requests.filter(r => r.statusCode === 'COMPLETED' || r.statusCode === 'FINALIZADO')
        };
    }, [requests]);

    // Handle auto-expand on search
    useEffect(() => {
        if (searchInput.trim()) {
            setExpandedSections({
                delivery: groups.delivery.length > 0,
                pending: groups.pending.length > 0,
                waiting_document: groups.waiting_document.length > 0,
                followup: groups.followup.length > 0,
                received: groups.received.length > 0
            });
        } else {
            // Restore default when search is cleared
            setExpandedSections({
                delivery: true,
                pending: true,
                waiting_document: false,
                followup: false,
                received: false
            });
        }
    }, [searchInput, groups.pending.length, groups.waiting_document.length, groups.followup.length, groups.received.length]);

    const toggleSection = (id: string) => {
        setExpandedSections(prev => ({ ...prev, [id]: !prev[id] }));
    };

    const renderTable = (data: RequestListItemDto[]) => {
        return (
            <StandardTable
                isEmpty={data.length === 0}
                emptyState={<TableEmptyState icon={<Package size={32} />} title="Nenhum pedido nesta operacionalização." />}
            >
                <thead>
                    <tr style={{ backgroundColor: '#FAFAFA', borderBottom: '1px solid var(--color-border)' }}>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'center', width: '100px' }}>Operação</th>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Número</th>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Tipo</th>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Título do Pedido</th>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Empresa</th>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Status</th>
                        <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'right' }}>Valor Estimado</th>
                    </tr>
                </thead>
                <tbody>
                    {data.map(req => (
                        <tr key={req.id}>
                            <td style={{ padding: '12px 20px', textAlign: 'center', borderBottom: '1px solid var(--color-border)' }}>
                                <Link 
                                    to={`/receiving/operation/${req.id}`} 
                                    className={(req.statusCode === 'COMPLETED' || req.statusCode === 'FINALIZADO') ? "btn-secondary" : "btn-primary"} 
                                    style={{ padding: '6px 12px', fontSize: '0.65rem', fontWeight: 800, letterSpacing: '0.05em', borderRadius: '6px' }}
                                >
                                    {(req.statusCode === 'COMPLETED' || req.statusCode === 'FINALIZADO') ? 'VISUALIZAR' : 'RECEBER'}
                                </Link>
                            </td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontWeight: 800, color: 'var(--color-primary)' }}>{req.requestNumber}</td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontWeight: 600 }}>{req.requestTypeName}</td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontSize: '0.85rem' }}>{req.title}</td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontSize: '0.85rem' }}>{req.companyName}</td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                                <span className={`badge ${
                                    req.statusBadgeColor === 'yellow' || req.statusBadgeColor === 'amber' ? 'badge-warning' :
                                    req.statusBadgeColor === 'green' || req.statusBadgeColor === 'emerald' ? 'badge-success' :
                                    req.statusBadgeColor === 'red' || req.statusBadgeColor === 'rose' || req.statusBadgeColor === 'rejected' ? 'badge-danger' :
                                    req.statusBadgeColor === 'blue' || req.statusBadgeColor === 'sky' || req.statusBadgeColor === 'indigo' ? 'badge-info' :
                                    'badge-neutral'
                                }`} style={{ fontSize: '0.6rem', padding: '2px 8px' }}>
                                    {req.statusName}
                                </span>
                            </td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', textAlign: 'right', fontWeight: 800, fontSize: '0.85rem' }}>
                                {req.currencyCode} {formatCurrencyAO(req.estimatedTotalAmount)}
                            </td>
                        </tr>
                    ))}
                </tbody>
            </StandardTable>
        );
    };

    return (
        <PageContainer>
            {/* Header */}
            <PageHeader
                data-tour="receiving-header"
                title="Workspace de Recebimento"
                subtitle="Gestão operacional de entrada de materiais e conferência de pedidos."
                icon={<Package size={28} />}
                actions={
                    <GuidedTourContextButton tourId="page-receiving-workspace" label="Tour da Tela" />
                }
            />

            {/* Sub-header info */}
            <div data-tour="receiving-info" style={{ padding: '12px 20px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', border: '2px solid var(--color-primary)', color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '10px', boxShadow: '4px 4px 0px rgba(var(--color-primary-rgb), 0.1)', borderRadius: 'var(--radius-md)' }}>
                <span style={{ backgroundColor: 'var(--color-primary)', color: '#fff', padding: '2px 8px', borderRadius: '4px', fontSize: '0.7rem' }}>NOTA</span>
                Este workspace organiza os pedidos por estágio operacional de recebimento de itens e serviços.
            </div>

            {/* Search */}
            <div data-tour="receiving-search">
            <SearchFilterBar
                searchValue={searchInput}
                onSearchChange={setSearchInput}
                searchPlaceholder="BUSCAR NO RECEBIMENTO..."
            />
            </div>

            {feedback.message && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback({ ...feedback, message: null })} />}

            {loading && requests.length === 0 ? (
                <div style={{ padding: '60px', textAlign: 'center', fontWeight: 700 }}>CARREGANDO...</div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {groups.delivery.length > 0 && (
                    <div data-tour="receiving-delivery">
                    <CollapsibleSection
                        title="Ag. Entrega/Serviço (Adiantamento)"
                        count={groups.delivery.length}
                        isOpen={expandedSections.delivery}
                        onToggle={() => toggleSection('delivery')}
                    >
                        {renderTable(groups.delivery)}
                    </CollapsibleSection>
                    </div>
                    )}

                    <div data-tour="receiving-pending">
                    <CollapsibleSection
                        title="Pedidos aguardando recebimento"
                        count={groups.pending.length}
                        isOpen={expandedSections.pending}
                        onToggle={() => toggleSection('pending')}
                    >
                        {renderTable(groups.pending)}
                    </CollapsibleSection>
                    </div>

                    <div data-tour="receiving-waiting-document">
                    <CollapsibleSection
                        title="Pedidos aguardando documento fiscal / finalização"
                        count={groups.waiting_document.length}
                        isOpen={expandedSections.waiting_document}
                        onToggle={() => toggleSection('waiting_document')}
                    >
                        {renderTable(groups.waiting_document)}
                    </CollapsibleSection>
                    </div>

                    <div data-tour="receiving-in-progress">
                    <CollapsibleSection
                        title="Pedidos em acompanhamento de recebimento"
                        count={groups.followup.length}
                        isOpen={expandedSections.followup}
                        onToggle={() => toggleSection('followup')}
                    >
                        {renderTable(groups.followup)}
                    </CollapsibleSection>
                    </div>

                    <div data-tour="receiving-completed">
                    <CollapsibleSection
                        title="Pedidos recebidos"
                        count={groups.received.length}
                        isOpen={expandedSections.received}
                        onToggle={() => toggleSection('received')}
                    >
                        {renderTable(groups.received)}
                    </CollapsibleSection>
                    </div>
                </div>
            )}
            
            {!loading && totalCount === 0 && (
                <TableEmptyState icon={<Package size={48} />} title="Nenhum pedido encontrado no recebimento." description="Ajuste os termos da sua busca." />
            )}
        </PageContainer>
    );
}

// ── Canonical group-level mode (Dashboard V2 drill-down) ──
// Renders exact RequestPoGroup rows from GET /api/v1/receiving/queue. Row identity is the group
// (requestPoGroupId), so two groups on the same request stay distinct — this is what lets the row
// count reconcile with the Dashboard summary. availableActions are echoed from the server evaluator;
// no status/action predicate is recomputed here. No aging and no monetary values are shown.

const BUCKET_LABELS: Record<ReceivingBucket, string> = {
    READY_FOR_RECEIPT: 'Entrada em recebimento',
    WAITING_RECEIPT: 'Aguardando recebimento',
    IN_FOLLOWUP: 'Acompanhamento parcial',
    WAITING_SUPPLIER_DELIVERY: 'Aguardando fornecedor',
};

const ACTION_LABELS: Record<string, string> = {
    MOVE_TO_RECEIPT: 'Mover p/ recebimento',
    CONFIRM_RECEIVING: 'Confirmar recebimento',
};

function ReceivingCanonicalWorkspace({ bucket }: { bucket: ReceivingBucket | null }) {
    const [rows, setRows] = useState<ReceivingQueueRowDto[]>([]);
    const [actionableRequests, setActionableRequests] = useState(0);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    useEffect(() => {
        localStorage.setItem('user_mode', 'RECEIVING');
    }, []);

    useEffect(() => {
        let alive = true;
        setLoading(true);
        api.receiving.getQueue({ actionableOnly: true, bucket: bucket ?? undefined })
            .then((res) => {
                if (!alive) return;
                setRows(res.rows || []);
                setActionableRequests(res.summary?.actionableRequests || 0);
            })
            .catch((err: any) => {
                if (!alive) return;
                setRows([]);
                setFeedback({ type: 'error', message: err?.message || 'Erro ao carregar a fila de recebimento' });
            })
            .finally(() => { if (alive) setLoading(false); });
        return () => { alive = false; };
    }, [bucket]);

    const subtitle = bucket
        ? `Grupos no estágio "${BUCKET_LABELS[bucket]}" com ação de Recebimento disponível.`
        : 'Todos os grupos com ação de Recebimento disponível no seu escopo.';

    return (
        <PageContainer>
            <PageHeader
                title="Fila de Recebimento"
                subtitle={subtitle}
                icon={<Package size={28} />}
                actions={<Link to="/receiving/workspace" className="btn-secondary" style={{ padding: '8px 14px', fontSize: '0.7rem', fontWeight: 800, letterSpacing: '0.05em', borderRadius: '6px' }}>VER WORKSPACE COMPLETO</Link>}
            />

            <div style={{ padding: '12px 20px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', border: '2px solid var(--color-primary)', color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '10px', borderRadius: 'var(--radius-md)' }}>
                <span style={{ backgroundColor: 'var(--color-primary)', color: '#fff', padding: '2px 8px', borderRadius: '4px', fontSize: '0.7rem' }}>FILA</span>
                {loading ? 'Carregando…' : `${rows.length} grupo${rows.length === 1 ? '' : 's'} · ${actionableRequests} pedido${actionableRequests === 1 ? '' : 's'}`}
            </div>

            {feedback.message && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback({ ...feedback, message: null })} />}

            {loading ? (
                <div style={{ padding: '60px', textAlign: 'center', fontWeight: 700 }}>CARREGANDO...</div>
            ) : (
                <StandardTable
                    isEmpty={rows.length === 0}
                    emptyState={<TableEmptyState icon={<Package size={48} />} title="Não há grupos pendentes de Recebimento nesta fila." />}
                >
                    <thead>
                        <tr style={{ backgroundColor: 'var(--color-bg-surface)', borderBottom: '1px solid var(--color-border)' }}>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'center', width: '120px' }}>Operação</th>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Número</th>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Título</th>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Fornecedor</th>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>P.O.</th>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Estágio</th>
                            <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Ações disponíveis</th>
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map((row) => (
                            <tr key={row.requestPoGroupId}>
                                <td style={{ padding: '12px 20px', textAlign: 'center', borderBottom: '1px solid var(--color-border)' }}>
                                    <Link to={`/receiving/operation/${row.requestId}`} className="btn-primary" style={{ padding: '6px 12px', fontSize: '0.65rem', fontWeight: 800, letterSpacing: '0.05em', borderRadius: '6px' }}>RECEBER</Link>
                                </td>
                                <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontWeight: 800, color: 'var(--color-primary)' }}>{row.requestNumber}</td>
                                <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontSize: '0.85rem' }}>{row.title || '—'}</td>
                                <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontSize: '0.85rem' }}>{row.supplierName || '—'}</td>
                                <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontSize: '0.85rem' }}>{row.purchaseOrderNumber || '—'}</td>
                                <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                                    <span className="badge badge-info" style={{ fontSize: '0.6rem', padding: '2px 8px' }}>
                                        {BUCKET_LABELS[row.actionableBucket as ReceivingBucket] || row.actionableBucket}
                                    </span>
                                </td>
                                <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                                    {(row.availableActions || []).map((a) => ACTION_LABELS[a] || a).join(' · ') || '—'}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </StandardTable>
            )}
        </PageContainer>
    );
}
