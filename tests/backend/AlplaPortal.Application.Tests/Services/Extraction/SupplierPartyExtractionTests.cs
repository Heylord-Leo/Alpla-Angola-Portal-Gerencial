using System;
using System.Linq;
using System.Reflection;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Infrastructure.Services.Extraction;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Extraction;

/// <summary>
/// A fiscal number must stay with the party whose block it was printed in.
/// </summary>
///
/// <remarks>
/// <para>The defect: an invoice issued BY FIX4U (NIF 5417528641) TO ALPLA ANGOLA PLASTICOS
/// (Nº Contribuinte 5417567485) came back with the CUSTOMER's fiscal number bound to the supplier.
/// Nothing in the mapping chain chose it — every layer is a straight pass-through — so the value
/// arrived that way from the model.</para>
///
/// <para>It arrived that way because the schema offered exactly one fiscal-number slot,
/// <c>supplierTaxId</c>, for a document that carries two. The model had to discard one, and the
/// prompt gave it no rule for which. <c>billedCompanyTaxId</c> now gives the customer's number its
/// own home.</para>
///
/// <para>These tests pin the two things that are deterministic: that the mapping binds each number
/// to its own party, and that the prompt still carries the guidance. What the model actually returns
/// for a real PDF cannot be asserted here — that is browser validation.</para>
/// </remarks>
public class SupplierPartyExtractionTests
{
    private const string SupplierNif = "5417528641";   // FIX4U's own
    private const string CustomerNif = "5417567485";   // ALPLA ANGOLA PLASTICOS

    private static ExtractionResultDto MapFromJson(string json)
    {
        // MapFromJson is the private seam between the model's JSON and the typed result. Reaching it
        // directly keeps the test on the mapping contract, with no HTTP and no provider call.
        var method = typeof(OpenAiDocumentExtractionProvider)
            .GetMethod("MapFromJson", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var provider = (OpenAiDocumentExtractionProvider)
            System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(OpenAiDocumentExtractionProvider));

        return (ExtractionResultDto)method.Invoke(provider, new object[] { json })!;
    }

    /// <summary>
    /// The exact document from the report: FIX4U bills ALPLA. Both fiscal numbers are present and
    /// each must land on its own party.
    /// </summary>
    [Fact]
    public void Fix4uInvoiceToAlpla_BindsEachFiscalNumberToItsOwnParty()
    {
        var json = $@"{{
            ""header"": {{
                ""supplierName"": ""FIX4U - Comercio e Industria, Lda"",
                ""supplierTaxId"": ""{SupplierNif}"",
                ""billedCompanyName"": ""ALPLA ANGOLA PLASTICOS LDA."",
                ""billedCompanyTaxId"": ""{CustomerNif}"",
                ""documentNumber"": ""FT 2026/119"",
                ""currency"": ""AOA"",
                ""totalAmount"": 100.00,
                ""grandTotal"": 114.00
            }},
            ""items"": [],
            ""qualityScore"": 0.95
        }}";

        var result = MapFromJson(json);

        Assert.Equal("FIX4U - Comercio e Industria, Lda", result.Header!.SupplierName);
        Assert.Equal(SupplierNif, result.Header.SupplierTaxId);

        Assert.Equal("ALPLA ANGOLA PLASTICOS LDA.", result.Header.BilledCompanyName);
        Assert.Equal(CustomerNif, result.Header.BilledCompanyTaxId);

        // The regression, stated as the thing that must never happen again.
        Assert.NotEqual(CustomerNif, result.Header.SupplierTaxId);
    }

    /// <summary>
    /// The customer's fiscal number must not leak into the supplier field even when the supplier
    /// block carried none. §5: a missing NIF is recoverable, a wrong one is not.
    /// </summary>
    [Fact]
    public void SupplierWithoutAFiscalNumber_LeavesSupplierTaxIdNull()
    {
        var json = $@"{{
            ""header"": {{
                ""supplierName"": ""FIX4U - Comercio e Industria, Lda"",
                ""supplierTaxId"": null,
                ""billedCompanyName"": ""ALPLA ANGOLA PLASTICOS LDA."",
                ""billedCompanyTaxId"": ""{CustomerNif}""
            }},
            ""items"": [],
            ""qualityScore"": 0.9
        }}";

        var result = MapFromJson(json);

        Assert.Equal("FIX4U - Comercio e Industria, Lda", result.Header!.SupplierName);
        Assert.Null(result.Header.SupplierTaxId);
        Assert.Equal(CustomerNif, result.Header.BilledCompanyTaxId);
    }

    /// <summary>
    /// A document that names no customer fiscal number still maps cleanly — the new field is
    /// optional, so older payloads and models that omit it are unaffected.
    /// </summary>
    [Fact]
    public void PayloadWithoutTheCustomerField_StillMaps()
    {
        var json = $@"{{
            ""header"": {{
                ""supplierName"": ""Fornecedor Externo Lda"",
                ""supplierTaxId"": ""{SupplierNif}"",
                ""billedCompanyName"": ""AlplaSOPRO""
            }},
            ""items"": [],
            ""qualityScore"": 0.9
        }}";

        var result = MapFromJson(json);

        Assert.Equal(SupplierNif, result.Header!.SupplierTaxId);
        Assert.Null(result.Header.BilledCompanyTaxId);
    }

    // ── The prompt contract ──────────────────────────────────────────────────────────────────

    private static string SystemPrompt()
    {
        var method = typeof(OpenAiDocumentExtractionProvider)
            .GetMethod("GetSystemPrompt", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var provider = (OpenAiDocumentExtractionProvider)
            System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(OpenAiDocumentExtractionProvider));

        return (string)method.Invoke(provider, Array.Empty<object>())!;
    }

    /// <summary>
    /// The guidance is the fix. Deleting it would restore the defect silently, with every mapping
    /// test above still green — so its presence is asserted rather than assumed.
    /// </summary>
    [Theory]
    [InlineData("billedCompanyTaxId")]          // the customer's number has a home
    [InlineData("PARTY IDENTIFICATION")]        // roles are established before fields are read
    [InlineData("Exmo.(s) Senhor(es)")]         // the customer-block marker from the report
    [InlineData("Nº Contribuinte")]             // the customer-side label
    [InlineData("Encomenda")]                   // the purchase-order inversion is preserved
    public void PromptCarriesThePartyRoleGuidance(string expected)
    {
        Assert.Contains(expected, SystemPrompt(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The worked example is deliberately in the prompt: it is the case that actually failed, and a
    /// concrete pairing instructs far better than an abstract rule.
    /// </summary>
    [Fact]
    public void PromptShowsTheFix4uWorkedExample()
    {
        var prompt = SystemPrompt();

        Assert.Contains(SupplierNif, prompt, StringComparison.Ordinal);
        Assert.Contains(CustomerNif, prompt, StringComparison.Ordinal);
        Assert.Contains("would be WRONG", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// §5: null beats a confident guess. The instruction must say so explicitly.
    /// </summary>
    [Fact]
    public void PromptPrefersNullOverBorrowingTheOtherPartysNumber()
    {
        var prompt = SystemPrompt();

        Assert.Contains("supplierTaxId = null", prompt, StringComparison.Ordinal);
        Assert.Contains("PREFER NULL", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt version is what ties a stored extraction back to the instructions that produced
    /// it. Changing the rules without changing the version makes past results unexplainable.
    /// </summary>
    [Fact]
    public void InvoicePromptVersionRecordsThePartyRoleRevision()
    {
        var field = typeof(OpenAiDocumentExtractionProvider)
            .GetField("InvoicePromptVersion", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal("v2.2-party-roles", (string)field.GetRawConstantValue()!);
    }
}
