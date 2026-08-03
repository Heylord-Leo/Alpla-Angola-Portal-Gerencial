using System;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The rules governing a classification that contradicts the document.
///
/// <para>These decide three things the UI cannot be trusted with: whether an override happened at
/// all, whether it may be accepted, and what identity the resulting audit row carries. The last one
/// is what makes "a repeated save does not duplicate history" true.</para>
/// </summary>
public class DocumentClassificationOverrideRecorderTests
{
    private const string Payment = RequestConstants.DocumentClassificationContexts.PaymentRequest;
    private static readonly Guid Scope = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Attachment = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DocumentClassificationOverrideRequest Request(
        string? suggested = "INVOICE",
        string? selected = "PROFORMA",
        decimal? confidence = 0.9m,
        bool acknowledged = true,
        string? justification = "Documento corrigido junto do fornecedor por email.",
        Guid? attachment = null,
        string context = Payment) =>
        new()
        {
            Context = context,
            ScopeId = Scope,
            AttachmentId = attachment ?? Attachment,
            SuggestedType = suggested,
            SelectedType = selected,
            Confidence = confidence,
            Acknowledged = acknowledged,
            Justification = justification
        };

    // ── When there is nothing to record ──

    [Theory]
    [InlineData("INVOICE", "INVOICE")]   // agreement
    [InlineData(null, "INVOICE")]        // nothing was suggested
    [InlineData("INVOICE", null)]        // nothing was selected
    [InlineData(null, null)]
    public void Agreement_or_absence_is_not_an_override(string? suggested, string? selected)
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request(suggested: suggested, selected: selected));

        Assert.False(result.ShouldRecord);
        Assert.Null(result.RejectionReason);   // not an error — simply ordinary data entry
    }

    [Fact]
    public void The_superseded_code_is_resolved_before_comparing()
    {
        // FINAL_INVOICE is the old name for INVOICE. Treating them as different types would
        // manufacture a conflict out of a rename.
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request(suggested: "FINAL_INVOICE", selected: "INVOICE"));

        Assert.False(result.ShouldRecord);
    }

    // ── Admissibility ──

    [Fact]
    public void An_unacknowledged_contradiction_is_refused()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(acknowledged: false));

        Assert.False(result.ShouldRecord);
        Assert.NotNull(result.RejectionReason);
        Assert.Contains("confirmar", result.RejectionReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_high_risk_override_without_a_written_reason_is_refused()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(justification: "engano"));

        Assert.False(result.ShouldRecord);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void A_justification_one_character_short_is_still_refused()
    {
        var tooShort = new string('a', DocumentClassificationOverrideRecorder.MinimumJustificationLength - 1);

        Assert.False(DocumentClassificationOverrideRecorder.Evaluate(Request(justification: tooShort)).ShouldRecord);
        Assert.True(DocumentClassificationOverrideRecorder.Evaluate(
            Request(justification: tooShort + "a")).ShouldRecord);
    }

    [Fact]
    public void An_invalid_context_is_refused_rather_than_stored()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(context: "SOMEWHERE_ELSE"));

        Assert.False(result.ShouldRecord);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void An_empty_scope_is_refused()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request() with { ScopeId = Guid.Empty });

        Assert.False(result.ShouldRecord);
        Assert.NotNull(result.RejectionReason);
    }

    // ── When a written reason is owed ──

    [Fact]
    public void Choosing_non_fiscal_for_a_fiscal_document_always_demands_a_reason()
    {
        // The observed defect: an FT invoice classified as "Fatura Proforma". This direction
        // understates fiscal reality, so low confidence is no excuse.
        Assert.True(DocumentClassificationOverrideRecorder.RequiresJustification(
            "INVOICE", "PROFORMA", confidence: 0.10m));

        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request(confidence: 0.10m, justification: "curto"));

        Assert.False(result.ShouldRecord);
    }

    [Fact]
    public void A_low_confidence_disagreement_between_two_fiscal_types_needs_only_acknowledgement()
    {
        Assert.False(DocumentClassificationOverrideRecorder.RequiresJustification(
            "INVOICE", "ADVANCE_INVOICE", confidence: 0.30m));

        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request(selected: "ADVANCE_INVOICE", confidence: 0.30m, justification: null));

        Assert.True(result.ShouldRecord);
    }

    [Fact]
    public void A_confident_reading_demands_a_reason_even_without_a_fiscal_downgrade()
    {
        Assert.True(DocumentClassificationOverrideRecorder.RequiresJustification(
            "INVOICE", "ADVANCE_INVOICE",
            confidence: DocumentClassificationOverrideRecorder.HighConfidenceThreshold));
    }

    // ── Identity of the recorded event ──

    [Fact]
    public void The_same_decision_produces_the_same_key_however_often_it_is_saved()
    {
        var first = DocumentClassificationOverrideRecorder.Evaluate(Request());
        var second = DocumentClassificationOverrideRecorder.Evaluate(
            Request(justification: "Documento corrigido junto do fornecedor por email."));

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void Changing_the_chosen_type_is_a_different_event()
    {
        var proforma = DocumentClassificationOverrideRecorder.Evaluate(Request(selected: "PROFORMA"));
        var advance = DocumentClassificationOverrideRecorder.Evaluate(Request(selected: "ADVANCE_INVOICE"));

        Assert.NotEqual(proforma.IdempotencyKey, advance.IdempotencyKey);
    }

    [Fact]
    public void Attaching_a_different_document_is_a_different_event()
    {
        // The evidence that was overridden is no longer the same evidence.
        var a = DocumentClassificationOverrideRecorder.Evaluate(Request());
        var b = DocumentClassificationOverrideRecorder.Evaluate(Request(attachment: Guid.NewGuid()));

        Assert.NotEqual(a.IdempotencyKey, b.IdempotencyKey);
    }

    [Fact]
    public void The_same_document_classified_in_two_contexts_stays_two_events()
    {
        var payment = DocumentClassificationOverrideRecorder.Evaluate(Request());
        var quotation = DocumentClassificationOverrideRecorder.Evaluate(
            Request(context: RequestConstants.DocumentClassificationContexts.QuotationManagement));

        Assert.NotEqual(payment.IdempotencyKey, quotation.IdempotencyKey);
    }

    [Fact]
    public void The_key_shape_is_the_agreed_one()
    {
        var key = DocumentClassificationOverrideRecorder.Evaluate(Request()).IdempotencyKey;

        Assert.Equal($"DC_OVERRIDE:{Payment}:{Scope:D}:{Attachment:D}:PROFORMA", key);
        Assert.True(key.Length <= PostPaymentIdempotencyKeys.MaxLength);
    }

    [Fact]
    public void A_classification_made_before_any_document_is_attached_still_has_an_identity()
    {
        var key = PostPaymentIdempotencyKeys.DocumentClassificationOverride(
            Payment, Scope, attachmentId: null, "PROFORMA");

        // "No attachment" must be legible as such, never as an empty GUID that could collide.
        Assert.Contains(":NONE:", key);
        Assert.NotEqual(
            PostPaymentIdempotencyKeys.DocumentClassificationOverride(Payment, Scope, Attachment, "PROFORMA"),
            key);
    }

    // ── The sentence a person reads ──

    [Fact]
    public void The_history_entry_names_both_types_and_carries_the_reason()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request(suggested: "INVOICE", selected: "ADVANCE_INVOICE",
                    justification: "Adiantamento acordado com o fornecedor em contrato."));

        Assert.Equal(
            "Classificação do documento alterada de \"Factura\" para \"Factura de Adiantamento\". " +
            "Justificativa: Adiantamento acordado com o fornecedor em contrato.",
            result.HistoryComment);
    }

    [Fact]
    public void An_override_without_a_required_reason_still_reads_as_a_complete_sentence()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request(selected: "ADVANCE_INVOICE", confidence: 0.3m, justification: null));

        Assert.Equal(
            "Classificação do documento alterada de \"Factura\" para \"Factura de Adiantamento\".",
            result.HistoryComment);
        Assert.DoesNotContain("Justificativa", result.HistoryComment);
    }

    [Fact]
    public void The_justification_is_trimmed_before_it_is_measured_or_stored()
    {
        var padded = "   " + new string('x', DocumentClassificationOverrideRecorder.MinimumJustificationLength) + "   ";
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(justification: padded));

        Assert.True(result.ShouldRecord);
        Assert.Equal(padded.Trim(), result.TrimmedJustification);
    }

    [Fact]
    public void An_unrecognised_suggestion_source_is_dropped_rather_than_recorded_as_fact()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request() with { SuggestionSource = "GUESSWORK" });

        Assert.True(result.ShouldRecord);
        Assert.Null(result.NormalizedSuggestionSource);
    }

    [Theory]
    [InlineData("ocr", "OCR")]
    [InlineData("Fallback", "FALLBACK")]
    public void A_recognised_suggestion_source_is_normalized(string raw, string expected)
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request() with { SuggestionSource = raw });

        Assert.Equal(expected, result.NormalizedSuggestionSource);
    }
}
