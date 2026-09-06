import { useNavigate } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';
import { api } from '../../../lib/api';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import type { PersonalActionDto } from '../../../types/dashboardV2';

// Dashboard V2 B5 — "Minha Operação" (PESSOAL). Renders ONLY the server's canonical personal actions
// (assigned Buyer work, owned Area approval, own drafts). It recomputes nothing: no role/status
// actionability and no due-date urgency (the server exposes no date buckets in B5). Shared role work
// never appears here. The section stays visible even when empty — an honest "nothing assigned to you"
// is the correct result for a Finance-only / Receiving-only user (their work lives in the shared queues).

const PESSOAL = { label: 'Pessoal', color: '#2f6f4f' };

// Backend action codes → PT wording (labels only; backend constants are never renamed).
const ACTION_LABELS: Record<string, string> = {
  SUBMIT_DRAFT: 'Finalizar / enviar pedido',
  ADD_QUOTATION: 'Adicionar cotação',
  SUBMIT_BATCH: 'Enviar para aprovação',
  RESOLVE_ADJUSTMENT: 'Resolver reajuste',
  AREA_APPROVAL: 'Aprovar pedido',
};

// Backend domain codes → PT labels. No label implies shared ownership.
const DOMAIN_LABELS: Record<string, string> = {
  BUYER: 'Compras',
  REQUESTER: 'Solicitante',
  APPROVAL: 'Aprovação de Área',
};

function domainLabel(code: string): string {
  return DOMAIN_LABELS[code] ?? code;
}
function actionLabel(code: string): string {
  return ACTION_LABELS[code] ?? code;
}

const sectionTitle: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 };

function PlanePill() {
  return (
    <span style={{
      fontSize: '0.68rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
      color: PESSOAL.color, backgroundColor: `${PESSOAL.color}1A`, borderRadius: 999, padding: '2px 9px',
    }}>{PESSOAL.label}</span>
  );
}

export function DashboardV2PersonalSection() {
  const navigate = useNavigate();
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getPersonal(signal));

  const header = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
      <h2 style={sectionTitle}>Minha Operação</h2>
      <PlanePill />
      <SectionInfo {...DASHBOARD_SECTION_HELP.personal} />
    </div>
  );

  if (status === 'loading') {
    return <section data-testid="dashboard-v2-personal" aria-busy="true">{header}<DashboardSectionSkeleton label="Carregando suas ações..." cards={3} /></section>;
  }
  if (status === 'error' || !data) {
    return <section data-testid="dashboard-v2-personal">{header}<DashboardSectionError onRetry={retry} /></section>;
  }

  const summary = data.summary;
  const isEmpty = summary.actionableActions === 0;

  return (
    <section data-testid="dashboard-v2-personal">
      {header}

      {isEmpty ? (
        <div style={{
          backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
          borderRadius: 12, padding: '20px',
        }}>
          <div style={{ fontSize: '0.95rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
            Nenhuma ação atribuída pessoalmente no momento.
          </div>
          <div style={{ marginTop: 6, fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
            Consulte as filas compartilhadas abaixo para atividades de Compras, Finanças e Recebimento.
          </div>
        </div>
      ) : (
        <>
          {/* Summary row */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 12, marginBottom: 12 }}>
            <div style={{
              backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
              borderRadius: 12, padding: '14px 16px',
            }}>
              <div style={{ fontSize: '0.72rem', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)' }}>
                Ações atribuídas
              </div>
              <div style={{ marginTop: 6, fontSize: '1.6rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)' }}>
                {summary.actionableActions}
              </div>
              <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                {summary.actionableRequests} pedido{summary.actionableRequests === 1 ? '' : 's'}
              </div>
            </div>

            {summary.byDomain.map((d) => (
              <div key={d.domain} style={{
                backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                borderRadius: 12, padding: '14px 16px',
              }}>
                <div style={{ fontSize: '0.72rem', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)' }}>
                  {domainLabel(d.domain)}
                </div>
                <div style={{ marginTop: 6, fontSize: '1.6rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)' }}>
                  {d.actions}
                </div>
                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                  {d.requests} pedido{d.requests === 1 ? '' : 's'}
                </div>
              </div>
            ))}
          </div>

          {/* Bounded action list */}
          <div style={{
            backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
            borderRadius: 12, overflow: 'hidden',
          }}>
            {data.actions.map((a) => (
              <PersonalActionRow key={`${a.domain}:${a.entityType}:${a.entityId}:${a.actionType}`}
                action={a}
                onOpen={a.targetPath ? () => navigate(a.targetPath!) : undefined}
              />
            ))}
          </div>
        </>
      )}
    </section>
  );
}

function PersonalActionRow({ action, onOpen }: { action: PersonalActionDto; onOpen?: () => void }) {
  const clickable = !!onOpen;
  const inner = (
    <>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>
            {action.requestNumber}
          </span>
          <span style={{
            fontSize: '0.68rem', fontWeight: 600, color: 'var(--color-text-muted)',
            backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
            borderRadius: 6, padding: '1px 6px',
          }}>
            {domainLabel(action.domain)}
          </span>
        </div>
        <div style={{ marginTop: 2, fontSize: '0.8rem', color: 'var(--color-text-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {action.title || '—'}
        </div>
      </div>
      <div style={{ fontSize: '0.78rem', fontWeight: 600, color: 'var(--color-text-main)', whiteSpace: 'nowrap' }}>
        {actionLabel(action.actionType)}
      </div>
      {clickable && <ChevronRight size={16} aria-hidden style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} />}
    </>
  );

  const baseStyle: React.CSSProperties = {
    display: 'flex', alignItems: 'center', gap: 12, width: '100%', textAlign: 'left',
    padding: '12px 16px', borderBottom: '1px solid var(--color-border)',
  };

  if (clickable) {
    return (
      <button type="button" onClick={onOpen} style={{ ...baseStyle, cursor: 'pointer', font: 'inherit', color: 'inherit', background: 'none', border: 'none', borderBottom: '1px solid var(--color-border)' }}>
        {inner}
      </button>
    );
  }
  return <div style={baseStyle} aria-disabled="true">{inner}</div>;
}
