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
    maxWidth = '1400px',
    padding = '24px 32px'
}: PageContainerProps) {
    return (
        <div 
            className={`page-container ${className}`}
            style={{
                minHeight: '100vh',
                padding: padding,
                width: '100%',
                maxWidth: maxWidth,
                margin: '0 auto',
                display: 'flex',
                flexDirection: 'column',
                gap: '32px',
                ...style
            }}
        >
            {children}
        </div>
    );
}
