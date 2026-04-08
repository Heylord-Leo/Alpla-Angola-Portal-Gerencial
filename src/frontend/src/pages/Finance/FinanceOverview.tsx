import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { FinanceSummaryDto } from '../../types';
import { AlertCircle, CheckCircle, Clock, DollarSign, TrendingUp, Presentation, AlertTriangle, BookOpen, X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { 
    BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip as RechartsTooltip, ResponsiveContainer, 
    PieChart, Pie, Cell, Legend 
} from 'recharts';

export default function FinanceOverview() {
    const [summary, setSummary] = useState<FinanceSummaryDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [showHelp, setShowHelp] = useState(false);
    
    const [companies, setCompanies] = useState<any[]>([]);
    const [selectedCompanyId, setSelectedCompanyId] = useState<number | null>(null);
    
    const navigate = useNavigate();

    useEffect(() => {
        api.lookups.getCompanies(false).then((data: any[]) => {
            setCompanies(data);
        }).catch((err: any) => console.error(err));
    }, []);

    useEffect(() => {
        setLoading(true);
        api.finance.getSummary(selectedCompanyId || undefined).then(data => {
            setSummary(data);
            setLoading(false);
        }).catch(err => {
            console.error(err);
            setLoading(false);
        });
    }, [selectedCompanyId]);

    if (loading) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold' }}>Carregando Dashboard Financeiro...</div>;
    if (!summary) return <div style={{ padding: '60px', textAlign: 'center', color: 'red' }}>Erro ao carregar dados.</div>;

    const formatCurrency = (val: number, currency: string = '') => {
        return new Intl.NumberFormat('pt-AO', { style: 'currency', currency: currency || 'AOA' }).format(val);
    };

    // Recharts Data Prep
    const PIE_COLORS = ['#3b82f6', '#f97316', '#22c55e', '#a855f7'];

    // Process CashFlow
    const cashFlowData = summary.cashFlowProjections?.map(c => ({
        name: c.date.split('-').reverse().slice(0,2).join('/'), // DD/MM
        value: c.totalAmount,
        currency: c.currencyCode
    })) || [];

    // Process Aging
    const agingData = summary.agingAnalysis ? [
        { name: '0-2 Dias', items: summary.agingAnalysis.zeroToTwoDays, color: '#22c55e' },
        { name: '3-5 Dias', items: summary.agingAnalysis.threeToFiveDays, color: '#eab308' },
        { name: '+5 Dias', items: summary.agingAnalysis.moreThanFiveDays, color: '#ef4444' }
    ] : [];

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
            
            {/* Action Bar: Company Switcher + Help Button */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px', marginBottom: '-16px' }}>
                <div style={{ display: 'flex', gap: '8px', overflowX: 'auto', paddingBottom: '4px' }}>
                    <button
                        onClick={() => setSelectedCompanyId(null)}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: selectedCompanyId === null ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                            color: selectedCompanyId === null ? '#fff' : 'var(--color-text-muted)',
                            border: selectedCompanyId === null ? '1px solid var(--color-primary)' : '1px solid var(--color-border)',
                            borderRadius: '8px',
                            fontWeight: 600,
                            fontSize: '14px',
                            cursor: 'pointer',
                            transition: 'all 0.2s',
                            boxShadow: selectedCompanyId === null ? '0 4px 6px rgba(0,0,0,0.1)' : '0 1px 2px rgba(0,0,0,0.05)',
                        }}
                    >
                        Consolidado Global
                    </button>
                    {companies.map(c => (
                        <button
                            key={c.id}
                            onClick={() => setSelectedCompanyId(c.id)}
                            style={{
                                padding: '8px 16px',
                                backgroundColor: selectedCompanyId === c.id ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                                color: selectedCompanyId === c.id ? '#fff' : 'var(--color-text-muted)',
                                border: selectedCompanyId === c.id ? '1px solid var(--color-primary)' : '1px solid var(--color-border)',
                                borderRadius: '8px',
                                fontWeight: 600,
                                fontSize: '14px',
                                cursor: 'pointer',
                                transition: 'all 0.2s',
                                boxShadow: selectedCompanyId === c.id ? '0 4px 6px rgba(0,0,0,0.1)' : '0 1px 2px rgba(0,0,0,0.05)',
                            }}
                        >
                            {c.name}
                        </button>
                    ))}
                </div>

                <button
                    onClick={() => setShowHelp(true)}
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        padding: '8px 16px',
                        backgroundColor: 'var(--color-bg-surface)',
                        color: 'var(--color-text-muted)',
                        border: '1px solid var(--color-border)',
                        borderRadius: '8px',
                        fontWeight: 600,
                        fontSize: '14px',
                        cursor: 'pointer',
                        boxShadow: '0 1px 2px rgba(0,0,0,0.05)',
                        transition: 'all 0.2s'
                    }}
                    onMouseOver={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-subtle)'; e.currentTarget.style.color = 'var(--color-text)'; }}
                    onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; e.currentTarget.style.color = 'var(--color-text-muted)'; }}
                >
                    <BookOpen size={18} /> Guia Financeiro
                </button>
            </div>

            {/* KPI Cards Row */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '24px' }}>
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', borderRadius: '12px', border: '1px solid var(--color-border)', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px' }}>
                        <span style={{ fontWeight: 600, fontSize: '13px', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Aguardando Ação</span>
                        <Clock size={20} color="#0284c7" />
                    </div>
                    <div style={{ fontSize: '2.5rem', fontWeight: 700, lineHeight: 1, color: 'var(--color-text)' }}>{summary.waitingFinanceAction}</div>
                    <div style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                        {formatCurrency(summary.pendingValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', borderRadius: '12px', border: '1px solid var(--color-border)', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px' }}>
                        <span style={{ fontWeight: 600, fontSize: '13px', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Agendados</span>
                        <CheckCircle size={20} color="#ea580c" />
                    </div>
                    <div style={{ fontSize: '2.5rem', fontWeight: 700, lineHeight: 1, color: 'var(--color-text)' }}>{summary.scheduledPayments}</div>
                    <div style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                        {formatCurrency(summary.scheduledValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

                <div style={{ backgroundColor: '#fff', padding: '24px', borderRadius: '12px', border: '1px solid #fca5a5', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', cursor: 'pointer', transition: 'all 0.2s', display: 'flex', flexDirection: 'column', gap: '8px' }} 
                     onClick={() => navigate('/finance/payments?filter=overdue')}
                     onMouseOver={(e) => { e.currentTarget.style.transform = 'translateY(-2px)'; e.currentTarget.style.boxShadow = '0 6px 12px rgba(239, 68, 68, 0.1)'; }}
                     onMouseOut={(e) => { e.currentTarget.style.transform = 'none'; e.currentTarget.style.boxShadow = '0 4px 6px rgba(0,0,0,0.05)'; }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px' }}>
                        <span style={{ fontWeight: 600, fontSize: '13px', textTransform: 'uppercase', color: '#ef4444' }}>Pagtos. Vencidos</span>
                        <AlertCircle size={20} color="#ef4444" />
                    </div>
                    <div style={{ fontSize: '2.5rem', fontWeight: 700, lineHeight: 1, color: '#ef4444' }}>{summary.overduePayments}</div>
                    <div style={{ fontSize: '1.1rem', fontWeight: 600, color: '#ef4444' }}>
                        {formatCurrency(summary.overdueValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', borderRadius: '12px', border: '1px solid var(--color-border)', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px' }}>
                        <span style={{ fontWeight: 600, fontSize: '13px', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Pago no Mês</span>
                        <DollarSign size={20} color="#16a34a" />
                    </div>
                    <div style={{ fontSize: '2.5rem', fontWeight: 700, lineHeight: 1, color: '#16a34a' }}>{summary.completedThisMonth}</div>
                    <div style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                        {formatCurrency(summary.paidThisMonthValue, summary.currencyCodes?.[0])}
                    </div>
                </div>
            </div>

            {/* Analytics Row 1: Diagramming */}
            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '24px' }}>
                
                {/* Projeção de Fluxo de Caixa */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', padding: '24px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', marginBottom: '8px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <TrendingUp size={20} color="#0284c7" /> Projeção de Fluxo de Caixa
                    </h3>
                    <p style={{ margin: '0 0 24px 0', color: '#64748b', fontSize: '14px', fontWeight: 500 }}>Previsão de saída para os próximos 15 dias baseado em agendamentos faturados.</p>
                    {cashFlowData.length > 0 ? (
                        <div style={{ height: '300px', width: '100%' }}>
                            <ResponsiveContainer width="100%" height="100%">
                                <BarChart data={cashFlowData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                                    <CartesianGrid strokeDasharray="3 3" vertical={false} />
                                    <XAxis dataKey="name" tick={{ fontSize: 12, fontWeight: 700 }} tickLine={false} axisLine={false} />
                                    <YAxis tickFormatter={(val) => `AOA ${(val / 1000000).toFixed(1)}M`} tick={{ fontSize: 12, fontWeight: 700 }} axisLine={false} tickLine={false} />
                                    <RechartsTooltip cursor={{ fill: '#f1f5f9' }} formatter={(value: any, _name: any, props: any) => [formatCurrency(Number(value), props.payload.currency), 'Projeção']} labelStyle={{ fontWeight: 'bold', color: '#0f172a' }} />
                                    <Bar dataKey="value" fill="#0284c7" radius={[4, 4, 0, 0]} />
                                </BarChart>
                            </ResponsiveContainer>
                        </div>
                    ) : (
                        <div style={{ height: '300px', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: '#64748b', fontWeight: 600, border: '2px dashed #cbd5e1', padding: '24px', textAlign: 'center', backgroundColor: '#f8fafc' }}>
                            <TrendingUp size={36} color="#94a3b8" style={{ marginBottom: '16px' }} />
                            <span style={{ fontSize: '15px', color: '#334155' }}>Sem agendamentos a lançar</span>
                            <span style={{ marginTop: '8px', fontSize: '13px', maxWidth: '350px' }}>
                                Quando existirem pagamentos agendados nos próximos 15 dias, preencheremos este quadro com as métricas de saída de caixa (por moeda). Esta ferramenta é crucial para prevenir estilhaçamento orçamental e prever picos de despesas.
                            </span>
                        </div>
                    )}
                </div>

                {/* Exposição Cambial */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', padding: '24px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', marginBottom: '8px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Presentation size={20} color="#ea580c" /> Exposição Cambial
                    </h3>
                    <p style={{ margin: '0 0 24px 0', color: '#64748b', fontSize: '14px', fontWeight: 500 }}>Montantes pendentes fragmentados por moeda circulante.</p>
                    
                    {summary.currencyExposures && summary.currencyExposures.length > 0 ? (
                        <div style={{ height: '250px', width: '100%' }}>
                            <ResponsiveContainer width="100%" height="100%">
                                <PieChart>
                                    <Pie data={summary.currencyExposures} dataKey="amount" nameKey="currencyCode" cx="50%" cy="50%" innerRadius={60} outerRadius={80} paddingAngle={5}>
                                        {summary.currencyExposures.map((_entry, index) => (
                                            <Cell key={`cell-${index}`} fill={PIE_COLORS[index % PIE_COLORS.length]} />
                                        ))}
                                    </Pie>
                                    <RechartsTooltip formatter={(value: any, name: any) => [formatCurrency(Number(value), String(name)), 'Montante']} />
                                    <Legend verticalAlign="bottom" height={36} iconType="circle" />
                                </PieChart>
                            </ResponsiveContainer>
                        </div>
                    ) : (
                        <div style={{ height: '250px', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: '#64748b', fontWeight: 600, border: '2px dashed #cbd5e1', padding: '24px', textAlign: 'center', backgroundColor: '#f8fafc' }}>
                            <Presentation size={36} color="#94a3b8" style={{ marginBottom: '16px' }} />
                            <span style={{ fontSize: '15px', color: '#334155' }}>Nenhum valor em risco</span>
                            <span style={{ marginTop: '8px', fontSize: '13px', maxWidth: '300px' }}>
                                Tudo tranquilo. Quando entrarem faturas indexadas a Dólares (USD), Euros (EUR) ou outras moedas circulantes, segregaremos os volumes retidos em gráficos de fatias aqui.
                            </span>
                        </div>
                    )}
                </div>
            </div>

            {/* Analytics Row 2: Operational Data */}
            <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1.5fr)', gap: '24px' }}>
                
                {/* Aging */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '1px solid var(--color-border)', borderRadius: '12px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', marginBottom: '24px' }}>Idade da Fila (Aging)</h3>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        {agingData.map((d) => {
                            const maxItems = Math.max(...agingData.map(x=>x.items), 1);
                            return (
                                <div key={d.name} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                                    <span style={{ fontWeight: 700, fontSize: '14px', color: '#475569' }}>{d.name}</span>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                        <div style={{ height: '10px', width: `${Math.min((d.items / maxItems) * 100, 100)}px`, backgroundColor: d.color, borderRadius: '4px' }}></div>
                                        <span style={{ fontWeight: 900, fontSize: '18px', color: d.color }}>{d.items}</span>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                </div>

                {/* Concentração de Pedidos (Top Fornecedores) */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '1px solid var(--color-border)', borderRadius: '12px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', marginBottom: '24px' }}>Top 5 Fornecedores Pendentes</h3>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        {summary.topSuppliers && summary.topSuppliers.length > 0 ? summary.topSuppliers.map((supplier, idx) => (
                            <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingBottom: '8px', borderBottom: idx < summary.topSuppliers.length - 1 ? '1px dashed #e2e8f0' : 'none' }}>
                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontWeight: 800, fontSize: '13px', color: '#0f172a', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: '130px' }} title={supplier.supplierName}>{supplier.supplierName}</span>
                                    <span style={{ fontWeight: 600, fontSize: '11px', color: '#94a3b8' }}>{supplier.requestCount} pedido(s)</span>
                                </div>
                                <span style={{ fontWeight: 800, fontSize: '14px', color: 'var(--color-primary)', textAlign: 'right' }}>
                                    {formatCurrency(supplier.totalPendingAmount, supplier.currencyCode)}
                                </span>
                            </div>
                        )) : (
                            <div style={{ padding: '16px', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: '#64748b', fontWeight: 600, textAlign: 'center', border: '2px dashed #cbd5e1', backgroundColor: '#f8fafc', flex: 1 }}>
                                <span style={{ fontSize: '13px' }}>
                                    Sem dados nominais. O sistema elencará automaticamente as cinco entidades físicas perante as quais o grupo Alpla tem as mais pesadas dívidas a saldar no curto prazo.
                                </span>
                            </div>
                        )}
                    </div>
                </div>

                {/* Atenção Imediata (Refactored to be narrower) */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '1px solid var(--color-border)', borderRadius: '12px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', display: 'flex', flexDirection: 'column' }}>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <AlertTriangle size={20} color="#b91c1c" /> Atenção Imediata
                    </h3>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', flex: 1, overflowY: 'auto' }}>
                        {summary.attentionPoints.map(point => {
                            const isDanger = point.type === 'DANGER';
                            const isWarning = point.type === 'WARNING';
                            return (
                                <div key={point.id} 
                                     onClick={() => navigate(point.targetPath)}
                                     style={{ 
                                         cursor: 'pointer', 
                                         padding: '16px', 
                                         border: `2px solid ${isDanger ? '#ef4444' : isWarning ? '#f97316' : 'var(--color-border)'}`,
                                         backgroundColor: isDanger ? '#fef2f2' : isWarning ? '#fff7ed' : '#f8fafc',
                                         display: 'flex',
                                         flexDirection: 'column',
                                         gap: '8px',
                                         transition: 'transform 0.1s'
                                     }}
                                     onMouseOver={(e) => e.currentTarget.style.transform = 'translateY(-2px)'}
                                     onMouseOut={(e) => e.currentTarget.style.transform = 'none'}
                                >
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                        <h4 style={{ margin: 0, fontWeight: 900, fontSize: '1.05rem', color: isDanger ? '#b91c1c' : isWarning ? '#c2410c' : 'var(--color-text)' }}>{point.title}</h4>
                                        <span style={{ backgroundColor: '#fff', padding: '2px 8px', borderRadius: '999px', fontWeight: 'bold', border: '1px solid #ccc', fontSize: '12px' }}>{point.count}</span>
                                    </div>
                                    <p style={{ margin: 0, fontSize: '13px', fontWeight: 600, color: '#475569' }}>{point.description}</p>
                                </div>
                            );
                        })}
                        {summary.attentionPoints.length === 0 && (
                            <div style={{ padding: '20px', textAlign: 'center', color: '#64748b', fontWeight: 600, border: '2px dashed #cbd5e1', flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                Nenhuma ação imediata pendente.
                            </div>
                        )}
                    </div>
                </div>


            </div>

            {/* HELP OVERLAY (MODAL) */}
            {showHelp && (
                <div style={{ 
                    position: 'fixed', top: 0, left: 0, width: '100vw', height: '100vh', 
                    backgroundColor: 'rgba(15, 23, 42, 0.7)', zIndex: 9999, 
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    backdropFilter: 'blur(4px)'
                }}>
                    <div style={{
                        backgroundColor: '#fff', border: '4px solid #0f172a',
                        width: '90%', maxWidth: '750px', maxHeight: '90vh', overflowY: 'auto',
                        boxShadow: '16px 16px 0 #0f172a', padding: '32px', position: 'relative'
                    }}>
                        <button 
                            onClick={() => setShowHelp(false)}
                            style={{
                                position: 'absolute', top: '16px', right: '16px', backgroundColor: 'transparent',
                                border: 'none', cursor: 'pointer', color: '#64748b'
                            }}
                            onMouseOver={(e) => e.currentTarget.style.color = '#0f172a'}
                            onMouseOut={(e) => e.currentTarget.style.color = '#64748b'}
                        >
                            <X size={32} />
                        </button>
                        
                        <h2 style={{ fontSize: '2rem', fontWeight: 900, textTransform: 'uppercase', marginBottom: '8px', borderBottom: '4px solid #e2e8f0', paddingBottom: '16px' }}>
                            Guia do Dashboard
                        </h2>
                        <p style={{ fontWeight: 500, fontSize: '15px', color: '#475569', marginBottom: '32px' }}>
                            Este glossário detalha o funcionamento orgânico do seu Centro de Comando Financeiro.
                        </p>

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            
                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                <div style={{ backgroundColor: '#e0f2fe', padding: '12px', color: '#0284c7', border: '2px solid #0284c7' }}><TrendingUp size={24} /></div>
                                <div>
                                    <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Projeção de Fluxo de Caixa</h4>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>O que é:</strong> Um gráfico de montanhas registrando todas as despesas que estão pendentes e possuem uma "Data de Solicitação" ou "Data Agendada" nos próximos 15 dias.
                                    </p>
                                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                        <em>Exemplo prático:</em> Se o banco liquidará 5 faturas de 1 Milhão no dia 12, você notará uma barra gigantesca disparando na régua do dia 12 para que o tesoureiro consiga provisionar verba horas antes do fato decorrer.
                                    </p>
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                <div style={{ backgroundColor: '#ffedd5', padding: '12px', color: '#ea580c', border: '2px solid #ea580c' }}><Presentation size={24} /></div>
                                <div>
                                    <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Exposição Cambial</h4>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>O que é:</strong> Fatiamento de toda a dívida global da Alpla em diferentes moedas usando um modelo em fatias.
                                    </p>
                                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                        <em>Exemplo prático:</em> A fatia do Euro (EUR) de repente atinge 70% do gráfico principal. Dado o cenário inflacionário angolano, isso indica que precisamos comprar Euros o mais rápido possível antes que a taxa suba e as margens despenquem.
                                    </p>
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                <div style={{ backgroundColor: '#f3f4f6', padding: '12px', color: '#1e293b', border: '2px solid #1e293b' }}><Clock size={24} /></div>
                                <div>
                                    <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Idade da Fila (Aging)</h4>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>O que é:</strong> Mostra há quanto tempo os processos estão parados na fila de trabalho das Finanças, sem avanço. O objetivo é identificar rapidamente o que está dentro do prazo e o que já está demorando mais do que o esperado.
                                    </p>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>Como ler:</strong><br />
                                        Os processos são agrupados por faixa de tempo, por exemplo:
                                    </p>
                                    <ul style={{ margin: '0 0 16px 20px', fontSize: '14px', color: '#334155', lineHeight: 1.6 }}>
                                        <li><strong>0-2 dias</strong> &rarr; itens recentes, ainda dentro do fluxo normal</li>
                                        <li><strong>3-5 dias</strong> &rarr; itens que pedem atenção</li>
                                        <li><strong>+5 dias</strong> &rarr; itens com atraso ou risco de impacto no pagamento</li>
                                    </ul>
                                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                                        <em>Exemplo prático:</em> Se houver muitos processos na faixa <strong>+5 dias</strong>, isso pode indicar acúmulo, atraso de validação ou risco de perder prazos acordados com fornecedores.
                                    </p>
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                <div style={{ backgroundColor: '#fce7f3', padding: '12px', color: '#db2777', border: '2px solid #db2777' }}><AlertCircle size={24} /></div>
                                <div>
                                    <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>Concentração de Top 5 Fornecedores</h4>
                                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                                        <strong>O que é:</strong> Elenca passivamente as entidades corporativas pesando o maior volume nominal de dinheiro nos fluxos ativos do grupo atual.
                                    </p>
                                </div>
                            </div>

                        </div>
                    </div>
                </div>
            )}

        </div>
    );
}
