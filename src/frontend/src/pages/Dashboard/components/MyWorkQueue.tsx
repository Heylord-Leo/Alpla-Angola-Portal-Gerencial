import { useNavigate } from 'react-router-dom';
import { CockpitSummaryDto } from '../../../types';
import { useAuth } from '../../../features/auth/AuthContext';
import { KPICard } from '../../../components/ui/KPICard';

interface MyWorkQueueProps {
    data: CockpitSummaryDto;
}

/**
 * "Minha Fila de Trabalho" — Role-contextual cards showing the logged-in user's pending actions.
 * 
 * Counter definitions:
 * - Aguardando minha ação: myTasksCriteria-based (role + status)
 * - Urgentes: subset where NeedByDateUtc is today or tomorrow
 * - Em reajuste: AREA_ADJUSTMENT or FINAL_ADJUSTMENT where user is responsible
 * - Atrasados: NeedByDateUtc < today and not terminal
 * - Próximos da data: NeedByDateUtc within next 3 days
 */
export function MyWorkQueue({ data }: MyWorkQueueProps) {
    const navigate = useNavigate();
    const { user } = useAuth();

    const cards = [
        {
            id: 'pending',
            title: 'Aguardando Minha Ação',
            value: data.myPendingActions,
            color: '#3b82f6',
            icon: (
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
                    <circle cx="9" cy="7" r="4" />
                    <path d="M22 21v-2a4 4 0 0 0-3-3.87" />
                    <path d="M16 3.13a4 4 0 0 1 0 7.75" />
                </svg>
            ),
            onClick: () => navigate('/requests?isAttention=true'),
            subtitle: 'Pedidos que dependem de si'
        },
        {
            id: 'urgent',
            title: 'Urgentes',
            value: data.myUrgentItems,
            color: '#f97316',
            icon: (
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="12" cy="12" r="10" />
                    <polyline points="12 6 12 12 16 14" />
                </svg>
            ),
            onClick: () => navigate('/requests?isAttention=true'),
            subtitle: 'Vencem hoje ou amanhã'
        },
        {
            id: 'adjustment',
            title: 'Em Reajuste',
            value: data.myAdjustmentItems,
            color: '#a855f7',
            icon: (
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                </svg>
            ),
            onClick: () => navigate('/requests?isAttention=true'),
            subtitle: 'Devolvidos para correção'
        },
        {
            id: 'overdue',
            title: 'Atrasados',
            value: data.myOverdueItems,
            color: '#ef4444',
            icon: (
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                    <line x1="12" y1="9" x2="12" y2="13" />
                    <line x1="12" y1="17" x2="12.01" y2="17" />
                </svg>
            ),
            onClick: () => navigate('/requests?isAttention=true'),
            subtitle: 'Passaram da data de necessidade'
        },
        {
            id: 'near-deadline',
            title: 'Próximos da Data',
            value: data.myNearDeadlineItems,
            color: '#eab308',
            icon: (
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
                    <line x1="16" y1="2" x2="16" y2="6" />
                    <line x1="8" y1="2" x2="8" y2="6" />
                    <line x1="3" y1="10" x2="21" y2="10" />
                </svg>
            ),
            onClick: () => navigate('/requests?isAttention=true'),
            subtitle: 'Vencem nos próximos 3 dias'
        }
    ];

    // Only show cards that have items OR are the main "pending" card
    const visibleCards = cards.filter(c => c.id === 'pending' || c.value > 0);

    return (
        <section>
            <div style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                marginBottom: '16px'
            }}>
                <h2 style={{
                    fontSize: '1.1rem',
                    fontWeight: 700,
                    color: 'var(--color-text)',
                    margin: 0
                }}>
                    Minha Fila de Trabalho
                </h2>
                {user && (
                    <span style={{
                        fontSize: '0.8rem',
                        color: 'var(--color-text-muted)',
                        fontWeight: 500
                    }}>
                        — {user.fullName}
                    </span>
                )}
            </div>
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                gap: '16px'
            }}>
                {visibleCards.map(card => (
                    <KPICard
                        key={card.id}
                        title={card.title}
                        value={card.value}
                        icon={card.icon}
                        color={card.color}
                        subtitle={card.subtitle}
                        onClick={card.onClick}
                    />
                ))}
            </div>
        </section>
    );
}
