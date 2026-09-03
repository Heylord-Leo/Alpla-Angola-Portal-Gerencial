import { useEffect, useState, ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Info } from 'lucide-react';
import { api } from '../../../lib/api';
import { ModernTooltip } from '../../../components/ui/ModernTooltip';
import type { DashboardV2ReceivingSectionDto, ReceivingSharedQueueSummaryDto } from '../../../types/dashboardV2';
import { receivingWorkspaceHref, type ReceivingDrillKey } from '../dashboardV2View';

// Dashboard V2 — Receiving section (slice B4.2). Operational counts only (no aging, no money). The server
// encodes entitlement: `shared` (Receiving role) → operational cards drilling into the canonical
// group-level workspace; `managerial` (Local Manager/SysAdmin without Receiving) → identical counts,
// view-only, no navigation. No role logic and no count recomputation here.

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

type Tone = 'neutral' | 'muted';

interface CardDef {
  key: string;
  label: string;
  value: number;
  secondary?: string;
  tone: Tone;
  tooltip: string;
  drill?: ReceivingDrillKey;
}

const sectionTitle: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 };

function buildCards(s: ReceivingSharedQueueSummaryDto): CardDef[] {
  return [
    { key: 'actionable', label: 'Grupos acionáveis', value: s.actionableGroups, secondary: `${s.actionableRequests} pedido${s.actionableRequests === 1 ? '' : 's'}`, tone: 'neutral', drill: 'actionable',
      tooltip: 'Grupos de P.O. com pelo menos uma ação disponível para Recebimento no momento.' },
    { key: 'readyForReceipt', label: 'Entrada em recebimento', value: s.readyForReceiptGroups, tone: 'neutral', drill: 'readyForReceipt',
      tooltip: 'Grupos com pagamento concluído que já estão disponíveis para a etapa de Recebimento.' },
    { key: 'waitingReceipt', label: 'Aguardando recebimento', value: s.waitingReceiptGroups, tone: 'neutral', drill: 'waitingReceipt',
      tooltip: 'Grupos que já entraram na etapa de Recebimento e aguardam confirmação.' },
    { key: 'followUp', label: 'Acompanhamento parcial', value: s.followUpGroups, tone: 'muted', drill: 'followUp',
      tooltip: 'Grupos com recebimento parcial que permanecem em acompanhamento.' },
    { key: 'waitingSupplierDelivery', label: 'Aguardando fornecedor', value: s.waitingSupplierDeliveryGroups, tone: 'muted', drill: 'waitingSupplierDelivery',
      tooltip: 'Grupos que permanecem sob Recebimento aguardando a entrega do fornecedor.' },
  ];
}

export function DashboardV2ReceivingSection() {
  const navigate = useNavigate();
  const [data, setData] = useState<DashboardV2ReceivingSectionDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    api.dashboardV2.getReceiving()
      .then((d) => { if (alive) setData(d); })
      .catch(() => { if (alive) setData(null); }) // isolated: never breaks Buyer/Finance/legacy dashboard
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, []);

  if (loading || !data) return null;

  const operational = !!data.shared;
  const summary = data.shared ?? data.managerial ?? null;
  if (!summary) return null;

  const cards = buildCards(summary);
  const showEmpty = summary.actionableGroups === 0;
  const emptyMessage = operational
    ? 'Não há grupos pendentes de Recebimento no momento.'
    : 'Não há pendências de Recebimento no escopo atual.';

  return (
    <section data-testid="dashboard-v2-receiving">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
        <h2 style={sectionTitle}>{operational ? 'Fila compartilhada — Recebimento' : 'Visão gerencial — Recebimento'}</h2>
        <Pill kind={operational ? 'compartilhado' : 'gerencial'} />
        {!operational && <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>Visão gerencial</span>}
      </div>

      {showEmpty ? (
        <div style={{
          backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
          borderRadius: 12, padding: '20px', color: 'var(--color-text-muted)', fontSize: '0.9rem',
        }}>{emptyMessage}</div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 12 }}>
          {cards.map((c) => (
            <ReceivingCard
              key={c.key}
              card={c}
              onOpen={operational && c.drill ? () => navigate(receivingWorkspaceHref(c.drill!)) : undefined}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function ReceivingCard({ card, onOpen }: { card: CardDef; onOpen?: () => void }) {
  const clickable = !!onOpen;
  const color = card.tone === 'muted' ? 'var(--color-text-muted)' : 'var(--color-text-main)';
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
      <div style={{ marginTop: 6, fontSize: '1.6rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color }}>{card.value}</div>
      {card.secondary && <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: 2 }}>{card.secondary}</div>}
    </>
  );

  const baseStyle: React.CSSProperties = {
    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
    borderRadius: 12, padding: '14px 16px', textAlign: 'left', width: '100%',
    opacity: card.tone === 'muted' ? 0.85 : 1,
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
