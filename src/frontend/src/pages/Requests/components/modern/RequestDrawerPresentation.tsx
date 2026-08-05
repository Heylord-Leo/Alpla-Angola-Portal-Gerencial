import React, { useCallback, useEffect, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { X } from 'lucide-react';
import { RequestEdit } from '../../RequestEdit';
import {
    DRAWER_KEYBOARD_STEP,
    DRAWER_KEYBOARD_STEP_LARGE,
    DRAWER_MAX_VIEWPORT_RATIO,
    DRAWER_MIN_WIDTH,
    clampDrawerWidth,
    readStoredDrawerWidth,
    storeDrawerWidth,
    widthFromPointer
} from '../../../../lib/drawerWidth';

interface RequestDrawerProps {
    requestId: string | null;
    isOpen: boolean;
    onClose: () => void;
}

export function RequestDrawerPresentation({ requestId, isOpen, onClose }: RequestDrawerProps) {
    /**
     * The drawer is a workspace, not a preview: drafts are edited, documents reviewed and requests
     * submitted from inside it. A fixed 800px made document cards and item tables unreadable, so the
     * width is the user's to choose - and is remembered, because choosing it every time is a tax.
     */
    const [width, setWidth] = useState(() =>
        readStoredDrawerWidth(typeof window === 'undefined' ? 1920 : window.innerWidth));
    const [isResizing, setIsResizing] = useState(false);
    const widthRef = useRef(width);
    widthRef.current = width;

    // A width chosen on a wide monitor must not push the drawer off a smaller one later.
    useEffect(() => {
        const onViewportResize = () => setWidth(w => clampDrawerWidth(w, window.innerWidth));
        window.addEventListener('resize', onViewportResize);
        return () => window.removeEventListener('resize', onViewportResize);
    }, []);

    const beginResize = useCallback((startEvent: React.PointerEvent<HTMLDivElement>) => {
        startEvent.preventDefault();
        const handle = startEvent.currentTarget;
        handle.setPointerCapture?.(startEvent.pointerId);
        setIsResizing(true);

        // Dragging across text would otherwise select it, and the selection survives the drag.
        const previousUserSelect = document.body.style.userSelect;
        document.body.style.userSelect = 'none';
        document.body.style.cursor = 'col-resize';

        const onMove = (e: PointerEvent) => setWidth(widthFromPointer(e.clientX, window.innerWidth));

        const onUp = () => {
            handle.releasePointerCapture?.(startEvent.pointerId);
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
            window.removeEventListener('pointercancel', onUp);
            document.body.style.userSelect = previousUserSelect;
            document.body.style.cursor = '';
            setIsResizing(false);
            storeDrawerWidth(widthRef.current);
        };

        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
        window.addEventListener('pointercancel', onUp);
    }, []);

    const onHandleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
        const step = e.shiftKey ? DRAWER_KEYBOARD_STEP_LARGE : DRAWER_KEYBOARD_STEP;
        // Left widens: the drawer grows leftwards from a right edge pinned to the viewport.
        const delta = e.key === 'ArrowLeft' ? step : e.key === 'ArrowRight' ? -step : 0;
        if (delta === 0) return;

        e.preventDefault();
        setWidth(w => {
            const next = clampDrawerWidth(w + delta, window.innerWidth);
            storeDrawerWidth(next);
            return next;
        });
    };

    return (
        <AnimatePresence>
            {isOpen && requestId && (
                <>
                    {/* Overlay */}
                    <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        onClick={onClose}
                        style={{
                            position: 'fixed',
                            inset: 0,
                            backgroundColor: 'rgba(15, 23, 42, 0.4)',
                            backdropFilter: 'blur(4px)',
                            WebkitBackdropFilter: 'blur(4px)',
                            zIndex: 'var(--z-drawer)' as any,
                        }}
                    />

                    {/* Drawer */}
                    <motion.div
                        initial={{ x: '100%', opacity: 0 }}
                        animate={{ x: 0, opacity: 1 }}
                        exit={{ x: '100%', opacity: 0 }}
                        transition={isResizing
                            ? { duration: 0 }
                            : { type: 'spring', damping: 25, stiffness: 200 }}
                        style={{
                            position: 'fixed',
                            right: 0,
                            top: 0,
                            bottom: 0,
                            width,
                            maxWidth: '100vw',
                            backgroundColor: 'var(--color-bg-surface)',
                            boxShadow: 'var(--shadow-premium)',
                            display: 'flex',
                            flexDirection: 'column',
                            zIndex: 'calc(var(--z-drawer) + 1)' as any,
                            overflow: 'hidden',
                        }}
                    >
                        {/* Resize handle - a slim strip on the left edge, invisible until wanted. */}
                        <div
                            role="separator"
                            aria-orientation="vertical"
                            aria-label="Redimensionar o painel do pedido"
                            aria-valuenow={Math.round(width)}
                            aria-valuemin={DRAWER_MIN_WIDTH}
                            aria-valuemax={Math.round(window.innerWidth * DRAWER_MAX_VIEWPORT_RATIO)}
                            tabIndex={0}
                            onPointerDown={beginResize}
                            onKeyDown={onHandleKeyDown}
                            style={{
                                position: 'absolute', left: 0, top: 0, bottom: 0, width: '10px',
                                cursor: 'col-resize', zIndex: 20, touchAction: 'none',
                                display: 'flex', alignItems: 'center', justifyContent: 'center'
                            }}
                            onMouseEnter={e => {
                                (e.currentTarget.firstElementChild as HTMLElement).style.opacity = '1';
                            }}
                            onMouseLeave={e => {
                                if (!isResizing) {
                                    (e.currentTarget.firstElementChild as HTMLElement).style.opacity = '0';
                                }
                            }}
                            onFocus={e => {
                                (e.currentTarget.firstElementChild as HTMLElement).style.opacity = '1';
                            }}
                            onBlur={e => {
                                (e.currentTarget.firstElementChild as HTMLElement).style.opacity = '0';
                            }}
                        >
                            <div style={{
                                width: '3px', height: '48px', borderRadius: '2px',
                                backgroundColor: 'var(--color-primary)',
                                opacity: isResizing ? 1 : 0, transition: 'opacity 0.15s ease'
                            }} />
                        </div>

                        {/* Header */}
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            gap: '12px',
                            flexWrap: 'wrap',
                            padding: '16px 20px 16px 26px',
                            borderBottom: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-surface)',
                            position: 'sticky',
                            top: 0,
                            zIndex: 10,
                            flexShrink: 0,
                        }}>
                            <div>
                                <h2 style={{ 
                                    fontSize: '1.1rem', 
                                    fontWeight: 800, 
                                    color: 'var(--color-primary)',
                                    margin: 0,
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.02em',
                                }}>Detalhes do Pedido</h2>
                                <p style={{ 
                                    fontSize: '0.8rem', 
                                    color: 'var(--color-text-muted)',
                                    margin: '2px 0 0',
                                    fontWeight: 500,
                                    textTransform: 'none',
                                }}>Visualização Rápida</p>
                            </div>
                            <button
                                onClick={onClose}
                                style={{
                                    padding: '8px',
                                    borderRadius: 'var(--radius-md)',
                                    border: '1px solid var(--color-border)',
                                    backgroundColor: 'transparent',
                                    cursor: 'pointer',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    transition: 'background-color 0.15s ease',
                                    color: 'var(--color-text-muted)',
                                }}
                                onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                                onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'transparent'; }}
                            >
                                <X size={20} />
                            </button>
                        </div>

                        {/* Content Scrollable Area */}
                        <div style={{
                            flex: 1, overflowY: 'auto', width: '100%', position: 'relative',
                            padding: '24px',
                            // While dragging, the content must not also react to the pointer.
                            pointerEvents: isResizing ? 'none' : undefined
                        }}>
                            <RequestEdit requestId={requestId} onClose={onClose} />
                        </div>
                    </motion.div>
                </>
            )}
        </AnimatePresence>
    );
}
