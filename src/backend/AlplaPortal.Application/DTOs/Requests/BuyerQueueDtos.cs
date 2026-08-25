using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Lightweight, server-derived Buyer-queue row. The frontend renders these codes/labels/capabilities
/// directly and must NOT re-derive the Buyer workflow (Phase 1 canonical model). One row == one
/// Request (never a line-item). See docs/BUYER_QUEUE_CANONICAL_MODEL.md.
/// </summary>
public class BuyerQueueItemDto
{
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string? Title { get; set; }

    // People
    public Guid? RequesterId { get; set; }
    public string? RequesterName { get; set; }

    // Org / display context
    public string? CompanyName { get; set; }
    public string? PlantName { get; set; }
    public string? DepartmentName { get; set; }
    public string RequestStatusCode { get; set; } = string.Empty;

    // Priority / deadline
    public string? NeedLevelCode { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string PriorityBand { get; set; } = string.Empty;
    public string DeadlineCondition { get; set; } = string.Empty;

    // Ownership
    public Guid? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public string OwnershipState { get; set; } = string.Empty;

    // Operational projection
    public string OperationalState { get; set; } = string.Empty;
    public string OperationalStateLabel { get; set; } = string.Empty;
    public List<BuyerNextActionDto> NextActions { get; set; } = new();
    public string CoverageStatus { get; set; } = string.Empty;
    public int ActiveItemCount { get; set; }
    public int CoveredCount { get; set; }
    public int PendingCount { get; set; }
    public int QuotationCount { get; set; }
    public int ActiveBatchCount { get; set; }
    public Dictionary<string, int> CoverageCounts { get; set; } = new();
    public List<BuyerAttentionSignalDto> AttentionSignals { get; set; } = new();
    public bool RequiresAttention { get; set; }

    // Notes (request-level annotations; loaded only for the returned page slice)
    public bool HasNotes { get; set; }
    public int NoteCount { get; set; }
    public string? LatestNoteText { get; set; }
    public DateTime? LatestNoteAtUtc { get; set; }
    public string? LatestNoteActorName { get; set; }

    // Capabilities (what THIS actor may do)
    public bool CanOpen { get; set; }
    public bool CanClaim { get; set; }
    public bool CanReassign { get; set; }
    public bool CanCancel { get; set; }
    public string? CancelBlockReason { get; set; }
}

public class BuyerNextActionDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Actionable { get; set; }
}

public class BuyerAttentionSignalDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

/// <summary>Request-level paginated queue page. TotalCount counts REQUESTS, never line-items.</summary>
public class BuyerQueuePageDto
{
    public List<BuyerQueueItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>The five canonical summary cards (Request counts), plus a per-state breakdown.</summary>
public class BuyerQueueSummaryDto
{
    public int Total { get; set; }
    public int RequiresAttention { get; set; }
    public int NeedsAction { get; set; }
    public int AwaitingApproval { get; set; }
    public int Unassigned { get; set; }
    public Dictionary<string, int> ByOperationalState { get; set; } = new();
}
