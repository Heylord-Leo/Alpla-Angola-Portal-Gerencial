import React, { useState, useRef, useEffect } from 'react';
import { MoreVertical } from 'lucide-react';
import { DropdownPortal } from './DropdownPortal';
import { Z_INDEX } from '../../constants/ui';

export interface KebabOption {
    label: string;
    onClick: () => void;
    icon?: React.ReactNode;
}

interface KebabMenuProps {
    options: KebabOption[];
}

export function KebabMenu({ options }: KebabMenuProps) {
    const [isOpen, setIsOpen] = useState(false);
    const triggerRef = useRef<HTMLButtonElement>(null);
    const [dropdownStyles, setDropdownStyles] = useState<React.CSSProperties>({});
    const popoverRef = useRef<HTMLDivElement>(null);

    const toggleOpen = (e: React.MouseEvent) => {
        e.stopPropagation();
        setIsOpen(!isOpen);
    };

    useEffect(() => {
        if (isOpen && triggerRef.current) {
            const rect = triggerRef.current.getBoundingClientRect();
            setDropdownStyles({
                position: 'fixed',
                top: `${rect.bottom + 4}px`,
                right: `${window.innerWidth - rect.right}px`,
                minWidth: '160px',
                backgroundColor: 'var(--color-bg-surface)',
                border: '2px solid var(--color-primary)',
                boxShadow: 'var(--shadow-brutal)',
                display: 'flex',
                flexDirection: 'column',
                zIndex: Z_INDEX.DROPDOWN as any,
                padding: '4px 0'
            });
        }
    }, [isOpen]);

    useEffect(() => {
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

        if (isOpen) {
            document.addEventListener('mousedown', handleClickOutside);
        }
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, [isOpen]);

    return (
        <div style={{ position: 'relative', display: 'inline-block' }}>
            <button
                ref={triggerRef}
                onClick={toggleOpen}
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
                    <div ref={popoverRef} style={dropdownStyles}>
                        {options.map((option, idx) => (
                            <button
                                key={idx}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    option.onClick();
                                    setIsOpen(false);
                                }}
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '12px',
                                    padding: '10px 16px',
                                    border: 'none',
                                    backgroundColor: 'transparent',
                                    width: '100%',
                                    textAlign: 'left',
                                    cursor: 'pointer',
                                    fontWeight: 700,
                                    fontSize: '0.85rem',
                                    textTransform: 'uppercase',
                                    color: 'var(--color-primary)',
                                    transition: 'background-color 0.1s'
                                }}
                                onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                                onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
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
