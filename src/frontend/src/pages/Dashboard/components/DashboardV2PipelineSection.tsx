import { useNavigate } from 'react-router-dom';
import { RefreshCw } from 'lucide-react';
import { api } from '../../../lib/api';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import type { OperationalPipelineStageDto } from '../../../types/dashboardV2';
import { groupStages, primaryEntityText, secondaryRequestText, isAdjustmentStage } from '../pipelineView';

// Dashboard V2 B6 — canonical Operational Pipeline (GERENCIAL, read-only). Replaces the legacy scalar
// request-level histogram. Every value comes from GET /api/dashboard/v2/pipeline; the frontend computes
// nothing (no status inspection, no membership, no counts). A request may appear in several stages, so the
// stage sum can exceed "Pedidos ativos" — the SectionInfo explains why.

const GERENCIAL = { label: 'Gerencial', color: '#3b5069' };

export function DashboardV2PipelineSection() {
  const navigate = useNavigate();
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getPipeline(signal));

  const header = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 4 }}>
      <h2 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 }}>Visão do Pipeline</h2>
      <span style={{
        fontSize: '0.68rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
        color: GERENCIAL.color, backgroundColor: `${GERENCIAL.color}1A`, borderRadius: 999, padding: '2px 9px',
      }}>{GERENCIAL.label}</span>
      <SectionInfo {...DASHBOARD_SECTION_HELP.pipeline} />
    </div>
  );

  if (status === 'loading') {
    return <section data-testid="dashboard-v2-pipeline" aria-busy="true">{header}<DashboardSectionSkeleton label="Carregando visão do processo..." cards={5} /></section>;
  }
  if (status === 'error' || !data) {
    return <section data-testid="dashboard-v2-pipeline">{header}<DashboardSectionError onRetry={retry} /></section>;
  }

  const groups = groupStages(data.stages);

  return (
    <section data-testid="dashboard-v2-pipeline">
      {header}

      {/* Headline: the distinct active-request denominator (NOT a stage sum). */}
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginBottom: 2 }}>
        <span style={{ fontSize: '0.72rem', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)' }}>
          Pedidos ativos
        </span>
        <span style={{ fontSize: '1.4rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)' }}>
          {data.uniqueActiveRequests}
        </span>
      </div>
      <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', margin: '0 0 14px 0', maxWidth: 640 }}>
        A soma das etapas pode exceder o total de pedidos ativos, pois um mesmo pedido pode possuir grupos ou lotes em etapas diferentes.
      </p>

      {/* Domain groups: wrap on narrow screens; the container scrolls, never the page body. */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, overflowX: 'auto' }}>
        {groups.map(({ group, stages }) => (
          <div key={group.key} style={{
            border: '1px solid var(--color-border)', borderRadius: 12, padding: '10px 12px',
            backgroundColor: 'var(--color-bg-page)', minWidth: 0,
          }}>
            <div style={{ fontSize: '0.68rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', marginBottom: 8 }}>
              {group.label}
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {stages.map((s) => (
                <PipelineStageCard
                  key={s.stage}
                  stage={s}
                  onOpen={s.targetPath ? () => navigate(s.targetPath!) : undefined}
                />
              ))}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function PipelineStageCard({ stage, onOpen }: { stage: OperationalPipelineStageDto; onOpen?: () => void }) {
  const clickable = !!onOpen;
  const secondary = secondaryRequestText(stage);
  const muted = stage.stage === 'COMPLETED'; // de-emphasize the (potentially large) terminal count

  const inner = (
    <>
      <div style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
        {isAdjustmentStage(stage) && <RefreshCw size={11} aria-hidden style={{ color: 'var(--color-text-muted)' }} />}
        <span style={{ fontSize: '0.72rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{stage.label}</span>
      </div>
      <div style={{ marginTop: 4, fontSize: '1.05rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: muted ? 'var(--color-text-muted)' : 'var(--color-text-main)' }}>
        {primaryEntityText(stage)}
      </div>
      {secondary && <div style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', marginTop: 1 }}>· {secondary}</div>}
    </>
  );

  const baseStyle: React.CSSProperties = {
    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
    borderRadius: 10, padding: '10px 12px', minWidth: 110, textAlign: 'left',
  };

  if (clickable) {
    return (
      <button type="button" onClick={onOpen} style={{ ...baseStyle, cursor: 'pointer', font: 'inherit', color: 'inherit', display: 'block' }}>
        {inner}
      </button>
    );
  }
  return <div style={baseStyle} aria-disabled="true">{inner}</div>;
}
