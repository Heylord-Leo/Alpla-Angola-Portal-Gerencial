import { useNavigate } from 'react-router-dom';
import { ArrowRight, Building2, FileText } from 'lucide-react';
import { PersonalActionItem } from '../../types/myActions';
import { formatCurrencyAO } from '../../lib/utils';

// v2.243.0 — ONE reusable, action-agnostic COMPACT row. Renders purely from the DTO (no per-action
// business logic, no hardcoded correction flow) and navigates via item.route. Two dense lines + a
// small right-aligned CTA; color is used sparingly (a thin left accent + subtle chips).

const NEED_LEVEL_LABEL: Record<string, string> = {
  CRITICO: 'Crítico', URGENTE: 'Urgente', NORMAL: 'Normal', BAIXO: 'Baixo',
};

function Chip({ text, tone }: { text: string; tone: 'action' | 'danger' | 'need' }) {
  const palette = {
    action: { bg: 'color-mix(in srgb, var(--color-primary) 10%, transparent)', fg: 'var(--color-primary)' },
    danger: { bg: 'color-mix(in srgb, var(--color-status-red) 12%, transparent)', fg: 'var(--color-status-red)' },
    need:   { bg: 'color-mix(in srgb, var(--color-status-amber) 14%, transparent)', fg: 'var(--color-status-amber)' },
  }[tone];
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', padding: '1px 7px', borderRadius: 6,
      background: palette.bg, color: palette.fg, fontWeight: 700, fontSize: '0.64rem',
      textTransform: 'uppercase', letterSpacing: '0.02em', whiteSpace: 'nowrap', lineHeight: 1.6,
    }}>{text}</span>
  );
}

export function PersonalActionCard({ item, targeted = false, cardRef }: {
  item: PersonalActionItem;
  /** Phase 2 — true while this row is the deep-link target (transient ~5s emphasis). */
  targeted?: boolean;
  /** Phase 2 — callback ref so the queue can scroll this row into view by stable action id. */
  cardRef?: (el: HTMLDivElement | null) => void;
}) {
  const navigate = useNavigate();

  const supplierName = item.supplierName || item.affectedSuppliers?.[0]?.supplierName || null;
  const extra = Math.max(0, (item.affectedSuppliers?.length ?? 0) - 1);
  const supplierLabel = supplierName ? (extra > 0 ? `${supplierName} +${extra}` : supplierName) : null;
  const supplierTitle = (item.affectedSuppliers?.length ?? 0) > 1
    ? item.affectedSuppliers.map(s => s.supplierName).filter(Boolean).join(', ')
    : (supplierName ?? undefined);
  const po = item.purchaseOrderNumber || item.affectedSuppliers?.[0]?.purchaseOrderNumber || null;
  const needLabel = item.needLevelCode ? (NEED_LEVEL_LABEL[item.needLevelCode] ?? item.needLevelCode) : null;
  // Sparse accent: amber for a P.O. correction, red when overdue, otherwise a subtle neutral edge.
  const accent = item.actionType === 'PO_CORRECTION' ? 'var(--color-status-amber)'
    : item.isOverdue ? 'var(--color-status-red)'
    : 'var(--color-border)';

  const onCta = () => { if (item.route) navigate(item.route); };

  return (
    <div
      ref={cardRef}
      data-targeted={targeted ? 'true' : undefined}
      aria-current={targeted ? 'true' : undefined}
      className={targeted ? 'pma-targeted' : undefined}
      style={{
      display: 'grid', gridTemplateColumns: '1fr auto', columnGap: 12, alignItems: 'center',
      padding: '9px 12px 9px 13px',
      // Transient target emphasis (~5s): red left accent + soft tint. The pulse itself is a CSS class
      // (pma-targeted) that is disabled under prefers-reduced-motion; the tint stays for both modes.
      background: targeted ? 'color-mix(in srgb, var(--color-status-red) 8%, var(--color-bg-surface))' : 'var(--color-bg-surface)',
      borderBottom: '1px solid var(--color-border)',
      borderLeft: `3px solid ${targeted ? 'var(--color-status-red)' : accent}`,
    }}>
      <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 3 }}>
        {/* Line 1: number · title · action + urgency chips */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
          <span style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '0.8rem', flexShrink: 0 }}>{item.requestNumber}</span>
          <span title={item.requestTitle} style={{ color: 'var(--color-text-main)', fontWeight: 600, fontSize: '0.8rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {item.requestTitle}
          </span>
          <span style={{ display: 'inline-flex', gap: 5, flexShrink: 0, marginLeft: 'auto' }}>
            <Chip text={item.actionLabel} tone="action" />
            {item.isOverdue && <Chip text="Vencido" tone="danger" />}
            {needLabel && <Chip text={needLabel} tone="need" />}
          </span>
        </div>
        {/* Line 2: supplier +N · PO · amount (only present fields) */}
        {(supplierLabel || po || item.amount != null) && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
            {supplierLabel && (
              <span title={supplierTitle} style={{ display: 'inline-flex', alignItems: 'center', gap: 5, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 300 }}>
                <Building2 size={11} /> {supplierLabel}
              </span>
            )}
            {po && <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}><FileText size={11} /> {po}</span>}
            {item.amount != null && (
              <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>{formatCurrencyAO(item.amount)} {item.currencyCode || ''}</span>
            )}
          </div>
        )}
      </div>
      {/* Compact CTA */}
      <button
        onClick={onCta}
        aria-label={`${item.actionLabel} — ${item.requestNumber}`}
        style={{
          display: 'inline-flex', alignItems: 'center', gap: 5, justifyContent: 'center',
          padding: '5px 10px', cursor: 'pointer', background: 'transparent',
          color: 'var(--color-primary)', border: '1px solid var(--color-primary)',
          borderRadius: 'var(--radius-sm)', fontWeight: 700, fontSize: '0.72rem', whiteSpace: 'nowrap',
        }}
      >
        {item.actionLabel} <ArrowRight size={13} />
      </button>
    </div>
  );
}
