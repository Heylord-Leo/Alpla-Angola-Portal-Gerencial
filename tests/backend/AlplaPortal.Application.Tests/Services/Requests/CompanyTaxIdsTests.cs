using AlplaPortal.Domain.Common;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>Phase 3C — centralized company NIF resolution for the Buyer "Solicitar cotação" email.</summary>
public class CompanyTaxIdsTests
{
    [Fact]
    public void Prefers_The_Persisted_TaxId_When_Present()
    {
        Assert.Equal("5417567485", CompanyTaxIds.Resolve("AlplaPLASTICO", "5417567485"));
        Assert.Equal("999", CompanyTaxIds.Resolve("AlplaSOPRO", "999")); // persisted wins even over the known map
    }

    [Fact]
    public void Falls_Back_To_The_Known_ALPLA_Angola_NIF_By_Name()
    {
        Assert.Equal("5417567485", CompanyTaxIds.Resolve("AlplaPLASTICO", null));
        Assert.Equal("5001760246", CompanyTaxIds.Resolve("AlplaSOPRO", "  "));
    }

    [Fact]
    public void Returns_Null_For_Unknown_Company_Without_A_TaxId()
    {
        Assert.Null(CompanyTaxIds.Resolve("Outra Empresa", null));
        Assert.Null(CompanyTaxIds.Resolve(null, null));
    }
}
