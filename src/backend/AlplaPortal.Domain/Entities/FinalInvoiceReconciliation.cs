using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable audit snapshot of a Final Invoice reconciliation: the commercial baseline compared
/// against the uploaded Final Invoice, plus the explicit decision Finance took about it.
///
/// Release 1 creates the table only — the calculator and the endpoints that write rows arrive in
/// Release 3. Rows are never updated: a new upload produces a new snapshot, which is what keeps
/// the audit trail honest when a document is replaced.
/// </summary>
public class FinalInvoiceReconciliation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The PO group whose Final Invoice obligation this snapshot belongs to.</summary>
    public Guid RequestPoGroupId { get; set; }
    public RequestPoGroup RequestPoGroup { get; set; } = null!;

    /// <summary>The reconciled Final Invoice attachment (also part of the history idempotency key).</summary>
    public Guid FinalInvoiceAttachmentId { get; set; }

    /// <summary>Commercial baseline total: winning quotation (QUOTATION) or proforma/request total (PAYMENT).</summary>
    public decimal BaselineTotal { get; set; }

    /// <summary>Grand total read from the Final Invoice.</summary>
    public decimal InvoiceTotal { get; set; }

    /// <summary>Unexplained difference left after the per-line reconciliation buckets.</summary>
    public decimal ResidualVariance { get; set; }

    /// <summary>Tolerance applied, from RequestConstants.FinancialIntegrity.CalculateTolerance().</summary>
    public decimal ToleranceApplied { get; set; }

    /// <summary>True when the residual variance exceeded the tolerance.</summary>
    public bool DivergenceDetected { get; set; }

    /// <summary>True when Finance explicitly accepted the divergence instead of rejecting it.</summary>
    public bool DivergenceAccepted { get; set; }

    /// <summary>Mandatory when <see cref="DivergenceAccepted"/> is true. Never silently empty.</summary>
    public string? DivergenceJustification { get; set; }

    /// <summary>Supplier identity of the invoice matched the group's supplier.</summary>
    public bool SupplierMatched { get; set; }

    /// <summary>Currency of the invoice matched the group's currency.</summary>
    public bool CurrencyMatched { get; set; }

    /// <summary>Full per-line comparison payload, serialized. Kept opaque to the database.</summary>
    public string ReconciliationDataJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}
