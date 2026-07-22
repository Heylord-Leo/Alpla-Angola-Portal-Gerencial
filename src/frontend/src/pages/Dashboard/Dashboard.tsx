import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import { CockpitSummaryDto } from '../../types';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { MyWorkQueue } from './components/MyWorkQueue';
import { QuickActions } from './components/QuickActions';
import { AlertList } from './components/AlertList';
import { BottleneckTable } from './components/BottleneckTable';
import { FinancialSummary } from './components/FinancialSummary';
import { WorkflowInteractive } from './components/WorkflowInteractive';
import { WorkflowStageDetails } from './components/WorkflowStageDetails';
import { WORKFLOW_STAGES } from './components/workflowData';

export function Dashboard() {
    const navigate = useNavigate();
    const [cockpit, setCockpit] = useState<CockpitSummaryDto | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [selectedStageId, setSelectedStageId] = useState('rascunho');
    const [workflowOpen, setWorkflowOpen] = useState(false);

    useEffect(() => {
        const fetchCockpit = async () => {
            try {
                setIsLoading(true);
                const data = await api.requests.getCockpitSummary();
                setCockpit(data);
                setError(null);
            } catch (err) {
                console.error('Error fetching cockpit summary:', err);
                setError('Não foi possível carregar os dados operacionais.');
            } finally {
                setIsLoading(false);
            }
        };

        fetchCockpit();
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

    // Loading state
    if (isLoading) {
        return (
            <PageContainer>
                <PageHeader 
                    title="Cockpit Gerencial"
                    subtitle="Prioridades, pendências e indicadores operacionais do processo de compras"
                />
                <div style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                    gap: '16px'
                }}>
                    {[1, 2, 3, 4, 5].map(i => (
                        <div key={i} style={{
                            height: '120px',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: '12px',
                            animation: 'pulse 1.5s ease-in-out infinite'
                        }} />
                    ))}
                </div>
                <style>{`
                    @keyframes pulse {
                        0%, 100% { opacity: 1; }
                        50% { opacity: 0.5; }
                    }
                `}</style>
            </PageContainer>
        );
    }

    // Error state
    if (error && !cockpit) {
        return (
            <PageContainer>
                <PageHeader 
                    title="Cockpit Gerencial"
                    subtitle="Prioridades, pendências e indicadores operacionais do processo de compras"
                />
                <div style={{
                    backgroundColor: '#fef2f2',
                    border: '1px solid #fecaca',
                    borderRadius: '12px',
                    padding: '24px',
                    color: '#dc2626',
                    fontWeight: 500,
                    display: 'flex',
                    alignItems: 'center',
                    gap: '12px'
                }}>
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                        <line x1="12" y1="9" x2="12" y2="13" />
                        <line x1="12" y1="17" x2="12.01" y2="17" />
                    </svg>
                    {error}
                </div>
            </PageContainer>
        );
    }

    if (!cockpit) return null;

    // Pipeline cards config
    const pipelineCards = [
        { title: 'Activos', value: cockpit.totalActiveRequests, color: '#3b82f6', onClick: () => navigate('/requests') },
        { title: 'Ag. Cotação', value: cockpit.waitingQuotation, color: '#6366f1', onClick: () => navigate('/buyer/items?requestStatus=WAITING_QUOTATION') },
        { title: 'Aprov. Área', value: cockpit.waitingAreaApproval, color: '#8b5cf6', onClick: () => navigate('/requests?statusCodes=WAITING_AREA_APPROVAL') },
        { title: 'Aprov. Final', value: cockpit.waitingFinalApproval, color: '#a855f7', onClick: () => navigate('/requests?statusCodes=WAITING_FINAL_APPROVAL,WAITING_COST_CENTER') },
        { title: 'Reajuste', value: cockpit.inAdjustment, color: '#f97316', onClick: () => navigate('/requests?statusCodes=AREA_ADJUSTMENT,FINAL_ADJUSTMENT') },
        { title: 'Ag. P.O', value: cockpit.awaitingPo, color: '#0ea5e9', onClick: () => navigate('/requests?statusCodes=APPROVED,QUOTATION_COMPLETED') },
        { title: 'Ag. Pagamento', value: cockpit.awaitingPayment, color: '#f59e0b', onClick: () => navigate('/finance/payments') },
        { title: 'Pago', value: cockpit.paymentCompleted, color: '#10b981', onClick: () => navigate('/requests?statusCodes=PAYMENT_COMPLETED') },
        { title: 'Recebimento', value: cockpit.waitingReceipt, color: '#14b8a6', onClick: () => navigate('/receiving/workspace') },
        { title: 'Concluídos', value: cockpit.completed, color: '#6b7280', onClick: () => navigate('/requests?statusCodes=COMPLETED') },
    ];

    return (
        <PageContainer>
            {/* ── Header ── */}
            <PageHeader 
                title="Cockpit Gerencial"
                subtitle="Prioridades, pendências e indicadores operacionais do processo de compras"
            />

            <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                {/* ── Section 1: Minha Fila de Trabalho ── */}
                <MyWorkQueue data={cockpit} />

                {/* ── Section 2: Pipeline KPI Cards ── */}
                <section>
                    <h2 style={{
                        fontSize: '1.1rem',
                        fontWeight: 700,
                        color: 'var(--color-text)',
                        margin: '0 0 16px 0'
                    }}>
                        Visão do Pipeline
                    </h2>
                    <div style={{ 
                        display: 'grid', 
                        gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', 
                        gap: '12px'
                    }}>
                        {pipelineCards.map(card => (
                            <div
                                key={card.title}
                                onClick={card.onClick}
                                style={{
                                    backgroundColor: 'var(--color-bg-surface)',
                                    border: '1px solid var(--color-border)',
                                    borderRadius: '10px',
                                    padding: '14px 16px',
                                    cursor: 'pointer',
                                    transition: 'all 0.15s',
                                    position: 'relative',
                                    overflow: 'hidden'
                                }}
                                onMouseOver={(e) => {
                                    e.currentTarget.style.transform = 'translateY(-1px)';
                                    e.currentTarget.style.boxShadow = `0 4px 12px ${card.color}20`;
                                    e.currentTarget.style.borderColor = `${card.color}40`;
                                }}
                                onMouseOut={(e) => {
                                    e.currentTarget.style.transform = 'none';
                                    e.currentTarget.style.boxShadow = 'none';
                                    e.currentTarget.style.borderColor = 'var(--color-border)';
                                }}
                            >
                                {/* Top color accent */}
                                <div style={{
                                    position: 'absolute',
                                    top: 0,
                                    left: 0,
                                    right: 0,
                                    height: '2px',
                                    backgroundColor: card.color
                                }} />
                                <div style={{
                                    fontSize: '0.7rem',
                                    fontWeight: 600,
                                    color: card.color,
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.03em',
                                    marginBottom: '6px'
                                }}>
                                    {card.title}
                                </div>
                                <div style={{
                                    fontSize: '1.75rem',
                                    fontWeight: 700,
                                    color: 'var(--color-text)',
                                    lineHeight: 1,
                                    fontVariantNumeric: 'tabular-nums'
                                }}>
                                    {card.value}
                                </div>
                            </div>
                        ))}
                    </div>
                </section>

                {/* ── Section 3: Quick Actions ── */}
                <QuickActions />

                {/* ── Section 4: Atenção Requerida ── */}
                <AlertList alerts={cockpit.alerts} />

                {/* ── Section 5 + 6: Bottlenecks & Financial (side by side on large screens) ── */}
                <div style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
                    gap: '32px',
                    alignItems: 'start'
                }}>
                    <BottleneckTable bottlenecks={cockpit.bottlenecks} />
                    <FinancialSummary data={cockpit.financialByStatus} />
                </div>

                {/* ── Section 7: Como funciona o processo (collapsible) ── */}
                <section>
                    <details
                        open={workflowOpen}
                        onToggle={(e) => setWorkflowOpen((e.target as HTMLDetailsElement).open)}
                        style={{
                            backgroundColor: 'rgba(var(--color-primary-rgb, 56, 189, 248), 0.02)',
                            borderRadius: '12px',
                            border: '1px solid rgba(var(--color-primary-rgb, 56, 189, 248), 0.08)',
                            overflow: 'hidden'
                        }}
                    >
                        <summary style={{
                            padding: '16px 20px',
                            cursor: 'pointer',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '10px',
                            listStyle: 'none',
                            userSelect: 'none'
                        }}>
                            <svg
                                width="16"
                                height="16"
                                viewBox="0 0 24 24"
                                fill="none"
                                stroke="var(--color-text-muted)"
                                strokeWidth="2"
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                style={{
                                    transition: 'transform 0.2s',
                                    transform: workflowOpen ? 'rotate(90deg)' : 'rotate(0deg)'
                                }}
                            >
                                <polyline points="9 18 15 12 9 6" />
                            </svg>
                            <span style={{
                                fontSize: '0.95rem',
                                fontWeight: 700,
                                color: 'var(--color-text)'
                            }}>
                                Como funciona o processo
                            </span>
                            <span style={{
                                fontSize: '0.75rem',
                                color: 'var(--color-text-muted)',
                                fontWeight: 500
                            }}>
                                — Guia visual do fluxo de suprimentos
                            </span>
                        </summary>
                        <div style={{ padding: '0 20px 20px' }}>
                            <WorkflowInteractive 
                                selectedStageId={selectedStageId} 
                                onSelectStage={setSelectedStageId} 
                            />
                            <WorkflowStageDetails stage={selectedStage} />
                        </div>
                    </details>
                </section>
            </div>
        </PageContainer>
    );
}
