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

// ── B3: Finance shared queue (operational counts; no monetary amounts — those are B7) ──
// The server encodes entitlement by which plane is present: `shared` = Finance-role user (operational,
// cards drill into /finance/payments); `managerial` = Local Manager/SysAdmin without Finance (view-only,
// no navigation). Both null → user has no Finance section.
export interface FinanceSharedQueueSummaryDto {
  actionableGroups: number;
  actionableRequests: number;
  needsSchedulingGroups: number;
  needsPaymentGroups: number;
  dueTodayGroups: number;
  overdueGroups: number;
  paidWaitingReceivingGroups: number;
}

export interface DashboardV2FinanceSectionDto {
  shared?: FinanceSharedQueueSummaryDto | null;
  managerial?: FinanceSharedQueueSummaryDto | null;
}

// ── B4: Receiving shared queue (operational counts; no aging, no money). Plane presence encodes
// entitlement: `shared` = Receiving-role user (operational, cards drill into the group-level workspace);
// `managerial` = Local Manager/SysAdmin without Receiving (view-only, no navigation). ──
export interface ReceivingSharedQueueSummaryDto {
  actionableGroups: number;
  actionableRequests: number;
  readyForReceiptGroups: number;
  waitingReceiptGroups: number;
  followUpGroups: number;
  waitingSupplierDeliveryGroups: number;
}

export interface DashboardV2ReceivingSectionDto {
  shared?: ReceivingSharedQueueSummaryDto | null;
  managerial?: ReceivingSharedQueueSummaryDto | null;
}

// Group-level Receiving queue (drill-down; reconciles exactly with the dashboard summary).
export interface ReceivingQueueRowDto {
  requestId: string;
  requestNumber: string;
  requestTypeCode: string;
  title?: string | null;
  requestPoGroupId: string;
  groupStatus: string;
  supplierName?: string | null;
  purchaseOrderNumber?: string | null;
  actionableBucket: string; // READY_FOR_RECEIPT | WAITING_RECEIPT | IN_FOLLOWUP | WAITING_SUPPLIER_DELIVERY
  availableActions: string[];
}

export interface ReceivingQueueResponseDto {
  rows: ReceivingQueueRowDto[];
  summary: ReceivingSharedQueueSummaryDto;
}

/** Canonical Receiving drill-down buckets (mirror ReceivingActionEvaluator.Buckets). */
export type ReceivingBucket = 'READY_FOR_RECEIPT' | 'WAITING_RECEIPT' | 'IN_FOLLOWUP' | 'WAITING_SUPPLIER_DELIVERY';
