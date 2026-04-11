import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, Search, Filter, FolderKanban } from 'lucide-react';
import { api } from '../../../../lib/api';
import { RequestListItemDto, DashboardSummaryDto } from '../../../../types';
import { ActionCarouselWidget } from './ActionCarouselWidget';
import { RequestsTableWidget } from './RequestsTableWidget';
import { RequestDrawerPresentation } from './RequestDrawerPresentation';

export function RequestsDashboard() {
    const navigate = useNavigate();

    // Core Data
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
    const [totalCount, setTotalCount] = useState(0);

    // Navigation state (Drawer)
    const [drawerRequestId, setDrawerRequestId] = useState<string | null>(null);

    // Filters and Pagination
    const [searchTerm, setSearchTerm] = useState('');
    const [debouncedSearch, setDebouncedSearch] = useState('');
    const [filterType, setFilterType] = useState<string>('all');
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(20);
    const [sortConfig, setSortConfig] = useState<{ key: string | null; direction: 'asc' | 'desc' }>({ key: null, direction: 'asc' });

    // Debounce search
    useEffect(() => {
        const handler = setTimeout(() => {
            setDebouncedSearch(searchTerm);
            setPage(1);
        }, 500);
        return () => clearTimeout(handler);
    }, [searchTerm]);

    const loadData = useCallback(async () => {
        try {
            setLoading(true);
            let typeIds = undefined;
            if (filterType === 'QUOTATION') typeIds = '1';
            if (filterType === 'PAYMENT') typeIds = '2';

            const data = await api.requests.list(
                debouncedSearch || undefined,
                { typeIds },
                page,
                pageSize
            );

            const pagedResult = data.pagedResult || (data as any).PagedResult;
            const summaryData = data.summary || (data as any).Summary;

            if (pagedResult) {
                setRequests(pagedResult.items || []);
                setTotalCount(pagedResult.totalCount || 0);
            } else {
                setRequests((data as any).items || []);
                setTotalCount((data as any).totalCount || 0);
            }
            if (summaryData) setSummary(summaryData);
        } catch (err) {
            console.error(err);
        } finally {
            setLoading(false);
        }
    }, [debouncedSearch, filterType, page, pageSize]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleRowClick = (requestId: string) => {
        setDrawerRequestId(requestId);
    };

    const handleSort = (key: string) => {
        let direction: 'asc' | 'desc' = 'asc';
        if (sortConfig.key === key && sortConfig.direction === 'asc') direction = 'desc';
        setSortConfig({ key, direction });
    };

    const FILTER_TABS = [
        { id: 'all', label: 'Todos' },
        { id: 'QUOTATION', label: 'Cotações' },
        { id: 'PAYMENT', label: 'Pagamentos' },
    ];

    return (
        <div style={{
            minHeight: '100vh',
            padding: '24px 32px',
            maxWidth: '1400px',
            margin: '0 auto',
            display: 'flex',
            flexDirection: 'column',
            gap: '32px',
        }}>

            {/* ── Header ── */}
            <header style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                flexWrap: 'wrap',
                gap: '16px',
            }}>
                <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '4px' }}>
                        <h1 style={{
                            fontSize: '1.5rem',
                            fontWeight: 900,
                            color: 'var(--color-primary)',
                            margin: 0,
                            letterSpacing: '-0.01em',
                        }}>Pedidos de Compras e Pagamentos</h1>
                        <span style={{
                            padding: '3px 10px',
                            borderRadius: 'var(--radius-full)',
                            fontSize: '0.6rem',
                            fontWeight: 800,
                            backgroundColor: '#EFF6FF',
                            color: 'var(--color-primary)',
                            textTransform: 'uppercase',
                            letterSpacing: '0.06em',
                        }}>Beta</span>
                    </div>
                    <p style={{
                        color: 'var(--color-text-muted)',
                        fontSize: '0.85rem',
                        margin: 0,
                        fontWeight: 500,
                    }}>Gerencie e acompanhe todos os pedidos corporativos em tempo real.</p>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <button
                        onClick={() => navigate('/requests')}
                        style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            gap: '8px',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            color: 'var(--color-text-main)',
                            padding: '10px 18px',
                            borderRadius: 'var(--radius-md)',
                            fontWeight: 600,
                            fontSize: '0.85rem',
                            cursor: 'pointer',
                            transition: 'all 0.15s ease',
                            boxShadow: 'var(--shadow-sm)',
                        }}
                        onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                        onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; }}
                    >
                        <FolderKanban size={18} style={{ color: 'var(--color-text-muted)' }} />
                        Modo Clássico
                    </button>
                    <button
                        onClick={() => navigate('/requests/new')}
                        style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            gap: '8px',
                            backgroundColor: 'var(--color-primary)',
                            border: '1px solid var(--color-primary)',
                            color: '#fff',
                            padding: '10px 18px',
                            borderRadius: 'var(--radius-md)',
                            fontWeight: 600,
                            fontSize: '0.85rem',
                            cursor: 'pointer',
                            transition: 'all 0.15s ease',
                            boxShadow: 'var(--shadow-sm)',
                        }}
                        onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-primary-hover)'; }}
                        onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-primary)'; }}
                    >
                        <Plus size={18} />
                        Novo Pedido
                    </button>
                </div>
            </header>

            {/* ── Action Carousel & Stats ── */}
            {summary && (
                <ActionCarouselWidget summary={summary} onRowClick={handleRowClick} />
            )}

            {/* ── Explorer Section ── */}
            <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {/* Section header + filters */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', flexWrap: 'wrap', gap: '16px' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <div>
                            <h2 style={{
                                fontSize: '1.1rem',
                                fontWeight: 800,
                                color: 'var(--color-primary)',
                                margin: 0,
                            }}>Explorador de Pedidos</h2>
                            <p style={{
                                fontSize: '0.75rem',
                                color: 'var(--color-text-muted)',
                                margin: '4px 0 0',
                                fontWeight: 500,
                                textTransform: 'none',
                            }}>O restante do portfólio de compras visível para si.</p>
                        </div>

                        {/* Quick Filters */}
                        <div style={{
                            display: 'flex',
                            backgroundColor: 'var(--color-bg-page)',
                            padding: '4px',
                            borderRadius: 'var(--radius-md)',
                            width: 'fit-content',
                            border: '1px solid var(--color-border)',
                        }}>
                            {FILTER_TABS.map(tab => (
                                <button
                                    key={tab.id}
                                    onClick={() => setFilterType(tab.id)}
                                    style={{
                                        padding: '6px 16px',
                                        borderRadius: 'var(--radius-sm)',
                                        fontSize: '0.8rem',
                                        fontWeight: 600,
                                        border: 'none',
                                        cursor: 'pointer',
                                        transition: 'all 0.15s ease',
                                        backgroundColor: filterType === tab.id ? 'var(--color-bg-surface)' : 'transparent',
                                        color: filterType === tab.id ? 'var(--color-primary)' : 'var(--color-text-muted)',
                                        boxShadow: filterType === tab.id ? 'var(--shadow-sm)' : 'none',
                                    }}
                                >
                                    {tab.label}
                                </button>
                            ))}
                        </div>
                    </div>

                    {/* Search + Filter Controls */}
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <div style={{ position: 'relative' }}>
                            <Search
                                size={16}
                                style={{
                                    position: 'absolute',
                                    left: '12px',
                                    top: '50%',
                                    transform: 'translateY(-50%)',
                                    color: 'var(--color-text-muted)',
                                    pointerEvents: 'none',
                                }}
                            />
                            <input
                                type="text"
                                placeholder="Buscar por número ou título..."
                                value={searchTerm}
                                onChange={(e) => setSearchTerm(e.target.value)}
                                style={{
                                    paddingLeft: '36px',
                                    paddingRight: '16px',
                                    paddingTop: '9px',
                                    paddingBottom: '9px',
                                    backgroundColor: 'var(--color-bg-surface)',
                                    border: '1px solid var(--color-border)',
                                    borderRadius: 'var(--radius-md)',
                                    fontSize: '0.85rem',
                                    width: '260px',
                                    boxShadow: 'var(--shadow-sm)',
                                    transition: 'border-color 0.15s ease, box-shadow 0.15s ease',
                                    color: 'var(--color-text-main)',
                                }}
                            />
                        </div>
                        <button style={{
                            padding: '9px',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-md)',
                            color: 'var(--color-text-muted)',
                            cursor: 'pointer',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            boxShadow: 'var(--shadow-sm)',
                            transition: 'background-color 0.15s ease',
                        }}
                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; }}
                        >
                            <Filter size={18} />
                        </button>
                    </div>
                </div>

                {/* Table */}
                <RequestsTableWidget
                    requests={requests}
                    loading={loading}
                    sortConfig={sortConfig}
                    onSort={handleSort}
                    onRowClick={handleRowClick}
                    page={page}
                    pageSize={pageSize}
                    totalCount={totalCount}
                    onPageChange={setPage}
                    onPageSizeChange={setPageSize}
                />
            </section>

            {/* ── Drawer ── */}
            <RequestDrawerPresentation
                requestId={drawerRequestId}
                isOpen={!!drawerRequestId}
                onClose={() => {
                    setDrawerRequestId(null);
                    loadData();
                }}
            />
        </div>
    );
}
