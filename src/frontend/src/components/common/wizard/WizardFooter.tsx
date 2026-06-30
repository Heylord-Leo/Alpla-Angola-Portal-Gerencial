import React from 'react';
import { ChevronLeft, ChevronRight, Loader2 } from 'lucide-react';

export interface WizardFooterProps {
    currentStep: number;
    totalSteps: number;
    onBack: () => void;
    onNext: () => void;
    isFirstStep: boolean;
    isLastStep: boolean;
    isSubmitting?: boolean;
    submitLabel?: string;
    nextLabel?: string;
    backLabel?: string;
    canProceed?: boolean;
    className?: string;
    style?: React.CSSProperties;
}

export function WizardFooter({
    currentStep,
    totalSteps,
    onBack,
    onNext,
    isFirstStep,
    isLastStep,
    isSubmitting = false,
    submitLabel = 'Criar equipamento',
    nextLabel = 'Próximo passo',
    backLabel = 'Voltar',
    canProceed = true,
    className,
    style
}: WizardFooterProps) {
    return (
        <div
            className={className}
            style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '16px 24px',
                borderTop: '1px solid var(--color-border)',
                backgroundColor: 'var(--color-bg-surface)',
                ...style
            }}
        >
            {/* Back button */}
            <div>
                {!isFirstStep && (
                    <button
                        type="button"
                        onClick={onBack}
                        disabled={isSubmitting}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                            padding: '10px 18px',
                            border: '1px solid var(--color-border)',
                            borderRadius: '8px',
                            backgroundColor: 'var(--color-bg-surface)',
                            color: 'var(--color-text-muted)',
                            fontSize: '0.85rem',
                            fontWeight: 500,
                            cursor: isSubmitting ? 'not-allowed' : 'pointer',
                            opacity: isSubmitting ? 0.5 : 1,
                            transition: 'all 0.15s ease',
                        }}
                    >
                        <ChevronLeft size={16} />
                        {backLabel}
                    </button>
                )}
            </div>

            {/* Step indicator + Next/Submit */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                <span style={{
                    fontSize: '0.8rem',
                    color: 'var(--color-text-muted)',
                    fontWeight: 500,
                }}>
                    Passo {currentStep + 1} de {totalSteps}
                </span>

                <button
                    type="button"
                    onClick={onNext}
                    disabled={isSubmitting || !canProceed}
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px',
                        padding: '10px 22px',
                        border: 'none',
                        borderRadius: '8px',
                        background: isLastStep
                            ? 'var(--color-status-green)'
                            : 'var(--color-primary)',
                        color: '#ffffff',
                        fontSize: '0.85rem',
                        fontWeight: 600,
                        cursor: (isSubmitting || !canProceed) ? 'not-allowed' : 'pointer',
                        opacity: (isSubmitting || !canProceed) ? 0.6 : 1,
                        transition: 'all 0.15s ease',
                        boxShadow: isLastStep
                            ? '0 2px 8px rgba(22,163,74,0.25)'
                            : 'var(--shadow-sm)',
                    }}
                >
                    {isSubmitting ? (
                        <>
                            <Loader2 size={16} style={{ animation: 'spin 1s linear infinite' }} />
                            A processar...
                        </>
                    ) : (
                        <>
                            {isLastStep ? submitLabel : nextLabel}
                            {!isLastStep && <ChevronRight size={16} />}
                        </>
                    )}
                </button>
            </div>
        </div>
    );
}
