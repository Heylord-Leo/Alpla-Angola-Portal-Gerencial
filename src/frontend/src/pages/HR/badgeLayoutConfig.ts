/**
 * Badge Layout Config — V3 Element-Oriented Absolute Schema
 *
 * Canonical types for the element-based badge layout system.
 * Used by BadgePreview (rendering) and BadgeLayoutEditorV3 (editing).
 *
 * Schema evolution:
 *   V1 (schemaVersion: 1) — flat toggles: showDepartment, headerBgColor, etc.
 *   V2 (schemaVersion: 2) — per-element config with type-specific properties (Grid).
 *   V3 (schemaVersion: 3) — per-element config with absolute visual coordinates (xPct/yPct).
 *
 * Empty string values ("") mean "use CSS default from design tokens".
 */

// ─── Legacy Types (for migration) ───

export interface LayoutConfigV1 {
    schemaVersion: number;
    headerBgColor?: string;
    showLogo?: boolean;
    showDepartment?: boolean;
    showCategory?: boolean;
    showCardNumber?: boolean;
    showCompany?: boolean;
    footerFields?: string[];
    photoSize?: 'small' | 'medium' | 'large';
}

export interface LayoutConfigV2 {
    schemaVersion: 2;
    elements: Partial<Record<string, any>>;
}

// ─── V3 Element Types ───

export type ElementType = 'text' | 'image' | 'shape';

/** Common absolute positioning properties for V3 */
export interface BaseElementV3 {
    type: ElementType;
    visible: boolean;
    xPct: number;       // X position % (0-100)
    yPct: number;       // Y position % (0-100)
    widthPct: number;   // Width % (0-100)
    heightPct: number;  // Height % (0-100)
    zIndex: number;     // Stacking order
}

/** Text element — labels, values, headings. */
export interface TextElement extends BaseElementV3 {
    type: 'text';
    fontSize: number;         // px (base reference)
    fontWeight: number;       // 400–900
    color: string;            // hex or empty for default
    fontFamily: string;       // empty = system default
    textAlign: 'left' | 'center' | 'right';
    textTransform: '' | 'uppercase' | 'lowercase' | 'capitalize';
    letterSpacing: number;    // em
    lineHeight: number;       // unitless multiplier
    whiteSpace: 'normal' | 'nowrap' | 'pre-wrap';
    textOverflow: 'clip' | 'ellipsis';
    alignItems: 'baseline' | 'flex-start' | 'center' | 'flex-end'; 
    justifyContent: 'flex-start' | 'center' | 'flex-end'; 
    labelText?: string;       // for footer items
}

/** Image element — logo, employee photo. */
export interface ImageElement extends BaseElementV3 {
    type: 'image';
    borderRadius: number;     // px
    borderWidth: number;      // px
    borderColor: string;      // hex
    objectFit: 'cover' | 'contain' | 'fill';
    customLogoDataUrl?: string | null;
}

/** Shape element — background blocks. */
export interface ShapeElement extends BaseElementV3 {
    type: 'shape';
    backgroundColor: string;  
    borderTopWidth: number;
    borderTopColor: string;
    borderRadius: number;
}

export type BadgeElement = TextElement | ImageElement | ShapeElement;

/** V3 Layout Configuration — Absolute positioning, orientation. */
export interface LayoutConfigV3 {
    schemaVersion: 3;
    orientation: 'vertical' | 'horizontal';
    elements: Record<ElementKey, BadgeElement>;
}

// ─── Element Keys ───

export const ELEMENT_KEYS = [
    'header', 'logo', 'companyName', 'photo', 'nameBlock',
    'name', 'department', 'category', 'footer',
    'footerLabelEmpCode', 'footerValueEmpCode',
    'footerLabelCard', 'footerValueCard',
    'footerLabelCompany', 'footerValueCompany',
    'footerLabelDoc', 'footerValueDoc',
] as const;

export type ElementKey = typeof ELEMENT_KEYS[number];

/** Human-readable labels for element keys. */
export const ELEMENT_LABELS: Record<ElementKey, string> = {
    header: 'Cabeçalho',
    logo: 'Logotipo',
    companyName: 'Nome da Empresa',
    photo: 'Foto do Funcionário',
    nameBlock: 'Bloco do Nome',
    name: 'Nome',
    department: 'Departamento',
    category: 'Categoria / Função',
    footer: 'Fundo do Rodapé',
    footerLabelEmpCode: 'Rótulo Nº Func.',
    footerValueEmpCode: 'Valor Nº Func.',
    footerLabelCard: 'Rótulo Cartão',
    footerValueCard: 'Valor Cartão',
    footerLabelCompany: 'Rótulo Empresa',
    footerValueCompany: 'Valor Empresa',
    footerLabelDoc: 'Rótulo Documento',
    footerValueDoc: 'Valor Documento',
};

/** Icon name hint for element list (mapped to lucide icons in the editor). */
export const ELEMENT_ICONS: Record<ElementKey, string> = {
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

// ─── Font Options ───

export const FONT_OPTIONS = [
    { value: '', label: 'Padrão do Sistema' },
    { value: "'Inter', sans-serif", label: 'Inter' },
    { value: "'Arial', sans-serif", label: 'Arial' },
    { value: "'Roboto', sans-serif", label: 'Roboto' },
];

// ─── Default V3 Config ───

export function textEl(overrides: Partial<TextElement> = {}): TextElement {
    return {
        type: 'text', visible: true, xPct: 0, yPct: 0, widthPct: 100, heightPct: 10, zIndex: 10,
        fontSize: 12, fontWeight: 500, color: '', fontFamily: '', textAlign: 'center', textTransform: '',
        letterSpacing: 0, lineHeight: 1.2, whiteSpace: 'nowrap', textOverflow: 'ellipsis',
        alignItems: 'center', justifyContent: 'flex-start', ...overrides,
    };
}

export function imageEl(overrides: Partial<ImageElement> = {}): ImageElement {
    return {
        type: 'image', visible: true, xPct: 0, yPct: 0, widthPct: 20, heightPct: 20, zIndex: 5,
        borderRadius: 0, borderWidth: 0, borderColor: '', objectFit: 'cover', ...overrides,
    };
}

export function shapeEl(overrides: Partial<ShapeElement> = {}): ShapeElement {
    return {
        type: 'shape', visible: true, xPct: 0, yPct: 0, widthPct: 100, heightPct: 100, zIndex: 0,
        backgroundColor: '', borderTopWidth: 0, borderTopColor: '', borderRadius: 0, ...overrides,
    };
}

/** Default V3 configuration mapping to a standard corporate vertical badge baseline. */
export const DEFAULT_V3_CONFIG: LayoutConfigV3 = {
    schemaVersion: 3,
    orientation: 'vertical',
    elements: {
        header: shapeEl({ backgroundColor: '#2563eb', xPct: 0, yPct: 0, widthPct: 100, heightPct: 15, zIndex: 1 }),
        logo: imageEl({ xPct: 5, yPct: 2, widthPct: 15, heightPct: 10, objectFit: 'contain', zIndex: 2 }),
        companyName: textEl({ xPct: 25, yPct: 2, widthPct: 70, heightPct: 10, fontSize: 10, fontWeight: 700, color: '#ffffff', textTransform: 'uppercase', textAlign: 'left', alignItems: 'center', zIndex: 2 }),
        
        photo: imageEl({ xPct: 25, yPct: 20, widthPct: 50, heightPct: 35, borderRadius: 6, borderWidth: 2, objectFit: 'cover', zIndex: 3 }),
        
        nameBlock: shapeEl({ xPct: 5, yPct: 58, widthPct: 90, heightPct: 20, backgroundColor: 'transparent', zIndex: 1 }),
        name: textEl({ xPct: 5, yPct: 58, widthPct: 90, heightPct: 14, fontSize: 14, fontWeight: 800, textAlign: 'center', alignItems: 'flex-end', zIndex: 4, whiteSpace: 'normal' }),
        department: textEl({ xPct: 5, yPct: 72, widthPct: 90, heightPct: 5, fontSize: 11, fontWeight: 600, textTransform: 'uppercase', textAlign: 'center', zIndex: 4 }),
        category: textEl({ xPct: 5, yPct: 77, widthPct: 90, heightPct: 4, fontSize: 10, textAlign: 'center', zIndex: 4 }),
        
        footer: shapeEl({ xPct: 0, yPct: 82, widthPct: 100, heightPct: 18, borderTopWidth: 1, backgroundColor: '#f9fafb', zIndex: 1 }),
        
        // Footer left column
        footerLabelEmpCode: textEl({ xPct: 5, yPct: 84, widthPct: 25, heightPct: 4, fontSize: 8, fontWeight: 700, textTransform: 'uppercase', textAlign: 'left', labelText: 'Nº Func.', alignItems: 'flex-end', zIndex: 5 }),
        footerValueEmpCode: textEl({ xPct: 5, yPct: 88, widthPct: 25, heightPct: 5, fontSize: 11, fontWeight: 700, textAlign: 'left', alignItems: 'flex-start', zIndex: 5 }),
        
        // Footer center column
        footerLabelCard: textEl({ xPct: 35, yPct: 84, widthPct: 30, heightPct: 4, fontSize: 8, fontWeight: 700, textTransform: 'uppercase', textAlign: 'left', labelText: 'Cartão', alignItems: 'flex-end', zIndex: 5 }),
        footerValueCard: textEl({ xPct: 35, yPct: 88, widthPct: 30, heightPct: 5, fontSize: 11, fontWeight: 700, textAlign: 'left', alignItems: 'flex-start', zIndex: 5 }),
        
        // Footer right column
        footerLabelCompany: textEl({ xPct: 70, yPct: 84, widthPct: 25, heightPct: 4, fontSize: 8, fontWeight: 700, textTransform: 'uppercase', textAlign: 'left', labelText: 'Empresa', alignItems: 'flex-end', zIndex: 5 }),
        footerValueCompany: textEl({ xPct: 70, yPct: 88, widthPct: 25, heightPct: 5, fontSize: 11, fontWeight: 700, textAlign: 'left', alignItems: 'flex-start', zIndex: 5 }),
        
        // Document row (below main footer columns)
        footerLabelDoc: textEl({ xPct: 5, yPct: 93, widthPct: 20, heightPct: 4, fontSize: 8, fontWeight: 700, textTransform: 'uppercase', textAlign: 'left', labelText: 'B.I.', alignItems: 'center', zIndex: 5 }),
        footerValueDoc: textEl({ xPct: 25, yPct: 93, widthPct: 40, heightPct: 4, fontSize: 10, fontWeight: 600, textAlign: 'left', alignItems: 'center', zIndex: 5 }),
    }
};

// ─── Migrations ───

/** Migrate from ancient V1 right into V3 defaults */
export function migrateV1toV3(v1: LayoutConfigV1): LayoutConfigV3 {
    const base = structuredClone(DEFAULT_V3_CONFIG);
    const els = base.elements;

    if (v1.headerBgColor) {
        (els.header as ShapeElement).backgroundColor = v1.headerBgColor;
    }

    (els.logo as ImageElement).visible = v1.showLogo ?? true;
    (els.department as TextElement).visible = v1.showDepartment ?? true;
    (els.category as TextElement).visible = v1.showCategory ?? true;
    (els.footerLabelCard as TextElement).visible = v1.showCardNumber ?? true;
    (els.footerValueCard as TextElement).visible = v1.showCardNumber ?? true;
    (els.footerLabelCompany as TextElement).visible = v1.showCompany ?? true;
    (els.footerValueCompany as TextElement).visible = v1.showCompany ?? true;

    // We no longer strictly honor 'small'/'large' photo sizing precisely from V1 directly,
    // V3 uses percentages. We stay with V3 medium default (50%).
    return base;
}

/** V2 was grid based without X/Y. Migrate V2 visual properties into V3 default coordinates. */
export function migrateV2toV3(v2: LayoutConfigV2): LayoutConfigV3 {
    const base = structuredClone(DEFAULT_V3_CONFIG);
    
    // We map only styling properties since V2 had no positional data.
    for (const key of Object.keys(base.elements) as ElementKey[]) {
        const v2El = v2.elements[key];
        if (v2El) {
            // Merge styling. We meticulously avoid blindly spreading to prevent overriding V3 xPct/yPct with undefined.
            const baseEl = base.elements[key];
            Object.assign(baseEl, {
                visible: v2El.visible ?? baseEl.visible,
                ...(v2El.color !== undefined && { color: v2El.color }),
                ...(v2El.backgroundColor !== undefined && { backgroundColor: v2El.backgroundColor }),
                ...(v2El.fontFamily !== undefined && { fontFamily: v2El.fontFamily }),
                ...(v2El.fontSize !== undefined && { fontSize: v2El.fontSize }),
                ...(v2El.fontWeight !== undefined && { fontWeight: v2El.fontWeight }),
                ...(v2El.textAlign !== undefined && { textAlign: v2El.textAlign }),
                ...(v2El.customLogoDataUrl !== undefined && { customLogoDataUrl: v2El.customLogoDataUrl }),
                ...(v2El.labelText !== undefined && { labelText: v2El.labelText }),
            });
        }
    }
    
    return base;
}

/**
 * Parse a raw layoutConfigJson and return a normalized V3 config.
 * Handles migration safely. Note that migrated items are considered "dirty"
 * and will require user save to persist V3 structure.
 */
export function parseLayoutConfig(json?: string | null): LayoutConfigV3 {
    if (!json) return structuredClone(DEFAULT_V3_CONFIG);

    try {
        const parsed = JSON.parse(json);

        if (parsed.schemaVersion === 3 && parsed.elements) {
            const merged = structuredClone(DEFAULT_V3_CONFIG);
            // Protect against missing fields by merging against defaults
            merged.orientation = parsed.orientation || 'vertical';
            for (const key of Object.keys(merged.elements) as ElementKey[]) {
                if (parsed.elements[key]) {
                    merged.elements[key] = { ...merged.elements[key], ...parsed.elements[key] };
                }
            }
            return merged;
        }

        if (parsed.schemaVersion === 2) {
            return migrateV2toV3(parsed);
        }

        // V1 or unknown → migrate V1 -> V3
        const v1: LayoutConfigV1 = {
            schemaVersion: 1,
            headerBgColor: parsed.headerBgColor,
            showLogo: parsed.showLogo,
            showDepartment: parsed.showDepartment,
            showCategory: parsed.showCategory,
            showCardNumber: parsed.showCardNumber,
            showCompany: parsed.showCompany,
        };
        return migrateV1toV3(v1);
    } catch {
        return structuredClone(DEFAULT_V3_CONFIG);
    }
}

// ─── Logo Validation ───

export const MAX_LOGO_SIZE_BYTES = 200 * 1024;
export const ACCEPTED_LOGO_TYPES = ['image/png', 'image/jpeg', 'image/svg+xml', 'image/webp'];

export function validateLogoFile(file: File): { valid: boolean; error?: string } {
    if (!ACCEPTED_LOGO_TYPES.includes(file.type)) {
        return {
            valid: false,
            error: `Tipo de ficheiro não suportado (${file.type}). Use PNG, JPEG, SVG ou WebP.`,
        };
    }
    if (file.size > MAX_LOGO_SIZE_BYTES) {
        const sizeKB = Math.round(file.size / 1024);
        return {
            valid: false,
            error: `Ficheiro demasiado grande (${sizeKB} KB). O tamanho máximo é 200 KB.`,
        };
    }
    return { valid: true };
}
