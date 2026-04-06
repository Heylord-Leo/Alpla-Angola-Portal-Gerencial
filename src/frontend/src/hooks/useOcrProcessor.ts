import { api } from '../lib/api';
import { IvaRate, Unit, CurrencyDto, OcrDraft, OcrDraftItem } from '../types';

export function useOcrProcessor(ivaRates: IvaRate[], units: Unit[], currencies: CurrencyDto[]) {

    const resolveCurrencyAlias = (val: string | undefined) => {
        if (!val) return '';
        const normalized = val.trim().toUpperCase().replace(/\.$/, '');
        
        if (currencies.some(c => c.code.toUpperCase() === normalized)) return normalized;

        const aliases: Record<string, string[]> = {
            'AOA': ['AKZ', 'KWANZA', 'KZ', 'KZS', 'AOA'],
            'USD': ['US$', 'DÓLAR', 'DOLAR', 'U.S. $', '$', 'USD'],
            'EUR': ['€', 'EURO', 'EUROS', 'EUR']
        };

        for (const [code, items] of Object.entries(aliases)) {
            if (items.includes(normalized) && currencies.some(c => c.code.toUpperCase() === code)) {
                return code;
            }
        }
        return '';
    };

    const resolveDateAlias = (val: string | undefined): string => {
        if (!val) return '';
        const trimmed = val.trim();
        
        // Already YYYY-MM-DD
        if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) return trimmed;

        // Common format DD/MM/YYYY or DD-MM-YYYY
        const dmyMatch = trimmed.match(/^(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{4})$/);
        if (dmyMatch) {
            const day = dmyMatch[1].padStart(2, '0');
            const month = dmyMatch[2].padStart(2, '0');
            const year = dmyMatch[3];
            return `${year}-${month}-${day}`;
        }

        // Match YYYY/MM/DD
        const ymdMatch = trimmed.match(/^(\d{4})[\/\-\.](\d{1,2})[\/\-\.](\d{1,2})$/);
        if (ymdMatch) {
            const year = ymdMatch[1];
            const month = ymdMatch[2].padStart(2, '0');
            const day = ymdMatch[3].padStart(2, '0');
            return `${year}-${month}-${day}`;
        }

        return trimmed;
    };

    const resolveUnitAlias = (val: string | undefined): string => {
        if (!val) return 'UN';
        const normalized = val.trim().toUpperCase().replace(/\.$/, '');

        const directMatch = units.find(u => 
            u.code.toUpperCase() === normalized || 
            u.name.toUpperCase() === normalized
        );
        if (directMatch) return directMatch.code;

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
                if (matchedUnit) return matchedUnit.code;
            }
        }
        return 'UN';
    };

    const calculateItemTotal = (item: Pick<OcrDraftItem, 'quantity' | 'unitPrice' | 'ivaRateId'>) => {
        const safeQty = item.quantity || 0;
        const safePrice = item.unitPrice || 0;
        const grossSubtotal = Math.round(safeQty * safePrice * 100) / 100;
        const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
        const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
        const ivaAmount = Math.round(grossSubtotal * (ivaPercent / 100) * 100) / 100;
        return Math.round((grossSubtotal + ivaAmount) * 100) / 100;
    };

    const calculateDraftTotal = (draft: Pick<OcrDraft, 'items' | 'discountAmount'>) => {
        let gross = 0;
        let ivaTotal = 0;
        
        draft.items.forEach(item => {
            const itemGross = Math.round((item.quantity || 0) * (item.unitPrice || 0) * 100) / 100;
            gross += itemGross;
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            ivaTotal += Math.round(itemGross * (ivaPercent / 100) * 100) / 100;
        });

        const discount = draft.discountAmount || 0;
        const taxableBase = Math.max(0, gross - discount);
        const discountRatio = gross > 0 ? (taxableBase / gross) : 1;
        const adjustedIva = Math.round(ivaTotal * discountRatio * 100) / 100;
        const total = Math.round((taxableBase + adjustedIva) * 100) / 100;

        return Math.max(0, total);
    };

    const mapOcrResultToDraft = async (result: any, attachmentId?: string): Promise<OcrDraft> => {
        const suggestions = result.integration?.headerSuggestions;
        
        // Safe primitive extractor: ensures we never return the raw OcrValueDto wrapper
        // If the wrapper doesn't exist, or its .value is undefined/null, we fall back to the safe default
        const getSafeValue = <T>(field: any, fallback: T): T => {
            if (field === undefined || field === null) return fallback;
            if (field.value !== undefined && field.value !== null) return field.value as T;
            return fallback;
        };

        const extractedSupplierName = getSafeValue<string>(suggestions?.supplierName, '');
        const extractedSupplierTaxId = getSafeValue<string>(suggestions?.supplierTaxId, '');
        
        // Part A: Supplier Matching
        let matchedSupplierId: number | null = null;
        let extractedSupplierPortalCode: string | null = null;
        if (extractedSupplierName && typeof extractedSupplierName === 'string') {
            try {
                const searchResults = await api.lookups.searchSuppliers(extractedSupplierName);
                const match = searchResults.find((s: any) => s.name.toLowerCase().trim() === extractedSupplierName.toLowerCase().trim());
                if (match) {
                    matchedSupplierId = match.id;
                    extractedSupplierPortalCode = match.portalCode;
                }
            } catch (e) {
                console.error("[useOcrProcessor] Supplier matching failed", e);
            }
        }

        const isTaxIdPlausible = extractedSupplierTaxId && typeof extractedSupplierTaxId === 'string' && extractedSupplierTaxId.length >= 5;

        // Extract currency from backend property matching [JsonPropertyName("currency")]
        const extractedCurrency = getSafeValue<string>(suggestions?.currency, '');

        const draft: OcrDraft = {
            supplierId: matchedSupplierId,
            supplierNameSnapshot: extractedSupplierName,
            supplierPortalCode: extractedSupplierPortalCode,
            supplierTaxId: isTaxIdPlausible ? extractedSupplierTaxId : '',
            documentNumber: getSafeValue<string>(suggestions?.documentNumber, ''),
            documentDate: resolveDateAlias(getSafeValue<string>(suggestions?.documentDate, '')),
            dueDate: resolveDateAlias(getSafeValue<string>(suggestions?.dueDate, '')),
            currency: resolveCurrencyAlias(extractedCurrency),
            extractedCurrency: extractedCurrency,
            discountAmount: getSafeValue<number>(suggestions?.discountAmount, 0),
            totalAmount: getSafeValue<number>(suggestions?.totalAmount, 0),
            proformaAttachmentId: attachmentId,
            items: (result.integration?.lineItemSuggestions || []).map((item: any, index: number) => {
                const extractedUnit = item.unit || '';
                const matchedUnitCode = resolveUnitAlias(extractedUnit);
                const matchedUnit = units.find(u => u.code === matchedUnitCode);
                
                const ivaRateId = (() => {
                    const rate = item.taxRate;
                    if (rate === undefined || rate === null) return null;
                    const matched = ivaRates.find(r => Math.abs(r.ratePercent - rate) < 0.01);
                    return matched ? matched.id : null;
                })();

                const baseItem = {
                    lineNumber: index + 1,
                    description: item.description || '',
                    quantity: item.quantity || 1,
                    unitId: matchedUnit ? matchedUnit.id : null,
                    unit: matchedUnitCode,
                    unitPrice: item.unitPrice || 0,
                    ivaRateId,
                    taxRate: item.taxRate
                };

                return {
                    ...baseItem,
                    // backend uses 'totalAmount' for line item price
                    totalPrice: item.totalAmount ?? calculateItemTotal(baseItem)
                };
            })
        };

        // Final sanity check for total amount if not returned by OCR or looks like purely gross
        if (draft.totalAmount === 0 && draft.items.length > 0) {
            draft.totalAmount = calculateDraftTotal(draft);
        }

        return draft;
    };

    return {
        resolveCurrencyAlias,
        resolveUnitAlias,
        calculateItemTotal,
        calculateDraftTotal,
        mapOcrResultToDraft
    };
}
