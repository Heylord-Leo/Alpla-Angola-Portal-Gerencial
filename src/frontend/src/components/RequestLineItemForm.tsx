import React from 'react';
import { LookupDto, CurrencyDto, RequestLineItemDto } from '../types';
import { CurrencyInput } from './CurrencyInput';

export interface RequestLineItemFormProps {
    itemForm: Partial<RequestLineItemDto>;
    setItemForm: (item: Partial<RequestLineItemDto> | null) => void;
    onSaveItem: (e: React.FormEvent) => void;
    itemSaving: boolean;
    units: LookupDto[];
    currencies: CurrencyDto[];
    currencyId: number;
    fieldErrors: Record<string, string[]>;
    clearFieldError: (fieldName: string) => void;
    companyId: number | null;
    plants: LookupDto[];
    requestTypeCode?: string;
}

export function RequestLineItemForm({
    itemForm,
    setItemForm,
    onSaveItem,
    itemSaving,
    units,
    currencies,
    currencyId,
    fieldErrors,
    clearFieldError,
    companyId,
    plants,
    requestTypeCode
}: RequestLineItemFormProps) {
    const hasError = (fieldName: string) => {
        const normalizedField = fieldName.toLowerCase();
        return Object.keys(fieldErrors).some(k => {
            const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
            return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
        });
    };

    const getInputStyle = (fieldName: string) => {
        const isError = hasError(fieldName);

        return {
            width: '100%',
            padding: '12px 14px',
            borderRadius: 'var(--radius-sm)',
            border: `1px solid ${isError ? '#EF4444' : 'var(--color-border-heavy)'}`,
            boxShadow: isError ? '0 0 0 2px rgba(239, 68, 68, 0.2)' : 'var(--shadow-brutal)',
            fontSize: '0.875rem',
            fontFamily: 'var(--font-family-body)',
            color: 'var(--color-text-main)',
            backgroundColor: 'var(--color-bg-page)',
            marginTop: '8px',
            transition: 'all 0.2s ease',
            outline: 'none'
        };
    };

    const renderFieldError = (fieldName: string) => {
        const normalizedField = fieldName.toLowerCase();
        const errors = Object.entries(fieldErrors)
            .filter(([k]) => {
                const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
                return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
            })
            .flatMap(([, v]) => v);

        if (errors.length === 0) return null;
        return (
            <div style={{ color: '#EF4444', fontSize: '0.75rem', marginTop: '4px', fontWeight: 600 }}>
                {errors.map((err, idx) => <div key={idx}>{err}</div>)}
            </div>
        );
    };

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        borderRadius: 'var(--radius-sm)',
        border: '1px solid var(--color-border-heavy)',
        boxShadow: 'var(--shadow-brutal)',
        fontSize: '0.875rem',
        fontFamily: 'var(--font-family-body)',
        color: 'var(--color-text-main)',
        backgroundColor: 'var(--color-bg-page)',
        marginTop: '8px',
        transition: 'all 0.2s ease',
        outline: 'none'
    };

    const labelStyle = {
        display: 'block',
        fontSize: '0.75rem',
        fontWeight: 600,
        textTransform: 'uppercase' as const,
        letterSpacing: '0.05em',
        color: 'var(--color-text-main)',
        marginBottom: '24px',
        position: 'relative' as const
    };

    return (
        <div style={{ padding: '24px', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-md)', border: '2px solid var(--color-primary)', boxShadow: '4px 4px 0px var(--color-primary-light)' }}>
            <h3 style={{ margin: '0 0 24px 0', fontSize: '1.25rem', fontFamily: 'var(--font-family-display)', color: 'var(--color-primary)' }}>
                {itemForm.id ? 'Editar Item' : 'Novo Item'}
            </h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '20px', paddingBottom: '16px' }}>
                <label style={{ ...labelStyle, gridColumn: 'span 2' }}>Descrição <span style={{ color: 'red' }}>*</span>
                    <input required type="text" value={itemForm.description || ''} onChange={e => { setItemForm({ ...itemForm, description: e.target.value }); clearFieldError('Description'); }} style={getInputStyle('Description')} placeholder="Ex: Cadeira Ergonômica" />
                    {renderFieldError('Description')}
                </label>

                <label style={labelStyle}>Prioridade <span style={{ color: 'red' }}>*</span>
                    <select
                        required
                        value={itemForm.itemPriority || ''}
                        onChange={e => { setItemForm({ ...itemForm, itemPriority: e.target.value as RequestLineItemDto['itemPriority'] }); clearFieldError('ItemPriority'); }}
                        style={getInputStyle('ItemPriority')}
                    >
                        <option value="" disabled>Selecione a prioridade...</option>
                        <option value="HIGH">Alta</option>
                        <option value="MEDIUM">Média</option>
                        <option value="LOW">Baixa</option>
                    </select>
                    {renderFieldError('ItemPriority')}
                </label>

                <label style={labelStyle}>Qtd <span style={{ color: 'red' }}>*</span>
                    <input required type="number" step="0.01" min="0.01" value={itemForm.quantity || ''} onChange={e => { setItemForm({ ...itemForm, quantity: Number(e.target.value) }); clearFieldError('Quantity'); }} style={getInputStyle('Quantity')} />
                    {renderFieldError('Quantity')}
                </label>

                <label style={labelStyle}>Unidade <span style={{ color: 'red' }}>*</span>
                    <select required value={itemForm.unit || ''} onChange={e => { setItemForm({ ...itemForm, unit: e.target.value }); clearFieldError('UnitId'); }} style={getInputStyle('UnitId')}>
                        <option value="" disabled>Selecione a unidade...</option>
                        {units.filter(u => u.isActive || u.code === itemForm.unit).map(u => (
                            <option key={u.id} value={u.code}>{u.code} - {u.name} {!u.isActive ? '(Inativo)' : ''}</option>
                        ))}
                    </select>
                    {renderFieldError('UnitId')}
                </label>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '20px', paddingBottom: '16px' }}>
                <label style={labelStyle}>Planta de Destino <span style={{ color: 'red' }}>*</span>
                    <select 
                        required 
                        value={itemForm.plantId || ''} 
                        onChange={e => { setItemForm({ ...itemForm, plantId: Number(e.target.value) }); clearFieldError('PlantId'); }} 
                        style={getInputStyle('PlantId')}
                        disabled={!companyId}
                    >
                        <option value="" disabled>
                            {!companyId 
                                ? 'Selecione primeiro a empresa no cabeçalho' 
                                : (plants.length === 0 ? 'Nenhuma planta disponível para esta empresa' : 'Selecione a planta...')}
                        </option>
                        {plants.filter(p => p.isActive || p.id === itemForm.plantId).map(p => (
                            <option key={p.id} value={p.id}>{p.name} {!p.isActive ? '(Inativo)' : ''}</option>
                        ))}
                    </select>
                    {renderFieldError('PlantId')}
                </label>

                <label style={labelStyle}>Preço Unit. <span style={{ color: 'red' }}>*</span>
                    <CurrencyInput
                        required
                        value={itemForm.unitPrice || ''}
                        onChange={val => { setItemForm({ ...itemForm, unitPrice: Number(val) }); clearFieldError('UnitPrice'); }}
                        style={getInputStyle('UnitPrice')}
                        hasError={hasError('UnitPrice')}
                    />
                    {renderFieldError('UnitPrice')}
                </label>

                {requestTypeCode === 'QUOTATION' ? (
                    <label style={labelStyle}>Moeda <span style={{ color: 'red' }}>*</span>
                        <select 
                            required 
                            value={itemForm.currencyId || ''} 
                            onChange={e => { setItemForm({ ...itemForm, currencyId: Number(e.target.value) }); clearFieldError('CurrencyId'); }} 
                            style={getInputStyle('CurrencyId')}
                        >
                            <option value="" disabled>Selecione a moeda...</option>
                            {currencies.filter(c => c.isActive || c.id === itemForm.currencyId).map(c => (
                                <option key={c.id} value={c.id}>{c.code} - {c.symbol}</option>
                            ))}
                        </select>
                        {renderFieldError('CurrencyId')}
                    </label>
                ) : (
                    <div style={{ ...labelStyle, display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
                        <span style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em', marginBottom: '8px' }}>
                            Moeda
                        </span>
                        <div style={{
                            padding: '12px 14px',
                            backgroundColor: 'var(--color-bg-page)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-sm)',
                            fontSize: '0.875rem',
                            fontWeight: 600,
                            color: 'var(--color-text-main)'
                        }}>
                            {currencies.find(c => c.id === Number(currencyId))?.code || '---'}
                        </div>
                        <span style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', marginTop: '4px' }}>
                            Herdada do cabeçalho
                        </span>
                    </div>
                )}

                <label style={{ ...labelStyle, gridColumn: 'span 2' }}>Notas / Observações
                    <input type="text" value={itemForm.notes || ''} onChange={e => setItemForm({ ...itemForm, notes: e.target.value })} style={inputStyle} placeholder="Opcional..." />
                </label>

                <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '8px' }}>
                    <button type="button" onClick={() => setItemForm(null)} className="btn-secondary" style={{ padding: '10px 20px' }}>Cancelar</button>
                    <button type="button" onClick={onSaveItem} disabled={itemSaving} className="btn-primary" style={{ padding: '10px 20px', opacity: itemSaving ? 0.7 : 1 }}>{itemSaving ? 'Salvando...' : 'Salvar Item'}</button>
                </div>
            </div>
        </div>
    );
}
