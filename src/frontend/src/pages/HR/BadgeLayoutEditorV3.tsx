import React, { useState, useRef, useCallback, useMemo } from 'react';
import {
    Save, Loader2, Eye, EyeOff, Upload, Trash2,
    Copy, Lock, MousePointer, ArrowLeft,
    PanelTop, Image, Building, Camera, RectangleHorizontal,
    Type, FolderOpen, Tag, PanelBottom, Hash, CreditCard, Building2,
    ZoomIn, ZoomOut, Maximize, Smartphone, Monitor, FileText
} from 'lucide-react';
import { BadgePreview, BadgeData } from './BadgePreview';
import {
    LayoutConfigV3, TextElement, ImageElement, ShapeElement,
    ElementKey, ELEMENT_KEYS, ELEMENT_LABELS,
    FONT_OPTIONS, BadgeElement,
    validateLogoFile, MAX_LOGO_SIZE_BYTES, ACCEPTED_LOGO_TYPES,
} from './badgeLayoutConfig';
import './badge-layout-editor-v3.css';

// ─── Icon Map ───

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const ICON_MAP: Record<string, React.ComponentType<any>> = {
    PanelTop, Image, Building, Camera, RectangleHorizontal,
    Type, FolderOpen, Tag, PanelBottom, Hash, CreditCard, Building2, FileText,
};

const ELEMENT_ICON_KEYS: Record<ElementKey, string> = {
    header: 'PanelTop',
    logo: 'Image',
    companyName: 'Building',
    photo: 'Camera',
    nameBlock: 'RectangleHorizontal',
    name: 'Type',
    department: 'FolderOpen',
    category: 'Tag',
    footer: 'PanelBottom',
    footerLabelEmpCode: 'Hash',
    footerValueEmpCode: 'Hash',
    footerLabelCard: 'CreditCard',
    footerValueCard: 'CreditCard',
    footerLabelCompany: 'Building2',
    footerValueCompany: 'Building2',
    footerLabelDoc: 'FileText',
    footerValueDoc: 'FileText',
};

// ─── Sample Data ───

const SAMPLE_DATA: BadgeData = {
    firstName: 'Luiana Daniela',
    lastName: 'Carvalho',
    fullName: 'LUIANA DANIELA ABRIGADA DE BRITO DA ROCHA CARVALHO',
    department: 'Produção',
    category: 'Operadora Especializada de Linha',
    employeeCode: 'EMP-LL-123',
    cardNumber: '554433221',
    company: 'ALPLAPLASTICO',
    photoUrl: null,
    documentLabel: 'B.I.',
    documentValue: '000123456LA042',
};

// ─── Text Alignment Options ───

const TEXT_ALIGN_OPTIONS = [
    { value: 'left', label: 'Esquerda' },
    { value: 'center', label: 'Centro' },
    { value: 'right', label: 'Direita' },
];

const TEXT_TRANSFORM_OPTIONS = [
    { value: '', label: 'Normal' },
    { value: 'uppercase', label: 'MAIÚSCULAS' },
    { value: 'lowercase', label: 'minúsculas' },
    { value: 'capitalize', label: 'Capitalizar' },
];

const FONT_WEIGHT_OPTIONS = [
    { value: 400, label: 'Normal (400)' },
    { value: 500, label: 'Médio (500)' },
    { value: 600, label: 'Semi-Bold (600)' },
    { value: 700, label: 'Bold (700)' },
    { value: 800, label: 'Extra-Bold (800)' },
    { value: 900, label: 'Black (900)' },
];

// ─── Props ───

interface BadgeLayoutEditorV3Props {
    config: LayoutConfigV3;
    layoutName: string;
    layoutStatus: string;            // DRAFT | ACTIVE | ARCHIVED
    hasHistory?: boolean;
    isNew: boolean;
    onSave: (config: LayoutConfigV3) => void;
    onClose: () => void;
    onCreateDraft?: () => void;      // For ACTIVE layouts: create new draft version
    isSaving: boolean;
}

/**
 * BadgeLayoutEditorV3 — Absolute Positioning Canvas Editor
 *
 * Panels:
 * 1. Elements List (left)  — select & toggle visibility
 * 2. Canvas Panel (center) — toolbar, live preview scaled visually, drag-and-drop elements
 * 3. Properties (right)    — per-element positioning and styling controls
 */
export default function BadgeLayoutEditorV3({
    config: initialConfig,
    layoutName,
    layoutStatus,
    hasHistory,
    isNew,
    onSave,
    onClose,
    onCreateDraft,
    isSaving,
}: BadgeLayoutEditorV3Props) {

    const [config, setConfig] = useState<LayoutConfigV3>(() => structuredClone(initialConfig));
    const [selectedKey, setSelectedKey] = useState<ElementKey | null>(null);
    const [logoError, setLogoError] = useState<string | null>(null);
    
    // Canvas Controls
    const [zoom, setZoom] = useState<number>(100);
    const [showGuides, setShowGuides] = useState<boolean>(true);

    const logoInputRef = useRef<HTMLInputElement>(null);

    const isReadOnly = (layoutStatus !== 'DRAFT' && layoutStatus !== 'ACTIVE' && !isNew) || (layoutStatus === 'ACTIVE' && !!hasHistory);

    // ─── Element Update Helpers ───

    const updateElement = useCallback((
        key: string, prop: string, value: any
    ) => {
        setConfig(prev => {
            const next = structuredClone(prev);
            const elKey = key as ElementKey;
            if (next.elements[elKey]) {
                (next.elements[elKey] as any)[prop] = value;
            }
            return next;
        });
    }, []);
    
    const applyElementUpdates = useCallback((key: ElementKey, updates: Partial<BadgeElement>) => {
        setConfig(prev => {
            const next = structuredClone(prev);
            if (next.elements[key]) {
                // Merge updates into the specific element
                next.elements[key] = { ...next.elements[key], ...updates } as any;
            }
            return next;
        });
    }, []);

    const toggleVisibility = useCallback((key: string) => {
        setConfig(prev => {
            const next = structuredClone(prev);
            const elKey = key as ElementKey;
            if (next.elements[elKey]) {
                next.elements[elKey]!.visible = !next.elements[elKey]!.visible;
            }
            return next;
        });
    }, []);

    // ─── Configuration Changes ───
    
    const setOrientation = useCallback((orientation: 'vertical' | 'horizontal') => {
        setConfig(prev => ({ ...prev, orientation }));
    }, []);

    // ─── Logo Upload ───

    const handleLogoUpload = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        setLogoError(null);
        const validation = validateLogoFile(file);
        if (!validation.valid) {
            setLogoError(validation.error || 'Ficheiro inválido');
            e.target.value = '';
            return;
        }

        const reader = new FileReader();
        reader.onload = (ev) => {
            const dataUrl = ev.target?.result as string;
            updateElement('logo', 'customLogoDataUrl', dataUrl);
        };
        reader.readAsDataURL(file);
        e.target.value = '';
    }, [updateElement]);

    const removeLogo = useCallback(() => {
        updateElement('logo', 'customLogoDataUrl', null);
        setLogoError(null);
    }, [updateElement]);

    // ─── Save ───

    const handleSave = useCallback(() => {
        onSave(config);
    }, [config, onSave]);

    // ─── Preview Badge Data ───

    const previewData: BadgeData = useMemo(() => {
        const els = config.elements;
        return {
            ...SAMPLE_DATA,
            // Example rules: if specific fields are hidden in editor, don't show data to reflect realism
            department: els.department?.visible !== false ? SAMPLE_DATA.department : undefined,
            category: els.category?.visible !== false ? SAMPLE_DATA.category : undefined,
            cardNumber: els.footerValueCard?.visible !== false ? SAMPLE_DATA.cardNumber : undefined,
            company: els.footerValueCompany?.visible !== false ? SAMPLE_DATA.company : undefined,
        };
    }, [config]);

    // ─── Element Click from Preview ───

    const handleElementClick = useCallback((key: ElementKey) => {
        setSelectedKey(key);
    }, []);

    // ─── Selected Element ───

    const selectedElement = selectedKey ? config.elements[selectedKey] : null;
    
    // Zoom levels mapping
    const handleZoomIn = () => setZoom(z => Math.min(z + 25, 200));
    const handleZoomOut = () => setZoom(z => Math.max(z - 25, 50));
    const handleZoomReset = () => setZoom(100);

    // ─── Viewport Calculation ───
    const CM_TO_PX = 37.7952755906; // Standard 96 DPI CSS approximation (96 / 2.54)
    const baseWidthPx = config.orientation === 'vertical' ? 5.4 * CM_TO_PX : 8.6 * CM_TO_PX;
    const baseHeightPx = config.orientation === 'vertical' ? 8.6 * CM_TO_PX : 5.4 * CM_TO_PX;

    // The scrollable surface needs to logically grow to trigger CSS overflow correctly
    const scaledWidth = baseWidthPx * (zoom / 100);
    const scaledHeight = baseHeightPx * (zoom / 100);

    // ─── Render ───

    return (
        <div className="ble-overlay">
            {/* Top Action Bar — always visible */}
            <div className="ble-topbar">
                <button className="ble-btn-back" onClick={onClose} title="Voltar">
                    <ArrowLeft size={18} />
                </button>
                <div className="ble-topbar-title">
                    <h2>{isNew ? 'Novo Layout V3' : layoutName}</h2>
                    {!isNew && (
                        <span className={`ble-topbar-status status-${layoutStatus.toLowerCase()}`}>
                            {layoutStatus === 'DRAFT' ? 'Rascunho' : layoutStatus === 'ACTIVE' ? 'Activo' : 'Arquivado'}
                        </span>
                    )}
                </div>
                <div className="ble-topbar-actions">
                    {isReadOnly && onCreateDraft && layoutStatus === 'ACTIVE' && (
                        <button className="ble-btn-ghost" onClick={onCreateDraft}>
                            <Copy size={14} /> Criar Nova Versão
                        </button>
                    )}
                    {!isReadOnly && (
                        <button
                            className="ble-btn-primary"
                            onClick={handleSave}
                            disabled={isSaving}
                        >
                            {isSaving ? (
                                <><Loader2 size={14} className="ble-spinner" /> Salvando...</>
                            ) : (
                                <><Save size={14} /> Salvar Rascunho</>
                            )}
                        </button>
                    )}
                </div>
            </div>

            {/* Read-Only Banner */}
            {isReadOnly && (
                <div className="ble-readonly-banner">
                    <Lock size={14} />
                    Layout {layoutStatus === 'ACTIVE' ? 'activo' : 'arquivado'} — apenas visualização.
                </div>
            )}

            {/* 3-Panel Main */}
            <div className="ble-main">
                {/* ── Left: Elements List ── */}
                <div className="ble-elements-panel">
                    <div className="ble-elements-title">Lista de Elementos</div>
                    {ELEMENT_KEYS.map((key: ElementKey) => {
                        const el = config.elements[key];
                        if (!el) return null;
                        const IconComp = ICON_MAP[ELEMENT_ICON_KEYS[key]] || Type;
                        const isHidden = el.visible === false;
                        const isSelected = selectedKey === key;

                        return (
                            <div
                                key={key}
                                className={`ble-element-item${isSelected ? ' selected' : ''}${isHidden ? ' hidden-el' : ''}`}
                                onClick={() => setSelectedKey(key)}
                            >
                                <IconComp size={14} />
                                <span className="ble-element-label">{ELEMENT_LABELS[key]}</span>
                                <button
                                    className="ble-element-visibility"
                                    title={isHidden ? 'Mostrar' : 'Ocultar'}
                                    disabled={isReadOnly}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        if (!isReadOnly) toggleVisibility(key);
                                    }}
                                >
                                    {isHidden ? <EyeOff size={13} /> : <Eye size={13} />}
                                </button>
                            </div>
                        );
                    })}
                </div>

                {/* ── Center: Canvas ── */}
                <div className="ble-preview-panel">
                    <div className="ble-canvas-toolbar">
                        {!isReadOnly && (
                            <>
                                <button 
                                    className={`ble-btn-ghost ble-btn-sm ${config.orientation === 'vertical' ? 'selected' : ''}`}
                                    onClick={() => setOrientation('vertical')}
                                    style={{ background: config.orientation === 'vertical' ? 'rgba(37,99,235,0.1)' : 'transparent' }}
                                >
                                    <Smartphone size={14} /> Vertical
                                </button>
                                <button 
                                    className={`ble-btn-ghost ble-btn-sm ${config.orientation === 'horizontal' ? 'selected' : ''}`}
                                    onClick={() => setOrientation('horizontal')}
                                    style={{ background: config.orientation === 'horizontal' ? 'rgba(37,99,235,0.1)' : 'transparent' }}
                                >
                                    <Monitor size={14} /> Horizontal
                                </button>
                                <div style={{width: '1px', height: '16px', background: 'var(--color-border)', margin: '0 8px'}} />
                                
                                <div className="ble-prop-toggle" style={{ marginRight: '8px' }}>
                                    <input 
                                        type="checkbox" 
                                        id="show-guides" 
                                        checked={showGuides} 
                                        onChange={(e) => setShowGuides(e.target.checked)} 
                                    />
                                    <label htmlFor="show-guides">Guias</label>
                                </div>
                            </>
                        )}
                        <span style={{ fontSize: '0.78rem', fontWeight: 600, color: 'var(--color-text-muted)', minWidth: '40px', textAlign: 'center' }}>
                            {zoom}%
                        </span>
                        <button className="ble-btn-ghost ble-btn-sm" onClick={handleZoomOut} disabled={zoom <= 50}><ZoomOut size={16} /></button>
                        <button className="ble-btn-ghost ble-btn-sm" onClick={handleZoomReset}><Maximize size={14} /></button>
                        <button className="ble-btn-ghost ble-btn-sm" onClick={handleZoomIn} disabled={zoom >= 200}><ZoomIn size={16} /></button>
                    </div>

                    <div className="ble-canvas-scroll-area">
                        <div 
                            className="ble-canvas-surface"
                            style={{
                                width: scaledWidth,
                                height: scaledHeight,
                                position: 'relative',
                                flexShrink: 0,
                                margin: 'auto', // Forces clean centering while fitting, and ensures no top/left clipping
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center'
                            }}
                            onClick={() => setSelectedKey(null)}
                        >
                            <div 
                                className="ble-canvas-viewport"
                                style={{ transform: `scale(${zoom / 100})` }}
                            >
                                <div onClick={(e) => e.stopPropagation()}>
                                    <BadgePreview
                                        data={previewData}
                                        layoutConfig={config}
                                        isEditing={!isReadOnly}
                                        onElementClick={handleElementClick}
                                        onElementChange={applyElementUpdates}
                                        selectedElement={selectedKey || undefined}
                                        zoom={zoom}
                                    />
                                </div>
                                
                                {/* Editor Overlay Guides */}
                                {showGuides && !isReadOnly && (
                                    <div className="ble-guides-overlay">
                                        <div className="ble-guide-safe-area" title="Margem de Segurança (3mm)" />
                                        <div className="ble-guide-line vertical" />
                                        <div className="ble-guide-line horizontal" />
                                    </div>
                                )}
                            </div>
                        </div>
                    </div>
                </div>

                {/* ── Right: Properties Panel ── */}
                <div className="ble-props-panel">
                    {!selectedElement ? (
                        <div className="ble-props-empty">
                            <MousePointer size={32} strokeWidth={1} />
                            <p>Selecione um elemento para ajustar propriedades absolutas e personalização.</p>
                        </div>
                    ) : (
                        <>
                            <div className="ble-props-header">
                                <h3>{ELEMENT_LABELS[selectedKey!]}</h3>
                                <span className={`ble-props-type-badge type-${selectedElement.type}`}>
                                    {selectedElement.type === 'text' ? 'Texto' : selectedElement.type === 'image' ? 'Imagem' : 'Bloco'}
                                </span>
                            </div>
                            <div className="ble-props-body">
                                {/* Visibility Toggle */}
                                <div className="ble-prop-toggle">
                                    <input
                                        type="checkbox"
                                        id="prop-visible"
                                        checked={selectedElement.visible}
                                        onChange={() => !isReadOnly && toggleVisibility(selectedKey!)}
                                        disabled={isReadOnly}
                                    />
                                    <label htmlFor="prop-visible">Visível</label>
                                </div>
                                
                                {/* Positioning & Sizing (Percentage values) */}
                                <div className="ble-props-section">
                                    <h4 className="ble-props-section-title">Posicionamento Absoluto</h4>
                                    <div className="ble-prop-inline">
                                        <div className="ble-prop-field">
                                            <label>X (%)</label>
                                            <input
                                                type="number"
                                                step={0.1}
                                                value={selectedElement.xPct}
                                                onChange={e => updateElement(selectedKey!, 'xPct', Number(e.target.value))}
                                                disabled={isReadOnly}
                                            />
                                        </div>
                                        <div className="ble-prop-field">
                                            <label>Y (%)</label>
                                            <input
                                                type="number"
                                                step={0.1}
                                                value={selectedElement.yPct}
                                                onChange={e => updateElement(selectedKey!, 'yPct', Number(e.target.value))}
                                                disabled={isReadOnly}
                                            />
                                        </div>
                                    </div>
                                    <div className="ble-prop-inline">
                                        <div className="ble-prop-field">
                                            <label>Largura (%)</label>
                                            <input
                                                type="number"
                                                step={0.1}
                                                min={1}
                                                value={selectedElement.widthPct}
                                                onChange={e => updateElement(selectedKey!, 'widthPct', Number(e.target.value))}
                                                disabled={isReadOnly}
                                            />
                                        </div>
                                        <div className="ble-prop-field">
                                            <label>Altura (%)</label>
                                            <input
                                                type="number"
                                                step={0.1}
                                                value={selectedElement.heightPct}
                                                onChange={e => updateElement(selectedKey!, 'heightPct', Number(e.target.value))}
                                                disabled={isReadOnly}
                                            />
                                        </div>
                                    </div>
                                </div>

                                {/* Type-Specific Properties */}
                                {selectedElement.type === 'text' && (
                                    <TextProperties
                                        el={selectedElement as TextElement}
                                        elKey={selectedKey!}
                                        onChange={updateElement}
                                        disabled={isReadOnly}
                                    />
                                )}
                                {selectedElement.type === 'image' && (
                                    <ImageProperties
                                        el={selectedElement as ImageElement}
                                        elKey={selectedKey!}
                                        onChange={updateElement}
                                        disabled={isReadOnly}
                                        onLogoUpload={handleLogoUpload}
                                        onLogoRemove={removeLogo}
                                        logoInputRef={logoInputRef}
                                        logoError={logoError}
                                    />
                                )}
                                {selectedElement.type === 'shape' && (
                                    <ShapeProperties
                                        el={selectedElement as ShapeElement}
                                        elKey={selectedKey!}
                                        onChange={updateElement}
                                        disabled={isReadOnly}
                                    />
                                )}
                            </div>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}

// ═══════════════════════════════════════════
// Property Sub-Components
// ═══════════════════════════════════════════

interface PropUpdater {
    (key: string, prop: string, value: any): void;
}

// ─── ColorField ───

function ColorField({ label, value, elKey, prop, onChange, disabled, defaultHint }: {
    label: string;
    value: string;
    elKey: string;
    prop: string;
    onChange: PropUpdater;
    disabled: boolean;
    defaultHint?: string;
}) {
    return (
        <div className="ble-prop-field">
            <label>{label}</label>
            <div className="ble-color-row">
                <input
                    type="color"
                    value={value || '#000000'}
                    onChange={e => onChange(elKey, prop, e.target.value)}
                    disabled={disabled}
                />
                <input
                    type="text"
                    value={value || ''}
                    onChange={e => onChange(elKey, prop, e.target.value)}
                    placeholder={defaultHint || '#000000'}
                    disabled={disabled}
                />
            </div>
        </div>
    );
}

// ─── TextProperties ───

function TextProperties({ el, elKey, onChange, disabled }: {
    el: TextElement;
    elKey: string;
    onChange: PropUpdater;
    disabled: boolean;
}) {
    return (
        <>
            {/* Typography */}
            <div className="ble-props-section">
                <h4 className="ble-props-section-title">Tipografia</h4>

                <div className="ble-prop-field">
                    <label>Família da Fonte</label>
                    <select
                        value={el.fontFamily || ''}
                        onChange={e => onChange(elKey, 'fontFamily', e.target.value)}
                        disabled={disabled}
                    >
                        {FONT_OPTIONS.map(f => (
                            <option key={f.value} value={f.value}>{f.label}</option>
                        ))}
                    </select>
                </div>

                <div className="ble-prop-inline">
                    <div className="ble-prop-field">
                        <label>Tamanho (px referencial)</label>
                        <input
                            type="number"
                            min={6}
                            max={48}
                            step={0.5}
                            value={el.fontSize || 12}
                            onChange={e => onChange(elKey, 'fontSize', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                    <div className="ble-prop-field">
                        <label>Peso</label>
                        <select
                            value={el.fontWeight || 400}
                            onChange={e => onChange(elKey, 'fontWeight', Number(e.target.value))}
                            disabled={disabled}
                        >
                            {FONT_WEIGHT_OPTIONS.map(w => (
                                <option key={w.value} value={w.value}>{w.label}</option>
                            ))}
                        </select>
                    </div>
                </div>

                <div className="ble-prop-inline">
                    <div className="ble-prop-field">
                        <label>Alinhamento</label>
                        <select
                            value={el.textAlign || 'left'}
                            onChange={e => onChange(elKey, 'textAlign', e.target.value)}
                            disabled={disabled}
                        >
                            {TEXT_ALIGN_OPTIONS.map(a => (
                                <option key={a.value} value={a.value}>{a.label}</option>
                            ))}
                        </select>
                    </div>
                    <div className="ble-prop-field">
                        <label>Transformação</label>
                        <select
                            value={el.textTransform || ''}
                            onChange={e => onChange(elKey, 'textTransform', e.target.value)}
                            disabled={disabled}
                        >
                            {TEXT_TRANSFORM_OPTIONS.map(t => (
                                <option key={t.value} value={t.value}>{t.label}</option>
                            ))}
                        </select>
                    </div>
                </div>

                <div className="ble-prop-inline">
                    <div className="ble-prop-field">
                        <label>Espaço Letras (em)</label>
                        <input
                            type="number"
                            min={0}
                            max={1}
                            step={0.01}
                            value={el.letterSpacing || 0}
                            onChange={e => onChange(elKey, 'letterSpacing', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                    <div className="ble-prop-field">
                        <label>Altura Linha</label>
                        <input
                            type="number"
                            min={0.8}
                            max={3}
                            step={0.1}
                            value={el.lineHeight || 1.2}
                            onChange={e => onChange(elKey, 'lineHeight', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                </div>

                <div className="ble-prop-field" style={{ marginTop: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <input
                            type="checkbox"
                            id={`ws-wrap-${elKey}`}
                            checked={el.whiteSpace !== 'nowrap'}
                            onChange={e => onChange(elKey, 'whiteSpace', e.target.checked ? 'normal' : 'nowrap')}
                            disabled={disabled}
                            style={{ width: '16px', height: '16px', cursor: 'pointer' }}
                        />
                        <label 
                            htmlFor={`ws-wrap-${elKey}`} 
                            style={{ margin: 0, fontSize: '0.85rem', cursor: 'pointer', fontWeight: 500 }}
                        >
                            Quebra de Linha (Permitir multi-linha)
                        </label>
                    </div>
                </div>
            </div>

            {/* Color */}
            <div className="ble-props-section">
                <h4 className="ble-props-section-title">Cor</h4>
                <ColorField
                    label="Cor do Texto"
                    value={el.color || '#000000'}
                    elKey={elKey}
                    prop="color"
                    onChange={onChange}
                    disabled={disabled}
                    defaultHint="Padrão CSS"
                />
            </div>

            {/* Label Text (for footer items) */}
            {el.labelText !== undefined && (
                <div className="ble-props-section">
                    <h4 className="ble-props-section-title">Rótulo / Texto Estático</h4>
                    <div className="ble-prop-field">
                        <label>Texto Secundário</label>
                        <input
                            type="text"
                            value={el.labelText || ''}
                            onChange={e => onChange(elKey, 'labelText', e.target.value)}
                            disabled={disabled}
                            placeholder="Ex: Nº Func."
                        />
                    </div>
                </div>
            )}
        </>
    );
}

// ─── ImageProperties ───

function ImageProperties({ el, elKey, onChange, disabled, onLogoUpload, onLogoRemove, logoInputRef, logoError }: {
    el: ImageElement;
    elKey: string;
    onChange: PropUpdater;
    disabled: boolean;
    onLogoUpload: (e: React.ChangeEvent<HTMLInputElement>) => void;
    onLogoRemove: () => void;
    logoInputRef: React.RefObject<HTMLInputElement | null> | React.MutableRefObject<HTMLInputElement | null>;
    logoError: string | null;
}) {
    const isLogo = elKey === 'logo';
    const maxSizeKB = Math.round(MAX_LOGO_SIZE_BYTES / 1024);

    return (
        <>
            {/* Object Fit */}
            <div className="ble-props-section">
                <h4 className="ble-props-section-title">Enquadramento</h4>
                <div className="ble-prop-field">
                    <label>Comportamento da Imagem</label>
                    <select
                        value={el.objectFit || 'contain'}
                        onChange={e => onChange(elKey, 'objectFit', e.target.value)}
                        disabled={disabled}
                    >
                        <option value="contain">Conter (Proteger Proporções, deixa espaços)</option>
                        <option value="cover">Cobrir Teto (Preenche caixa cortando excessos)</option>
                        <option value="fill">Forçar Preenchimento (100% esticado, pode distorcer)</option>
                    </select>
                </div>
            </div>

            {/* Border */}
            <div className="ble-props-section">
                <h4 className="ble-props-section-title">Borda</h4>
                <div className="ble-prop-inline">
                    <div className="ble-prop-field">
                        <label>Raio (px)</label>
                        <input
                            type="number"
                            min={0}
                            max={50}
                            value={el.borderRadius || 0}
                            onChange={e => onChange(elKey, 'borderRadius', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                    <div className="ble-prop-field">
                        <label>Espessura (px)</label>
                        <input
                            type="number"
                            min={0}
                            max={10}
                            value={el.borderWidth || 0}
                            onChange={e => onChange(elKey, 'borderWidth', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                </div>
                <ColorField
                    label="Cor da Borda"
                    value={el.borderColor || 'transparent'}
                    elKey={elKey}
                    prop="borderColor"
                    onChange={onChange}
                    disabled={disabled}
                    defaultHint="Padrão CSS"
                />
            </div>

            {/* Logo Upload (only for logo element) */}
            {isLogo && (
                <div className="ble-props-section">
                    <h4 className="ble-props-section-title">Logo Personalizado</h4>
                    <div className="ble-logo-upload">
                        {el.customLogoDataUrl ? (
                            <>
                                <img
                                    src={el.customLogoDataUrl}
                                    alt="Logo preview"
                                    className="ble-logo-preview"
                                />
                                {!disabled && (
                                    <div className="ble-logo-actions">
                                        <button
                                            className="ble-btn-secondary ble-btn-sm"
                                            onClick={() => logoInputRef.current?.click()}
                                        >
                                            <Upload size={12} /> Substituir
                                        </button>
                                        <button
                                            className="ble-btn-danger-sm"
                                            onClick={onLogoRemove}
                                        >
                                            <Trash2 size={12} /> Remover
                                        </button>
                                    </div>
                                )}
                            </>
                        ) : (
                            <>
                                {!disabled && (
                                    <button
                                        className="ble-btn-secondary ble-btn-sm"
                                        onClick={() => logoInputRef.current?.click()}
                                    >
                                        <Upload size={12} /> Carregar Logo
                                    </button>
                                )}
                                <p className="ble-logo-hint">
                                    PNG, JPEG, SVG ou WebP · Máx. {maxSizeKB} KB
                                </p>
                            </>
                        )}
                        {logoError && (
                            <p className="ble-logo-error">{logoError}</p>
                        )}
                        <input
                            ref={logoInputRef as React.LegacyRef<HTMLInputElement>}
                            type="file"
                            accept={ACCEPTED_LOGO_TYPES.join(',')}
                            onChange={onLogoUpload}
                            style={{ display: 'none' }}
                        />
                    </div>
                </div>
            )}
        </>
    );
}

// ─── ShapeProperties ───

function ShapeProperties({ el, elKey, onChange, disabled }: {
    el: ShapeElement;
    elKey: string;
    onChange: PropUpdater;
    disabled: boolean;
}) {
    return (
        <>
            {/* Background */}
            <div className="ble-props-section">
                <h4 className="ble-props-section-title">Fundo</h4>
                <ColorField
                    label="Cor de Fundo"
                    value={el.backgroundColor || 'transparent'}
                    elKey={elKey}
                    prop="backgroundColor"
                    onChange={onChange}
                    disabled={disabled}
                    defaultHint="Padrão CSS"
                />
            </div>

            {/* Border */}
            <div className="ble-props-section">
                <h4 className="ble-props-section-title">Borda Superior / Raio</h4>
                <div className="ble-prop-inline">
                    <div className="ble-prop-field">
                        <label>Borda Sup. (px)</label>
                        <input
                            type="number"
                            min={0}
                            max={10}
                            value={el.borderTopWidth || 0}
                            onChange={e => onChange(elKey, 'borderTopWidth', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                    <div className="ble-prop-field">
                        <label>Raio (px)</label>
                        <input
                            type="number"
                            min={0}
                            max={30}
                            value={el.borderRadius || 0}
                            onChange={e => onChange(elKey, 'borderRadius', Number(e.target.value))}
                            disabled={disabled}
                        />
                    </div>
                </div>
                <ColorField
                    label="Cor Borda Superior"
                    value={el.borderTopColor || 'transparent'}
                    elKey={elKey}
                    prop="borderTopColor"
                    onChange={onChange}
                    disabled={disabled}
                    defaultHint="Padrão CSS"
                />
            </div>
        </>
    );
}
