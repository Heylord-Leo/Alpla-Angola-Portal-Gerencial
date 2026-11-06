/**
 * Viana 3 Production Monitor — Diagnostic view
 *
 * Route: /operations/viana3-monitor/diagnostics
 *
 * Deliberately separate from the TV page. Shows the recorded state only:
 *   • Historical open SYSTEM balance at the machine areas (booking balance of
 *     T_ProduktionsBuchungen, areas 1–6) — NOT physical stock, NOT Warehouse 3
 *     stock, never added to any available-stock total. Decision 2026-11: the
 *     postings of lots 2687/2700 are not corrected.
 *   • AlplaLABEL Waste whose lot no longer exists in the plan (deleted lots).
 *
 * Refreshes hourly (server cache 3600 s); manual refresh available.
 */

import { useState, useEffect, useCallback, type CSSProperties } from 'react';
import { fetchViana3MonitorDiagnostics } from '../../lib/operationsApi';
import type { Viana3MonitorDiagnosticsResponse } from '../../types/operations.types';
import { useEnvironment } from '../../contexts/EnvironmentContext';

const REFRESH_MS = 60 * 60 * 1000;

const C = {
    text: '#f1f5f9', textSecondary: '#cbd5e1', muted: '#94a3b8', dim: '#64748b',
    border: 'rgba(148,163,184,0.12)', surface: 'rgba(30,41,59,0.6)',
    good: '#22c55e', warn: '#f59e0b', bad: '#ef4444', info: '#60a5fa',
};

function parseLocal(iso: string | null | undefined): string {
    if (!iso) return '—';
    const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/.exec(iso);
    return m ? `${m[3]}/${m[2]}/${m[1]} ${m[4]}:${m[5]}` : '—';
}

function fmtInt(n: number | null | undefined): string {
    if (n === null || n === undefined || isNaN(n)) return '—';
    return Math.round(n).toLocaleString('pt-PT');
}

function fmtKg(n: number | null | undefined): string {
    if (n === null || n === undefined || isNaN(n)) return '—';
    return `${n.toLocaleString('pt-PT', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`;
}

const th: CSSProperties = {
    textAlign: 'left', padding: '8px 10px', fontSize: '12px', color: C.muted, fontWeight: 700,
    letterSpacing: '0.4px', borderBottom: `1px solid ${C.border}`, whiteSpace: 'nowrap',
};
const td: CSSProperties = {
    padding: '7px 10px', fontSize: '14px', color: C.textSecondary, borderBottom: `1px solid ${C.border}`,
    fontVariantNumeric: 'tabular-nums', verticalAlign: 'top',
};
const tdNum: CSSProperties = { ...td, textAlign: 'right', color: C.text, fontWeight: 700 };

export default function Viana3MonitorDiagnosticsPage() {
    const [data, setData] = useState<Viana3MonitorDiagnosticsResponse | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(true);
    const { isTest } = useEnvironment();

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const result = await fetchViana3MonitorDiagnostics();
            setData(result);
            setError(null);
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Erro de conexão');
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { load(); }, [load]);
    useEffect(() => {
        const t = setInterval(load, REFRESH_MS);
        return () => clearInterval(t);
    }, [load]);

    const src = data?.source;
    const sourceChip = !src ? null
        : src.status === 'LIVE' ? { text: 'AO VIVO', color: C.good }
            : src.status === 'CACHED' ? { text: 'CACHE (1 h)', color: C.info }
                : { text: 'DESATUALIZADO', color: C.warn };

    return (
        <div style={{
            minHeight: '100vh', background: 'linear-gradient(160deg, #0a0f1e 0%, #111827 40%, #0a0f1e 100%)',
            color: '#e2e8f0', fontFamily: "'Inter', 'Segoe UI', -apple-system, sans-serif",
        }}>
            {isTest && (
                <div style={{
                    height: '24px', background: 'linear-gradient(90deg, #d97706, #f59e0b, #d97706)', color: '#451a03',
                    display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '11px', fontWeight: 800, letterSpacing: '0.1em',
                }}>
                    AMBIENTE DE TESTE
                </div>
            )}

            <div style={{ padding: '18px 28px', borderBottom: `1px solid ${C.border}`, display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '16px', flexWrap: 'wrap' }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '24px', fontWeight: 800, color: '#f8fafc' }}>Monitor de Produção — Viana 3 · Diagnóstico</h1>
                    <div style={{ fontSize: '13px', color: C.muted, marginTop: '4px' }}>
                        Estado registado no AlplaPROD_aovia3 (só leitura). Esta vista não faz parte do ecrã principal da TV.
                        {' '}<a href="/operations/viana3-monitor" style={{ color: C.info }}>← Voltar ao monitor</a>
                    </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '14px', fontSize: '14px', color: C.muted }}>
                    {sourceChip && (
                        <span style={{
                            fontSize: '12px', fontWeight: 800, padding: '3px 10px', borderRadius: '8px',
                            color: sourceChip.color, background: `${sourceChip.color}1f`, border: `1px solid ${sourceChip.color}55`,
                        }}>
                            {sourceChip.text}
                        </span>
                    )}
                    {src && <span title={src.timeZoneLabel}>🕐 Atualizado {parseLocal(src.fetchedAtLocal)} (hora de Angola)</span>}
                    <button onClick={load} disabled={loading} style={{
                        padding: '6px 14px', borderRadius: '8px', border: `1px solid ${C.border}`, background: C.surface,
                        color: C.text, cursor: loading ? 'wait' : 'pointer', fontSize: '13px', fontWeight: 600,
                    }}>
                        {loading ? 'A atualizar…' : 'Atualizar agora'}
                    </button>
                </div>
            </div>

            {src?.status === 'STALE' && (
                <div style={{ padding: '8px 28px', background: 'rgba(245,158,11,0.10)', color: '#fcd34d', fontSize: '14px', fontWeight: 600 }}>
                    ⚠ AlplaPROD indisponível — a mostrar os últimos dados obtidos às {parseLocal(src.fetchedAtLocal)}
                    {src.unavailableReason ? ` · ${src.unavailableReason}` : ''}
                </div>
            )}
            {error && (
                <div style={{ padding: '8px 28px', background: 'rgba(239,68,68,0.12)', color: '#fca5a5', fontSize: '14px', fontWeight: 600 }}>
                    ⚠ {error}
                </div>
            )}

            {data && (
                <div style={{ padding: '18px 28px', display: 'flex', flexDirection: 'column', gap: '22px' }}>
                    {/* Notes */}
                    <div style={{ padding: '12px 16px', borderRadius: '12px', background: C.surface, border: `1px solid ${C.border}` }}>
                        {data.notes.map((n, i) => (
                            <div key={i} style={{ fontSize: '13px', color: C.textSecondary, lineHeight: 1.5 }}>• {n}</div>
                        ))}
                    </div>

                    {/* Open system balances */}
                    <section>
                        <h2 style={{ margin: '0 0 8px', fontSize: '18px', fontWeight: 800, color: C.text }}>
                            Saldo histórico em aberto no sistema — áreas de máquina (diagnóstico)
                        </h2>
                        <div style={{ fontSize: '13px', color: C.muted, marginBottom: '8px' }}>
                            Saldo de lançamentos por código de barras. Não é stock físico nem stock do Armazém 3 e não entra em nenhum total disponível.
                        </div>
                        <div style={{ overflowX: 'auto', borderRadius: '12px', border: `1px solid ${C.border}`, background: C.surface }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr>
                                        <th style={th}>Máquina</th>
                                        <th style={th}>Pré-forma</th>
                                        <th style={th}>Código de barras</th>
                                        <th style={th}>Último lote</th>
                                        <th style={th}>Última operação</th>
                                        <th style={{ ...th, textAlign: 'right' }}>Saldo no sistema (un.)</th>
                                        <th style={th}>Último movimento</th>
                                        <th style={{ ...th, textAlign: 'right' }}>Dias</th>
                                        <th style={th}>Estado</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {data.openSystemBalances.length === 0 ? (
                                        <tr><td style={td} colSpan={9}>Sem saldos em aberto nas áreas de máquina.</td></tr>
                                    ) : data.openSystemBalances.map((b, i) => (
                                        <tr key={`${b.machineId}-${b.barcode ?? i}`}>
                                            <td style={td}>{b.machineName ?? `Área ${b.machineId}`}</td>
                                            <td style={td}>{b.preformArticle}</td>
                                            <td style={td}>{b.barcode ?? '—'}</td>
                                            <td style={td}>{b.lastLotId ?? '—'}</td>
                                            <td style={td}>{b.lastOperation ?? '—'}{b.lastOperation === 1460 ? ' (anulação)' : b.lastOperation === 1420 ? ' (consumo)' : ''}</td>
                                            <td style={tdNum}>{fmtInt(b.openSystemBalance)}</td>
                                            <td style={td}>{parseLocal(b.lastMovement)}</td>
                                            <td style={tdNum}>{b.daysSinceLastMovement ?? '—'}</td>
                                            <td style={{ ...td, color: C.warn }}>{b.balanceStatus}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </section>

                    {/* Orphan waste */}
                    <section>
                        <h2 style={{ margin: '0 0 8px', fontSize: '18px', fontWeight: 800, color: C.text }}>
                            Waste AlplaLABEL sem lote válido (últimos {data.orphanWasteWindowDays} dias)
                        </h2>
                        <div style={{ fontSize: '13px', color: C.muted, marginBottom: '8px' }}>
                            O lote do lançamento não existe no plano (lote apagado). kg = valor registado; unidades = estimativa com o peso da garrafa do lançamento (pressuposto também para o artigo 55).
                        </div>
                        <div style={{ overflowX: 'auto', borderRadius: '12px', border: `1px solid ${C.border}`, background: C.surface }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr>
                                        <th style={th}>Lançamento</th>
                                        <th style={th}>Data</th>
                                        <th style={th}>Tipo</th>
                                        <th style={{ ...th, textAlign: 'right' }}>kg registados</th>
                                        <th style={th}>Lote no lançamento</th>
                                        <th style={th}>Máquina</th>
                                        <th style={th}>Artigo garrafa</th>
                                        <th style={{ ...th, textAlign: 'right' }}>Peso (g)</th>
                                        <th style={{ ...th, textAlign: 'right' }}>Unidades (estimativa)</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {data.orphanWaste.length === 0 ? (
                                        <tr><td style={td} colSpan={9}>Sem lançamentos de Waste sem lote válido.</td></tr>
                                    ) : data.orphanWaste.map(w => (
                                        <tr key={w.bookingId}>
                                            <td style={td}>{w.bookingId}</td>
                                            <td style={td}>{parseLocal(w.bookedAt)}</td>
                                            <td style={td}>{w.wasteType} (art. {w.wasteArticleId})</td>
                                            <td style={tdNum}>{fmtKg(w.wasteKg)}</td>
                                            <td style={td}>{w.lotOnBooking ?? '—'}</td>
                                            <td style={td}>{w.machineId ?? '—'}</td>
                                            <td style={td}>{w.bottleArticle ?? '—'}</td>
                                            <td style={tdNum}>{w.bottleWeightG ?? '—'}</td>
                                            <td style={tdNum}>{w.unitsEstimate !== null ? `≈ ${fmtInt(w.unitsEstimate)}` : '—'}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </section>

                    <div style={{ fontSize: '12px', color: C.dim }}>
                        Fonte: AlplaPROD_aovia3 (só leitura) · {src?.timeZoneLabel} · atualização automática a cada hora.
                    </div>
                </div>
            )}
        </div>
    );
}
