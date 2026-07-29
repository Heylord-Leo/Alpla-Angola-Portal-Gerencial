import { ApprovalQueueItemDto } from '../../../types';
import { formatCurrencyAO, getUrgencyStyle } from '../../../lib/utils';
import { ClipboardList, Clock, AlertCircle, Wallet, FileText } from 'lucide-react';
import { KPICard } from '../../../components/common/dashboard/KPICard';
import { Tooltip } from '../../../components/ui/Tooltip';

interface QueueSummaryProps {
    areaApprovals: ApprovalQueueItemDto[];
    finalApprovals: ApprovalQueueItemDto[];
    pendingContracts?: number;
}

export function QueueSummary({ areaApprovals, finalApprovals, pendingContracts }: QueueSummaryProps) {
    const allActions = [...areaApprovals, ...finalApprovals];

    // The queue unit is the actionable approval (batch+stage). The primary count is ACTIONS/cards,
    // not distinct requests — with a secondary "X ações em Y pedidos" so request context is not lost.
    const totalCount = allActions.length;
    const distinctRequests = new Set(allActions.map(a => a.requestId)).size;
    const actionsSubtitle = totalCount === distinctRequests
        ? `${totalCount} ${totalCount === 1 ? 'ação' : 'ações'} em ${distinctRequests} ${distinctRequests === 1 ? 'pedido' : 'pedidos'}`
        : `${totalCount} ações em ${distinctRequests} ${distinctRequests === 1 ? 'pedido' : 'pedidos'}`;

    // Authoritative queue total — sum of each actionable card's amount exactly once, excluding
    // unresolved (null) ones, mirroring ApprovalQueueAmountResolver.SumActionable. Same rule as each
    // card, so cards and this KPI can never diverge.
    const totalValue = allActions.reduce((sum, req) => sum + (req.actionableAmount ?? 0), 0);
    const urgentCount = allActions.filter(req => {
        const style = getUrgencyStyle(req.needByDateUtc, req.batchStatus);
        return style && style.priority >= 2;
    }).length;
    const alertCount = allActions.filter(req =>
        req.requestTypeCode === 'QUOTATION' && !req.selectedQuotationId
    ).length;

    const cards: Array<{ id: string; label: string; count: number; icon: typeof ClipboardList; color: string; displayValue?: string; subtitle?: string; tooltip?: string }> = [
        { id: 'total', label: 'Ações Pendentes', count: totalCount, icon: ClipboardList, color: 'bg-blue-500', subtitle: actionsSubtitle, tooltip: 'Cada lote/ação de aprovação conta como uma entrada. Um pedido com dois lotes gera duas ações.' },
        { id: 'value', label: 'Valor Total em Fila', count: totalValue, icon: Wallet, color: 'bg-emerald-500', displayValue: formatCurrencyAO(totalValue, 'AOA') },
        { id: 'urgent', label: 'Urgentes', count: urgentCount, icon: Clock, color: 'bg-amber-500' },
        { id: 'alerts', label: 'Com Alertas', count: alertCount, icon: AlertCircle, color: 'bg-rose-500', tooltip: 'Ações com pontos de atenção na análise.' },
        ...(pendingContracts != null && pendingContracts > 0 ? [{
            id: 'contracts',
            label: 'Contratos Pendentes',
            count: pendingContracts,
            icon: FileText as typeof ClipboardList,
            color: 'bg-violet-500',
            tooltip: 'Contratos aguardando revisão técnica ou aprovação final.',
        }] : []),
    ];

    return (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '16px', marginBottom: '24px', width: '100%' }}>
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
                        subtitle={card.subtitle}
                        nonInteractive={true}
                    />
                );
                if ('tooltip' in card && card.tooltip) {
                    return (
                        <Tooltip key={card.id} content={
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                <div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Definição</div>
                                <div style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-primary)' }}>{card.tooltip}</div>
                            </div>
                        }>
                            {kpiElement}
                        </Tooltip>
                    );
                }
                return kpiElement;
            })}
        </div>
    );
}
