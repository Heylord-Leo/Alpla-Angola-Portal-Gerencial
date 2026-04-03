import { useEffect, useState, useCallback, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Search, Package, X } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestListItemDto } from '../../types';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';

export function ReceivingWorkspace() {
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

            // 3. Fetch requests using the IDs
            const data = await api.requests.list(
                searchInput, 
                { 
                    statusIds: targetStatusIds,
                    typeIds: targetTypeIds,
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
        if (data.length === 0) {
            return (
                <div style={{ 
                    padding: '80px 20px', 
                    textAlign: 'center', 
                    color: 'var(--color-text-muted)', 
                    backgroundColor: '#fafafa',
                    border: '1px dashed var(--color-border)',
                    borderRadius: 'var(--radius-lg)',
                    margin: '20px'
                }}>
                    <Package size={48} style={{ opacity: 0.2, margin: '0 auto 16px', color: 'var(--color-primary)' }} />
                    <p style={{ fontWeight: 900, fontSize: '0.8rem', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Nenhum pedido nesta operacionalização.</p>
                </div>
            );
        }

        return (
            <table style={{ minWidth: '1000px', margin: 0, borderCollapse: 'collapse' }}>
                <thead>
                    <tr>
                        <th style={{ textAlign: 'center', width: '100px' }}>Operação</th>
                        <th>Número</th>
                        <th>Tipo</th>
                        <th>Título do Pedido</th>
                        <th>Empresa</th>
                        <th>Status</th>
                        <th style={{ textAlign: 'right' }}>Valor Estimado</th>
                    </tr>
                </thead>
                <tbody>
                    {data.map(req => (
                        <tr key={req.id}>
                            <td style={{ textAlign: 'center' }}>
                                <Link 
                                    to={`/receiving/operation/${req.id}`} 
                                    className={(req.statusCode === 'COMPLETED' || req.statusCode === 'FINALIZADO') ? "btn-secondary" : "btn-primary"} 
                                    style={{ padding: '8px 16px', fontSize: '0.65rem', fontWeight: 900, letterSpacing: '0.05em' }}
                                >
                                    {(req.statusCode === 'COMPLETED' || req.statusCode === 'FINALIZADO') ? 'VISUALIZAR' : 'RECEBER'}
                                </Link>
                            </td>
                            <td style={{ fontWeight: 900, color: 'var(--color-primary)', letterSpacing: '0.02em' }}>{req.requestNumber}</td>
                            <td style={{ fontWeight: 600 }}>{req.requestTypeName}</td>
                            <td>{req.title}</td>
                            <td>{req.companyName}</td>
                            <td>
                                <span className={`badge ${
                                    req.statusBadgeColor === 'yellow' || req.statusBadgeColor === 'amber' ? 'badge-warning' :
                                    req.statusBadgeColor === 'green' || req.statusBadgeColor === 'emerald' ? 'badge-success' :
                                    req.statusBadgeColor === 'red' || req.statusBadgeColor === 'rose' || req.statusBadgeColor === 'rejected' ? 'badge-danger' :
                                    req.statusBadgeColor === 'blue' || req.statusBadgeColor === 'sky' || req.statusBadgeColor === 'indigo' ? 'badge-info' :
                                    'badge-neutral'
                                }`}>
                                    {req.statusName}
                                </span>
                            </td>
                            <td style={{ textAlign: 'right', fontWeight: 800 }}>
                                {req.currencyCode} {formatCurrencyAO(req.estimatedTotalAmount)}
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>
        );
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%' }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '1px solid var(--color-border)', paddingBottom: '24px' }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)', fontWeight: 900, display: 'flex', alignItems: 'center', gap: '16px' }}>
                        <Package size={40} />
                        Workspace de Recebimento
                    </h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 800, letterSpacing: '0.1em', textTransform: 'uppercase', fontSize: '0.8rem', opacity: 0.8 }}>
                        Gestão operacional de entrada de materiais e conferência de pedidos.
                    </p>
                </div>
            </div>

            {/* Sub-header info */}
            <div style={{ padding: '12px 20px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', border: '2px solid var(--color-primary)', color: 'var(--color-primary)', fontWeight: 700, fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '10px', boxShadow: '4px 4px 0px rgba(var(--color-primary-rgb), 0.1)' }}>
                <span style={{ backgroundColor: 'var(--color-primary)', color: '#fff', padding: '2px 8px', borderRadius: '4px', fontSize: '0.7rem' }}>NOTA</span>
                Este workspace organiza os pedidos por estágio operacional após a conclusão do pagamento.
            </div>

            {/* Search */}
            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                padding: '24px', 
                borderRadius: 'var(--radius-lg)', 
                border: '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-sm)'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', backgroundColor: '#fcfcfc', padding: '0 16px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)' }}>
                    <Search size={20} color="var(--color-primary)" strokeWidth={2.5} style={{ opacity: 0.6 }} />
                    <input
                        type="text"
                        placeholder="BUSCAR NO RECEBIMENTO..."
                        value={searchInput}
                        onChange={(e) => setSearchInput(e.target.value)}
                        style={{ border: 'none', outline: 'none', width: '100%', fontSize: '0.85rem', padding: '12px 0', backgroundColor: 'transparent', fontWeight: 600, color: 'var(--color-primary)', textTransform: 'uppercase' }}
                    />
                    {searchInput && (
                        <button onClick={() => setSearchInput('')} style={{ background: 'none', border: 'none', cursor: 'pointer' }}>
                            <X size={18} />
                        </button>
                    )}
                </div>
            </div>

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
                <div style={{ padding: '80px 20px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                    <Package size={64} style={{ opacity: 0.1, margin: '0 auto 20px' }} />
                    <p style={{ fontWeight: 700 }}>Nenhum pedido encontrado no recebimento.</p>
                </div>
            )}
        </div>
    );
}
