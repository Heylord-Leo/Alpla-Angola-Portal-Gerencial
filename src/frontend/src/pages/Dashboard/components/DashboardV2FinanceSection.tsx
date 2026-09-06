import { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Info } from 'lucide-react';
import { api } from '../../../lib/api';
import { ModernTooltip } from '../../../components/ui/ModernTooltip';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import type { FinanceSharedQueueSummaryDto } from '../../../types/dashboardV2';
import { financePaymentsHref, type FinanceDrillKey } from '../dashboardV2View';

// Dashboard V2 — Finance section (slice B3.2). Operational counts only (no money — that is B7).
// The server encodes entitlement: `shared` (Finance role) renders operational cards that drill into
// /finance/payments; `managerial` (Local Manager/SysAdmin without Finance) renders identical counts
// view-only (no navigation). This component performs NO role logic and NO count recomputation.

const PLANE = {
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

// Severity → token-based colors (dark-mode safe; defined tokens only).
type Tone = 'neutral' | 'attention' | 'critical' | 'muted';
function toneColor(tone: Tone): string {
  switch (tone) {
    case 'critical': return '#dc2626';
    case 'attention': return '#d97706';
    case 'muted': return 'var(--color-text-muted)';
    default: return 'var(--color-text-main)';
  }
}

interface CardDef {
  key: string;
  label: string;
  value: number;
  secondary?: string;      // e.g. "N pedidos"
  tone: Tone;
  tooltip: string;
  drill?: FinanceDrillKey; // present → operational (clickable) when in the shared plane
}

const sectionTitle: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 };

function buildCards(s: FinanceSharedQueueSummaryDto): CardDef[] {
  return [
    { key: 'actionable', label: 'Grupos acionáveis', value: s.actionableGroups, secondary: `${s.actionableRequests} pedido${s.actionableRequests === 1 ? '' : 's'}`, tone: 'neutral', drill: 'actionable',
      tooltip: 'Grupos de P.O. com pelo menos uma ação disponível para Finanças no momento.' },
    { key: 'needsScheduling', label: 'Para agendar', value: s.needsSchedulingGroups, tone: 'neutral', drill: 'needsScheduling',
      tooltip: 'Grupos que estão prontos para o agendamento de pagamento.' },
    { key: 'needsPayment', label: 'Pagamentos a confirmar', value: s.needsPaymentGroups, tone: 'neutral', drill: 'needsPayment',
      tooltip: 'Grupos com pagamento que requer ação de Finanças, incluindo pagamentos já agendados quando ainda há ação disponível.' },
    { key: 'dueToday', label: 'Vencem hoje', value: s.dueTodayGroups, tone: 'attention', drill: 'dueToday',
      tooltip: 'Grupos com obrigação financeira aberta e data de pagamento agendada para hoje.' },
    { key: 'overdue', label: 'Agendamentos vencidos', value: s.overdueGroups, tone: 'critical', drill: 'overdue',
      tooltip: 'Grupos com obrigação financeira ainda aberta cuja data de pagamento agendada já passou.' },
    { key: 'paidWaitingReceiving', label: 'Pagos / aguardando recebimento', value: s.paidWaitingReceivingGroups, tone: 'muted', drill: 'paidWaitingReceiving',
      tooltip: 'Grupos com pagamento concluído que já saíram da fila operacional de Finanças e aguardam a etapa seguinte.' },
  ];
}

function isEmpty(s: FinanceSharedQueueSummaryDto): boolean {
  return s.actionableGroups === 0 && s.paidWaitingReceivingGroups === 0
    && s.dueTodayGroups === 0 && s.overdueGroups === 0;
}

export function DashboardV2FinanceSection() {
  const navigate = useNavigate();
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getFinance(signal));

  // The plane label is data-dependent, so the loading/error shell uses a neutral title (no false chip).
  const loadingHeader = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
      <h2 style={sectionTitle}>Finanças</h2>
      <SectionInfo {...DASHBOARD_SECTION_HELP.finance} />
    </div>
  );
  if (status === 'loading') {
    return <section data-testid="dashboard-v2-finance" aria-busy="true">{loadingHeader}<DashboardSectionSkeleton label="Carregando fila financeira..." cards={5} /></section>;
  }
  if (status === 'error') {
    return <section data-testid="dashboard-v2-finance">{loadingHeader}<DashboardSectionError onRetry={retry} /></section>;
  }
  if (!data) return null;

  // Entitlement comes from the server: prefer the operational Shared plane when present.
  const operational = !!data.shared;
  const summary = data.shared ?? data.managerial ?? null;
  if (!summary) return null; // user has no Finance section

  const cards = buildCards(summary);
  const emptyMessage = operational
    ? 'Não há ações financeiras pendentes no momento.'
    : 'Não há pendências financeiras no escopo atual.';
  const showEmpty = isEmpty(summary);

  return (
    <section data-testid="dashboard-v2-finance">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
        <h2 style={sectionTitle}>{operational ? 'Fila compartilhada — Finanças' : 'Visão gerencial — Finanças'}</h2>
        <Pill kind={operational ? 'compartilhado' : 'gerencial'} />
        {!operational && <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>Visão gerencial</span>}
        <SectionInfo {...DASHBOARD_SECTION_HELP.finance} />
      </div>

      {showEmpty && summary.paidWaitingReceivingGroups === 0 ? (
        <div style={{
          backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
          borderRadius: 12, padding: '20px', color: 'var(--color-text-muted)', fontSize: '0.9rem',
        }}>{emptyMessage}</div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 12 }}>
          {cards.map((c) => (
            <FinanceCard
              key={c.key}
              card={c}
              operational={operational}
              onOpen={operational && c.drill ? () => navigate(financePaymentsHref(c.drill!)) : undefined}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function FinanceCard({ card, operational, onOpen }: { card: CardDef; operational: boolean; onOpen?: () => void }) {
  const clickable = !!onOpen;
  const color = toneColor(card.tone);
  const inner: ReactNode = (
    <>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
        <span style={{ fontSize: '0.72rem', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)' }}>
          {card.label}
        </span>
        <ModernTooltip
          side="top" align="end" openOnClick maxWidth={260}
          ariaLabel={`${card.label} — informação`}
          content={<span style={{ fontSize: '0.8rem', lineHeight: 1.45, color: 'var(--color-text-main)' }}>{card.tooltip}</span>}
        >
          <span style={{ display: 'inline-flex', cursor: 'help' }}><Info size={12} aria-hidden style={{ opacity: 0.6 }} /></span>
        </ModernTooltip>
      </div>
      <div style={{ marginTop: 6, fontSize: '1.6rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color }}>
        {card.value}
      </div>
      {card.secondary && (
        <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: 2 }}>{card.secondary}</div>
      )}
    </>
  );

  const baseStyle: React.CSSProperties = {
    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
    borderRadius: 12, padding: '14px 16px', textAlign: 'left', width: '100%',
    opacity: card.tone === 'muted' ? 0.85 : 1,
  };

  // Operational (Finance-entitled) → real button (keyboard + focus). Managerial → plain div, no
  // interactive semantics, no navigation.
  if (clickable) {
    return (
      <button
        type="button"
        onClick={onOpen}
        style={{ ...baseStyle, cursor: 'pointer', font: 'inherit', color: 'inherit', display: 'block' }}
      >
        {inner}
      </button>
    );
  }
  return <div style={baseStyle} aria-disabled="true">{inner}</div>;
}
