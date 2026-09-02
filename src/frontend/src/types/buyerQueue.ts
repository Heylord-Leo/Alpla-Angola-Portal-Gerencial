// Buyer operational queue (Phase 2). TS mirror of BuyerQueueDtos.cs — the frontend consumes these
// server-derived codes/labels/capabilities and must NOT re-derive the Buyer workflow.

export interface BuyerNextAction {
  code: string;
  label: string;
  actionable: boolean;
}

export interface BuyerAttentionSignal {
  code: string;
  severity: string; // BLOCKING | URGENT_DEADLINE | WARNING
}

export interface BuyerQueueItem {
  requestId: string;
  requestNumber: string;
  title?: string | null;

  requesterId?: string | null;
  requesterName?: string | null;

  companyName?: string | null;
  plantName?: string | null;
  departmentName?: string | null;
  requestStatusCode: string;

  needLevelCode?: string | null;
  needByDateUtc?: string | null;
  createdAtUtc: string;
  priorityBand: string; // EXCEPTION_OR_OVERDUE | STANDARD
  deadlineCondition: string; // OVERDUE | DUE_TODAY | APPROACHING | WITHIN_DEADLINE | NONE

  buyerId?: string | null;
  buyerName?: string | null;
  ownershipState: string; // MINE | UNASSIGNED | OTHER

  operationalState: string;
  operationalStateLabel: string;
  nextActions: BuyerNextAction[];
  coverageStatus: string;
  activeItemCount: number;
  coveredCount: number;
  pendingCount: number;
  quotationCount: number;
  activeBatchCount: number;
  coverageCounts: Record<string, number>;
  attentionSignals: BuyerAttentionSignal[];
  requiresAttention: boolean;

  hasNotes: boolean;
  noteCount: number;
  latestNoteText?: string | null;
  latestNoteAtUtc?: string | null;
  latestNoteActorName?: string | null;

  canOpen: boolean;
  canClaim: boolean;
  canReassign: boolean;
  canCancel: boolean;
  cancelBlockReason?: string | null;
}

export interface BuyerQueuePage {
  items: BuyerQueueItem[];
  page: number;
  pageSize: number;
  totalCount: number; // Requests, never line-items
  totalPages: number;
}

export interface BuyerQueueSummary {
  total: number;
  requiresAttention: number;
  needsAction: number;
  awaitingApproval: number;
  unassigned: number;
  byOperationalState: Record<string, number>;
}

export interface BuyerQueueParams {
  query?: string;
  company?: number;
  plant?: number;
  department?: number;
  ownership?: string; // all | me | unassigned
  buyer?: string; // explicit buyer GUID filter (Dashboard V2 workload drill-down)
  operationalState?: string;
  priority?: string;
  deadline?: string;
  needLevel?: string; // CRITICO | URGENTE | NORMAL | BAIXO (a real filter; 'ALL'/absent = no need filter)
  includeCompleted?: boolean;
  sort?: string;
  page?: number;
  pageSize?: number;
}
