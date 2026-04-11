import { ClipboardList, Clock, AlertCircle, DollarSign, Package } from 'lucide-react';
import { KPICard } from '../../../components/ui/KPICard';
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
            icon: <ClipboardList size={20} />,
            color: '#60a5fa' // blue-400
        },
        {
            id: 'quotation',
            label: 'Em Cotação',
            count: summary.waitingQuotation,
            icon: <Clock size={20} />,
            color: '#fbbf24' // amber-400
        },
        {
            id: 'approval',
            label: 'Aguardando Aprovação',
            count: summary.awaitingApproval,
            icon: <AlertCircle size={20} />,
            color: '#fb7185' // rose-400
        },
        {
            id: 'payment',
            label: 'Aguardando Pagamento',
            count: summary.awaitingPayment,
            icon: <DollarSign size={20} />,
            color: '#a78bfa' // violet-400
        },
        {
            id: 'receiving',
            label: 'Recebimentos Pendentes',
            count: summary.pendingReceiving,
            icon: <Package size={20} />,
            color: '#34d399' // emerald-400
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
                    title={card.label}
                    value={card.count}
                    icon={card.icon}
                    color={card.color}
                />
            ))}
        </div>
    );
}
