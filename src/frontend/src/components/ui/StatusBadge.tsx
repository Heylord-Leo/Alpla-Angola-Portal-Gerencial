

interface StatusBadgeProps {
    code: string;
    name: string;
    color: string;
}

export function StatusBadge({ code, name, color }: StatusBadgeProps) {
    // Map backend color names to brutalist CSS classes used in the project
    const badgeType = color === 'red' ? 'danger' :
                     color === 'yellow' ? 'warning' :
                     color === 'green' ? 'success' :
                     color || 'neutral';

    return (
        <span 
            className={`badge badge-sm badge-${badgeType}`} 
            title={`Código: ${code}`}
        >
            {name}
        </span>
    );
}
