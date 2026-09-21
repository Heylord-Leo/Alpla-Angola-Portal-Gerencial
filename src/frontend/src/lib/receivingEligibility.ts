// v2.245.0 — the SINGLE canonical frontend mirror of the backend receiving-eligibility rule
// (AlplaPortal.Domain.Services.ReceivingActionEvaluator.ActionableStatuses). A RequestPoGroup is
// Receiving-actionable ONLY when its GROUP status is one of these — never by the request scalar status.
// This is what keeps the UI from exposing an action the backend will reject (the PAYMENT PENDING drift).
//
// PENDING is deliberately absent and must stay absent: it is the pre-approval group status, never a
// valid receiving phase.
import { ROLES } from '../constants/roles';

/** Canonical backend-valid receiving group statuses (verbatim mirror of the server evaluator). */
export const RECEIVING_ACTIONABLE_GROUP_STATUSES = [
  'PAYMENT_COMPLETED',
  'WAITING_RECEIPT',
  'IN_FOLLOWUP',
  'WAITING_SUPPLIER_DELIVERY',
] as const;

// Legacy display aliases seen on older group rows that map onto the canonical actionable states.
// Kept so pre-existing data keeps working; NEVER includes PENDING.
const LEGACY_ACTIONABLE_ALIASES = ['PAG_REALIZADO', 'AG_RECIBO', 'RECEBIMENTO_ANDAMENTO'] as const;

const ACTIONABLE = new Set<string>([
  ...RECEIVING_ACTIONABLE_GROUP_STATUSES,
  ...LEGACY_ACTIONABLE_ALIASES,
]);

/** True when a group's own status is a valid receiving phase (backend will accept receiving actions). */
export function isReceivingActionableGroupStatus(groupStatus?: string | null): boolean {
  return !!groupStatus && ACTIONABLE.has(groupStatus);
}

/** Human-readable read-only blocker shown when a group is not yet in a valid receiving phase (§15). */
export const RECEIVING_PHASE_BLOCKER =
  'Este grupo ainda não está numa fase válida de recebimento.';

// ── v2.245.0 duplicate-confirm fix ───────────────────────────────────────────
// CONFIRM_RECEIVING is a one-time operator attestation, distinct from generic receiving access
// (isReceivingActionableGroupStatus). It is available ONLY from a PRE-confirmation status AND only once
// every item is received. WAITING_RECEIPT is the POST-confirmation state — the confirm button must NOT
// reappear there (that was the REQ-06/07/2026-023 defect). Mirrors the backend
// ReceivingActionEvaluator.ConfirmActionStatuses / IsReceivingConfirmed.

/** Pre-confirmation group statuses the CONFIRM_RECEIVING action is offered from (excludes WAITING_RECEIPT). */
export const RECEIVING_CONFIRM_ACTION_STATUSES = [
  'PAYMENT_COMPLETED',
  'IN_FOLLOWUP',
  'WAITING_SUPPLIER_DELIVERY',
] as const;

// Pre-confirmation legacy aliases (NOT AG_RECIBO — that maps to the post-confirmation WAITING_RECEIPT).
const CONFIRM_ACTION_ALIASES = ['PAG_REALIZADO', 'RECEBIMENTO_ANDAMENTO'] as const;
// Post-confirmation group statuses: receiving already confirmed.
const CONFIRMED_STATUSES = ['WAITING_RECEIPT', 'AG_RECIBO', 'WAITING_FISCAL_RECEIPT', 'COMPLETED'] as const;

const CONFIRM_ACTION = new Set<string>([...RECEIVING_CONFIRM_ACTION_STATUSES, ...CONFIRM_ACTION_ALIASES]);
const CONFIRMED = new Set<string>(CONFIRMED_STATUSES);

/** True when the group's receiving has already been confirmed (post-confirmation state). */
export function isReceivingConfirmed(groupStatus?: string | null): boolean {
  return !!groupStatus && CONFIRMED.has(groupStatus);
}

/** True when the group is in a pre-confirmation status the CONFIRM action can be offered from. */
export function isPreConfirmReceivingStatus(groupStatus?: string | null): boolean {
  return !!groupStatus && CONFIRM_ACTION.has(groupStatus);
}

/**
 * The button rule for CONFIRM_RECEIVING: a valid pre-confirmation status AND every item received AND not
 * already confirmed. Never equate this with isReceivingActionableGroupStatus.
 */
export function canConfirmReceiving(groupStatus: string | null | undefined, allItemsReceived: boolean): boolean {
  return isPreConfirmReceivingStatus(groupStatus) && allItemsReceived && !isReceivingConfirmed(groupStatus);
}

// ── v2.245.5 item registration / correction ──────────────────────────────────
// Registering or correcting an item's accumulated quantity (REGISTRAR / AJUSTAR) is a PRE-confirmation
// action. Once the operator confirmed receiving (WAITING_RECEIPT and beyond) quantities are frozen; a
// correction first requires the explicit, audited REABRIR RECEBIMENTO (which returns the group to
// IN_FOLLOWUP). Mirrors the backend ReceivingActionEvaluator.CanRegisterItemReceipt.
export function canRegisterItemReceipt(groupStatus?: string | null): boolean {
  return isPreConfirmReceivingStatus(groupStatus);
}

/** Only a confirmed-but-not-yet-finalized group (WAITING_RECEIPT) can be reopened for correction. */
export function canReopenReceiving(groupStatus?: string | null): boolean {
  return groupStatus === 'WAITING_RECEIPT';
}

export type ReceivingItemActionLabel = 'REGISTRAR' | 'AJUSTAR' | 'VER DETALHES';

/**
 * The per-item action label. Pre-confirmation: a pending item is REGISTERED, an already (partially)
 * received item is ADJUSTED (absolute accumulated correction, audited). Any read-only context —
 * including a confirmed (WAITING_RECEIPT) or COMPLETED group — only allows viewing.
 */
export function receivingItemActionLabel(
  groupStatus: string | null | undefined,
  receivedQty: number | null | undefined,
  readOnly: boolean,
): ReceivingItemActionLabel {
  if (readOnly || !canRegisterItemReceipt(groupStatus)) return 'VER DETALHES';
  return (receivedQty ?? 0) > 0 ? 'AJUSTAR' : 'REGISTRAR';
}

/** Roles allowed to REABRIR RECEBIMENTO — mirrors the backend (Receiving or System Administrator). */
export const RECEIVING_REOPEN_ROLES = [ROLES.RECEIVING, ROLES.SYSTEM_ADMINISTRATOR] as const;

export function userCanReopenReceiving(roles: readonly string[] | null | undefined): boolean {
  return !!roles?.some((r) => (RECEIVING_REOPEN_ROLES as readonly string[]).includes(r));
}

/** REABRIR RECEBIMENTO is offered only on a confirmed group, to an authorized user, outside read-only views. */
export function canShowReopenReceiving(
  groupStatus: string | null | undefined,
  roles: readonly string[] | null | undefined,
  readOnly: boolean,
): boolean {
  return !readOnly && canReopenReceiving(groupStatus) && userCanReopenReceiving(roles);
}
