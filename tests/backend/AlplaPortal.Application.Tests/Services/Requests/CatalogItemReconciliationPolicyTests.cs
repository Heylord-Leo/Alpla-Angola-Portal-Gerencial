using AlplaPortal.Domain.Services;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The rule that decides whether two item descriptions name the same catalogue item.
/// </summary>
///
/// <remarks>
/// It was already load-bearing — the batch matcher used it to decide what the catalogue "already
/// knows" — but it lived privately inside a controller and had no tests. Multi-document PAYMENT gave
/// it a second job: recognising that two invoices in one request bill the same unknown item, so the
/// user is not walked through registering it twice. Both jobs fail the same way if the rule drifts.
/// </remarks>
public class CatalogItemReconciliationPolicyTests
{
    // ── Normalisation ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("  TRANSPORTE LOCAL  ", "transporte local")]
    [InlineData("Transporte  Local", "transporte local")]
    [InlineData("Serviço de instalação", "servico de instalacao")]
    [InlineData("MANUTENÇÃO PREVENTIVA.", "manutencao preventiva")]
    [InlineData("Item;", "item")]
    [InlineData("Cabo\tUTP\nCat6", "cabo utp cat6")]
    public void NormalizeDescription_AppliesEveryStep(string input, string expected)
    {
        Assert.Equal(expected, CatalogItemReconciliationPolicy.NormalizeDescription(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeDescription_TreatsBlankAsEmpty(string? input)
    {
        Assert.Equal(string.Empty, CatalogItemReconciliationPolicy.NormalizeDescription(input));
    }

    /// <summary>
    /// Punctuation is stripped only from the END. A code like "CAT.6" must stay one description.
    /// </summary>
    [Fact]
    public void NormalizeDescription_KeepsInteriorPunctuation()
    {
        Assert.Equal("cabo cat.6", CatalogItemReconciliationPolicy.NormalizeDescription("Cabo CAT.6"));
    }

    // ── Equivalence ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The exact Release 3 scenario: the same service billed on two invoices of one payment request,
    /// typed slightly differently on each. One resolution has to settle both.
    /// </summary>
    [Theory]
    [InlineData("TRANSPORTE LOCAL", "Transporte local")]
    [InlineData("TRANSPORTE LOCAL", "transporte  local.")]
    [InlineData("Serviço instalação", "SERVICO INSTALACAO")]
    public void AreEquivalent_MatchesTheSameItemWrittenDifferently(string left, string right)
    {
        Assert.True(CatalogItemReconciliationPolicy.AreEquivalent(left, right));
    }

    [Theory]
    [InlineData("TRANSPORTE LOCAL", "TRANSPORTE INTERNACIONAL")]
    [InlineData("Cabo UTP Cat6", "Cabo UTP Cat5")]
    public void AreEquivalent_DoesNotMatchDifferentItems(string left, string right)
    {
        Assert.False(CatalogItemReconciliationPolicy.AreEquivalent(left, right));
    }

    /// <summary>
    /// Two empty rows are not "the same item". Otherwise one resolution would claim all of them.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("", "TRANSPORTE LOCAL")]
    public void AreEquivalent_IsFalseWhenEitherSideSaysNothing(string? left, string? right)
    {
        Assert.False(CatalogItemReconciliationPolicy.AreEquivalent(left, right));
    }

    // ── Which lines still need answering ─────────────────────────────────────────────────────

    [Fact]
    public void RequiresReconciliation_IsTrueForADescribedLineWithNoCatalogueLink()
    {
        Assert.True(CatalogItemReconciliationPolicy.RequiresReconciliation("TRANSPORTE LOCAL", null));
    }

    /// <summary>A line already linked is settled, whatever its description says.</summary>
    [Fact]
    public void RequiresReconciliation_IsFalseWhenAlreadyLinked()
    {
        Assert.False(CatalogItemReconciliationPolicy.RequiresReconciliation("qualquer coisa", 42));
    }

    /// <summary>
    /// An empty row is not a line yet. Asking the user to reconcile nothing turns a guardrail into
    /// an obstacle, and an obstacle is what people learn to click past.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiresReconciliation_IsFalseForAnEmptyRow(string? description)
    {
        Assert.False(CatalogItemReconciliationPolicy.RequiresReconciliation(description, null));
    }
}
