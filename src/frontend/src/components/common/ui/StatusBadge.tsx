

export type StatusVariant = 
    | 'green'   // Available, Active, Warranty active
    | 'blue'    // Assigned, In use
    | 'purple'  // Reserved
    | 'orange'  // Purchase document pending, warning
    | 'yellow'  // Repair, Pending
    | 'gray'    // Retired, Expired, Inactive, Default unknown
    | 'red';    // Lost, Blocked, Error, Destructive

export interface StatusBadgeProps {
    status: string;
    label?: string;
    variant?: StatusVariant;
}

const colorMap: Record<StatusVariant, { bg: string; text: string; border: string }> = {
    green: { bg: '#ecfdf5', text: '#059669', border: '#a7f3d0' },
    blue: { bg: '#eff6ff', text: '#2563eb', border: '#bfdbfe' },
    purple: { bg: '#f5f3ff', text: '#7c3aed', border: '#ddd6fe' },
    orange: { bg: '#fff7ed', text: '#ea580c', border: '#fed7aa' },
    yellow: { bg: '#fefce8', text: '#ca8a04', border: '#fef08a' },
    gray: { bg: '#f9fafb', text: '#4b5563', border: '#e5e7eb' },
    red: { bg: '#fef2f2', text: '#dc2626', border: '#fecaca' }
};

export function StatusBadge({ status, label, variant }: StatusBadgeProps) {
    // If a variant is not explicitly provided, try to guess safely based on common statuses.
    let mappedVariant: StatusVariant = variant || 'gray';
    
    if (!variant) {
        const normalized = status.toUpperCase();
        if (['AVAILABLE', 'ACTIVE', 'WARRANTY_ACTIVE', 'COMPLETED', 'APPROVED'].includes(normalized)) mappedVariant = 'green';
        else if (['ASSIGNED', 'IN_USE'].includes(normalized)) mappedVariant = 'blue';
        else if (['RESERVED'].includes(normalized)) mappedVariant = 'purple';
        else if (['REPAIR', 'PENDING', 'IN_PROGRESS'].includes(normalized)) mappedVariant = 'yellow';
        else if (['PURCHASE_DOCUMENT_PENDING', 'WARNING'].includes(normalized)) mappedVariant = 'orange';
        else if (['LOST', 'BLOCKED', 'ERROR', 'CANCELED', 'REJECTED'].includes(normalized)) mappedVariant = 'red';
        else if (['RETIRED', 'EXPIRED', 'INACTIVE'].includes(normalized)) mappedVariant = 'gray';
    }

    const colors = colorMap[mappedVariant];
    const displayLabel = label || status;

    return (
        <span style={{
            display: 'inline-flex',
            alignItems: 'center',
            padding: '4px 10px',
            backgroundColor: colors.bg,
            color: colors.text,
            border: `1px solid ${colors.border}`,
            borderRadius: '16px',
            fontSize: '0.75rem',
            fontWeight: 600,
            textTransform: 'uppercase',
            letterSpacing: '0.02em',
            whiteSpace: 'nowrap'
        }}>
            {displayLabel}
        </span>
    );
}
