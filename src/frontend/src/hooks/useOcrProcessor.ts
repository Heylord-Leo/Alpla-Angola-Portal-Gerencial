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
        
        // Only consider active units for new extractions
        const activeUnits = units.filter(u => u.isActive !== false);

        const directMatch = activeUnits.find(u => 
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
                const matchedUnit = activeUnits.find(u => u.code.toUpperCase() === canonicalCode);
                if (matchedUnit) return matchedUnit.code;
            }
        }
        return 'UN';
    };

    const calculateItemTotal = (item: Pick<OcrDraftItem, 'quantity' | 'unitPrice' | 'ivaRateId' | 'discountAmount'>) => {
        const safeQty = item.quantity || 0;
        const safePrice = item.unitPrice || 0;
        const discount = item.discountAmount || 0;
        const grossSubtotal = Math.round(safeQty * safePrice * 100) / 100;
        const netSubtotal = Math.max(0, grossSubtotal - discount);
        
        const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
        const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
        const ivaAmount = Math.round(netSubtotal * (ivaPercent / 100) * 100) / 100;
        return Math.round((netSubtotal + ivaAmount) * 100) / 100;
    };

    const calculateDraftTotal = (draft: Pick<OcrDraft, 'items' | 'discountAmount'>) => {
        let gross = 0;
        let ivaTotal = 0;
        
        draft.items.forEach(item => {
            const itemGrossRaw = Math.round(((item.quantity || 0) * (item.unitPrice || 0)) * 100) / 100;
            const itemDiscount = item.discountAmount || 0;
            const itemNet = Math.max(0, itemGrossRaw - itemDiscount);
            gross += itemNet;
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            ivaTotal += Math.round(itemNet * (ivaPercent / 100) * 100) / 100;
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
        
        // --- Supplier Name Normalizer: strips trailing punctuation, collapses whitespace ---
        const normalizeName = (name: string): string => {
            return name
                .toLowerCase()
                .trim()
                .replace(/[.,;:!]+$/g, '')   // Remove trailing punctuation (e.g. "SA." → "SA")
                .replace(/\s+/g, ' ')         // Collapse multiple spaces
                .replace(/[''`´]/g, "'")      // Normalize apostrophes
                .trim();
        };

        // Part A: Supplier Matching (multi-strategy)
        let matchedSupplierId: number | null = null;
        let extractedSupplierPortalCode: string | null = null;
        
        if (extractedSupplierName && typeof extractedSupplierName === 'string') {
            try {
                // Search by name first
                const searchResults = await api.lookups.searchSuppliers(extractedSupplierName);
                const normalizedExtracted = normalizeName(extractedSupplierName);

                // Strategy 1: Exact normalized name match
                let match = searchResults.find((s: any) => normalizeName(s.name) === normalizedExtracted);

                // Strategy 2: NIF/TaxId match — if name didn't match but we have a tax ID
                if (!match && extractedSupplierTaxId) {
                    const normalizedTaxId = extractedSupplierTaxId.replace(/[\s\-.]/g, '').trim();
                    if (normalizedTaxId.length >= 5) {
                        // First check if any name-search result has matching NIF
                        match = searchResults.find((s: any) => 
                            s.taxId && s.taxId.replace(/[\s\-.]/g, '').trim() === normalizedTaxId
                        );
                        
                        // If still no match, do a dedicated search by NIF
                        if (!match) {
                            const nifResults = await api.lookups.searchSuppliers(normalizedTaxId);
                            match = nifResults.find((s: any) => 
                                s.taxId && s.taxId.replace(/[\s\-.]/g, '').trim() === normalizedTaxId
                            );
                        }
                    }
                }

                // Strategy 3: Fuzzy contains — one name contains the other (for trade names / abbreviations)
                if (!match && normalizedExtracted.length >= 5) {
                    match = searchResults.find((s: any) => {
                        const normalizedDb = normalizeName(s.name);
                        return normalizedDb.includes(normalizedExtracted) || normalizedExtracted.includes(normalizedDb);
                    });
                }

                if (match) {
                    matchedSupplierId = match.id;
                    extractedSupplierPortalCode = match.portalCode;
                }

                // --- Supplier Match Diagnostics ---
                console.group('[OCR] Supplier Match Diagnostics');
                console.log('Extracted Name:', extractedSupplierName);
                console.log('Normalized Name:', normalizedExtracted);
                console.log('Extracted NIF:', extractedSupplierTaxId || '(empty)');
                console.log('Search Results:', searchResults.length, 'candidates');
                console.log('Match Result:', match
                    ? `✅ Matched → ${match.name} (ID: ${match.id}, Code: ${match.portalCode})`
                    : '❌ No match — supplier may need registration'
                );
                console.groupEnd();
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

                // --- IVA Uncertainty Detection ---
                // OCR may fail to identify item-level IVA and default to 0 (or null).
                // We flag items as uncertain when:
                //   1. taxRate was not extracted at all (null/undefined)
                //   2. taxRate is 0 but ivaRateId resolved to null (no matching rate found)
                const ivaUncertain = (
                    item.taxRate === undefined || item.taxRate === null
                );

                // --- Discount Cross-Validation & Recovery ---
                // Portuguese invoices often show discount as % in the "Desc." column.
                // The AI sometimes confuses the "Desc." (discount) and "IVA" (tax) columns,
                // returning IVA% as discountPercent. We use totalPrice (from the "Valor" column)
                // as the anchor of truth to detect and correct such errors.
                const rawQty = item.quantity || 1;
                const rawPrice = item.unitPrice || 0;
                const rawDiscountPct = item.discountPercent || 0;
                const rawDiscountAmt = item.discountAmount || 0;
                const rawTotalPrice = item.totalAmount || item.totalPrice || 0;
                const grossLineTotal = rawQty * rawPrice;
                
                let resolvedDiscount = rawDiscountAmt;
                let resolvedDiscountPct: number | undefined = rawDiscountPct > 0 ? rawDiscountPct : undefined;

                if (rawTotalPrice > 0 && grossLineTotal > 0) {
                    // Strategy A: Use totalPrice as anchor to reverse-engineer the actual discount
                    const impliedDiscount = Math.round((grossLineTotal - rawTotalPrice) * 100) / 100;
                    const impliedDiscountPct = Math.round((impliedDiscount / grossLineTotal) * 10000) / 100;

                    // If the AI's discountAmount doesn't produce the right totalPrice, use the implied one
                    const aiCalculatedTotal = Math.round((grossLineTotal - rawDiscountAmt) * 100) / 100;
                    if (Math.abs(aiCalculatedTotal - rawTotalPrice) > 1) {
                        console.warn(
                            `[OCR] Item ${index + 1}: AI discount (${rawDiscountAmt}) produces total ${aiCalculatedTotal}, ` +
                            `but document says ${rawTotalPrice}. Correcting discount to ${impliedDiscount} (${impliedDiscountPct}%).`
                        );
                        resolvedDiscount = Math.max(0, impliedDiscount);
                        resolvedDiscountPct = impliedDiscountPct;
                    }
                } else if (rawDiscountPct > 0) {
                    // Strategy B: No totalPrice available — calculate from percentage
                    const expectedDiscount = Math.round(grossLineTotal * (rawDiscountPct / 100) * 100) / 100;
                    if (Math.abs(resolvedDiscount - expectedDiscount) > 0.01) {
                        console.warn(`[OCR] Item ${index + 1}: discountAmount (${resolvedDiscount}) doesn't match ${rawDiscountPct}% of ${rawQty}×${rawPrice} = ${expectedDiscount}. Using calculated value.`);
                        resolvedDiscount = expectedDiscount;
                    }
                }

                // Strategy C: If we only have discountAmount, calculate the percent
                if (resolvedDiscount > 0 && !resolvedDiscountPct && grossLineTotal > 0) {
                    resolvedDiscountPct = Math.round((resolvedDiscount / grossLineTotal) * 10000) / 100;
                }

                const baseItem = {
                    lineNumber: index + 1,
                    description: item.description || '',
                    quantity: rawQty,
                    unitId: matchedUnit ? matchedUnit.id : null,
                    unit: matchedUnitCode,
                    unitPrice: rawPrice,
                    discountAmount: resolvedDiscount,
                    discountPercent: resolvedDiscountPct,
                    ivaRateId,
                    taxRate: item.taxRate,
                    ivaUncertain
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

        // --- Header IVA Detection ---
        // Detect if the document header/totals imply IVA exists.
        // Compare the OCR grand total vs item-level subtotal (without IVA).
        // If grand total is significantly higher, the document likely has IVA.
        const ocrGrandTotal = getSafeValue<number>(suggestions?.grandTotal, 0) || getSafeValue<number>(suggestions?.totalAmount, 0);
        const itemSubtotalGross = draft.items.reduce((sum, item) => {
            return sum + Math.max(0, (item.quantity || 0) * (item.unitPrice || 0) - (item.discountAmount || 0));
        }, 0) - (draft.discountAmount || 0);

        // Determine if the document header implies IVA exists
        let headerImpliesIva = false;
        if (ocrGrandTotal > 0 && itemSubtotalGross > 0) {
            const impliedIvaRatio = (ocrGrandTotal - itemSubtotalGross) / itemSubtotalGross;
            headerImpliesIva = impliedIvaRatio > 0.01; // >1% difference suggests IVA
        }

        // --- Pass 2: Enhanced Uncertainty Detection ---
        // If the header/totals show IVA exists, but items have taxRate=0, the AI likely
        // defaulted to 0 because IVA was only in the document summary, not per line item.
        // In this case, flag those items as uncertain too — 0 is not a confirmed "Isento".
        if (headerImpliesIva) {
            const allItemsZeroOrUncertain = draft.items.every(
                item => item.ivaUncertain || item.taxRate === 0
            );
            if (allItemsZeroOrUncertain) {
                draft.items.forEach(item => {
                    if (item.taxRate === 0) {
                        console.warn(
                            `[OCR] Item "${(item.description || '').substring(0, 40)}": taxRate=0 but header implies IVA exists. ` +
                            `Flagging as uncertain (AI likely defaulted 0 for missing per-item IVA).`
                        );
                        item.ivaUncertain = true;
                        item.ivaRateId = null; // Clear the auto-resolved "Isento (0%)" match
                    }
                });
            }
        }

        // Set headerHasIva based on whether header implies IVA AND items have uncertainty
        const hasUncertainIvaItems = draft.items.some(item => item.ivaUncertain);
        draft.headerHasIva = headerImpliesIva && hasUncertainIvaItems;

        // ─── Part C: Catalog Item Auto-Match ─────────────────────────────────
        // After all item mapping is done, attempt to auto-link each item to an
        // existing catalog entry using normalized exact matching (server-side).
        // Only 100% normalized matches are accepted — no fuzzy/partial matches.
        // Items with no match get NEEDS_REVIEW status for manual resolution.
        try {
            const descriptions = draft.items.map(item => item.description || '');
            const nonEmptyDescriptions = descriptions.filter(d => d.trim().length > 0);

            if (nonEmptyDescriptions.length > 0) {
                const matchResults = await api.catalogItems.batchMatch(descriptions);

                draft.items.forEach((item, index) => {
                    const match = matchResults[index];
                    if (match && match.id) {
                        item.itemCatalogId = match.id;
                        item.itemCatalogCode = match.code || null;
                        item.autoMatchStatus = 'AUTO_MATCHED';
                        // Use catalog default unit if current unitId is not set
                        if (!item.unitId && match.defaultUnitId) {
                            item.unitId = match.defaultUnitId;
                        }
                        console.log(
                            `[OCR] Item ${index + 1} "${(item.description || '').substring(0, 40)}": ` +
                            `✅ Correspondência automática → ${match.code} (ID: ${match.id})`
                        );
                    } else if (item.description && item.description.trim().length > 0) {
                        item.autoMatchStatus = 'NEEDS_REVIEW';
                    }
                });
            }
        } catch (e) {
            console.warn('[OCR] Catalog auto-match failed (non-blocking):', e);
            // Non-blocking: if auto-match fails, items keep their default state
            // and users can still manually link via autocomplete
        }

        // --- Calculation Diagnostics (visible in browser DevTools > Console) ---
        console.group('[OCR] Extraction & Calculation Diagnostics');
        console.log('Header Total (grandTotal from OCR):', getSafeValue<number>(suggestions?.grandTotal, 0));
        console.log('Header Total (totalAmount fallback):', getSafeValue<number>(suggestions?.totalAmount, 0));
        console.log('Final Draft Total:', draft.totalAmount);
        console.log('Global Discount:', draft.discountAmount);
        console.log('Header Has IVA:', draft.headerHasIva, '| Items with uncertain IVA:', draft.items.filter(i => i.ivaUncertain).length);
        console.table(draft.items.map((item, i) => ({
            '#': i + 1,
            description: (item.description || '').substring(0, 40),
            qty: item.quantity,
            unitPrice: item.unitPrice,
            discount: item.discountAmount,
            totalPrice: item.totalPrice,
            ivaUncertain: item.ivaUncertain,
            autoMatch: item.autoMatchStatus || '-',
            catalogId: item.itemCatalogId || '-',
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
