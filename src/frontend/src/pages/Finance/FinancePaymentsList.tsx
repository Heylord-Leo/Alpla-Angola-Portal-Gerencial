import { useEffect, useState, useRef, ReactNode, CSSProperties } from 'react';
import { api } from '../../lib/api';
import { FinanceObligationsResponseDto, FinanceObligationDto, FinanceObligationContainerDto, FinanceCurrencyAmountDto } from '../../types';
import { useSearchParams } from 'react-router-dom';
import { Check, Clock, Search, X, CalendarClock, AlertTriangle, FileText, CreditCard, AlertCircle, CheckCircle2, Eye, MessageSquare, StickyNote, XCircle, RotateCcw, SlidersHorizontal } from 'lucide-react';
import { FinanceActionModal, FinanceActionType } from '../../components/modals/FinanceActionModal';
import { logger } from '../../lib/logger';
import { FeedbackType } from '../../components/ui/Feedback';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { KPICard } from '../../components/ui/KPICard';
import { KebabMenu, KebabOption } from '../../components/ui/KebabMenu';
import { ModernTooltip } from '../../components/ui/ModernTooltip';
import { RequestDrawerPresentation } from '../Requests/components/modern/RequestDrawerPresentation';
import { isAdvanceGroupStatus, resolveAttachmentUploadParams, resolveObligationRowFlags, resolveObligationActionPlan, obligationActionLabel, ObligationActionCode, FINANCE_DEFAULT_SORT, FINANCE_SORT_OPTIONS, FINANCE_CLEAR_KEYS, resolveNoteTooltip, countAdvancedFilters } from '../../lib/financePaymentsView';

// ── Card → filter mapping (work queues) ──
type CardKey = 'needsScheduling' | 'needsPayment' | 'dueToday' | 'overdue' | 'paidWaitingReceiving';

const formatMoney = (amount: number, currency?: string | null) =>
    `${(currency || '—')} ${amount.toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

function CurrencyTotals({ amounts, muted = false }: { amounts: FinanceCurrencyAmountDto[]; muted?: boolean }) {
    if (!amounts || amounts.length === 0) return <span style={{ opacity: 0.5 }}>—</span>;
    return (
        <span style={{ color: muted ? 'var(--color-text-muted)' : 'inherit' }}>
            {amounts.map((a, i) => (
                <span key={a.currencyCode}>{i > 0 ? ' · ' : ''}{formatMoney(a.amount, a.currencyCode)}</span>
            ))}
        </span>
    );
}

export default function FinancePaymentsList() {
    const [data, setData] = useState<FinanceObligationsResponseDto | null>(null);
    const [searchParams, setSearchParams] = useSearchParams();

    const page = parseInt(searchParams.get('page') || '1');
    const pageSize = parseInt(searchParams.get('pageSize') || '20');
    const search = searchParams.get('search') || undefined;
    const currencyCode = searchParams.get('currencyCode') || undefined;
    const actionClass = searchParams.get('actionClass') || undefined;
    const overdueOnly = searchParams.get('overdueOnly') === 'true';
    const dueTodayOnly = searchParams.get('dueTodayOnly') === 'true';
    const actionableOnly = searchParams.get('actionableOnly') === 'true';
    const sortBy = searchParams.get('sortBy') || FINANCE_DEFAULT_SORT;
    const companyId = searchParams.get('companyId') ? parseInt(searchParams.get('companyId')!) : undefined;
    const plantId = searchParams.get('plantId') ? parseInt(searchParams.get('plantId')!) : undefined;
    const departmentId = searchParams.get('departmentId') ? parseInt(searchParams.get('departmentId')!) : undefined;

    const [drawerRequestId, setDrawerRequestId] = useState<string | null>(null);
    const [actionModal, setActionModal] = useState<{ show: boolean; action: FinanceActionType; requestId: string | null; groupId: string | null; expectedAmount?: number; obligation?: FinanceObligationDto }>({ show: false, action: null, requestId: null, groupId: null });
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [searchInput, setSearchInput] = useState(search || '');
    const [showMoreFilters, setShowMoreFilters] = useState(!!(companyId || plantId || departmentId));
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [departments, setDepartments] = useState<any[]>([]);

    useEffect(() => {
        api.lookups.getCompanies().then(setCompanies).catch(() => {});
        api.lookups.getDepartments().then(setDepartments).catch(() => {});
    }, []);
    useEffect(() => {
        api.lookups.getPlants(companyId).then(setPlants).catch(() => {});
    }, [companyId]);

    useEffect(() => { setSearchInput(search || ''); }, [search]);
    const firstSearch = useRef(true);
    useEffect(() => {
        if (firstSearch.current) { firstSearch.current = false; return; }
        const handler = setTimeout(() => {
            if (searchInput !== (search || '')) setParam('search', searchInput);
        }, 500);
        return () => clearTimeout(handler);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [searchInput]);

    useEffect(() => { loadData(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [page, pageSize, search, currencyCode, actionClass, overdueOnly, dueTodayOnly, actionableOnly, sortBy, companyId, plantId, departmentId]);

    const loadData = () => {
        api.finance.getObligations({ page, pageSize, search, currencyCode, actionClass, overdueOnly, dueTodayOnly, actionableOnly, sortBy, companyId, plantId, departmentId })
            .then(setData)
            .catch(err => setFeedback({ type: 'error', message: err?.message || 'Falha ao carregar obrigações.' }));
    };

    // Apply one or MORE param changes atomically — a single URLSearchParams + one setSearchParams.
    // (Two sequential setParam calls each rebuild from the same stale `searchParams`, so the second
    // clobbers the first — that was the company/plant bug: selecting a company then clearing plant
    // discarded the company. Always co-change dependent params through setParams.)
    const setParams = (updates: Record<string, string | boolean | undefined>) => {
        const p = new URLSearchParams(searchParams);
        for (const [key, value] of Object.entries(updates)) {
            if (value === undefined || value === '' || value === false) p.delete(key);
            else p.set(key, String(value));
        }
        p.set('page', '1');
        setSearchParams(p);
    };
    const setParam = (key: string, value: string | boolean | undefined) => setParams({ [key]: value });

    // Cards act as mutually-exclusive work-queue filters (overdue/dueToday are urgency overlays).
    const selectCard = (card: CardKey) => {
        const p = new URLSearchParams(searchParams);
        ['actionClass', 'overdueOnly', 'dueTodayOnly'].forEach(k => p.delete(k));
        const already =
            (card === 'needsScheduling' && actionClass === 'NEEDS_SCHEDULING') ||
            (card === 'needsPayment' && actionClass === 'NEEDS_PAYMENT') ||
            (card === 'paidWaitingReceiving' && actionClass === 'PAID_WAITING_RECEIVING') ||
            (card === 'overdue' && overdueOnly) ||
            (card === 'dueToday' && dueTodayOnly);
        if (!already) {
            if (card === 'needsScheduling') p.set('actionClass', 'NEEDS_SCHEDULING');
            else if (card === 'needsPayment') p.set('actionClass', 'NEEDS_PAYMENT');
            else if (card === 'paidWaitingReceiving') p.set('actionClass', 'PAID_WAITING_RECEIVING');
            else if (card === 'overdue') p.set('overdueOnly', 'true');
            else if (card === 'dueToday') p.set('dueTodayOnly', 'true');
        }
        p.set('page', '1');
        setSearchParams(p);
    };

    const clearFilters = () => {
        const p = new URLSearchParams(searchParams);
        FINANCE_CLEAR_KEYS.forEach(k => p.delete(k)); // includes sortBy → resets to default newest-first
        p.set('page', '1');
        setSearchParams(p);
    };

    // ── Action dispatcher — Detalhes opens the drawer; every mutation (incl. NOTE) opens the modal ──
    const onObligationAction = (o: FinanceObligationDto, code: ObligationActionCode) => {
        if (code === 'DETAILS') { setDrawerRequestId(o.requestId); return; }
        const action = code as FinanceActionType; // 'SCHEDULE' | 'PAY' | 'CANCEL_SCHEDULE' | 'RETURN' | 'NOTE'
        const expectedAmount = (o.plannedAmount && o.plannedAmount > 0 ? o.plannedAmount : o.groupAmount) || undefined;
        setActionModal({ show: true, action, requestId: o.requestId, groupId: o.requestPoGroupId, expectedAmount, obligation: o });
        setFeedback({ type: 'success', message: null });
    };

    const handleConfirmAction = async (action: FinanceActionType, payload: { date?: string; notes?: string; file?: File | null; amount?: string }) => {
        if (!actionModal.requestId) return;
        setProcessing(true);
        setFeedback({ type: 'success', message: null });
        try {
            const isAdvance = isAdvanceGroupStatus(actionModal.obligation?.groupStatusCode);

            if (action === 'SCHEDULE' && payload.date) {
                const up = resolveAttachmentUploadParams(action, !!payload.file, actionModal.groupId);
                if (up) await api.attachments.upload(actionModal.requestId, [payload.file!], up.typeCode, up.poGroupId);
                if (isAdvance) await api.requests.scheduleAdvancePayment(actionModal.requestId, { requestPoGroupId: actionModal.groupId!, scheduledDate: new Date(payload.date).toISOString(), comment: payload.notes || 'Adiantamento agendado via portal' });
                else await api.finance.schedulePayment(actionModal.requestId, actionModal.groupId!, new Date(payload.date).toISOString(), payload.notes || 'Agendado via portal');
            } else if (action === 'PAY') {
                let attachmentId: string | undefined;
                const up = resolveAttachmentUploadParams(action, !!payload.file, actionModal.groupId);
                if (up) {
                    const res = await api.attachments.upload(actionModal.requestId, [payload.file!], up.typeCode, up.poGroupId);
                    if (res && res.length > 0) attachmentId = res[0].id;
                }
                const actualPaidAmount = payload.amount ? parseFloat(payload.amount) : undefined;
                const paidDate = payload.date ? new Date(payload.date).toISOString() : undefined;
                if (isAdvance) await api.requests.confirmAdvancePayment(actionModal.requestId, { requestPoGroupId: actionModal.groupId!, actualPaidAmount: actualPaidAmount || 0, paidDate: paidDate!, comment: payload.notes || 'Adiantamento liquidado via portal', paymentProofAttachmentId: attachmentId });
                else await api.finance.markAsPaid(actionModal.requestId, actionModal.groupId!, attachmentId!, actualPaidAmount!, paidDate!, payload.notes || 'Liquidado via portal');
            } else if (action === 'RETURN' && payload.notes) {
                await api.finance.returnForAdjustment(actionModal.requestId, payload.notes, actionModal.groupId ?? undefined);
            } else if (action === 'NOTE' && payload.notes) {
                await api.finance.addNote(actionModal.requestId, payload.notes);
            } else if (action === 'CANCEL_SCHEDULE' && payload.notes) {
                await api.finance.cancelSchedule(actionModal.requestId, actionModal.groupId!, payload.notes);
            }
            setActionModal({ show: false, action: null, requestId: null, groupId: null });
            loadData();
        } catch (err: any) {
            const msg = err instanceof Error ? err.message : (err?.response?.data?.message || 'Falha ao executar ação.');
            logger.error(`Erro ação financeira ${actionModal.action} pedido ${actionModal.requestId}: ${msg}`, err, 'Global');
            setFeedback({ type: 'error', message: msg });
        } finally { setProcessing(false); }
    };

    if (!data) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold' }}>Carregando obrigações...</div>;

    const s = data.summary;
    const totalPages = Math.max(1, Math.ceil(data.pagedResult.totalCount / pageSize));

    const cards: { key: CardKey; label: string; count: number; amounts: FinanceCurrencyAmountDto[]; color: string; icon: ReactNode; selected: boolean }[] = [
        { key: 'needsScheduling', label: 'Aguardando Agendamento', count: s.needsScheduling.count, amounts: s.needsScheduling.amountsByCurrency, color: '#0284c7', icon: <Clock size={20} />, selected: actionClass === 'NEEDS_SCHEDULING' },
        { key: 'needsPayment', label: 'Pagamento Pendente', count: s.needsPayment.count, amounts: s.needsPayment.amountsByCurrency, color: '#d97706', icon: <CreditCard size={20} />, selected: actionClass === 'NEEDS_PAYMENT' },
        { key: 'dueToday', label: 'Vencimento Hoje', count: s.dueToday.count, amounts: s.dueToday.amountsByCurrency, color: '#ea580c', icon: <CalendarClock size={20} />, selected: dueTodayOnly },
        { key: 'overdue', label: 'Pagamentos Vencidos', count: s.overdue.count, amounts: s.overdue.amountsByCurrency, color: '#ef4444', icon: <AlertCircle size={20} />, selected: overdueOnly },
        { key: 'paidWaitingReceiving', label: 'Pagos / Aguardando Recebimento', count: s.paidWaitingReceiving.count, amounts: s.paidWaitingReceiving.amountsByCurrency, color: '#16a34a', icon: <CheckCircle2 size={20} />, selected: actionClass === 'PAID_WAITING_RECEIVING' },
    ];

    return (
        <PageContainer>
            <PageHeader
                title="OBRIGAÇÕES & PAGAMENTOS"
                subtitle="Finanças · Priorize pagamentos, acompanhe vencimentos e trate cada fornecedor de forma independente."
            />

            {feedback.message && (
                <div style={{ margin: '8px 0', padding: '10px 14px', borderRadius: '6px', fontSize: '0.85rem', fontWeight: 600,
                    backgroundColor: feedback.type === 'error' ? '#fef2f2' : '#f0fdf4', color: feedback.type === 'error' ? '#b91c1c' : '#15803d',
                    border: `1px solid ${feedback.type === 'error' ? '#fecaca' : '#bbf7d0'}` }}>
                    {feedback.message}
                    <button onClick={() => setFeedback({ type: 'success', message: null })} style={{ float: 'right', background: 'none', border: 'none', cursor: 'pointer', color: 'inherit' }}><X size={14} /></button>
                </div>
            )}

            {/* ── Work-queue cards (KPICard standard, per docs/ui/kpi_cards.md) ── */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(230px, 1fr))', gap: '16px', margin: '16px 0 20px' }}>
                {cards.map(c => (
                    <div key={c.key} style={{ position: 'relative' }}>
                        <KPICard
                            title={c.label}
                            value={c.count}
                            icon={c.icon}
                            color={c.color}
                            borderColor={c.selected ? c.color : undefined}
                            bgColor={c.selected ? `${c.color}0F` : undefined}
                            subtitle={<span style={{ fontSize: '0.8rem', fontWeight: 600 }}><CurrencyTotals amounts={c.amounts} /></span>}
                            onClick={() => selectCard(c.key)}
                            style={{ ['--kpi-padding' as any]: '16px', ['--kpi-value-size' as any]: '1.9rem', ['--kpi-icon-size' as any]: '34px' }}
                        />
                        {c.selected && (
                            <span aria-hidden style={{ position: 'absolute', top: 10, right: 10, width: 9, height: 9, borderRadius: '50%', backgroundColor: c.color, boxShadow: `0 0 0 3px ${c.color}33` }} />
                        )}
                    </div>
                ))}
            </div>

            {/* ── Filter toolbar (primary row + advanced panel) ── */}
            {(() => {
                const ctrl: CSSProperties = { padding: '8px 10px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)', fontSize: '0.85rem', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)', height: '38px' };
                const advCount = countAdvancedFilters({ companyId, plantId, departmentId, currencyCode, actionableOnly, overdueOnly });
                return (
                <div style={{ marginBottom: '16px' }}>
                    {/* PRIMARY TOOLBAR: Search (widest) · Situação financeira · Ordenar · Mais filtros · Limpar */}
                    <div style={{ display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' }}>
                        <div style={{ position: 'relative', flex: '1 1 320px', minWidth: '240px', maxWidth: '520px' }}>
                            <Search size={15} style={{ position: 'absolute', left: 11, top: 11, opacity: 0.4 }} />
                            <input value={searchInput} onChange={e => setSearchInput(e.target.value)} placeholder="Buscar nº, fornecedor, NIF, P.O. ou título..."
                                style={{ ...ctrl, width: '100%', padding: '8px 10px 8px 32px' }} />
                        </div>
                        <select value={actionClass || ''} onChange={e => setParam('actionClass', e.target.value || undefined)} style={{ ...ctrl, borderColor: actionClass ? 'var(--color-primary)' : 'var(--color-border)' }}>
                            <option value="">Situação financeira: todas</option>
                            <option value="NEEDS_SCHEDULING">Aguardando Agendamento</option>
                            <option value="NEEDS_PAYMENT">Pagamento Pendente</option>
                            <option value="FISCAL_DOCUMENT_PENDING">Documento Fiscal Pendente</option>
                            <option value="PAID_WAITING_RECEIVING">Pagos / Aguardando Recebimento</option>
                        </select>
                        <select value={sortBy} onChange={e => setParam('sortBy', e.target.value === FINANCE_DEFAULT_SORT ? undefined : e.target.value)} style={ctrl} title="Ordenar">
                            {FINANCE_SORT_OPTIONS.map(o => <option key={o.value} value={o.value}>Ordenar: {o.label}</option>)}
                        </select>
                        <button onClick={() => setShowMoreFilters(v => !v)}
                            style={{ ...ctrl, display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', fontWeight: 600, borderColor: (showMoreFilters || advCount > 0) ? 'var(--color-primary)' : 'var(--color-border)', color: advCount > 0 ? 'var(--color-primary)' : 'var(--color-text-main)' }}>
                            <SlidersHorizontal size={15} /> Mais filtros{advCount > 0 ? ` (${advCount})` : ''}
                        </button>
                        {(advCount > 0 || actionClass || search) && (
                            <button onClick={clearFilters} style={{ ...ctrl, background: 'none', cursor: 'pointer', fontWeight: 600, color: 'var(--color-text-muted)' }}>Limpar filtros</button>
                        )}
                    </div>
                    {/* ADVANCED PANEL — Empresa · Planta · Departamento · Moeda · toggles */}
                    {showMoreFilters && (
                        <div style={{ display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap', marginTop: '10px', padding: '12px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)' }}>
                            <select value={companyId ?? ''}
                                onChange={e => setParams({ companyId: e.target.value || undefined, plantId: undefined })} // atomic: company + clear plant
                                style={{ ...ctrl, borderColor: companyId ? 'var(--color-primary)' : 'var(--color-border)' }}>
                                <option value="">Empresa: todas</option>
                                {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                            </select>
                            <select value={plantId ?? ''} onChange={e => setParam('plantId', e.target.value || undefined)} style={{ ...ctrl, borderColor: plantId ? 'var(--color-primary)' : 'var(--color-border)' }}>
                                <option value="">Planta: todas</option>
                                {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                            </select>
                            <select value={departmentId ?? ''} onChange={e => setParam('departmentId', e.target.value || undefined)} style={{ ...ctrl, borderColor: departmentId ? 'var(--color-primary)' : 'var(--color-border)' }}>
                                <option value="">Departamento: todos</option>
                                {departments.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
                            </select>
                            <select value={currencyCode || ''} onChange={e => setParam('currencyCode', e.target.value || undefined)} style={{ ...ctrl, borderColor: currencyCode ? 'var(--color-primary)' : 'var(--color-border)' }}>
                                <option value="">Moeda: todas</option>
                                <option value="AOA">AOA</option><option value="USD">USD</option><option value="EUR">EUR</option><option value="ZAR">ZAR</option>
                            </select>
                            <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.82rem', fontWeight: 600, cursor: 'pointer' }}>
                                <input type="checkbox" checked={actionableOnly} onChange={e => setParam('actionableOnly', e.target.checked)} /> Só acionáveis
                            </label>
                            <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.82rem', fontWeight: 600, cursor: 'pointer', color: overdueOnly ? 'var(--color-status-red)' : 'inherit' }}>
                                <input type="checkbox" checked={overdueOnly} onChange={e => setParam('overdueOnly', e.target.checked)} /> Apenas vencidos
                            </label>
                        </div>
                    )}
                </div>
                );
            })()}

            {/* ── Obligation containers ── */}
            {data.pagedResult.items.length === 0 ? (
                <div style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 600 }}>Nenhuma obrigação pendente 🎉</div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    {data.pagedResult.items.map(container => (
                        <ContainerCard key={container.requestId} container={container}
                            onOpenRequest={setDrawerRequestId}
                            onAction={onObligationAction} />
                    ))}
                </div>
            )}

            {/* ── Pagination ── */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '18px', fontSize: '0.82rem' }}>
                <span style={{ color: 'var(--color-text-muted)' }}>{data.pagedResult.totalCount} pedido(s)</span>
                <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    <button disabled={page <= 1} onClick={() => { const p = new URLSearchParams(searchParams); p.set('page', String(page - 1)); setSearchParams(p); }}
                        style={{ padding: '6px 10px', borderRadius: '6px', border: '1px solid var(--color-border)', background: 'none', cursor: page <= 1 ? 'default' : 'pointer', opacity: page <= 1 ? 0.4 : 1 }}>Anterior</button>
                    <span>Página {page} de {totalPages}</span>
                    <button disabled={page >= totalPages} onClick={() => { const p = new URLSearchParams(searchParams); p.set('page', String(page + 1)); setSearchParams(p); }}
                        style={{ padding: '6px 10px', borderRadius: '6px', border: '1px solid var(--color-border)', background: 'none', cursor: page >= totalPages ? 'default' : 'pointer', opacity: page >= totalPages ? 0.4 : 1 }}>Próxima</button>
                </div>
            </div>

            <FinanceActionModal
                show={actionModal.show}
                action={actionModal.action}
                isAdvance={isAdvanceGroupStatus(actionModal.obligation?.groupStatusCode)}
                processing={processing}
                feedback={feedback}
                expectedAmount={actionModal.expectedAmount}
                cancelScheduleContext={actionModal.obligation ? {
                    supplierName: actionModal.obligation.supplierName || '—',
                    amount: actionModal.obligation.plannedAmount ?? actionModal.obligation.obligationAmount ?? 0,
                    currencyCode: actionModal.obligation.currencyCode || 'AOA',
                    scheduledDateUtc: actionModal.obligation.scheduledDateUtc ?? null,
                } : undefined}
                onConfirm={handleConfirmAction}
                onClose={() => setActionModal({ show: false, action: null, requestId: null, groupId: null })}
                onCloseFeedback={() => setFeedback({ type: 'success', message: null })}
            />

            <RequestDrawerPresentation requestId={drawerRequestId} isOpen={!!drawerRequestId} onClose={() => setDrawerRequestId(null)} />
        </PageContainer>
    );
}

// ── One Request container with its obligation rows (Option C) ──
function ContainerCard({ container, onOpenRequest, onAction }: {
    container: FinanceObligationContainerDto;
    onOpenRequest: (id: string) => void;
    onAction: (o: FinanceObligationDto, code: ObligationActionCode) => void;
}) {
    const multi = container.obligations.length > 1;
    return (
        <div style={{ border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', overflow: 'hidden', backgroundColor: 'var(--color-bg-surface)', boxShadow: 'var(--shadow-sm)' }}>
            {/* Container header — distinct but not another oversized card */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', padding: '10px 16px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.035)', borderBottom: '1px solid var(--color-border)' }}>
                <div style={{ minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                        <button onClick={() => onOpenRequest(container.requestId)}
                            style={{ background: 'none', border: 'none', padding: 0, cursor: 'pointer', fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-primary)' }}>
                            {container.requestNumber}
                        </button>
                        <NoteIndicator container={container} />
                        <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '440px' }}>{container.title || '—'}</span>
                        {multi && (
                            <span style={{ fontSize: '0.68rem', fontWeight: 700, color: 'var(--color-primary)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.08)', padding: '2px 8px', borderRadius: 'var(--radius-full)' }}>
                                {container.supplierCount || container.obligations.length} fornecedores
                            </span>
                        )}
                    </div>
                    <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                        {[container.department, container.plant].filter(Boolean).join(' · ') || '—'}
                    </div>
                </div>
                <div style={{ textAlign: 'right', fontSize: '0.8rem', fontWeight: 700, whiteSpace: 'nowrap' }}>
                    <CurrencyTotals amounts={container.totalsByCurrency} />
                </div>
            </div>
            <div>
                {container.obligations.map(o => <ObligationRow key={o.requestPoGroupId} o={o} onAction={onAction} />)}
            </div>
        </div>
    );
}

// Subtle request-level Finance-note indicator with hover preview (notes are request-level).
function NoteIndicator({ container }: { container: FinanceObligationContainerDto }) {
    const tip = resolveNoteTooltip(container);
    if (!tip) return null;
    return (
        <ModernTooltip content={
            <div style={{ maxWidth: 320 }}>
                <div style={{ fontWeight: 700, marginBottom: 4 }}>{tip.title}</div>
                <div style={{ fontSize: '0.82rem', whiteSpace: 'pre-wrap' }}>{tip.body}</div>
                {tip.extra && <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: 6 }}>{tip.extra}</div>}
            </div>
        }>
            <span aria-label="Observação de Finanças" style={{ display: 'inline-flex', alignItems: 'center', color: 'var(--color-status-amber)', cursor: 'default' }}>
                <StickyNote size={14} />
            </span>
        </ModernTooltip>
    );
}

function ObligationRow({ o, onAction }: { o: FinanceObligationDto; onAction: (o: FinanceObligationDto, code: ObligationActionCode) => void }) {
    const plan = resolveObligationActionPlan({ groupStatusCode: o.groupStatusCode, financeActions: o.financeActions });
    const advance = isAdvanceGroupStatus(o.groupStatusCode);
    const { isPaid: paid, isNoFinance: noFinance, isOverdue: overdue } = resolveObligationRowFlags(o);

    const menuIcon = (code: ObligationActionCode): ReactNode => {
        switch (code) {
            case 'DETAILS': return <Eye size={14} />;
            case 'NOTE': return <MessageSquare size={14} />;
            case 'PAY': return <Check size={14} />;
            case 'CANCEL_SCHEDULE': return <XCircle size={14} />;
            case 'RETURN': return <RotateCcw size={14} />;
            default: return null;
        }
    };
    const menuOptions: KebabOption[] = plan.menu.map(code => ({
        label: obligationActionLabel(code, advance),
        icon: menuIcon(code),
        onClick: () => onAction(o, code),
    }));

    return (
        <div style={{
            display: 'grid', gridTemplateColumns: 'minmax(0, 1.7fr) minmax(0, 1.1fr) minmax(0, 1fr) minmax(0, 1.1fr) minmax(0, 1.7fr) auto', gap: '12px', alignItems: 'center',
            padding: '12px 16px', borderBottom: '1px solid var(--color-border)',
            borderLeft: overdue ? '3px solid var(--color-status-red)' : '3px solid transparent',
            opacity: paid ? 0.68 : 1,
            backgroundColor: overdue ? 'rgba(var(--color-status-rejected-rgb), 0.05)' : 'transparent'
        }}>
            {/* 1 · Supplier (+ secondary status chip) */}
            <div style={{ minWidth: 0 }}>
                <ModernTooltip content={o.supplierName || 'Fornecedor não definido'}>
                    <div style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {paid && <Check size={13} style={{ color: 'var(--color-status-green)', marginRight: 4, verticalAlign: '-2px' }} />}
                        {o.supplierName || 'Fornecedor não definido'}
                    </div>
                </ModernTooltip>
                <span style={{ display: 'inline-block', marginTop: '3px', fontSize: '0.66rem', fontWeight: 600, color: overdue ? 'var(--color-status-red)' : 'var(--color-text-muted)', backgroundColor: overdue ? 'rgba(var(--color-status-rejected-rgb), 0.1)' : 'rgba(0,0,0,0.04)', padding: '1px 7px', borderRadius: 'var(--radius-full)' }}>
                    {o.operationalStateLabel}
                </span>
            </div>
            {/* 2 · PO */}
            <div style={{ fontSize: '0.8rem', minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {o.purchaseOrderNumber
                    ? <span style={{ fontWeight: 600 }}><FileText size={12} style={{ opacity: 0.5, marginRight: 4, verticalAlign: '-1px' }} />{o.purchaseOrderNumber}</span>
                    : <span style={{ color: 'var(--color-text-muted)' }}>Sem P.O.</span>}
            </div>
            {/* 3 · Due date */}
            <div style={{ fontSize: '0.8rem' }}>
                {o.dueDate
                    ? <span style={{ color: overdue ? 'var(--color-status-red)' : 'var(--color-text-main)', fontWeight: overdue ? 700 : 500 }}>
                        <CalendarClock size={12} style={{ opacity: 0.5, marginRight: 4, verticalAlign: '-1px' }} />
                        {new Date(o.dueDate).toLocaleDateString('pt-AO')}
                        {overdue && <span style={{ display: 'block', fontSize: '0.68rem', fontWeight: 700 }}>Vencido há {o.overdueDays} {o.overdueDays === 1 ? 'dia' : 'dias'}</span>}
                        {o.isDueToday && <span style={{ display: 'block', fontSize: '0.68rem', fontWeight: 700, color: 'var(--color-status-orange)' }}>Vence hoje</span>}
                      </span>
                    : <span style={{ color: 'var(--color-text-muted)' }}>—</span>}
            </div>
            {/* 4 · Amount */}
            <div style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)', whiteSpace: 'nowrap' }}>{formatMoney(o.obligationAmount, o.currencyCode)}</div>
            {/* 5 · NEXT ACTION (primary semantic field) */}
            <div style={{ minWidth: 0 }}>
                <div style={{ fontSize: '0.82rem', fontWeight: 700, color: overdue ? 'var(--color-status-red)' : (paid || noFinance ? 'var(--color-text-muted)' : 'var(--color-text-main)'), overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {noFinance && <AlertTriangle size={13} style={{ color: 'var(--color-status-amber)', marginRight: 4, verticalAlign: '-2px' }} />}
                    {o.nextActionLabel || '—'}
                </div>
                <div style={{ fontSize: '0.68rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{o.responsibleRole}</div>
            </div>
            {/* 6 · Actions — one primary inline, the rest in the kebab */}
            <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', alignItems: 'center' }}>
                {plan.primary && (
                    <button onClick={() => onAction(o, plan.primary!.action)}
                        style={{ padding: '7px 14px', borderRadius: 'var(--radius-md)', border: 'none', backgroundColor: 'var(--color-primary)', color: '#fff', fontWeight: 600, fontSize: '0.78rem', display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', whiteSpace: 'nowrap' }}>
                        {plan.primary.action === 'SCHEDULE' ? <Clock size={14} /> : <CreditCard size={14} />} {plan.primary.label}
                    </button>
                )}
                <KebabMenu options={menuOptions} />
            </div>
        </div>
    );
}
