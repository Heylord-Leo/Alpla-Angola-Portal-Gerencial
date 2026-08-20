using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Services;

/// <summary>One positively identified Primavera purchase-order reference.</summary>
public sealed record PrimaveraPoParse(string Family, int Year, int Sequence)
{
    /// <summary>User-facing form, e.g. "ECF11 2026/421".</summary>
    public string Display => $"{Family} {Year}/{Sequence}";

    /// <summary>Canonical identity, e.g. "ECF11-2026-421". What duplicate detection compares.</summary>
    public string Canonical => $"{Family}-{Year}-{Sequence}";
}

public enum PoDuplicateVerdict
{
    None = 0,
    /// <summary>Same canonical identity within the SAME legal entity — the real duplicate.</summary>
    Block = 1,
    /// <summary>Same canonical identity in ANOTHER legal entity: Primavera sequences are
    /// per-company, so this is informational only — never an override.</summary>
    CrossCompanyInfo = 2
}

public sealed record PoDuplicateMatch(
    PoDuplicateVerdict Verdict, Guid GroupId, string? StoredPoNumber, string? RequestNumber);

/// <summary>
/// Deterministic recognition of ALPLA Angola's three Primavera purchase-order families:
/// <c>ECF</c> (stock material), <c>ECF10</c> (miscellaneous/office materials) and <c>ECF11</c>
/// (services). The reference lives in the document TITLE ("PO Serviços ECF11 2026/421",
/// "Encomenda Mat Escritório/Diversos ECF10 2026/219", "Encomenda a Fornecedor ECF 2026/107"),
/// which is exactly why a generic "find the document number" OCR strategy kept landing on the
/// most prominent numeric field instead — the supplier's NIF.
///
/// <para>Rules: positive identification first (family + year + sequence, longest family wins so
/// ECF11 is never read as ECF); NIF-shape rejection is only a secondary backstop. Pure and
/// deterministic — no OCR calls, no fuzzy matching.</para>
/// </summary>
public static class PrimaveraPoReference
{
    /// <summary>
    /// The reference grammar. Alternation is ordered longest-first (ECF11 | ECF10 | ECF) so the
    /// family is never truncated; separators tolerate OCR spacing and '/' vs '-'. The year is
    /// anchored to 20xx so an article code can never masquerade as a reference.
    /// </summary>
    private static readonly Regex Reference = new(
        @"\b(?<family>ECF11|ECF10|ECF)\b[\s.:]*(?<year>20\d{2})\s*[/\-]\s*(?<seq>\d{1,6})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A family word appearing at all — used to warn when a family is visible but no
    /// complete reference parsed, so the UI never silently populates a guess.</summary>
    private static readonly Regex FamilyWord = new(
        @"\bECF(?:11|10)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Human labels for the three families, for review UI and reports.</summary>
    public static readonly IReadOnlyDictionary<string, string> FamilyLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ECF"] = "Material de stock",
            ["ECF10"] = "Material diverso / escritório",
            ["ECF11"] = "Serviços"
        };

    /// <summary>First positively identified reference in the text, or null. Title prefixes
    /// ("PO Serviços …", "Encomenda …") are ignored naturally — only the grammar matters.</summary>
    public static PrimaveraPoParse? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = Reference.Match(text);
        if (!match.Success) return null;

        return new PrimaveraPoParse(
            match.Groups["family"].Value.ToUpperInvariant(),
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["seq"].Value));
    }

    /// <summary>Every distinct reference in the text, for audits of multi-reference documents.</summary>
    public static IReadOnlyList<PrimaveraPoParse> ParseAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<PrimaveraPoParse>();

        return Reference.Matches(text)
            .Select(m => new PrimaveraPoParse(
                m.Groups["family"].Value.ToUpperInvariant(),
                int.Parse(m.Groups["year"].Value),
                int.Parse(m.Groups["seq"].Value)))
            .DistinctBy(p => p.Canonical)
            .ToList();
    }

    /// <summary>A family word is visible but no complete reference parses — warn, never guess.</summary>
    public static bool MentionsFamilyWithoutReference(string? text) =>
        !string.IsNullOrWhiteSpace(text) && FamilyWord.IsMatch(text) && TryParse(text) == null;

    /// <summary>
    /// Conservative normalization for NON-Primavera reference values (FT/FP/FA/PP/FTC…):
    /// trim, uppercase, collapse whitespace runs. Deliberately nothing cleverer — no family is
    /// ever invented for a family-less historical value like "2026/107".
    /// </summary>
    public static string NormalizeLoose(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
            if (pendingSpace && builder.Length > 0) builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }

    /// <summary>Digits-only view, for NIF comparisons.</summary>
    private static string DigitsOf(string? value) =>
        new(value?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());

    /// <summary>Angolan fiscal numbers are 10 digits. Shape check only — a backstop, never the rule.</summary>
    public static bool LooksLikeNif(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return trimmed.All(char.IsDigit) && trimmed.Length == 10;
    }

    /// <summary>
    /// A value that must NEVER be accepted as a purchase-order number: equal to any known fiscal
    /// number (the supplier's, or an ALPLA legal entity's), or bare NIF-shaped with no positive
    /// Primavera parse. Positive identification always wins: a real reference never trips this.
    /// </summary>
    public static bool IsForbiddenPoNumber(string? value, IEnumerable<string?> knownNifs)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (TryParse(value) != null) return false;              // a positive reference is never forbidden

        var digits = DigitsOf(value);
        if (digits.Length == 0) return false;

        if (knownNifs.Any(n => !string.IsNullOrWhiteSpace(n) &&
                               digits == DigitsOf(n) && digits.Length >= 8))
            return true;

        return LooksLikeNif(value);
    }

    /// <summary>
    /// Duplicate evaluation over existing stored PO values.
    ///
    /// <para>Primavera references compare by CANONICAL identity, scoped to the legal entity:
    /// the same canonical in the same company blocks (existing override mechanism applies);
    /// the same canonical in another company is informational only — the companies are separate
    /// legal entities with independent Primavera sequences. Non-Primavera values keep the
    /// pre-existing global scope, upgraded from raw equality to conservative normalization.
    /// Primavera and family-less values never cross-match — a family is never invented.</para>
    /// </summary>
    public static PoDuplicateMatch Evaluate(
        string? candidateValue,
        int candidateCompanyId,
        IEnumerable<(Guid GroupId, string? StoredPoNumber, int CompanyId, string? RequestNumber)> existing)
    {
        if (string.IsNullOrWhiteSpace(candidateValue))
            return new PoDuplicateMatch(PoDuplicateVerdict.None, Guid.Empty, null, null);

        var candidateParse = TryParse(candidateValue);
        var candidateLoose = NormalizeLoose(candidateValue);

        PoDuplicateMatch? crossCompany = null;

        foreach (var row in existing)
        {
            if (string.IsNullOrWhiteSpace(row.StoredPoNumber)) continue;

            if (candidateParse != null)
            {
                var storedParse = TryParse(row.StoredPoNumber);
                if (storedParse == null) continue;                       // never invent a family
                if (!string.Equals(storedParse.Canonical, candidateParse.Canonical,
                        StringComparison.Ordinal)) continue;

                if (row.CompanyId == candidateCompanyId)
                    return new PoDuplicateMatch(PoDuplicateVerdict.Block,
                        row.GroupId, row.StoredPoNumber, row.RequestNumber);

                crossCompany ??= new PoDuplicateMatch(PoDuplicateVerdict.CrossCompanyInfo,
                    row.GroupId, row.StoredPoNumber, row.RequestNumber);
            }
            else
            {
                if (!string.Equals(NormalizeLoose(row.StoredPoNumber), candidateLoose,
                        StringComparison.Ordinal)) continue;

                // Pre-existing behavior preserved: non-Primavera references block globally.
                return new PoDuplicateMatch(PoDuplicateVerdict.Block,
                    row.GroupId, row.StoredPoNumber, row.RequestNumber);
            }
        }

        return crossCompany ?? new PoDuplicateMatch(PoDuplicateVerdict.None, Guid.Empty, null, null);
    }
}
