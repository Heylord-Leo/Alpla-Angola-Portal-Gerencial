using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AlplaPortal.Domain.Common;

namespace AlplaPortal.Domain.Services;

/// <summary>One internal ALPLA legal entity, as recorded in the <c>Companies</c> table.</summary>
public sealed record InternalCompanyRef(int Id, string Name, string? Code, string? TaxId);

/// <summary>
/// Answers one question: <b>may this counterparty be used as the payable supplier of a PAYMENT
/// request?</b>
/// </summary>
///
/// <remarks>
/// <para><b>Why this exists.</b> A source document was read by OCR and the composer offered
/// <c>ALPLA ANGOLA PLASTICOS LDA.</c> as the supplier. The reading was not wrong — ALPLA was the
/// issuer and FIX4U the customer — but an ALPLA Angola legal entity can never be the entity the
/// Portal owes money to on an ordinary payment request. A document ALPLA issued to an external
/// customer is a sales-side document; it is evidence that somebody owes ALPLA, not the reverse.</para>
///
/// <para><b>The authoritative list is the <c>Companies</c> table.</b> It already holds the ALPLA
/// Angola legal entities with their <c>Code</c> and their fiscal number, and
/// <c>Company.TaxId</c> was documented from the start as the field used "to exclude internal NIFs
/// from supplier matching/creation". No second list is introduced here — every method takes the
/// company rows as an argument.</para>
///
/// <para><b>Identification, strongest first.</b> The fiscal number is the identifier that actually
/// means something: it is unique across companies, indexed, stored normalized, and cannot be
/// restyled the way a trade name can. Names are consulted only as a fallback, for the case the
/// reported defect turns on — a document that names the entity but whose NIF was not read.</para>
///
/// <para><b>This is financial-integrity policy, not a permission.</b> There is deliberately no
/// override parameter, no role check and no "force" flag. A System Administrator cannot be given a
/// way past it, because the rule is not about who is asking.</para>
///
/// <para>Pure and deterministic. No database, no clock, no culture dependence.</para>
/// </remarks>
public static class InternalCompanyPolicy
{
    /// <summary>Stable machine code for the typed business error.</summary>
    public const string ViolationCode = "PAYMENT_INTERNAL_COMPANY_AS_SUPPLIER";

    public const string SupplierMessage =
        "A empresa identificada como emitente pertence à ALPLA e não pode ser utilizada como " +
        "fornecedor em um pedido de pagamento. Verifique se o documento selecionado é o correto.";

    public const string CreationMessage =
        "Esta entidade pertence à ALPLA e não pode ser criada/utilizada como fornecedor para " +
        "pedidos de pagamento.";

    // ── Normalisation ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Upper-cased, accent-free, punctuation-free, single-spaced. Used only for name comparison.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var stripped = new string(
            name.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray()
        ).Normalize(NormalizationForm.FormC);

        // Punctuation becomes a separator rather than vanishing, so "ALPLA,LDA" does not fuse into
        // one token and slip past the word-boundary matching below.
        return Regex.Replace(Regex.Replace(stripped, "[^A-Z0-9]+", " "), @"\s+", " ").Trim();
    }

    // ── Identification ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The internal company owning this fiscal number, if any. The strongest signal available.
    /// </summary>
    public static InternalCompanyRef? MatchByTaxId(
        string? taxId, IEnumerable<InternalCompanyRef> companies)
    {
        var normalized = TaxIdNormalizer.Normalize(taxId);
        if (normalized.Length == 0) return null;

        return companies.FirstOrDefault(
            c => TaxIdNormalizer.Normalize(c.TaxId) == normalized);
    }

    /// <summary>
    /// The internal company this name denotes, if any — <b>fallback only</b>.
    /// </summary>
    ///
    /// <remarks>
    /// <para>Matches the registered company name and code exactly (<c>ALPLAPLASTICO</c>,
    /// <c>ALPLASOPRO</c>, <c>APA</c>, <c>APS</c>), plus the Angolan trade names those rows are
    /// registered under commercially — <c>ALPLA ANGOLA PLASTICOS LDA</c>,
    /// <c>ALPLA ANGOLA SOPRO LDA</c> — which is how the entities actually appear on a document and
    /// therefore how OCR reports them.</para>
    ///
    /// <para>Matching is on whole words, and requires <b>ALPLA and ANGOLA together</b> with the
    /// distinguishing term. That deliberately keeps the other ALPLA group companies in the supplier
    /// master out of scope: <c>ALPLA Hispaniola SRL</c>, <c>IBEROALPLA PORTUGAL LDA</c>,
    /// <c>BRASALPLA</c> and <c>ALPLA-Werke Alwin Lehner GmbH</c> are foreign entities, not ALPLA
    /// Angola, and this rule is about the Angolan legal entities the Portal itself bills as. A
    /// substring test would have caught <c>BRASALPLA</c> and blocked a legitimate supplier.</para>
    /// </remarks>
    public static InternalCompanyRef? MatchByAlias(
        string? name, IEnumerable<InternalCompanyRef> companies)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length == 0) return null;

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasAlpla = words.Contains("ALPLA");
        var hasAngola = words.Contains("ANGOLA");

        foreach (var company in companies)
        {
            var companyName = NormalizeName(company.Name);

            // The registered name, exactly. The company CODE is deliberately not matched: "APA" and
            // "APS" are three letters, and blocking any supplier that happens to be called that
            // would be a false positive on a rule that has no override.
            if (companyName.Length > 0 && normalized == companyName) return company;

            // "ALPLAPLASTICO" written as "ALPLA PLASTICO", and the Angolan trade name.
            var distinguishing = DistinguishingTerm(companyName);
            if (distinguishing == null) continue;

            var namesTheEntity = words.Any(w => w.StartsWith(distinguishing, StringComparison.Ordinal));
            if (!namesTheEntity) continue;

            if (hasAlpla && (hasAngola || words.Length <= 2)) return company;
        }

        return null;
    }

    /// <summary>
    /// The distinguishing part of an internal company's registered name — PLASTICO / SOPRO.
    /// </summary>
    private static string? DistinguishingTerm(string normalizedCompanyName)
    {
        if (normalizedCompanyName.Length == 0) return null;

        var withoutPrefix = normalizedCompanyName.StartsWith("ALPLA", StringComparison.Ordinal)
            ? normalizedCompanyName["ALPLA".Length..].Replace(" ", string.Empty)
            : normalizedCompanyName.Replace(" ", string.Empty);

        return withoutPrefix.Length >= 4 ? withoutPrefix : null;
    }

    /// <summary>
    /// The internal company this counterparty resolves to, by fiscal number then by name.
    /// </summary>
    public static InternalCompanyRef? Resolve(
        string? name, string? taxId, IEnumerable<InternalCompanyRef> companies)
    {
        var list = companies as IReadOnlyCollection<InternalCompanyRef> ?? companies.ToList();
        return MatchByTaxId(taxId, list) ?? MatchByAlias(name, list);
    }

    // ── The question everything else asks ────────────────────────────────────────────────────

    /// <summary>
    /// Whether this counterparty may be the payable supplier of a PAYMENT request.
    /// </summary>
    ///
    /// <remarks>
    /// <para>Note what is <b>not</b> here: any comparison against the request's own company. The
    /// rule is not "the supplier must differ from the buying company". A request raised by
    /// AlplaPLASTICO naming AlplaSOPRO as supplier is just as wrong as one naming itself — both are
    /// internal counterparties, and an ordinary payment request is not the instrument for money
    /// moving between two ALPLA entities. If intercompany payments are ever needed they deserve an
    /// explicit INTERCOMPANY workflow, not the accidental reuse of the supplier field.</para>
    /// </remarks>
    public static bool CanBePaymentSupplier(
        string? name, string? taxId, IEnumerable<InternalCompanyRef> companies)
        => Resolve(name, taxId, companies) == null;
}
