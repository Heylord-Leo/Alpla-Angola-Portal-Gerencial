import { useState, useEffect, useCallback, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    History, Search, Printer, X, Loader2,
    AlertTriangle, ChevronLeft, ChevronRight, User, Trash2
} from 'lucide-react';
import { BadgePreview, BadgeData } from './BadgePreview';
import { apiFetch, API_BASE_URL } from '../../lib/api';
import './badge-print-history.css';

// ─── Types ───

interface PrintHistoryItem {
    id: string;
    employeeCode: string;
    employeeName: string;
    department?: string;
    companyCode?: string;
    plantCode?: string;
    photoSource?: string;
    printedAtUtc: string;
    printedByName?: string;
    printCount: number;
    layoutName?: string;
}

interface PrintSnapshot {
    snapshotPayloadJson: string;
    photoSource: string;
    photoReference?: string;
    layoutConfigJson?: string;
}

const PHOTO_SOURCE_LABELS: Record<string, string> = {
    INNUX: 'Innux',
    MANUAL_UPLOAD: 'Upload Manual',
    NONE: 'Sem Foto',
};

const COMPANY_OPTIONS = [
    { value: '', label: 'Todas as Empresas' },
    { value: 'ALPLAPLASTICO', label: 'Alpla Plástico' },
    { value: 'ALPLASOPRO', label: 'Alpla Sopro' },
];

/**
 * BadgePrintHistoryPage — History tracking and reprint screen.
 *
 * Features:
 * - Paginated, filterable print history table
 * - Snapshot-based reprint without re-querying Primavera/Innux
 * - Graceful photo degradation (placeholder if source unavailable)
 * - Reprint audit trail (creates BadgePrintEvent on each reprint)
 */
export default function BadgePrintHistoryPage() {
    // List state
    const [history, setHistory] = useState<PrintHistoryItem[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [page, setPage] = useState(1);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const PAGE_SIZE = 15;

    // Filters
    const [filterEmployee, setFilterEmployee] = useState('');
    const [filterCompany, setFilterCompany] = useState('');
    const searchTimeout = useRef<ReturnType<typeof setTimeout>>();

    // Reprint modal
    const [reprintItem, setReprintItem] = useState<PrintHistoryItem | null>(null);
    const [reprintSnapshot, setReprintSnapshot] = useState<PrintSnapshot | null>(null);
    const [isLoadingSnapshot, setIsLoadingSnapshot] = useState(false);
    const [isReprinting, setIsReprinting] = useState(false);
    const [reprintReason, setReprintReason] = useState('');

    // ─── Load History ───
    const loadHistory = useCallback(async (p: number = page) => {
        setIsLoading(true);
        setError(null);
        try {
            const params = new URLSearchParams({
                page: p.toString(),
                pageSize: PAGE_SIZE.toString(),
            });
            if (filterEmployee.trim()) params.set('employeeCode', filterEmployee.trim());
            if (filterCompany) params.set('companyCode', filterCompany);

            const res = await apiFetch(`${API_BASE_URL}/api/badges/history?${params}`);
            const data = await res.json();
            
            // Normalize items to array to prevent 'length' property crashes
            setHistory(data?.items || []);
            setTotalCount(data?.totalCount || 0);
            setPage(data?.page || 1);
        } catch (err) {
            setError('Erro ao carregar histórico de impressão.');
            console.error('Load history error:', err);
        } finally {
            setIsLoading(false);
        }
    }, [filterEmployee, filterCompany, page]);

    useEffect(() => { loadHistory(1); }, [filterCompany]); // Reset to page 1 on filter change

    // Debounced employee search
    useEffect(() => {
        if (searchTimeout.current) clearTimeout(searchTimeout.current);
        searchTimeout.current = setTimeout(() => loadHistory(1), 400);
        return () => { if (searchTimeout.current) clearTimeout(searchTimeout.current); };
    }, [filterEmployee]);

    const totalPages = Math.ceil(totalCount / PAGE_SIZE);

    // ─── Reprint Flow ───
    const handleOpenReprint = async (item: PrintHistoryItem) => {
        setReprintItem(item);
        setReprintReason('');
        setIsLoadingSnapshot(true);
        setReprintSnapshot(null);

        try {
            const res = await apiFetch(`${API_BASE_URL}/api/badges/history/${item.id}/snapshot`);
            const snapshot: PrintSnapshot = await res.json();
            setReprintSnapshot(snapshot);
        } catch (err) {
            console.error('Load snapshot error:', err);
        } finally {
            setIsLoadingSnapshot(false);
        }
    };

    const handleCloseReprint = () => {
        setReprintItem(null);
        setReprintSnapshot(null);
    };

    const handleReprint = async () => {
        if (!reprintItem) return;
        setIsReprinting(true);

        try {
            await apiFetch(`${API_BASE_URL}/api/badges/history/${reprintItem.id}/reprint`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ reason: reprintReason.trim() || null }),
            });

            // Trigger print
            window.print();

            handleCloseReprint();
            await loadHistory();
        } catch (err) {
            console.error('Reprint error:', err);
        } finally {
            setIsReprinting(false);
        }
    };

    // Build badge data from snapshot
    const getSnapshotBadgeData = (): BadgeData | null => {
        if (!reprintSnapshot) return null;
        try {
            const payload = JSON.parse(reprintSnapshot.snapshotPayloadJson || '{}');

            // Photo resilience: check if reference is still valid,
            // let BadgePreview handle placeholder rendering
            let photoUrl = payload.photoUrl || reprintSnapshot.photoReference || null;

            return {
                firstName: payload.firstName || '',
                lastName: payload.lastName || '',
                fullName: payload.fullName || `${payload.firstName || ''} ${payload.lastName || ''}`,
                department: payload.department,
                category: payload.category,
                employeeCode: payload.employeeCode || reprintItem?.employeeCode || '',
                cardNumber: payload.cardNumber,
                company: payload.company || reprintItem?.companyCode,
                photoUrl,
            };
        } catch {
            return null;
        }
    };

    const snapshotLayoutConfig = reprintSnapshot?.layoutConfigJson
        ? (() => { try { return JSON.parse(reprintSnapshot.layoutConfigJson); } catch { return undefined; } })()
        : undefined;

    // ─── Delete Flow ───
    const handleDeleteHistory = async (id: string, employeeName: string) => {
        if (!confirm(`Tem a certeza que deseja excluir o histórico de impressão de ${employeeName}? Esta ação é irreversível e se for a única impressão de um Layout, irá destravá-lo para edição.`)) {
            return;
        }
        
        try {
            const response = await apiFetch(`${API_BASE_URL}/api/badges/history/${id}`, {
                method: 'DELETE'
            });

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || 'Erro ao excluir o histórico.');
            }

            // Refresh table
            loadHistory();
        } catch (err: any) {
            console.error('Delete history error:', err);
            alert(err.message || 'Ocorreu um erro ao excluir.');
        }
    };

    return (
        <div className="bph-container">
            {/* Header */}
            <div className="bph-header">
                <div>
                    <h2 className="bph-title">
                        <History size={20} /> Histórico de Impressão
                    </h2>
                    <p className="bph-subtitle">
                        Consulte crachás já impressos e reimprima rapidamente
                    </p>
                </div>
            </div>

            {/* Filters */}
            <div className="bph-filters">
                <div className="bph-search-field">
                    <Search size={15} />
                    <input
                        placeholder="Buscar por código do funcionário..."
                        value={filterEmployee}
                        onChange={e => setFilterEmployee(e.target.value)}
                    />
                </div>
                <select
                    className="bph-filter-select"
                    value={filterCompany}
                    onChange={e => setFilterCompany(e.target.value)}
                >
                    {COMPANY_OPTIONS.map(opt => (
                        <option key={opt.value} value={opt.value}>{opt.label}</option>
                    ))}
                </select>
            </div>

            {/* Error */}
            {error && (
                <div className="bph-error">
                    <AlertTriangle size={16} /> {error}
                </div>
            )}

            {/* Loading */}
            {isLoading && (
                <div className="bph-loading">
                    <Loader2 size={20} className="bph-spinner" /> Carregando histórico...
                </div>
            )}

            {/* Empty state */}
            {!isLoading && (!history || history.length === 0) && !error && (
                <div className="bph-empty">
                    <History size={40} strokeWidth={1} />
                    <p className="bph-empty-title">Nenhuma impressão registada</p>
                    <p className="bph-empty-subtitle">
                        O histórico será preenchido quando crachás forem impressos na área de Funcionários
                    </p>
                </div>
            )}

            {/* History Table */}
            {!isLoading && history && history.length > 0 && (
                <>
                    <div className="bph-table-wrapper">
                        <table className="bph-table">
                            <thead>
                                <tr>
                                    <th>Funcionário</th>
                                    <th>Código</th>
                                    <th>Departamento</th>
                                    <th>Empresa</th>
                                    <th>Foto</th>
                                    <th>Layout</th>
                                    <th>Data</th>
                                    <th>Impresso Por</th>
                                    <th>Nº Imp.</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                {history.map(item => (
                                    <motion.tr
                                        key={item.id}
                                        initial={{ opacity: 0 }}
                                        animate={{ opacity: 1 }}
                                        transition={{ duration: 0.15 }}
                                    >
                                        <td className="bph-cell-name">{item.employeeName}</td>
                                        <td><code className="bph-code">{item.employeeCode}</code></td>
                                        <td>{item.department || '—'}</td>
                                        <td>{item.companyCode || '—'}</td>
                                        <td>
                                            <span className={`bph-photo-badge ${item.photoSource?.toLowerCase() || 'none'}`}>
                                                {PHOTO_SOURCE_LABELS[item.photoSource || 'NONE'] || item.photoSource}
                                            </span>
                                        </td>
                                        <td>{item.layoutName || 'Padrão V1'}</td>
                                        <td className="bph-cell-date">
                                            {new Date(item.printedAtUtc).toLocaleDateString('pt-AO', {
                                                day: '2-digit', month: '2-digit', year: 'numeric',
                                                hour: '2-digit', minute: '2-digit',
                                            })}
                                        </td>
                                        <td>{item.printedByName || '—'}</td>
                                        <td className="bph-cell-count">
                                            <span className={`bph-count ${item.printCount > 1 ? 'reprint' : ''}`}>
                                                {item.printCount}
                                            </span>
                                        </td>
                                        <td>
                                            <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'flex-end' }}>
                                                <button
                                                    className="bph-btn-reprint"
                                                    title="Reimprimir crachá"
                                                    onClick={() => handleOpenReprint(item)}
                                                >
                                                    <Printer size={14} />
                                                    <span>Reimprimir</span>
                                                </button>
                                                <button
                                                    className="bph-btn-reprint"
                                                    style={{ color: '#ef4444', borderColor: '#ef4444', backgroundColor: 'transparent' }}
                                                    title="Excluir Histórico (Teste)"
                                                    onClick={() => handleDeleteHistory(item.id, item.employeeName)}
                                                >
                                                    <Trash2 size={14} />
                                                </button>
                                            </div>
                                        </td>
                                    </motion.tr>
                                ))}
                            </tbody>
                        </table>
                    </div>

                    {/* Pagination */}
                    {totalPages > 1 && (
                        <div className="bph-pagination">
                            <button
                                className="bph-page-btn"
                                disabled={page <= 1}
                                onClick={() => loadHistory(page - 1)}
                            >
                                <ChevronLeft size={16} /> Anterior
                            </button>
                            <span className="bph-page-info">
                                Página {page} de {totalPages} • {totalCount} registos
                            </span>
                            <button
                                className="bph-page-btn"
                                disabled={page >= totalPages}
                                onClick={() => loadHistory(page + 1)}
                            >
                                Próxima <ChevronRight size={16} />
                            </button>
                        </div>
                    )}
                </>
            )}

            {/* ── Reprint Modal ── */}
            <AnimatePresence>
                {reprintItem && (
                    <motion.div
                        className="bph-modal-overlay"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        onClick={handleCloseReprint}
                    >
                        <motion.div
                            className="bph-modal"
                            initial={{ scale: 0.95, opacity: 0 }}
                            animate={{ scale: 1, opacity: 1 }}
                            exit={{ scale: 0.95, opacity: 0 }}
                            transition={{ duration: 0.2 }}
                            onClick={e => e.stopPropagation()}
                        >
                            <div className="bph-modal-header">
                                <h3>Reimprimir Crachá</h3>
                                <button className="bph-btn-close" onClick={handleCloseReprint}>
                                    <X size={18} />
                                </button>
                            </div>

                            <div className="bph-modal-body">
                                <div className="bph-modal-info">
                                    <div className="bph-modal-employee">
                                        <User size={16} />
                                        <strong>{reprintItem.employeeName}</strong>
                                        <code>{reprintItem.employeeCode}</code>
                                    </div>
                                    <p className="bph-modal-meta">
                                        Impressão original: {new Date(reprintItem.printedAtUtc).toLocaleDateString('pt-AO')}
                                        {reprintItem.printedByName && ` por ${reprintItem.printedByName}`}
                                        {' '} • Total de impressões: {reprintItem.printCount}
                                    </p>

                                    {/* Optional reason field (future-ready) */}
                                    <div className="bph-modal-field">
                                        <label>Motivo da Reimpressão (opcional)</label>
                                        <input
                                            value={reprintReason}
                                            onChange={e => setReprintReason(e.target.value)}
                                            placeholder="Ex: Cartão danificado, troca de departamento..."
                                        />
                                    </div>
                                </div>

                                {/* Preview */}
                                <div className="bph-modal-preview">
                                    {isLoadingSnapshot && (
                                        <div className="bph-loading">
                                            <Loader2 size={20} className="bph-spinner" /> Carregando dados...
                                        </div>
                                    )}
                                    {!isLoadingSnapshot && reprintSnapshot && (() => {
                                        const badgeData = getSnapshotBadgeData();
                                        if (!badgeData) return (
                                            <div className="bph-error">
                                                <AlertTriangle size={16} /> Não foi possível recuperar os dados do crachá.
                                            </div>
                                        );
                                        return (
                                            <div className="bph-preview-card print-target">
                                                <BadgePreview
                                                    data={badgeData}
                                                    layoutConfig={snapshotLayoutConfig}
                                                />
                                            </div>
                                        );
                                    })()}
                                </div>
                            </div>

                            <div className="bph-modal-footer">
                                <button className="bph-btn-secondary" onClick={handleCloseReprint}>
                                    Cancelar
                                </button>
                                <button
                                    className="bph-btn-primary"
                                    onClick={handleReprint}
                                    disabled={isReprinting || isLoadingSnapshot || !reprintSnapshot}
                                >
                                    {isReprinting ? (
                                        <><Loader2 size={14} className="bph-spinner" /> Imprimindo...</>
                                    ) : (
                                        <><Printer size={14} /> Reimprimir</>
                                    )}
                                </button>
                            </div>
                        </motion.div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
