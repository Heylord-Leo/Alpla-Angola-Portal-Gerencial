import React, { useState, useEffect } from 'react';
import { Plus, ToggleLeft, ToggleRight } from 'lucide-react';
import { itEquipmentCatalogApi, itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, ErrorBox } from './EquipmentFormModal';
import { FormInput } from '../common/form/FormInput';
import { FormSelect } from '../common/form/FormSelect';

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
            <div style={{ display: 'flex', gap: 8, marginBottom: 12, alignItems: 'flex-start' }}>
                <FormInput
                    value={newName} onChange={setNewName} placeholder="Novo fabricante..."
                    onKeyDown={e => e.key === 'Enter' && handleCreate()}
                    style={{ flex: 1, margin: 0 }}
                />
                <button onClick={handleCreate} style={{...addBtnStyle, marginTop: 0}}><Plus size={14} /> Adicionar</button>
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
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ id: string; code: string; displayName: string; isActive: boolean; sortOrder: number }>>([]);
    const [loading, setLoading] = useState(true);
    const [newName, setNewName] = useState('');
    const [newMfrId, setNewMfrId] = useState('');
    const [newTypeCode, setNewTypeCode] = useState('');
    const [error, setError] = useState('');

    const load = async () => {
        try {
            setLoading(true);
            const [models, mfrs, types] = await Promise.all([
                itEquipmentCatalogApi.models.list(),
                itEquipmentCatalogApi.manufacturers.list(true),
                itEquipmentApi.types.list(true)
            ]);
            setItems(models);
            setManufacturers(mfrs);
            setEquipmentTypes(types);
        } catch { /* empty */ } finally { setLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const handleCreate = async () => {
        if (!newName.trim() || !newMfrId || !newTypeCode) {
            if (!newTypeCode) setError('Selecione um tipo de equipamento válido.');
            return;
        }
        try {
            setError('');
            await itEquipmentCatalogApi.models.create({
                name: newName.trim(),
                manufacturerId: newMfrId,
                equipmentTypeCode: newTypeCode
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

    // Resolve type code to display name, with fallbacks for null/invalid
    const resolveTypeName = (code: string | null | undefined): { label: string; isValid: boolean } => {
        if (!code) return { label: 'Tipo não definido', isValid: false };
        const found = equipmentTypes.find(t => t.code === code);
        if (found) return { label: found.displayName, isValid: true };
        return { label: 'Tipo inválido / não encontrado', isValid: false };
    };

    const canCreate = !!(newName.trim() && newMfrId && newTypeCode);

    return (
        <div>
            {error && <ErrorBox msg={error} />}
            <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                <FormSelect
                    value={newMfrId}
                    onChange={setNewMfrId}
                    options={[
                        { label: 'Fabricante...', value: '' },
                        ...manufacturers.map(m => ({ label: m.name, value: m.id }))
                    ]}
                    style={{ flex: '0 0 180px', margin: 0 }}
                />
                <FormInput
                    value={newName} onChange={setNewName} placeholder="Nome do modelo..."
                    style={{ flex: 1, margin: 0 }}
                />
                <FormSelect
                    value={newTypeCode}
                    onChange={setNewTypeCode}
                    options={[
                        { label: 'Tipo...', value: '' },
                        ...equipmentTypes.map(t => ({ label: t.displayName, value: t.code }))
                    ]}
                    style={{ flex: '0 0 160px', margin: 0 }}
                />
                <button onClick={handleCreate} disabled={!canCreate} style={{
                    ...addBtnStyle,
                    marginTop: 0,
                    opacity: canCreate ? 1 : 0.5,
                    cursor: canCreate ? 'pointer' : 'not-allowed'
                }}><Plus size={14} /> Adicionar</button>
            </div>
            <CatalogTable
                items={items}
                loading={loading}
                columns={['Fabricante', 'Modelo', 'Tipo', 'Estado']}
                renderRow={(item) => {
                    const typeInfo = resolveTypeName(item.equipmentTypeCode);
                    return (
                        <>
                            <td style={cellStyle}>{item.manufacturerName}</td>
                            <td style={cellStyle}>{item.name}</td>
                            <td style={cellStyle}>
                                <span style={{
                                    ...badgeStyle,
                                    color: typeInfo.isValid ? 'var(--color-text)' : '#d97706',
                                    borderColor: typeInfo.isValid ? 'var(--color-border)' : '#fde68a',
                                    backgroundColor: typeInfo.isValid ? 'var(--color-bg-surface)' : '#fffbeb'
                                }}>
                                    {typeInfo.label}
                                </span>
                            </td>
                            <td style={cellStyle}>
                                <ToggleButton active={item.isActive} onClick={() => handleToggle(item.id)} />
                            </td>
                        </>
                    );
                }}
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
            <div style={{ display: 'flex', gap: 8, marginBottom: 12, alignItems: 'flex-start' }}>
                <FormInput
                    value={newName} onChange={setNewName} placeholder="Novo processador..."
                    onKeyDown={e => e.key === 'Enter' && handleCreate()}
                    style={{ flex: 1, margin: 0 }}
                />
                <button onClick={handleCreate} style={{...addBtnStyle, marginTop: 0}}><Plus size={14} /> Adicionar</button>
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
            <div style={{ display: 'flex', gap: 8, marginBottom: 12, alignItems: 'flex-start' }}>
                <FormInput
                    value={newName} onChange={setNewName} placeholder="Ex: 16 GB"
                    onKeyDown={e => e.key === 'Enter' && handleCreate()}
                    style={{ flex: 1, margin: 0 }}
                />
                <FormInput
                    type="number"
                    value={newGb} onChange={setNewGb} placeholder="GB (num)"
                    style={{ flex: '0 0 100px', margin: 0 }}
                />
                <button onClick={handleCreate} style={{...addBtnStyle, marginTop: 0}}><Plus size={14} /> Adicionar</button>
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
