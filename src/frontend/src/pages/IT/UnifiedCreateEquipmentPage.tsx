import React, { useState, Suspense } from 'react';
import { useNavigate } from 'react-router-dom';
import { Monitor, Layers, ArrowLeft } from 'lucide-react';
import { Breadcrumb } from '../../components/common/ui/Breadcrumb';

// Lazy-load each wizard so only the selected mode's code is downloaded
const IndividualWizard = React.lazy(() => import('./CreateEquipmentWizardPage'));
const BatchWizard = React.lazy(() => import('./BatchEquipmentWizardPage'));

const BREADCRUMBS = [
    { label: 'T.I.', to: '/it/equipment' },
    { label: 'Estoque de Equipamentos', to: '/it/equipment' },
    { label: 'Novo Equipamento' },
];

const LoadingFallback = () => (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '400px', color: 'var(--color-text-muted, #6b7280)' }}>
        Carregando…
    </div>
);

export default function UnifiedCreateEquipmentPage() {
    const navigate = useNavigate();
    const [mode, setMode] = useState<'individual' | 'batch' | null>(null);

    // ── Once a mode is selected, delegate to the appropriate wizard ──
    if (mode === 'individual') {
        return (
            <Suspense fallback={<LoadingFallback />}>
                <IndividualWizard onExit={() => setMode(null)} onModeChange={() => setMode(null)} />
            </Suspense>
        );
    }

    if (mode === 'batch') {
        return (
            <Suspense fallback={<LoadingFallback />}>
                <BatchWizard onExit={() => setMode(null)} onModeChange={() => setMode(null)} />
            </Suspense>
        );
    }

    // ── Step 0: Mode selection ──
    return (
        <div style={{
            display: 'flex',
            flexDirection: 'column',
            minHeight: 'calc(100vh - 64px)',
            maxWidth: '1400px',
            margin: '0 auto',
            padding: '0 32px',
        }}>
            {/* Breadcrumb + Title — same as WizardLayout header */}
            <div style={{ paddingTop: '16px', marginBottom: '20px' }}>
                <Breadcrumb items={BREADCRUMBS} />
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <div style={{ color: 'var(--color-primary)' }}>
                        <Monitor size={28} />
                    </div>
                    <div>
                        <h1 style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 }}>
                            Novo Equipamento
                        </h1>
                        <p style={{ color: 'var(--color-text-muted)', fontSize: '0.875rem', margin: 0, marginTop: '4px' }}>
                            Selecione o tipo de cadastro para continuar.
                        </p>
                    </div>
                </div>
            </div>

            {/* Main content: matches WizardLayout sidebar + content area */}
            <div style={{
                display: 'flex',
                gap: '24px',
                flex: 1,
                minHeight: 0,
            }}>
                {/* Sidebar placeholder — same width as wizard step indicator */}
                <div style={{
                    width: '240px',
                    flexShrink: 0,
                    alignSelf: 'flex-start',
                }} />

                {/* Content area — same flex as wizard step content */}
                <div style={{
                    flex: 1,
                    minWidth: 0,
                    display: 'flex',
                    flexDirection: 'column',
                    minHeight: '500px',
                }}>
                    <div style={{
                        flex: 1,
                        paddingBottom: '24px',
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'flex-start',
                        paddingTop: '48px',
                    }}>
                        <div style={{
                            display: 'grid',
                            gridTemplateColumns: '1fr 1fr',
                            gap: '24px',
                            maxWidth: '640px',
                            width: '100%',
                        }}>
                            {/* Individual card */}
                            <button
                                onClick={() => setMode('individual')}
                                style={{
                                    display: 'flex',
                                    flexDirection: 'column',
                                    alignItems: 'center',
                                    gap: '16px',
                                    padding: '40px 32px',
                                    backgroundColor: 'var(--color-bg-surface)',
                                    border: '2px solid var(--color-border)',
                                    borderRadius: '12px',
                                    cursor: 'pointer',
                                    transition: 'all 0.2s ease',
                                    textAlign: 'center',
                                    boxShadow: 'var(--shadow-sm)',
                                }}
                                onMouseEnter={e => {
                                    e.currentTarget.style.borderColor = 'var(--color-primary)';
                                    e.currentTarget.style.boxShadow = '0 4px 20px rgba(var(--color-primary-rgb),0.15)';
                                    e.currentTarget.style.transform = 'translateY(-2px)';
                                }}
                                onMouseLeave={e => {
                                    e.currentTarget.style.borderColor = 'var(--color-border)';
                                    e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
                                    e.currentTarget.style.transform = 'translateY(0)';
                                }}
                            >
                                <div style={{
                                    width: '64px', height: '64px', borderRadius: '16px',
                                    background: 'linear-gradient(135deg, rgba(var(--color-primary-rgb),0.06), rgba(var(--color-primary-rgb),0.14))',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                                }}>
                                    <Monitor size={28} color="var(--color-primary)" />
                                </div>
                                <div>
                                    <h3 style={{ margin: '0 0 8px 0', fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                        Cadastro individual
                                    </h3>
                                    <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)', lineHeight: 1.5 }}>
                                        Criar apenas um equipamento.
                                    </p>
                                </div>
                            </button>

                            {/* Batch card */}
                            <button
                                onClick={() => setMode('batch')}
                                style={{
                                    display: 'flex',
                                    flexDirection: 'column',
                                    alignItems: 'center',
                                    gap: '16px',
                                    padding: '40px 32px',
                                    backgroundColor: 'var(--color-bg-surface)',
                                    border: '2px solid var(--color-border)',
                                    borderRadius: '12px',
                                    cursor: 'pointer',
                                    transition: 'all 0.2s ease',
                                    textAlign: 'center',
                                    boxShadow: 'var(--shadow-sm)',
                                }}
                                onMouseEnter={e => {
                                    e.currentTarget.style.borderColor = 'var(--color-secondary, var(--color-primary))';
                                    e.currentTarget.style.boxShadow = '0 4px 20px rgba(var(--color-primary-rgb),0.12)';
                                    e.currentTarget.style.transform = 'translateY(-2px)';
                                }}
                                onMouseLeave={e => {
                                    e.currentTarget.style.borderColor = 'var(--color-border)';
                                    e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
                                    e.currentTarget.style.transform = 'translateY(0)';
                                }}
                            >
                                <div style={{
                                    width: '64px', height: '64px', borderRadius: '16px',
                                    background: 'linear-gradient(135deg, rgba(var(--color-primary-rgb),0.04), rgba(var(--color-primary-rgb),0.10))',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                                }}>
                                    <Layers size={28} color="var(--color-secondary, var(--color-primary))" />
                                </div>
                                <div>
                                    <h3 style={{ margin: '0 0 8px 0', fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                        Cadastro em lote
                                    </h3>
                                    <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)', lineHeight: 1.5 }}>
                                        Criar vários equipamentos semelhantes, cada um com código de ativo próprio.
                                    </p>
                                </div>
                            </button>
                        </div>
                    </div>
                </div>
            </div>

            {/* Back link — same sticky footer zone as WizardFooter */}
            <div style={{
                position: 'sticky',
                bottom: 0,
                marginLeft: '-32px',
                marginRight: '-32px',
                padding: '16px 32px',
                backgroundColor: 'var(--color-bg-surface)',
                borderTop: '1px solid var(--color-border)',
                marginTop: 'auto',
                zIndex: 10,
            }}>
                <button
                    onClick={() => navigate('/it/equipment')}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '8px',
                        padding: '8px 16px', backgroundColor: 'transparent',
                        border: '1px solid var(--color-border)', borderRadius: '8px',
                        color: 'var(--color-text-muted)', fontSize: '0.85rem',
                        cursor: 'pointer', transition: 'all 0.2s',
                    }}
                >
                    <ArrowLeft size={15} /> Voltar ao estoque
                </button>
            </div>
        </div>
    );
}
