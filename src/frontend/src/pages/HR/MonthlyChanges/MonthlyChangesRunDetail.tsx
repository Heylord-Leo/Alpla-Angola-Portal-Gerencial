import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { 
    hrMonthlyChangesApi, 
    ProcessingRunDetailDto, 
    MonthlyChangeItemDto, 
    AnomalyItemDto, 
    ProcessingLogDto 
} from '../../../lib/hrMonthlyChangesApi';
import { format } from 'date-fns';
import './MonthlyChangesRunDetail.css';

const getStatusLabelPT = (code: string): string => {
    switch (code) {
        case 'DRAFT': return 'Rascunho';
        case 'SYNCING': return 'A Sincronizar';
        case 'NEEDS_REVIEW': return 'Por Rever';
        case 'READY_FOR_EXPORT': return 'Pronto a Exportar';
        case 'EXPORTED': return 'Exportado';
        case 'CLOSED': return 'Fechado';
        case 'FAILED': return 'Falhou';
        case 'AUTO_CODED': return 'Sugestão Automática';
        case 'APPROVED': return 'Aprovado';
        case 'EXCLUDED': return 'Excluído';
        default: return code;
    }
};

const getTypeLabelPT = (type: string): string => {
    switch (type) {
        case 'UNJUSTIFIED_ABSENCE': return 'Falta Injustificada';
        case 'JUSTIFIED_ABSENCE': return 'Falta Justificada';
        case 'LATENESS': return 'Atraso';
        case 'ANOMALY': return 'Anomalia';
        default: return type;
    }
};

const MonthlyChangesRunDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    
    const [run, setRun] = useState<ProcessingRunDetailDto | null>(null);
    const [items, setItems] = useState<MonthlyChangeItemDto[]>([]);
    const [anomalies, setAnomalies] = useState<AnomalyItemDto[]>([]);
    const [logs, setLogs] = useState<ProcessingLogDto[]>([]);
    
    const [activeTab, setActiveTab] = useState<'items' | 'anomalies' | 'logs'>('items');
    const [isLoading, setIsLoading] = useState(true);
    const [isExecuting, setIsExecuting] = useState(false);

    // Modals state
    const [excludeModalItem, setExcludeModalItem] = useState<MonthlyChangeItemDto | null>(null);
    const [excludeReason, setExcludeReason] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    const [resolveModalItem, setResolveModalItem] = useState<{ id: string, name: string, type: string } | null>(null);
    const [resolveNote, setResolveNote] = useState('');
    const [resolveAction, setResolveAction] = useState<'APPROVE' | 'EXCLUDE'>('APPROVE');

    // Filters for items
    const [statusFilter, setStatusFilter] = useState<string>('ALL');
    const [typeFilter, setTypeFilter] = useState<string>('ALL');

    const loadData = async () => {
        if (!id) return;
        setIsLoading(true);
        try {
            const [runData, itemsData, anomaliesData, logsData] = await Promise.all([
                hrMonthlyChangesApi.getRunDetail(id),
                hrMonthlyChangesApi.listItems(id),
                hrMonthlyChangesApi.listAnomalies(id),
                hrMonthlyChangesApi.listLogs(id)
            ]);
            setRun(runData);
            setItems(Array.isArray(itemsData) ? itemsData : []);
            setAnomalies(Array.isArray(anomaliesData) ? anomaliesData : []);
            setLogs(Array.isArray(logsData) ? logsData : []);
        } catch (err) {
            console.error('Error loading run detail:', err);
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, [id]);

    const handleExecuteRun = async () => {
        if (!id) return;
        setIsExecuting(true);
        try {
            await hrMonthlyChangesApi.executeRun(id);
            await loadData();
        } catch (err: any) {
            alert(`Falha ao executar processamento: ${err?.response?.data?.message || err.message}`);
        } finally {
            setIsExecuting(false);
        }
    };

    const handleApprove = async (itemId: string) => {
        if (!id) return;
        try {
            await hrMonthlyChangesApi.approveItem(id, itemId);
            await loadData();
        } catch (err: any) {
            alert(`Falha ao aprovar item: ${err?.response?.data?.message || err.message}`);
        }
    };

    const handleReinclude = async (itemId: string) => {
        if (!id) return;
        try {
            await hrMonthlyChangesApi.reincludeItem(id, itemId);
            await loadData();
        } catch (err: any) {
            alert(`Falha ao re-incluir item: ${err?.response?.data?.message || err.message}`);
        }
    };

    const submitExclude = async () => {
        if (!id || !excludeModalItem) return;
        if (!excludeReason.trim()) {
            alert("O motivo da exclusão é obrigatório.");
            return;
        }
        setIsSubmitting(true);
        try {
            await hrMonthlyChangesApi.excludeItem(id, excludeModalItem.id, { reason: excludeReason });
            setExcludeModalItem(null);
            setExcludeReason('');
            await loadData();
        } catch (err: any) {
            alert(`Falha ao excluir item: ${err?.response?.data?.message || err.message}`);
        } finally {
            setIsSubmitting(false);
        }
    };

    const submitResolve = async () => {
        if (!id || !resolveModalItem) return;
        if (!resolveNote.trim()) {
            alert("A nota de resolução é obrigatória.");
            return;
        }
        setIsSubmitting(true);
        try {
            await hrMonthlyChangesApi.resolveAnomaly(id, resolveModalItem.id, {
                action: resolveAction,
                resolutionNote: resolveNote
            });
            setResolveModalItem(null);
            setResolveNote('');
            setResolveAction('APPROVE');
            await loadData();
        } catch (err: any) {
            alert(`Falha ao resolver anomalia: ${err?.response?.data?.message || err.message}`);
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleReadyForExport = async () => {
        if (!id) return;
        setIsExecuting(true);
        try {
            await hrMonthlyChangesApi.markReadyForExport(id);
            await loadData();
        } catch (err: any) {
            alert(`Falha ao marcar como pronto a exportar: ${err?.response?.data?.message || err.message}`);
        } finally {
            setIsExecuting(false);
        }
    };

    const handleRevertToReview = async () => {
        if (!id) return;
        setIsExecuting(true);
        try {
            await hrMonthlyChangesApi.revertToReview(id);
            await loadData();
        } catch (err: any) {
            alert(`Falha ao reverter para revisão: ${err?.response?.data?.message || err.message}`);
        } finally {
            setIsExecuting(false);
        }
    };

    const filteredItems = items.filter(item => {
        if (statusFilter !== 'ALL' && item.statusCode !== statusFilter) return false;
        if (typeFilter !== 'ALL' && item.occurrenceType !== typeFilter) return false;
        return true;
    });

    if (isLoading) return <div className="mc-loading">A carregar detalhes do processamento...</div>;
    if (!run) return <div className="mc-error">Processamento não encontrado.</div>;

    return (
        <div className="mc-detail-container fade-in">
            <header className="mc-detail-header card">
                <div className="mc-header-top">
                    <div className="mc-title-group">
                        <button className="btn-secondary" onClick={() => navigate('/hr/monthly-changes')}>
                            &larr; Voltar
                        </button>
                        <div>
                            <h1>{run.entityName} - {run.month}/{run.year}</h1>
                            <p className="mc-run-meta">
                                Criado a {format(new Date(run.createdAtUtc), 'yyyy-MM-dd HH:mm')} por {run.createdByEmail || 'Sistema'}
                            </p>
                        </div>
                    </div>
                    <div className="mc-header-actions">
                        <span className={`badge ${
                            run.statusCode === 'READY_FOR_EXPORT' || run.statusCode === 'EXPORTED' || run.statusCode === 'CLOSED' ? 'badge-success' :
                            run.statusCode === 'SYNCING' || run.statusCode === 'NEEDS_REVIEW' ? 'badge-warning' :
                            run.statusCode === 'FAILED' ? 'badge-danger' : 'badge-neutral'
                        }`}>{getStatusLabelPT(run.statusCode)}</span>
                        {run.statusCode === 'DRAFT' && (
                            <button className="btn-primary" onClick={handleExecuteRun} disabled={isExecuting}>
                                {isExecuting ? 'A executar...' : 'Executar Processamento'}
                            </button>
                        )}
                        {run.statusCode === 'NEEDS_REVIEW' && run.unresolvedCount === 0 && (
                            <button className="btn-primary" onClick={handleReadyForExport} disabled={isExecuting} title="Todos os itens resolvidos. Marcar como pronto a exportar.">
                                {isExecuting ? 'A processar...' : 'Pronto a Exportar'}
                            </button>
                        )}
                        {run.statusCode === 'READY_FOR_EXPORT' && (
                            <button className="btn-secondary" onClick={handleRevertToReview} disabled={isExecuting}>
                                {isExecuting ? 'A processar...' : 'Reverter para Revisão'}
                            </button>
                        )}
                        <button className="btn-secondary" onClick={loadData}>Atualizar</button>
                    </div>
                </div>

                <div className="mc-summary-stats">
                    <div className="mc-stat-box">
                        <span className="mc-stat-value">{run.syncedRowCount}</span>
                        <span className="mc-stat-label">Linhas Sincronizadas</span>
                    </div>
                    <div className="mc-stat-box">
                        <span className="mc-stat-value">{run.occurrenceCount}</span>
                        <span className="mc-stat-label">Ocorrências</span>
                    </div>
                    <div className="mc-stat-box">
                        <span className="mc-stat-value">{run.autoCodedCount}</span>
                        <span className="mc-stat-label">Sugestões Automáticas</span>
                    </div>
                    <div className="mc-stat-box">
                        <span className="mc-stat-value">{run.needsReviewCount}</span>
                        <span className="mc-stat-label">Por Rever</span>
                    </div>
                    <div className="mc-stat-box mc-stat-alert">
                        <span className="mc-stat-value">{run.anomalyCount}</span>
                        <span className="mc-stat-label">Anomalias</span>
                    </div>
                </div>
            </header>

            <div className="mc-detail-content card">
                <div className="mc-tabs">
                    <button 
                        className={`mc-tab ${activeTab === 'items' ? 'active' : ''}`}
                        onClick={() => setActiveTab('items')}
                    >
                        Revisão de Itens ({items.length})
                    </button>
                    <button 
                        className={`mc-tab ${activeTab === 'anomalies' ? 'active' : ''}`}
                        onClick={() => setActiveTab('anomalies')}
                    >
                        Anomalias {anomalies.length > 0 && <span className="mc-tab-badge">{anomalies.length}</span>}
                    </button>
                    <button 
                        className={`mc-tab ${activeTab === 'logs' ? 'active' : ''}`}
                        onClick={() => setActiveTab('logs')}
                    >
                        Logs
                    </button>
                </div>

                <div className="mc-tab-content">
                    {activeTab === 'items' && (
                        <div className="mc-items-view">
                            <div className="mc-filters">
                                <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
                                    <option value="ALL">Todos os Estados</option>
                                    <option value="AUTO_CODED">Sugestão Automática</option>
                                    <option value="NEEDS_REVIEW">Por Rever</option>
                                    <option value="APPROVED">Aprovado</option>
                                </select>
                                <select value={typeFilter} onChange={e => setTypeFilter(e.target.value)}>
                                    <option value="ALL">Todos os Tipos</option>
                                    <option value="UNJUSTIFIED_ABSENCE">Falta Injustificada</option>
                                    <option value="JUSTIFIED_ABSENCE">Falta Justificada</option>
                                    <option value="LATENESS">Atraso</option>
                                    <option value="ANOMALY">Anomalia</option>
                                </select>
                            </div>
                            
                            {filteredItems.length === 0 ? (
                                <div className="mc-empty-state">Nenhum item corresponde aos filtros selecionados.</div>
                            ) : (
                                <div className="mc-table-container">
                                    <table className="mc-table mc-items-table">
                                        <thead>
                                            <tr>
                                                <th>Data</th>
                                                <th>Colaborador</th>
                                                <th>Tipo / Regra</th>
                                                <th>Duração</th>
                                                <th>Estado</th>
                                                <th>Código Primavera</th>
                                                <th>Ações</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {filteredItems.map(item => (
                                                <tr key={item.id} className={item.isAnomaly ? 'mc-row-anomaly' : ''}>
                                                    <td>{format(new Date(item.date), 'yyyy-MM-dd')}</td>
                                                    <td>
                                                        <div className="mc-emp-info">
                                                            <span className="mc-emp-name">{item.employeeName}</span>
                                                            <span className="mc-emp-code">{item.employeeCode}</span>
                                                        </div>
                                                    </td>
                                                    <td>
                                                        <div className="mc-type-info">
                                                            <span className="mc-type">{getTypeLabelPT(item.occurrenceType)}</span>
                                                            {item.isAnomaly && <span className="mc-flag-anomaly">⚠ Anomalia</span>}
                                                            <span className="mc-rule" title={item.detectionRule}>{item.detectionRule.split(':')[0]}</span>
                                                        </div>
                                                    </td>
                                                    <td>{item.durationMinutes} min</td>
                                                    <td>
                                                        <span className={`badge ${
                                                            item.statusCode === 'READY_FOR_EXPORT' || item.statusCode === 'EXPORTED' || item.statusCode === 'APPROVED' ? 'badge-success' :
                                                            item.statusCode === 'NEEDS_REVIEW' ? 'badge-warning' :
                                                            item.statusCode === 'EXCLUDED' ? 'badge-neutral' :
                                                            item.statusCode === 'FAILED' ? 'badge-danger' : 'badge-neutral'
                                                        }`}>
                                                            {getStatusLabelPT(item.statusCode)}
                                                        </span>
                                                    </td>
                                                    <td>
                                                        {item.primaveraCode ? (
                                                            <span className="mc-pv-code" title={item.primaveraCodeDescription || ''}>
                                                                {item.primaveraCode}
                                                            </span>
                                                        ) : '-'}
                                                    </td>
                                                    <td>
                                                        <div className="mc-row-actions">
                                                            {item.statusCode === 'AUTO_CODED' && !item.isAnomaly && (
                                                                <button className="btn-primary" style={{ padding: '4px 10px', fontSize: '0.75rem' }} onClick={() => handleApprove(item.id)}>Aprovar</button>
                                                            )}
                                                            {(item.statusCode === 'AUTO_CODED' || item.statusCode === 'APPROVED' || item.statusCode === 'NEEDS_REVIEW') && !item.isAnomaly && (
                                                                <button className="btn-secondary" style={{ padding: '4px 10px', fontSize: '0.75rem' }} onClick={() => setExcludeModalItem(item)}>Excluir</button>
                                                            )}
                                                            {item.statusCode === 'EXCLUDED' && (
                                                                <button className="btn-secondary" style={{ padding: '4px 10px', fontSize: '0.75rem' }} onClick={() => handleReinclude(item.id)}>Re-incluir</button>
                                                            )}
                                                            {item.isAnomaly && item.statusCode === 'NEEDS_REVIEW' && (
                                                                <button className="btn-primary" style={{ padding: '4px 10px', fontSize: '0.75rem' }} onClick={() => setResolveModalItem({ id: item.id, name: item.employeeName, type: item.occurrenceType })}>Resolver</button>
                                                            )}
                                                        </div>
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            )}
                        </div>
                    )}

                    {activeTab === 'anomalies' && (
                        <div className="mc-anomalies-view">
                            {anomalies.length === 0 ? (
                                <div className="mc-empty-state">Nenhuma anomalia encontrada neste processamento.</div>
                            ) : (
                                <div className="mc-table-container">
                                    <table className="mc-table mc-anomalies-table">
                                        <thead>
                                            <tr>
                                                <th>Data</th>
                                                <th>Colaborador</th>
                                                <th>Tipo</th>
                                                <th>Motivo da Anomalia</th>
                                                <th>Estado</th>
                                                <th>Ações</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {anomalies.map(anomaly => (
                                                <tr key={anomaly.id}>
                                                    <td>{format(new Date(anomaly.date), 'yyyy-MM-dd')}</td>
                                                    <td>
                                                        <div className="mc-emp-info">
                                                            <span className="mc-emp-name">{anomaly.employeeName}</span>
                                                            <span className="mc-emp-code">{anomaly.employeeCode}</span>
                                                        </div>
                                                    </td>
                                                    <td>{getTypeLabelPT(anomaly.occurrenceType)}</td>
                                                    <td className="mc-text-danger">{anomaly.anomalyReason}</td>
                                                    <td>
                                                        <span className={`badge ${
                                                            anomaly.statusCode === 'APPROVED' ? 'badge-success' :
                                                            anomaly.statusCode === 'NEEDS_REVIEW' ? 'badge-warning' :
                                                            anomaly.statusCode === 'EXCLUDED' ? 'badge-neutral' : 'badge-neutral'
                                                        }`}>
                                                            {getStatusLabelPT(anomaly.statusCode)}
                                                        </span>
                                                    </td>
                                                    <td>
                                                        {anomaly.statusCode === 'NEEDS_REVIEW' && (
                                                            <button className="btn-primary" style={{ padding: '4px 10px', fontSize: '0.75rem' }} onClick={() => setResolveModalItem({ id: anomaly.id, name: anomaly.employeeName, type: anomaly.occurrenceType })}>Resolver</button>
                                                        )}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            )}
                        </div>
                    )}

                    {activeTab === 'logs' && (
                        <div className="mc-logs-view">
                            {logs.length === 0 ? (
                                <div className="mc-empty-state">Nenhum log disponível.</div>
                            ) : (
                                <div className="mc-logs-container">
                                    {logs.map((log, index) => (
                                        <div key={index} className="mc-log-entry">
                                            <span className="mc-log-time">{format(new Date(log.occurredAtUtc), 'HH:mm:ss.SSS')}</span>
                                            <span className="mc-log-type">[{log.eventType}]</span>
                                            <span className="mc-log-msg">{log.message}</span>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    )}
                </div>
            </div>

            {/* Exclude Modal */}
            {excludeModalItem && (
                <div className="mc-modal-overlay">
                    <div className="mc-modal-content card">
                        <h2>Excluir Item</h2>
                        <p>Está a excluir <strong>{getTypeLabelPT(excludeModalItem.occurrenceType)}</strong> de <strong>{excludeModalItem.employeeName}</strong>.</p>
                        <div className="mc-form-group">
                            <label>Motivo da Exclusão</label>
                            <textarea 
                                value={excludeReason} 
                                onChange={e => setExcludeReason(e.target.value)} 
                                rows={3}
                                placeholder="Porque é que este item deve ser excluído da exportação?"
                                className="mc-form-control"
                            ></textarea>
                        </div>
                        <div className="mc-modal-actions">
                            <button className="btn-secondary" onClick={() => { setExcludeModalItem(null); setExcludeReason(''); }} disabled={isSubmitting}>Cancelar</button>
                            <button className="btn-primary" style={{ backgroundColor: 'var(--color-status-red)', borderColor: 'var(--color-status-red)' }} onClick={submitExclude} disabled={isSubmitting || !excludeReason.trim()}>
                                {isSubmitting ? 'A processar...' : 'Excluir Item'}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* Resolve Anomaly Modal */}
            {resolveModalItem && (
                <div className="mc-modal-overlay">
                    <div className="mc-modal-content card">
                        <h2>Resolver Anomalia</h2>
                        <p>A resolver anomalia para <strong>{resolveModalItem.name}</strong> ({getTypeLabelPT(resolveModalItem.type)}).</p>
                        <div className="mc-form-group">
                            <label>Ação de Resolução</label>
                            <select 
                                value={resolveAction} 
                                onChange={e => setResolveAction(e.target.value as 'APPROVE' | 'EXCLUDE')}
                                className="mc-form-control"
                            >
                                <option value="APPROVE">Aprovar (será exportado)</option>
                                <option value="EXCLUDE">Excluir (não será exportado)</option>
                            </select>
                        </div>
                        <div className="mc-form-group">
                            <label>Nota de Resolução (Obrigatório para Auditoria)</label>
                            <textarea 
                                value={resolveNote} 
                                onChange={e => setResolveNote(e.target.value)} 
                                rows={3}
                                placeholder="Explique como esta anomalia foi resolvida..."
                                className="mc-form-control"
                            ></textarea>
                        </div>
                        <div className="mc-modal-actions">
                            <button className="btn-secondary" onClick={() => { setResolveModalItem(null); setResolveNote(''); setResolveAction('APPROVE'); }} disabled={isSubmitting}>Cancelar</button>
                            <button className="btn-primary" onClick={submitResolve} disabled={isSubmitting || !resolveNote.trim()}>
                                {isSubmitting ? 'A processar...' : 'Confirmar Resolução'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default MonthlyChangesRunDetail;
