// ============================================================================
// Reload safety for the version-mismatch update flow.
//
// - Unsaved-work registry: components (forms/wizards) may register a predicate so the update modal
//   warns before reloading instead of silently discarding input. Nothing is registered by default,
//   so the guard never blocks spuriously.
// - Reload-loop guard: a per-build one-shot marker (sessionStorage) so a stale-chunk reload is not
//   attempted forever if the shell is momentarily inconsistent mid-deploy.
// ============================================================================

import { buildInfo } from '../buildInfo';

const RELOAD_KEY = 'portal_reload_attempted_build';

type DirtyPredicate = () => boolean;
const dirtyPredicates = new Set<DirtyPredicate>();

/** Register an "is there unsaved work?" predicate. Returns an unregister function. */
export function registerUnsavedWork(predicate: DirtyPredicate): () => void {
    dirtyPredicates.add(predicate);
    return () => { dirtyPredicates.delete(predicate); };
}

export function hasUnsavedWork(): boolean {
    for (const p of dirtyPredicates) {
        try { if (p()) return true; } catch { /* ignore a faulty predicate */ }
    }
    return false;
}

export function reloadAlreadyAttempted(): boolean {
    try { return sessionStorage.getItem(RELOAD_KEY) === buildInfo.buildId; } catch { return false; }
}

export function markReloadAttempted(): void {
    try { sessionStorage.setItem(RELOAD_KEY, buildInfo.buildId); } catch { /* storage may be unavailable */ }
}
