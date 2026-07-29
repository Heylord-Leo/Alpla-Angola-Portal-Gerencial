import React from 'react';

interface PageContainerProps {
    children: React.ReactNode;
    className?: string;
    style?: React.CSSProperties;
    maxWidth?: string;
    padding?: string;
}

export function PageContainer({ 
    children, 
    className = '', 
    style = {}, 
    maxWidth = 'var(--page-max-width, 1400px)',
    padding
}: PageContainerProps) {
    return (
        <div 
            className={`page-container ${className}`}
            style={{
                minHeight: 0,
                flex: 1,
                padding: padding || `var(--spacing-page-y, 24px) var(--spacing-page-x, 32px)`,
                width: '100%',
                maxWidth: maxWidth,
                margin: '0 auto',
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--spacing-page-gap, 32px)',
                minWidth: 0,
                overflowX: 'hidden',
                ...style
            }}
        >
            {children}
        </div>
    );
}
