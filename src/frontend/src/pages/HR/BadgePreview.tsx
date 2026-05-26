import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import { User, Maximize2, X } from 'lucide-react';
import { motion, useMotionValue } from 'framer-motion';
import {
    LayoutConfigV3, parseLayoutConfig,
    TextElement, ImageElement, ShapeElement,
    type ElementKey, ELEMENT_KEYS, BadgeElement, LayoutConfigV2
} from './badgeLayoutConfig';

// Re-export for backward compatibility and consumers
export type { LayoutConfigV2, LayoutConfigV3 };
export { parseLayoutConfig };

export interface BadgeData {
    firstName?: string;
    lastName?: string;
    fullName?: string;
    department?: string;
    category?: string;
    employeeCode: string;
    cardNumber?: string;
    company?: string;
    photoUrl?: string | null;
    layoutId?: string;
    /** Resolved document label: "B.I." or "Passaporte" */
    documentLabel?: string;
    /** Resolved document value: the actual identity/passport number */
    documentValue?: string;
}

interface BadgePreviewProps {
    data: BadgeData | null;
    printRef?: React.Ref<HTMLDivElement>;
    layoutConfig?: any;
    /** If provided, elements are draggable/resizable via UI interactions */
    isEditing?: boolean;
    /** Editor mode: fires when an element is clicked in the preview. */
    onElementClick?: (key: ElementKey) => void;
    /** Callback for when an element is moved or resized */
    onElementChange?: (key: ElementKey, updates: Partial<BadgeElement>) => void;
    /** Editor mode: the currently selected element key (gets a highlight). */
    selectedElement?: string;
    /** Current zoom level (percentage, e.g. 100, 150, 200). Used to correct drag math. */
    zoom?: number;
}

/**
 * BadgePreview — V3 Rendering Engine (Absolute Positioning)
 *
 * Renders a CR-80 standard ID card preview, supporting both Vertical and Horizontal orientations.
 * Elements are positioned via xPct, yPct, widthPct, heightPct to remain scale-independent.
 * 
 * Includes framer-motion drag controls enabled via `isEditing` mode for the V3 editor canvas.
 */
export function BadgePreview({
    data, printRef, layoutConfig, isEditing, onElementClick, onElementChange, selectedElement, zoom
}: BadgePreviewProps) {
    const [photoError, setPhotoError] = useState(false);
    const [isFullscreen, setIsFullscreen] = useState(false);
    const containerRef = React.useRef<HTMLDivElement>(null);

    const config: LayoutConfigV3 = React.useMemo(() => {
        if (!layoutConfig) return parseLayoutConfig(null);
        if ('elements' in layoutConfig && layoutConfig.schemaVersion === 3) {
            return layoutConfig;
        }
        return parseLayoutConfig(JSON.stringify(layoutConfig));
    }, [layoutConfig]);

    if (!data) {
        return (
            <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                width: '85.6mm', height: '53.98mm', boxSizing: 'border-box',
                border: '2px dashed var(--color-border)', borderRadius: '10px',
                color: 'var(--color-text-muted)', fontSize: '0.85rem',
                fontWeight: 500, textAlign: 'center', padding: '24px'
            }}>
                Selecione um funcionário para visualizar o crachá
            </div>
        );
    }

    const isVertical = config.orientation === 'vertical';
    // CR-80 Standards Dimensions
    const cardWidth = isVertical ? '53.98mm' : '85.6mm';
    const cardHeight = isVertical ? '85.6mm' : '53.98mm';

    const displayName = data.fullName || (data.firstName && data.lastName
        ? `${data.firstName} ${data.lastName}`
        : data.firstName || '—');
    const companyLabel = resolveCompanyLabel(data.company);

    const content = (
        <div 
            ref={containerRef}
            className="badge-card-container" 
            style={{
                position: 'relative',
                width: cardWidth,
                height: cardHeight,
                backgroundColor: '#ffffff',
                overflow: 'hidden',
                boxSizing: 'border-box',
                borderRadius: '3.18mm', // Standard CR-80 radius
                boxShadow: '0 4px 15px rgba(0,0,0,0.1), 0 1px 3px rgba(0,0,0,0.05)',
                border: '1px solid rgba(0,0,0,0.05)',
                // The print fidelity relies strictly on absolute percent positioning inside this container 
            }}
        >
            {ELEMENT_KEYS.map(key => {
                const el = config.elements[key];
                if (!el || el.visible === false) return null;

                return (
                    <DraggableElement
                        key={`${key}-${el.type}`}
                        elKey={key}
                        el={el}
                        isEditing={!!isEditing}
                        selected={selectedElement === key}
                        containerRef={containerRef}
                        onClick={() => onElementClick?.(key)}
                        onChange={(updates) => onElementChange?.(key, updates as Partial<BadgeElement>)}
                        data={data}
                        displayName={displayName}
                        companyLabel={companyLabel}
                        photoError={photoError}
                        setPhotoError={setPhotoError}
                        zoom={zoom}
                    />
                );
            })}
        </div>
    );

    return (
        <div style={{ position: 'relative', display: 'inline-block' }}>
            <div ref={printRef} className="hr-badge-print-area hr-badge-v3">
                {content}
            </div>
            
            {!isEditing && (
                <button
                    onClick={() => setIsFullscreen(true)}
                    title="Ampliar Crachá"
                    className="bph-no-print"
                    style={{
                        position: 'absolute',
                        top: '-12px', right: '-12px',
                        background: '#fff', border: '1px solid #e2e8f0',
                        borderRadius: '50%', width: '32px', height: '32px',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        cursor: 'pointer', boxShadow: '0 2px 5px rgba(0,0,0,0.1)',
                        color: '#64748b', zIndex: 10
                    }}
                >
                    <Maximize2 size={16} />
                </button>
            )}

            {isFullscreen && typeof document !== 'undefined' && createPortal(
                <div style={{
                    position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
                    zIndex: 99999, backgroundColor: 'rgba(0,0,0,0.85)',
                    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center'
                }}>
                    <button
                        onClick={() => setIsFullscreen(false)}
                        title="Fechar Ampliação"
                        style={{
                            position: 'absolute', top: '24px', right: '24px',
                            background: 'rgba(255,255,255,0.1)', border: 'none',
                            color: '#fff', cursor: 'pointer', borderRadius: '50%',
                            width: '48px', height: '48px', display: 'flex', alignItems: 'center', justifyContent: 'center',
                            transition: 'background 0.2s'
                        }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'rgba(255,255,255,0.2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'rgba(255,255,255,0.1)'}
                    >
                        <X size={24} />
                    </button>
                    <div style={{ transform: 'scale(1.5)', transformOrigin: 'center' }}>
                        {content}
                    </div>
                    <div style={{ marginTop: '70px', color: '#fff', fontSize: '1rem', fontWeight: 500, letterSpacing: '1px' }}>
                        PRÉ-VISUALIZAÇÃO AMPLIADA
                    </div>
                </div>,
                document.body
            )}
        </div>
    );
}

// ─── Draggable Element Wrapper ───

interface DraggableElementProps {
    elKey: ElementKey;
    el: BadgeElement;
    isEditing: boolean;
    selected: boolean;
    containerRef: React.RefObject<HTMLDivElement>;
    onClick: () => void;
    onChange: (updates: Partial<BadgeElement>) => void;
    data: BadgeData;
    displayName: string;
    companyLabel: string;
    photoError: boolean;
    setPhotoError: (e: boolean) => void;
    zoom?: number;
}

function DraggableElement({
    elKey, el, isEditing, selected, containerRef, onClick, onChange, data, displayName, companyLabel, photoError, setPhotoError, zoom
}: DraggableElementProps) {
    const x = useMotionValue(0);
    const y = useMotionValue(0);

    const handleDragEnd = (_event: any, info: any) => {
        if (!containerRef.current) return;
        
        const rect = containerRef.current.getBoundingClientRect();
        
        // Zoom-aware drag correction:
        // - info.offset is in the element's LOCAL (unscaled) coordinate space
        // - rect (from getBoundingClientRect) is in VISUAL (scaled) screen pixels
        // - At 200% zoom: rect.width = originalWidth * 2, but info.offset.x is unscaled
        // - Without correction, the percent delta is off by 1/scale
        // - Multiplying by scale restores the correct logical percentage
        const scale = (zoom ?? 100) / 100;
        const dxPct = (info.offset.x / rect.width) * 100 * scale;
        const dyPct = (info.offset.y / rect.height) * 100 * scale;

        let newXPct = Math.max(0, Math.min(100 - el.widthPct, el.xPct + dxPct));
        let newYPct = Math.max(0, Math.min(100 - el.heightPct, el.yPct + dyPct));

        // Reset the motion value transforms because we are physically updating layout properties (`left`/`top`).
        x.set(0);
        y.set(0);

        onChange({ xPct: newXPct, yPct: newYPct });
    };

    const wrapperStyle: React.CSSProperties = {
        position: 'absolute',
        left: `${el.xPct}%`,
        top: `${el.yPct}%`,
        width: `${el.widthPct}%`,
        height: `${el.heightPct}%`,
        zIndex: el.zIndex || 10,
        boxSizing: 'border-box',
        cursor: isEditing ? 'pointer' : 'default',
        ...(selected && isEditing ? { outline: '2px dashed #2563eb', outlineOffset: '-1px' } : {})
    };

    const innerContent = renderElementContent(elKey, el, data, displayName, companyLabel, photoError, setPhotoError);

    return (
        <motion.div
            style={{ ...wrapperStyle, x, y }}
            drag={isEditing}
            dragMomentum={false}
            onDragEnd={handleDragEnd}
            onClick={(e) => {
                if (isEditing) {
                    e.stopPropagation();
                    onClick();
                }
            }}
        >
            {innerContent}
        </motion.div>
    );
}

// ─── Render Element Specifics ───

function renderElementContent(elKey: ElementKey, el: BadgeElement, data: BadgeData, displayName: string, companyLabel: string, photoError: boolean, setPhotoError: (e: boolean) => void) {
    if (el.type === 'shape') {
        const s = el as ShapeElement;
        return (
            <div style={{
                width: '100%', height: '100%',
                backgroundColor: s.backgroundColor,
                ...(s.borderTopWidth ? { borderTop: `${s.borderTopWidth}px solid ${s.borderTopColor}` } : {}),
                ...(s.borderRadius ? { borderRadius: `${s.borderRadius}px` } : {}),
            }} />
        );
    }

    if (el.type === 'image') {
        const i = el as ImageElement;
        const isPhoto = elKey === 'photo';
        const logoSrc = i.customLogoDataUrl || '/logo-v2.png';
        const logoFilter = i.customLogoDataUrl ? {} : { filter: 'brightness(0) invert(1)' };

        if (isPhoto) {
            const showPhoto = data.photoUrl && !photoError;
            return (
                <div style={{
                    width: '100%', height: '100%',
                    backgroundColor: '#f3f4f6',
                    overflow: 'hidden',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    ...(i.borderRadius ? { borderRadius: `${i.borderRadius}px` } : {}),
                    ...(i.borderWidth ? { border: `${i.borderWidth}px solid ${i.borderColor || '#e5e7eb'}` } : {}),
                }}>
                    {showPhoto ? (
                        <img src={data.photoUrl!} alt={displayName} style={{ width: '100%', height: '100%', objectFit: i.objectFit || 'cover' }} onError={() => setPhotoError(true)} />
                    ) : (
                        <User size={32} color="#9ca3af" />
                    )}
                </div>
            );
        }

        return (
            <img 
                src={logoSrc} 
                alt="Logo" 
                style={{ 
                    width: '100%', height: '100%', objectFit: i.objectFit || 'contain',
                    ...(i.borderRadius ? { borderRadius: `${i.borderRadius}px` } : {}),
                    ...(i.borderWidth ? { border: `${i.borderWidth}px solid ${i.borderColor}` } : {}),
                    ...logoFilter 
                }} 
            />
        );
    }

    if (el.type === 'text') {
        const t = el as TextElement;
        let textContent = '';
        // For document label elements, data.documentLabel overrides config labelText
        // This enables dynamic B.I./Passaporte switching based on employee nationality
        if (elKey === 'footerLabelDoc' && data.documentLabel) {
            textContent = data.documentLabel;
        } else if (t.labelText) {
            textContent = t.labelText;
        } else if (elKey === 'companyName') textContent = companyLabel;
        else if (elKey === 'name') textContent = displayName;
        else if (elKey === 'department') textContent = data.department || '';
        else if (elKey === 'category') textContent = data.category || '';
        else if (elKey === 'footerValueEmpCode') textContent = data.employeeCode;
        else if (elKey === 'footerValueCard') textContent = data.cardNumber || '—';
        else if (elKey === 'footerValueCompany') textContent = companyLabel;
        else if (elKey === 'footerValueDoc') textContent = data.documentValue || '—';

        const isWrapped = t.whiteSpace === 'normal' || t.whiteSpace === 'pre-wrap';
        const horizontalAlign = t.textAlign === 'right' ? 'flex-end' : (t.textAlign === 'center' ? 'center' : 'flex-start');

        return (
            <div style={{
                width: '100%', height: '100%',
                display: 'flex',
                alignItems: t.alignItems || 'center',
                justifyContent: horizontalAlign,
                color: t.color || 'inherit',
                fontFamily: t.fontFamily || 'inherit',
                fontSize: t.fontSize ? `${t.fontSize}px` : 'inherit',
                fontWeight: t.fontWeight || 'inherit',
                textAlign: t.textAlign || 'left',
                textTransform: (t.textTransform as any) || 'none',
                letterSpacing: t.letterSpacing ? `${t.letterSpacing}em` : 'normal',
                lineHeight: t.lineHeight || 1.2,
                overflow: 'hidden'
            }}>
                <span style={{ 
                    whiteSpace: t.whiteSpace || 'nowrap', 
                    textOverflow: isWrapped ? 'clip' : (t.textOverflow || 'ellipsis'),
                    overflow: 'hidden',
                    maxWidth: '100%',
                    display: 'block'
                }}>
                    {textContent}
                </span>
            </div>
        );
    }

    return null;
}

/**
 * Resolves Primavera company codes to display-friendly labels.
 */
function resolveCompanyLabel(company?: string): string {
    if (!company) return '—';

    const labels: Record<string, string> = {
        'ALPLAPLASTICO': 'Alpla Plástico',
        'ALPLASOPRO': 'Alpla Sopro'
    };

    return labels[company.toUpperCase()] || company;
}
