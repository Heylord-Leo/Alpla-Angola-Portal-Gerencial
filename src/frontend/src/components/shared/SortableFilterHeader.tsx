import React, { useState, useRef, useEffect, useCallback } from 'react';
import { ArrowUp, ArrowDown, Filter, X } from 'lucide-react';

// ─── Types ─────────────────────────────────────────────────────────────────────

export type SortDirection = 'asc' | 'desc';

export interface SortConfig {
    key: string;
    direction: SortDirection;
}

export interface ColumnFilterConfig {
    type: 'text' | 'enum';
    enumOptions?: { value: string; label: string }[];
}

// ─── Normalization Helper ──────────────────────────────────────────────────────

/** Normalize text for accent-insensitive, case-insensitive, space-collapsed matching. */
export function normalizeFilterText(text: string): string {
    return text
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '') // strip diacritics
        .toLowerCase()
        .replace(/\s+/g, ' ')
        .trim();
}

// ─── Component ─────────────────────────────────────────────────────────────────

interface SortableFilterHeaderProps {
    /** Display label for the column header */
    label: string;
    /** Sort key for this column */
    sortKey: string;
    /** Filter configuration */
    filterConfig: ColumnFilterConfig;
    /** Current sort state */
    currentSort: SortConfig | null;
    /** Current filter value for this column */
    currentFilter: string | string[] | undefined;
    /** Sort change callback */
    onSortChange: (sort: SortConfig | null) => void;
    /** Filter change callback */
    onFilterChange: (key: string, value: string | string[] | undefined) => void;
    /** Extra className for the th */
    className?: string;
}

export function SortableFilterHeader({
    label,
    sortKey,
    filterConfig,
    currentSort,
    currentFilter,
    onSortChange,
    onFilterChange,
    className = ''
}: SortableFilterHeaderProps) {
    const [popoverOpen, setPopoverOpen] = useState(false);
    const popoverRef = useRef<HTMLDivElement>(null);
    const btnRef = useRef<HTMLButtonElement>(null);

    const isActive = currentSort?.key === sortKey;
    const hasFilter = filterConfig.type === 'text'
        ? typeof currentFilter === 'string' && currentFilter.length > 0
        : Array.isArray(currentFilter) && currentFilter.length > 0;

    // ── Close popover on outside click ──
    useEffect(() => {
        if (!popoverOpen) return;
        const handler = (e: MouseEvent) => {
            if (
                popoverRef.current && !popoverRef.current.contains(e.target as Node) &&
                btnRef.current && !btnRef.current.contains(e.target as Node)
            ) {
                setPopoverOpen(false);
            }
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, [popoverOpen]);

    // ── Sort toggle: asc → desc → clear ──
    const handleSortClick = useCallback(() => {
        if (!isActive) {
            onSortChange({ key: sortKey, direction: 'asc' });
        } else if (currentSort?.direction === 'asc') {
            onSortChange({ key: sortKey, direction: 'desc' });
        } else {
            onSortChange(null);
        }
    }, [isActive, currentSort, sortKey, onSortChange]);

    // ── Filter toggle ──
    const handleFilterToggle = useCallback((e: React.MouseEvent) => {
        e.stopPropagation();
        setPopoverOpen(prev => !prev);
    }, []);

    return (
        <th className={`sfh-th ${hasFilter ? 'sfh-th-filtered' : ''} ${className}`}>
            <div className="sfh-header-cell">
                {/* Sort area: clicking the label sorts */}
                <button
                    className="sfh-sort-btn"
                    onClick={handleSortClick}
                    title={`Ordenar por ${label}`}
                    type="button"
                >
                    <span className="sfh-label">{label}</span>
                    <span className="sfh-sort-icon">
                        {isActive && currentSort?.direction === 'asc' && (
                            <ArrowUp size={12} className="sfh-arrow sfh-arrow-active" />
                        )}
                        {isActive && currentSort?.direction === 'desc' && (
                            <ArrowDown size={12} className="sfh-arrow sfh-arrow-active" />
                        )}
                        {!isActive && (
                            <ArrowUp size={12} className="sfh-arrow sfh-arrow-idle" />
                        )}
                    </span>
                </button>

                {/* Filter icon: opens the popover */}
                <button
                    ref={btnRef}
                    className={`sfh-filter-btn ${hasFilter ? 'sfh-filter-btn-active' : ''}`}
                    onClick={handleFilterToggle}
                    title={`Filtrar ${label}`}
                    type="button"
                >
                    <Filter size={11} />
                </button>
            </div>

            {/* Filter popover */}
            {popoverOpen && (
                <div ref={popoverRef} className="sfh-popover">
                    <div className="sfh-popover-header">
                        <span className="sfh-popover-title">Filtrar: {label}</span>
                        {hasFilter && (
                            <button
                                className="sfh-popover-clear"
                                onClick={() => {
                                    onFilterChange(sortKey, undefined);
                                    setPopoverOpen(false);
                                }}
                                type="button"
                            >
                                <X size={12} />
                                Limpar
                            </button>
                        )}
                    </div>

                    {filterConfig.type === 'text' && (
                        <TextFilter
                            value={(currentFilter as string) ?? ''}
                            onChange={(val) => onFilterChange(sortKey, val || undefined)}
                        />
                    )}

                    {filterConfig.type === 'enum' && filterConfig.enumOptions && (
                        <EnumFilter
                            options={filterConfig.enumOptions}
                            selected={(currentFilter as string[]) ?? []}
                            onChange={(val) => onFilterChange(sortKey, val.length > 0 ? val : undefined)}
                        />
                    )}
                </div>
            )}
        </th>
    );
}

// ─── Text Filter Sub-Component ──────────────────────────────────────────────

function TextFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
    const inputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        inputRef.current?.focus();
    }, []);

    return (
        <div className="sfh-text-filter">
            <input
                ref={inputRef}
                type="text"
                className="sfh-text-input"
                placeholder="Contém..."
                value={value}
                onChange={(e) => onChange(e.target.value)}
            />
        </div>
    );
}

// ─── Enum Filter Sub-Component ──────────────────────────────────────────────

function EnumFilter({
    options,
    selected,
    onChange
}: {
    options: { value: string; label: string }[];
    selected: string[];
    onChange: (v: string[]) => void;
}) {
    const toggle = (val: string) => {
        if (selected.includes(val)) {
            onChange(selected.filter(s => s !== val));
        } else {
            onChange([...selected, val]);
        }
    };

    return (
        <div className="sfh-enum-filter">
            {options.map(opt => (
                <label key={opt.value} className="sfh-enum-option">
                    <input
                        type="checkbox"
                        checked={selected.includes(opt.value)}
                        onChange={() => toggle(opt.value)}
                    />
                    <span>{opt.label}</span>
                </label>
            ))}
        </div>
    );
}
