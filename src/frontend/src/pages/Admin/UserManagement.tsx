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
    RefreshCw
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { Link } from 'react-router-dom';

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

    useEffect(() => {
        loadData();
        loadMasterData();
    }, []);

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
            
            // Filter roles for Local Manager
            const filteredRoles = isSystemAdmin 
                ? roles 
                : roles.filter((r: any) => ![ 'System Administrator', 'Local Manager' ].includes(r.roleName));

            setAllRoles(filteredRoles);
            setAllPlants(plants);
            setAllDepts(depts);
        } catch (err: any) {
            console.error('Failed to load master data', err);
        }
    }

    const filteredUsers = useMemo(() => {
        return users.filter(u => {
            const matchesSearch = u.fullName.toLowerCase().includes(searchTerm.toLowerCase()) || 
                                 u.email.toLowerCase().includes(searchTerm.toLowerCase());
            const matchesStatus = statusFilter === 'all' || 
                                 (statusFilter === 'active' && u.isActive) || 
                                 (statusFilter === 'inactive' && !u.isActive);
            return matchesSearch && matchesStatus;
        });
    }, [users, searchTerm, statusFilter]);

    async function handleOpenCreate() {
        setEditingUser(null);
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
            alert('Erro ao guardar: ' + err.message);
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
        page: { display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', minWidth: 0 } as React.CSSProperties,
        header: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', width: '100%', minWidth: 0 } as React.CSSProperties,
        breadcrumbs: { fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: '8px', display: 'flex', alignItems: 'center', gap: 4 },
        title: { margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', fontFamily: 'var(--font-family-display)', textTransform: 'uppercase' as const, letterSpacing: '-0.02em', display: 'flex', alignItems: 'center', gap: 16 } as React.CSSProperties,
        filtersCard: { backgroundColor: 'var(--color-bg-surface)', padding: '16px', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-primary)', display: 'flex', flexWrap: 'wrap' as const, gap: '16px', alignItems: 'center' } as React.CSSProperties,
        input: { padding: '10px 12px', border: '2px solid var(--color-border)', fontSize: '0.85rem', fontWeight: 600, outline: 'none', background: 'var(--color-bg-page)' } as React.CSSProperties,
        select: { padding: '10px 12px', border: '2px solid var(--color-border)', fontSize: '0.85rem', background: '#fff', fontWeight: 600, outline: 'none' } as React.CSSProperties,
        labelSm: { fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: 4, display: 'block' },
        tableContainer: { overflowX: 'auto' as const, border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', background: '#fff' },
        table: { width: '100%', borderCollapse: 'collapse' as const, fontSize: '0.85rem' },
        th: { padding: '12px 16px', background: 'var(--color-primary)', color: '#fff', fontWeight: 800, textAlign: 'left' as const, fontSize: '0.75rem', letterSpacing: '0.05em', textTransform: 'uppercase' as const, border: '1px solid var(--color-primary)' },
        td: { padding: '12px 16px', borderBottom: '1px solid var(--color-border)', verticalAlign: 'middle' as const },
        trHover: { background: 'var(--color-bg-page)' },
        badge: { display: 'inline-flex', alignItems: 'center', gap: 4, padding: '2px 8px', fontSize: '10px', fontWeight: 800, textTransform: 'uppercase' as const, border: '1px solid currentColor' },
        drawerOverlay: { position: 'fixed' as const, inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', justifyContent: 'flex-end', zIndex: 1000 },
        drawer: { background: '#fff', width: '100%', maxWidth: 560, height: '100vh', display: 'flex', flexDirection: 'column' as const, borderLeft: '4px solid var(--color-primary)', boxShadow: '-8px 0 24px rgba(0,0,0,0.2)' },
        drawerHeader: { padding: '24px', background: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-primary)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
        drawerBody: { flex: 1, overflowY: 'auto' as const, padding: '32px', display: 'flex', flexDirection: 'column' as const, gap: '24px' },
        drawerFooter: { padding: '20px 32px', borderTop: '2px solid var(--color-border)', display: 'flex', justifyContent: 'flex-end', gap: 12, background: 'var(--color-bg-page)' }
    };

    return (
        <div style={s.page}>
            {/* Header */}
            <div style={s.header}>
                <div>
                    <div style={s.breadcrumbs}>
                        <Link to="/admin" style={{ color: 'inherit' }}>Administração</Link>
                        <ChevronRight size={12} />
                        <span>Gestão de Utilizadores</span>
                    </div>
                    <h1 style={s.title}>
                        <Users size={32} strokeWidth={2.5} /> Gestão de Utilizadores
                    </h1>
                </div>
                <button onClick={handleOpenCreate} className="btn btn-primary" style={{ marginBottom: 4 }}>
                    <Plus size={18} /> Criar Utilizador
                </button>
            </div>

            {/* Filters */}
            <div style={s.filtersCard}>
                <div style={{ flex: 1, minWidth: '300px', position: 'relative' }}>
                    <Search size={16} style={{ position: 'absolute', left: 12, top: 12, opacity: 0.5 }} />
                    <input 
                        style={{ ...s.input, width: '100%', paddingLeft: 40 }}
                        placeholder="Pesquisar por nome ou e-mail..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                    />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                    <span style={s.labelSm}>Estado:</span>
                    <select 
                        style={s.select}
                        value={statusFilter}
                        onChange={(e: any) => setStatusFilter(e.target.value)}
                    >
                        <option value="all">Todos</option>
                        <option value="active">Ativos</option>
                        <option value="inactive">Inativos</option>
                    </select>
                </div>
                <button onClick={loadData} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-primary)', padding: 8 }}>
                    <RefreshCw size={18} className={isLoading ? 'animate-spin' : ''} />
                </button>
            </div>

            {/* Error Banner */}
            {error && (
                <div style={{ background: 'var(--color-status-red)18', border: '2px solid var(--color-status-red)', padding: '12px 16px', color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 12 }}>
                    <XCircle size={18} />
                    {error}
                </div>
            )}

            {/* Table */}
            <div style={s.tableContainer}>
                <table style={s.table}>
                    <thead>
                        <tr>
                            <th style={s.th}>Utilizador / E-mail</th>
                            <th style={{ ...s.th, textAlign: 'center' }}>Estado</th>
                            <th style={s.th}>Funções</th>
                            <th style={s.th}>Escopo de Atuação</th>
                            <th style={{ ...s.th, width: 80, textAlign: 'right' }}>Ações</th>
                        </tr>
                    </thead>
                    <tbody>
                        {isLoading && users.length === 0 ? (
                            <tr><td colSpan={5} style={{ ...s.td, textAlign: 'center', padding: 48, color: 'var(--color-text-muted)', fontWeight: 600 }}>A carregar utilizadores...</td></tr>
                        ) : filteredUsers.length === 0 ? (
                            <tr><td colSpan={5} style={{ ...s.td, textAlign: 'center', padding: 48, color: 'var(--color-text-muted)', fontWeight: 600 }}>Nenhum utilizador encontrado.</td></tr>
                        ) : filteredUsers.map((user, i) => (
                            <tr key={user.id} style={i % 2 === 0 ? {} : s.trHover}>
                                <td style={s.td}>
                                    <div style={{ fontWeight: 800, fontSize: '0.95rem' }}>{user.fullName}</div>
                                    <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', fontWeight: 600 }}>{user.email}</div>
                                </td>
                                <td style={{ ...s.td, textAlign: 'center' }}>
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
                                <td style={s.td}>
                                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                                        {user.roles.map(role => (
                                            <span key={role} style={{ ...s.badge, color: 'var(--color-primary)', background: 'var(--color-primary)10', fontSize: '9px' }}>
                                                <Shield size={10} /> {role}
                                            </span>
                                        ))}
                                    </div>
                                </td>
                                <td style={s.td}>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                                            {user.plants.length > 0 ? user.plants.map(p => (
                                                <span key={p} style={{ fontSize: '10px', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 4, background: '#f3f4f6', padding: '2px 6px', border: '1px solid #d1d5db' }}>
                                                    <MapPin size={10} /> {p}
                                                </span>
                                            )) : <span style={{ fontSize: '10px', fontStyle: 'italic', color: '#9ca3af' }}>Nenhuma Planta</span>}
                                        </div>
                                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                                            {user.departments.length > 0 ? user.departments.map(d => (
                                                <span key={d} style={{ fontSize: '10px', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 4, background: '#f8fafc', padding: '2px 6px', border: '1px solid #cbd5e1' }}>
                                                    <Building2 size={10} /> {d}
                                                </span>
                                            )) : <span style={{ fontSize: '10px', fontStyle: 'italic', color: '#9ca3af' }}>Nenhum Departamento</span>}
                                        </div>
                                    </div>
                                </td>
                                <td style={{ ...s.td, textAlign: 'right' }}>
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
                </table>
            </div>

            {/* Edit / Create Right-Side Drawer */}
            {isDrawerOpen && (
                <div style={s.drawerOverlay} onClick={e => e.target === e.currentTarget && setIsDrawerOpen(false)}>
                    <div style={s.drawer} className="animate-in slide-in-from-right duration-300">
                        <div style={s.drawerHeader}>
                            <h2 style={{ fontSize: '1.25rem', fontWeight: 900, textTransform: 'uppercase', margin: 0, color: 'var(--color-primary)' }}>
                                {editingUser ? 'Editar Utilizador' : 'Novo Utilizador'}
                            </h2>
                            <button onClick={() => setIsDrawerOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer', opacity: 0.5 }}><X size={24} /></button>
                        </div>
                        
                        <form style={s.drawerBody} onSubmit={handleSave}>
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
                                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 8, marginTop: 8 }}>
                                    {allRoles.map(role => (
                                        <label key={role.id} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: 10, border: '1px solid var(--color-border)', cursor: 'pointer', fontSize: 11, fontWeight: 700, background: formData.roleIds.includes(role.id) ? 'var(--color-primary)08' : '#fff' }}>
                                            <input type="checkbox" checked={formData.roleIds.includes(role.id)} onChange={() => setFormData({...formData, roleIds: toggleId(formData.roleIds, role.id)})} />
                                            {role.roleName}
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
            )}

            {/* Temp Password Overlay */}
            {resultPassword && (
                <div style={{ ...s.drawerOverlay, background: 'rgba(0,0,0,0.85)', backdropFilter: 'blur(8px)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <div style={{ background: '#fff', padding: 48, border: '4px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', maxWidth: 440, textAlign: 'center' }}>
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
            )}
        </div>
    );
}
