import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { RequestsGrid } from './components/RequestsGrid';

export function RequestsList() {
    const [loadingLookups, setLoadingLookups] = useState(true);
    const [lookups, setLookups] = useState<any>(null);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    const location = useLocation();
    const navigate = useNavigate();
    const locationState = location.state as { successMessage?: string } | null;

    useEffect(() => {
        if (locationState?.successMessage) {
            setFeedback({ type: 'success', message: locationState.successMessage });
            const newState = { ...locationState };
            delete newState.successMessage;
            navigate({ pathname: location.pathname, search: location.search }, { replace: true, state: newState });
        }
    }, [locationState, navigate, location.pathname, location.search]);

    useEffect(() => {
        async function fetchLookups() {
            try {
                const [statuses, requestTypes, plants, companies, departments] = await Promise.all([
                    api.lookups.getRequestStatuses(false),
                    api.lookups.getRequestTypes(false),
                    api.lookups.getPlants(undefined, false),
                    api.lookups.getCompanies(false),
                    api.lookups.getDepartments(false)
                ]);
                setLookups({ statuses, requestTypes, plants, companies, departments });
            } catch (err) {
                console.error("Failed to load lookups:", err);
                setFeedback({ type: 'error', message: 'Erro ao carregar dados de base.' });
            } finally {
                setLoadingLookups(false);
            }
        }
        fetchLookups();
    }, []);

    if (loadingLookups) {
        return (
            <div style={{ padding: '60px', textAlign: 'center', color: 'var(--color-primary)', fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                A Carregar Sistema...
            </div>
        );
    }

    if (!lookups) return null;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px', width: '100%', minWidth: 0, paddingBottom: '60px' }}>
            {/* Sticky Action Info Block */}
            {feedback.message && (
                <div style={{ position: 'sticky', top: 'calc(var(--header-height) - 1rem)', zIndex: 10, backgroundColor: 'var(--color-bg-surface)', padding: '2rem 0 0 0', margin: '-2rem 0 0 0', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                </div>
            )}

            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '1px solid var(--color-border)', paddingBottom: '16px', width: '100%', minWidth: 0 }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Pedidos de Compras e Pagamentos</h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' }}>Gerencie e acompanhe todos os pedidos corporativos.</p>
                </div>
                <Link to="/requests/new" className="btn-primary" style={{ display: 'flex', alignItems: 'center', gap: '12px', textDecoration: 'none' }}>
                    <Plus size={20} strokeWidth={3} />
                    Novo Pedido
                </Link>
            </div>

            {/* Section 1: Algomerado de pedidos na responsabilidade do user */}
            <RequestsGrid 
                title="Para Minha Ação" 
                subtitle="Pedidos que requerem a sua intervenção imediata baseando-se na sua função e escopo"
                emptyMessage="Não tem tarefas pendentes."
                lookups={lookups}
                baseFilters={{ myTasksOnly: true }}
                showKPIs={false}
            />

            {/* Divider */}
            <div style={{ height: '2px', backgroundColor: 'var(--color-border)', margin: '16px 0', width: '100%' }} />

            {/* Section 2: Restante de pedidos, em ordem do mais novo para o mais antigo */}
            <RequestsGrid 
                title="Explorador de Pedidos"
                subtitle="O restante do portefólio de compras visível para si"
                emptyMessage="Nenhum pedido encontrado com estes filtros."
                lookups={lookups}
                baseFilters={{ excludeMyTasks: true }}
                showKPIs={true}
            />
        </div>
    );
}
