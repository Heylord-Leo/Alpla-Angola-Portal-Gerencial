namespace AlplaPortal.Application.DTOs.Finance;

using System;
using System.Collections.Generic;
using AlplaPortal.Application.DTOs.Common;

/// <summary>One RequestPoGroup projected as an independent Finance obligation (Phase 3).</summary>
public class FinanceObligationDto
{
    // Identity
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;
    public string? RequestTitle { get; set; }
    public string? Department { get; set; }
    public string? Plant { get; set; }
    // Group
    public Guid RequestPoGroupId { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierNif { get; set; }
    public string? SupplierTaxId { get; set; }
    public string GroupStatusCode { get; set; } = string.Empty;
    public string GroupStatusLabel { get; set; } = string.Empty;
    /// <summary>Corporate operational-state label (e.g. "Aguardando Agendamento", "Pagamento Vencido").</summary>
    public string OperationalStateLabel { get; set; } = string.Empty;
    public string? PurchaseOrderNumber { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal GroupAmount { get; set; }
    // Payment
    public int? PaymentId { get; set; }
    public string? PaymentType { get; set; }
    public DateTime? ScheduledDateUtc { get; set; }
    public decimal? PlannedAmount { get; set; }
    public decimal? ActualPaidAmount { get; set; }
    public DateTime? PaidDateUtc { get; set; }
    public bool HasPaymentProof { get; set; }
    // Action
    public List<string> FinanceActions { get; set; } = new();
    public string ActionClass { get; set; } = string.Empty;
    public string ActionClassLabel { get; set; } = string.Empty;
    public string? NextActionLabel { get; set; }
    public string ResponsibleRole { get; set; } = string.Empty;
    // Timing
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public int OverdueDays { get; set; }
    public bool IsDueToday { get; set; }
    // Display
    public decimal ObligationAmount { get; set; }
}

/// <summary>A Request as a visual container of one-or-more obligations (Option C).</summary>
public class FinanceObligationContainerDto
{
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Department { get; set; }
    public string? Plant { get; set; }
    public int SupplierCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Whether this container should render expanded by default (mixed/multi-obligation).</summary>
    public bool ExpandByDefault { get; set; }
    /// <summary>Per-currency totals of the container's obligations (never summed across currencies).</summary>
    public List<FinanceCurrencyAmountDto> TotalsByCurrency { get; set; } = new();
    public List<FinanceObligationDto> Obligations { get; set; } = new();
    // ── Finance notes (request-level "NOTA_FINANCEIRA" history), minimal metadata only ──
    public bool HasNotes { get; set; }
    public int NoteCount { get; set; }
    public string? LatestNoteText { get; set; }
    public DateTime? LatestNoteAtUtc { get; set; }
    public string? LatestNoteActorName { get; set; }
}

public class FinanceCurrencyAmountDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>One work-queue card: a count of obligations plus per-currency amounts.</summary>
public class FinanceObligationCardDto
{
    public string ActionClass { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<FinanceCurrencyAmountDto> AmountsByCurrency { get; set; } = new();
}

public class FinanceObligationSummaryDto
{
    public FinanceObligationCardDto NeedsScheduling { get; set; } = new();
    public FinanceObligationCardDto NeedsPayment { get; set; } = new();
    public FinanceObligationCardDto DueToday { get; set; } = new();
    public FinanceObligationCardDto Overdue { get; set; } = new();
    public FinanceObligationCardDto PaidWaitingReceiving { get; set; } = new();
    /// <summary>Total count of Finance-actionable obligations (needs scheduling + needs payment + fiscal).</summary>
    public int ActionableTotal { get; set; }
    public List<FinanceCurrencyAmountDto> ActionableAmountsByCurrency { get; set; } = new();
}

public class FinanceObligationsResponseDto
{
    public PagedResult<FinanceObligationContainerDto> PagedResult { get; set; } = new();
    public FinanceObligationSummaryDto Summary { get; set; } = new();
}
