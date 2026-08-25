// Phase 3D / Layer D — pure decision logic for the Supplier Sheet drawer's dirty + supplier-switch guards.
// Extracted so the guard behavior is unit-tested without a render stack. No API, no React.

export type DrawerGuard = { reason: 'close' | 'switch'; target?: number };

export interface DrawerOpenDecision {
  nextShownId: number | null;   // the supplier the drawer should show now (unchanged when a guard is raised)
  guard: DrawerGuard | null;    // a confirmation to raise before proceeding, or null
}

/**
 * Decide what happens when `requestedId` is opened.
 *  - drawer closed, or same supplier → just show it (no guard)
 *  - different supplier, not dirty → switch immediately (content remounts on id change)
 *  - different supplier, dirty → keep current and raise a switch guard (never discard silently)
 */
export function decideOpen(shownId: number | null, isDirty: boolean, requestedId: number): DrawerOpenDecision {
  if (shownId === null || shownId === requestedId) return { nextShownId: requestedId, guard: null };
  if (!isDirty) return { nextShownId: requestedId, guard: null };
  return { nextShownId: shownId, guard: { reason: 'switch', target: requestedId } };
}

/** Decide what happens on a close request: close immediately when clean, otherwise raise a close guard. */
export function decideClose(isDirty: boolean): { close: boolean; guard: DrawerGuard | null } {
  return isDirty ? { close: false, guard: { reason: 'close' } } : { close: true, guard: null };
}

/** On "Descartar alterações": a switch goes to its target; a close unmounts (null). */
export function resolveDiscard(guard: DrawerGuard): { nextShownId: number | null } {
  return { nextShownId: guard.reason === 'switch' && guard.target != null ? guard.target : null };
}
