import { PersonalActionItem } from '../../types/myActions';

// v2.243.0 Phase 2 — pure deep-link target matching. GUID identity fields (requestId, poGroupId) are
// compared CASE-INSENSITIVELY: a deep-link may carry an UPPERCASE id while .NET serializes Guid
// lowercase, so a strict === would falsely fail. actionType stays an exact (canonical enum) match.
// Applied only to id fields — never to arbitrary business text.

export const normalizeId = (v?: string | null) => (v ?? '').trim().toLowerCase();

export interface TargetMatchInput {
  actionType: string | null;
  requestId?: string;
  poGroupId?: string;
}

/**
 * Resolve the deep-link target within the backend-resolved page:
 *   1. requestId + actionType, preferring an exact poGroupId (own or within affectedSuppliers);
 *   2. fallback to the requestId + actionType item the backend already resolved (aggregated actions).
 * GUID casing never makes a valid target fail; a genuinely different requestId never matches.
 */
export function resolveTargetItem(items: PersonalActionItem[], target: TargetMatchInput): PersonalActionItem | undefined {
  const tReq = normalizeId(target.requestId);
  const tGrp = normalizeId(target.poGroupId);
  const idMatch = (i: PersonalActionItem) => normalizeId(i.requestId) === tReq && i.actionType === target.actionType;
  return items.find(i => idMatch(i) &&
      (!target.poGroupId || normalizeId(i.poGroupId) === tGrp || (i.affectedSuppliers?.some(g => normalizeId(g.poGroupId) === tGrp) ?? false)))
    ?? items.find(idMatch);
}
