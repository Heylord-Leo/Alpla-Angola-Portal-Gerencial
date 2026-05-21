import React, { useState, useEffect, useCallback, useRef } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import { ModalWrapper, SubmitBtn, ErrorBox, Field, Row, TextArea, SelectField, cancelBtnStyle, labelStyle, inputStyle } from './EquipmentFormModal';
import { Info, AlertTriangle } from 'lucide-react';
import type { ITEquipmentDetail, ITEquipmentAssignment } from '../../types/itEquipment';

interface Props { equipmentId: string; onClose: () => void; onSuccess: () => void; }

interface UserOption { id: string; fullName: string; email: string; }

export function ChangeEquipmentUserModal({ equipmentId, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [warnings, setWarnings] = useState<string[]>([]);
    const [detail, setDetail] = useState<ITEquipmentDetail | null>(null);
    const [loading, setLoading] = useState(true);

    // Current assignment
    const [currentAssignment, setCurrentAssignment] = useState<ITEquipmentAssignment | null>(null);

    // Return form
    const [returnCondition, setReturnCondition] = useState('GOOD');
    const [returnNotes, setReturnNotes] = useState('');

    // New user form
    const [form, setForm] = useState({
        newAssignedToUserId: undefined as string | undefined,
        newAssignedToName: '',
        newAssignedToEmail: '',
        newAssignedToDepartment: '',
        newAssignedToPlant: '',
        newAssignmentNotes: '',
    });
    const set = (f: string, v: string) => setForm(p => ({ ...p, [f]: v }));

    // Master data
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
    const [allUsers, setAllUsers] = useState<UserOption[]>([]);
    const [usersLoaded, setUsersLoaded] = useState(false);

    // Same user confirmation
    const [sameUserConfirmed, setSameUserConfirmed] = useState(false);

    // Load equipment detail + master data + users
    useEffect(() => {
        const load = async () => {
            try {
                setLoading(true);
                const [eq, plantsData, deptsData, users] = await Promise.all([
                    itEquipmentApi.get(equipmentId),
                    api.lookups.getPlants(),
                    api.lookups.getDepartments(),
                    api.users.list(false),
                ]);
                setDetail(eq);
                const active = (eq.assignments || []).find((a: ITEquipmentAssignment) => a.assignmentStatus === 'ACTIVE');
                setCurrentAssignment(active || null);
                setPlants((plantsData || []).filter((p: any) => p.isActive !== false));
                setDepartments((deptsData || []).filter((d: any) => d.isActive !== false));
                setAllUsers((users || []).map((u: any) => ({
                    id: u.id,
                    fullName: u.fullName || `${u.firstName || ''} ${u.lastName || ''}`.trim(),
                    email: u.email || ''
                })));
                setUsersLoaded(true);
            } catch { }
            finally { setLoading(false); setLoadingMaster(false); }
        };
        load();
    }, [equipmentId]);

    // User search (client-side filter)
    const searchUsers = useCallback((query: string) => {
        if (query.length < 2) { setUserOptions([]); setShowDropdown(false); return; }
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
        setSameUserConfirmed(false);
        set('newAssignedToUserId', '');
        set('newAssignedToName', value);
        set('newAssignedToEmail', '');
        if (searchTimeout.current) clearTimeout(searchTimeout.current);
        searchTimeout.current = setTimeout(() => searchUsers(value), 300);
    };

    const selectUser = (user: UserOption) => {
        setSelectedUser(user);
        setUserSearch(user.fullName);
        setShowDropdown(false);
        setSameUserConfirmed(false);
        setForm(p => ({
            ...p,
            newAssignedToUserId: user.id,
            newAssignedToName: user.fullName,
            newAssignedToEmail: user.email
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

    // Validation
    const needsReturnNotes = returnCondition !== 'GOOD';
    const isSameUser = currentAssignment && form.newAssignedToName.trim().toLowerCase() === currentAssignment.assignedToName?.toLowerCase();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');

        if (!currentAssignment) { setError('Este equipamento não possui uma atribuição ativa.'); return; }
        if (!form.newAssignedToName.trim()) { setError('Informe o novo utilizador.'); return; }
        if (!form.newAssignedToEmail.trim()) { setError('Informe o email do novo utilizador.'); return; }
        if (needsReturnNotes && !returnNotes.trim()) {
            setError('Informe uma observação quando o equipamento não estiver em bom estado.');
            return;
        }
        if (returnCondition !== 'GOOD') {
            setError('Não é possível transferir o equipamento para outro utilizador quando a condição da devolução indica dano ou necessidade de conserto. Faça a devolução normal e envie o equipamento para conserto.');
            return;
        }
        if (isSameUser && !sameUserConfirmed) {
            setError('O novo utilizador é o mesmo utilizador atual. Confirme se deseja continuar.');
            setSameUserConfirmed(true);
            return;
        }

        try {
            setSaving(true); setWarnings([]);
            const result = await itEquipmentApi.changeUser(equipmentId, {
                returnCondition,
                returnNotes: returnNotes || undefined,
                newAssignedToUserId: form.newAssignedToUserId || undefined,
                newAssignedToName: form.newAssignedToName,
                newAssignedToEmail: form.newAssignedToEmail,
                newAssignedToDepartment: form.newAssignedToDepartment || undefined,
                newAssignedToPlant: form.newAssignedToPlant || undefined,
                newAssignmentNotes: form.newAssignmentNotes || undefined,
            });
            if (result?.warnings && result.warnings.length > 0) {
                setWarnings(result.warnings);
                setTimeout(() => onSuccess(), 4000);
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

    const sectionTitleStyle: React.CSSProperties = {
        fontSize: '0.82rem', fontWeight: 700, color: 'var(--color-text)',
        padding: '6px 0 4px', borderBottom: '1px solid var(--color-border)', marginBottom: 8,
        display: 'flex', alignItems: 'center', gap: 6
    };

    const readOnlyFieldStyle: React.CSSProperties = {
        fontSize: '0.8rem', color: 'var(--color-text-muted)',
        display: 'flex', justifyContent: 'space-between', padding: '3px 0'
    };

    if (loading) {
        return (
            <ModalWrapper title="Trocar Utilizador" onClose={onClose}>
                <div style={{ padding: 40, textAlign: 'center', color: 'var(--color-text-muted)' }}>Carregando...</div>
            </ModalWrapper>
        );
    }

    if (!detail || !currentAssignment) {
        return (
            <ModalWrapper title="Trocar Utilizador" onClose={onClose}>
                <ErrorBox msg="Este equipamento não possui uma atribuição ativa." />
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 12 }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Fechar</button>
                </div>
            </ModalWrapper>
        );
    }

    return (
        <ModalWrapper title="Trocar Utilizador" onClose={onClose} width={580}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {error && <ErrorBox msg={error} />}

                {/* Warnings */}
                {warnings.length > 0 && (
                    <div style={{
                        padding: '10px 14px', backgroundColor: '#fffbeb', border: '1px solid #fcd34d',
                        borderRadius: 8, color: '#92400e', fontSize: '0.82rem',
                        display: 'flex', gap: 8, alignItems: 'flex-start'
                    }}>
                        <AlertTriangle size={16} style={{ marginTop: 2, flexShrink: 0, color: '#f59e0b' }} />
                        <div>
                            <div style={{ fontWeight: 600, marginBottom: 4 }}>
                                O equipamento foi transferido e os documentos foram gerados, mas ocorreu uma falha ao enviar um ou mais e-mails.
                            </div>
                            {warnings.map((w, i) => <div key={i}>• {w}</div>)}
                        </div>
                    </div>
                )}

                {/* Info notice */}
                <div style={{
                    padding: '10px 14px', backgroundColor: 'rgba(59,130,246,0.08)',
                    border: '1px solid rgba(59,130,246,0.2)', borderRadius: 8,
                    color: 'var(--color-text-muted)', fontSize: '0.78rem',
                    display: 'flex', gap: 8, alignItems: 'flex-start'
                }}>
                    <Info size={16} style={{ marginTop: 2, flexShrink: 0, color: '#3b82f6' }} />
                    <span>
                        Esta ação irá gerar automaticamente um <strong>Termo de Devolução em PDF</strong> para o utilizador
                        atual e um <strong>Termo de Entrega em PDF</strong> para o novo utilizador.
                    </span>
                </div>

                {/* ═══ Section A: Return ═══ */}
                <div style={sectionTitleStyle}>
                    <span style={{ color: '#8b5cf6' }}>A)</span> Devolução do utilizador atual
                </div>

                <div style={{
                    background: 'var(--color-bg-alt, #f9fafb)', borderRadius: 8, padding: '10px 14px',
                    border: '1px solid var(--color-border)', fontSize: '0.8rem'
                }}>
                    <div style={readOnlyFieldStyle}><span>Utilizador atual:</span> <strong>{currentAssignment.assignedToName}</strong></div>
                    <div style={readOnlyFieldStyle}><span>Email:</span> <span>{currentAssignment.assignedToEmail || '—'}</span></div>
                    <div style={readOnlyFieldStyle}><span>Departamento:</span> <span>{currentAssignment.assignedToDepartment || '—'}</span></div>
                    <div style={readOnlyFieldStyle}><span>Planta:</span> <span>{currentAssignment.assignedToPlant || '—'}</span></div>
                    <div style={readOnlyFieldStyle}><span>Data de atribuição:</span> <span>{new Date(currentAssignment.assignedDate).toLocaleDateString('pt-PT')}</span></div>
                    <div style={readOnlyFieldStyle}><span>Equipamento:</span> <span>{detail.equipmentType} {detail.manufacturer} {detail.model}</span></div>
                    <div style={readOnlyFieldStyle}><span>Asset Tag:</span> <strong>{detail.assetTag}</strong></div>
                </div>

                <SelectField label="Condição na devolução *" value={returnCondition} onChange={setReturnCondition}
                    options={[
                        { value: 'GOOD', label: 'Em bom estado' },
                        { value: 'DAMAGED', label: 'Danificado' },
                        { value: 'NEEDS_REPAIR', label: 'Necessita conserto' },
                    ]} />

                {returnCondition !== 'GOOD' && (
                    <div style={{
                        padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca',
                        borderRadius: 8, color: '#991b1b', fontSize: '0.78rem'
                    }}>
                        <strong>⚠ Transferência bloqueada:</strong> Não é possível transferir o equipamento quando a condição indica dano ou necessidade de conserto. Faça a devolução normal e envie o equipamento para conserto.
                    </div>
                )}

                <div>
                    <TextArea label="Observações da devolução" value={returnNotes} onChange={setReturnNotes} rows={2} />
                    {needsReturnNotes && !returnNotes.trim() && (
                        <div style={{ color: '#dc2626', fontSize: '0.75rem', marginTop: 2 }}>
                            Informe uma observação quando o equipamento não estiver em bom estado.
                        </div>
                    )}
                </div>

                {/* ═══ Section B: Assignment ═══ */}
                <div style={{ ...sectionTitleStyle, marginTop: 4 }}>
                    <span style={{ color: '#3b82f6' }}>B)</span> Entrega ao novo utilizador
                </div>

                {/* User search */}
                <div style={{ position: 'relative' }} ref={dropdownRef}>
                    <label style={labelStyle}>Novo utilizador *</label>
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
                        <span style={{ position: 'absolute', right: 12, top: 32, fontSize: 11, color: '#999' }}>Pesquisando...</span>
                    )}
                    {selectedUser && (
                        <span style={{ fontSize: 11, color: '#16a34a', marginTop: 2 }}>✓ Utilizador do portal selecionado</span>
                    )}
                    {isSameUser && (
                        <span style={{ fontSize: 11, color: '#f59e0b', marginTop: 2 }}>
                            ⚠ Mesmo utilizador atual. {sameUserConfirmed ? 'Confirmado.' : 'Clique em "Transferir" novamente para confirmar.'}
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
                    label={selectedUser ? 'Email do novo utilizador (preenchido automaticamente)' : 'Email do novo utilizador *'}
                    value={form.newAssignedToEmail}
                    onChange={v => set('newAssignedToEmail', v)}
                />

                <Row>
                    <div style={{ flex: 1 }}>
                        <label style={labelStyle}>Nova planta</label>
                        <select
                            value={form.newAssignedToPlant}
                            onChange={e => set('newAssignedToPlant', e.target.value)}
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
                        <label style={labelStyle}>Novo departamento</label>
                        <select
                            value={form.newAssignedToDepartment}
                            onChange={e => set('newAssignedToDepartment', e.target.value)}
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

                <TextArea label="Observações da entrega" value={form.newAssignmentNotes} onChange={v => set('newAssignmentNotes', v)} rows={2} />

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn
                        label={isSameUser && !sameUserConfirmed ? 'Confirmar Transferência' : 'Transferir'}
                        loading={saving}
                        disabled={returnCondition !== 'GOOD'}
                    />
                </div>
            </form>
        </ModalWrapper>
    );
}
