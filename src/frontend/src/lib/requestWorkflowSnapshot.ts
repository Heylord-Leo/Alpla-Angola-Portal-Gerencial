/**
 * docs §G.19.3 — the workflow state a request detail shows after any action is derived from ONE server payload
 * (GET /api/v1/requests/{id}), never from a client-side assumption about the destination status.
 *
 * Display precedence is the Phase 4B.2 rule: the server's group-aware display label first, the persisted status
 * name otherwise. Groups, history, attachments and quotations are refreshed together so no projection (badge,
 * action panels, correction decision) can read a stale group next to a fresh scalar.
 */
export interface WorkflowSnapshot {
    status: string;
    statusFullName: string;
    statusBadgeColor: string;
    statusHistory: any[];
    attachments: any[];
    quotations: any[];
    selectedQuotationId: string | null;
    poGroups: any[];
}

export interface WorkflowSnapshotSource {
    statusCode: string;
    statusName: string;
    statusBadgeColor?: string | null;
    displayStatusName?: string | null;
    statusHistory?: any[] | null;
    attachments?: any[] | null;
    quotations?: any[] | null;
    selectedQuotationId?: string | null;
    poGroups?: any[] | null;
}

export function deriveWorkflowSnapshot(data: WorkflowSnapshotSource): WorkflowSnapshot {
    return {
        status: data.statusCode,
        statusFullName: data.displayStatusName ?? data.statusName,
        statusBadgeColor: data.statusBadgeColor ?? '',
        statusHistory: data.statusHistory || [],
        attachments: data.attachments || [],
        quotations: data.quotations || [],
        selectedQuotationId: data.selectedQuotationId || null,
        poGroups: data.poGroups || [],
    };
}
