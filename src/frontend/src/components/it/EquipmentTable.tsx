import { ArrowUp, ArrowDown, Loader2 } from 'lucide-react';
import { EQUIPMENT_STATUS_CONFIG, EQUIPMENT_TYPE_CONFIG } from '../../types/itEquipment';
import type { ITEquipmentListResponse } from '../../types/itEquipment';
import { StatusBadge } from '../common/ui/StatusBadge';

interface Props {
    data: ITEquipmentListResponse | null;
    loading: boolean;
    sortBy: string;
    isDescending: boolean;
    onSort: (field: string) => void;
    onRowClick: (id: string) => void;
}

export function EquipmentTable({ data, loading, sortBy, isDescending, onSort, onRowClick }: Props) {
    const columns = [
        { key: 'assettag', label: 'Código do Ativo', width: '180px' },
        { key: 'hostname', label: 'Hostname', width: '140px' },
        { key: 'type', label: 'Tipo', width: '100px' },
        { key: 'status', label: 'Status', width: '120px' },
        { key: 'manufacturer', label: 'Fabricante', width: '120px' },
        { key: 'model', label: 'Modelo', width: '140px' },
        { key: 'serialnumber', label: 'Serial Number', width: '150px' },
        { key: 'owner', label: 'Utilizador', width: '160px' },
        { key: 'plant', label: 'Planta', width: '90px' },
    ];

    if (loading && !data) {
        return (
            <div style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                padding: 60, backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                borderRadius: 12
            }}>
                <Loader2 size={32} className="spin" style={{ color: 'var(--color-primary)', animation: 'spin 1s linear infinite' }} />
                <p style={{ color: 'var(--color-text-muted)', marginTop: 12, fontSize: '0.9rem' }}>Carregando equipamentos...</p>
                <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
            </div>
        );
    }

    if (!data || data.items.length === 0) {
        return (
            <div style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                padding: 60, backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                borderRadius: 12
            }}>
                <div style={{ fontSize: 40, marginBottom: 12 }}>📦</div>
                <p style={{ color: 'var(--color-text-muted)', fontSize: '0.95rem', fontWeight: 500 }}>
                    Nenhum equipamento encontrado
                </p>
                <p style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem', opacity: 0.7 }}>
                    Use o botão "Novo Equipamento" ou "Importar CSV" para começar.
                </p>
            </div>
        );
    }

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
            borderRadius: 12, overflow: 'hidden', position: 'relative'
        }}>
            {loading && (
                <div style={{
                    position: 'absolute', top: 0, left: 0, right: 0, height: 2,
                    background: 'linear-gradient(90deg, transparent, #3b82f6, transparent)',
                    animation: 'shimmer 1.5s infinite',
                    zIndex: 10
                }} />
            )}
            <style>{`@keyframes shimmer { 0% { transform: translateX(-100%); } 100% { transform: translateX(100%); } }`}</style>

            <div style={{ overflowX: 'auto' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
                    <thead>
                        <tr style={{ borderBottom: '1px solid var(--color-border)' }}>
                            {columns.map(col => (
                                <th
                                    key={col.key}
                                    onClick={() => onSort(col.key)}
                                    style={{
                                        padding: '12px 14px', textAlign: 'left', fontWeight: 600,
                                        color: 'var(--color-text-muted)', fontSize: '0.75rem',
                                        textTransform: 'uppercase', letterSpacing: '0.05em',
                                        cursor: 'pointer', userSelect: 'none', whiteSpace: 'nowrap',
                                        width: col.width,
                                        backgroundColor: sortBy === col.key ? 'rgba(59,130,246,0.04)' : 'transparent'
                                    }}
                                >
                                    <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                                        {col.label}
                                        {sortBy === col.key && (
                                            isDescending ? <ArrowDown size={12} /> : <ArrowUp size={12} />
                                        )}
                                    </span>
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody>
                        {data.items.map(item => {
                            const statusCfg = EQUIPMENT_STATUS_CONFIG[item.statusCode] || EQUIPMENT_STATUS_CONFIG['UNKNOWN'];
                            const typeCfg = EQUIPMENT_TYPE_CONFIG[item.equipmentType] || EQUIPMENT_TYPE_CONFIG['UNKNOWN'];

                            return (
                                <tr
                                    key={item.id}
                                    onClick={() => onRowClick(item.id)}
                                    style={{
                                        borderBottom: '1px solid var(--color-border)',
                                        cursor: 'pointer',
                                        transition: 'background 0.15s'
                                    }}
                                    onMouseOver={(e) => e.currentTarget.style.backgroundColor = 'rgba(59,130,246,0.03)'}
                                    onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                                >
                                    <td style={{ padding: '10px 14px', fontWeight: 600, color: 'var(--color-text)', fontFamily: 'monospace' }}>
                                        {item.assetTag}
                                    </td>
                                    <td style={{ padding: '10px 14px', color: item.hostname ? 'var(--color-text)' : 'var(--color-text-muted)' }}>
                                        {item.hostname || '—'}
                                    </td>
                                    <td style={{ padding: '10px 14px', color: 'var(--color-text)' }}>
                                        {typeCfg.label}
                                    </td>
                                    <td style={{ padding: '10px 14px' }}>
                                        <StatusBadge status={item.statusCode} label={statusCfg.label} />
                                    </td>
                                    <td style={{ padding: '10px 14px', color: item.manufacturer ? 'var(--color-text)' : 'var(--color-text-muted)' }}>
                                        {item.manufacturer || '—'}
                                    </td>
                                    <td style={{ padding: '10px 14px', color: item.model ? 'var(--color-text)' : 'var(--color-text-muted)' }}>
                                        {item.model || '—'}
                                    </td>
                                    <td style={{ padding: '10px 14px', fontFamily: 'monospace', fontSize: '0.8rem', color: item.serialNumber ? 'var(--color-text)' : 'var(--color-text-muted)' }}>
                                        {item.serialNumber || '—'}
                                    </td>
                                    <td style={{ padding: '10px 14px', color: item.currentOwnerName ? 'var(--color-text)' : 'var(--color-text-muted)' }}>
                                        {item.currentOwnerName || '—'}
                                    </td>
                                    <td style={{ padding: '10px 14px', color: item.plant ? 'var(--color-text)' : 'var(--color-text-muted)' }}>
                                        {item.plant || '—'}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
        </div>
    );
}
