import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { useNavigate } from 'react-router-dom';
import { KPICard } from '../../components/ui/KPICard';
import { Users, Clock, CalendarX, CalendarClock, RefreshCw } from 'lucide-react';

export default function HROverview() {
    const [stats, setStats] = useState<any>(null);
    const [loading, setLoading] = useState(true);
    const navigate = useNavigate();

    useEffect(() => {
        api.hrLeave.getDashboard().then(data => {
            setStats(data);
            setLoading(false);
        }).catch(err => {
            console.error(err);
            setLoading(false);
        });
    }, []);

    if (loading) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold', color: '#64748b' }}>Carregando Portal RH...</div>;
    if (!stats) return <div style={{ padding: '60px', textAlign: 'center', color: '#ef4444', fontWeight: 'bold' }}>Erro ao carregar dados do dashboard.</div>;

    const formatDate = (dateStr: string) => {
        if (!dateStr) return '';
        const d = new Date(dateStr);
        return d.toLocaleDateString('pt-AO') + ' ' + d.toLocaleTimeString('pt-AO', { hour: '2-digit', minute: '2-digit' });
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 800, color: '#0f172a' }}>Portal RH</h2>
                    <p style={{ margin: '4px 0 0 0', color: '#64748b', fontWeight: 500 }}>
                        Visão operacional de férias e ausências.
                    </p>
                </div>
                {stats.lastSync && (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', color: '#94a3b8', fontWeight: 600, backgroundColor: 'var(--color-bg-surface)', padding: '8px 16px', borderRadius: '8px', border: '1px solid var(--color-border)', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                        <RefreshCw size={14} color="#64748b" />
                        Última Sincronização INNUX: {formatDate(stats.lastSync.completedAtUtc)}
                    </div>
                )}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '24px' }}>
                <KPICard
                    title="Aguardando Aprovação"
                    value={stats.pendingLeave}
                    icon={<Clock size={20} />}
                    color="#f59e0b"
                    borderColor="#fcd34d"
                    onClick={() => navigate('/hr/leave?status=Submitted')}
                />
                <KPICard
                    title="Ausentes Hoje"
                    value={stats.absentToday}
                    icon={<CalendarX size={20} />}
                    color="#ef4444"
                    borderColor="#fca5a5"
                    onClick={() => navigate('/hr/team-calendar')}
                />
                <KPICard
                    title="Férias (Próx. 7 Dias)"
                    value={stats.upcomingLeave}
                    icon={<CalendarClock size={20} />}
                    color="#0ea5e9"
                    borderColor="#7dd3fc"
                    onClick={() => navigate('/hr/team-calendar')}
                />
                <KPICard
                    title="Equipe Ativa"
                    value={stats.activeEmployees}
                    icon={<Users size={20} />}
                    color="#10b981"
                    borderColor="#6ee7b7"
                    onClick={() => navigate('/hr/employees')}
                />
            </div>
            
            <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: '12px', border: '1px solid var(--color-border)', boxShadow: '0 4px 6px rgba(0,0,0,0.05)' }}>
                <h3 style={{ margin: '0 0 16px 0', fontSize: '1.2rem', fontWeight: 800, color: '#0f172a' }}>Gestão de Ausências</h3>
                <p style={{ margin: '0 0 16px 0', color: '#475569', lineHeight: 1.6, fontWeight: 500 }}>
                    Este painel centraliza as requisições de férias e ausências da sua equipe.
                    Você pode aprovar solicitações pendentes, verificar o cronograma da equipe para garantir a cobertura operacional e gerenciar o cadastro de colaboradores.
                </p>
                <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <button 
                        onClick={() => navigate('/hr/leave')}
                        style={{ padding: '8px 16px', backgroundColor: '#0ea5e9', color: '#fff', border: 'none', borderRadius: '8px', fontWeight: 600, cursor: 'pointer', transition: 'background-color 0.2s', boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}
                        onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#0284c7'}
                        onMouseOut={(e) => e.currentTarget.style.backgroundColor = '#0ea5e9'}
                    >
                        Ver Solicitações
                    </button>
                    <button 
                        onClick={() => navigate('/hr/team-calendar')}
                        style={{ padding: '8px 16px', backgroundColor: 'var(--color-bg-surface)', color: '#475569', border: '1px solid var(--color-border)', borderRadius: '8px', fontWeight: 600, cursor: 'pointer', transition: 'all 0.2s' }}
                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#f1f5f9'; e.currentTarget.style.color = '#0f172a'; }}
                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; e.currentTarget.style.color = '#475569'; }}
                    >
                        Acessar Calendário
                    </button>
                </div>
            </div>
        </div>
    );
}
