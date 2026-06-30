import React, { useState, useEffect, useRef, useMemo } from 'react';
import { ChevronDown, Search, X } from 'lucide-react';
import { useDropdownPosition } from '../../../hooks/useDropdownPosition';
import { DropdownPortal } from '../../ui/DropdownPortal';
import { labelStyle } from './FormFieldWrapper';

export interface FormSearchableSelectOption {
    value: string;
    label: string;
}

export interface FormSearchableSelectProps {
    label: string;
    value: string;
    onChange: (value: string) => void;
    options: FormSearchableSelectOption[];
    placeholder?: string;
    searchPlaceholder?: string;
    disabled?: boolean;
    error?: string;
    style?: React.CSSProperties;
    /** If true, adds " *" to the label */
    required?: boolean;
}

/**
 * A form-integrated searchable select dropdown for static option lists.
 * Uses portal-based rendering (never clipped) with max-height scroll.
 * 
 * Use this instead of FormSelect when options > 8 or the list is expected to grow.
 * Uses the same visual language as SearchableDropdown but with client-side filtering.
 */
export function FormSearchableSelect({
    label,
    value,
    onChange,
    options,
    placeholder = 'Selecione...',
    searchPlaceholder = 'Pesquisar...',
    disabled = false,
    error,
    style,
    required = false,
}: FormSearchableSelectProps) {
    const [isOpen, setIsOpen] = useState(false);
    const [searchTerm, setSearchTerm] = useState('');
    const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    const panelRef = useRef<HTMLDivElement>(null);
    const searchInputRef = useRef<HTMLInputElement>(null);

    const dropdownStyle = useDropdownPosition(containerRef, isOpen, 300, 480);

    // Client-side filter
    const filteredOptions = useMemo(() => {
        if (!searchTerm.trim()) return options;
        const term = searchTerm.toLowerCase();
        return options.filter(opt => opt.label.toLowerCase().includes(term));
    }, [options, searchTerm]);

    const selectedLabel = options.find(opt => opt.value === value)?.label || '';

    // Close on click outside
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

    // Focus search input when opened
    useEffect(() => {
        if (isOpen && searchInputRef.current) {
            setTimeout(() => searchInputRef.current?.focus(), 50);
        }
    }, [isOpen]);

    const toggleDropdown = () => {
        if (disabled) return;
        if (!isOpen) {
            setSearchTerm('');
            setHoveredIndex(null);
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

    const handleSelect = (opt: FormSearchableSelectOption) => {
        onChange(opt.value);
        setIsOpen(false);
        setHoveredIndex(null);
    };

    const handleClear = (e: React.MouseEvent) => {
        e.stopPropagation();
        onChange('');
    };

    // ─── Styles ─────────────────────────────────────────────────
    const triggerStyle: React.CSSProperties = {
        width: '100%',
        padding: '10px 14px',
        border: `1px solid ${error ? '#ef4444' : 'var(--color-border)'}`,
        borderRadius: '6px',
        backgroundColor: disabled ? 'var(--color-field-disabled-bg, #f9fafb)' : 'var(--color-bg-surface)',
        color: selectedLabel ? 'var(--color-text-main)' : 'var(--color-text-muted)',
        fontSize: '0.85rem',
        cursor: disabled ? 'not-allowed' : 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        opacity: disabled ? 0.6 : 1,
        minHeight: '40px',
        transition: 'all 0.15s ease-out',
        userSelect: 'none',
        boxSizing: 'border-box',
    };

    const panelStyleFinal: React.CSSProperties = {
        ...(dropdownStyle || {}),
        backgroundColor: 'var(--color-bg-surface)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-md)',
        overflow: 'hidden',
        width: containerRef.current ? containerRef.current.offsetWidth : 'auto',
        minWidth: '220px',
        display: 'flex',
        flexDirection: 'column',
        zIndex: 10000,
        borderRadius: '8px',
    };

    return (
        <div style={{ flex: 1, ...style }}>
            {/* Label */}
            <label style={labelStyle}>
                {label}{required && ' *'}
            </label>

            {/* Trigger */}
            <div ref={containerRef} style={{ position: 'relative' }}>
                <div
                    onClick={toggleDropdown}
                    onKeyDown={handleKeyDown}
                    style={triggerStyle}
                    tabIndex={disabled ? -1 : 0}
                >
                    <span style={{
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        paddingRight: '8px',
                        fontWeight: selectedLabel ? 500 : 400,
                    }}>
                        {selectedLabel || placeholder}
                    </span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                        {selectedLabel && !disabled && (
                            <button
                                type="button"
                                onMouseDown={(e) => { e.stopPropagation(); handleClear(e as any); }}
                                style={{
                                    background: 'none', border: 'none', cursor: 'pointer',
                                    color: 'var(--color-text-muted)', fontSize: '1rem', lineHeight: 1,
                                    padding: '2px 4px', display: 'flex', alignItems: 'center',
                                }}
                                title="Limpar"
                            >
                                <X size={14} />
                            </button>
                        )}
                        <ChevronDown
                            size={16}
                            style={{
                                color: 'var(--color-text-muted)',
                                transition: 'transform 0.2s',
                                transform: isOpen ? 'rotate(180deg)' : 'rotate(0deg)',
                            }}
                        />
                    </div>
                </div>

                {/* Dropdown via portal */}
                {isOpen && (
                    <DropdownPortal>
                        <div style={panelStyleFinal} ref={panelRef}>
                            {/* Search box */}
                            {options.length > 5 && (
                                <div style={{
                                    padding: '8px',
                                    borderBottom: '1px solid var(--color-border)',
                                    backgroundColor: 'var(--color-bg-page)',
                                    position: 'relative',
                                }}>
                                    <Search size={14} style={{
                                        position: 'absolute', left: '18px', top: '50%',
                                        transform: 'translateY(-50%)', color: 'var(--color-text-muted)',
                                        pointerEvents: 'none',
                                    }} />
                                    <input
                                        ref={searchInputRef}
                                        type="text"
                                        value={searchTerm}
                                        onChange={e => setSearchTerm(e.target.value)}
                                        placeholder={searchPlaceholder}
                                        style={{
                                            width: '100%',
                                            padding: '8px 12px 8px 32px',
                                            border: '1px solid var(--color-border)',
                                            borderRadius: '6px',
                                            fontSize: '0.82rem',
                                            outline: 'none',
                                            backgroundColor: 'var(--color-bg-surface)',
                                            fontFamily: 'inherit',
                                            boxSizing: 'border-box',
                                        }}
                                    />
                                </div>
                            )}

                            {/* Options list */}
                            <div style={{ maxHeight: '280px', overflowY: 'auto', backgroundColor: 'var(--color-bg-surface)' }}>
                                {filteredOptions.length === 0 ? (
                                    <div style={{
                                        padding: '20px', textAlign: 'center',
                                        color: 'var(--color-text-muted)', fontSize: '0.82rem',
                                        fontStyle: 'italic',
                                    }}>
                                        Nenhum resultado encontrado.
                                    </div>
                                ) : (
                                    filteredOptions.map((opt, index) => {
                                        const isSelected = opt.value === value;
                                        const isHovered = hoveredIndex === index;
                                        return (
                                            <div
                                                key={opt.value}
                                                onMouseEnter={() => setHoveredIndex(index)}
                                                onMouseLeave={() => setHoveredIndex(null)}
                                                onMouseDown={(e) => {
                                                    e.preventDefault();
                                                    handleSelect(opt);
                                                }}
                                                style={{
                                                    padding: '10px 14px',
                                                    cursor: 'pointer',
                                                    borderBottom: '1px solid var(--color-border)',
                                                    backgroundColor: isSelected
                                                        ? 'rgba(var(--color-primary-rgb),0.08)'
                                                        : isHovered
                                                        ? 'var(--color-bg-page)'
                                                        : 'var(--color-bg-surface)',
                                                    color: isSelected ? 'var(--color-primary)' : 'var(--color-text-main)',
                                                    fontWeight: isSelected ? 600 : 400,
                                                    fontSize: '0.85rem',
                                                    transition: 'background-color 0.1s ease',
                                                }}
                                            >
                                                {opt.label}
                                            </div>
                                        );
                                    })
                                )}
                            </div>
                        </div>
                    </DropdownPortal>
                )}
            </div>

            {/* Error text */}
            {error && (
                <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: '4px' }}>
                    {error}
                </div>
            )}
        </div>
    );
}
