import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { X, ChevronRight, ChevronLeft, Save, AlertCircle } from 'lucide-react';
import { RequestDetailsDto } from '../../../types';
import { useQuotationWizardState, QuotationWizardStep } from './hooks/useQuotationWizardState';
import { useQuotationValidation } from './hooks/useQuotationValidation';
import { WizardStepRequestOverview } from './WizardStepRequestOverview';
import { WizardStepDocumentsOcr } from './WizardStepDocumentsOcr';
import { WizardStepReconciliation } from './WizardStepReconciliation';
import { WizardStepFinalReview } from './WizardStepFinalReview';
import { WizardStepSupplierValidation } from './WizardStepSupplierValidation';

interface QuotationWizardModalProps {
    request: RequestDetailsDto | null;
    wizardState: ReturnType<typeof useQuotationWizardState>;
    onSaveQuotation: (draft: any) => Promise<{ success: boolean; error?: string }>;
    isProcessingOcr: boolean;
    onUploadFile: (file: File) => void;
    onCancelWizard: () => Promise<void>;
    onReplaceDocument: (attachmentId: string) => Promise<boolean>;
    ivaRates: any[];
    units: any[];
    currencies: any[];
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
    currencies
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

    const [mounted, setMounted] = useState(false);
    useEffect(() => {
        setMounted(true);
    }, []);

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
        if (currentIndex > 0) {
            goToStep(STEPS[currentIndex - 1].key);
        }
    };

    const handleSave = async () => {
        if (canSubmit) {
            const validation = validateDraft(draft);
            if (!validation.isValid) {
                setSaveError(validation.errors.join('\n'));
                return;
            }

            setIsSaving(true);
            setSaveError(null);
            try {
                const response = await onSaveQuotation(draft);
                if (response.success) {
                    closeWizard();
                } else {
                    setSaveError(response.error || 'Não foi possível salvar a cotação. Verifique os dados e tente novamente.');
                }
            } finally {
                setIsSaving(false);
            }
        }
    };

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files.length > 0) {
            onUploadFile(e.target.files[0]);
        }
    };

    const handleReplaceConfirm = async () => {
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
                    />
                )}
                        {currentStep === 'RECONCILIATION' && (
                            <WizardStepReconciliation draft={draft} request={request} wizardState={wizardState} ivaRates={ivaRates} />
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

            {/* Inline Error Panel for Save Failures */}
            {saveError && (
                <div style={{ backgroundColor: '#fef2f2', borderTop: '1px solid #fecaca', padding: '16px 24px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#b91c1c', fontWeight: 600 }}>
                        <AlertCircle style={{ width: 18, height: 18 }} />
                        Falha ao salvar cotação
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
                                disabled={!validation.isValid || isSaving || !wizardState.isFinalReviewConfirmed}
                                style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', borderRadius: '6px', backgroundColor: (validation.isValid && wizardState.isFinalReviewConfirmed) ? 'var(--color-primary)' : '#94a3b8', color: '#fff', fontWeight: 600, border: 'none', cursor: (validation.isValid && wizardState.isFinalReviewConfirmed && !isSaving) ? 'pointer' : 'not-allowed' }}
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
