import React, { useState, useRef, useEffect } from 'react';
import { Filter, ChevronDown, Check } from 'lucide-react';
import { DropdownPortal } from './DropdownPortal';
import { Z_INDEX } from '../../constants/ui';

export interface FilterOption {
    id: string | number;
    name: string;
}

export interface FilterGroup {
    name: string;
    options: FilterOption[];
}

interface FilterDropdownProps {
    label: string;
    groups?: FilterGroup[];
    options?: FilterOption[]; // If no groups
    selectedIds: string[];
    onChange: (newIds: string[]) => void;
    icon?: React.ReactNode;
}

export function FilterDropdown({ label, groups, options, selectedIds, onChange, icon }: FilterDropdownProps) {
    const [isOpen, setIsOpen] = useState(false);
    const triggerRef = useRef<HTMLButtonElement>(null);
    const [dropdownStyles, setDropdownStyles] = useState<React.CSSProperties>({});
    const popoverRef = useRef<HTMLDivElement>(null);

    const toggleOpen = () => setIsOpen(!isOpen);

    useEffect(() => {
        if (isOpen && triggerRef.current) {
            const rect = triggerRef.current.getBoundingClientRect();
            setDropdownStyles({
                position: 'fixed',
                top: `${rect.bottom + 8}px`,
                left: `${rect.left}px`,
                width: '300px', // Fixed width or match trigger if needed
                maxHeight: '400px',
                overflowY: 'auto',
                backgroundColor: 'var(--color-bg-surface)',
                border: '2px solid var(--color-primary)',
                boxShadow: 'var(--shadow-brutal)',
                display: 'flex',
                flexDirection: 'column',
                zIndex: Z_INDEX.DROPDOWN as any
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

    const handleToggleOption = (id: string | number) => {
        const strId = String(id);
        const newSelection = selectedIds.includes(strId)
            ? selectedIds.filter(v => v !== strId)
            : [...selectedIds, strId];
        onChange(newSelection);
    };

    const hasSelection = selectedIds.length > 0;

    const renderOptions = (optList: FilterOption[]) => (
        optList.map(opt => (
            <label key={opt.id} style={{
                display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', 
                cursor: 'pointer', transition: 'background-color 0.1s'
            }}
            onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
            onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
            >
                <input 
                    type="checkbox" 
                    checked={selectedIds.includes(String(opt.id))} 
                    onChange={() => handleToggleOption(opt.id)}
                    style={{ position: 'absolute', opacity: 0, width: 0, height: 0 }} 
                />
                <div style={{
                    width: '18px', height: '18px', border: '2px solid var(--color-primary)', 
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    backgroundColor: selectedIds.includes(String(opt.id)) ? 'var(--color-primary)' : 'var(--color-bg-surface)'
                }}>
                    {selectedIds.includes(String(opt.id)) && <Check size={14} color="var(--color-bg-surface)" strokeWidth={3} />}
                </div>
                <span style={{ fontWeight: 600, fontSize: '0.9rem', color: 'var(--color-text-main)' }}>{opt.name}</span>
            </label>
        ))
    );

    return (
        <>
            <button
                ref={triggerRef}
                onClick={toggleOpen}
                style={{
                    display: 'flex', alignItems: 'center', gap: '8px',
                    padding: '8px 16px', backgroundColor: 'var(--color-bg-surface)',
                    border: `2px solid ${hasSelection ? 'var(--color-primary)' : 'var(--color-primary)'}`,
                    cursor: 'pointer', color: 'var(--color-primary)',
                    fontWeight: 700, textTransform: 'uppercase', fontSize: '0.85rem',
                    position: 'relative' // for pseudo elements if needed
                }}
            >
                {icon || <Filter size={16} strokeWidth={2.5} />}
                {label} {hasSelection && `(${selectedIds.length})`}
                <ChevronDown size={16} strokeWidth={2.5} style={{ 
                    marginLeft: '8px', 
                    transform: isOpen ? 'rotate(180deg)' : 'none', 
                    transition: 'transform 0.2s' 
                }} />
            </button>

            {isOpen && (
                <DropdownPortal>
                    <div ref={popoverRef} style={dropdownStyles}>
                        {groups ? (
                            groups.map((group, idx) => (
                                <div key={group.name} style={{
                                    borderBottom: idx < groups.length - 1 ? '2px solid var(--color-border)' : 'none'
                                }}>
                                    <div style={{
                                        padding: '8px 16px', backgroundColor: 'var(--color-bg-page)',
                                        color: 'var(--color-text-muted)', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase'
                                    }}>
                                        {group.name}
                                    </div>
                                    <div style={{ padding: '8px 0' }}>
                                        {renderOptions(group.options)}
                                    </div>
                                </div>
                            ))
                        ) : (
                            <div style={{ padding: '8px 0' }}>
                                {options && renderOptions(options)}
                            </div>
                        )}
                        {(!groups && !options?.length) && (
                            <div style={{ padding: '16px', color: 'var(--color-text-muted)', fontSize: '0.85rem', textAlign: 'center' }}>
                                Nenhum filtro disponível
                            </div>
                        )}
                    </div>
                </DropdownPortal>
            )}
        </>
    );
}
