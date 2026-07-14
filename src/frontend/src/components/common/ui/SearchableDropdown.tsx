import React, { useState, useEffect, useRef } from 'react';
import { ChevronDown, Search } from 'lucide-react';
import { useDropdownPosition } from '../../../hooks/useDropdownPosition';
import { DropdownPortal } from '../../ui/DropdownPortal';

export interface SearchableDropdownProps<T> {
    value: T | null;
    onChange: (item: T | null) => void;
    onSearch: (term: string) => Promise<T[]>;
    getDisplayValue: (item: T | null) => string;
    renderItem: (item: T, isHovered: boolean) => React.ReactNode;
    renderHeader?: () => React.ReactNode;
    placeholder?: string;
    searchPlaceholder?: string;
    disabled?: boolean;
    hasError?: boolean;
    hasWarning?: boolean;
    isUnresolved?: boolean;
    unresolvedMessage?: string;
    className?: string;
    name?: string;
    dropdownWidth?: number | string;
    minDropdownWidth?: number | string;
    clearable?: boolean;
}

export function SearchableDropdown<T>({
    value,
    onChange,
    onSearch,
    getDisplayValue,
    renderItem,
    renderHeader,
    placeholder = 'Selecione...',
    searchPlaceholder = 'Pesquisar...',
    disabled = false,
    hasError = false,
    hasWarning = false,
    isUnresolved = false,
    unresolvedMessage = ' (SUGESTÃO OCR - NÃO ENCONTRADO)',
    className,
    name,
    dropdownWidth,
    minDropdownWidth = '300px',
    clearable = true
}: SearchableDropdownProps<T>) {
    const [searchTerm, setSearchTerm] = useState('');
    const [results, setResults] = useState<T[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isOpen, setIsOpen] = useState(false);
    const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    const panelRef = useRef<HTMLDivElement>(null);
    const searchInputRef = useRef<HTMLInputElement>(null);
    const debounceTimer = useRef<any>();

    const dropdownStyle = useDropdownPosition(containerRef, isOpen, 300, 480);
    const selectedDisplay = getDisplayValue(value);

    useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            const isOutsideContainer = containerRef.current && !containerRef.current.contains(event.target as Node);
            const isOutsidePanel = panelRef.current && !panelRef.current.contains(event.target as Node);
            
            if (isOutsideContainer && isOutsidePanel) {
                setIsOpen(false);
            }
        }
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    useEffect(() => {
        if (isOpen && searchInputRef.current) {
            setTimeout(() => searchInputRef.current?.focus(), 50);
        }
    }, [isOpen]);

    const performSearch = async (term: string) => {
        setIsLoading(true);
        try {
            const data = await onSearch(term);
            setResults(data);
        } catch (error) {
            console.error('Search failed', error);
            setResults([]);
        } finally {
            setIsLoading(false);
        }
    };

    const toggleDropdown = () => {
        if (disabled) return;
        if (!isOpen) {
            setSearchTerm('');
            performSearch('');
        }
        setIsOpen(!isOpen);
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        if (disabled) return;
        if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') {
            e.preventDefault();
            toggleDropdown();
        }
    };

    const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const val = e.target.value;
        setSearchTerm(val);
        if (debounceTimer.current) clearTimeout(debounceTimer.current);
        debounceTimer.current = setTimeout(() => performSearch(val), 300);
    };

    const handleSelect = (item: T) => {
        setIsOpen(false);
        setHoveredIndex(null);
        onChange(item);
    };

    const clearSelection = (e: React.MouseEvent) => {
        e.stopPropagation();
        onChange(null);
        if (isOpen) {
            setSearchTerm('');
            performSearch('');
        }
    };

    // ─── Styles ────────────────────────────────────────────────────────────────

    const triggerStyle: React.CSSProperties = {
        width: '100%',
        padding: '12px 14px',
        border: `1px solid ${hasError ? '#EF4444' : hasWarning ? '#d97706' : 'var(--color-border)'}`,
        boxShadow: hasError ? '0 0 0 3px rgba(239,68,68,0.25)' : hasWarning ? '0 0 0 3px rgba(245,158,11,0.25)' : 'var(--shadow-md)',
        fontSize: '0.875rem',
        color: selectedDisplay ? 'var(--color-text-main)' : 'var(--color-placeholder)',
        backgroundColor: hasError ? '#FEF2F2' : hasWarning ? '#fffbeb' : disabled ? 'var(--color-field-disabled-bg)' : '#ffffff',
        cursor: disabled ? 'not-allowed' : 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        opacity: disabled ? 0.7 : 1,
        minHeight: '48px',
        transition: 'all 0.15s ease-out',
        userSelect: 'none',
        borderRadius: '6px' // standard
    };

    const panelStyle: React.CSSProperties = {
        ...(dropdownStyle || {}),
        backgroundColor: 'var(--color-bg-surface)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-md)',
        overflow: 'hidden',
        minWidth: minDropdownWidth,
        width: dropdownWidth || (containerRef.current ? containerRef.current.offsetWidth : 'auto'),
        display: 'flex',
        flexDirection: 'column',
        zIndex: 10000,
        borderRadius: '8px'
    };

    const searchAreaStyle: React.CSSProperties = {
        padding: '10px',
        borderBottom: '1px solid var(--color-border)',
        backgroundColor: 'var(--color-bg-page)',
        position: 'relative',
    };

    const searchInputStyle: React.CSSProperties = {
        width: '100%',
        padding: '8px 12px 8px 36px',
        border: '1px solid var(--color-border)',
        fontSize: '0.875rem',
        outline: 'none',
        backgroundColor: 'var(--color-bg-surface)',
        fontFamily: 'inherit',
        boxSizing: 'border-box',
        borderRadius: '6px'
    };

    return (
        <div className={className} ref={containerRef} style={{ position: 'relative' }}>
            {/* ── Trigger (Closed State) ── */}
            <div 
                onClick={toggleDropdown} 
                onKeyDown={handleKeyDown}
                style={triggerStyle} 
                tabIndex={disabled ? -1 : 0}
                data-field={name}
            >
                <span style={{ 
                    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: '8px', 
                    fontWeight: selectedDisplay ? 600 : 400, textTransform: 'none', letterSpacing: '0.01em', fontSize: '0.85rem',
                    fontStyle: isUnresolved ? 'italic' : 'normal',
                    color: isUnresolved ? '#9a3412' : selectedDisplay ? 'var(--color-text-main)' : 'var(--color-placeholder)'
                }}>
                    {selectedDisplay || placeholder}
                    {isUnresolved && selectedDisplay && unresolvedMessage}
                </span>
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                    {clearable && selectedDisplay && !disabled && (
                        <button
                            type="button"
                            onMouseDown={(e) => { e.stopPropagation(); clearSelection(e as any); }}
                            style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#9ca3af', fontSize: '1.2rem', lineHeight: 1, padding: '2px 4px' }}
                            title="Limpar"
                        >
                            &times;
                        </button>
                    )}
                    <ChevronDown size={18} style={{ color: '#111827', transition: 'transform 0.2s', transform: isOpen ? 'rotate(180deg)' : 'rotate(0deg)' }} />
                </div>
            </div>

            {/* ── Dropdown Panel (Open State via Portal) ── */}
            {isOpen && (
                <DropdownPortal>
                    <div style={panelStyle} ref={panelRef}>
                        {/* Search Box */}
                        <div style={searchAreaStyle}>
                            <Search size={15} style={{ position: 'absolute', left: '22px', top: '50%', transform: 'translateY(-50%)', color: '#6b7280', pointerEvents: 'none' }} />
                            <input
                                ref={searchInputRef}
                                type="text"
                                value={searchTerm}
                                onChange={handleSearchChange}
                                placeholder={searchPlaceholder}
                                style={searchInputStyle}
                            />
                        </div>

                        {/* Custom Header (Optional) */}
                        {renderHeader && renderHeader()}

                        {/* Result Rows */}
                        <div style={{ maxHeight: '280px', overflowY: 'auto', backgroundColor: 'var(--color-bg-surface)' }}>
                            {isLoading ? (
                                <div style={{ padding: '32px', textAlign: 'center' }}>
                                    <div style={{ display: 'inline-block', width: '24px', height: '24px', border: '3px solid #e5e7eb', borderTopColor: '#111827', borderRadius: '50%', animation: 'spin 0.8s linear infinite' }} />
                                </div>
                            ) : results.length === 0 ? (
                                <div style={{ padding: '24px', textAlign: 'center', color: '#9ca3af', fontSize: '0.875rem', fontStyle: 'italic' }}>
                                    Nenhum resultado encontrado.
                                </div>
                            ) : (
                                results.map((item, index) => (
                                    <div
                                        key={index}
                                        onMouseEnter={() => setHoveredIndex(index)}
                                        onMouseLeave={() => setHoveredIndex(null)}
                                        onMouseDown={(e) => {
                                            e.preventDefault();
                                            handleSelect(item);
                                        }}
                                        style={{
                                            cursor: 'pointer',
                                            borderBottom: '1px solid #f3f4f6',
                                            backgroundColor: hoveredIndex === index ? '#f3f4f6' : '#ffffff',
                                            transition: 'background-color 0.1s ease',
                                        }}
                                    >
                                        {renderItem(item, hoveredIndex === index)}
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                </DropdownPortal>
            )}
        </div>
    );
}
