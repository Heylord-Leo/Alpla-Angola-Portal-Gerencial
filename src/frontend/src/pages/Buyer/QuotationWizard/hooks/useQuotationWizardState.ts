import { useState, useMemo } from 'react';
import { OcrDraft, OcrDraftItem } from '../../../../types';
import { isLineItemEligibleForQuotation } from '../../batchEligibility';
import { validateReconciliationJustification } from '../../../../lib/reconciliationJustificationValidator';
import { hasMaterialOcrChange, isFractionalForIntegerUnit } from '../../../../lib/lineReconciliation';
import {
    ClassificationConflictState,
    EMPTY_CONFLICT,
    canConfirmConflict,
    evaluateClassificationConflict
} from '../../../../lib/documentClassificationDecision';

/** Quality-validates a justification, mirroring the backend. An untouched legacy line — status
 * and justification exactly as persisted (tracked via originalReconciliationJustification, set at
 * hydration in BuyerItemsList) — is exempt, mirroring the backend's legacy-vs-edited skip. */
function isJustificationSatisfied(item: OcrDraftItem): boolean {
    const isLegacyUntouched = item.originalReconciliationJustification != null
        && (item.reconciliationJustification ?? '') === (item.originalReconciliationJustification ?? '');
    if (isLegacyUntouched) return true;
    return validateReconciliationJustification(item.reconciliationJustification).isValid;
}

export type QuotationWizardStep = 'OVERVIEW' | 'DOCUMENTS_OCR' | 'RECONCILIATION' | 'SUPPLIER_VALIDATION' | 'FINAL_REVIEW';

export type QuotationWizardSource = 'MANUAL' | 'UPLOAD';

export interface UseQuotationWizardStateReturn {
    isOpen: boolean;
    currentStep: QuotationWizardStep;
    draft: OcrDraft | null;
    isEditing: boolean;
    editingQuotationId: string | null;
    isFinalReviewConfirmed: boolean;
    
    // Actions
    openWizard: (mode: 'NEW' | 'EDIT', initialDraft?: OcrDraft | null, quotationId?: string, source?: QuotationWizardSource) => void;
    closeWizard: () => void;

    goToStep: (step: QuotationWizardStep) => void;
    setDraft: React.Dispatch<React.SetStateAction<OcrDraft | null>>;
    updateDraftHeader: (field: keyof OcrDraft, value: any, ivaRates?: any[]) => void;
    updateDraftItem: (itemIndex: number, field: keyof OcrDraftItem, value: any, ivaRates?: any[]) => void;
    updateDraftItemFields: (itemIndex: number, fields: Partial<OcrDraftItem>, ivaRates?: any[]) => void;
    addDraftItem: () => void;
    removeDraftItem: (index: number, ivaRates?: any[]) => void;
    toggleNotQuotedPlaceholder: (requestItem: any) => void;
    setIsFinalReviewConfirmed: React.Dispatch<React.SetStateAction<boolean>>;

    /**
     * The Buyer's answer to a classification that contradicts the document reading. Lives on the
     * wizard rather than the draft because it describes a decision about the draft, not a field of
     * it — and it must survive the step navigation that rebuilds parts of the draft.
     */
    classificationConflict: ClassificationConflictState;
    setClassificationConflict: React.Dispatch<React.SetStateAction<ClassificationConflictState>>;

    // State derivation
    canGoNext: (request: any, ivaRates?: any[], units?: any[]) => boolean;
    canSubmit: boolean;
}

export function useQuotationWizardState(): UseQuotationWizardStateReturn {
    const [isOpen, setIsOpen] = useState(false);
    const [currentStep, setCurrentStep] = useState<QuotationWizardStep>('OVERVIEW');
    const [draft, setDraft] = useState<OcrDraft | null>(null);
    const [isEditing, setIsEditing] = useState(false);
    const [editingQuotationId, setEditingQuotationId] = useState<string | null>(null);
    const [isFinalReviewConfirmed, setIsFinalReviewConfirmed] = useState(false);
    const [classificationConflict, setClassificationConflict] =
        useState<ClassificationConflictState>(EMPTY_CONFLICT);

    // Live running total of the WHOLE current draft (every item, regardless of reconciliation
    // status). Matches useOcrProcessor's calculateDraftTotal used for the initial post-OCR
    // value, and matches scope with `ocrTotalAmount` for the DOCUMENTS_OCR step's own
    // OCR-vs-current divergence banner. Filtering to reconciled-only statuses here made the
    // total collapse to 0 during DOCUMENTS_OCR, since items have no reconciliationStatus yet at
    // that stage — that filtering only belongs to the backend's post-reconciliation Financial
    // Integrity comparison and to WizardStepFinalReview's separately-computed "Total Final
    // Considerado", not to this running draft total.
    const recalculateQuotationTotal = (d: OcrDraft, rates: any[]) => {
        let gross = 0;
        let ivaTotal = 0;
        d.items.forEach(item => {
            const itemGrossRaw = Math.round(((item.quantity || 0) * (item.unitPrice || 0)) * 100) / 100;
            const itemDiscount = item.discountAmount || 0;
            const itemNet = Math.max(0, itemGrossRaw - itemDiscount);
            gross += itemNet;
            const selectedIva = rates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            ivaTotal += Math.round(itemNet * (ivaPercent / 100) * 100) / 100;
        });
        const discount = d.discountAmount || 0;
        const taxableBase = Math.max(0, gross - discount);
        const discountRatio = gross > 0 ? (taxableBase / gross) : 1;
        const adjustedIva = Math.round(ivaTotal * discountRatio * 100) / 100;
        return Math.max(0, Math.round((taxableBase + adjustedIva) * 100) / 100);
    };

    const openWizard = (mode: 'NEW' | 'EDIT', initialDraft: OcrDraft | null = null, quotationId?: string, source: QuotationWizardSource = 'MANUAL') => {
        setIsEditing(mode === 'EDIT');
        setEditingQuotationId(quotationId || null);
        setDraft(initialDraft);
        setCurrentStep(source === 'UPLOAD' ? 'DOCUMENTS_OCR' : 'OVERVIEW');
        setIsFinalReviewConfirmed(false);
        // A confirmation belongs to one specific contradiction on one specific document.
        setClassificationConflict(EMPTY_CONFLICT);
        setIsOpen(true);
    };

    const closeWizard = () => {
        setIsOpen(false);
        setDraft(null);
        setIsEditing(false);
        setEditingQuotationId(null);
        setCurrentStep('OVERVIEW');
        setIsFinalReviewConfirmed(false);
        setClassificationConflict(EMPTY_CONFLICT);
    };

    const goToStep = (step: QuotationWizardStep) => {
        setCurrentStep(step);
    };

    const updateDraftHeader = (field: keyof OcrDraft, value: any, ivaRates: any[] = []) => {
        setDraft(prev => {
            if (!prev) return prev;
            const next = { ...prev, [field]: value };
            if (field === 'currency' && value) next.extractedCurrency = undefined;
            if (field === 'discountAmount') next.totalAmount = recalculateQuotationTotal(next, ivaRates);
            return next;
        });
    };

    const updateDraftItem = (itemIndex: number, field: keyof OcrDraftItem, value: any, ivaRates: any[] = []) => {
        setDraft(prev => {
            if (!prev) return prev;
            const items = [...prev.items];
            const item = { ...items[itemIndex], [field]: value };
            
            const safeQty = item.quantity || 0;
            const safePrice = item.unitPrice || 0;
            const grossSubtotal = Math.round(safeQty * safePrice * 100) / 100;
            const itemDiscount = item.discountAmount || 0;
            const netSubtotal = Math.max(0, grossSubtotal - itemDiscount);
            
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            const ivaAmount = Math.round(netSubtotal * (ivaPercent / 100) * 100) / 100;
            
            item.totalPrice = Math.round((netSubtotal + ivaAmount) * 100) / 100;
            items[itemIndex] = item;
            
            const next = { ...prev, items };
            next.totalAmount = recalculateQuotationTotal(next, ivaRates);
            return next;
        });
    };

    
    const toggleNotQuotedPlaceholder = (requestItem: any) => {
        setDraft(prev => {
            if (!prev) return prev;
            const items = [...prev.items];
            const existingIdx = items.findIndex(i => i.reconciliationStatus === 'NOT_QUOTED' && i.mappedRequestLineItemId === requestItem.id);
            
            if (existingIdx >= 0) {
                items.splice(existingIdx, 1);
            } else {
                items.push({
                    lineNumber: items.length > 0 ? Math.max(...items.map(i => i.lineNumber)) + 1 : 1,
                    description: `[NÃO COTADO] ${requestItem.description}`,
                    quantity: 0,
                    unitId: null,
                    unitPrice: 0,
                    ivaRateId: null,
                    totalPrice: 0,
                    itemCatalogId: null,
                    itemCatalogCode: null,
                    discountAmount: 0,
                    mappedRequestLineItemId: requestItem.id,
                    reconciliationStatus: 'NOT_QUOTED'
                });
            }
            
            return { ...prev, items };
        });
    };

    
    const updateDraftItemFields = (itemIndex: number, fields: Partial<OcrDraftItem>, ivaRates: any[] = []) => {
        setDraft(prev => {
            if (!prev) return prev;
            const items = [...prev.items];
            const item = { ...items[itemIndex], ...fields };
            
            const safeQty = item.quantity || 0;
            const safePrice = item.unitPrice || 0;
            const grossSubtotal = Math.round(safeQty * safePrice * 100) / 100;
            const itemDiscount = item.discountAmount || 0;
            const netSubtotal = Math.max(0, grossSubtotal - itemDiscount);
            
            const selectedIva = ivaRates.find(r => r.id === item.ivaRateId);
            const ivaPercent = selectedIva ? selectedIva.ratePercent : 0;
            const ivaAmount = Math.round(netSubtotal * (ivaPercent / 100) * 100) / 100;
            
            item.totalPrice = Math.round((netSubtotal + ivaAmount) * 100) / 100;
            items[itemIndex] = item;
            
            const next = { ...prev, items };
            next.totalAmount = recalculateQuotationTotal(next, ivaRates);
            return next;
        });
    };

    const addDraftItem = () => {
        setDraft(prev => {
            if (!prev) return prev;
            const newItem: OcrDraftItem = {
                lineNumber: prev.items.length + 1,
                description: '',
                quantity: 1,
                unitId: null,
                unitPrice: 0,
                ivaRateId: null,
                totalPrice: 0,
                itemCatalogId: null,
                itemCatalogCode: null,
                discountAmount: 0
            };
            return { ...prev, items: [...prev.items, newItem] };
        });
    };

    const removeDraftItem = (index: number, ivaRates: any[] = []) => {
        setDraft(prev => {
            if (!prev) return prev;
            const items = prev.items.filter((_, i) => i !== index).map((it, i) => ({ ...it, lineNumber: i + 1 }));
            const next = { ...prev, items };
            next.totalAmount = recalculateQuotationTotal(next, ivaRates);
            return next;
        });
    };

    const canGoNext = (request: any, ivaRates: any[] = [], units: any[] = []): boolean => {
        switch (currentStep) {
            case 'OVERVIEW': return true;
            case 'DOCUMENTS_OCR': {
                if (!draft) return false;
                const hasDocNum = !!draft.documentNumber?.trim();
                const hasDocDate = !!draft.documentDate;
                const hasDueDate = !!draft.dueDate;
                const hasDocType = !!draft.documentType;
                const hasCurrency = !!draft.currency;
                const hasItems = draft.items.length > 0;
                const noUncertainIva = draft.items.every(i => !i.ivaUncertain);
                const allItemsValid = draft.items.every(i => i.totalPrice !== undefined && i.totalPrice >= 0);
                // A classification that contradicts the document must be answered before advancing —
                // the same rule the backend enforces, asked at the step where the choice was made.
                const classificationSettled = canConfirmConflict(
                    evaluateClassificationConflict(draft.documentType, draft.documentClassification),
                    classificationConflict.acknowledged,
                    classificationConflict.justification);
                return hasDocNum && hasDocDate && hasDueDate && hasDocType && hasCurrency && hasItems
                    && noUncertainIva && allItemsValid && classificationSettled;
            }
            case 'RECONCILIATION': {
                if (!draft || !request) return false;

                // 1. Every OCR item is resolved and justifications provided if required
                const realItems = draft.items.filter(i => i.reconciliationStatus !== 'NOT_QUOTED');
                for (const item of realItems) {
                    if (!item.reconciliationStatus) return false; // Missing status
                    if (item.reconciliationStatus === 'SUBSTITUTE' && (!item.reconciliationJustification || !item.reconciliationJustification.trim())) return false;
                    if (item.reconciliationStatus === 'EXTRA_ITEM' && !isJustificationSatisfied(item)) return false;
                    // Ignoring a document line WITH value removes it from the integrity baseline —
                    // that exclusion needs a per-line justification (zero-value lines are exempt).
                    if (item.reconciliationStatus === 'IGNORED' && (item.totalPrice ?? 0) > 0 && !isJustificationSatisfied(item)) return false;
                    if (item.reconciliationStatus === 'MAPPED' && !item.mappedRequestLineItemId) return false;
                    if (item.reconciliationStatus === 'SUBSTITUTE' && !item.mappedRequestLineItemId) return false;
                    // Integer-unit quantities must be whole numbers (mirrors the backend invalid-input rule).
                    if (isFractionalForIntegerUnit(item, units)) return false;
                    // Material financial edit vs the OCR baseline requires a valid line-adjustment reason
                    // before advancing (mirrors the backend's per-line 400 gate).
                    if (['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM'].includes(item.reconciliationStatus as string)
                        && hasMaterialOcrChange(item, ivaRates)
                        && !validateReconciliationJustification(item.lineAdjustmentJustification).isValid) return false;
                }

                // NOT_QUOTED marks are document-scoped and need no justification —
                // closing an item without quotation is a separate table action.

                // 2. Every requested item still eligible for this wizard is covered.
                // Items already in a batch, approved, or formally proposed/accepted
                // as not-quoted are handled elsewhere and must not block advancing.
                const pendingCount = (request.lineItems || [])
                    .filter(isLineItemEligibleForQuotation)
                    .filter((reqItem: any) => {
                        const isMapped = draft.items.some(i => (i.reconciliationStatus === 'MAPPED' || i.reconciliationStatus === 'SUBSTITUTE') && i.mappedRequestLineItemId === reqItem.id);
                        const isNotQuoted = draft.items.some(i => i.reconciliationStatus === 'NOT_QUOTED' && i.mappedRequestLineItemId === reqItem.id);
                        return !isMapped && !isNotQuoted;
                    }).length;

                return pendingCount === 0;
            }
            case 'SUPPLIER_VALIDATION': {
                if (!draft) return false;
                return !!draft.supplierId;
            }
            case 'FINAL_REVIEW': return true;
            default: return false;
        }
    };

    const canSubmit = useMemo(() => {
        if (!draft) return false;
        return draft.supplierId !== null &&
               draft.documentNumber?.trim() !== '' &&
               draft.documentDate !== null &&
               draft.dueDate !== null &&
               draft.documentType !== null &&
               draft.currency !== null &&
               draft.items.length > 0 &&
               // The backend rejects an unconfirmed contradiction with a 400; refuse to submit one.
               canConfirmConflict(
                   evaluateClassificationConflict(draft.documentType, draft.documentClassification),
                   classificationConflict.acknowledged,
                   classificationConflict.justification);
    }, [draft, classificationConflict]);

    return {
        isOpen,
        currentStep,
        draft,
        isEditing,
        editingQuotationId,
        isFinalReviewConfirmed,
        openWizard,
        closeWizard,
        goToStep,
        setDraft,
        updateDraftHeader,
        updateDraftItem,
        updateDraftItemFields,
        addDraftItem,
        removeDraftItem,
        toggleNotQuotedPlaceholder,
        setIsFinalReviewConfirmed,
        classificationConflict,
        setClassificationConflict,

        canGoNext,
        canSubmit
    };
}
