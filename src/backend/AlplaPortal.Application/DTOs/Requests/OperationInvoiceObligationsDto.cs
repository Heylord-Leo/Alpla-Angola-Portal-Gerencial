using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Release 4 Phase 1b: the operation-invoice obligations of one request, derived on read.
///
/// <para>Diagnostic and strictly read-only. Amounts for groups whose expected total was never
/// captured are <c>null</c>, never zero — the client must treat <c>ExpectedAmount == null</c> as
/// "unknown finish line", not as "nothing owed".</para>
/// </summary>
public class OperationInvoiceObligationsDto
{
    public Guid RequestId { get; set; }

    public List<OperationInvoiceObligationDto> Obligations { get; set; } = new();

    public OperationInvoiceObligationRollupDto Rollup { get; set; } = new();
}

/// <summary>The operation-invoice obligation of one PO group, fully explained.</summary>
public class OperationInvoiceObligationDto
{
    public Guid GroupId { get; set; }

    // ── Group identity ──
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? Currency { get; set; }
    public string? PaymentConditionCode { get; set; }
    public int? PlantId { get; set; }

    // ── Obligation ──
    /// <summary>Canonical identity (legacy FINAL_INVOICE already mapped to INVOICE); null = unclassified.</summary>
    public string? SourceDocumentType { get; set; }
    public bool RequiresOperationInvoice { get; set; }

    /// <summary>Null when never captured (pre-flag group). Never coerced to zero.</summary>
    public decimal? ExpectedAmount { get; set; }
    public string? ExpectedCurrency { get; set; }

    // ── Coverage ──
    public decimal ValidatedCoveredAmount { get; set; }
    public decimal PendingCoveredAmount { get; set; }

    /// <summary>Null when <see cref="ExpectedAmount"/> is unknown.</summary>
    public decimal? RemainingAmount { get; set; }
    public decimal AppliedTolerance { get; set; }

    // ── State ──
    /// <summary>Recomputed from authoritative inputs on this read.</summary>
    public string DerivedStatus { get; set; } = string.Empty;

    /// <summary>The cached column as it stands in the database.</summary>
    public string PersistedStatus { get; set; } = string.Empty;

    /// <summary>The two disagree. Diagnostic only — this endpoint never repairs.</summary>
    public bool StatusDrift { get; set; }

    public bool ClosedShort { get; set; }

    public string? PurchaseOrderNumber { get; set; }

    // ── Traceability ──
    /// <summary>Derived from the group's line items, deduplicated, in line order.</summary>
    public List<Guid> PaymentSourceDocumentIds { get; set; } = new();
    public int LineItemCount { get; set; }

    // ── Explanation ──
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>Request-level counts plus per-currency sums. Currencies are never summed together.</summary>
public class OperationInvoiceObligationRollupDto
{
    public int TotalGroups { get; set; }
    public int RequiringOperationInvoiceCount { get; set; }
    public int PendingActionCount { get; set; }
    public int SatisfiedCount { get; set; }
    public int NotRequiredCount { get; set; }
    public int UnclassifiedCount { get; set; }
    public int DriftCount { get; set; }
    public bool HasStatusDrift { get; set; }

    /// <summary>Groups requiring an invoice whose expected total is unrecorded, across all currencies.</summary>
    public int GroupsWithUnknownExpectedTotal { get; set; }

    public List<OperationInvoiceObligationCurrencyTotalsDto> CurrencyTotals { get; set; } = new();
}

/// <summary>One currency bucket. UNKNOWN collects pre-flag groups that carry no currency at all.</summary>
public class OperationInvoiceObligationCurrencyTotalsDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExpectedTotal { get; set; }
    public decimal ValidatedTotal { get; set; }
    public decimal PendingValidationTotal { get; set; }
    public decimal RemainingTotal { get; set; }

    /// <summary>These groups' amounts are absent from the sums above — unknown, not zero.</summary>
    public int GroupsWithUnknownExpectedTotal { get; set; }
}
