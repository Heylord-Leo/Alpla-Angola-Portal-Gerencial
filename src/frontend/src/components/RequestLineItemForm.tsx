import React from 'react';
import { LookupDto, RequestLineItemDto, PrimaveraRequestValidationResultDto } from '../types';
import { CurrencyInput } from './CurrencyInput';
import { CatalogItemAutocomplete } from './CatalogItemAutocomplete';
import { api } from '../lib/api';
import { AlertCircle, CheckCircle, Info, XCircle } from 'lucide-react';

export interface RequestLineItemFormProps {
    itemForm: Partial<RequestLineItemDto>;
    setItemForm: (item: Partial<RequestLineItemDto> | null) => void;
    onSaveItem: (e: React.FormEvent) => void;
    itemSaving: boolean;
    units: LookupDto[];
    fieldErrors: Record<string, string[]>;
    clearFieldError: (fieldName: string) => void;
    companyId: number | null;
    plants: LookupDto[];
    requestTypeCode?: string;
    costCenters: LookupDto[];
    ivaRates: LookupDto[];
    supplierId?: number | null;
}

export function RequestLineItemForm({
    itemForm,
    setItemForm,
    onSaveItem,
    itemSaving,
    units,
    fieldErrors,
    clearFieldError,
    companyId,
    plants,
    requestTypeCode,
    costCenters,
    ivaRates,
    supplierId
}: RequestLineItemFormProps) {
    const [validationResult, setValidationResult] = React.useState<PrimaveraRequestValidationResultDto | null>(null);
    const [isValidating, setIsValidating] = React.useState<boolean>(false);

    React.useEffect(() => {
        // Clear state if no catalog code or company selected
        if (!itemForm.itemCatalogCode || !companyId) {
            setValidationResult(null);
            setIsValidating(false);
            return;
        }

        const runValidation = async () => {
            setIsValidating(true);
            setValidationResult(null);
            try {
                const res = await api.requests.validateLine({
                    companyId: companyId as number,
                    itemCatalogCode: itemForm.itemCatalogCode as string,
                    supplierId: supplierId || null
                });
                setValidationResult(res);
            } catch (error) {
                console.error("Validation error:", error);
                setValidationResult({
                    status: "ERROR",
                    messages: ["Ocorreu um erro ao validar os dados no Primavera."],
                    isArticleFound: false,
                    isSupplierFound: false,
                    isRelationshipValid: false
                });
            } finally {
                setIsValidating(false);
            }
        };

        const timeoutId = setTimeout(runValidation, 500);
        return () => clearTimeout(timeoutId);
    }, [itemForm.itemCatalogCode, companyId, supplierId]);

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
            border: `1px solid ${isError ? '#EF4444' : 'var(--color-border)'}`,
            boxShadow: isError ? '0 0 0 3px rgba(239, 68, 68, 0.2)' : 'var(--shadow-sm)',
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
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-sm)',
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
        <div style={{ padding: '24px', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-md)' }}>
            <h3 style={{ margin: '0 0 24px 0', fontSize: '1.25rem', fontFamily: 'var(--font-family-display)', color: 'var(--color-primary)' }}>
                {itemForm.id ? 'Editar Item' : 'Novo Item'}
            </h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '20px', paddingBottom: '16px' }}>
                <label style={{ ...labelStyle, gridColumn: 'span 2' }}>Descrição / Item do Catálogo <span style={{ color: 'red' }}>*</span>
                    <div style={{ marginTop: '8px' }}>
                        <CatalogItemAutocomplete
                            value={itemForm.itemCatalogCode ? `[${itemForm.itemCatalogCode}] ${itemForm.description || ''}` : (itemForm.description || '')}
                            itemCatalogId={itemForm.itemCatalogId ?? null}
                            onChange={(description, catalogId, catalogCode, defaultUnitId) => {
                                const updates: Partial<RequestLineItemDto> = {
                                    ...itemForm,
                                    description,
                                    itemCatalogId: catalogId,
                                    itemCatalogCode: catalogCode
                                };
                                // Auto-fill unit from catalog when selecting a catalog item
                                if (catalogId && defaultUnitId) {
                                    const matchingUnit = units.find(u => u.id === defaultUnitId);
                                    if (matchingUnit) {
                                        updates.unit = matchingUnit.code;
                                    }
                                }
                                setItemForm(updates);
                                clearFieldError('Description');
                            }}
                            placeholder="Pesquisar item do catálogo ou digitar descrição..."
                            style={{
                                ...getInputStyle('Description'),
                                marginTop: 0
                            }}
                        />
                    </div>
                    {itemForm.itemCatalogCode && (
                        <div style={{ marginTop: '4px', fontSize: '0.7rem', color: 'var(--color-primary)', fontWeight: 600 }}>
                            📦 Vinculado ao catálogo: {itemForm.itemCatalogCode}
                        </div>
                    )}
                    {renderFieldError('Description')}
                </label>

                <label style={labelStyle}>Centro de Custo
                    <select
                        required={false}
                        value={itemForm.costCenterId || ''}
                        onChange={e => { setItemForm({ ...itemForm, costCenterId: Number(e.target.value) }); clearFieldError('CostCenterId'); }}
                        style={getInputStyle('CostCenterId')}
                        disabled={!itemForm.plantId}
                    >
                        <option value="">
                            {!itemForm.plantId
                                ? 'Selecione primeiro a planta de destino'
                                : (costCenters.filter(cc => (cc.isActive || cc.id === itemForm.costCenterId) && cc.plantId === itemForm.plantId).length === 0
                                    ? 'Nenhum centro de custo para esta planta'
                                    : 'Selecione o centro de custo (opcional)...')}
                        </option>
                        {costCenters
                            .filter(cc => (cc.isActive || cc.id === itemForm.costCenterId) && cc.plantId === itemForm.plantId)
                            .map(cc => (
                                <option key={cc.id} value={cc.id}>{cc.code} - {cc.name}</option>
                            ))}
                    </select>
                    {renderFieldError('CostCenterId')}
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
                <label style={labelStyle}>Planta de Destino
                    <select 
                        required={false} 
                        value={itemForm.plantId || ''} 
                        onChange={e => {
                            const newPlantId = Number(e.target.value);
                            // Clear CC when plant changes to prevent invalid plant/CC combinations
                            setItemForm({ ...itemForm, plantId: newPlantId, costCenterId: null });
                            clearFieldError('PlantId');
                            clearFieldError('CostCenterId');
                        }} 
                        style={getInputStyle('PlantId')}
                        disabled={!companyId}
                    >
                        <option value="">
                            {!companyId 
                                ? 'Selecione primeiro a empresa no cabeçalho' 
                                : (plants.length === 0 ? 'Nenhuma planta disponível para esta empresa' : 'Selecione a planta (usar padrão do pedido)...')}
                        </option>
                        {plants.filter(p => p.isActive || p.id === itemForm.plantId).map(p => (
                            <option key={p.id} value={p.id}>{p.name} {!p.isActive ? '(Inativo)' : ''}</option>
                        ))}
                    </select>
                    <span style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', marginTop: '4px', fontStyle: 'italic' }}>
                        Se vazio, assume a planta principal definida no cabeçalho do pedido.
                    </span>
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

                <label style={labelStyle}>Taxa IVA <span style={{ color: 'red' }}>*</span>
                    <select
                        required
                        value={itemForm.ivaRateId || ''}
                        onChange={e => { setItemForm({ ...itemForm, ivaRateId: Number(e.target.value) }); clearFieldError('IvaRateId'); }}
                        style={getInputStyle('IvaRateId')}
                    >
                        <option value="" disabled>Selecione a taxa IVA...</option>
                        {ivaRates.filter(tax => tax.isActive || tax.id === itemForm.ivaRateId).map(tax => (
                            <option key={tax.id} value={tax.id}>{tax.name}</option>
                        ))}
                    </select>
                    {renderFieldError('IvaRateId')}
                </label>

                {requestTypeCode === 'PAYMENT' && (
                    <label style={labelStyle}>Data de Vencimento <span style={{ color: 'red' }}>*</span>
                        <input
                            required
                            type="date"
                            value={itemForm.dueDate ? itemForm.dueDate.substring(0, 10) : ''}
                            onChange={e => {
                                setItemForm({ ...itemForm, dueDate: e.target.value || null });
                                clearFieldError('DueDate');
                            }}
                            style={getInputStyle('DueDate')}
                        />
                        {renderFieldError('DueDate')}
                    </label>
                )}

                <label style={{ ...labelStyle, gridColumn: 'span 2' }}>Notas / Observações
                    <input type="text" value={itemForm.notes || ''} onChange={e => setItemForm({ ...itemForm, notes: e.target.value })} style={inputStyle} placeholder="Opcional..." />
                </label>

                {/* Validation Banner UI */}
                {(isValidating || validationResult) && (
                    <div style={{ gridColumn: '1 / -1', marginTop: '16px', marginBottom: '8px' }}>
                        {isValidating ? (
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border-heavy)', borderRadius: 'var(--radius-sm)', color: 'var(--color-text-main)', fontSize: '0.875rem' }}>
                                <div className="spinner" style={{ width: '16px', height: '16px', border: '2px solid var(--color-border-heavy)', borderTopColor: 'var(--color-primary)', borderRadius: '50%', animation: 'spin 1s linear infinite' }} />
                                Validando artigo no Primavera...
                            </div>
                        ) : validationResult && (
                            <div style={{ 
                                display: 'flex', 
                                alignItems: 'flex-start', 
                                gap: '12px', 
                                padding: '12px 16px', 
                                borderRadius: 'var(--radius-sm)', 
                                border: `1px solid ${
                                    validationResult.status === 'VALID' ? '#10B981' : 
                                    validationResult.status === 'WARNING' ? '#F59E0B' : 
                                    validationResult.status === 'INVALID' ? '#EF4444' : '#6B7280'
                                }`,
                                backgroundColor: 
                                    validationResult.status === 'VALID' ? 'rgba(16, 185, 129, 0.1)' : 
                                    validationResult.status === 'WARNING' ? 'rgba(245, 158, 11, 0.1)' : 
                                    validationResult.status === 'INVALID' ? 'rgba(239, 68, 68, 0.1)' : 'rgba(107, 114, 128, 0.1)'
                            }}>
                                <div style={{ marginTop: '2px' }}>
                                    {validationResult.status === 'VALID' ? <CheckCircle size={20} color="#10B981" /> :
                                     validationResult.status === 'WARNING' ? <AlertCircle size={20} color="#F59E0B" /> :
                                     validationResult.status === 'INVALID' ? <XCircle size={20} color="#EF4444" /> :
                                     <Info size={20} color="#6B7280" />}
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                    <strong style={{ 
                                        fontSize: '0.875rem',
                                        color: 
                                            validationResult.status === 'VALID' ? '#047857' : 
                                            validationResult.status === 'WARNING' ? '#B45309' : 
                                            validationResult.status === 'INVALID' ? '#B91C1C' : '#374151'
                                    }}>
                                        {validationResult.status === 'VALID' ? 'Validação Concluída' :
                                         validationResult.status === 'WARNING' ? 'Aviso de Validação' :
                                         validationResult.status === 'INVALID' ? 'Erro de Validação (Bloqueante)' :
                                         'Erro de Sistema'}
                                    </strong>
                                    {validationResult.messages.map((msg, idx) => (
                                        <span key={idx} style={{ fontSize: '0.875rem', color: 'var(--color-text-main)' }}>{msg}</span>
                                    ))}
                                </div>
                            </div>
                        )}
                    </div>
                )}

                <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '8px' }}>
                    <button type="button" onClick={() => setItemForm(null)} className="btn-secondary" style={{ padding: '10px 20px' }}>Cancelar</button>
                    <button 
                        type="button" 
                        onClick={onSaveItem} 
                        disabled={itemSaving || isValidating || validationResult?.status === 'INVALID' || validationResult?.status === 'ERROR'} 
                        className="btn-primary" 
                        style={{ padding: '10px 20px', opacity: (itemSaving || isValidating || validationResult?.status === 'INVALID' || validationResult?.status === 'ERROR') ? 0.7 : 1 }}>
                        {itemSaving ? 'Salvando...' : 'Salvar Item'}
                    </button>
                </div>
            </div>
        </div>
    );
}
