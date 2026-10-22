import React from 'react';
import { User as UserIcon } from 'lucide-react';
import type { AssignActionDecision } from './workspaceAssignActions';

interface Props {
    buyerName?: string | null;
    assignAction: AssignActionDecision;
    busy: boolean;
    onClaim: () => void;
    onTakeover: () => void;
}

/**
 * "Comprador" field of the Workspace header.
 *
 * Root cause of the invisible takeover button (TEST v2.248.0, REQ-20/08/2026-271): the button used to be rendered
 * INSIDE the generic `Meta` value container, whose style is `white-space: nowrap; overflow: hidden;
 * text-overflow: ellipsis` in a `minmax(150px, 1fr)` grid cell — the owner's name plus "Assumir pedido de outro
 * comprador" did not fit the cell width, so the button existed in the DOM but was clipped out of view. This field
 * renders the name and the action as separate block rows: the name keeps the ellipsis treatment, the action never
 * sits inside an overflow-hidden / nowrap container (`BuyerOwnerField.test.ts` renders it and asserts exactly that).
 */
export function BuyerOwnerField({ buyerName, assignAction, busy, onClaim, onTakeover }: Props) {
    return (
        <div style={{ minWidth: 0 }} data-tour="buyer-workspace-owner">
            <div style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.03em', color: 'var(--color-text-muted)' }}>
                <UserIcon size={13} /> Comprador
            </div>
            <div style={{ fontSize: '0.84rem', fontWeight: 600, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={buyerName || undefined}>
                {buyerName || 'Não atribuído'}
            </div>
            {assignAction.kind !== 'NONE' && (
                <div style={{ marginTop: 4 }}>
                    <button
                        type="button"
                        data-tour="buyer-workspace-assign"
                        disabled={busy}
                        onClick={() => assignAction.kind === 'TAKEOVER' ? onTakeover() : onClaim()}
                        title={assignAction.reason}
                        style={{ display: 'inline-flex', alignItems: 'center', gap: 4, padding: '3px 9px', borderRadius: 8, border: '1px solid var(--color-primary)', background: assignAction.kind === 'CLAIM' ? 'var(--color-primary)' : 'var(--color-bg-surface)', color: assignAction.kind === 'CLAIM' ? '#fff' : 'var(--color-primary)', fontSize: '0.7rem', fontWeight: 700, cursor: busy ? 'not-allowed' : 'pointer', opacity: busy ? 0.6 : 1, whiteSpace: 'normal', textAlign: 'left' }}
                    >
                        <UserIcon size={12} /> {assignAction.label}
                    </button>
                </div>
            )}
        </div>
    );
}
