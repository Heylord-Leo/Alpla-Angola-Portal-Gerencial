using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Common;

/// <summary>
/// Canonical NIF/TaxId normalization shared across the domain (suppliers and internal companies):
/// keep only alphanumerics, uppercased. Removes spaces, dots, dashes and other separators so two
/// formats of the same NIF ("500-123.456" and "500123456") compare and index as equal.
/// </summary>
public static class TaxIdNormalizer
{
    public static string Normalize(string? taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId)) return string.Empty;
        return Regex.Replace(taxId.ToUpperInvariant(), "[^A-Z0-9]", "");
    }

    /// <summary>Normalized value, or null when empty — the canonical form to persist.</summary>
    public static string? NormalizeOrNull(string? taxId)
    {
        var n = Normalize(taxId);
        return n.Length == 0 ? null : n;
    }
}
