using System.Linq;
using AlplaPortal.Domain.Constants;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Adjustment V2 Phase 3 — the shared business label catalog. Guarantees the notification code
/// (and future timeline/approver read models) render friendly Portuguese labels, never raw reason
/// codes, and that every catalog code has a label. Pure unit tests — no database.
/// </summary>
public class AdjustmentEventLabelsTests
{
    [Fact]
    public void EveryReasonCode_HasAFriendlyLabel_ThatIsNotTheRawCode()
    {
        foreach (var code in AdjustmentConstants.ReasonCodes.All)
        {
            var label = AdjustmentEventLabels.ReasonLabel(code);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(code, label);       // must be a friendly label, never the enum string
            Assert.DoesNotContain("_", label);  // raw codes are SCREAMING_SNAKE_CASE
        }
    }

    [Theory]
    [InlineData(AdjustmentConstants.ReasonCodes.PriceNegotiation, "Preço / negociação")]
    [InlineData(AdjustmentConstants.ReasonCodes.NewQuotation, "Solicitar nova cotação")]
    [InlineData(AdjustmentConstants.ReasonCodes.Supplier, "Fornecedor")]
    [InlineData(AdjustmentConstants.ReasonCodes.SupplierDeliveryTime, "Prazo de entrega do fornecedor")]
    [InlineData(AdjustmentConstants.ReasonCodes.RequestedQuantity, "Quantidade solicitada")]
    [InlineData(AdjustmentConstants.ReasonCodes.RemoveRequestItem, "Remover item do pedido")]
    public void KeyReasonLabels_MatchTheApprovedDesign(string code, string expected)
    {
        Assert.Equal(expected, AdjustmentEventLabels.ReasonLabel(code));
    }

    [Fact]
    public void SourceStage_MapsToTheCorrectRequestedEventLabel()
    {
        Assert.Equal(AdjustmentEventLabels.RequestedAtArea, AdjustmentEventLabels.RequestedAt(AdjustmentConstants.SourceStages.Area));
        Assert.Equal(AdjustmentEventLabels.RequestedAtFinal, AdjustmentEventLabels.RequestedAt(AdjustmentConstants.SourceStages.Final));
        Assert.Equal("Reajuste solicitado na Aprovação de Área", AdjustmentEventLabels.RequestedAtArea);
        Assert.Equal("Reajuste solicitado na Aprovação Final", AdjustmentEventLabels.RequestedAtFinal);
    }

    [Fact]
    public void ActionRequiredLabels_ExistForBothActors()
    {
        Assert.Equal("Ação necessária do Solicitante", AdjustmentEventLabels.ActionRequiredRequester);
        Assert.Equal("Ação necessária do Comprador", AdjustmentEventLabels.ActionRequiredBuyer);
    }
}
