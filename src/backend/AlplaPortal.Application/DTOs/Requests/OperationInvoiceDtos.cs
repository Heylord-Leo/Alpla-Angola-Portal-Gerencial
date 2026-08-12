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

    /// <summary>
    /// Concurrency token on Update (Phase 2c): the RowVersion the client loaded. Ignored on
    /// Create. On update, a null field keeps the persisted value (source-document convention),
    /// so this DTO doubles as the partial-update payload.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>Voids an invoice registered in error (Phase 2c). Never a Finance rejection.</summary>
public class VoidOperationInvoiceDto
{
    /// <summary>Mandatory — a void without a written reason is not auditable.</summary>
    public string? Reason { get; set; }

    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Finance validation of a pending invoice (Phase 2e). The transition needs nothing but the
/// concurrency token — status and the validation stamps are server-controlled, always.
/// </summary>
public class ValidateOperationInvoiceDto
{
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Phase 3A: explicit Finance decisions for allocations that push a group beyond its expected
    /// total (approved rule #13). One entry per over-expected group; validation fails without them.
    /// </summary>
    public List<OperationInvoiceDivergenceAcceptanceDto>? DivergenceAcceptances { get; set; }
}

/// <summary>Finance rejection of a pending invoice (Phase 2e). Terminal; reason mandatory.</summary>
public class RejectOperationInvoiceDto
{
    public string? Reason { get; set; }

    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Replaces a VALIDATED invoice with a corrected one (Phase 2c) — the only path out of VALIDATED.
/// The header fields describe the NEW invoice; <c>RowVersion</c> is the ORIGINAL invoice's
/// concurrency token, because the original is what this operation mutates.
/// </summary>
public class ReplaceOperationInvoiceDto : SaveOperationInvoiceDto
{
    /// <summary>Mandatory — why the validated invoice is being superseded.</summary>
    public string? ReplacementReason { get; set; }
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
    /// <summary>True when either dimension found an effective duplicate — the one flag a client
    /// needs to branch on; the members below say which kind and name the existing invoice.</summary>
    public bool HasDuplicate => SameFile != null || SameBusinessDocument != null;

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

// ═══════════════════════════════════════════════════════════════════════════════════════════
// Release 4 Phase 3A — allocation / coverage / short-close / expected-total activation
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>One allocation as the UI reads it. Effectiveness is DERIVED from the owning
/// invoice's status — the row itself carries no state.</summary>
public class OperationInvoiceAllocationDto
{
    public Guid Id { get; set; }
    public Guid OperationInvoiceId { get; set; }
    public Guid RequestPoGroupId { get; set; }

    public decimal AllocatedNetAmount { get; set; }
    public decimal AllocatedTaxAmount { get; set; }
    public decimal AllocatedGrossAmount { get; set; }

    public int SequenceNumber { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // ── Derived classification (server-computed, never client-decided) ──
    /// <summary>Owning invoice status — see RequestConstants.OperationInvoiceDocumentStatuses.</summary>
    public string InvoiceStatus { get; set; } = string.Empty;
    public string? InvoiceDocumentNumber { get; set; }
    public string? InvoiceDocumentSeries { get; set; }

    /// <summary>Counts toward validated coverage (invoice VALIDATED).</summary>
    public bool IsEffective { get; set; }
    /// <summary>Draft awaiting the Finance decision (invoice UPLOADED/PENDING_VALIDATION).</summary>
    public bool IsPendingDecision { get; set; }

    public string? GroupSupplierName { get; set; }
    public string? GroupCurrencyCode { get; set; }
}

/// <summary>
/// Atomic replace-set for one invoice's allocations (Phase 3A). The payload IS the resulting
/// set: rows are added/updated/removed server-side to match it. Identity + amounts + note only —
/// coverage, remaining, tolerance and effectiveness are computed by the server.
/// <c>RowVersion</c> is the INVOICE token; every touched group is additionally guarded by its
/// own database RowVersion inside the same transaction.
/// </summary>
public class SaveOperationInvoiceAllocationsDto
{
    public byte[]? RowVersion { get; set; }
    public List<SaveOperationInvoiceAllocationItemDto> Allocations { get; set; } = new();
}

public class SaveOperationInvoiceAllocationItemDto
{
    public Guid RequestPoGroupId { get; set; }
    public decimal AllocatedNetAmount { get; set; }
    public decimal AllocatedTaxAmount { get; set; }
    public decimal AllocatedGrossAmount { get; set; }
    /// <summary>Optional context. MANDATORY (meaningful text) when a Finance/SysAdmin allocation
    /// pushes the group beyond its expected total — the draft-time divergence explanation.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Explicit Finance decision on an over-expected group at validation time (approved rule #13):
/// without an entry carrying <c>Accepted=true</c> and a meaningful justification for every
/// over-expected group, validation fails. Never inferred, never auto-accepted.
/// </summary>
public class OperationInvoiceDivergenceAcceptanceDto
{
    public Guid RequestPoGroupId { get; set; }
    public bool Accepted { get; set; }
    public string? Justification { get; set; }
}

/// <summary>Read model of one short-close proposal/decision.</summary>
public class OperationInvoiceShortCloseDto
{
    public Guid Id { get; set; }
    public Guid RequestPoGroupId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Guid ProposedByUserId { get; set; }
    public string? ProposedByName { get; set; }
    public DateTime ProposedAtUtc { get; set; }
    public string ProposalJustification { get; set; } = string.Empty;
    public Guid? EvidenceAttachmentId { get; set; }
    public decimal RemainingAmountAtProposal { get; set; }

    public Guid? DecidedByUserId { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }

    public byte[]? RowVersion { get; set; }
}

public class ProposeOperationInvoiceShortCloseDto
{
    /// <summary>Mandatory meaningful justification for closing below the expected total.</summary>
    public string? Justification { get; set; }
    /// <summary>Optional supporting evidence (attachment of this request).</summary>
    public Guid? EvidenceAttachmentId { get; set; }
}

public class DecideOperationInvoiceShortCloseDto
{
    /// <summary>Optional on approval; MANDATORY on rejection.</summary>
    public string? DecisionReason { get; set; }
    public byte[]? RowVersion { get; set; }
}

// ── Expected-total activation (controlled Release 4 backfill — Admin operation) ──

/// <summary>One group as the activation preview reports it. PREVIEW never mutates.</summary>
public class ExpectedTotalBackfillGroupDto
{
    public Guid RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public Guid RequestPoGroupId { get; set; }
    public string? SupplierName { get; set; }
    public string? CurrencyCode { get; set; }
    public string GroupStatus { get; set; } = string.Empty;
    public string OperationInvoiceStatus { get; set; } = string.Empty;
    public decimal? CurrentExpectedTotal { get; set; }
    public decimal? ProposedExpectedTotal { get; set; }
    public bool Eligible { get; set; }
    /// <summary>Why the group is skipped (NOT_CLASSIFIED / NOT_REQUIRED / NO_TOTAL / …).</summary>
    public string? SkipReason { get; set; }
}

public class ExpectedTotalBackfillPreviewDto
{
    public int EligibleCount { get; set; }
    public int SkippedCount { get; set; }
    public List<ExpectedTotalBackfillGroupDto> Groups { get; set; } = new();
}

public class ApplyExpectedTotalBackfillDto
{
    /// <summary>Mandatory meaningful reason, frozen into every written group's justification.</summary>
    public string? Reason { get; set; }
}

public class ExpectedTotalBackfillResultDto
{
    public int WrittenCount { get; set; }
    public int SkippedCount { get; set; }
    public List<ExpectedTotalBackfillGroupDto> WrittenGroups { get; set; } = new();
}
