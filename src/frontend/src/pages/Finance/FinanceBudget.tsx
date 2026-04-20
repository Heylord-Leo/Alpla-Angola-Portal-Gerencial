import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import { BookOpen, Settings, TrendingUp, AlertTriangle } from 'lucide-react';

// ─── Helpers ─────────────────────────────────────────────────────────────────

function formatCurrency(val: number, currency: string = '') {
    return new Intl.NumberFormat('pt-AO', {
        style: 'currency',
        currency: currency || 'AOA',
    }).format(val);
}

// ─── Budget KPI Card ──────────────────────────────────────────────────────────

interface BudgetKPIProps {
    label: string;
    value: string;
    sub?: string;
    color: string;
}

function BudgetKPI({ label, value, sub, color }: BudgetKPIProps) {
    return (
        <div style={{
            display: 'flex', flexDirection: 'column', gap: '4px',
            padding: '16px 20px',
            backgroundColor: 'var(--color-bg-page)',
            borderRadius: '10px',
            border: `1px solid ${color}33`,
            flex: 1,
        }}>
            <span style={{ fontSize: '11px', fontWeight: 800, textTransform: 'uppercase', color, letterSpacing: '0.06em' }}>
                {label}
            </span>
            <span style={{ fontSize: '1.4rem', fontWeight: 900, color }}>
                {value}
            </span>
            {sub && (
                <span style={{ fontSize: '11px', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                    {sub}
                </span>
            )}
        </div>
    );
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export default function FinanceBudget() {
    const navigate = useNavigate();
    const [budgetYear, setBudgetYear] = useState<number>(new Date().getFullYear());
    const [budgetData, setBudgetData] = useState<any>(null);
    const [budgetLoading, setBudgetLoading] = useState(false);
    const [selectedDeptId, setSelectedDeptId] = useState<number | null>(null);
    const [deptDetails, setDeptDetails] = useState<any[]>([]);
    const [deptDetailsLoading, setDeptDetailsLoading] = useState(false);

    useEffect(() => {
        setBudgetLoading(true);
        api.financeBudget.getOverview(budgetYear).then(data => {
            setBudgetData(data);
            setBudgetLoading(false);
        }).catch(err => {
            console.error(err);
            setBudgetLoading(false);
        });
    }, [budgetYear]);

    useEffect(() => {
        if (selectedDeptId) {
            setDeptDetailsLoading(true);
            api.financeBudget.getDepartmentDetails(selectedDeptId, budgetYear).then(data => {
                setDeptDetails(data);
                setDeptDetailsLoading(false);
            }).catch(err => {
                console.error(err);
                setDeptDetailsLoading(false);
            });
        } else {
            setDeptDetails([]);
        }
    }, [selectedDeptId, budgetYear]);

    // ── Derived data ──────────────────────────────────────────────────────────
    const budgetGlobalKPIs = (budgetData?.departments || []).reduce((acc: any, d: any) => {
        if (!acc[d.currencyCode]) {
            acc[d.currencyCode] = { budget: 0, committed: 0, paid: 0 };
        }
        acc[d.currencyCode].budget += d.totalBudget;
        acc[d.currencyCode].committed += d.committedSpend;
        acc[d.currencyCode].paid += d.paidSpend;
        return acc;
    }, {});

    const sortedDepts = [...(budgetData?.departments || [])]
        .filter((d: any) => d.totalBudget > 0)
        .sort((a, b) => {
            const pctA = a.committedSpend / a.totalBudget;
            const pctB = b.committedSpend / b.totalBudget;
            return pctB - pctA;
        });

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>

            {/* Header Row */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ fontSize: '1.4rem', fontWeight: 900, color: 'var(--color-text)', margin: 0 }}>
                        Acompanhamento Orçamental
                    </h2>
                    <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--color-text-muted)', fontWeight: 500 }}>
                        Controlo de despesa comprometida face ao orçamento aprovado por departamento.
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
                    <select
                        value={budgetYear}
                        onChange={e => { setBudgetYear(Number(e.target.value)); setSelectedDeptId(null); }}
                        style={{
                            padding: '9px 16px', borderRadius: '8px',
                            border: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-surface)',
                            fontWeight: 700, color: 'var(--color-text)', cursor: 'pointer',
                            fontSize: '14px',
                        }}
                    >
                        {[new Date().getFullYear() - 1, new Date().getFullYear(), new Date().getFullYear() + 1].map(y => (
                            <option key={y} value={y}>{y}</option>
                        ))}
                    </select>
                    <button
                        onClick={() => navigate('/finance/budget-config')}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '8px',
                            padding: '9px 18px',
                            backgroundColor: 'var(--color-bg-surface)',
                            color: 'var(--color-text)',
                            borderRadius: '8px', fontWeight: 700,
                            border: '1px solid var(--color-border)',
                            cursor: 'pointer', fontSize: '14px',
                            transition: 'all 0.2s',
                        }}
                        onMouseEnter={e => { e.currentTarget.style.backgroundColor = '#0f172a'; e.currentTarget.style.color = '#fff'; }}
                        onMouseLeave={e => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; e.currentTarget.style.color = 'var(--color-text)'; }}
                    >
                        <Settings size={15} /> Configurar Orçamento
                    </button>
                </div>
            </div>

            {budgetLoading ? (
                <div style={{ padding: '60px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', borderRadius: '12px', border: '1px solid var(--color-border)' }}>
                    <span style={{ fontWeight: 600, color: '#64748b' }}>Carregando dados de orçamento...</span>
                </div>
            ) : (
                <>
                    {/* ── Global KPI Strip ──────────────────────────────────── */}
                    {Object.keys(budgetGlobalKPIs).length === 0 ? (
                        <div style={{
                            padding: '32px', textAlign: 'center',
                            border: '2px dashed var(--color-border)', borderRadius: '12px',
                            color: 'var(--color-text-muted)', fontWeight: 600, fontSize: '14px',
                        }}>
                            Nenhum orçamento definido para {budgetYear}. Use "Configurar Orçamento" para começar.
                        </div>
                    ) : (
                        Object.entries(budgetGlobalKPIs).map(([curr, data]: [string, any], idx) => {
                            const bal = data.budget - data.committed;
                            const pct = data.budget > 0 ? (data.committed / data.budget) * 100 : 0;
                            const isOver = pct >= 100;
                            const isWarning = pct >= 80 && !isOver;
                            const statusColor = isOver ? '#ef4444' : isWarning ? '#f59e0b' : '#22c55e';
                            return (
                                <div key={curr} style={{
                                    backgroundColor: 'var(--color-bg-surface)',
                                    borderRadius: '12px',
                                    border: '1px solid var(--color-border)',
                                    boxShadow: '0 4px 6px rgba(0,0,0,0.05)',
                                    overflow: 'hidden',
                                    marginTop: idx > 0 ? '8px' : '0',
                                }}>
                                    {/* Currency header */}
                                    <div style={{
                                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                        padding: '16px 24px',
                                        borderBottom: '1px solid var(--color-border)',
                                        backgroundColor: 'var(--color-bg-page)',
                                    }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                            <TrendingUp size={18} color="#0284c7" />
                                            <span style={{ fontWeight: 800, fontSize: '15px', color: 'var(--color-text)' }}>
                                                Síntese Global {budgetYear} — {curr}
                                            </span>
                                        </div>
                                        <span style={{
                                            padding: '3px 10px', borderRadius: '6px', fontSize: '12px', fontWeight: 800,
                                            backgroundColor: isOver ? '#fef2f2' : isWarning ? '#fff7ed' : '#f0fdf4',
                                            color: statusColor,
                                            border: `1px solid ${statusColor}55`,
                                        }}>
                                            {pct.toFixed(1)}% utilizado
                                        </span>
                                    </div>

                                    {/* KPI Strip */}
                                    <div style={{ display: 'flex', gap: '12px', padding: '16px 24px', flexWrap: 'wrap' }}>
                                        <BudgetKPI
                                            label={`Orçamento (${curr})`}
                                            value={formatCurrency(data.budget, curr)}
                                            color="#0284c7"
                                        />
                                        <BudgetKPI
                                            label="Consumido"
                                            value={formatCurrency(data.committed, curr)}
                                            sub={`${pct.toFixed(1)}% do orçamento`}
                                            color="#b91c1c"
                                        />
                                        <BudgetKPI
                                            label="Pago"
                                            value={formatCurrency(data.paid, curr)}
                                            sub="Já liquidado"
                                            color="#16a34a"
                                        />
                                        <BudgetKPI
                                            label="Saldo Disponível"
                                            value={formatCurrency(bal, curr)}
                                            color={bal < 0 ? '#ef4444' : '#7c3aed'}
                                        />
                                    </div>

                                    {/* Progress Bar */}
                                    <div style={{ padding: '0 24px 20px' }}>
                                        <div style={{ height: '8px', width: '100%', backgroundColor: '#e2e8f0', borderRadius: '4px', overflow: 'hidden' }}>
                                            <div style={{
                                                height: '100%',
                                                width: `${Math.min(pct, 100)}%`,
                                                backgroundColor: statusColor,
                                                transition: 'width 0.6s ease-out',
                                                borderRadius: '4px',
                                            }} />
                                        </div>
                                    </div>
                                </div>
                            );
                        })
                    )}

                    {/* ── Department grid ───────────────────────────────────── */}
                    {sortedDepts.length > 0 && (
                        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1.5fr) minmax(0, 1fr)', gap: '24px' }}>

                            {/* Left: Top Departments at Risk */}
                            <div style={{
                                backgroundColor: 'var(--color-bg-surface)', padding: '24px',
                                border: '1px solid var(--color-border)', borderRadius: '12px',
                                boxShadow: '0 4px 6px rgba(0,0,0,0.05)',
                            }}>
                                <h3 style={{
                                    fontSize: '0.95rem', fontWeight: 700, color: 'var(--color-text)',
                                    textTransform: 'uppercase', marginBottom: '16px',
                                    display: 'flex', alignItems: 'center', gap: '8px',
                                }}>
                                    <AlertTriangle size={16} color="#f59e0b" />
                                    Departamentos por Consumo Orçamental
                                </h3>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                                    {sortedDepts.map((d: any) => {
                                        const pct = (d.committedSpend / d.totalBudget) * 100;
                                        const isOver = pct >= 100;
                                        const isWarning = pct >= 80 && !isOver;
                                        return (
                                            <div
                                                key={d.departmentId}
                                                onClick={() => setSelectedDeptId(prev => prev === d.departmentId ? null : d.departmentId)}
                                                style={{
                                                    display: 'flex', flexDirection: 'column', gap: '8px',
                                                    padding: '12px', borderRadius: '8px', cursor: 'pointer',
                                                    border: `1px solid ${selectedDeptId === d.departmentId ? 'var(--color-primary)' : '#e2e8f0'}`,
                                                    backgroundColor: selectedDeptId === d.departmentId
                                                        ? 'rgba(2,132,199,0.05)'
                                                        : 'transparent',
                                                    transition: 'all 0.15s',
                                                }}
                                                onMouseEnter={e => { if (selectedDeptId !== d.departmentId) e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                                                onMouseLeave={e => { if (selectedDeptId !== d.departmentId) e.currentTarget.style.backgroundColor = 'transparent'; }}
                                            >
                                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                    <span style={{ fontWeight: 800, fontSize: '13px', color: 'var(--color-text)' }}>
                                                        {d.departmentName}
                                                    </span>
                                                    <span style={{
                                                        padding: '2px 8px', borderRadius: '4px', fontSize: '11px', fontWeight: 800,
                                                        backgroundColor: isOver ? '#fef2f2' : isWarning ? '#fff7ed' : '#f0fdf4',
                                                        color: isOver ? '#ef4444' : isWarning ? '#f97316' : '#22c55e',
                                                        border: `1px solid ${isOver ? '#fca5a5' : isWarning ? '#fdba74' : '#86efac'}`,
                                                    }}>
                                                        {isOver ? 'OVER BUDGET' : isWarning ? 'WARNING' : 'SAFE'}
                                                    </span>
                                                </div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                    <div style={{ flex: 1, height: '6px', backgroundColor: '#e2e8f0', borderRadius: '3px', overflow: 'hidden' }}>
                                                        <div style={{
                                                            height: '100%',
                                                            width: `${Math.min(pct, 100)}%`,
                                                            backgroundColor: isOver ? '#ef4444' : isWarning ? '#f59e0b' : '#3b82f6',
                                                            transition: 'width 0.5s ease-out',
                                                        }} />
                                                    </div>
                                                    <span style={{
                                                        fontSize: '12px', fontWeight: 800, minWidth: '45px', textAlign: 'right',
                                                        color: isOver ? '#ef4444' : '#475569',
                                                    }}>
                                                        {pct.toFixed(1)}%
                                                    </span>
                                                </div>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '12px', color: '#64748b' }}>
                                                    <span><strong>Consumido:</strong> {formatCurrency(d.committedSpend, d.currencyCode)}</span>
                                                    <span><strong>Saldo:</strong> {formatCurrency(d.totalBudget - d.committedSpend, d.currencyCode)}</span>
                                                </div>
                                            </div>
                                        );
                                    })}
                                </div>
                            </div>

                            {/* Right: Drill-down Cost Centers */}
                            <div style={{
                                backgroundColor: 'var(--color-bg-surface)', padding: '24px',
                                border: '1px solid var(--color-border)', borderRadius: '12px',
                                boxShadow: '0 4px 6px rgba(0,0,0,0.05)',
                                display: 'flex', flexDirection: 'column',
                            }}>
                                <h3 style={{
                                    fontSize: '0.95rem', fontWeight: 700, color: 'var(--color-text)',
                                    textTransform: 'uppercase', marginBottom: '16px',
                                }}>
                                    Centros de Custo
                                    {selectedDeptId && ` — ${sortedDepts.find(d => d.departmentId === selectedDeptId)?.departmentName || ''}`}
                                </h3>

                                {!selectedDeptId ? (
                                    <div style={{
                                        flex: 1, display: 'flex', flexDirection: 'column',
                                        alignItems: 'center', justifyContent: 'center',
                                        color: '#64748b', fontSize: '14px',
                                        border: '2px dashed #cbd5e1', borderRadius: '8px',
                                        padding: '32px', textAlign: 'center',
                                    }}>
                                        <BookOpen size={28} color="#94a3b8" style={{ marginBottom: '12px' }} />
                                        Selecione um departamento à esquerda para ver a distribuição por centro de custo.
                                    </div>
                                ) : deptDetailsLoading ? (
                                    <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#64748b', fontWeight: 600 }}>
                                        Carregando detalhe...
                                    </div>
                                ) : deptDetails.length === 0 ? (
                                    <div style={{
                                        flex: 1, border: '2px dashed #cbd5e1', borderRadius: '8px',
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        color: '#64748b', fontSize: '14px', padding: '24px', textAlign: 'center',
                                    }}>
                                        Nenhuma despesa registada para este departamento em {budgetYear}.
                                    </div>
                                ) : (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', overflowY: 'auto' }}>
                                        {deptDetails.map((cc, idx) => (
                                            <div key={idx} style={{
                                                display: 'flex', flexDirection: 'column', gap: '4px',
                                                paddingBottom: '12px',
                                                borderBottom: idx < deptDetails.length - 1 ? '1px solid #e2e8f0' : 'none',
                                            }}>
                                                <span style={{ fontWeight: 800, fontSize: '13px', color: '#334155' }}>
                                                    {cc.costCenterName}
                                                </span>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                    <span style={{ fontSize: '12px', color: '#64748b' }}>Comprometido</span>
                                                    <span style={{ fontWeight: 800, fontSize: '13px', color: '#b91c1c' }}>
                                                        {formatCurrency(cc.committedSpend, cc.currencyCode)}
                                                    </span>
                                                </div>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                    <span style={{ fontSize: '12px', color: '#64748b' }}>Pago (incluído)</span>
                                                    <span style={{ fontWeight: 700, fontSize: '13px', color: '#22c55e' }}>
                                                        {formatCurrency(cc.paidSpend, cc.currencyCode)}
                                                    </span>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>
                    )}
                </>
            )}
        </div>
    );
}
