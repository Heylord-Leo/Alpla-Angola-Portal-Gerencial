import React from 'react';

export interface SectionCardProps {
    title: string;
    icon?: React.ReactNode;
    description?: string;
    action?: React.ReactNode;
    children: React.ReactNode;
    className?: string;
    style?: React.CSSProperties;
    bodyStyle?: React.CSSProperties;
}

export function SectionCard({
    title,
    icon,
    description,
    action,
    children,
    className,
    style,
    bodyStyle
}: SectionCardProps) {
    return (
        <div className={className} style={{
            backgroundColor: '#ffffff',
            border: '1px solid var(--color-border)',
            borderRadius: '12px',
            overflow: 'hidden',
            marginBottom: '16px',
            ...style
        }}>
            {/* Header */}
            <div style={{
                padding: '12px 16px',
                borderBottom: '1px solid var(--color-border)',
                backgroundColor: '#fafafa',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    {icon && (
                        <div style={{ color: 'var(--color-text-muted)' }}>
                            {icon}
                        </div>
                    )}
                    <div>
                        <h3 style={{
                            margin: 0,
                            fontSize: '0.9rem',
                            fontWeight: 600,
                            color: 'var(--color-text-main)'
                        }}>
                            {title}
                        </h3>
                        {description && (
                            <p style={{
                                margin: 0,
                                marginTop: '2px',
                                fontSize: '0.75rem',
                                color: 'var(--color-text-muted)'
                            }}>
                                {description}
                            </p>
                        )}
                    </div>
                </div>
                
                {action && (
                    <div style={{ display: 'flex', alignItems: 'center' }}>
                        {action}
                    </div>
                )}
            </div>

            {/* Body */}
            <div style={{ padding: '16px', ...bodyStyle }}>
                {children}
            </div>
        </div>
    );
}
