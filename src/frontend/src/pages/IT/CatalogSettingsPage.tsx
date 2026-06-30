import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, Edit3, ToggleLeft, ToggleRight, Loader2, AlertCircle } from 'lucide-react';
import { itEquipmentCatalogApi, itEquipmentApi } from '../../lib/itEquipmentApi';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { StandardTable } from '../../components/ui/StandardTable';
import { StatusBadge } from '../../components/common/ui/StatusBadge';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { CatalogDrawer, SimpleCatalogType } from '../../components/it/CatalogDrawer';
import { EmptyState } from '../../components/common/ui/EmptyState';

type CatalogTab = 'manufacturers' | 'models' | 'processors' | 'memory';

export default function CatalogSettingsPage() {
    const navigate = useNavigate();
    
    const [tab, setTab] = useState<CatalogTab>('manufacturers');
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');
    
    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    
    // For mapping codes to display names
    const [equipmentTypesMap, setEquipmentTypesMap] = useState<Record<string, string>>({});

    // Drawer state
    const [isDrawerOpen, setIsDrawerOpen] = useState(false);
    const [editingItem, setEditingItem] = useState<any>(null);

    // Confirmation dialog state
    const [confirmAction, setConfirmAction] = useState<{ id: string, name: string, active: boolean } | null>(null);

    const loadData = async () => {
        try {
            setLoading(true);
            setError('');
            
            // If tab is models, ensure we have types loaded for mapping
            if (tab === 'models' && Object.keys(equipmentTypesMap).length === 0) {
                const types = await itEquipmentApi.types.list(false);
                const map: Record<string, string> = {};
                types.forEach(t => { map[t.code] = t.displayName; });
                setEquipmentTypesMap(map);
            }

            let data;
            const fetchActiveOnly = statusFilter === 'active' ? true : undefined;
            
            switch (tab) {
                case 'manufacturers':
                    data = await itEquipmentCatalogApi.manufacturers.list(fetchActiveOnly);
                    break;
                case 'models':
                    // Note: API doesn't support generic activeOnly for models list right now via simple boolean in some older specs, 
                    // but the implementation in lib supports { activeOnly: boolean }
                    data = await itEquipmentCatalogApi.models.list(fetchActiveOnly ? { activeOnly: true } : undefined);
                    break;
                case 'processors':
                    data = await itEquipmentCatalogApi.processors.list(fetchActiveOnly);
                    break;
                case 'memory':
                    data = await itEquipmentCatalogApi.memoryOptions.list(fetchActiveOnly);
                    break;
            }
            
            if (statusFilter === 'inactive') {
                data = data.filter((i: any) => !i.isActive);
            }
            
            setItems(data || []);
        } catch (err: any) {
            setError(err.message || 'Falha ao carregar catálogo.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, [tab, statusFilter]);

    // Handle Search filter locally
    const filteredItems = items.filter(item => {
        if (!search.trim()) return true;
        const q = search.toLowerCase();
        const name = item.name || item.displayName || '';
        return name.toLowerCase().includes(q);
    });

    const getTabConfig = () => {
        switch (tab) {
            case 'manufacturers': return { title: 'Fabricante', key: 'manufacturers' as SimpleCatalogType };
            case 'models': return { title: 'Modelo', key: null };
            case 'processors': return { title: 'Processador', key: 'processors' as SimpleCatalogType };
            case 'memory': return { title: 'Memória', key: 'memory' as SimpleCatalogType };
        }
    };

    const handleCreate = () => {
        if (tab === 'models') {
            navigate('/it/catalogs/models/new');
        } else {
            setEditingItem(null);
            setIsDrawerOpen(true);
        }
    };

    const handleEdit = (item: any) => {
        if (tab === 'models') {
            navigate(`/it/catalogs/models/${item.id}/edit`);
        } else {
            setEditingItem(item);
            setIsDrawerOpen(true);
        }
    };

    const handleSaveSimple = async (data: any) => {
        const apiMap = {
            manufacturers: itEquipmentCatalogApi.manufacturers,
            processors: itEquipmentCatalogApi.processors,
            memory: itEquipmentCatalogApi.memoryOptions
        };
        const api = apiMap[tab as SimpleCatalogType];
        if (!api) return;

        if (editingItem) {
            await api.update(editingItem.id, data);
        } else {
            await api.create(data);
        }
        await loadData();
    };

    const handleToggleConfirm = async () => {
        if (!confirmAction) return;
        
        const apiMap = {
            manufacturers: itEquipmentCatalogApi.manufacturers,
            models: itEquipmentCatalogApi.models,
            processors: itEquipmentCatalogApi.processors,
            memory: itEquipmentCatalogApi.memoryOptions
        };
        const api = apiMap[tab];

        try {
            await api.toggle(confirmAction.id);
            await loadData();
        } catch (err: any) {
            setError(err.message || 'Falha ao alterar o estado.');
        } finally {
            setConfirmAction(null);
        }
    };

    const config = getTabConfig();

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
            {/* Internal Tabs */}
            <div data-tour="it-catalog-tabs" style={{ display: 'flex', gap: 0, borderBottom: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                {([
                    { key: 'manufacturers' as const, label: 'Fabricantes' },
                    { key: 'models' as const, label: 'Modelos' },
                    { key: 'processors' as const, label: 'Processadores' },
                    { key: 'memory' as const, label: 'Memória RAM' },
                ]).map(t => (
                    <button
                        key={t.key}
                        onClick={() => { setTab(t.key); setSearch(''); setStatusFilter('all'); }}
                        style={{
                            padding: '12px 24px', border: 'none', cursor: 'pointer', fontWeight: 600,
                            fontSize: '0.9rem', background: 'none',
                            color: tab === t.key ? 'var(--color-primary)' : 'var(--color-text-muted)',
                            borderBottom: tab === t.key ? '2px solid var(--color-primary)' : '2px solid transparent',
                            marginBottom: -1, transition: 'all 0.2s'
                        }}
                    >
                        {t.label}
                    </button>
                ))}
            </div>

            {error && (
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '12px 16px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#991b1b', fontSize: '0.9rem' }}>
                    <AlertCircle size={18} />
                    <span>{error}</span>
                </div>
            )}

            {/* Action Bar */}
            <div data-tour="it-catalog-actions" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 16 }}>
                <SearchFilterBar
                    searchPlaceholder="Pesquisar por nome ou código..."
                    searchValue={search}
                    onSearchChange={setSearch}
                    tabs={[
                        { id: 'all', label: 'Todos os estados' },
                        { id: 'active', label: 'Ativos' },
                        { id: 'inactive', label: 'Inativos' }
                    ]}
                    activeTabId={statusFilter}
                    onTabChange={(v: any) => setStatusFilter(v)}
                />
                
                <button 
                    onClick={handleCreate}
                    style={{
                        padding: '8px 16px', border: 'none', borderRadius: 6,
                        background: 'var(--color-primary)', color: 'white', fontWeight: 600,
                        cursor: 'pointer', fontSize: '0.9rem',
                        display: 'flex', alignItems: 'center', gap: 8,
                        boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                    }}
                >
                    <Plus size={18} />
                    Novo {config.title}
                </button>
            </div>

            {/* Data Table */}
            <div data-tour="it-catalog-table" style={{ backgroundColor: 'var(--color-bg-surface)', borderRadius: 8, border: '1px solid var(--color-border)', overflow: 'hidden' }}>
                {loading ? (
                    <div style={{ padding: '60px 20px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                        <Loader2 size={24} className="animate-spin" style={{ margin: '0 auto 12px' }} />
                        <p style={{ margin: 0, fontSize: '0.9rem' }}>A carregar catálogo...</p>
                    </div>
                ) : filteredItems.length === 0 ? (
                    <EmptyState 
                        title={`Nenhum ${config.title.toLowerCase()} encontrado`}
                        description="Tente ajustar os filtros ou crie um novo registo."
                        icon={<AlertCircle />}
                    />
                ) : (
                    <StandardTable isEmpty={filteredItems.length === 0}>
                        <thead>
                            <tr>
                                {tab === 'models' ? (
                                    <>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Nome do Modelo</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Fabricante</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Tipo de Equipamento</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Estado</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}></th>
                                    </>
                                ) : tab === 'memory' ? (
                                    <>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Descrição</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Capacidade (GB)</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Estado</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}></th>
                                    </>
                                ) : (
                                    <>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Nome</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Estado</th>
                                        <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}></th>
                                    </>
                                )}
                            </tr>
                        </thead>
                        <tbody>
                            {filteredItems.map((item: any) => (
                                <tr key={item.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                                {tab === 'models' ? (
                                    <>
                                        <td style={{ padding: '12px 16px', fontSize: '0.9rem', fontWeight: 500, color: 'var(--color-text)' }}>{item.name}</td>
                                        <td style={{ padding: '12px 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
                                            {/* We don't have manufacturer name embedded in model list response currently, usually it's just manufacturerId, 
                                                but if backend returns it as manufacturer.name, we could use that. Let's just show ID or Name if available. 
                                                Assume the list might have it or we just display what's available. */}
                                            {item.manufacturer?.name || item.manufacturerId || 'N/D'}
                                        </td>
                                        <td style={{ padding: '12px 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
                                            {item.equipmentTypeCode ? (equipmentTypesMap[item.equipmentTypeCode] || item.equipmentTypeCode) : 'N/D'}
                                        </td>
                                    </>
                                ) : tab === 'memory' ? (
                                    <>
                                        <td style={{ padding: '12px 16px', fontSize: '0.9rem', fontWeight: 500, color: 'var(--color-text)' }}>{item.displayName}</td>
                                        <td style={{ padding: '12px 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>{item.valueInGb ? `${item.valueInGb} GB` : '-'}</td>
                                    </>
                                ) : (
                                    <td style={{ padding: '12px 16px', fontSize: '0.9rem', fontWeight: 500, color: 'var(--color-text)' }}>{item.name}</td>
                                )}
                                
                                <td style={{ padding: '12px 16px' }}>
                                    <StatusBadge 
                                        status={item.isActive ? 'ACTIVE' : 'INACTIVE'} 
                                        label={item.isActive ? 'Ativo' : 'Inativo'} 
                                    />
                                </td>
                                
                                <td style={{ padding: '12px 16px', textAlign: 'right' }}>
                                    <KebabMenu
                                        options={[
                                            {
                                                label: 'Editar',
                                                icon: <Edit3 size={16} />,
                                                onClick: () => handleEdit(item)
                                            },
                                            {
                                                label: item.isActive ? 'Desativar' : 'Ativar',
                                                icon: item.isActive ? <ToggleLeft size={16} /> : <ToggleRight size={16} />,
                                                onClick: () => setConfirmAction({ id: item.id, name: item.name || item.displayName, active: item.isActive })
                                            }
                                        ]}
                                    />
                                </td>
                            </tr>
                        ))}
                        </tbody>
                    </StandardTable>
                )}
            </div>

            {/* Simple Entities Drawer */}
            {config.key && (
                <CatalogDrawer
                    isOpen={isDrawerOpen}
                    onClose={() => setIsDrawerOpen(false)}
                    catalogType={config.key}
                    editingItem={editingItem}
                    onSave={handleSaveSimple}
                />
            )}

            {/* Confirmation Dialog for Toggle Status */}
            {confirmAction && (
                <ConfirmationDialog
                    title={confirmAction.active ? 'Desativar Registo?' : 'Ativar Registo?'}
                    message={
                        confirmAction.active 
                            ? `Tem a certeza que deseja desativar o registo "${confirmAction.name}"? Desativar este registo impedirá sua seleção em novos cadastros, mas não alterará registros históricos existentes.`
                            : `Tem a certeza que deseja ativar o registo "${confirmAction.name}"?`
                    }
                    confirmText={confirmAction.active ? 'Sim, desativar' : 'Sim, ativar'}
                    cancelText="Cancelar"
                    variant={confirmAction.active ? 'destructive' : 'primary'}
                    onConfirm={handleToggleConfirm}
                    onCancel={() => setConfirmAction(null)}
                />
            )}
        </div>
    );
}
