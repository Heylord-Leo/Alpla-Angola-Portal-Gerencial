/**
 * Viana 3 Production Monitor — machine-card design contracts (mirror of
 * AlplaPortal.Application/DTOs/Operations/Viana3CardDesignDtos.cs).
 * Display preferences only: no production data, no AlplaPROD change.
 */

export type Viana3DesignNumberFormat = 'INT' | 'THOUSANDS' | 'COMPACT' | 'DECIMAL1';
export type Viana3DesignAlign = 'LEFT' | 'CENTER' | 'RIGHT';
export type Viana3DesignImageFit = 'CONTAIN' | 'COVER';
export type Viana3DesignFontWeight = 400 | 600 | 700 | 800;

export interface Viana3CardDesignStyle {
    color?: string | null;
    background?: string | null;
    borderColor?: string | null;
    accent?: string | null;
    fontSize?: number | null;
    fontWeight?: Viana3DesignFontWeight | null;
    align?: Viana3DesignAlign | null;
    opacity?: number | null;
    radius?: number | null;
}

export interface Viana3CardDesignProps {
    label?: string | null;
    numberFormat?: Viana3DesignNumberFormat | null;
    unitSuffix?: string | null;
    showSecondary?: boolean | null;
    text?: string | null;
    imageAssetId?: string | null;
    imageFit?: Viana3DesignImageFit | null;
}

export interface Viana3CardDesignItem {
    /** Catalogue key ("reported") or free item key ("free-text-1" … "free-image-6"). */
    key: string;
    enabled: boolean;
    x: number;
    y: number;
    w: number;
    h: number;
    rotation: number;
    z: number;
    style: Viana3CardDesignStyle;
    props: Viana3CardDesignProps;
}

export interface Viana3CardDesignDocument {
    schemaVersion: number;
    canvasWidth: number;
    canvasHeight: number;
    background?: string | null;
    items: Viana3CardDesignItem[];
}

export type Viana3CardDesignSource = 'OWN' | 'TEMPLATE' | 'BUILTIN';

export interface Viana3CardDesign {
    machineId: number;
    isTemplate: boolean;
    isPersisted: boolean;
    effectiveSource: Viana3CardDesignSource;
    design: Viana3CardDesignDocument;
    updatedAtUtc: string | null;
    updatedByName: string | null;
}

export interface Viana3CardDesignBundle {
    template: Viana3CardDesignDocument | null;
    machines: { machineId: number; design: Viana3CardDesignDocument }[];
}

export interface SaveViana3CardDesignRequest {
    design: Viana3CardDesignDocument;
    saveToken: string;
    expectedUpdatedAtUtc: string | null;
}

// ─── Assets ───

export interface Viana3DesignAsset {
    id: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    width: number;
    height: number;
    createdAtUtc: string;
    createdByName: string | null;
    /** Designs referencing the asset (0 = template). */
    referencedBy: number[];
    fileMissing: boolean;
    url: string;
}

export interface Viana3DesignAssetList {
    assets: Viana3DesignAsset[];
    activeCount: number;
    quota: number;
    pendingPurge: number;
    missingFiles: number;
    maxSizeBytes: number;
    maxDimension: number;
}

export interface Viana3DesignAssetUploadResult {
    asset: Viana3DesignAsset;
    reused: boolean;
}

export interface Viana3DesignCleanupResult {
    purged: number;
    tombstoned: number;
    orphanFilesRemoved: number;
    missingFiles: number;
    errors: string[];
}

// ─── Catalogue (read-only data-source metadata) ───

export type Viana3CardItemKind = 'TEXT' | 'NUMBER' | 'BAR' | 'CHIP' | 'ILLUSTRATION' | 'PALLETS' | 'LIST' | 'WASTE';

export interface Viana3CardItemSource {
    payloadField: string;
    table: string;
    field: string;
    filters: string[];
    query: 'Q1' | 'Q2' | 'SERVICE' | string;
    alias: string;
    verification: 'VERIFIED' | 'NOT_VERIFIED';
    verificationDetail: string | null;
    expression: string | null;
}

export interface Viana3CardItem {
    key: string;
    label: string;
    group: string;
    kind: Viana3CardItemKind;
    dataDriven: boolean;
    rotatable: boolean;
    description: string;
    sources: Viana3CardItemSource[];
    calculation: string | null;
    caveats: string[];
    configuredValues: Record<string, string>;
}

export interface Viana3CardItemCatalog {
    items: Viana3CardItem[];
    verifiedAtUtc: string;
    sourceDatabase: string;
    maxFreeItems: number;
}
