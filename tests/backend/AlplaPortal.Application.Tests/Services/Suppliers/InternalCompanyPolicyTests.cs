using AlplaPortal.Domain.Services;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// An ALPLA Angola legal entity may never be the payable supplier of a PAYMENT request.
/// </summary>
///
/// <remarks>
/// The defect that produced this: a source document was read, ALPLA was the issuer and FIX4U the
/// customer, and the composer offered <c>ALPLA ANGOLA PLASTICOS LDA.</c> as the supplier. A document
/// ALPLA issued to an external customer is a sales-side document — evidence that somebody owes
/// ALPLA, not the reverse — so it cannot originate a payment at all.
/// </remarks>
public class InternalCompanyPolicyTests
{
    /// <summary>
    /// The authoritative rows, exactly as seeded in the <c>Companies</c> table.
    /// </summary>
    private static readonly InternalCompanyRef[] Companies =
    {
        new(1, "AlplaPLASTICO", "APA", "5417567485"),
        new(2, "AlplaSOPRO",    "APS", "5001760246")
    };

    private static bool CanPay(string? name, string? taxId)
        => InternalCompanyPolicy.CanBePaymentSupplier(name, taxId, Companies);

    // ── The two internal entities ────────────────────────────────────────────────────────────

    [Fact]
    public void AlplaPlastico_IsRejectedAsPaymentSupplier()
    {
        Assert.False(CanPay("ALPLA ANGOLA PLASTICOS LDA.", "5417567485"));
    }

    [Fact]
    public void AlplaSopro_IsRejectedAsPaymentSupplier()
    {
        Assert.False(CanPay("ALPLA ANGOLA SOPRO, LDA", "5001760246"));
    }

    [Fact]
    public void ResolveNamesTheEntityThatWasMatched()
    {
        var resolved = InternalCompanyPolicy.Resolve("qualquer nome", "5001760246", Companies);

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved!.Id);
        Assert.Equal("AlplaSOPRO", resolved.Name);
    }

    // ── Genuine third parties still work ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("FIX4U - Comercio e Industria, Lda", "5000123456")]
    [InlineData("Sonangol Distribuidora", "5410001234")]
    [InlineData("Um fornecedor sem NIF", null)]
    public void ExternalSupplier_IsAccepted(string name, string? taxId)
    {
        Assert.True(CanPay(name, taxId));
    }

    /// <summary>
    /// Other ALPLA group companies are foreign entities, not ALPLA Angola, and are legitimately in
    /// the supplier master. A substring test for "ALPLA" would have blocked every one of them —
    /// BRASALPLA most embarrassingly, since the letters appear in the middle of the word.
    /// </summary>
    [Theory]
    [InlineData("ALPLA Hispaniola, SRL")]
    [InlineData("IBEROALPLA PORTUGAL, LDA")]
    [InlineData("BRASALPLA")]
    [InlineData("ALPLA-Werke Alwin Lehner GmbH & Co KG")]
    [InlineData("ALPLA TABA Plastics S.A.E")]
    [InlineData("ALPLA TRADING SA (PTY) LTD")]
    [InlineData("ALPLA MEXICO SA")]
    public void OtherAlplaGroupCompanies_AreNotAngolanInternalEntities(string name)
    {
        Assert.True(CanPay(name, null));
    }

    // ── Identification by the strongest available signal ─────────────────────────────────────

    /// <summary>
    /// The NIF wins over the name. A supplier row calling itself anything at all is still the
    /// internal entity if it carries the internal entity's fiscal number.
    /// </summary>
    [Theory]
    [InlineData("Fornecedor Qualquer Lda")]
    [InlineData("FIX4U - Comercio e Industria, Lda")]
    public void NameVariation_WithInternalNif_IsRejected(string name)
    {
        Assert.False(CanPay(name, "5417567485"));
    }

    [Theory]
    [InlineData("5417567485")]
    [InlineData("541.756.7485")]
    [InlineData("  5417-567-485 ")]
    [InlineData("5417 567 485")]
    public void InternalNif_IsRecognisedInAnyFormatting(string taxId)
    {
        Assert.False(CanPay("Seja qual for o nome", taxId));
    }

    /// <summary>
    /// The case the reported defect actually turns on: the document names the entity, but its
    /// fiscal number was never read. Checking the NIF alone let this straight through.
    /// </summary>
    [Theory]
    [InlineData("ALPLA ANGOLA PLASTICOS LDA.")]
    [InlineData("Alpla Angola Plásticos, Lda")]
    [InlineData("ALPLA  ANGOLA   PLASTICOS")]
    [InlineData("ALPLA ANGOLA SOPRO LDA")]
    [InlineData("AlplaPLASTICO")]
    [InlineData("ALPLA PLASTICO")]
    [InlineData("ALPLA SOPRO")]
    public void InternalAlias_WithoutMatchingNif_IsRejected(string name)
    {
        Assert.False(CanPay(name, null));
    }

    /// <summary>A NIF that belongs to nobody internal does not rescue an internal name.</summary>
    [Fact]
    public void InternalAlias_WithAForeignNif_IsStillRejected()
    {
        Assert.False(CanPay("ALPLA ANGOLA PLASTICOS LDA.", "9999999999"));
    }

    // ── The rule is stronger than "supplier != own company" ──────────────────────────────────

    /// <summary>
    /// Both directions. A request raised by AlplaPLASTICO naming AlplaSOPRO is just as wrong as one
    /// naming itself: they are both internal counterparties, and an ordinary payment request is not
    /// the instrument for money moving between two ALPLA entities.
    /// </summary>
    [Theory]
    [InlineData(1, "ALPLA ANGOLA SOPRO, LDA", "5001760246")]   // company PLASTICO, supplier SOPRO
    [InlineData(2, "ALPLA ANGOLA PLASTICOS LDA.", "5417567485")] // company SOPRO, supplier PLASTICO
    public void CrossEntity_IsRejectedInBothDirections(
        int requestCompanyId, string supplierName, string supplierTaxId)
    {
        var resolved = InternalCompanyPolicy.Resolve(supplierName, supplierTaxId, Companies);

        Assert.NotNull(resolved);
        // The point: it is refused even though it is NOT the requesting company. A
        // `supplier != request.Company` check would have let exactly this through.
        Assert.NotEqual(requestCompanyId, resolved!.Id);
        Assert.False(CanPay(supplierName, supplierTaxId));
    }

    // ── No bypass ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// There is no actor, role or override in the signature, and that is the design: financial
    /// integrity is not a permission, so a System Administrator has nothing to present that would
    /// change the answer. This test exists to fail loudly if someone adds one.
    /// </summary>
    [Fact]
    public void PolicyExposesNoBypassParameter()
    {
        var method = typeof(InternalCompanyPolicy)
            .GetMethod(nameof(InternalCompanyPolicy.CanBePaymentSupplier))!;

        var parameterNames = method.GetParameters().Select(p => p.Name!.ToLowerInvariant()).ToList();

        Assert.DoesNotContain(parameterNames, n =>
            n.Contains("force") || n.Contains("override") || n.Contains("bypass") ||
            n.Contains("role") || n.Contains("admin") || n.Contains("actor"));

        // And the answer for an internal entity is the same no matter how often it is asked.
        Assert.False(CanPay("AlplaPLASTICO", "5417567485"));
        Assert.False(CanPay("AlplaPLASTICO", "5417567485"));
    }

    // ── Nothing to decide ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An empty counterparty is not "internal" — it is missing, which the ordinary mandatory-field
    /// validation already reports. Claiming it here would replace a clear message with a confusing
    /// one.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    public void UnknownSupplier_IsNotTreatedAsInternal(string? name, string? taxId)
    {
        Assert.True(CanPay(name, taxId));
    }

    [Fact]
    public void NoCompaniesConfigured_BlocksNothing()
    {
        Assert.True(InternalCompanyPolicy.CanBePaymentSupplier(
            "ALPLA ANGOLA PLASTICOS LDA.", "5417567485", Array.Empty<InternalCompanyRef>()));
    }

    // ── Normalisation ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Alpla Angola Plásticos, Lda", "ALPLA ANGOLA PLASTICOS LDA")]
    [InlineData("  ALPLA   ANGOLA  ", "ALPLA ANGOLA")]
    [InlineData("ALPLA-Werke", "ALPLA WERKE")]
    public void NormalizeName_StripsAccentsPunctuationAndExtraSpace(string input, string expected)
    {
        Assert.Equal(expected, InternalCompanyPolicy.NormalizeName(input));
    }

    /// <summary>
    /// Punctuation must SEPARATE rather than vanish: "ALPLA,ANGOLA" is two words. If it were removed
    /// silently the tokens would fuse and word-boundary matching would miss the entity entirely.
    /// </summary>
    [Fact]
    public void NormalizeName_TreatsPunctuationAsASeparator()
    {
        Assert.Equal("ALPLA ANGOLA SOPRO LDA",
            InternalCompanyPolicy.NormalizeName("ALPLA,ANGOLA.SOPRO/LDA"));
    }

    [Fact]
    public void ViolationCodeIsStable()
    {
        Assert.Equal("PAYMENT_INTERNAL_COMPANY_AS_SUPPLIER", InternalCompanyPolicy.ViolationCode);
    }
}
