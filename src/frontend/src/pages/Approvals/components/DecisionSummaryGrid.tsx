import { ReactNode } from 'react';

interface SummaryItemProps {
    label: string;
    value: ReactNode;
    icon?: ReactNode;
    fullWidth?: boolean;
}

function SummaryItem({ label, value, icon, fullWidth = false }: SummaryItemProps) {
    return (
        <div style={{
            display: 'flex',
            flexDirection: 'column',
            gap: '4px',
            gridColumn: fullWidth ? '1 / -1' : 'span 1',
            padding: '12px',
            backgroundColor: '#f8fafc',
            border: '1px solid var(--color-border)',
            borderRadius: '0'
        }}>
            <span style={{
                fontSize: '10px',
                fontWeight: 800,
                color: 'var(--color-text-muted)',
                textTransform: 'uppercase',
                letterSpacing: '0.05em',
                display: 'flex',
                alignItems: 'center',
                gap: '6px'
            }}>
                {icon && <span style={{ opacity: 0.7 }}>{icon}</span>}
                {label}
            </span>
            <span style={{
                fontSize: '0.85rem',
                fontWeight: 700,
                color: 'var(--color-text-main)',
                lineHeight: 1.4,
                wordBreak: 'break-word'
            }}>
                {value || '---'}
            </span>
        </div>
    );
}

interface DecisionSummaryGridProps {
    items: SummaryItemProps[];
}

export function DecisionSummaryGrid({ items }: DecisionSummaryGridProps) {
    return (
        <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(140px, 1fr))',
            gap: '12px'
        }}>
            {items.map((item, id) => (
                <SummaryItem key={id} {...item} />
            ))}
        </div>
    );
}
