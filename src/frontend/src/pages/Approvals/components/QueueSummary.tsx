import { RequestListItemDto } from '../../../types';
import { formatCurrencyAO, getUrgencyStyle } from '../../../lib/utils';
import { ClipboardList, Clock, AlertCircle, Wallet } from 'lucide-react';
import { KPICard } from '../../../components/common/dashboard/KPICard';
import { Tooltip } from '../../../components/ui/Tooltip';

interface QueueSummaryProps {
    areaApprovals: RequestListItemDto[];
    finalApprovals: RequestListItemDto[];
}

export function QueueSummary({ areaApprovals, finalApprovals }: QueueSummaryProps) {
    const allRequests = [...areaApprovals, ...finalApprovals];

    // 1. Total Pending
    const totalCount = allRequests.length;

    // 2. Total Value — sum of estimated amounts
    const totalValue = allRequests.reduce((sum, req) => sum + (req.estimatedTotalAmount || 0), 0);

    // 3. Urgent Count (Priority >= 2: due today or overdue)
    const urgentCount = allRequests.filter(req => {
        const style = getUrgencyStyle(req.needByDateUtc, req.statusCode);
        return style && style.priority >= 2;
    }).length;

    // 4. Alerts Count (QUOTATION type missing a selected winner)
    const alertCount = allRequests.filter(req =>
        req.requestTypeCode === 'QUOTATION' && !req.selectedQuotationId
    ).length;

    const cards: Array<{ id: string; label: string; count: number; icon: typeof ClipboardList; color: string; displayValue?: string; tooltip?: string }> = [
        {
            id: 'total',
            label: 'Total Pendente',
            count: totalCount,
            icon: ClipboardList,
            color: 'bg-blue-500',
        },
        {
            id: 'value',
            label: 'Valor Total em Fila',
            count: totalValue,
            icon: Wallet,
            color: 'bg-emerald-500',
            displayValue: formatCurrencyAO(totalValue, 'AOA'),
        },
        {
            id: 'urgent',
            label: 'Urgentes',
            count: urgentCount,
            icon: Clock,
            color: 'bg-amber-500',
        },
        {
            id: 'alerts',
            label: 'Com Alertas',
            count: alertCount,
            icon: AlertCircle,
            color: 'bg-rose-500',
            tooltip: 'Pedidos com pontos de atenção na análise.',
        },
    ];

    return (
        <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: '16px',
            marginBottom: '24px',
            width: '100%',
        }}>
            {cards.map((card, index) => {
                const kpiElement = (
                    <KPICard
                        key={card.id}
                        label={card.label}
                        count={card.count}
                        icon={card.icon}
                        isActive={false}
                        onClick={() => {}}
                        color={card.color}
                        delay={index * 0.08}
                        displayValue={card.displayValue}
                        nonInteractive={true}
                    />
                );

                if ('tooltip' in card && card.tooltip) {
                    return (
                        <Tooltip
                            key={card.id}
                            content={
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                    <div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Definição</div>
                                    <div style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-primary)' }}>{card.tooltip}</div>
                                </div>
                            }
                        >
                            {kpiElement}
                        </Tooltip>
                    );
                }

                return kpiElement;
            })}
        </div>
    );
}
