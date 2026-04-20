import { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Plus, Upload, Trash2, RefreshCcw, Hash, Calendar, CheckCircle2, AlertCircle, AlertTriangle } from 'lucide-react';
import { api } from '../lib/api';
import { SupplierAutocomplete } from './SupplierAutocomplete';
import { CatalogItemAutocomplete } from './CatalogItemAutocomplete';
import { QuotationDraft, QuotationDraftItem, IvaRate, Unit } from '../types/quotation';
import { formatCurrencyAO, computeFileHash, formatDateTime } from '../lib/utils';
import { Feedback, FeedbackType } from './ui/Feedback';

interface QuotationEntryProps {
    requestId: string;
    ivaRates: IvaRate[];
    units: Unit[];
    currencies: any[];
    onComplete: () => void;
    highlighted?: boolean;
}

export function QuotationEntry({
    requestId,
    ivaRates: initialIvaRates,
    units: initialUnits,
    currencies: initialCurrencies,
    onComplete,
    highlighted
}: QuotationEntryProps) {
    const [step, setStep] = useState<'SELECT' | 'UPLOAD' | 'EDIT'>(highlighted ? 'UPLOAD' : 'SELECT');
    const [isProcessing, setIsProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string } | null>(null);
    const [duplicateWarning, setDuplicateWarning] = useState<{ isOpen: boolean; requestNumber: string; uploadCallback: () => void; uploadedBy?: string; createdAtUtc?: string } | null>(null);

    const [ivaRates, setIvaRates] = useState<IvaRate[]>(initialIvaRates || []);
    const [units, setUnits] = useState<Unit[]>(initialUnits || []);
    const [currencies, setCurrencies] = useState<any[]>(initialCurrencies || []);

    const [formErrors, setFormErrors] = useState<Record<string, string>>({});

    const [draft, setDraft] = useState<QuotationDraft>({
        supplierId: null,
        supplierNameSnapshot: '',
        documentNumber: '',
        documentDate: new Date().toISOString().split('T')[0],
        currency: '',
        discountAmount: 0,
        totalAmount: 0,
        items: []
    });

    const [ocrResult, setOcrResult] = useState<any>(null);
    const fileInputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        if (initialIvaRates?.length) setIvaRates(initialIvaRates);
        if (initialUnits?.length) setUnits(initialUnits);
        if (initialCurrencies?.length) setCurrencies(initialCurrencies);
    }, [initialIvaRates, initialUnits, initialCurrencies]);

    const resolveCurrencyAlias = (val: string | undefined) => {
        if (!val) return '';
        const normalized = val.trim().toUpperCase().replace(/\.$/, '');
        if (currencies.some(c => c.code.toUpperCase() === normalized)) return normalized;

        const aliases: Record<string, string[]> = {
            'AOA': ['AKZ', 'KWANZA'],
            'USD': ['US$', 'DÓLAR', 'DOLAR'],
            'EUR': ['€', 'EURO']
        };

        for (const [code, items] of Object.entries(aliases)) {
            if (items.includes(normalized) && currencies.some(c => c.code.toUpperCase() === code)) {
                return code;
            }
        }
        return '';
    };

    const resolveUnitAlias = (val: string | undefined): number | null => {
        if (!val) return null;
        const normalized = val.trim().toUpperCase().replace(/\.$/, '');
        const activeUnits = units.filter(u => u.isActive !== false);
        const directMatch = activeUnits.find(u => u.code.toUpperCase() === normalized || u.name.toUpperCase() === normalized);
        if (directMatch) return directMatch.id;

        const unitAliases: Record<string, string[]> = {
            'UN': ['UN', 'UND', 'UNID', 'UNIDADE', 'EA', 'EACH', 'PC', 'PCS', 'PÇ', 'PÇS'],
            'KG': ['KG', 'KGS', 'QUILO', 'QUILOGRAMA', 'KILO', 'KILOGRAM', 'KILOGRAMS'],
            'L': ['L', 'LT', 'LTS', 'LITRO', 'LITROS', 'LITER', 'LITERS'],
            'M': ['M', 'MT', 'MTS', 'METRO', 'METROS', 'METER', 'METERS'],
            'CX': ['CX', 'CXA', 'CAIXA', 'CAIXAS', 'BOX', 'BOXES', 'CTN', 'CARTON']
        };

        for (const [canonicalCode, aliases] of Object.entries(unitAliases)) {
            if (aliases.includes(normalized)) {
                const matchedUnit = activeUnits.find(u => u.code.toUpperCase() === canonicalCode);
                if (matchedUnit) return matchedUnit.id;
            }
        }
        return null;
    };

    const _processUpload = async (file: File) => {
        setIsProcessing(true);
        try {
            const uploadRes = await api.attachments.upload(requestId, [file], 'PROFORMA');
            const fileId = Array.isArray(uploadRes) ? uploadRes[0]?.id : uploadRes.id;

            const result = await api.requests.ocrExtract(requestId, fileId);
            setOcrResult(result);

            const suggestions = result.integration?.headerSuggestions;
            const extractedSupplierName = suggestions?.supplierName || '';
            const extractedSupplierTaxId = suggestions?.supplierTaxId || '';
            const isTaxIdPlausible = extractedSupplierTaxId.length > 5;

            let matchedSupplierId: number | null = null;
            if (isTaxIdPlausible) {
                try {
                    const suppliers = await api.lookups.searchSuppliers(extractedSupplierTaxId);
                    if (suppliers.length === 1) matchedSupplierId = suppliers[0].id;
                } catch { /* ignore */ }
            }

            const initialDraft: QuotationDraft = {
                supplierId: matchedSupplierId,
                supplierNameSnapshot: extractedSupplierName,
                supplierTaxId: isTaxIdPlausible ? extractedSupplierTaxId : '',
                documentNumber: suggestions?.documentNumber?.value || '',
                documentDate: suggestions?.documentDate?.value || '',
                currency: resolveCurrencyAlias(suggestions?.currency?.value),
                extractedCurrency: suggestions?.currency?.value,
                discountAmount: suggestions?.discountAmount?.value || 0,
                totalAmount: suggestions?.grandTotal?.value || 0,
                proformaAttachmentId: fileId,
                items: (result.integration?.lineItemSuggestions || []).map((item: any, index: number) => {
                    const extractedUnit = item.unit || '';
                    const matchedUnitId = resolveUnitAlias(extractedUnit);
                    const safeQty = item.quantity || 1;
                    const safePrice = item.unitPrice || 0;
                    const grossSubtotal = Math.round(safeQty * safePrice * 100) / 100;
                    const ivaUncertain = (item.taxRate === undefined || item.taxRate === null);

                    return {
                        lineNumber: index + 1,
                        description: item.description || '',
                        quantity: safeQty,
                        unitId: matchedUnitId,
                        unit: extractedUnit,
                        unitPrice: safePrice,
                        ivaRateId: (() => {
                            const rate = item.taxRate;
                            if (rate === undefined || rate === null) return null;
                            const matched = ivaRates.find(r => Math.abs(r.ratePercent - rate) < 0.01);
                            return matched ? matched.id : null;
                        })(),
                        taxRate: item.taxRate,
                        totalPrice: grossSubtotal,
                        ivaUncertain
                    };
                })
            };

            const ocrGrandTotal = suggestions?.grandTotal?.value || suggestions?.totalAmount?.value || 0;
            const itemSubtotalGross = initialDraft.items.reduce((sum, item) => {
                return sum + Math.max(0, (item.quantity || 0) * (item.unitPrice || 0));
            }, 0) - (initialDraft.discountAmount || 0);

            let headerImpliesIva = false;
            if (ocrGrandTotal > 0 && itemSubtotalGross > 0) {
                const impliedIvaRatio = (ocrGrandTotal - itemSubtotalGross) / itemSubtotalGross;
                headerImpliesIva = impliedIvaRatio > 0.01;
            }

            if (headerImpliesIva) {
                const allItemsZeroOrUncertain = initialDraft.items.every(
                    item => item.ivaUncertain || item.taxRate === 0
                );
                if (allItemsZeroOrUncertain) {
                    initialDraft.items.forEach(item => {
                        if (item.taxRate === 0) {
                            item.ivaUncertain = true;
                            item.ivaRateId = null;
                        }
                    });
                }
            }

            const hasUncertainItems = initialDraft.items.some(item => item.ivaUncertain);
            initialDraft.headerHasIva = headerImpliesIva && hasUncertainItems;
            initialDraft.totalAmount = recalculateQuotationTotal(initialDraft);
            setDraft(initialDraft);
            setStep('EDIT');
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Falha ao salvar cotação.' });
        } finally {
            setIsProcessing(false);
        }
    };

    const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        if (fileInputRef.current) fileInputRef.current.value = '';

        setIsProcessing(true);
        setFeedback(null);
        let hasDuplicateWarning = false;

        try {
            const hash = await computeFileHash(file);
            const dupCheck = await api.attachments.checkDuplicate(hash);
            if (dupCheck.isDuplicate) {
                hasDuplicateWarning = true;
                setDuplicateWarning({
                    isOpen: true,
                    requestNumber: dupCheck.requestNumber || 'Desconhecido',
                    uploadedBy: dupCheck.uploadedBy,
                    createdAtUtc: dupCheck.createdAtUtc,
                    uploadCallback: () => {
                        setDuplicateWarning(null);
                        _processUpload(file);
                    }
                });
                return;
            }
        } catch (err) {
            console.error('Duplicate check failed', err);
        } finally {
            if (!hasDuplicateWarning) setIsProcessing(false);
        }

        _processUpload(file);
    };

    const recalculateQuotationTotal = (d: QuotationDraft) => {
        let gross = 0;
        let ivaTotal = 0;
        d.items.forEach(item => {
            const itemGrossRaw = Math.round(((item.quantity || 0) * (item.unitPrice || 0)) * 100) / 100;
            const itemDiscount = item.discountAmount || 0;
            const itemNet = Math.max(0, itemGrossRaw - itemDiscount);
            gross += itemNet;
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            ivaTotal += Math.round(itemNet * (ivaPercent / 100) * 100) / 100;
        });
        const discount = d.discountAmount || 0;
        const taxableBase = Math.max(0, gross - discount);
        const discountRatio = gross > 0 ? (taxableBase / gross) : 1;
        const adjustedIva = Math.round(ivaTotal * discountRatio * 100) / 100;
        return Math.max(0, Math.round((taxableBase + adjustedIva) * 100) / 100);
    };

    const updateHeader = (field: keyof QuotationDraft, value: any) => {
        setDraft(prev => {
            const next = { ...prev, [field]: value };
            if (field === 'currency' && value) next.extractedCurrency = undefined;
            if (field === 'discountAmount') next.totalAmount = recalculateQuotationTotal(next);
            return next;
        });
    };

    const updateItem = (index: number, field: keyof QuotationDraftItem, value: any) => {
        setDraft(prev => {
            const items = [...prev.items];
            const item = { ...items[index], [field]: value };
            const safeQty = item.quantity || 0;
            const safePrice = item.unitPrice || 0;
            const grossSubtotal = Math.round(safeQty * safePrice * 100) / 100;
            const itemDiscount = item.discountAmount || 0;
            const netSubtotal = Math.max(0, grossSubtotal - itemDiscount);
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            const ivaAmount = Math.round(netSubtotal * (ivaPercent / 100) * 100) / 100;
            item.totalPrice = Math.round((netSubtotal + ivaAmount) * 100) / 100;
            items[index] = item;
            const next = { ...prev, items };
            next.totalAmount = recalculateQuotationTotal(next);
            return next;
        });
    };

    const addItem = () => {
        setDraft(prev => {
            const newItem: QuotationDraftItem = {
                lineNumber: prev.items.length + 1,
                description: '',
                quantity: 1,
                unitId: null,
                unitPrice: 0,
                ivaRateId: null,
                totalPrice: 0,
                itemCatalogId: null,
                itemCatalogCode: null
            };
            return { ...prev, items: [...prev.items, newItem] };
        });
    };

    const removeItem = (index: number) => {
        setDraft(prev => {
            const items = prev.items.filter((_, i) => i !== index).map((it, i) => ({ ...it, lineNumber: i + 1 }));
            const next = { ...prev, items };
            next.totalAmount = recalculateQuotationTotal(next);
            return next;
        });
    };

    const handleSave = async () => {
        const errors: Record<string, string> = {};
        if (!draft.supplierId) errors.supplierId = 'Fornecedor é obrigatório.';
        if (!draft.documentNumber) errors.documentNumber = 'Nº Documento é obrigatório.';
        if (!draft.documentDate) errors.documentDate = 'Data é obrigatória.';
        if (!draft.currency) errors.currency = 'Moeda é obrigatória.';
        if (draft.items.length === 0) setFeedback({ type: 'error', message: 'Adicione pelo menos um item.' });

        if (Object.keys(errors).length > 0) {
            setFormErrors(errors);
            return;
        }

        setIsProcessing(true);
        try {
            await api.requests.saveQuotation(requestId, {
                supplierId: draft.supplierId!,
                supplierNameSnapshot: draft.supplierNameSnapshot,
                documentNumber: draft.documentNumber,
                documentDate: draft.documentDate,
                currency: draft.currency,
                discountAmount: draft.discountAmount,
                totalAmount: draft.totalAmount,
                attachmentId: draft.proformaAttachmentId,
                items: draft.items.map(it => ({
                    description: it.description,
                    quantity: it.quantity,
                    unitId: it.unitId!,
                    unitPrice: it.unitPrice,
                    ivaRateId: it.ivaRateId,
                    itemCatalogId: it.itemCatalogId || null,
                    lineTotal: it.totalPrice
                }))
            });
            onComplete();
        } catch (err: any) {
            setFeedback({ type: 'error', message: 'Por favor, corrija os erros no formulário antes de salvar.' });
        } finally {
            setIsProcessing(false);
        }
    };

    // ── Shared inline style atoms ─────────────────────────────────────────────
    const labelStyle: React.CSSProperties = {
        display: 'block', fontSize: '10px', fontWeight: 900,
        textTransform: 'uppercase', letterSpacing: '0.05em',
        color: '#64748b', marginBottom: '4px',
    };
    const inputBase: React.CSSProperties = {
        width: '100%', padding: '8px 12px',
        backgroundColor: '#f8fafc', border: '2px solid #e2e8f0',
        fontWeight: 700, fontSize: '14px', color: '#0f172a',
        outline: 'none', transition: 'all 0.15s', boxSizing: 'border-box',
    };
    const inputError: React.CSSProperties = { ...inputBase, borderColor: '#ef4444', color: '#dc2626' };
    const inlineInputStyle: React.CSSProperties = {
        width: '100%', background: 'transparent',
        border: 'none', borderBottom: '1px solid transparent',
        outline: 'none', padding: '4px 0', fontWeight: 700,
        fontSize: '13px', color: '#0f172a', transition: 'border-color 0.15s',
        boxSizing: 'border-box',
    };

    return (
        <div style={{
            backgroundColor: '#fff',
            border: '2px solid #0f172a',
            boxShadow: '4px 4px 0px 0px rgba(15,23,42,1)',
            padding: '24px',
        }}>
            <AnimatePresence mode="wait">
                {step === 'UPLOAD' ? (
                    <motion.div
                        key="upload"
                        initial={{ opacity: 0, y: 10 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -10 }}
                        style={{ textAlign: 'center', padding: '32px 0' }}
                    >
                        <Upload style={{ width: 48, height: 48, margin: '0 auto 16px', color: '#94a3b8', display: 'block' }} />
                        <h3 style={{ fontSize: '1.25rem', fontWeight: 900, color: '#0f172a', marginBottom: '8px', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Carregar Proforma / Orçamento
                        </h3>
                        <p style={{ color: '#64748b', marginBottom: '32px', maxWidth: '420px', margin: '0 auto 32px' }}>
                            O sistema utilizará Inteligência Artificial para extrair automaticamente fornecedor, itens e valores do PDF.
                        </p>

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', alignItems: 'center' }}>
                            <button
                                onClick={() => fileInputRef.current?.click()}
                                disabled={isProcessing}
                                style={{
                                    padding: '12px 32px', backgroundColor: isProcessing ? '#64748b' : '#dc2626',
                                    color: '#fff', fontWeight: 900, textTransform: 'uppercase',
                                    fontSize: '13px', letterSpacing: '0.1em',
                                    border: 'none', cursor: isProcessing ? 'not-allowed' : 'pointer',
                                    display: 'flex', alignItems: 'center', gap: '8px',
                                    transition: 'background-color 0.2s',
                                }}
                                onMouseEnter={e => { if (!isProcessing) e.currentTarget.style.backgroundColor = '#0f172a'; }}
                                onMouseLeave={e => { if (!isProcessing) e.currentTarget.style.backgroundColor = '#dc2626'; }}
                            >
                                {isProcessing
                                    ? <RefreshCcw style={{ width: 20, height: 20, animation: 'spin 1s linear infinite' }} />
                                    : <Upload style={{ width: 20, height: 20 }} />
                                }
                                {isProcessing ? 'Processando OCR...' : 'Selecionar Documento (PDF)'}
                            </button>

                            <button
                                onClick={() => setStep('EDIT')}
                                disabled={isProcessing}
                                style={{
                                    color: '#64748b', fontWeight: 700, fontSize: '13px',
                                    background: 'none', border: 'none', cursor: 'pointer',
                                    display: 'flex', alignItems: 'center', gap: '4px',
                                    transition: 'color 0.15s',
                                }}
                                onMouseEnter={e => e.currentTarget.style.color = '#0f172a'}
                                onMouseLeave={e => e.currentTarget.style.color = '#64748b'}
                            >
                                <Plus style={{ width: 16, height: 16 }} />
                                Introduzir Dados Manualmente
                            </button>
                        </div>

                        <input
                            type="file"
                            ref={fileInputRef}
                            onChange={handleFileUpload}
                            accept=".pdf,image/*"
                            style={{ display: 'none' }}
                        />

                        {feedback && (
                            <div style={{ marginTop: '24px' }}>
                                <Feedback type={feedback.type} message={feedback.message} />
                            </div>
                        )}
                    </motion.div>
                ) : (
                    <motion.div
                        key="edit"
                        initial={{ opacity: 0, scale: 0.98 }}
                        animate={{ opacity: 1, scale: 1 }}
                        style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}
                    >
                        {/* Header fields */}
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px' }}>
                            {/* Supplier — spans 2 cols on wider screens */}
                            <div style={{ gridColumn: 'span 2' }}>
                                <label style={labelStyle}>Fornecedor</label>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                    <SupplierAutocomplete
                                        initialName={draft.supplierNameSnapshot}
                                        onChange={(id, name) => {
                                            updateHeader('supplierId', id);
                                            updateHeader('supplierNameSnapshot', name);
                                            setFormErrors(prev => ({ ...prev, supplierId: '' }));
                                        }}
                                        hasError={!!formErrors.supplierId}
                                    />
                                    {ocrResult && !draft.supplierId && draft.supplierNameSnapshot && (
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '10px', color: '#d97706', fontWeight: 700 }}>
                                            <AlertCircle style={{ width: 12, height: 12 }} />
                                            <span>Sugerido pelo OCR: "{draft.supplierNameSnapshot}"</span>
                                        </div>
                                    )}
                                </div>
                            </div>

                            {/* Doc Number */}
                            <div>
                                <label style={labelStyle}>Nº Documento</label>
                                <div style={{ position: 'relative' }}>
                                    <Hash style={{ position: 'absolute', left: 10, top: 10, width: 16, height: 16, color: '#94a3b8' }} />
                                    <input
                                        type="text"
                                        value={draft.documentNumber}
                                        onChange={e => updateHeader('documentNumber', e.target.value)}
                                        style={{ ...(formErrors.documentNumber ? inputError : inputBase), paddingLeft: '34px' }}
                                        placeholder="Ex: 2024/001"
                                    />
                                </div>
                            </div>

                            {/* Doc Date */}
                            <div>
                                <label style={labelStyle}>Data Documento</label>
                                <div style={{ position: 'relative' }}>
                                    <Calendar style={{ position: 'absolute', left: 10, top: 10, width: 16, height: 16, color: '#94a3b8' }} />
                                    <input
                                        type="date"
                                        value={draft.documentDate}
                                        onChange={e => updateHeader('documentDate', e.target.value)}
                                        style={{ ...(formErrors.documentDate ? inputError : inputBase), paddingLeft: '34px' }}
                                    />
                                </div>
                            </div>

                            {/* Currency */}
                            <div>
                                <label style={labelStyle}>Moeda</label>
                                <select
                                    value={draft.currency}
                                    onChange={e => updateHeader('currency', e.target.value)}
                                    style={formErrors.currency ? inputError : inputBase}
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

                        {/* Items Table */}
                        <div style={{ overflowX: 'auto', border: '2px solid #e2e8f0' }}>
                            <table style={{ width: '100%', fontSize: '13px', borderCollapse: 'collapse' }}>
                                <thead style={{ backgroundColor: '#f8fafc', borderBottom: '2px solid #e2e8f0' }}>
                                    <tr>
                                        {[
                                            { label: '#', w: 48, align: 'center' as const },
                                            { label: 'Descrição do Item', w: 300, align: 'left' as const },
                                            { label: 'Qtd', w: 80, align: 'left' as const },
                                            { label: 'Unidade', w: 100, align: 'left' as const },
                                            { label: 'Preço Unit', w: 120, align: 'right' as const },
                                            { label: 'Taxa IVA', w: 130, align: 'left' as const },
                                            { label: 'Total c/ IVA', w: 130, align: 'right' as const },
                                            { label: '', w: 40, align: 'left' as const },
                                        ].map(col => (
                                            <th key={col.label} style={{
                                                padding: '8px 16px', fontSize: '10px', fontWeight: 900,
                                                textTransform: 'uppercase', letterSpacing: '0.05em',
                                                color: '#64748b', width: col.w, textAlign: col.align,
                                            }}>
                                                {col.label}
                                            </th>
                                        ))}
                                    </tr>
                                </thead>
                                <tbody>
                                    {draft.items.map((item, idx) => (
                                        <tr key={idx} style={{ borderBottom: '1px solid #f1f5f9' }}
                                            onMouseEnter={e => e.currentTarget.style.backgroundColor = 'rgba(248,250,252,0.5)'}
                                            onMouseLeave={e => e.currentTarget.style.backgroundColor = 'transparent'}
                                        >
                                            <td style={{ padding: '12px 16px', textAlign: 'center', fontWeight: 700, color: '#94a3b8' }}>{item.lineNumber}</td>

                                            <td style={{ padding: '12px 16px', minWidth: '300px' }}>
                                                <CatalogItemAutocomplete
                                                    value={item.itemCatalogCode ? `[${item.itemCatalogCode}] ${item.description}` : item.description}
                                                    itemCatalogId={item.itemCatalogId ?? null}
                                                    onChange={(description, catalogId, catalogCode, defaultUnitId) => {
                                                        setDraft(prev => {
                                                            const items = [...prev.items];
                                                            const current = { ...items[idx] };
                                                            current.description = description;
                                                            current.itemCatalogId = catalogId;
                                                            current.itemCatalogCode = catalogCode;
                                                            if (catalogId && defaultUnitId) {
                                                                const matchingUnit = units.find(u => u.id === defaultUnitId);
                                                                if (matchingUnit) current.unitId = matchingUnit.id;
                                                            }
                                                            items[idx] = current;
                                                            const next = { ...prev, items };
                                                            next.totalAmount = recalculateQuotationTotal(next);
                                                            return next;
                                                        });
                                                    }}
                                                    placeholder="Pesquisar catálogo ou digitar descrição..."
                                                    style={{
                                                        border: 'none', borderBottom: '1px solid transparent',
                                                        borderRadius: 0, padding: '4px 0',
                                                        fontSize: '0.875rem', backgroundColor: 'transparent', fontWeight: 500
                                                    }}
                                                />
                                                {item.itemCatalogCode && (
                                                    <div style={{ fontSize: '9px', color: '#4f46e5', fontWeight: 700, marginTop: '2px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                        📦 {item.itemCatalogCode}
                                                    </div>
                                                )}
                                            </td>

                                            <td style={{ padding: '12px 16px' }}>
                                                <input
                                                    type="number"
                                                    value={item.quantity}
                                                    onChange={e => updateItem(idx, 'quantity', parseFloat(e.target.value) || 0)}
                                                    style={{ ...inlineInputStyle, textAlign: 'center' }}
                                                    onMouseEnter={e => e.currentTarget.style.borderBottomColor = '#cbd5e1'}
                                                    onMouseLeave={e => e.currentTarget.style.borderBottomColor = 'transparent'}
                                                    onFocus={e => e.currentTarget.style.borderBottomColor = '#dc2626'}
                                                    onBlur={e => e.currentTarget.style.borderBottomColor = 'transparent'}
                                                />
                                            </td>

                                            <td style={{ padding: '12px 16px', position: 'relative' }}>
                                                <select
                                                    value={item.unitId || ''}
                                                    onChange={e => updateItem(idx, 'unitId', parseInt(e.target.value) || null)}
                                                    style={{ ...inlineInputStyle, fontSize: '12px' }}
                                                    onMouseEnter={e => e.currentTarget.style.borderBottomColor = '#cbd5e1'}
                                                    onMouseLeave={e => e.currentTarget.style.borderBottomColor = 'transparent'}
                                                    onFocus={e => e.currentTarget.style.borderBottomColor = '#dc2626'}
                                                    onBlur={e => e.currentTarget.style.borderBottomColor = 'transparent'}
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

                                            <td style={{ padding: '12px 16px' }}>
                                                <input
                                                    type="number"
                                                    value={item.unitPrice}
                                                    onChange={e => updateItem(idx, 'unitPrice', parseFloat(e.target.value) || 0)}
                                                    style={{ ...inlineInputStyle, textAlign: 'right' }}
                                                    onMouseEnter={e => e.currentTarget.style.borderBottomColor = '#cbd5e1'}
                                                    onMouseLeave={e => e.currentTarget.style.borderBottomColor = 'transparent'}
                                                    onFocus={e => e.currentTarget.style.borderBottomColor = '#dc2626'}
                                                    onBlur={e => e.currentTarget.style.borderBottomColor = 'transparent'}
                                                />
                                            </td>

                                            <td style={{ padding: '12px 16px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                    <select
                                                        value={item.ivaRateId || ''}
                                                        onChange={e => updateItem(idx, 'ivaRateId', parseInt(e.target.value) || null)}
                                                        style={{
                                                            ...inlineInputStyle, fontSize: '12px',
                                                            ...(item.ivaUncertain ? { borderBottomColor: '#ef4444', borderBottomWidth: '2px', backgroundColor: 'rgba(239,68,68,0.04)' } : {})
                                                        }}
                                                        title={item.ivaUncertain ? 'IVA não identificado pelo OCR. Verifique manualmente.' : undefined}
                                                        onMouseEnter={e => { if (!item.ivaUncertain) e.currentTarget.style.borderBottomColor = '#cbd5e1'; }}
                                                        onMouseLeave={e => { if (!item.ivaUncertain) e.currentTarget.style.borderBottomColor = 'transparent'; }}
                                                        onFocus={e => e.currentTarget.style.borderBottomColor = '#dc2626'}
                                                        onBlur={e => { if (!item.ivaUncertain) e.currentTarget.style.borderBottomColor = 'transparent'; }}
                                                    >
                                                        <option value="">IVA...</option>
                                                        {ivaRates.filter(r => r.isActive).map(r => (
                                                            <option key={r.id} value={r.id}>{r.name} ({r.ratePercent}%)</option>
                                                        ))}
                                                    </select>
                                                    {item.ivaUncertain && (
                                                        <span style={{ fontSize: '9px', color: '#ef4444', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '4px' }}
                                                            title="O OCR não conseguiu identificar o IVA para este item.">
                                                            <AlertCircle style={{ width: 10, height: 10, flexShrink: 0 }} />
                                                            IVA não identificado
                                                        </span>
                                                    )}
                                                </div>
                                            </td>

                                            <td style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 900, color: '#0f172a' }}>
                                                {formatCurrencyAO(item.totalPrice)} {draft.currency || 'AOA'}
                                            </td>

                                            <td style={{ padding: '12px 16px' }}>
                                                <button
                                                    onClick={() => removeItem(idx)}
                                                    style={{ color: '#cbd5e1', background: 'none', border: 'none', cursor: 'pointer', transition: 'color 0.15s', display: 'flex' }}
                                                    onMouseEnter={e => e.currentTarget.style.color = '#dc2626'}
                                                    onMouseLeave={e => e.currentTarget.style.color = '#cbd5e1'}
                                                >
                                                    <Trash2 style={{ width: 16, height: 16 }} />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                            <button
                                onClick={addItem}
                                style={{
                                    width: '100%', padding: '12px', backgroundColor: '#f8fafc',
                                    color: '#64748b', fontWeight: 900, textTransform: 'uppercase',
                                    fontSize: '10px', letterSpacing: '0.03em',
                                    border: 'none', cursor: 'pointer',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                                    transition: 'background-color 0.15s',
                                }}
                                onMouseEnter={e => e.currentTarget.style.backgroundColor = '#f1f5f9'}
                                onMouseLeave={e => e.currentTarget.style.backgroundColor = '#f8fafc'}
                            >
                                <Plus style={{ width: 16, height: 16 }} />
                                Adicionar Novo Item
                            </button>
                        </div>

                        {/* IVA Uncertainty Banner */}
                        {draft.headerHasIva && draft.items.some(i => i.ivaUncertain) && (
                            <div style={{
                                display: 'flex', alignItems: 'center', gap: '8px',
                                padding: '12px', backgroundColor: '#fef2f2',
                                border: '1px solid #fca5a5', borderRadius: '4px',
                                color: '#b91c1c', fontSize: '12px', fontWeight: 600,
                            }}>
                                <AlertTriangle style={{ width: 16, height: 16, flexShrink: 0 }} />
                                <span>
                                    <strong>Atenção:</strong> O documento contém IVA nos totais, mas o IVA por item não foi identificado pelo OCR.
                                    Verifique e corrija manualmente o campo IVA de cada item antes de salvar.
                                </span>
                            </div>
                        )}

                        {/* Footer: Discount + Total */}
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', gap: '24px', paddingTop: '24px', flexWrap: 'wrap' }}>
                            <div style={{ width: '220px' }}>
                                <label style={labelStyle}>Desconto Comercial</label>
                                <input
                                    type="number"
                                    value={draft.discountAmount}
                                    onChange={e => updateHeader('discountAmount', parseFloat(e.target.value) || 0)}
                                    style={{ ...inputBase, textAlign: 'right' }}
                                />
                            </div>

                            {/* Total panel */}
                            <div style={{
                                backgroundColor: '#0f172a', color: '#fff',
                                padding: '24px', minWidth: '300px', position: 'relative', overflow: 'hidden',
                            }}>
                                <div style={{ position: 'absolute', top: 0, right: 0, width: 96, height: 96, backgroundColor: 'rgba(220,38,38,0.1)', borderRadius: '50%', marginRight: '-32px', marginTop: '-32px' }} />
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', position: 'relative' }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', color: '#94a3b8', fontWeight: 700, fontSize: '11px', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                                        <span>Total s/ IVA</span>
                                        {(() => {
                                            const gross = draft.items.reduce((sum, item) => {
                                                const g = Math.round(((item.quantity || 0) * (item.unitPrice || 0)) * 100) / 100;
                                                return sum + Math.max(0, g - (item.discountAmount || 0));
                                            }, 0);
                                            const taxableBase = Math.max(0, gross - (draft.discountAmount || 0));
                                            return <span>{formatCurrencyAO(taxableBase)} {draft.currency || 'AOA'}</span>;
                                        })()}
                                    </div>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', color: '#94a3b8', fontWeight: 700, fontSize: '11px', textTransform: 'uppercase', letterSpacing: '0.1em', paddingBottom: '8px', borderBottom: '1px solid rgba(255,255,255,0.1)' }}>
                                        <span>Total IVA</span>
                                        {(() => {
                                            const gross = draft.items.reduce((sum, item) => {
                                                const g = Math.round(((item.quantity || 0) * (item.unitPrice || 0)) * 100) / 100;
                                                return sum + Math.max(0, g - (item.discountAmount || 0));
                                            }, 0);
                                            const iva = draft.items.reduce((sum, item) => {
                                                const g = Math.round(((item.quantity || 0) * (item.unitPrice || 0)) * 100) / 100;
                                                const itemNet = Math.max(0, g - (item.discountAmount || 0));
                                                const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
                                                return sum + Math.round(itemNet * ((selectedIva?.ratePercent || 0) / 100) * 100) / 100;
                                            }, 0);
                                            const discountRatio = gross > 0 ? (Math.max(0, gross - (draft.discountAmount || 0)) / gross) : 1;
                                            return <span>{formatCurrencyAO(Math.round(iva * discountRatio * 100) / 100)} {draft.currency || 'AOA'}</span>;
                                        })()}
                                    </div>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '8px' }}>
                                        <span style={{ fontWeight: 900, textTransform: 'uppercase', fontSize: '12px', letterSpacing: '-0.02em' }}>Valor Líquido</span>
                                        <span style={{ fontSize: '1.5rem', fontWeight: 900, color: '#f87171' }}>
                                            {formatCurrencyAO(draft.totalAmount)} {draft.currency || 'AOA'}
                                        </span>
                                    </div>
                                </div>
                            </div>
                        </div>

                        {/* Actions */}
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '24px', borderTop: '2px solid #0f172a' }}>
                            <button
                                onClick={onComplete}
                                style={{ padding: '8px 24px', color: '#64748b', fontWeight: 900, textTransform: 'uppercase', fontSize: '11px', letterSpacing: '0.1em', background: 'none', border: 'none', cursor: 'pointer', transition: 'color 0.15s' }}
                                onMouseEnter={e => e.currentTarget.style.color = '#0f172a'}
                                onMouseLeave={e => e.currentTarget.style.color = '#64748b'}
                            >
                                Cancelar
                            </button>
                            <div style={{ display: 'flex', gap: '16px' }}>
                                {ocrResult && (
                                    <button
                                        onClick={() => setStep('UPLOAD')}
                                        style={{
                                            padding: '8px 24px', backgroundColor: '#f1f5f9',
                                            color: '#0f172a', fontWeight: 900, textTransform: 'uppercase',
                                            fontSize: '11px', letterSpacing: '0.1em',
                                            border: 'none', cursor: 'pointer',
                                            display: 'flex', alignItems: 'center', gap: '8px',
                                            transition: 'background-color 0.15s',
                                        }}
                                        onMouseEnter={e => e.currentTarget.style.backgroundColor = '#e2e8f0'}
                                        onMouseLeave={e => e.currentTarget.style.backgroundColor = '#f1f5f9'}
                                    >
                                        <RefreshCcw style={{ width: 16, height: 16 }} />
                                        Refazer OCR
                                    </button>
                                )}
                                <button
                                    onClick={handleSave}
                                    disabled={isProcessing}
                                    style={{
                                        padding: '12px 32px', backgroundColor: '#0f172a', color: '#fff',
                                        fontWeight: 900, textTransform: 'uppercase', fontSize: '13px',
                                        letterSpacing: '0.1em', border: 'none',
                                        cursor: isProcessing ? 'not-allowed' : 'pointer',
                                        display: 'flex', alignItems: 'center', gap: '8px',
                                        boxShadow: '4px 4px 0px 0px rgba(239,68,68,1)',
                                        transition: 'background-color 0.15s',
                                    }}
                                    onMouseEnter={e => { if (!isProcessing) e.currentTarget.style.backgroundColor = '#dc2626'; }}
                                    onMouseLeave={e => { if (!isProcessing) e.currentTarget.style.backgroundColor = '#0f172a'; }}
                                >
                                    {isProcessing
                                        ? <RefreshCcw style={{ width: 20, height: 20, animation: 'spin 1s linear infinite' }} />
                                        : <CheckCircle2 style={{ width: 20, height: 20 }} />
                                    }
                                    Confirmar Detalhes
                                </button>
                            </div>
                        </div>

                        {feedback && feedback.type === 'error' && (
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#dc2626', justifyContent: 'flex-end', fontWeight: 700, fontSize: '14px' }}>
                                <AlertCircle style={{ width: 16, height: 16 }} />
                                <span>{feedback.message}</span>
                            </div>
                        )}
                    </motion.div>
                )}
            </AnimatePresence>

            {/* Duplicate Warning Modal */}
            <AnimatePresence>
                {duplicateWarning?.isOpen && (
                    <div style={{ position: 'fixed', inset: 0, zIndex: 100, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '16px', backgroundColor: 'rgba(17,24,39,0.5)', backdropFilter: 'blur(4px)' }}>
                        <motion.div
                            initial={{ opacity: 0, scale: 0.95 }}
                            animate={{ opacity: 1, scale: 1 }}
                            exit={{ opacity: 0, scale: 0.95 }}
                            style={{ backgroundColor: '#fff', borderRadius: '12px', boxShadow: '0 25px 50px -12px rgba(0,0,0,0.25)', width: '100%', maxWidth: '448px', overflow: 'hidden', border: '1px solid #e5e7eb' }}
                        >
                            <div style={{ padding: '24px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '48px', height: '48px', margin: '0 auto 16px', backgroundColor: '#fef3c7', borderRadius: '50%' }}>
                                    <AlertTriangle size={24} color="#d97706" />
                                </div>
                                <h3 style={{ fontSize: '1.125rem', fontWeight: 600, textAlign: 'center', color: '#111827', marginBottom: '8px' }}>
                                    Documento Já Existente
                                </h3>
                                <p style={{ fontSize: '0.875rem', color: '#4b5563', textAlign: 'center', marginBottom: '16px' }}>
                                    Aviso de duplicidade: Este orçamento/proforma já foi carregado no sistema anteriormente.
                                </p>

                                <div style={{ backgroundColor: 'var(--color-bg-page)', padding: '16px', borderRadius: '8px', fontSize: '0.875rem', color: '#4b5563', marginBottom: '24px' }}>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Pedido Vinculado:</span> {duplicateWarning.requestNumber}</p>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Enviado por:</span> {duplicateWarning.uploadedBy || 'Desconhecido'}</p>
                                    <p><span style={{ fontWeight: 600, color: '#374151' }}>Enviado em:</span> {duplicateWarning.createdAtUtc ? formatDateTime(duplicateWarning.createdAtUtc) : '-'}</p>
                                </div>

                                <div style={{ display: 'flex', gap: '12px' }}>
                                    <button
                                        type="button"
                                        onClick={() => setDuplicateWarning(null)}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: '#374151', backgroundColor: '#fff', border: '1px solid var(--color-border)', borderRadius: '8px', cursor: 'pointer' }}
                                    >
                                        Cancelar Envio
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => duplicateWarning?.uploadCallback?.()}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: '#fff', backgroundColor: '#d97706', border: 'none', borderRadius: '8px', cursor: 'pointer' }}
                                    >
                                        Estou Ciente, Prosseguir
                                    </button>
                                </div>
                            </div>
                        </motion.div>
                    </div>
                )}
            </AnimatePresence>
        </div>
    );
}
