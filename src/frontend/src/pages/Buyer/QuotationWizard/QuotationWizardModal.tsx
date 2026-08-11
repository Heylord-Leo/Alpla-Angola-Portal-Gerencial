import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { X, ChevronRight, ChevronLeft, Save, AlertCircle, ShieldAlert } from 'lucide-react';
import { RequestDetailsDto, FinancialIntegrityCheckFailedDto, QuotationReconciliationDto } from '../../../types';
import { useQuotationWizardState, QuotationWizardStep } from './hooks/useQuotationWizardState';
import { useQuotationValidation } from './hooks/useQuotationValidation';
import { formatCurrencyAO } from '../../../lib/utils';
import { draftCalculationSignature } from '../../../lib/lineReconciliation';
import { WizardStepRequestOverview } from './WizardStepRequestOverview';
import { WizardStepDocumentsOcr } from './WizardStepDocumentsOcr';
import { WizardStepReconciliation } from './WizardStepReconciliation';
import { WizardStepFinalReview } from './WizardStepFinalReview';
import { WizardStepSupplierValidation } from './WizardStepSupplierValidation';

type QuotationSaveResult =
    | { success: true }
    | ({ success: false } & FinancialIntegrityCheckFailedDto)
    | { success: false; residualUnexplained: true; reconciliation: QuotationReconciliationDto; detail: string }
    | { success: false; error: string };

function isResidualFailure(
    result: QuotationSaveResult
): result is { success: false; residualUnexplained: true; reconciliation: QuotationReconciliationDto; detail: string } {
    return result.success === false && 'residualUnexplained' in result && (result as any).residualUnexplained === true;
}

// TS can't structurally narrow an intersection-typed union member via `in`/property checks
// (the discriminant lives on a mixed-in type, not a flat literal field), so an explicit type
// guard is used instead of relying on automatic narrowing.
function isIntegrityFailure(
    result: QuotationSaveResult
): result is { success: false } & FinancialIntegrityCheckFailedDto {
    return result.success === false && 'integrityCheckFailed' in result && result.integrityCheckFailed === true;
}

interface QuotationWizardModalProps {
    request: RequestDetailsDto | null;
    wizardState: ReturnType<typeof useQuotationWizardState>;
    onSaveQuotation: (
        draft: any,
        overridePayload?: { financialIntegrityOverride: boolean; overrideJustification: string }
    ) => Promise<QuotationSaveResult>;
    /** Authoritative, read-only reconciliation preview for the current draft. Resolves to a
     * QuotationReconciliationDto; rejects on failure (the modal shows the error + a retry). */
    onReconcilePreview: (draft: any) => Promise<QuotationReconciliationDto>;
    isProcessingOcr: boolean;
    onUploadFile: (file: File) => void;
    onCancelWizard: () => Promise<void>;
    onReplaceDocument: (attachmentId: string) => Promise<boolean>;
    ivaRates: any[];
    units: any[];
    currencies: any[];
    /** Upsert a requested line item created/returned by the from-proforma workaround into the active request. */
    onRequestLineItemUpserted?: (item: any) => void;
}

const STEPS: { key: QuotationWizardStep; title: string }[] = [
    { key: 'OVERVIEW', title: 'Visão Geral' },
    { key: 'DOCUMENTS_OCR', title: 'Documento e Extração' },
    { key: 'RECONCILIATION', title: 'Reconciliação' },
    { key: 'SUPPLIER_VALIDATION', title: 'Validação do Fornecedor' },
    { key: 'FINAL_REVIEW', title: 'Revisão Final' }
];

export const QuotationWizardModal: React.FC<QuotationWizardModalProps> = ({
    request,
    wizardState,
    onSaveQuotation,
    onReconcilePreview,
    isProcessingOcr,
    onUploadFile,
    onCancelWizard,
    onReplaceDocument,
    ivaRates,
    units,
    currencies,
    onRequestLineItemUpserted
}) => {
    const { 
        isOpen, 
        currentStep, 
        draft, 
        isEditing, 
        closeWizard, 
        goToStep, 
        canGoNext, 
        canSubmit,
        } = wizardState;
    const { validateDraft } = useQuotationValidation();
    const [isSaving, setIsSaving] = useState(false);
    const [saveError, setSaveError] = useState<string | null>(null);

    // Financial Integrity Gate (409) — kept as structured data, separate from the plain
    // `saveError` string and from the unrelated per-item `reconciliationJustification`
    // mechanism. `overrideJustification` is local to this modal, never merged into the draft.
    const [integrityError, setIntegrityError] = useState<FinancialIntegrityCheckFailedDto | null>(null);
    // Signed-residual gate: the unexplained document difference after explained line adjustments.
    const [residualError, setResidualError] = useState<QuotationReconciliationDto | null>(null);
    const [overrideJustification, setOverrideJustification] = useState('');

    // ── Authoritative pre-save reconciliation preview (primary UX; the 409 handler is the fallback) ──
    const [preview, setPreview] = useState<QuotationReconciliationDto | null>(null);
    const [previewLoading, setPreviewLoading] = useState(false);
    const [previewError, setPreviewError] = useState<string | null>(null);
    // Signature the current preview was computed for — used to detect a stale preview after edits.
    const [previewSignature, setPreviewSignature] = useState<string | null>(null);
    const previewReqId = useRef(0);

    const draftSignature = draft ? draftCalculationSignature(draft) : '';
    const previewStale = !!preview && previewSignature !== draftSignature;
    // Whether this draft even carries an OCR header total (otherwise no reconciliation applies).
    const hasOcrTotal = typeof draft?.ocrTotalAmount === 'number' && Number.isFinite(draft.ocrTotalAmount) && draft.ocrTotalAmount > 0;

    const fetchPreview = React.useCallback(async () => {
        if (!draft) return;
        const sig = draftCalculationSignature(draft);
        const reqId = ++previewReqId.current;
        setPreviewLoading(true);
        setPreviewError(null);
        try {
            const result = await onReconcilePreview(draft);
            if (reqId !== previewReqId.current) return; // a newer request superseded this one
            setPreview(result);
            setPreviewSignature(sig);
        } catch (err: any) {
            if (reqId !== previewReqId.current) return;
            setPreview(null);
            setPreviewSignature(null);
            setPreviewError(err?.message || 'Não foi possível calcular o resumo de reconciliação. Tente recalcular.');
        } finally {
            if (reqId === previewReqId.current) setPreviewLoading(false);
        }
    }, [draft, onReconcilePreview]);

    // Fetch (or refresh) the authoritative preview whenever the user is on the final-review step and
    // the preview is missing or stale (any calculation-affecting field changed on an earlier step).
    useEffect(() => {
        if (!isOpen) return;
        if (currentStep !== 'FINAL_REVIEW') return;
        if (!hasOcrTotal) { setPreview(null); setPreviewError(null); return; }
        if (previewLoading) return;
        if (!preview || previewStale) { void fetchPreview(); }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isOpen, currentStep, draftSignature, hasOcrTotal]);

    // Residual gate derived from the AUTHORITATIVE preview (not from a 409).
    const residualBlocksSave = !!preview && Math.abs(preview.residualVariance) > preview.toleranceApplied;
    const residualJustificationValid = overrideJustification.trim().length >= 20;
    const [justificationTouched, setJustificationTouched] = useState(false);
    const justificationRef = useRef<HTMLTextAreaElement>(null);

    // Synchronous re-entrancy lock: `isSaving` (React state) is not guaranteed to have committed
    // between two rapid-fire click events in the same tick, so this ref is checked/set BEFORE
    // any await, and cleared in a finally block — belt-and-suspenders alongside the `disabled`
    // button state and the `isSaving` state check.
    const isSavingRef = useRef(false);

    const [mounted, setMounted] = useState(false);
    useEffect(() => {
        setMounted(true);
    }, []);

    // The modal stays mounted while closed (returns null), so local state survives between
    // sessions. A save error (and any Financial Integrity override in progress) from a
    // previous quotation must never leak into a new one: reset transient submit state on
    // every open/close transition.
    useEffect(() => {
        setSaveError(null);
        setIsSaving(false);
        setIntegrityError(null);
        setResidualError(null);
        setOverrideJustification('');
        setJustificationTouched(false);
        setPreview(null);
        setPreviewError(null);
        setPreviewSignature(null);
        isSavingRef.current = false;
    }, [isOpen]);

    // Lock background document scroll while the wizard modal is open
    useEffect(() => {
        if (!isOpen) return;

        const previousOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';

        return () => {
            document.body.style.overflow = previousOverflow;
        };
    }, [isOpen]);

    // Within the SAME session: as soon as the user starts fixing the cause (any draft
    // mutation — reconciliation status, justification, financial values, document), the stale
    // save error must disappear. The draft reference only changes on real mutations, so an
    // untouched error stays visible. A stale Financial Integrity comparison is invalidated
    // the same way — editing the draft after a 409 means the totals it was computed from no
    // longer apply, so the panel (and any half-typed justification) is cleared too.
    useEffect(() => {
        setSaveError(null);
        setIntegrityError(null);
        setResidualError(null);
        setOverrideJustification('');
        setJustificationTouched(false);
    }, [draft]);

    // Accessibility: move focus into the justification field as soon as the panel appears.
    useEffect(() => {
        if (integrityError || residualError) {
            justificationRef.current?.focus();
        }
    }, [integrityError, residualError]);

    if (!isOpen) return null;

    const currentIndex = STEPS.findIndex(s => s.key === currentStep);
    const totalSteps = STEPS.length;
    const isLastStep = currentIndex === totalSteps - 1;

    const handleNext = () => {
        const isNextAllowed = canGoNext(request, ivaRates, units);
        if (isNextAllowed && currentIndex < totalSteps - 1) {
            goToStep(STEPS[currentIndex + 1].key);
        }
    };

    const handleBack = () => {
        setSaveError(null); // navigating back to fix data — the previous save error is stale
        setIntegrityError(null); // ...and so is any Financial Integrity comparison computed from it
        setOverrideJustification('');
        setJustificationTouched(false);
        if (currentIndex > 0) {
            goToStep(STEPS[currentIndex - 1].key);
        }
    };

    // Shared by the initial "Salvar Cotação" click and the override retry — always resends the
    // COMPLETE current draft (never just the override fields). Guarded twice against duplicate
    // submission: the synchronous `isSavingRef` (checked/set before any await, so it closes the
    // same-tick rapid-click window that `isSaving` state alone cannot guarantee) and the
    // `isSaving` state (drives the `disabled` button prop).
    const performSave = async (overridePayload?: { financialIntegrityOverride: boolean; overrideJustification: string }) => {
        if (!canSubmit || isSaving || isSavingRef.current) return;
        isSavingRef.current = true;

        const validation = validateDraft(draft, ivaRates, units);
        if (!validation.isValid) {
            setSaveError(validation.errors.join('\n'));
            isSavingRef.current = false;
            return;
        }

        setIsSaving(true);
        setSaveError(null);
        try {
            // Ambiguous-save pre-attempt snapshot capture/reuse now lives entirely inside
            // onSaveQuotation (BuyerItemsList.handleWizardSaveQuotation) — it needs a FRESH
            // server read, not this modal's `request` prop (client-side state, populated once
            // when the wizard opened and never refreshed; could predate a quotation created by
            // another tab/session). See that function for the fresh-GET capture logic.
            const response = await onSaveQuotation(draft, overridePayload);
            if (response.success) {
                setIntegrityError(null);
                setResidualError(null);
                closeWizard();
            } else if (isResidualFailure(response)) {
                // Signed-residual gate: show the breakdown and require a residual justification.
                setResidualError(response.reconciliation);
            } else if (isIntegrityFailure(response)) {
                // Keep the panel visible and refresh the comparison values on every attempt —
                // including a retry that fails again with a new variance. The justification the
                // buyer already typed is intentionally left untouched.
                setIntegrityError(response);
            } else {
                // A failure unrelated to Financial Integrity must not discard an in-progress
                // override: leave `integrityError`/`overrideJustification` exactly as they were.
                setSaveError(response.error || 'Não foi possível salvar a cotação. Verifique os dados e tente novamente.');
            }
        } finally {
            setIsSaving(false);
            isSavingRef.current = false;
        }
    };

    // Primary save: when the authoritative preview reports an over-tolerance residual, the residual
    // justification is sent as the override (the residual amount is still recorded, never zeroed).
    const handleSave = () => {
        if (residualBlocksSave) {
            setJustificationTouched(true);
            if (!residualJustificationValid) { justificationRef.current?.focus(); return; }
            performSave({ financialIntegrityOverride: true, overrideJustification: overrideJustification.trim() });
        } else {
            performSave();
        }
    };

    // Save is blocked until the authoritative preview is present, fresh, and — when it reports an
    // over-tolerance residual — a valid residual justification has been entered.
    const previewGateBlocksSave = currentStep === 'FINAL_REVIEW' && hasOcrTotal &&
        (previewLoading || !!previewError || !preview || previewStale || (residualBlocksSave && !residualJustificationValid));

    const handleRetryWithOverride = () => {
        const trimmed = overrideJustification.trim();
        setJustificationTouched(true);
        if (!trimmed) {
            justificationRef.current?.focus();
            return;
        }
        performSave({ financialIntegrityOverride: true, overrideJustification: trimmed });
    };

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files.length > 0) {
            onUploadFile(e.target.files[0]);
        }
    };

    const handleReplaceConfirm = async () => {
        setSaveError(null); // a new document starts a fresh attempt — previous save errors are stale
        setIntegrityError(null);
        setResidualError(null);
        setOverrideJustification('');
        setJustificationTouched(false);
        if (!isEditing && draft?.proformaAttachmentId) {
            const success = await onReplaceDocument(draft.proformaAttachmentId);
            if (success) {
                wizardState.setDraft(null);
            }
        } else {
            wizardState.setDraft(null);
        }
    };

    const handleClose = async () => {
        setSaveError(null); // never carry a save error into the next wizard session
        setIntegrityError(null); // ...nor a Financial Integrity override in progress
        setOverrideJustification('');
        setJustificationTouched(false);
        await onCancelWizard();
    };

    const validation = validateDraft(draft, ivaRates, units);

    const modalContent = (
        <div
            style={{
                position: 'fixed',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.6)',
                backdropFilter: 'blur(4px)',
                zIndex: 1500,
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                padding: '24px'
            }}
        >
            <div
                className="wizard-modal-container"
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#F3F4F6',
                    borderRadius: '12px',
                    width: '100%',
                    maxWidth: 'min(1200px, calc(100vw - 48px))',
                    display: 'flex',
                    flexDirection: 'column',
                    boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                    overflow: 'hidden'
                }}
            >
            
            {/* Header */}
            <div style={{ padding: '16px 24px', borderBottom: '1px solid var(--color-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#f8fafc' }}>
                <div>
                    <h2 style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 }}>
                        {isEditing ? 'Editar Cotação' : 'Registrar Nova Cotação'}
                    </h2>
                    {request && (
                        <p style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', margin: '4px 0 0 0' }}>
                            {request.requestNumber} • {request.requesterName}
                        </p>
                    )}
                </div>
                <button 
                    onClick={handleClose}
                    style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '8px', borderRadius: '50%' }}
                    onMouseOver={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                    onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                >
                    <X size={24} />
                </button>
            </div>

            {/* Progress Indicators */}
            <div style={{ backgroundColor: '#fff', borderBottom: '1px solid var(--color-border)', padding: '12px 24px' }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    {STEPS.map((step, index) => {
                        const isCompleted = index < currentIndex;
                        const isCurrent = index === currentIndex;
                        
                        return (
                            <div key={step.key} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flex: 1, position: 'relative' }}>
                                <div style={{ display: 'flex', alignItems: 'center', width: '100%', justifyContent: 'center', position: 'relative' }}>
                                    <div style={{
                                        width: '32px', height: '32px', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.875rem', fontWeight: 700, zIndex: 10,
                                        backgroundColor: isCompleted ? 'var(--color-primary)' : isCurrent ? '#e0f2fe' : '#f1f5f9',
                                        color: isCompleted ? '#fff' : isCurrent ? 'var(--color-primary)' : 'var(--color-text-muted)',
                                        border: `2px solid ${isCompleted ? 'var(--color-primary)' : isCurrent ? 'var(--color-primary)' : 'var(--color-border)'}`
                                    }}>
                                        {index + 1}
                                    </div>
                                    {index < totalSteps - 1 && (
                                        <div style={{
                                            position: 'absolute', top: '15px', left: '50%', width: '100%', height: '2px', zIndex: 0,
                                            backgroundColor: isCompleted ? 'var(--color-primary)' : 'var(--color-border)'
                                        }} />
                                    )}
                                </div>
                                <span style={{ marginTop: '8px', fontSize: '0.75rem', fontWeight: 600, color: isCurrent ? 'var(--color-primary)' : isCompleted ? 'var(--color-text-main)' : 'var(--color-text-muted)' }}>
                                    {step.title}
                                </span>
                            </div>
                        );
                    })}
                </div>
            </div>

            {/* Content Area */}
            <div style={{ flex: 1, overflowY: 'auto', padding: '24px', backgroundColor: '#f8fafc' }}>
                <div style={{ backgroundColor: '#fff', borderRadius: '8px', border: '1px solid var(--color-border)', padding: '24px', minHeight: '100%', boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)' }}>
                    {currentStep === 'OVERVIEW' && (
                            <WizardStepRequestOverview request={request} />
                        )}
                        {currentStep === 'DOCUMENTS_OCR' && (
                    <WizardStepDocumentsOcr
                        draft={draft}
                        isProcessingOcr={isProcessingOcr}
                        onUpload={handleFileChange}
                        onReplaceConfirm={handleReplaceConfirm}
                        wizardState={wizardState}
                        ivaRates={ivaRates}
                        units={units}
                        currencies={currencies}
                        request={request}
                    />
                )}
                        {currentStep === 'RECONCILIATION' && (
                            <WizardStepReconciliation draft={draft} request={request} wizardState={wizardState} ivaRates={ivaRates} units={units} onRequestLineItemUpserted={onRequestLineItemUpserted} />
                        )}
                        {currentStep === 'SUPPLIER_VALIDATION' && (
                            <WizardStepSupplierValidation draft={draft} wizardState={wizardState} />
                        )}
                        {currentStep === 'FINAL_REVIEW' && (
                            <WizardStepFinalReview
                                draft={draft}
                                validation={validation}
                                wizardState={wizardState}
                                reconciliation={preview}
                                reconciliationLoading={previewLoading}
                                reconciliationError={previewError}
                                reconciliationStale={previewStale}
                                hasOcrTotal={hasOcrTotal}
                                onRecalculate={fetchPreview}
                                residualJustification={overrideJustification}
                                onResidualJustificationChange={setOverrideJustification}
                                residualJustificationTouched={justificationTouched}
                                justificationRef={justificationRef}
                            />
                        )}
                    </div>
            </div>

            {/* Financial Integrity Gate override panel — replaces the plain error panel below
                when SaveQuotation returns 409 integrityCheckFailed. This is the ONLY path back
                to a successful save once the buyer's corrections diverge from the OCR total
                beyond tolerance; it is intentionally independent of reconciliationJustification. */}
            {integrityError && (() => {
                const missingJustification = justificationTouched && !overrideJustification.trim();
                const currency = draft?.currency;
                return (
                    <div style={{ backgroundColor: 'var(--color-status-red-surface)', borderTop: '1px solid var(--color-status-red)', padding: '16px 24px', display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-status-red)', fontWeight: 700, fontSize: '0.9rem' }}>
                            <ShieldAlert style={{ width: 18, height: 18, flexShrink: 0 }} />
                            Divergência financeira detectada
                        </div>
                        <p style={{ margin: 0, color: 'var(--color-text-main)', fontSize: '0.875rem', lineHeight: 1.5 }}>
                            O total da cotação após as suas correções ({formatCurrencyAO(integrityError.quotationTotal, currency)}) difere do total extraído do documento ({formatCurrencyAO(integrityError.ocrOriginalTotal, currency)}) em {formatCurrencyAO(integrityError.varianceAmount, currency)}. Informe o motivo da divergência para continuar.
                        </p>
                        <p style={{ margin: 0, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>
                            Tolerância aplicada: {formatCurrencyAO(integrityError.toleranceApplied, currency)}
                        </p>

                        <div>
                            <label htmlFor="financial-integrity-justification" style={{ display: 'block', fontSize: '0.875rem', fontWeight: 'var(--font-weight-medium, 500)', color: 'var(--color-text-main)', marginBottom: '8px' }}>
                                Justificativa da divergência *
                            </label>
                            <textarea
                                id="financial-integrity-justification"
                                ref={justificationRef}
                                value={overrideJustification}
                                onChange={(e) => setOverrideJustification(e.target.value)}
                                placeholder="Ex.: item duplicado removido pelo OCR; quantidade corrigida conforme o documento original..."
                                rows={3}
                                disabled={isSaving}
                                style={{
                                    width: '100%', fontSize: '0.875rem', padding: '10px',
                                    border: missingJustification ? '1px solid var(--color-status-red)' : '1px solid var(--color-border)',
                                    borderRadius: 'var(--radius-sm, 4px)', fontFamily: 'var(--font-family-body)',
                                    resize: 'vertical', boxSizing: 'border-box', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)'
                                }}
                            />
                            {missingJustification && (
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-status-red)', marginTop: '6px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                    <AlertCircle size={12} /> A justificativa é obrigatória para salvar com divergência financeira.
                                </span>
                            )}
                        </div>

                        <div>
                            <button
                                onClick={handleRetryWithOverride}
                                disabled={isSaving}
                                style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: 'var(--color-status-red)', color: '#fff', fontWeight: 600, cursor: isSaving ? 'not-allowed' : 'pointer', opacity: isSaving ? 0.7 : 1 }}
                            >
                                <Save size={16} />
                                {isSaving ? 'Salvando...' : 'Salvar com Justificativa'}
                            </button>
                        </div>
                    </div>
                );
            })()}

            {/* Signed-residual gate panel — the OCR document's own header/line inconsistency remaining
                after all explained line adjustments. Renamed so it no longer claims to explain the
                whole gross variance; the residual amount is NEVER zeroed by providing a justification. */}
            {residualError && (() => {
                const missingJustification = justificationTouched && !overrideJustification.trim();
                const c = draft?.currency;
                const r = residualError;
                return (
                    <div style={{ backgroundColor: 'var(--color-status-red-surface)', borderTop: '1px solid var(--color-status-red)', padding: '16px 24px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-status-red)', fontWeight: 700, fontSize: '0.9rem' }}>
                            <ShieldAlert style={{ width: 18, height: 18, flexShrink: 0 }} /> Diferença não explicada do documento
                        </div>
                        <p style={{ margin: 0, color: 'var(--color-text-main)', fontSize: '0.875rem', lineHeight: 1.5 }}>
                            Após os ajustes de linha explicados, resta uma diferença não explicada de <strong>{formatCurrencyAO(r.residualVariance, c)}</strong> entre o total do documento OCR ({formatCurrencyAO(r.ocrHeaderTotal, c)}) e o total considerado ({formatCurrencyAO(r.finalConsideredTotal, c)}).
                        </p>
                        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '2px 16px' }}>
                            <span>Diferença estrutural (cabeçalho vs linhas): {formatCurrencyAO(r.structuralHeaderDifference, c)}</span>
                            <span>Diferença de componentes OCR: {formatCurrencyAO(r.ocrLineComponentDifference, c)}</span>
                            <span>Ignorados: {formatCurrencyAO(r.ignoredImpact, c)}</span>
                            <span>Quantidade: {formatCurrencyAO(r.quantityImpact, c)}</span>
                            <span>Preço: {formatCurrencyAO(r.unitPriceImpact, c)}</span>
                            <span>Desconto: {formatCurrencyAO(r.discountImpact, c)}</span>
                            <span>IVA: {formatCurrencyAO(r.ivaImpact, c)}</span>
                            <span>IVA de resumo reconhecido: {formatCurrencyAO(r.documentSummaryIvaCredit, c)}</span>
                            <span>Desconto global: {formatCurrencyAO(r.globalDiscountImpact, c)}</span>
                            <span>Adições manuais: {formatCurrencyAO(r.manualAdditionsImpact, c)}</span>
                            <span>Tolerância: {formatCurrencyAO(r.toleranceApplied, c)}</span>
                        </div>
                        <div>
                            <label htmlFor="residual-justification" style={{ display: 'block', fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', marginBottom: '8px' }}>
                                Justificativa da diferença residual *
                            </label>
                            <textarea
                                id="residual-justification"
                                ref={justificationRef}
                                value={overrideJustification}
                                onChange={(e) => setOverrideJustification(e.target.value)}
                                placeholder="Ex.: frete não itemizado pelo OCR; linha omitida na extração; arredondamento do documento..."
                                rows={3}
                                disabled={isSaving}
                                style={{ width: '100%', fontSize: '0.875rem', padding: '10px', border: missingJustification ? '1px solid var(--color-status-red)' : '1px solid var(--color-border)', borderRadius: 'var(--radius-sm, 4px)', fontFamily: 'var(--font-family-body)', resize: 'vertical', boxSizing: 'border-box', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)' }}
                            />
                            {missingJustification && (
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-status-red)', marginTop: '6px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                    <AlertCircle size={12} /> A justificativa da diferença residual é obrigatória para continuar.
                                </span>
                            )}
                        </div>
                        <div>
                            <button onClick={handleRetryWithOverride} disabled={isSaving}
                                style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: 'var(--color-status-red)', color: '#fff', fontWeight: 600, cursor: isSaving ? 'not-allowed' : 'pointer', opacity: isSaving ? 0.7 : 1 }}>
                                <Save size={16} /> {isSaving ? 'Salvando...' : 'Salvar com Justificativa'}
                            </button>
                        </div>
                    </div>
                );
            })()}

            {/* Inline Error Panel for Save Failures unrelated to Financial Integrity. Rendered
                independently of the panel above: if a retry-with-override fails for a different
                reason, both stay visible — the entered justification must not be discarded. */}
            {saveError && (
                <div style={{ backgroundColor: '#fef2f2', borderTop: '1px solid #fecaca', padding: '16px 24px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#b91c1c', fontWeight: 600 }}>
                        <AlertCircle style={{ width: 18, height: 18 }} />
                        Não foi possível salvar a cotação
                    </div>
                    <p style={{ margin: 0, color: '#991b1b', fontSize: '0.875rem', whiteSpace: 'pre-line' }}>
                        {saveError}
                    </p>
                </div>
            )}

            {/* Footer / Actions */}
            <div style={{ padding: '16px 24px', borderTop: '1px solid var(--color-border)', backgroundColor: '#fff', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <button
                    onClick={handleClose}
                    style={{ background: 'none', border: '1px solid var(--color-border)', padding: '8px 16px', borderRadius: '6px', color: 'var(--color-text-main)', fontWeight: 600, cursor: 'pointer' }}
                >
                    Cancelar
                </button>
                <div style={{ display: 'flex', gap: '12px' }}>
                    <button
                        onClick={handleBack}
                        disabled={currentIndex === 0}
                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', border: '1px solid var(--color-border)', backgroundColor: currentIndex === 0 ? '#f1f5f9' : '#fff', color: currentIndex === 0 ? 'var(--color-text-muted)' : 'var(--color-text-main)', fontWeight: 600, cursor: currentIndex === 0 ? 'not-allowed' : 'pointer' }}
                    >
                        <ChevronLeft size={20} />
                        Anterior
                    </button>
                        {!isLastStep ? (
                            <button
                                onClick={handleNext}
                                disabled={!canGoNext(request, ivaRates, units)}
                                style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: !canGoNext(request, ivaRates, units) ? '#e0e7ff' : 'var(--color-primary)', color: !canGoNext(request, ivaRates, units) ? '#818cf8' : '#fff', fontWeight: 600, cursor: !canGoNext(request, ivaRates, units) ? 'not-allowed' : 'pointer', boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)' }}
                            >
                                Avançar <ChevronRight size={16} />
                            </button>
                        ) : (
                            (() => {
                                const saveEnabled = validation.isValid && wizardState.isFinalReviewConfirmed && !integrityError && !previewGateBlocksSave && !isSaving;
                                const title = integrityError ? 'Resolva a divergência financeira acima antes de tentar salvar novamente.'
                                    : previewLoading ? 'Calculando o resumo de reconciliação...'
                                    : previewError ? 'Recalcule o resumo de reconciliação para continuar.'
                                    : previewStale ? 'O resumo está desatualizado — recalcule antes de salvar.'
                                    : (residualBlocksSave && !residualJustificationValid) ? 'Informe a justificativa da diferença residual para continuar.'
                                    : undefined;
                                return (
                                    <button
                                        onClick={handleSave}
                                        disabled={!saveEnabled}
                                        title={title}
                                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', backgroundColor: saveEnabled ? 'var(--color-primary)' : '#94a3b8', color: '#fff', fontWeight: 600, border: 'none', cursor: saveEnabled ? 'pointer' : 'not-allowed' }}
                                    >
                                        <Save size={20} />
                                        {isSaving ? 'Salvando...' : (residualBlocksSave ? 'Salvar com Justificativa' : 'Salvar Cotação')}
                                    </button>
                                );
                            })()
                        )}
                    </div>
                </div>
            </div>
        </div>
    );

    if (!mounted) return null;
    return createPortal(modalContent, document.body);
};
