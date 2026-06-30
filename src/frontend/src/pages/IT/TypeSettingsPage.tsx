import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, Edit3, ToggleLeft, ToggleRight, Loader2, AlertCircle } from 'lucide-react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import { StandardTable } from '../../components/ui/StandardTable';
import { StatusBadge } from '../../components/common/ui/StatusBadge';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { EmptyState } from '../../components/common/ui/EmptyState';

export default function TypeSettingsPage() {
    const navigate = useNavigate();
    
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');
    
    const [items, setItems] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');

    // Confirmation dialog state
    const [confirmAction, setConfirmAction] = useState<{ id: string, name: string, active: boolean } | null>(null);

    const loadData = async () => {
        try {
            setLoading(true);
            setError('');
            
            // Note: API for types.list supports a boolean for activeOnly, but we'll fetch all and filter client-side for "inactive"
            const data = await itEquipmentApi.types.list(statusFilter === 'active');
            
            let filtered = data;
            if (statusFilter === 'inactive') {
                filtered = data.filter((i: any) => !i.isActive);
            }
            
            // Sort by sortOrder
            filtered.sort((a, b) => a.sortOrder - b.sortOrder);
            
            setItems(filtered);
        } catch (err: any) {
            setError(err.message || 'Falha ao carregar tipos de equipamento.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, [statusFilter]);

    // Handle Search filter locally
    const filteredItems = items.filter(item => {
        if (!search.trim()) return true;
        const q = search.toLowerCase();
        return item.displayName.toLowerCase().includes(q) || item.code.toLowerCase().includes(q);
    });

    const handleCreate = () => {
        navigate('/it/types/new');
    };

    const handleEdit = (item: any) => {
        navigate(`/it/types/${item.id}/edit`);
    };

    const handleToggleConfirm = async () => {
        if (!confirmAction) return;
        
        try {
            await itEquipmentApi.types.toggle(confirmAction.id);
            await loadData();
        } catch (err: any) {
            setError(err.message || 'Falha ao alterar o estado.');
        } finally {
            setConfirmAction(null);
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 24, paddingTop: 16 }}>
            {error && (
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '12px 16px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#991b1b', fontSize: '0.9rem' }}>
                    <AlertCircle size={18} />
                    <span>{error}</span>
                </div>
            )}

            {/* Action Bar */}
            <div data-tour="it-type-actions" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 16 }}>
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
                    Novo Tipo
                </button>
            </div>

            {/* Data Table */}
            <div data-tour="it-type-table" style={{ backgroundColor: 'var(--color-bg-surface)', borderRadius: 8, border: '1px solid var(--color-border)', overflow: 'hidden' }}>
                {loading ? (
                    <div style={{ padding: '60px 20px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                        <Loader2 size={24} className="animate-spin" style={{ margin: '0 auto 12px' }} />
                        <p style={{ margin: 0, fontSize: '0.9rem' }}>A carregar tipos de equipamento...</p>
                    </div>
                ) : filteredItems.length === 0 ? (
                    <EmptyState 
                        title="Nenhum tipo encontrado"
                        description="Tente ajustar os filtros ou crie um novo registo."
                        icon={<AlertCircle />}
                    />
                ) : (
                    <StandardTable isEmpty={filteredItems.length === 0}>
                        <thead>
                            <tr>
                                <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Código</th>
                                <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Nome de Exibição</th>
                                <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Ordem</th>
                                <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Estado</th>
                                <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}></th>
                            </tr>
                        </thead>
                        <tbody>
                            {filteredItems.map((item: any) => (
                                <tr key={item.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                                <td style={{ padding: '12px 16px', fontSize: '0.9rem', fontFamily: 'monospace', fontWeight: 600, color: 'var(--color-text)' }}>
                                    {item.code}
                                </td>
                                <td style={{ padding: '12px 16px', fontSize: '0.9rem', fontWeight: 500, color: 'var(--color-text)' }}>
                                    {item.displayName}
                                </td>
                                <td style={{ padding: '12px 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
                                    {item.sortOrder}
                                </td>
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
                                                onClick: () => setConfirmAction({ id: item.id, name: item.displayName, active: item.isActive })
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

            {/* Confirmation Dialog for Toggle Status */}
            {confirmAction && (
                <ConfirmationDialog
                    title={confirmAction.active ? 'Desativar Registo?' : 'Ativar Registo?'}
                    message={
                        confirmAction.active 
                            ? `Tem a certeza que deseja desativar o tipo "${confirmAction.name}"? Desativar este registo impedirá sua seleção em novos cadastros, mas não alterará registros históricos existentes.`
                            : `Tem a certeza que deseja ativar o tipo "${confirmAction.name}"?`
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
