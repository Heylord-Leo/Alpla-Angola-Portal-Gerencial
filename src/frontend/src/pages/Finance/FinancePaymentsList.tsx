import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { FinanceListResponseDto } from '../../types';
import { useSearchParams } from 'react-router-dom';
import { Check, Clock, AlertTriangle, FileText } from 'lucide-react';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { FinanceActionModal, FinanceActionType } from '../../components/modals/FinanceActionModal';
import { FeedbackType } from '../../components/ui/Feedback';

export default function FinancePaymentsList() {
    const [data, setData] = useState<FinanceListResponseDto | null>(null);
    const [searchParams] = useSearchParams();
    const filter = searchParams.get('filter') || undefined;

    const [actionModal, setActionModal] = useState<{ show: boolean; action: FinanceActionType; requestId: string | null }>({ show: false, action: null, requestId: null });
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    useEffect(() => {
        loadData();
    }, [filter]);

    const loadData = () => {
        api.finance.getPayments(filter).then(res => setData(res));
    };

    const handleActionClick = (requestId: string, action: FinanceActionType) => {
        setActionModal({ show: true, action, requestId });
        setFeedback({ type: 'success', message: null });
    };

    const handleConfirmAction = async (action: FinanceActionType, payload: { date?: string; notes?: string; file?: File | null }) => {
        if (!actionModal.requestId) return;
        setProcessing(true);
        setFeedback({ type: 'success', message: null });

        try {
            if (action === 'SCHEDULE' && payload.date) {
                if (payload.file) {
                    await api.attachments.upload(actionModal.requestId, [payload.file], 'PAYMENT_SCHEDULE');
                }
                await api.finance.schedulePayment(actionModal.requestId, new Date(payload.date).toISOString(), payload.notes || "Agendado via portal");
            } else if (action === 'PAY') {
                await api.finance.markAsPaid(actionModal.requestId, new Date().toISOString(), payload.notes || "Liquidado via portal");
            } else if (action === 'RETURN' && payload.notes) {
                await api.finance.returnForAdjustment(actionModal.requestId, payload.notes);
            }
            
            setActionModal({ show: false, action: null, requestId: null });
            loadData();
        } catch (err: any) {
            console.error(err);
            setFeedback({ type: 'error', message: err.response?.data?.message || "Falha ao executar ação." });
        } finally {
            setProcessing(false);
        }
    };

    if (!data) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold' }}>Carregando Pagamentos...</div>;

    const getBadgeStyle = (color: string) => {
        return {
            padding: '4px 8px',
            borderRadius: '4px',
            fontWeight: 800,
            fontSize: '0.75rem',
            backgroundColor: `var(--color-${color}-100, #f1f5f9)`,
            color: `var(--color-${color}-800, #334155)`,
            border: `1px solid var(--color-${color}-300, #cbd5e1)`,
            textTransform: 'uppercase' as any
        };
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <h2 style={{ margin: 0, fontSize: '1.8rem', fontWeight: 900, textTransform: 'uppercase' }}>Obrigações & Pagamentos</h2>
                {filter && (
                    <div style={{ backgroundColor: '#fff7ed', padding: '8px 16px', border: '2px solid #f97316', fontWeight: 800, color: '#c2410c' }}>
                        Filtro Ativo: {filter.toUpperCase()}
                    </div>
                )}
            </div>

            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '2px solid var(--color-border)', 
                boxShadow: 'var(--shadow-brutal)',
                overflowX: 'auto'
            }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                    <thead style={{ backgroundColor: 'var(--color-border)', color: '#fff' }}>
                        <tr>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Identificação</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Fornecedor</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Vencimento / Status</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Valor</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase', textAlign: 'right' }}>Ações</th>
                        </tr>
                    </thead>
                    <tbody>
                        {data.pagedResult.items.map((item, i) => (
                            <tr key={item.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: i % 2 === 0 ? '#fff' : '#f8fafc' }}>
                                <td style={{ padding: '16px' }}>
                                    <div style={{ fontWeight: 800, color: 'var(--color-primary)' }}>{item.requestNumber}</div>
                                    <div style={{ fontSize: '0.85rem', color: '#64748b', fontWeight: 600 }}>{item.plantName}</div>
                                </td>
                                <td style={{ padding: '16px' }}>
                                    <div style={{ fontWeight: 700 }}>{item.supplierName}</div>
                                    <div style={{ fontSize: '0.85rem', color: '#64748b' }}>Req: {item.requesterName}</div>
                                </td>
                                <td style={{ padding: '16px', verticalAlign: 'top' }}>
                                    <div style={{ marginBottom: '12px' }}>
                                        <span style={getBadgeStyle(item.statusBadgeColor)}>{item.statusName}</span>
                                    </div>
                                    {item.needByDateUtc && (
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', marginBottom: '8px' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.80rem', fontWeight: 600, color: '#64748b' }}>
                                                Original: {new Date(item.needByDateUtc).toLocaleDateString()}
                                            </div>
                                            {(item.scheduledDateUtc || item.isOverdue || item.isDueSoon) && (
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.85rem', fontWeight: item.isOverdue ? 800 : 700, color: item.isOverdue ? '#dc2626' : item.scheduledDateUtc ? '#0284c7' : item.isDueSoon ? '#d97706' : '#475569' }}>
                                                    <Clock size={16} /> 
                                                    {item.scheduledDateUtc ? `Agendado: ${new Date(item.scheduledDateUtc).toLocaleDateString()}` : `Vence: ${new Date(item.needByDateUtc).toLocaleDateString()}`}
                                                    {item.isOverdue && <span style={{ backgroundColor: '#fef2f2', color: '#dc2626', padding: '2px 6px', borderRadius: '4px', border: '1px solid #fca5a5', fontSize: '0.75rem', fontWeight: 800 }}>Atrasado</span>}
                                                </div>
                                            )}
                                        </div>
                                    )}
                                    {item.isMissingDocuments && (
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', marginTop: '8px' }}>
                                            {item.missingDocumentTypes.map(type => {
                                                const label = type === 'PO' ? 'Sem P.O.' : type === 'PROFORMA' ? 'Sem Proforma' : 'Sem comprovativo';
                                                return (
                                                    <div key={type} style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', fontSize: '0.8rem', fontWeight: 800, color: '#ea580c', backgroundColor: '#fff7ed', padding: '2px 8px', border: '1px solid #fdba74', borderRadius: '4px', width: 'fit-content' }}>
                                                        <AlertTriangle size={12} /> {label}
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    )}
                                </td>
                                <td style={{ padding: '16px', fontWeight: 900, fontSize: '1.1rem' }}>
                                    {new Intl.NumberFormat('pt-AO', { style: 'currency', currency: item.currencyCode || 'AOA' }).format(item.amount)}
                                </td>
                                <td style={{ padding: '16px', textAlign: 'right' }}>
                                    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                                        <KebabMenu options={[
                                            ...(item.availableFinanceActions.includes('SCHEDULE') ? [{ label: 'Agendar pagamento', icon: <Clock size={16} />, onClick: () => handleActionClick(item.id, 'SCHEDULE') }] : []),
                                            ...(item.availableFinanceActions.includes('PAY') ? [{ label: 'Marcar como pago', icon: <Check size={16} />, onClick: () => handleActionClick(item.id, 'PAY') }] : []),
                                            { label: 'Detalhes', icon: <FileText size={16} />, onClick: () => window.open(`/requests/${item.id}`, '_blank') },
                                            ...(item.availableFinanceActions.includes('RETURN') ? [{ label: 'Devolver para ajuste', icon: <AlertTriangle size={16} color="#dc2626" />, onClick: () => handleActionClick(item.id, 'RETURN') }] : [])
                                        ]} />
                                    </div>
                                </td>
                            </tr>
                        ))}
                        {data.pagedResult.items.length === 0 && (
                            <tr>
                                <td colSpan={5} style={{ padding: '40px', textAlign: 'center', fontWeight: 600, color: '#64748b' }}>
                                    Nenhum pagamento localizado para os critérios informados.
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            </div>

            <FinanceActionModal
                show={actionModal.show}
                action={actionModal.action}
                onClose={() => setActionModal({ show: false, action: null, requestId: null })}
                onConfirm={handleConfirmAction}
                processing={processing}
                feedback={feedback}
                onCloseFeedback={() => setFeedback({ type: 'success', message: null })}
            />
        </div>
    );
}
