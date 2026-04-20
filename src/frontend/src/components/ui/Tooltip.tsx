import React, { useState, useRef, useEffect } from 'react';
import { DropdownPortal } from './DropdownPortal';
import { Z_INDEX } from '../../constants/ui';

interface TooltipProps {
    children: React.ReactNode;
    content: React.ReactNode;
    variant?: 'light' | 'dark';
    side?: 'top' | 'bottom' | 'left' | 'right';
    align?: 'start' | 'center' | 'end';
}

export function Tooltip({ 
    children, 
    content, 
    variant = 'light',
    side = 'top',
    align = 'center'
}: TooltipProps) {
    const [isVisible, setIsVisible] = useState(false);
    const triggerRef = useRef<HTMLDivElement>(null);
    const [tooltipStyles, setTooltipStyles] = useState<React.CSSProperties>({});

    useEffect(() => {
        if (isVisible && triggerRef.current) {
            const rect = triggerRef.current.getBoundingClientRect();
            const isDark = variant === 'dark';
            
            let top: string = 'auto';
            let left: string = 'auto';
            let transform: string = '';

            // Side positioning
            if (side === 'top') {
                top = `${rect.top - 8}px`;
                transform = 'translateY(-100%)';
            } else if (side === 'bottom') {
                top = `${rect.bottom + 8}px`;
                transform = 'translateY(0)';
            } else if (side === 'left') {
                left = `${rect.left - 8}px`;
                transform = 'translateX(-100%)';
            } else { // right
                left = `${rect.right + 8}px`;
                transform = 'translateX(0)';
            }

            // Alignment positioning
            if (side === 'top' || side === 'bottom') {
                if (align === 'start') {
                    left = `${rect.left}px`;
                    transform += ' translateX(0)';
                } else if (align === 'end') {
                    left = `${rect.right}px`;
                    transform += ' translateX(-100%)';
                } else { // center
                    left = `${rect.left + rect.width / 2}px`;
                    transform += ' translateX(-50%)';
                }
            } else { // side is left or right
                if (align === 'start') {
                    top = `${rect.top}px`;
                    transform += ' translateY(0)';
                } else if (align === 'end') {
                    top = `${rect.bottom}px`;
                    transform += ' translateY(-100%)';
                } else { // center
                    top = `${rect.top + rect.height / 2}px`;
                    transform += ' translateY(-50%)';
                }
            }
            
            setTooltipStyles({
                position: 'fixed',
                top,
                left,
                transform,
                backgroundColor: isDark ? '#0f172a' : 'var(--color-bg-surface)',
                color: isDark ? '#ffffff' : 'inherit',
                border: isDark ? 'none' : '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-md)',
                padding: isDark ? '8px 12px' : '12px 16px',
                zIndex: Z_INDEX.TOOLTIP as any,
                pointerEvents: 'none',
                minWidth: isDark ? 'auto' : '200px',
                maxWidth: '350px',
                borderRadius: isDark ? 'var(--radius-sm)' : 'var(--radius-md)'
            });
        }
    }, [isVisible, variant, side, align]);

    return (
        <div 
            ref={triggerRef}
            onMouseEnter={() => setIsVisible(true)}
            onMouseLeave={() => setIsVisible(false)}
            onFocus={() => setIsVisible(true)}
            onBlur={() => setIsVisible(false)}
            tabIndex={0}
            style={{ display: 'inline-block', position: 'relative', outline: 'none' }}
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
