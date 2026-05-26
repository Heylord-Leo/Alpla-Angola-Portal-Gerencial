import React, { useState, useEffect, useRef } from 'react';
import { api } from '../lib/api';
import { ChevronDown, Search } from 'lucide-react';
import { useDropdownPosition } from '../hooks/useDropdownPosition';
import { DropdownPortal } from './ui/DropdownPortal';

interface EmployeeAutocompleteProps {
    initialName?: string;
    initialCode?: string;
    onChange: (id: string | null, name: string, code?: string) => void;
    placeholder?: string;
    disabled?: boolean;
    hasError?: boolean;
    className?: string;
    name?: string;
}

const COL_WIDTHS = '100px 1fr 120px';

export function EmployeeAutocomplete({
    initialName = '',
    initialCode = '',
    onChange,
    placeholder = 'Selecionar funcionário...',
    disabled = false,
    hasError = false,
    className,
    name = 'EmployeeId'
}: EmployeeAutocompleteProps) {
    const getInitialDisplay = () => {
        if (!initialName) return '';
        return initialCode ? `[${initialCode}] ${initialName}` : initialName;
    };

    const [selectedDisplay, setSelectedDisplay] = useState(getInitialDisplay());
    const [searchTerm, setSearchTerm] = useState('');
    const [results, setResults] = useState<any[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isOpen, setIsOpen] = useState(false);
    const [hoveredId, setHoveredId] = useState<string | null>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    const panelRef = useRef<HTMLDivElement>(null);
    const searchInputRef = useRef<HTMLInputElement>(null);
    const debounceTimer = useRef<any>();

    const dropdownStyle = useDropdownPosition(containerRef, isOpen, 300, 480);

    useEffect(() => {
        setSelectedDisplay(getInitialDisplay());
    }, [initialName, initialCode]);

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
            // Respecting scoped visibility. getEmployees in backend filters by ManagerUserId / Dept
            const res = await api.hrLeave.getEmployees({ search: term, active: true, pageSize: 20 });
            setResults(res.items);
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

    const handleSelect = (e: any) => {
        const displayName = `[${e.employeeCode}] ${e.fullName}`;
        setSelectedDisplay(displayName);
        setIsOpen(false);
        setHoveredId(null);
        onChange(e.id, e.fullName, e.employeeCode);
    };

    const clearSelection = (e: React.MouseEvent) => {
        e.stopPropagation();
        setSelectedDisplay('');
        onChange(null, '', '');
        if (isOpen) {
            setSearchTerm('');
            performSearch('');
        }
    };

    // ─── Styles ────────────────────────────────────────────────────────────────

    const triggerStyle: React.CSSProperties = {
        width: '100%',
        padding: '12px 14px',
        border: `1px solid ${hasError ? '#EF4444' : 'var(--color-border)'}`,
        boxShadow: hasError ? '0 0 0 3px rgba(239,68,68,0.25)' : 'var(--shadow-md)',
        fontSize: '0.875rem',
        color: selectedDisplay ? 'var(--color-text-main)' : 'var(--color-placeholder)',
        backgroundColor: hasError ? '#FEF2F2' : disabled ? 'var(--color-field-disabled-bg)' : '#ffffff',
        cursor: disabled ? 'not-allowed' : 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        opacity: disabled ? 0.7 : 1,
        minHeight: '46px',
        transition: 'all 0.15s ease-out',
        userSelect: 'none',
        borderRadius: '6px',
    };

    const panelStyle: React.CSSProperties = {
        ...(dropdownStyle || {}),
        backgroundColor: 'var(--color-bg-surface)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-md)',
        overflow: 'hidden',
        minWidth: 'max(100%, 350px)',
        maxWidth: '90vw',
        display: 'flex',
        flexDirection: 'column',
        borderRadius: '6px',
        zIndex: 10000,
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
        borderRadius: '6px',
    };

    const gridStyle: React.CSSProperties = {
        display: 'grid',
        gridTemplateColumns: COL_WIDTHS,
        width: '100%',
    };

    const headerStyle: React.CSSProperties = {
        ...gridStyle,
        backgroundColor: 'var(--color-bg-page)', 
        borderBottom: '1px solid var(--color-border)',
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

    const getRowStyle = (id: string): React.CSSProperties => ({
        ...gridStyle,
        cursor: 'pointer',
        borderBottom: '1px solid #f3f4f6',
        backgroundColor: hoveredId === id ? '#f3f4f6' : '#ffffff',
        transition: 'background-color 0.1s ease',
    });

    const getCellStyle = (_id: string, extra?: React.CSSProperties): React.CSSProperties => ({
        padding: '12px 10px',
        fontSize: '0.825rem',
        color: 'var(--color-text-main)',
        display: 'flex',
        alignItems: 'center',
        overflow: 'hidden',
        ...extra,
    });

    const getPortalCellStyle = (id: string): React.CSSProperties => ({
        ...getCellStyle(id),
        borderRight: `1px solid ${hoveredId === id ? '#e5e7eb' : '#f3f4f6'}`,
        fontFamily: 'monospace',
        fontWeight: 700,
        color: 'var(--color-primary)',
        fontSize: '0.75rem',
    });

    const getNameStyle = (id: string): React.CSSProperties => ({
        ...getCellStyle(id),
        fontWeight: 600,
        textTransform: 'uppercase',
        letterSpacing: '0.01em',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        borderRight: `1px solid ${hoveredId === id ? '#e5e7eb' : '#f3f4f6'}`,
    });

    const getDeptStyle = (id: string): React.CSSProperties => ({
        ...getCellStyle(id),
        fontSize: '0.75rem',
        color: 'var(--color-text-muted)',
    });

    return (
        <div className={className} ref={containerRef} style={{ position: 'relative' }}>
            <div 
                onClick={toggleDropdown} 
                onKeyDown={handleKeyDown}
                style={triggerStyle} 
                tabIndex={disabled ? -1 : 0}
                data-field={name}
            >
                <span style={{ 
                    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: '8px', 
                    fontWeight: 600, textTransform: 'none', letterSpacing: '0.01em', fontSize: '0.85rem',
                    color: selectedDisplay ? 'var(--color-text-main)' : 'var(--color-placeholder)'
                }}>
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

            {isOpen && (
                <DropdownPortal>
                    <div style={panelStyle} ref={panelRef}>
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

                        <div style={headerStyle}>
                            <div style={headerCellBorderStyle}>Matrícula</div>
                            <div style={headerCellBorderStyle}>Nome</div>
                            <div style={headerCellStyle}>Departamento</div>
                        </div>

                        <div style={{ maxHeight: '280px', overflowY: 'auto', backgroundColor: 'var(--color-bg-surface)' }}>
                            {isLoading ? (
                                <div style={{ padding: '32px', textAlign: 'center' }}>
                                    <div style={{ display: 'inline-block', width: '24px', height: '24px', border: '3px solid #e5e7eb', borderTopColor: '#111827', borderRadius: '50%', animation: 'spin 0.8s linear infinite' }} />
                                </div>
                            ) : results.length === 0 ? (
                                <div style={{ padding: '24px', textAlign: 'center', color: '#9ca3af', fontSize: '0.875rem', fontStyle: 'italic' }}>
                                    Nenhum funcionário encontrado.
                                </div>
                            ) : (
                                results.map((emp) => (
                                    <div
                                        key={emp.id}
                                        style={getRowStyle(emp.id)}
                                        onMouseEnter={() => setHoveredId(emp.id)}
                                        onMouseLeave={() => setHoveredId(null)}
                                        onMouseDown={(e) => {
                                            e.preventDefault();
                                            handleSelect(emp);
                                        }}
                                    >
                                        <div style={getPortalCellStyle(emp.id)}>{emp.employeeCode}</div>
                                        <div style={getNameStyle(emp.id)}>{emp.fullName}</div>
                                        <div style={getDeptStyle(emp.id)}>{emp.portalDepartmentName || emp.innuxDepartmentName || '—'}</div>
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
