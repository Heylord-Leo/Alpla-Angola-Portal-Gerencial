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
            const spaceBelow = window.innerHeight - rect.bottom;
            const spaceAbove = rect.top;
            const dropdownMaxHeight = 400;
            
            const shouldOpenUpwards = spaceBelow < dropdownMaxHeight && spaceAbove > spaceBelow;

            setDropdownStyles({
                position: 'fixed',
                ...(shouldOpenUpwards ? { bottom: `${window.innerHeight - rect.top + 8}px` } : { top: `${rect.bottom + 8}px` }),
                left: `${rect.left}px`,
                width: '300px', // Fixed width or match trigger if needed
                maxHeight: `${shouldOpenUpwards ? Math.min(spaceAbove - 16, dropdownMaxHeight) : Math.min(spaceBelow - 16, dropdownMaxHeight)}px`,
                overflowY: 'auto',
                backgroundColor: '#ffffff',
                border: '1px solid #e2e8f0',
                borderRadius: '8px',
                boxShadow: '0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)',
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
            onMouseEnter={(e) => e.currentTarget.style.backgroundColor = '#f8fafc'}
            onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
            >
                <input 
                    type="checkbox" 
                    checked={selectedIds.includes(String(opt.id))} 
                    onChange={() => handleToggleOption(opt.id)}
                    style={{ position: 'absolute', opacity: 0, width: 0, height: 0 }} 
                />
                <div style={{
                    width: '16px', height: '16px', 
                    border: `1px solid ${selectedIds.includes(String(opt.id)) ? '#2563eb' : '#cbd5e1'}`, 
                    borderRadius: '4px',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    backgroundColor: selectedIds.includes(String(opt.id)) ? '#2563eb' : '#ffffff',
                    transition: 'all 0.15s ease'
                }}>
                    {selectedIds.includes(String(opt.id)) && <Check size={12} color="#ffffff" strokeWidth={3} />}
                </div>
                <span style={{ fontWeight: 500, fontSize: '0.85rem', color: '#1e293b' }}>{opt.name}</span>
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
                    padding: '6px 12px',
                    backgroundColor: '#ffffff',
                    border: `1px solid ${(isOpen || hasSelection) ? 'var(--color-primary)' : '#cbd5e1'}`,
                    borderRadius: '6px',
                    cursor: 'pointer', 
                    color: (isOpen || hasSelection) ? 'var(--color-primary)' : '#1e293b',
                    fontWeight: 700, 
                    textTransform: 'uppercase', 
                    fontSize: '0.75rem',
                    position: 'relative',
                    transition: 'all 0.15s ease',
                    boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)'
                }}
                onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = '#f8fafc'; }}
                onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = '#ffffff'; }}
            >
                {icon || <Filter size={14} color={(isOpen || hasSelection) ? 'var(--color-primary)' : '#64748b'} strokeWidth={2} />}
                {label} {hasSelection && `(${selectedIds.length})`}
                <ChevronDown size={14} color={(isOpen || hasSelection) ? 'var(--color-primary)' : '#64748b'} strokeWidth={2} style={{ 
                    marginLeft: '4px', 
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
                                    borderBottom: idx < groups.length - 1 ? '1px solid #e2e8f0' : 'none'
                                }}>
                                    <div style={{
                                        padding: '8px 16px', backgroundColor: '#f8fafc',
                                        color: '#64748b', fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase'
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
