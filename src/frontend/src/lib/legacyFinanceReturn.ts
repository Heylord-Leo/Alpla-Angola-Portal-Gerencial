import type { LegacyFinanceReturnCorrectionDto } from '../types';

/**
 * docs §G.19.3 — legacy request-level Finance return (returns made before the group-scoped return of 2026-08-23
 * set only the request scalar to WAITING_PO_CORRECTION and left the sole group at PO_ISSUED).
 *
 * The decision that a group needs the legacy correction — and that the Buyer's justification is mandatory for it —
 * is the backend's (`RequestDetailsDto.legacyReturnCorrection`, computed by the same predicate RegisterPo enforces).
 * This helper only maps that decision onto the loaded groups: it never infers anything from the scalar status.
 */
export function resolveLegacyReturnCorrectionGroup(
    decision: LegacyFinanceReturnCorrectionDto | null | undefined,
    poGroups: any[] | null | undefined,
): any | null {
    if (!decision || !decision.justificationRequired || !decision.poGroupId || !poGroups) return null;
    return poGroups.find(g => g && g.id === decision.poGroupId && g.status !== 'CANCELLED') ?? null;
}
