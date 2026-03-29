import { ClipboardList, Clock, AlertCircle, DollarSign, Package } from 'lucide-react';
import { KPICard } from '../../../components/common/dashboard/KPICard';
import { PurchasingSummaryDto } from '../../../types';

interface PurchasingKPISummaryProps {
    summary: PurchasingSummaryDto;
}

export function PurchasingKPISummary({ summary }: PurchasingKPISummaryProps) {
    const cards = [
        {
            id: 'total',
            label: 'Total Pedidos Abertos',
            count: summary.totalActiveRequests,
            icon: ClipboardList,
            color: 'bg-blue-400'
        },
        {
            id: 'quotation',
            label: 'Em Cotação',
            count: summary.waitingQuotation,
            icon: Clock,
            color: 'bg-amber-400'
        },
        {
            id: 'approval',
            label: 'Aguardando Aprovação',
            count: summary.awaitingApproval,
            icon: AlertCircle,
            color: 'bg-rose-400'
        },
        {
            id: 'payment',
            label: 'Aguardando Pagamento',
            count: summary.awaitingPayment,
            icon: DollarSign,
            color: 'bg-violet-400'
        },
        {
            id: 'receiving',
            label: 'Recebimentos Pendentes',
            count: summary.pendingReceiving,
            icon: Package,
            color: 'bg-emerald-400'
        }
    ];

    return (
        <div style={{ 
            display: 'grid', 
            gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', 
            gap: '16px', 
            width: '100%'
        }}>
            {cards.map((card, index) => (
                <KPICard
                    key={card.id}
                    label={card.label}
                    count={card.count}
                    icon={card.icon}
                    isActive={false} // No filtering on landing page cards for now
                    onClick={() => {}} 
                    color={card.color}
                    delay={index * 0.1}
                />
            ))}
        </div>
    );
}
