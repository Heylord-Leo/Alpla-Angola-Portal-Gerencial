import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { PurchasingKPISummary } from './components/PurchasingKPISummary';
import { AttentionPanel } from './components/AttentionPanel';
import { QuickActions } from './components/QuickActions';
import { PurchasingHelpDrawer } from './components/PurchasingHelpDrawer';
import { PurchasingSummaryDto } from '../../types';
import { Feedback } from '../../components/ui/Feedback';
import { LayoutGrid, PlayCircle } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export default function PurchasingLandingPage() {
    const [summary, setSummary] = useState<PurchasingSummaryDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [isHelpDrawerOpen, setIsHelpDrawerOpen] = useState(false);

    useEffect(() => {
        async function loadSummary() {
            try {
                setLoading(true);
                const data = await api.requests.getPurchasingSummary();
                setSummary(data);
            } catch (err: any) {
                console.error("Failed to load purchasing summary:", err);
                setError(err.message || 'Erro ao carregar dados do cockpit.');
            } finally {
                setLoading(false);
            }
        }
        loadSummary();
    }, []);

    return (
        <PageContainer>
            
            {/* Header Section */}
            <PageHeader
                title="Compras & Logística"
                subtitle="Cockpit Operacional • Portal Gerencial ALPLA"
            />

            {error && <Feedback type="error" message={error} onClose={() => setError(null)} />}

            {loading ? (
                <div style={{ padding: '100px', textAlign: 'center', color: 'var(--color-primary)', fontWeight: 800, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                    Inicializando Cockpit...
                </div>
            ) : summary ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                    
                    {/* KPI Cards Section */}
                    <section>
                        <PurchasingKPISummary summary={summary} />
                    </section>

                    {/* Main Content Grid */}
                    <div style={{ 
                        display: 'grid', 
                        gridTemplateColumns: 'minmax(0, 2fr) minmax(0, 1fr)', 
                        gap: '32px',
                        alignItems: 'start'
                    }}>
                        
                        {/* Left Column: Attention Panel */}
                        <section style={{ 
                            backgroundColor: 'var(--color-bg-surface)', 
                            padding: '24px', 
                            border: '2px solid var(--color-border)',
                            boxShadow: 'var(--shadow-md)',
                            minHeight: '400px'
                        }}>
                            <AttentionPanel points={summary.attentionPoints} />
                        </section>

                        {/* Right Column: Quick Actions & Instructions */}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                            <section style={{ 
                                backgroundColor: 'var(--color-bg-surface)', 
                                padding: '24px', 
                                border: '2px solid var(--color-border)',
                                boxShadow: 'var(--shadow-md)'
                            }}>
                                <QuickActions />
                            </section>

                            {/* Operational Guidance */}
                            <section 
                                onClick={() => setIsHelpDrawerOpen(true)}
                                style={{ 
                                    backgroundColor: 'var(--color-primary)', 
                                    color: '#fff',
                                    padding: '24px', 
                                    boxShadow: 'var(--shadow-md)',
                                    display: 'flex',
                                    flexDirection: 'column',
                                    gap: '12px',
                                    cursor: 'pointer',
                                    transition: 'all 0.15s ease-out',
                                    border: '2px solid var(--color-primary)'
                                }}
                                onMouseOver={(e) => {
                                    e.currentTarget.style.backgroundColor = 'var(--color-bg-page)';
                                    e.currentTarget.style.color = 'var(--color-primary)';
                                    e.currentTarget.style.transform = 'translateY(-3px)';
                                    e.currentTarget.style.boxShadow = 'var(--shadow-lg)';
                                }}
                                onMouseOut={(e) => {
                                    e.currentTarget.style.backgroundColor = 'var(--color-primary)';
                                    e.currentTarget.style.color = '#fff';
                                    e.currentTarget.style.transform = 'translateY(0)';
                                    e.currentTarget.style.boxShadow = 'var(--shadow-md)';
                                }}
                            >
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 900, textTransform: 'uppercase', fontSize: '1rem' }}>
                                    <PlayCircle size={24} strokeWidth={2.5} />
                                    Manual de Operação
                                </div>
                                <p style={{ margin: 0, fontSize: '0.85rem', lineHeight: '1.5', fontWeight: 600 }}>
                                    Clique para acessar o guia rápido de processos, priorização de tarefas e instruções de uso do Cockpit ALPLA.
                                </p>
                                <div style={{ 
                                    marginTop: '8px', 
                                    fontSize: '0.75rem', 
                                    fontWeight: 800, 
                                    textTransform: 'uppercase', 
                                    letterSpacing: '0.05em',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '4px'
                                }}>
                                    Acessar Guia Rápido →
                                </div>
                            </section>
                        </div>

                    </div>
                </div>
            ) : null}

            {/* Drawers */}
            <PurchasingHelpDrawer 
                isOpen={isHelpDrawerOpen} 
                onClose={() => setIsHelpDrawerOpen(false)} 
            />
        </PageContainer>
    );
}
