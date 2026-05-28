import React, { createContext, useContext, useCallback, useMemo } from 'react';
import { Joyride, STATUS, EVENTS, ACTIONS, type EventData, type Step, type TooltipRenderProps } from 'react-joyride';
import type { LiveGuideId, LiveGuideDefinition, LiveGuideContextValue } from './liveGuideTypes';
import { useLiveGuide } from './useLiveGuide';
import { ChevronLeft, ChevronRight, X, SkipForward, CheckCircle2, AlertCircle } from 'lucide-react';



/**
 * Sticky header height + breathing room.
 * Used as Joyride scrollOffset so spotlighted targets are not hidden behind the topbar.
 * Reads the CSS custom property `--header-height` if available; falls back to 106px (90px + 16px).
 * Same approach as `useGuidedTour.ts`.
 */
const HEADER_SCROLL_OFFSET = (() => {
    try {
        const raw = getComputedStyle(document.documentElement).getPropertyValue('--header-height')?.trim();
        if (raw) {
            const parsed = parseInt(raw, 10);
            if (!Number.isNaN(parsed) && parsed > 0) return parsed + 16;
        }
    } catch { /* SSR / JSDOM */ }
    return 106; // 90px topbar + 16px breathing
})();

/**
 * LiveGuideProvider
 *
 * React context provider that renders Joyride in controlled mode
 * for spotlight/overlay/positioning, with a fully custom tooltip
 * that controls validation, step transitions, and button states.
 *
 * Must be placed inside AuthProvider (for useAuth) and inside AppShell.
 * Should be nested inside GuidedTourProvider so both systems coexist.
 */

const LiveGuideContext = createContext<LiveGuideContextValue | null>(null);

/** Hook for consuming the Live Guide context */
export function useLiveGuideContext(): LiveGuideContextValue {
    const ctx = useContext(LiveGuideContext);
    if (!ctx) throw new Error('useLiveGuideContext must be used within LiveGuideProvider');
    return ctx;
}

// ─── Internal tooltip data context ─────────────────────────────────────
// This context carries the tooltip state (isValidated, currentStep, etc.)
// directly to the tooltip component WITHOUT going through Joyride props.
// This is critical because Joyride internally memoizes the tooltip component
// and does NOT re-render it when only `tooltipComponent` function reference changes.
//
// By using a separate context:
// - StableTooltipWrapper is a module-level stable component (never changes)
// - Joyride sees a stable tooltipComponent reference → no unnecessary unmount/remount
// - When isValidated changes → context value changes → tooltip re-renders directly

interface TooltipDataContextValue {
    currentStep: ReturnType<typeof useLiveGuide>['currentStep'];
    currentStepIndex: number;
    totalSteps: number;
    /** Number of currently visible/applicable steps (excludes conditional steps whose condition is false) */
    visibleTotalSteps: number;
    /** 1-indexed ordinal position among visible steps */
    visibleStepNumber: number;
    isFirstStep: boolean;
    isLastStep: boolean;
    isValidated: boolean;
    targetExists: boolean;
    onNext: () => void;
    onPrev: () => void;
    onSkip: () => void;
    onClose: () => void;
}

const TooltipDataContext = createContext<TooltipDataContextValue | null>(null);

/**
 * Stable tooltip wrapper — defined at module scope so its reference never changes.
 * Reads all dynamic data from TooltipDataContext instead of closure props.
 * This is the component passed to Joyride's `tooltipComponent` prop.
 */
function StableTooltipWrapper(props: TooltipRenderProps) {
    const data = useContext(TooltipDataContext);
    if (!data) return null;
    return (
        <LiveGuideTooltip
            tooltipProps={props}
            currentStep={data.currentStep}
            currentStepIndex={data.currentStepIndex}
            totalSteps={data.totalSteps}
            visibleTotalSteps={data.visibleTotalSteps}
            visibleStepNumber={data.visibleStepNumber}
            isFirstStep={data.isFirstStep}
            isLastStep={data.isLastStep}
            isValidated={data.isValidated}
            targetExists={data.targetExists}
            onNext={data.onNext}
            onPrev={data.onPrev}
            onSkip={data.onSkip}
            onClose={data.onClose}
        />
    );
}

interface LiveGuideProviderProps {
    children: React.ReactNode;
}

export function LiveGuideProvider({ children }: LiveGuideProviderProps) {
    const guide = useLiveGuide();

    /**
     * Registry of guide definition factories keyed by LiveGuideId.
     * Each page component registers its factory via a ref callback
     * before calling startLiveGuide.
     */
    const guideFactoriesRef = React.useRef<Map<LiveGuideId, () => LiveGuideDefinition>>(new Map());

    /** Register a guide factory (called by pages that offer live guides) */
    const registerGuideFactory = useCallback(
        (guideId: LiveGuideId, factory: () => LiveGuideDefinition) => {
            guideFactoriesRef.current.set(guideId, factory);
        },
        []
    );

    /** Unregister a guide factory (cleanup on unmount) */
    const unregisterGuideFactory = useCallback((guideId: LiveGuideId) => {
        guideFactoriesRef.current.delete(guideId);
    }, []);

    /** Start a live guide by ID */
    const startLiveGuide = useCallback(
        (guideId: LiveGuideId) => {
            const factory = guideFactoriesRef.current.get(guideId);
            if (!factory) {
                console.warn(`[LiveGuide] No factory registered for guide "${guideId}"`);
                return;
            }
            const definition = factory();
            guide.startGuide(definition);
        },
        [guide]
    );

    /** Close the active live guide */
    const closeLiveGuide = useCallback(() => {
        guide.closeGuide();
    }, [guide]);

    // Build Joyride steps from the current guide definition
    const joyrideSteps: Step[] = useMemo(() => {
        if (!guide.guideDefinition || !guide.isActive) return [];
        return guide.guideDefinition.steps.map((step) => ({
            target: step.target,
            content: '', // Content is rendered by the custom tooltip
            placement: step.placement ?? 'auto',
            disableBeacon: true,
            spotlightClicks: true, // Allow user to interact with the spotted element
            disableOverlayClose: true,
        }));
    }, [guide.guideDefinition, guide.isActive]);

    // Joyride callback — handles overlay/finish events and manual scroll compensation
    const handleJoyrideCallback = useCallback(
        (data: EventData) => {
            const { status, action, type } = data;
            // If user clicks overlay or Joyride triggers finish, close the guide
            if (status === STATUS.FINISHED || status === STATUS.SKIPPED) {
                guide.closeGuide();
            }
            if (action === ACTIONS.CLOSE && type === EVENTS.STEP_AFTER) {
                guide.closeGuide();
            }


            if (type === EVENTS.STEP_BEFORE && data.step?.target) {
                requestAnimationFrame(() => {
                    setTimeout(() => {
                        const target = data.step?.target;
                        if (!target) return;
                        const el = typeof target === 'string'
                            ? document.querySelector(target) as HTMLElement | null
                            : target as HTMLElement;
                        if (!el) return;

                        const rect = el.getBoundingClientRect();
                        const isBehindHeader = rect.top < HEADER_SCROLL_OFFSET;
                        const isBelowFold = rect.bottom > window.innerHeight;


                        if (isBehindHeader || isBelowFold) {
                            const scrollY = window.scrollY + rect.top - HEADER_SCROLL_OFFSET;
                            window.scrollTo({ top: Math.max(0, scrollY), behavior: 'smooth' });
                        }
                    }, 80);
                });
            }
        },
        [guide]
    );

    // Tooltip data — provided via context to StableTooltipWrapper.
    // When any value here changes, the tooltip re-renders immediately
    // without relying on Joyride to propagate the update.
    const tooltipData = useMemo<TooltipDataContextValue>(() => ({
        currentStep: guide.currentStep,
        currentStepIndex: guide.currentStepIndex,
        totalSteps: guide.totalSteps,
        visibleTotalSteps: guide.visibleSteps,
        visibleStepNumber: guide.visibleStepNumber,
        isFirstStep: guide.isFirstStep,
        isLastStep: guide.isLastStep,
        isValidated: guide.isValidated,
        targetExists: guide.targetExists,
        onNext: guide.nextStep,
        onPrev: guide.prevStep,
        onSkip: guide.skipStep,
        onClose: guide.closeGuide,
    }), [guide.currentStep, guide.currentStepIndex, guide.totalSteps,
         guide.visibleSteps, guide.visibleStepNumber,
         guide.isFirstStep, guide.isLastStep, guide.isValidated,
         guide.targetExists, guide.nextStep, guide.prevStep,
         guide.skipStep, guide.closeGuide]);

    const contextValue = useMemo<LiveGuideContextValue & {
        registerGuideFactory: (id: LiveGuideId, factory: () => LiveGuideDefinition) => void;
        unregisterGuideFactory: (id: LiveGuideId) => void;
    }>(() => ({
        startLiveGuide,
        closeLiveGuide,
        isLiveGuideActive: guide.isActive,
        activeLiveGuideId: guide.guideDefinition?.id ?? null,
        registerGuideFactory,
        unregisterGuideFactory,
    }), [startLiveGuide, closeLiveGuide, guide.isActive, guide.guideDefinition?.id,
         registerGuideFactory, unregisterGuideFactory]);

    return (
        <LiveGuideContext.Provider value={contextValue}>
            {children}

            {/* Joyride in controlled mode — spotlight + overlay + positioning only */}
            {guide.isActive && joyrideSteps.length > 0 && (
                <TooltipDataContext.Provider value={tooltipData}>
                    <Joyride
                        steps={joyrideSteps}
                        stepIndex={guide.currentStepIndex}
                        run={guide.isActive}
                        continuous={false}
                        scrollToFirstStep
                        tooltipComponent={StableTooltipWrapper}
                        onEvent={handleJoyrideCallback}
                        floatingOptions={{
                            shiftOptions: {
                                padding: { top: HEADER_SCROLL_OFFSET, bottom: 16, left: 16, right: 16 },
                            },
                        }}
                        options={{
                            spotlightPadding: 8,
                            spotlightRadius: 12,
                            overlayColor: 'rgba(0, 0, 0, 0.45)',
                            zIndex: 10000,
                            skipBeacon: true,
                            showProgress: false,
                            buttons: [],
                            overlayClickAction: false,
                            scrollOffset: HEADER_SCROLL_OFFSET,
                            scrollDuration: 350,
                            offset: 12,
                        }}
                    />
                </TooltipDataContext.Provider>
            )}
        </LiveGuideContext.Provider>
    );
}

/**
 * Extend the context type to expose registration methods
 * used by page components via the useLiveGuideContext hook.
 */
export function useLiveGuideRegistration() {
    const ctx = useContext(LiveGuideContext) as LiveGuideContextValue & {
        registerGuideFactory?: (id: LiveGuideId, factory: () => LiveGuideDefinition) => void;
        unregisterGuideFactory?: (id: LiveGuideId) => void;
    } | null;
    if (!ctx) throw new Error('useLiveGuideRegistration must be used within LiveGuideProvider');
    return {
        registerGuideFactory: ctx.registerGuideFactory!,
        unregisterGuideFactory: ctx.unregisterGuideFactory!,
    };
}


// ─── Custom Tooltip Component ──────────────────────────────────────────

interface LiveGuideTooltipProps {
    tooltipProps: TooltipRenderProps;
    currentStep: ReturnType<typeof useLiveGuide>['currentStep'];
    currentStepIndex: number;
    totalSteps: number;
    visibleTotalSteps: number;
    visibleStepNumber: number;
    isFirstStep: boolean;
    isLastStep: boolean;
    isValidated: boolean;
    targetExists: boolean;
    onNext: () => void;
    onPrev: () => void;
    onSkip: () => void;
    onClose: () => void;
}

function LiveGuideTooltip({
    tooltipProps,
    currentStep,
    currentStepIndex: _currentStepIndex,
    totalSteps: _totalSteps,
    visibleTotalSteps,
    visibleStepNumber,
    isFirstStep,
    isLastStep,
    isValidated,
    targetExists,
    onNext,
    onPrev,
    onSkip,
    onClose,
}: LiveGuideTooltipProps) {
    if (!currentStep) return null;

    const showSkip = currentStep.allowSkip;
    const showValidationMessage = !isValidated && currentStep.requiredAction !== 'none' && currentStep.validationMessage;
    const canProceed = isValidated || currentStep.requiredAction === 'none' || !currentStep.validate;
    const nextLabel = isLastStep ? 'Concluir' : 'Próximo';

    return (
        <div
            {...tooltipProps.tooltipProps}
            style={{
                backgroundColor: 'white',
                borderRadius: '12px',
                boxShadow: '0 20px 60px rgba(0, 0, 0, 0.2), 0 4px 16px rgba(0, 0, 0, 0.1)',
                maxWidth: '380px',
                minWidth: '300px',
                fontFamily: 'var(--font-family-body, Inter, system-ui, sans-serif)',
                overflow: 'hidden',
                border: '1px solid rgba(0, 0, 0, 0.08)',
            }}
        >
            {/* Header */}
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: '14px 16px 10px',
                    borderBottom: '1px solid #f0f0f0',
                    background: 'linear-gradient(135deg, rgba(var(--color-primary-rgb, 0, 112, 192), 0.06) 0%, rgba(var(--color-primary-rgb, 0, 112, 192), 0.02) 100%)',
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <div
                        style={{
                            width: '24px',
                            height: '24px',
                            borderRadius: '6px',
                            backgroundColor: 'var(--color-primary, #0070C0)',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            color: 'white',
                            fontSize: '0.7rem',
                            fontWeight: 900,
                            flexShrink: 0,
                        }}
                    >
                        {visibleStepNumber}
                    </div>
                    <div
                        style={{
                            fontSize: '0.85rem',
                            fontWeight: 800,
                            color: 'var(--color-text-main, #111827)',
                            fontFamily: 'var(--font-family-display, Inter, system-ui, sans-serif)',
                        }}
                    >
                        {currentStep.title}
                    </div>
                </div>
                <button
                    onClick={onClose}
                    title="Fechar guia"
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        width: '28px',
                        height: '28px',
                        border: 'none',
                        background: 'none',
                        cursor: 'pointer',
                        color: 'var(--color-text-muted, #6b7280)',
                        borderRadius: '6px',
                        transition: 'all 0.15s',
                        flexShrink: 0,
                    }}
                    onMouseEnter={(e) => {
                        e.currentTarget.style.backgroundColor = '#f3f4f6';
                        e.currentTarget.style.color = '#ef4444';
                    }}
                    onMouseLeave={(e) => {
                        e.currentTarget.style.backgroundColor = 'transparent';
                        e.currentTarget.style.color = 'var(--color-text-muted, #6b7280)';
                    }}
                >
                    <X size={16} strokeWidth={2.5} />
                </button>
            </div>

            {/* Body */}
            <div style={{ padding: '14px 16px' }}>
                <div
                    style={{
                        margin: 0,
                        fontSize: '0.82rem',
                        color: 'var(--color-text-main, #374151)',
                        lineHeight: 1.55,
                        fontWeight: 500,
                    }}
                >
                    {!targetExists && currentStep.fallbackContent
                        ? currentStep.fallbackContent
                        : typeof currentStep.content === 'string'
                          ? currentStep.content.split('\n').map((line, i, arr) => (
                                <span key={i}>
                                    {line}
                                    {i < arr.length - 1 && <br />}
                                </span>
                            ))
                          : currentStep.content}
                </div>

                {/* Validation message */}
                {showValidationMessage && (
                    <div
                        style={{
                            marginTop: '10px',
                            padding: '8px 12px',
                            backgroundColor: '#FEF2F2',
                            border: '1px solid #FECACA',
                            borderRadius: '6px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                        }}
                    >
                        <AlertCircle size={14} style={{ color: '#EF4444', flexShrink: 0 }} />
                        <span
                            style={{
                                fontSize: '0.75rem',
                                color: '#DC2626',
                                fontWeight: 600,
                            }}
                        >
                            {currentStep.validationMessage}
                        </span>
                    </div>
                )}

                {/* Validated indicator */}
                {isValidated && currentStep.requiredAction !== 'none' && currentStep.validate && (
                    <div
                        style={{
                            marginTop: '10px',
                            padding: '8px 12px',
                            backgroundColor: '#F0FDF4',
                            border: '1px solid #BBF7D0',
                            borderRadius: '6px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                        }}
                    >
                        <CheckCircle2 size={14} style={{ color: '#16A34A', flexShrink: 0 }} />
                        <span
                            style={{
                                fontSize: '0.75rem',
                                color: '#166534',
                                fontWeight: 600,
                            }}
                        >
                            Campo preenchido — pode avançar.
                        </span>
                    </div>
                )}
            </div>

            {/* Footer */}
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: '10px 16px 14px',
                    borderTop: '1px solid #f0f0f0',
                }}
            >
                {/* Step counter */}
                <span
                    style={{
                        fontSize: '0.7rem',
                        fontWeight: 700,
                        color: 'var(--color-text-muted, #9ca3af)',
                        textTransform: 'uppercase',
                        letterSpacing: '0.04em',
                    }}
                >
                    Passo {visibleStepNumber} de {visibleTotalSteps}
                </span>

                {/* Action buttons */}
                <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                    {/* Back button */}
                    {!isFirstStep && (
                        <button
                            onClick={onPrev}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '4px',
                                padding: '6px 12px',
                                fontSize: '0.75rem',
                                fontWeight: 700,
                                color: 'var(--color-text-main, #374151)',
                                backgroundColor: 'transparent',
                                border: '1px solid var(--color-border, #e5e7eb)',
                                borderRadius: '6px',
                                cursor: 'pointer',
                                transition: 'all 0.15s',
                            }}
                            onMouseEnter={(e) => {
                                e.currentTarget.style.backgroundColor = '#f9fafb';
                            }}
                            onMouseLeave={(e) => {
                                e.currentTarget.style.backgroundColor = 'transparent';
                            }}
                        >
                            <ChevronLeft size={14} />
                            Anterior
                        </button>
                    )}

                    {/* Skip button */}
                    {showSkip && (
                        <button
                            onClick={onSkip}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '4px',
                                padding: '6px 12px',
                                fontSize: '0.75rem',
                                fontWeight: 700,
                                color: 'var(--color-text-muted, #6b7280)',
                                backgroundColor: 'transparent',
                                border: '1px solid var(--color-border, #e5e7eb)',
                                borderRadius: '6px',
                                cursor: 'pointer',
                                transition: 'all 0.15s',
                            }}
                            onMouseEnter={(e) => {
                                e.currentTarget.style.backgroundColor = '#f9fafb';
                            }}
                            onMouseLeave={(e) => {
                                e.currentTarget.style.backgroundColor = 'transparent';
                            }}
                        >
                            <SkipForward size={12} />
                            Pular
                        </button>
                    )}

                    {/* Next / Complete button */}
                    <button
                        onClick={canProceed ? onNext : undefined}
                        disabled={!canProceed}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '4px',
                            padding: '6px 14px',
                            fontSize: '0.75rem',
                            fontWeight: 800,
                            color: 'white',
                            backgroundColor: canProceed
                                ? 'var(--color-primary, #0070C0)'
                                : '#d1d5db',
                            border: 'none',
                            borderRadius: '6px',
                            cursor: canProceed ? 'pointer' : 'not-allowed',
                            transition: 'all 0.15s',
                            opacity: canProceed ? 1 : 0.7,
                        }}
                        onMouseEnter={(e) => {
                            if (canProceed) {
                                e.currentTarget.style.filter = 'brightness(1.1)';
                            }
                        }}
                        onMouseLeave={(e) => {
                            e.currentTarget.style.filter = 'none';
                        }}
                    >
                        {isLastStep ? (
                            <>
                                <CheckCircle2 size={14} />
                                {nextLabel}
                            </>
                        ) : (
                            <>
                                {nextLabel}
                                <ChevronRight size={14} />
                            </>
                        )}
                    </button>
                </div>
            </div>
        </div>
    );
}
