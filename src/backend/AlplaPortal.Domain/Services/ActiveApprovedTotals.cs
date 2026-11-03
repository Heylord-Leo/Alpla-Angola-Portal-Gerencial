using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// D5 — historical approved amounts (<c>Request.ApprovedTotalAmount</c>, <c>ApprovalBatch.ApprovedTotalAmount</c>) are
/// evidence and are never rewritten. The ACTIVE approved figure is computed here, per currency, from what is still
/// alive: non-cancelled P.O. groups of APPROVED batches, and non-released batch items. Currencies are never summed
/// together; a caller that needs one scalar gets it only when exactly one currency is involved.
/// </summary>
public static class ActiveApprovedTotals
{
    public sealed record CurrencyTotal(string Currency, decimal Amount);

    public sealed record BatchTotals(decimal? HistoricalApprovedTotal, IReadOnlyList<CurrencyTotal> Active, IReadOnlyList<CurrencyTotal> Released, int ReleasedItemCount);

    /// <summary>Per-currency totals of the request's live groups (non-cancelled, batchless or of an APPROVED batch).</summary>
    public static IReadOnlyList<CurrencyTotal> ForRequest(IEnumerable<RequestPoGroup> groups, IEnumerable<ApprovalBatch> batches)
    {
        var approvedBatchIds = batches
            .Where(b => b.Status == RequestConstants.ApprovalBatchStatuses.Approved)
            .Select(b => b.Id).ToHashSet();
        return groups
            .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled
                        && (g.ApprovalBatchId == null || approvedBatchIds.Contains(g.ApprovalBatchId.Value)))
            .GroupBy(g => Normalize(g.CurrencyCode ?? g.Currency?.Code))
            .Select(x => new CurrencyTotal(x.Key, Math.Round(x.Sum(g => g.TotalAmount), 2)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Single scalar when exactly one currency is involved; otherwise null (the caller shows the per-currency list or
    /// "MULTIPLE"). An empty list yields (null, null).
    /// </summary>
    public static (decimal? Amount, string? Currency) Scalar(IReadOnlyList<CurrencyTotal> totals) =>
        totals.Count == 1 ? (totals[0].Amount, totals[0].Currency) : (null, totals.Count > 1 ? DepartmentalAmountResolver.MultipleCurrencies : null);

    /// <summary>
    /// Batch-level split: active = non-released items valued from the frozen winning candidate (legacy items: the live
    /// selected quotation item), released = the complement. Requires Items → SelectedCandidate / SelectedQuotationItem
    /// (with Quotation) to be loaded.
    /// </summary>
    public static BatchTotals ForBatch(ApprovalBatch batch)
    {
        var active = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var released = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var releasedCount = 0;
        foreach (var bi in batch.Items)
        {
            var (amount, currency) = ValueOf(bi);
            if (amount == null) continue;
            var target = bi.ReleasedAtUtc.HasValue ? released : active;
            if (bi.ReleasedAtUtc.HasValue) releasedCount++;
            target[currency] = target.TryGetValue(currency, out var cur) ? cur + amount.Value : amount.Value;
        }
        return new BatchTotals(batch.ApprovedTotalAmount, ToList(active), ToList(released), releasedCount);
    }

    private static (decimal? Amount, string Currency) ValueOf(ApprovalBatchItem bi)
    {
        if (bi.SelectedCandidate != null)
            return (bi.SelectedCandidate.LineTotal, Normalize(bi.SelectedCandidate.Currency));
        if (bi.SelectedQuotationItem != null)
            return (bi.SelectedQuotationItem.LineTotal, Normalize(bi.SelectedQuotationItem.Quotation?.Currency));
        return (null, "—");
    }

    private static IReadOnlyList<CurrencyTotal> ToList(Dictionary<string, decimal> d) =>
        d.Select(kv => new CurrencyTotal(kv.Key, Math.Round(kv.Value, 2))).OrderBy(t => t.Currency, StringComparer.Ordinal).ToList();

    private static string Normalize(string? currency) => string.IsNullOrWhiteSpace(currency) ? "—" : currency.Trim().ToUpperInvariant();
}
