import type React from 'react';
import type { BuyerQueueItem } from '../../types/buyerQueue';
import { resolveAssignAction, type AssignActionDecision, type AssignRoleNames } from './workspaceAssignActions';

/**
 * Claim / takeover entry in the quotation queue's row kebab ("Ver detalhes", "Adicionar observação", …).
 * Reuses the ONE eligibility policy (`resolveAssignAction`), the shared TakeOverRequestDialog and `runAssignRequest`:
 * no separate permission logic. The kebab closes itself after an option click (KebabMenu), so the dialog opens with
 * the menu already closed; CLAIM goes straight to the existing claim handler.
 */
export interface KebabOptionLike { label: string; onClick: () => void; icon?: React.ReactNode; }

export function resolveQueueAssignAction(
    item: Pick<BuyerQueueItem, 'requestTypeCode' | 'requestStatusCode' | 'buyerId'>,
    user: { id?: string | null; roles?: readonly string[] | null } | null | undefined,
    roleNames: AssignRoleNames,
): AssignActionDecision {
    return resolveAssignAction({ requestTypeCode: item.requestTypeCode, requestStatusCode: item.requestStatusCode, buyerId: item.buyerId }, user, roleNames);
}

/** Appends the eligible assign action (if any) after the row's standard options; NONE adds nothing. */
export function buildRequestRowKebab(
    base: KebabOptionLike[],
    decision: AssignActionDecision,
    handlers: { onClaim: () => void; onTakeOver: () => void },
    icon?: React.ReactNode,
): KebabOptionLike[] {
    if (decision.kind === 'NONE') return base;
    const onClick = decision.kind === 'TAKEOVER' ? handlers.onTakeOver : handlers.onClaim;
    return [...base, { label: decision.label ?? 'Assumir pedido', onClick, icon }];
}
