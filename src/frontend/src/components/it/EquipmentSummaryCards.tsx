import { Package, Monitor, CheckCircle, Wrench, AlertTriangle, Archive, BookmarkCheck, HelpCircle } from 'lucide-react';
import { KPICard } from '../ui/KPICard';
import type { ITEquipmentSummary } from '../../types/itEquipment';

interface Props {
    summary: ITEquipmentSummary;
    activeFilter: string;
    onFilterClick: (status: string) => void;
}

export function EquipmentSummaryCards({ summary, activeFilter, onFilterClick }: Props) {
    const cards = [
        { key: '', title: 'Total Equipamentos', value: summary.total, icon: <Package size={20} />, color: '#6366f1' },
        { key: 'IN_USE', title: 'Em Uso', value: summary.inUse, icon: <Monitor size={20} />, color: '#3b82f6' },
        { key: 'AVAILABLE', title: 'Disponíveis', value: summary.available, icon: <CheckCircle size={20} />, color: '#10b981' },
        { key: 'IN_REPAIR', title: 'Em Conserto', value: summary.inRepair, icon: <Wrench size={20} />, color: '#f97316' },
        { key: 'LOST', title: 'Perdidos', value: summary.lost, icon: <AlertTriangle size={20} />, color: '#ef4444' },
        { key: 'RETIRED', title: 'Baixados', value: summary.retired, icon: <Archive size={20} />, color: '#6b7280' },
        { key: 'RESERVED', title: 'Reservados', value: summary.reserved, icon: <BookmarkCheck size={20} />, color: '#f59e0b' },
        { key: 'UNKNOWN', title: 'Desconhecido', value: summary.unknown, icon: <HelpCircle size={20} />, color: '#9ca3af' },
    ];

    return (
        <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(165px, 1fr))',
            gap: 12,
            marginBottom: 20
        }}>
            {cards.map(card => (
                <KPICard
                    key={card.key || 'total'}
                    title={card.title}
                    value={card.value}
                    icon={card.icon}
                    color={card.color}
                    onClick={card.key ? () => onFilterClick(card.key) : undefined}
                    borderColor={activeFilter === card.key && card.key ? `${card.color}60` : undefined}
                    bgColor={activeFilter === card.key && card.key ? `${card.color}08` : undefined}
                    style={{ minHeight: 100 }}
                />
            ))}
        </div>
    );
}
