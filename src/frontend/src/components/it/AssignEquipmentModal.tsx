import React, { useState, useEffect, useCallback, useRef } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import { ModalWrapper, SubmitBtn, ErrorBox, Field, Row, TextArea, cancelBtnStyle, labelStyle, inputStyle } from './EquipmentFormModal';

interface Props { equipmentId: string; onClose: () => void; onSuccess: () => void; }

interface UserOption { id: string; fullName: string; email: string; }

export function AssignEquipmentModal({ equipmentId, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [warnings, setWarnings] = useState<string[]>([]);
    const todayISO = new Date().toISOString().slice(0, 10);
    const [form, setForm] = useState({
        assignedToUserId: undefined as string | undefined,
        assignedToName: '',
        assignedToEmail: '',
        assignedDate: todayISO,
        assignedToDepartment: '',
        assignedToPlant: '',
        notes: ''
    });
    const set = (f: string, v: string) => setForm(p => ({ ...p, [f]: v }));

    // Master data for dropdowns
    const [plants, setPlants] = useState<any[]>([]);
    const [departments, setDepartments] = useState<any[]>([]);
    const [loadingMaster, setLoadingMaster] = useState(true);

    // User search
    const [userSearch, setUserSearch] = useState('');
    const [userOptions, setUserOptions] = useState<UserOption[]>([]);
    const [showDropdown, setShowDropdown] = useState(false);
    const [searchLoading, setSearchLoading] = useState(false);
    const [selectedUser, setSelectedUser] = useState<UserOption | null>(null);
    const searchTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
    const dropdownRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        const loadMasterData = async () => {
            try {
                setLoadingMaster(true);
                const [plantsData, deptsData] = await Promise.all([
                    api.lookups.getPlants(),
                    api.lookups.getDepartments(),
                ]);
                setPlants((plantsData || []).filter((p: any) => p.isActive !== false));
                setDepartments((deptsData || []).filter((d: any) => d.isActive !== false));
            } catch {
                // Silently fail
            } finally {
                setLoadingMaster(false);
            }
        };
        loadMasterData();
    }, []);

    // Load all portal users once for search
    const [allUsers, setAllUsers] = useState<UserOption[]>([]);
    const [_usersLoaded, setUsersLoaded] = useState(false);

    useEffect(() => {
        const loadUsers = async () => {
            try {
                const users = await api.users.list(false);
                setAllUsers((users || []).map((u: any) => ({
                    id: u.id,
                    fullName: u.fullName || `${u.firstName || ''} ${u.lastName || ''}`.trim(),
                    email: u.email || ''
                })));
            } catch {
                // Silently fail — manual entry fallback works
            } finally {
                setUsersLoaded(true);
            }
        };
        loadUsers();
    }, []);

    // Debounced user search (client-side filter)
    const searchUsers = useCallback((query: string) => {
        if (query.length < 2) {
            setUserOptions([]);
            setShowDropdown(false);
            return;
        }
        setSearchLoading(true);
        const q = query.toLowerCase();
        const filtered = allUsers.filter(u =>
            u.fullName.toLowerCase().includes(q) || u.email.toLowerCase().includes(q)
        ).slice(0, 15);
        setUserOptions(filtered);
        setShowDropdown(filtered.length > 0);
        setSearchLoading(false);
    }, [allUsers]);

    const handleUserSearchChange = (value: string) => {
        setUserSearch(value);
        setSelectedUser(null);
        set('assignedToUserId', '');
        set('assignedToName', value);
        set('assignedToEmail', '');

        if (searchTimeout.current) clearTimeout(searchTimeout.current);
        searchTimeout.current = setTimeout(() => searchUsers(value), 300);
    };

    const selectUser = (user: UserOption) => {
        setSelectedUser(user);
        setUserSearch(user.fullName);
        setShowDropdown(false);
        setForm(p => ({
            ...p,
            assignedToUserId: user.id,
            assignedToName: user.fullName,
            assignedToEmail: user.email
        }));
    };

    // Close dropdown on outside click
    useEffect(() => {
        const handler = (e: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node))
                setShowDropdown(false);
        };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, []);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!form.assignedToName.trim()) { setError('Nome do utilizador é obrigatório.'); return; }
        if (!form.assignedToEmail.trim()) { setError('Email do utilizador é obrigatório para gerar o Termo de Responsabilidade.'); return; }
        if (!form.assignedDate) { setError('Data de disponibilização é obrigatória.'); return; }
        try {
            setSaving(true); setError(''); setWarnings([]);
            const result = await itEquipmentApi.assign(equipmentId, {
                assignedToUserId: form.assignedToUserId || undefined,
                assignedToName: form.assignedToName,
                assignedToEmail: form.assignedToEmail,
                assignedDate: form.assignedDate ? new Date(form.assignedDate + 'T00:00:00Z').toISOString() : undefined,
                assignedToDepartment: form.assignedToDepartment || undefined,
                assignedToPlant: form.assignedToPlant || undefined,
                notes: form.notes || undefined
            });
            if (result?.warnings && result.warnings.length > 0) {
                setWarnings(result.warnings);
                // Show warnings briefly then close
                setTimeout(() => onSuccess(), 3000);
            } else {
                onSuccess();
            }
        } catch (err: any) { setError(err.message); } finally { setSaving(false); }
    };

    const dropdownStyle: React.CSSProperties = {
        position: 'absolute', top: '100%', left: 0, right: 0,
        background: '#fff', border: '1px solid #d1d5db', borderRadius: 6,
        boxShadow: '0 8px 24px rgba(0,0,0,0.12)', zIndex: 100,
        maxHeight: 200, overflowY: 'auto'
    };

    const optionStyle: React.CSSProperties = {
        padding: '8px 12px', cursor: 'pointer', fontSize: 13,
        borderBottom: '1px solid #f0f0f0', transition: 'background 0.15s'
    };

    return (
        <ModalWrapper title="Atribuir Equipamento" onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}
                {warnings.length > 0 && (
                    <div style={{ background: '#fef3cd', color: '#856404', padding: '8px 12px', borderRadius: 6, fontSize: 12.5, border: '1px solid #ffc107' }}>
                        <strong>⚠️ Atribuição concluída com avisos:</strong>
                        {warnings.map((w, i) => <div key={i} style={{ marginTop: 4 }}>{w}</div>)}
                    </div>
                )}

                {/* User search with autocomplete */}
                <div style={{ position: 'relative' }} ref={dropdownRef}>
                    <label style={labelStyle}>Utilizador *</label>
                    <input
                        type="text"
                        value={userSearch}
                        onChange={e => handleUserSearchChange(e.target.value)}
                        onFocus={() => userOptions.length > 0 && setShowDropdown(true)}
                        placeholder="Pesquisar utilizador por nome..."
                        autoComplete="off"
                        style={{ ...inputStyle, position: 'relative' }}
                    />
                    {searchLoading && (
                        <span style={{ position: 'absolute', right: 12, top: 32, fontSize: 11, color: '#999' }}>
                            Pesquisando...
                        </span>
                    )}
                    {selectedUser && (
                        <span style={{ fontSize: 11, color: '#16a34a', marginTop: 2 }}>
                            ✓ Utilizador do portal selecionado
                        </span>
                    )}
                    {showDropdown && userOptions.length > 0 && (
                        <div style={dropdownStyle}>
                            {userOptions.map(u => (
                                <div
                                    key={u.id}
                                    onClick={() => selectUser(u)}
                                    onMouseEnter={e => (e.currentTarget.style.background = '#f3f4f6')}
                                    onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                                    style={optionStyle}
                                >
                                    <div style={{ fontWeight: 500 }}>{u.fullName}</div>
                                    {u.email && <div style={{ fontSize: 11, color: '#888' }}>{u.email}</div>}
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                <Field
                    label={selectedUser ? 'Email do Utilizador (preenchido automaticamente)' : 'Email do Utilizador *'}
                    value={form.assignedToEmail}
                    onChange={v => set('assignedToEmail', v)}
                />

                <div>
                    <label style={labelStyle}>Data de disponibilização ao utilizador *</label>
                    <input
                        type="date"
                        value={form.assignedDate}
                        onChange={e => set('assignedDate', e.target.value)}
                        required
                        style={inputStyle}
                    />
                    <span style={{ fontSize: 11, color: '#888', marginTop: 2, display: 'block' }}>
                        Data em que o equipamento foi/será disponibilizado ao utilizador.
                    </span>
                </div>

                <Row>
                    <div style={{ flex: 1 }}>
                        <label style={labelStyle}>Planta</label>
                        <select
                            value={form.assignedToPlant}
                            onChange={e => set('assignedToPlant', e.target.value)}
                            disabled={loadingMaster}
                            style={{ ...inputStyle, cursor: loadingMaster ? 'wait' : 'pointer' }}
                        >
                            <option value="">Selecione a planta</option>
                            {plants.map((p: any) => (
                                <option key={p.id} value={p.name}>{p.name}</option>
                            ))}
                        </select>
                    </div>
                    <div style={{ flex: 1 }}>
                        <label style={labelStyle}>Departamento</label>
                        <select
                            value={form.assignedToDepartment}
                            onChange={e => set('assignedToDepartment', e.target.value)}
                            disabled={loadingMaster}
                            style={{ ...inputStyle, cursor: loadingMaster ? 'wait' : 'pointer' }}
                        >
                            <option value="">Selecione o departamento</option>
                            {departments.map((d: any) => (
                                <option key={d.id} value={d.name}>{d.name}</option>
                            ))}
                        </select>
                    </div>
                </Row>
                <TextArea label="Notas" value={form.notes} onChange={v => set('notes', v)} rows={2} />
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label="Atribuir" loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
