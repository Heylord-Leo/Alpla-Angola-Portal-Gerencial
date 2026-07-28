namespace AlplaPortal.Application.Validation;

/// <summary>
/// Deterministic minimum-quality check for justification text (reconciliation SUBSTITUTE/
/// EXTRA_ITEM/value-bearing IGNORED justifications, and batch-composition EXCLUDE comments).
/// Mirrors the length rule already used for budget justification and cost-center reassignment
/// reason elsewhere in the codebase, plus two placeholder-detection heuristics — no AI/ML involved.
/// </summary>
public static class ReconciliationJustificationValidator
{
    /// <summary>Minimum length for a free-text justification.</summary>
    public const int MinLength = 20;

    /// <summary>Longest run of one repeated character, as a fraction of the trimmed text length,
    /// above which the text is treated as a placeholder (e.g. "aaaaaaaaaaaaaaaaaaaaa",
    /// "okkkkkkkkkkkkkkkkkkkkkk").</summary>
    private const double MaxRepeatedCharRatio = 0.6;

    public static bool IsValid(string? justification, out string errorMessage)
    {
        var trimmed = (justification ?? string.Empty).Trim();

        if (trimmed.Length < MinLength)
        {
            errorMessage = $"A justificativa deve ter pelo menos {MinLength} caracteres.";
            return false;
        }

        if (trimmed.All(char.IsDigit))
        {
            errorMessage = "A justificativa não pode conter apenas números.";
            return false;
        }

        if (HasExcessiveRepeatedCharacters(trimmed))
        {
            errorMessage = "A justificativa parece ser um texto de preenchimento (caracteres repetidos). Informe um motivo real.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool HasExcessiveRepeatedCharacters(string text)
    {
        var longestRun = 1;
        var currentRun = 1;
        for (var i = 1; i < text.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(text[i - 1]))
            {
                currentRun++;
                if (currentRun > longestRun) longestRun = currentRun;
            }
            else
            {
                currentRun = 1;
            }
        }

        return (double)longestRun / text.Length > MaxRepeatedCharRatio;
    }
}
