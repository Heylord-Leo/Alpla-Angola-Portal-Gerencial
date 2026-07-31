using System;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Single source of truth for <c>RequestStatusHistory.IdempotencyKey</c> values (and the matching
/// <c>EmailOutboxEntry.CorrelationId</c>) of the Post-Payment Completion Workflow.
///
/// Design rule — every key is derived exclusively from PERSISTED BUSINESS IDENTIFIERS:
/// <list type="bullet">
/// <item>no date, no <c>DateOnly</c>, no timestamp — correctness never depends on when a retry happens;</item>
/// <item>no per-attempt random GUID — a retried transaction must recompute a byte-identical key.</item>
/// </list>
///
/// Consequences that make the scheme correct:
/// <list type="bullet">
/// <item>the same attachment validated twice yields the same key → deduplicated;</item>
/// <item>a legitimate replacement document is a DIFFERENT attachment → different key → new row,
/// even seconds after the rejected one;</item>
/// <item>group completion is identified by the Fiscal Receipt, the terminal document that closes
/// the group — a group can never complete without one;</item>
/// <item>request completion is identified by <c>Request.CompletionCycleId</c>, persisted by the
/// winning atomic transition and reused verbatim by every retry.</item>
/// </list>
/// </summary>
public static class PostPaymentIdempotencyKeys
{
    /// <summary>Maximum length of the persisted column. Every key produced here stays far below it.</summary>
    public const int MaxLength = 256;

    // ── Final Invoice ──

    /// <summary>FINAL_INVOICE_UPLOADED — FI_UP:{GroupId}:{AttachmentId}</summary>
    public static string FinalInvoiceUploaded(Guid groupId, Guid attachmentId)
        => Build("FI_UP", groupId, attachmentId);

    /// <summary>FINAL_INVOICE_VALIDATED — FI_VAL:{GroupId}:{AttachmentId}</summary>
    public static string FinalInvoiceValidated(Guid groupId, Guid attachmentId)
        => Build("FI_VAL", groupId, attachmentId);

    /// <summary>FINAL_INVOICE_REJECTED — FI_REJ:{GroupId}:{AttachmentId}</summary>
    public static string FinalInvoiceRejected(Guid groupId, Guid attachmentId)
        => Build("FI_REJ", groupId, attachmentId);

    /// <summary>FINAL_INVOICE_REPLACEMENT_REQUESTED — FI_REP:{GroupId}:{AttachmentId}</summary>
    public static string FinalInvoiceReplacementRequested(Guid groupId, Guid attachmentId)
        => Build("FI_REP", groupId, attachmentId);

    /// <summary>FINAL_INVOICE_DIVERGENCE_ACCEPTED — FI_DIV:{GroupId}:{AttachmentId}</summary>
    public static string FinalInvoiceDivergenceAccepted(Guid groupId, Guid attachmentId)
        => Build("FI_DIV", groupId, attachmentId);

    // ── Fiscal Receipt ──

    /// <summary>FISCAL_RECEIPT_UPLOADED — FR_UP:{GroupId}:{AttachmentId}</summary>
    public static string FiscalReceiptUploaded(Guid groupId, Guid attachmentId)
        => Build("FR_UP", groupId, attachmentId);

    // ── Operational Receipt ──

    /// <summary>
    /// OPERATIONAL_RECEIPT_COMPLETED — OR_DONE:{GroupId}.
    /// Group-scoped: operational receipt completes at most once per group.
    /// </summary>
    public static string OperationalReceiptCompleted(Guid groupId)
        => $"OR_DONE:{Format(groupId)}";

    // ── Completion ──

    /// <summary>
    /// GROUP_COMPLETED — GC:{GroupId}:{FiscalReceiptAttachmentId}.
    /// Stable because the Fiscal Receipt is the terminal document of the group: the same receipt
    /// can never produce two GROUP_COMPLETED rows, and a retry recomputes the identical key.
    /// </summary>
    public static string GroupCompleted(Guid groupId, Guid fiscalReceiptAttachmentId)
        => Build("GC", groupId, fiscalReceiptAttachmentId);

    /// <summary>
    /// REQUEST_COMPLETED — RC:{RequestId}:{CompletionCycleId}.
    /// <paramref name="completionCycleId"/> is <c>Request.CompletionCycleId</c>: generated inside
    /// the transaction that performs the parent transition and persisted in the SAME SaveChanges
    /// as StatusId. A transition that loses the RowVersion race rolls back entirely — its GUID
    /// never reaches the database — and the loser reuses the winner's persisted value.
    /// </summary>
    public static string RequestCompleted(Guid requestId, Guid completionCycleId)
        => Build("RC", requestId, completionCycleId);

    // ── Legacy classification ──

    /// <summary>
    /// LEGACY_DOCUMENT_CLASSIFIED — LC:{GroupId}:{SourceDocumentType}.
    /// Keyed by the decision itself: re-applying the same classification is a no-op, while
    /// correcting it to a different type is a distinct, separately audited event.
    /// </summary>
    public static string LegacyDocumentClassified(Guid groupId, string sourceDocumentType)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentType))
            throw new ArgumentException("Source document type is required.", nameof(sourceDocumentType));

        return $"LC:{Format(groupId)}:{sourceDocumentType.Trim().ToUpperInvariant()}";
    }

    // ── Document classification (Release 2 corrected) ──

    /// <summary>
    /// DOCUMENT_CLASSIFIED — DC:{Scope}:{ScopeId}:{SourceDocumentType}.
    /// Records the classification decision together with any acknowledged OCR conflict.
    /// Re-affirming the same classification deduplicates; changing it is a new audited event.
    /// </summary>
    public static string DocumentClassified(string scope, Guid scopeId, string sourceDocumentType)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(sourceDocumentType))
            throw new ArgumentException("Source document type is required.", nameof(sourceDocumentType));

        return $"DC:{scope.Trim().ToUpperInvariant()}:{Format(scopeId)}:{sourceDocumentType.Trim().ToUpperInvariant()}";
    }

    private static string Build(string prefix, Guid scopeId, Guid identityId)
        => $"{prefix}:{Format(scopeId)}:{Format(identityId)}";

    /// <summary>
    /// Canonical GUID rendering: lowercase, hyphenated ("D"). Fixed so the same business fact
    /// always produces byte-identical text regardless of who builds it.
    /// Guid.Empty is rejected — an empty identity would collapse distinct events onto one key.
    /// </summary>
    private static string Format(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An empty GUID is not a valid idempotency identity.", nameof(value));

        return value.ToString("D").ToLowerInvariant();
    }
}
