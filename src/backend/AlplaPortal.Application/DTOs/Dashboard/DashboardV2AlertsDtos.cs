using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Dashboard;

// ── B8: Dashboard V2 canonical Alerts (GERENCIAL/COMPARTILHADO/PESSOAL, read-only). ──
// An alert is a RISK/DEADLINE condition over a canonical entity that has an OPEN action — higher-signal
// than the operational queues, never a mirror of them. B8 covers only domains with a reliable
// domain-appropriate date: Buyer (NeedByDateUtc, gated to an open Buyer action) and Finance
// (RequestPayment.ScheduledDateUtc, still scheduled). Approval/PO/Receiving/Documentation aging is
// deferred to B9 (no persisted stage-entry timestamp). No money/FX. Request.CreatedAtUtc is never used
// as a stage age. Summary is null when the caller is not entitled (frontend hides the section).

public static class AlertDomains
{
    public const string Buyer = "BUYER";
    public const string Finance = "FINANCE";
}

public static class AlertEntityTypes
{
    public const string Request = "REQUEST";
    public const string PoGroup = "PO_GROUP";
}

public static class AlertTypes
{
    public const string BuyerOverdue = "BUYER_OVERDUE";
    public const string BuyerDueToday = "BUYER_DUE_TODAY";
    public const string BuyerDueSoon = "BUYER_DUE_SOON";
    public const string FinanceScheduledOverdue = "FINANCE_SCHEDULED_OVERDUE";
    public const string FinanceScheduledDueSoon = "FINANCE_SCHEDULED_DUE_SOON";
}

public static class AlertSeverities
{
    public const string Attention = "ATTENTION";
    public const string Critical = "CRITICAL";
}

public static class AlertPlanes
{
    public const string Pessoal = "PESSOAL";
    public const string Compartilhado = "COMPARTILHADO";
    public const string Gerencial = "GERENCIAL";
}

public sealed class DashboardV2AlertDto
{
    /// <summary>Stable identity = Domain:EntityType:EntityId:AlertType.</summary>
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;      // BUYER | FINANCE
    public string EntityType { get; set; } = string.Empty;  // REQUEST | PO_GROUP
    public string EntityId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;    // ATTENTION | CRITICAL
    public string Plane { get; set; } = string.Empty;       // PESSOAL | COMPARTILHADO | GERENCIAL
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DateUtc { get; set; }                   // the relevant NeedBy/Scheduled date
    public int DaysDelta { get; set; }                      // signed days vs today (negative = overdue)
    public string? TargetPath { get; set; }
    public bool CanNavigate { get; set; }
}

public sealed class AlertDomainCountDto
{
    public string Domain { get; set; } = string.Empty;
    public int Attention { get; set; }
    public int Critical { get; set; }
}

public sealed class DashboardV2AlertsSummaryDto
{
    public int AttentionCount { get; set; }
    public int CriticalCount { get; set; }
    public List<AlertDomainCountDto> ByDomain { get; set; } = new();
    /// <summary>Count of the full deduplicated alert population (BEFORE the display cap). AttentionCount +
    /// CriticalCount == TotalAlertCount.</summary>
    public int TotalAlertCount { get; set; }
    /// <summary>Count of alerts actually returned in <c>Alerts</c> (AFTER the display cap).</summary>
    public int DisplayedAlertCount { get; set; }
    /// <summary>True when the visible list was capped (TotalAlertCount &gt; DisplayedAlertCount).</summary>
    public bool IsTruncated { get; set; }
}

public sealed class DashboardV2AlertsDto
{
    /// <summary>Null when the caller is not entitled (Buyer / Finance / Local Manager / SysAdmin).</summary>
    public DashboardV2AlertsSummaryDto? Summary { get; set; }
    public List<DashboardV2AlertDto> Alerts { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}
