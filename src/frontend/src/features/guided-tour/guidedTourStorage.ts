import type { TourId, TourState, TourStatus } from './guidedTourTypes';

/**
 * Guided Tour Storage
 * 
 * localStorage-based persistence for tour state.
 * Keys are versioned and scoped to the authenticated user ID
 * to prevent state leaks between users.
 */

const STORAGE_VERSION = 'v1';

/** Build the localStorage key for a given tour + user */
function buildKey(tourId: TourId, userId: string): string {
    return `guided-tour:${tourId}:${STORAGE_VERSION}:${userId}`;
}

/** Default state for a tour that has never been seen */
const DEFAULT_STATE: TourState = {
    status: 'not-started',
    lastSeenAt: null,
};

/**
 * Read the persisted tour state for a specific user.
 * Returns default 'not-started' state if nothing is stored or data is corrupted.
 */
export function getTourState(tourId: TourId, userId: string): TourState {
    try {
        const raw = localStorage.getItem(buildKey(tourId, userId));
        if (!raw) return { ...DEFAULT_STATE };
        const parsed = JSON.parse(raw);
        // Basic shape validation
        if (parsed && typeof parsed.status === 'string') {
            return parsed as TourState;
        }
        return { ...DEFAULT_STATE };
    } catch {
        return { ...DEFAULT_STATE };
    }
}

/**
 * Persist the tour state for a specific user.
 * Automatically sets the `lastSeenAt` timestamp.
 */
export function setTourState(tourId: TourId, userId: string, status: TourStatus): void {
    try {
        const state: TourState = {
            status,
            lastSeenAt: new Date().toISOString(),
        };
        localStorage.setItem(buildKey(tourId, userId), JSON.stringify(state));
    } catch {
        // localStorage may be full or disabled — fail silently
        console.warn('[GuidedTour] Failed to persist tour state');
    }
}

/**
 * Reset the tour state so it can be restarted.
 * Used by the manual "restart tour" help button.
 */
export function resetTourState(tourId: TourId, userId: string): void {
    try {
        localStorage.removeItem(buildKey(tourId, userId));
    } catch {
        console.warn('[GuidedTour] Failed to reset tour state');
    }
}
