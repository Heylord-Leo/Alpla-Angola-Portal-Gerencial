import React from 'react';
import { FileQuestion } from 'lucide-react';

export interface EmptyStateProps {
    title: string;
    description?: string;
    icon?: React.ReactNode;
    action?: React.ReactNode;
    className?: string;
    style?: React.CSSProperties;
}

export function EmptyState({
    title,
    description,
    icon,
    action,
    className,
    style
}: EmptyStateProps) {
    return (
        <div className={className} style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '48px 24px',
            textAlign: 'center',
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px dashed var(--color-border)',
            borderRadius: '12px',
            ...style
        }}>
            <div style={{
                color: 'var(--color-placeholder)',
                marginBottom: '16px',
                display: 'flex',
                justifyContent: 'center'
            }}>
                {icon || <FileQuestion size={48} strokeWidth={1.5} />}
            </div>
            
            <h3 style={{
                margin: 0,
                fontSize: '1rem',
                fontWeight: 600,
                color: 'var(--color-text-main)',
                marginBottom: description ? '8px' : '0'
            }}>
                {title}
            </h3>
            
            {description && (
                <p style={{
                    margin: 0,
                    fontSize: '0.85rem',
                    color: 'var(--color-text-muted)',
                    maxWidth: '400px',
                    lineHeight: 1.5
                }}>
                    {description}
                </p>
            )}

            {action && (
                <div style={{ marginTop: '24px' }}>
                    {action}
                </div>
            )}
        </div>
    );
}
