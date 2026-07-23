import { resolveParentDisplayStatus, resolveGroupStatusLabel, hasMultipleOperationalGroups, resolveOperationalGroups, DisplayGroupSummary } from '../../../lib/requestGroupDisplayState';

interface RequestGroupDisplaySummaryProps {
    poGroups: (DisplayGroupSummary & { supplierNameSnapshot?: string | null })[] | null | undefined;
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
