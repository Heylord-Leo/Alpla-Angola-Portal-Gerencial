using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Dashboard;

/// <summary>
/// Dashboard V2 — Buyer section (Phase B slice B1+B2). Three planes:
///  - Personal  (PESSOAL)       : only work assigned to the current user (BuyerId == me).
///  - Shared    (COMPARTILHADO) : the unassigned Compras pool (BuyerId == null).
///  - Workload  (GERENCIAL)     : per-buyer distribution, managerial visibility only.
/// Any plane the current user is not entitled to is null (the frontend simply omits it).
/// All numbers are server-calculated from the canonical Buyer projection; the frontend renders only.
/// </summary>
public class DashboardV2BuyerSectionDto
{
    public BuyerPersonalSummaryDto? Personal { get; set; }
    public BuyerSharedQueueSummaryDto? Shared { get; set; }
    public BuyerWorkloadSummaryDto? Workload { get; set; }
}

/// <summary>PESSOAL — the current user's own assigned buyer workload (never includes unassigned).</summary>
public class BuyerPersonalSummaryDto
{
    public int AssignedRequests { get; set; }
    public int ActionableRequests { get; set; }
    public int PendingQuotationItems { get; set; }
    public int ReadyForBatchItems { get; set; }
    public int AdjustmentRequests { get; set; }
    public int OverdueActionableRequests { get; set; }
    public int CriticalActionableRequests { get; set; }
}

/// <summary>COMPARTILHADO — the shared, unassigned Compras queue (BuyerId == null). Never personal.</summary>
public class BuyerSharedQueueSummaryDto
{
    public int UnassignedRequests { get; set; }
    public int UnassignedActionableRequests { get; set; }
    public int UnassignedPendingItems { get; set; }
    public int UnassignedReadyItems { get; set; }
    public int UnassignedNeedsQuotationRequests { get; set; }
    public int UnassignedPartialCoverageRequests { get; set; }
    public int UnassignedReadyForApprovalRequests { get; set; }
    public int UnassignedAdjustmentRequests { get; set; }
    public int UnassignedOverdueActionableRequests { get; set; }
    public int UnassignedCriticalActionableRequests { get; set; }
}

/// <summary>GERENCIAL — per-buyer workload distribution plus the explicit unassigned bucket.</summary>
public class BuyerWorkloadSummaryDto
{
    /// <summary>Assigned buyers (sorted by the endpoint), each a workload row.</summary>
    public List<BuyerWorkloadRowDto> Rows { get; set; } = new();

    /// <summary>The shared unassigned bucket as its own row (IsUnassigned = true). Never merged into Rows'
    /// per-buyer totals; surfaced separately so the UI can pin it.</summary>
    public BuyerWorkloadRowDto? Unassigned { get; set; }
}

/// <summary>One workload row — a buyer, or (IsUnassigned) the shared pool. Never a performance score.</summary>
public class BuyerWorkloadRowDto
{
    public System.Guid? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public bool IsUnassigned { get; set; }

    public int AssignedRequests { get; set; }
    public int ActionableRequests { get; set; }
    public int PendingQuotationItems { get; set; }
    public int ReadyForBatchItems { get; set; }
    public int NeedsQuotationRequests { get; set; }
    public int PartialCoverageRequests { get; set; }
    public int ReadyForApprovalRequests { get; set; }
    public int AdjustmentRequests { get; set; }
    public int OverdueActionableRequests { get; set; }
    public int CriticalActionableRequests { get; set; }
}
