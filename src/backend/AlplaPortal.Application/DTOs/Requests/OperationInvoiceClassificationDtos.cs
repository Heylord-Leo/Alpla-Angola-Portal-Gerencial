using System;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// v2.245.8 — body of <c>POST {requestId}/po-groups/{groupId}/operation-invoice-classification</c>:
/// the Finance decision that gives an UNCLASSIFIED (legacy / pre-activation) P.O. group its document
/// identity. Identity only — every obligation is re-derived from it by the single resolver.
/// </summary>
public class ClassifyOperationInvoiceDto
{
    /// <summary>One of <c>RequestConstants.SourceDocumentTypes.ValidValues</c> (legacy aliases accepted).</summary>
    public string? SourceDocumentType { get; set; }

    /// <summary>Mandatory operator reason, persisted verbatim in the audit event.</summary>
    public string? Justification { get; set; }
}

/// <summary>The group after classification — what the drawer needs to refresh its coverage card.</summary>
public class OperationInvoiceClassificationResultDto
{
    public Guid RequestId { get; set; }
    public Guid GroupId { get; set; }
    public string? PreviousSourceDocumentType { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public string OperationInvoiceStatus { get; set; } = string.Empty;
    public bool RequiresOperationInvoice { get; set; }
    public bool RequiresSeparateFiscalReceipt { get; set; }
    public bool RequiresAdvanceRegularization { get; set; }
    public bool RequiresFinanceClassificationReview { get; set; }
    public decimal? ExpectedAmount { get; set; }
    public string? ExpectedCurrency { get; set; }
    public DateTime ClassifiedAtUtc { get; set; }
}

/// <summary>
/// v2.245.8 — answer of <c>POST {requestId}/operation-invoices/preflight</c>: the SAME admissibility
/// gates the create endpoint applies (scope, role, obligation, lifecycle status), evaluated before the
/// client uploads the invoice file. Failures come back as the create endpoint's own ProblemDetails.
/// </summary>
public class OperationInvoiceCreatePreflightDto
{
    public bool Admissible { get; set; }
}

/// <summary>
/// v2.245.8 — an OPERATION_INVOICE attachment of the request that no invoice has claimed yet: the
/// durable, server-side record of a registration interrupted after its upload (create refused,
/// lost response, closed modal, reload, another session). The drawer offers to reuse or release it.
/// </summary>
public class OperationInvoiceUnclaimedAttachmentDto
{
    public Guid AttachmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public decimal FileSizeMBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string? UploadedByName { get; set; }
}
