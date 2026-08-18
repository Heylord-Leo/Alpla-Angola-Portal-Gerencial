using System;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 1 foundation tests for the history idempotency keys (plan v6 §20).
///
/// These tests exist to pin down the property the whole deduplication scheme rests on:
/// a key is a function of persisted business identifiers ONLY. If a future change smuggles a
/// timestamp, a DateOnly or a freshly generated GUID into a key, the stability assertions below
/// fail immediately rather than silently producing duplicate history rows in production.
/// </summary>
public class PostPaymentIdempotencyKeyTests
{
    private static readonly Guid GroupId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid AttachmentId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
    private static readonly Guid RequestId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly Guid CycleId = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");

    // ── Exact formats (plan v6 §20.1) ──

    [Fact]
    public void Keys_use_the_documented_prefixes_and_identifiers()
    {
        var g = GroupId.ToString("D").ToLowerInvariant();
        var a = AttachmentId.ToString("D").ToLowerInvariant();
        var r = RequestId.ToString("D").ToLowerInvariant();
        var c = CycleId.ToString("D").ToLowerInvariant();

        Assert.Equal($"FI_UP:{g}:{a}", PostPaymentIdempotencyKeys.FinalInvoiceUploaded(GroupId, AttachmentId));
        Assert.Equal($"FI_VAL:{g}:{a}", PostPaymentIdempotencyKeys.FinalInvoiceValidated(GroupId, AttachmentId));
        Assert.Equal($"FI_REJ:{g}:{a}", PostPaymentIdempotencyKeys.FinalInvoiceRejected(GroupId, AttachmentId));
        Assert.Equal($"FI_REP:{g}:{a}", PostPaymentIdempotencyKeys.FinalInvoiceReplacementRequested(GroupId, AttachmentId));
        Assert.Equal($"FI_DIV:{g}:{a}", PostPaymentIdempotencyKeys.FinalInvoiceDivergenceAccepted(GroupId, AttachmentId));
        Assert.Equal($"FR_UP:{g}:{a}", PostPaymentIdempotencyKeys.FiscalReceiptUploaded(GroupId, AttachmentId));
        Assert.Equal($"OR_DONE:{g}", PostPaymentIdempotencyKeys.OperationalReceiptCompleted(GroupId));
        Assert.Equal($"GC:{g}:{a}", PostPaymentIdempotencyKeys.GroupCompleted(GroupId, AttachmentId));
        Assert.Equal($"RC:{r}:{c}", PostPaymentIdempotencyKeys.RequestCompleted(RequestId, CycleId));
        Assert.Equal($"LC:{g}:PROFORMA", PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "PROFORMA"));
    }

    // ── Stability: the property that makes retries safe ──

    [Fact]
    public void Group_completion_key_is_identical_across_repeated_calls()
    {
        var first = PostPaymentIdempotencyKeys.GroupCompleted(GroupId, AttachmentId);
        var second = PostPaymentIdempotencyKeys.GroupCompleted(GroupId, AttachmentId);
        var third = PostPaymentIdempotencyKeys.GroupCompleted(GroupId, AttachmentId);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Request_completion_key_is_identical_across_repeated_calls_with_the_persisted_cycle_id()
    {
        // Simulates a retry: the loser of the RowVersion race reloads and reuses the winner's
        // persisted CompletionCycleId, so the key must not drift.
        var winner = PostPaymentIdempotencyKeys.RequestCompleted(RequestId, CycleId);
        var retry = PostPaymentIdempotencyKeys.RequestCompleted(RequestId, CycleId);

        Assert.Equal(winner, retry);
    }

    [Fact]
    public void Keys_do_not_vary_with_time()
    {
        // No date, no DateOnly, no timestamp component: two calls separated in time must match,
        // and no key may contain today's date in any common rendering.
        var before = PostPaymentIdempotencyKeys.FinalInvoiceValidated(GroupId, AttachmentId);
        System.Threading.Thread.Sleep(15);
        var after = PostPaymentIdempotencyKeys.FinalInvoiceValidated(GroupId, AttachmentId);

        Assert.Equal(before, after);

        var now = DateTime.UtcNow;
        Assert.DoesNotContain(now.ToString("yyyy-MM-dd"), before, StringComparison.Ordinal);
        Assert.DoesNotContain(now.ToString("yyyyMMdd"), before, StringComparison.Ordinal);
    }

    [Fact]
    public void Group_completion_key_is_derived_from_the_fiscal_receipt_not_from_a_new_guid()
    {
        var fiscalReceipt = Guid.NewGuid();

        var key = PostPaymentIdempotencyKeys.GroupCompleted(GroupId, fiscalReceipt);

        Assert.Contains(fiscalReceipt.ToString("D").ToLowerInvariant(), key, StringComparison.Ordinal);
        Assert.Equal(key, PostPaymentIdempotencyKeys.GroupCompleted(GroupId, fiscalReceipt));
    }

    // ── Discrimination: distinct business facts must not collide ──

    [Fact]
    public void Different_attachments_produce_different_keys_so_a_replacement_is_recorded()
    {
        var rejected = Guid.NewGuid();
        var replacement = Guid.NewGuid();

        var rejectionKey = PostPaymentIdempotencyKeys.FinalInvoiceRejected(GroupId, rejected);
        var reuploadKey = PostPaymentIdempotencyKeys.FinalInvoiceUploaded(GroupId, replacement);

        Assert.NotEqual(rejectionKey, reuploadKey);
        Assert.NotEqual(
            PostPaymentIdempotencyKeys.FinalInvoiceUploaded(GroupId, rejected),
            PostPaymentIdempotencyKeys.FinalInvoiceUploaded(GroupId, replacement));
    }

    [Fact]
    public void Different_actions_on_the_same_attachment_produce_different_keys()
    {
        var upload = PostPaymentIdempotencyKeys.FinalInvoiceUploaded(GroupId, AttachmentId);
        var validate = PostPaymentIdempotencyKeys.FinalInvoiceValidated(GroupId, AttachmentId);
        var reject = PostPaymentIdempotencyKeys.FinalInvoiceRejected(GroupId, AttachmentId);
        var replace = PostPaymentIdempotencyKeys.FinalInvoiceReplacementRequested(GroupId, AttachmentId);
        var divergence = PostPaymentIdempotencyKeys.FinalInvoiceDivergenceAccepted(GroupId, AttachmentId);
        var fiscal = PostPaymentIdempotencyKeys.FiscalReceiptUploaded(GroupId, AttachmentId);

        var all = new[] { upload, validate, reject, replace, divergence, fiscal };
        Assert.Equal(all.Length, new System.Collections.Generic.HashSet<string>(all, StringComparer.Ordinal).Count);
    }

    [Fact]
    public void Different_groups_produce_different_keys()
    {
        Assert.NotEqual(
            PostPaymentIdempotencyKeys.OperationalReceiptCompleted(Guid.NewGuid()),
            PostPaymentIdempotencyKeys.OperationalReceiptCompleted(Guid.NewGuid()));
    }

    [Fact]
    public void Legacy_classification_key_distinguishes_the_two_billing_document_types()
    {
        Assert.NotEqual(
            PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "PROFORMA"),
            PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "FINAL_INVOICE"));
    }

    [Fact]
    public void Legacy_classification_key_is_case_and_whitespace_normalised()
    {
        Assert.Equal(
            PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "PROFORMA"),
            PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "  proforma "));
    }

    // ── Guards ──

    [Fact]
    public void Empty_guids_are_rejected_an_empty_identity_would_collapse_distinct_events()
    {
        Assert.Throws<ArgumentException>(() => PostPaymentIdempotencyKeys.GroupCompleted(Guid.Empty, AttachmentId));
        Assert.Throws<ArgumentException>(() => PostPaymentIdempotencyKeys.GroupCompleted(GroupId, Guid.Empty));
        Assert.Throws<ArgumentException>(() => PostPaymentIdempotencyKeys.RequestCompleted(RequestId, Guid.Empty));
        Assert.Throws<ArgumentException>(() => PostPaymentIdempotencyKeys.OperationalReceiptCompleted(Guid.Empty));
    }

    [Fact]
    public void Blank_billing_document_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "   "));
        Assert.Throws<ArgumentException>(() => PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, null!));
    }

    [Fact]
    public void Every_key_fits_the_persisted_column()
    {
        var keys = new[]
        {
            PostPaymentIdempotencyKeys.FinalInvoiceUploaded(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.FinalInvoiceValidated(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.FinalInvoiceRejected(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.FinalInvoiceReplacementRequested(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.FinalInvoiceDivergenceAccepted(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.FiscalReceiptUploaded(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.OperationalReceiptCompleted(GroupId),
            PostPaymentIdempotencyKeys.GroupCompleted(GroupId, AttachmentId),
            PostPaymentIdempotencyKeys.RequestCompleted(RequestId, CycleId),
            PostPaymentIdempotencyKeys.LegacyDocumentClassified(GroupId, "FINAL_INVOICE")
        };

        foreach (var key in keys)
            Assert.True(key.Length <= PostPaymentIdempotencyKeys.MaxLength, $"Key too long: {key}");
    }
}
