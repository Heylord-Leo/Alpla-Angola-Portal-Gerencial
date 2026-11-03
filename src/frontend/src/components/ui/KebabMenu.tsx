import React, { useState, useRef, useEffect, useLayoutEffect } from 'react';
import { MoreVertical } from 'lucide-react';
import { DropdownPortal } from './DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { computeKebabMenuPlacement, KEBAB_MENU_MIN_WIDTH, type KebabMenuPlacement } from './kebabMenuPlacement';

export interface KebabOption {
    label: string;
    onClick: () => void;
    icon?: React.ReactNode;
}

interface KebabMenuProps {
    options: KebabOption[];
    /**
     * Layering override for menus rendered INSIDE an elevated surface (v2.228.2). The default
     * keeps every page-context menu exactly where it always was (Z_INDEX.DROPDOWN = 500); a
     * kebab living inside a Drawer (1400/1401) passes Z_INDEX.MODAL (1500) so its popup clears
     * the drawer while still stacking below ModalWrapper flows (2000/2001). Never change the
     * global --z-dropdown token for this.
     */
    zIndex?: string;
    /** Accessible name of the trigger (default "Ações"). */
    ariaLabel?: string;
}

/**
 * Shared kebab ("⋮") menu. The popover is rendered through DropdownPortal (document.body) with
 * position:fixed, so an ancestor `overflow: auto/hidden` (table scroll wrappers, cards) can never clip it.
 * v2.249.1: placement is viewport-aware — measured after mount, right-aligned to the trigger, flipped above
 * it when the space below is insufficient (last table rows), clamped on narrow viewports/zoom, height-capped
 * with internal scrolling; closes on Escape, outside click, window resize and any scroll outside the menu
 * (a fixed popover must not drift away from its trigger inside a scrolling container).
 */
export function KebabMenu({ options, zIndex, ariaLabel = 'Ações' }: KebabMenuProps) {
    const [isOpen, setIsOpen] = useState(false);
    const [placement, setPlacement] = useState<KebabMenuPlacement | null>(null);
    const triggerRef = useRef<HTMLButtonElement>(null);
    const popoverRef = useRef<HTMLDivElement>(null);

    const toggleOpen = (e: React.MouseEvent) => {
        e.stopPropagation();
        setIsOpen(!isOpen);
    };

    // Measure the rendered popover, then place it (two-pass: first paint is invisible at 0/0).
    useLayoutEffect(() => {
        if (!isOpen) { setPlacement(null); return; }
        const trigger = triggerRef.current?.getBoundingClientRect();
        const menu = popoverRef.current?.getBoundingClientRect();
        if (!trigger || !menu) return;
        setPlacement(computeKebabMenuPlacement(
            { top: trigger.top, left: trigger.left, right: trigger.right, bottom: trigger.bottom, width: trigger.width, height: trigger.height },
            { width: menu.width, height: menu.height },
            { width: window.innerWidth, height: window.innerHeight },
        ));
    }, [isOpen, options.length]);

    useEffect(() => {
        if (!isOpen) return;
        const handleClickOutside = (event: MouseEvent) => {
            if (
                popoverRef.current &&
                !popoverRef.current.contains(event.target as Node) &&
                triggerRef.current &&
                !triggerRef.current.contains(event.target as Node)
            ) {
                setIsOpen(false);
            }
        };
        const handleKey = (event: KeyboardEvent) => {
            if (event.key === 'Escape') { setIsOpen(false); triggerRef.current?.focus(); }
        };
        const handleScroll = (event: Event) => {
            // Scrolling INSIDE the (height-capped) menu is fine; any other scroll detaches a fixed popover.
            if (popoverRef.current && event.target instanceof Node && popoverRef.current.contains(event.target)) return;
            setIsOpen(false);
        };
        const handleResize = () => setIsOpen(false);
        document.addEventListener('mousedown', handleClickOutside);
        document.addEventListener('keydown', handleKey);
        window.addEventListener('scroll', handleScroll, true);
        window.addEventListener('resize', handleResize);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
            document.removeEventListener('keydown', handleKey);
            window.removeEventListener('scroll', handleScroll, true);
            window.removeEventListener('resize', handleResize);
        };
    }, [isOpen]);

    const popoverStyle: React.CSSProperties = {
        position: 'fixed',
        top: placement ? `${placement.top}px` : 0,
        left: placement ? `${placement.left}px` : 0,
        visibility: placement ? 'visible' : 'hidden',
        minWidth: `${KEBAB_MENU_MIN_WIDTH}px`,
        maxHeight: placement ? `${placement.maxHeight}px` : undefined,
        overflowY: 'auto',
        backgroundColor: '#ffffff',
        border: '1px solid #e2e8f0',
        borderRadius: '8px',
        boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -2px rgba(0, 0, 0, 0.05)',
        display: 'flex',
        flexDirection: 'column',
        zIndex: (zIndex ?? Z_INDEX.DROPDOWN) as any,
        padding: '4px 0',
    };

    return (
        <div style={{ position: 'relative', display: 'inline-block' }}>
            <button
                ref={triggerRef}
                type="button"
                onClick={toggleOpen}
                aria-label={ariaLabel}
                aria-haspopup="menu"
                aria-expanded={isOpen}
                style={{
                    background: 'none',
                    border: 'none',
                    cursor: 'pointer',
                    padding: '8px',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'var(--color-primary)',
                    borderRadius: '4px',
                    transition: 'background-color 0.1s'
                }}
                onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
            >
                <MoreVertical size={20} strokeWidth={2.5} />
            </button>

            {isOpen && (
                <DropdownPortal>
                    <div ref={popoverRef} role="menu" aria-label={ariaLabel} style={popoverStyle}>
                        {options.map((option, idx) => (
                            <button
                                key={idx}
                                type="button"
                                role="menuitem"
                                onClick={(e) => {
                                    e.stopPropagation();
                                    option.onClick();
                                    setIsOpen(false);
                                }}
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '10px',
                                    padding: '8px 16px',
                                    border: 'none',
                                    backgroundColor: 'transparent',
                                    width: '100%',
                                    textAlign: 'left',
                                    cursor: 'pointer',
                                    fontWeight: 500,
                                    fontSize: '0.85rem',
                                    color: '#334155',
                                    whiteSpace: 'nowrap',
                                    transition: 'all 0.15s ease'
                                }}
                                onMouseEnter={(e) => {
                                    e.currentTarget.style.backgroundColor = '#f1f5f9';
                                    e.currentTarget.style.color = 'var(--color-primary)';
                                }}
                                onMouseLeave={(e) => {
                                    e.currentTarget.style.backgroundColor = 'transparent';
                                    e.currentTarget.style.color = '#334155';
                                }}
                            >
                                {option.icon}
                                {option.label}
                            </button>
                        ))}
                    </div>
                </DropdownPortal>
            )}
        </div>
    );
}
