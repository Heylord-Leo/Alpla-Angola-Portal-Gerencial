using System;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// One actionable row in the Approval Center queue. The queue unit is the actionable
/// <c>ApprovalBatch</c> (per <see cref="ApprovalStage"/>), NOT the Request — a single request with
/// two simultaneous WAITING_AREA_APPROVAL batches produces two independent rows that share a
/// <see cref="RequestNumber"/> but have distinct <see cref="ApprovalBatchId"/>.
///
/// <para>Identity is <see cref="ApprovalBatchId"/> + <see cref="ApprovalStage"/>, surfaced as the
/// stable <see cref="QueueKey"/>. PAYMENT (and legacy whole-request) approvals have no batch — they
/// keep a request-level action identity (<see cref="ApprovalBatchId"/> = null, QueueKey =
/// requestId+stage). No PAYMENT is ever forced into a fake batch.</para>
/// </summary>
public class ApprovalQueueItemDto
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public Guid RequestId { get; set; }
    public string? RequestNumber { get; set; }

    /// <summary>Actionable batch id. Null for PAYMENT / legacy whole-request actions.</summary>
    public Guid? ApprovalBatchId { get; set; }

    /// <summary>Batch (lot) number, when this row is a batch. Null for request-level rows.</summary>
    public int? LotNumber { get; set; }

    /// <summary>"AREA" or "FINAL" — the stage this row is actionable under.</summary>
    public string ApprovalStage { get; set; } = string.Empty;

    /// <summary>
    /// Stable, unique row key: <c>{approvalBatchId}:{stage}</c> for batch rows,
    /// <c>{requestId}:{stage}</c> for request-level rows. Frontend selection/dedup key.
    /// </summary>
    public string QueueKey { get; set; } = string.Empty;

    // ── Actionable status (the batch's own status for batch rows) ──────────────
    public string BatchStatus { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StatusBadgeColor { get; set; } = string.Empty;

    // ── Request context (shared across a request's rows) ───────────────────────
    /// <summary>The parent request's own aggregate status — may differ from the batch status
    /// (e.g. request WAITING_QUOTATION while a batch is WAITING_AREA_APPROVAL).</summary>
    public string RequestStatusCode { get; set; } = string.Empty;
    public string RequestStatusName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int RequestTypeId { get; set; }
    public string RequestTypeCode { get; set; } = string.Empty;
    public string RequestTypeName { get; set; } = string.Empty;

    public string RequesterName { get; set; } = string.Empty;

    public int DepartmentId { get; set; }
    public string? DepartmentName { get; set; }

    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public int? PlantId { get; set; }
    public string? PlantName { get; set; }

    /// <summary>Supplier for THIS row: the batch's winning supplier for batch rows, the request's
    /// selected/legacy supplier for request-level rows. Never a sibling batch's supplier.</summary>
    public string? SupplierDisplay { get; set; }

    public string? CostCenterCode { get; set; }
    public string? CostCenterName { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>Number of items in THIS actionable unit (batch item count, or request line count).</summary>
    public int ItemCount { get; set; }

    // ── Money (authoritative — ApprovalQueueAmountResolver, per THIS row) ───────
    public decimal? ActionableAmount { get; set; }
    public string? ActionableAmountSource { get; set; }
    public bool HasAmountInconsistency { get; set; }

    // ── Dates / urgency ────────────────────────────────────────────────────────
    public int? NeedLevelId { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Guid? SelectedQuotationId { get; set; }
}
