import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { FinanceListResponseDto } from '../../types';
import { useSearchParams } from 'react-router-dom';
import { Check, Clock, AlertTriangle, FileText, MessageSquare, ChevronLeft, ChevronRight, Search, X } from 'lucide-react';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { ModernTooltip } from '../../components/ui/ModernTooltip';
import { FinanceActionModal, FinanceActionType } from '../../components/modals/FinanceActionModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { StandardTable } from '../../components/ui/StandardTable';
import { RequestDrawerPresentation } from '../Requests/components/modern/RequestDrawerPresentation';

export default function FinancePaymentsList() {
    const [data, setData] = useState<FinanceListResponseDto | null>(null);
    const [searchParams, setSearchParams] = useSearchParams();
    const filter = searchParams.get('filter') || undefined;
    const page = parseInt(searchParams.get('page') || '1');
    const pageSize = parseInt(searchParams.get('pageSize') || '20');
    const statusCodes = searchParams.get('statusCodes') || undefined;
    const currencyCode = searchParams.get('currencyCode') || undefined;
    const searchSupplier = searchParams.get('searchSupplier') || undefined;
    
    const [highlightedRequestId, setHighlightedRequestId] = useState<string | null>(null);
    const [drawerRequestId, setDrawerRequestId] = useState<string | null>(null);

    const [actionModal, setActionModal] = useState<{ show: boolean; action: FinanceActionType; requestId: string | null }>({ show: false, action: null, requestId: null });
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    useEffect(() => {
        loadData();
    }, [filter, page, pageSize, statusCodes, currencyCode, searchSupplier]);

    useEffect(() => {
        const focusId = searchParams.get('highlightRequestId');
        if (focusId) {
            setHighlightedRequestId(focusId);
            setTimeout(() => {
                const el = document.getElementById(`payment-row-${focusId}`);
                if (el) {
                    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
                }
            }, 300);

            // Remove highlight class after 5 seconds
            setTimeout(() => setHighlightedRequestId(null), 5000);

            // Clean up URL
            const newParams = new URLSearchParams(searchParams);
            newParams.delete('highlightRequestId');
            window.history.replaceState({}, '', `${window.location.pathname}?${newParams.toString()}`);
        }
    }, [searchParams]);

    const loadData = () => {
        api.finance.getPayments(filter, page, pageSize, undefined, statusCodes, currencyCode, searchSupplier).then(res => setData(res));
    };

    const handleSetParam = (key: string, value: string) => {
        const newParams = new URLSearchParams(searchParams);
        if (value) newParams.set(key, value);
        else newParams.delete(key);
        newParams.set('page', '1');
        setSearchParams(newParams);
    };

    const handlePageChange = (newPage: number) => {
        const newParams = new URLSearchParams(searchParams);
        newParams.set('page', newPage.toString());
        setSearchParams(newParams);
    };

    const handlePageSizeChange = (newPageSize: number) => {
        const newParams = new URLSearchParams(searchParams);
        newParams.set('pageSize', newPageSize.toString());
        newParams.set('page', '1');
        setSearchParams(newParams);
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
                if (payload.file) {
                    await api.attachments.upload(actionModal.requestId, [payload.file], 'PAYMENT_PROOF');
                }
                await api.finance.markAsPaid(actionModal.requestId, new Date().toISOString(), payload.notes || "Liquidado via portal");
            } else if (action === 'RETURN' && payload.notes) {
                await api.finance.returnForAdjustment(actionModal.requestId, payload.notes);
            } else if (action === 'NOTE' && payload.notes) {
                await api.finance.addNote(actionModal.requestId, payload.notes);
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

    const getStatusTooltip = (code: string, name: string) => {
        switch (code) {
            case 'DRAFT': return 'Pedido em rascunho. Não enviado.';
            case 'WAITING_QUOTATION': return 'Aguardando área de compras cotar valores do mercado.';
            case 'IN_ADJUSTMENT': return 'Devolvido para ajustes de informações ou anexos.';
            case 'WAITING_AREA_APPROVAL': return 'Aguardando aprovação do gestor de área.';
            case 'WAITING_FINAL_APPROVAL': return 'Aguardando aprovação da diretoria.';
            case 'WAITING_PO': return 'Aguardando emissão e envio do PO.';
            case 'WAITING_PAYMENT': return 'Aguardando tesouraria planejar ou realizar pagamento.';
            case 'PAYMENT_SCHEDULED': return 'Pagamento agendado para data futura.';
            case 'PAID':
            case 'PAYMENT_COMPLETED': return 'O pagamento foi liquidado e aprovado.';
            case 'IN_FOLLOWUP': return 'Pagamento ok. Aguardando recebimento físico dos itens.';
            case 'CANCELLED': return 'Pedido foi cancelado e sem prosseguimento.';
            default: return `Status atual: ${name}`;
        }
    };

    return (
        <PageContainer>
            <style>{`
                @keyframes highlightPulse {
                    0% { box-shadow: 0 0 0 0 rgba(220, 38, 38, 0.4); border-color: #dc2626; }
                    70% { box-shadow: 0 0 0 10px rgba(220, 38, 38, 0); border-color: transparent; }
                    100% { box-shadow: 0 0 0 0 rgba(220, 38, 38, 0); border-color: transparent; }
                }
                .section-attention-highlight td {
                    animation: highlightPulse 1.5s ease-out 3;
                    border-top: 2px solid #dc2626 !important;
                    border-bottom: 2px solid #dc2626 !important;
                }
                .section-attention-highlight td:first-child {
                    border-left: 2px solid #dc2626 !important;
                }
                .section-attention-highlight td:last-child {
                    border-right: 2px solid #dc2626 !important;
                }
            `}</style>
            <PageHeader
                title="Obrigações & Pagamentos"
                actions={
                    filter ? (
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', backgroundColor: '#fff7ed', padding: '6px 12px', border: '2px solid #f97316', borderRadius: 'var(--radius-md, 6px)', fontWeight: 800, color: '#c2410c' }}>
                            <span>Filtro Ativo: {filter.toUpperCase()}</span>
                            <button
                                onClick={() => {
                                    const p = new URLSearchParams(searchParams);
                                    p.delete('filter');
                                    p.set('page', '1');
                                    setSearchParams(p);
                                }}
                                style={{ background: 'transparent', border: 'none', color: '#c2410c', cursor: 'pointer', display: 'flex', alignItems: 'center', padding: '2px', borderRadius: '4px' }}
                                title="Remover filtro"
                                onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'rgba(194, 65, 12, 0.1)'}
                                onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                            >
                                <X size={16} />
                            </button>
                        </div>
                    ) : undefined
                }
            />

            <div style={{ display: 'flex', gap: '16px', marginBottom: '16px', alignItems: 'flex-end', backgroundColor: '#fff', padding: '16px 20px', borderRadius: 'var(--radius-lg, 8px)', border: '1px solid var(--color-border)', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <label style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Status</label>
                    <select
                        value={statusCodes || ''}
                        onChange={(e) => handleSetParam('statusCodes', e.target.value)}
                        style={{ padding: '8px 12px', borderRadius: 'var(--radius-md, 6px)', border: '1px solid var(--color-border)', width: '220px', fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)' }}
                    >
                        <option value="">Todos os status</option>
                        <option value="PAID,PAYMENT_COMPLETED">Pagos</option>
                        <option value="PAYMENT_SCHEDULED">Agendados</option>
                        <option value="PO_ISSUED">P.O Emitida</option>
                    </select>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <label style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Moeda</label>
                    <select
                        value={currencyCode || ''}
                        onChange={(e) => handleSetParam('currencyCode', e.target.value)}
                        style={{ padding: '8px 12px', borderRadius: 'var(--radius-md, 6px)', border: '1px solid var(--color-border)', width: '150px', fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)' }}
                    >
                        <option value="">Todas</option>
                        <option value="AOA">AOA (Kwanzas)</option>
                        <option value="USD">USD (Dólares)</option>
                        <option value="EUR">EUR (Euros)</option>
                        <option value="ZAR">ZAR (Rands)</option>
                    </select>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: 1, minWidth: '250px' }}>
                    <label style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Fornecedor</label>
                    <div style={{ position: 'relative' }}>
                        <input
                            type="text"
                            placeholder="Buscar fornecedor..."
                            value={searchSupplier || ''}
                            onChange={(e) => handleSetParam('searchSupplier', e.target.value)}
                            style={{ padding: '8px 12px', paddingLeft: '36px', borderRadius: 'var(--radius-md, 6px)', border: '1px solid var(--color-border)', width: '100%', fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)' }}
                        />
                        <Search size={16} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'var(--color-text-muted)' }} />
                    </div>
                </div>
                {(statusCodes || currencyCode || searchSupplier) && (
                    <button
                        onClick={() => {
                            const p = new URLSearchParams(searchParams);
                            p.delete('statusCodes'); p.delete('currencyCode'); p.delete('searchSupplier'); p.set('page', '1');
                            setSearchParams(p);
                        }}
                        style={{ padding: '8px 16px', background: 'transparent', border: 'none', color: '#dc2626', fontWeight: 800, cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.8rem', textTransform: 'uppercase' }}
                    >
                        Limpar
                    </button>
                )}
            </div>

            <StandardTable isEmpty={!data.pagedResult || data.pagedResult.items.length === 0}>
                <thead>
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
                        <tr key={item.id} id={`payment-row-${item.id}`} className={highlightedRequestId === item.id.toString() ? 'section-attention-highlight' : ''} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: i % 2 === 0 ? '#fff' : '#f8fafc' }}>
                            <td style={{ padding: '16px' }}>
                                <div style={{ fontWeight: 800, color: 'var(--color-primary)' }}>{item.requestNumber}</div>
                                <div style={{ fontSize: '0.85rem', color: '#64748b', fontWeight: 600 }}>{item.plantName}</div>
                            </td>
                            <td style={{ padding: '16px' }}>
                                <div style={{ fontWeight: 700 }}>{item.supplierName}</div>
                                <div style={{ fontSize: '0.85rem', color: '#64748b' }}>Req: {item.requesterName}</div>
                            </td>
                            <td style={{ padding: '16px', verticalAlign: 'top' }}>
                                <div style={{ marginBottom: '12px', display: 'flex', alignItems: 'center', gap: '10px' }}>
                                    <ModernTooltip content={
                                        <div style={{ fontSize: '0.8rem', fontWeight: 600 }}>{getStatusTooltip(item.statusCode, item.statusName)}</div>
                                    } side="top">
                                        <span style={{ ...getBadgeStyle(item.statusBadgeColor), cursor: 'help' }}>{item.statusName}</span>
                                    </ModernTooltip>

                                    {item.paidDateUtc ? (
                                        <ModernTooltip content={
                                            <div style={{ fontSize: '0.8rem', fontWeight: 600 }}>Data do pagamento: {new Date(item.paidDateUtc).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}</div>
                                        } side="top">
                                            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: '#15803d', fontWeight: 800, fontSize: '0.75rem', cursor: 'help' }}>
                                                <Check size={16} strokeWidth={3} /> Pago
                                            </span>
                                        </ModernTooltip>
                                    ) : item.scheduledDateUtc ? (
                                        <ModernTooltip content={
                                            <div style={{ fontSize: '0.8rem', fontWeight: 600 }}>Data de agendamento do pagamento: {new Date(item.scheduledDateUtc).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}</div>
                                        } side="top">
                                            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: '#0369a1', fontWeight: 800, fontSize: '0.75rem', cursor: 'help' }}>
                                                <Clock size={16} strokeWidth={3} /> Agendado
                                            </span>
                                        </ModernTooltip>
                                    ) : null}
                                </div>
                                {item.needByDateUtc && (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', marginBottom: '8px' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.80rem', fontWeight: 600, color: '#64748b' }}>
                                            Original: {new Date(item.needByDateUtc).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}
                                        </div>
                                        {(item.scheduledDateUtc || item.isOverdue || item.isDueSoon) && (
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.85rem', fontWeight: item.isOverdue ? 800 : 700, color: item.isOverdue ? '#dc2626' : item.scheduledDateUtc ? '#0284c7' : item.isDueSoon ? '#d97706' : '#475569' }}>
                                                <Clock size={16} />
                                                {item.scheduledDateUtc ? `Agendado: ${new Date(item.scheduledDateUtc).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}` : `Vence: ${new Date(item.needByDateUtc).toLocaleDateString('pt-BR', { timeZone: 'UTC' })}`}
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
                                        { label: 'Detalhes', icon: <FileText size={16} />, onClick: () => setDrawerRequestId(item.id.toString()) },
                                        ...(item.availableFinanceActions.includes('ADD_NOTE') ? [{ label: 'Adicionar Observação', icon: <MessageSquare size={16} />, onClick: () => handleActionClick(item.id, 'NOTE') }] : []),
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
            </StandardTable>

            {data.pagedResult && data.pagedResult.totalCount > 0 && (
                <div style={{
                    padding: '12px 20px',
                    backgroundColor: '#FAFAFA',
                    border: '1px solid var(--color-border)',
                    borderTop: 'none',
                    borderBottomLeftRadius: 'var(--radius-lg, 8px)',
                    borderBottomRightRadius: 'var(--radius-lg, 8px)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontSize: '0.75rem',
                    color: 'var(--color-text-muted)',
                    flexWrap: 'wrap',
                    gap: '32px',
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <span>Pág:</span>
                        <select
                            value={pageSize.toString()}
                            onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                            style={{
                                padding: '2px 6px',
                                fontSize: '0.75rem',
                                border: '1px solid var(--color-border)',
                                borderRadius: 'var(--radius-sm, 4px)',
                                backgroundColor: 'var(--color-bg-surface, #fff)',
                                color: 'var(--color-text-main, #0f172a)',
                            }}
                        >
                            <option value="20">20</option>
                            <option value="50">50</option>
                            <option value="100">100</option>
                        </select>
                    </div>

                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <span>
                            Mostrando {data.pagedResult.items.length > 0 ? (page - 1) * pageSize + 1 : 0} - {Math.min(page * pageSize, data.pagedResult.totalCount)} de {data.pagedResult.totalCount}
                        </span>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <button
                                disabled={page === 1}
                                onClick={() => handlePageChange(page - 1)}
                                style={{
                                    padding: '6px',
                                    border: '1px solid var(--color-border)',
                                    borderRadius: 'var(--radius-sm, 4px)',
                                    backgroundColor: 'var(--color-bg-surface, #fff)',
                                    cursor: page === 1 ? 'default' : 'pointer',
                                    opacity: page === 1 ? 0.4 : 1,
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    color: 'var(--color-text-main, #0f172a)',
                                }}
                            >
                                <ChevronLeft size={14} />
                            </button>
                            <span style={{ fontWeight: 700 }}>
                                {page} / {Math.ceil(data.pagedResult.totalCount / pageSize) > 0 ? Math.ceil(data.pagedResult.totalCount / pageSize) : 1}
                            </span>
                            <button
                                disabled={page >= Math.ceil(data.pagedResult.totalCount / pageSize)}
                                onClick={() => handlePageChange(page + 1)}
                                style={{
                                    padding: '6px',
                                    border: '1px solid var(--color-border)',
                                    borderRadius: 'var(--radius-sm, 4px)',
                                    backgroundColor: 'var(--color-bg-surface, #fff)',
                                    cursor: page >= Math.ceil(data.pagedResult.totalCount / pageSize) ? 'default' : 'pointer',
                                    opacity: page >= Math.ceil(data.pagedResult.totalCount / pageSize) ? 0.4 : 1,
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    color: 'var(--color-text-main, #0f172a)',
                                }}
                            >
                                <ChevronRight size={14} />
                            </button>
                        </div>
                    </div>
                </div>
            )}

            <FinanceActionModal
                show={actionModal.show}
                action={actionModal.action}
                onClose={() => setActionModal({ show: false, action: null, requestId: null })}
                onConfirm={handleConfirmAction}
                processing={processing}
                feedback={feedback}
                onCloseFeedback={() => setFeedback({ type: 'success', message: null })}
            />

            <RequestDrawerPresentation
                isOpen={!!drawerRequestId}
                requestId={drawerRequestId}
                onClose={() => setDrawerRequestId(null)}
            />
        </PageContainer>
    );
}
