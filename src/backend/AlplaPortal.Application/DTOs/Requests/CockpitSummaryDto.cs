namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Aggregated DTO for the Dashboard Operational Cockpit.
/// Returns all data needed by the redesigned dashboard in a single API call.
/// Designed to support optional filter params (company, plant, department, period) in future versions.
/// </summary>
public class CockpitSummaryDto
{
    // ── "Minha Fila de Trabalho" — role-based counters ──

    /// <summary>Total requests where the current user is the next actor based on role + status.</summary>
    public int MyPendingActions { get; set; }

    /// <summary>Subset of MyPendingActions where NeedByDateUtc is today or tomorrow.</summary>
    public int MyUrgentItems { get; set; }

    /// <summary>Requests in AREA_ADJUSTMENT or FINAL_ADJUSTMENT where the user is responsible.</summary>
    public int MyAdjustmentItems { get; set; }

    /// <summary>Requests in user scope where NeedByDateUtc < today and not terminal.</summary>
    public int MyOverdueItems { get; set; }

    /// <summary>Requests in user scope where NeedByDateUtc is within the next 3 days (not overdue).</summary>
    public int MyNearDeadlineItems { get; set; }

    // ── Global pipeline counters (scoped by GetScopedRequestsQuery) ──

    public int TotalActiveRequests { get; set; }
    public int Draft { get; set; }
    public int WaitingQuotation { get; set; }
    public int WaitingAreaApproval { get; set; }
    public int WaitingFinalApproval { get; set; }
    public int InAdjustment { get; set; }
    public int AwaitingPo { get; set; }
    public int AwaitingPayment { get; set; }
    public int PaymentCompleted { get; set; }
    public int WaitingReceipt { get; set; }
    public int Completed { get; set; }

    // ── Bottleneck data ──
    public List<StageBottleneckDto> Bottlenecks { get; set; } = new();

    // ── Financial summary ──
    public List<FinancialByStatusDto> FinancialByStatus { get; set; } = new();

    // ── Attention alerts ──
    public List<AttentionAlertDto> Alerts { get; set; } = new();
}

/// <summary>
/// Bottleneck entry: how many requests are stuck in each workflow stage
/// and when the oldest one entered that stage.
/// </summary>
public class StageBottleneckDto
{
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime? OldestCreatedAtUtc { get; set; }
}

/// <summary>
/// Financial aggregation for a logical group of statuses.
/// </summary>
public class FinancialByStatusDto
{
    /// <summary>Display label, e.g. "Solicitado", "Aprovado", "Pago".</summary>
    public string GroupLabel { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<string> CurrencyCodes { get; set; } = new();
    public int Count { get; set; }
}

/// <summary>
/// Structured alert for the "Atenção Requerida" dashboard section.
/// </summary>
public class AttentionAlertDto
{
    public string Id { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ResponsibleArea { get; set; } = string.Empty;

    /// <summary>OVERDUE, NEAR_DEADLINE, STUCK, ADJUSTMENT</summary>
    public string AlertType { get; set; } = string.Empty;

    /// <summary>CRITICAL, WARNING, INFO</summary>
    public string Severity { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public string TargetPath { get; set; } = string.Empty;
}
