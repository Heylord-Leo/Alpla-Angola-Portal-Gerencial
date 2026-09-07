import React, { useState, useRef, useEffect, useLayoutEffect } from 'react';
import { DropdownPortal } from './DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { motion, AnimatePresence } from 'framer-motion';
import { computeTooltipPosition } from './tooltipPosition';

interface ModernTooltipProps {
    children: React.ReactNode;
    content: React.ReactNode;
    side?: 'top' | 'bottom' | 'left' | 'right';
    align?: 'start' | 'center' | 'end';
    /**
     * Also open on click/tap and keep it open until dismissed. Needed on touch devices, where
     * hover does not exist, and for structured content the user needs time to read.
     * Off by default so every existing hover-only usage is unaffected.
     */
    openOnClick?: boolean;
    /** Accessible label for the trigger when it wraps an icon with no text of its own. */
    ariaLabel?: string;
    /** Widen beyond the 300px default for structured content such as an obligation list. */
    maxWidth?: number;
    /**
     * Set to -1 when the wrapped child is itself focusable (a button), so the tooltip does not add a
     * second, redundant tab stop in front of it. Focus events still bubble from the child, so the
     * tooltip continues to appear on keyboard focus.
     */
    triggerTabIndex?: number;
}

export function ModernTooltip({
    children,
    content,
    side = 'top',
    align = 'center',
    openOnClick = false,
    ariaLabel,
    maxWidth = 300,
    triggerTabIndex = 0
}: ModernTooltipProps) {
    const [isVisible, setIsVisible] = useState(false);
    // Set by a click/tap or Enter/Space; survives mouseleave so the content can actually be read.
    const [isPinned, setIsPinned] = useState(false);
    const triggerRef = useRef<HTMLDivElement>(null);
    const tooltipRef = useRef<HTMLDivElement>(null);
    const [tooltipStyles, setTooltipStyles] = useState<React.CSSProperties>({});
    const [transformOrigin, setTransformOrigin] = useState<string>('bottom');
    // False until the popover has been measured and viewport-corrected — kept hidden while measuring so
    // there is no corner-flash flicker.
    const [positioned, setPositioned] = useState(false);

    // Viewport-aware placement: measure the trigger AND the (already width-constrained) popover, then
    // flip/clamp into the viewport so no edge is ever crossed. See computeTooltipPosition (pure, tested).
    useLayoutEffect(() => {
        if (!isVisible || !triggerRef.current || !tooltipRef.current) return;
        const trigger = triggerRef.current.getBoundingClientRect();
        const el = tooltipRef.current;
        const viewport = { width: window.innerWidth, height: window.innerHeight };
        const p = computeTooltipPosition(
            trigger,
            { width: el.offsetWidth, height: el.offsetHeight },
            side, align, viewport, maxWidth, 12,
        );
        setTransformOrigin(p.transformOrigin);
        setTooltipStyles({
            position: 'fixed',
            top: `${p.top}px`,
            left: `${p.left}px`,
            zIndex: Z_INDEX.TOOLTIP as any,
            // A pinned tooltip must be interactive (scroll/select); a hover one must not steal the
            // pointer from the element underneath.
            pointerEvents: isPinned ? 'auto' : 'none',
            minWidth: 'auto',
            maxWidth: `${p.maxWidth}px`,
            maxHeight: `${p.maxHeight}px`,
            overflowY: 'auto',
            fontSize: '0.8rem',
        });
        setPositioned(true);
    }, [isVisible, isPinned, side, align, maxWidth]);

    // Reset the measure gate whenever the popover closes so the next open re-measures cleanly.
    useEffect(() => { if (!isVisible) setPositioned(false); }, [isVisible]);

    // Off-screen measuring style: width already constrained to the viewport so the wrapped height is
    // correct before we compute the final placement; hidden until positioned.
    const measuringStyles: React.CSSProperties = {
        position: 'fixed',
        top: 0,
        left: 0,
        visibility: 'hidden',
        zIndex: Z_INDEX.TOOLTIP as any,
        pointerEvents: 'none',
        minWidth: 'auto',
        maxWidth: `${typeof window !== 'undefined' ? Math.min(maxWidth, window.innerWidth - 24) : maxWidth}px`,
        maxHeight: `${typeof window !== 'undefined' ? window.innerHeight - 24 : 600}px`,
        overflowY: 'auto',
        fontSize: '0.8rem',
    };

    // Escape closes a pinned tooltip, and dismissing returns focus to the trigger.
    useEffect(() => {
        if (!isPinned) return;

        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                setIsPinned(false);
                setIsVisible(false);
                triggerRef.current?.focus();
            }
        };
        document.addEventListener('keydown', onKeyDown);
        return () => document.removeEventListener('keydown', onKeyDown);
    }, [isPinned]);

    const toggle = () => {
        const next = !isPinned;
        setIsPinned(next);
        setIsVisible(next);
    };

    return (
        <div
            ref={triggerRef}
            role={openOnClick ? 'button' : undefined}
            aria-label={ariaLabel}
            aria-expanded={openOnClick ? isVisible : undefined}
            onMouseEnter={() => setIsVisible(true)}
            onMouseLeave={() => { if (!isPinned) setIsVisible(false); }}
            onFocus={() => setIsVisible(true)}
            onBlur={() => { if (!isPinned) setIsVisible(false); }}
            onClick={openOnClick ? (e) => { e.preventDefault(); e.stopPropagation(); toggle(); } : undefined}
            onKeyDown={openOnClick ? (e) => {
                if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); }
            } : undefined}
            tabIndex={triggerTabIndex}
            style={{ display: 'inline-block', position: 'relative', outline: 'none' }}
        >
            {children}
            <AnimatePresence>
                {isVisible && (
                    <DropdownPortal>
                        <div ref={tooltipRef} style={positioned ? tooltipStyles : measuringStyles}>
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
