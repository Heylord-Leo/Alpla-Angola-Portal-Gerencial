import React from 'react';
import { api } from '../../../lib/api';
import {
    SavedQuotationDto, OcrDraft, OcrDraftItem, FinancialIntegrityCheckFailedDto, AmbiguousSavePreAttemptSnapshot,
} from '../../../types';
import { isFiscalDocument } from '../../../lib/sourceDocumentType';
import { OcrDocumentClassification } from '../../../lib/documentClassificationDecision';
import type { UseQuotationWizardStateReturn } from './hooks/useQuotationWizardState';
import { buildQuotationPayload, tryReconcileAmbiguousSave } from './quotationSaveLogic';
import { buildManualQuotationDraftItems } from './manualQuotationDraft';

// ─────────────────────────────────────────────────────────────────────────────
// Reusable Quotation Wizard ORCHESTRATION FACADE (Phase 3C.1 / Stage 2A-R). A STATELESS factory: it
// owns NO state and no hooks. It receives the host's state + setters + refs + callbacks as explicit
// dependencies and returns the wizard handler functions — the SAME implementations previously inlined
// in BuyerItemsList, MOVED verbatim (single-source), with every `loadData()` → `onSaved()` and
// `setFeedback(...)` → `onFeedback(...)`. Call ordering, arguments and return shapes are unchanged.
//
// State ownership stays with the host (BuyerItemsList keeps isSaving, quotationWizardState,
// wizardActiveRequest, isProcessingOcr, temp-attachment ids, the ambiguous-save ref, and the entire
// duplicate-file feature). The host recreates the controller each render with current values, so the
// returned closures behave exactly like the former inline handlers. Wizard internals are untouched.
// ─────────────────────────────────────────────────────────────────────────────

/** Rehydrates the stored evidence blob, tolerating anything that is not the shape we wrote. */
function parseClassificationEvidence(json?: string | null): Partial<OcrDocumentClassification> {
    if (!json) return {};
    try {
        const parsed = JSON.parse(json);
        return parsed && typeof parsed === 'object' ? parsed as Partial<OcrDocumentClassification> : {};
    } catch {
        return {};
    }
}

export type WizardFeedback = { type: 'success' | 'error' | 'info'; message: string };

export interface BuyerQuotationWizardControllerDeps {
    quotationWizardState: UseQuotationWizardStateReturn;
    wizardActiveRequest: any;
    setWizardActiveRequest: React.Dispatch<React.SetStateAction<any>>;
    setIsSaving: (v: boolean) => void;
    setIsProcessingOcr: React.Dispatch<React.SetStateAction<Record<string, boolean>>>;
    temporaryWizardAttachmentIds: string[];
    setTemporaryWizardAttachmentIds: React.Dispatch<React.SetStateAction<string[]>>;
    preAttemptSnapshotRef: React.MutableRefObject<AmbiguousSavePreAttemptSnapshot | 'unavailable' | null>;
    /** From the host's useOcrProcessor. */
    mapOcrResultToDraft: (result: any, attachmentId?: string) => Promise<OcrDraft>;
    /** Host refresh after a successful save (classic: loadData; workspace: reload projection). */
    onSaved: () => void | Promise<void>;
    /** Host feedback surface (no browser alerts). */
    onFeedback: (feedback: WizardFeedback) => void;
}

export function createBuyerQuotationWizardController(deps: BuyerQuotationWizardControllerDeps) {
    const {
        quotationWizardState, wizardActiveRequest, setWizardActiveRequest, setIsSaving, setIsProcessingOcr,
        temporaryWizardAttachmentIds, setTemporaryWizardAttachmentIds, preAttemptSnapshotRef,
        mapOcrResultToDraft, onSaved, onFeedback,
    } = deps;

    const handleWizardSaveQuotation = async (
        draft: OcrDraft,
        overridePayload?: { financialIntegrityOverride: boolean; overrideJustification: string }
    ): Promise<
        | { success: true }
        | ({ success: false } & FinancialIntegrityCheckFailedDto)
        | { success: false; residualUnexplained: true; reconciliation: any; detail: string }
        | { success: false; error: string }
    > => {
        if (!wizardActiveRequest) return { success: false, error: 'No active request' };
        try {
            const payload = buildQuotationPayload(draft, quotationWizardState.classificationConflict);

            setIsSaving(true);
            const requestId = wizardActiveRequest.requestId;
            const isEditing = quotationWizardState.isEditing;
            const quotationId = quotationWizardState.editingQuotationId;

            const ocrTotalHeader = typeof draft.ocrTotalAmount === 'number' && Number.isFinite(draft.ocrTotalAmount) && draft.ocrTotalAmount > 0
                ? draft.ocrTotalAmount
                : undefined;

            if (isEditing && quotationId) {
                const updateResult = await api.requests.updateQuotation(requestId, quotationId, {
                    ...payload,
                    ocrTotal: ocrTotalHeader,
                    financialIntegrityOverride: overridePayload?.financialIntegrityOverride ?? false,
                    overrideJustification: overridePayload?.overrideJustification
                });
                if (updateResult && 'residualUnexplained' in updateResult && updateResult.residualUnexplained) {
                    return { success: false, residualUnexplained: true, reconciliation: updateResult.reconciliation, detail: updateResult.detail } as any;
                }
            } else {
                if (preAttemptSnapshotRef.current === null) {
                    try {
                        const freshRequest = await api.requests.get(requestId);
                        preAttemptSnapshotRef.current = {
                            existingQuotationIds: new Set((freshRequest.quotations || []).map(q => q.id)),
                            attemptStartedAtUtc: new Date().toISOString()
                        };
                    } catch (baselineError) {
                        console.warn('[AmbiguousSave] Could not capture a fresh quotation baseline before save; ambiguous-save reconciliation will be skipped for this submission if the create call fails ambiguously.', baselineError);
                        preAttemptSnapshotRef.current = 'unavailable';
                    }
                }
                const preAttemptSnapshot = preAttemptSnapshotRef.current !== 'unavailable' ? preAttemptSnapshotRef.current : null;

                const ocrTotal = typeof draft.ocrTotalAmount === 'number' && Number.isFinite(draft.ocrTotalAmount) && draft.ocrTotalAmount > 0
                    ? draft.ocrTotalAmount
                    : undefined;
                let createResult: any;
                try {
                    createResult = await api.requests.saveQuotation(requestId, {
                        ...payload,
                        ocrTotal,
                        financialIntegrityOverride: overridePayload?.financialIntegrityOverride ?? false,
                        overrideJustification: overridePayload?.overrideJustification
                    });
                } catch (createError: any) {
                    const isNetworkError = createError?.status === 0 || createError?.status === undefined;
                    const is5xx = typeof createError?.status === 'number' && createError.status >= 500 && createError.status < 600;
                    const isDuplicateSupplierConflict = createError?.status === 409 &&
                        createError?.details?.title === 'Regra de Negócio Violada';

                    if ((isNetworkError || is5xx || isDuplicateSupplierConflict) && preAttemptSnapshot) {
                        const matched = await tryReconcileAmbiguousSave(requestId, draft, preAttemptSnapshot);
                        if (matched) {
                            preAttemptSnapshotRef.current = null;
                            await onSaved();
                            quotationWizardState.closeWizard();
                            setWizardActiveRequest(null);
                            setTemporaryWizardAttachmentIds([]);
                            onFeedback({
                                type: 'info',
                                message: 'Cotação salva após interrupção da resposta: a resposta inicial foi interrompida, mas confirmámos que a cotação foi salva corretamente no servidor.'
                            });
                            return { success: true };
                        }
                    }

                    throw createError;
                }

                if (createResult && 'integrityCheckFailed' in createResult && createResult.integrityCheckFailed) {
                    return {
                        success: false,
                        integrityCheckFailed: true,
                        ocrOriginalTotal: createResult.ocrOriginalTotal,
                        quotationTotal: createResult.quotationTotal,
                        varianceAmount: createResult.varianceAmount,
                        variancePercent: createResult.variancePercent,
                        toleranceApplied: createResult.toleranceApplied,
                        detail: createResult.detail
                    };
                }
                if (createResult && 'residualUnexplained' in createResult && createResult.residualUnexplained) {
                    return { success: false, residualUnexplained: true, reconciliation: createResult.reconciliation, detail: createResult.detail } as any;
                }
            }

            preAttemptSnapshotRef.current = null;
            await onSaved();
            quotationWizardState.closeWizard();
            setWizardActiveRequest(null);
            setTemporaryWizardAttachmentIds([]);
            onFeedback({ type: 'success', message: isEditing ? 'Cotação atualizada com sucesso!' : 'Cotação registrada com sucesso!' });

            return { success: true };
        } catch (error: any) {
            console.error('Error saving quotation from wizard:', error);
            return { success: false, error: error?.message || 'Erro ao salvar cotação.' };
        } finally {
            setIsSaving(false);
        }
    };

    // The actual upload + OCR sequence, unchanged. The host's dup-check wrapper calls this to proceed.
    const startWizardUpload = async (file: File) => {
        if (!wizardActiveRequest) return;
        setIsProcessingOcr(prev => ({ ...prev, [wizardActiveRequest.requestId]: true }));
        try {
            const uploadData = await api.attachments.upload(wizardActiveRequest.requestId, [file], 'QUOTATION');
            const attachmentId = uploadData[0].id;
            setTemporaryWizardAttachmentIds(prev => [...prev, attachmentId]);
            const result = await api.requests.ocrExtract(wizardActiveRequest.requestId, file);
            const initialDraft = await mapOcrResultToDraft(result, attachmentId);

            quotationWizardState.setDraft(initialDraft);
        } catch (error: any) {
            console.error('Wizard OCR Error:', error);
            onFeedback({ type: 'error', message: 'Erro ao processar documento via OCR.' });
        } finally {
            setIsProcessingOcr(prev => ({ ...prev, [wizardActiveRequest.requestId]: false }));
        }
    };

    const handleReconcilePreview = async (draft: OcrDraft): Promise<any> => {
        if (!wizardActiveRequest) throw new Error('No active request');
        const ocrTotal = typeof draft.ocrTotalAmount === 'number' && Number.isFinite(draft.ocrTotalAmount) && draft.ocrTotalAmount > 0
            ? draft.ocrTotalAmount : undefined;
        const payload = { ...buildQuotationPayload(draft), ocrTotal };
        return await api.requests.reconcilePreview(
            wizardActiveRequest.requestId, payload, quotationWizardState.editingQuotationId || undefined);
    };

    const handleReplaceDocumentForWizard = async (attachmentId: string) => {
        if (!wizardActiveRequest || !quotationWizardState.editingQuotationId) return false;
        try {
            await api.requests.updateQuotation(wizardActiveRequest.requestId, quotationWizardState.editingQuotationId, {
                proformaAttachmentId: attachmentId
            } as any);
            return true;
        } catch (err) {
            return false;
        }
    };

    const handleWizardLineItemUpserted = (item: any) => {
        if (!item || !item.id) return;
        setWizardActiveRequest((prev: any) => {
            if (!prev) return prev;
            const existing = prev.lineItems || [];
            const mapped = {
                id: item.id,
                lineNumber: item.lineNumber,
                description: item.description,
                quantity: item.quantity,
                unit: undefined,
                unitId: item.unitId ?? null,
                unitPrice: item.unitPrice ?? 0,
                totalAmount: (item.unitPrice ?? 0) * (item.quantity ?? 1),
                notes: null,
                itemCatalogId: item.itemCatalogId ?? null,
                lineItemStatusCode: undefined,
                quotationLifecycleStatus: item.quotationLifecycleStatus,
            };
            const idx = existing.findIndex((li: any) => li.id === item.id);
            const lineItems = idx >= 0
                ? existing.map((li: any, i: number) => (i === idx ? { ...li, ...mapped } : li))
                : [...existing, mapped];
            return { ...prev, lineItems };
        });
    };

    const onCancelWizard = async () => {
        quotationWizardState.closeWizard();
        setWizardActiveRequest(null);
        preAttemptSnapshotRef.current = null;
        if (temporaryWizardAttachmentIds.length > 0) {
            try {
                await Promise.all(temporaryWizardAttachmentIds.map(id => api.attachments.delete(id)));
            } catch (err) {
                console.error('Error cleaning up temporary wizard attachments:', err);
            }
            setTemporaryWizardAttachmentIds([]);
        }
    };

    const handleOpenWizard = (group: any, mode: 'MANUAL' | 'UPLOAD', editQuotation?: SavedQuotationDto) => {
        preAttemptSnapshotRef.current = null;
        setWizardActiveRequest(group);
        if (editQuotation) {
            const draft: OcrDraft = {
                supplierId: editQuotation.supplierId || null,
                supplierNameSnapshot: editQuotation.supplierNameSnapshot || '',
                documentNumber: editQuotation.documentNumber || '',
                documentDate: editQuotation.documentDate ? editQuotation.documentDate.split('T')[0] : '',
                documentType: editQuotation.documentType === 'FINAL_INVOICE'
                    ? 'FINAL'
                    : (editQuotation.documentType as OcrDraft['documentType']) || undefined,
                documentClassification: editQuotation.documentTypeOcrSuggestion
                    ? {
                        suggestedType: editQuotation.documentTypeOcrSuggestion,
                        confidence: editQuotation.documentTypeOcrConfidence ?? null,
                        indicatesFiscalDocument: isFiscalDocument(editQuotation.documentTypeOcrSuggestion),
                        ...parseClassificationEvidence(editQuotation.documentTypeEvidenceJson)
                    }
                    : null,
                currency: editQuotation.currency || 'AOA',
                totalAmount: editQuotation.totalAmount || 0,
                discountAmount: editQuotation.discountAmount || 0,
                proformaAttachmentId: editQuotation.proformaAttachmentId || undefined,
                items: editQuotation.items.map(item => ({
                    mappedRequestLineItemId: item.mappedRequestLineItemId,
                    lineNumber: item.lineNumber,
                    description: item.description,
                    quantity: item.quantity,
                    unitId: item.unitId || null,
                    unitPrice: item.unitPrice,
                    ivaRateId: item.ivaRateId || null,
                    totalPrice: item.lineTotal,
                    discountAmount: item.discountAmount || 0,
                    itemCatalogId: item.itemCatalogId || null,
                    reconciliationStatus: (item.reconciliationStatus || (item.mappedRequestLineItemId ? 'MAPPED' : 'NOT_QUOTED')) as OcrDraftItem['reconciliationStatus'],
                    reconciliationJustification: item.reconciliationJustification || null,
                    originalReconciliationJustification: item.reconciliationJustification || null,
                    lineOrigin: (item.ocrOriginalLineTotal != null || item.ocrOriginalQuantity != null) ? 'OCR' : 'MANUAL',
                    ocrOriginalQuantity: item.ocrOriginalQuantity ?? null,
                    ocrOriginalUnitPrice: item.ocrOriginalUnitPrice ?? null,
                    ocrOriginalDiscountAmount: item.ocrOriginalDiscountAmount ?? null,
                    ocrOriginalIvaRatePercent: item.ocrOriginalIvaRatePercent ?? null,
                    ocrOriginalUnitText: item.ocrOriginalUnitText ?? null,
                    ocrOriginalUnitId: item.ocrOriginalUnitId ?? null,
                    ocrOriginalLineTotal: item.ocrOriginalLineTotal ?? null,
                    lineAdjustmentJustification: item.lineAdjustmentJustification || null
                }))
            };
            quotationWizardState.openWizard('EDIT', draft, editQuotation.id, mode);
        } else {
            const initialDraft: OcrDraft = {
                supplierId: null,
                supplierNameSnapshot: '',
                documentNumber: '',
                documentDate: '',
                currency: 'AOA',
                totalAmount: 0,
                discountAmount: 0,
                // Only items still open for quotation are seeded — as priceable quotation rows
                // (reconciliationStatus unset), from the NORMALIZED request line items. See
                // manualQuotationDraft.ts (Option A fix + shape follow-up).
                items: buildManualQuotationDraftItems(group.lineItems)
            };
            quotationWizardState.openWizard('NEW', mode === 'UPLOAD' ? null : initialDraft, undefined, mode);
        }
    };

    return {
        handleWizardSaveQuotation,
        startWizardUpload,
        handleReconcilePreview,
        handleReplaceDocumentForWizard,
        handleWizardLineItemUpserted,
        onCancelWizard,
        handleOpenWizard,
    };
}
