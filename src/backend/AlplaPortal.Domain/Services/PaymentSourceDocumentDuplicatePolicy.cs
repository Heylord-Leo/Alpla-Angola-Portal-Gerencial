using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>One active source document, reduced to what duplicate detection needs.</summary>
public sealed record DuplicateCandidateDocument
{
    public Guid Id { get; init; }
    public Guid AttachmentId { get; init; }
    public int SequenceNumber { get; init; }
    public string? DocumentNumber { get; init; }
    public string? SupplierName { get; init; }
    public string? FileHash { get; init; }
    public bool IsVoided { get; init; }
}

public enum DuplicateScope
{
    None = 0,
    /// <summary>The identical file is already on THIS request. Not negotiable.</summary>
    SameRequest = 1,
    /// <summary>The identical file was used by another request. Warned, per existing policy.</summary>
    OtherRequest = 2
}

public enum DuplicateOutcome
{
    None = 0,
    /// <summary>Refused outright: no reading of the file could make it correct.</summary>
    Block = 1,
    /// <summary>Allowed after an explicit, acknowledged decision.</summary>
    Warn = 2
}

public sealed record DuplicateDecision
{
    public bool IsDuplicate => Scope != DuplicateScope.None;
    public DuplicateScope Scope { get; init; } = DuplicateScope.None;
    public DuplicateOutcome Outcome { get; init; } = DuplicateOutcome.None;

    public Guid? ConflictingDocumentId { get; init; }
    public int? ConflictingSequenceNumber { get; init; }
    public string? ConflictingDocumentNumber { get; init; }
    public string? SupplierName { get; init; }

    public static readonly DuplicateDecision Clear = new();
}

/// <summary>
/// Whether a file about to be attached is one the request already has.
///
/// <para>Pure, and deliberately so: this runs twice — once as a preflight before the browser spends
/// the user's time on OCR and a full review, and again inside the persistence transaction where it
/// is authoritative. Two callers, one rule, no drift.</para>
///
/// <para>The distinction that matters is <b>scope</b>. The same file already on THIS request is
/// refused outright: there is no reading of it that would make paying one invoice twice within one
/// request correct, so there is nothing for the user to acknowledge. The same file on ANOTHER
/// request is a warning — legitimate cases exist, and the existing policy lets the user proceed
/// deliberately.</para>
/// </summary>
public static class PaymentSourceDocumentDuplicatePolicy
{
    /// <summary>
    /// Decides on a candidate file.
    /// </summary>
    /// <param name="candidateHash">Content hash of the file being attached.</param>
    /// <param name="sameRequestDocuments">Every source document of this request, voided included.</param>
    /// <param name="replacingDocumentId">
    /// The document whose attachment is being replaced, if any. <b>It cannot conflict with itself</b>
    /// — re-selecting a document's own current file is a no-op, not a duplicate, and treating it as
    /// one would make "Substituir anexo" fail whenever the user picked the file already there.
    /// </param>
    /// <param name="existsOnAnotherRequest">
    /// Whether the same hash is attached to some other request. Resolved by the caller, because it
    /// depends on what the current user is allowed to see.
    /// </param>
    public static DuplicateDecision Decide(
        string? candidateHash,
        IEnumerable<DuplicateCandidateDocument> sameRequestDocuments,
        Guid? replacingDocumentId,
        bool existsOnAnotherRequest)
    {
        // No hash, no comparison. Never guess from a filename: a renamed copy of the same invoice is
        // the same invoice, and two unrelated documents are often both "factura.pdf".
        if (string.IsNullOrWhiteSpace(candidateHash))
            return existsOnAnotherRequest ? WarnOther() : DuplicateDecision.Clear;

        var conflict = sameRequestDocuments
            .Where(d => !d.IsVoided)                       // a withdrawn document is not in the request
            .Where(d => d.Id != replacingDocumentId)       // a document cannot duplicate itself
            .FirstOrDefault(d => string.Equals(
                d.FileHash, candidateHash, StringComparison.OrdinalIgnoreCase));

        if (conflict != null)
        {
            return new DuplicateDecision
            {
                Scope = DuplicateScope.SameRequest,
                Outcome = DuplicateOutcome.Block,
                ConflictingDocumentId = conflict.Id,
                ConflictingSequenceNumber = conflict.SequenceNumber,
                ConflictingDocumentNumber = conflict.DocumentNumber,
                SupplierName = conflict.SupplierName
            };
        }

        return existsOnAnotherRequest ? WarnOther() : DuplicateDecision.Clear;
    }

    private static DuplicateDecision WarnOther() => new()
    {
        Scope = DuplicateScope.OtherRequest,
        Outcome = DuplicateOutcome.Warn
    };
}
