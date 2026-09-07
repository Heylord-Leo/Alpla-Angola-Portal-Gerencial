import { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { AlertOctagon, AlertTriangle, ChevronRight } from 'lucide-react';
import type { DashboardV2AlertDto } from '../../../types/dashboardV2';
import { alertDomainLabel, alertPlaneMeta, alertSeverityMeta, alertUrgencyText } from '../alertsView';

// Dashboard V2 B8.2a — one compact alert row, shared by the Dashboard preview and the full-list drawer.
// Max TWO visual text lines on desktop: a meta line (severity · request · domain · plane · urgency) and
// the title. The description is intentionally NOT rendered — in B8 the canonical description is only a
// restatement of the deadline (e.g. "Vencido há 57 dia(s)."), which already appears as the generated
// urgency; rendering it would duplicate the line and inflate row height (the visual defect being fixed).
// Navigation is gated strictly on the server's canNavigate + targetPath; anything else is a read-only row.

export function AlertRow({ alert, onNavigate }: { alert: DashboardV2AlertDto; onNavigate?: () => void }) {
  const sev = alertSeverityMeta(alert.severity);
  const plane = alertPlaneMeta(alert.plane);
  const clickable = alert.canNavigate && !!alert.targetPath;
  const Icon = sev.isCritical ? AlertOctagon : AlertTriangle;

  const inner: ReactNode = (
    <>
      <Icon size={16} aria-hidden style={{ color: sev.color, flexShrink: 0, marginTop: 1 }} />
      <div style={{ flex: 1, minWidth: 0 }}>
        {/* Line 1 — meta; wraps cleanly at narrow widths (no horizontal body scroll). */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span style={{
            fontSize: '0.62rem', fontWeight: 700, letterSpacing: '0.03em', textTransform: 'uppercase',
            color: sev.color, border: `1px solid ${sev.color}`, borderRadius: 6, padding: '0 5px',
          }}>{sev.label}</span>
          <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>
            {alert.requestNumber}
          </span>
          <span style={{
            fontSize: '0.64rem', fontWeight: 600, color: 'var(--color-text-muted)',
            backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
            borderRadius: 6, padding: '0 6px',
          }}>{alertDomainLabel(alert.domain)}</span>
          <span style={{
            fontSize: '0.64rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
            color: plane.color, backgroundColor: `${plane.color}1A`, borderRadius: 999, padding: '0 7px',
          }}>{plane.label}</span>
          <span style={{ fontSize: '0.74rem', fontWeight: 600, color: sev.color, whiteSpace: 'nowrap', marginLeft: 'auto' }}>
            {alertUrgencyText(alert.daysDelta)}
          </span>
        </div>
        {/* Line 2 — title (single line, ellipsized). */}
        <div style={{ marginTop: 2, fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text-main)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {alert.title}
        </div>
      </div>
      {clickable && <ChevronRight size={15} aria-hidden style={{ color: 'var(--color-text-muted)', flexShrink: 0, alignSelf: 'center' }} />}
    </>
  );

  const baseStyle: React.CSSProperties = {
    display: 'flex', alignItems: 'flex-start', gap: 10, width: '100%', textAlign: 'left',
    padding: '9px 14px', borderBottom: '1px solid var(--color-border)',
  };

  if (clickable) {
    return (
      <Link
        to={alert.targetPath!}
        onClick={onNavigate}
        className="alert-row-link"
        style={{ ...baseStyle, cursor: 'pointer', color: 'inherit', textDecoration: 'none' }}
      >
        {inner}
      </Link>
    );
  }
  return <div style={baseStyle} aria-disabled="true">{inner}</div>;
}
