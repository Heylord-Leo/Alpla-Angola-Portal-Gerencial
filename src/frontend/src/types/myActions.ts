// v2.243.0 Para Minha Ação V2 — frontend types mirroring the released backend contract
// GET /api/v1/requests/my-actions (PersonalActionProjectionService / PersonalActionItemDto).
// Derived projection data — no persistence. Fields match the backend DTO exactly.

/** One PO group affected by a correction action (aggregated onto a single PO_CORRECTION item). */
export interface PersonalActionAffectedSupplier {
  poGroupId: string;
  supplierId?: number | null;
  supplierName?: string | null;
  purchaseOrderNumber?: string | null;
}

/** A single actionable unit of a user's personal work (one request may yield several). */
export interface PersonalActionItem {
  actionId: string;
  requestId: string;
  requestNumber: string;
  requestTitle: string;
  requestTypeCode: string;

  actionType: string;
  actionLabel: string;
  actionStatus: string;

  poGroupId?: string | null;
  supplierId?: number | null;
  supplierName?: string | null;
  purchaseOrderNumber?: string | null;
  affectedSuppliers: PersonalActionAffectedSupplier[];

  ownerUserId: string;
  ownerName?: string | null;

  dueDateUtc?: string | null;
  needLevelCode?: string | null;
  stageEnteredAtUtc?: string | null;
  createdAtUtc: string;
  isOverdue: boolean;
  priorityBand: string;

  amount?: number | null;
  currencyCode?: string | null;

  /** Future-ready deep-link hint the CTA navigates to (Phase 2 consumes targeting params). */
  route: string;
}

/** One action category (chip) with its count and highest priority band present. */
export interface MyActionCategory {
  actionType: string;
  label: string;
  count: number;
  highestPriorityBand?: string | null;
}

/** my-actions response: the full category summary (chips) plus the paged item slice. */
export interface MyActionsResponse {
  categories: MyActionCategory[];
  items: PersonalActionItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Query parameters for GET /api/v1/requests/my-actions. */
export interface MyActionsParams {
  actionType?: string;
  page?: number;
  pageSize?: number;
  sort?: string;
  targetRequestId?: string;
  targetPoGroupId?: string;
  targetActionType?: string;
}
