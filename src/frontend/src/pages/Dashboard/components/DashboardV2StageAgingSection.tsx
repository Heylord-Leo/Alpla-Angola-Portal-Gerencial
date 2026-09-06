import { api } from '../../../lib/api';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import type { DashboardV2StageAgingStageDto } from '../../../types/dashboardV2';
import { entityCountText, oldestAgeText, compositionText, compositionSegments, rankByBottleneck, isThresholded, hasMeaningfulComposition, type SegmentTone } from '../stageAgingView';

// Dashboard V2 B9.5 — canonical "Gargalos do Processo" (Stage Aging, GERENCIAL, read-only). Renders the
// server's per-stage time-in-CURRENT-STAGE analytics from GET /api/dashboard/v2/stage-aging. It never
// re-derives severity, never computes age, never coerces null severity/age to 0, and never uses request-age
// wording. `summary === null` (not entitled) → render nothing. Rows are read-only (server canNavigate=false).

const GERENCIAL = { label: 'Gerencial', color: '#3b5069' };

const sectionTitle: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 };

// Tone → color. Severity uses the established Dashboard palette; unknown is a theme-adaptive neutral.
function toneColor(tone: SegmentTone): string {
  switch (tone) {
    case 'critical': return '#dc2626';
    case 'attention': return '#d97706';
    case 'normal': return '#2f6f4f';
    case 'known': return '#3b5069';
    default: return 'var(--color-text-muted)'; // unknown — neutral, distinct
  }
}

function StatPill({ value, label, color }: { value: number; label: string; color: string }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'baseline', gap: 5,
      backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 999, padding: '3px 11px',
    }}>
      <span style={{ width: 7, height: 7, borderRadius: '50%', backgroundColor: color, alignSelf: 'center' }} aria-hidden />
      <span style={{ fontSize: '0.95rem', fontWeight: 700, color: 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>{value}</span>
      <span style={{ fontSize: '0.72rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{label}</span>
    </span>
  );
}

export function DashboardV2StageAgingSection() {
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getStageAging(signal));

  const header = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
      <h2 style={sectionTitle}>Gargalos do Processo</h2>
      <span style={{
        fontSize: '0.68rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
        color: GERENCIAL.color, backgroundColor: `${GERENCIAL.color}1A`, borderRadius: 999, padding: '2px 9px',
      }}>{GERENCIAL.label}</span>
      <SectionInfo {...DASHBOARD_SECTION_HELP.stageAging} />
    </div>
  );

  if (status === 'loading') {
    return <section data-testid="dashboard-v2-stage-aging" aria-busy="true">{header}<DashboardSectionSkeleton label="Carregando gargalos..." cards={4} /></section>;
  }
  if (status === 'error' || !data) {
    return <section data-testid="dashboard-v2-stage-aging">{header}<DashboardSectionError onRetry={retry} /></section>;
  }

  // Entitlement is encoded by the server: no summary → the user has no Stage Aging section.
  if (!data.summary) return null;

  const s = data.summary;
  const ranked = rankByBottleneck(data.stages); // risk-first display; server array not mutated

  return (
    <section data-testid="dashboard-v2-stage-aging">
      {header}

      {s.totalActiveEntities === 0 ? (
        <div style={{
          backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
          borderRadius: 12, padding: '18px', color: 'var(--color-text-muted)', fontSize: '0.9rem',
        }}>
          Não há etapas com medição de permanência ativa no seu escopo.
        </div>
      ) : (
        <>
          {/* Compact summary — risk first, unknown visible but secondary. From summary counts, not rows. */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 10 }}>
            <StatPill value={s.criticalEntities} label="críticos" color="#dc2626" />
            <StatPill value={s.attentionEntities} label="em atenção" color="#d97706" />
            <StatPill value={s.unknownAgeEntities} label="sem idade disponível" color="var(--color-text-muted)" />
          </div>

          <div style={{
            backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
            borderRadius: 12, overflow: 'hidden',
          }}>
            {ranked.map((stage) => <StageRow key={stage.stageCode} stage={stage} />)}
          </div>
        </>
      )}
    </section>
  );
}

function StageRow({ stage }: { stage: DashboardV2StageAgingStageDto }) {
  const segments = compositionSegments(stage);
  const total = stage.entityCount || 1;
  return (
    <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12, flexWrap: 'wrap', padding: '11px 16px', borderBottom: '1px solid var(--color-border)' }}
      aria-disabled="true">
      {/* Left: stage identity + grain/request count */}
      <div style={{ flex: '1 1 180px', minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{stage.label}</span>
          <span style={{
            fontSize: '0.62rem', fontWeight: 600, letterSpacing: '0.03em', textTransform: 'uppercase',
            color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
            borderRadius: 6, padding: '0 6px',
          }}>{stage.domain}</span>
        </div>
        <div style={{ marginTop: 2, fontSize: '0.76rem', color: 'var(--color-text-muted)' }}>{entityCountText(stage)}</div>
      </div>

      {/* Right: composition bar (only when it carries severity meaning) + text + oldest age */}
      <div style={{ flex: '1 1 220px', minWidth: 0 }}>
        {/* Bar is rendered ONLY for threshold-enabled stages with classified entities — never a solid
            all-unknown or thresholdless "known" band (severity color must mean something). */}
        {hasMeaningfulComposition(stage) && (
          <div style={{ display: 'flex', height: 6, borderRadius: 3, overflow: 'hidden', backgroundColor: 'var(--color-bg-page)' }} aria-hidden>
            {segments.map((seg, i) => (
              <div key={i} style={{ width: `${(seg.count / total) * 100}%`, backgroundColor: toneColor(seg.tone) }} />
            ))}
          </div>
        )}
        <div style={{ marginTop: 4, fontSize: '0.78rem', color: 'var(--color-text-main)' }}>
          {compositionText(stage)}
        </div>
        <div style={{ marginTop: 1, fontSize: '0.74rem', color: stage.oldestAgeDays === null ? 'var(--color-text-muted)' : 'var(--color-text-main)' }}>
          {oldestAgeText(stage.oldestAgeDays)}
          {!isThresholded(stage) && stage.oldestAgeDays !== null && (
            <span style={{ color: 'var(--color-text-muted)' }}> · sem classificação de severidade</span>
          )}
        </div>
      </div>
    </div>
  );
}
