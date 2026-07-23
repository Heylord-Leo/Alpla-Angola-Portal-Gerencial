using System.Collections.Generic;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Pure unit tests for RequestGroupDisplayStateCalculator — no database, no controller. Mirrors
/// src/frontend/src/lib/requestGroupDisplayState.test.mjs's "resolveParentDisplayStatus" suite
/// exactly (same bucket statuses, same label strings, same resolution order). Update both sides
/// together.
/// </summary>
public class RequestGroupDisplayStateCalculatorTests
{
    [Fact]
    public void Request100Shape_PaymentScheduledPlusAdvancePaymentCompleted_ReturnsPaymentsInProgress()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PAYMENT_SCHEDULED", "ADVANCE_PAYMENT_COMPLETED" });

        Assert.Equal("PAYMENTS_IN_PROGRESS", result.DisplayStatusCode);
        Assert.Equal("Pagamentos em andamento", result.DisplayStatusName);
    }

    [Fact]
    public void SameExactCodeAcrossAllGroups_PaymentScheduled_ReturnsNoOverride()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PAYMENT_SCHEDULED", "PAYMENT_SCHEDULED" });

        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }

    [Fact]
    public void SameExactCodeAcrossAllGroups_Completed_ReturnsNoOverride()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "COMPLETED", "COMPLETED" });

        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }

    [Fact]
    public void DifferentCodes_SameScheduledBucket_ReturnsPagamentosAgendados()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PAYMENT_SCHEDULED", "ADVANCE_PAYMENT_SCHEDULED" });

        Assert.Equal("SCHEDULED", result.DisplayStatusCode);
        Assert.Equal("Pagamentos agendados", result.DisplayStatusName);
    }

    [Fact]
    public void DifferentCodes_SamePaidOrPostPaymentBucket_ReturnsPagamentosConcluidos()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PAYMENT_COMPLETED", "WAITING_RECEIPT" });

        Assert.Equal("PAID_OR_POST_PAYMENT", result.DisplayStatusCode);
        Assert.Equal("Pagamentos concluídos", result.DisplayStatusName);
    }

    [Fact]
    public void DifferentCodes_SameWaitingActionBucket_ReturnsAguardandoProcessamentoFinanceiro()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PO_ISSUED", "PAYMENT_REQUEST_SENT" });

        Assert.Equal("WAITING_ACTION", result.DisplayStatusCode);
        Assert.Equal("Aguardando processamento financeiro", result.DisplayStatusName);
    }

    [Fact]
    public void DifferentCodes_SameAdvancePaidBucket_ReturnsAdiantamentosRealizados()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "ADVANCE_PAYMENT_COMPLETED", "WAITING_SUPPLIER_DELIVERY" });

        Assert.Equal("ADVANCE_PAID", result.DisplayStatusCode);
        Assert.Equal("Adiantamentos realizados", result.DisplayStatusName);
    }

    [Fact]
    public void MixedBuckets_PaymentCompletedPlusPaymentScheduled_ReturnsPaymentsInProgress()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PAYMENT_COMPLETED", "PAYMENT_SCHEDULED" });

        Assert.Equal("PAYMENTS_IN_PROGRESS", result.DisplayStatusCode);
        Assert.Equal("Pagamentos em andamento", result.DisplayStatusName);
    }

    [Fact]
    public void CancelledGroupsExcluded_OnlyOneRealGroupRemains_ReturnsNoOverride()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "PAYMENT_SCHEDULED", "CANCELLED" });

        // Only one non-CANCELLED group -> single group, single code -> no override.
        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }

    [Fact]
    public void NoGroups_ReturnsNoOverride()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new List<string?>());

        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }

    [Fact]
    public void AllCancelled_ReturnsNoOverride()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "CANCELLED", "CANCELLED" });

        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }

    [Fact]
    public void UnrecognizedGroupStatus_ReturnsNoOverride_NeverGuesses()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(new[] { "SOME_FUTURE_STATUS" });

        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }

    [Fact]
    public void NullInput_DoesNotThrow_ReturnsNoOverride()
    {
        var result = RequestGroupDisplayStateCalculator.Resolve(null!);

        Assert.Null(result.DisplayStatusCode);
        Assert.Null(result.DisplayStatusName);
    }
}
