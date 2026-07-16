import { useState, useEffect, useCallback } from 'react';
import { Plus, Trash2, Power, AlertTriangle, CheckCircle2, Globe } from 'lucide-react';
import { api } from '../../lib/api';

interface ManagerRow {
    id: number;
    plantId: number | null;
    plantName: string | null;
    userId: string;
    userFullName: string;
    userEmail: string;
    userIsActive: boolean;
    isActive: boolean;
}

interface Props {
    departmentId: number;
    departmentCode: string;
    /** All plants (active ones are offered for adding). */
    plants: any[];
    /** Users from api.users.list() — includes plants/departments scope codes. */
    users: any[];
}

const labelStyle: React.CSSProperties = {
    display: 'block', fontSize: '0.75rem', fontWeight: 800,
    color: 'var(--color-text-main)', textTransform: 'uppercase', marginBottom: '6px'
};

const selectStyle: React.CSSProperties = {
    width: '100%', padding: '10px', backgroundColor: 'white',
    border: '2px solid var(--color-border)', fontSize: '0.85rem', fontWeight: 600, outline: 'none'
};

/**
 * Managers de aprovação de área por departamento + planta — única fonte de
 * configuração do roteamento de aprovação (redesign concluído na Fase C).
 */
export function DepartmentManagersGrid({ departmentId, departmentCode, plants, users }: Props) {
    const [managers, setManagers] = useState<ManagerRow[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [confirmation, setConfirmation] = useState<string | null>(null);

    const [newPlantId, setNewPlantId] = useState<string>('');   // '' = Global
    const [newUserId, setNewUserId] = useState<string>('');

    const load = useCallback(async () => {
        try {
            setLoading(true);
            setManagers(await api.lookups.getDepartmentManagers(departmentId));
        } catch (err: any) {
            setError(err.message || 'Falha ao carregar managers.');
        } finally {
            setLoading(false);
        }
    }, [departmentId]);

    useEffect(() => { load(); }, [load]);

    const activePlants = plants.filter(p => p.isActive);
    const activeUsers = users.filter(u => u.isActive);
    const selectedUser = activeUsers.find(u => u.id === newUserId);

    // D3 — aviso ANTES de salvar: escopos de visibilidade que serão criados automaticamente.
    const pendingScopeAdditions: string[] = [];
    if (selectedUser) {
        const userDeptCodes: string[] = (selectedUser.departments || []).map((c: string) => (c || '').toUpperCase());
        const userPlantCodes: string[] = (selectedUser.plants || []).map((c: string) => (c || '').toUpperCase());
        if (departmentCode && !userDeptCodes.includes(departmentCode.toUpperCase())) {
            pendingScopeAdditions.push(`Departamento ${departmentCode}`);
        }
        const targetPlants = newPlantId ? activePlants.filter(p => String(p.id) === newPlantId) : activePlants;
        for (const p of targetPlants) {
            if (p.code && !userPlantCodes.includes(p.code.toUpperCase())) {
                pendingScopeAdditions.push(`Planta ${p.name}`);
            }
        }
    }

    const handleAdd = async () => {
        if (!newUserId) return;
        setSaving(true);
        setError(null);
        setConfirmation(null);
        try {
            const result = await api.lookups.addDepartmentManager(departmentId, {
                userId: newUserId,
                plantId: newPlantId ? Number(newPlantId) : null
            });
            const created = [
                ...(result.createdDepartmentScopes || []).map((n: string) => `Departamento ${n}`),
                ...(result.createdPlantScopes || []).map((n: string) => `Planta ${n}`)
            ];
            setConfirmation(created.length > 0
                ? `Manager adicionado. Escopos de visibilidade criados automaticamente: ${created.join(', ')}.`
                : 'Manager adicionado. Nenhum escopo novo foi necessário.');
            setNewUserId('');
            setNewPlantId('');
            await load();
        } catch (err: any) {
            setError(err.message || 'Falha ao adicionar manager.');
        } finally {
            setSaving(false);
        }
    };

    const handleToggle = async (managerId: number) => {
        setError(null);
        setConfirmation(null);
        try {
            await api.lookups.toggleDepartmentManager(departmentId, managerId);
            await load();
        } catch (err: any) {
            setError(err.message || 'Falha ao alternar estado do manager.');
        }
    };

    const handleRemove = async (managerId: number) => {
        if (!window.confirm('Remover este manager? Os escopos de visibilidade do utilizador serão mantidos.')) return;
        setError(null);
        setConfirmation(null);
        try {
            await api.lookups.removeDepartmentManager(departmentId, managerId);
            await load();
        } catch (err: any) {
            setError(err.message || 'Falha ao remover manager.');
        }
    };

    return (
        <div style={{ marginTop: '16px', borderTop: '2px solid var(--color-border)', paddingTop: '16px' }}>
            <label style={labelStyle}>Managers de Aprovação de Área</label>
            <p style={{ margin: '0 0 10px', fontSize: '0.65rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                Defina os responsáveis por planta ou um manager global para o departamento. Managers específicos recebem
                as notificações da própria planta; managers globais funcionam como responsáveis de cobertura (recebem
                quando não há específicos e podem sempre aprovar). Esta grade é a única configuração de aprovação de área.
                Não confundir com a role Local Manager (administração de utilizadores).
            </p>

            {loading ? (
                <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>A carregar managers…</p>
            ) : (
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem', marginBottom: '12px' }}>
                    <thead>
                        <tr style={{ textAlign: 'left', borderBottom: '2px solid var(--color-border)' }}>
                            <th style={{ padding: '6px' }}>Planta</th>
                            <th style={{ padding: '6px' }}>Utilizador</th>
                            <th style={{ padding: '6px' }}>Estado</th>
                            <th style={{ padding: '6px', width: '80px' }}></th>
                        </tr>
                    </thead>
                    <tbody>
                        {managers.length === 0 && (
                            <tr><td colSpan={4} style={{ padding: '10px 6px', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                Nenhum manager cadastrado para este departamento.
                            </td></tr>
                        )}
                        {managers.map(m => (
                            <tr key={m.id} style={{ borderBottom: '1px solid var(--color-border)', opacity: m.isActive ? 1 : 0.5 }}>
                                <td style={{ padding: '6px', fontWeight: 700 }}>
                                    {m.plantId === null
                                        ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}><Globe size={12} /> Global</span>
                                        : m.plantName}
                                </td>
                                <td style={{ padding: '6px' }}>
                                    {m.userFullName}
                                    {!m.userIsActive && (
                                        <span style={{ marginLeft: '6px', color: '#DC2626', fontWeight: 700, fontSize: '0.7rem' }}>
                                            (utilizador inativo)
                                        </span>
                                    )}
                                    <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>{m.userEmail}</div>
                                </td>
                                <td style={{ padding: '6px' }}>{m.isActive ? 'Ativo' : 'Inativo'}</td>
                                <td style={{ padding: '6px', whiteSpace: 'nowrap' }}>
                                    <button type="button" onClick={() => handleToggle(m.id)} title={m.isActive ? 'Desativar' : 'Ativar'}
                                        style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-main)', padding: '4px' }}>
                                        <Power size={15} />
                                    </button>
                                    <button type="button" onClick={() => handleRemove(m.id)} title="Remover"
                                        style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#DC2626', padding: '4px' }}>
                                        <Trash2 size={15} />
                                    </button>
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.5fr auto', gap: '8px', alignItems: 'end' }}>
                <div>
                    <label style={{ ...labelStyle, fontSize: '0.65rem' }}>Planta</label>
                    <select style={selectStyle} value={newPlantId} onChange={e => setNewPlantId(e.target.value)}>
                        <option value="">Global (todas as plantas)</option>
                        {activePlants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                </div>
                <div>
                    <label style={{ ...labelStyle, fontSize: '0.65rem' }}>Utilizador</label>
                    <select style={selectStyle} value={newUserId} onChange={e => setNewUserId(e.target.value)}>
                        <option value="">Selecione…</option>
                        {activeUsers.map(u => <option key={u.id} value={u.id}>{u.fullName}</option>)}
                    </select>
                </div>
                <button type="button" onClick={handleAdd} disabled={!newUserId || saving}
                    style={{
                        padding: '10px 14px', border: 'none', cursor: newUserId && !saving ? 'pointer' : 'not-allowed',
                        backgroundColor: 'var(--color-primary)', color: 'white', fontWeight: 700, fontSize: '0.8rem',
                        display: 'inline-flex', alignItems: 'center', gap: '6px', opacity: newUserId && !saving ? 1 : 0.5
                    }}>
                    <Plus size={15} /> Adicionar
                </button>
            </div>

            {pendingScopeAdditions.length > 0 && (
                <div style={{ marginTop: '10px', padding: '10px', backgroundColor: '#FFFBEB', border: '1px solid #FCD34D', fontSize: '0.75rem', color: '#92400E', display: 'flex', gap: '8px', alignItems: 'flex-start' }}>
                    <AlertTriangle size={16} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <span>
                        Ao confirmar, os seguintes escopos de visibilidade serão adicionados automaticamente a este
                        utilizador: <b>{pendingScopeAdditions.join(', ')}</b>. Sem eles, o manager não veria os pedidos na fila.
                    </span>
                </div>
            )}

            {confirmation && (
                <div style={{ marginTop: '10px', padding: '10px', backgroundColor: '#F0FDF4', border: '1px solid #86EFAC', fontSize: '0.75rem', color: '#166534', display: 'flex', gap: '8px', alignItems: 'flex-start' }}>
                    <CheckCircle2 size={16} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <span>{confirmation}</span>
                </div>
            )}

            {error && (
                <div style={{ marginTop: '10px', padding: '10px', backgroundColor: '#FEF2F2', border: '1px solid #FCA5A5', fontSize: '0.75rem', color: '#991B1B' }}>
                    {error}
                </div>
            )}
        </div>
    );
}
