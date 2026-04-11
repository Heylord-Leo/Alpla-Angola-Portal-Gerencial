import { useEffect, useState, useMemo } from 'react';
import { api } from '../../lib/api';
import { FinanceHistoryItemDto, PagedResult } from '../../types';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';

// Icons mapping roughly
const SearchIcon = () => (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="square" strokeLinejoin="miter">
        <circle cx="11" cy="11" r="8"></circle>
        <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
    </svg>
);

const DownloadIcon = () => (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="square" strokeLinejoin="miter">
        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
        <polyline points="7 10 12 15 17 10"></polyline>
        <line x1="12" y1="15" x2="12" y2="3"></line>
    </svg>
);

const ActivityIcon = () => (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="square" strokeLinejoin="miter">
        <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>
    </svg>
);

export default function FinanceHistory() {
    const [data, setData] = useState<PagedResult<FinanceHistoryItemDto> | null>(null);
    const [searchQuery, setSearchQuery] = useState('');
    const [actionFilter, setActionFilter] = useState('');
    const [isLoading, setIsLoading] = useState(true);

    const loadData = () => {
        setIsLoading(true);
        api.finance.getHistory(1, 100, searchQuery, actionFilter)
            .then(res => {
                setData(res);
                setIsLoading(false);
            })
            .catch(err => {
                console.error(err);
                setIsLoading(false);
            });
    };

    useEffect(() => {
        const timeoutId = setTimeout(() => {
            loadData();
        }, 500); // debounce search
        return () => clearTimeout(timeoutId);
    }, [searchQuery, actionFilter]);

    const handleExport = async () => {
        try {
            await api.finance.exportHistory(searchQuery, actionFilter);
        } catch (e) {
            alert('Falha ao exportar auditoria');
        }
    };

    // Grouping by Date (just YY-MM-DD)
    const groupedItems = useMemo(() => {
        if (!data || !data.items) return {};
        const groups: Record<string, FinanceHistoryItemDto[]> = {};
        
        data.items.forEach(item => {
            const dateStr = new Date(item.createdAtUtc).toLocaleDateString('pt-AO', {
                year: 'numeric', month: 'long', day: 'numeric'
            });
            if (!groups[dateStr]) groups[dateStr] = [];
            groups[dateStr].push(item);
        });
        
        return groups;
    }, [data]);

    const formatCurrency = (val: number | null, currency: string | null) => {
        if (val === null || val === undefined) return '---';
        return new Intl.NumberFormat('pt-AO', { style: 'currency', currency: currency || 'AOA' }).format(val);
    };

    const getActionProps = (action: string) => {
        switch (action) {
            case 'PAYMENT_SCHEDULED': return { label: 'Agendado', bg: '#eff6ff', color: '#2563eb', border: '#bfdbfe' };
            case 'PAYMENT_COMPLETED': return { label: 'Pago', bg: '#f0fdf4', color: '#16a34a', border: '#bbf7d0' };
            case 'DOCUMENTO ADICIONADO': return { label: 'Comprovativo', bg: '#fdf4ff', color: '#c026d3', border: '#f5d0fe' };
            case 'NOTA_FINANCEIRA': return { label: 'Nota Interna', bg: '#f8fafc', color: '#475569', border: '#e2e8f0' };
            case 'FINANCE_RETURN_ADJUSTMENT': return { label: 'Devolvido', bg: '#fff7ed', color: '#ea580c', border: '#fed7aa' };
            default: return { label: action, bg: '#f3f4f6', color: '#374151', border: '#d1d5db' };
        }
    };

    return (
        <PageContainer>
            
            <PageHeader
                title="Auditoria Financeira"
                subtitle="Acompanhe o percurso de pagamentos, agendamentos e alterações financeiras."
                actions={
                    <button
                        onClick={handleExport}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '12px 24px',
                            backgroundColor: 'white',
                            color: 'var(--color-primary)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-sm)',
                            fontWeight: 700,
                            fontSize: '14px',
                            textTransform: 'uppercase',
                            cursor: 'pointer',
                        }}
                    >
                        <DownloadIcon /> Exportar CSV
                    </button>
                }
            />

            <SearchFilterBar
                searchValue={searchQuery}
                onSearchChange={setSearchQuery}
                searchPlaceholder="PESQUISE POR PEDIDO, TÍTULO, RESPONSÁVEL OU PALAVRAS-CHAVE..."
                tabs={[
                    { id: '', label: 'Todos' },
                    { id: 'PAYMENT_SCHEDULED', label: 'Agendados' },
                    { id: 'PAYMENT_COMPLETED', label: 'Pagos' },
                    { id: 'DOCUMENTO ADICIONADO', label: 'Comprovativos' },
                    { id: 'NOTA_FINANCEIRA', label: 'Notas' },
                    { id: 'FINANCE_RETURN_ADJUSTMENT', label: 'Devoluções' }
                ]}
                activeTabId={actionFilter}
                onTabChange={(id) => setActionFilter(id)}
            />

            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '2px solid var(--color-border)',
                boxShadow: 'var(--shadow-brutal)',
                padding: '32px'
            }}>
                {isLoading && (!data || !data.items) ? (
                    <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold' }}>Carregando dados...</div>
                ) : Object.keys(groupedItems).length === 0 ? (
                    <div style={{ padding: '60px', textAlign: 'center', color: '#64748b' }}>
                        <ActivityIcon />
                        <h3 style={{ marginTop: '16px', fontWeight: 800 }}>Nenhum Registro</h3>
                        <p>Nenhuma movimentação encontrada para o filtro atual.</p>
                    </div>
                ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
                        {Object.entries(groupedItems).map(([dateLabel, items]) => (
                            <div key={dateLabel}>
                                <div style={{ 
                                    padding: '8px 16px', 
                                    backgroundColor: '#f1f5f9', 
                                    display: 'inline-block',
                                    fontWeight: 800,
                                    fontSize: '13px',
                                    color: '#334155',
                                    border: '2px solid var(--color-border)',
                                    marginBottom: '16px',
                                    textTransform: 'uppercase'
                                }}>
                                    {dateLabel}
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', paddingLeft: '8px', borderLeft: '3px solid #e2e8f0' }}>
                                    {items.map(item => {
                                        const action = getActionProps(item.actionTaken);
                                        return (
                                            <div 
                                                key={item.id} 
                                                style={{ 
                                                    position: 'relative',
                                                    backgroundColor: 'var(--color-bg-surface)', 
                                                    border: '2px solid var(--color-border)', 
                                                    padding: '20px',
                                                    marginLeft: '24px',
                                                    display: 'flex',
                                                    justifyContent: 'space-between',
                                                    alignItems: 'center',
                                                    transition: 'all 0.15s ease'
                                                }}
                                                onMouseOver={(e) => { e.currentTarget.style.transform = 'translateX(4px)'; e.currentTarget.style.borderColor = action.color; }}
                                                onMouseOut={(e) => { e.currentTarget.style.transform = 'translateX(0)'; e.currentTarget.style.borderColor = 'var(--color-border)'; }}
                                            >
                                                {/* Connecting line dot */}
                                                <div style={{ position: 'absolute', left: '-29px', top: '50%', transform: 'translateY(-50%)', width: '12px', height: '12px', borderRadius: '50%', backgroundColor: action.color, border: '2px solid #fff' }}></div>

                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxWidth: '60%' }}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                                        <span style={{ 
                                                            padding: '4px 8px', 
                                                            backgroundColor: action.bg, 
                                                            color: action.color, 
                                                            border: `1px solid ${action.border}`,
                                                            fontWeight: 800, 
                                                            fontSize: '11px',
                                                            textTransform: 'uppercase'
                                                        }}>
                                                            {action.label}
                                                        </span>
                                                        <span style={{ fontWeight: 700, color: '#64748b', fontSize: '13px' }}>
                                                            {new Date(item.createdAtUtc).toLocaleTimeString('pt-AO', { hour: '2-digit', minute: '2-digit' })}
                                                        </span>
                                                    </div>
                                                    
                                                    <div style={{ fontWeight: 600, color: '#334155', fontSize: '15px' }}>
                                                        <span style={{ color: 'var(--color-primary)', fontWeight: 800, marginRight: '6px' }}>{item.actorName}</span>
                                                        {item.comment ? (
                                                            <span>: <span style={{ fontWeight: 500 }}>{item.comment}</span></span>
                                                        ) : (
                                                            <span style={{ color: '#94a3b8', fontStyle: 'italic', fontWeight: 400 }}> Executou a ação.</span>
                                                        )}
                                                    </div>

                                                    <a href={`/requests/${item.requestId}`} target="_blank" rel="noreferrer" style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', textDecoration: 'none', color: '#0f172a', fontWeight: 800, fontSize: '14px', marginTop: '8px' }}>
                                                        <span style={{ backgroundColor: '#e2e8f0', padding: '2px 6px', fontSize: '12px' }}>#{item.requestNumber}</span> 
                                                        <span style={{ textDecoration: 'underline' }}>{item.requestTitle}</span>
                                                    </a>
                                                </div>

                                                <div style={{ textAlign: 'right' }}>
                                                    <div style={{ fontSize: '1.2rem', fontWeight: 900, color: '#0f172a' }}>
                                                        {formatCurrency(item.amount, item.currencyCode)}
                                                    </div>
                                                </div>
                                            </div>
                                        );
                                    })}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
            
        </PageContainer>
    );
}
