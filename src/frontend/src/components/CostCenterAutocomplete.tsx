import React, { useState, useEffect, useRef } from 'react';
import { api } from '../lib/api';
import { ChevronDown, Search } from 'lucide-react';
import { useDropdownPosition } from '../hooks/useDropdownPosition';
import { DropdownPortal } from './ui/DropdownPortal';

interface CostCenterAutocompleteProps {
    initialId?: number | null;
    initialName?: string;
    initialCode?: string;
    onChange: (id: number | null, name: string) => void;
    placeholder?: string;
    disabled?: boolean;
    hasError?: boolean;
    className?: string;
}

// Column widths for the tabular layout — must match exactly in header and rows
const COL_WIDTHS = '110px 1fr';

export function CostCenterAutocomplete({
    initialId = null,
    initialName = '',
    initialCode = '',
    onChange,
    placeholder = 'Selecionar centro de custo...',
    disabled = false,
    hasError = false,
    className
}: CostCenterAutocompleteProps) {
    const getInitialDisplay = () => {
        if (!initialName) return '';
        return initialCode ? `[${initialCode}] ${initialName}` : initialName;
    };

    const [selectedDisplay, setSelectedDisplay] = useState(getInitialDisplay());
    const [searchTerm, setSearchTerm] = useState('');
    const [results, setResults] = useState<any[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isOpen, setIsOpen] = useState(false);
    const [hoveredId, setHoveredId] = useState<number | null>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    const panelRef = useRef<HTMLDivElement>(null);
    const searchInputRef = useRef<HTMLInputElement>(null);
    const debounceTimer = useRef<any>();

    const dropdownStyle = useDropdownPosition(containerRef, isOpen);

    useEffect(() => {
        setSelectedDisplay(getInitialDisplay());
    }, [initialName, initialCode, initialId]);

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
            // Small delay to ensure panel is rendered
            setTimeout(() => searchInputRef.current?.focus(), 50);
        }
    }, [isOpen]);

    const performSearch = async (term: string) => {
        setIsLoading(true);
        try {
            const data = await api.lookups.searchCostCenters(term);
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

    const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const val = e.target.value;
        setSearchTerm(val);
        if (debounceTimer.current) clearTimeout(debounceTimer.current);
        debounceTimer.current = setTimeout(() => performSearch(val), 300);
    };

    const handleSelect = (s: any) => {
        const displayName = `[${s.code}] ${s.name}`;
        setSelectedDisplay(displayName);
        setIsOpen(false);
        setHoveredId(null);
        onChange(s.id, s.name);
    };

    const clearSelection = (e: React.MouseEvent) => {
        e.stopPropagation();
        setSelectedDisplay('');
        onChange(null, '');
        if (isOpen) {
            setSearchTerm('');
            performSearch('');
        }
    };

    // ─── Styles ────────────────────────────────────────────────────────────────

    const triggerStyle: React.CSSProperties = {
        width: '100%',
        padding: '12px 14px',
        border: `2px solid ${hasError ? '#EF4444' : 'var(--color-border-heavy)'}`,
        boxShadow: hasError ? '6px 6px 0px #EF4444' : 'var(--shadow-brutal)',
        fontSize: '0.875rem',
        color: selectedDisplay ? 'var(--color-text-main)' : 'var(--color-text-muted)',
        backgroundColor: hasError ? '#FEF2F2' : disabled ? '#f9fafb' : '#ffffff',
        cursor: disabled ? 'not-allowed' : 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        opacity: disabled ? 0.7 : 1,
        minHeight: '48px',
        transition: 'all 0.15s ease-out',
        userSelect: 'none',
    };

    const panelStyle: React.CSSProperties = {
        ...(dropdownStyle || {}),
        backgroundColor: '#ffffff',
        border: '2px solid var(--color-border-heavy)',
        boxShadow: 'var(--shadow-brutal)',
        overflow: 'hidden',
        minWidth: '400px',
        display: 'flex',
        flexDirection: 'column',
    };

    const searchAreaStyle: React.CSSProperties = {
        padding: '10px',
        borderBottom: '2px solid var(--color-border-heavy)',
        backgroundColor: '#f9fafb',
        position: 'relative',
    };

    const searchInputStyle: React.CSSProperties = {
        width: '100%',
        padding: '8px 12px 8px 36px',
        border: '2px solid var(--color-border-heavy)',
        fontSize: '0.875rem',
        outline: 'none',
        backgroundColor: '#ffffff',
        fontFamily: 'inherit',
        boxSizing: 'border-box',
    };

    // Header and row grid: both use the same gridTemplateColumns
    const gridStyle: React.CSSProperties = {
        display: 'grid',
        gridTemplateColumns: COL_WIDTHS,
        width: '100%',
    };

    const headerStyle: React.CSSProperties = {
        ...gridStyle,
        backgroundColor: '#f3f4f6', // Light gray standard
        borderBottom: '2px solid var(--color-border-heavy)',
    };

    const headerCellStyle: React.CSSProperties = {
        padding: '8px 10px',
        fontSize: '0.65rem',
        fontWeight: 700,
        letterSpacing: '0.08em',
        textTransform: 'uppercase',
        color: 'var(--color-text-muted)',
    };

    const headerCellBorderStyle: React.CSSProperties = {
        ...headerCellStyle,
        borderRight: '1px solid var(--color-border)',
    };

    const getRowStyle = (id: number): React.CSSProperties => ({
        ...gridStyle,
        cursor: 'pointer',
        borderBottom: '1px solid #f3f4f6',
        backgroundColor: hoveredId === id ? '#f3f4f6' : '#ffffff', // Subtle gray hover
        transition: 'background-color 0.1s ease',
    });

    const getCellStyle = (_id: number, extra?: React.CSSProperties): React.CSSProperties => ({
        padding: '12px 10px',
        fontSize: '0.825rem',
        color: 'var(--color-text-main)',
        display: 'flex',
        alignItems: 'center',
        overflow: 'hidden',
        ...extra,
    });

    const getPortalCellStyle = (id: number): React.CSSProperties => ({
        ...getCellStyle(id),
        borderRight: `1px solid ${hoveredId === id ? '#e5e7eb' : '#f3f4f6'}`,
        fontFamily: 'monospace',
        fontWeight: 700,
        color: 'var(--color-primary)',
        fontSize: '0.75rem',
    });


    const getNameStyle = (id: number): React.CSSProperties => ({
        ...getCellStyle(id),
        fontWeight: 600,
        textTransform: 'uppercase',
        letterSpacing: '0.01em',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
    });

    return (
        <div className={className} ref={containerRef} style={{ position: 'relative', marginTop: '8px', zIndex: isOpen ? 5 : 1 }}>

            {/* ── Trigger (Closed State) ── */}
            <div onClick={toggleDropdown} style={triggerStyle}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: '8px', fontWeight: 500, textTransform: 'uppercase', letterSpacing: '0.02em', fontSize: '0.85rem' }}>
                    {selectedDisplay || placeholder}
                </span>
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                    {selectedDisplay && !disabled && (
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
                                placeholder="Pesquisar por nome ou código..."
                                style={searchInputStyle}
                            />
                        </div>

                        {/* Column Header */}
                        <div style={headerStyle}>
                            <div style={headerCellBorderStyle}>Código</div>
                            <div style={headerCellStyle}>Descrição</div>
                        </div>

                        {/* Result Rows */}
                        <div style={{ maxHeight: '280px', overflowY: 'auto', backgroundColor: '#ffffff' }}>
                            {isLoading ? (
                                <div style={{ padding: '32px', textAlign: 'center' }}>
                                    <div style={{ display: 'inline-block', width: '24px', height: '24px', border: '3px solid #e5e7eb', borderTopColor: '#111827', borderRadius: '50%', animation: 'spin 0.8s linear infinite' }} />
                                </div>
                            ) : results.length === 0 ? (
                                <div style={{ padding: '24px', textAlign: 'center', color: '#9ca3af', fontSize: '0.875rem', fontStyle: 'italic' }}>
                                    Nenhum centro de custo encontrado.
                                </div>
                            ) : (
                                results.map((s) => (
                                    <div
                                        key={s.id}
                                        style={getRowStyle(s.id)}
                                        onMouseEnter={() => setHoveredId(s.id)}
                                        onMouseLeave={() => setHoveredId(null)}
                                        onMouseDown={(e) => {
                                            e.preventDefault(); // Prevent blur before select
                                            handleSelect(s);
                                        }}
                                    >
                                        <div style={getPortalCellStyle(s.id)}>{s.code}</div>
                                        <div style={getNameStyle(s.id)}>{s.name}</div>
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
