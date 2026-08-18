import React, { useCallback, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';
import { Z_INDEX } from '../../constants/ui';

export type InfoModalTone = 'neutral' | 'info' | 'warning' | 'danger';

interface Props {
    isOpen: boolean;
    /** Called by the close button, the backdrop and Escape. For a decision modal this is "cancel". */
    onClose: () => void;
    title: string;
    /** Rendered in the header beside the title — usually the same icon as the trigger. */
    icon?: React.ReactNode;
    tone?: InfoModalTone;
    /** Fixed max width in px. The modal narrows below this on small viewports. */
    maxWidth?: number;
    /** Actions pinned to the bottom, outside the scrolling body. */
    footer?: React.ReactNode;
    /** A decision modal should not be dismissed by a stray click outside it. */
    closeOnBackdrop?: boolean;
    children: React.ReactNode;
}

/** Accent per tone. Only the header rule and the icon are tinted — the body stays neutral so the
 *  content, not the chrome, carries the severity. */
const TONE_COLOR: Record<InfoModalTone, string> = {
    neutral: 'var(--color-primary)',
    info: '#2563eb',
    warning: '#b45309',
    danger: '#b91c1c'
};

const FOCUSABLE =
    'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * The Portal's modal for explanations and decisions that were previously inline text.
 *
 * <p>Built rather than reused because the existing <code>ModalWrapper</code> has no focus trap and
 * no Escape handling, and retrofitting either would change the behaviour of every modal in the
 * application. The visual language is deliberately identical to it — same surface tokens, same
 * radius, same header rule — so this reads as the same component family.</p>
 *
 * <p>Everything about it is theme-token driven, so it inverts correctly in dark mode, and the body
 * scrolls internally rather than letting the page grow.</p>
 */
export function InfoModal({
    isOpen,
    onClose,
    title,
    icon,
    tone = 'neutral',
    maxWidth = 620,
    footer,
    closeOnBackdrop = true,
    children
}: Props) {
    const containerRef = useRef<HTMLDivElement>(null);
    // Captured before the modal steals focus, so dismissing returns the user exactly where they were.
    const previouslyFocused = useRef<HTMLElement | null>(null);

    const handleKeyDown = useCallback((e: KeyboardEvent) => {
        if (e.key === 'Escape') {
            e.stopPropagation();
            onClose();
            return;
        }

        if (e.key !== 'Tab' || !containerRef.current) return;

        const focusable = Array.from(
            containerRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)
        ).filter(el => el.offsetParent !== null);

        if (focusable.length === 0) {
            e.preventDefault();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement as HTMLElement | null;

        // Wrap at both ends — without this, Tab walks out of the modal and into the page behind it.
        if (e.shiftKey && (active === first || !containerRef.current.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && active === last) {
            e.preventDefault();
            first.focus();
        }
    }, [onClose]);

    useEffect(() => {
        if (!isOpen) return;

        previouslyFocused.current = document.activeElement as HTMLElement | null;

        // Focus the first control if there is one, otherwise the dialog itself, so a screen reader
        // announces the title rather than leaving focus behind on the trigger.
        const target = containerRef.current?.querySelector<HTMLElement>(FOCUSABLE) ?? containerRef.current;
        target?.focus();

        document.addEventListener('keydown', handleKeyDown, true);
        return () => {
            document.removeEventListener('keydown', handleKeyDown, true);
            previouslyFocused.current?.focus();
        };
    }, [isOpen, handleKeyDown]);

    if (!isOpen) return null;

    const accent = TONE_COLOR[tone];

    return createPortal(
        <div
            role="presentation"
            onMouseDown={closeOnBackdrop ? onClose : undefined}
            style={{
                position: 'fixed', inset: 0,
                backgroundColor: 'rgba(15, 23, 42, 0.55)',
                backdropFilter: 'blur(4px)',
                zIndex: Z_INDEX.MODAL as any,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                padding: '24px'
            }}
        >
            <div
                ref={containerRef}
                role="dialog"
                aria-modal="true"
                aria-label={title}
                tabIndex={-1}
                onMouseDown={e => e.stopPropagation()}
                style={{
                    width: '100%',
                    maxWidth: `${maxWidth}px`,
                    // 85vh keeps the whole dialog visible at 1600×900 without the page scrolling.
                    maxHeight: '85vh',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '14px',
                    boxShadow: '0 20px 60px rgba(0,0,0,0.30)',
                    display: 'flex', flexDirection: 'column',
                    outline: 'none',
                    animation: 'infoModalIn 0.18s ease-out'
                }}
            >
                <style>{`@keyframes infoModalIn { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }`}</style>

                <div style={{
                    display: 'flex', alignItems: 'center', gap: '10px',
                    padding: '14px 20px', borderBottom: '1px solid var(--color-border)'
                }}>
                    {icon && <span style={{ color: accent, display: 'flex', flexShrink: 0 }}>{icon}</span>}
                    <h3 style={{
                        margin: 0, flex: 1, minWidth: 0,
                        fontSize: '1rem', fontWeight: 700, color: 'var(--color-text-main)'
                    }}>
                        {title}
                    </h3>
                    <button
                        type="button"
                        onClick={onClose}
                        aria-label="Fechar"
                        style={{
                            background: 'none', border: 'none', cursor: 'pointer',
                            color: 'var(--color-text-muted)', display: 'flex', padding: '4px', flexShrink: 0
                        }}
                    >
                        <X size={18} />
                    </button>
                </div>

                {/* Wide content (evidence lines, long titles) scrolls inside here — the page never
                    scrolls sideways because of a modal. */}
                <div style={{
                    padding: '16px 20px', overflowY: 'auto', overflowX: 'hidden', flex: 1,
                    overflowWrap: 'anywhere'
                }}>
                    {children}
                </div>

                {footer && (
                    <div style={{
                        padding: '12px 20px', borderTop: '1px solid var(--color-border)',
                        display: 'flex', justifyContent: 'flex-end', gap: '10px', flexWrap: 'wrap'
                    }}>
                        {footer}
                    </div>
                )}
            </div>
        </div>,
        document.body
    );
}
