using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// The single rule for deciding whether two item descriptions name the same catalogue item, and
/// whether a line still needs reconciling at all.
/// </summary>
///
/// <remarks>
/// <para>This logic already existed — privately, inside <c>CatalogItemsController</c>, where the
/// batch-match endpoint used it to normalise both the incoming descriptions and the catalogue rows
/// before comparing them. It is lifted here unchanged so that <b>one</b> definition of "equivalent
/// description" serves every caller.</para>
///
/// <para>That mattered as soon as a PAYMENT request could carry several documents. Two invoices in
/// one request may both bill <c>TRANSPORTE LOCAL</c>; when the user resolves the first line, the
/// second must be recognised as the same thing so the catalogue does not grow a duplicate entry for
/// a name it already knows. The temptation is to compare the two descriptions with <c>==</c>, which
/// would treat <c>Transporte local.</c> and <c>TRANSPORTE  LOCAL</c> as different items. The rule
/// below is the one the automatic matcher already applies, so a line reused across documents is
/// matched on exactly the same terms as a line matched against the catalogue itself.</para>
///
/// <para>Pure and deterministic: no database, no clock, no culture dependence.</para>
/// </remarks>
public static class CatalogItemReconciliationPolicy
{
    /// <summary>
    /// Trim → lowercase → strip diacritics → collapse whitespace → drop trailing punctuation.
    /// </summary>
    ///
    /// <remarks>
    /// Applied identically to both sides of every comparison. Normalising only one side is the
    /// classic way these matchers quietly stop matching: the catalogue holds
    /// <c>"Serviço de instalação"</c> and the reading produces <c>"SERVICO DE INSTALACAO"</c>, and
    /// a half-normalised comparison declares an item unknown that the Portal has known for years.
    /// </remarks>
    public static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var normalized = description.Trim().ToLowerInvariant();

        normalized = new string(
            normalized.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray()
        ).Normalize(NormalizationForm.FormC);

        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = normalized.TrimEnd('.', ',', ';', ':', '!');

        return normalized;
    }

    /// <summary>
    /// Whether two descriptions name the same catalogue item under the matching rule above.
    /// </summary>
    ///
    /// <remarks>
    /// Two blank descriptions are <b>not</b> equivalent. An empty line has nothing to reconcile, and
    /// treating every empty line as "the same item" would let one resolution silently claim all of
    /// them.
    /// </remarks>
    public static bool AreEquivalent(string? left, string? right)
    {
        var a = NormalizeDescription(left);
        if (a.Length == 0) return false;

        return a == NormalizeDescription(right);
    }

    /// <summary>
    /// Whether a line still has to be resolved against the catalogue before the request may proceed.
    /// </summary>
    ///
    /// <remarks>
    /// A line already linked to a catalogue item is settled, whatever its description says. A line
    /// with no description is not a line yet — it is an empty row — and asking the user to reconcile
    /// nothing is how a guardrail becomes an obstacle.
    /// </remarks>
    public static bool RequiresReconciliation(string? description, int? itemCatalogId)
    {
        if (itemCatalogId.HasValue) return false;
        return NormalizeDescription(description).Length > 0;
    }
}
