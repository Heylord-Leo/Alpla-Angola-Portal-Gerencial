import React, { useState, useRef, useEffect } from 'react';
import { DropdownPortal } from './DropdownPortal';

interface TooltipProps {
    children: React.ReactNode;
    content: React.ReactNode;
    variant?: 'light' | 'dark';
}

export function Tooltip({ children, content, variant = 'light' }: TooltipProps) {
    const [isVisible, setIsVisible] = useState(false);
    const triggerRef = useRef<HTMLDivElement>(null);
    const [tooltipStyles, setTooltipStyles] = useState<React.CSSProperties>({});

    useEffect(() => {
        if (isVisible && triggerRef.current) {
            const rect = triggerRef.current.getBoundingClientRect();
            const isDark = variant === 'dark';
            
            // Default position: top-centered
            setTooltipStyles({
                position: 'fixed',
                top: `${rect.top - 8}px`,
                left: `${rect.left + rect.width / 2}px`,
                transform: 'translate(-50%, -100%)',
                backgroundColor: isDark ? '#0f172a' : 'var(--color-bg-surface)',
                color: isDark ? '#ffffff' : 'inherit',
                border: isDark ? 'none' : '2px solid var(--color-primary)',
                boxShadow: 'var(--shadow-brutal)',
                padding: isDark ? '8px 12px' : '12px 16px',
                zIndex: 10000,
                pointerEvents: 'none',
                minWidth: isDark ? 'auto' : '200px',
                maxWidth: '350px',
                borderRadius: isDark ? '4px' : '0'
            });
        }
    }, [isVisible, variant]);

    return (
        <div 
            ref={triggerRef}
            onMouseEnter={() => setIsVisible(true)}
            onMouseLeave={() => setIsVisible(false)}
            style={{ display: 'inline-block', position: 'relative' }}
        >
            {children}
            {isVisible && (
                <DropdownPortal>
                    <div style={tooltipStyles}>
                        {content}
                    </div>
                </DropdownPortal>
            )}
        </div>
    );
}
