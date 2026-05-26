import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, Search, ChevronLeft, ChevronRight, FileText, AlertTriangle, Clock, CheckCircle, XCircle, RotateCcw } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { fetchContracts, ContractListItem, ContractSummary, CONTRACT_STATUS_MAP } from '../../lib/contractsApi';
import { Tooltip } from '../../components/ui/Tooltip';

function StatusBadge({ statusCode, wasReturnedFromApproval }: { statusCode: string; wasReturnedFromApproval?: boolean }) {
    const cfg = CONTRACT_STATUS_MAP[statusCode] || { label: statusCode, color: '#6b7280', bg: '#f3f4f6' };
    return (
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
            <span style={{
                display: 'inline-flex', alignItems: 'center', gap: '4px',
                padding: '4px 12px', borderRadius: 'var(--radius-full)',
                fontSize: '0.75rem', fontWeight: 700, letterSpacing: '0.02em',
                color: cfg.color, backgroundColor: cfg.bg
            }}>
                {statusCode === 'ACTIVE' && <CheckCircle size={12} />}
                {statusCode === 'DRAFT' && <FileText size={12} />}
                {statusCode === 'SUSPENDED' && <XCircle size={12} />}
                {(statusCode === 'UNDER_REVIEW' || statusCode === 'UNDER_TECHNICAL_REVIEW' || statusCode === 'UNDER_FINAL_REVIEW') && <Clock size={12} />}
                {cfg.label}
            </span>
            {statusCode === 'DRAFT' && wasReturnedFromApproval && (
                <Tooltip
                    variant="dark"
                    side="top"
                    content={
                        <span style={{ fontSize: '0.75rem', fontWeight: 600 }}>
                            Este contrato foi enviado para aprovação mas foi devolvido por um aprovador com pedido de correção.
                        </span>
                    }
                >
                    <span style={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        padding: '3px 10px', borderRadius: 'var(--radius-full)',
                        fontSize: '0.72rem', fontWeight: 700, letterSpacing: '0.02em',
                        color: '#92400e', backgroundColor: '#fef3c7',
                        border: '1px solid #fcd34d', cursor: 'help'
                    }}>
                        <RotateCcw size={11} />
                        Devolvido p/ revisão
                    </span>
                </Tooltip>
            )}
        </span>
    );
}

function SummaryCard({ label, value, color, icon }: { label: string; value: number; color: string; icon: React.ReactNode }) {
    return (
        <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            style={{
                flex: 1, minWidth: '160px',
                padding: '1.25rem', borderRadius: 'var(--radius-lg)',
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                display: 'flex', flexDirection: 'column', gap: '0.5rem'
            }}
        >
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-text-muted)', fontSize: '0.8rem', fontWeight: 600 }}>
                <span style={{ color }}>{icon}</span>
                {label}
            </div>
            <div style={{ fontSize: '1.75rem', fontWeight: 800, fontFamily: 'var(--font-family-display)', color }}>
                {value}
            </div>
        </motion.div>
    );
}

export default function ContractsList() {
    const navigate = useNavigate();
    const [items, setItems] = useState<ContractListItem[]>([]);
    const [summary, setSummary] = useState<ContractSummary | null>(null);
    const [loading, setLoading] = useState(true);
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');
    const [page, setPage] = useState(1);
    const [totalCount, setTotalCount] = useState(0);
    const pageSize = 20;

    const loadData = useCallback(async () => {
        setLoading(true);
        try {
            const params: Record<string, string | number> = { page, pageSize };
            if (search) params.search = search;
            if (statusFilter) params.statusCodes = statusFilter;
            const res = await fetchContracts(params);
            setItems(res.items);
            setSummary(res.summary);
            setTotalCount(res.totalCount);
        } catch (err) {
            console.error('Failed to load contracts', err);
        } finally {
            setLoading(false);
        }
    }, [page, search, statusFilter]);

    useEffect(() => { loadData(); }, [loadData]);

    const totalPages = Math.ceil(totalCount / pageSize);

    const formatDate = (iso: string) => {
        if (!iso) return '—';
        return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric' });
    };

    const formatCurrency = (val?: number, code?: string) => {
        if (val === null || val === undefined) return '—';
        return `${val.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${code || ''}`.trim();
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', paddingTop: '1.5rem' }}>
            {/* Summary Cards */}
            {summary && (
                <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
                    <SummaryCard label="Total" value={summary.totalContracts} color="var(--color-primary)" icon={<FileText size={16} />} />
                    <SummaryCard label="Ativos" value={summary.activeContracts} color="#059669" icon={<CheckCircle size={16} />} />
                    <SummaryCard label="Rascunho" value={summary.draftContracts} color="#6b7280" icon={<Clock size={16} />} />
                    <SummaryCard label="Expiram em 30d" value={summary.expiringIn30Days} color="#d97706" icon={<AlertTriangle size={16} />} />
                    <SummaryCard label="Suspensos" value={summary.suspendedContracts} color="#dc2626" icon={<XCircle size={16} />} />
                </div>
            )}

            {/* Toolbar */}
            <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', flexWrap: 'wrap' }}>
                <div style={{
                    display: 'flex', alignItems: 'center', gap: '8px', flex: 1, minWidth: '200px',
                    backgroundColor: 'var(--color-bg-page)', padding: '10px 16px',
                    borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)'
                }}>
                    <Search size={16} style={{ color: 'var(--color-text-muted)' }} />
                    <input
                        placeholder="Pesquisar contratos..."
                        value={search}
                        onChange={e => { setSearch(e.target.value); setPage(1); }}
                        style={{
                            flex: 1, border: 'none', outline: 'none', backgroundColor: 'transparent',
                            fontSize: '0.9rem', color: 'var(--color-text-main)', fontFamily: 'var(--font-family-body)'
                        }}
                    />
                </div>

                <select
                    value={statusFilter}
                    onChange={e => { setStatusFilter(e.target.value); setPage(1); }}
                    style={{
                        padding: '10px 16px', borderRadius: 'var(--radius-md)',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                        fontSize: '0.85rem', fontWeight: 600, cursor: 'pointer',
                        color: 'var(--color-text-main)', fontFamily: 'var(--font-family-body)'
                    }}
                >
                    <option value="">Todos os Status</option>
                    <option value="DRAFT">Rascunho</option>
                    <option value="UNDER_REVIEW">Em Revisão (Legacy)</option>
                    <option value="UNDER_TECHNICAL_REVIEW">Revisão Técnica</option>
                    <option value="UNDER_FINAL_REVIEW">Aprovação Final</option>
                    <option value="ACTIVE">Ativo</option>
                    <option value="SUSPENDED">Suspenso</option>
                    <option value="EXPIRED">Expirado</option>
                    <option value="TERMINATED">Terminado</option>
                </select>

                <button
                    onClick={() => navigate('/contracts/new')}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '8px',
                        padding: '10px 24px', borderRadius: 'var(--radius-md)',
                        backgroundColor: 'var(--color-primary)', color: 'white',
                        border: 'none', cursor: 'pointer', fontWeight: 700, fontSize: '0.85rem',
                        fontFamily: 'var(--font-family-body)', transition: 'all 0.2s'
                    }}
                >
                    <Plus size={16} /> Novo Contrato
                </button>
            </div>

            {/* Table */}
            <div style={{ overflowX: 'auto', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
                    <thead>
                        <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                            {['Nº Contrato', 'Título', 'Tipo', 'Fornecedor', 'Status', 'Início', 'Expiração', 'Valor', 'Obrigações'].map(h => (
                                <th key={h} style={{
                                    padding: '12px 16px', textAlign: 'left', fontWeight: 700,
                                    color: 'var(--color-text-muted)', fontSize: '0.75rem',
                                    textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap'
                                }}>
                                    {h}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {loading ? (
                            <tr><td colSpan={9} style={{ padding: '3rem', textAlign: 'center', color: 'var(--color-text-muted)' }}>Carregando...</td></tr>
                        ) : items.length === 0 ? (
                            <tr><td colSpan={9} style={{ padding: '3rem', textAlign: 'center', color: 'var(--color-text-muted)' }}>Nenhum contrato encontrado.</td></tr>
                        ) : (
                            <AnimatePresence>
                                {items.map((c, i) => (
                                    <motion.tr
                                        key={c.id}
                                        initial={{ opacity: 0 }}
                                        animate={{ opacity: 1 }}
                                        exit={{ opacity: 0 }}
                                        transition={{ delay: i * 0.02 }}
                                        onClick={() => navigate(`/contracts/${c.id}`)}
                                        style={{
                                            cursor: 'pointer', borderBottom: '1px solid var(--color-border)',
                                            transition: 'background-color 0.15s',
                                            backgroundColor: (c.statusCode === 'DRAFT' && c.wasReturnedFromApproval)
                                                ? '#fffbeb'
                                                : 'transparent'
                                        }}
                                        onMouseEnter={e => (e.currentTarget.style.backgroundColor = (c.statusCode === 'DRAFT' && c.wasReturnedFromApproval) ? '#fef3c7' : 'rgba(var(--color-primary-rgb), 0.04)')}
                                        onMouseLeave={e => (e.currentTarget.style.backgroundColor = (c.statusCode === 'DRAFT' && c.wasReturnedFromApproval) ? '#fffbeb' : 'transparent')}
                                    >
                                        <td style={{ padding: '12px 16px', fontWeight: 700, fontFamily: 'var(--font-family-display)', color: 'var(--color-primary)', whiteSpace: 'nowrap' }}>
                                            {c.contractNumber}
                                        </td>
                                        <td style={{ padding: '12px 16px', fontWeight: 600, maxWidth: '200px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            {c.title}
                                        </td>
                                        <td style={{ padding: '12px 16px', color: 'var(--color-text-muted)' }}>{c.contractTypeName}</td>
                                        <td style={{ padding: '12px 16px' }}>{c.supplierName || c.counterpartyName || '—'}</td>
                                        <td style={{ padding: '12px 16px' }}><StatusBadge statusCode={c.statusCode} wasReturnedFromApproval={c.wasReturnedFromApproval} /></td>
                                        <td style={{ padding: '12px 16px', whiteSpace: 'nowrap' }}>{formatDate(c.effectiveDateUtc)}</td>
                                        <td style={{ padding: '12px 16px', whiteSpace: 'nowrap' }}>{formatDate(c.expirationDateUtc)}</td>
                                        <td style={{ padding: '12px 16px', fontWeight: 700, fontFamily: 'var(--font-family-display)', whiteSpace: 'nowrap' }}>
                                            {formatCurrency(c.totalContractValue, c.currencyCode)}
                                        </td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>
                                            <span style={{ fontWeight: 700 }}>{c.pendingObligationCount}</span>
                                            <span style={{ color: 'var(--color-text-muted)' }}> / {c.obligationCount}</span>
                                        </td>
                                    </motion.tr>
                                ))}
                            </AnimatePresence>
                        )}
                    </tbody>
                </table>
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.5rem 0' }}>
                    <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                        {totalCount} contrato{totalCount !== 1 ? 's' : ''} encontrado{totalCount !== 1 ? 's' : ''}
                    </span>
                    <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                        <button
                            disabled={page <= 1}
                            onClick={() => setPage(p => p - 1)}
                            style={{
                                padding: '8px', borderRadius: 'var(--radius-md)',
                                border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                                cursor: page <= 1 ? 'default' : 'pointer', opacity: page <= 1 ? 0.5 : 1
                            }}
                        >
                            <ChevronLeft size={16} />
                        </button>
                        <span style={{ fontSize: '0.85rem', fontWeight: 600 }}>
                            {page} / {totalPages}
                        </span>
                        <button
                            disabled={page >= totalPages}
                            onClick={() => setPage(p => p + 1)}
                            style={{
                                padding: '8px', borderRadius: 'var(--radius-md)',
                                border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                                cursor: page >= totalPages ? 'default' : 'pointer', opacity: page >= totalPages ? 0.5 : 1
                            }}
                        >
                            <ChevronRight size={16} />
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
}
