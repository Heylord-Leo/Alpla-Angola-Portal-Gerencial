import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { X, ChevronRight, ChevronLeft, Save, AlertCircle, ShieldAlert } from 'lucide-react';
import { RequestDetailsDto, FinancialIntegrityCheckFailedDto } from '../../../types';
import { useQuotationWizardState, QuotationWizardStep } from './hooks/useQuotationWizardState';
import { useQuotationValidation } from './hooks/useQuotationValidation';
import { formatCurrencyAO } from '../../../lib/utils';
import { WizardStepRequestOverview } from './WizardStepRequestOverview';
import { WizardStepDocumentsOcr } from './WizardStepDocumentsOcr';
import { WizardStepReconciliation } from './WizardStepReconciliation';
import { WizardStepFinalReview } from './WizardStepFinalReview';
import { WizardStepSupplierValidation } from './WizardStepSupplierValidation';

type QuotationSaveResult =
    | { success: true }
    | ({ success: false } & FinancialIntegrityCheckFailedDto)
    | { success: false; error: string };

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
    const [overrideJustification, setOverrideJustification] = useState('');
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
        setOverrideJustification('');
        setJustificationTouched(false);
        isSavingRef.current = false;
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
        setOverrideJustification('');
        setJustificationTouched(false);
    }, [draft]);

    // Accessibility: move focus into the justification field as soon as the panel appears.
    useEffect(() => {
        if (integrityError) {
            justificationRef.current?.focus();
        }
    }, [integrityError]);

    if (!isOpen) return null;

    const currentIndex = STEPS.findIndex(s => s.key === currentStep);
    const totalSteps = STEPS.length;
    const isLastStep = currentIndex === totalSteps - 1;

    const handleNext = () => {
        const isNextAllowed = canGoNext(request);
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

        const validation = validateDraft(draft);
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
                closeWizard();
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

    const handleSave = () => performSave();

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

    const validation = validateDraft(draft);

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
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#F3F4F6',
                    borderRadius: '12px',
                    width: '100%',
                    maxWidth: '1200px',
                    maxHeight: '90vh',
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
                                disabled={!canGoNext(request)}
                                style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: !canGoNext(request) ? '#e0e7ff' : 'var(--color-primary)', color: !canGoNext(request) ? '#818cf8' : '#fff', fontWeight: 600, cursor: !canGoNext(request) ? 'not-allowed' : 'pointer', boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)' }}
                            >
                                Avançar <ChevronRight size={16} />
                            </button>
                        ) : (
                            <button
                                onClick={handleSave}
                                disabled={!validation.isValid || isSaving || !wizardState.isFinalReviewConfirmed || !!integrityError}
                                title={integrityError ? 'Resolva a divergência financeira acima antes de tentar salvar novamente.' : undefined}
                                style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', backgroundColor: (validation.isValid && wizardState.isFinalReviewConfirmed && !integrityError) ? 'var(--color-primary)' : '#94a3b8', color: '#fff', fontWeight: 600, border: 'none', cursor: (validation.isValid && wizardState.isFinalReviewConfirmed && !isSaving && !integrityError) ? 'pointer' : 'not-allowed' }}
                            >
                                <Save size={20} />
                                {isSaving ? 'Salvando...' : 'Salvar Cotação'}
                            </button>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );

    if (!mounted) return null;
    return createPortal(modalContent, document.body);
};
