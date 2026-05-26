import { apiFetch, API_BASE_URL } from './api';

// ─── Request DTOs ────────────────────────────────────────────────────────────

export interface CreateProcessingRunRequest {
  entityId: number;
  year: number;
  month: number;
}

export interface ExcludeItemRequest {
  reason: string;
}

export interface ResolveAnomalyRequest {
  action: 'APPROVE' | 'EXCLUDE';
  resolutionNote: string;
}

// ─── Response DTOs ───────────────────────────────────────────────────────────

export interface ProcessingRunSummaryDto {
  id: string;
  entityId: number;
  entityName: string;
  year: number;
  month: number;
  statusCode: string;
  statusLabel: string;
  syncedRowCount: number;
  occurrenceCount: number;
  anomalyCount: number;
  unresolvedCount: number;
  createdAtUtc: string;
  createdByEmail: string | null;
  syncedAtUtc: string | null;
  detectionCompletedAtUtc: string | null;
  errorMessage: string | null;
}

export interface ProcessingRunDetailDto extends ProcessingRunSummaryDto {
  approvedCount: number;
  adjustedCount: number;
  excludedCount: number;
  autoCodedCount: number;
  needsReviewCount: number;
}

export interface MonthlyChangeItemDto {
  id: string;
  employeeCode: string;
  employeeName: string;
  date: string;
  occurrenceType: string;
  detectionRule: string;
  durationMinutes: number;
  scheduleCode: string | null;
  statusCode: string;
  statusLabel: string;
  primaveraCode: string | null;
  primaveraCodeDescription: string | null;
  hours: number | null;
  costCenter: string | null;
  isManualOverride: boolean;
  isAnomaly: boolean;
  anomalyReason: string | null;
  createdAtUtc: string;
}

export interface AnomalyItemDto {
  id: string;
  employeeCode: string;
  employeeName: string;
  date: string;
  occurrenceType: string;
  anomalyReason: string | null;
  statusCode: string;
  statusLabel: string;
  resolutionNote: string | null;
  resolvedBy: string | null;
  resolvedAtUtc: string | null;
}

export interface ProcessingLogDto {
  eventType: string;
  message: string;
  actor: string | null;
  occurredAtUtc: string;
}

// ─── API Client ──────────────────────────────────────────────────────────────

async function handleHrApiError(res: Response): Promise<never> {
  const text = await res.text();
  let errorMsg = text;
  
  try {
    const json = JSON.parse(text);
    if (json.message) errorMsg = json.message;
    else if (json.title) errorMsg = json.title;
    else if (json.detail) errorMsg = json.detail;
  } catch (e) {
    // text is not JSON, leave as is
  }

  const translations: Record<string, string> = {
    "EntityId must be 1 (AlplaPLASTICO) or 6 (AlplaSOPRO).": "A Entidade deve ser 1 (AlplaPLASTICO) ou 6 (AlplaSOPRO).",
    "Year must be between 2020 and 2030.": "O ano deve estar entre 2020 e 2030.",
    "Month must be between 1 and 12.": "O mês deve estar entre 1 e 12.",
    "Exclusion reason is required.": "O motivo de exclusão é obrigatório.",
    "Resolution note is required.": "A nota de resolução é obrigatória.",
    "Action must be APPROVE or EXCLUDE.": "A ação deve ser APPROVE ou EXCLUDE.",
    "Run is not in DRAFT state": "O processamento não está em estado RASCUNHO (DRAFT).",
    "Run is not in NEEDS_REVIEW state": "O processamento não está em estado NECESSITA REVISÃO (NEEDS_REVIEW).",
    "Only AUTO_CODED or NEEDS_REVIEW items can be approved.": "Apenas itens codificados automaticamente (AUTO_CODED) ou em revisão (NEEDS_REVIEW) podem ser aprovados.",
    "Only PENDING items can be excluded.": "Apenas itens PENDENTES podem ser excluídos.",
    "Only EXCLUDED items can be re-included.": "Apenas itens EXCLUÍDOS podem ser re-incluídos.",
    "Cannot resolve non-anomaly item.": "Não é possível resolver um item que não seja uma anomalia.",
    "Cannot resolve an item that is already approved or excluded.": "Não é possível resolver um item que já está aprovado ou excluído.",
    "Run has unresolved anomalies.": "O processamento contém anomalias não resolvidas.",
    "Run is not in READY_FOR_EXPORT state": "O processamento não está no estado PRONTO PARA EXPORTAÇÃO (READY_FOR_EXPORT)."
  };

  if (translations[errorMsg]) {
    errorMsg = translations[errorMsg];
  } else if (errorMsg.includes("not found")) {
    errorMsg = "Registo não encontrado.";
  }

  throw new Error(errorMsg);
}

export const hrMonthlyChangesApi = {
  /**
   * Creates a new DRAFT processing run for the specified entity and month.
   */
  createRun: async (request: CreateProcessingRunRequest): Promise<ProcessingRunDetailDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  /**
   * Returns all processing runs, optionally filtered by entity.
   */
  listRuns: async (entityId?: number): Promise<ProcessingRunSummaryDto[]> => {
    const url = entityId 
      ? `${API_BASE_URL}/api/hr/monthly-changes/runs?entityId=${entityId}`
      : `${API_BASE_URL}/api/hr/monthly-changes/runs`;
    const res = await apiFetch(url);
    if (!res.ok) await handleHrApiError(res);
    if (res.status === 204) return [];
    try { return await res.json(); } catch { return []; }
  },

  /**
   * Returns full detail for a specific processing run.
   */
  getRunDetail: async (runId: string): Promise<ProcessingRunDetailDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}`);
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  /**
   * Executes the pipeline (Sync + Detect) for a DRAFT run.
   */
  executeRun: async (runId: string): Promise<ProcessingRunDetailDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/execute`, { method: 'POST' });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  /**
   * Returns all generated items for a specific processing run.
   */
  listItems: async (runId: string): Promise<MonthlyChangeItemDto[]> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/items`);
    if (!res.ok) await handleHrApiError(res);
    if (res.status === 204) return [];
    try { return await res.json(); } catch { return []; }
  },

  /**
   * Returns all anomalies associated with a specific processing run.
   */
  listAnomalies: async (runId: string): Promise<AnomalyItemDto[]> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/anomalies`);
    if (!res.ok) await handleHrApiError(res);
    if (res.status === 204) return [];
    try { return await res.json(); } catch { return []; }
  },

  /**
   * Returns processing logs for a specific run.
   */
  listLogs: async (runId: string): Promise<ProcessingLogDto[]> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/logs`);
    if (!res.ok) await handleHrApiError(res);
    if (res.status === 204) return [];
    try { return await res.json(); } catch { return []; }
  },

  // ─── Review Actions ──────────────────────────────────────────────────────────

  approveItem: async (runId: string, itemId: string): Promise<MonthlyChangeItemDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/items/${itemId}/approve`, {
      method: 'POST'
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  excludeItem: async (runId: string, itemId: string, request: ExcludeItemRequest): Promise<MonthlyChangeItemDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/items/${itemId}/exclude`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  reincludeItem: async (runId: string, itemId: string): Promise<MonthlyChangeItemDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/items/${itemId}/reinclude`, {
      method: 'POST'
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  resolveAnomaly: async (runId: string, itemId: string, request: ResolveAnomalyRequest): Promise<MonthlyChangeItemDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/items/${itemId}/resolve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  // ─── Run State Transitions ───────────────────────────────────────────────────

  markReadyForExport: async (runId: string): Promise<ProcessingRunDetailDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/ready-for-export`, {
      method: 'POST'
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  },

  revertToReview: async (runId: string): Promise<ProcessingRunDetailDto> => {
    const res = await apiFetch(`${API_BASE_URL}/api/hr/monthly-changes/runs/${runId}/revert-to-review`, {
      method: 'POST'
    });
    if (!res.ok) await handleHrApiError(res);
    return res.json();
  }
};
