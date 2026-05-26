import { useState, useCallback, useEffect, useRef } from 'react';
import { useLocation } from 'react-router-dom';
import type { EventData, Status } from 'react-joyride';
import { EVENTS, STATUS, ACTIONS } from 'react-joyride';
import { useAuth } from '../auth/AuthContext';
import { filterActiveSteps } from './tours/portalMainTour';
import { getTourState, setTourState, resetTourState } from './guidedTourStorage';
import { getTourById, getToursForRoute } from './guidedTourRegistry';
import type { TourId, TourStep, TourDefinition } from './guidedTourTypes';

/**
 * Sticky header height + breathing room.
 * Used to compute scrollOffset so tour targets don't get hidden behind the topbar.
 * Reads the CSS custom property if available; falls back to 80px.
 */
const HEADER_OFFSET_PX = (() => {
    try {
        const raw = getComputedStyle(document.documentElement).getPropertyValue('--header-height')?.trim();
        if (raw) {
            const parsed = parseInt(raw, 10);
            if (!Number.isNaN(parsed) && parsed > 0) return parsed + 16; // breathing room
        }
    } catch { /* SSR / JSDOM */ }
    return 80; // 64px topbar + 16px breathing
})();

/**
 * useGuidedTour
 * 
 * Encapsulates all tour lifecycle logic for the registry-based multi-tour system:
 * - Checks layout readiness (not just a timer)
 * - Reads/writes persistence via localStorage (per tour + user)
 * - Filters steps based on DOM availability
 * - Handles all Joyride callbacks
 * - Manually compensates scroll position for the sticky header
 * - Exposes startTour, startCurrentModuleTour, startCurrentPageTour
 * - Route-aware resolution via getToursForRoute
 */

/**
 * Scrolls the step target element into view.
 *
 * For page tours: compensates for the sticky header using window.scrollTo().
 * For drawer tours: scrolls inside the specified container instead.
 *
 * @param target  — Joyride step target (CSS selector or HTMLElement)
 * @param scrollContainerSelector — optional CSS selector for the scroll container
 *                                  (e.g. '[data-tour-scroll-container="approval-drawer"]')
 */
const DRAWER_OFFSET_PX = 16; // breathing room inside drawer (no sticky topbar)

function scrollTargetIntoView(
    target: string | HTMLElement | undefined | null,
    scrollContainerSelector?: string
) {
    if (!target) return;

    const resolve = (): HTMLElement | null => {
        if (typeof target === 'string') {
            try { return document.querySelector(target); } catch { return null; }
        }
        return target;
    };

    // Wait one frame for Joyride to finish its own scroll
    requestAnimationFrame(() => {
        setTimeout(() => {
            const el = resolve();
            if (!el) return;

            // --- Drawer-aware scrolling ---
            if (scrollContainerSelector) {
                const container = document.querySelector(scrollContainerSelector) as HTMLElement | null;
                if (container) {
                    const containerRect = container.getBoundingClientRect();
                    const elRect = el.getBoundingClientRect();

                    // Check if the element is above the visible area of the container
                    const isAboveView = elRect.top < containerRect.top + DRAWER_OFFSET_PX;
                    // Check if below the visible bottom (accounting for sticky footer ~72px)
                    const stickyFooterHeight = 72;
                    const isBelowView = elRect.bottom > containerRect.bottom - stickyFooterHeight;

                    if (isAboveView || isBelowView) {
                        const scrollTop = container.scrollTop
                            + elRect.top
                            - containerRect.top
                            - DRAWER_OFFSET_PX;
                        container.scrollTo({ top: Math.max(0, scrollTop), behavior: 'smooth' });
                    }
                    return;
                }
                // Fallback: container not found, use window scroll
            }

            // --- Standard page scroll (sticky topbar compensation) ---
            const rect = el.getBoundingClientRect();
            const isBehindHeader = rect.top < HEADER_OFFSET_PX;
            const isBelowFold = rect.bottom > window.innerHeight;

            if (isBehindHeader || isBelowFold) {
                const scrollY = window.scrollY + rect.top - HEADER_OFFSET_PX;
                window.scrollTo({ top: Math.max(0, scrollY), behavior: 'smooth' });
            }
        }, 80); // small delay for Joyride's own scroll to settle
    });
}
export function useGuidedTour() {
    const { user } = useAuth();
    const userId = user?.id;
    const location = useLocation();

    const [run, setRun] = useState(false);
    const [steps, setSteps] = useState<TourStep[]>([]);
    const [showWelcome, setShowWelcome] = useState(false);
    const [activeTourId, setActiveTourId] = useState<TourId | null>(null);
    const [activeTourDef, setActiveTourDef] = useState<TourDefinition | null>(null);
    const [noStepsMessage, setNoStepsMessage] = useState<string | null>(null);
    const hasCheckedRef = useRef(false);

    /**
     * Layout readiness check (portal-main auto-show only).
     * Waits for the authenticated user AND the topbar/main menu to be in the DOM.
     * Uses a polling interval (200ms) with a max timeout (8s) instead of a fixed delay.
     */
    useEffect(() => {
        if (!userId || hasCheckedRef.current) return;

        let attempts = 0;
        const MAX_ATTEMPTS = 40; // 40 × 200ms = 8s max wait
        const POLL_INTERVAL = 200;
        let timerId: number;

        const checkLayout = () => {
            attempts++;
            const topbar = document.querySelector('[data-tour="topbar"]');
            const menu = document.querySelector('[data-tour="main-menu"]');

            if (topbar && menu) {
                // Layout is ready — check if portal-main tour should be shown
                hasCheckedRef.current = true;
                const state = getTourState('portal-main', userId);
                if (state.status === 'not-started') {
                    setShowWelcome(true);
                }
                return;
            }

            if (attempts < MAX_ATTEMPTS) {
                timerId = window.setTimeout(checkLayout, POLL_INTERVAL);
            }
        };

        // Small initial delay to let the first render complete
        timerId = window.setTimeout(checkLayout, 500);

        return () => {
            window.clearTimeout(timerId);
        };
    }, [userId]);

    // Auto-clear noStepsMessage after 3s
    useEffect(() => {
        if (!noStepsMessage) return;
        const timer = window.setTimeout(() => setNoStepsMessage(null), 3000);
        return () => window.clearTimeout(timer);
    }, [noStepsMessage]);

    /**
     * Internal: start a tour by its definition.
     * Filters steps, handles empty result, starts Joyride.
     */
    const executeTourStart = useCallback((tour: TourDefinition) => {
        if (!userId) return;

        // Reset state so it can be restarted even after completion
        resetTourState(tour.id, userId);

        // Close welcome modal if open
        setShowWelcome(false);

        // Dispatch preparation event — page components can listen and
        // perform pre-tour setup (e.g., expand the first request card).
        // The event is synchronous; handlers run immediately.
        window.dispatchEvent(new CustomEvent('guided-tour:prepare', {
            detail: { tourId: tour.id },
        }));

        // Wait for React to re-render after preparation (300ms),
        // then filter steps based on the updated DOM.
        const prepDelay = 350; // enough for React state update + DOM paint
        setTimeout(() => {
            requestAnimationFrame(() => {
                const activeSteps = filterActiveSteps(tour.steps);
                if (activeSteps.length === 0) {
                    console.warn(`[GuidedTour] No valid steps found for tour "${tour.id}"`);
                    setNoStepsMessage('Nenhum passo disponível para este tour no seu perfil atual.');
                    return;
                }
                setActiveTourId(tour.id);
                setActiveTourDef(tour);
                setSteps(activeSteps);
                setRun(true);
            });
        }, prepDelay);
    }, [userId]);

    /**
     * Start a specific tour by ID. Defaults to 'portal-main' if omitted.
     */
    const startTour = useCallback((tourId?: TourId) => {
        const id = tourId || 'portal-main';
        const tour = getTourById(id);
        if (!tour) {
            console.warn(`[GuidedTour] Tour "${id}" not found in registry`);
            return;
        }
        executeTourStart(tour);
    }, [executeTourStart]);

    /**
     * Start the module-level tour for the current route (if any).
     */
    const startCurrentModuleTour = useCallback(() => {
        const { module } = getToursForRoute(location.pathname);
        if (module) {
            executeTourStart(module);
        } else {
            setNoStepsMessage('Nenhum tour de módulo disponível para esta área.');
        }
    }, [location.pathname, executeTourStart]);

    /**
     * Start the page-level tour for the current route (if any).
     */
    const startCurrentPageTour = useCallback(() => {
        const { page } = getToursForRoute(location.pathname);
        if (page) {
            executeTourStart(page);
        } else {
            setNoStepsMessage('Nenhum tour de tela disponível para esta página.');
        }
    }, [location.pathname, executeTourStart]);

    /**
     * Get list of tours available for the current route.
     */
    const getAvailableTours = useCallback((): TourDefinition[] => {
        const { portal, module, page } = getToursForRoute(location.pathname);
        const result: TourDefinition[] = [portal];
        if (module) result.push(module);
        if (page) result.push(page);
        return result;
    }, [location.pathname]);

    /**
     * Dismiss the welcome modal without starting the tour.
     * Persists 'skipped' state so it won't appear again.
     */
    const dismissWelcome = useCallback(() => {
        if (userId) {
            setTourState('portal-main', userId, 'skipped');
        }
        setShowWelcome(false);
    }, [userId]);

    /**
     * Joyride onEvent handler.
     * Manages tour lifecycle events: completion, skip, errors.
     */
    const handleEvent = useCallback((data: EventData) => {
        const { status, action, type } = data;
        const finishedStatuses: Status[] = [STATUS.FINISHED, STATUS.SKIPPED];

        // Tour finished (completed all steps) or skipped
        if (finishedStatuses.includes(status)) {
            setRun(false);
            if (userId && activeTourId) {
                setTourState(activeTourId, userId, status === STATUS.FINISHED ? 'completed' : 'skipped');
            }
            setActiveTourId(null);
            setActiveTourDef(null);
            return;
        }

        // Error occurred — stop gracefully
        if (type === EVENTS.ERROR) {
            console.warn('[GuidedTour] Joyride error — stopping tour gracefully');
            setRun(false);
            setActiveTourId(null);
            setActiveTourDef(null);
            return;
        }

        // Compensate scroll position for sticky header on each step
        // For drawer tours, scrollContainerSelector routes to the drawer container
        if (type === EVENTS.STEP_BEFORE && data.step?.target) {
            scrollTargetIntoView(
                data.step.target as string | HTMLElement,
                activeTourDef?.scrollContainerSelector
            );
        }

        // Handle close button (X) on individual steps
        if (type === EVENTS.STEP_AFTER && action === ACTIONS.CLOSE) {
            setRun(false);
            if (userId && activeTourId) {
                setTourState(activeTourId, userId, 'skipped');
            }
            setActiveTourId(null);
            setActiveTourDef(null);
            return;
        }
    }, [userId, activeTourId]);

    return {
        run,
        steps,
        showWelcome,
        handleEvent,
        startTour,
        startCurrentModuleTour,
        startCurrentPageTour,
        getAvailableTours,
        activeTourId,
        activeTourDef,
        noStepsMessage,
        dismissWelcome,
    };
}
