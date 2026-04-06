import { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Plus, Upload, Trash2, RefreshCcw, Hash, Calendar, CheckCircle2, AlertCircle, AlertTriangle } from 'lucide-react';
import { api } from '../lib/api';
import { SupplierAutocomplete } from './SupplierAutocomplete';
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
    
    // Use props but allow local state if needed (though props are preferred)
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

    // Sync local lookups if props change
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
        const directMatch = units.find(u => u.code.toUpperCase() === normalized || u.name.toUpperCase() === normalized);
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
                const matchedUnit = units.find(u => u.code.toUpperCase() === canonicalCode);
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
                        totalPrice: grossSubtotal
                    };
                })
            };

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

        setIsProcessing(true);
        setFeedback(null);

        try {
            const hash = await computeFileHash(file);
            const dupCheck = await api.attachments.checkDuplicate(hash);
            if (dupCheck.isDuplicate) {
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
            console.error("Duplicate check failed", err);
        } finally {
            if (!duplicateWarning) { // Only clear if we are not opening the modal
                setIsProcessing(false);
            }
        }

        _processUpload(file);
    };

    const recalculateQuotationTotal = (d: QuotationDraft) => {
        let gross = 0;
        let iva = 0;
        d.items.forEach(item => {
            const itemGross = Math.round((item.quantity || 0) * (item.unitPrice || 0) * 100) / 100;
            gross += itemGross;
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            iva += Math.round(itemGross * (ivaPercent / 100) * 100) / 100;
        });

        const discount = d.discountAmount || 0;
        const taxableBase = Math.max(0, gross - discount);
        const discountRatio = gross > 0 ? (taxableBase / gross) : 1;
        const adjustedIva = Math.round(iva * discountRatio * 100) / 100;
        const total = Math.round((taxableBase + adjustedIva) * 100) / 100;

        return Math.max(0, total);
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
            
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            const ivaAmount = Math.round(grossSubtotal * (ivaPercent / 100) * 100) / 100;
            
            item.totalPrice = Math.round((grossSubtotal + ivaAmount) * 100) / 100;
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
                totalPrice: 0
            };
            const next = { ...prev, items: [...prev.items, newItem] };
            return next;
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

    return (
        <div className="bg-white border-2 border-slate-900 shadow-[4px_4px_0px_0px_rgba(15,23,42,1)] p-6">
            <AnimatePresence mode="wait">
                {step === 'UPLOAD' ? (
                    <motion.div 
                        key="upload"
                        initial={{ opacity: 0, y: 10 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -10 }}
                        className="text-center py-8"
                    >
                        <Upload className="w-12 h-12 mx-auto text-slate-400 mb-4" />
                        <h3 className="text-xl font-black text-slate-900 mb-2 uppercase tracking-tight">
                            Carregar Proforma / Orçamento
                        </h3>
                        <p className="text-slate-500 mb-8 max-w-md mx-auto">
                            O sistema utilizará Inteligência Artificial para extrair automaticamente fornecedor, itens e valores do PDF.
                        </p>

                        <div className="flex flex-col gap-4 items-center">
                            <button
                                onClick={() => fileInputRef.current?.click()}
                                disabled={isProcessing}
                                className="group relative px-8 py-3 bg-red-600 text-white font-black uppercase text-sm tracking-widest hover:bg-slate-900 transition-all flex items-center gap-2"
                            >
                                {isProcessing ? (
                                    <RefreshCcw className="w-5 h-5 animate-spin" />
                                ) : (
                                    <Upload className="w-5 h-5" />
                                )}
                                {isProcessing ? 'Processando OCR...' : 'Selecionar Documento (PDF)'}
                            </button>

                            <button
                                onClick={() => setStep('EDIT')}
                                disabled={isProcessing}
                                className="text-slate-500 font-bold hover:text-slate-900 text-sm flex items-center gap-1"
                            >
                                <Plus className="w-4 h-4" />
                                Introduzir Dados Manualmente
                            </button>
                        </div>
                        
                        <input 
                            type="file" 
                            ref={fileInputRef} 
                            onChange={handleFileUpload} 
                            accept=".pdf,image/*" 
                            className="hidden" 
                        />

                        {feedback && (
                            <div className="mt-6">
                                <Feedback type={feedback.type} message={feedback.message} />
                            </div>
                        )}
                    </motion.div>
                ) : (
                    <motion.div 
                        key="edit"
                        initial={{ opacity: 0, scale: 0.98 }}
                        animate={{ opacity: 1, scale: 1 }}
                        className="space-y-6"
                    >
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                            <div className="md:col-span-2">
                                <label className="block text-xs font-black uppercase text-slate-500 mb-1">Fornecedor</label>
                                <div className="space-y-1">
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
                                        <div className="flex items-center gap-1 text-[10px] text-amber-600 font-bold">
                                            <AlertCircle className="w-3 h-3" />
                                            <span>Sugerido pelo OCR: "{draft.supplierNameSnapshot}"</span>
                                        </div>
                                    )}
                                </div>
                            </div>

                            <div>
                                <label className="block text-xs font-black uppercase text-slate-500 mb-1">Nº Documento</label>
                                <div className="relative">
                                    <Hash className="absolute left-3 top-2.5 w-4 h-4 text-slate-400" />
                                    <input 
                                        type="text"
                                        value={draft.documentNumber}
                                        onChange={e => updateHeader('documentNumber', e.target.value)}
                                        className={`w-full pl-9 pr-4 py-2 bg-slate-50 border-2 font-bold focus:bg-white focus:outline-none transition-colors ${formErrors.documentNumber ? 'border-red-500 text-red-600' : 'border-slate-200'}`}
                                        placeholder="Ex: 2024/001"
                                    />
                                </div>
                            </div>

                            <div>
                                <label className="block text-xs font-black uppercase text-slate-500 mb-1">Data Documento</label>
                                <div className="relative">
                                    <Calendar className="absolute left-3 top-2.5 w-4 h-4 text-slate-400" />
                                    <input 
                                        type="date"
                                        value={draft.documentDate}
                                        onChange={e => updateHeader('documentDate', e.target.value)}
                                        className={`w-full pl-9 pr-4 py-2 bg-slate-50 border-2 font-bold focus:bg-white focus:outline-none transition-colors ${formErrors.documentDate ? 'border-red-500 text-red-600' : 'border-slate-200'}`}
                                    />
                                </div>
                            </div>

                            <div>
                                <label className="block text-xs font-black uppercase text-slate-500 mb-1">Moeda</label>
                                <select 
                                    value={draft.currency}
                                    onChange={e => updateHeader('currency', e.target.value)}
                                    className={`w-full px-4 py-2 bg-slate-50 border-2 font-bold focus:bg-white focus:outline-none transition-colors ${formErrors.currency ? 'border-red-500 text-red-600 text-sm' : 'border-slate-200'}`}
                                >
                                    <option value="">Selecione...</option>
                                    {currencies.map(c => <option key={c.code} value={c.code}>{c.code} - {c.name}</option>)}
                                </select>
                                {draft.extractedCurrency && !draft.currency && (
                                    <div className="mt-1 flex items-center gap-1 text-[10px] text-amber-600 font-bold">
                                        <AlertCircle className="w-3 h-3" />
                                        <span>Sugestão OCR: {draft.extractedCurrency}</span>
                                    </div>
                                )}
                            </div>
                        </div>

                        {/* Items Table */}
                        <div className="overflow-x-auto border-2 border-slate-200">
                            <table className="w-full text-sm">
                                <thead className="bg-slate-50 border-b-2 border-slate-200">
                                    <tr>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 w-12 text-center">#</th>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 MIN-W-[300PX]">Descrição do Item</th>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 w-24">Qtd</th>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 w-32">Unidade</th>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 w-40 text-right">Preço Unit</th>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 w-40">Taxa IVA</th>
                                        <th className="px-4 py-2 text-left font-black uppercase text-[10px] text-slate-500 w-40 text-right">Total c/ IVA</th>
                                        <th className="px-4 py-2 w-12"></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {draft.items.map((item, idx) => (
                                        <tr key={idx} className="border-b border-slate-100 hover:bg-slate-50/50">
                                            <td className="px-4 py-3 text-center font-bold text-slate-400">{item.lineNumber}</td>
                                            <td className="px-4 py-3">
                                                <input 
                                                    type="text"
                                                    value={item.description}
                                                    onChange={e => updateItem(idx, 'description', e.target.value)}
                                                    className="w-full bg-transparent border-b border-transparent hover:border-slate-300 focus:border-red-500 focus:outline-none py-1 font-medium transition-colors"
                                                    placeholder="Descrição do produto ou serviço..."
                                                />
                                            </td>
                                            <td className="px-4 py-3">
                                                <input 
                                                    type="number"
                                                    value={item.quantity}
                                                    onChange={e => updateItem(idx, 'quantity', parseFloat(e.target.value) || 0)}
                                                    className="w-full bg-transparent border-b border-transparent hover:border-slate-300 focus:border-red-500 focus:outline-none py-1 font-bold text-center transition-colors"
                                                />
                                            </td>
                                            <td className="px-4 py-3">
                                                <select
                                                    value={item.unitId || ''}
                                                    onChange={e => updateItem(idx, 'unitId', parseInt(e.target.value) || null)}
                                                    className="w-full bg-transparent border-b border-transparent hover:border-slate-300 focus:border-red-500 focus:outline-none py-1 text-xs font-bold transition-colors"
                                                >
                                                    <option value="">Unid...</option>
                                                    {units.map(u => <option key={u.id} value={u.id}>{u.code}</option>)}
                                                </select>
                                                {item.unit && !item.unitId && (
                                                    <div className="absolute top-full z-10 text-[9px] text-amber-600 bg-amber-50 px-1 border border-amber-200">Sug: {item.unit}</div>
                                                )}
                                            </td>
                                            <td className="px-4 py-3">
                                                <input 
                                                    type="number"
                                                    value={item.unitPrice}
                                                    onChange={e => updateItem(idx, 'unitPrice', parseFloat(e.target.value) || 0)}
                                                    className="w-full bg-transparent border-b border-transparent hover:border-slate-300 focus:border-red-500 focus:outline-none py-1 font-bold text-right transition-colors"
                                                />
                                            </td>
                                            <td className="px-4 py-3">
                                                <select
                                                    value={item.ivaRateId || ''}
                                                    onChange={e => updateItem(idx, 'ivaRateId', parseInt(e.target.value) || null)}
                                                    className="w-full bg-transparent border-b border-transparent hover:border-slate-300 focus:border-red-500 focus:outline-none py-1 text-xs font-bold transition-colors"
                                                >
                                                    <option value="">IVA...</option>
                                                    {ivaRates.filter(r => r.isActive).map(r => <option key={r.id} value={r.id}>{r.name} ({r.ratePercent}%)</option>)}
                                                </select>
                                            </td>
                                            <td className="px-4 py-3 text-right font-black text-slate-900">
                                                {formatCurrencyAO(item.totalPrice)} {draft.currency || 'AOA'}
                                            </td>
                                            <td className="px-4 py-3">
                                                <button onClick={() => removeItem(idx)} className="text-slate-300 hover:text-red-600 transition-colors">
                                                    <Trash2 className="w-4 h-4" />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                            <button 
                                onClick={addItem}
                                className="w-full py-3 bg-slate-50 hover:bg-slate-100 text-slate-500 font-black uppercase text-[10px] tracking-tight flex items-center justify-center gap-2 transition-colors"
                            >
                                <Plus className="w-4 h-4" />
                                Adicionar Novo Item
                            </button>
                        </div>

                        {/* Footer Summary */}
                        <div className="flex flex-col md:flex-row justify-between items-end gap-6 pt-6">
                            <div className="w-full md:w-64">
                                <label className="block text-xs font-black uppercase text-slate-500 mb-1">Desconto Comercial</label>
                                <div className="relative">
                                    <input 
                                        type="number"
                                        value={draft.discountAmount}
                                        onChange={e => updateHeader('discountAmount', parseFloat(e.target.value) || 0)}
                                        className="w-full px-4 py-2 bg-slate-50 border-2 border-slate-200 font-bold focus:bg-white focus:outline-none transition-colors text-right"
                                    />
                                </div>
                            </div>

                            <div className="bg-slate-900 text-white p-6 min-w-[300px] relative overflow-hidden">
                                <div className="absolute top-0 right-0 w-24 h-24 bg-red-600/10 rounded-full -mr-8 -mt-8" />
                                <div className="space-y-2 relative">
                                    <div className="flex justify-between text-slate-400 font-bold text-xs uppercase tracking-widest">
                                        <span>Total s/ IVA</span>
                                        {(() => {
                                            const gross = draft.items.reduce((sum, item) => sum + ((item.quantity||0) * (item.unitPrice||0)), 0);
                                            const discount = draft.discountAmount || 0;
                                            const taxableBase = Math.max(0, gross - discount);
                                            return <span>{formatCurrencyAO(taxableBase)} {draft.currency || 'AOA'}</span>;
                                        })()}
                                    </div>
                                    <div className="flex justify-between text-slate-400 font-bold text-xs uppercase tracking-widest pb-2 border-b border-white/10">
                                        <span>Total IVA</span>
                                        {(() => {
                                            const gross = draft.items.reduce((sum, item) => sum + ((item.quantity||0) * (item.unitPrice||0)), 0);
                                            const iva = draft.items.reduce((sum, item) => {
                                                const itemGross = (item.quantity||0) * (item.unitPrice||0);
                                                const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
                                                const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
                                                return sum + Math.round(itemGross * (ivaPercent / 100) * 100) / 100;
                                            }, 0);
                                            const discount = draft.discountAmount || 0;
                                            const taxableBase = Math.max(0, gross - discount);
                                            const ratio = gross > 0 ? (taxableBase / gross) : 1;
                                            return <span>{formatCurrencyAO(Math.round(iva * ratio * 100) / 100)} {draft.currency || 'AOA'}</span>;
                                        })()}
                                    </div>
                                    <div className="flex justify-between items-center pt-2">
                                        <span className="font-black uppercase text-xs tracking-tighter">Valor Líquido</span>
                                        <span className="text-2xl font-black text-red-500">
                                            {formatCurrencyAO(draft.totalAmount)} {draft.currency || 'AOA'}
                                        </span>
                                    </div>
                                </div>
                            </div>
                        </div>

                        {/* Actions */}
                        <div className="flex justify-between items-center pt-6 border-t-2 border-slate-900">
                            <button 
                                onClick={onComplete}
                                className="px-6 py-2 text-slate-500 font-black uppercase text-xs tracking-widest hover:text-slate-900 transition-colors"
                            >
                                Cancelar
                            </button>
                            <div className="flex gap-4">
                                {ocrResult && (
                                    <button 
                                        onClick={() => setStep('UPLOAD')}
                                        className="px-6 py-2 bg-slate-100 text-slate-900 font-black uppercase text-xs tracking-widest hover:bg-slate-200 transition-colors flex items-center gap-2"
                                    >
                                        <RefreshCcw className="w-4 h-4" />
                                        Refazer OCR
                                    </button>
                                )}
                                <button 
                                    onClick={handleSave}
                                    disabled={isProcessing}
                                    className="px-8 py-3 bg-slate-900 text-white font-black uppercase text-sm tracking-widest hover:bg-red-600 transition-all flex items-center gap-2 shadow-[4px_4px_0px_0px_rgba(239,68,68,1)]"
                                >
                                    {isProcessing ? <RefreshCcw className="w-5 h-5 animate-spin" /> : <CheckCircle2 className="w-5 h-5" />}
                                    Confirmar Detalhes
                                </button>
                            </div>
                        </div>

                        {feedback && feedback.type === 'error' && (
                            <div className="flex items-center gap-2 text-red-600 justify-end font-bold text-sm">
                                <AlertCircle className="w-4 h-4" />
                                <span>{feedback.message}</span>
                            </div>
                        )}
                    </motion.div>
                )}
            </AnimatePresence>

            <AnimatePresence>
                {duplicateWarning?.isOpen && (
                    <div style={{ position: 'fixed', inset: 0, zIndex: 100, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '16px', backgroundColor: 'rgba(17, 24, 39, 0.5)', backdropFilter: 'blur(4px)' }}>
                        <motion.div
                            initial={{ opacity: 0, scale: 0.95 }}
                            animate={{ opacity: 1, scale: 1 }}
                            exit={{ opacity: 0, scale: 0.95 }}
                            style={{ backgroundColor: 'white', borderRadius: '12px', boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)', width: '100%', maxWidth: '448px', overflow: 'hidden', position: 'relative', border: '1px solid #e5e7eb' }}
                        >
                            <div style={{ padding: '24px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '48px', height: '48px', margin: '0 auto 16px', backgroundColor: '#fef3c7', borderRadius: '9999px' }}>
                                    <AlertTriangle size={24} color="#d97706" />
                                </div>
                                <h3 style={{ fontSize: '1.125rem', fontWeight: 600, textAlign: 'center', color: '#111827', marginBottom: '8px' }}>
                                    Documento Já Existente
                               </h3>
                                <p style={{ fontSize: '0.875rem', color: '#4b5563', textAlign: 'center', marginBottom: '16px' }}>
                                    Aviso de duplicidade: Este orçamento/proforma já foi carregado no sistema anteriormente.
                                </p>
                                
                                <div style={{ backgroundColor: '#f9fafb', padding: '16px', borderRadius: '8px', fontSize: '0.875rem', color: '#4b5563', marginBottom: '24px' }}>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Pedido Vinculado:</span> {duplicateWarning.requestNumber}</p>
                                    <p style={{ marginBottom: '8px' }}><span style={{ fontWeight: 600, color: '#374151' }}>Enviado por:</span> {duplicateWarning.uploadedBy || 'Desconhecido'}</p>
                                    <p><span style={{ fontWeight: 600, color: '#374151' }}>Enviado em:</span> {duplicateWarning.createdAtUtc ? formatDateTime(duplicateWarning.createdAtUtc) : '-'}</p>
                                </div>

                                <div style={{ display: 'flex', gap: '12px', marginTop: '24px' }}>
                                    <button
                                        type="button"
                                        onClick={() => setDuplicateWarning(null)}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: '#374151', backgroundColor: 'white', border: '1px solid #d1d5db', borderRadius: '8px', cursor: 'pointer' }}
                                    >
                                        Cancelar Envio
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => {
                                            if (duplicateWarning?.uploadCallback) {
                                                duplicateWarning.uploadCallback();
                                            }
                                        }}
                                        style={{ flex: 1, padding: '8px 16px', fontSize: '0.875rem', fontWeight: 500, color: 'white', backgroundColor: '#d97706', border: 'none', borderRadius: '8px', cursor: 'pointer' }}
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
