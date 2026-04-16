import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Search, Save, Edit2, X, RefreshCw, AlertTriangle, Building, Users } from 'lucide-react';
import { ModernTooltip } from '../../components/ui/ModernTooltip';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { HRActionModal, HRActionType } from '../../components/modals/HRActionModal';

import { DepartmentMasterAutocomplete } from '../../components/DepartmentMasterAutocomplete';

export default function HREmployeeDirectory() {
    const [employees, setEmployees] = useState<any[]>([]);
    const [loading, setLoading] = useState(false);
    
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const pageSize = 20;

    // Master Data for mapping
    const [plants, setPlants] = useState<any[]>([]);
    const [managers, setManagers] = useState<any[]>([]);
    
    const [editingId, setEditingId] = useState<string | null>(null);
    const [editForm, setEditForm] = useState({
        portalDepartmentId: '' as string | number,
        plantId: '' as string | number,
        managerUserId: '' as string,
        departmentMasterId: '' as string | number | null,
        departmentMasterName: '' as string
    });
    
    const [saving, setSaving] = useState(false);
    const [syncing] = useState(false); // remove setSyncing

    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });
    const [actionModal, setActionModal] = useState<HRActionType>(null);
    const [modalProcessing, setModalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });

    useEffect(() => {
        loadMasterData();
    }, []);

    useEffect(() => {
        loadEmployees();
    }, [page, search]);

    const loadMasterData = async () => {
        try {
            const [plRes, usersRes] = await Promise.all([
                api.lookups.getPlants(undefined, true),
                api.users.list()
            ]);
            setPlants(plRes);
            // Managers could be anyone with AREA_APPROVER or something, 
            // but for safety, we allow assigning any user who might represent a manager.
            // Let's filter active users who have area approver or local manager roles.
            const possibleManagers = usersRes.filter((u: any) => 
                u.isActive && 
                u.roles.some((r: string) => ['AreaApprover', 'LocalManager', 'Admin'].includes(r))
            );
            setManagers(possibleManagers.length > 0 ? possibleManagers : usersRes);
        } catch (error) {
            console.error('Error loading master data', error);
        }
    };

    const loadEmployees = async () => {
        setLoading(true);
        try {
            const res = await api.hrLeave.getEmployees({ page, pageSize, search });
            setEmployees(res.items);
            setTotal(res.total);
        } catch (error) {
            console.error('Error loading employees', error);
        } finally {
            setLoading(false);
        }
    };

    const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setSearch(e.target.value);
        setPage(1);
    };

    const startEditing = (emp: any) => {
        setEditingId(emp.id);
        setEditForm({
            portalDepartmentId: emp.portalDepartmentId || '',
            plantId: emp.plantId || '',
            managerUserId: emp.managerUserId || '',
            departmentMasterId: emp.departmentMasterId || null,
            departmentMasterName: emp.departmentMasterName || ''
        });
    };

    const cancelEditing = () => {
        setEditingId(null);
    };

    const getCompanyForPlant = (plantId: string | number | null | undefined): string => {
        if (!plantId) return '';
        const p = plants.find(plant => plant.id.toString() === plantId.toString());
        if (!p || !p.name) return '';
        const nameUpper = p.name.toUpperCase();
        if (nameUpper.includes('VIANA 1') || nameUpper.includes('VIANA 2')) return 'ALPLAPLASTICOS';
        if (nameUpper.includes('VIANA 3')) return 'ALPLASOPRO';
        return '';
    };

    const handlePlantChange = (newPlantId: string) => {
        const oldCompany = getCompanyForPlant(editForm.plantId);
        const newCompany = getCompanyForPlant(newPlantId);
        
        const updates: any = { plantId: newPlantId };
        
        // Wipe mapping if cross-company boundary
        if (editForm.departmentMasterId && newCompany !== oldCompany) {
            updates.departmentMasterId = null;
            updates.departmentMasterName = '';
            setFeedback({ type: 'info', message: 'Departamento Mestre limpo, pois a nova Planta pertence a outra Empresa.' });
        }
        
        setEditForm({ ...editForm, ...updates });
    };

    const saveMapping = async (id: string) => {
        setSaving(true);
        try {
            const payload = {
                portalDepartmentId: editForm.portalDepartmentId ? Number(editForm.portalDepartmentId) : null,
                plantId: editForm.plantId ? Number(editForm.plantId) : null,
                managerUserId: editForm.managerUserId || null,
                departmentMasterId: editForm.departmentMasterId ? Number(editForm.departmentMasterId) : null
            };
            await api.hrLeave.updateEmployeeMapping(id, payload);
            await loadEmployees();
            setEditingId(null);
            setFeedback({ type: 'success', message: 'Mapeamento atualizado com sucesso.' });
        } catch (error) {
            console.error('Error saving mapping', error);
            setFeedback({ type: 'error', message: 'Falha ao atualizar o mapeamento.' });
        } finally {
            setSaving(false);
        }
    };

    const handleSync = async () => {
        setActionModal('SYNC');
    };

    const handleConfirmAction = async (action: HRActionType) => {
        if (action === 'SYNC') {
            setModalProcessing(true);
            setModalFeedback({ type: 'info', message: null });
            try {
                const data = await api.hrLeave.syncEmployees();
                if (data.status === 'FAILED') throw new Error(data.message || 'Erro de rede ao sincronizar');
                
                let msg = `Criados: ${data.employeesCreated} | Atualizados: ${data.employeesUpdated} | Desativados: ${data.employeesDeactivated}`;
                if (data.status === 'PARTIAL') {
                    msg = `Alguns erros ocorreram. ${msg}. ${data.message}`;
                }
                setFeedback({ type: 'success', message: msg });
                loadEmployees();
                setActionModal(null);
            } catch (error: any) {
                console.error('Sync error', error);
                setModalFeedback({ type: 'error', message: error.message || 'Erro durante a sincronização com o Innux. Verifique se o módulo está ativo e configurado corretamente.' });
            } finally {
                setModalProcessing(false);
            }
        } else if (action === 'SYNC_DEPARTMENTS') {
            setModalProcessing(true);
            setModalFeedback({ type: 'info', message: null });
            try {
                const data = await api.hrLeave.syncDepartments();
                
                let msg = `Criados: ${data.created} | Atualizados: ${data.updated} | Total Processados: ${data.processed}`;
                
                if (data.errors && data.errors.length > 0) {
                    msg = `Sincronização concluída com erros. ${msg}. Erros em: ${data.errors.length} database(s).`;
                    setFeedback({ type: 'warning', message: msg });
                } else if (data.processed === 0) {
                    msg = `Nenhum departamento encontrado nas bases configuradas.`;
                    setFeedback({ type: 'warning', message: msg });
                } else {
                    setFeedback({ type: 'success', message: `${data.message} ${msg}` });
                }
                
                // reload current employees and maybe masters
                loadEmployees();
                loadMasterData();
                setActionModal(null);
            } catch (error: any) {
                console.error('Dept Sync error', error);
                setModalFeedback({ type: 'error', message: error.message || 'Erro durante a sincronização de departamentos com o Primavera.' });
            } finally {
                setModalProcessing(false);
            }
        }
    };

    return (
        <div style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <Feedback 
                type={feedback.type} 
                message={feedback.message} 
                onClose={() => setFeedback({ type: 'info', message: null })} 
            />
            
            <HRActionModal
                show={actionModal !== null}
                action={actionModal}
                onClose={() => {
                    setActionModal(null);
                    setModalFeedback({ type: 'info', message: null });
                }}
                onConfirm={handleConfirmAction}
                processing={modalProcessing}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback({ type: 'info', message: null })}
            />
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 800, color: '#0f172a' }}>Directório de Funcionários</h2>
                    <p style={{ margin: '4px 0 0 0', color: '#64748b', fontWeight: 500 }}>
                        Mapeamento da hierarquia entre Innux, Plantas e Departamentos Portal.
                    </p>
                </div>
                
                <div style={{ display: 'flex', gap: '16px' }}>
                    <div style={{ position: 'relative', width: '300px' }}>
                        <Search size={16} color="#94a3b8" style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)' }} />
                        <input
                            type="text"
                            placeholder="Pesquisar por nome ou número..."
                            value={search}
                            onChange={handleSearchChange}
                            style={{ 
                                width: '100%', padding: '10px 12px 10px 36px', 
                                borderRadius: '8px', border: '1px solid #cbd5e1',
                                fontSize: '14px', outline: 'none'
                            }}
                        />
                    </div>
                    <button 
                        onClick={() => setActionModal('SYNC_DEPARTMENTS' as unknown as HRActionType)}
                        disabled={syncing}
                        className="btn-secondary"
                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 16px', borderRadius: '8px' }}
                    >
                        <RefreshCw size={16} className={syncing ? 'animate-spin' : ''} />
                        Sincronizar Departamentos
                    </button>
                    <button 
                        onClick={handleSync}
                        disabled={syncing}
                        className="btn-primary"
                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 16px', borderRadius: '8px' }}
                    >
                        <RefreshCw size={16} className={syncing ? 'animate-spin' : ''} />
                        Sincronizar Innux
                    </button>
                </div>
            </div>

            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', minWidth: '800px' }}>
                        <thead>
                            <tr style={{ backgroundColor: '#f8fafc', borderBottom: '2px solid #e2e8f0' }}>
                                <th style={{ padding: '16px', fontSize: '12px', fontWeight: 800, color: '#475569', textTransform: 'uppercase' }}>Funcionário / Rolo</th>
                                <th style={{ padding: '16px', fontSize: '12px', fontWeight: 800, color: '#475569', textTransform: 'uppercase' }}>Planta (Portal)</th>
                                <th style={{ padding: '16px', fontSize: '12px', fontWeight: 800, color: '#475569', textTransform: 'uppercase' }}>Departamento (Mestre)</th>
                                <th style={{ padding: '16px', fontSize: '12px', fontWeight: 800, color: '#475569', textTransform: 'uppercase' }}>Responsável/Chefe</th>
                                <th style={{ padding: '16px', fontSize: '12px', fontWeight: 800, color: '#475569', textTransform: 'uppercase', textAlign: 'right' }}>Ações</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>
                                        <RefreshCw size={24} className="animate-spin" style={{ margin: '0 auto 12px' }} />
                                        A carregar diretório...
                                    </td>
                                </tr>
                            ) : employees.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>
                                        Nenhum funcionário encontrado. {search ? 'Limpe a pesquisa ou tente outro termo.' : 'Sincronize com o Innux para popular a base.'}
                                    </td>
                                </tr>
                            ) : (
                                employees.map(emp => {
                                    const isEditing = editingId === emp.id;
                                    
                                    return (
                                        <tr key={emp.id} style={{ borderBottom: '1px solid #e2e8f0', backgroundColor: !emp.isMapped ? '#fffbeb' : '#fff' }}>
                                            <td style={{ padding: '16px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                    <span style={{ fontWeight: 700, color: '#0f172a', fontSize: '14px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                        {emp.fullName}
                                                        {!emp.isMapped && (
                                                            <ModernTooltip content="Mapeamento incompleto. Este funcionário não será visível para gerentes.">
                                                                <AlertTriangle size={14} color="#d97706" />
                                                            </ModernTooltip>
                                                        )}
                                                    </span>
                                                    <span style={{ fontSize: '12px', color: '#64748b', display: 'flex', gap: '8px', marginTop: '4px' }}>
                                                        <strong>#{emp.employeeCode}</strong> | Origem: {emp.innuxDepartmentName || '-'}
                                                    </span>
                                                </div>
                                            </td>
                                            
                                            <td style={{ padding: '16px', width: '20%' }}>
                                                {isEditing ? (
                                                    <select 
                                                        value={editForm.plantId} 
                                                        onChange={e => handlePlantChange(e.target.value)}
                                                        style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                                    >
                                                        <option value="">Não atribuído</option>
                                                        {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                                    </select>
                                                ) : (
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '13px', color: emp.plantName ? '#0f172a' : '#94a3b8' }}>
                                                        <Building size={14} />
                                                        {emp.plantName || 'Não atribuído'}
                                                    </span>
                                                )}
                                            </td>

                                            <td style={{ padding: '16px', width: '30%' }}>
                                                {isEditing ? (
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                        <DepartmentMasterAutocomplete
                                                            initialId={editForm.departmentMasterId as number | null}
                                                            initialName={editForm.departmentMasterName}
                                                            suggestedMatchText={emp.innuxDepartmentName || ''}
                                                            allowedCompanyCode={getCompanyForPlant(editForm.plantId)}
                                                            plantNotSelected={!editForm.plantId}
                                                            onChange={(id, name) => setEditForm(prev => ({ ...prev, departmentMasterId: id, departmentMasterName: name }))}
                                                        />
                                                    </div>
                                                ) : (
                                                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                        <span style={{ fontSize: '13px', fontWeight: 600, color: emp.departmentMasterName ? '#0ea5e9' : '#94a3b8' }}>
                                                            {emp.departmentMasterName || 'Não atribuído'}
                                                        </span>
                                                        {emp.portalDepartmentName && !emp.departmentMasterName && (
                                                            // Fallback to legacy portal department if master is missing
                                                            <span style={{ fontSize: '11px', color: '#f59e0b', marginTop: '2px' }}>
                                                                Legacy: {emp.portalDepartmentName}
                                                            </span>
                                                        )}
                                                    </div>
                                                )}
                                            </td>

                                            <td style={{ padding: '16px', width: '20%' }}>
                                                {isEditing ? (
                                                    <select 
                                                        value={editForm.managerUserId} 
                                                        onChange={e => setEditForm({...editForm, managerUserId: e.target.value})}
                                                        style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                                    >
                                                        <option value="">Sem responsável</option>
                                                        {managers.map(u => <option key={u.id} value={u.id}>{u.fullName}</option>)}
                                                    </select>
                                                ) : (
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '13px', color: emp.managerName ? '#0f172a' : '#94a3b8' }}>
                                                        <Users size={14} />
                                                        {emp.managerName || 'Sem responsável'}
                                                    </span>
                                                )}
                                            </td>

                                            <td style={{ padding: '16px', textAlign: 'right' }}>
                                                {isEditing ? (
                                                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                                        <button 
                                                            onClick={() => cancelEditing()}
                                                            disabled={saving}
                                                            style={{ padding: '6px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: '#fff', cursor: 'pointer' }}
                                                        >
                                                            <X size={16} color="#64748b" />
                                                        </button>
                                                        <button 
                                                            onClick={() => saveMapping(emp.id)}
                                                            disabled={saving}
                                                            style={{ padding: '6px 12px', borderRadius: '6px', border: 'none', backgroundColor: '#0ea5e9', color: '#fff', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 600 }}
                                                        >
                                                            {saving ? <RefreshCw size={14} className="animate-spin" /> : <Save size={14} />}
                                                            Salvar
                                                        </button>
                                                    </div>
                                                ) : (
                                                    <button 
                                                        onClick={() => startEditing(emp)}
                                                        style={{ padding: '6px 12px', borderRadius: '6px', backgroundColor: 'transparent', border: '1px solid #e2e8f0', cursor: 'pointer', color: '#475569', display: 'inline-flex', alignItems: 'center', gap: '6px', fontSize: '13px', fontWeight: 600 }}
                                                        onMouseOver={e => e.currentTarget.style.backgroundColor = '#f8fafc'}
                                                        onMouseOut={e => e.currentTarget.style.backgroundColor = 'transparent'}
                                                    >
                                                        <Edit2 size={14} />
                                                        Mapear
                                                    </button>
                                                )}
                                            </td>
                                        </tr>
                                    );
                                })
                            )}
                        </tbody>
                    </table>
                </div>
                
                {/* Pagination */}
                {total > pageSize && (
                    <div style={{ padding: '16px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#f8fafc' }}>
                        <span style={{ fontSize: '13px', color: '#64748b' }}>
                            Mostrando {(page - 1) * pageSize + 1} a {Math.min(page * pageSize, total)} de {total}
                        </span>
                        <div style={{ display: 'flex', gap: '8px' }}>
                            <button 
                                disabled={page === 1}
                                onClick={() => setPage(p => p - 1)}
                                style={{ padding: '6px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: page === 1 ? '#e2e8f0' : '#fff', cursor: page === 1 ? 'not-allowed' : 'pointer', fontSize: '13px', fontWeight: 600, color: '#475569' }}
                            >
                                Anterior
                            </button>
                            <button 
                                disabled={page * pageSize >= total}
                                onClick={() => setPage(p => p + 1)}
                                style={{ padding: '6px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: page * pageSize >= total ? '#e2e8f0' : '#fff', cursor: page * pageSize >= total ? 'not-allowed' : 'pointer', fontSize: '13px', fontWeight: 600, color: '#475569' }}
                            >
                                Seguinte
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
