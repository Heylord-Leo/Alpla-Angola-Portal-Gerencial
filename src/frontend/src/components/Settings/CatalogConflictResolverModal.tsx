import React, { useState, useCallback, useEffect, useRef } from 'react';
import { api } from '../../lib/api';
import {
    CatalogSyncPreviewItemDto,
    CatalogConflictResolution,
    CatalogResolveConflictRequestDto,
    CatalogResolveConflictResultDto
} from '../../types';
import {
    X,
    AlertTriangle,
    RefreshCw,
    Link2,
    PlusCircle,
    Search,
    CheckCircle2,
    Loader2,
    ArrowRight,
    Info
} from 'lucide-react';

// ─── Resolution strategy configuration ──────────────────────────────────

interface StrategyConfig {
    key: CatalogConflictResolution;
    label: string;
    description: string;
    icon: React.ReactNode;
    requiresPortalItem: boolean;
    requiresManualSearch: boolean;
    requiresFieldSelection: boolean;
}

const STRATEGIES: StrategyConfig[] = [
    {
        key: 'UpdatePortal',
        label: 'Atualizar Portal com dados do Primavera',
        description: 'Atualiza campos selecionados no item Portal existente com os dados do Primavera.',
        icon: <RefreshCw size={16} />,
        requiresPortalItem: true,
        requiresManualSearch: false,
        requiresFieldSelection: true,
    },
    {
        key: 'ConfirmAssociation',
        label: 'Confirmar Associação',
        description: 'Vincula o código Primavera ao item Portal sem alterar os dados existentes.',
        icon: <Link2 size={16} />,
        requiresPortalItem: true,
        requiresManualSearch: false,
        requiresFieldSelection: false,
    },
    {
        key: 'CreateNew',
        label: 'Criar Novo Item no Portal',
        description: 'Cria um novo item de catálogo no Portal com os dados do Primavera.',
        icon: <PlusCircle size={16} />,
        requiresPortalItem: false,
        requiresManualSearch: false,
        requiresFieldSelection: false,
    },
    {
        key: 'AssociateManually',
        label: 'Associar a Outro Item',
        description: 'Busca e vincula o código Primavera a um item diferente do Portal.',
        icon: <Search size={16} />,
        requiresPortalItem: false,
        requiresManualSearch: true,
        requiresFieldSelection: false,
    },
];

// ─── Field selection configuration ──────────────────────────────────────

interface FieldOption {
    key: string;
    label: string;
    primaveraAccessor: (item: CatalogSyncPreviewItemDto) => string | null | undefined;
    portalAccessor: (item: CatalogSyncPreviewItemDto) => string | null | undefined;
}

const UPDATE_FIELDS: FieldOption[] = [
    {
        key: 'Description',
        label: 'Descrição',
        primaveraAccessor: (i) => i.primaveraDescription,
        portalAccessor: (i) => i.portalDescription,
    },
    {
        key: 'Category',
        label: 'Família / Categoria',
        primaveraAccessor: (i) => i.primaveraFamily,
        portalAccessor: () => null, // Portal category not in preview DTO
    },
    {
        key: 'Unit',
        label: 'Unidade',
        primaveraAccessor: (i) => i.primaveraBaseUnit,
        portalAccessor: () => null,
    },
    {
        key: 'PrimaveraCode',
        label: 'Vincular Código Primavera',
        primaveraAccessor: (i) => i.primaveraCode,
        portalAccessor: () => null,
    },
];

// ─── Search result type ─────────────────────────────────────────────────

interface CatalogSearchResult {
    id: number;
    code: string;
    description: string;
    primaveraCode?: string | null;
    isActive: boolean;
}

// ─── Component Props ────────────────────────────────────────────────────

interface CatalogConflictResolverModalProps {
    isOpen: boolean;
    onClose: () => void;
    onResolved: () => void;
    item: CatalogSyncPreviewItemDto | null;
    companyId: number;
}

// ─── Component ──────────────────────────────────────────────────────────

export function CatalogConflictResolverModal({
    isOpen,
    onClose,
    onResolved,
    item,
    companyId
}: CatalogConflictResolverModalProps) {
    // ── State ──
    const [resolution, setResolution] = useState<CatalogConflictResolution | null>(null);
    const [selectedFields, setSelectedFields] = useState<Set<string>>(new Set());
    const [manualSearchQuery, setManualSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<CatalogSearchResult[]>([]);
    const [searchLoading, setSearchLoading] = useState(false);
    const [selectedManualItem, setSelectedManualItem] = useState<CatalogSearchResult | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    // ── Reset on open ──
    useEffect(() => {
        if (isOpen) {
            setResolution(null);
            setSelectedFields(new Set());
            setManualSearchQuery('');
            setSearchResults([]);
            setSelectedManualItem(null);
            setSubmitting(false);
            setError(null);
            setSuccessMessage(null);
        }
    }, [isOpen]);

    // ── Manual search with debounce ──
    useEffect(() => {
        if (resolution !== 'AssociateManually' || manualSearchQuery.trim().length < 2) {
            setSearchResults([]);
            return;
        }

        if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        searchTimerRef.current = setTimeout(async () => {
            setSearchLoading(true);
            try {
                const results = await api.catalogItems.search(manualSearchQuery.trim(), 15);
                setSearchResults(results);
            } catch {
                setSearchResults([]);
            } finally {
                setSearchLoading(false);
            }
        }, 350);

        return () => {
            if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        };
    }, [manualSearchQuery, resolution]);

    // ── Field toggle ──
    const toggleField = useCallback((field: string) => {
        setSelectedFields(prev => {
            const next = new Set(prev);
            if (next.has(field)) next.delete(field);
            else next.add(field);
            return next;
        });
    }, []);

    // ── Derived state ──
    const hasPortalMatch = item?.portalItemId != null;

    const canConfirm = (() => {
        if (!resolution || !item) return false;
        if (resolution === 'UpdatePortal') return selectedFields.size > 0 && hasPortalMatch;
        if (resolution === 'ConfirmAssociation') return hasPortalMatch;
        if (resolution === 'CreateNew') return true;
        if (resolution === 'AssociateManually') return selectedManualItem !== null;
        return false;
    })();

    // ── Preview text ──
    const previewText = (() => {
        if (!resolution || !item) return null;

        switch (resolution) {
            case 'UpdatePortal': {
                const fields = Array.from(selectedFields)
                    .map(f => UPDATE_FIELDS.find(uf => uf.key === f)?.label ?? f)
                    .join(', ');
                return `Os seguintes campos do item Portal #${item.portalItemId} (${item.portalCode}) serão atualizados: ${fields}.`;
            }
            case 'ConfirmAssociation':
                return `O código Primavera '${item.primaveraCode}' será vinculado ao item Portal #${item.portalItemId} (${item.portalCode}) sem alterar dados.`;
            case 'CreateNew':
                return `Um novo item de catálogo será criado (código ITM-XXXXX) com a descrição '${item.primaveraDescription ?? item.primaveraCode}'.`;
            case 'AssociateManually':
                if (!selectedManualItem) return 'Selecione um item do Portal para vincular.';
                return `O código Primavera '${item.primaveraCode}' será vinculado ao item Portal #${selectedManualItem.id} (${selectedManualItem.code}: ${selectedManualItem.description}).`;
            default:
                return null;
        }
    })();

    // ── Submit ──
    const handleConfirm = async () => {
        if (!item || !resolution || !canConfirm) return;
        setSubmitting(true);
        setError(null);
        setSuccessMessage(null);

        const body: CatalogResolveConflictRequestDto = {
            primaveraCode: item.primaveraCode,
            resolution,
            portalItemId: (resolution === 'UpdatePortal' || resolution === 'ConfirmAssociation')
                ? item.portalItemId
                : undefined,
            targetPortalItemId: resolution === 'AssociateManually'
                ? selectedManualItem?.id
                : undefined,
            primaveraDescription: item.primaveraDescription,
            primaveraFamily: item.primaveraFamily,
            primaveraBaseUnit: item.primaveraBaseUnit,
            updateFields: resolution === 'UpdatePortal'
                ? Array.from(selectedFields)
                : undefined,
        };

        try {
            const result: CatalogResolveConflictResultDto = await api.sync.catalog.resolveConflict(companyId, body);
            if (result.success) {
                setSuccessMessage(result.message);
                setTimeout(() => {
                    onResolved();
                    onClose();
                }, 1200);
            } else {
                setError(result.message || 'Erro desconhecido ao resolver conflito.');
            }
        } catch (err: any) {
            setError(err?.message || 'Falha na comunicação com o servidor.');
        } finally {
            setSubmitting(false);
        }
    };

    // ── Don't render if not open or no item ──
    if (!isOpen || !item) return null;

    // ── Comparison rows ──
    const comparisonRows = [
        { label: 'Código', primavera: item.primaveraCode, portal: item.portalCode },
        { label: 'Descrição', primavera: item.primaveraDescription, portal: item.portalDescription },
        { label: 'Família / Categoria', primavera: item.primaveraFamily, portal: null },
        { label: 'Unidade', primavera: item.primaveraBaseUnit, portal: null },
    ];

    return (
        <div className="conflict-resolver-overlay" onClick={onClose}>
            <div className="conflict-resolver-container" onClick={e => e.stopPropagation()}>
                {/* ── Header ── */}
                <div className="conflict-resolver-header">
                    <div className="conflict-resolver-header-title">
                        <AlertTriangle size={20} className="conflict-resolver-icon-amber" />
                        <h2>Resolver Conflito de Catálogo</h2>
                    </div>
                    <button
                        className="conflict-resolver-close-btn"
                        onClick={onClose}
                        type="button"
                        title="Fechar"
                    >
                        <X size={18} />
                    </button>
                </div>

                {/* ── Context line ── */}
                <div className="conflict-resolver-context">
                    <span className="conflict-resolver-context-code">{item.primaveraCode}</span>
                    {item.conflictDetail && (
                        <span className="conflict-resolver-context-detail">{item.conflictDetail}</span>
                    )}
                </div>

                {/* ── Side-by-side comparison ── */}
                <div className="conflict-resolver-comparison">
                    <div className="conflict-resolver-comparison-header">
                        <div className="conflict-resolver-col-label conflict-resolver-col-primavera">
                            🟠 Primavera
                        </div>
                        <div className="conflict-resolver-col-label conflict-resolver-col-portal">
                            🔵 Portal
                        </div>
                    </div>
                    {comparisonRows.map(row => {
                        const isDiff = row.primavera && row.portal &&
                            row.primavera.trim().toLowerCase() !== row.portal.trim().toLowerCase();
                        return (
                            <div key={row.label} className="conflict-resolver-comparison-row">
                                <div className="conflict-resolver-row-label">{row.label}</div>
                                <div className={`conflict-resolver-row-value conflict-resolver-val-primavera ${isDiff ? 'conflict-resolver-diff' : ''}`}>
                                    {row.primavera ?? '—'}
                                </div>
                                <div className={`conflict-resolver-row-value conflict-resolver-val-portal ${isDiff ? 'conflict-resolver-diff' : ''}`}>
                                    {row.portal ?? '—'}
                                </div>
                            </div>
                        );
                    })}
                </div>

                {/* ── Strategy selector ── */}
                <div className="conflict-resolver-strategies">
                    <div className="conflict-resolver-section-label">Ação de Resolução</div>
                    <div className="conflict-resolver-strategy-list">
                        {STRATEGIES.map(strategy => {
                            // Hide UpdatePortal and ConfirmAssociation if no Portal match
                            if (strategy.requiresPortalItem && !hasPortalMatch) return null;
                            const isSelected = resolution === strategy.key;
                            return (
                                <label
                                    key={strategy.key}
                                    className={`conflict-resolver-strategy-option ${isSelected ? 'conflict-resolver-strategy-selected' : ''}`}
                                >
                                    <input
                                        type="radio"
                                        name="resolution"
                                        value={strategy.key}
                                        checked={isSelected}
                                        onChange={() => {
                                            setResolution(strategy.key);
                                            setError(null);
                                            setSelectedManualItem(null);
                                            setManualSearchQuery('');
                                        }}
                                    />
                                    <span className="conflict-resolver-strategy-icon">{strategy.icon}</span>
                                    <div className="conflict-resolver-strategy-text">
                                        <span className="conflict-resolver-strategy-label">{strategy.label}</span>
                                        <span className="conflict-resolver-strategy-desc">{strategy.description}</span>
                                    </div>
                                </label>
                            );
                        })}
                    </div>
                </div>

                {/* ── Field selection (UpdatePortal only) ── */}
                {resolution === 'UpdatePortal' && (
                    <div className="conflict-resolver-fields">
                        <div className="conflict-resolver-section-label">Campos a Atualizar</div>
                        <div className="conflict-resolver-field-list">
                            {UPDATE_FIELDS.map(field => {
                                const primVal = field.primaveraAccessor(item);
                                if (!primVal && field.key !== 'PrimaveraCode') return null; // Skip fields with no Primavera data
                                return (
                                    <label key={field.key} className="conflict-resolver-field-option">
                                        <input
                                            type="checkbox"
                                            checked={selectedFields.has(field.key)}
                                            onChange={() => toggleField(field.key)}
                                        />
                                        <span className="conflict-resolver-field-label">{field.label}</span>
                                        {primVal && (
                                            <span className="conflict-resolver-field-value">
                                                <ArrowRight size={12} />
                                                {primVal}
                                            </span>
                                        )}
                                    </label>
                                );
                            })}
                        </div>
                    </div>
                )}

                {/* ── Manual search (AssociateManually only) ── */}
                {resolution === 'AssociateManually' && (
                    <div className="conflict-resolver-manual-search">
                        <div className="conflict-resolver-section-label">Buscar Item no Portal</div>
                        <div className="conflict-resolver-search-input-wrapper">
                            <Search size={14} />
                            <input
                                type="text"
                                className="conflict-resolver-search-input"
                                placeholder="Buscar por código ou descrição..."
                                value={manualSearchQuery}
                                onChange={e => {
                                    setManualSearchQuery(e.target.value);
                                    setSelectedManualItem(null);
                                }}
                            />
                            {searchLoading && <Loader2 size={14} className="sync-spin" />}
                        </div>
                        {searchResults.length > 0 && (
                            <div className="conflict-resolver-search-results">
                                {searchResults.map(r => (
                                    <button
                                        key={r.id}
                                        type="button"
                                        className={`conflict-resolver-search-result ${selectedManualItem?.id === r.id ? 'conflict-resolver-search-result-selected' : ''}`}
                                        onClick={() => setSelectedManualItem(r)}
                                    >
                                        <span className="conflict-resolver-search-result-code">{r.code}</span>
                                        <span className="conflict-resolver-search-result-desc">{r.description}</span>
                                        {r.primaveraCode && (
                                            <span className="conflict-resolver-search-result-prim">
                                                Prim: {r.primaveraCode}
                                            </span>
                                        )}
                                    </button>
                                ))}
                            </div>
                        )}
                        {selectedManualItem && (
                            <div className="conflict-resolver-selected-item">
                                <CheckCircle2 size={14} />
                                <span>Selecionado: <strong>{selectedManualItem.code}</strong> — {selectedManualItem.description}</span>
                            </div>
                        )}
                    </div>
                )}

                {/* ── Preview summary ── */}
                {previewText && canConfirm && !successMessage && (
                    <div className="conflict-resolver-preview">
                        <Info size={14} />
                        <span>{previewText}</span>
                    </div>
                )}

                {/* ── Error / Success banners ── */}
                {error && (
                    <div className="conflict-resolver-error">
                        <AlertTriangle size={14} />
                        <span>{error}</span>
                    </div>
                )}
                {successMessage && (
                    <div className="conflict-resolver-success">
                        <CheckCircle2 size={14} />
                        <span>{successMessage}</span>
                    </div>
                )}

                {/* ── Footer ── */}
                <div className="conflict-resolver-footer">
                    <button
                        type="button"
                        className="sync-btn sync-btn-ghost"
                        onClick={onClose}
                        disabled={submitting}
                    >
                        Cancelar
                    </button>
                    <button
                        type="button"
                        className="sync-btn sync-btn-primary"
                        onClick={handleConfirm}
                        disabled={!canConfirm || submitting || !!successMessage}
                    >
                        {submitting
                            ? <><Loader2 size={16} className="sync-spin" /> Processando...</>
                            : <><CheckCircle2 size={16} /> Confirmar Resolução</>
                        }
                    </button>
                </div>
            </div>
        </div>
    );
}
