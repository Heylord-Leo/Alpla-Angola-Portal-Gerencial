using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Dashboard;

/// <summary>
/// Dashboard V2 — Receiving section (Phase B slice B4). Two planes:
///  - Shared     (COMPARTILHADO) : the shared Receiving queue for Receiving-role users.
///  - Managerial (GERENCIAL)     : aggregate visibility for Local Manager / SysAdmin (no actions).
/// Counts only — no aging KPIs (deferred until a defensible stage-entry source exists) and no monetary
/// totals (B7). Primary unit = RequestPoGroup; ActionableRequests is the distinct-request secondary.
/// </summary>
public class DashboardV2ReceivingSectionDto
{
    public ReceivingSharedQueueSummaryDto? Shared { get; set; }
    public ReceivingSharedQueueSummaryDto? Managerial { get; set; }
}

/// <summary>Receiving shared-queue operational counts (group primary; distinct-request secondary).</summary>
public class ReceivingSharedQueueSummaryDto
{
    public int ActionableGroups { get; set; }
    public int ActionableRequests { get; set; }
    /// <summary>PAYMENT_COMPLETED — paid, ready to move to receipt ("Entrada em recebimento").</summary>
    public int ReadyForReceiptGroups { get; set; }
    public int WaitingReceiptGroups { get; set; }
    public int FollowUpGroups { get; set; }
    public int WaitingSupplierDeliveryGroups { get; set; }
}

// ── Group-level Receiving queue (drill-down foundation so B4.2 reconciles exactly with the dashboard) ──

public class ReceivingQueueRowDto
{
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Guid RequestPoGroupId { get; set; }
    public string GroupStatus { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    /// <summary>READY_FOR_RECEIPT / WAITING_RECEIPT / IN_FOLLOWUP / WAITING_SUPPLIER_DELIVERY.</summary>
    public string ActionableBucket { get; set; } = string.Empty;
    /// <summary>Canonical Receiving action codes available now (MOVE_TO_RECEIPT / CONFIRM_RECEIVING).</summary>
    public List<string> AvailableActions { get; set; } = new();
}

public class ReceivingQueueResponseDto
{
    public List<ReceivingQueueRowDto> Rows { get; set; } = new();
    public ReceivingSharedQueueSummaryDto Summary { get; set; } = new();
}
