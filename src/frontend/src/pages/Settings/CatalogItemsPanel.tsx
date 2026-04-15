import React, { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import { ItemCatalogDto, Unit } from '../../types';
import { FeedbackType } from '../../components/ui/Feedback';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { Edit2, Power, PowerOff, Search, Database } from 'lucide-react';

interface CatalogItemsPanelProps {
    feedback: { message: string; type: FeedbackType } | null;
    setFeedback: (f: { message: string; type: FeedbackType } | null) => void;
}

/**
 * Master Data tab panel for managing catalog items.
 * Follows the same card-based layout as other MasterData tabs.
 */
export function CatalogItemsPanel({ setFeedback }: CatalogItemsPanelProps) {
    const navigate = useNavigate();
    const [items, setItems] = useState<ItemCatalogDto[]>([]);
    const [units, setUnits] = useState<Unit[]>([]);
    const [loading, setLoading] = useState(true);
    const [editId, setEditId] = useState<number | null>(null);
    const [searchQuery, setSearchQuery] = useState('');
    const [showInactive] = useState(true);
    const [categoryFilter, setCategoryFilter] = useState('');

    const [formData, setFormData] = useState({
        description: '',
        primaveraCode: '',
        supplierCode: '',
        defaultUnitId: null as number | null,
        category: ''
    });

    const loadItems = useCallback(async (query: string = '') => {
        try {
            setLoading(true);
            const itemsData = await api.catalogItems.getAll(showInactive, query, query ? 50 : 10);
            setItems(itemsData);
        } catch (err: any) {
            setFeedback({ message: 'Falha ao carregar catálogo de itens.', type: 'error' });
        } finally {
            setLoading(false);
        }
    }, [showInactive, setFeedback]);

    const loadUnits = useCallback(async () => {
        try {
            const unitsData = await api.lookups.getUnits(true);
            setUnits(unitsData);
        } catch (err: any) {
            console.error(err);
        }
    }, []);

    useEffect(() => {
        loadUnits();
    }, [loadUnits]);

    useEffect(() => {
        const timer = setTimeout(() => {
            loadItems(searchQuery);
        }, 500);
        return () => clearTimeout(timer);
    }, [searchQuery, loadItems]);

    const handleEdit = (item: ItemCatalogDto) => {
        setEditId(item.id);
        setFormData({
            description: item.description,
            primaveraCode: item.primaveraCode || '',
            supplierCode: item.supplierCode || '',
            defaultUnitId: item.defaultUnitId,
            category: item.category || ''
        });
    };

    const handleCancel = () => {
        setEditId(null);
        setFormData({ description: '', primaveraCode: '', supplierCode: '', defaultUnitId: null, category: '' });
    };

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        setFeedback(null);

        if (!formData.description.trim()) {
            setFeedback({ message: 'A descrição é obrigatória.', type: 'error' });
            return;
        }

        try {
            const payload = {
                description: formData.description.trim(),
                primaveraCode: formData.primaveraCode.trim() || undefined,
                supplierCode: formData.supplierCode.trim() || undefined,
                defaultUnitId: formData.defaultUnitId || null,
                category: formData.category.trim() || undefined
            };

            if (editId) {
                await api.catalogItems.update(editId, payload);
                setFeedback({ message: 'Item do catálogo atualizado.', type: 'success' });
            } else {
                await api.catalogItems.create(payload);
                setFeedback({ message: 'Item do catálogo criado.', type: 'success' });
            }
            handleCancel();
            loadItems(searchQuery);
        } catch (err: any) {
            setFeedback({ message: err.message || 'Falha ao salvar item.', type: 'error' });
        }
    };

    const handleToggleActive = async (id: number) => {
        try {
            await api.catalogItems.toggleActive(id);
            setFeedback({ message: 'Estado do item alterado.', type: 'success' });
            loadItems(searchQuery);
        } catch (err: any) {
            setFeedback({ message: err.message || 'Falha ao alternar estado.', type: 'error' });
        }
    };

    // Extract unique categories for filter
    const categories = Array.from(new Set(items.map(i => i.category).filter(Boolean)));

    // Filtered items (Server-side search handles text, leaving Category filtering local)
    const filteredItems = items.filter(item => {
        const matchesCategory = !categoryFilter || item.category === categoryFilter;
        return matchesCategory;
    });

    const labelStyle: React.CSSProperties = {
        display: 'block',
        fontSize: '0.75rem',
        fontWeight: 800,
        color: 'var(--color-text-muted)',
        textTransform: 'uppercase',
        marginBottom: '6px'
    };

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '12px',
        backgroundColor: 'var(--color-bg-page)',
        border: '2px solid var(--color-border)',
        fontSize: '0.85rem',
        fontFamily: 'var(--font-family-body)',
        color: 'var(--color-text-main)'
    };

    const thStyle: React.CSSProperties = {
        padding: '12px 16px',
        fontSize: '0.65rem',
        fontWeight: 800,
        textTransform: 'uppercase',
        letterSpacing: '0.08em',
        color: 'var(--color-text-muted)',
        textAlign: 'left',
        borderBottom: '2px solid var(--color-border)',
        whiteSpace: 'nowrap'
    };

    const tdStyle: React.CSSProperties = {
        padding: '12px 16px',
        fontSize: '0.85rem',
        borderBottom: '1px solid var(--color-border)',
        color: 'var(--color-text-main)',
        verticalAlign: 'middle'
    };

    if (loading) {
        return <div style={{ padding: '40px', textAlign: 'center', color: 'var(--color-text-muted)' }}>Carregando catálogo...</div>;
    }

    return (
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(350px, 1fr) 2fr', gap: '32px', alignItems: 'start' }}>

            {/* Form Column */}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                padding: '32px',
                borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)',
                position: 'sticky',
                top: 'calc(var(--header-height) + 1rem)'
            }}>
                <h2 style={{
                    marginTop: 0,
                    marginBottom: '24px',
                    fontSize: '1.25rem',
                    fontWeight: 900,
                    textTransform: 'uppercase',
                    color: 'var(--color-primary)',
                    borderBottom: '2px solid var(--color-border)',
                    paddingBottom: '8px'
                }}>
                    {editId ? 'Editar' : 'Novo'} Item do Catálogo
                </h2>
                <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                    <div>
                        <label style={labelStyle}>Descrição *</label>
                        <input
                            type="text"
                            value={formData.description}
                            onChange={e => setFormData(prev => ({ ...prev, description: e.target.value }))}
                            style={inputStyle}
                            placeholder="Descrição do item"
                            required
                        />
                    </div>
                    <div>
                        <label style={labelStyle}>Cód. Primavera</label>
                        <input
                            type="text"
                            value={formData.primaveraCode}
                            onChange={e => setFormData(prev => ({ ...prev, primaveraCode: e.target.value }))}
                            style={inputStyle}
                            placeholder="Código ERP Primavera"
                        />
                    </div>
                    <div>
                        <label style={labelStyle}>Cód. Fornecedor</label>
                        <input
                            type="text"
                            value={formData.supplierCode}
                            onChange={e => setFormData(prev => ({ ...prev, supplierCode: e.target.value }))}
                            style={inputStyle}
                            placeholder="Código Fornecedor"
                        />
                    </div>
                    <div>
                        <label style={labelStyle}>Unidade Padrão</label>
                        <select
                            value={formData.defaultUnitId || ''}
                            onChange={e => setFormData(prev => ({ ...prev, defaultUnitId: e.target.value ? Number(e.target.value) : null }))}
                            style={inputStyle}
                        >
                            <option value="">— Nenhuma —</option>
                            {units.filter(u => u.isActive).map(u => (
                                <option key={u.id} value={u.id}>{u.code} — {u.name}</option>
                            ))}
                        </select>
                    </div>
                    <div>
                        <label style={labelStyle}>Categoria</label>
                        <input
                            type="text"
                            value={formData.category}
                            onChange={e => setFormData(prev => ({ ...prev, category: e.target.value }))}
                            style={inputStyle}
                            placeholder="Ex: Material de Escritório"
                        />
                    </div>

                    <div style={{ display: 'flex', gap: '12px' }}>
                        <button type="submit" style={{
                            flex: 1,
                            padding: '14px',
                            backgroundColor: 'var(--color-primary)',
                            color: '#fff',
                            border: 'none',
                            fontWeight: 800,
                            fontSize: '0.8rem',
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em',
                            cursor: 'pointer'
                        }}>
                            {editId ? 'Atualizar' : 'Criar'}
                        </button>
                        {editId && (
                            <button type="button" onClick={handleCancel} style={{
                                padding: '14px 20px',
                                backgroundColor: 'var(--color-bg-page)',
                                border: '2px solid var(--color-border)',
                                fontWeight: 700,
                                fontSize: '0.8rem',
                                textTransform: 'uppercase',
                                cursor: 'pointer',
                                color: 'var(--color-text-main)'
                            }}>
                                Cancelar
                            </button>
                        )}
                    </div>
                </form>

                {/* Primavera Sync Navigation */}
                <div style={{ marginTop: '24px', borderTop: '2px solid var(--color-border)', paddingTop: '16px' }}>
                    <button
                        type="button"
                        onClick={() => navigate('/settings/sync/catalog')}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '12px 16px',
                            backgroundColor: 'rgba(var(--color-primary-rgb), 0.06)',
                            border: '1px solid rgba(var(--color-primary-rgb), 0.15)',
                            borderRadius: 'var(--radius-md)',
                            fontWeight: 600,
                            fontSize: '0.8rem',
                            cursor: 'pointer',
                            color: 'var(--color-primary)',
                            width: '100%',
                            justifyContent: 'center',
                            transition: 'all 0.15s ease'
                        }}
                    >
                        <Database size={16} />
                        Sincronizar com Primavera
                    </button>
                    <p style={{ marginTop: '8px', fontSize: '0.7rem', color: 'var(--color-text-muted)', lineHeight: 1.4 }}>
                        Compare e importe itens do catálogo Primavera de forma controlada.
                    </p>
                </div>
            </div>

            {/* Table Column */}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)',
                overflow: 'hidden'
            }}>
                {/* Search & Filters */}
                <div style={{
                    padding: '16px 20px',
                    borderBottom: '2px solid var(--color-border)',
                    display: 'flex',
                    gap: '12px',
                    alignItems: 'center',
                    flexWrap: 'wrap'
                }}>
                    <div style={{ position: 'relative', flex: '1', minWidth: '200px' }}>
                        <Search size={14} style={{
                            position: 'absolute',
                            left: '12px',
                            top: '50%',
                            transform: 'translateY(-50%)',
                            color: 'var(--color-text-muted)',
                            pointerEvents: 'none'
                        }} />
                        <input
                            type="text"
                            placeholder="Pesquisar por código ou descrição..."
                            value={searchQuery}
                            onChange={e => setSearchQuery(e.target.value)}
                            style={{
                                ...inputStyle,
                                paddingLeft: '34px',
                                border: '1px solid var(--color-border)'
                            }}
                        />
                    </div>
                    {categories.length > 0 && (
                        <select
                            value={categoryFilter}
                            onChange={e => setCategoryFilter(e.target.value)}
                            style={{
                                ...inputStyle,
                                width: 'auto',
                                minWidth: '150px',
                                border: '1px solid var(--color-border)'
                            }}
                        >
                            <option value="">Todas Categorias</option>
                            {categories.map(cat => (
                                <option key={cat} value={cat!}>{cat}</option>
                            ))}
                        </select>
                    )}
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                        <span style={{ fontWeight: 700 }}>{filteredItems.length}</span>
                        <span>itens</span>
                    </div>
                </div>

                {/* Table */}
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                        <thead>
                            <tr style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                <th style={thStyle}>Código</th>
                                <th style={thStyle}>Descrição</th>
                                <th style={thStyle}>Unidade</th>
                                <th style={thStyle}>Categoria</th>
                                <th style={thStyle}>Origem</th>
                                <th style={thStyle}>Estado</th>
                                <th style={{ ...thStyle, textAlign: 'center' }}>Ações</th>
                            </tr>
                        </thead>
                        <tbody>
                            {filteredItems.length === 0 ? (
                                <tr>
                                    <td colSpan={7} style={{ ...tdStyle, textAlign: 'center', color: 'var(--color-text-muted)', fontStyle: 'italic', padding: '32px' }}>
                                        Nenhum item encontrado.
                                    </td>
                                </tr>
                            ) : (
                                filteredItems.map(item => (
                                    <tr key={item.id} style={{ opacity: item.isActive ? 1 : 0.5 }}>
                                        <td style={{ ...tdStyle, fontFamily: 'monospace', fontWeight: 700, color: 'var(--color-primary)', fontSize: '0.8rem' }}>
                                            {item.code}
                                        </td>
                                        <td style={{ ...tdStyle, fontWeight: 600 }}>
                                            {item.description}
                                        </td>
                                        <td style={{ ...tdStyle, fontSize: '0.8rem' }}>
                                            {item.defaultUnitCode || '—'}
                                        </td>
                                        <td style={{ ...tdStyle, fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                                            {item.category || '—'}
                                        </td>
                                        <td style={tdStyle}>
                                            <span style={{
                                                display: 'inline-block',
                                                padding: '2px 8px',
                                                fontSize: '0.65rem',
                                                fontWeight: 700,
                                                textTransform: 'uppercase',
                                                letterSpacing: '0.05em',
                                                backgroundColor: item.origin === 'IMPORTED_SHAREPOINT' ? '#dbeafe' : '#f3f4f6',
                                                color: item.origin === 'IMPORTED_SHAREPOINT' ? '#1e40af' : '#4b5563',
                                                borderRadius: '4px'
                                            }}>
                                                {item.origin === 'IMPORTED_SHAREPOINT' ? 'SharePoint' : 'Manual'}
                                            </span>
                                        </td>
                                        <td style={tdStyle}>
                                            <span style={{
                                                display: 'inline-block',
                                                padding: '2px 8px',
                                                fontSize: '0.65rem',
                                                fontWeight: 700,
                                                textTransform: 'uppercase',
                                                backgroundColor: item.isActive ? '#dcfce7' : '#fee2e2',
                                                color: item.isActive ? '#166534' : '#991b1b',
                                                borderRadius: '4px'
                                            }}>
                                                {item.isActive ? 'Ativo' : 'Inativo'}
                                            </span>
                                        </td>
                                        <td style={{ ...tdStyle, textAlign: 'center' }}>
                                            <KebabMenu options={[
                                                {
                                                    label: 'Editar',
                                                    icon: <Edit2 size={14} />,
                                                    onClick: () => handleEdit(item)
                                                },
                                                {
                                                    label: item.isActive ? 'Desativar' : 'Ativar',
                                                    icon: item.isActive ? <PowerOff size={14} /> : <Power size={14} />,
                                                    onClick: () => handleToggleActive(item.id)
                                                }
                                            ]} />
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}
