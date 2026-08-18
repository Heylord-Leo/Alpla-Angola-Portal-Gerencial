namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// DTO for creating an ApprovalBatch via POST /api/v1/requests/{id}/batches.
/// </summary>
public class CreateApprovalBatchDto
{
    /// <summary>Items to include in the batch, each with the candidate options the Area Approver
    /// will choose from. The Buyer never selects a winner — there is deliberately no winner field
    /// anywhere in this contract.</summary>
    public List<BatchItemDto> Items { get; set; } = new();

    /// <summary>Optional buyer comment describing the batch.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Buyer's batch-composition decision (INCLUDE/EXCLUDE) for every genuine EXTRA_ITEM
    /// quotation line belonging to a quotation contributing a winner to this batch. Keys are
    /// QuotationItemId. Every such line must have an entry or CreateBatch rejects with
    /// EXTRA_ITEMS_PENDING_DECISION. IGNORED lines are never part of this dictionary.
    /// </summary>
    public Dictionary<Guid, ExtraItemDecisionDto>? ExtraItemDecisions { get; set; }
}

/// <summary>
/// Single item within a batch create/update request. Candidate model: the client only IDENTIFIES
/// quotation lines (plus an optional note); every commercial value is snapshotted server-side.
/// </summary>
public class BatchItemDto
{
    /// <summary>The request line item to include in the batch.</summary>
    public Guid RequestLineItemId { get; set; }

    /// <summary>The candidate quotation options for this item (at least one required).</summary>
    public List<BatchCandidateInputDto> Candidates { get; set; } = new();
}

/// <summary>One candidate option the Buyer submits for a requested item.</summary>
public class BatchCandidateInputDto
{
    public Guid QuotationItemId { get; set; }

    /// <summary>Optional informational note, frozen with the snapshot. Never implies winner or
    /// preference semantics.</summary>
    public string? BuyerNote { get; set; }
}

/// <summary>
/// Detailed response DTO for a single ApprovalBatch (used for GET detail and POST response).
/// </summary>
public class ApprovalBatchDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int BatchNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public decimal? ApprovedTotalAmount { get; set; }
    public string? BudgetJustification { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public List<ApprovalBatchItemDto> Items { get; set; } = new();

    // ── Pre-decision candidate summary (decision 5) ──
    // Never persisted; computed from candidate snapshots. All null for legacy batches.

    /// <summary>Total number of candidate options across all items.</summary>
    public int CandidateOptionCount { get; set; }

    /// <summary>Distinct suppliers across all candidate options.</summary>
    public int CandidateSupplierCount { get; set; }

    /// <summary>Cheapest possible combination (sum of each item's lowest candidate LineTotal).
    /// Null unless every item has candidates in a single shared currency. Informational only —
    /// NEVER a persisted approved amount.</summary>
    public decimal? MinCandidateCombinationTotal { get; set; }

    /// <summary>Most expensive possible combination, same conditions as the minimum.</summary>
    public decimal? MaxCandidateCombinationTotal { get; set; }
}

/// <summary>
/// Response DTO for a single item within an ApprovalBatch.
/// </summary>
public class ApprovalBatchItemDto
{
    public Guid Id { get; set; }
    public Guid RequestLineItemId { get; set; }
    public string? RequestLineItemDescription { get; set; }
    public int? RequestLineItemLineNumber { get; set; }
    public decimal? RequestLineItemQuantity { get; set; }

    /// <summary>Winning quotation item. Candidate model: null until the Area decision.
    /// Legacy batches: the buyer-selected winner from the old flow.</summary>
    public Guid? SelectedQuotationItemId { get; set; }
    public string? SelectedQuotationItemDescription { get; set; }
    public decimal? SelectedQuotationItemUnitPrice { get; set; }
    public decimal? SelectedQuotationItemLineTotal { get; set; }
    public string? SupplierName { get; set; }

    // ── Candidate model (empty candidates + non-null winner ⇒ legacy item) ──
    public List<ApprovalBatchItemCandidateDto> Candidates { get; set; } = new();
    public Guid? SelectedCandidateId { get; set; }
    public Guid? WinnerSelectedByUserId { get; set; }
    public DateTime? WinnerSelectedAtUtc { get; set; }
    public string? WinnerSelectionJustification { get; set; }

    /// <summary>True for historical items decided by the Buyer under the pre-candidate model
    /// (zero candidate rows, winner already populated). No snapshot exists for them.</summary>
    public bool IsLegacyBuyerSelectedWinner { get; set; }
}

/// <summary>Frozen snapshot of one candidate option, as submitted by the Buyer.</summary>
public class ApprovalBatchItemCandidateDto
{
    public Guid Id { get; set; }
    public Guid QuotationItemId { get; set; }
    public Guid QuotationId { get; set; }

    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierNif { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? UnitText { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal IvaRatePercent { get; set; }
    public decimal IvaAmount { get; set; }
    public decimal GrossSubtotal { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? QuotationDocumentNumber { get; set; }
    public DateTime? QuotationDocumentDate { get; set; }

    public bool HasReconciliationWarnings { get; set; }
    public string? ReconciliationStatus { get; set; }
    public string? ReconciliationJustification { get; set; }
    public string? LineAdjustmentJustification { get; set; }

    public string? BuyerNote { get; set; }

    /// <summary>True when this candidate is the Area-selected winner of its item.</summary>
    public bool IsWinner { get; set; }

    /// <summary>"MENOR VALOR" badge: lowest LineTotal among this item's candidates in the same
    /// currency. Informational only — never auto-selects anything.</summary>
    public bool IsLowestTotal { get; set; }
}

/// <summary>
/// Summary response DTO for listing batches (used for GET list).
/// </summary>
public class ApprovalBatchListItemDto
{
    public Guid Id { get; set; }
    public int BatchNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public int ItemCount { get; set; }
    public decimal? ApprovedTotalAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
}

/// <summary>
/// DTO for batch-level area approval actions (approve / reject / request-adjustment).
/// Mirrors ApprovalActionDto but omits ItemAwards, SelectedQuotationId, and FinancialIntegrityOverride.
/// Candidate model: winners come from <see cref="Selections"/> (approve only); legacy batches
/// keep reading ApprovalBatchItem.SelectedQuotationItemId.
/// </summary>
public class BatchApprovalActionDto
{
    public string? Comment { get; set; }

    /// <summary>Maps RequestLineItemId → {PlantId, CostCenterId} (single-allocation shorthand).</summary>
    public Dictionary<Guid, ItemApprovalAssignmentDto>? ItemAssignments { get; set; }

    /// <summary>Maps RequestLineItemId → List of allocation lines (multi-allocation).</summary>
    public Dictionary<Guid, List<ItemAllocationLineDto>>? ItemAllocations { get; set; }

    /// <summary>Budget justification (required when budget status is critical/over-budget).</summary>
    public string? BudgetJustification { get; set; }

    /// <summary>Alternative budget line reassignments.</summary>
    public List<AllocationReassignmentDto> Reassignments { get; set; } = new();

    /// <summary>Extra item decisions (APPROVE / REJECT). Keys are QuotationItemIds.</summary>
    public Dictionary<Guid, ExtraItemDecisionDto>? ExtraItemDecisions { get; set; }

    /// <summary>
    /// Area winner selections (candidate model, approve action only): exactly one entry per
    /// ApprovalBatchItem that carries candidates. Required for candidate-based batches; omitted
    /// for legacy batches whose winners were buyer-selected. Carries NO commercial values —
    /// only the chosen candidate identity and an optional/mandatory justification.
    /// </summary>
    public List<BatchWinnerSelectionDto>? Selections { get; set; }
}

/// <summary>The Area Approver's winner choice for one batch item.</summary>
public class BatchWinnerSelectionDto
{
    public Guid ApprovalBatchItemId { get; set; }
    public Guid SelectedCandidateId { get; set; }

    /// <summary>Mandatory (min. 20 meaningful characters) when the chosen candidate is more
    /// expensive than the cheapest same-currency candidate beyond the FinancialIntegrity
    /// tolerance; optional otherwise (persisted whenever supplied).</summary>
    public string? WinnerSelectionJustification { get; set; }
}

/// <summary>
/// DTO for editing a batch during rework (AREA_ADJUSTMENT or FINAL_ADJUSTMENT).
/// Allows changing item membership, candidate membership, and BuyerNotes — never winners.
/// </summary>
public class UpdateApprovalBatchDto
{
    /// <summary>Updated list of items with their candidate options. Newly added candidates are
    /// snapshotted at update time; retained candidates keep their frozen snapshot (only the
    /// BuyerNote may change in place).</summary>
    public List<BatchItemDto> Items { get; set; } = new();

    /// <summary>Optional updated comment.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Buyer's batch-composition decision (INCLUDE/EXCLUDE) for every genuine EXTRA_ITEM
    /// quotation line belonging to a quotation contributing a winner to this batch, as it stands
    /// after this edit. Allows changing a prior decision, subject to the safe-reversal rules for
    /// INCLUDE→EXCLUDE (see IBatchExtraItemDecisionService).
    /// </summary>
    public Dictionary<Guid, ExtraItemDecisionDto>? ExtraItemDecisions { get; set; }
}

/// <summary>One informational (non-batch, non-total-affecting) quotation line shown to the Area
/// Approver for transparency — buyer-excluded extra, IGNORED line, or unresolved legacy extra.</summary>
public class BatchInformationalItemDto
{
    public Guid QuotationItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? SupplierName { get; set; }
    public string? QuotationDocumentNumber { get; set; }

    /// <summary>Why the line was classified this way at reconciliation time (SUBSTITUTE/EXTRA_ITEM/IGNORED).</summary>
    public string? ReconciliationJustification { get; set; }

    /// <summary>Buyer's batch-composition comment — only populated for buyer-excluded extras.</summary>
    public string? Comment { get; set; }
}

/// <summary>Three separately-labeled, read-only informational lists for one ApprovalBatch (§F/§H).
/// None of these affect the batch total or ApprovalBatchItem membership.</summary>
public class BatchInformationalLinesDto
{
    /// <summary>Genuine EXTRA_ITEM lines the buyer explicitly decided not to include in this batch.</summary>
    public List<BatchInformationalItemDto> ExcludedExtraItems { get; set; } = new();

    /// <summary>IGNORED-status lines from the contributing quotation(s) — a complete, valid,
    /// already-justified terminal state. Never requires a decision, never blocks anything.</summary>
    public List<BatchInformationalItemDto> IgnoredLines { get; set; } = new();

    /// <summary>Genuine EXTRA_ITEM lines with NO recorded ApprovalBatchExtraItemDecision at all —
    /// only possible for batches created before this rule existed. Blocks Area Approval progression.</summary>
    public List<BatchInformationalItemDto> UnresolvedLegacyLines { get; set; } = new();
}

/// <summary>
/// DTO for cancelling/voiding an ApprovalBatch.
/// </summary>
public class CancelApprovalBatchRequestDto
{
    /// <summary>Mandatory justification explaining why the batch is being cancelled.</summary>
    public string Justification { get; set; } = string.Empty;
}
