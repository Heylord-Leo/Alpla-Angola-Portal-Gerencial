using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The same rule decides twice: as a preflight before the browser spends the user's time on OCR and
/// a full review, and again inside the persistence transaction where it is authoritative. Two
/// callers, one policy, so they cannot drift.
///
/// <para>Scope is what separates the two outcomes. The identical file already on THIS request is
/// refused outright — there is no reading of it that would make paying one invoice twice within one
/// request correct. The identical file on ANOTHER request is a warning, because legitimate cases
/// exist and the user may proceed deliberately.</para>
/// </summary>
public class PaymentSourceDocumentDuplicatePolicyTests
{
    private const string HashA = "aaaa1111";
    private const string HashB = "bbbb2222";

    private static readonly Guid Doc1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Doc2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DuplicateCandidateDocument Document(
        Guid id, int sequence, string hash, bool voided = false) => new()
    {
        Id = id,
        AttachmentId = Guid.NewGuid(),
        SequenceNumber = sequence,
        DocumentNumber = $"FT-00{sequence}",
        SupplierName = "NEXUS DIGITAL ANGOLA, LDA",
        FileHash = hash,
        IsVoided = voided
    };

    private static List<DuplicateCandidateDocument> TwoDocuments() => new()
    {
        Document(Doc1, 1, HashA),
        Document(Doc2, 2, HashB)
    };

    // ── Same request: blocked, never merely warned ──────────────────────────────────────────

    [Fact]
    public void The_same_file_already_on_this_request_is_blocked()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            HashA, TwoDocuments(), replacingDocumentId: null, existsOnAnotherRequest: false);

        Assert.True(decision.IsDuplicate);
        Assert.Equal(DuplicateScope.SameRequest, decision.Scope);
        Assert.Equal(DuplicateOutcome.Block, decision.Outcome);
        Assert.Equal(Doc1, decision.ConflictingDocumentId);
        Assert.Equal(1, decision.ConflictingSequenceNumber);
        Assert.Equal("FT-001", decision.ConflictingDocumentNumber);
    }

    [Fact]
    public void Same_request_outranks_another_request()
    {
        // Present on both. The block is the stronger statement and must win, or the user would be
        // offered an acknowledgement for something that is not negotiable.
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            HashA, TwoDocuments(), replacingDocumentId: null, existsOnAnotherRequest: true);

        Assert.Equal(DuplicateScope.SameRequest, decision.Scope);
        Assert.Equal(DuplicateOutcome.Block, decision.Outcome);
    }

    [Fact]
    public void Hash_comparison_ignores_case()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            HashA.ToUpperInvariant(), TwoDocuments(), null, false);

        Assert.Equal(DuplicateOutcome.Block, decision.Outcome);
    }

    // ── Replacement cannot conflict with itself ─────────────────────────────────────────────

    [Fact]
    public void Replacing_a_document_with_its_own_current_file_is_not_a_duplicate()
    {
        // Re-selecting the file already attached is a no-op. Calling it a duplicate would make
        // "Substituir anexo" fail whenever the user picked the file that is already there.
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            HashB, TwoDocuments(), replacingDocumentId: Doc2, existsOnAnotherRequest: false);

        Assert.False(decision.IsDuplicate);
        Assert.Equal(DuplicateOutcome.None, decision.Outcome);
    }

    [Fact]
    public void Replacing_a_document_with_another_documents_file_is_still_blocked()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            HashA, TwoDocuments(), replacingDocumentId: Doc2, existsOnAnotherRequest: false);

        Assert.Equal(DuplicateScope.SameRequest, decision.Scope);
        Assert.Equal(DuplicateOutcome.Block, decision.Outcome);
        Assert.Equal(Doc1, decision.ConflictingDocumentId);
    }

    // ── Voided documents are not in the request ─────────────────────────────────────────────

    [Fact]
    public void A_voided_document_does_not_block()
    {
        var documents = new List<DuplicateCandidateDocument>
        {
            Document(Doc1, 1, HashA, voided: true)
        };

        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(HashA, documents, null, false);

        Assert.False(decision.IsDuplicate);
    }

    // ── Another request: warned, per the existing policy ────────────────────────────────────

    [Fact]
    public void The_same_file_on_another_request_is_warned_not_blocked()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            "cccc3333", TwoDocuments(), null, existsOnAnotherRequest: true);

        Assert.True(decision.IsDuplicate);
        Assert.Equal(DuplicateScope.OtherRequest, decision.Scope);
        Assert.Equal(DuplicateOutcome.Warn, decision.Outcome);
        // Nothing about this request is named: the conflict is elsewhere.
        Assert.Null(decision.ConflictingDocumentId);
    }

    // ── Everything else is clear ────────────────────────────────────────────────────────────

    [Fact]
    public void An_unrelated_file_is_clear()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            "cccc3333", TwoDocuments(), null, existsOnAnotherRequest: false);

        Assert.False(decision.IsDuplicate);
        Assert.Equal(DuplicateScope.None, decision.Scope);
        Assert.Equal(DuplicateOutcome.None, decision.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_hash_nothing_on_this_request_can_be_matched(string? hash)
    {
        // Never fall back to filenames: a renamed copy of one invoice is the same invoice, and two
        // unrelated documents are often both "factura.pdf".
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(hash, TwoDocuments(), null, false);

        Assert.False(decision.IsDuplicate);
    }

    [Fact]
    public void Without_a_hash_a_known_cross_request_match_is_still_reported()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            null, TwoDocuments(), null, existsOnAnotherRequest: true);

        Assert.Equal(DuplicateScope.OtherRequest, decision.Scope);
        Assert.Equal(DuplicateOutcome.Warn, decision.Outcome);
    }

    [Fact]
    public void An_empty_request_blocks_nothing()
    {
        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            HashA, new List<DuplicateCandidateDocument>(), null, false);

        Assert.False(decision.IsDuplicate);
    }
}
