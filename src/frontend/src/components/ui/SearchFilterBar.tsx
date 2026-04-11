import React, { useState } from 'react';
import { Search, Filter, X } from 'lucide-react';

interface FilterTab {
    id: string;
    label: string;
}

interface SearchFilterBarProps {
    title?: string;
    subtitle?: string;
    
    // Search
    searchPlaceholder?: string;
    searchValue: string;
    onSearchChange: (val: string) => void;
    
    // Filters
    tabs?: FilterTab[];
    activeTabId?: string;
    onTabChange?: (tabId: string) => void;
    
    // Advanced Filter Toggle
    onToggleAdvancedFilters?: () => void;
    showAdvancedFilters?: boolean;
    
    // Extra actions (e.g. specialized buttons)
    actions?: React.ReactNode;
}

export function SearchFilterBar({
    title,
    subtitle,
    searchPlaceholder = "Buscar...",
    searchValue,
    onSearchChange,
    tabs,
    activeTabId,
    onTabChange,
    onToggleAdvancedFilters,
    showAdvancedFilters,
    actions
}: SearchFilterBarProps) {
    const [isFocused, setIsFocused] = useState(false);

    return (
        <div style={{ 
            display: 'flex', 
            justifyContent: 'space-between', 
            alignItems: 'flex-end', 
            flexWrap: 'wrap', 
            gap: '16px' 
        }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {(title || subtitle) && (
                    <div>
                        {title && (
                            <h2 style={{
                                fontSize: '1.1rem',
                                fontWeight: 800,
                                color: 'var(--color-primary)',
                                margin: 0,
                            }}>{title}</h2>
                        )}
                        {subtitle && (
                            <p style={{
                                fontSize: '0.75rem',
                                color: 'var(--color-text-muted)',
                                margin: '4px 0 0',
                                fontWeight: 500,
                                textTransform: 'none',
                            }}>{subtitle}</p>
                        )}
                    </div>
                )}

                {tabs && tabs.length > 0 && (
                    <div style={{
                        display: 'flex',
                        backgroundColor: 'rgba(241, 245, 249, 0.8)',
                        padding: '4px',
                        borderRadius: '8px',
                        width: 'fit-content',
                        border: '1px solid rgba(226, 232, 240, 0.5)',
                        gap: '4px',
                        overflowX: 'auto'
                    }}>
                        {tabs.map(tab => {
                            const isActive = activeTabId === tab.id;
                            return (
                                <button
                                    key={tab.id}
                                    onClick={() => onTabChange?.(tab.id)}
                                    style={{
                                        padding: '6px 16px',
                                        borderRadius: '6px',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        border: 'none',
                                        cursor: 'pointer',
                                        transition: 'all 0.15s ease',
                                        backgroundColor: isActive ? '#ffffff' : 'transparent',
                                        color: isActive ? 'var(--color-primary)' : '#64748b',
                                        boxShadow: isActive ? '0 1px 2px 0 rgba(0, 0, 0, 0.05)' : 'none',
                                        whiteSpace: 'nowrap'
                                    }}
                                    onMouseEnter={(e) => { 
                                        if(!isActive) { 
                                            e.currentTarget.style.color = '#0f172a'; 
                                            e.currentTarget.style.backgroundColor = 'rgba(226,232,240,0.5)';
                                        } 
                                    }}
                                    onMouseLeave={(e) => { 
                                        if(!isActive) { 
                                            e.currentTarget.style.color = '#64748b'; 
                                            e.currentTarget.style.backgroundColor = 'transparent';
                                        } 
                                    }}
                                >
                                    {tab.label}
                                </button>
                            );
                        })}
                    </div>
                )}
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <div style={{ position: 'relative' }}>
                    <Search
                        size={16}
                        style={{
                            position: 'absolute',
                            left: '12px',
                            top: '50%',
                            transform: 'translateY(-50%)',
                            color: '#94a3b8',
                            pointerEvents: 'none',
                        }}
                    />
                    <input
                        type="text"
                        placeholder={searchPlaceholder}
                        value={searchValue}
                        onChange={(e) => onSearchChange(e.target.value)}
                        onFocus={() => setIsFocused(true)}
                        onBlur={() => setIsFocused(false)}
                        style={{
                            paddingLeft: '36px',
                            paddingRight: searchValue ? '36px' : '16px',
                            paddingTop: '8px',
                            paddingBottom: '8px',
                            backgroundColor: '#ffffff',
                            border: `1px solid ${isFocused ? 'var(--color-primary)' : '#e2e8f0'}`,
                            borderRadius: '8px',
                            fontSize: '0.85rem',
                            width: '260px',
                            boxShadow: isFocused ? '0 0 0 2px rgba(37, 99, 235, 0.2)' : '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
                            transition: 'border-color 0.15s ease, box-shadow 0.15s ease',
                            color: '#0f172a',
                            outline: 'none',
                        }}
                    />
                    {searchValue && (
                        <button
                            onClick={() => onSearchChange('')}
                            style={{
                                position: 'absolute',
                                right: '8px',
                                top: '50%',
                                transform: 'translateY(-50%)',
                                background: 'transparent',
                                border: 'none',
                                cursor: 'pointer',
                                color: '#94a3b8',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                padding: '4px',
                            }}
                            onMouseEnter={(e) => e.currentTarget.style.color = '#475569'}
                            onMouseLeave={(e) => e.currentTarget.style.color = '#94a3b8'}
                        >
                            <X size={14} />
                        </button>
                    )}
                </div>
                
                {onToggleAdvancedFilters && (
                    <button 
                        onClick={onToggleAdvancedFilters}
                        style={{
                            padding: '8px',
                            backgroundColor: showAdvancedFilters ? '#f8fafc' : '#ffffff',
                            border: '1px solid #e2e8f0',
                            borderRadius: '8px',
                            color: showAdvancedFilters ? 'var(--color-primary)' : '#475569',
                            cursor: 'pointer',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
                            transition: 'all 0.15s ease',
                        }}
                        onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = '#f8fafc'; }}
                        onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = showAdvancedFilters ? '#f8fafc' : '#ffffff'; }}
                    >
                        <Filter size={18} />
                    </button>
                )}

                {actions}
            </div>
        </div>
    );
}
