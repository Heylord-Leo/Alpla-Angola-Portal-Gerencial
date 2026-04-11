import { useState, useEffect } from 'react';
import { Network, ArrowLeft, Database, Search, Cpu } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../../lib/api';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export function IntegrationHealth() {
    const [ocrStatus, setOcrStatus] = useState<any>(null);

    useEffect(() => {
        api.admin.diagnostics.getHealth().then(data => {
            setOcrStatus(data.localOcr.status === 'Healthy' || data.openAi.status === 'Healthy' ? 'Operacional' : 'Indisponível');
        }).catch(() => setOcrStatus('Erro na verificação'));
    }, []);

    const IntegrationCard = ({ name, type, status, description, isRoadmap }: { name: string, type: string, status: string, description: string, isRoadmap?: boolean }) => (
        <div style={{ 
            backgroundColor: isRoadmap ? 'var(--color-bg-page)' : 'var(--color-bg-surface)', 
            border: `1px solid var(--color-border)`,
            borderRadius: 'var(--radius-lg)', 
            padding: '1.5rem',
            opacity: isRoadmap ? 0.6 : 1
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
        <PageContainer>
            <PageHeader
                title="Saúde das Integrações"
                subtitle="Estado das comunicações com sistemas externos e provedores"
                icon={<Network size={32} strokeWidth={2.5} />}
            />

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

            <div style={{ border: '1px dashed var(--color-border)', borderRadius: 'var(--radius-lg)', padding: '2rem', textAlign: 'center' }}>
                <div style={{ display: 'flex', justifyContent: 'center', gap: '2rem', opacity: 0.2 }}>
                    <Database size={48} />
                    <Search size={48} />
                    <Cpu size={48} />
                </div>
                <p style={{ color: 'var(--color-text-muted)', marginTop: '1rem', fontWeight: 500 }}>
                    As integrações com sistemas operacionais e ERPs estão actualmente em fase de evolução e mapeamento técnico.
                </p>
            </div>
        </PageContainer>
    );
}
