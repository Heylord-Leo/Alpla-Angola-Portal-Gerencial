/**
 * Operations Module — API Client
 *
 * Follows the same pattern as contractsApi.ts:
 * - Uses apiFetch + API_BASE_URL from ./api
 * - Throws ApiError on non-OK responses
 * - Page components catch ApiError and check .status for differentiated handling
 *
 * @since v2.164.0 — Phase 3 Frontend MVP (timeline)
 * @since v2.166.0 — Phase 5 Frontend List Integration (list)
 * @since v2.171.0 — Phase 6 Transfer Details (details)
 */

import { apiFetch, API_BASE_URL, ApiError } from './api';
import type {
    OperationsTimelineResponse,
    OperationsTransferListResponse,
    OperationsTransferListFilters,
    OperationsTransferDetail,
    OperationsLiveBoardResponse,
    Viana3MonitorResponse,
    Viana3MonitorDiagnosticsResponse,
    Viana3MonitorLayout,
    Viana3MonitorScreen,
    SaveViana3MonitorLayoutRequest,
    CreateViana3MonitorScreenRequest,
    RenameViana3MonitorScreenRequest,
} from '../types/operations.types';

const BASE = `${API_BASE_URL}/api/operations/transfers`;
const LIVE_BOARD_BASE = `${API_BASE_URL}/api/operations/live-board`;
const VIANA3_MONITOR_BASE = `${API_BASE_URL}/api/operations/viana3-monitor`;

// ─── Helpers ───

async function handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
        const text = await response.text();
        let message = text || `HTTP ${response.status}`;

        // Try to extract structured message from JSON error body
        try {
            const json = JSON.parse(text);
            if (json.message) message = json.message;
            else if (json.error) message = json.error;
        } catch {
            // Not JSON — use raw text
        }

        throw new ApiError(message, response.status);
    }
    return response.json();
}

// ─── API Functions ───

/**
 * Fetches the normalized timeline for a purchase order in a specific plant.
 *
 * GET /api/operations/transfers/{plant}/{idBestellung}/timeline
 *
 * @param plant   Plant code: VIANA1, VIANA2, VIANA3
 * @param idBestellung  Purchase order ID (positive integer)
 * @returns Normalized timeline with events ordered by sortOrder
 *
 * @throws ApiError(400) — Invalid plant or ID
 * @throws ApiError(404) — PO not found in plant
 * @throws ApiError(503) — Integration disabled/unavailable
 * @throws ApiError(500) — Unexpected server error
 */
export async function fetchOperationsTimeline(
    plant: string,
    idBestellung: number
): Promise<OperationsTimelineResponse> {
    const res = await apiFetch(`${BASE}/${encodeURIComponent(plant)}/${idBestellung}/timeline`);
    return handleResponse<OperationsTimelineResponse>(res);
}

/**
 * Fetches a paginated, filterable list of transfers/purchase orders.
 *
 * GET /api/operations/transfers?plant=...&dateFrom=...&dateTo=...
 *
 * @param filters  Filter parameters (plant, dateFrom, dateTo required)
 * @returns Paginated list with metadata
 *
 * @throws ApiError(400) — Invalid filters or date range
 * @throws ApiError(401) — Not authenticated
 * @throws ApiError(503) — Integration disabled/unavailable
 * @throws ApiError(500) — Unexpected server error
 */
export async function fetchOperationsTransfers(
    filters: OperationsTransferListFilters
): Promise<OperationsTransferListResponse> {
    const params = new URLSearchParams();

    // Required
    params.append('plant', filters.plant);
    params.append('dateFrom', filters.dateFrom);
    params.append('dateTo', filters.dateTo);

    // Optional — only send if non-empty
    if (filters.status?.trim()) params.append('status', filters.status.trim());
    if (filters.articleSearch?.trim()) params.append('articleSearch', filters.articleSearch.trim());
    if (filters.poSearch?.trim()) params.append('poSearch', filters.poSearch.trim());

    // Pagination
    params.append('page', String(filters.page));
    params.append('pageSize', String(filters.pageSize));

    const res = await apiFetch(`${BASE}?${params.toString()}`);
    return handleResponse<OperationsTransferListResponse>(res);
}

/**
 * Fetches detailed transfer information for a single purchase order.
 *
 * GET /api/operations/transfers/{plant}/{idBestellung}/details
 *
 * @param plant   Plant code: VIANA1, VIANA2, VIANA3
 * @param idBestellung  Purchase order ID (positive integer)
 * @returns Detailed transfer data (header, material, quantity, loading, receipt, tech refs)
 *
 * @throws ApiError(400) — Invalid plant or ID
 * @throws ApiError(404) — PO not found in plant
 * @throws ApiError(503) — Integration disabled/unavailable
 * @throws ApiError(500) — Unexpected server error
 */
export async function fetchOperationsTransferDetails(
    plant: string,
    idBestellung: number
): Promise<OperationsTransferDetail> {
    const res = await apiFetch(`${BASE}/${encodeURIComponent(plant)}/${idBestellung}/details`);
    return handleResponse<OperationsTransferDetail>(res);
}

/**
 * Fetches the Live Transfer Board data for a specific plant.
 *
 * GET /api/operations/live-board?plant={plant}
 *
 * Returns pre-simplified, pre-classified transfers ready for TV rendering.
 *
 * @param params.plant           Plant code: VIANA1, VIANA2, VIANA3
 * @param params.refreshSeconds  Suggested refresh interval (30–300, default 60)
 * @param params.maxInbound      Max inbound cards to return (default 8)
 * @param params.maxOutbound     Max outbound cards to return (default 8)
 * @param params.includeRecentlyCompleted  Include completed transfers (default true)
 * @param params.completedWindowHours      Hours to keep completed visible (default 4)
 *
 * @throws ApiError(400) — Invalid plant
 * @throws ApiError(401) — Not authenticated
 * @throws ApiError(503) — Integration disabled/unavailable
 * @throws ApiError(500) — Unexpected server error
 *
 * @since v2.178.0 — Phase Live 3 Frontend TV Page
 */
export async function fetchOperationsLiveBoard(params: {
    plant: string;
    refreshSeconds?: number;
    maxInbound?: number;
    maxOutbound?: number;
    includeRecentlyCompleted?: boolean;
    completedWindowHours?: number;
}): Promise<OperationsLiveBoardResponse> {
    const qs = new URLSearchParams();
    qs.append('plant', params.plant);
    if (params.refreshSeconds != null) qs.append('refreshSeconds', String(params.refreshSeconds));
    if (params.maxInbound != null) qs.append('maxInbound', String(params.maxInbound));
    if (params.maxOutbound != null) qs.append('maxOutbound', String(params.maxOutbound));
    if (params.includeRecentlyCompleted != null) qs.append('includeRecentlyCompleted', String(params.includeRecentlyCompleted));
    if (params.completedWindowHours != null) qs.append('completedWindowHours', String(params.completedWindowHours));

    const res = await apiFetch(`${LIVE_BOARD_BASE}?${qs.toString()}`);
    return handleResponse<OperationsLiveBoardResponse>(res);
}


// ─── Viana 3 Production Monitor (TV) ───

/**
 * Fetches the Viana 3 Production Monitor payload (read-only AlplaPROD_aovia3 projection).
 *
 * GET /api/operations/viana3-monitor?refreshSeconds=120
 *
 * Anonymous endpoint (TV signage). When AlplaPROD is unavailable the backend
 * returns its last successful snapshot with `source.status === 'STALE'`.
 *
 * @throws ApiError(503) — Integration disabled / AlplaPROD unreachable and no snapshot
 * @throws ApiError(500) — Unexpected server error
 */
export async function fetchViana3Monitor(params?: { refreshSeconds?: number }): Promise<Viana3MonitorResponse> {
    const qs = new URLSearchParams();
    if (params?.refreshSeconds !== undefined && !isNaN(params.refreshSeconds)) {
        qs.set('refreshSeconds', String(params.refreshSeconds));
    }
    const query = qs.toString();
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}${query ? `?${query}` : ''}`);
    return handleResponse<Viana3MonitorResponse>(res);
}

/**
 * Fetches the Viana 3 monitor diagnostics (historical open system balance at the
 * machine areas, Waste on deleted lots). Cached one hour server-side.
 *
 * GET /api/operations/viana3-monitor/diagnostics
 */
export async function fetchViana3MonitorDiagnostics(): Promise<Viana3MonitorDiagnosticsResponse> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/diagnostics`);
    return handleResponse<Viana3MonitorDiagnosticsResponse>(res);
}

// ─── Viana 3 Production Monitor: shared TV screens / layouts ───

/**
 * Reads the layout of the DEFAULT screen (historical TV URL). Anonymous. Display preferences only.
 *
 * GET /api/operations/viana3-monitor/layout
 */
export async function fetchViana3MonitorLayout(): Promise<Viana3MonitorLayout> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/layout`);
    return handleResponse<Viana3MonitorLayout>(res);
}

/**
 * Reads the layout of one screen. Anonymous — the read-only TV loads it with every data refresh.
 *
 * GET /api/operations/viana3-monitor/screens/{screenKey}/layout
 * @throws ApiError(404) — unknown screen
 */
export async function fetchViana3MonitorScreenLayout(screenKey: string): Promise<Viana3MonitorLayout> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens/${encodeURIComponent(screenKey)}/layout`);
    return handleResponse<Viana3MonitorLayout>(res);
}

/** Screen list (editor). OPERATIONS / System Administrator. GET /api/operations/viana3-monitor/screens */
export async function fetchViana3MonitorScreens(): Promise<Viana3MonitorScreen[]> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens`);
    return handleResponse<Viana3MonitorScreen[]>(res);
}

/** Creates a named screen (stable generated key). POST /api/operations/viana3-monitor/screens */
export async function createViana3MonitorScreen(body: CreateViana3MonitorScreenRequest): Promise<Viana3MonitorLayout> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
    return handleResponse<Viana3MonitorLayout>(res);
}

/** Renames a screen. PUT /api/operations/viana3-monitor/screens/{screenKey} */
export async function renameViana3MonitorScreen(screenKey: string, body: RenameViana3MonitorScreenRequest): Promise<Viana3MonitorScreen> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens/${encodeURIComponent(screenKey)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
    return handleResponse<Viana3MonitorScreen>(res);
}

/**
 * Replaces the layout of one screen. OPERATIONS / System Administrator only.
 *
 * PUT /api/operations/viana3-monitor/screens/{screenKey}/layout
 * @throws ApiError(400) — invalid layout document (message in Portuguese)
 * @throws ApiError(401/403) — not allowed
 */
export async function saveViana3MonitorScreenLayout(screenKey: string, body: SaveViana3MonitorLayoutRequest): Promise<Viana3MonitorLayout> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens/${encodeURIComponent(screenKey)}/layout`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
    return handleResponse<Viana3MonitorLayout>(res);
}

/** Resets one screen's layout. DELETE /api/operations/viana3-monitor/screens/{screenKey}/layout */
export async function resetViana3MonitorScreenLayout(screenKey: string): Promise<Viana3MonitorLayout> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens/${encodeURIComponent(screenKey)}/layout`, { method: 'DELETE' });
    return handleResponse<Viana3MonitorLayout>(res);
}

/** Deletes a named screen (the default screen is refused with 400). DELETE /api/operations/viana3-monitor/screens/{screenKey} */
export async function deleteViana3MonitorScreen(screenKey: string): Promise<void> {
    const res = await apiFetch(`${VIANA3_MONITOR_BASE}/screens/${encodeURIComponent(screenKey)}`, { method: 'DELETE' });
    if (!res.ok) await handleResponse<unknown>(res);
}
