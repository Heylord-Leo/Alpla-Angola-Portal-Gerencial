using System;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Create payload for an operation invoice (Release 4 Phase 2b). RequestId comes from the route,
/// never from the body. Manual creation only — OCR intake is Phase 5.
///
/// <para>Amount contract (deterministic, documented): <c>GrossAmount</c> is required and positive;
/// <c>NetAmount</c> and <c>TaxAmount</c> travel TOGETHER or not at all, and when present must
/// reconcile to the gross within the financial-integrity tolerance. Nothing is ever inferred.</para>
/// </summary>
public class SaveOperationInvoiceDto
{
    /// <summary>A TYPE_OPERATION_INVOICE attachment of this request, not yet claimed. Required.</summary>
    public Guid? AttachmentId { get; set; }

    public int? SupplierId { get; set; }

    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Finance must see that numbers were typed, not read. Phase 2b creation is manual by
    /// definition, so omitting this defaults to TRUE; it exists so the future OCR intake can
    /// state the opposite.
    /// </summary>
    public bool? AmountsEnteredManually { get; set; }
}

/// <summary>One operation invoice as the UI sees it. No allocation detail — that is Phase 3.</summary>
public class OperationInvoiceDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }

    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierTaxIdSnapshot { get; set; }

    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }

    /// <summary>Document lifecycle — see RequestConstants.OperationInvoiceDocumentStatuses.</summary>
    public string Status { get; set; } = string.Empty;
    public bool AmountsEnteredManually { get; set; }

    public string? Notes { get; set; }

    public Guid AttachmentId { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentStorageReference { get; set; }

    public DateTime UploadedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string? UploadedByName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public DateTime? ValidatedAtUtc { get; set; }
    public Guid? ValidatedByUserId { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }

    /// <summary>Set when this invoice was replaced by a corrected one. The chain stays readable.</summary>
    public Guid? SupersededByOperationInvoiceId { get; set; }

    /// <summary>Concurrency token for the Phase 2c update contract.</summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Advisory duplicate preflight, asked BEFORE the user finishes typing an invoice. The 409 at
/// persistence remains the enforcement — two clients can both pass this and only one can win.
/// </summary>
public class CheckOperationInvoiceDuplicateDto
{
    /// <summary>Client-side hash of the file about to be attached, when available.</summary>
    public string? ContentHash { get; set; }

    public int? SupplierId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
}

/// <summary>What the preflight found. Null members mean "nothing of that kind".</summary>
public class OperationInvoiceDuplicateResultDto
{
    /// <summary>The identical file is already registered as an effective operation invoice.</summary>
    public OperationInvoiceDuplicateCandidateDto? SameFile { get; set; }

    /// <summary>The same fiscal identity (supplier + number + series) already exists.</summary>
    public OperationInvoiceDuplicateCandidateDto? SameBusinessDocument { get; set; }
}

/// <summary>Safe identification of an existing invoice — enough to explain, nothing sensitive.</summary>
public class OperationInvoiceDuplicateCandidateDto
{
    public Guid OperationInvoiceId { get; set; }
    public Guid RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public string Status { get; set; } = string.Empty;
}
