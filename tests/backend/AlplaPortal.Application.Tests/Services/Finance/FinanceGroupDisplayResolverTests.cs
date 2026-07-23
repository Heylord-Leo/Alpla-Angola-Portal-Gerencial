using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers FinanceGroupDisplayResolver — the fallback chain that resolves a safe display
/// supplier/currency for a RequestPoGroup whose own snapshot fields are null (legacy PAYMENT-type
/// auto-created groups, never actively synced — same root-cause class as DEC-149). Confirmed via
/// browser evidence on REQ-13/07/2026-054 (HOTEL STATION): the group's SupplierNameSnapshot/
/// CurrencyCode were null even though the parent request's own Supplier/Currency were known.
/// </summary>
public class FinanceGroupDisplayResolverTests
{
    [Fact]
    public void ResolveSupplierName_GroupSnapshotPresent_UsesGroupSnapshot()
    {
        var result = FinanceGroupDisplayResolver.ResolveSupplierName("Group Supplier", false, null, "Request Supplier");
        Assert.Equal("Group Supplier", result);
    }

    [Fact]
    public void ResolveSupplierName_GroupSnapshotNull_QuotationSelected_UsesQuotationSupplier()
    {
        var result = FinanceGroupDisplayResolver.ResolveSupplierName(null, true, "Quotation Supplier", "Request Supplier");
        Assert.Equal("Quotation Supplier", result);
    }

    [Fact]
    public void ResolveSupplierName_GroupSnapshotNull_NoQuotation_UsesRequestSupplier()
    {
        // The exact confirmed bug scenario: PAYMENT-type request, no quotation, legacy null group snapshot.
        var result = FinanceGroupDisplayResolver.ResolveSupplierName(null, false, null, "HOTEL STATION");
        Assert.Equal("HOTEL STATION", result);
    }

    [Fact]
    public void ResolveSupplierName_QuotationSelectedButSupplierNull_FallsBackToRequestSupplier()
    {
        var result = FinanceGroupDisplayResolver.ResolveSupplierName(null, true, null, "Request Supplier");
        Assert.Equal("Request Supplier", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSupplierName_TrulyUnknownEverywhere_FallsBackToDashes(string? groupSnapshot)
    {
        var result = FinanceGroupDisplayResolver.ResolveSupplierName(groupSnapshot, false, null, null);
        Assert.Equal("---", result);
    }

    [Fact]
    public void ResolveCurrencyCode_GroupCodePresent_UsesGroupCode()
    {
        var result = FinanceGroupDisplayResolver.ResolveCurrencyCode("USD", false, null, "AOA");
        Assert.Equal("USD", result);
    }

    [Fact]
    public void ResolveCurrencyCode_GroupCodeNull_QuotationSelected_UsesQuotationCurrency()
    {
        var result = FinanceGroupDisplayResolver.ResolveCurrencyCode(null, true, "USD", "AOA");
        Assert.Equal("USD", result);
    }

    [Fact]
    public void ResolveCurrencyCode_GroupCodeNull_NoQuotation_UsesRequestCurrency()
    {
        // The exact confirmed bug scenario: request currency is AOA, group.CurrencyCode is null.
        var result = FinanceGroupDisplayResolver.ResolveCurrencyCode(null, false, null, "AOA");
        Assert.Equal("AOA", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveCurrencyCode_TrulyUnknownEverywhere_FallsBackToDashes(string? groupCode)
    {
        var result = FinanceGroupDisplayResolver.ResolveCurrencyCode(groupCode, false, null, null);
        Assert.Equal("---", result);
    }
}
