import { useState, useCallback, useEffect, useRef } from 'react';
import type { LiveGuideDefinition, LiveGuideStep } from './liveGuideTypes';
import { setLiveGuideState } from './liveGuideStorage';
import { useAuth } from '../../auth/AuthContext';

/**
 * Pure helper: check whether a next valid step exists from a given index.
 * Used in the hook body (outside callbacks) to compute `isLastStep`.
 */
function findNextValidStepPure(def: LiveGuideDefinition, fromIndex: number, direction: 1 | -1): number {
    let idx = fromIndex + direction;
    while (idx >= 0 && idx < def.steps.length) {
        const step = def.steps[idx];
        if (!step.condition || step.condition()) return idx;
        idx += direction;
    }
    return -1;
}

/**
 * useLiveGuide
 *
 * Core hook managing the Live Guide lifecycle:
 * - Start/stop a guide
 * - Track current step index
 * - Validate before progressing
 * - Auto-skip steps whose condition() returns false
 * - Handle target-not-found gracefully
 * - Re-evaluate validation state periodically
 */
export function useLiveGuide() {
    const { user } = useAuth();
    const userId = user?.id;

    const [isActive, setIsActive] = useState(false);
    const [currentStepIndex, setCurrentStepIndex] = useState(0);
    const [guideDefinition, setGuideDefinition] = useState<LiveGuideDefinition | null>(null);
    const [isValidated, setIsValidated] = useState(false);
    const [targetExists, setTargetExists] = useState(true);

    const guideDefRef = useRef<LiveGuideDefinition | null>(null);
    const stepIndexRef = useRef(0);

    // Keep refs in sync for use in interval callbacks (avoids stale closures)
    useEffect(() => {
        guideDefRef.current = guideDefinition;
    }, [guideDefinition]);

    useEffect(() => {
        stepIndexRef.current = currentStepIndex;
    }, [currentStepIndex]);

    const currentStep: LiveGuideStep | null =
        guideDefinition && isActive
            ? guideDefinition.steps[currentStepIndex] ?? null
            : null;

    const totalSteps = guideDefinition?.steps.length ?? 0;

    /**
     * Count only visible/applicable steps (those whose condition() returns true or have no condition).
     * This ensures the step counter in the tooltip is accurate even when conditional steps are hidden.
     */
    const visibleSteps = guideDefinition
        ? guideDefinition.steps.filter(s => !s.condition || s.condition()).length
        : 0;

    /**
     * Ordinal position of the current step among visible steps (1-indexed).
     * Used for display: "Passo 3 de 8" instead of raw array index.
     */
    const visibleStepNumber = guideDefinition
        ? guideDefinition.steps
              .slice(0, currentStepIndex + 1)
              .filter(s => !s.condition || s.condition()).length
        : 0;

    const isLastStep = currentStepIndex === totalSteps - 1
        || (guideDefinition ? findNextValidStepPure(guideDefinition, currentStepIndex, 1) === -1 : false);
    const isFirstStep = currentStepIndex === 0;

    /**
     * Find the next valid step index, skipping steps whose condition() returns false.
     * Returns -1 if no valid step exists beyond the current index.
     */
    const findNextValidStep = useCallback((fromIndex: number, direction: 1 | -1 = 1): number => {
        const def = guideDefRef.current;
        if (!def) return -1;

        let idx = fromIndex + direction;
        while (idx >= 0 && idx < def.steps.length) {
            const step = def.steps[idx];
            if (!step.condition || step.condition()) {
                return idx;
            }
            idx += direction;
        }
        return -1;
    }, []);

    /**
     * Periodic validation + target existence polling (300ms).
     *
     * Reads the current step directly from refs (guideDefRef + stepIndexRef)
     * so the interval callback is never stale — it always evaluates the
     * latest step's validate() function, which in turn reads the latest
     * form values via the factory-provided getter.
     */
    useEffect(() => {
        if (!isActive || !currentStep) return;

        const tick = () => {
            const def = guideDefRef.current;
            const idx = stepIndexRef.current;
            const step = def?.steps[idx];

            // --- Validation ---
            if (!step || step.requiredAction === 'none' || !step.validate) {
                setIsValidated(true);
            } else {
                try {
                    const result = step.validate();
                    setIsValidated(result);
                } catch {
                    setIsValidated(false);
                }
            }

            // --- Target existence ---
            if (step) {
                try {
                    setTargetExists(!!document.querySelector(step.target));
                } catch {
                    setTargetExists(false);
                }
            } else {
                setTargetExists(true);
            }
        };

        // Immediate first evaluation
        tick();

        const interval = window.setInterval(tick, 300);
        return () => window.clearInterval(interval);
    }, [isActive, currentStepIndex]); // eslint-disable-line react-hooks/exhaustive-deps

    // Call beforeStep hook when the step changes
    useEffect(() => {
        if (!isActive || !currentStep) return;
        if (currentStep.beforeStep) {
            try {
                currentStep.beforeStep();
            } catch (err) {
                console.warn('[LiveGuide] beforeStep hook error:', err);
            }
        }
    }, [isActive, currentStepIndex]); // eslint-disable-line react-hooks/exhaustive-deps

    /**
     * Start a live guide from its full definition.
     */
    const startGuide = useCallback((definition: LiveGuideDefinition) => {
        if (!definition.enabled || definition.steps.length === 0) {
            console.warn(`[LiveGuide] Guide "${definition.id}" is disabled or has no steps`);
            return;
        }

        guideDefRef.current = definition;
        setGuideDefinition(definition);

        // Find the first valid step (skip steps whose condition returns false)
        let startIdx = 0;
        const firstStep = definition.steps[0];
        if (firstStep.condition && !firstStep.condition()) {
            const nextValid = findNextValidStep(-1, 1);
            if (nextValid === -1) {
                console.warn(`[LiveGuide] No valid steps in guide "${definition.id}"`);
                return;
            }
            startIdx = nextValid;
        }

        setCurrentStepIndex(startIdx);
        setIsActive(true);
        setIsValidated(false);
        console.info(`[LiveGuide] Started guide "${definition.id}" at step ${startIdx}`);
    }, [findNextValidStep]);

    /**
     * Advance to the next step. Validates the current step first.
     *
     * If the next valid step has a DOM target that isn't available yet
     * (e.g., due to AnimatePresence animation delay after a form change),
     * the function retries for up to 500ms before committing.
     */
    const nextStep = useCallback(() => {
        if (!guideDefRef.current || !isActive) return;

        const step = guideDefRef.current.steps[currentStepIndex];
        if (!step) return;

        // Check validation for required steps
        if (step.requiredAction !== 'none' && step.validate && !step.allowSkip) {
            if (!step.validate()) {
                setIsValidated(false);
                return; // Block progression
            }
        }

        // Call afterStep hook
        if (step.afterStep) {
            try {
                step.afterStep();
            } catch (err) {
                console.warn('[LiveGuide] afterStep hook error:', err);
            }
        }

        // Find next valid step
        const nextIdx = findNextValidStep(currentStepIndex, 1);
        if (nextIdx === -1) {
            completeGuide();
            return;
        }

        const nextStepDef = guideDefRef.current.steps[nextIdx];

        // If the next step has a conditional target, wait for it to appear in DOM.
        // This handles AnimatePresence / framer-motion animation delays where React
        // conditionally renders a section that may not be in the DOM instantly.
        if (nextStepDef?.condition && nextStepDef.target) {
            const targetSelector = nextStepDef.target;
            const targetEl = document.querySelector(targetSelector);

            if (!targetEl) {
                // Target not found yet — retry with exponential backoff
                let attempts = 0;
                const maxAttempts = 6; // ~50+100+100+100+100+100 = 550ms max
                const delays = [50, 100, 100, 100, 100, 100];

                const retryFindTarget = () => {
                    attempts++;
                    const el = document.querySelector(targetSelector);
                    if (el) {
                        // Target found — commit step change
                        setCurrentStepIndex(nextIdx);
                        setIsValidated(false);
                        return;
                    }
                    if (attempts < maxAttempts) {
                        setTimeout(retryFindTarget, delays[attempts]);
                    } else {
                        // Target never appeared — skip to the next non-conditional step
                        console.warn(
                            `[LiveGuide] Target "${targetSelector}" not found after ${maxAttempts} retries, skipping step "${nextStepDef.id}"`
                        );
                        const fallbackIdx = findNextValidStep(nextIdx, 1);
                        if (fallbackIdx === -1) {
                            completeGuide();
                        } else {
                            setCurrentStepIndex(fallbackIdx);
                            setIsValidated(false);
                        }
                    }
                };

                setTimeout(retryFindTarget, delays[0]);
                return; // Don't commit yet — waiting for target
            }
        }

        // Target exists (or step has no condition) — commit immediately
        setCurrentStepIndex(nextIdx);
        setIsValidated(false);
    }, [isActive, currentStepIndex, findNextValidStep]); // eslint-disable-line react-hooks/exhaustive-deps

    /**
     * Go back to the previous valid step.
     */
    const prevStep = useCallback(() => {
        if (!guideDefRef.current || !isActive || currentStepIndex <= 0) return;

        const step = guideDefRef.current.steps[currentStepIndex];
        if (step?.afterStep) {
            try { step.afterStep(); } catch { /* ignore */ }
        }

        const prevIdx = findNextValidStep(currentStepIndex, -1);
        if (prevIdx === -1) return; // Already at the first valid step

        setCurrentStepIndex(prevIdx);
        setIsValidated(false);
    }, [isActive, currentStepIndex, findNextValidStep]);

    /**
     * Skip the current step (only allowed if allowSkip is true).
     */
    const skipStep = useCallback(() => {
        if (!guideDefRef.current || !isActive) return;

        const step = guideDefRef.current.steps[currentStepIndex];
        if (!step?.allowSkip) return;

        if (step.afterStep) {
            try { step.afterStep(); } catch { /* ignore */ }
        }

        const nextIdx = findNextValidStep(currentStepIndex, 1);
        if (nextIdx === -1) {
            completeGuide();
            return;
        }

        setCurrentStepIndex(nextIdx);
        setIsValidated(false);
    }, [isActive, currentStepIndex, findNextValidStep]); // eslint-disable-line react-hooks/exhaustive-deps

    /**
     * Close the guide without completing (user cancelled).
     */
    const closeGuide = useCallback(() => {
        if (userId && guideDefRef.current) {
            setLiveGuideState(guideDefRef.current.id, userId, 'cancelled');
        }
        setIsActive(false);
        setGuideDefinition(null);
        setCurrentStepIndex(0);
        setIsValidated(false);
        console.info('[LiveGuide] Guide cancelled by user');
    }, [userId]);

    /**
     * Internal: mark the guide as completed.
     */
    const completeGuide = useCallback(() => {
        if (userId && guideDefRef.current) {
            setLiveGuideState(guideDefRef.current.id, userId, 'completed');
        }
        setIsActive(false);
        setGuideDefinition(null);
        setCurrentStepIndex(0);
        setIsValidated(false);
        console.info('[LiveGuide] Guide completed');
    }, [userId]);

    return {
        isActive,
        currentStep,
        currentStepIndex,
        totalSteps,
        visibleSteps,
        visibleStepNumber,
        isFirstStep,
        isLastStep,
        isValidated,
        targetExists,
        guideDefinition,
        startGuide,
        nextStep,
        prevStep,
        skipStep,
        closeGuide,
    };
}
