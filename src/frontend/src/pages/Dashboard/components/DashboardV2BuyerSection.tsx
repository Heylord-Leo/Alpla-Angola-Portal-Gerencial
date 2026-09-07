import { useNavigate } from 'react-router-dom';
import { Info } from 'lucide-react';
import { api } from '../../../lib/api';
import { ModernTooltip } from '../../../components/ui/ModernTooltip';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import type { BuyerWorkloadRowDto } from '../../../types/dashboardV2';
import {
  hasPersonalWork,
  hasSharedWork,
  personalAttention,
  buyerQueueHref,
  workloadValue,
  workloadColumnMaxes,
  barPercent,
  displayWorkloadRows,
  type WorkloadMetricKey,
} from '../dashboardV2View';

// Column definitions drive both the header tooltips and the in-cell bars. Each column keeps its own
// independent scale (see workloadColumnMaxes) and its own semantic color family. `color` uses a
// low-saturation tint behind the value so both themes stay readable.
interface ColDef {
  key: WorkloadMetricKey;
  header: string;
  color: string;   // bar fill (rendered at low opacity)
  title: string;
  body: string;
  unit: string;
}
const WORKLOAD_COLUMNS: ColDef[] = [
  { key: 'assigned', header: 'Atribuídos', color: '#3b82f6',
    title: 'Pedidos atribuídos',
    body: 'Total de pedidos ativos pertencentes à carteira deste comprador dentro do escopo atual.',
    unit: 'pedidos' },
  { key: 'actionable', header: 'Acionáveis', color: '#0e9aa7',
    title: 'Pedidos acionáveis',
    body: 'Pedidos em que existe pelo menos uma ação de Compras disponível agora, como adicionar cotação, enviar itens prontos para aprovação ou resolver um reajuste.',
    unit: 'pedidos' },
  { key: 'pending', header: 'Itens pendentes', color: '#d97706',
    title: 'Itens pendentes',
    body: 'Soma dos itens que ainda precisam de tratamento de cotação ou cobertura nos pedidos deste comprador.',
    unit: 'itens' },
  { key: 'ready', header: 'Itens prontos', color: '#16a34a',
    title: 'Itens prontos',
    body: 'Soma dos itens já cobertos e disponíveis para seguir em lote para o fluxo de aprovação.',
    unit: 'itens' },
  { key: 'attention', header: 'Atenção', color: '#dc2626',
    title: 'Requer atenção',
    body: 'Pedidos acionáveis de Compras cuja data de necessidade está hoje ou já venceu. Um pedido só entra aqui enquanto ainda existir uma ação de Compras aberta.',
    unit: 'pedidos' },
];

const BUYER_COL = {
  title: 'Comprador',
  body: "Responsável pela carteira. Agrupa os pedidos ativos pelo comprador atribuído. 'Sem atribuição' representa pedidos que ainda não possuem comprador.",
};

// Dashboard V2 — Buyer section (slice B1+B2). Three planes, always labelled so a shared queue can
// never read as personal work. All counts are server-calculated; this component only renders.

const PLANE: Record<string, { label: string; color: string }> = {
  pessoal: { label: 'Pessoal', color: '#0e7490' },
  compartilhado: { label: 'Compartilhado', color: '#a15c1e' },
  gerencial: { label: 'Gerencial', color: '#3b5069' },
};

function Pill({ kind }: { kind: keyof typeof PLANE }) {
  const p = PLANE[kind];
  return (
    <span style={{
      fontSize: '0.68rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
      color: p.color, backgroundColor: `${p.color}1A`, borderRadius: 999, padding: '2px 9px',
    }}>{p.label}</span>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 92 }}>
      <span style={{ fontSize: '1.35rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)' }}>{value}</span>
      <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>{label}</span>
    </div>
  );
}

const sectionTitle: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 };
const card: React.CSSProperties = {
  backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
  borderRadius: 12, padding: '16px 18px', cursor: 'pointer',
};

export function DashboardV2BuyerSection() {
  const navigate = useNavigate();
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getBuyer(undefined, signal));

  // Buyer has data-dependent planes (Pessoal/Compartilhado/Gerencial), so the loading/error shell uses a
  // neutral "Compras" title and never renders false planes before the response is known.
  const loadingHeader = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
      <h2 style={sectionTitle}>Compras</h2>
    </div>
  );
  if (status === 'loading') {
    return <div data-testid="dashboard-v2-buyer" aria-busy="true">{loadingHeader}<DashboardSectionSkeleton label="Carregando fila de Compras..." cards={4} /></div>;
  }
  if (status === 'error' || !data) {
    return <div data-testid="dashboard-v2-buyer">{loadingHeader}<DashboardSectionError onRetry={retry} /></div>;
  }

  const { personal, shared, workload } = data;
  const showPersonal = hasPersonalWork(personal);
  const showShared = hasSharedWork(shared);
  const rows = displayWorkloadRows(workload?.rows);
  const showWorkload = !!workload && (rows.length > 0 || !!workload.unassigned);

  if (!showPersonal && !showShared && !showWorkload) return null;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }} data-testid="dashboard-v2-buyer">
      {/* ── PESSOAL ── */}
      {showPersonal && personal && (
        <section>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
            <h2 style={sectionTitle}>Minha Operação — Compras</h2><Pill kind="pessoal" /><SectionInfo {...DASHBOARD_SECTION_HELP.buyerPersonal} />
          </div>
          <div
            style={{ ...card, borderLeft: `3px solid ${PLANE.pessoal.color}`, display: 'flex', gap: 28, flexWrap: 'wrap' }}
            onClick={() => navigate(buyerQueueHref({ ownership: 'me' }))}
            role="button" tabIndex={0}
            onKeyDown={(e) => { if (e.key === 'Enter') navigate(buyerQueueHref({ ownership: 'me' })); }}
          >
            <Metric label="Atribuídos" value={personal.assignedRequests} />
            <Metric label="Acionáveis" value={personal.actionableRequests} />
            <Metric label="Itens pendentes" value={personal.pendingQuotationItems} />
            <Metric label="Itens prontos" value={personal.readyForBatchItems} />
            <Metric label="Atenção" value={personalAttention(personal)} />
          </div>
        </section>
      )}

      {/* ── COMPARTILHADO ── */}
      {showShared && shared && (
        <section>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
            <h2 style={sectionTitle}>Fila compartilhada de Compras</h2><Pill kind="compartilhado" /><SectionInfo {...DASHBOARD_SECTION_HELP.buyerShared} />
          </div>
          <div
            style={{ ...card, borderLeft: `3px solid ${PLANE.compartilhado.color}`, display: 'flex', gap: 28, flexWrap: 'wrap' }}
            onClick={() => navigate(buyerQueueHref({ ownership: 'unassigned' }))}
            role="button" tabIndex={0}
            onKeyDown={(e) => { if (e.key === 'Enter') navigate(buyerQueueHref({ ownership: 'unassigned' })); }}
          >
            <Metric label="Sem comprador" value={shared.unassignedRequests} />
            <Metric label="Acionáveis" value={shared.unassignedActionableRequests} />
            <Metric label="Itens pendentes" value={shared.unassignedPendingItems} />
            <Metric label="Itens prontos" value={shared.unassignedReadyItems} />
          </div>
        </section>
      )}

      {/* ── GERENCIAL ── */}
      {showWorkload && workload && (() => {
        // Per-column scale spans every displayed row, including the pinned unassigned bucket.
        const scaleRows = [...(workload.unassigned ? [workload.unassigned] : []), ...rows];
        const maxes = workloadColumnMaxes(scaleRows);
        return (
          <section>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
              <h2 style={sectionTitle}>Carga da Equipe de Compras</h2><Pill kind="gerencial" /><SectionInfo {...DASHBOARD_SECTION_HELP.buyerWorkload} />
            </div>
            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 12, overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.875rem', minWidth: 620 }}>
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)' }}>
                    <HeaderCell align="left" title={BUYER_COL.title} body={BUYER_COL.body}>Comprador</HeaderCell>
                    {WORKLOAD_COLUMNS.map((c) => (
                      <HeaderCell key={c.key} align="right" title={c.title} body={c.body} unit={c.unit}>{c.header}</HeaderCell>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {workload.unassigned && (
                    <WorkloadRow key="unassigned" name="Sem atribuição" row={workload.unassigned} maxes={maxes} pinned
                      onClick={() => navigate(buyerQueueHref({ ownership: 'unassigned' }))} />
                  )}
                  {rows.map((r) => (
                    <WorkloadRow key={r.buyerId || r.buyerName || 'row'} name={r.buyerName || '—'} row={r} maxes={maxes}
                      onClick={() => r.buyerId && navigate(buyerQueueHref({ buyerId: r.buyerId }))} />
                  ))}
                  {rows.length === 0 && !workload.unassigned && (
                    <tr><td colSpan={6} style={{ padding: 20, textAlign: 'center', color: 'var(--color-text-muted)' }}>Sem carga atribuída.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        );
      })()}
    </div>
  );
}

function HeaderCell({ children, align, title, body, unit }: {
  children: React.ReactNode; align: 'left' | 'right'; title: string; body: string; unit?: string;
}) {
  return (
    <th style={{ textAlign: align, padding: '10px 14px', fontSize: '0.72rem', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>
      <ModernTooltip
        side="top"
        align={align === 'right' ? 'end' : 'start'}
        openOnClick
        maxWidth={280}
        ariaLabel={`${title} — informação`}
        content={(
          // Explicit theme tokens (not inherited) so the body/title stay high-contrast in dark mode;
          // the tooltip surface itself is --color-bg-surface (set by ModernTooltip), so text-main sits
          // on the correct ground in both themes. "Unidade" uses the muted token: readable but secondary.
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, textAlign: 'left', textTransform: 'none', letterSpacing: 'normal', fontWeight: 400, color: 'var(--color-text-main)' }}>
            <strong style={{ fontSize: '0.82rem', color: 'var(--color-text-main)' }}>{title}</strong>
            <span style={{ fontSize: '0.8rem', lineHeight: 1.45, color: 'var(--color-text-main)' }}>{body}</span>
            {unit && <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>Unidade: {unit}</span>}
          </div>
        )}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, cursor: 'help', justifyContent: align === 'right' ? 'flex-end' : 'flex-start' }}>
          {children}
          <Info size={12} aria-hidden style={{ opacity: 0.6, flexShrink: 0 }} />
        </span>
      </ModernTooltip>
    </th>
  );
}

function WorkloadRow({ name, row, maxes, pinned, onClick }: {
  name: string;
  row: BuyerWorkloadRowDto;
  maxes: Record<WorkloadMetricKey, number>;
  pinned?: boolean;
  onClick: () => void;
}) {
  return (
    <tr
      style={{ borderBottom: '1px solid var(--color-border)', cursor: 'pointer', backgroundColor: pinned ? 'rgba(161,92,30,0.06)' : undefined }}
      onClick={onClick}
      role="button" tabIndex={0}
      onKeyDown={(e) => { if (e.key === 'Enter') onClick(); }}
    >
      <td style={{ padding: '10px 14px', fontWeight: pinned ? 700 : 500, color: 'var(--color-text-main)' }}>
        {pinned ? <span style={{ color: '#a15c1e' }}>{name}</span> : name}
      </td>
      {WORKLOAD_COLUMNS.map((c) => {
        const value = workloadValue(row, c.key);
        const pct = barPercent(value, maxes[c.key]);
        return (
          <td key={c.key} style={{ padding: '10px 14px', position: 'relative' }}>
            {/* Decorative proportional bar (per-column scale). aria-hidden — the value is the real datum. */}
            {pct > 0 && (
              <div aria-hidden style={{
                position: 'absolute', right: 8, top: '50%', transform: 'translateY(-50%)',
                height: 20, width: `calc(${pct}% - 12px)`, minWidth: 2, maxWidth: 'calc(100% - 12px)',
                backgroundColor: c.color, opacity: 0.16, borderRadius: 4, pointerEvents: 'none',
              }} />
            )}
            <span style={{ position: 'relative', display: 'block', textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)', fontWeight: 600 }}>
              {value}
            </span>
          </td>
        );
      })}
    </tr>
  );
}
