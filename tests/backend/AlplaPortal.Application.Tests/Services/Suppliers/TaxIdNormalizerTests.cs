using AlplaPortal.Domain.Common;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// Canonical NIF/TaxId normalization shared by suppliers and internal companies.
/// Guarantees that two formats of the same fiscal number index and compare as equal.
/// </summary>
public class TaxIdNormalizerTests
{
    [Theory]
    [InlineData("5001-760.246", "5001760246")]
    [InlineData("5001 760 246", "5001760246")]
    [InlineData("  5001760246  ", "5001760246")]
    [InlineData("pt-500.999", "PT500999")]
    [InlineData("PT500999", "PT500999")]
    public void Normalize_StripsSeparators_UppercasesLetters(string input, string expected)
        => Assert.Equal(expected, TaxIdNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Normalize_EmptyOrSeparatorsOnly_ReturnsEmpty(string? input)
        => Assert.Equal(string.Empty, TaxIdNormalizer.Normalize(input));

    [Fact]
    public void NormalizeOrNull_Empty_ReturnsNull()
    {
        Assert.Null(TaxIdNormalizer.NormalizeOrNull("   "));
        Assert.Null(TaxIdNormalizer.NormalizeOrNull("."));
    }

    [Fact]
    public void NormalizeOrNull_Value_ReturnsNormalized()
        => Assert.Equal("5417567485", TaxIdNormalizer.NormalizeOrNull(" 5417-567.485 "));

    [Fact] // Both internal NIFs round-trip to their persisted (already-normalized) form
    public void Normalize_InternalCompanyNifs_AreStable()
    {
        Assert.Equal("5417567485", TaxIdNormalizer.Normalize("5417567485"));
        Assert.Equal("5001760246", TaxIdNormalizer.Normalize("5001760246"));
    }
}
