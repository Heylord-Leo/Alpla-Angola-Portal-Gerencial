import { useState, useEffect } from 'react';
import { Network, ArrowLeft, Database, Search, Cpu } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../../lib/api';

export function IntegrationHealth() {
    const [ocrStatus, setOcrStatus] = useState<any>(null);

    useEffect(() => {
        api.admin.diagnostics.getHealth().then(data => {
            setOcrStatus(data.localOcr.status === 'Healthy' || data.openAi.status === 'Healthy' ? 'Operacional' : 'Indisponível');
        }).catch(() => setOcrStatus('Erro na verificação'));
    }, []);

    const IntegrationCard = ({ name, type, status, description, isRoadmap }: { name: string, type: string, status: string, description: string, isRoadmap?: boolean }) => (
        <div style={{ 
            backgroundColor: isRoadmap ? 'var(--color-bg-main)' : 'var(--color-bg-surface)', 
            border: `2px solid ${isRoadmap ? 'var(--color-border-light)' : 'var(--color-border-heavy)'}`, 
            padding: '1.5rem',
            boxShadow: isRoadmap ? 'none' : 'var(--shadow-brutal)',
            opacity: isRoadmap ? 0.7 : 1
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1rem' }}>
                <div>
                    <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 900, textTransform: 'uppercase' }}>{name}</h3>
                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>{type}</span>
                </div>
                <span style={{ 
                    fontSize: '0.75rem', 
                    fontWeight: 800, 
                    padding: '4px 8px', 
                    backgroundColor: isRoadmap ? 'var(--color-bg-surface)' : (status === 'Operacional' ? 'var(--color-status-green)' : 'var(--color-status-red)'),
                    color: isRoadmap ? 'var(--color-text-muted)' : 'white',
                    height: 'fit-content'
                }}>
                    {status}
                </span>
            </div>
            <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>{description}</p>
            {isRoadmap && (
                <div style={{ marginTop: '1rem', fontSize: '0.7rem', fontWeight: 900, color: 'var(--color-status-blue)', textTransform: 'uppercase' }}>
                    Prevista uma fase futura
                </div>
            )}
        </div>
    );

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
            <header style={{ borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', marginBottom: '1.5rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '0.5rem' }}>
                    <Link to="/admin/workspace" style={{ color: 'var(--color-text-muted)', display: 'flex' }}>
                        <ArrowLeft size={24} />
                    </Link>
                    <div style={{ 
                        backgroundColor: 'var(--color-status-blue)', 
                        padding: '0.5rem', 
                        display: 'flex', 
                        border: '2px solid var(--color-status-blue)',
                        color: 'white'
                    }}>
                        <Network size={24} />
                    </div>
                    <div>
                        <h1 style={{ margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Saúde das Integrações
                        </h1>
                        <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' }}>
                            Estado das comunicações com sistemas externos e provedores
                        </p>
                    </div>
                </div>
            </header>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(350px, 1fr))', gap: '1.5rem' }}>
                <IntegrationCard 
                    name="Extração de Documentos" 
                    type="OCR / AI Service" 
                    status={ocrStatus || 'A verificar...'} 
                    description="Monitorização da comunicação com os serviços de extração de dados de facturas, cotações e documentos operacionais (Local OCR e OpenAI)."
                />
                <IntegrationCard 
                    name="AlplaPROD" 
                    type="Production & Supply Chain System" 
                    status="Não Disponível" 
                    description="Acompanhamento do estado das comunicações relacionadas ao AlplaPROD e aos serviços associados ao fluxo operacional e de dados produtivos."
                    isRoadmap
                />
                <IntegrationCard 
                    name="Primavera ERP" 
                    type="Enterprise Resource Planning" 
                    status="Não Disponível" 
                    description="Integração de pagamentos e fluxos financeiros com a base Primavera v10."
                    isRoadmap
                />
            </div>

            <div style={{ border: '2px dashed var(--color-border-light)', padding: '2rem', textAlign: 'center' }}>
                <div style={{ display: 'flex', justifyContent: 'center', gap: '2rem', opacity: 0.2 }}>
                    <Database size={48} />
                    <Search size={48} />
                    <Cpu size={48} />
                </div>
                <p style={{ color: 'var(--color-text-muted)', marginTop: '1rem', fontWeight: 500 }}>
                    As integrações com sistemas operacionais e ERPs estão actualmente em fase de evolução e mapeamento técnico.
                </p>
            </div>
        </div>
    );
}
