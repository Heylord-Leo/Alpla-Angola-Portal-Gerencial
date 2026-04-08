namespace AlplaPortal.Application.DTOs.Finance;

using System;
using System.Collections.Generic;
using AlplaPortal.Application.DTOs.Common;

public class FinanceSummaryDto
{
    public int WaitingFinanceAction { get; set; }
    public int ScheduledPayments { get; set; }
    public int OverduePayments { get; set; }
    public int CompletedThisMonth { get; set; }
    
    public decimal PendingValue { get; set; }
    public decimal ScheduledValue { get; set; }
    public decimal OverdueValue { get; set; }
    public decimal PaidThisMonthValue { get; set; }
    public List<string> CurrencyCodes { get; set; } = new();

    public List<FinanceAttentionPointDto> AttentionPoints { get; set; } = new();

    public List<FinanceCashFlowProjectionDto> CashFlowProjections { get; set; } = new();
    public List<FinanceCurrencyExposureDto> CurrencyExposures { get; set; } = new();
    public List<FinanceTopSupplierDto> TopSuppliers { get; set; } = new();
    public FinanceAgingAnalysisDto AgingAnalysis { get; set; } = new();
}

public class FinanceCashFlowProjectionDto
{
    public string Date { get; set; } = string.Empty; // ISO Format yyyy-MM-dd
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public class FinanceCurrencyExposureDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public class FinanceTopSupplierDto
{
    public string SupplierName { get; set; } = string.Empty;
    public decimal TotalPendingAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int RequestCount { get; set; }
}

public class FinanceAgingAnalysisDto
{
    public int ZeroToTwoDays { get; set; }
    public int ThreeToFiveDays { get; set; }
    public int MoreThanFiveDays { get; set; }
}

public class FinanceAttentionPointDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public string TargetPath { get; set; } = string.Empty;
    public string Type { get; set; } = "INFO"; // e.g., DANGER, WARNING, INFO
}

public class FinanceListItemDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public string PlantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime? ScheduledDateUtc { get; set; }
    public DateTime? PaidDateUtc { get; set; }

    public string StatusCode { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StatusBadgeColor { get; set; } = string.Empty;

    public bool IsOverdue { get; set; }
    public bool IsDueSoon { get; set; }
    public bool IsMissingDocuments { get; set; }
    
    public List<string> MissingDocumentTypes { get; set; } = new();
    public List<string> AvailableFinanceActions { get; set; } = new();
}

public class FinanceListResponseDto
{
    public PagedResult<FinanceListItemDto> PagedResult { get; set; } = new();
    public FinanceSummaryDto Summary { get; set; } = new();
}

public class FinanceHistoryItemDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }

    public string ActionTaken { get; set; } = string.Empty; // e.g., "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "NOTE_ADDED"
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string? NewStatusCode { get; set; }
    public string? NewStatusName { get; set; }
}

public class FinanceActionRequestDto
{
    public DateTime? ActionDateUtc { get; set; }
    public string? Notes { get; set; }
}
