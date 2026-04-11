import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import { DashboardSummaryDto } from '../../types';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { KPICard } from '../../components/ui/KPICard';
import { QuickActions } from './components/QuickActions';
import { AttentionList } from './components/AttentionList';
import { WorkflowInteractive } from './components/WorkflowInteractive';
import { WorkflowStageDetails } from './components/WorkflowStageDetails';
import { WORKFLOW_STAGES } from './components/workflowData';
import { LayoutDashboard, ShoppingCart, Users, CheckCircle, RotateCcw, AlertTriangle } from 'lucide-react';

export function Dashboard() {
    const navigate = useNavigate();
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [selectedStageId, setSelectedStageId] = useState('rascunho');

    useEffect(() => {
        const fetchSummary = async () => {
            try {
                setIsLoading(true);
                const data = await api.requests.getDashboardSummary();
                setSummary(data);
                setError(null);
            } catch (err) {
                console.error('Error fetching dashboard summary:', err);
                setError('Não foi possível carregar os dados operacionais.');
            } finally {
                setIsLoading(false);
            }
        };

        fetchSummary();
    }, []);

    const selectedStage = selectedStageId === 'reajuste' 
        ? {
            id: 'reajuste',
            label: 'Conceito de Reajuste',
            role: 'Sistema / Aprovadores',
            responsible: 'Aprovadores (Área ou Final)',
            goal: 'Garantir que o pedido seja corrigido caso hajam inconsistências detectadas durante a aprovação.',
            actions: [
                'O aprovador identifica um erro ou falta de informação',
                'O aprovador clica em "Solicitar Reajuste" e descreve o motivo',
                'O pedido retorna para o status de Reajuste (A.A ou A.F)',
                'O comprador recebe a notificação, corrige o pedido e submete novamente'
            ],
            documents: ['Justificativa de Reajuste'],
            nextStage: 'Retorno à Cotação / Edição'
        } as any
        : WORKFLOW_STAGES.find(s => s.id === selectedStageId) || WORKFLOW_STAGES[0];

    return (
        <PageContainer>
            <PageHeader 
                title="Cockpit Gerencial"
                subtitle="Visão consolidada das operações de suprimentos"
            />

            {/* Section 2: Operational Cards */}
            <section>
                <div style={{ 
                    display: 'grid', 
                    gridTemplateColumns: 'repeat(auto-fill, minmax(250px, 1fr))', 
                    gap: '1.5rem' 
                }}>
                    <KPICard 
                        title="Total de Pedidos" 
                        value={isLoading ? '...' : (summary?.totalRequests || 0)}
                        icon={<ShoppingCart size={20} />} 
                        subtitle="Volume total de solicitações"
                        onClick={() => navigate('/requests')}
                    />
                    <KPICard 
                        title="Aguardando Cotação" 
                        value={isLoading ? '...' : (summary?.waitingQuotation || 0)}
                        icon={<Users size={20} />} 
                        color="var(--color-status-blue)"
                        subtitle="Pedidos em fase de orçamentação"
                        onClick={() => navigate('/buyer/items?requestStatus=WAITING_QUOTATION')}
                    />
                    <KPICard 
                        title="Aguardando Aprovação Área" 
                        value={isLoading ? '...' : (summary?.waitingAreaApproval || 0)}
                        icon={<CheckCircle size={20} />} 
                        color="var(--color-status-indigo)"
                        subtitle="Análise técnica e consistência"
                        onClick={() => navigate('/requests?statusCodes=WAITING_AREA_APPROVAL')}
                    />
                    <KPICard 
                        title="Aguardando Aprovação Final" 
                        value={isLoading ? '...' : (summary?.waitingFinalApproval || 0)}
                        icon={<CheckCircle size={20} />} 
                        color="var(--color-status-purple)"
                        subtitle="Validação final e inserção C.C"
                        onClick={() => navigate('/requests?statusCodes=WAITING_FINAL_APPROVAL,WAITING_COST_CENTER')}
                    />
                    <KPICard 
                        title="Em Reajuste" 
                        value={isLoading ? '...' : (summary?.inAdjustment || 0)}
                        icon={<RotateCcw size={20} />} 
                        color="var(--color-status-orange)"
                        subtitle="Necessitam de correção/justificativa"
                        onClick={() => navigate('/requests?statusCodes=AREA_ADJUSTMENT,FINAL_ADJUSTMENT')}
                    />
                    <KPICard 
                        title="Em Atenção" 
                        value={isLoading ? '...' : (summary?.inAttention || 0)}
                        icon={<AlertTriangle size={20} />} 
                        color="var(--color-status-red)"
                        subtitle="Próximos da data de necessidade"
                        onClick={() => navigate('/requests?isAttention=true')}
                    />
                </div>
                {error && (
                    <div style={{ 
                        marginTop: '1rem', 
                        color: 'var(--color-status-red)', 
                        fontSize: '0.85rem',
                        fontWeight: 600
                    }}>
                        ⚠️ {error}
                    </div>
                )}
            </section>

            {/* Section 2.5: Quick Actions */}
            <QuickActions />

            {/* Section 2.7: Attention List */}
            <section style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                padding: '24px', 
                borderRadius: 'var(--radius-lg)', 
                border: '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-sm)',
                marginTop: '1rem'
            }}>
                <header style={{ marginBottom: '20px' }}>
                    <h2 style={{ 
                        margin: 0, 
                        fontSize: '1.25rem', 
                        fontWeight: 900, 
                        textTransform: 'uppercase',
                        borderLeft: '4px solid var(--color-primary)',
                        paddingLeft: '1rem',
                        letterSpacing: '-0.01em'
                    }}>
                        Atenção Requerida
                    </h2>
                </header>
                <AttentionList />
            </section>

            {/* Section 3: Interactive Workflow */}
            <section style={{ 
                display: 'flex', 
                flexDirection: 'column', 
                gap: '1.5rem',
                backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)',
                padding: '32px',
                borderRadius: 'var(--radius-lg)',
                border: '1px solid rgba(var(--color-primary-rgb), 0.05)',
                marginTop: '1rem'
            }}>
                <header>
                    <h2 style={{ 
                        margin: 0, 
                        fontSize: '1.25rem', 
                        fontWeight: 900, 
                        textTransform: 'uppercase',
                        borderLeft: '4px solid var(--color-primary)',
                        paddingLeft: '1rem',
                        letterSpacing: '-0.01em'
                    }}>
                        Workflow Interativo
                    </h2>
                    <p style={{ margin: '0.5rem 0 0 1.25rem', color: 'var(--color-text-muted)', fontSize: '0.9rem', fontWeight: 500 }}>
                        Guia visual do fluxo de suprimentos e responsabilidades operacionais
                    </p>
                </header>

                <div style={{ marginTop: '1rem' }}>
                    <WorkflowInteractive 
                        selectedStageId={selectedStageId} 
                        onSelectStage={setSelectedStageId} 
                    />
                    <WorkflowStageDetails stage={selectedStage} />
                </div>
            </section>
        </PageContainer>
    );
}
