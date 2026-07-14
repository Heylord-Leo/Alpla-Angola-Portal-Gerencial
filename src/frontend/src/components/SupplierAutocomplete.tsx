import React from 'react';
import { api } from '../lib/api';
import { SupplierSearchDto } from '../types';
import { SearchableDropdown } from './common/ui/SearchableDropdown';

interface SupplierAutocompleteProps {
    initialName?: string;
    initialPortalCode?: string;
    onChange: (id: number | null, name: string, portalCode?: string, primaveraCode?: string, registrationStatus?: string) => void;
    placeholder?: string;
    disabled?: boolean;
    hasError?: boolean;
    hasWarning?: boolean;
    isUnresolved?: boolean;
    className?: string;
    name?: string;
}

// Column widths for the tabular layout — must match exactly in header and rows
const COL_WIDTHS = '110px 110px 1fr';

export function SupplierAutocomplete({
    initialName = '',
    initialPortalCode = '',
    onChange,
    placeholder = 'Selecionar fornecedor...',
    disabled = false,
    hasError = false,
    hasWarning = false,
    isUnresolved = false,
    className,
    name = 'SupplierId'
}: SupplierAutocompleteProps) {
    
    // We mock a SupplierSearchDto for the initial value if we only have name/code
    const initialValue: SupplierSearchDto | null = initialName ? {
        id: -1, // Dummy ID for initial display state
        name: initialName,
        portalCode: initialPortalCode || ''
    } : null;

    const handleSearch = async (term: string) => {
        return await api.lookups.searchSuppliers(term);
    };

    const handleChange = (item: SupplierSearchDto | null) => {
        if (item) {
            onChange(item.id, item.name, item.portalCode, item.primaveraCode, item.registrationStatus);
        } else {
            onChange(null, '', '', '', undefined);
        }
    };

    const getDisplayValue = (item: SupplierSearchDto | null) => {
        if (!item) return '';
        return item.portalCode ? `${item.name} — ${item.primaveraCode || 'S/NIF'} — ${item.portalCode}` : item.name;
    };

    // ─── Styles ────────────────────────────────────────────────────────────────

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

    const getCellStyle = (extra?: React.CSSProperties): React.CSSProperties => ({
        padding: '12px 10px',
        fontSize: '0.825rem',
        color: 'var(--color-text-main)',
        display: 'flex',
        alignItems: 'center',
        overflow: 'hidden',
        ...extra,
    });

    return (
        <SearchableDropdown<SupplierSearchDto>
            value={initialValue}
            onChange={handleChange}
            onSearch={handleSearch}
            getDisplayValue={getDisplayValue}
            placeholder={placeholder}
            searchPlaceholder="Pesquisar por nome ou código..."
            disabled={disabled}
            hasError={hasError}
            hasWarning={hasWarning}
            isUnresolved={isUnresolved}
            className={className}
            name={name}
            minDropdownWidth="480px"
            renderHeader={() => (
                <div style={headerStyle}>
                    <div style={headerCellBorderStyle}>Portal</div>
                    <div style={headerCellBorderStyle}>Primavera</div>
                    <div style={headerCellStyle}>Descrição</div>
                </div>
            )}
            renderItem={(s, isHovered) => (
                <div style={{ ...gridStyle, width: '100%' }}>
                    <div style={{
                        ...getCellStyle(),
                        borderRight: `1px solid ${isHovered ? '#e5e7eb' : '#f3f4f6'}`,
                        fontFamily: 'monospace',
                        fontWeight: 700,
                        color: 'var(--color-primary)',
                        fontSize: '0.75rem',
                    }}>
                        {s.portalCode}
                    </div>
                    <div style={{
                        ...getCellStyle(),
                        borderRight: `1px solid ${isHovered ? '#e5e7eb' : '#f3f4f6'}`,
                        fontFamily: 'monospace',
                        fontSize: '0.75rem',
                        color: 'var(--color-text-muted)',
                    }}>
                        {s.primaveraCode || '—'}
                    </div>
                    <div style={{
                        ...getCellStyle(),
                        fontWeight: 600,
                        textTransform: 'uppercase',
                        letterSpacing: '0.01em',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                    }}>
                        {s.name}
                    </div>
                </div>
            )}
        />
    );
}
