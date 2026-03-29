import { ClipboardList, Clock, CheckCircle2, AlertCircle, DollarSign } from 'lucide-react';
import { KPICard } from './KPICard';
import { DashboardSummaryDto } from '../../../types';

interface KPISummaryProps {
    summary: DashboardSummaryDto;
    activeFilter: string | null; // label of the active card
    onFilterChange: (filter: string | null) => void;
}

export function KPISummary({ summary, activeFilter, onFilterChange }: KPISummaryProps) {
    const cards = [
        {
            id: 'all',
            label: 'Total de Pedidos',
            count: summary.totalRequests,
            icon: ClipboardList,
            color: 'bg-blue-400',
            filter: 'Todos'
        },
        {
            id: 'quotation',
            label: 'Em Cotação',
            count: summary.waitingQuotation,
            icon: Clock,
            color: 'bg-amber-400',
            filter: 'Em Cotação'
        },
        {
            id: 'approval',
            label: 'Aguardando Aprovação',
            count: summary.awaitingApproval,
            icon: AlertCircle,
            color: 'bg-rose-400',
            filter: 'Em Aprovação'
        },
        {
            id: 'payment',
            label: 'Aguardando Pagamento',
            count: summary.awaitingPayment,
            icon: DollarSign,
            color: 'bg-violet-400',
            filter: 'Aguardando Pagamento'
        },
        {
            id: 'finished',
            label: 'Finalizados',
            count: summary.completedRequests,
            icon: CheckCircle2,
            color: 'bg-emerald-400',
            filter: 'Finalizados'
        }
    ];

    return (
        <div style={{ 
            display: 'grid', 
            gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', 
            gap: '16px', 
            marginBottom: '24px',
            width: '100%'
        }}>
            {cards.map((card, index) => (
                <KPICard
                    key={card.id}
                    label={card.label}
                    count={card.count}
                    icon={card.icon}
                    isActive={activeFilter === card.filter}
                    onClick={() => onFilterChange(card.filter)}
                    color={card.color}
                    delay={index * 0.1}
                />
            ))}
        </div>
    );
}
