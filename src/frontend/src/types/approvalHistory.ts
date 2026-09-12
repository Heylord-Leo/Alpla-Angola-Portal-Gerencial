// v2.244.0 Approval Center V2 — Phase 2. Read-only approval history / audit timeline shapes.
// Mirrors the backend ApprovalHistory DTOs (AlplaPortal.Application.DTOs.Approvals).

export type ApprovalDecisionCode = 'APPROVED' | 'REJECTED' | 'RETURNED' | 'RESUBMITTED';
export type ApprovalStageCode = 'AREA' | 'FINAL';

export interface ApprovalHistoryRow {
  id: string;
  requestId: string;
  requestNumber: string;
  requestTitle: string;
  requestTypeCode: string;
  approvalLevel: ApprovalStageCode | null;
  decision: ApprovalDecisionCode | null;
  actionTaken: string;
  batchNumber: number | null;
  requesterName: string;
  departmentName: string | null;
  companyName: string | null;
  plantName: string | null;
  approverUserId: string;
  approverName: string;
  decisionAtUtc: string;
  comment: string | null;
  amount: number | null;
  currencyCode: string | null;
  supplierName: string | null;
  previousStatusCode: string | null;
  newStatusCode: string | null;
}

export interface ApprovalHistoryPage {
  items: ApprovalHistoryRow[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  approvedCount: number;
  rejectedCount: number;
  returnedCount: number;
  resubmittedCount: number;
}

export interface ApprovalTimelineEvent {
  id: string;
  actionTaken: string;
  actionLabel: string;
  approvalLevel: ApprovalStageCode | null;
  decision: ApprovalDecisionCode | null;
  isDecision: boolean;
  actorUserId: string;
  actorName: string;
  comment: string | null;
  previousStatusCode: string | null;
  newStatusCode: string | null;
  batchNumber: number | null;
  createdAtUtc: string;
}

// Filters the HISTÓRICO tab sends; every field optional = not applied.
export interface ApprovalHistoryQuery {
  search?: string;
  decision?: ApprovalDecisionCode;
  stage?: ApprovalStageCode;
  approverId?: string;
  requestType?: string;
  departmentId?: number;
  companyId?: number;
  plantId?: number;
  dateFrom?: string;
  dateTo?: string;
  page?: number;
  pageSize?: number;
  sort?: string;
}
