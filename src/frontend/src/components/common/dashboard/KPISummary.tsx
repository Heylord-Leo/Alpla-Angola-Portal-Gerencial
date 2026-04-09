import { ClipboardList, Clock, AlertCircle, DollarSign, TrendingUp } from 'lucide-react';
import { KPICard } from './KPICard';
import { DashboardSummaryDto } from '../../../types';
import { formatCurrencyAO } from '../../../lib/utils';

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
            id: 'po',
            label: 'Aguardando P.O',
            count: summary.awaitingPo,
            icon: ClipboardList,
            color: 'bg-orange-400',
            filter: 'Aguardando P.O'
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
            id: 'filtered-total',
            label: 'Total Filtrado',
            count: summary.filteredTotal,
            icon: TrendingUp,
            color: 'bg-emerald-500',
            filter: 'FilteredTotal',
            displayValue: summary.filteredCurrencyCodes.length > 1 
                ? 'Múltiplas moedas' 
                : summary.filteredCurrencyCodes.length === 1
                    ? formatCurrencyAO(summary.filteredTotal, summary.filteredCurrencyCodes[0])
                    : '0,00',
            subtitle: summary.filteredCurrencyCodes.length > 1 
                ? 'Refine os filtros para ver o total por moeda' 
                : undefined,
            nonInteractive: true,
            trendValue: summary.filteredTotalTrend,
            trendLabel: summary.filteredTotalTrendLabel
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
                    displayValue={card.displayValue}
                    subtitle={card.subtitle}
                    nonInteractive={card.nonInteractive}
                    trendValue={(card as any).trendValue}
                    trendLabel={(card as any).trendLabel}
                />
            ))}
        </div>
    );
}
