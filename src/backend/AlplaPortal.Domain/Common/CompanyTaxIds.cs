using System.Collections.Generic;

namespace AlplaPortal.Domain.Common;

/// <summary>
/// Single, centralized source for a company's fiscal NIF used on outbound documents (e.g. the Buyer
/// "Solicitar cotação" email). The authoritative value is Company.TaxId; this helper only supplies a
/// known-good fallback for the two ALPLA Angola companies when that column is empty, so no
/// company-name conditionals get scattered through the frontend. Never guesses beyond these.
/// </summary>
public static class CompanyTaxIds
{
    private static readonly IReadOnlyDictionary<string, string> KnownByName = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        ["AlplaPLASTICO"] = "5417567485",
        ["AlplaSOPRO"] = "5001760246",
    };

    /// <summary>Prefer the persisted Company.TaxId; fall back to the known ALPLA Angola NIF by name.</summary>
    public static string? Resolve(string? companyName, string? taxId)
    {
        if (!string.IsNullOrWhiteSpace(taxId)) return taxId;
        if (!string.IsNullOrWhiteSpace(companyName) && KnownByName.TryGetValue(companyName.Trim(), out var known)) return known;
        return null;
    }
}
