import { useEffect, useState, useCallback, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Package } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestListItemDto } from '../../types';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { StandardTable, TableEmptyState } from '../../components/ui/StandardTable';

export function ReceivingWorkspace() {
    const { user: currentUser } = useAuth();
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [searchInput, setSearchInput] = useState('');
    const [totalCount, setTotalCount] = useState(0);

    // Group toggle states
    const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({
        pending: true,
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
                'COMPLETED', 'FINALIZADO'
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
            
            const isSystemAdmin = currentUser?.roles.includes('System Administrator');
            
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
            pending: requests.filter(r => r.statusCode === 'PAYMENT_COMPLETED' || r.statusCode === 'WAITING_RECEIPT' || r.statusCode === 'PAG_REALIZADO' || r.statusCode === 'AG_RECIBO'),
            followup: requests.filter(r => r.statusCode === 'IN_FOLLOWUP'),
            received: requests.filter(r => r.statusCode === 'COMPLETED' || r.statusCode === 'FINALIZADO')
        };
    }, [requests]);

    // Handle auto-expand on search
    useEffect(() => {
        if (searchInput.trim()) {
            setExpandedSections({
                pending: groups.pending.length > 0,
                followup: groups.followup.length > 0,
                received: groups.received.length > 0
            });
        } else {
            // Restore default when search is cleared
            setExpandedSections({
                pending: true,
                followup: false,
                received: false
            });
        }
    }, [searchInput, groups.pending.length, groups.followup.length, groups.received.length]);

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
                title="Workspace de Recebimento"
                subtitle="Gestão operacional de entrada de materiais e conferência de pedidos."
                icon={<Package size={28} />}
            />

            {/* Sub-header info */}
            <div style={{ padding: '12px 20px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', border: '2px solid var(--color-primary)', color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '10px', boxShadow: '4px 4px 0px rgba(var(--color-primary-rgb), 0.1)', borderRadius: 'var(--radius-md)' }}>
                <span style={{ backgroundColor: 'var(--color-primary)', color: '#fff', padding: '2px 8px', borderRadius: '4px', fontSize: '0.7rem' }}>NOTA</span>
                Este workspace organiza os pedidos por estágio operacional após a conclusão do pagamento.
            </div>

            {/* Search */}
            <SearchFilterBar
                searchValue={searchInput}
                onSearchChange={setSearchInput}
                searchPlaceholder="BUSCAR NO RECEBIMENTO..."
            />

            {feedback.message && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback({ ...feedback, message: null })} />}

            {loading && requests.length === 0 ? (
                <div style={{ padding: '60px', textAlign: 'center', fontWeight: 700 }}>CARREGANDO...</div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <CollapsibleSection
                        title="Pedidos aguardando recebimento"
                        count={groups.pending.length}
                        isOpen={expandedSections.pending}
                        onToggle={() => toggleSection('pending')}
                    >
                        {renderTable(groups.pending)}
                    </CollapsibleSection>

                    <CollapsibleSection
                        title="Pedidos em acompanhamento de recebimento"
                        count={groups.followup.length}
                        isOpen={expandedSections.followup}
                        onToggle={() => toggleSection('followup')}
                    >
                        {renderTable(groups.followup)}
                    </CollapsibleSection>

                    <CollapsibleSection
                        title="Pedidos recebidos"
                        count={groups.received.length}
                        isOpen={expandedSections.received}
                        onToggle={() => toggleSection('received')}
                    >
                        {renderTable(groups.received)}
                    </CollapsibleSection>
                </div>
            )}
            
            {!loading && totalCount === 0 && (
                <TableEmptyState icon={<Package size={48} />} title="Nenhum pedido encontrado no recebimento." description="Ajuste os termos da sua busca." />
            )}
        </PageContainer>
    );
}
