using System;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// <c>OTHER</c> and <c>UNCLASSIFIED</c> are the extraction declining to classify, not an opinion.
///
/// <para>Naming a concrete type after that <b>supplies</b> the missing classification; it does not
/// contradict one. Treating it as an override asked the user to tick an acknowledgement and write
/// twenty characters of justification for answering a question the system itself had asked.</para>
///
/// <para>The decision is still recorded — under its own event key, so "how often did people disagree
/// with the reading" stays a question with an honest answer.</para>
/// </summary>
public class IndeterminateClassificationTests
{
    private static readonly Guid Scope = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Attachment = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DocumentClassificationOverrideRequest Request(
        string? suggested, string selected, bool acknowledged = false,
        string? justification = null, decimal? confidence = null) => new()
    {
        Context = RequestConstants.DocumentClassificationContexts.PaymentRequest,
        ScopeId = Scope,
        AttachmentId = Attachment,
        SuggestedType = suggested,
        SelectedType = selected,
        Confidence = confidence,
        Acknowledged = acknowledged,
        Justification = justification
    };

    // ── Indeterminate suggestions are never conflicts ───────────────────────────────────────

    [Theory]
    [InlineData("OTHER", "INVOICE")]
    [InlineData("OTHER", "PROFORMA")]
    [InlineData("UNCLASSIFIED", "INVOICE")]
    [InlineData("UNCLASSIFIED", "ADVANCE_INVOICE")]
    public void UserClassification_AfterIndeterminateReading_IsNotAnOverride(
        string suggested, string selected)
    {
        // No acknowledgement, no justification — deliberately.
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(suggested, selected));

        Assert.Null(result.RejectionReason);
        Assert.True(result.ShouldRecord);
        Assert.Equal(DocumentClassificationDecisionKind.UserClassifiedIndeterminate, result.Kind);
        Assert.Null(result.TrimmedJustification);
    }

    [Theory]
    [InlineData("OTHER", "INVOICE")]
    [InlineData("UNCLASSIFIED", "INVOICE")]
    [InlineData(null, "INVOICE")]
    public void FillingAGapInTheReading_NeverRequiresJustification(
        string? suggested, string selected)
    {
        Assert.False(DocumentClassificationOverrideRecorder.RequiresJustification(
            suggested, selected, confidence: 0.99m));
    }

    [Fact]
    public void UserClassification_AfterIndeterminateReading_ExplainsItselfInHistory()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request("OTHER", "INVOICE"));

        Assert.Contains("definida como", result.HistoryComment);
        Assert.Contains("pelo utilizador", result.HistoryComment);
        Assert.Contains("não identificou um tipo específico", result.HistoryComment);
        // It must not read as a contradiction of something.
        Assert.DoesNotContain("alterada de", result.HistoryComment);
    }

    // ── Its own event identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void UserClassification_UsesADistinctEventKey_FromAnOverride()
    {
        var indeterminate = DocumentClassificationOverrideRecorder.Evaluate(Request("OTHER", "INVOICE"));
        var overridden = DocumentClassificationOverrideRecorder.Evaluate(
            Request("PROFORMA", "INVOICE", acknowledged: true));

        Assert.StartsWith("DC_SET:", indeterminate.IdempotencyKey);
        Assert.StartsWith("DC_OVERRIDE:", overridden.IdempotencyKey);
        Assert.NotEqual(indeterminate.IdempotencyKey, overridden.IdempotencyKey);
    }

    [Fact]
    public void RepeatedSaves_OfTheSameDecision_ProduceTheSameKey()
    {
        // Idempotency is what keeps an edited draft from accumulating one row per save.
        var first = DocumentClassificationOverrideRecorder.Evaluate(Request("OTHER", "INVOICE"));
        var second = DocumentClassificationOverrideRecorder.Evaluate(Request("OTHER", "INVOICE"));

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void ChangingTheSelection_IsADifferentDecision_AndADifferentKey()
    {
        var invoice = DocumentClassificationOverrideRecorder.Evaluate(Request("OTHER", "INVOICE"));
        var proforma = DocumentClassificationOverrideRecorder.Evaluate(Request("OTHER", "PROFORMA"));

        Assert.NotEqual(invoice.IdempotencyKey, proforma.IdempotencyKey);
    }

    // ── Real contradictions are untouched ───────────────────────────────────────────────────

    [Theory]
    [InlineData("INVOICE", "PROFORMA")]
    [InlineData("PROFORMA", "INVOICE")]
    public void ContradictingASpecificReading_StillRequiresAcknowledgement(
        string suggested, string selected)
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(suggested, selected));

        Assert.False(result.ShouldRecord);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void ChoosingNonFiscalOverFiscal_StillDemandsAWrittenReason()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(
            Request("INVOICE", "PROFORMA", acknowledged: true, justification: "curto"));

        Assert.False(result.ShouldRecord);
        Assert.NotNull(result.RejectionReason);
        Assert.Contains("justificar", result.RejectionReason!);
    }

    [Fact]
    public void AConfirmedAndJustifiedContradiction_IsRecordedAsAnOverride()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(
            "INVOICE", "PROFORMA", acknowledged: true,
            justification: "O fornecedor emitiu a factura por engano; o documento pago é a proforma."));

        Assert.True(result.ShouldRecord);
        Assert.Equal(DocumentClassificationDecisionKind.Override, result.Kind);
        Assert.NotNull(result.TrimmedJustification);
    }

    /// <summary>
    /// A document nobody read is ordinary data entry, and stays unaudited.
    ///
    /// <para>Distinct from OTHER/UNCLASSIFIED, where an extraction did run and failed to decide.
    /// Recording every manually typed classification would bury the decisions that matter.</para>
    /// </summary>
    [Fact]
    public void ClassifyingADocumentNothingWasSuggestedFor_RecordsNothing()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request(null, "INVOICE"));

        Assert.False(result.ShouldRecord);
        Assert.Null(result.RejectionReason);
        Assert.Equal(DocumentClassificationDecisionKind.None, result.Kind);
    }

    [Fact]
    public void AgreeingWithTheReading_RecordsNothing()
    {
        var result = DocumentClassificationOverrideRecorder.Evaluate(Request("INVOICE", "INVOICE"));

        Assert.False(result.ShouldRecord);
        Assert.Null(result.RejectionReason);
        Assert.Equal(DocumentClassificationDecisionKind.None, result.Kind);
    }

    // ── The predicate the rest of the rules rest on ─────────────────────────────────────────

    [Theory]
    [InlineData("INVOICE", true)]
    [InlineData("PROFORMA", true)]
    [InlineData("OTHER", false)]
    [InlineData("UNCLASSIFIED", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAuthoritative_TreatsOtherAndUnclassifiedAsNoOpinion(string? suggested, bool expected)
    {
        Assert.Equal(expected, DocumentClassificationOverrideRecorder.IsAuthoritative(suggested));
    }
}
