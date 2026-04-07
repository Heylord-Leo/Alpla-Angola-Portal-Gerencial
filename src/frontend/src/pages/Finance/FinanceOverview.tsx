import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { FinanceSummaryDto } from '../../types';
import { AlertCircle, CheckCircle, Clock, DollarSign } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

export default function FinanceOverview() {
    const [summary, setSummary] = useState<FinanceSummaryDto | null>(null);
    const [loading, setLoading] = useState(true);
    const navigate = useNavigate();

    useEffect(() => {
        api.finance.getSummary().then(data => {
            setSummary(data);
            setLoading(false);
        }).catch(err => {
            console.error(err);
            setLoading(false);
        });
    }, []);

    if (loading) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold' }}>Carregando Resumo...</div>;
    if (!summary) return <div style={{ padding: '60px', textAlign: 'center', color: 'red' }}>Erro ao carregar dados.</div>;

    const formatCurrency = (val: number, currency: string = '') => {
        return new Intl.NumberFormat('pt-AO', { style: 'currency', currency: currency || 'AOA' }).format(val);
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '24px' }}>
                
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-border)', boxShadow: 'var(--shadow-brutal)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
                        <span style={{ fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Aguardando Ação de Finanças</span>
                        <Clock size={24} color="#0284c7" />
                    </div>
                    <div style={{ fontSize: '3rem', fontWeight: 900, lineHeight: 1 }}>{summary.waitingFinanceAction}</div>
                    <div style={{ marginTop: '8px', fontSize: '1.2rem', fontWeight: 'bold' }}>
                        {formatCurrency(summary.pendingValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-border)', boxShadow: 'var(--shadow-brutal)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
                        <span style={{ fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Agendados</span>
                        <CheckCircle size={24} color="#ea580c" />
                    </div>
                    <div style={{ fontSize: '3rem', fontWeight: 900, lineHeight: 1 }}>{summary.scheduledPayments}</div>
                    <div style={{ marginTop: '8px', fontSize: '1.2rem', fontWeight: 'bold' }}>
                        {formatCurrency(summary.scheduledValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

                <div style={{ backgroundColor: '#fef2f2', padding: '24px', border: '2px solid #ef4444', boxShadow: '4px 4px 0 #ef4444', cursor: 'pointer' }} onClick={() => navigate('/finance/payments?filter=overdue')}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
                        <span style={{ fontWeight: 800, textTransform: 'uppercase', color: '#b91c1c' }}>Pagamentos Vencidos</span>
                        <AlertCircle size={24} color="#ef4444" />
                    </div>
                    <div style={{ fontSize: '3rem', fontWeight: 900, lineHeight: 1, color: '#b91c1c' }}>{summary.overduePayments}</div>
                    <div style={{ marginTop: '8px', fontSize: '1.2rem', fontWeight: 'bold', color: '#b91c1c' }}>
                        {formatCurrency(summary.overdueValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-border)', boxShadow: 'var(--shadow-brutal)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
                        <span style={{ fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Volume Pago no Mês</span>
                        <DollarSign size={24} color="#16a34a" />
                    </div>
                    <div style={{ fontSize: '2.5rem', fontWeight: 900, lineHeight: 1, color: '#16a34a' }}>{summary.completedThisMonth}</div>
                    <div style={{ marginTop: '8px', fontSize: '1.2rem', fontWeight: 'bold' }}>
                        {formatCurrency(summary.paidThisMonthValue, summary.currencyCodes?.[0])}
                    </div>
                </div>

            </div>

            <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-border)', boxShadow: 'var(--shadow-brutal)' }}>
                <h3 style={{ fontSize: '1.5rem', fontWeight: 900, textTransform: 'uppercase', marginBottom: '24px' }}>Atenção Imediata</h3>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: '16px' }}>
                    {summary.attentionPoints.map(point => {
                        const isDanger = point.type === 'DANGER';
                        const isWarning = point.type === 'WARNING';
                        return (
                            <div key={point.id} 
                                 onClick={() => navigate(point.targetPath)}
                                 style={{ 
                                     cursor: 'pointer', 
                                     padding: '20px', 
                                     border: `2px solid ${isDanger ? '#ef4444' : isWarning ? '#f97316' : 'var(--color-border)'}`,
                                     backgroundColor: isDanger ? '#fef2f2' : isWarning ? '#fff7ed' : '#f8fafc',
                                     display: 'flex',
                                     flexDirection: 'column',
                                     gap: '12px',
                                     transition: 'transform 0.1s'
                                 }}
                                 onMouseOver={(e) => e.currentTarget.style.transform = 'translateY(-2px)'}
                                 onMouseOut={(e) => e.currentTarget.style.transform = 'none'}
                            >
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                    <h4 style={{ margin: 0, fontWeight: 900, fontSize: '1.2rem', color: isDanger ? '#b91c1c' : isWarning ? '#c2410c' : 'var(--color-text)' }}>{point.title}</h4>
                                    <span style={{ backgroundColor: '#fff', padding: '4px 12px', borderRadius: '999px', fontWeight: 'bold', border: '1px solid #ccc' }}>{point.count}</span>
                                </div>
                                <p style={{ margin: 0, fontSize: '0.9rem', fontWeight: 500, color: '#475569' }}>{point.description}</p>
                            </div>
                        );
                    })}
                    {summary.attentionPoints.length === 0 && (
                        <div style={{ padding: '40px', textAlign: 'center', color: '#64748b', fontWeight: 600 }}>
                            Nenhuma ação imediata pendente neste momento.
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
