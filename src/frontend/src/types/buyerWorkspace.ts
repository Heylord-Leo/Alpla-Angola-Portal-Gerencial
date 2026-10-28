// Buyer Request Workspace (Phase 3A) — TS mirror of BuyerWorkspaceDto.cs. Read-only; server-derived.
import type { BuyerNextAction } from './buyerQueue';
import type { ActiveApprovedTotalsDto, LastFinanceReturnDto, PoGroupCancellationDto, PriorEcfConfirmationDto } from './index';

export interface BuyerWorkspaceCoverage {
  totalItems: number;
  treated: number;
  pending: number;
  coverageStatus: string;
  approved: number;
  inActiveBatch: number;
  readyForBatch: number;
  closedNotQuoted: number;
  notQuotedProposed: number;
  notQuotedAccepted: number;
  cancelledDeleted: number;
}

export interface BuyerWorkspaceItem {
  id: string;
  lineNumber: number;
  itemCatalogCode?: string | null;
  description: string;
  quantity: number;
  unitName?: string | null;
  coverageBucket: string;
  supplierName?: string | null;
  selectedQuotationSummary?: string | null;
  /** Server-computed: whether this item may be closed via "Desconsiderar item" (close-not-quoted). */
  canCloseNotQuoted: boolean;
}

export interface BuyerWorkspaceQuotation {
  id: string;
  supplierId?: number | null;
  supplierName?: string | null;
  documentNumber?: string | null;
  documentDate?: string | null;
  itemsQuotedCount: number;
  currency?: string | null;
  totalAmount: number;
  documentCount: number;
  isSelected: boolean;
  /** §7 — ever submitted for approval: ordinary Editar/Excluir are locked (server decision). */
  isSubmittedForApproval?: boolean;
  submittedBatchNumber?: number | null;
  submissionLockReason?: string | null;
  /** v2.249.2 — items needing an explicit reuse authorization before entering a new batch (DEC-147 / D3). */
  reuseBlockedItemCount?: number;
  /** v2.249.2 — items with an active reuse authorization. */
  reuseAuthorizedItemCount?: number;
}

export interface BuyerWorkspaceBatchAdjustmentReason {
  reasonCode: string;
  requestLineItemId?: string | null;
  lineNumber?: number | null;
  detail?: string | null;
}

export interface BuyerWorkspaceBatchAdjustment {
  cycleNumber: number;
  sourceStage: string; // AREA | FINAL
  status: string;      // WAITING_BUYER | WAITING_REQUESTER | ...
  wholeBatch: boolean;
  approverComment: string;
  requestedByName?: string | null;
  requestedAtUtc: string;
  reasons: BuyerWorkspaceBatchAdjustmentReason[];
  // Phase 4 — the Buyer's "Resposta ao reajuste" once the cycle is resolved/resubmitted
  // (null while still open). Read-only display.
  responseNote?: string | null;
  respondedByName?: string | null;
  respondedAtUtc?: string | null;
}

export interface BuyerWorkspaceBatch {
  id: string;
  batchNumber: number;
  status: string;
  kind: string; // ACTIVE | APPROVED | REJECTED | CANCELLED | SUPERSEDED
  itemCount: number;
  itemLineNumbers: number[];
  approvedTotalAmount?: number | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  createdByName?: string | null;
  areaDecisionAtUtc?: string | null;
  /** Adjustment V2 (Phase 3): the batch's OPEN structured adjustment cycle, or null when none. */
  adjustment?: BuyerWorkspaceBatchAdjustment | null;
  // ── Phase 1 P.O. cancellation (D5/D7): the batch stays APPROVED; released items are shown explicitly ──
  releasedItemCount?: number;
  releasedLineNumbers?: number[];
  /** Per currency — NEVER summed across currencies. */
  activeTotalsByCurrency?: CurrencyAmount[];
  releasedTotalsByCurrency?: CurrencyAmount[];
}

/** One P.O. group of the request as the Buyer sees it, with the server-decided actions (correct / cancel) and blocks. */
export interface BuyerWorkspacePoGroup {
  id: string;
  status: string;
  supplierId?: number | null;
  supplierName?: string | null;
  currencyCode?: string | null;
  totalAmount: number;
  purchaseOrderNumber?: string | null;
  approvalBatchNumber?: number | null;
  paymentConditionCode?: string | null;
  plantId?: number | null;
  lineNumbers: number[];
  createdAtUtc: string;
  rowVersion?: string | null;
  poResponsibleBuyerId?: string | null;
  lastFinanceReturn?: LastFinanceReturnDto | null;
  cancellation?: PoGroupCancellationDto | null;
  /** v2.249.2 — WAITING_PO: the group awaits its FIRST P.O. (registration on the request page). */
  canRegisterPo?: boolean;
  /** WAITING_PO_CORRECTION only (returned by Finance). v2.249.2: never true for WAITING_PO. */
  canCorrectPo: boolean;
  canCancel: boolean;
  cancelBlockCode?: string | null;
  cancelBlockMessage?: string | null;
  financeConfirmationWouldBeRequired: boolean;
  priorEcfConfirmation?: PriorEcfConfirmationDto | null;
}

export interface CurrencyAmount {
  currency: string;
  amount: number;
}

export interface BuyerWorkspaceSupplier {
  supplierId?: number | null;
  name: string;
  nif?: string | null;
  isActive: boolean;
  registrationStatus?: string | null;
  purchaseCount: number;
  totalsByCurrency: CurrencyAmount[];
  lastPurchaseUtc?: string | null;
  quotationsReceived: number;
  quotationsSelected: number;
  involvedSelected: boolean;
  canOpenSheet: boolean;
}

export interface BuyerWorkspace {
  requestId: string;
  requestNumber: string;
  title?: string | null;
  description?: string | null;
  requestStatusCode: string;
  /** QUOTATION | PAYMENT (request-level "Assumir pedido" applies to QUOTATION only). */
  requestTypeCode?: string | null;

  requesterId?: string | null;
  requesterName?: string | null;
  buyerId?: string | null;
  buyerName?: string | null;
  createdByName?: string | null;

  companyName?: string | null;
  companyTaxId?: string | null;
  plantName?: string | null;
  departmentName?: string | null;

  needLevelCode?: string | null;
  needByDateUtc?: string | null;
  createdAtUtc: string;

  operationalState: string;
  operationalStateLabel: string;
  nextActions: BuyerNextAction[];
  priorityBand: string;
  deadlineCondition: string;
  requiresAttention: boolean;

  coverage: BuyerWorkspaceCoverage;
  items: BuyerWorkspaceItem[];
  quotations: BuyerWorkspaceQuotation[];
  batches: BuyerWorkspaceBatch[];
  suppliers: BuyerWorkspaceSupplier[];
  // ── Phase 1 P.O. cancellation / return to quotation ──
  poGroups?: BuyerWorkspacePoGroup[];
  activeApprovedTotals?: ActiveApprovedTotalsDto | null;
  // ── v2.249.2 — quotation window as the SERVER sees it (scalar window OR item-level window) ──
  quotationWindowOpen?: boolean;
  quotationWindowClosedReasonCode?: string | null;
  openLineItemIds?: string[];
}
