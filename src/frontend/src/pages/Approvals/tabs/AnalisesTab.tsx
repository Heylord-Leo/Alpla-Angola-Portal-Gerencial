import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties, ReactNode } from 'react';
import { Loader2, RefreshCw, BarChart3, Info } from 'lucide-react';
import { api } from '../../../lib/api';
import type { ApprovalAnalytics, ApprovalAnalyticsQuery } from '../../../types/approvalAnalytics';
import { formatDuration } from './formatDuration';

// v2.244.0 Approval Center V2 — Phase 3. Read-only ANÁLISES: SLA/duration KPIs, Área-vs-Final
// bottleneck, decision mix, trend and approver performance. All figures are server-computed; this tab
// only formats and lays them out (no client-side metric recomputation). Inline SVG/CSS, no chart lib.

type DatePreset = 'today' | '7' | '30' | '90' | 'all';

const chip: CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '6px 12px', borderRadius: 8,
  border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', cursor: 'pointer',
  fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-secondary, var(--color-text-main))', whiteSpace: 'nowrap',
};
const chipActive: CSSProperties = { background: 'var(--color-primary)', color: '#fff', borderColor: 'var(--color-primary)' };

function dateFromPreset(p: DatePreset): string | undefined {
  if (p === 'all') return undefined;
  const d = new Date();
  if (p === 'today') d.setHours(0, 0, 0, 0);
  else if (p === '7') d.setDate(d.getDate() - 7);
  else if (p === '30') d.setDate(d.getDate() - 30);
  else if (p === '90') d.setDate(d.getDate() - 90);
  return d.toISOString();
}
// §14 — day for ≤30d, week for 90d, month for all.
function resolutionFor(p: DatePreset): 'day' | 'week' | 'month' {
  if (p === 'all') return 'month';
  if (p === '90') return 'week';
  return 'day';
}
const pct = (r: number) => `${Math.round(r * 100)}%`;

export function AnalisesTab() {
  const [preset, setPreset] = useState<DatePreset>('30');
  const [requestType, setRequestType] = useState<'' | 'QUOTATION' | 'PAYMENT'>('');
  const [approverSort, setApproverSort] = useState<'decisions' | 'slowest' | 'fastest'>('decisions');

  const [data, setData] = useState<ApprovalAnalytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const reqToken = useRef(0);

  const query = useMemo<ApprovalAnalyticsQuery>(() => ({
    dateFrom: dateFromPreset(preset),
    resolution: resolutionFor(preset),
    requestType: requestType || undefined,
  }), [preset, requestType]);

  const load = useCallback(async () => {
    const token = ++reqToken.current;
    setLoading(true); setError(false);
    try {
      const res = await api.approvals.getAnalytics(query);
      if (token === reqToken.current) { setData(res); setLoading(false); }
    } catch {
      if (token === reqToken.current) { setError(true); setLoading(false); }
    }
  }, [query]);

  useEffect(() => { load(); }, [load]);

  const approversSorted = useMemo(() => {
    if (!data) return [];
    const list = [...data.approvers];
    if (approverSort === 'slowest') list.sort((a, b) => (b.averageDecisionSeconds ?? -1) - (a.averageDecisionSeconds ?? -1));
    else if (approverSort === 'fastest') list.sort((a, b) => (a.averageDecisionSeconds ?? Number.MAX_SAFE_INTEGER) - (b.averageDecisionSeconds ?? Number.MAX_SAFE_INTEGER));
    else list.sort((a, b) => b.decisionCount - a.decisionCount);
    return list;
  }, [data, approverSort]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* Filters */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        <Group label="Período">
          <Chip active={preset === 'today'} onClick={() => setPreset('today')}>Hoje</Chip>
          <Chip active={preset === '7'} onClick={() => setPreset('7')}>7 dias</Chip>
          <Chip active={preset === '30'} onClick={() => setPreset('30')}>30 dias</Chip>
          <Chip active={preset === '90'} onClick={() => setPreset('90')}>90 dias</Chip>
          <Chip active={preset === 'all'} onClick={() => setPreset('all')}>Tudo</Chip>
        </Group>
        <Group label="Tipo">
          <Chip active={requestType === ''} onClick={() => setRequestType('')}>Todos</Chip>
          <Chip active={requestType === 'QUOTATION'} onClick={() => setRequestType('QUOTATION')}>Cotação</Chip>
          <Chip active={requestType === 'PAYMENT'} onClick={() => setRequestType('PAYMENT')}>Pagamento</Chip>
        </Group>
      </div>

      {loading ? (
        <Center><Loader2 size={16} className="spin-icon" /> Carregando análises…</Center>
      ) : error ? (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, padding: '32px 0', color: 'var(--color-status-red, #dc2626)' }}>
          <span style={{ fontWeight: 700 }}>Não foi possível carregar as análises.</span>
          <button type="button" onClick={load} style={{ ...chip, cursor: 'pointer' }}><RefreshCw size={14} /> Tentar novamente</button>
        </div>
      ) : !data || data.summary.totalDecisions === 0 ? (
        <Center><BarChart3 size={24} /> Sem dados de aprovação neste período.</Center>
      ) : (
        <>
          {/* KPI row */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 12 }}>
            <Kpi label="Decisões" value={data.summary.totalDecisions.toString()} />
            <Kpi label="Tempo médio" value={formatDuration(data.duration.averageSeconds)} hint="Tempo total de aprovação (entrada na área → decisão final)." sample={data.duration.sampleCount} />
            <Kpi label="Mediana" value={formatDuration(data.duration.medianSeconds)} hint="Metade das aprovações concluiu em menos deste tempo." sample={data.duration.sampleCount} />
            <Kpi label="P90" value={formatDuration(data.duration.p90Seconds)} hint="P90: 90% das aprovações foram concluídas em até este tempo." sample={data.duration.sampleCount} />
            <Kpi label="Taxa de aprovação" value={pct(data.summary.approvalRate)} />
            <Kpi label="Devolvidas / Rejeitadas" value={`${data.summary.returned} / ${data.summary.rejected}`} />
          </div>

          {/* Bottleneck: Área vs Final */}
          <Section title="Tempo por etapa (gargalo)">
            <StageBars
              area={data.bottleneck.areaAverageSeconds} areaN={data.areaDuration.sampleCount}
              final={data.bottleneck.finalAverageSeconds} finalN={data.finalDuration.sampleCount}
              dominant={data.bottleneck.dominantStage}
            />
          </Section>

          {/* Decision mix */}
          <Section title="Resultado das decisões">
            <DecisionMix s={data.summary} />
          </Section>

          {/* Trend */}
          <Section title={`Evolução das decisões (${data.resolution === 'month' ? 'mês' : data.resolution === 'week' ? 'semana' : 'dia'})`}>
            <Trend points={data.trend} />
          </Section>

          {/* Approvers */}
          <Section
            title="Aprovadores"
            action={
              <select value={approverSort} onChange={e => setApproverSort(e.target.value as any)} aria-label="Ordenar aprovadores"
                style={{ padding: '4px 8px', borderRadius: 6, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontSize: '0.75rem', fontWeight: 700 }}>
                <option value="decisions">Mais decisões</option>
                <option value="slowest">Mais lento</option>
                <option value="fastest">Mais rápido</option>
              </select>
            }
          >
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
                <thead>
                  <tr style={{ background: 'var(--color-bg-page)', textAlign: 'left' }}>
                    {['Aprovador', 'Decisões', 'Aprovadas', 'Rej./Dev.', 'Tempo médio', 'Mediana'].map((h, i) => (
                      <th key={i} style={{ padding: '8px 12px', fontSize: '0.64rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)', whiteSpace: 'nowrap', textAlign: i === 0 ? 'left' : 'right' }}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {approversSorted.map(a => (
                    <tr key={a.approverUserId} style={{ borderTop: '1px solid var(--color-border)' }}>
                      <td style={{ padding: '8px 12px', fontWeight: 700, whiteSpace: 'nowrap' }}>{a.approverName}</td>
                      <td style={{ padding: '8px 12px', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{a.decisionCount}</td>
                      <td style={{ padding: '8px 12px', textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--color-status-green, #16a34a)' }}>{a.approved}</td>
                      <td style={{ padding: '8px 12px', textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-muted)' }}>{a.rejected + a.returned}</td>
                      <td style={{ padding: '8px 12px', textAlign: 'right', whiteSpace: 'nowrap' }} title={a.speedSampleCount > 0 ? `${a.speedSampleCount} amostras` : 'sem amostras de tempo'}>
                        {a.averageDecisionSeconds != null ? formatDuration(a.averageDecisionSeconds) : '—'}
                      </td>
                      <td style={{ padding: '8px 12px', textAlign: 'right', whiteSpace: 'nowrap', color: 'var(--color-text-muted)' }}>
                        {a.medianDecisionSeconds != null ? formatDuration(a.medianDecisionSeconds) : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p style={{ margin: '8px 0 0', fontSize: '0.68rem', color: 'var(--color-text-muted)' }}>
              Tempo = espera na etapa do aprovador até à sua decisão. Carga operacional, não ranking.
            </p>
          </Section>
        </>
      )}
    </div>
  );
}

// ── Presentational bits ─────────────────────────────────────────────

function Center({ children }: { children: ReactNode }) {
  return <div style={{ display: 'flex', alignItems: 'center', gap: 8, justifyContent: 'center', padding: '40px 0', color: 'var(--color-text-muted)' }}>{children}</div>;
}

function Group({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div role="group" aria-label={label} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span style={{ fontSize: '0.64rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>{label}</span>
      {children}
    </div>
  );
}

function Chip({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return <button type="button" aria-pressed={active} onClick={onClick} style={{ ...chip, ...(active ? chipActive : {}) }}>{children}</button>;
}

function Section({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section style={{ border: '1px solid var(--color-border)', borderRadius: 10, padding: 16, background: 'var(--color-bg-surface)' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, marginBottom: 12 }}>
        <h3 style={{ margin: 0, fontSize: '0.82rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.03em', color: 'var(--color-text-main)' }}>{title}</h3>
        {action}
      </div>
      {children}
    </section>
  );
}

function Kpi({ label, value, hint, sample }: { label: string; value: string; hint?: string; sample?: number }) {
  return (
    <div style={{ border: '1px solid var(--color-border)', borderRadius: 10, padding: '12px 14px', background: 'var(--color-bg-surface)', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
        <span style={{ fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)' }}>{label}</span>
        {hint && (
          <span title={hint} aria-label={hint} tabIndex={0} style={{ display: 'inline-flex', cursor: 'help' }}>
            <Info size={12} style={{ color: 'var(--color-text-muted)' }} />
          </span>
        )}
      </div>
      <span style={{ fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>{value}</span>
      {sample != null && <span style={{ fontSize: '0.62rem', color: 'var(--color-text-muted)' }}>{sample} {sample === 1 ? 'amostra' : 'amostras'}</span>}
    </div>
  );
}

function StageBars({ area, areaN, final, finalN, dominant }: { area: number; areaN: number; final: number; finalN: number; dominant: string | null }) {
  const max = Math.max(area, final, 1);
  const Row = ({ label, secs, n, hot }: { label: string; secs: number; n: number; hot: boolean }) => (
    <div style={{ display: 'grid', gridTemplateColumns: '110px 1fr 90px', gap: 10, alignItems: 'center' }}>
      <span style={{ fontSize: '0.78rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{label}</span>
      <div style={{ background: 'var(--color-bg-page)', borderRadius: 6, height: 20, overflow: 'hidden', border: '1px solid var(--color-border)' }}>
        <div style={{ width: `${Math.round((secs / max) * 100)}%`, height: '100%', background: hot ? 'var(--color-status-amber, #d97706)' : 'var(--color-primary)', minWidth: secs > 0 ? 4 : 0 }} />
      </div>
      <span style={{ fontSize: '0.78rem', fontWeight: 700, textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
        {formatDuration(secs)} <span style={{ fontSize: '0.62rem', color: 'var(--color-text-muted)' }}>({n})</span>
      </span>
    </div>
  );
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <Row label="Aprovação de Área" secs={area} n={areaN} hot={dominant === 'AREA'} />
      <Row label="Aprovação Final" secs={final} n={finalN} hot={dominant === 'FINAL'} />
      {dominant && (
        <p style={{ margin: 0, fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
          Gargalo dominante: <strong style={{ color: 'var(--color-status-amber, #d97706)' }}>{dominant === 'FINAL' ? 'Aprovação Final' : 'Aprovação de Área'}</strong> (maior tempo médio).
        </p>
      )}
    </div>
  );
}

function DecisionMix({ s }: { s: ApprovalAnalytics['summary'] }) {
  const total = s.approved + s.rejected + s.returned + s.resubmitted || 1;
  const parts: Array<[string, number, string]> = [
    ['Aprovado', s.approved, 'var(--color-status-green, #16a34a)'],
    ['Rejeitado', s.rejected, 'var(--color-status-red, #dc2626)'],
    ['Devolvido', s.returned, 'var(--color-status-amber, #d97706)'],
    ['Reenviado', s.resubmitted, 'var(--color-primary)'],
  ];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', height: 22, borderRadius: 6, overflow: 'hidden', border: '1px solid var(--color-border)' }} role="img" aria-label={parts.map(([l, v]) => `${l}: ${v}`).join(', ')}>
        {parts.map(([label, v, color]) => v > 0 && (
          <div key={label} title={`${label}: ${v}`} style={{ width: `${(v / total) * 100}%`, background: color }} />
        ))}
      </div>
      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
        {parts.map(([label, v, color]) => (
          <span key={label} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-secondary, var(--color-text-main))' }}>
            <span style={{ width: 10, height: 10, borderRadius: 2, background: color, display: 'inline-block' }} /> {label}: {v}
          </span>
        ))}
      </div>
    </div>
  );
}

function Trend({ points }: { points: ApprovalAnalytics['trend'] }) {
  if (points.length === 0) return <Center>Sem dados no período.</Center>;
  const max = Math.max(...points.map(p => p.decisions), 1);
  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', gap: 6, height: 130, overflowX: 'auto', paddingBottom: 4 }}>
      {points.map(p => (
        <div key={p.bucket} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4, minWidth: 34 }} title={`${p.bucket}: ${p.decisions} decisões`}>
          <span style={{ fontSize: '0.62rem', fontWeight: 700, color: 'var(--color-text-muted)', fontVariantNumeric: 'tabular-nums' }}>{p.decisions}</span>
          <div style={{ width: 22, height: `${Math.round((p.decisions / max) * 90)}px`, minHeight: p.decisions > 0 ? 3 : 0, background: 'var(--color-primary)', borderRadius: '3px 3px 0 0' }} />
          <span style={{ fontSize: '0.56rem', color: 'var(--color-text-muted)', whiteSpace: 'nowrap', transform: 'rotate(-45deg)', transformOrigin: 'center', marginTop: 4 }}>{p.bucket.slice(5)}</span>
        </div>
      ))}
    </div>
  );
}

export default AnalisesTab;
