import React from 'react';
import { Check } from 'lucide-react';

export interface WizardStep {
    key: string;
    label: string;
    description?: string;
}

export interface WizardStepIndicatorProps {
    steps: WizardStep[];
    currentStep: number; // 0-indexed
    completedSteps?: Set<number>;
    className?: string;
    style?: React.CSSProperties;
}

export function WizardStepIndicator({
    steps,
    currentStep,
    completedSteps = new Set(),
    className,
    style
}: WizardStepIndicatorProps) {
    return (
        <nav className={className} style={{ display: 'flex', flexDirection: 'column', gap: '4px', ...style }}>
            {steps.map((step, index) => {
                const isActive = index === currentStep;
                const isCompleted = completedSteps.has(index);
                const isUpcoming = index > currentStep && !isCompleted;

                return (
                    <div
                        key={step.key}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '12px',
                            padding: '10px 14px',
                            borderRadius: '8px',
                            backgroundColor: isActive ? 'rgba(var(--color-primary-rgb),0.08)' : 'transparent',
                            transition: 'background-color 0.2s ease',
                            cursor: 'default',
                        }}
                    >
                        {/* Step number / icon */}
                        <div style={{
                            width: '28px',
                            height: '28px',
                            borderRadius: '50%',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            fontSize: '0.75rem',
                            fontWeight: 700,
                            flexShrink: 0,
                            transition: 'all 0.2s ease',
                            ...(isCompleted ? {
                                backgroundColor: 'var(--color-status-green)',
                                color: '#ffffff',
                                border: '2px solid var(--color-status-green)',
                            } : isActive ? {
                                backgroundColor: 'var(--color-primary)',
                                color: '#ffffff',
                                border: '2px solid var(--color-primary)',
                                boxShadow: '0 0 0 4px rgba(var(--color-primary-rgb),0.15)',
                            } : {
                                backgroundColor: 'var(--color-bg-surface)',
                                color: 'var(--color-text-muted)',
                                border: '2px solid var(--color-border)',
                            })
                        }}>
                            {isCompleted ? <Check size={14} strokeWidth={3} /> : index + 1}
                        </div>

                        {/* Step label */}
                        <div style={{ flex: 1, minWidth: 0 }}>
                            <div style={{
                                fontSize: '0.82rem',
                                fontWeight: isActive ? 600 : 500,
                                color: isUpcoming ? 'var(--color-text-muted)' : 'var(--color-text-main)',
                                whiteSpace: 'nowrap',
                                overflow: 'hidden',
                                textOverflow: 'ellipsis',
                            }}>
                                {step.label}
                            </div>
                            {step.description && (
                                <div style={{
                                    fontSize: '0.72rem',
                                    color: 'var(--color-text-muted)',
                                    marginTop: '1px',
                                    whiteSpace: 'nowrap',
                                    overflow: 'hidden',
                                    textOverflow: 'ellipsis',
                                }}>
                                    {step.description}
                                </div>
                            )}
                        </div>
                    </div>
                );
            })}
        </nav>
    );
}
