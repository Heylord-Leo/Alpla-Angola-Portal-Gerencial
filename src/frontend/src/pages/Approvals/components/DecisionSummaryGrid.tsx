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
            display: 'flex', flexDirection: 'column', gap: '4px', padding: '16px',
            backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-sm)',
            gridColumn: fullWidth ? '1 / -1' : 'auto'
        }}>
            <span style={{
                fontSize: '0.625rem', textTransform: 'uppercase', fontWeight: 700,
                color: 'var(--color-text-muted)', letterSpacing: '0.05em',
                display: 'flex', alignItems: 'center', gap: '6px'
            }}>
                {icon && <span style={{ opacity: 0.6 }}>{icon}</span>}
                {label}
            </span>
            <span style={{
                fontSize: '0.875rem', fontWeight: 700, color: 'var(--color-text-main)',
                wordBreak: 'break-word', lineHeight: 1.2
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
            display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px'
        }}>
            {items.map((item, id) => (
                <SummaryItem key={id} {...item} />
            ))}
        </div>
    );
}
