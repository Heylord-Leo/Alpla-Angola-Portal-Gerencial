import React, { useState, useEffect } from 'react';
import { Plus, ToggleLeft, ToggleRight } from 'lucide-react';
import { itEquipmentCatalogApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, ErrorBox, inputStyle } from './EquipmentFormModal';

type CatalogTab = 'manufacturers' | 'models' | 'processors' | 'memory';

interface Props {
    onClose: () => void;
}

export function ManageEquipmentCatalogsModal({ onClose }: Props) {
    const [tab, setTab] = useState<CatalogTab>('manufacturers');

    return (
        <ModalWrapper title="Gerir Catálogos de Equipamentos" onClose={onClose} width={720}>
            <div style={{ display: 'flex', gap: 0, marginBottom: 16, borderBottom: '2px solid var(--color-border)' }}>
                {([
                    { key: 'manufacturers' as const, label: 'Fabricantes' },
                    { key: 'models' as const, label: 'Modelos' },
                    { key: 'processors' as const, label: 'Processadores' },
                    { key: 'memory' as const, label: 'Memória' },
                ]).map(t => (
                    <button
                        key={t.key}
                        onClick={() => setTab(t.key)}
                        style={{
                            padding: '8px 16px', border: 'none', cursor: 'pointer', fontWeight: 600,
                            fontSize: '0.82rem', background: 'none',
                            color: tab === t.key ? 'var(--color-primary)' : 'var(--color-text-muted)',
                            borderBottom: tab === t.key ? '2px solid var(--color-primary)' : '2px solid transparent',
                            marginBottom: -2, transition: 'all 0.2s'
                        }}
                    >
                        {t.label}
                    </button>
                ))}
            </div>
            {tab === 'manufacturers' && <ManufacturersTab />}
            {tab === 'models' && <ModelsTab />}
            {tab === 'processors' && <ProcessorsTab />}
            {tab === 'memory' && <MemoryTab />}
        </ModalWrapper>
    );
}

// ═══════════════════════════════════════════════════════════════
//  MANUFACTURERS TAB
// ═══════════════════════════════════════════════════════════════
function ManufacturersTab() {
    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [newName, setNewName] = useState('');
    const [error, setError] = useState('');

    const load = async () => {
        try {
            setLoading(true);
            const data = await itEquipmentCatalogApi.manufacturers.list();
            setItems(data);
        } catch { /* empty */ } finally { setLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const handleCreate = async () => {
        if (!newName.trim()) return;
        try {
            setError('');
            await itEquipmentCatalogApi.manufacturers.create({ name: newName.trim() });
            setNewName('');
            await load();
        } catch (err: any) {
            setError(err.message || 'Erro ao criar.');
        }
    };

    const handleToggle = async (id: string) => {
        await itEquipmentCatalogApi.manufacturers.toggle(id);
        await load();
    };

    return (
        <div>
            {error && <ErrorBox msg={error} />}
            <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
                <input value={newName} onChange={e => setNewName(e.target.value)} placeholder="Novo fabricante..."
                    style={{ ...inputStyle, flex: 1 }}
                    onKeyDown={e => e.key === 'Enter' && handleCreate()}
                />
                <button onClick={handleCreate} style={addBtnStyle}><Plus size={14} /> Adicionar</button>
            </div>
            <CatalogTable
                items={items}
                loading={loading}
                columns={['Nome', 'Estado']}
                renderRow={(item) => (
                    <>
                        <td style={cellStyle}>{item.name}</td>
                        <td style={cellStyle}>
                            <ToggleButton active={item.isActive} onClick={() => handleToggle(item.id)} />
                        </td>
                    </>
                )}
            />
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════
//  MODELS TAB
// ═══════════════════════════════════════════════════════════════
function ModelsTab() {
    const [items, setItems] = useState<any[]>([]);
    const [manufacturers, setManufacturers] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [newName, setNewName] = useState('');
    const [newMfrId, setNewMfrId] = useState('');
    const [newTypeCode, setNewTypeCode] = useState('');
    const [error, setError] = useState('');

    const load = async () => {
        try {
            setLoading(true);
            const [models, mfrs] = await Promise.all([
                itEquipmentCatalogApi.models.list(),
                itEquipmentCatalogApi.manufacturers.list(true)
            ]);
            setItems(models);
            setManufacturers(mfrs);
        } catch { /* empty */ } finally { setLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const handleCreate = async () => {
        if (!newName.trim() || !newMfrId) return;
        try {
            setError('');
            await itEquipmentCatalogApi.models.create({
                name: newName.trim(),
                manufacturerId: newMfrId,
                equipmentTypeCode: newTypeCode || undefined
            });
            setNewName('');
            setNewTypeCode('');
            await load();
        } catch (err: any) {
            setError(err.message || 'Erro ao criar.');
        }
    };

    const handleToggle = async (id: string) => {
        await itEquipmentCatalogApi.models.toggle(id);
        await load();
    };

    return (
        <div>
            {error && <ErrorBox msg={error} />}
            <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
                <select value={newMfrId} onChange={e => setNewMfrId(e.target.value)} style={{ ...inputStyle, flex: '0 0 180px' }}>
                    <option value="">Fabricante...</option>
                    {manufacturers.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
                <input value={newName} onChange={e => setNewName(e.target.value)} placeholder="Nome do modelo..."
                    style={{ ...inputStyle, flex: 1 }}
                />
                <input value={newTypeCode} onChange={e => setNewTypeCode(e.target.value)} placeholder="Tipo (ex: LAPTOP)"
                    style={{ ...inputStyle, flex: '0 0 130px' }}
                />
                <button onClick={handleCreate} style={addBtnStyle}><Plus size={14} /> Adicionar</button>
            </div>
            <CatalogTable
                items={items}
                loading={loading}
                columns={['Fabricante', 'Modelo', 'Tipo', 'Estado']}
                renderRow={(item) => (
                    <>
                        <td style={cellStyle}>{item.manufacturerName}</td>
                        <td style={cellStyle}>{item.name}</td>
                        <td style={cellStyle}><span style={badgeStyle}>{item.equipmentTypeCode || '—'}</span></td>
                        <td style={cellStyle}>
                            <ToggleButton active={item.isActive} onClick={() => handleToggle(item.id)} />
                        </td>
                    </>
                )}
            />
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════
//  PROCESSORS TAB
// ═══════════════════════════════════════════════════════════════
function ProcessorsTab() {
    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [newName, setNewName] = useState('');
    const [error, setError] = useState('');

    const load = async () => {
        try {
            setLoading(true);
            const data = await itEquipmentCatalogApi.processors.list();
            setItems(data);
        } catch { /* empty */ } finally { setLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const handleCreate = async () => {
        if (!newName.trim()) return;
        try {
            setError('');
            await itEquipmentCatalogApi.processors.create({ name: newName.trim() });
            setNewName('');
            await load();
        } catch (err: any) {
            setError(err.message || 'Erro ao criar.');
        }
    };

    const handleToggle = async (id: string) => {
        await itEquipmentCatalogApi.processors.toggle(id);
        await load();
    };

    return (
        <div>
            {error && <ErrorBox msg={error} />}
            <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
                <input value={newName} onChange={e => setNewName(e.target.value)} placeholder="Novo processador..."
                    style={{ ...inputStyle, flex: 1 }}
                    onKeyDown={e => e.key === 'Enter' && handleCreate()}
                />
                <button onClick={handleCreate} style={addBtnStyle}><Plus size={14} /> Adicionar</button>
            </div>
            <CatalogTable
                items={items}
                loading={loading}
                columns={['Nome', 'Estado']}
                renderRow={(item) => (
                    <>
                        <td style={cellStyle}>{item.name}</td>
                        <td style={cellStyle}>
                            <ToggleButton active={item.isActive} onClick={() => handleToggle(item.id)} />
                        </td>
                    </>
                )}
            />
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════
//  MEMORY TAB
// ═══════════════════════════════════════════════════════════════
function MemoryTab() {
    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [newName, setNewName] = useState('');
    const [newGb, setNewGb] = useState('');
    const [error, setError] = useState('');

    const load = async () => {
        try {
            setLoading(true);
            const data = await itEquipmentCatalogApi.memoryOptions.list();
            setItems(data);
        } catch { /* empty */ } finally { setLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const handleCreate = async () => {
        if (!newName.trim()) return;
        try {
            setError('');
            await itEquipmentCatalogApi.memoryOptions.create({
                displayName: newName.trim(),
                valueInGb: newGb ? parseInt(newGb) : undefined
            });
            setNewName('');
            setNewGb('');
            await load();
        } catch (err: any) {
            setError(err.message || 'Erro ao criar.');
        }
    };

    const handleToggle = async (id: string) => {
        await itEquipmentCatalogApi.memoryOptions.toggle(id);
        await load();
    };

    return (
        <div>
            {error && <ErrorBox msg={error} />}
            <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
                <input value={newName} onChange={e => setNewName(e.target.value)} placeholder="Ex: 16 GB"
                    style={{ ...inputStyle, flex: 1 }}
                    onKeyDown={e => e.key === 'Enter' && handleCreate()}
                />
                <input value={newGb} onChange={e => setNewGb(e.target.value)} placeholder="GB (num)"
                    type="number"
                    style={{ ...inputStyle, flex: '0 0 100px' }}
                />
                <button onClick={handleCreate} style={addBtnStyle}><Plus size={14} /> Adicionar</button>
            </div>
            <CatalogTable
                items={items}
                loading={loading}
                columns={['Nome', 'GB', 'Estado']}
                renderRow={(item) => (
                    <>
                        <td style={cellStyle}>{item.displayName}</td>
                        <td style={cellStyle}>{item.valueInGb ?? '—'}</td>
                        <td style={cellStyle}>
                            <ToggleButton active={item.isActive} onClick={() => handleToggle(item.id)} />
                        </td>
                    </>
                )}
            />
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════
//  SHARED HELPERS
// ═══════════════════════════════════════════════════════════════
function CatalogTable({ items, loading, columns, renderRow }: {
    items: any[];
    loading: boolean;
    columns: string[];
    renderRow: (item: any) => React.ReactNode;
}) {
    if (loading) return <div style={{ textAlign: 'center', padding: 24, color: 'var(--color-text-muted)' }}>Carregando...</div>;
    if (items.length === 0) return <div style={{ textAlign: 'center', padding: 24, color: 'var(--color-text-muted)' }}>Nenhum item cadastrado.</div>;

    return (
        <div style={{ maxHeight: 340, overflowY: 'auto', borderRadius: 8, border: '1px solid var(--color-border)' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem' }}>
                <thead>
                    <tr>
                        {columns.map(c => (
                            <th key={c} style={{
                                textAlign: 'left', padding: '8px 12px', fontWeight: 600,
                                color: 'var(--color-text-muted)', borderBottom: '1px solid var(--color-border)',
                                position: 'sticky', top: 0, background: 'var(--color-bg-surface)',
                                fontSize: '0.72rem', textTransform: 'uppercase', letterSpacing: '0.04em'
                            }}>{c}</th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {items.map(item => (
                        <tr key={item.id} style={{
                            borderBottom: '1px solid var(--color-border)',
                            opacity: item.isActive ? 1 : 0.5,
                            transition: 'opacity 0.2s'
                        }}>
                            {renderRow(item)}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

function ToggleButton({ active, onClick }: { active: boolean; onClick: () => void }) {
    return (
        <button onClick={onClick} style={{
            display: 'flex', alignItems: 'center', gap: 4, padding: '2px 8px', border: 'none',
            background: active ? '#dcfce7' : '#fef2f2',
            color: active ? '#16a34a' : '#dc2626',
            borderRadius: 12, cursor: 'pointer', fontSize: '0.75rem', fontWeight: 600
        }}>
            {active ? <ToggleRight size={14} /> : <ToggleLeft size={14} />}
            {active ? 'Ativo' : 'Inativo'}
        </button>
    );
}

const cellStyle: React.CSSProperties = {
    padding: '8px 12px', color: 'var(--color-text)'
};

const badgeStyle: React.CSSProperties = {
    padding: '2px 8px', borderRadius: 10, background: 'var(--color-bg-surface)',
    border: '1px solid var(--color-border)', fontSize: '0.72rem',
    fontWeight: 500, fontFamily: 'monospace'
};

const addBtnStyle: React.CSSProperties = {
    display: 'flex', alignItems: 'center', gap: 4, padding: '6px 14px',
    background: 'linear-gradient(135deg, #3b82f6, #2563eb)', border: 'none',
    borderRadius: 6, color: '#fff', fontSize: '0.82rem', fontWeight: 600,
    cursor: 'pointer', whiteSpace: 'nowrap'
};
