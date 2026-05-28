import type { LiveGuideId, LiveGuideState, LiveGuideStatus } from './liveGuideTypes';

/**
 * Live Guide Storage
 *
 * localStorage-based persistence for live guide state.
 * Keys are versioned and scoped to the authenticated user ID
 * to prevent state leaks between users.
 *
 * Follows the same pattern as guidedTourStorage.ts.
 */

const STORAGE_VERSION = 'v1';

/** Build the localStorage key for a given guide + user */
function buildKey(guideId: LiveGuideId, userId: string): string {
    return `live-guide:${guideId}:${STORAGE_VERSION}:${userId}`;
}

/** Default state for a guide that has never been used */
const DEFAULT_STATE: LiveGuideState = {
    status: 'not-started',
    lastSeenAt: null,
};

/**
 * Read the persisted live guide state for a specific user.
 * Returns default 'not-started' state if nothing is stored or data is corrupted.
 */
export function getLiveGuideState(guideId: LiveGuideId, userId: string): LiveGuideState {
    try {
        const raw = localStorage.getItem(buildKey(guideId, userId));
        if (!raw) return { ...DEFAULT_STATE };
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed.status === 'string') {
            return parsed as LiveGuideState;
        }
        return { ...DEFAULT_STATE };
    } catch {
        return { ...DEFAULT_STATE };
    }
}

/**
 * Persist the live guide state for a specific user.
 * Automatically sets the `lastSeenAt` timestamp.
 */
export function setLiveGuideState(guideId: LiveGuideId, userId: string, status: LiveGuideStatus): void {
    try {
        const state: LiveGuideState = {
            status,
            lastSeenAt: new Date().toISOString(),
        };
        localStorage.setItem(buildKey(guideId, userId), JSON.stringify(state));
    } catch {
        console.warn('[LiveGuide] Failed to persist guide state');
    }
}

/**
 * Reset the live guide state so it can be restarted.
 */
export function resetLiveGuideState(guideId: LiveGuideId, userId: string): void {
    try {
        localStorage.removeItem(buildKey(guideId, userId));
    } catch {
        console.warn('[LiveGuide] Failed to reset guide state');
    }
}
