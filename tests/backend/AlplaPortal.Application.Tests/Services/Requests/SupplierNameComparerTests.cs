using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.242.0 — the canonical backend supplier comparator that fixes the false "Fornecedor divergente"
/// on accent/spacing/hyphen/punctuation-only OCR differences. Uses the SAME logical vectors as the
/// frontend ocrPoValidation tests so the authoritative RegisterPo check and the modal agree.
/// </summary>
public class SupplierNameComparerTests
{
    // ── Equivalent (must MATCH) ─────────────────────────────────────────────────────────────
    [Theory]
    // The confirmed incident pair: accents dropped + spaces around the hyphen.
    [InlineData("KRONES ANGOLA-Representações, Comércio e Indústria", "KRONES ANGOLA - Representacoes, Comercio e Industria")]
    [InlineData("KRONES ANGOLA - Representacoes, Comercio e Industria", "KRONES ANGOLA-Representações, Comércio e Indústria")]
    // Punctuation-only differences.
    [InlineData("A.B.C., LDA", "ABC LDA")]
    // Pure case + trailing whitespace.
    [InlineData("  krones angola  ", "KRONES ANGOLA")]
    public void Equivalent_names_match(string extracted, string expected)
    {
        Assert.True(SupplierNameComparer.NamesMatch(extracted, expected));
        var sim = SupplierNameComparer.Similarity(extracted, expected);
        Assert.NotNull(sim);
        Assert.True(sim!.Value >= SupplierNameComparer.MatchThreshold);
    }

    // ── Different (must DIVERGE) ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("AFRI INDUS COMERCIAL (SU), LDA", "KRONES ANGOLA - Representacoes, Comercio e Industria")]
    // Similar but genuinely distinct (only one shared token → 1/2 = 0.5 < 0.6).
    [InlineData("KRONES PORTUGAL", "KRONES ANGOLA")]
    [InlineData("TECNOLOGIAS XYZ, LDA", "SERVICOS DEF, SA")]
    public void Different_names_diverge(string extracted, string expected)
    {
        Assert.False(SupplierNameComparer.NamesMatch(extracted, expected));
    }

    [Fact]
    public void Null_or_empty_is_not_a_match()
    {
        Assert.False(SupplierNameComparer.NamesMatch(null, "KRONES ANGOLA"));
        Assert.False(SupplierNameComparer.NamesMatch("KRONES ANGOLA", null));
        Assert.False(SupplierNameComparer.NamesMatch("", "KRONES ANGOLA"));
        Assert.Null(SupplierNameComparer.Similarity("KRONES", "   "));
        Assert.Null(SupplierNameComparer.Similarity("---", "KRONES")); // no alphanumeric content
    }

    [Fact]
    public void Normalization_strips_diacritics_and_lowercases()
    {
        Assert.Equal("comercio e industria", SupplierNameComparer.NormalizeForComparison("Comércio e Indústria"));
    }
}
