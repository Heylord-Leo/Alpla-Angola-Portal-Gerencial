import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { hrMonthlyChangesApi, ProcessingRunSummaryDto } from '../../../lib/hrMonthlyChangesApi';
import { format } from 'date-fns';
import './MonthlyChangesList.css';

const getStatusLabelPT = (code: string): string => {
    switch (code) {
        case 'DRAFT': return 'Rascunho';
        case 'SYNCING': return 'A Sincronizar';
        case 'NEEDS_REVIEW': return 'Por Rever';
        case 'READY_FOR_EXPORT': return 'Pronto a Exportar';
        case 'EXPORTED': return 'Exportado';
        case 'CLOSED': return 'Fechado';
        case 'FAILED': return 'Falhou';
        default: return code;
    }
};

const MonthlyChangesList: React.FC = () => {
    const navigate = useNavigate();
    const [runs, setRuns] = useState<ProcessingRunSummaryDto[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [isCreating, setIsCreating] = useState(false);
    const [newRunEntityId, setNewRunEntityId] = useState(1);
    const [newRunYear, setNewRunYear] = useState(new Date().getFullYear());
    const [newRunMonth, setNewRunMonth] = useState(new Date().getMonth() + 1);

    const loadRuns = async () => {
        setIsLoading(true);
        setError(null);
        try {
            const data = await hrMonthlyChangesApi.listRuns();
            // Ensure we always set an array, even if the API returns null or an unexpected shape
            setRuns(Array.isArray(data) ? data : []);
        } catch (err: any) {
            console.error('Error loading processing runs:', err);
            setError(err.message || 'Falha ao carregar processamentos.');
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        loadRuns();
    }, []);

    const handleCreateRun = async (e: React.FormEvent) => {
        e.preventDefault();
        setIsCreating(true);
        try {
            const result = await hrMonthlyChangesApi.createRun({
                entityId: newRunEntityId,
                year: newRunYear,
                month: newRunMonth
            });
            await loadRuns();
            // navigate to detail? Or just refresh list.
            navigate(`/hr/monthly-changes/runs/${result.id}`);
        } catch (err: any) {
            alert(`Falha ao criar processamento: ${err?.response?.data?.message || err.message}`);
        } finally {
            setIsCreating(false);
        }
    };

    const handleExecuteRun = async (e: React.MouseEvent, runId: string) => {
        e.stopPropagation();
        try {
            await hrMonthlyChangesApi.executeRun(runId);
            loadRuns();
        } catch (err: any) {
            alert(`Falha ao executar processamento: ${err?.response?.data?.message || err.message}`);
        }
    };

    return (
        <div className="mc-list-container fade-in">
            <header className="mc-list-header">
                <div className="mc-header-title">
                    <h1>Processamento Mensal de RH</h1>
                    <p>Gerir e exportar alterações de assiduidade para o Primavera</p>
                </div>
            </header>

            <div className="mc-create-panel card">
                <h3>Criar Novo Processamento</h3>
                <form onSubmit={handleCreateRun} className="mc-create-form">
                    <div className="mc-form-group">
                        <label>Entidade</label>
                        <select value={newRunEntityId} onChange={e => setNewRunEntityId(Number(e.target.value))}>
                            <option value={1}>AlplaPLASTICO (1)</option>
                            <option value={6}>AlplaSOPRO (6)</option>
                        </select>
                    </div>
                    <div className="mc-form-group">
                        <label>Ano</label>
                        <input type="number" value={newRunYear} onChange={e => setNewRunYear(Number(e.target.value))} min={2020} max={2030} />
                    </div>
                    <div className="mc-form-group">
                        <label>Mês</label>
                        <input type="number" value={newRunMonth} onChange={e => setNewRunMonth(Number(e.target.value))} min={1} max={12} />
                    </div>
                    <div className="mc-form-actions">
                        <button type="submit" className="btn-primary" disabled={isCreating}>
                            {isCreating ? 'A criar...' : 'Criar Processamento'}
                        </button>
                    </div>
                </form>
            </div>

            <div className="mc-list-content card">
                <h3>Processamentos Recentes</h3>
                
                {isLoading ? (
                    <div className="mc-loading">A carregar processamentos...</div>
                ) : error ? (
                    <div className="mc-empty-state" style={{ color: 'var(--status-danger)' }}>{error}</div>
                ) : !runs || runs.length === 0 ? (
                    <div className="mc-empty-state">Nenhum processamento encontrado. Crie um para começar.</div>
                ) : (
                    <table className="mc-table">
                        <thead>
                            <tr>
                                <th>Entidade</th>
                                <th>Período</th>
                                <th>Estado</th>
                                <th>Linhas Sincronizadas</th>
                                <th>Ocorrências</th>
                                <th>Anomalias</th>
                                <th>Criado a</th>
                                <th>Ações</th>
                            </tr>
                        </thead>
                        <tbody>
                            {runs.map(run => (
                                <tr key={run.id} onClick={() => navigate(`/hr/monthly-changes/runs/${run.id}`)} className="mc-clickable-row">
                                    <td>{run.entityName}</td>
                                    <td>{run.month}/{run.year}</td>
                                    <td>
                                        <span className={`badge ${
                                            run.statusCode === 'READY_FOR_EXPORT' || run.statusCode === 'EXPORTED' || run.statusCode === 'CLOSED' ? 'badge-success' :
                                            run.statusCode === 'SYNCING' || run.statusCode === 'NEEDS_REVIEW' ? 'badge-warning' :
                                            run.statusCode === 'FAILED' ? 'badge-danger' : 'badge-neutral'
                                        }`}>
                                            {getStatusLabelPT(run.statusCode)}
                                        </span>
                                    </td>
                                    <td>{run.syncedRowCount}</td>
                                    <td>{run.occurrenceCount}</td>
                                    <td>
                                        {run.anomalyCount > 0 ? (
                                            <span className="badge badge-danger badge-sm">{run.anomalyCount}</span>
                                        ) : '0'}
                                    </td>
                                    <td>{format(new Date(run.createdAtUtc), 'yyyy-MM-dd HH:mm')}</td>
                                    <td>
                                        <div className="mc-row-actions">
                                            {run.statusCode === 'DRAFT' && (
                                                <button 
                                                    className="btn-secondary"
                                                    onClick={(e) => handleExecuteRun(e, run.id)}
                                                >
                                                    Executar
                                                </button>
                                            )}
                                            <button className="btn-secondary">Ver</button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
        </div>
    );
};

export default MonthlyChangesList;
