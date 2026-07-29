// ============================================================================
// Global stale lazy-chunk failure handling.
//
// After a deploy that removed the previous hashed chunks (robocopy /MIR), an already-open tab that
// lazy-loads a route will fail to fetch a chunk that no longer exists. We detect that centrally and
// route it into the same update-required state + blocking modal as a version mismatch.
//
// Detection prefers event/error TYPE (Vite 5's `vite:preloadError`, ChunkLoadError name) and uses
// message strings only as a documented fallback, because the wording varies by browser.
// ============================================================================

import { versionSignal } from './versionSignal';
import { logger } from './logger';

// Documented cross-browser fallback substrings (Chrome/Firefox/Safari/Edge variants).
const CHUNK_MESSAGE_PATTERNS = [
    'Failed to fetch dynamically imported module',
    'error loading dynamically imported module',
    'Importing a module script failed',
    'dynamically imported module',
    'ChunkLoadError',
    'Loading chunk',
    'Loading CSS chunk',
];

/** True when the given error/message looks like a dynamic-import (lazy chunk) load failure. */
export function isChunkLoadError(input: unknown): boolean {
    if (!input) return false;
    // Type-based detection first.
    const name = (input as { name?: string })?.name;
    if (name === 'ChunkLoadError') return true;
    const message =
        typeof input === 'string' ? input : ((input as { message?: string })?.message ?? '');
    if (!message) return false;
    return CHUNK_MESSAGE_PATTERNS.some(p => message.includes(p));
}

let installed = false;

/** Install global listeners once (call from main.tsx before render). */
export function installChunkErrorHandler(): void {
    if (installed) return;
    installed = true;

    // Vite 5 preload error — the most reliable, type-based signal. Prevent the default hard throw.
    window.addEventListener('vite:preloadError', (event: Event) => {
        event.preventDefault();
        signalChunk('vite:preloadError');
    });

    window.addEventListener('error', (event: ErrorEvent) => {
        if (isChunkLoadError(event.error) || isChunkLoadError(event.message)) {
            signalChunk('window.error');
        }
    });

    window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
        if (isChunkLoadError(event.reason)) {
            signalChunk('unhandledrejection');
        }
    });
}

function signalChunk(source: string): void {
    if (versionSignal.isOutdated()) return;
    try {
        logger.log({
            level: 'Warning',
            eventType: 'STALE_CHUNK_DETECTED',
            message: `Stale lazy-loaded chunk detected (${source}) — Portal likely updated.`,
            componentKey: 'Global',
        });
    } catch { /* logging must never block the update flow */ }
    versionSignal.markOutdated('chunk');
}
