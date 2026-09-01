using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Canonical, server-derived read model for the Buyer Request Workspace (Phase 3A).
/// GET /api/v1/buyer/requests/{id}/workspace. The frontend renders these values and must NOT
/// re-derive the Buyer workflow — operational state, per-item coverage buckets, batch classification
/// and supplier metrics all come from the server (coverage taxonomy is shared with the Buyer queue
/// via BuyerQueueProjectionBuilder). Read-only foundation: no mutations.
/// </summary>
public class BuyerWorkspaceDto
{
    // ── Header / identity ──
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string RequestStatusCode { get; set; } = string.Empty;

    public Guid? RequesterId { get; set; }
    public string? RequesterName { get; set; }
    public Guid? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public string? CreatedByName { get; set; }

    public string? CompanyName { get; set; }
    public string? CompanyTaxId { get; set; }
    public string? PlantName { get; set; }
    public string? DepartmentName { get; set; }

    public string? NeedLevelCode { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // ── Server-derived Buyer workflow (same source as the queue) ──
    public string OperationalState { get; set; } = string.Empty;
    public string OperationalStateLabel { get; set; } = string.Empty;
    public List<BuyerNextActionDto> NextActions { get; set; } = new();
    public string PriorityBand { get; set; } = string.Empty;
    public string DeadlineCondition { get; set; } = string.Empty;
    public bool RequiresAttention { get; set; }

    // ── Coverage (Tab 1 summary; canonical taxonomy) ──
    public BuyerWorkspaceCoverageDto Coverage { get; set; } = new();

    // ── Collections ──
    public List<BuyerWorkspaceItemDto> Items { get; set; } = new();
    public List<BuyerWorkspaceQuotationDto> Quotations { get; set; } = new();
    public List<BuyerWorkspaceBatchDto> Batches { get; set; } = new();
    public List<BuyerWorkspaceSupplierDto> Suppliers { get; set; } = new();
}

public class BuyerWorkspaceCoverageDto
{
    public int TotalItems { get; set; }        // active items (excludes cancelled/deleted)
    public int Treated { get; set; }           // approved + in-batch + ready + closed + not-quoted-accepted
    public int Pending { get; set; }
    public string CoverageStatus { get; set; } = string.Empty;
    // Per-bucket counts (canonical CoverageBuckets keys).
    public int Approved { get; set; }
    public int InActiveBatch { get; set; }
    public int ReadyForBatch { get; set; }
    public int ClosedNotQuoted { get; set; }
    public int NotQuotedProposed { get; set; }   // legacy — surfaced only when present
    public int NotQuotedAccepted { get; set; }   // legacy — surfaced only when present
    public int CancelledDeleted { get; set; }
}

public class BuyerWorkspaceItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string? ItemCatalogCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? UnitName { get; set; }
    public string CoverageBucket { get; set; } = string.Empty; // canonical bucket
    public string? SupplierName { get; set; }
    public string? SelectedQuotationSummary { get; set; } // e.g. "Fornecedor X · 1.234,00 AOA" when reliable
    /// <summary>
    /// Server-computed eligibility for the "Desconsiderar item" / close-not-quoted action — mirrors the
    /// exact rule enforced by POST /line-items/{id}/close-not-quoted (lifecycle null|QUOTATION_PENDING AND
    /// not in an active/approved batch). The Workspace renders the action only when this is true; the
    /// endpoint remains authoritative.
    /// </summary>
    public bool CanCloseNotQuoted { get; set; }
}

public class BuyerWorkspaceQuotationDto
{
    public Guid Id { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }
    public int ItemsQuotedCount { get; set; }
    public string? Currency { get; set; }
    public decimal TotalAmount { get; set; }
    public int DocumentCount { get; set; }   // proforma attachment presence (0/1)
    public bool IsSelected { get; set; }
}

public class BuyerWorkspaceBatchDto
{
    public Guid Id { get; set; }
    public int BatchNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // ACTIVE | APPROVED | REJECTED | CANCELLED | SUPERSEDED
    public int ItemCount { get; set; }
    public List<int> ItemLineNumbers { get; set; } = new();
    public decimal? ApprovedTotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? AreaDecisionAtUtc { get; set; } // earliest winner-selection stamp, when present

    /// <summary>Adjustment V2 (Phase 3) — the batch's OPEN structured adjustment cycle, when one
    /// exists. Read-only display detail; null for batches with no open V2 cycle (legacy or never
    /// adjusted). Does NOT expose resolutions/field-changes/candidate-reviews (later phases).</summary>
    public BuyerWorkspaceBatchAdjustmentDto? Adjustment { get; set; }
}

/// <summary>Adjustment V2 (Phase 3) — read-only projection of one OPEN adjustment cycle for the
/// batch details surface. Codes stay raw here; the frontend renders friendly labels.</summary>
public class BuyerWorkspaceBatchAdjustmentDto
{
    public int CycleNumber { get; set; }
    public string SourceStage { get; set; } = string.Empty; // AREA | FINAL
    public string Status { get; set; } = string.Empty;      // WAITING_BUYER | WAITING_REQUESTER | ...
    public bool WholeBatch { get; set; }
    public string ApproverComment { get; set; } = string.Empty;
    public string? RequestedByName { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public List<BuyerWorkspaceBatchAdjustmentReasonDto> Reasons { get; set; } = new();

    // Phase 4 — the Buyer's "Resposta ao reajuste" once the cycle has been resolved/resubmitted
    // (null while the cycle is still open). Read-only display; no later-phase surfaces exposed.
    public string? ResponseNote { get; set; }
    public string? RespondedByName { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
}

public class BuyerWorkspaceBatchAdjustmentReasonDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public Guid? RequestLineItemId { get; set; }
    /// <summary>Resolved line number of the affected item, when item-scoped (for display).</summary>
    public int? LineNumber { get; set; }
    public string? Detail { get; set; }
}

public class BuyerWorkspaceSupplierDto
{
    public int? SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Nif { get; set; }
    public bool IsActive { get; set; }
    public string? RegistrationStatus { get; set; }
    public int PurchaseCount { get; set; }                    // distinct issued POs (global track record)
    public List<CurrencyAmountDto> TotalsByCurrency { get; set; } = new(); // never summed across currencies
    public DateTime? LastPurchaseUtc { get; set; }
    public int QuotationsReceived { get; set; }
    public int QuotationsSelected { get; set; }
    public bool InvolvedSelected { get; set; }                // has a selected quotation on THIS request
    public bool CanOpenSheet { get; set; }                    // Phase 3A: false (Supplier Sheet reuse is INVASIVE — deferred)
}

public class CurrencyAmountDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
