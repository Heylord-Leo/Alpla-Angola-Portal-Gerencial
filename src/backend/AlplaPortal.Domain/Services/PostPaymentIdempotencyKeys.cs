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

    /// <summary>
    /// DOCUMENT_CLASSIFICATION_OVERRIDE — DC_OVERRIDE:{Context}:{ScopeId}:{AttachmentId}:{SelectedType}.
    ///
    /// <para>Identifies the DECISION, not the act of saving it. Re-saving a draft that already
    /// carries the same confirmed override recomputes the same key and writes nothing new, which is
    /// what keeps an edited draft from accumulating one audit row per keystroke-triggered save.
    /// Changing the selection to a different type is a genuinely different decision and therefore a
    /// new, separately audited event — as is attaching a different document, because the evidence
    /// the user overrode is no longer the same evidence.</para>
    ///
    /// <para><paramref name="attachmentId"/> is nullable: a payment draft can be classified before
    /// any document is attached. Absence is rendered as the literal <c>NONE</c> rather than an empty
    /// GUID, so "no attachment" can never be confused with a real identity.</para>
    /// </summary>
    public static string DocumentClassificationOverride(
        string context, Guid scopeId, Guid? attachmentId, string selectedType)
    {
        if (string.IsNullOrWhiteSpace(context))
            throw new ArgumentException("Context is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(selectedType))
            throw new ArgumentException("Selected document type is required.", nameof(selectedType));

        var attachment = attachmentId.HasValue ? Format(attachmentId.Value) : "NONE";

        return $"DC_OVERRIDE:{context.Trim().ToUpperInvariant()}:{Format(scopeId)}:{attachment}:" +
               selectedType.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// DOCUMENT_CLASSIFICATION_SET — DC_SET:{Context}:{ScopeId}:{AttachmentId}:{SelectedType}
    ///
    /// <para>The user named a type the extraction could not determine (it read OTHER, UNCLASSIFIED,
    /// or nothing at all). A decision worth recording, but <b>not</b> an override: nothing was
    /// contradicted, so no acknowledgement and no written justification are owed.</para>
    ///
    /// <para>A distinct prefix from <c>DC_OVERRIDE</c> on purpose. The two events mean different
    /// things and are counted differently when someone asks how often people disagreed with the
    /// reading; sharing a key would also let one silently deduplicate the other away.</para>
    /// </summary>
    public static string DocumentClassificationSet(
        string context, Guid scopeId, Guid? attachmentId, string selectedType)
    {
        if (string.IsNullOrWhiteSpace(context))
            throw new ArgumentException("Context is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(selectedType))
            throw new ArgumentException("Selected document type is required.", nameof(selectedType));

        var attachment = attachmentId.HasValue ? Format(attachmentId.Value) : "NONE";

        return $"DC_SET:{context.Trim().ToUpperInvariant()}:{Format(scopeId)}:{attachment}:" +
               selectedType.Trim().ToUpperInvariant();
    }

    // ── PAYMENT source documents (Release 3) ──

    /// <summary>
    /// PAYMENT_SOURCE_DOCUMENT_ADDED — PSD_ADD:{RequestId}:{AttachmentId}.
    ///
    /// <para>Keyed on the attachment because the document row does not exist yet, and because the
    /// attachment is what a retried upload would duplicate. Deliberately covers CREATION only: an
    /// ordinary field edit must stay repeatable forever, so no key is issued for updates.</para>
    /// </summary>
    public static string PaymentSourceDocumentAdded(Guid requestId, Guid attachmentId)
        => Build("PSD_ADD", requestId, attachmentId);

    /// <summary>PAYMENT_SOURCE_DOCUMENT_VOIDED — PSD_VOID:{PaymentSourceDocumentId}.</summary>
    public static string PaymentSourceDocumentVoided(Guid paymentSourceDocumentId)
        => $"PSD_VOID:{Format(paymentSourceDocumentId)}";

    /// <summary>
    /// PAYMENT_SOURCE_DOCUMENT_REPLACED — PSD_REPL:{PaymentSourceDocumentId}:{NewAttachmentId}.
    /// The new attachment is part of the identity: replacing the same document twice with two
    /// different files is two events, replacing it twice with the same file is one.
    /// </summary>
    public static string PaymentSourceDocumentReplaced(Guid paymentSourceDocumentId, Guid newAttachmentId)
        => Build("PSD_REPL", paymentSourceDocumentId, newAttachmentId);

    /// <summary>
    /// PAYMENT_GROUPS_BUILT — PG_BUILD:{RequestId}.
    /// Request-scoped and fired once: re-running final approval must never produce a second set of
    /// PO groups.
    /// </summary>
    public static string PaymentGroupsBuilt(Guid requestId)
        => $"PG_BUILD:{Format(requestId)}";

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
