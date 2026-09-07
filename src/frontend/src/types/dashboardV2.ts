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

// ── B5: "Minha Operação" (PESSOAL) — canonical personal actions only. Mirrors DashboardV2PersonalDtos.cs.
// The server returns ONLY work the signed-in user personally owns (assigned actionable Buyer work, owned
// Area-approval work, own DRAFT requests). Shared role work never appears; there are no urgency buckets
// (B5.1 deliberately exposes no defensible per-domain due date). The frontend renders these values and
// recomputes NOTHING (no role/status actionability, no NeedByDate math).
export interface PersonalActionDto {
  domain: string;        // BUYER | APPROVAL | REQUESTER
  entityType: string;    // REQUEST | APPROVAL_BATCH
  entityId: string;
  requestId: string;
  requestNumber: string;
  actionType: string;    // ADD_QUOTATION | SUBMIT_BATCH | RESOLVE_ADJUSTMENT | AREA_APPROVAL | SUBMIT_DRAFT
  title?: string | null;
  targetPath?: string | null;
  dueDate?: string | null; // always null in B5.1 (urgency deferred)
}

export interface PersonalActionDomainCountDto {
  domain: string;
  actions: number;
  requests: number;
}

export interface PersonalActionSummaryDto {
  actionableActions: number;
  actionableRequests: number;
  byDomain: PersonalActionDomainCountDto[];
}

export interface DashboardV2PersonalSectionDto {
  summary: PersonalActionSummaryDto;
  actions: PersonalActionDto[];
}

// ── B6: canonical Operational Pipeline (GERENCIAL, read-only). Mirrors DashboardV2PipelineDtos.cs.
// A request may appear in several stages at once (canOverlap); uniqueActiveRequests is the distinct
// active-request denominator, NEVER the sum of stage counts. Each stage is measured in entityType
// units. The frontend renders these values and computes nothing.
export interface OperationalPipelineStageDto {
  domain: string;      // PREPARACAO | COMPRAS | APROVACOES | PO | FINANCAS | RECEBIMENTO | DOCUMENTACAO | CONCLUSAO
  stage: string;       // stable stage code
  label: string;
  entityType: string;  // REQUEST | APPROVAL_BATCH | PO_GROUP | LINE_ITEM
  entityCount: number;
  requestCount: number;
  sortOrder: number;
  targetPath?: string | null; // set only where an exact canonical filter exists
  canOverlap: boolean;
}

export interface DashboardV2PipelineDto {
  uniqueActiveRequests: number;
  stages: OperationalPipelineStageDto[];
  generatedAtUtc: string;
}

// ── B7: canonical currency-safe Financial Summary (GERENCIAL, read-only). Mirrors DashboardV2FinancialDtos.cs.
// Amounts are NEVER combined across currencies (one row per currency, explicit UNKNOWN bucket); no FX.
// `currentExposure` is null when the caller is not entitled (Finance/Local Manager/SysAdmin) — the frontend
// hides the section. The frontend only formats these server values; it computes nothing.
export interface CurrencyAmountDto {
  currencyCode: string;   // ISO code, or "UNKNOWN"
  amount: number;         // decimal; per-currency sum only, never combined
  entityCount: number;
  requestCount: number;
}

export interface FinancialCategoryDto {
  code: string;           // EM_APROVACAO | AGUARDANDO_PO | EM_PROCESSAMENTO_FINANCEIRO | PAGO_AGUARDANDO_RECEBIMENTO
  label: string;
  entityType: string;     // APPROVAL_BATCH | PO_GROUP
  entityCount: number;
  requestCount: number;
  currencies: CurrencyAmountDto[];
  isAuthoritative: boolean; // false when some entities have no authoritative amount
}

// B7.3 — secondary paid-history summary: confirmed payment evidence within a period, by payment currency
// (never combined; no FX; refunds not netted). Null when not entitled, alongside currentExposure.
export interface PaidHistoryDto {
  periodCode: string;   // LAST_30_DAYS
  periodLabel: string;  // "Últimos 30 dias"
  fromUtc: string;
  toUtc: string;
  currencies: CurrencyAmountDto[]; // one row per currency; entityCount = payments in that currency
  paymentCount: number;
  requestCount: number;
  isAuthoritative: boolean;
}

export interface DashboardV2FinancialDto {
  currentExposure: FinancialCategoryDto[] | null; // null = not entitled → hide section
  paidHistory: PaidHistoryDto | null;             // null = not entitled
  generatedAtUtc: string;
}

/** Stable backend code for an unresolved currency; the UI labels it "Moeda não identificada". */
export const UNKNOWN_CURRENCY = 'UNKNOWN';

// ── B8: canonical Alerts (read-only). Mirrors DashboardV2AlertsDtos.cs. An alert is a risk/deadline
// condition over a canonical entity that still has an OPEN action — higher-signal than the queues, never
// a mirror of them. B8 covers Buyer (need-by date, gated to an open buyer action) and Finance (scheduled
// payment date, still scheduled). `summary` is null when the caller is not entitled → the frontend hides
// the section. The frontend renders these server values and recomputes NOTHING (no status/actionability,
// no urgency math beyond formatting the server's daysDelta into wording). The list is server-ordered
// (critical, then date, then domain, then entity) and bounded — never re-sorted or paginated client-side.
export interface DashboardV2AlertDto {
  id: string;             // stable identity = Domain:EntityType:EntityId:AlertType
  domain: string;         // BUYER | FINANCE
  entityType: string;     // REQUEST | PO_GROUP
  entityId: string;
  requestId: string;
  requestNumber: string;
  alertType: string;      // BUYER_OVERDUE | BUYER_DUE_TODAY | BUYER_DUE_SOON | FINANCE_SCHEDULED_OVERDUE | FINANCE_SCHEDULED_DUE_SOON
  severity: string;       // ATTENTION | CRITICAL
  plane: string;          // PESSOAL | COMPARTILHADO | GERENCIAL
  title: string;
  description: string;
  dateUtc: string;        // the relevant need-by / scheduled date
  daysDelta: number;      // signed days vs today (negative = overdue); the frontend only formats wording
  targetPath?: string | null;
  canNavigate: boolean;
}

export interface DashboardV2AlertDomainSummaryDto {
  domain: string;         // BUYER | FINANCE
  attention: number;
  critical: number;
}

export interface DashboardV2AlertsSummaryDto {
  attentionCount: number; // full deduplicated population (never derived from the visible list)
  criticalCount: number;
  byDomain: DashboardV2AlertDomainSummaryDto[];
  totalAlertCount: number;     // deduped population BEFORE the display cap
  displayedAlertCount: number; // alerts actually returned in `alerts` AFTER the cap
  isTruncated: boolean;        // totalAlertCount > displayedAlertCount
}

export interface DashboardV2AlertsDto {
  summary: DashboardV2AlertsSummaryDto | null; // null = not entitled → hide section
  alerts: DashboardV2AlertDto[];
  generatedAtUtc: string;
}

// ── B9.4/B9.5: canonical Stage Aging ("Gargalos", GERENCIAL, read-only). Mirrors DashboardV2StageAgingDtos.cs.
// Age is time-in-current-stage (Africa/Luanda calendar days), NEVER request age. Unknown age is first-class:
// `oldestAgeDays`/`oldestStageEnteredAtUtc` are null when no known-age entity exists, and the severity counts
// (`normalCount`/`attentionCount`/`criticalCount`) are null for thresholdless stages (Finance/Documentation).
// The frontend must NOT coerce these nulls to 0. `summary` is null when the caller is not entitled.
export interface StageAgingThresholdProfileDto {
  attentionAfterDays: number; // age strictly greater → ATTENTION
  criticalAfterDays: number;  // age strictly greater → CRITICAL
  isFormalSla: boolean;       // always false — operational guidance only
}

export interface DashboardV2StageAgingStageDto {
  domain: string;
  stageCode: string;
  label: string;
  entityType: string;         // APPROVAL_BATCH | PO_GROUP
  sortOrder: number;
  entityCount: number;
  requestCount: number;
  knownAgeEntityCount: number;
  unknownAgeEntityCount: number;
  normalCount: number | null;    // null = thresholdless stage (no severity)
  attentionCount: number | null;
  criticalCount: number | null;
  oldestStageEnteredAtUtc: string | null;
  oldestAgeDays: number | null;  // null = no known-age entity
  thresholdProfile: StageAgingThresholdProfileDto | null;
  targetPath: string | null;
  canNavigate: boolean;          // false in B9.4/B9.5 (managerial analytics, read-only)
}

export interface DashboardV2StageAgingSummaryDto {
  totalActiveEntities: number;
  totalActiveRequests: number;
  knownAgeEntities: number;
  unknownAgeEntities: number;
  attentionEntities: number;
  criticalEntities: number;
}

export interface DashboardV2StageAgingDto {
  summary: DashboardV2StageAgingSummaryDto | null; // null = not entitled → hide section
  stages: DashboardV2StageAgingStageDto[];
  generatedAtUtc: string;
}
