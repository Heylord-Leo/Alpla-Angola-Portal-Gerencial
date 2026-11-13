using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Explorer "Total Filtrado" — one total per currency over EVERY request matching the active filters (all pages).
/// Currencies are never summed together.
///
/// Rules (the same ones the explorer rows use since the DEV round 1 correction of 2026-11-12):
/// <list type="bullet">
/// <item>amount = approved snapshot (<c>Request.ApprovedTotalAmount</c>) when present, else the selected quotation total,
/// else the requester's estimate; its currency is <c>ApprovedCurrencyCode</c> / the quotation currency / the request currency
/// respectively — the caller projects these pairs in the database and hands them over as <see cref="SingleCurrencyRow"/>;</item>
/// <item>a request approved in several currencies (<c>ApprovedCurrencyCode = "MULTIPLE"</c>) contributes each component once in
/// its own currency, from its live groups (<see cref="ActiveApprovedTotals.ForRequest"/>: cancelled groups and groups of
/// non-approved batches excluded) — never its summed scalar;</item>
/// <item>cancelled / rejected requests contribute nothing (the rows print "-----" for them) — the caller filters them out.</item>
/// </list>
/// </summary>
public static class RequestFilteredTotals
{
    /// <summary>A single-currency request already reduced to (currency, amount). A null currency is bucketed as "AOA", which is
    /// what the explorer rows print for a request without a currency.</summary>
    // Member-initialised (not a positional record): EF Core only translates a GroupBy key taken from a projected member,
    // never from a constructor argument.
    public sealed class SingleCurrencyRow
    {
        public string? Currency { get; init; }
        public decimal Amount { get; init; }
    }

    public const string FallbackCurrency = "AOA";

    public const string MultipleCurrencies = "MULTIPLE";

    /// <summary>Cancelled / rejected requests contribute nothing (the explorer rows print "-----" for them).</summary>
    public static readonly string[] ExcludedStatusCodes = { "CANCELLED", "REJECTED" };

    /// <summary>True for a request approved in several currencies — reduced in memory from its live groups, never by the scalar.</summary>
    public static readonly Expression<Func<Request, bool>> IsMultiCurrency =
        r => r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0 && r.ApprovedCurrencyCode == MultipleCurrencies;

    /// <summary>
    /// The grouped (currency, Σ amount) query over single-currency requests — one row per currency, summed in the database,
    /// with the explorer row rules: approved snapshot + <c>ApprovedCurrencyCode</c>, else the selected quotation total +
    /// currency, else the requester's estimate + request currency.
    ///
    /// The selected quotation is brought in as an OUTER APPLY / left join (<c>SelectMany … DefaultIfEmpty</c>) so that the
    /// summed expression only references columns. A correlated <c>FirstOrDefault()</c> subquery inside the amount made SQL
    /// Server refuse the statement with error 130 ("Cannot perform an aggregate function on an expression containing an
    /// aggregate or a subquery") — DEV blocker of 2026-11-12, executed-on-LocalDB regression in
    /// RequestsExplorerFilteredTotalsRelationalTests.
    /// </summary>
    public static IQueryable<SingleCurrencyRow> GroupSingleCurrency(IQueryable<Request> singleCurrencyRequests) =>
        singleCurrencyRequests
            .SelectMany(
                r => r.Quotations.Where(q => q.Id == r.SelectedQuotationId).DefaultIfEmpty(),
                (r, q) => new SingleCurrencyRow
                {
                    Currency = (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0 && r.ApprovedCurrencyCode != null)
                        ? r.ApprovedCurrencyCode
                        : r.SelectedQuotationId.HasValue
                            ? (q != null ? q.Currency : null)
                            : (r.Currency != null ? r.Currency.Code : null),
                    Amount = (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0)
                        ? r.ApprovedTotalAmount.Value
                        : r.SelectedQuotationId.HasValue
                            ? (q != null ? q.TotalAmount : 0m)   // rows print 0 for a dangling SelectedQuotationId
                            : r.EstimatedTotalAmount,
                })
            .GroupBy(x => x.Currency)
            .Select(g => new SingleCurrencyRow { Currency = g.Key, Amount = g.Sum(x => x.Amount) });

    /// <summary>Negation of a predicate expression, kept as an expression so EF Core can translate it.</summary>
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    public static IReadOnlyList<ActiveApprovedTotals.CurrencyTotal> Merge(
        IEnumerable<SingleCurrencyRow> singleCurrencyRows,
        IEnumerable<Request> multiCurrencyRequests)
    {
        var buckets = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var row in singleCurrencyRows)
            Add(buckets, Normalize(row.Currency), row.Amount);

        foreach (var request in multiCurrencyRequests)
        {
            foreach (var component in ActiveApprovedTotals.ForRequest(request.PoGroups, request.ApprovalBatches))
                Add(buckets, Normalize(component.Currency), component.Amount);
        }

        return buckets
            .Select(kv => new ActiveApprovedTotals.CurrencyTotal(kv.Key, Math.Round(kv.Value, 2)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();
    }

    private static void Add(Dictionary<string, decimal> buckets, string currency, decimal amount) =>
        buckets[currency] = buckets.TryGetValue(currency, out var current) ? current + amount : amount;

    private static string Normalize(string? currency) =>
        string.IsNullOrWhiteSpace(currency) || currency == "—" ? FallbackCurrency : currency.Trim().ToUpperInvariant();
}
