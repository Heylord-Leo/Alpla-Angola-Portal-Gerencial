import { useState, useEffect } from 'react';
import { Activity, ArrowLeft, CheckCircle2, XCircle, AlertCircle, Settings } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../../lib/api';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

interface ServiceStatus {
    status: string;
    configured: boolean;
    reachable: boolean;
    healthy: boolean;
    message?: string;
}

interface DiagnosisData {
    backend: ServiceStatus;
    database: ServiceStatus;
    localOcr: ServiceStatus;
    openAi: ServiceStatus;
}

export function ServiceDiagnosis() {
    const [data, setData] = useState<DiagnosisData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const fetchHealth = async () => {
        setLoading(true);
        try {
            const result = await api.admin.diagnostics.getHealth();
            setData(result);
            setError(null);
        } catch (err: any) {
            setError('Falha ao obter dados de diagnóstico.');
            console.error(err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchHealth();
    }, []);

    const StatusCard = ({ title, status, icon: Icon }: { title: string, status: ServiceStatus, icon: any }) => {
        const getStatusColor = () => {
            if (status.status === 'Healthy') return 'var(--color-status-green)';
            if (status.status === 'Unhealthy' || status.status === 'Unreachable') return 'var(--color-status-red)';
            if (status.status === 'Configured') return 'var(--color-status-blue)';
            return 'var(--color-text-muted)';
        };

        const getStatusIcon = () => {
            if (status.healthy && status.reachable) return <CheckCircle2 size={20} color="var(--color-status-green)" />;
            if (!status.configured) return <Settings size={20} color="var(--color-text-muted)" />;
            if (!status.reachable) return <XCircle size={20} color="var(--color-status-red)" />;
            return <AlertCircle size={20} color="var(--color-status-orange)" />;
        };

        return (
            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '1px solid var(--color-border)', 
                borderRadius: 'var(--radius-lg)',
                padding: '1.5rem',
                display: 'flex',
                flexDirection: 'column',
                gap: '1rem'
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                        <div style={{ padding: '0.5rem', backgroundColor: 'var(--color-bg-main)', border: '1px solid var(--color-border-heavy)' }}>
                            <Icon size={20} />
                        </div>
                        <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 800, textTransform: 'uppercase' }}>{title}</h3>
                    </div>
                    {getStatusIcon()}
                </div>

                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem' }}>
                    <Badge label="Configurado" active={status.configured} />
                    <Badge label="Acessível" active={status.reachable} />
                    <Badge label="Saudável" active={status.healthy} />
                </div>

                {status.message && (
                    <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-status-red)', fontWeight: 500 }}>
                        {status.message}
                    </p>
                )}

                <div style={{ marginTop: 'auto', paddingTop: '0.5rem', borderTop: '1px dashed var(--color-border-light)' }}>
                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: getStatusColor(), textTransform: 'uppercase' }}>
                        Estado: {status.status}
                    </span>
                </div>
            </div>
        );
    };

    const Badge = ({ label, active }: { label: string, active: boolean }) => (
        <span style={{ 
            fontSize: '0.7rem', 
            fontWeight: 700, 
            padding: '2px 6px',
            border: `1px solid ${active ? 'var(--color-border-heavy)' : 'var(--color-border-light)'}`,
            backgroundColor: active ? 'var(--color-bg-main)' : 'transparent',
            color: active ? 'var(--color-text-main)' : 'var(--color-text-muted)',
            textTransform: 'uppercase',
            opacity: active ? 1 : 0.5
        }}>
            {label}
        </span>
    );

    return (
        <PageContainer>
            <PageHeader
                title="Diagnóstico de Serviços"
                subtitle="Estado de saúde e conectividade do ecossistema"
                icon={<Activity size={32} strokeWidth={2.5} />}
                actions={
                    <button 
                        onClick={fetchHealth}
                        disabled={loading}
                        style={{
                            padding: '0.75rem 1.5rem',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-md)',
                            color: 'var(--color-text-main)',
                            fontWeight: 700,
                            cursor: 'pointer',
                            display: 'flex',
                            alignItems: 'center',
                            gap: 8
                        }}
                    >
                        {loading ? 'A verificar...' : 'Actualizar'}
                    </button>
                }
            />

            {error && (
                <div style={{ padding: '1rem', backgroundColor: 'var(--color-status-red)18', border: '1px solid var(--color-status-red)', borderRadius: 'var(--radius-md)', color: 'var(--color-status-red)', fontWeight: 700 }}>
                    {error}
                </div>
            )}

            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', 
                gap: '1.5rem' 
            }}>
                {loading && !data ? (
                    Array(4).fill(0).map((_, i) => (
                        <div key={i} style={{ height: '180px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', opacity: 0.5 }}></div>
                    ))
                ) : data ? (
                    <>
                        <StatusCard title="Backend API" status={data.backend} icon={Activity} />
                        <StatusCard title="Base de Dados" status={data.database} icon={Activity} />
                        <StatusCard title="Serviço OCR" status={data.localOcr} icon={Activity} />
                        <StatusCard title="OpenAI API" status={data.openAi} icon={Activity} />
                    </>
                ) : null}
            </div>

            <section style={{ 
                backgroundColor: 'var(--color-bg-main)', 
                border: '1px solid var(--color-border)', 
                borderRadius: 'var(--radius-lg)',
                padding: '1.5rem',
                marginTop: '1rem'
            }}>
                <h3 style={{ fontSize: '0.875rem', fontWeight: 800, textTransform: 'uppercase', marginBottom: '1rem' }}>Notas de Diagnóstico</h3>
                <ul style={{ margin: 0, paddingLeft: '1.25rem', fontSize: '0.875rem', color: 'var(--color-text-muted)', display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                    <li>A verificação de conectividade é realizada em tempo real a partir do servidor central.</li>
                    <li>O <b>Serviço OCR</b> e <b>OpenAI</b> são validados apenas se estiverem configurados como o provedor activo.</li>
                    <li>As credenciais não são expostas neste painel por motivos de segurança.</li>
                </ul>
            </section>
        </PageContainer>
    );
}
