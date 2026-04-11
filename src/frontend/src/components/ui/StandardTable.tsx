import React from 'react';

interface StandardTableProps {
    children: React.ReactNode;
    emptyState?: React.ReactNode;
    loading?: boolean;
    loadingState?: React.ReactNode;
    isEmpty?: boolean;
    style?: React.CSSProperties;
    containerStyle?: React.CSSProperties;
}

export function StandardTable({
    children,
    emptyState,
    loading,
    loadingState,
    isEmpty,
    style = {},
    containerStyle = {}
}: StandardTableProps) {
    if (loading && loadingState) {
        return <>{loadingState}</>;
    }

    if (!loading && isEmpty && emptyState) {
        return <>{emptyState}</>;
    }

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: '12px',
            overflow: 'hidden',
            boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.05)',
            ...containerStyle
        }}>
            <div style={{ overflowX: 'auto' }}>
                <table style={{
                    width: '100%',
                    borderCollapse: 'collapse',
                    fontSize: '0.85rem',
                    ...style
                }}>
                    {/* The caller should provide standard standard <thead> and <tbody> with their <tr>/<th>/<td> elements.
                        However, we inject a class or CSS target down into children headers and cells via global CSS or inline logic if preferred. 
                        For now, the calling component should match class="standard-table" if they use external CSS, 
                        or we can just wrap the structure. 
                    */}
                    {children}
                </table>
            </div>
            {/* The caller handles footer/pagination outside or within a complementary component */}
        </div>
    );
}

export function TableEmptyState({ icon, title, description }: { icon?: React.ReactNode, title: string, description?: string }) {
    return (
        <div style={{ 
            padding: '64px 32px', 
            textAlign: 'center', 
            color: 'var(--color-text-muted)',
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px dashed var(--color-border)',
            borderRadius: '12px',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center'
        }}>
            {icon && <div style={{ opacity: 0.3, marginBottom: '16px' }}>{icon}</div>}
            <p style={{ fontWeight: 800, fontSize: '0.9rem', margin: '0 0 8px 0', color: 'var(--color-text-main)' }}>{title}</p>
            {description && <p style={{ fontSize: '0.85rem', margin: 0 }}>{description}</p>}
        </div>
    );
}
