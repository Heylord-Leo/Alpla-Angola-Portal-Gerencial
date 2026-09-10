using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.242.0 — canonical supplier-name comparison for OCR PO validation. The single backend
/// counterpart of the frontend <c>ocrPoValidation.calculateSimilarity</c> / <c>normalizeForComparison</c>,
/// so the authoritative <c>RegisterPo</c> divergence check agrees with the modal instead of raising a
/// false "Fornecedor divergente" for accent/spacing/hyphen/punctuation-only differences that OCR
/// routinely introduces.
///
/// <para>Normalization (mirrors the frontend, in order): trim → invariant lowercase → Unicode NFD →
/// drop combining marks (diacritics) → NFC. Equality then strips ALL non-alphanumerics (compact
/// form); a token-overlap fallback compares alphanumeric tokens. NOT a fuzzy/edit-distance matcher —
/// genuinely different suppliers still diverge.</para>
/// </summary>
public static class SupplierNameComparer
{
    /// <summary>Match when similarity ≥ this (mirrors the frontend's &lt; 0.6 divergence threshold).</summary>
    public const double MatchThreshold = 0.6;

    /// <summary>Trim + invariant-lower + strip diacritics (NFD → drop NonSpacingMark → NFC).
    /// Mirrors frontend <c>normalizeForComparison</c> and reuses the same rule as
    /// <c>RequestsController.NormalizeForDedup</c>.</summary>
    public static string NormalizeForComparison(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lowered = s.Trim().ToLowerInvariant();
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>0..1 similarity, or null when the comparison is not meaningful (either side has no
    /// alphanumeric content). Exact compact match = 1.0; one contains the other = 0.9; otherwise the
    /// overlap of alphanumeric tokens (length &gt; 1) over the larger token set. Mirrors the frontend
    /// <c>calculateSimilarity</c> exactly.</summary>
    public static double? Similarity(string? a, string? b)
    {
        if (a is null || b is null) return null;
        var ta = a.Trim();
        var tb = b.Trim();
        if (ta.Length == 0 || tb.Length == 0) return null;

        var na = NormalizeForComparison(ta);
        var nb = NormalizeForComparison(tb);

        var s1 = Regex.Replace(na, "[^a-z0-9]", "");
        var s2 = Regex.Replace(nb, "[^a-z0-9]", "");
        if (s1.Length == 0 || s2.Length == 0) return null;
        if (s1 == s2) return 1.0;
        if (s1.Contains(s2) || s2.Contains(s1)) return 0.9;

        var t1 = Regex.Replace(na, "[^a-z0-9\\s]", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToArray();
        var t2 = Regex.Replace(nb, "[^a-z0-9\\s]", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToArray();
        if (t1.Length == 0 || t2.Length == 0) return 0.0;

        var set2 = new HashSet<string>(t2);
        var matched = t1.Count(t => set2.Contains(t));
        return (double)matched / Math.Max(t1.Length, t2.Length);
    }

    /// <summary>True when the two names are the same supplier (similarity ≥ threshold). An
    /// uncomparable result (null similarity — e.g. no alphanumeric content) is NOT a match; the
    /// caller decides whether to surface a divergence. The caller must first ensure it actually has
    /// an expected name — an ABSENT expected supplier is "unavailable", not a mismatch.</summary>
    public static bool NamesMatch(string? extracted, string? expected)
    {
        var sim = Similarity(extracted, expected);
        return sim.HasValue && sim.Value >= MatchThreshold;
    }
}
