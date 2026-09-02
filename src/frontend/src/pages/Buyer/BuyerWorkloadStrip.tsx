import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import type { DashboardV2BuyerSectionDto } from '../../types/dashboardV2';
import { displayWorkloadRows } from '../Dashboard/dashboardV2View';

// Compact Buyer workload distribution for the Buyer queue screen (Dashboard V2 slice B2).
// Renders the SAME server workload summary as the dashboard (managerial visibility only — when the
// user has no workload plane the server returns null and this renders nothing). Clicking a chip
// filters the list; counts are never recomputed on the client.

interface Props {
  company?: number;
  plant?: number;
  department?: number;
  needLevel?: string;
  activeBuyerId?: string;      // currently-applied ?buyer=
  activeUnassigned?: boolean;  // currently-applied ?ownership=unassigned
  onSelectBuyer: (buyerId: string) => void;
  onSelectUnassigned: () => void;
  onClear: () => void;
}

export function BuyerWorkloadStrip(props: Props) {
  const { company, plant, department, needLevel, activeBuyerId, activeUnassigned } = props;
  const [data, setData] = useState<DashboardV2BuyerSectionDto | null>(null);

  useEffect(() => {
    let alive = true;
    api.dashboardV2.getBuyer({ company, plant, department, needLevel })
      .then((d) => { if (alive) setData(d); })
      .catch(() => { if (alive) setData(null); });
    return () => { alive = false; };
  }, [company, plant, department, needLevel]);

  const workload = data?.workload;
  if (!workload) return null;
  const rows = displayWorkloadRows(workload.rows);
  if (rows.length === 0 && !workload.unassigned) return null;

  const anyActive = !!activeBuyerId || !!activeUnassigned;

  return (
    <div data-testid="buyer-workload-strip" style={{
      display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center',
      padding: '10px 12px', border: '1px solid var(--color-border)', borderRadius: 10,
      backgroundColor: 'var(--color-bg-surface)', marginBottom: 12,
    }}>
      <span style={{ fontSize: '0.72rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', marginRight: 4 }}>
        Carga por comprador
      </span>

      {workload.unassigned && (
        <Chip
          label="Sem atribuição"
          count={workload.unassigned.actionableRequests}
          active={!!activeUnassigned}
          accent="#a15c1e"
          onClick={props.onSelectUnassigned}
        />
      )}

      {rows.map((r) => (
        <Chip
          key={r.buyerId || r.buyerName || 'b'}
          label={r.buyerName || '—'}
          count={r.actionableRequests}
          active={!!activeBuyerId && activeBuyerId === r.buyerId}
          onClick={() => r.buyerId && props.onSelectBuyer(r.buyerId)}
        />
      ))}

      {anyActive && (
        <button onClick={props.onClear} style={{
          fontSize: '0.75rem', color: 'var(--color-text-muted)', background: 'none',
          border: '1px solid var(--color-border)', borderRadius: 999, padding: '3px 10px', cursor: 'pointer',
        }}>Limpar filtro de comprador</button>
      )}
    </div>
  );
}

function Chip({ label, count, active, accent, onClick }: {
  label: string; count: number; active: boolean; accent?: string; onClick: () => void;
}) {
  const color = accent || '#0e7490';
  return (
    <button
      onClick={onClick}
      aria-pressed={active}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 6, cursor: 'pointer',
        fontSize: '0.8rem', fontWeight: 600, padding: '4px 10px', borderRadius: 999,
        border: `1px solid ${active ? color : 'var(--color-border)'}`,
        backgroundColor: active ? `${color}1A` : 'var(--color-bg-elevated, var(--color-bg-surface))',
        color: active ? color : 'var(--color-text-main)',
      }}
    >
      <span>{label}</span>
      <span style={{ fontVariantNumeric: 'tabular-nums', color: active ? color : 'var(--color-text-muted)' }}>{count}</span>
    </button>
  );
}
