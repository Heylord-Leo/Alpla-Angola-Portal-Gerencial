import { useEffect, useState, type ReactNode } from 'react';
import { api } from '../../lib/api';
import {
    BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip as RechartsTooltip,
    ResponsiveContainer, Legend
} from 'recharts';
import {
    FileText, AlertTriangle, CheckCircle, TrendingUp,
    Clock, ShieldAlert, Filter, ChevronLeft, ChevronRight
} from 'lucide-react';

// ─── Types ────────────────────────────────────────────────────────────────────

interface CurrencyTotal { currencyCode: string; totalAmount: number; }
interface MonthlySeries { yearMonth: string; currencyCode: string; projectedAmount: number; pipelineAmount: number; confirmedAmount: number; }
interface ProjectionItem {
    obligationId: string; contractId: string; contractNumber: string; contractTitle: string;
    supplierName: string; companyName: string; departmentName?: string; departmentId?: number;
    obligationLabel?: string; amount: number; currencyCode: string;
    dueDateUtc?: string; graceDateUtc?: string; penaltyStartDateUtc?: string;
    forecastBucket: string; riskLevelCode: string;
    linkedRequestNumber?: string; linkedRequestStatus?: string;
}
interface ProjectionSummary {
    currentMonthByCurrency: CurrencyTotal[];
    nextThreeMonthsByCurrency: CurrencyTotal[];
    pipelineByCurrency: CurrencyTotal[];
    confirmedByCurrency: CurrencyTotal[];
    realizedByCurrency: CurrencyTotal[];
    overdueNoRequestCount: number;
    penaltyRiskCount: number;
    monthlySeries: MonthlySeries[];
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function fmtCurrency(val: number, currency: string) {
    return new Intl.NumberFormat('pt-AO', { style: 'currency', currency: currency || 'AOA' }).format(val);
}

function fmtCurrencyList(values: CurrencyTotal[]) {
    if (!values || values.length === 0) return '—';
    return values.map(v => fmtCurrency(v.totalAmount, v.currencyCode)).join(' / ');
}

function fmtDate(d?: string) {
    if (!d) return '—';
    return new Date(d).toLocaleDateString('pt-AO', { day: '2-digit', month: 'short', year: 'numeric' });
}

const BUCKET_LABELS: Record<string, string> = {
    PROJECTED: 'Projetado',
    OVERDUE_NO_REQUEST: 'Vencido s/ Pedido',
    PIPELINE: 'Em Pipeline',
    CONFIRMED: 'Confirmado',
    REALIZED: 'Realizado',
};

const BUCKET_COLORS: Record<string, string> = {
    PROJECTED: '#0284c7',
    OVERDUE_NO_REQUEST: '#ef4444',
    PIPELINE: '#f97316',
    CONFIRMED: '#16a34a',
    REALIZED: '#7c3aed',
};

const RISK_COLORS: Record<string, string> = {
    HIGH: '#ef4444',
    MEDIUM: '#f97316',
    LOW: '#64748b',
};

const RISK_LABELS: Record<string, string> = {
    HIGH: 'Alto',
    MEDIUM: 'Médio',
    LOW: 'Baixo',
};

// ─── Chart Data Prep ─────────────────────────────────────────────────────────

function buildChartData(series: MonthlySeries[]) {
    // Group by yearMonth (pick first currency found for simplicity — multi-currency shows one bar per entry)
    const byMonth: Record<string, { yearMonth: string; projected: number; pipeline: number; confirmed: number; currency: string }> = {};
    for (const s of series) {
        if (!byMonth[s.yearMonth]) {
            byMonth[s.yearMonth] = { yearMonth: s.yearMonth, projected: 0, pipeline: 0, confirmed: 0, currency: s.currencyCode };
        }
        byMonth[s.yearMonth].projected += s.projectedAmount;
        byMonth[s.yearMonth].pipeline += s.pipelineAmount;
        byMonth[s.yearMonth].confirmed += s.confirmedAmount;
    }
    return Object.values(byMonth).sort((a, b) => a.yearMonth.localeCompare(b.yearMonth)).map(d => {
        const [year, month] = d.yearMonth.split('-');
        const months = ['Jan', 'Fev', 'Mar', 'Abr', 'Mai', 'Jun', 'Jul', 'Ago', 'Set', 'Out', 'Nov', 'Dez'];
        return {
            name: `${months[parseInt(month, 10) - 1]}/${year.slice(2)}`,
            Projetado: d.projected,
            Pipeline: d.pipeline,
            Confirmado: d.confirmed,
            currency: d.currency,
        };
    });
}

// ─── Local KPI Card ──────────────────────────────────────────────────────────
// Replicates the exact same visual pattern as ui/KPICard but uses adaptive
// font-size so long monetary strings never overflow.

interface KPICardLocalProps {
    title: string;
    value: string | number;
    subtitle?: string;
    icon: ReactNode;
    color: string;
    borderColor?: string;
}

function ProjectionKPICard({ title, value, subtitle, icon, color, borderColor }: KPICardLocalProps) {
    const [hovered, setHovered] = useState(false);
    const valueStr = typeof value === 'number' ? String(value) : value;
    // Shrink font for long currency strings; integers stay large
    const valueFontSize = valueStr.length > 18 ? '1.1rem' : valueStr.length > 10 ? '1.35rem' : '2.5rem';

    return (
        <div
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
            style={{
                backgroundColor: 'var(--color-bg-surface)',
                padding: '24px',
                borderRadius: '12px',
                border: `1px solid ${hovered ? 'rgba(56,189,248,0.4)' : (borderColor || 'var(--color-border)')}`,
                boxShadow: hovered
                    ? '0 10px 25px rgba(56,189,248,0.25), 0 0 0 1px rgba(56,189,248,0.2)'
                    : '0 4px 6px rgba(0,0,0,0.05)',
                display: 'flex',
                flexDirection: 'column',
                gap: '8px',
                position: 'relative',
                overflow: 'hidden',
                transform: hovered ? 'translateY(-2px)' : 'none',
                transition: 'all 0.2s',
            }}
        >
            {/* Header: title + icon */}
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px', position: 'relative', zIndex: 1 }}>
                <span style={{ fontWeight: 600, fontSize: '13px', textTransform: 'uppercase', color: color }}>
                    {title}
                </span>
                <div style={{
                    width: 40, height: 40,
                    backgroundColor: `${color}1A`,
                    color,
                    borderRadius: '8px',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                }}>
                    {icon}
                </div>
            </div>

            {/* Value */}
            <div style={{
                fontSize: valueFontSize,
                fontWeight: 700,
                lineHeight: 1.15,
                color: color,
                position: 'relative',
                zIndex: 1,
                wordBreak: 'break-word',
            }}>
                {value}
            </div>

            {/* Subtitle */}
            {subtitle && (
                <div style={{
                    fontSize: '0.8rem',
                    fontWeight: 600,
                    color: 'var(--color-text-muted)',
                    marginTop: '4px',
                    position: 'relative',
                    zIndex: 1,
                }}>
                    {subtitle}
                </div>
            )}

            {/* Ghost icon background */}
            <div style={{
                position: 'absolute', right: -8, bottom: -8,
                opacity: 0.04, pointerEvents: 'none', zIndex: 0, color,
            }}>
                <span style={{ fontSize: 80, lineHeight: 1 }}>{icon}</span>
            </div>
        </div>
    );
}

// ─── Main Component ───────────────────────────────────────────────────────────

interface Props { selectedCompanyId: number | null; }

export default function ContractProjectionSection({ selectedCompanyId }: Props) {
    const [summary, setSummary] = useState<ProjectionSummary | null>(null);
    const [summaryLoading, setSummaryLoading] = useState(true);

    const [items, setItems] = useState<ProjectionItem[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [itemsLoading, setItemsLoading] = useState(false);

    // Filters
    const [filterBucket, setFilterBucket] = useState<string>('');
    const [filterOnlyAtRisk, setFilterOnlyAtRisk] = useState(false);
    const [page, setPage] = useState(1);
    const pageSize = 15;

    // Load summary
    useEffect(() => {
        setSummaryLoading(true);
        api.finance.getContractProjectionSummary(selectedCompanyId ?? undefined)
            .then((data: ProjectionSummary) => { setSummary(data); setSummaryLoading(false); })
            .catch(() => setSummaryLoading(false));
    }, [selectedCompanyId]);

    // Load detail table
    useEffect(() => {
        setItemsLoading(true);
        api.finance.getContractProjections({
            companyId: selectedCompanyId ?? undefined,
            bucket: filterBucket || undefined,
            onlyAtRisk: filterOnlyAtRisk || undefined,
            page,
            pageSize,
        }).then((data: any) => {
            setItems(data.items ?? []);
            setTotalCount(data.totalCount ?? 0);
            setItemsLoading(false);
        }).catch(() => setItemsLoading(false));
    }, [selectedCompanyId, filterBucket, filterOnlyAtRisk, page]);

    const chartData = summary ? buildChartData(summary.monthlySeries) : [];
    const totalPages = Math.ceil(totalCount / pageSize);

    return (
        <div style={{ marginTop: '16px', display: 'flex', flexDirection: 'column', gap: '24px' }}>

            {/* Section Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                <h2 style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text)', margin: 0, display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <FileText size={24} color="#7c3aed" /> Projeção Contratual
                </h2>
                <span style={{ fontSize: '13px', fontWeight: 600, color: '#64748b', backgroundColor: 'var(--color-bg-page)', padding: '6px 12px', borderRadius: '6px', border: '1px solid var(--color-border)' }}>
                    Fonte: contratos ATIVOS · Obrigações de pagamento
                </span>
            </div>

            {summaryLoading ? (
                <div style={{ padding: '40px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', borderRadius: '12px', border: '1px solid var(--color-border)' }}>
                    <span style={{ fontWeight: 600, color: '#64748b' }}>Carregando projeção contratual...</span>
                </div>
            ) : (
                <>
                    {/* KPI Cards Row — same visual as ui/KPICard with adaptive value font-size */}
                    <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fill, minmax(250px, 1fr))',
                        gap: '20px',
                    }}>
                        <ProjectionKPICard
                            title="Compromissos deste Mês"
                            value={fmtCurrencyList(summary?.currentMonthByCurrency ?? [])}
                            icon={<Clock size={20} />}
                            color="#0284c7"
                            subtitle="Obrigações com vencimento no mês corrente"
                        />
                        <ProjectionKPICard
                            title="Próximos 90 Dias"
                            value={fmtCurrencyList(summary?.nextThreeMonthsByCurrency ?? [])}
                            icon={<TrendingUp size={20} />}
                            color="#7c3aed"
                            subtitle="Saídas previstas até 90 dias"
                        />
                        <ProjectionKPICard
                            title="Em Pipeline"
                            value={fmtCurrencyList(summary?.pipelineByCurrency ?? [])}
                            icon={<FileText size={20} />}
                            color="#f97316"
                            subtitle="Obrigações com pedido em curso"
                        />
                        <ProjectionKPICard
                            title="Confirmados"
                            value={fmtCurrencyList(summary?.confirmedByCurrency ?? [])}
                            icon={<CheckCircle size={20} />}
                            color="#16a34a"
                            subtitle="Pedidos aprovados ou agendados"
                        />
                        <ProjectionKPICard
                            title="Vencidos s/ Pedido"
                            value={summary?.overdueNoRequestCount ?? 0}
                            icon={<AlertTriangle size={20} />}
                            color="#ef4444"
                            borderColor="#fca5a5"
                            subtitle="Obrigações que precisam de ação imediata"
                        />
                        <ProjectionKPICard
                            title="Risco de Penalidade"
                            value={summary?.penaltyRiskCount ?? 0}
                            icon={<ShieldAlert size={20} />}
                            color="#b91c1c"
                            borderColor="#fca5a5"
                            subtitle="Penalidades activas ou eminentes"
                        />
                    </div>

                    {/* Contractual Bar Chart */}
                    <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', padding: '24px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                        <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', marginBottom: '8px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <TrendingUp size={20} color="#7c3aed" /> Calendário de Saídas Contratuais (6 Meses)
                        </h3>
                        <p style={{ margin: '0 0 20px 0', color: '#64748b', fontSize: '14px', fontWeight: 500 }}>
                            Montantes projetados por situação — contratos ATIVOS ordenados por vencimento.
                        </p>

                        {chartData.length === 0 ? (
                            <div style={{ height: '260px', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: '#64748b', fontWeight: 600, border: '2px dashed #cbd5e1', borderRadius: '8px', backgroundColor: 'var(--color-bg-page)' }}>
                                <FileText size={36} color="#94a3b8" style={{ marginBottom: '12px' }} />
                                <span style={{ fontSize: '15px', color: '#334155' }}>Sem obrigações contratuais activas</span>
                                <span style={{ marginTop: '8px', fontSize: '13px', maxWidth: '380px', textAlign: 'center' }}>
                                    Quando existirem contratos ATIVOS com parcelas configuradas, o calendário contratual será exibido aqui.
                                </span>
                            </div>
                        ) : (
                            <div style={{ height: '280px', width: '100%' }}>
                                <ResponsiveContainer width="100%" height="100%">
                                    <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                                        <CartesianGrid strokeDasharray="3 3" vertical={false} />
                                        <XAxis dataKey="name" tick={{ fontSize: 12, fontWeight: 700 }} tickLine={false} axisLine={false} />
                                        <YAxis
                                            tickFormatter={(val) => `${(val / 1000000).toFixed(1)}M`}
                                            tick={{ fontSize: 11, fontWeight: 700 }}
                                            axisLine={false}
                                            tickLine={false}
                                        />
                                        <RechartsTooltip
                                            cursor={{ fill: '#f1f5f9' }}
                                            formatter={(value: any, name: any) => [fmtCurrency(Number(value), 'AOA'), name]}
                                            labelStyle={{ fontWeight: 'bold', color: '#0f172a' }}
                                        />
                                        <Legend />
                                        <Bar dataKey="Projetado" stackId="a" fill="#0284c7" radius={[0, 0, 0, 0]} />
                                        <Bar dataKey="Pipeline" stackId="a" fill="#f97316" radius={[0, 0, 0, 0]} />
                                        <Bar dataKey="Confirmado" stackId="a" fill="#16a34a" radius={[4, 4, 0, 0]} />
                                    </BarChart>
                                </ResponsiveContainer>
                            </div>
                        )}
                    </div>

                    {/* Detail Table */}
                    <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', padding: '24px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px', marginBottom: '20px' }}>
                            <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', margin: 0 }}>
                                Detalhe das Obrigações
                            </h3>
                            {/* Filters */}
                            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', alignItems: 'center' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <Filter size={14} color="#64748b" />
                                    <span style={{ fontSize: '12px', fontWeight: 700, color: '#64748b' }}>FILTRAR:</span>
                                </div>
                                <select
                                    value={filterBucket}
                                    onChange={e => { setFilterBucket(e.target.value); setPage(1); }}
                                    style={{ padding: '6px 10px', borderRadius: '6px', border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)', fontWeight: 600, fontSize: '13px', color: 'var(--color-text)', cursor: 'pointer' }}
                                >
                                    <option value="">Todos os buckets</option>
                                    {Object.entries(BUCKET_LABELS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                                </select>
                                <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '13px', fontWeight: 600, color: '#64748b', cursor: 'pointer' }}>
                                    <input
                                        type="checkbox"
                                        checked={filterOnlyAtRisk}
                                        onChange={e => { setFilterOnlyAtRisk(e.target.checked); setPage(1); }}
                                    />
                                    Só em risco
                                </label>
                            </div>
                        </div>

                        {itemsLoading ? (
                            <div style={{ padding: '32px', textAlign: 'center', color: '#64748b', fontWeight: 600 }}>Carregando...</div>
                        ) : items.length === 0 ? (
                            <div style={{ padding: '32px', textAlign: 'center', color: '#64748b', fontSize: '14px', border: '2px dashed #cbd5e1', borderRadius: '8px' }}>
                                Nenhuma obrigação correspondente aos filtros seleccionados.
                            </div>
                        ) : (
                            <>
                                <div style={{ overflowX: 'auto' }}>
                                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                                        <thead>
                                            <tr style={{ borderBottom: '2px solid var(--color-border)' }}>
                                                {['Contrato', 'Fornecedor', 'Departamento', 'Valor', 'Vencimento', 'Situação', 'Risco', 'Pedido'].map(h => (
                                                    <th key={h} style={{ padding: '10px 12px', textAlign: 'left', fontWeight: 800, fontSize: '11px', color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap' }}>{h}</th>
                                                ))}
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {items.map((item, idx) => (
                                                <tr
                                                    key={item.obligationId}
                                                    style={{
                                                        borderBottom: '1px solid var(--color-border)',
                                                        backgroundColor: idx % 2 === 0 ? 'transparent' : 'var(--color-bg-page)',
                                                        transition: 'background-color 0.15s',
                                                    }}
                                                    onMouseOver={e => (e.currentTarget.style.backgroundColor = '#f0f9ff')}
                                                    onMouseOut={e => (e.currentTarget.style.backgroundColor = idx % 2 === 0 ? 'transparent' : 'var(--color-bg-page)')}
                                                >
                                                    <td style={{ padding: '10px 12px' }}>
                                                        <div style={{ fontWeight: 800, fontSize: '13px', color: '#0f172a' }}>{item.contractNumber}</div>
                                                        <div style={{ fontSize: '11px', color: '#64748b', maxWidth: '160px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={item.contractTitle}>
                                                            {item.contractTitle}
                                                        </div>
                                                        {item.obligationLabel && (
                                                            <div style={{ fontSize: '11px', color: '#94a3b8', fontStyle: 'italic' }}>{item.obligationLabel}</div>
                                                        )}
                                                    </td>
                                                    <td style={{ padding: '10px 12px' }}>
                                                        <span style={{ fontWeight: 600, color: '#334155', maxWidth: '130px', display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={item.supplierName}>
                                                            {item.supplierName}
                                                        </span>
                                                    </td>
                                                    <td style={{ padding: '10px 12px', color: '#475569', fontWeight: 600 }}>
                                                        {item.departmentName ?? '—'}
                                                    </td>
                                                    <td style={{ padding: '10px 12px', fontWeight: 900, color: '#0f172a', whiteSpace: 'nowrap' }}>
                                                        {fmtCurrency(item.amount, item.currencyCode)}
                                                    </td>
                                                    <td style={{ padding: '10px 12px', whiteSpace: 'nowrap' }}>
                                                        <span style={{ fontSize: '13px', fontWeight: 700, color: item.forecastBucket === 'OVERDUE_NO_REQUEST' ? '#ef4444' : '#334155' }}>
                                                            {fmtDate(item.dueDateUtc)}
                                                        </span>
                                                        {item.penaltyStartDateUtc && (
                                                            <div style={{ fontSize: '10px', color: '#ef4444', fontWeight: 700 }}>
                                                                Penalidade: {fmtDate(item.penaltyStartDateUtc)}
                                                            </div>
                                                        )}
                                                    </td>
                                                    <td style={{ padding: '10px 12px' }}>
                                                        <span style={{
                                                            display: 'inline-block',
                                                            padding: '3px 8px',
                                                            borderRadius: '4px',
                                                            fontSize: '11px',
                                                            fontWeight: 800,
                                                            backgroundColor: `${BUCKET_COLORS[item.forecastBucket] ?? '#64748b'}20`,
                                                            color: BUCKET_COLORS[item.forecastBucket] ?? '#64748b',
                                                            border: `1px solid ${BUCKET_COLORS[item.forecastBucket] ?? '#64748b'}40`,
                                                            whiteSpace: 'nowrap'
                                                        }}>
                                                            {BUCKET_LABELS[item.forecastBucket] ?? item.forecastBucket}
                                                        </span>
                                                    </td>
                                                    <td style={{ padding: '10px 12px' }}>
                                                        <span style={{
                                                            display: 'inline-flex', alignItems: 'center', gap: '4px',
                                                            padding: '3px 8px',
                                                            borderRadius: '4px',
                                                            fontSize: '11px',
                                                            fontWeight: 800,
                                                            backgroundColor: `${RISK_COLORS[item.riskLevelCode] ?? '#64748b'}18`,
                                                            color: RISK_COLORS[item.riskLevelCode] ?? '#64748b',
                                                        }}>
                                                            {item.riskLevelCode === 'HIGH' && <ShieldAlert size={12} />}
                                                            {RISK_LABELS[item.riskLevelCode] ?? item.riskLevelCode}
                                                        </span>
                                                    </td>
                                                    <td style={{ padding: '10px 12px' }}>
                                                        {item.linkedRequestNumber ? (
                                                            <span style={{ fontSize: '12px', fontWeight: 700, color: '#0284c7' }}>{item.linkedRequestNumber}</span>
                                                        ) : (
                                                            <span style={{ fontSize: '12px', color: '#94a3b8' }}>—</span>
                                                        )}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>

                                {/* Pagination */}
                                {totalPages > 1 && (
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '16px', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                                        <span style={{ fontSize: '13px', color: '#64748b', fontWeight: 600 }}>
                                            {totalCount} obrigações · Página {page} de {totalPages}
                                        </span>
                                        <div style={{ display: 'flex', gap: '8px' }}>
                                            <button
                                                onClick={() => setPage(p => Math.max(1, p - 1))}
                                                disabled={page === 1}
                                                style={{ display: 'flex', alignItems: 'center', gap: '4px', padding: '6px 12px', border: '1px solid var(--color-border)', borderRadius: '6px', backgroundColor: 'var(--color-bg-page)', color: page === 1 ? '#94a3b8' : 'var(--color-text)', fontWeight: 600, fontSize: '13px', cursor: page === 1 ? 'not-allowed' : 'pointer' }}
                                            >
                                                <ChevronLeft size={14} /> Anterior
                                            </button>
                                            <button
                                                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                                                disabled={page === totalPages}
                                                style={{ display: 'flex', alignItems: 'center', gap: '4px', padding: '6px 12px', border: '1px solid var(--color-border)', borderRadius: '6px', backgroundColor: 'var(--color-bg-page)', color: page === totalPages ? '#94a3b8' : 'var(--color-text)', fontWeight: 600, fontSize: '13px', cursor: page === totalPages ? 'not-allowed' : 'pointer' }}
                                            >
                                                Próxima <ChevronRight size={14} />
                                            </button>
                                        </div>
                                    </div>
                                )}
                            </>
                        )}
                    </div>
                </>
            )}
        </div>
    );
}
