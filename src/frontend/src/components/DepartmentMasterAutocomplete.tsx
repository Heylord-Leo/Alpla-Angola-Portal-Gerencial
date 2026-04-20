import React, { useState, useEffect, useRef } from 'react';
import { api } from '../lib/api';
import { ChevronDown, Search } from 'lucide-react';
import { useDropdownPosition } from '../hooks/useDropdownPosition';
import { DropdownPortal } from './ui/DropdownPortal';

interface DepartmentMasterAutocompleteProps {
    initialId?: number | null;
    initialName?: string;
    suggestedMatchText?: string;
    onChange: (id: number | null, name: string) => void;
    placeholder?: string;
    disabled?: boolean;
    hasError?: boolean;
    className?: string;
    allowedCompanyCode?: string | null;
    plantNotSelected?: boolean;
}

const COL_WIDTHS = '70px 110px 1fr';

export function DepartmentMasterAutocomplete({
    initialId = null,
    initialName = '',
    suggestedMatchText = '',
    onChange,
    placeholder = 'Selecionar departamento mestre...',
    disabled = false,
    hasError = false,
    className,
    allowedCompanyCode,
    plantNotSelected = false
}: DepartmentMasterAutocompleteProps) {
    const [selectedDisplay, setSelectedDisplay] = useState(initialName);
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
        setSelectedDisplay(initialName);
    }, [initialName, initialId]);

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
            const data = await api.hrLeave.getDepartmentMasters(term);
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
        const displayName = s.displayName || `${s.departmentName} (${s.companyCode})`;
        setSelectedDisplay(displayName);
        setIsOpen(false);
        setHoveredId(null);
        onChange(s.id, displayName);
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

    const normalizeStr = (str: string) => {
        if (!str) return '';
        return str.normalize('NFD').replace(/[\u0300-\u036f]/g, "").toLowerCase().trim();
    };

    const getProcessedResults = () => {
        let baseResults = results;
        if (allowedCompanyCode) {
            baseResults = results.filter(s => s.companyCode?.toUpperCase() === allowedCompanyCode.toUpperCase());
        }

        const normSuggest = normalizeStr(suggestedMatchText);
        
        const processed = baseResults.map(s => {
            const normName = normalizeStr(s.departmentName);
            let matchScore = 0;
            
            if (normSuggest) {
                if (normName === normSuggest) {
                    matchScore = 2; // Exact Match
                } else if (normSuggest.length >= 3 && normName.length >= 3 && 
                          (normName.includes(normSuggest) || normSuggest.includes(normName))) {
                    matchScore = 1; // Partial Match
                }
            }
            return { ...s, matchScore };
        });

        // Ensure stable sort if scores are identical
        processed.sort((a, b) => {
            if (b.matchScore !== a.matchScore) {
                return b.matchScore - a.matchScore; 
            }
            // Secondary sort by company code to group duplicates
            return (a.companyCode || '').localeCompare(b.companyCode || '');
        });

        return processed;
    };

    const processedResults = getProcessedResults();

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
        minHeight: '48px',
        transition: 'all 0.15s ease-out',
        userSelect: 'none',
    };

    const panelStyle: React.CSSProperties = {
        ...(dropdownStyle || {}),
        backgroundColor: 'var(--color-bg-surface)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-md)',
        overflow: 'hidden',
        minWidth: '550px',
        display: 'flex',
        flexDirection: 'column',
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

    const getRowStyle = (id: number, matchScore: number): React.CSSProperties => {
        let baseBg = '#ffffff';
        if (matchScore === 2) baseBg = '#f0fdf4'; // Light green for exact
        else if (matchScore === 1) baseBg = '#f8fafc'; // Light blue for partial
        
        return {
            ...gridStyle,
            cursor: 'pointer',
            borderBottom: '1px solid #f3f4f6',
            backgroundColor: hoveredId === id ? '#e2e8f0' : baseBg,
            transition: 'background-color 0.1s ease',
        };
    };

    const getCellStyle = (_id: number, extra?: React.CSSProperties): React.CSSProperties => ({
        padding: '12px 10px',
        fontSize: '0.825rem',
        color: 'var(--color-text-main)',
        display: 'flex',
        alignItems: 'center',
        overflow: 'hidden',
        ...extra,
    });

    const isFieldDisabled = disabled || plantNotSelected;
    const currentPlaceholder = plantNotSelected ? 'Selecione primeiro a planta...' : placeholder;

    return (
        <div className={className} ref={containerRef} style={{ position: 'relative', marginTop: '8px', zIndex: isOpen ? 5 : 1 }}>
            <div onClick={() => !isFieldDisabled && toggleDropdown()} style={{...triggerStyle, backgroundColor: hasError ? '#FEF2F2' : isFieldDisabled ? 'var(--color-field-disabled-bg)' : '#ffffff', cursor: isFieldDisabled ? 'not-allowed' : 'pointer', opacity: isFieldDisabled ? 0.7 : 1}}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: '8px', fontWeight: 500, textTransform: 'none', letterSpacing: '0.01em', fontSize: '0.85rem' }}>
                    {selectedDisplay || currentPlaceholder}
                </span>
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                    {selectedDisplay && !isFieldDisabled && (
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
                                placeholder="Pesquisar departamento..."
                                style={searchInputStyle}
                            />
                        </div>

                        <div style={headerStyle}>
                            <div style={headerCellBorderStyle}>Código</div>
                            <div style={headerCellBorderStyle}>Empresa</div>
                            <div style={headerCellStyle}>Descrição</div>
                        </div>

                        <div style={{ maxHeight: '280px', overflowY: 'auto', backgroundColor: 'var(--color-bg-surface)' }}>
                            {isLoading ? (
                                <div style={{ padding: '32px', textAlign: 'center' }}>
                                    <div style={{ display: 'inline-block', width: '24px', height: '24px', border: '3px solid #e5e7eb', borderTopColor: '#111827', borderRadius: '50%', animation: 'spin 0.8s linear infinite' }} />
                                </div>
                            ) : processedResults.length === 0 ? (
                                <div style={{ padding: '24px', textAlign: 'center', color: '#9ca3af', fontSize: '0.875rem', fontStyle: 'italic' }}>
                                    Nenhum departamento encontrado.
                                </div>
                            ) : (
                                processedResults.map((s) => {
                                    return (
                                        <div
                                            key={s.id}
                                            style={getRowStyle(s.id, s.matchScore)}
                                            onMouseEnter={() => setHoveredId(s.id)}
                                            onMouseLeave={() => setHoveredId(null)}
                                            onMouseDown={(e) => {
                                                e.preventDefault();
                                                handleSelect(s);
                                            }}
                                        >
                                            <div style={{ ...getCellStyle(s.id), borderRight: '1px solid #f3f4f6', fontFamily: 'monospace', fontWeight: 700, color: 'var(--color-primary)', fontSize: '0.75rem' }}>{s.departmentCode}</div>
                                            <div style={{ ...getCellStyle(s.id), borderRight: '1px solid #f3f4f6', fontSize: '0.75rem', color: '#64748b' }}>{s.companyCode}</div>
                                            <div style={{ ...getCellStyle(s.id), fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.01em', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', display: 'flex', alignItems: 'center', gap: '8px', justifyContent: 'space-between' }}>
                                                <span>{s.departmentName}</span>
                                                {s.matchScore > 0 && (
                                                    <span style={{ 
                                                        backgroundColor: s.matchScore === 2 ? '#22c55e' : '#cbd5e1', 
                                                        color: s.matchScore === 2 ? 'white' : '#334155',
                                                        padding: '2px 6px', 
                                                        borderRadius: '12px', 
                                                        fontSize: '0.6rem', 
                                                        fontWeight: 800,
                                                        flexShrink: 0
                                                    }}>
                                                        {s.matchScore === 2 ? '✨ PROVÁVEL' : 'SUGESTÃO'}
                                                    </span>
                                                )}
                                            </div>
                                        </div>
                                    );
                                })
                            )}
                        </div>
                    </div>
                </DropdownPortal>
            )}
        </div>
    );
}
