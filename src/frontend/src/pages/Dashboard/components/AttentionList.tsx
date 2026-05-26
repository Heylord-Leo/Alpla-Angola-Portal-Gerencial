import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../../../lib/api';
import { RequestListItemDto } from '../../../types';
import { getRequestGuidance, getUrgencyStyle, formatDate } from '../../../lib/utils';
import { ChevronRight, Clock, User } from 'lucide-react';

export function AttentionList() {
    const navigate = useNavigate();
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [isLoading, setIsLoading] = useState(true);

    useEffect(() => {
        const fetchAttentionRequests = async () => {
            try {
                setIsLoading(true);
                const data = await api.requests.list(undefined, { isAttention: true }, 1, 5);
                setRequests(Array.isArray(data) ? data : (data as any).items || []);
            } catch (err) {
                console.error('Error fetching attention requests:', err);
            } finally {
                setIsLoading(false);
            }
        };

        fetchAttentionRequests();
    }, []);

    if (isLoading) {
        return (
            <div style={{ padding: '2rem', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', border: '2px solid var(--color-border)' }}>
                <p style={{ fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', fontSize: '0.8rem', letterSpacing: '0.1em' }}>
                    Carregando pedidos críticos...
                </p>
            </div>
        );
    }

    if (!isLoading && requests.length === 0) {
        return null; // Don't show the section if there's nothing to show
    }

    return (
        <section style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <header>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                    <h2 style={{ 
                        margin: 0, 
                        fontSize: '1.25rem', 
                        fontWeight: 800, 
                        textTransform: 'uppercase',
                        borderLeft: '4px solid var(--color-status-red)',
                        paddingLeft: '1rem'
                    }}>
                        Pedidos em Atenção
                    </h2>
                    <span style={{ 
                        backgroundColor: 'var(--color-status-red)', 
                        color: 'white', 
                        fontSize: '0.7rem', 
                        fontWeight: 900, 
                        padding: '2px 8px',
                        borderRadius: '2px',
                        textTransform: 'uppercase'
                    }}>
                        {requests.length} Críticos
                    </span>
                </div>
                <p style={{ margin: '0.25rem 0 0 1.25rem', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                    Solicitações pendentes que requerem ação imediata devido ao prazo ou status
                </p>
            </header>

            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '2px solid var(--color-primary)', 
                boxShadow: 'var(--shadow-md)',
                overflow: 'hidden'
            }}>
                {requests.map((req) => {
                    const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);
                    const guidance = getRequestGuidance(req.statusCode, req.requestTypeCode);
                    
                    return (
                        <div 
                            key={req.id}
                            onClick={() => navigate(req.statusCode === 'DRAFT' ? `/requests/${req.id}/edit` : `/requests/${req.id}`)}
                            style={{
                                display: 'grid',
                                gridTemplateColumns: '120px 1fr 180px 180px 40px',
                                gap: '1rem',
                                padding: '1rem 1.5rem',
                                alignItems: 'center',
                                borderBottom: '2px solid var(--color-border)',
                                cursor: 'pointer',
                                transition: 'all 0.1s',
                                backgroundColor: urgency?.backgroundColor || 'inherit',
                            }}
                            className="attention-row"
                        >
                            {/* ID */}
                            <div style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '0.85rem' }}>
                                {req.requestNumber || 'PENDENTE'}
                            </div>

                            {/* Title & Info */}
                            <div style={{ minWidth: 0 }}>
                                <div style={{ fontWeight: 700, fontSize: '0.95rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                    {req.title}
                                </div>
                                <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', display: 'flex', gap: '0.5rem', marginTop: '2px' }}>
                                    <span>{req.companyName}</span>
                                    <span>•</span>
                                    <span>{req.departmentName}</span>
                                </div>
                            </div>

                            {/* Status */}
                            <div>
                                <span style={{ 
                                    backgroundColor: `var(--color-status-${req.statusBadgeColor || 'default'})`,
                                    color: 'white',
                                    fontSize: '0.65rem',
                                    fontWeight: 800,
                                    padding: '2px 6px',
                                    textTransform: 'uppercase'
                                }}>
                                    {req.statusName}
                                </span>
                            </div>

                            {/* Deadline & Responsible */}
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.75rem', fontWeight: 800 }}>
                                    <Clock size={12} />
                                    {formatDate(req.needByDateUtc)}
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                    <User size={12} />
                                    {guidance.responsible}
                                </div>
                            </div>

                            {/* Action Icon */}
                            <div style={{ display: 'flex', justifyContent: 'flex-end', color: 'var(--color-primary)' }}>
                                <ChevronRight size={20} />
                            </div>
                        </div>
                    );
                })}
                
                <Link 
                    to="/requests?isAttention=true"
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        padding: '0.75rem',
                        textDecoration: 'none',
                        fontSize: '0.75rem',
                        fontWeight: 800,
                        backgroundColor: 'var(--color-bg-page)',
                        color: 'var(--color-primary)',
                        textTransform: 'uppercase',
                        letterSpacing: '0.05em'
                    }}
                >
                    Ver Todos os Pedidos em Atenção
                </Link>
            </div>
            
            <style>{`
                .attention-row:hover {
                    background-color: var(--color-bg-page) !important;
                    transform: translateX(4px);
                }
                .attention-row:last-of-type {
                    border-bottom: none;
                }
            `}</style>
        </section>
    );
}
