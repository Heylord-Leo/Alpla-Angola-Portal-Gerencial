import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    Plus, Edit3, CheckCircle, Archive, Copy,
    Eye, X, Layers, AlertTriangle, Loader2
} from 'lucide-react';
import { apiFetch, API_BASE_URL } from '../../lib/api';
import { LayoutConfigV3, parseLayoutConfig } from './badgeLayoutConfig';
import BadgeLayoutEditorV3 from './BadgeLayoutEditorV3';
import './badge-layout-designer.css';

// ─── Types ───

interface BadgeLayoutItem {
    id: string;
    name: string;
    description?: string;
    version: number;
    status: string; // DRAFT | ACTIVE | ARCHIVED
    companyCode?: string;
    badgeType?: string;
    plantCode?: string;
    createdAtUtc: string;
    activatedAtUtc?: string;
    createdByName?: string;
    hasHistory?: boolean;
}

interface BadgeLayoutDetail extends BadgeLayoutItem {
    layoutConfigJson: string;
    updatedAtUtc?: string;
    updatedByName?: string;
}

const COMPANY_OPTIONS = [
    { value: '', label: 'Todas as Empresas' },
    { value: 'ALPLAPLASTICO', label: 'Alpla Plástico' },
    { value: 'ALPLASOPRO', label: 'Alpla Sopro' },
];

const BADGE_TYPE_OPTIONS = [
    { value: '', label: 'Padrão (Funcionário)' },
    { value: 'VISITOR', label: 'Visitante' },
    { value: 'CONTRACTOR', label: 'Prestador de Serviço' },
];

const STATUS_LABELS: Record<string, { label: string; className: string }> = {
    DRAFT: { label: 'Rascunho', className: 'status-draft' },
    ACTIVE: { label: 'Activo', className: 'status-active' },
    ARCHIVED: { label: 'Arquivado', className: 'status-archived' },
};

/**
 * BadgeLayoutDesigner — Administrative screen for creating/editing badge templates.
 *
 * V2: Full element-based editor with 3-panel layout.
 *
 * Versioning workflow:
 * - New layouts start as DRAFT
 * - Only DRAFT layouts can be edited in place
 * - Publishing promotes DRAFT → ACTIVE, archiving previous ACTIVE
 * - "Nova Versão" on ACTIVE creates a DRAFT copy with Version + 1
 */
export default function BadgeLayoutDesigner() {
    const [layouts, setLayouts] = useState<BadgeLayoutItem[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Editor state
    const [isEditorOpen, setIsEditorOpen] = useState(false);
    const [editingLayout, setEditingLayout] = useState<BadgeLayoutDetail | null>(null);
    const [isNew, setIsNew] = useState(false);
    const [isSaving, setIsSaving] = useState(false);

    // New layout form state
    const [newLayoutStep, setNewLayoutStep] = useState<'form' | 'editor'>('form');
    const [formName, setFormName] = useState('');
    const [formDescription, setFormDescription] = useState('');
    const [formCompany, setFormCompany] = useState('');
    const [formBadgeType, setFormBadgeType] = useState('');

    // V2 editor config
    const [editorConfig, setEditorConfig] = useState<LayoutConfigV3>(parseLayoutConfig(null));

    // ─── Load Layouts ───
    const loadLayouts = useCallback(async () => {
        setIsLoading(true);
        setError(null);
        try {
            const res = await apiFetch(`${API_BASE_URL}/api/badges/layouts`);
            if (!res.ok) {
                const errBody = await res.json().catch(() => null);
                throw new Error(errBody?.message || `Erro ${res.status} ao carregar layouts.`);
            }
            const data = await res.json();
            setLayouts(Array.isArray(data) ? data : []);
        } catch (err: any) {
            setError(err?.message || 'Erro ao carregar layouts. Tente novamente.');
            console.error('Load layouts error:', err);
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => { loadLayouts(); }, [loadLayouts]);

    // ─── Open Editor: New Layout (2-step: form → editor) ───
    const handleCreate = () => {
        setEditingLayout(null);
        setIsNew(true);
        setFormName('');
        setFormDescription('');
        setFormCompany('');
        setFormBadgeType('');
        setEditorConfig(parseLayoutConfig(null));
        setNewLayoutStep('form');
        setIsEditorOpen(true);
    };

    // ─── Open Editor: Edit Existing ───
    const handleEdit = async (layout: BadgeLayoutItem) => {
        try {
            const res = await apiFetch(`${API_BASE_URL}/api/badges/layouts/${layout.id}`);
            const detail: BadgeLayoutDetail = await res.json();
            setEditingLayout(detail);
            setIsNew(false);
            setFormName(detail.name);
            setFormDescription(detail.description || '');
            setFormCompany(detail.companyCode || '');
            setFormBadgeType(detail.badgeType || '');

            // Parse & auto-migrate to V2
            const config = parseLayoutConfig(detail.layoutConfigJson);
            setEditorConfig(config);
            setNewLayoutStep('editor');
            setIsEditorOpen(true);
        } catch (err) {
            console.error('Load layout detail error:', err);
        }
    };

    const handleCloseEditor = () => {
        setIsEditorOpen(false);
        setEditingLayout(null);
        setIsNew(false);
        setNewLayoutStep('form');
    };

    // ─── Save from V3 Editor ───
    const handleSaveFromEditor = async (config: LayoutConfigV3) => {
        if (isNew && !formName.trim()) return;
        setIsSaving(true);

        try {
            const configJson = JSON.stringify(config);
            let res: Response;

            if (isNew) {
                res = await apiFetch(`${API_BASE_URL}/api/badges/layouts`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: formName.trim(),
                        description: formDescription.trim() || null,
                        companyCode: formCompany || null,
                        badgeType: formBadgeType || null,
                        layoutConfigJson: configJson,
                    }),
                });
            } else if (editingLayout) {
                res = await apiFetch(`${API_BASE_URL}/api/badges/layouts/${editingLayout.id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: formName.trim(),
                        description: formDescription.trim() || null,
                        companyCode: formCompany || null,
                        badgeType: formBadgeType || null,
                        layoutConfigJson: configJson,
                    }),
                });
            } else {
                return;
            }

            if (!res!.ok) {
                const errBody = await res!.json().catch(() => null);
                throw new Error(errBody?.message || `Erro ${res!.status} ao salvar layout.`);
            }

            handleCloseEditor();
            await loadLayouts();
        } catch (err: any) {
            console.error('Save layout error:', err);
            setError(err?.message || 'Erro ao salvar layout.');
            return; // Don't close editor on failure
        } finally {
            setIsSaving(false);
        }
    };

    // ─── Publish ───
    const handlePublish = async (layoutId: string) => {
        try {
            await apiFetch(`${API_BASE_URL}/api/badges/layouts/${layoutId}/publish`, {
                method: 'POST',
            });
            await loadLayouts();
        } catch (err) {
            console.error('Publish error:', err);
        }
    };

    // ─── New Draft from Active ───
    const handleNewDraft = async (layoutId: string) => {
        try {
            await apiFetch(`${API_BASE_URL}/api/badges/layouts/${layoutId}/new-draft`, {
                method: 'POST',
            });
            handleCloseEditor();
            await loadLayouts();
        } catch (err) {
            console.error('New draft error:', err);
        }
    };

    // ─── Archive ───
    const handleArchive = async (layoutId: string) => {
        try {
            await apiFetch(`${API_BASE_URL}/api/badges/layouts/${layoutId}`, {
                method: 'DELETE',
            });
            await loadLayouts();
        } catch (err) {
            console.error('Archive error:', err);
        }
    };

    // Proceed from new-layout form to editor
    const handleProceedToEditor = () => {
        if (!formName.trim()) return;
        setNewLayoutStep('editor');
    };

    return (
        <div className="bld-container">
            {/* Header */}
            <div className="bld-header">
                <div>
                    <h2 className="bld-title">
                        <Layers size={20} /> Layouts de Crachá
                    </h2>
                    <p className="bld-subtitle">
                        Crie e gerencie templates de crachás para diferentes empresas e tipos
                    </p>
                </div>
                <button className="bld-btn-primary" onClick={handleCreate}>
                    <Plus size={16} /> Novo Layout
                </button>
            </div>

            {/* Error State */}
            {error && (
                <div className="bld-error">
                    <AlertTriangle size={16} /> {error}
                </div>
            )}

            {/* Loading State */}
            {isLoading && (
                <div className="bld-loading">
                    <Loader2 size={20} className="bld-spinner" /> Carregando layouts...
                </div>
            )}

            {/* Layouts Grid */}
            {!isLoading && layouts.length === 0 && !error && (
                <div className="bld-empty">
                    <Layers size={40} strokeWidth={1} />
                    <p className="bld-empty-title">Nenhum layout criado</p>
                    <p className="bld-empty-subtitle">
                        Crie o primeiro template de crachá para começar
                    </p>
                    <button className="bld-btn-primary" onClick={handleCreate}>
                        <Plus size={16} /> Criar Primeiro Layout
                    </button>
                </div>
            )}

            {!isLoading && layouts.length > 0 && (
                <div className="bld-grid">
                    {layouts.map(layout => {
                        const statusInfo = STATUS_LABELS[layout.status] || STATUS_LABELS.DRAFT;
                        return (
                            <motion.div
                                key={layout.id}
                                className="bld-card"
                                initial={{ opacity: 0, y: 8 }}
                                animate={{ opacity: 1, y: 0 }}
                                transition={{ duration: 0.2 }}
                            >
                                <div className="bld-card-header">
                                    <div className="bld-card-title">{layout.name}</div>
                                    <span className={`bld-status ${statusInfo.className}`}>
                                        {statusInfo.label}
                                    </span>
                                </div>
                                <div className="bld-card-meta">
                                    <span>v{layout.version}</span>
                                    {layout.companyCode && <span>• {layout.companyCode}</span>}
                                    {layout.badgeType && <span>• {layout.badgeType}</span>}
                                </div>
                                <div className="bld-card-footer">
                                    <span className="bld-card-date">
                                        {new Date(layout.createdAtUtc).toLocaleDateString('pt-AO')}
                                    </span>
                                    <div className="bld-card-actions">
                                        {layout.status === 'DRAFT' && (
                                            <>
                                                <button className="bld-btn-icon" title="Editar" onClick={() => handleEdit(layout)}>
                                                    <Edit3 size={14} />
                                                </button>
                                                <button className="bld-btn-icon publish" title="Publicar" onClick={() => handlePublish(layout.id)}>
                                                    <CheckCircle size={14} />
                                                </button>
                                                <button className="bld-btn-icon danger" title="Arquivar" onClick={() => handleArchive(layout.id)}>
                                                    <Archive size={14} />
                                                </button>
                                            </>
                                        )}
                                        {layout.status === 'ACTIVE' && (
                                            <>
                                                {!layout.hasHistory ? (
                                                    <button className="bld-btn-icon" title="Editar Layout Diretamente" onClick={() => handleEdit(layout)}>
                                                        <Edit3 size={14} />
                                                    </button>
                                                ) : (
                                                    <button className="bld-btn-icon" title="Visualizar" onClick={() => handleEdit(layout)}>
                                                        <Eye size={14} />
                                                    </button>
                                                )}
                                                <button className="bld-btn-icon" title="Nova Versão (Rascunho)" onClick={() => handleNewDraft(layout.id)}>
                                                    <Copy size={14} />
                                                </button>
                                                <button className="bld-btn-icon danger" title="Arquivar/Excluir" onClick={() => handleArchive(layout.id)}>
                                                    <Archive size={14} />
                                                </button>
                                            </>
                                        )}
                                        {layout.status === 'ARCHIVED' && (
                                            <button className="bld-btn-icon" title="Visualizar" onClick={() => handleEdit(layout)}>
                                                <Eye size={14} />
                                            </button>
                                        )}
                                    </div>
                                </div>
                            </motion.div>
                        );
                    })}
                </div>
            )}

            {/* ── V2 Editor ── */}
            <AnimatePresence>
                {isEditorOpen && (
                    <>
                        {/* Step 1: New layout form (name, description, scope) */}
                        {isNew && newLayoutStep === 'form' && (
                            <motion.div
                                className="bld-editor-overlay"
                                initial={{ opacity: 0 }}
                                animate={{ opacity: 1 }}
                                exit={{ opacity: 0 }}
                                onClick={handleCloseEditor}
                            >
                                <motion.div
                                    className="bld-editor-panel"
                                    initial={{ x: '100%' }}
                                    animate={{ x: 0 }}
                                    exit={{ x: '100%' }}
                                    transition={{ type: 'spring', stiffness: 300, damping: 30 }}
                                    onClick={e => e.stopPropagation()}
                                    style={{ width: 'min(90vw, 480px)' }}
                                >
                                    <div className="bld-editor-header">
                                        <h3>Novo Layout</h3>
                                        <button className="bld-btn-icon" onClick={handleCloseEditor}>
                                            <X size={18} />
                                        </button>
                                    </div>
                                    <div className="bld-editor-form" style={{ padding: '20px' }}>
                                        <div className="bld-form-section">
                                            <h4>Informações Gerais</h4>
                                            <div className="bld-form-field">
                                                <label>Nome do Layout *</label>
                                                <input
                                                    value={formName}
                                                    onChange={e => setFormName(e.target.value)}
                                                    placeholder="Ex: Crachá Padrão Plástico"
                                                    autoFocus
                                                />
                                            </div>
                                            <div className="bld-form-field">
                                                <label>Descrição</label>
                                                <input
                                                    value={formDescription}
                                                    onChange={e => setFormDescription(e.target.value)}
                                                    placeholder="Descrição opcional"
                                                />
                                            </div>
                                        </div>

                                        <div className="bld-form-section">
                                            <h4>Escopo de Activação</h4>
                                            <div className="bld-form-field">
                                                <label>Empresa</label>
                                                <select value={formCompany} onChange={e => setFormCompany(e.target.value)}>
                                                    {COMPANY_OPTIONS.map(opt => (
                                                        <option key={opt.value} value={opt.value}>{opt.label}</option>
                                                    ))}
                                                </select>
                                            </div>
                                            <div className="bld-form-field">
                                                <label>Tipo de Crachá</label>
                                                <select value={formBadgeType} onChange={e => setFormBadgeType(e.target.value)}>
                                                    {BADGE_TYPE_OPTIONS.map(opt => (
                                                        <option key={opt.value} value={opt.value}>{opt.label}</option>
                                                    ))}
                                                </select>
                                            </div>
                                        </div>

                                        <div className="bld-editor-actions">
                                            <button className="bld-btn-secondary" onClick={handleCloseEditor}>
                                                Cancelar
                                            </button>
                                            <button
                                                className="bld-btn-primary"
                                                onClick={handleProceedToEditor}
                                                disabled={!formName.trim()}
                                            >
                                                Continuar para o Editor →
                                            </button>
                                        </div>
                                    </div>
                                </motion.div>
                            </motion.div>
                        )}

                        {/* Step 2: V3 Element Editor (full-page) */}
                        {(newLayoutStep === 'editor' || !isNew) && (
                            <motion.div
                                initial={{ opacity: 0 }}
                                animate={{ opacity: 1 }}
                                exit={{ opacity: 0 }}
                            >
                                <BadgeLayoutEditorV3
                                    config={editorConfig}
                                    layoutName={isNew ? formName : (editingLayout?.name || '')}
                                    layoutStatus={isNew ? 'DRAFT' : (editingLayout?.status || 'DRAFT')}
                                    hasHistory={!isNew ? editingLayout?.hasHistory : undefined}
                                    isNew={isNew}
                                    onSave={handleSaveFromEditor}
                                    onClose={handleCloseEditor}
                                    onCreateDraft={editingLayout ? () => handleNewDraft(editingLayout.id) : undefined}
                                    isSaving={isSaving}
                                />
                            </motion.div>
                        )}
                    </>
                )}
            </AnimatePresence>
        </div>
    );
}
