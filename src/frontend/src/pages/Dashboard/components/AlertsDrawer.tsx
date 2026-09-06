import { useEffect, useRef } from 'react';
import { X } from 'lucide-react';
import type { DashboardV2AlertDto, DashboardV2AlertsSummaryDto } from '../../../types/dashboardV2';
import { alertSummaryText, alertBackendTruncationText } from '../alertsView';
import { AlertRow } from './AlertRow';

// Dashboard V2 B8.2a — informational drawer listing ALL alerts the server returned (bounded at 100). The
// Dashboard stays a summary surface; deep browsing lives here. This drawer does NOT refetch — it renders
// the alerts already loaded by the section, in server order (never re-sorted). Backend truncation is made
// explicit (summary.isTruncated): the header states "returned 100 of N", so the list is never implied
// complete. Filters are intentionally NOT offered here: with the server cap, a severity/domain filter
// could show zero rows for a class the summary still counts (e.g. the trailing ATTENTION alerts beyond the
// cap), which would be misleading — correctness over feature richness (a later slice may add server-side
// filtering). Accessibility follows the project drawer pattern plus Escape-to-close and focus management.

interface AlertsDrawerProps {
  alerts: DashboardV2AlertDto[];
  summary: DashboardV2AlertsSummaryDto;
  onClose: () => void;
}

export function AlertsDrawer({ alerts, summary, onClose }: AlertsDrawerProps) {
  const closeRef = useRef<HTMLButtonElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  // Escape closes; lock background scroll; move focus into the drawer and restore it on close.
  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeRef.current?.focus();

    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);

    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = previousOverflow;
      previouslyFocused.current?.focus?.();
    };
  }, [onClose]);

  return (
    <>
      <style>{`
        @keyframes alertsDrawerIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
        @media (prefers-reduced-motion: reduce) { .alerts-drawer-panel { animation: none !important; } }
        .alerts-drawer-close:focus-visible { outline: 2px solid var(--color-text-main); outline-offset: 2px; }
      `}</style>

      {/* Backdrop — click closes. Purely a dismiss target (no other interactivity). */}
      <div
        onClick={onClose}
        aria-hidden="true"
        style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.4)', zIndex: 'var(--z-drawer)' as unknown as number }}
      />

      {/* Panel */}
      <div
        className="alerts-drawer-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="alerts-drawer-title"
        style={{
          position: 'fixed', top: 0, right: 0, bottom: 0, width: 'min(560px, 96vw)',
          backgroundColor: 'var(--color-bg-surface)', borderLeft: '1px solid var(--color-border)',
          zIndex: 'calc(var(--z-drawer) + 1)' as unknown as number,
          display: 'flex', flexDirection: 'column', boxShadow: '-8px 0 30px rgba(0,0,0,0.15)',
          animation: 'alertsDrawerIn 0.25s ease-out',
        }}
      >
        {/* Header (fixed) */}
        <div style={{ padding: '16px 20px', borderBottom: '1px solid var(--color-border)', flexShrink: 0 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12 }}>
            <div style={{ minWidth: 0 }}>
              <h2 id="alerts-drawer-title" style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                Todos os alertas
              </h2>
              <div style={{ marginTop: 4, fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
                {alertSummaryText(summary.criticalCount, summary.attentionCount)}
              </div>
            </div>
            <button
              ref={closeRef}
              type="button"
              onClick={onClose}
              className="alerts-drawer-close"
              aria-label="Fechar"
              style={{
                background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)',
                padding: 6, borderRadius: 6, flexShrink: 0,
              }}
            >
              <X size={20} aria-hidden />
            </button>
          </div>

          {/* Backend truncation transparency — never imply all N were loaded. */}
          {summary.isTruncated && (
            <div style={{
              marginTop: 10, fontSize: '0.76rem', color: 'var(--color-text-muted)',
              backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
              borderRadius: 8, padding: '8px 10px',
            }}>
              {alertBackendTruncationText(summary.displayedAlertCount, summary.totalAlertCount)}
            </div>
          )}
        </div>

        {/* Scrollable list — scrolling happens INSIDE the drawer; the Dashboard behind stays fixed. */}
        <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
          <style>{`
            .alerts-drawer-panel .alert-row-link:focus-visible { outline: 2px solid var(--color-text-main); outline-offset: -2px; }
            .alerts-drawer-panel .alert-row-link:hover { background-color: var(--color-bg-page); }
          `}</style>
          {alerts.map((a) => <AlertRow key={a.id} alert={a} onNavigate={onClose} />)}
        </div>
      </div>
    </>
  );
}
