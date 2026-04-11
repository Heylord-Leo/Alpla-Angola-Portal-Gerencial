import React, { useState, useRef, useEffect } from 'react';
import { DropdownPortal } from './DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { motion, AnimatePresence } from 'framer-motion';

interface ModernTooltipProps {
    children: React.ReactNode;
    content: React.ReactNode;
    side?: 'top' | 'bottom' | 'left' | 'right';
    align?: 'start' | 'center' | 'end';
}

export function ModernTooltip({ 
    children, 
    content, 
    side = 'top',
    align = 'center'
}: ModernTooltipProps) {
    const [isVisible, setIsVisible] = useState(false);
    const triggerRef = useRef<HTMLDivElement>(null);
    const [tooltipStyles, setTooltipStyles] = useState<React.CSSProperties>({});
    const [transformOrigin, setTransformOrigin] = useState<string>('bottom');

    useEffect(() => {
        if (isVisible && triggerRef.current) {
            const rect = triggerRef.current.getBoundingClientRect();
            
            let top: string = 'auto';
            let left: string = 'auto';
            let transform: string = '';
            let tOrigin = 'center';

            // Side positioning
            if (side === 'top') {
                top = `${rect.top - 8}px`;
                transform = 'translateY(-100%)';
                tOrigin = 'bottom';
            } else if (side === 'bottom') {
                top = `${rect.bottom + 8}px`;
                transform = 'translateY(0)';
                tOrigin = 'top';
            } else if (side === 'left') {
                left = `${rect.left - 8}px`;
                transform = 'translateX(-100%)';
                tOrigin = 'right';
            } else { // right
                left = `${rect.right + 8}px`;
                transform = 'translateX(0)';
                tOrigin = 'left';
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
            
            setTransformOrigin(tOrigin);
            setTooltipStyles({
                position: 'fixed',
                top,
                left,
                transform,
                zIndex: Z_INDEX.TOOLTIP as any,
                pointerEvents: 'none',
                minWidth: 'auto',
                maxWidth: '300px',
                fontSize: '0.8rem',
            });
        }
    }, [isVisible, side, align]);

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
            <AnimatePresence>
                {isVisible && (
                    <DropdownPortal>
                        <div style={tooltipStyles}>
                            <motion.div 
                                initial={{ opacity: 0, scale: 0.95, y: side === 'top' ? 5 : side === 'bottom' ? -5 : 0, x: side === 'left' ? 5 : side === 'right' ? -5 : 0 }}
                                animate={{ opacity: 1, scale: 1, y: 0, x: 0 }}
                                exit={{ opacity: 0, scale: 0.95, transition: { duration: 0.1 } }}
                                transition={{ type: 'spring', stiffness: 400, damping: 25 }}
                                style={{ 
                                    transformOrigin,
                                    backgroundColor: 'var(--color-bg-surface)',
                                    color: 'var(--color-text-main)',
                                    border: '1px solid var(--color-border)',
                                    boxShadow: '0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)',
                                    padding: '10px 14px',
                                    borderRadius: '8px',
                                }}
                            >
                            {content}
                            </motion.div>
                        </div>
                    </DropdownPortal>
                )}
            </AnimatePresence>
        </div>
    );
}
