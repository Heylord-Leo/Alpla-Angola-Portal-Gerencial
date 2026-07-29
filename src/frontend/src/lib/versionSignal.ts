// ============================================================================
// Version-mismatch signal bus.
//
// A tiny framework-free pub/sub that decouples the *detectors* of an outdated frontend
// (the version monitor's poll, a 409 CLIENT_VERSION_OUTDATED from apiFetch, and stale lazy-chunk
// failures) from the *reactor* (the VersionProvider that renders the blocking modal). It also tracks
// in-flight write requests so the modal never reloads on top of an active mutation/upload.
// ============================================================================

export type UpdateReason = 'version' | 'chunk';

type Listener = (reason: UpdateReason) => void;

let outdated = false;
let firstReason: UpdateReason | null = null;
let listeners: Listener[] = [];
let activeWrites = 0;

export const versionSignal = {
    /** Mark the frontend as outdated and notify subscribers (idempotent — first reason wins). */
    markOutdated(reason: UpdateReason): void {
        if (outdated) return;
        outdated = true;
        firstReason = reason;
        for (const l of listeners) {
            try { l(reason); } catch { /* never let a listener break the signal */ }
        }
    },
    isOutdated(): boolean {
        return outdated;
    },
    reason(): UpdateReason | null {
        return firstReason;
    },
    subscribe(listener: Listener): () => void {
        listeners.push(listener);
        // Replay to a late subscriber so it cannot miss an already-fired signal.
        if (outdated && firstReason) {
            try { listener(firstReason); } catch { /* ignore */ }
        }
        return () => { listeners = listeners.filter(l => l !== listener); };
    },

    // ── Active write tracking (reload safety) ──
    beginWrite(): void { activeWrites += 1; },
    endWrite(): void { activeWrites = Math.max(0, activeWrites - 1); },
    hasActiveWrites(): boolean { return activeWrites > 0; },
};
