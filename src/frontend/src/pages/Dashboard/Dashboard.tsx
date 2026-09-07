import { useState } from 'react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { QuickActions } from './components/QuickActions';
import { DashboardV2StageAgingSection } from './components/DashboardV2StageAgingSection';
import { DashboardV2FinancialSection } from './components/DashboardV2FinancialSection';
import { WorkflowInteractive } from './components/WorkflowInteractive';
import { WorkflowStageDetails } from './components/WorkflowStageDetails';
import { WORKFLOW_STAGES } from './components/workflowData';
import { DashboardV2PersonalSection } from './components/DashboardV2PersonalSection';
import { DashboardV2PipelineSection } from './components/DashboardV2PipelineSection';
import { SectionInfo } from '../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from './dashboardSectionHelp';
import { DashboardV2BuyerSection } from './components/DashboardV2BuyerSection';
import { DashboardV2FinanceSection } from './components/DashboardV2FinanceSection';
import { DashboardV2ReceivingSection } from './components/DashboardV2ReceivingSection';
import { DashboardV2AlertsSection } from './components/DashboardV2AlertsSection';

export function Dashboard() {
    // B9.6: the legacy cockpit-summary fetch and its page-level loading/error/null gate are removed. Every
    // Dashboard V2 section self-fetches (GET /api/dashboard/v2/*) and owns its own loading/error state via
    // useSectionData, so the page renders its sections directly — no global gate, no legacy sweep.
    const [selectedStageId, setSelectedStageId] = useState('rascunho');
    const [workflowOpen, setWorkflowOpen] = useState(false);

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
            {/* ── Header ── */}
            <PageHeader 
                title="Cockpit Gerencial"
                subtitle="Prioridades, pendências e indicadores operacionais do processo de compras"
            />

            <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                {/* ── Dashboard V2 (Phase B slice B5): canonical personal actions (Pessoal). Replaces the
                       legacy role/status personal union — see DashboardV2PersonalSection. ── */}
                <DashboardV2PersonalSection />

                {/* ── Dashboard V2 (Phase B slice B1+B2): canonical Buyer section (Pessoal / Compartilhado / Gerencial) ── */}
                <DashboardV2BuyerSection />

                {/* ── Dashboard V2 (Phase B slice B3): Finance shared queue (Compartilhado / Gerencial) ── */}
                <DashboardV2FinanceSection />

                {/* ── Dashboard V2 (Phase B slice B4): Receiving shared queue (Compartilhado / Gerencial) ── */}
                <DashboardV2ReceivingSection />

                {/* ── Dashboard V2 (Phase B slice B8): canonical Alerts ("Atenção Necessária"). Placed as an
                       attention band AFTER the personal/shared work queues and BEFORE the managerial
                       analytics — higher-signal than analytics, but it does not replace the work queues.
                       Entitlement-gated on the server (summary null → renders nothing). Replaces the stale
                       legacy AlertList that B5 hid. ── */}
                <DashboardV2AlertsSection />

                {/* ── VISÃO GERENCIAL: retained legacy analytical sections. These are NOT personal — they
                       are plant/department-wide, request-level summaries kept temporarily until their
                       dedicated V2 slices (pipeline=B6, financial=B7, alerts=B8, stage-aging=B9). ── */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 8 }}>
                    <h2 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 }}>Visão Gerencial</h2>
                    <span style={{
                        fontSize: '0.68rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
                        color: '#3b5069', backgroundColor: '#3b50691A', borderRadius: 999, padding: '2px 9px'
                    }}>Gerencial</span>
                    <SectionInfo {...DASHBOARD_SECTION_HELP.gerencial} />
                </div>

                {/* ── Canonical Operational Pipeline (B6.2). Replaces the legacy scalar Request.Status
                       histogram; self-fetches GET /api/dashboard/v2/pipeline. ── */}
                <DashboardV2PipelineSection />

                {/* ── "Atenção Requerida" (legacy AlertList) is intentionally HIDDEN in B5: its global
                       OVERDUE/NEAR alerts fire from NeedByDate on any non-terminal request (incl. already
                       paid / in-receiving), so ~64% were stale and would read as "minha ação". A correct,
                       open-obligation-gated, de-duplicated alert engine is B8. Backend cockpit-summary
                       (incl. its `alerts`) is left untouched; we simply do not render it here. ── */}

                {/* ── Canonical Stage Aging / Gargalos (B9.5). Replaced the legacy BottleneckTable whose
                       "Idade" was request-creation age; self-fetches GET /api/dashboard/v2/stage-aging and
                       shows true time-in-current-stage. The legacy cockpit-summary dependency was removed
                       entirely in B9.6. ── */}
                <DashboardV2StageAgingSection />

                {/* ── Canonical currency-safe Financial Summary (B7.2). Replaces the legacy mixed-currency
                       "Resumo Financeiro"; self-fetches GET /api/dashboard/v2/financial and is entitlement-
                       gated on the server (hidden when currentExposure is null). ── */}
                <DashboardV2FinancialSection />

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
                                color: 'var(--color-text-main)'
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

                {/* ── Quick Actions (utility shortcuts; permission-gated inside the component) ── */}
                <QuickActions />
            </div>
        </PageContainer>
    );
}
