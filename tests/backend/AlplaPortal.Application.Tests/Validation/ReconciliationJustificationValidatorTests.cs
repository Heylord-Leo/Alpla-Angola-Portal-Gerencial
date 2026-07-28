using AlplaPortal.Application.Validation;
using Xunit;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// Deterministic justification-quality gate for SUBSTITUTE/EXTRA_ITEM/value-bearing IGNORED
/// reconciliation justifications and EXCLUDE batch-composition comments. Test cases mirror the
/// literal placeholder text found in request REQ-15/07/2026-075's own data during investigation
/// ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "okkkkkkkkkkkkkkkkkkkkkk", "233333333333333333333"),
/// which motivated adding this validator.
/// </summary>
public class ReconciliationJustificationValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("curto")]
    [InlineData("dezenove caract.")] // 16 chars, below the 20-char minimum
    public void IsValid_TooShort_ReturnsFalse(string? text)
    {
        Assert.False(ReconciliationJustificationValidator.IsValid(text, out var error));
        Assert.Contains("20", error);
    }

    [Theory]
    [InlineData("233333333333333333333")]
    [InlineData("00000000000000000000000")]
    [InlineData("12345678901234567890")]
    public void IsValid_NumericOnly_AtOrAboveMinLength_ReturnsFalseWithNumericError(string text)
    {
        Assert.False(ReconciliationJustificationValidator.IsValid(text, out var error));
        Assert.Contains("números", error);
    }

    [Fact]
    public void IsValid_NumericOnly_BelowMinLength_ReturnsFalseWithLengthErrorNotNumericError()
    {
        // Short numeric strings are already caught by the length check — the length message wins.
        Assert.False(ReconciliationJustificationValidator.IsValid("1111111111", out var error));
        Assert.Contains("20", error);
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("okkkkkkkkkkkkkkkkkkkkkk")]
    [InlineData("XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX")]
    public void IsValid_RepeatedCharacterPlaceholder_ReturnsFalse(string text)
    {
        Assert.False(ReconciliationJustificationValidator.IsValid(text, out var error));
        Assert.Contains("preenchimento", error);
    }

    [Theory]
    [InlineData("Fornecedor não trabalha mais com este item específico do documento.")]
    [InlineData("Item substituído por especificação equivalente disponível em estoque do fornecedor.")]
    [InlineData("Cliente solicitou remoção desta linha após revisão orçamental do departamento.")]
    public void IsValid_MeaningfulText_ReturnsTrue(string text)
    {
        Assert.True(ReconciliationJustificationValidator.IsValid(text, out var error));
        Assert.Empty(error);
    }

    [Fact]
    public void IsValid_MeaningfulTextAtMinLength_ReturnsTrue()
    {
        var realistic = "Motivo real do comprador"; // 25 chars, meaningful, no repeated-character run
        Assert.True(ReconciliationJustificationValidator.IsValid(realistic, out _));
    }
}
