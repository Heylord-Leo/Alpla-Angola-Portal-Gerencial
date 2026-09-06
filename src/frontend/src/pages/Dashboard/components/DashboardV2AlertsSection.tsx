import { useState } from 'react';
import { api } from '../../../lib/api';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import { AlertRow } from './AlertRow';
import { AlertsDrawer } from './AlertsDrawer';
import { alertPreviewFooterText } from '../alertsView';

// Dashboard V2 B8 (+ B8.2a compaction) — canonical Alerts ("Atenção Necessária"). The Dashboard is a
// SUMMARY surface: it shows compact severity counts and a PREVIEW of at most 6 highest-priority alerts,
// never the full (up to 100) list inline. Deep browsing opens an informational drawer. Every value comes
// from GET /api/dashboard/v2/alerts; this component recomputes nothing (no roles, no status/actionability,
// no urgency math beyond wording) and never re-sorts. `summary === null` (not entitled) → render nothing;
// entitled with zero alerts → the section stays visible with an honest empty state.

const PREVIEW_LIMIT = 6;

const sectionTitle: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 };

function StatPill({ value, label, color }: { value: number; label: string; color: string }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'baseline', gap: 5,
      backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
      borderRadius: 999, padding: '3px 11px',
    }}>
      <span style={{ width: 7, height: 7, borderRadius: '50%', backgroundColor: color, alignSelf: 'center' }} aria-hidden />
      <span style={{ fontSize: '0.95rem', fontWeight: 700, color: 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>{value}</span>
      <span style={{ fontSize: '0.72rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{label}</span>
    </span>
  );
}

export function DashboardV2AlertsSection() {
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getAlerts(signal));
  const [drawerOpen, setDrawerOpen] = useState(false);

  const header = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
      <h2 style={sectionTitle}>Atenção Necessária</h2>
      <SectionInfo {...DASHBOARD_SECTION_HELP.alerts} />
    </div>
  );

  if (status === 'loading') {
    return <section data-testid="dashboard-v2-alerts" aria-busy="true">{header}<DashboardSectionSkeleton label="Carregando alertas..." cards={3} /></section>;
  }
  if (status === 'error' || !data) {
    return <section data-testid="dashboard-v2-alerts">{header}<DashboardSectionError onRetry={retry} /></section>;
  }

  // Entitlement is encoded by the server: no summary → the user has no Alerts section.
  if (!data.summary) return null;

  const s = data.summary;
  const isEmpty = data.alerts.length === 0;
  // Presentation-only preview (server order preserved; no mutation, no re-sort).
  const preview = data.alerts.slice(0, PREVIEW_LIMIT);
  const showViewAll = s.totalAlertCount > preview.length;

  return (
    <section data-testid="dashboard-v2-alerts">
      {header}

      {isEmpty ? (
        <div style={{
          backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
          borderRadius: 12, padding: '20px',
        }}>
          <div style={{ fontSize: '0.95rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
            Não há alertas ativos no seu escopo.
          </div>
          <div style={{ marginTop: 6, fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
            Isso não significa que não haja trabalho — consulte as filas operacionais acima.
          </div>
        </div>
      ) : (
        <>
          {/* Compact summary badges — full population counts (never inferred from the preview). */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 10 }}>
            <StatPill value={s.criticalCount} label="críticos" color="#dc2626" />
            <StatPill value={s.attentionCount} label="em atenção" color="#d97706" />
            <StatPill value={s.totalAlertCount} label="ativos" color="var(--color-text-muted)" />
          </div>

          {/* Preview list — at most 6 rows; the full list lives in the drawer. */}
          <style>{`
            .alert-row-link:focus-visible { outline: 2px solid var(--color-text-main); outline-offset: -2px; }
            .alert-row-link:hover { background-color: var(--color-bg-page); }
          `}</style>
          <div style={{
            backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
            borderRadius: 12, overflow: 'hidden',
          }}>
            {preview.map((a) => <AlertRow key={a.id} alert={a} />)}
          </div>

          {/* Concise preview footer: honest "N of total" + the drawer affordance. */}
          {showViewAll && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', marginTop: 8 }}>
              <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                {alertPreviewFooterText(preview.length, s.totalAlertCount)}
              </span>
              <button
                type="button"
                onClick={() => setDrawerOpen(true)}
                style={{
                  font: 'inherit', fontSize: '0.8rem', fontWeight: 600, cursor: 'pointer',
                  color: 'var(--color-text-main)', backgroundColor: 'var(--color-bg-page)',
                  border: '1px solid var(--color-border)', borderRadius: 8, padding: '6px 12px',
                }}
              >
                Ver todos os alertas
              </button>
            </div>
          )}
        </>
      )}

      {drawerOpen && <AlertsDrawer alerts={data.alerts} summary={s} onClose={() => setDrawerOpen(false)} />}
    </section>
  );
}
