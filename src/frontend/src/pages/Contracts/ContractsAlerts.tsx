import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bell, AlertTriangle, Clock, X } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { fetchActiveAlerts, dismissAlert, ContractAlert, CONTRACT_STATUS_MAP } from '../../lib/contractsApi';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';

export default function ContractsAlerts() {
    const navigate = useNavigate();
    const [alerts, setAlerts] = useState<ContractAlert[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    const load = async () => {
        setLoading(true);
        try {
            const data = await fetchActiveAlerts();
            setAlerts(data);
        } catch (err) {
            console.error('Failed to load alerts:', err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, []);

    const handleDismiss = async (alertId: string) => {
        try {
            await dismissAlert(alertId);
            setAlerts(prev => prev.filter(a => a.id !== alertId));
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao dispensar alerta.' });
        }
    };

    const formatDate = (iso?: string) => {
        if (!iso) return '—';
        return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric' });
    };

    if (loading) return <div style={{ padding: '3rem', textAlign: 'center', color: 'var(--color-text-muted)' }}>Carregando alertas...</div>;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', paddingTop: '1.5rem' }}>
            <Feedback 
                type={feedback.type} 
                message={feedback.message} 
                onClose={() => setFeedback({ ...feedback, message: null })} 
                isFixed 
            />
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '0.5rem' }}>
                <Bell size={20} style={{ color: 'var(--color-primary)' }} />
                <h3 style={{ fontSize: '1rem', fontWeight: 700, margin: 0 }}>
                    Alertas Ativos
                    {alerts.length > 0 && (
                        <span style={{
                            marginLeft: '8px', padding: '2px 10px', borderRadius: 'var(--radius-full)',
                            backgroundColor: '#fef3c7', color: '#d97706', fontSize: '0.75rem', fontWeight: 800
                        }}>
                            {alerts.length}
                        </span>
                    )}
                </h3>
            </div>

            {alerts.length === 0 ? (
                <div style={{
                    padding: '3rem', textAlign: 'center', color: 'var(--color-text-muted)',
                    backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-lg)',
                    border: '1px solid var(--color-border)'
                }}>
                    <Bell size={32} style={{ marginBottom: '8px', opacity: 0.3 }} />
                    <p style={{ fontSize: '0.9rem', margin: 0 }}>Nenhum alerta ativo. Tudo em dia!</p>
                </div>
            ) : (
                <AnimatePresence>
                    {alerts.map((a, i) => (
                        <motion.div
                            key={a.id}
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                            exit={{ opacity: 0, x: -50 }}
                            transition={{ delay: i * 0.05 }}
                            style={{
                                display: 'flex', alignItems: 'center', gap: '1rem', padding: '1rem 1.25rem',
                                borderRadius: 'var(--radius-lg)', border: '1px solid #fbbf24',
                                backgroundColor: '#fffbeb', cursor: 'pointer'
                            }}
                            onClick={() => navigate(`/contracts/${a.contractId}`)}
                        >
                            <AlertTriangle size={22} style={{ color: '#d97706', flexShrink: 0 }} />
                            <div style={{ flex: 1 }}>
                                <div style={{ fontWeight: 700, fontSize: '0.9rem', color: '#92400e' }}>
                                    {a.contractNumber} — {a.contractTitle}
                                </div>
                                <div style={{ fontSize: '0.8rem', color: '#a16207', marginTop: '2px' }}>
                                    {a.alertType} • {a.message || `Data gatilho: ${formatDate(a.triggerDateUtc)}`}
                                </div>
                            </div>
                            <button
                                onClick={e => { e.stopPropagation(); handleDismiss(a.id); }}
                                title="Dispensar"
                                style={{
                                    padding: '6px', borderRadius: 'var(--radius-full)',
                                    border: '1px solid #fbbf24', backgroundColor: 'transparent',
                                    color: '#d97706', cursor: 'pointer', display: 'flex'
                                }}
                            >
                                <X size={14} />
                            </button>
                        </motion.div>
                    ))}
                </AnimatePresence>
            )}
        </div>
    );
}
