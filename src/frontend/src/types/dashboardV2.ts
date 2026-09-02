// Dashboard V2 (Phase B, slice B1+B2). TS mirror of DashboardV2Dtos.cs. The frontend RENDERS these
// server-calculated values and must NOT re-derive Buyer workflow semantics. Three planes:
//   Personal (PESSOAL) · Shared (COMPARTILHADO) · Workload (GERENCIAL).
// Any plane the current user is not entitled to arrives as null and is simply not rendered.

export interface BuyerPersonalSummaryDto {
  assignedRequests: number;
  actionableRequests: number;
  pendingQuotationItems: number;
  readyForBatchItems: number;
  adjustmentRequests: number;
  overdueActionableRequests: number;
  criticalActionableRequests: number;
}

export interface BuyerSharedQueueSummaryDto {
  unassignedRequests: number;
  unassignedActionableRequests: number;
  unassignedPendingItems: number;
  unassignedReadyItems: number;
  unassignedNeedsQuotationRequests: number;
  unassignedPartialCoverageRequests: number;
  unassignedReadyForApprovalRequests: number;
  unassignedAdjustmentRequests: number;
  unassignedOverdueActionableRequests: number;
  unassignedCriticalActionableRequests: number;
}

export interface BuyerWorkloadRowDto {
  buyerId?: string | null;
  buyerName?: string | null;
  isUnassigned: boolean;
  assignedRequests: number;
  actionableRequests: number;
  pendingQuotationItems: number;
  readyForBatchItems: number;
  needsQuotationRequests: number;
  partialCoverageRequests: number;
  readyForApprovalRequests: number;
  adjustmentRequests: number;
  overdueActionableRequests: number;
  criticalActionableRequests: number;
}

export interface BuyerWorkloadSummaryDto {
  rows: BuyerWorkloadRowDto[];
  unassigned?: BuyerWorkloadRowDto | null;
}

export interface DashboardV2BuyerSectionDto {
  personal?: BuyerPersonalSummaryDto | null;
  shared?: BuyerSharedQueueSummaryDto | null;
  workload?: BuyerWorkloadSummaryDto | null;
}

export interface DashboardV2BuyerParams {
  company?: number;
  plant?: number;
  department?: number;
  needLevel?: string;
}
