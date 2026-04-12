import React, { useState, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { api } from '../lib/api';
import { ItemCatalogDto } from '../types';
import { Package } from 'lucide-react';

interface CatalogItemAutocompleteProps {
    value: string;  // Current description text
    itemCatalogId: number | null;
    onChange: (description: string, itemCatalogId: number | null, itemCatalogCode: string | null, defaultUnitId: number | null) => void;
    placeholder?: string;
    disabled?: boolean;
    style?: React.CSSProperties;
}

/**
 * Inline autocomplete for catalog item selection.
 * When the user types, it searches the catalog and shows a dropdown.
 * Selecting a catalog item fills the description and stores the catalogId.
 * If the user types freely without selecting, it remains a manual item (null catalogId).
 */
export function CatalogItemAutocomplete({
    value,
    itemCatalogId,
    onChange,
    placeholder = 'Pesquisar item do catálogo ou digitar descrição...',
    disabled = false,
    style
}: CatalogItemAutocompleteProps) {
    const [inputValue, setInputValue] = useState(value);
    const [results, setResults] = useState<ItemCatalogDto[]>([]);
    const [isOpen, setIsOpen] = useState(false);
    const [isLoading, setIsLoading] = useState(false);
    const [hoveredIndex, setHoveredIndex] = useState(-1);
    const containerRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);
    const debounceTimer = useRef<any>();
    const [dropdownStyles, setDropdownStyles] = useState<React.CSSProperties>({});

    useEffect(() => {
        setInputValue(value);
    }, [value]);

    useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        }
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    const performSearch = async (term: string) => {
        if (!term || term.length < 2) {
            setResults([]);
            setIsOpen(false);
            return;
        }
        setIsLoading(true);
        try {
            const data = await api.catalogItems.search(term, 10);
            setResults(data.slice(0, 10));
            setIsOpen(data.length > 0);
        } catch {
            setResults([]);
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        if (isOpen && containerRef.current) {
            const updatePosition = () => {
                if (containerRef.current) {
                    const rect = containerRef.current.getBoundingClientRect();
                    setDropdownStyles({
                        position: 'absolute',
                        top: `${rect.bottom + window.scrollY + 2}px`,
                        left: `${rect.left + window.scrollX}px`,
                        width: `${rect.width}px`,
                        zIndex: 99999,
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '1px solid var(--color-border-heavy)',
                        boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
                        maxHeight: '500px',
                        overflowY: 'auto',
                        borderRadius: 'var(--radius-sm)'
                    });
                }
            };
            
            updatePosition();
            window.addEventListener('resize', updatePosition);
            
            // Also listen to window scroll to smoothly update absolute coordinate if needed, though scrollY handles document scrolling
            return () => {
                window.removeEventListener('resize', updatePosition);
            };
        }
    }, [isOpen, results]);

    const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const val = e.target.value;
        setInputValue(val);
        // Clear catalog reference when user edits text (becomes manual item)
        onChange(val, null, null, null);

        if (debounceTimer.current) clearTimeout(debounceTimer.current);
        debounceTimer.current = setTimeout(() => performSearch(val), 300);
    };

    const handleSelect = (item: ItemCatalogDto) => {
        const display = `[${item.code}] ${item.description}`;
        setInputValue(display);
        setIsOpen(false);
        setHoveredIndex(-1);
        onChange(item.description, item.id, item.code, item.defaultUnitId);
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        if (!isOpen || results.length === 0) return;
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            setHoveredIndex(prev => Math.min(prev + 1, results.length - 1));
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            setHoveredIndex(prev => Math.max(prev - 1, 0));
        } else if (e.key === 'Enter' && hoveredIndex >= 0) {
            e.preventDefault();
            handleSelect(results[hoveredIndex]);
        } else if (e.key === 'Escape') {
            setIsOpen(false);
        }
    };

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '10px 12px',
        paddingLeft: itemCatalogId ? '32px' : '12px',
        borderRadius: 'var(--radius-sm)',
        border: '1px solid var(--color-border-heavy)',
        fontSize: '0.85rem',
        fontFamily: 'var(--font-family-body)',
        color: 'var(--color-text-main)',
        backgroundColor: disabled ? 'var(--color-field-disabled-bg)' : 'var(--color-bg-page)',
        outline: 'none',
        ...(style || {})
    };



    return (
        <div ref={containerRef} style={{ position: 'relative' }}>
            {/* Catalog icon indicator */}
            {itemCatalogId && (
                <Package
                    size={14}
                    style={{
                        position: 'absolute',
                        left: '10px',
                        top: '50%',
                        transform: 'translateY(-50%)',
                        color: 'var(--color-primary)',
                        pointerEvents: 'none',
                        zIndex: 2
                    }}
                />
            )}
            <input
                ref={inputRef}
                type="text"
                value={inputValue}
                onChange={handleInputChange}
                onKeyDown={handleKeyDown}
                onFocus={() => {
                    if (inputValue && inputValue.length >= 2 && results.length > 0) {
                        setIsOpen(true);
                    }
                }}
                placeholder={placeholder}
                disabled={disabled}
                style={inputStyle}
            />

            {isOpen && createPortal(
                <div style={dropdownStyles}>
                    {isLoading ? (
                        <div style={{ padding: '12px', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>
                            Pesquisando...
                        </div>
                    ) : results.length === 0 ? (
                        <div style={{ padding: '12px', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.8rem', fontStyle: 'italic' }}>
                            Nenhum item encontrado no catálogo.
                        </div>
                    ) : (
                        results.map((item, idx) => (
                            <div
                                key={item.id}
                                onMouseEnter={() => setHoveredIndex(idx)}
                                onMouseLeave={() => setHoveredIndex(-1)}
                                onMouseDown={(e) => {
                                    e.preventDefault();
                                    handleSelect(item);
                                }}
                                style={{
                                    padding: '8px 12px',
                                    cursor: 'pointer',
                                    backgroundColor: hoveredIndex === idx ? 'var(--color-bg-page)' : 'transparent',
                                    borderBottom: '1px solid var(--color-border)',
                                    transition: 'background-color 0.1s ease'
                                }}
                            >
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <span style={{
                                        fontFamily: 'monospace',
                                        fontWeight: 700,
                                        fontSize: '0.75rem',
                                        color: 'var(--color-primary)',
                                        minWidth: '80px'
                                    }}>
                                        {item.code}
                                    </span>
                                    <span style={{
                                        fontSize: '0.825rem',
                                        color: 'var(--color-text-main)',
                                        fontWeight: 500,
                                        overflow: 'hidden',
                                        textOverflow: 'ellipsis',
                                        whiteSpace: 'nowrap'
                                    }}>
                                        {item.description}
                                    </span>
                                </div>
                                {(item.category || item.defaultUnitCode || item.primaveraCode || item.supplierCode) && (
                                    <div style={{ marginTop: '2px', fontSize: '0.7rem', color: 'var(--color-text-muted)', display: 'flex', flexWrap: 'wrap', gap: '4px 8px' }}>
                                        {item.category && <span>{item.category}</span>}
                                        {item.defaultUnitCode && <span><span style={{ opacity: 0.7 }}>Unid:</span> {item.defaultUnitCode}</span>}
                                        {item.primaveraCode && <span><span style={{ opacity: 0.7 }}>Primavera:</span> {item.primaveraCode}</span>}
                                        {item.supplierCode && <span><span style={{ opacity: 0.7 }}>Fornecedor:</span> {item.supplierCode}</span>}
                                    </div>
                                )}
                            </div>
                        ))
                    )}
                </div>,
                document.body
            )}
        </div>
    );
}
