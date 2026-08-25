import React, { useEffect, useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { api } from '../../lib/api';
import type { RequestTimelineDto, LotTimelineDto, TimelineStepDto } from '../../types';
import { stepStateMeta, clampIndex, formatLotAmount, timelineStepTimestamp } from './buyerWorkspaceView';

/**
 * Buyer Workspace lot timeline (Phase 3A.1) — PRESENTATION ONLY. Consumes the SAME server data as the
 * shared timeline (api.requests.getTimeline → RequestTimelineDto) and renders it VERTICALLY, ONE lot
 * per carousel slide. It duplicates no workflow logic and never invents stages/timestamps — it shows
 * exactly the steps/lots the backend already derived. Multi-lot requests expose the per-lot `lots`
 * timelines; single-lot/legacy requests fall back to the request-level `steps` as one slide.
 */
export function BuyerLotTimeline({ requestId }: { requestId: string }) {
  const [data, setData] = useState<RequestTimelineDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [index, setIndex] = useState(0);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    api.requests.getTimeline(requestId)
      .then(d => { if (alive) { setData(d); setIndex(0); } })
      .catch(() => { if (alive) setData(null); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [requestId]);

  // Slides: real per-lot timelines when present, else a single request-level slide.
  const slides = useMemo<{ header: SlideHeader | null; steps: TimelineStepDto[] }[]>(() => {
    if (!data) return [];
    const lots = data.lots ?? [];
    if (lots.length > 0) return lots.map(l => ({ header: lotHeader(l), steps: l.steps }));
    return [{ header: null, steps: data.steps }];
  }, [data]);

  if (loading) return <div style={{ color: 'var(--color-text-muted)', fontSize: '0.82rem', padding: 8 }}>Carregando linha do tempo…</div>;
  if (slides.length === 0) return <div style={{ color: 'var(--color-text-muted)', fontSize: '0.82rem', padding: 8 }}>Sem linha do tempo disponível.</div>;

  const i = clampIndex(index, slides.length);
  const slide = slides[i];
  const multi = slides.length > 1;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {multi && (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <button onClick={() => setIndex(clampIndex(i - 1, slides.length))} disabled={i <= 0} style={navBtn(i <= 0)} aria-label="Lote anterior"><ChevronLeft size={16} /></button>
          <span style={{ fontSize: '0.72rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>{i + 1} / {slides.length}</span>
          <button onClick={() => setIndex(clampIndex(i + 1, slides.length))} disabled={i >= slides.length - 1} style={navBtn(i >= slides.length - 1)} aria-label="Próximo lote"><ChevronRight size={16} /></button>
        </div>
      )}

      {slide.header && <LotHeaderView header={slide.header} />}

      {/* Vertical timeline */}
      <div style={{ display: 'flex', flexDirection: 'column' }}>
        {slide.steps.map((s, si) => {
          const meta = stepStateMeta(s.state);
          const last = si === slide.steps.length - 1;
          return (
            <div key={si} style={{ display: 'flex', gap: 10 }}>
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                <span style={{ width: 12, height: 12, borderRadius: 999, background: s.state === 'completed' ? meta.color : 'transparent', border: `2px solid ${meta.color}`, flexShrink: 0, marginTop: 3 }} />
                {!last && <span style={{ width: 2, flex: 1, minHeight: 18, background: 'var(--color-border)' }} />}
              </div>
              <div style={{ paddingBottom: last ? 0 : 14, minWidth: 0 }}>
                <div style={{ fontSize: '0.82rem', fontWeight: meta.muted ? 500 : 700, color: meta.muted ? 'var(--color-text-muted)' : 'var(--color-text-main)' }}>
                  {s.label}{s.state === 'current' && <span style={{ color: 'var(--color-primary)', fontWeight: 700 }}> · atual</span>}
                </div>
                <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>
                  {(() => { const ts = timelineStepTimestamp(s.state, s.completedAt); return ts.date ? fmt(ts.date) : ts.text; })()}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Dots */}
      {multi && (
        <div style={{ display: 'flex', justifyContent: 'center', gap: 6 }}>
          {slides.map((_, di) => (
            <button key={di} onClick={() => setIndex(di)} aria-label={`Lote ${di + 1}`} style={{
              width: 7, height: 7, borderRadius: 999, border: 'none', padding: 0, cursor: 'pointer',
              background: di === i ? 'var(--color-primary)' : 'color-mix(in srgb, var(--color-text-muted) 30%, transparent)',
            }} />
          ))}
        </div>
      )}
    </div>
  );
}

interface SlideHeader { title: string; supplier?: string | null; amount: number; currency?: string | null; statusLabel: string; }
function lotHeader(l: LotTimelineDto): SlideHeader {
  return {
    title: l.lotNumber != null ? `Lote ${l.lotNumber}` : (l.label || 'Lote'),
    supplier: l.supplierName,
    amount: l.totalAmount,
    currency: l.currencyCode,
    statusLabel: l.statusLabel,
  };
}

function LotHeaderView({ header }: { header: SlideHeader }) {
  return (
    <div style={{ background: 'var(--color-bg-page)', border: '1px solid var(--color-border)', borderRadius: 8, padding: '8px 10px', display: 'flex', flexDirection: 'column', gap: 2 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <span style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '0.82rem' }}>{header.title}</span>
        <span style={{ fontSize: '0.68rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>{header.statusLabel}</span>
      </div>
      {header.supplier && <span title={header.supplier} style={{ fontSize: '0.74rem', color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{header.supplier}</span>}
      {header.amount > 0 && <span style={{ fontSize: '0.74rem', fontWeight: 600, color: 'var(--color-text-main)' }}>{formatLotAmount(header.amount, header.currency)}</span>}
    </div>
  );
}

function fmt(iso: string): string { try { return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: 'short', year: 'numeric' }); } catch { return '—'; } }
const navBtn = (disabled: boolean): React.CSSProperties => ({
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 28, height: 28, borderRadius: 7,
  border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: disabled ? 'var(--color-text-muted)' : 'var(--color-text-main)',
  cursor: disabled ? 'not-allowed' : 'pointer', opacity: disabled ? 0.5 : 1,
});

export default BuyerLotTimeline;
