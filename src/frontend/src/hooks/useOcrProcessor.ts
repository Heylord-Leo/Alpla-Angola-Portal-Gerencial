import { api } from '../lib/api';
import { IvaRate, Unit, CurrencyDto, OcrDraft, OcrDraftItem, LookupDto } from '../types';

export function useOcrProcessor(ivaRates: IvaRate[], units: Unit[], currencies: CurrencyDto[], companies: LookupDto[] = []) {

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

    const calculateItemTotal = (item: Pick<OcrDraftItem, 'quantity' | 'unitPrice' | 'ivaRateId' | 'discountAmount'>) => {
        const safeQty = item.quantity || 0;
        const safePrice = item.unitPrice || 0;
        const discount = item.discountAmount || 0;
        const grossSubtotal = Math.round((safeQty * safePrice - discount) * 100) / 100;
        
        const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
        const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
        const ivaAmount = Math.round(grossSubtotal * (ivaPercent / 100) * 100) / 100;
        return Math.round((grossSubtotal + ivaAmount) * 100) / 100;
    };

    const calculateDraftTotal = (draft: Pick<OcrDraft, 'items' | 'discountAmount'>) => {
        let gross = 0;
        let ivaTotal = 0;
        
        draft.items.forEach(item => {
            const itemGross = Math.round(((item.quantity || 0) * (item.unitPrice || 0) - (item.discountAmount || 0)) * 100) / 100;
            gross += itemGross;
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            ivaTotal += Math.round(itemGross * (ivaPercent / 100) * 100) / 100;
        });

        const discount = draft.discountAmount || 0; // Global discount
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

        const extractedBilledCompany = getSafeValue<string>(suggestions?.billedCompany, '');
        const extractedCurrency = getSafeValue<string>(suggestions?.currencyCode, '') || getSafeValue<string>(suggestions?.currency, 'EUR');

        // Part B: Company Matching (Keyword-based)
        // Strategy: extract distinguishing keywords (PLASTICO/SOPRO) from both the OCR text
        // and system company names, then match on keyword overlap.
        let matchedCompanyId: number | null = null;
        let isCompanyOcrAutoFilled = false;
        
        if (extractedBilledCompany && typeof extractedBilledCompany === 'string' && companies.length > 0) {
            const normalizedExtracted = extractedBilledCompany.toLowerCase();
            
            // Check for distinguishing keywords
            const extractedHasPlastico = /plastico/i.test(normalizedExtracted);
            const extractedHasSopro = /sopro/i.test(normalizedExtracted);
            
            for (const company of companies) {
                const companyNameLower = company.name.toLowerCase();
                const companyHasPlastico = /plastico/i.test(companyNameLower);
                const companyHasSopro = /sopro/i.test(companyNameLower);
                
                // Match if both contain the same distinguishing keyword
                if ((extractedHasPlastico && companyHasPlastico) || (extractedHasSopro && companyHasSopro)) {
                    matchedCompanyId = company.id;
                    isCompanyOcrAutoFilled = true;
                    break;
                }
            }
            
            // Fallback: if no keyword match but text contains 'alpla', pick the first ALPLA company
            if (!matchedCompanyId && /alpla/i.test(normalizedExtracted) && companies.length === 1) {
                matchedCompanyId = companies[0].id;
                isCompanyOcrAutoFilled = true;
            }
        }

        // --- Company Match Diagnostics (visible in browser DevTools > Console) ---
        console.group('[OCR] Company Match Diagnostics');
        console.log('Extracted Company (raw):', extractedBilledCompany || '(empty)');
        console.log('System Companies:', companies.map(c => ({ id: c.id, name: c.name })));
        console.log('Keywords detected:', {
            hasPlastico: extractedBilledCompany ? /plastico/i.test(extractedBilledCompany) : false,
            hasSopro: extractedBilledCompany ? /sopro/i.test(extractedBilledCompany) : false,
            hasAlpla: extractedBilledCompany ? /alpla/i.test(extractedBilledCompany) : false,
        });
        console.log('Match Result:', matchedCompanyId
            ? `✅ Matched → Company ID ${matchedCompanyId} (${companies.find(c => c.id === matchedCompanyId)?.name})`
            : '❌ No match found'
        );
        console.groupEnd();

        const draft: OcrDraft = {
            supplierId: matchedSupplierId,
            supplierNameSnapshot: extractedSupplierName,
            supplierPortalCode: extractedSupplierPortalCode,
            supplierTaxId: extractedSupplierTaxId,
            companyId: matchedCompanyId,
            extractedCompanyName: extractedBilledCompany,
            isCompanyOcrAutoFilled: isCompanyOcrAutoFilled,
            documentNumber: getSafeValue<string>(suggestions?.documentNumber, ''),
            documentDate: resolveDateAlias(getSafeValue<string>(suggestions?.documentDate, '')),
            dueDate: resolveDateAlias(getSafeValue<string>(suggestions?.dueDate, '')),
            currency: resolveCurrencyAlias(extractedCurrency),
            extractedCurrency: extractedCurrency,
            discountAmount: getSafeValue<number>(suggestions?.discountAmount, 0),
            totalAmount: getSafeValue<number>(suggestions?.grandTotal, 0) || getSafeValue<number>(suggestions?.totalAmount, 0),
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

                // Cross-validate discountAmount using discountPercent if available
                // The AI sometimes returns per-unit discount instead of total line discount
                const rawQty = item.quantity || 1;
                const rawPrice = item.unitPrice || 0;
                const rawDiscountPct = item.discountPercent || 0;
                const rawDiscountAmt = item.discountAmount || 0;
                
                let resolvedDiscount = rawDiscountAmt;
                if (rawDiscountPct > 0) {
                    // Calculate expected total line discount from percentage
                    const expectedDiscount = Math.round(rawQty * rawPrice * (rawDiscountPct / 100) * 100) / 100;
                    // If the OCR discountAmount doesn't match the expected value, use the calculated one
                    if (Math.abs(resolvedDiscount - expectedDiscount) > 0.01) {
                        console.warn(`[OCR] Item ${index + 1}: discountAmount (${resolvedDiscount}) doesn't match ${rawDiscountPct}% of ${rawQty}×${rawPrice} = ${expectedDiscount}. Using calculated value.`);
                        resolvedDiscount = expectedDiscount;
                    }
                }

                const baseItem = {
                    lineNumber: index + 1,
                    description: item.description || '',
                    quantity: rawQty,
                    unitId: matchedUnit ? matchedUnit.id : null,
                    unit: matchedUnitCode,
                    unitPrice: rawPrice,
                    discountAmount: resolvedDiscount,
                    ivaRateId,
                    taxRate: item.taxRate
                };

                return {
                    ...baseItem,
                    // Always recalculate from components (qty * unitPrice - discount + IVA)
                    totalPrice: calculateItemTotal(baseItem)
                };
            })
        };

        // Always recalculate total from item components for consistency
        // (since individual item totals are now always calculated from qty * unitPrice - discount)
        if (draft.items.length > 0) {
            draft.totalAmount = calculateDraftTotal(draft);
        }

        // --- Calculation Diagnostics (visible in browser DevTools > Console) ---
        console.group('[OCR] Extraction & Calculation Diagnostics');
        console.log('Header Total (grandTotal from OCR):', getSafeValue<number>(suggestions?.grandTotal, 0));
        console.log('Header Total (totalAmount fallback):', getSafeValue<number>(suggestions?.totalAmount, 0));
        console.log('Final Draft Total:', draft.totalAmount);
        console.log('Global Discount:', draft.discountAmount);
        console.table(draft.items.map((item, i) => ({
            '#': i + 1,
            description: (item.description || '').substring(0, 40),
            qty: item.quantity,
            unitPrice: item.unitPrice,
            discount: item.discountAmount,
            totalPrice: item.totalPrice,
            gross: ((item.quantity || 0) * (item.unitPrice || 0)).toFixed(2),
            net: (((item.quantity || 0) * (item.unitPrice || 0)) - (item.discountAmount || 0)).toFixed(2)
        })));
        console.groupEnd();

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
