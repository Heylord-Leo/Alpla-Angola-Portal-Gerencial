import React from 'react';

interface PageHeaderProps {
    title: React.ReactNode;
    subtitle?: React.ReactNode;
    icon?: React.ReactNode;
    actions?: React.ReactNode;
    style?: React.CSSProperties;
}

export function PageHeader({ title, subtitle, icon, actions, style = {} }: PageHeaderProps) {
    return (
        <header style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: '16px',
            ...style
        }}>
            <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: subtitle ? '4px' : '0' }}>
                    {icon && (
                        <div style={{ display: 'flex', alignItems: 'center', color: 'var(--color-primary)' }}>
                            {icon}
                        </div>
                    )}
                    <h1 style={{
                        fontSize: '1.5rem',
                        fontWeight: 900,
                        color: 'var(--color-primary)',
                        margin: 0,
                        letterSpacing: '-0.01em',
                    }}>
                        {title}
                    </h1>
                    {/* Render any inline badges/chips related to the title here if passed */}
                </div>
                {subtitle && (
                    <p style={{
                        color: 'var(--color-text-muted)',
                        fontSize: '0.85rem',
                        margin: 0,
                        fontWeight: 500,
                    }}>
                        {subtitle}
                    </p>
                )}
            </div>
            {actions && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {actions}
                </div>
            )}
        </header>
    );
}
