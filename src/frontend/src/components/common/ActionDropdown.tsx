import React, { useState, useRef, useEffect } from 'react';
import { MoreHorizontal } from 'lucide-react';

interface ActionOption {
    label: string;
    icon?: React.ReactNode;
    onClick: () => void;
    color?: string;
    disabled?: boolean;
    disabledReason?: string;
}

interface ActionDropdownProps {
    options: ActionOption[];
}

export function ActionDropdown({ options }: ActionDropdownProps) {
    const [open, setOpen] = useState(false);
    const ref = useRef<HTMLDivElement>(null);

    useEffect(() => {
        const handler = (e: MouseEvent) => {
            if (ref.current && !ref.current.contains(e.target as Node)) {
                setOpen(false);
            }
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, []);

    // Filter out options that are null/falsy
    const validOptions = options.filter(Boolean);
    if (validOptions.length === 0) return null;

    return (
        <div style={{ position: 'relative' }} ref={ref}>
            <button
                onClick={() => setOpen(!open)}
                style={{
                    display: 'flex', alignItems: 'center', gap: 4, padding: '5px 10px',
                    border: '1px solid var(--color-border)', borderRadius: 6,
                    background: 'var(--color-bg-surface)', color: 'var(--color-text)',
                    cursor: 'pointer', fontSize: '0.78rem', fontWeight: 600,
                    transition: 'all 0.15s'
                }}
                onMouseOver={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; }}
            >
                Mais ações <MoreHorizontal size={13} />
            </button>
            {open && (
                <div style={{
                    position: 'absolute', top: '100%', right: 0, marginTop: 4,
                    background: '#fff', border: '1px solid #d1d5db', borderRadius: 6,
                    boxShadow: '0 8px 24px rgba(0,0,0,0.12)', zIndex: 100,
                    minWidth: 160, display: 'flex', flexDirection: 'column',
                    padding: '4px 0'
                }}>
                    {validOptions.map((opt, i) => (
                        <button
                            key={i}
                            disabled={opt.disabled}
                            title={opt.disabled ? opt.disabledReason : undefined}
                            onClick={() => {
                                if (!opt.disabled) {
                                    opt.onClick();
                                    setOpen(false);
                                }
                            }}
                            style={{
                                display: 'flex', alignItems: 'center', gap: 8, padding: '8px 12px',
                                border: 'none', background: 'transparent', textAlign: 'left',
                                cursor: opt.disabled ? 'not-allowed' : 'pointer', fontSize: '0.78rem',
                                color: opt.disabled ? '#9ca3af' : (opt.color || 'var(--color-text)'),
                                width: '100%', transition: 'background 0.1s',
                                opacity: opt.disabled ? 0.6 : 1
                            }}
                            onMouseOver={(e) => { if (!opt.disabled) e.currentTarget.style.backgroundColor = '#f3f4f6'; }}
                            onMouseOut={(e) => { if (!opt.disabled) e.currentTarget.style.backgroundColor = 'transparent'; }}
                        >
                            {opt.icon && <span style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: 14 }}>{opt.icon}</span>}
                            {opt.label}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}
