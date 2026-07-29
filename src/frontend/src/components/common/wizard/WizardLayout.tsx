import React from 'react';
import { Breadcrumb, BreadcrumbItem } from '../ui/Breadcrumb';
import { WizardStepIndicator, WizardStep } from './WizardStepIndicator';
import { WizardFooter } from './WizardFooter';

export interface WizardLayoutProps {
    /** Breadcrumb trail for the page */
    breadcrumbs: BreadcrumbItem[];
    /** Page title displayed above the wizard */
    title: string;
    /** Optional subtitle/description */
    subtitle?: React.ReactNode;
    /** Icon to display next to the title */
    titleIcon?: React.ReactNode;
    /** Wizard step definitions */
    steps: WizardStep[];
    /** Currently active step index (0-based) */
    currentStep: number;
    /** Set of completed step indices */
    completedSteps?: Set<number>;
    /** Step content to render */
    children: React.ReactNode;
    /** Footer props */
    onBack: () => void;
    onNext: () => void;
    isSubmitting?: boolean;
    submitLabel?: string;
    canProceed?: boolean;
}

export function WizardLayout({
    breadcrumbs,
    title,
    subtitle,
    titleIcon,
    steps,
    currentStep,
    completedSteps = new Set(),
    children,
    onBack,
    onNext,
    isSubmitting = false,
    submitLabel,
    canProceed = true,
}: WizardLayoutProps) {
    const isFirstStep = currentStep === 0;
    const isLastStep = currentStep === steps.length - 1;

    return (
        <div style={{
            display: 'flex',
            flexDirection: 'column',
            minHeight: 'calc(100vh - 64px)', // Account for top navbar
            maxWidth: '1400px',
            margin: '0 auto',
            padding: '0 32px',
        }}>
            {/* Breadcrumb + Page Title */}
            <div style={{ paddingTop: '16px', marginBottom: '20px' }}>
                <Breadcrumb items={breadcrumbs} />
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {titleIcon && (
                        <div style={{ color: 'var(--color-primary)' }}>
                            {titleIcon}
                        </div>
                    )}
                    <div>
                        <h1 style={{
                            fontSize: '1.5rem',
                            fontWeight: 700,
                            color: 'var(--color-text)',
                            margin: 0,
                        }}>
                            {title}
                        </h1>
                        {subtitle && (
                            <p style={{
                                color: 'var(--color-text-muted)',
                                fontSize: '0.875rem',
                                marginTop: '4px',
                                margin: 0,
                            }}>
                                {subtitle}
                            </p>
                        )}
                    </div>
                </div>
            </div>

            {/* Main content: Sidebar + Step Content */}
            <div style={{
                display: 'flex',
                gap: '24px',
                flex: 1,
                minHeight: 0,
            }}>
                {/* Step Sidebar */}
                <div style={{
                    width: 'clamp(180px, 15vw, 240px)',
                    flexShrink: 0,
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '12px',
                    padding: '16px 8px',
                    alignSelf: 'flex-start',
                    position: 'sticky',
                    top: '80px',
                }}>
                    <WizardStepIndicator
                        steps={steps}
                        currentStep={currentStep}
                        completedSteps={completedSteps}
                    />
                </div>

                {/* Step Content */}
                <div style={{
                    flex: 1,
                    minWidth: 0,
                    display: 'flex',
                    flexDirection: 'column',
                    minHeight: '500px',
                }}>
                    <div style={{
                        flex: 1,
                        paddingBottom: '24px',
                    }}>
                        {children}
                    </div>
                </div>
            </div>

            {/* Footer */}
            <div style={{
                position: 'sticky',
                bottom: 0,
                marginLeft: '-32px',
                marginRight: '-32px',
                marginTop: 'auto',
                zIndex: 10,
            }}>
                <WizardFooter
                    currentStep={currentStep}
                    totalSteps={steps.length}
                    onBack={onBack}
                    onNext={onNext}
                    isFirstStep={isFirstStep}
                    isLastStep={isLastStep}
                    isSubmitting={isSubmitting}
                    submitLabel={submitLabel}
                    canProceed={canProceed}
                />
            </div>
        </div>
    );
}
