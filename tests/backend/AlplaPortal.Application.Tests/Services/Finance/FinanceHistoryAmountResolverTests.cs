using System;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Pure unit tests for FinanceHistoryAmountResolver — no database, no controller. Covers the three
/// resolution tiers (structured group comment, amount-only comment, request-level fallback for a
/// genuine transition with no parseable comment) plus the explicit non-goal: PAYMENT_COMPLETED
/// comments never yield a GroupId under the current schema (no RequestPoGroupId column on
/// RequestStatusHistory, and MarkAsPaid's comment never embeds one).
/// </summary>
public class FinanceHistoryAmountResolverTests
{
    private static readonly Guid NcrGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void StructuredGroupComment_ParsesAmountAndGroupId()
    {
        var comment = "[Grupo P.O.: NCR ANGOLA INFORMATICA, LDA | Moeda: AOA | Total: 70,341.42 | GroupId: 11111111-1111-1111-1111-111111111111] Pagamento agendado. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_SCHEDULED", null, null, 0m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Equal(NcrGroupId, result.GroupId);
        Assert.Equal("NCR ANGOLA INFORMATICA, LDA", result.SupplierName);
        Assert.Equal("AOA", result.CurrencyCode);
    }

    [Fact]
    public void MalformedStructuredComment_InvalidTotal_ReturnsNullAmount_NoException()
    {
        var comment = "[Grupo P.O.: NCR | Moeda: AOA | Total: not-a-number | GroupId: 11111111-1111-1111-1111-111111111111] Pagamento agendado.";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_SCHEDULED", 999m, null, 0m);

        Assert.Null(result.Amount);
        Assert.Equal(NcrGroupId, result.GroupId); // the GroupId segment is independently still valid
    }

    [Fact]
    public void MalformedStructuredComment_InvalidGroupId_ReturnsNullGroupId_NoException()
    {
        var comment = "[Grupo P.O.: NCR | Moeda: AOA | Total: 70,341.42 | GroupId: not-a-guid] Pagamento agendado.";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_SCHEDULED", null, null, 0m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Null(result.GroupId);
    }

    [Fact]
    public void MalformedStructuredComment_MissingClosingBracket_TreatedAsUnstructured_NoException()
    {
        // No exception, and since ActionTaken is a real transition code, falls through to tier 3.
        var comment = "[Grupo P.O.: NCR | Moeda: AOA | Total: 70,341.42 | GroupId: 11111111-1111-1111-1111-111111111111 Pagamento agendado.";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_SCHEDULED", 500m, null, 0m);

        Assert.Equal(500m, result.Amount);
        Assert.Null(result.GroupId);
    }

    [Fact]
    public void MontanteOnlyComment_PaymentCompleted_ParsesAmount_GroupIdRemainsNull()
    {
        // The exact comment MarkAsPaid writes — amount present, no GroupId anywhere in the text.
        var comment = "Pagamento realizado. Montante: 70341.42. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_COMPLETED", null, null, 0m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Null(result.GroupId);
    }

    [Fact]
    public void MontanteOnlyComment_NeverInfersGroupFromAmountMatching()
    {
        // Even when approvedTotalAmount/other context could coincidentally match, GroupId must
        // stay null — this resolver never guesses a group from an amount comparison.
        var comment = "Pagamento realizado. Montante: 70341.42. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_COMPLETED", 70341.42m, 70341.42m, 70341.42m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Null(result.GroupId);
    }

    [Fact]
    public void DocumentoAdicionado_NoAmountAnywhereInComment_ReturnsNull()
    {
        var comment = "Documento \"po_ncr.pdf\" (P.O) adicionado ao pedido por Test User.";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "DOCUMENTO ADICIONADO", 345480.42m, null, 0m);

        Assert.Null(result.Amount);
        Assert.Null(result.GroupId);
    }

    [Fact]
    public void DocumentoAdicionado_NeverFallsBackToRequestLevelAmount_EvenIfApproved()
    {
        // DOCUMENTO ADICIONADO always sets NewStatusId = PreviousStatusId (current status echoed) —
        // it is never treated as a genuine transition, regardless of the request's current status.
        var result = FinanceHistoryAmountResolver.Resolve(null, "DOCUMENTO ADICIONADO", 345480.42m, null, 0m);

        Assert.Null(result.Amount);
    }

    [Fact]
    public void GenericObservation_NotaFinanceira_ReturnsNull()
    {
        var comment = "Nota de Finanças: Aguardando confirmação do fornecedor.";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "NOTA_FINANCEIRA", 345480.42m, null, 0m);

        Assert.Null(result.Amount);
    }

    [Fact]
    public void FinanceReturnAdjustment_ReturnsNull()
    {
        var result = FinanceHistoryAmountResolver.Resolve("Devolvido por Finanças para ajuste: falta assinatura.", "FINANCE_RETURN_ADJUSTMENT", 345480.42m, null, 0m);

        Assert.Null(result.Amount);
    }

    [Fact]
    public void PaymentDivergenceDetected_CommentHasNoMontantePrefix_ReturnsNull()
    {
        // Real comment shape from FinanceController: "Montante Esperado=X, Montante Pago=Y" — does
        // not match the "Montante: <value>" pattern (no colon immediately after "Montante"), so it
        // correctly falls through rather than being misread as a MarkAsPaid-style comment.
        var comment = "[SISTEMA] Pagamento realizado acima do valor aprovado (Grupo P.O.). Montante Esperado=1000.00, Montante Pago=1200.00, Diferença=200.00 (20.00%).";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_DIVERGENCE_DETECTED", 1000m, null, 0m);

        Assert.Null(result.Amount);
    }

    [Fact]
    public void RequestLevelFallback_GenuineTransition_NoParseableComment_UsesApprovedTotalAmount()
    {
        // A real PAYMENT_COMPLETED-family transition whose comment doesn't parse under tiers 1/2
        // (e.g. legacy data) still deserves a real number rather than nothing.
        var result = FinanceHistoryAmountResolver.Resolve("Some other comment with no amount.", "PAYMENT_COMPLETED", 345480.42m, 999m, 111m);

        Assert.Equal(345480.42m, result.Amount);
        Assert.Null(result.GroupId);
    }

    [Fact]
    public void RequestLevelFallback_ApprovedTotalAmountZeroOrAbsent_FallsBackToSelectedQuotation()
    {
        var result = FinanceHistoryAmountResolver.Resolve(null, "PAYMENT_SCHEDULED", 0m, 999m, 111m);

        Assert.Equal(999m, result.Amount);
    }

    [Fact]
    public void RequestLevelFallback_BothAbsent_FallsBackToEstimatedTotalAmount()
    {
        var result = FinanceHistoryAmountResolver.Resolve(null, "PAYMENT_COMPLETED", null, null, 111m);

        Assert.Equal(111m, result.Amount);
    }

    [Fact]
    public void NonTransitionActionTaken_NoComment_NeverFallsBackToRequestLevelAmount()
    {
        // Guards against the exact risk this resolver was designed to avoid: NewStatus.Code can
        // coincidentally equal a finance status for a non-transition row (DOCUMENTO
        // ADICIONADO/NOTA_FINANCEIRA always echo the request's current status as NewStatusId), but
        // gating tier 3 on ActionTaken (not NewStatus.Code) means it must never fire here.
        var result = FinanceHistoryAmountResolver.Resolve(null, "DOCUMENTO ADICIONADO", 345480.42m, null, 0m);
        Assert.Null(result.Amount);
    }

    [Fact]
    public void EmptyComment_NullActionTaken_ReturnsEmptyResolution_NoException()
    {
        var result = FinanceHistoryAmountResolver.Resolve(string.Empty, null, null, null, 0m);
        Assert.Null(result.Amount);
        Assert.Null(result.GroupId);
    }

    // ── New "Lote #n" structured format (SchedulePayment/MarkAsPaid/ConfirmAdvancePayment going forward) ──

    [Fact]
    public void NewLoteFormat_ScheduleComment_WithBatch_ParsesAmountAndLoteNumber()
    {
        var comment = "[Lote #2 | NCR ANGOLA INFORMATICA, LDA | Moeda: AOA | Total: 70,341.42] Pagamento agendado. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_SCHEDULED", null, null, 0m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Equal(2, result.LoteNumber);
        Assert.Equal("NCR ANGOLA INFORMATICA, LDA", result.SupplierName);
        Assert.Equal("AOA", result.CurrencyCode);
        Assert.Null(result.GroupId); // never a GUID in the new format
    }

    [Fact]
    public void NewLoteFormat_AdvancePaymentComment_WithBatch_ParsesAmountAndLoteNumber()
    {
        var comment = "[Lote #1 | ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento de 100% realizado. Pago em 24/07/2026. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "ADVANCE_PAYMENT_COMPLETED", null, null, 0m);

        Assert.Equal(275139.00m, result.Amount);
        Assert.Equal(1, result.LoteNumber);
        Assert.Equal("ITEC LDA", result.SupplierName);
    }

    [Fact]
    public void NewLoteFormat_BatchlessGroup_OmitsLoteSegment_StillParsesAmount()
    {
        // No ApprovalBatch -> the writer omits the "Lote #n | " segment entirely (never "Lote #0").
        var comment = "[ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento de 30% realizado. Pago em 24/07/2026. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "ADVANCE_PAYMENT_COMPLETED", null, null, 0m);

        Assert.Equal(275139.00m, result.Amount);
        Assert.Null(result.LoteNumber);
        Assert.Equal("ITEC LDA", result.SupplierName);
    }

    [Fact]
    public void NewLoteFormat_MalformedLoteNumber_DoesNotThrow_StillAttemptsAmount()
    {
        // "Lote #" without digits doesn't match the lote sub-pattern at all, so the whole prefix
        // falls through to the next tier rather than partially matching — still must not throw.
        var comment = "[Lote #abc | ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento realizado.";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "ADVANCE_PAYMENT_COMPLETED", null, null, 0m);

        Assert.Null(result.LoteNumber);
    }

    [Fact]
    public void LegacyGroupIdFormat_StillParses_GuidNeverExposedAsLoteNumber()
    {
        var comment = "[Grupo P.O.: NCR ANGOLA INFORMATICA, LDA | Moeda: AOA | Total: 70,341.42 | GroupId: 11111111-1111-1111-1111-111111111111] Pagamento agendado. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_SCHEDULED", null, null, 0m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Equal(NcrGroupId, result.GroupId);
        Assert.Null(result.LoteNumber);
    }

    [Fact]
    public void OldPlainMontanteFormat_StillParses_NoLoteOrGroupId()
    {
        var comment = "Pagamento realizado. Montante: 70341.42. ";

        var result = FinanceHistoryAmountResolver.Resolve(comment, "PAYMENT_COMPLETED", null, null, 0m);

        Assert.Equal(70341.42m, result.Amount);
        Assert.Null(result.GroupId);
        Assert.Null(result.LoteNumber);
    }

    [Fact]
    public void AdvancePaymentCompleted_NoParseableComment_FallsBackToRequestLevelAmount()
    {
        // Tier-4 fallback must recognize ADVANCE_PAYMENT_COMPLETED as a genuine transition too.
        var result = FinanceHistoryAmountResolver.Resolve("some unrelated comment", "ADVANCE_PAYMENT_COMPLETED", 345480.42m, null, 0m);

        Assert.Equal(345480.42m, result.Amount);
    }
}
