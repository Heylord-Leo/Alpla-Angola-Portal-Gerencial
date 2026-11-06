/**
 * Viana 3 Production Monitor — card-design API client.
 *
 * Same conventions as operationsApi.ts: apiFetch + API_BASE_URL, ApiError on non-OK
 * responses (status + parsed message + structured detail for 409 conflicts).
 */

import { apiFetch, API_BASE_URL, ApiError } from './api';
import type {
    Viana3CardDesign, Viana3CardDesignBundle, SaveViana3CardDesignRequest,
    Viana3DesignAssetList, Viana3DesignAssetUploadResult, Viana3DesignCleanupResult, Viana3CardItemCatalog,
} from '../types/viana3CardDesign.types';

const BASE = `${API_BASE_URL}/api/operations/viana3-monitor/card-design`;

async function handle<T>(response: Response): Promise<T> {
    if (!response.ok) {
        const text = await response.text();
        let message = text || `HTTP ${response.status}`;
        let body: unknown = null;
        try {
            body = JSON.parse(text);
            const json = body as { message?: string; error?: string };
            if (json.message) message = json.message;
            else if (json.error) message = json.error;
        } catch {
            // not JSON
        }
        const err = new ApiError(message, response.status);
        if (body && typeof body === 'object') {
            const json = body as { error?: string; detail?: unknown };
            err.errorCode = json.error;
            err.details = body;
        }
        throw err;
    }
    if (response.status === 204) return undefined as T;
    return response.json();
}

/** Anonymous bundle (template + machine overrides) read by the TV with every refresh. */
export async function fetchViana3CardDesignBundle(): Promise<Viana3CardDesignBundle> {
    return handle<Viana3CardDesignBundle>(await apiFetch(BASE));
}

/** Item catalogue with verified data-source metadata (editor). */
export async function fetchViana3CardDesignCatalog(): Promise<Viana3CardItemCatalog> {
    return handle<Viana3CardItemCatalog>(await apiFetch(`${BASE}/catalog`));
}

function designPath(machineId: number): string {
    return machineId === 0 ? `${BASE}/template` : `${BASE}/${machineId}`;
}

/** Editor view of one machine (0 = template). */
export async function fetchViana3CardDesign(machineId: number): Promise<Viana3CardDesign> {
    return handle<Viana3CardDesign>(await apiFetch(designPath(machineId)));
}

/** Saves (upsert) the design. 409 DESIGN_MODIFIED / ASSET_UNAVAILABLE / BOARD_BUSY, 400 DESIGN_INVALID. */
export async function saveViana3CardDesign(machineId: number, body: SaveViana3CardDesignRequest): Promise<Viana3CardDesign> {
    return handle<Viana3CardDesign>(await apiFetch(designPath(machineId), {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    }));
}

/** Deletes the machine's own design (back to the template) or the template (back to the built-in card). */
export async function resetViana3CardDesign(machineId: number): Promise<Viana3CardDesign> {
    return handle<Viana3CardDesign>(await apiFetch(designPath(machineId), { method: 'DELETE' }));
}

// ─── Assets ───

export async function fetchViana3DesignAssets(): Promise<Viana3DesignAssetList> {
    return handle<Viana3DesignAssetList>(await apiFetch(`${BASE}/assets`));
}

/** Multipart upload ("file"). 400 DESIGN_INVALID (type/size/signature), 409 QUOTA_EXCEEDED. */
export async function uploadViana3DesignAsset(file: File): Promise<Viana3DesignAssetUploadResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return handle<Viana3DesignAssetUploadResult>(await apiFetch(`${BASE}/assets`, { method: 'POST', body: form }));
}

/** Two-phase delete. 409 ASSET_REFERENCED when a design still uses it. */
export async function deleteViana3DesignAsset(id: string): Promise<void> {
    await handle<void>(await apiFetch(`${BASE}/assets/${encodeURIComponent(id)}`, { method: 'DELETE' }));
}

/** Tombstones every unreferenced asset and runs the reconciliation sweep. */
export async function cleanupViana3DesignAssets(): Promise<Viana3DesignCleanupResult> {
    return handle<Viana3DesignCleanupResult>(await apiFetch(`${BASE}/assets/cleanup`, { method: 'POST' }));
}

/** Absolute URL of an asset (anonymous endpoint). */
export function viana3DesignAssetUrl(assetId: string): string {
    return `${BASE}/assets/${encodeURIComponent(assetId)}`;
}
