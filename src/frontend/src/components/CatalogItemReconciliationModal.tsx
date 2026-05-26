/**
 * CatalogItemReconciliationModal
 *
 * Batch review modal for resolving unmatched catalog items.
 * Shows ALL unresolved items in one table, not one-at-a-time.
 *
 * Per-row actions:
 *   - Link to existing catalog item (inline CatalogItemAutocomplete)
 *   - Create new catalog item (calls reconciliation-create endpoint)
 *   - Keep as free text (with optional justification)
 *
 * Follows QuickSupplierModal design patterns.
 * All user-facing labels are in Portuguese.
 */
import { useState, useCallback } from 'react';
import { X, Link2, PlusCircle, Check, AlertCircle, Package, Info } from 'lucide-react';
import { CatalogItemAutocomplete } from './CatalogItemAutocomplete';
import { api } from '../lib/api';
import {
    ClassifiedItem,
    ItemResolution,
    ReconciliationItemStatus,
} from '../types';

interface CatalogItemReconciliationModalProps {
    isOpen: boolean;
    onClose: () => void;
    classifiedItems: ClassifiedItem[];
    onResolveAll: (resolutions: ItemResolution[]) => void;
}

type RowAction = 'idle' | 'linking' | 'creating';

interface RowState {
    action: RowAction;
    linkedCatalogId?: number | null;
    linkedCatalogCode?: string | null;
    linkedDescription?: string;
    defaultUnitId?: number | null;
    justification: string;
    resolved: boolean;
    resolvedStatus?: ReconciliationItemStatus;
    isProcessing: boolean;
}

const STATUS_LABELS: Record<ReconciliationItemStatus, string> = {
    'MATCHED': 'Vinculado',
    'UNMATCHED': 'Sem Correspondência',
    'LOW_CONFIDENCE': 'Baixa Confiança',
    'CREATED_PENDING': 'Criado (Pend. Validação)',
    'LINKED_MANUALLY': 'Vinculado Manualmente',
    'FREE_TEXT': 'Texto Livre',
};

const STATUS_COLORS: Record<ReconciliationItemStatus, { bg: string; text: string }> = {
    'MATCHED': { bg: '#DCFCE7', text: '#166534' },
    'UNMATCHED': { bg: '#FEF3C7', text: '#92400E' },
    'LOW_CONFIDENCE': { bg: '#FEF3C7', text: '#92400E' },
    'CREATED_PENDING': { bg: '#E0E7FF', text: '#3730A3' },
    'LINKED_MANUALLY': { bg: '#DCFCE7', text: '#166534' },
    'FREE_TEXT': { bg: '#F1F5F9', text: '#475569' },
};

export function CatalogItemReconciliationModal({
    isOpen,
    onClose,
    classifiedItems,
    onResolveAll,
}: CatalogItemReconciliationModalProps) {
    // Only show unresolved items
    const unresolvedItems = classifiedItems.filter(
        ci => ci.status === 'UNMATCHED' || ci.status === 'LOW_CONFIDENCE'
    );

    const [rowStates, setRowStates] = useState<Map<number, RowState>>(new Map());

    const getRowState = (index: number): RowState => {
        return rowStates.get(index) || {
            action: 'idle',
            justification: '',
            resolved: false,
            isProcessing: false,
        };
    };

    const updateRowState = useCallback((index: number, updates: Partial<RowState>) => {
        setRowStates(prev => {
            const next = new Map(prev);
            const current = next.get(index) || {
                action: 'idle' as RowAction,
                justification: '',
                resolved: false,
                isProcessing: false,
            };
            next.set(index, { ...current, ...updates });
            return next;
        });
    }, []);

    const handleLink = useCallback((itemIndex: number, description: string, catalogId: number | null, catalogCode: string | null, defaultUnitId: number | null) => {
        if (catalogId) {
            updateRowState(itemIndex, {
                action: 'linking',
                linkedCatalogId: catalogId,
                linkedCatalogCode: catalogCode,
                linkedDescription: description,
                defaultUnitId,
                resolved: true,
                resolvedStatus: 'LINKED_MANUALLY',
            });
        }
    }, [updateRowState]);

    const handleCreateNew = useCallback(async (itemIndex: number, description: string) => {
        updateRowState(itemIndex, { isProcessing: true });
        try {
            const result = await api.catalogItems.reconciliationCreate({ description });
            updateRowState(itemIndex, {
                action: 'creating',
                linkedCatalogId: result.id,
                linkedCatalogCode: result.code,
                linkedDescription: result.description,
                defaultUnitId: result.defaultUnitId,
                resolved: true,
                resolvedStatus: 'CREATED_PENDING',
                isProcessing: false,
            });
        } catch (err) {
            console.error('Failed to create catalog item via reconciliation:', err);
            updateRowState(itemIndex, { isProcessing: false });
        }
    }, [updateRowState]);

    const handleSaveAll = useCallback(() => {
        const resolutions: ItemResolution[] = [];

        unresolvedItems.forEach(ci => {
            const rs = getRowState(ci.index);
            if (rs.resolved && rs.resolvedStatus) {
                resolutions.push({
                    itemIndex: ci.index,
                    status: rs.resolvedStatus,
                    linkedCatalogId: rs.linkedCatalogId,
                    linkedCatalogCode: rs.linkedCatalogCode,
                    linkedDescription: rs.linkedDescription,
                    defaultUnitId: rs.defaultUnitId,
                    justification: rs.justification || undefined,
                });
            }
        });

        onResolveAll(resolutions);
        setRowStates(new Map());
        onClose();
    }, [unresolvedItems, rowStates, onResolveAll, onClose]);

    const resolvedCount = unresolvedItems.filter(ci => getRowState(ci.index).resolved).length;
    const totalUnresolved = unresolvedItems.length;

    if (!isOpen) return null;

    return (
        <div style={{
            position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
            backgroundColor: 'rgba(0,0,0,0.5)', zIndex: 99998,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            padding: '24px',
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface, #fff)',
                borderRadius: 'var(--radius-lg, 12px)',
                width: '100%',
                maxWidth: '900px',
                maxHeight: '80vh',
                display: 'flex',
                flexDirection: 'column',
                boxShadow: '0 20px 60px rgba(0,0,0,0.3)',
            }}>
                {/* Header */}
                <div style={{
                    padding: '24px 28px', display: 'flex', justifyContent: 'space-between',
                    alignItems: 'center', borderBottom: '1px solid var(--color-border, #e2e8f0)',
                }}>
                    <div>
                        <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-text-primary, #1e293b)' }}>
                            Reconciliação de Itens do Catálogo
                        </h2>
                        <p style={{ margin: '4px 0 0', fontSize: '0.8125rem', color: 'var(--color-text-muted, #64748b)' }}>
                            {totalUnresolved} {totalUnresolved === 1 ? 'item precisa' : 'itens precisam'} de revisão
                        </p>
                    </div>
                    <button onClick={onClose} style={{
                        background: 'none', border: 'none', cursor: 'pointer',
                        color: 'var(--color-text-muted, #64748b)', padding: '4px',
                    }}>
                        <X size={20} />
                    </button>
                </div>

                {/* Help Section */}
                <div style={{
                    margin: '20px 28px 0', padding: '16px', backgroundColor: '#F8FAFC',
                    borderRadius: '8px', border: '1px solid #E2E8F0', display: 'flex', gap: '12px'
                }}>
                    <Info size={20} style={{ color: '#3B82F6', flexShrink: 0, marginTop: '2px' }} />
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', fontSize: '0.8125rem', color: '#475569' }}>
                        <div>
                            <strong style={{ color: '#1E293B' }}>Vincular:</strong> Use esta opção quando o item já existe no catálogo, mas o sistema não o encontrou automaticamente. Pesquise e selecione o item correto.
                        </div>
                        <div>
                            <strong style={{ color: '#1E293B' }}>Criar Novo:</strong> Use esta opção quando o item realmente não existe no catálogo. O sistema criará um novo item pendente de validação, que poderá ser usado neste pedido/cotação.
                        </div>
                    </div>
                </div>

                {/* Progress Bar */}
                <div style={{ padding: '16px 28px', borderBottom: '1px solid var(--color-border, #e2e8f0)' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px', fontSize: '0.75rem', fontWeight: 700 }}>
                        <span style={{ color: 'var(--color-text-muted, #64748b)' }}>Progresso da Revisão</span>
                        <span style={{ color: resolvedCount === totalUnresolved ? '#16A34A' : '#D97706' }}>
                            {resolvedCount} / {totalUnresolved} resolvidos
                        </span>
                    </div>
                    <div style={{ width: '100%', height: '6px', backgroundColor: '#F1F5F9', borderRadius: '3px', overflow: 'hidden' }}>
                        <div style={{
                            width: `${totalUnresolved > 0 ? (resolvedCount / totalUnresolved) * 100 : 0}%`,
                            height: '100%',
                            backgroundColor: resolvedCount === totalUnresolved ? '#16A34A' : '#4F46E5',
                            borderRadius: '3px',
                            transition: 'width 0.3s ease',
                        }} />
                    </div>
                </div>

                {/* Items Table */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '0 28px' }}>
                    <table style={{ width: '100%', fontSize: '0.8125rem', borderCollapse: 'collapse' }}>
                        <thead>
                            <tr style={{ borderBottom: '2px solid var(--color-border, #e2e8f0)' }}>
                                <th style={{ padding: '12px 8px', textAlign: 'left', fontWeight: 800, fontSize: '0.6875rem', textTransform: 'uppercase', color: 'var(--color-text-muted, #64748b)', letterSpacing: '0.05em' }}>#</th>
                                <th style={{ padding: '12px 8px', textAlign: 'left', fontWeight: 800, fontSize: '0.6875rem', textTransform: 'uppercase', color: 'var(--color-text-muted, #64748b)', letterSpacing: '0.05em' }}>Descrição do Item</th>
                                <th style={{ padding: '12px 8px', textAlign: 'center', fontWeight: 800, fontSize: '0.6875rem', textTransform: 'uppercase', color: 'var(--color-text-muted, #64748b)', letterSpacing: '0.05em' }}>Estado</th>
                                <th style={{ padding: '12px 8px', textAlign: 'center', fontWeight: 800, fontSize: '0.6875rem', textTransform: 'uppercase', color: 'var(--color-text-muted, #64748b)', letterSpacing: '0.05em' }}>Ações</th>
                            </tr>
                        </thead>
                        <tbody>
                            {unresolvedItems.map(ci => {
                                const rs = getRowState(ci.index);
                                const effectiveStatus: ReconciliationItemStatus = rs.resolved && rs.resolvedStatus ? rs.resolvedStatus : ci.status;
                                const colors = STATUS_COLORS[effectiveStatus];

                                return (
                                    <tr key={ci.index} style={{ borderBottom: '1px solid #F1F5F9' }}>
                                        <td style={{ padding: '14px 8px', fontWeight: 700, color: '#94A3B8' }}>
                                            {ci.index + 1}
                                        </td>
                                        <td style={{ padding: '14px 8px' }}>
                                            <div style={{ fontWeight: 600, color: 'var(--color-text-primary, #1e293b)', marginBottom: '4px' }}>
                                                {ci.item.description}
                                            </div>
                                            {rs.resolved && rs.linkedCatalogCode && (
                                                <div style={{ fontSize: '0.6875rem', color: '#4F46E5', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                    <Package size={10} /> {rs.linkedCatalogCode} — {rs.linkedDescription || ci.item.description}
                                                </div>
                                            )}
                                            {rs.action === 'linking' && !rs.resolved && (
                                                <div style={{ marginTop: '8px', maxWidth: '300px' }}>
                                                    <CatalogItemAutocomplete
                                                        value=""
                                                        itemCatalogId={null}
                                                        onChange={(desc, catId, catCode, defaultUnitId) => handleLink(ci.index, desc, catId, catCode, defaultUnitId)}
                                                        placeholder="Pesquisar no catálogo..."
                                                        style={{ fontSize: '0.8125rem', padding: '6px 8px', border: '1px solid #CBD5E1', borderRadius: '4px' }}
                                                    />
                                                </div>
                                            )}
                                        </td>
                                        <td style={{ padding: '14px 8px', textAlign: 'center' }}>
                                            <span style={{
                                                display: 'inline-flex', alignItems: 'center', gap: '4px',
                                                padding: '4px 10px', borderRadius: '12px',
                                                fontSize: '0.6875rem', fontWeight: 700,
                                                backgroundColor: colors.bg, color: colors.text,
                                            }}>
                                                {rs.resolved && <Check size={10} />}
                                                {STATUS_LABELS[effectiveStatus]}
                                            </span>
                                        </td>
                                        <td style={{ padding: '14px 8px', textAlign: 'center' }}>
                                            {rs.isProcessing ? (
                                                <span style={{ fontSize: '0.75rem', color: '#64748B' }}>Processando...</span>
                                            ) : rs.resolved ? (
                                                <button
                                                    onClick={() => updateRowState(ci.index, {
                                                        action: 'idle', resolved: false, resolvedStatus: undefined,
                                                        linkedCatalogId: null, linkedCatalogCode: null,
                                                        linkedDescription: undefined, defaultUnitId: null,
                                                        justification: '',
                                                    })}
                                                    style={{
                                                        background: 'none', border: 'none', cursor: 'pointer',
                                                        fontSize: '0.75rem', color: '#64748B', textDecoration: 'underline',
                                                    }}
                                                >
                                                    Desfazer
                                                </button>
                                            ) : (
                                                <div style={{ display: 'flex', gap: '6px', justifyContent: 'center', flexWrap: 'wrap' }}>
                                                    <button
                                                        onClick={() => updateRowState(ci.index, { action: 'linking' })}
                                                        title="Vincular ao catálogo"
                                                        style={{
                                                            display: 'flex', alignItems: 'center', gap: '4px',
                                                            padding: '5px 10px', borderRadius: '4px',
                                                            backgroundColor: '#EEF2FF', color: '#4F46E5',
                                                            border: '1px solid #C7D2FE', cursor: 'pointer',
                                                            fontSize: '0.6875rem', fontWeight: 600,
                                                        }}
                                                    >
                                                        <Link2 size={12} /> Vincular
                                                    </button>
                                                    <button
                                                        onClick={() => handleCreateNew(ci.index, ci.item.description)}
                                                        title="Criar novo item no catálogo"
                                                        style={{
                                                            display: 'flex', alignItems: 'center', gap: '4px',
                                                            padding: '5px 10px', borderRadius: '4px',
                                                            backgroundColor: '#F0FDF4', color: '#16A34A',
                                                            border: '1px solid #BBF7D0', cursor: 'pointer',
                                                            fontSize: '0.6875rem', fontWeight: 600,
                                                        }}
                                                    >
                                                        <PlusCircle size={12} /> Criar Novo
                                                    </button>
                                                </div>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>

                {/* Footer */}
                <div style={{
                    padding: '20px 28px', borderTop: '1px solid var(--color-border, #e2e8f0)',
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                }}>
                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted, #64748b)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                        <AlertCircle size={14} />
                        Itens criados serão marcados como "Pendente de Validação" no catálogo.
                    </div>
                    <div style={{ display: 'flex', gap: '10px' }}>
                        <button
                            onClick={onClose}
                            style={{
                                padding: '10px 20px', borderRadius: 'var(--radius-sm, 6px)',
                                backgroundColor: 'transparent', color: 'var(--color-text-muted, #64748b)',
                                border: '1px solid var(--color-border, #e2e8f0)', cursor: 'pointer',
                                fontWeight: 600, fontSize: '0.875rem',
                            }}
                        >
                            Cancelar
                        </button>
                        <button
                            onClick={handleSaveAll}
                            disabled={resolvedCount !== totalUnresolved || totalUnresolved === 0}
                            style={{
                                padding: '10px 20px', borderRadius: 'var(--radius-sm, 6px)',
                                backgroundColor: resolvedCount === totalUnresolved && totalUnresolved > 0 ? 'var(--color-primary, #4f46e5)' : '#CBD5E1',
                                color: '#fff', border: 'none', cursor: resolvedCount === totalUnresolved && totalUnresolved > 0 ? 'pointer' : 'not-allowed',
                                fontWeight: 700, fontSize: '0.875rem',
                                opacity: resolvedCount === totalUnresolved && totalUnresolved > 0 ? 1 : 0.6,
                            }}
                        >
                            Salvar Resoluções ({resolvedCount})
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}
