import React, { useState, useEffect, useMemo } from 'react';
import { 
    Users, 
    Search, 
    Plus, 
    Edit2, 
    Key, 
    CheckCircle2, 
    XCircle, 
    ChevronRight, 
    Shield,
    MapPin,
    Building2,
    Lock,
    X,
    RefreshCw,
    Info
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { Link } from 'react-router-dom';
import { DropdownPortal } from '../../components/ui/DropdownPortal';
import { Tooltip } from '../../components/ui/Tooltip';
import { ROLE_DESCRIPTIONS } from '../../constants/roles';
import { Z_INDEX } from '../../constants/ui';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { StandardTable } from '../../components/ui/StandardTable';

interface UserListDto {
    id: string;
    fullName: string;
    email: string;
    isActive: boolean;
    roles: string[];
    plants: string[];
    departments: string[];
    canEdit: boolean;
}

export default function UserManagement() {
    const { user: currentUser } = useAuth();
    const [users, setUsers] = useState<UserListDto[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [searchTerm, setSearchTerm] = useState('');
    const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('active');

    // Master Data
    const [allRoles, setAllRoles] = useState<any[]>([]);
    const [allPlants, setAllPlants] = useState<any[]>([]);
    const [allDepts, setAllDepts] = useState<any[]>([]);

    // Drawer State
    const [isDrawerOpen, setIsDrawerOpen] = useState(false);
    const [drawerError, setDrawerError] = useState<string | null>(null);
    const [editingUser, setEditingUser] = useState<any>(null);
    const [formData, setFormData] = useState({
        fullName: '',
        email: '',
        isActive: true,
        roleIds: [] as number[],
        plantIds: [] as number[],
        departmentIds: [] as number[]
    });

    // Result Password Modal
    const [resultPassword, setResultPassword] = useState<string | null>(null);

    const isSystemAdmin = currentUser?.roles.includes('System Administrator');
    const isLocalManager = currentUser?.roles.includes('Local Manager');

    useEffect(() => {
        loadData();
    }, []);

    useEffect(() => {
        if (currentUser) {
            loadMasterData();
        }
    }, [currentUser]);

    async function loadData() {
        setIsLoading(true);
        setError(null);
        try {
            const data = await api.users.list(true);
            setUsers(data);
        } catch (err: any) {
            setError(err.message);
        } finally {
            setIsLoading(false);
        }
    }

    async function loadMasterData() {
        try {
            const [roles, plants, depts] = await Promise.all([
                api.lookups.getRoles(),
                api.lookups.getPlants(),
                api.lookups.getDepartments()
            ]);
            
            const managerAssignableRoles = [
                'Requester', 
                'Receiving', 
                'Viewer / Management', 
                'Import', 
                'Area Approver'
            ];

            const filteredRoles = isSystemAdmin
                ? roles
                : isLocalManager
                ? roles.filter((r: any) => managerAssignableRoles.includes(r.roleName))
                : roles.filter((r: any) => ![ 'System Administrator', 'Local Manager' ].includes(r.roleName));

            // Filter plants and departments for Local Manager scope (Case-insensitive Code match)
            const filteredPlants = isSystemAdmin
                ? plants
                : plants.filter((p: any) => 
                    currentUser?.plants?.some((code: string) => 
                        code.toLowerCase() === (p.code || '').toLowerCase()
                    )
                );

            const filteredDepts = isSystemAdmin
                ? depts
                : depts.filter((d: any) => 
                    currentUser?.departments?.some((code: string) => 
                        code.toLowerCase() === (d.code || '').toLowerCase()
                    )
                );

            setAllRoles(filteredRoles);
            setAllPlants(filteredPlants);
            setAllDepts(filteredDepts);
        } catch (err: any) {
            console.error('Failed to load master data', err);
        }
    }

    const filteredUsers = useMemo(() => {
        // Pre-map allowed names/codes for efficient lookup
        const allowedPlantNames = allPlants.map(p => (p.name || '').toLowerCase());
        const allowedDeptNames = allDepts.map(d => (d.name || '').toLowerCase());
        const allowedPlantCodes = allPlants.map(p => (p.code || '').toLowerCase());
        const allowedDeptCodes = allDepts.map(d => (d.code || '').toLowerCase());

        return users.filter(u => {
            // Scope Filter for Local Managers: must share at least one plant or department
            if (!isSystemAdmin && currentUser) {
                const hasPlantMatch = u.plants.some(p => {
                    const val = (p || '').toLowerCase();
                    return allowedPlantNames.includes(val) || allowedPlantCodes.includes(val);
                });

                const hasDeptMatch = u.departments.some(d => {
                    const val = (d || '').toLowerCase();
                    return allowedDeptNames.includes(val) || allowedDeptCodes.includes(val);
                });

                if (!hasPlantMatch && !hasDeptMatch) return false;
            }

            const matchesSearch = u.fullName.toLowerCase().includes(searchTerm.toLowerCase()) || 
                                 u.email.toLowerCase().includes(searchTerm.toLowerCase());
            const matchesStatus = statusFilter === 'all' || 
                                 (statusFilter === 'active' && u.isActive) || 
                                 (statusFilter === 'inactive' && !u.isActive);
            return matchesSearch && matchesStatus;
        });
    }, [users, searchTerm, statusFilter, isSystemAdmin, currentUser, allPlants, allDepts]);

    async function handleOpenCreate() {
        setEditingUser(null);
        setDrawerError(null);
        setFormData({
            fullName: '',
            email: '',
            isActive: true,
            roleIds: [],
            plantIds: [],
            departmentIds: []
        });
        setIsDrawerOpen(true);
    }

    async function handleOpenEdit(userId: string) {
        try {
            const userDetails = await api.users.get(userId);
            setEditingUser(userDetails);
            setDrawerError(null);
            setFormData({
                fullName: userDetails.fullName,
                email: userDetails.email,
                isActive: userDetails.isActive,
                roleIds: userDetails.roleIds,
                plantIds: userDetails.plantIds,
                departmentIds: userDetails.departmentIds
            });
            setIsDrawerOpen(true);
        } catch (err: any) {
            alert('Erro ao carregar detalhes: ' + err.message);
        }
    }

    async function handleSave(e: React.FormEvent) {
        e.preventDefault();
        setDrawerError(null);
        try {
            if (editingUser) {
                await api.users.update(editingUser.id, formData);
                setIsDrawerOpen(false);
                loadData();
            } else {
                const res = await api.users.create(formData);
                setResultPassword(res.newPassword);
                setIsDrawerOpen(false);
                loadData();
            }
        } catch (err: any) {
            setDrawerError(err.message);
        }
    }

    async function handleResetPassword(userId: string, email: string) {
        if (!confirm(`Tem a certeza que deseja repor a palavra-passe do utilizador ${email}?`)) return;
        try {
            const res = await api.users.resetPassword(userId);
            setResultPassword(res.newPassword);
        } catch (err: any) {
            alert('Erro ao repor: ' + err.message);
        }
    }

    const toggleId = (list: number[], id: number) => {
        return list.includes(id) ? list.filter(i => i !== id) : [...list, id];
    };

    // ─── Styles ─────────────────────────────────────────────────────────────────
    const s = {
        drawerOverlay: { position: 'fixed' as const, inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', justifyContent: 'flex-end', zIndex: Z_INDEX.DRAWER },
        drawer: { background: 'var(--color-bg-surface)', width: '100%', maxWidth: 560, height: '100vh', display: 'flex', flexDirection: 'column' as const, borderLeft: '4px solid var(--color-primary)', boxShadow: '-8px 0 24px rgba(0,0,0,0.2)' },
        drawerHeader: { padding: '24px', background: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-primary)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
        drawerBody: { flex: 1, overflowY: 'auto' as const, padding: '32px', display: 'flex', flexDirection: 'column' as const, gap: '24px' },
        drawerFooter: { padding: '20px 32px', borderTop: '2px solid var(--color-border)', display: 'flex', justifyContent: 'flex-end', gap: 12, background: 'var(--color-bg-page)' },
        input: { padding: '10px 12px', border: '2px solid var(--color-border)', fontSize: '0.85rem', fontWeight: 600, outline: 'none', background: 'var(--color-bg-page)' } as React.CSSProperties,
        labelSm: { fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: 4, display: 'block' },
        badge: { display: 'inline-flex', alignItems: 'center', gap: 4, padding: '2px 8px', fontSize: '10px', fontWeight: 800, textTransform: 'uppercase' as const, border: '1px solid currentColor' }
    };

    // Columns moved directly to render function for StandardTable

    return (
        <PageContainer>
            {/* Header */}
            <PageHeader
                title="Gestão de Utilizadores"
                icon={<Users size={32} strokeWidth={2.5} />}
                subtitle={
                    <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                        <Link to="/admin" style={{ color: 'inherit', textDecoration: 'none' }}>Administração</Link>
                        <ChevronRight size={12} />
                        Gestão de Utilizadores
                    </span>
                }
                actions={
                    <button onClick={handleOpenCreate} className="btn btn-primary" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <Plus size={18} /> Criar Utilizador
                    </button>
                }
            />

            {/* Filters */}
            <SearchFilterBar
                searchValue={searchTerm}
                onSearchChange={setSearchTerm}
                searchPlaceholder="Pesquisar por nome ou e-mail..."
                tabs={[
                    { id: 'all', label: 'Todos' },
                    { id: 'active', label: 'Ativos' },
                    { id: 'inactive', label: 'Inativos' }
                ]}
                activeTabId={statusFilter}
                onTabChange={(id) => setStatusFilter(id as any)}
                actions={
                    <button onClick={loadData} style={{ background: 'var(--color-bg-page)', border: '1px solid var(--color-border)', cursor: 'pointer', color: 'var(--color-text-muted)', padding: '8px 12px', borderRadius: 'var(--radius-md)' }}>
                        <RefreshCw size={18} className={isLoading ? 'animate-spin' : ''} />
                    </button>
                }
            />

            {/* Error Banner */}
            {error && (
                <div style={{ background: 'var(--color-status-red)18', border: '2px solid var(--color-status-red)', padding: '12px 16px', color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 12 }}>
                    <XCircle size={18} />
                    {error}
                </div>
            )}

            {/* Table */}
            <StandardTable
                loading={isLoading}
                loadingState={<div style={{ padding: '64px', textAlign: 'center', color: 'var(--color-text-muted)' }}>A carregar utilizadores...</div>}
                isEmpty={filteredUsers.length === 0}
                emptyState={
                    <div style={{ padding: '64px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                        {searchTerm ? 'Nenhum utilizador encontrado com estes filtros.' : 'Sem utilizadores listados.'}
                    </div>
                }
            >
                <thead>
                    <tr style={{ borderBottom: '2px solid var(--color-border)', textAlign: 'left', color: 'var(--color-text-muted)' }}>
                        <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Utilizador / E-mail</th>
                        <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', textAlign: 'center' }}>Estado</th>
                        <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Funções</th>
                        <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Escopo de Atuação</th>
                        <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', textAlign: 'right', width: '100px' }}>Ações</th>
                    </tr>
                </thead>
                <tbody style={{ backgroundColor: 'white' }}>
                    {filteredUsers.map(user => (
                        <tr key={user.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                            <td style={{ padding: '16px' }}>
                                <div>
                                    <div style={{ fontWeight: 800, fontSize: '0.95rem' }}>{user.fullName}</div>
                                    <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', fontWeight: 600 }}>{user.email}</div>
                                </div>
                            </td>
                            <td style={{ padding: '16px', textAlign: 'center' }}>
                                {user.isActive ? (
                                    <span style={{ ...s.badge, color: 'var(--color-status-green)', background: 'var(--color-status-green)10' }}>
                                        <CheckCircle2 size={12} /> Ativo
                                    </span>
                                ) : (
                                    <span style={{ ...s.badge, color: 'var(--color-status-red)', background: 'var(--color-status-red)10' }}>
                                        <XCircle size={12} /> Inativo
                                    </span>
                                )}
                            </td>
                            <td style={{ padding: '16px' }}>
                                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                                    {user.roles.map(role => (
                                        <span key={role} style={{ ...s.badge, color: 'var(--color-primary)', background: 'var(--color-primary)10', fontSize: '9px' }}>
                                            <Shield size={10} /> {role}
                                        </span>
                                    ))}
                                </div>
                            </td>
                            <td style={{ padding: '16px' }}>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                                        {user.plants.length > 0 ? user.plants.map(p => (
                                            <span key={p} style={{ fontSize: '10px', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 4, background: 'var(--color-bg-page)', padding: '2px 6px', border: '1px solid var(--color-border)' }}>
                                                <MapPin size={10} /> {p}
                                            </span>
                                        )) : <span style={{ fontSize: '10px', fontStyle: 'italic', color: 'var(--color-text-muted)' }}>Nenhuma Planta</span>}
                                    </div>
                                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                                        {user.departments.length > 0 ? user.departments.map(d => (
                                            <span key={d} style={{ fontSize: '10px', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 4, background: 'var(--color-bg-page)', padding: '2px 6px', border: '1px solid var(--color-border)' }}>
                                                <Building2 size={10} /> {d}
                                            </span>
                                        )) : <span style={{ fontSize: '10px', fontStyle: 'italic', color: 'var(--color-text-muted)' }}>Nenhum Departamento</span>}
                                    </div>
                                </div>
                            </td>
                            <td style={{ padding: '16px', textAlign: 'right' }}>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 4 }}>
                                    {user.canEdit ? (
                                        <>
                                            <button onClick={() => handleOpenEdit(user.id)} title="Editar" style={{ padding: 6, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer' }}>
                                                <Edit2 size={18} />
                                            </button>
                                            <button onClick={() => handleResetPassword(user.id, user.email)} title="Repor Palavra-passe" style={{ padding: 6, color: 'var(--color-status-orange)', background: 'none', border: 'none', cursor: 'pointer' }}>
                                                <Key size={18} />
                                            </button>
                                        </>
                                    ) : (
                                        <div title="Sem permissão (Fora do escopo)" style={{ opacity: 0.3, padding: 6 }}>
                                            <Lock size={18} />
                                        </div>
                                    )}
                                </div>
                            </td>
                        </tr>
                    ))}
                </tbody>
            </StandardTable>

            {/* Edit / Create Right-Side Drawer */}
            {isDrawerOpen && (
                <DropdownPortal>
                    <div style={s.drawerOverlay} onClick={e => e.target === e.currentTarget && setIsDrawerOpen(false)}>
                        <div style={s.drawer} className="animate-in slide-in-from-right duration-300">
                            <div style={s.drawerHeader}>
                                <h2 style={{ fontSize: '1.25rem', fontWeight: 900, textTransform: 'uppercase', margin: 0, color: 'var(--color-primary)' }}>
                                    {editingUser ? 'Editar Utilizador' : 'Novo Utilizador'}
                                </h2>
                                <button onClick={() => setIsDrawerOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer', opacity: 0.5 }}><X size={24} /></button>
                            </div>
                            
                            <form style={s.drawerBody} onSubmit={handleSave}>
                                {drawerError && (
                                    <div style={{ background: 'var(--color-status-red)18', border: '2px solid var(--color-status-red)', padding: '12px 16px', color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 12 }}>
                                        <XCircle size={18} />
                                        {drawerError}
                                    </div>
                                )}
                                <section style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                                    <label style={s.labelSm}>Identificação Base</label>
                                    <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 16 }}>
                                        <div>
                                            <label style={{ fontSize: 11, fontWeight: 700, marginBottom: 4, display: 'block' }}>Nome Completo</label>
                                            <input 
                                                required style={{ ...s.input, width: '100%' }}
                                                value={formData.fullName}
                                                onChange={e => setFormData({...formData, fullName: e.target.value})}
                                            />
                                        </div>
                                        <div>
                                            <label style={{ fontSize: 11, fontWeight: 700, marginBottom: 4, display: 'block' }}>E-mail Corporativo</label>
                                            <input 
                                                required type="email" disabled={!!editingUser}
                                                style={{ ...s.input, width: '100%', opacity: editingUser ? 0.6 : 1 }}
                                                value={formData.email}
                                                onChange={e => setFormData({...formData, email: e.target.value})}
                                            />
                                        </div>
                                    </div>
                                </section>

                                <section style={{ padding: 16, border: '2px solid var(--color-border)', background: 'var(--color-bg-page)' }}>
                                    <label style={s.labelSm}>Estado da Conta</label>
                                    <div style={{ display: 'flex', gap: 24, marginTop: 12 }}>
                                        <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontWeight: 700, fontSize: 13 }}>
                                            <input type="radio" checked={formData.isActive} onChange={() => setFormData({...formData, isActive: true})} /> Ativo
                                        </label>
                                        <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontWeight: 700, fontSize: 13 }}>
                                            <input type="radio" checked={!formData.isActive} onChange={() => setFormData({...formData, isActive: false})} /> Inativo
                                        </label>
                                    </div>
                                </section>

                                <section>
                                    <label style={s.labelSm}>Funções e Permissões</label>
                                    <p style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginBottom: 8, fontWeight: 600, lineHeight: 1.4 }}>
                                        Selecione uma ou mais funções para definir o que este utilizador poderá fazer no sistema. Passe o mouse no ícone de informação para ver a descrição de cada função.
                                    </p>
                                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 8, marginTop: 8 }}>
                                        {allRoles.map((role, idx) => (
                                            <label key={role.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, padding: 10, border: '1px solid var(--color-border)', cursor: 'pointer', fontSize: 11, fontWeight: 700, background: formData.roleIds.includes(role.id) ? 'var(--color-primary)08' : 'var(--color-bg-page)' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                                    <input type="checkbox" checked={formData.roleIds.includes(role.id)} onChange={() => setFormData({...formData, roleIds: toggleId(formData.roleIds, role.id)})} />
                                                    {role.roleName}
                                                </div>
                                                {ROLE_DESCRIPTIONS[role.roleName] && (
                                                    <Tooltip 
                                                        content={ROLE_DESCRIPTIONS[role.roleName]}
                                                        side="top"
                                                        align={idx % 2 === 0 ? 'start' : 'end'}
                                                    >
                                                        <div style={{ display: 'flex', alignItems: 'center', padding: '2px' }} title="Clique ou passe o mouse para mais detalhes">
                                                            <Info size={14} style={{ opacity: 0.5, cursor: 'help' }} />
                                                        </div>
                                                    </Tooltip>
                                                )}
                                            </label>
                                        ))}
                                    </div>
                                </section>

                                <section style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24 }}>
                                    <div>
                                        <label style={s.labelSm}>Escopo: Plantas</label>
                                        <div style={{ maxHeight: 200, overflowY: 'auto', border: '1px solid var(--color-border)', padding: 8, display: 'flex', flexDirection: 'column', gap: 4 }}>
                                            {allPlants.map(p => (
                                                <label key={p.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 11, fontWeight: 600, cursor: 'pointer' }}>
                                                    <input type="checkbox" checked={formData.plantIds.includes(p.id)} onChange={() => setFormData({...formData, plantIds: toggleId(formData.plantIds, p.id)})} />
                                                    {p.code} - {p.name}
                                                </label>
                                            ))}
                                        </div>
                                    </div>
                                    <div>
                                        <label style={s.labelSm}>Escopo: Departamentos</label>
                                        <div style={{ maxHeight: 200, overflowY: 'auto', border: '1px solid var(--color-border)', padding: 8, display: 'flex', flexDirection: 'column', gap: 4 }}>
                                            {allDepts.map(d => (
                                                <label key={d.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 11, fontWeight: 600, cursor: 'pointer' }}>
                                                    <input type="checkbox" checked={formData.departmentIds.includes(d.id)} onChange={() => setFormData({...formData, departmentIds: toggleId(formData.departmentIds, d.id)})} />
                                                    {d.code} - {d.name}
                                                </label>
                                            ))}
                                        </div>
                                    </div>
                                </section>
                            </form>

                            <div style={s.drawerFooter}>
                                <button onClick={() => setIsDrawerOpen(false)} style={{ background: 'none', border: 'none', fontWeight: 800, textTransform: 'uppercase', fontSize: 12, cursor: 'pointer', color: 'var(--color-text-muted)' }}>Cancelar</button>
                                <button onClick={handleSave} className="btn btn-primary">
                                    {editingUser ? 'Guardar Alterações' : 'Criar Utilizador'}
                                </button>
                            </div>
                        </div>
                    </div>
                </DropdownPortal>
            )}

            {/* Temp Password Overlay */}
            {resultPassword && (
                <DropdownPortal>
                    <div style={{ ...s.drawerOverlay, background: 'rgba(0,0,0,0.85)', backdropFilter: 'blur(8px)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        <div style={{ background: 'var(--color-bg-surface)', padding: 48, border: '4px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', maxWidth: 440, textAlign: 'center' }}>
                            <div style={{ color: 'var(--color-primary)', marginBottom: 24 }}><Key size={64} style={{ margin: '0 auto' }} /></div>
                            <h2 style={{ fontSize: '1.5rem', fontWeight: 900, marginBottom: 12 }}>AUTENTICAÇÃO GERADA</h2>
                            <p style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontWeight: 700, marginBottom: 24 }}>Copiue e guarde. Esta informação não será exibida novamente.</p>
                            <div style={{ padding: 16, background: 'var(--color-bg-page)', border: '2px dashed var(--color-primary)', fontSize: '1.5rem', fontWeight: 900, letterSpacing: '0.2em', margin: '0 0 32px 0' }}>{resultPassword}</div>
                            <button 
                                className="btn btn-primary" style={{ width: '100%', padding: 16 }}
                                onClick={async () => { await navigator.clipboard.writeText(resultPassword); setResultPassword(null); }}
                            >
                                FECHAR E COPIAR
                            </button>
                        </div>
                    </div>
                </DropdownPortal>
            )}
        </PageContainer>
    );
}
