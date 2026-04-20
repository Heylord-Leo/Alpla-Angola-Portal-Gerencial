// @ts-nocheck
/**
 * RequestGeneralDataSection
 * 
 * Phase 1 extraction from RequestEdit.tsx (lines 563-836).
 * Purely presentational — all state, handlers, and style helpers
 * are received via props from the parent component.
 * 
 * Includes:
 * - Partial Edit Mode banner (quotation stage)
 * - "Dados Gerais do Pedido" form card with all header fields
 */
import React from 'react';
import { ShieldCheck, AlertCircle, AlertTriangle, UserPlus } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { SupplierAutocomplete } from '../../../components/SupplierAutocomplete';
import { DateInput } from '../../../components/DateInput';
import { LookupDto } from '../../../types';

export interface RequestGeneralDataSectionProps {
    // Form state & handlers
    formData: Record<string, any>;
    setFormData: (updater: (prev: any) => any) => void;
    handleChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => void;
    clearFieldError: (fieldName: string) => void;

    // Supplier state
    supplierName: string;
    setSupplierName: (name: string) => void;
    supplierPortalCode: string;
    setSupplierPortalCode: (code: string) => void;
    setQuickSupplierModal: (config: { show: boolean; initialName: string; initialTaxId: string }) => void;

    // Lookups
    needLevels: LookupDto[];
    departments: LookupDto[];
    companies: LookupDto[];
    plants: LookupDto[];

    // Permissions & computed flags
    canEditHeader: boolean;
    canEditSupplier: boolean;
    isQuotationPartiallyEditable: boolean;
    isQuotationStage: boolean;
    hasSavedQuotations: boolean;

    // Context
    requestTypeCode: string | null;
    requestNumber: string | null;
    status: string | null;
    lineItemsCount: number;

    // Style helpers (Phase 4A: CSS Module class names)
    sectionTitleClassName: string;
    labelClassName: string;
    getInputClassName: (fieldName: string) => string;
    renderFieldError: (fieldName: string) => React.ReactNode;
    getFieldErrors: (fieldName: string) => string[] | null;
}

export function RequestGeneralDataSection({
    formData, setFormData, handleChange, clearFieldError,
    supplierName, setSupplierName, supplierPortalCode, setSupplierPortalCode, setQuickSupplierModal,
    needLevels, departments, companies, plants,
    canEditHeader, canEditSupplier, isQuotationPartiallyEditable, isQuotationStage, hasSavedQuotations,
    requestTypeCode, requestNumber, status, lineItemsCount,
    sectionTitleClassName, labelClassName, getInputClassName, renderFieldError, getFieldErrors
}: RequestGeneralDataSectionProps) {
    return (
        <>
            {/* Partial Edit Mode Banner */}
            {isQuotationPartiallyEditable && (
                <div style={{ 
                    margin: '0 0 24px', padding: '16px', borderRadius: 'var(--radius-lg)', 
                    backgroundColor: '#f0f9ff', border: '1px solid #0ea5e9',
                    display: 'flex', alignItems: 'center', gap: '16px'
                }}>
                    <ShieldCheck size={24} color="#0ea5e9" />
                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                        <span style={{ fontWeight: 800, fontSize: '0.85rem', color: '#0369a1', textTransform: 'uppercase' }}>
                            Modo de Edição Parcial Ativo
                        </span>
                        <span style={{ fontSize: '0.8rem', color: '#0c4a6e' }}>
                            O pedido está em fase de cotação. Você pode ajustar o título, descrição e justificativa. 
                            {hasSavedQuotations ? ' Os itens e fornecedor estão bloqueados por existirem cotações salvas.' : ' Itens e fornecedor permanecem editáveis até que a primeira cotação seja registrada.'}
                        </span>
                    </div>
                </div>
            )}

            {/* Dados Gerais do Pedido */}
            <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-md)', border: '1px solid var(--color-border)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px', borderBottom: '3px solid var(--color-primary)', paddingBottom: '12px' }}>
                    <h2 className={sectionTitleClassName} style={{ borderBottom: 'none', marginBottom: 0, paddingBottom: 0, marginTop: 0, fontSize: '1.1rem', fontWeight: 900 }}>Dados Gerais do Pedido</h2>
                    {requestNumber && (
                        <div style={{
                            display: 'flex',
                            flexDirection: 'column',
                            alignItems: 'flex-end',
                            gap: '2px',
                            borderLeft: '2px solid var(--color-border)',
                            paddingLeft: '16px'
                        }}>
                            <span style={{ fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                                Código de Rastreio
                            </span>
                            <span style={{
                                fontSize: '1.4rem',
                                fontWeight: 900,
                                color: 'var(--color-primary)',
                                fontFamily: 'var(--font-family-display)',
                                lineHeight: 1
                            }}>
                                {requestNumber}
                            </span>
                        </div>
                    )}
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    <label className={labelClassName}>
                        Título do Pedido <span style={{ color: 'red' }}>*</span>
                        <input
                            required
                            type="text"
                            name="title"
                            value={formData.title}
                            onChange={handleChange}
                            placeholder="Ex: Aquisição de Laptops para TI"
                            className={getInputClassName('Title')}
                            disabled={!canEditHeader}
                        />
                        {renderFieldError('Title')}
                    </label>

                    <label className={labelClassName}>
                        Descrição ou Justificativa <span style={{ color: 'red' }}>*</span>
                        <textarea
                            required
                            name="description"
                            value={formData.description}
                            onChange={handleChange}
                            rows={4}
                            placeholder="Explique o motivo e os detalhes primários..."
                            className={getInputClassName('Description')}
                            style={{ resize: 'vertical' }}
                            disabled={!canEditHeader}
                        />
                        {renderFieldError('Description')}
                    </label>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                        <label className={labelClassName}>
                            Tipo de Pedido <span style={{ color: 'red' }}>*</span>
                            <select
                                name="requestTypeId"
                                value={formData.requestTypeId}
                                onChange={handleChange}
                                className={getInputClassName('RequestTypeId')}
                                disabled={!canEditHeader || isQuotationPartiallyEditable || status !== 'DRAFT'}
                            >
                                <option value={1}>COTAÇÃO (COM)</option>
                                <option value={2}>PAGAMENTO (PAG)</option>
                            </select>
                            {renderFieldError('RequestTypeId')}
                        </label>

                        <div className={labelClassName}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <span>
                                    Fornecedor {Number(formData.requestTypeId) === 1 && <span style={{ color: 'red' }}>*</span>}
                                </span>
                                {canEditSupplier && (
                                    <button
                                        type="button"
                                        onClick={() => setQuickSupplierModal({
                                            show: true,
                                            initialName: supplierName,
                                            initialTaxId: ''
                                        })}
                                        style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', padding: 0, textDecoration: 'underline', display: 'flex', alignItems: 'center', gap: '4px' }}
                                    >
                                        <UserPlus size={12} />
                                        + NOVO FORNECEDOR
                                    </button>
                                )}
                            </div>
                            <SupplierAutocomplete
                                initialName={supplierName}
                                initialPortalCode={supplierPortalCode}
                                onChange={(id, name) => {
                                    setFormData(prev => ({ ...prev, supplierId: id ? String(id) : '' }));
                                    setSupplierName(name);
                                    setSupplierPortalCode('');
                                    clearFieldError('SupplierId');
                                }}
                                hasError={!!getFieldErrors('SupplierId')}
                                className="mt-1"
                                disabled={!canEditSupplier}
                            />
                            {canEditSupplier && !formData.supplierId && supplierName && (
                                <div style={{ marginTop: '8px', padding: '10px 12px', backgroundColor: '#fff7ed', border: '1px solid #ffedd5', borderRadius: '4px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                        <AlertCircle size={16} color="#c2410c" />
                                        <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#9a3412' }}>
                                            Fornecedor sugerido <strong>"{supplierName}"</strong> não encontrado.
                                        </span>
                                    </div>
                                    <button
                                        type="button"
                                        onClick={() => setQuickSupplierModal({
                                            show: true,
                                            initialName: supplierName,
                                            initialTaxId: ''
                                        })}
                                        style={{ 
                                            backgroundColor: '#f97316', color: '#fff', border: 'none', padding: '4px 10px', 
                                            borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800, cursor: 'pointer',
                                            textTransform: 'uppercase'
                                        }}
                                    >
                                        CRIAR AGORA
                                    </button>
                                </div>
                            )}
                            {isQuotationStage && (
                                <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                    Em tratamento pelo comprador.
                                </span>
                            )}
                            {renderFieldError('SupplierId')}
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '24px' }}>
                        <label className={labelClassName}>
                            Grau de Necessidade <span style={{ color: 'red' }}>*</span>
                            <select name="needLevelId" value={formData.needLevelId} onChange={handleChange} className={getInputClassName('NeedLevelId')} disabled={!canEditHeader}>
                                <option value="">-- Selecione --</option>
                                {needLevels.filter(nl => nl.isActive || Number(formData.needLevelId) === nl.id).map(nl => (
                                    <option key={nl.id} value={nl.id}>{nl.name}</option>
                                ))}
                            </select>
                            {renderFieldError('NeedLevelId')}
                        </label>

                        <AnimatePresence>
                            {(requestTypeCode === 'QUOTATION' || Number(formData.requestTypeId) === 1 || requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) && (
                                <motion.div
                                    initial={{ opacity: 0, height: 0 }}
                                    animate={{ opacity: 1, height: 'auto' }}
                                    exit={{ opacity: 0, height: 0 }}
                                    style={{ overflow: 'hidden' }}
                                >
                                    <label className={labelClassName}>
                                        {(requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) ? 'Data de vencimento' : 'Necessário até (Data limite)'} <span style={{ color: 'red' }}>*</span>
                                        <DateInput
                                            required
                                            name="needByDateUtc"
                                            value={formData.needByDateUtc}
                                            onChange={(val) => {
                                                setFormData(prev => ({ ...prev, needByDateUtc: val }));
                                                clearFieldError('NeedByDateUtc');
                                            }}
                                            hasError={!!getFieldErrors('NeedByDateUtc')}
                                            disabled={!canEditHeader}
                                        />
                                        {renderFieldError('NeedByDateUtc')}
                                        {!getFieldErrors('NeedByDateUtc') && formData.needByDateUtc && new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0) && (
                                            <div style={{ color: '#D97706', fontSize: '0.75rem', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600 }}>
                                                <AlertTriangle size={12} />
                                                {(requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) ? 'O documento está vencido.' : 'A data selecionada está no passado.'}
                                            </div>
                                        )}
                                    </label>
                                </motion.div>
                            )}
                        </AnimatePresence>

                        <label className={labelClassName}>
                            Departamento <span style={{ color: 'red' }}>*</span>
                            <select name="departmentId" value={formData.departmentId} onChange={handleChange} className={getInputClassName('DepartmentId')} disabled={!canEditHeader}>
                                <option value="">-- Selecione --</option>
                                {departments.filter(d => d.isActive || Number(formData.departmentId) === d.id).map(d => (
                                    <option key={d.id} value={d.id}>{d.name}</option>
                                ))}
                            </select>
                            {renderFieldError('DepartmentId')}
                        </label>

                        <label className={labelClassName}>
                            Empresa <span style={{ color: 'red' }}>*</span>
                            <select 
                                name="companyId" 
                                value={formData.companyId} 
                                onChange={handleChange} 
                                className={getInputClassName('CompanyId')}
                                style={{
                                    ...(lineItemsCount > 0 || isQuotationPartiallyEditable || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                }}
                                disabled={!canEditHeader || isQuotationPartiallyEditable || lineItemsCount > 0}
                            >
                                <option value="">-- Selecione --</option>
                                {companies.filter(c => c.isActive || Number(formData.companyId) === c.id).map(c => (
                                    <option key={c.id} value={c.id}>{c.name}</option>
                                ))}
                            </select>
                            {lineItemsCount > 0 && (
                                <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                    Empresa bloqueada pois o pedido já possui itens.
                                </span>
                            )}
                            {renderFieldError('CompanyId')}
                        </label>

                        <label className={labelClassName}>
                            Planta <span style={{ color: 'red' }}>*</span>
                            <select 
                                name="plantId" 
                                value={formData.plantId} 
                                onChange={handleChange} 
                                className={getInputClassName('PlantId')}
                                style={{
                                    ...(!formData.companyId || isQuotationPartiallyEditable || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                }}
                                disabled={!canEditHeader || isQuotationPartiallyEditable || !formData.companyId}
                            >
                                <option value="">-- Selecione --</option>
                                {plants.filter(p => (p.isActive && (!formData.companyId || p.companyId === Number(formData.companyId))) || formData.plantId === p.id.toString()).map(p => (
                                    <option key={p.id} value={p.id}>{p.name}</option>
                                ))}
                            </select>
                            {!formData.companyId && (
                                <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                    Selecione uma empresa primeiro.
                                </span>
                            )}
                            {renderFieldError('PlantId')}
                        </label>
                    </div>

                </div>
            </section>
        </>
    );
}
