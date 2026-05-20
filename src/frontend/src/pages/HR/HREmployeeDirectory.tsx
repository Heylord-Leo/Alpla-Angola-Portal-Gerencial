import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Search, Save, Edit2, X, RefreshCw, AlertTriangle, Building, Users, Filter, Sparkles, CheckCircle2 } from 'lucide-react';
import { ModernTooltip } from '../../components/ui/ModernTooltip';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { HRActionModal, HRActionType } from '../../components/modals/HRActionModal';

import { DepartmentMasterAutocomplete } from '../../components/DepartmentMasterAutocomplete';

// ─── Filter Chip Definitions ───
type FilterChipKey = 'all' | 'mapped' | 'unmapped' | 'noPlant' | 'noDept' | 'noManager' | 'withSuggestion';
interface FilterChipDef { key: FilterChipKey; label: string; icon?: React.ReactNode; color: string; }
const FILTER_CHIPS: FilterChipDef[] = [
    { key: 'all', label: 'Todos', color: '#0f172a' },
    { key: 'mapped', label: 'Mapeados', icon: <CheckCircle2 size={13} />, color: '#16a34a' },
    { key: 'unmapped', label: 'Não Mapeados', icon: <AlertTriangle size={13} />, color: '#d97706' },
    { key: 'noPlant', label: 'Sem Planta', icon: <Building size={13} />, color: '#dc2626' },
    { key: 'noDept', label: 'Sem Departamento', color: '#9333ea' },
    { key: 'noManager', label: 'Sem Responsável', icon: <Users size={13} />, color: '#0284c7' },
    { key: 'withSuggestion', label: 'Com Sugestão', icon: <Sparkles size={13} />, color: '#f59e0b' },
];

export default function HREmployeeDirectory() {
    const [employees, setEmployees] = useState<any[]>([]);
    const [loading, setLoading] = useState(false);
    
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const pageSize = 20;

    // ─── Filters ───
    const [activeChip, setActiveChip] = useState<FilterChipKey>('all');
    const [filterPlantId, setFilterPlantId] = useState<string>('');
    const [filterDepartmentId, setFilterDepartmentId] = useState<string>('');
    const [filterInnuxDept, setFilterInnuxDept] = useState<string>('');
    const [showFilters, setShowFilters] = useState(false);

    // ─── KPI Summary ───
    const [summary, setSummary] = useState<any>(null);

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
    

    const [selectedIds, setSelectedIds] = useState<string[]>([]);
    const [bulkModalVisible, setBulkModalVisible] = useState(false);
    const [bulkStep, setBulkStep] = useState<1 | 2>(1);
    const [bulkSaving, setBulkSaving] = useState(false);
    const [bulkForm, setBulkForm] = useState({
        plantId: '' as string | number,
        departmentMasterId: '' as string | number | null,
        departmentMasterName: '' as string,
        managerUserId: '' as string
    });

    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });
    const [actionModal, setActionModal] = useState<HRActionType>(null);
    const [modalProcessing, setModalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });

    useEffect(() => {
        loadMasterData();
    }, []);

    useEffect(() => {
        setSelectedIds([]);
        loadEmployees();
    }, [page, search, activeChip, filterPlantId, filterDepartmentId, filterInnuxDept]);

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
            const params: Record<string, string | number | boolean> = { page, pageSize };
            if (search) params.search = search;

            // Map chip to backend params
            if (activeChip === 'mapped') params.mappingStatus = 'mapped';
            else if (activeChip === 'unmapped') params.mappingStatus = 'unmapped';
            else if (activeChip === 'noPlant') params.missingField = 'plant';
            else if (activeChip === 'noDept') params.missingField = 'department';
            else if (activeChip === 'noManager') params.missingField = 'manager';
            else if (activeChip === 'withSuggestion') params.hasSuggestion = true;

            if (filterPlantId) params.plantId = filterPlantId;
            if (filterDepartmentId) params.departmentMasterId = filterDepartmentId;
            if (filterInnuxDept) params.innuxDepartment = filterInnuxDept;

            const res = await api.hrLeave.getEmployees(params);
            setEmployees(res.items);
            setTotal(res.total);
            if (res.summary) setSummary(res.summary);
        } catch (error) {
            console.error('Error loading employees', error);
        } finally {
            setLoading(false);
        }
    };

    const handleChipClick = (key: FilterChipKey) => {
        setActiveChip(key);
        setPage(1);
    };

    const clearAllFilters = () => {
        setActiveChip('all');
        setFilterPlantId('');
        setFilterDepartmentId('');
        setFilterInnuxDept('');
        setSearch('');
        setPage(1);
    };

    const hasActiveFilters = activeChip !== 'all' || filterPlantId || filterDepartmentId || filterInnuxDept || search;

    const acceptSuggestion = (emp: any) => {
        // For ALPLASOPRO, pre-fill with Viana 3
        if (emp.suggestedPlantSource === 'PRIMAVERA:ALPLASOPRO') {
            const viana3 = plants.find(p => p.name?.toUpperCase().includes('VIANA 3'));
            startEditing(emp);
            if (viana3) {
                setTimeout(() => {
                    setEditForm(prev => ({ ...prev, plantId: viana3.id }));
                    setFeedback({ type: 'info', message: `Sugestão Viana 3 pré-preenchida. Confirme salvando.` });
                }, 50);
            }
        } else {
            // For ambiguous, just open edit mode
            startEditing(emp);
            setFeedback({ type: 'info', message: 'Sugestão ambígua — selecione a planta manualmente (Viana 1 ou Viana 2).' });
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

    const handleBulkPlantChange = (newPlantId: string) => {
        const oldCompany = getCompanyForPlant(bulkForm.plantId);
        const newCompany = getCompanyForPlant(newPlantId);

        const updates: any = { plantId: newPlantId };
        
        if (bulkForm.departmentMasterId && newCompany !== oldCompany) {
            updates.departmentMasterId = null;
            updates.departmentMasterName = '';
        }
        
        setBulkForm({ ...bulkForm, ...updates });
    };

    const submitBulkMapping = async () => {
        setBulkSaving(true);
        try {
            const payload = {
                employeeIds: selectedIds,
                plantId: bulkForm.plantId ? Number(bulkForm.plantId) : null,
                departmentMasterId: bulkForm.departmentMasterId && bulkForm.departmentMasterId !== 'CLEAR' ? Number(bulkForm.departmentMasterId) : null,
                managerUserId: bulkForm.managerUserId && bulkForm.managerUserId !== 'CLEAR' ? bulkForm.managerUserId : null,
                clearDepartmentMaster: bulkForm.departmentMasterId === 'CLEAR',
                clearManager: bulkForm.managerUserId === 'CLEAR'
            };
            
            const res = await api.hrLeave.updateBulkEmployeeMapping(payload);
            setFeedback({ 
                type: res.errors > 0 ? 'warning' : 'success', 
                message: `Atualização em massa concluída. Processados: ${res.processed} | Atualizados: ${res.updated} | Ignorados: ${res.skipped} | Não encontrados: ${res.notFound} | Erros: ${res.errors}` 
            });
            
            setSelectedIds([]);
            setBulkModalVisible(false);
            await loadEmployees();
        } catch (error: any) {
            console.error('Error in bulk mapping', error);
            setFeedback({ type: 'error', message: error.message || 'Falha ao processar atribuição em massa.' });
        } finally {
            setBulkSaving(false);
        }
    };


    const handleConfirmAction = async (action: HRActionType) => {
        if (action === 'SYNC_ALL' || action === 'SYNC' || action === 'SYNC_DEPARTMENTS') {
            setModalProcessing(true);
            setModalFeedback({ type: 'info', message: null });
            try {
                // 1. Departamentos
                const deptData = await api.hrLeave.syncDepartments();
                
                // 2. Funcionários
                const empData = await api.hrLeave.syncEmployees();

                // 3. Resolve plant suggestions (Primavera read-only)
                let sugMsg = '';
                try {
                    const sugData = await api.hrLeave.resolveSuggestions();
                    sugMsg = ` [Sugestões] Alta: ${sugData.highConfidence} | Ambígua: ${sugData.ambiguous} | N/E: ${sugData.notFound}`;
                } catch (sugError: any) {
                    sugMsg = ' [Sugestões] Erro ao resolver sugestões de planta.';
                    console.warn('Suggestion resolution failed', sugError);
                }
                
                let deptMsg = `[Mestre] Processados: ${deptData.processed} | Criados: ${deptData.created} | Erros: ${deptData.errors ? deptData.errors.length : 0}`;
                let empMsg = `[Innux] Criados: ${empData.employeesCreated} | Atualizados: ${empData.employeesUpdated} | Desativados: ${empData.employeesDeactivated}`;
                
                let finalStatus: FeedbackType = 'success';
                let alertContext = '';

                if (deptData.errors && deptData.errors.length > 0) {
                    finalStatus = 'warning';
                }
                if (empData.status === 'PARTIAL') {
                    finalStatus = 'warning';
                    alertContext = ` Aviso: ${empData.message}`;
                }

                setFeedback({ type: finalStatus, message: `Sincronização global concluída com sucesso. ${deptMsg}. ${empMsg}.${sugMsg}${alertContext}` });
                
                // Recarrega tudo
                loadEmployees();
                loadMasterData();
                setActionModal(null);
            } catch (error: any) {
                console.error('Unified Sync error', error);
                setModalFeedback({ type: 'error', message: error.message || 'Falha ao processar as sincronizações. Verifique se os serviços (Innux e/ou Primavera) estão operacionais na rede.' });
            } finally {
                setModalProcessing(false);
            }
        }
    };

    // ─── Suggestion Rendering Helper ───
    const renderSuggestionHint = (emp: any) => {
        if (!emp.suggestedPlantConfidence || emp.suggestedPlantConfidence === 'NotFound') {
            if (emp.suggestedPlantConfidence === 'NotFound') {
                return (
                    <div style={{ marginTop: '4px', fontSize: '11px', color: '#94a3b8', fontStyle: 'italic' }}>
                        Sugestão Primavera: Não encontrada
                    </div>
                );
            }
            return null;
        }

        const isHigh = emp.suggestedPlantConfidence === 'High';
        const bgColor = isHigh ? '#f0fdf4' : '#fefce8';
        const borderColor = isHigh ? '#bbf7d0' : '#fef08a';
        const textColor = isHigh ? '#166534' : '#854d0e';
        const label = isHigh ? 'Viana 3' : 'Viana 1 ou Viana 2';
        const confidence = isHigh ? 'Alta' : 'Parcial / Ambígua';
        const source = emp.suggestedPlantSource?.replace('PRIMAVERA:', '') || '';

        return (
            <div style={{ marginTop: '6px', padding: '6px 8px', borderRadius: '6px', backgroundColor: bgColor, border: `1px solid ${borderColor}`, fontSize: '11px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', color: textColor, fontWeight: 600 }}>
                    <Sparkles size={11} />
                    Sugestão: {label}
                </div>
                <div style={{ color: textColor, opacity: 0.8, marginTop: '2px' }}>
                    Fonte: {source} | Confiança: {confidence}
                </div>
                {!emp.plantId && (
                    <button
                        onClick={(e) => { e.stopPropagation(); acceptSuggestion(emp); }}
                        style={{ marginTop: '4px', padding: '2px 8px', borderRadius: '4px', border: `1px solid ${borderColor}`, backgroundColor: '#fff', color: textColor, fontSize: '10px', fontWeight: 700, cursor: 'pointer', letterSpacing: '0.03em' }}
                        onMouseOver={e => e.currentTarget.style.backgroundColor = bgColor}
                        onMouseOut={e => e.currentTarget.style.backgroundColor = '#fff'}
                    >
                        {isHigh ? '✓ Aceitar Sugestão' : '✎ Mapear Manualmente'}
                    </button>
                )}
            </div>
        );
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
                
                <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                    <div style={{ position: 'relative', width: '280px' }}>
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
                        onClick={() => setShowFilters(!showFilters)}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '10px 14px', borderRadius: '8px', border: `1px solid ${showFilters ? '#0ea5e9' : '#cbd5e1'}`, backgroundColor: showFilters ? '#f0f9ff' : '#fff', cursor: 'pointer', fontSize: '13px', fontWeight: 600, color: showFilters ? '#0284c7' : '#475569' }}
                    >
                        <Filter size={15} />
                        Filtros
                        {hasActiveFilters && <span style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: '#0ea5e9' }} />}
                    </button>
                    <button 
                        onClick={() => setActionModal('SYNC_ALL')}
                        className="btn-primary"
                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 16px', borderRadius: '8px' }}
                    >
                        <RefreshCw size={16} />
                        Sincronizar
                    </button>
                </div>
            </div>

            {/* ─── KPI Summary Cards ─── */}
            {summary && (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: '12px' }}>
                    {[
                        { label: 'Total', value: summary.total, color: '#0f172a', chipKey: 'all' as FilterChipKey },
                        { label: 'Mapeados', value: summary.fullyMapped, color: '#16a34a', chipKey: 'mapped' as FilterChipKey },
                        { label: 'Não Mapeados', value: summary.unmapped, color: '#d97706', chipKey: 'unmapped' as FilterChipKey },
                        { label: 'Sem Planta', value: summary.withoutPlant, color: '#dc2626', chipKey: 'noPlant' as FilterChipKey },
                        { label: 'Sem Depto', value: summary.withoutDepartment, color: '#9333ea', chipKey: 'noDept' as FilterChipKey },
                        { label: 'Sem Chefe', value: summary.withoutManager, color: '#0284c7', chipKey: 'noManager' as FilterChipKey },
                        { label: 'Com Sugestão', value: summary.withSuggestion, color: '#f59e0b', chipKey: 'withSuggestion' as FilterChipKey },
                    ].map(card => (
                        <div
                            key={card.label}
                            onClick={() => handleChipClick(card.chipKey)}
                            style={{
                                padding: '14px 16px', borderRadius: '10px', cursor: 'pointer',
                                backgroundColor: activeChip === card.chipKey ? `${card.color}08` : 'var(--color-bg-surface)',
                                border: `1.5px solid ${activeChip === card.chipKey ? card.color : 'var(--color-border)'}`,
                                transition: 'all 0.15s ease',
                                boxShadow: activeChip === card.chipKey ? `0 0 0 3px ${card.color}15` : 'var(--shadow-sm)',
                            }}
                            onMouseOver={e => { if (activeChip !== card.chipKey) e.currentTarget.style.borderColor = card.color + '80'; }}
                            onMouseOut={e => { if (activeChip !== card.chipKey) e.currentTarget.style.borderColor = 'var(--color-border)'; }}
                        >
                            <div style={{ fontSize: '11px', fontWeight: 700, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>{card.label}</div>
                            <div style={{ fontSize: '1.5rem', fontWeight: 800, color: card.color, marginTop: '4px' }}>{card.value ?? '-'}</div>
                        </div>
                    ))}
                </div>
            )}

            {/* ─── Filter Bar ─── */}
            {showFilters && (
                <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'center', padding: '14px 16px', backgroundColor: '#f8fafc', borderRadius: '10px', border: '1px solid #e2e8f0' }}>
                    <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                        {FILTER_CHIPS.map(chip => (
                            <button
                                key={chip.key}
                                onClick={() => handleChipClick(chip.key)}
                                style={{
                                    display: 'flex', alignItems: 'center', gap: '4px', padding: '5px 10px', borderRadius: '16px',
                                    border: `1px solid ${activeChip === chip.key ? chip.color : '#cbd5e1'}`,
                                    backgroundColor: activeChip === chip.key ? chip.color + '12' : '#fff',
                                    color: activeChip === chip.key ? chip.color : '#475569',
                                    fontSize: '12px', fontWeight: 600, cursor: 'pointer', transition: 'all 0.12s ease',
                                }}
                            >
                                {chip.icon}
                                {chip.label}
                            </button>
                        ))}
                    </div>

                    <div style={{ width: '1px', height: '24px', backgroundColor: '#e2e8f0' }} />

                    <select
                        value={filterPlantId}
                        onChange={e => { setFilterPlantId(e.target.value); setPage(1); }}
                        style={{ padding: '6px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '12px', fontWeight: 500, color: filterPlantId ? '#0f172a' : '#94a3b8', minWidth: '140px', cursor: 'pointer' }}
                    >
                        <option value="">Planta (Todas)</option>
                        {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>

                    <input
                        type="text"
                        placeholder="Origem Innux..."
                        value={filterInnuxDept}
                        onChange={e => { setFilterInnuxDept(e.target.value); setPage(1); }}
                        style={{ padding: '6px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '12px', width: '140px' }}
                    />

                    {hasActiveFilters && (
                        <button
                            onClick={clearAllFilters}
                            style={{ padding: '5px 10px', borderRadius: '6px', border: '1px solid #fecaca', backgroundColor: '#fef2f2', color: '#dc2626', fontSize: '12px', fontWeight: 600, cursor: 'pointer' }}
                        >
                            <X size={12} style={{ marginRight: '4px', verticalAlign: 'middle' }} />
                            Limpar
                        </button>
                    )}
                </div>
            )}

            {selectedIds.length > 0 && (
                <div style={{ backgroundColor: '#f0f9ff', border: '1px solid #bae6fd', borderRadius: '8px', padding: '12px 16px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ color: '#0369a1', fontWeight: 600, fontSize: '14px' }}>{selectedIds.length} funcionário(s) selecionado(s)</span>
                    <div style={{ display: 'flex', gap: '8px' }}>
                        <button onClick={() => setSelectedIds([])} style={{ padding: '8px 12px', border: '1px solid #bae6fd', background: '#fff', borderRadius: '6px', color: '#0369a1', fontWeight: 600, fontSize: '13px', cursor: 'pointer' }}>
                            Cancelar
                        </button>
                        <button onClick={() => { setBulkModalVisible(true); setBulkStep(1); setBulkForm({ plantId: '', departmentMasterId: null, departmentMasterName: '', managerUserId: '' }); }} style={{ padding: '8px 12px', border: 'none', background: '#0284c7', color: '#fff', borderRadius: '6px', fontWeight: 600, fontSize: '13px', cursor: 'pointer' }}>
                            Atribuir departamento (Massa)
                        </button>
                    </div>
                </div>
            )}

            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', minWidth: '800px' }}>
                        <thead>
                            <tr style={{ backgroundColor: '#f8fafc', borderBottom: '2px solid #e2e8f0' }}>
                                <th style={{ padding: '16px', width: '40px' }}>
                                    <input 
                                        type="checkbox" 
                                        checked={employees.length > 0 && selectedIds.length === employees.length}
                                        onChange={(e) => {
                                            if (e.target.checked) setSelectedIds(employees.map(emp => emp.id));
                                            else setSelectedIds([]);
                                        }}
                                        style={{ cursor: 'pointer' }}
                                    />
                                </th>
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
                                    <td colSpan={6} style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>
                                        <RefreshCw size={24} style={{ margin: '0 auto 12px', display: 'block', animation: 'spin 1s linear infinite' }} />
                                        A carregar diretório...
                                    </td>
                                </tr>
                            ) : employees.length === 0 ? (
                                <tr>
                                    <td colSpan={6} style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>
                                        Nenhum funcionário encontrado. {search ? 'Limpe a pesquisa ou tente outro termo.' : 'Sincronize com o Innux para popular a base.'}
                                    </td>
                                </tr>
                            ) : (
                                employees.map(emp => {
                                    const isEditing = editingId === emp.id;
                                    
                                    return (
                                        <tr key={emp.id} style={{ borderBottom: '1px solid #e2e8f0', backgroundColor: !emp.isMapped ? '#fffbeb' : '#fff' }}>
                                            <td style={{ padding: '16px' }}>
                                                <input 
                                                    type="checkbox"
                                                    checked={selectedIds.includes(emp.id)}
                                                    onChange={(e) => {
                                                        if (e.target.checked) setSelectedIds(prev => [...prev, emp.id]);
                                                        else setSelectedIds(prev => prev.filter(id => id !== emp.id));
                                                    }}
                                                    style={{ cursor: 'pointer' }}
                                                />
                                            </td>
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
                                                    {!emp.plantId && renderSuggestionHint(emp)}
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
                                                            {saving ? <RefreshCw size={14} style={{ animation: 'spin 1s linear infinite' }} /> : <Save size={14} />}
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

            {bulkModalVisible && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(15,23,42,0.6)', zIndex: 100, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <div style={{ backgroundColor: '#fff', borderRadius: '12px', width: '500px', maxWidth: '90vw', overflow: 'hidden', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}>
                        <div style={{ padding: '20px 24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <h3 style={{ margin: 0, fontSize: '1.25rem', color: '#0f172a' }}>Atribuição em Massa</h3>
                            <button onClick={() => setBulkModalVisible(false)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', padding: '4px' }}>
                                <X size={20} color="#64748b" />
                            </button>
                        </div>
                        
                        <div style={{ padding: '24px' }}>
                            {bulkStep === 1 ? (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                    <div>
                                        <label style={{ display: 'block', marginBottom: '8px', fontWeight: 600, color: '#334155', fontSize: '13px' }}>Planta *</label>
                                        <select 
                                            value={bulkForm.plantId} 
                                            onChange={e => handleBulkPlantChange(e.target.value)}
                                            style={{ width: '100%', padding: '10px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                        >
                                            <option value="" disabled>Selecione a planta</option>
                                            {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                        </select>
                                    </div>
                                    
                                    <div>
                                        <label style={{ display: 'block', marginBottom: '8px', fontWeight: 600, color: bulkForm.plantId ? '#334155' : '#94a3b8', fontSize: '13px' }}>Departamento (Mestre) {bulkForm.plantId && '*'}</label>
                                        {bulkForm.plantId ? (
                                            <DepartmentMasterAutocomplete
                                                initialId={bulkForm.departmentMasterId && bulkForm.departmentMasterId !== 'CLEAR' ? bulkForm.departmentMasterId as number : null}
                                                initialName={bulkForm.departmentMasterId === 'CLEAR' ? '' : bulkForm.departmentMasterName}
                                                suggestedMatchText=""
                                                allowedCompanyCode={getCompanyForPlant(bulkForm.plantId)}
                                                plantNotSelected={!bulkForm.plantId}
                                                onChange={(id, name) => setBulkForm(prev => ({ ...prev, departmentMasterId: id, departmentMasterName: name }))}
                                            />
                                        ) : (
                                            <div style={{ padding: '10px', backgroundColor: '#f1f5f9', borderRadius: '6px', color: '#64748b', fontSize: '13px', border: '1px dashed #cbd5e1' }}>
                                                Selecione primeiro a planta para habilitar os departamentos.
                                            </div>
                                        )}
                                        {bulkForm.plantId && (
                                            <div style={{ marginTop: '8px' }}>
                                                <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '13px', color: '#64748b', cursor: 'pointer' }}>
                                                    <input 
                                                        type="checkbox" 
                                                        checked={bulkForm.departmentMasterId === 'CLEAR'}
                                                        onChange={e => {
                                                            if (e.target.checked) setBulkForm(prev => ({ ...prev, departmentMasterId: 'CLEAR', departmentMasterName: '' }));
                                                            else setBulkForm(prev => ({ ...prev, departmentMasterId: null, departmentMasterName: '' }));
                                                        }}
                                                    />
                                                    Remover Departamento Mestre
                                                </label>
                                            </div>
                                        )}
                                    </div>

                                    <div>
                                        <label style={{ display: 'block', marginBottom: '8px', fontWeight: 600, color: '#334155', fontSize: '13px' }}>Gestor / Responsável</label>
                                        <select 
                                            value={bulkForm.managerUserId} 
                                            onChange={e => setBulkForm({...bulkForm, managerUserId: e.target.value})}
                                            style={{ width: '100%', padding: '10px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                        >
                                            <option value="">Sem alteração / Manter atual</option>
                                            <option value="CLEAR">🛑 Remover Responsável/Gestor</option>
                                            {managers.map(u => <option key={u.id} value={u.id}>{u.fullName}</option>)}
                                        </select>
                                    </div>
                                </div>
                            ) : (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                    <div style={{ backgroundColor: '#f8fafc', padding: '16px', borderRadius: '8px', border: '1px solid #e2e8f0' }}>
                                        <h4 style={{ margin: '0 0 12px 0', color: '#0f172a', display: 'flex', alignItems: 'center', gap: '8px' }}>
                                            <AlertTriangle size={18} color="#d97706" />
                                            Confirme as alterações
                                        </h4>
                                        <p style={{ margin: '0 0 16px 0', fontSize: '14px', color: '#475569' }}>
                                            Esta ação irá atualizar os seguintes campos para <strong>{selectedIds.length}</strong> funcionário(s) selecionado(s):
                                        </p>
                                        
                                        <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: '8px', fontSize: '14px' }}>
                                            <li style={{ display: 'flex', gap: '8px' }}>
                                                <Building size={16} color="#64748b" /> 
                                                <span>Planta: <strong>{plants.find(p => p.id.toString() === bulkForm.plantId.toString())?.name}</strong></span>
                                            </li>
                                            <li style={{ display: 'flex', gap: '8px' }}>
                                                <Users size={16} color="#64748b" /> 
                                                <span>Departamento: <strong>{bulkForm.departmentMasterId === 'CLEAR' ? 'Remover Departamento Mestre' : (bulkForm.departmentMasterName || 'Sem alteração')}</strong></span>
                                            </li>
                                            <li style={{ display: 'flex', gap: '8px' }}>
                                                <Users size={16} color="#64748b" /> 
                                                <span>Gestor: <strong>{bulkForm.managerUserId === 'CLEAR' ? 'Remover Gestor' : (bulkForm.managerUserId ? managers.find(m => m.id === bulkForm.managerUserId)?.fullName : 'Sem alteração')}</strong></span>
                                            </li>
                                        </ul>
                                    </div>
                                </div>
                            )}
                        </div>

                        <div style={{ padding: '16px 24px', backgroundColor: '#f8fafc', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'flex-end', gap: '12px' }}>
                            <button 
                                onClick={() => bulkStep === 2 ? setBulkStep(1) : setBulkModalVisible(false)}
                                disabled={bulkSaving}
                                style={{ padding: '10px 16px', borderRadius: '8px', border: '1px solid #cbd5e1', backgroundColor: '#fff', cursor: 'pointer', fontWeight: 600, color: '#475569' }}
                            >
                                {bulkStep === 2 ? 'Voltar' : 'Cancelar'}
                            </button>
                            {bulkStep === 1 ? (
                                <button 
                                    onClick={() => setBulkStep(2)}
                                    disabled={!bulkForm.plantId}
                                    style={{ padding: '10px 16px', borderRadius: '8px', border: 'none', backgroundColor: '#0ea5e9', color: '#fff', cursor: bulkForm.plantId ? 'pointer' : 'not-allowed', fontWeight: 600, opacity: bulkForm.plantId ? 1 : 0.6 }}
                                >
                                    Continuar
                                </button>
                            ) : (
                                <button 
                                    onClick={submitBulkMapping}
                                    disabled={bulkSaving}
                                    style={{ padding: '10px 16px', borderRadius: '8px', border: 'none', backgroundColor: '#0284c7', color: '#fff', cursor: bulkSaving ? 'not-allowed' : 'pointer', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}
                                >
                                    {bulkSaving ? <RefreshCw size={16} style={{ animation: 'spin 1s linear infinite' }} /> : <Save size={16} />}
                                    Aplicar
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
