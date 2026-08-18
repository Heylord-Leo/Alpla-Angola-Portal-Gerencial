using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable audit snapshot of a operation invoice reconciliation: the commercial baseline compared
/// against the uploaded operation invoice, plus the explicit decision Finance took about it.
///
/// Release 1 creates the table only — the calculator and the endpoints that write rows arrive in
/// Release 3. Rows are never updated: a new upload produces a new snapshot, which is what keeps
/// the audit trail honest when a document is replaced.
/// </summary>
public class OperationInvoiceReconciliation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The PO group whose operation-invoice obligation this snapshot belongs to.</summary>
    public Guid RequestPoGroupId { get; set; }
    public RequestPoGroup RequestPoGroup { get; set; } = null!;

    /// <summary>The reconciled operation-invoice attachment.</summary>
    public Guid OperationInvoiceAttachmentId { get; set; }

    /// <summary>The document this comparison was computed for.</summary>
    public Guid? OperationInvoiceId { get; set; }

    /// <summary>
    /// The allocation this comparison belongs to — the natural grain, because the comparison is
    /// against ONE group's baseline. A consolidated invoice therefore produces one snapshot per
    /// group it touches, each self-contained.
    /// </summary>
    public Guid? OperationInvoiceAllocationId { get; set; }

    /// <summary>The NIF printed on the document matched the registered supplier.</summary>
    public bool NifMatched { get; set; }

    /// <summary>The billed entity matched the request's company.</summary>
    public bool CompanyMatched { get; set; }

    /// <summary>OCR read the document as something other than an operation invoice.</summary>
    public string? ClassificationWarning { get; set; }

    /// <summary>The share of the invoice allocated to this group.</summary>
    public decimal AllocatedTotal { get; set; }

    /// <summary>
    /// Cumulative validated coverage of the group BEFORE this comparison, and the expected total as
    /// it stood then. Stored so the snapshot can be re-read years later without reconstructing the
    /// state of the world at the time.
    /// </summary>
    public decimal CumulativeValidatedTotalBefore { get; set; }
    public decimal ExpectedTotalAtComparison { get; set; }

    /// <summary>Commercial baseline total: winning quotation (QUOTATION) or proforma/request total (PAYMENT).</summary>
    public decimal BaselineTotal { get; set; }

    /// <summary>Grand total read from the operation invoice.</summary>
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
