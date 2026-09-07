using System;
using System.Linq;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Xunit;
using Agg = AlplaPortal.Infrastructure.Services.Dashboard.FinancialCurrencyAggregator;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B7.1 — the currency-safety core. Locks PD-B7-01 (never sum across currencies), PD-B7-11 (explicit
/// UNKNOWN bucket) and PD-B7-05/07 (unvalued contributions counted but never fabricated). Pure math — no DB.
/// </summary>
public class FinancialCurrencyAggregatorTests
{
    private static Guid G() => Guid.NewGuid();
    private static Agg.Contribution C(Guid entity, Guid req, string? cur, decimal? amt) => new(entity, req, cur, amt);

    [Fact]
    public void Never_sums_across_currencies()
    {
        var r = Agg.Aggregate(new[]
        {
            C(G(), G(), "AOA", 10_000_000m),
            C(G(), G(), "EUR", 5_000m),
            C(G(), G(), "USD", 2_000m),
        });

        Assert.Equal(3, r.Currencies.Count);
        Assert.Equal(10_000_000m, r.Currencies.Single(c => c.CurrencyCode == "AOA").Amount);
        Assert.Equal(5_000m, r.Currencies.Single(c => c.CurrencyCode == "EUR").Amount);
        Assert.Equal(2_000m, r.Currencies.Single(c => c.CurrencyCode == "USD").Amount);
        // There is no combined total anywhere.
        Assert.DoesNotContain(r.Currencies, c => c.Amount == 10_007_000m);
    }

    [Fact]
    public void Same_currency_values_sum()
    {
        var req = G();
        var r = Agg.Aggregate(new[] { C(G(), req, "AOA", 3_000_000m), C(G(), req, "AOA", 5_000_000m) });
        var aoa = r.Currencies.Single();
        Assert.Equal("AOA", aoa.CurrencyCode);
        Assert.Equal(8_000_000m, aoa.Amount);
        Assert.Equal(2, aoa.EntityCount);
        Assert.Equal(1, aoa.RequestCount); // same request
    }

    [Fact]
    public void Null_or_blank_currency_falls_into_the_UNKNOWN_bucket_isolated()
    {
        var r = Agg.Aggregate(new[]
        {
            C(G(), G(), null, 100m),
            C(G(), G(), "  ", 50m),
            C(G(), G(), "---", 25m),   // canonical display fallback
            C(G(), G(), "AOA", 200m),
        });
        var unknown = r.Currencies.Single(c => c.CurrencyCode == FinancialCurrency.Unknown);
        Assert.Equal(175m, unknown.Amount);       // 100 + 50 + 25 kept separate from AOA
        Assert.Equal(200m, r.Currencies.Single(c => c.CurrencyCode == "AOA").Amount);
    }

    [Fact]
    public void Normalizes_currency_case_and_whitespace()
    {
        var r = Agg.Aggregate(new[] { C(G(), G(), " aoa ", 1m), C(G(), G(), "AOA", 1m) });
        Assert.Single(r.Currencies);
        Assert.Equal("AOA", r.Currencies[0].CurrencyCode);
        Assert.Equal(2m, r.Currencies[0].Amount);
    }

    [Fact]
    public void Unvalued_contribution_is_counted_but_never_valued_and_flags_not_authoritative()
    {
        var g1 = G(); var g2 = G();
        var r = Agg.Aggregate(new[] { C(g1, G(), "AOA", 500m), C(g2, G(), "AOA", null) });
        Assert.Equal(2, r.EntityCount);        // both counted in the population
        Assert.False(r.IsAuthoritative);       // one had no amount
        var aoa = r.Currencies.Single();
        Assert.Equal(500m, aoa.Amount);        // the null contributes no fabricated zero
        Assert.Equal(1, aoa.EntityCount);      // only the valued one appears in the currency row
    }

    [Fact]
    public void Category_counts_dedupe_entities_and_requests_across_currencies()
    {
        var group = G(); var req = G();
        // Same group has completed payments in two currencies (bad/historical data) → split by currency,
        // counted once at category level.
        var r = Agg.Aggregate(new[] { C(group, req, "AOA", 3m), C(group, req, "USD", 4m) });
        Assert.Equal(1, r.EntityCount);
        Assert.Equal(1, r.RequestCount);
        Assert.Equal(2, r.Currencies.Count);
    }

    [Fact]
    public void Empty_population_is_authoritative_with_no_currency_rows()
    {
        var r = Agg.Aggregate(Array.Empty<Agg.Contribution>());
        Assert.Empty(r.Currencies);
        Assert.Equal(0, r.EntityCount);
        Assert.True(r.IsAuthoritative);
    }
}
