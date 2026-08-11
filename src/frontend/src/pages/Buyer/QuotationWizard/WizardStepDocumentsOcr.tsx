import React from 'react';
import { Trash2, AlertCircle, CheckCircle2, AlertTriangle, Hash, Calendar, Info } from 'lucide-react';
import { OcrDraft, OcrDraftItem, RequestDetailsDto } from '../../../types';
import { useQuotationWizardState } from './hooks/useQuotationWizardState';
import { SupplierAutocomplete } from '../../../components/SupplierAutocomplete';
import { formatCurrencyAO, formatDate } from '../../../lib/utils';
import { ConfirmationDialog } from '../../../components/common/ConfirmationDialog';
import { SourceDocumentTypeField } from '../../../components/requests/SourceDocumentTypeField';
import { useState } from 'react';

// --- FormattedNumberInput Component ---
interface FormattedNumberInputProps {
    value: number | undefined;
    onChange: (val: number) => void;
    style?: React.CSSProperties;
    placeholder?: string;
    currencyCode?: string; // If provided, uses formatCurrencyAO. If missing, formats as standard number (or percentage).
    isPercentage?: boolean;
}

const FormattedNumberInput: React.FC<FormattedNumberInputProps> = ({ value, onChange, style, placeholder, currencyCode, isPercentage }) => {
    const [isFocused, setIsFocused] = useState(false);
    const [localValue, setLocalValue] = useState(value !== undefined ? value.toString() : '');

    // Sync local value when external value changes and not focused
    React.useEffect(() => {
        if (!isFocused) {
            setLocalValue(value !== undefined ? value.toString() : '');
        }
    }, [value, isFocused]);

    const handleFocus = () => {
        setIsFocused(true);
        setLocalValue(value !== undefined ? value.toString() : '');
    };

    const handleBlur = () => {
        setIsFocused(false);
        const parsed = parseFloat(localValue.replace(',', '.'));
        if (!isNaN(parsed)) {
            onChange(parsed);
        } else if (localValue === '') {
            onChange(0); // or handle empty differently if needed
        }
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const val = e.target.value;
        // Allow numbers, commas, and dots
        if (/^[0-9.,]*$/.test(val)) {
            setLocalValue(val);
        }
    };

    const displayValue = isFocused 
        ? localValue 
        : (isPercentage 
            ? (value !== undefined ? `${value.toString().replace('.', ',')}%` : '')
            : formatCurrencyAO(value, currencyCode));

    return (
        <input
            type="text"
            value={displayValue}
            onChange={handleChange}
            onFocus={handleFocus}
            onBlur={handleBlur}
            style={style}
            placeholder={placeholder}
        />
    );
};
// --------------------------------------

interface WizardStepDocumentsOcrProps {
    draft: OcrDraft | null;
    isProcessingOcr: boolean;
    onUpload: (e: React.ChangeEvent<HTMLInputElement>) => void;
    onReplaceConfirm: () => void;
    wizardState: ReturnType<typeof useQuotationWizardState>;
    ivaRates: any[];
    units: any[];
    currencies: any[];
    /** Used only for the non-blocking "supplier already has other quotations" notice below. */
    request?: RequestDetailsDto | null;
}

export const WizardStepDocumentsOcr: React.FC<WizardStepDocumentsOcrProps> = ({
    draft,
    isProcessingOcr,
    onUpload,
    onReplaceConfirm,
    wizardState,
    ivaRates,
    units,
    currencies,
    request
}) => {
    const { updateDraftHeader, updateDraftItem, removeDraftItem } = wizardState;

    // Purely derived — no state, no effects. Recomputes on every render from the current
    // supplier and the request's current quotation list, so it naturally updates/disappears
    // when the supplier changes, is cleared, the document is replaced (new draft), or a
    // different request's wizard opens (new `request` prop) — nothing to clear explicitly.
    // Informational only: a supplier may legitimately submit multiple quotations for one
    // request (revision, complementary offer, alternative pricing, etc.) — this never blocks
    // advancing or saving.
    const existingSupplierQuotations = (draft?.supplierId != null && request?.quotations)
        ? request.quotations.filter((q: any) => q.supplierId === draft.supplierId && q.id !== wizardState.editingQuotationId)
        : [];

    // ── Shared inline style atoms ─────────────────────────────────────────────
    const labelStyle: React.CSSProperties = {
        display: 'block', fontSize: '10px', fontWeight: 900,
        textTransform: 'uppercase', letterSpacing: '0.05em',
        color: '#64748b', marginBottom: '4px',
    };
    const inputBase: React.CSSProperties = {
        width: '100%', padding: '8px 12px',
        // Longhand border props (not the `border` shorthand) so the error state can override only
        // borderColor without React warning about mixing shorthand/longhand across rerenders.
        backgroundColor: '#f8fafc', borderWidth: '1px', borderStyle: 'solid', borderColor: '#cbd5e1', borderRadius: '4px',
        fontWeight: 500, fontSize: '14px', color: '#0f172a',
        outline: 'none', transition: 'all 0.15s', boxSizing: 'border-box',
    };
    const inputError: React.CSSProperties = { ...inputBase, borderColor: '#ef4444', color: '#dc2626' };
    const inlineInputStyle: React.CSSProperties = {
        width: '100%', background: 'transparent',
        border: 'none', borderBottom: '1px solid transparent',
        outline: 'none', padding: '4px 0', fontWeight: 500,
        fontSize: '13px', color: '#0f172a', transition: 'border-color 0.15s',
        boxSizing: 'border-box',
    };

    const [showReplaceConfirm, setShowReplaceConfirm] = useState(false);
    const [pendingDeleteIndex, setPendingDeleteIndex] = useState<number | null>(null);

    const handleConfirmReplace = () => {
        setShowReplaceConfirm(false);
        onReplaceConfirm();
    };

    const handleConfirmDelete = () => {
        if (pendingDeleteIndex !== null) {
            removeDraftItem(pendingDeleteIndex, ivaRates);
        }
        setPendingDeleteIndex(null);
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div style={{ borderBottom: '1px solid var(--color-border)', paddingBottom: '16px' }}>
                <h3 style={{ fontSize: '1.125rem', fontWeight: 600, color: 'var(--color-text-main)', margin: '0 0 8px 0' }}>
                    Documento e Extração
                </h3>
                <p style={{ fontSize: '0.875rem', color: '#64748b', margin: 0 }}>
                    Revise os dados extraídos pelo OCR. Corrija fornecedor, datas, moeda, itens, impostos e totais antes de avançar.
                </p>
            </div>
            
            {!draft ? (
                <div style={{ border: '2px dashed #cbd5e1', borderRadius: '8px', padding: '48px', textAlign: 'center', backgroundColor: '#f8fafc' }}>
                    <p style={{ fontSize: '0.875rem', color: '#475569', marginBottom: '16px' }}>Faça upload da cotação (PDF ou Imagem) para iniciar a extração OCR automática.</p>
                    <input 
                        type="file" 
                        accept=".pdf,.png,.jpg,.jpeg" 
                        onChange={onUpload} 
                        style={{ display: 'none' }} 
                        id="wizard-file-upload" 
                        disabled={isProcessingOcr}
                    />
                    <label 
                        htmlFor="wizard-file-upload" 
                        style={{ 
                            display: 'inline-flex', alignItems: 'center', justifyContent: 'center', 
                            padding: '8px 16px', border: '1px solid transparent', fontSize: '0.875rem', 
                            fontWeight: 500, borderRadius: '6px', boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)', 
                            color: '#fff', 
                            backgroundColor: isProcessingOcr ? '#818cf8' : 'var(--color-primary)', 
                            cursor: isProcessingOcr ? 'not-allowed' : 'pointer'
                        }}
                    >
                        {isProcessingOcr ? 'Extraindo dados (OCR)...' : 'Selecionar Arquivo'}
                    </label>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    {/* Header Controls */}
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <h4 style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', margin: 0 }}>Dados da Cotação</h4>
                        <button onClick={() => setShowReplaceConfirm(true)} style={{ fontSize: '0.75rem', color: '#dc2626', fontWeight: 500, background: 'none', border: 'none', cursor: 'pointer', padding: 0 }}>
                            Substituir Documento
                        </button>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px' }}>
                        {/* Supplier */}
                        <div style={{ gridColumn: 'span 2' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: '4px' }}>
                                <label style={labelStyle}>Fornecedor</label>
                            </div>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                <SupplierAutocomplete
                                    initialName={draft.supplierNameSnapshot}
                                    onChange={(id: number | null, name: string, portalCode?: string, primaveraCode?: string, registrationStatus?: string) => {
                                        updateDraftHeader('supplierId', id, ivaRates);
                                        updateDraftHeader('supplierNameSnapshot', name, ivaRates);
                                        updateDraftHeader('supplierPortalCode', portalCode, ivaRates);
                                        updateDraftHeader('supplierPrimaveraCode', primaveraCode, ivaRates);
                                        updateDraftHeader('supplierRegistrationStatus', registrationStatus, ivaRates);
                                    }}
                                    hasWarning={!draft.supplierId && !!draft.supplierNameSnapshot}
                                />
                                {!draft.supplierId && draft.supplierNameSnapshot && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '10px', color: '#d97706', fontWeight: 700 }}>
                                        <AlertCircle style={{ width: 12, height: 12 }} />
                                        <span>Fornecedor sugerido pelo OCR não encontrado no sistema. Será possível criar ou vincular o fornecedor na etapa final.</span>
                                    </div>
                                )}
                            </div>
                        </div>

                        {/* Supplier-already-has-quotations notice — informational only, never blocks. */}
                        {existingSupplierQuotations.length > 0 && (
                            <div style={{ gridColumn: '1 / -1', display: 'flex', alignItems: 'flex-start', gap: '10px', padding: '14px 16px', backgroundColor: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '6px' }}>
                                <Info style={{ width: 18, height: 18, color: '#2563eb', flexShrink: 0, marginTop: '1px' }} />
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', flex: 1, minWidth: 0 }}>
                                    <div>
                                        <div style={{ fontWeight: 700, fontSize: '0.8125rem', color: '#1e3a8a' }}>
                                            Este fornecedor já possui outras cotações neste pedido
                                        </div>
                                        <div style={{ fontSize: '0.75rem', color: '#1e40af', marginTop: '2px', lineHeight: 1.5 }}>
                                            {draft.supplierNameSnapshot || 'Este fornecedor'} já apresentou {existingSupplierQuotations.length} cotação(ões) para este pedido. Verifique se este documento é uma nova proposta, uma revisão ou uma possível duplicação.
                                        </div>
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                        {existingSupplierQuotations.map((q: any) => (
                                            <div key={q.id} style={{ fontSize: '0.75rem', color: '#1e3a8a', display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: '8px', padding: '6px 8px', backgroundColor: '#dbeafe', borderRadius: '4px' }}>
                                                <span>Doc: {q.documentNumber || 'S/N'} • {q.documentDate ? formatDate(q.documentDate) : 'N/A'} • {q.itemCount ?? (q.items?.length || 0)} item(ns)</span>
                                                <span style={{ fontWeight: 700 }}>{q.currency || ''} {formatCurrencyAO(q.totalAmount || 0)}</span>
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            </div>
                        )}

                        {/* Doc Number */}
                        <div>
                            <label style={labelStyle}>Nº Documento</label>
                            <div style={{ position: 'relative' }}>
                                <Hash style={{ position: 'absolute', left: 10, top: 10, width: 16, height: 16, color: '#94a3b8' }} />
                                <input
                                    type="text"
                                    value={draft.documentNumber || ''}
                                    onChange={e => updateDraftHeader('documentNumber', e.target.value, ivaRates)}
                                    style={{ ...(!draft.documentNumber ? inputError : inputBase), paddingLeft: '34px' }}
                                    placeholder="Ex: 2024/001"
                                />
                            </div>
                        </div>

                        {/* Document identity — Release 2 corrected.
                            Describes WHAT the supplier issued, not what the workflow will do.
                            Sits immediately after the document number because both identify the
                            same artefact; the dates that follow describe when it applies.

                            Uses the same component as the Payment screen so the OCR suggestion, the
                            conflict modal and the pending-value rule behave identically in both
                            contexts — the Buyer cannot silently reclassify a document the extraction
                            already read either. */}
                        <div>
                            <SourceDocumentTypeField
                                context="QUOTATION_MANAGEMENT"
                                value={draft.documentType ?? ''}
                                onChange={val => updateDraftHeader('documentType', val, ivaRates)}
                                ocr={draft.documentClassification ?? null}
                                conflict={wizardState.classificationConflict}
                                onConflictChange={wizardState.setClassificationConflict}
                                error={!draft.documentType ? 'Selecione o tipo de documento anexado.' : null}
                                labelStyle={{ ...labelStyle, textTransform: 'none' }}
                                inputStyle={!draft.documentType ? inputError : inputBase}
                            />
                        </div>

                        {/* Doc Date */}
                        <div>
                            <label style={labelStyle}>Data Documento</label>
                            <div style={{ position: 'relative' }}>
                                <Calendar style={{ position: 'absolute', left: 10, top: 10, width: 16, height: 16, color: '#94a3b8' }} />
                                <input
                                    type="date"
                                    value={draft.documentDate || ''}
                                    onChange={e => updateDraftHeader('documentDate', e.target.value, ivaRates)}
                                    style={{ ...(!draft.documentDate ? inputError : inputBase), paddingLeft: '34px' }}
                                />
                            </div>
                        </div>

                        {/* Due Date */}
                        <div>
                            <label style={labelStyle}>Data de Vencimento da Cotação</label>
                            <div style={{ position: 'relative' }}>
                                <Calendar style={{ position: 'absolute', left: 10, top: 10, width: 16, height: 16, color: '#94a3b8' }} />
                                <input
                                    type="date"
                                    value={draft.dueDate || ''}
                                    onChange={e => updateDraftHeader('dueDate', e.target.value, ivaRates)}
                                    style={{ ...(!draft.dueDate ? inputError : inputBase), paddingLeft: '34px' }}
                                />
                            </div>
                            {!draft.dueDate && (
                                <div style={{ fontSize: '10px', color: '#ef4444', marginTop: '4px', fontWeight: 600 }}>
                                    Informe a data de vencimento da cotação antes de avançar.
                                </div>
                            )}
                        </div>

                        {/* Currency */}
                        <div>
                            <label style={labelStyle}>Moeda</label>
                            <select
                                value={draft.currency || ''}
                                onChange={e => updateDraftHeader('currency', e.target.value, ivaRates)}
                                style={!draft.currency ? inputError : inputBase}
                            >
                                <option value="">Selecione...</option>
                                {currencies.map(c => <option key={c.code} value={c.code}>{c.code} - {c.name}</option>)}
                            </select>
                            {draft.extractedCurrency && !draft.currency && (
                                <div style={{ marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontSize: '10px', color: '#d97706', fontWeight: 700 }}>
                                    <AlertCircle style={{ width: 12, height: 12 }} />
                                    <span>Sugestão OCR: {draft.extractedCurrency}</span>
                                </div>
                            )}
                        </div>
                    </div>

                    {/* OCR Warnings Banners */}
                    {draft.globalVatInferred && draft.inferredVatRatePercent !== undefined && (
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', backgroundColor: '#f0fdf4', border: '1px solid #86efac', borderRadius: '4px', color: '#166534', fontSize: '12px', fontWeight: 600 }}>
                            <CheckCircle2 style={{ width: 16, height: 16, flexShrink: 0 }} />
                            <span>IVA global de {draft.inferredVatRatePercent}% identificado no resumo do documento e aplicado automaticamente a todos os itens.</span>
                        </div>
                    )}

                    {!draft.globalVatInferred && draft.headerHasIva && draft.items.some(i => i.ivaUncertain) && (
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', backgroundColor: '#fef2f2', border: '1px solid #fca5a5', borderRadius: '4px', color: '#b91c1c', fontSize: '12px', fontWeight: 600 }}>
                            <AlertTriangle style={{ width: 16, height: 16, flexShrink: 0 }} />
                            <span><strong>Atenção:</strong> O documento contém IVA nos totais, mas o IVA por item não foi identificado pelo OCR. Verifique e corrija manualmente o campo IVA de cada item.</span>
                        </div>
                    )}

                    {draft.ocrTotalAmount !== undefined && Math.abs(draft.totalAmount - draft.ocrTotalAmount) > 0.05 && (
                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', padding: '12px', backgroundColor: '#fffbeb', border: '1px solid #fcd34d', borderRadius: '4px', color: '#b45309', fontSize: '12px', fontWeight: 500 }}>
                            <AlertTriangle style={{ width: 16, height: 16, flexShrink: 0, marginTop: '2px' }} />
                            <div>
                                <div style={{ fontWeight: 600, marginBottom: '4px' }}>O total calculado difere do total extraído pelo OCR. Verifique os valores das linhas antes de avançar.</div>
                                <div>Total extraído pelo OCR: <strong>{formatCurrencyAO(draft.ocrTotalAmount)}</strong></div>
                                <div>Total calculado: <strong>{formatCurrencyAO(draft.totalAmount)}</strong></div>
                                <div>Diferença: <strong>{formatCurrencyAO(Math.abs(draft.totalAmount - draft.ocrTotalAmount))}</strong></div>
                            </div>
                        </div>
                    )}

                    {/* Items Editor */}
                    <div style={{ border: '1px solid var(--color-border)', borderRadius: '6px', overflow: 'hidden' }}>
                        <div style={{ overflowX: 'auto' }}>
                            <table style={{ width: '100%', fontSize: '13px', borderCollapse: 'collapse' }}>
                                <thead style={{ backgroundColor: '#f8fafc', borderBottom: '1px solid #e2e8f0' }}>
                                    <tr>
                                        {[
                                            { label: '#', w: 40, align: 'center' as const },
                                            { label: 'Descrição do Item', w: 250, align: 'left' as const },
                                            { label: 'Qtd', w: 70, align: 'left' as const },
                                            { label: 'Unidade', w: 90, align: 'left' as const },
                                            { label: 'Preço Unit', w: 100, align: 'right' as const },
                                            { label: 'Desc. Item', w: 90, align: 'right' as const },
                                            { label: 'Taxa IVA', w: 120, align: 'left' as const },
                                            { label: 'Total c/ IVA', w: 110, align: 'right' as const },
                                            { label: '', w: 40, align: 'left' as const },
                                        ].map(col => (
                                            <th key={col.label} style={{ padding: '8px 12px', fontSize: '10px', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#64748b', width: col.w, textAlign: col.align }}>
                                                {col.label}
                                            </th>
                                        ))}
                                    </tr>
                                </thead>
                                <tbody>
                                    {draft.items.map((item: OcrDraftItem, idx: number) => (
                                        <tr key={idx} style={{ borderBottom: '1px solid #f1f5f9' }}>
                                            <td style={{ padding: '8px 12px', textAlign: 'center', fontWeight: 600, color: '#94a3b8' }}>{item.lineNumber}</td>
                                            
                                            <td style={{ padding: '8px 12px' }}>
                                                <input
                                                    type="text"
                                                    value={item.description || ''}
                                                    onChange={e => updateDraftItem(idx, 'description', e.target.value, ivaRates)}
                                                    style={inlineInputStyle}
                                                    placeholder="Descrição extraída..."
                                                />
                                            </td>

                                            <td style={{ padding: '8px 12px' }}>
                                                <FormattedNumberInput
                                                    value={item.quantity}
                                                    onChange={val => updateDraftItem(idx, 'quantity', val, ivaRates)}
                                                    style={{ ...inlineInputStyle, textAlign: 'center' }}
                                                />
                                            </td>

                                            <td style={{ padding: '8px 12px', position: 'relative' }}>
                                                <select
                                                    value={item.unitId || ''}
                                                    onChange={e => updateDraftItem(idx, 'unitId', parseInt(e.target.value) || null, ivaRates)}
                                                    style={{ ...inlineInputStyle, fontSize: '12px' }}
                                                >
                                                    <option value="">Unid...</option>
                                                    {units.filter(u => u.isActive !== false || u.id === item.unitId).map(u => (
                                                        <option key={u.id} value={u.id}>{u.code}</option>
                                                    ))}
                                                </select>
                                                {item.unit && !item.unitId && (
                                                    <div style={{ position: 'absolute', top: '100%', zIndex: 10, fontSize: '9px', color: '#d97706', backgroundColor: '#fffbeb', padding: '2px 4px', border: '1px solid #fcd34d' }}>
                                                        Sug: {item.unit}
                                                    </div>
                                                )}
                                            </td>

                                            <td style={{ padding: '8px 12px' }}>
                                                <FormattedNumberInput
                                                    value={item.unitPrice}
                                                    onChange={val => updateDraftItem(idx, 'unitPrice', val, ivaRates)}
                                                    style={{ ...inlineInputStyle, textAlign: 'right' }}
                                                    currencyCode={draft.currency}
                                                />
                                            </td>

                                            <td style={{ padding: '8px 12px' }}>
                                                <FormattedNumberInput
                                                    value={item.discountAmount}
                                                    onChange={val => updateDraftItem(idx, 'discountAmount', val, ivaRates)}
                                                    style={{ ...inlineInputStyle, textAlign: 'right' }}
                                                    currencyCode={draft.currency}
                                                />
                                            </td>

                                            <td style={{ padding: '8px 12px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <select
                                                        value={item.ivaRateId || ''}
                                                        onChange={e => {
                                                            updateDraftItem(idx, 'ivaRateId', parseInt(e.target.value) || null, ivaRates);
                                                            updateDraftItem(idx, 'ivaUncertain', false, ivaRates); // Clear uncertainty when user explicitly selects
                                                        }}
                                                        style={{
                                                            ...inlineInputStyle, fontSize: '12px',
                                                            ...(item.ivaUncertain ? { borderBottomColor: '#ef4444', borderBottomWidth: '2px', backgroundColor: 'rgba(239,68,68,0.04)' } : {})
                                                        }}
                                                    >
                                                        <option value="">IVA...</option>
                                                        {ivaRates.filter((r: { isActive: boolean }) => r.isActive).map((r: { id: number, name: string, ratePercent: number }) => (
                                                            <option key={r.id} value={r.id}>{r.name} ({r.ratePercent}%)</option>
                                                        ))}
                                                    </select>
                                                    {draft.items.some((i: OcrDraftItem) => i.ivaUncertain) && (
                                                        <span style={{ fontSize: '9px', color: '#ef4444', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '2px' }}>
                                                            <AlertCircle style={{ width: 10, height: 10, flexShrink: 0 }} />
                                                            Por favor confirme
                                                        </span>
                                                    )}
                                                </div>
                                            </td>

                                            <td style={{ padding: '8px 12px', textAlign: 'right', fontWeight: 600, color: '#0f172a' }}>
                                                {formatCurrencyAO(item.totalPrice || 0)} {draft.currency || ''}
                                            </td>

                                            <td style={{ padding: '8px 12px' }}>
                                                <button onClick={() => setPendingDeleteIndex(idx)} style={{ color: '#cbd5e1', background: 'none', border: 'none', cursor: 'pointer' }}>
                                                    <Trash2 style={{ width: 16, height: 16 }} />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </div>

                    {/* Totals Panel */}
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', flexWrap: 'wrap', gap: '16px' }}>
                        <div style={{ width: '220px' }}>
                            <label style={labelStyle}>Desconto Comercial Global</label>
                            <FormattedNumberInput
                                value={draft.discountAmount}
                                onChange={val => updateDraftHeader('discountAmount', val, ivaRates)}
                                style={{ ...inputBase, textAlign: 'right' }}
                                currencyCode={draft.currency}
                            />
                        </div>

                        <div style={{ backgroundColor: '#eff6ff', color: '#1e3a8a', padding: '16px', borderRadius: '8px', border: '1px solid #bfdbfe', minWidth: '300px' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem', color: '#1e40af' }}>
                                    <span style={{ fontWeight: 500 }}>Subtotal s/ IVA:</span>
                                    {(() => {
                                        const gross = draft.items.reduce((sum: number, item: OcrDraftItem) => sum + Math.max(0, ((item.quantity || 0) * (item.unitPrice || 0)) - (item.discountAmount || 0)), 0);
                                        const taxableBase = Math.max(0, gross - (draft.discountAmount || 0));
                                        return <span>{formatCurrencyAO(taxableBase)} {draft.currency || ''}</span>;
                                    })()}
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem', color: '#1e40af', borderBottom: '1px solid #bfdbfe', paddingBottom: '8px' }}>
                                    <span style={{ fontWeight: 500 }}>Total IVA:</span>
                                    {(() => {
                                        const gross = draft.items.reduce((sum: number, item: OcrDraftItem) => sum + Math.max(0, ((item.quantity || 0) * (item.unitPrice || 0)) - (item.discountAmount || 0)), 0);
                                        const iva = draft.items.reduce((sum: number, item: OcrDraftItem) => {
                                            const g = Math.max(0, ((item.quantity || 0) * (item.unitPrice || 0)) - (item.discountAmount || 0));
                                            const rate = ivaRates.find(r => r.id === item.ivaRateId)?.ratePercent || 0;
                                            return sum + (g * rate / 100);
                                        }, 0);
                                        const taxableBase = Math.max(0, gross - (draft.discountAmount || 0));
                                        const discountRatio = gross > 0 ? (taxableBase / gross) : 1;
                                        const adjustedIva = Math.round(iva * discountRatio * 100) / 100;
                                        return <span>{formatCurrencyAO(adjustedIva)} {draft.currency || ''}</span>;
                                    })()}
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem', color: '#1e3a8a', paddingTop: '8px' }}>
                                    <span style={{ fontWeight: 700 }}>Total da Cotação:</span>
                                    <span style={{ fontWeight: 700, fontSize: '1.25rem' }}>{formatCurrencyAO(draft.totalAmount || 0)} {draft.currency || ''}</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            )}
            
            {showReplaceConfirm && (
                <ConfirmationDialog
                    title="Substituir documento da cotação?"
                    message="Substituir o documento apagará os dados extraídos atuais desta cotação. Deseja continuar?"
                    confirmText="Substituir Documento"
                    cancelText="Cancelar"
                    variant="destructive"
                    onConfirm={handleConfirmReplace}
                    onCancel={() => setShowReplaceConfirm(false)}
                />
            )}

            {pendingDeleteIndex !== null && (
                <ConfirmationDialog
                    title="Remover item extraído?"
                    message="Este item foi extraído do documento. Removê-lo pode gerar uma divergência no total da cotação e exigir uma justificativa ao salvar. Deseja continuar?"
                    confirmText="Remover item"
                    cancelText="Cancelar"
                    variant="destructive"
                    onConfirm={handleConfirmDelete}
                    onCancel={() => setPendingDeleteIndex(null)}
                />
            )}
        </div>
    );
};
