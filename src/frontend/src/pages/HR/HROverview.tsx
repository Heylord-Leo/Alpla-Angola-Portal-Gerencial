import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { useNavigate } from 'react-router-dom';
import { KPICard } from '../../components/ui/KPICard';
import { Users, Clock, CalendarX, CalendarClock, RefreshCw, AlertTriangle, UserX, ServerCrash } from 'lucide-react';

export default function HROverview() {
    const [data, setData] = useState<any>(null);
    const [loading, setLoading] = useState(true);
    const navigate = useNavigate();

    useEffect(() => {
        api.hrLeave.getDashboard().then(res => {
            setData(res);
            setLoading(false);
        }).catch(err => {
            console.error(err);
            setLoading(false);
        });
    }, []);

    if (loading) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold', color: '#64748b' }}>Carregando Portal RH...</div>;
    if (!data) return <div style={{ padding: '60px', textAlign: 'center', color: '#ef4444', fontWeight: 'bold' }}>Erro ao carregar dados do dashboard.</div>;

    const formatDate = (dateStr: string) => {
        if (!dateStr) return '';
        const d = new Date(dateStr);
        return d.toLocaleDateString('pt-AO') + ' ' + d.toLocaleTimeString('pt-AO', { hour: '2-digit', minute: '2-digit' });
    };

    const { currentSituation, actionRequired, lastSync } = data;
    const hasActions = actionRequired.missingMappings > 0 || actionRequired.staleRequests > 0 || actionRequired.syncIssuesCount > 0;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 800, color: '#0f172a' }}>Command Center RH</h2>
                    <p style={{ margin: '4px 0 0 0', color: '#64748b', fontWeight: 500 }}>
                        Visão operacional orientada a ação.
                    </p>
                </div>
                {lastSync && (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', color: '#94a3b8', fontWeight: 600, backgroundColor: 'var(--color-bg-surface)', padding: '8px 16px', borderRadius: '8px', border: '1px solid var(--color-border)', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                        <RefreshCw size={14} color="#64748b" />
                        Última Sincronização INNUX: {formatDate(lastSync.completedAtUtc)}
                    </div>
                )}
            </div>

            {hasActions && (
                <div>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: '#b91c1c', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <AlertTriangle size={18} /> Ação Necessária
                    </h3>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '16px' }}>
                        {actionRequired.missingMappings > 0 && (
                            <div 
                                onClick={() => navigate('/hr/employees')} 
                                style={{ padding: '16px', backgroundColor: '#fef2f2', border: '1px solid #fca5a5', borderRadius: '8px', cursor: 'pointer', transition: 'all 0.2s', display: 'flex', gap: '12px', alignItems: 'center', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}
                            >
                                <div style={{ backgroundColor: '#fee2e2', padding: '10px', borderRadius: '50%', color: '#ef4444' }}><UserX size={20} /></div>
                                <div>
                                    <div style={{ fontWeight: 800, color: '#991b1b', fontSize: '1.25rem' }}>{actionRequired.missingMappings}</div>
                                    <div style={{ fontSize: '0.875rem', color: '#b91c1c', fontWeight: 600 }}>Equipe sem Mapeamento (RH)</div>
                                </div>
                            </div>
                        )}
                        {actionRequired.staleRequests > 0 && (
                            <div 
                                onClick={() => navigate('/hr/leave?status=Submitted')} 
                                style={{ padding: '16px', backgroundColor: '#fffbeb', border: '1px solid #fcd34d', borderRadius: '8px', cursor: 'pointer', transition: 'all 0.2s', display: 'flex', gap: '12px', alignItems: 'center', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}
                            >
                                <div style={{ backgroundColor: '#fef3c7', padding: '10px', borderRadius: '50%', color: '#f59e0b' }}><Clock size={20} /></div>
                                <div>
                                    <div style={{ fontWeight: 800, color: '#b45309', fontSize: '1.25rem' }}>{actionRequired.staleRequests}</div>
                                    <div style={{ fontSize: '0.875rem', color: '#d97706', fontWeight: 600 }}>Aprovações Pendentes (+48h)</div>
                                </div>
                            </div>
                        )}
                        {actionRequired.syncIssuesCount > 0 && (
                            <div 
                                style={{ padding: '16px', backgroundColor: '#faf5ff', border: '1px solid #d8b4fe', borderRadius: '8px', display: 'flex', gap: '12px', alignItems: 'center', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}
                            >
                                <div style={{ backgroundColor: '#f3e8ff', padding: '10px', borderRadius: '50%', color: '#a855f7' }}><ServerCrash size={20} /></div>
                                <div>
                                    <div style={{ fontWeight: 800, color: '#7e22ce', fontSize: '1.25rem' }}>Falha</div>
                                    <div style={{ fontSize: '0.875rem', color: '#9333ea', fontWeight: 600 }}>Sincronização INNUX Falhou</div>
                                </div>
                            </div>
                        )}
                    </div>
                </div>
            )}

            <div>
                <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: '#334155', marginBottom: '16px' }}>
                    Situação Atual
                </h3>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '20px' }}>
                    <KPICard
                        title="Ausentes Hoje"
                        value={currentSituation.absentToday}
                        icon={<CalendarX size={20} />}
                        color="#ef4444"
                        borderColor="#fca5a5"
                        onClick={() => navigate('/hr/team-calendar')}
                    />
                    <KPICard
                        title="Em Férias (Próx 7 Dias)"
                        value={currentSituation.upcomingLeave}
                        icon={<CalendarClock size={20} />}
                        color="#0ea5e9"
                        borderColor="#7dd3fc"
                        onClick={() => navigate('/hr/team-calendar')}
                    />
                    <KPICard
                        title="Aguardando Análise"
                        value={currentSituation.pendingLeave}
                        icon={<Clock size={20} />}
                        color="#f59e0b"
                        borderColor="#fcd34d"
                        onClick={() => navigate('/hr/leave?status=Submitted')}
                    />
                    <KPICard
                        title="Efetivo Ativo Mapeado"
                        value={currentSituation.activeEmployees}
                        icon={<Users size={20} />}
                        color="#10b981"
                        borderColor="#6ee7b7"
                        onClick={() => navigate('/hr/employees')}
                    />
                </div>
            </div>
            
        </div>
    );
}
