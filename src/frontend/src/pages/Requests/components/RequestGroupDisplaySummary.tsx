import { resolveParentDisplayStatus, resolveGroupStatusLabel, hasMultipleOperationalGroups, resolveOperationalGroups, DisplayGroupSummary } from '../../../lib/requestGroupDisplayState';
import { CANCELLED_GROUP_CHIP_LABEL, priorEcfBlockMessage } from '../../../lib/poGroupCancellation';
import type { PriorEcfConfirmationDto, PoGroupCancellationDto } from '../../../types';

type SummaryGroup = DisplayGroupSummary & {
    supplierNameSnapshot?: string | null;
    purchaseOrderNumber?: string | null;
    /** Phase 1 P.O. cancellation: present on CANCELLED groups. */
    cancellation?: PoGroupCancellationDto | null;
    /** D4: replacement P.O. blocked until Finance confirms the previous ECF annulment. */
    priorEcfConfirmation?: PriorEcfConfirmationDto | null;
};

/**
 * Phase 1 P.O. cancellation — compact chips for the request page: "P.O. cancelada" for every CANCELLED group
 * (kept out of the operational summary on purpose) and a short notice on a WAITING_PO group whose replacement
 * P.O. is blocked by an unconfirmed prior ECF cancellation. Renders nothing when neither applies.
 */
export function RequestGroupCancellationChips({ poGroups }: { poGroups: SummaryGroup[] | null | undefined }) {
    const cancelled = (poGroups || []).filter(g => g.status === 'CANCELLED');
    const blocked = (poGroups || []).filter(g => g.status === 'WAITING_PO' && g.priorEcfConfirmation?.blocked);
    if (cancelled.length === 0 && blocked.length === 0) return null;
    return (
        <div data-tour="request-po-cancellation-chips" style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            {cancelled.length > 0 && (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                    {cancelled.map(g => (
                        <span key={g.id} title={g.cancellation ? `${g.cancellation.reasonLabel || g.cancellation.reasonCode} — ${g.cancellation.reason}` : undefined}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '3px 10px', borderRadius: '999px', fontSize: '0.72rem', fontWeight: 700, color: '#991B1B', backgroundColor: '#FEF2F2', border: '1px solid #FECACA' }}>
                            {CANCELLED_GROUP_CHIP_LABEL}
                            <span style={{ fontWeight: 600, color: '#B91C1C' }}>{[g.supplierNameSnapshot, g.cancellation?.purchaseOrderNumberSnapshot || g.purchaseOrderNumber].filter(Boolean).join(' · ')}</span>
                        </span>
                    ))}
                </div>
            )}
            {blocked.map(g => (
                <div key={`prior-${g.id}`} role="alert" style={{ padding: '8px 12px', borderRadius: '6px', fontSize: '0.75rem', fontWeight: 600, color: '#991B1B', backgroundColor: '#FEF2F2', border: '1px solid #FECACA' }}>
                    {g.supplierNameSnapshot ? `${g.supplierNameSnapshot}: ` : ''}{priorEcfBlockMessage(g.priorEcfConfirmation)}
                </div>
            ))}
        </div>
    );
}

interface RequestGroupDisplaySummaryProps {
    poGroups: SummaryGroup[] | null | undefined;
    /** The persisted Request.Status name — used unchanged whenever there's nothing to reconcile
     *  (single shared group status, or no groups). */
    fallbackStatusName: string;
}

/**
 * Compact group-aware summary shown in the Request drawer for multi-group QUOTATION requests,
 * where the persisted Request.Status can only ever reflect one "furthest behind" group (see
 * RequestGroupDisplayStateCalculator/resolveParentDisplayStatus). Renders nothing for single-group
 * or group-less requests — the drawer's existing status display is already accurate there.
 *
 * Reuses GROUP_STATUS_LABELS (via resolveGroupStatusLabel) — the same friendly per-group labels
 * already shown in the Finance workspace's group cards. Do not duplicate a third label map here.
 */
export function RequestGroupDisplaySummary({ poGroups, fallbackStatusName }: RequestGroupDisplaySummaryProps) {
    if (!hasMultipleOperationalGroups(poGroups)) return null;

    const display = resolveParentDisplayStatus(poGroups, fallbackStatusName);
    const operationalGroups = resolveOperationalGroups(poGroups);

    return (
        <div style={{
            display: 'flex',
            flexDirection: 'column',
            gap: '8px',
            padding: '12px 16px',
            backgroundColor: 'rgba(0,0,0,0.02)',
            borderRadius: 'var(--radius-md, 8px)',
            border: '1px dashed var(--color-border)',
        }}>
            <div style={{ fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                {display.label}
            </div>
            <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                {operationalGroups.map(group => (
                    <li key={group.id} style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                        <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>
                            {group.supplierNameSnapshot || '---'}
                        </span>
                        {' — '}
                        {resolveGroupStatusLabel(group.status)}
                    </li>
                ))}
            </ul>
        </div>
    );
}
