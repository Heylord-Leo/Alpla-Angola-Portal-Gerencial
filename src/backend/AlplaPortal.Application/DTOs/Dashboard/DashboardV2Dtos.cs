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

// ── B3: Finance shared queue (operational / count-based; monetary totals belong to B7) ──

/// <summary>
/// Dashboard V2 — Finance section (Phase B slice B3). Two planes:
///  - Shared     (COMPARTILHADO) : the shared Finance queue for Finance-role users.
///  - Managerial (GERENCIAL)     : aggregate visibility for Local Manager / SysAdmin (no actions).
/// A plane the current user is not entitled to is null (the frontend omits it). Counts only — no
/// monetary amounts (currency-safe financial totals are B7 Financial Summary).
/// </summary>
public class DashboardV2FinanceSectionDto
{
    public FinanceSharedQueueSummaryDto? Shared { get; set; }
    public FinanceSharedQueueSummaryDto? Managerial { get; set; }
}

/// <summary>Finance shared-queue operational counts. Primary unit = obligation (RequestPoGroup);
/// ActionableRequests is the distinct-request secondary. No amounts (B7 owns money).</summary>
public class FinanceSharedQueueSummaryDto
{
    public int ActionableGroups { get; set; }
    public int ActionableRequests { get; set; }
    public int NeedsSchedulingGroups { get; set; }
    public int NeedsPaymentGroups { get; set; }
    public int DueTodayGroups { get; set; }
    public int OverdueGroups { get; set; }
    /// <summary>Informational only (PAYMENT_COMPLETED / paid-waiting-receiving) — NOT Finance-actionable.</summary>
    public int PaidWaitingReceivingGroups { get; set; }
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
