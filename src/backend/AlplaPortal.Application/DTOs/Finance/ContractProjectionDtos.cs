namespace AlplaPortal.Application.DTOs.Finance;

using System;
using System.Collections.Generic;

// ─── Shared value type ─────────────────────────────────────────────────────

/// <summary>An amount grouped by currency code — never mixed or normalised.</summary>
public class ContractProjectionCurrencyTotalDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

// ─── Summary KPIs ──────────────────────────────────────────────────────────

/// <summary>
/// High-level KPI summary for contract-driven cash flow projections.
/// Returned by GET /api/v1/finance/contract-projections/summary
/// </summary>
public class ContractProjectionSummaryDto
{
    // KPI 1: obligations due this calendar month, not yet on a request
    public List<ContractProjectionCurrencyTotalDto> CurrentMonthByCurrency { get; set; } = new();

    // KPI 2: obligations due in the next 90 days (PROJECTED + OVERDUE)
    public List<ContractProjectionCurrencyTotalDto> NextThreeMonthsByCurrency { get; set; } = new();

    // KPI 3: obligations that already have a linked request (PIPELINE bucket)
    public List<ContractProjectionCurrencyTotalDto> PipelineByCurrency { get; set; } = new();

    // KPI 4: confirmed (approved + scheduled)
    public List<ContractProjectionCurrencyTotalDto> ConfirmedByCurrency { get; set; } = new();

    // KPI 5: realised this year
    public List<ContractProjectionCurrencyTotalDto> RealizedByCurrency { get; set; } = new();

    // KPI 6: overdue obligations with no linked request and penalty already accruing
    public int OverdueNoRequestCount { get; set; }
    public int PenaltyRiskCount { get; set; }

    // Monthly series for the bar chart (next 6 months, grouped by currency)
    public List<ContractProjectionMonthlySeriesDto> MonthlySeries { get; set; } = new();
}

/// <summary>One bar in the contractual projection bar chart.</summary>
public class ContractProjectionMonthlySeriesDto
{
    /// <summary>ISO year-month: "2025-05"</summary>
    public string YearMonth { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ProjectedAmount { get; set; }
    public decimal PipelineAmount { get; set; }
    public decimal ConfirmedAmount { get; set; }
}

// ─── Detail list item ──────────────────────────────────────────────────────

/// <summary>
/// One row in the contractual projection detail table.
/// Returned by GET /api/v1/finance/contract-projections
/// </summary>
public class ContractProjectionItemDto
{
    public string ObligationId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public string ContractTitle { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public int? DepartmentId { get; set; }
    public string? ObligationLabel { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTime? DueDateUtc { get; set; }
    public DateTime? GraceDateUtc { get; set; }
    public DateTime? PenaltyStartDateUtc { get; set; }

    /// <summary>Derived: PROJECTED | PIPELINE | CONFIRMED | REALIZED | OVERDUE_NO_REQUEST</summary>
    public string ForecastBucket { get; set; } = string.Empty;

    /// <summary>Derived: LOW | MEDIUM | HIGH</summary>
    public string RiskLevelCode { get; set; } = string.Empty;

    /// <summary>Linked request number, if any.</summary>
    public string? LinkedRequestNumber { get; set; }
    public string? LinkedRequestStatus { get; set; }
}

/// <summary>Paged result wrapper for ContractProjectionItemDto.</summary>
public class ContractProjectionPagedResultDto
{
    public List<ContractProjectionItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
