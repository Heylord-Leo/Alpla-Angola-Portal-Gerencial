using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.DTOs.Dashboard;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

/// <summary>
/// The currency-safety core of the B7 Financial Summary (pure, DB-free, unit-testable). Given per-entity
/// monetary contributions, it partitions strictly by currency: amounts of different currencies are NEVER
/// summed (PD-B7-01), null/blank currencies fall into an explicit <see cref="FinancialCurrency.Unknown"/>
/// bucket (PD-B7-11), and a contribution with no authoritative amount is counted for the category but
/// never given a fabricated value (PD-B7-05/07). No FX conversion of any kind.
/// </summary>
public static class FinancialCurrencyAggregator
{
    /// <param name="EntityId">Stable identity of the contributing entity (batch/group) — dedupes and counts.</param>
    /// <param name="RequestId">Owning request — for the distinct-request count.</param>
    /// <param name="Currency">Raw currency code (may be null/blank → UNKNOWN).</param>
    /// <param name="Amount">Authoritative amount, or null when none exists (counted, not valued).</param>
    public readonly record struct Contribution(Guid EntityId, Guid RequestId, string? Currency, decimal? Amount);

    public static string NormalizeCurrency(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return FinancialCurrency.Unknown;
        var c = code.Trim().ToUpperInvariant();
        // The canonical display resolver uses "---" as its no-currency fallback.
        return c == "---" ? FinancialCurrency.Unknown : c;
    }

    public sealed record Result(
        List<CurrencyAmountDto> Currencies, int EntityCount, int RequestCount, bool IsAuthoritative);

    /// <summary>Aggregate one category's contributions. Category counts cover ALL distinct entities/requests
    /// (valued or not); currency rows only carry contributions that have an amount.</summary>
    public static Result Aggregate(IEnumerable<Contribution> contributions)
    {
        var list = contributions.ToList();

        // Category-level population: every distinct entity / request, valued or not.
        var entityCount = list.Select(c => c.EntityId).Distinct().Count();
        var requestCount = list.Select(c => c.RequestId).Distinct().Count();
        // Not authoritative if any contribution lacks an amount.
        var isAuthoritative = list.All(c => c.Amount.HasValue);

        var currencies = list
            .Where(c => c.Amount.HasValue) // valued contributions only — never fabricate a zero
            .GroupBy(c => NormalizeCurrency(c.Currency))
            .OrderBy(g => g.Key == FinancialCurrency.Unknown ? 1 : 0) // UNKNOWN last
            .ThenBy(g => g.Key)
            .Select(g => new CurrencyAmountDto
            {
                CurrencyCode = g.Key,
                Amount = g.Sum(c => c.Amount!.Value),                       // per-currency sum ONLY
                EntityCount = g.Select(c => c.EntityId).Distinct().Count(),
                RequestCount = g.Select(c => c.RequestId).Distinct().Count(),
            })
            .ToList();

        return new Result(currencies, entityCount, requestCount, isAuthoritative);
    }
}
