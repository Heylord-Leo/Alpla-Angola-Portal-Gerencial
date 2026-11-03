using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.5 — the LIFECYCLE context of a saved quotation and of each of its items, derived only from stored relationships:
/// approval batches (items, candidates, winner, release stamps), the request lines (current lifecycle, current winner, group)
/// and the DEC-147 / D3 reuse rule. Found in TEST on REQ-28/10/2026-628: the TREVOTECH quotation (awarded, then its P.O. group
/// cancelled and item 1 released and re-quoted with MAQSIS) was listed like any open quotation. Nothing here changes
/// eligibility or allocations; it names what already happened, per item (a cancellation of one item's award never classifies
/// the whole quotation), and separates the HISTORICAL adjudication status from the CURRENT reuse eligibility. Pure.
/// </summary>
public static class QuotationAdjudicationProjection
{
    /// <summary>Per quotation item.</summary>
    public static class ItemStatuses
    {
        /// <summary>NOT_QUOTED / IGNORED placeholder — never a candidate.</summary>
        public const string NotQuoted = "NOT_QUOTED";
        public const string NotSubmitted = "NOT_SUBMITTED";
        public const string InApproval = "IN_APPROVAL";
        public const string Awarded = "AWARDED";
        public const string CandidateNotAwarded = "CANDIDATE_NOT_AWARDED";
        /// <summary>Winner of an item whose award was cancelled (P.O.-group cancellation release) or whose batch was cancelled.</summary>
        public const string AwardCancelled = "AWARD_CANCELLED";
        public const string CandidateCancelled = "CANDIDATE_CANCELLED";
        public const string Rejected = "REJECTED";
    }

    /// <summary>What happened to the request LINE after the historical award (for AWARD_CANCELLED / CANDIDATE_CANCELLED).</summary>
    public static class LineStates
    {
        public const string RequotedWithOther = "REQUOTED_WITH_OTHER";
        public const string PendingRequote = "PENDING_REQUOTE";
        public const string ClosedAfterApproval = "CLOSED_AFTER_APPROVAL";
        public const string AwardedHere = "AWARDED_HERE";
    }

    /// <summary>Per quotation.</summary>
    public static class QuotationStatuses
    {
        public const string Open = "OPEN";
        public const string InApproval = "IN_APPROVAL";
        public const string Awarded = "AWARDED";
        public const string NotAwarded = "NOT_AWARDED";
        public const string PartiallyHistorical = "PARTIALLY_HISTORICAL";
        public const string HistoricalCancelled = "HISTORICAL_CANCELLED";
        public const string Rejected = "REJECTED";
    }

    public sealed record QuotationItemFacts(Guid Id, Guid QuotationId, Guid? MappedRequestLineItemId, string? ReconciliationStatus);
    public sealed record BatchItemFacts(Guid RequestLineItemId, Guid? SelectedQuotationItemId, IReadOnlyList<Guid> CandidateQuotationItemIds, DateTime? ReleasedAtUtc, string? ReleaseKind);
    public sealed record BatchFacts(Guid Id, int BatchNumber, string Status, DateTime CreatedAtUtc, IReadOnlyList<BatchItemFacts> Items);
    public sealed record LineFacts(Guid Id, int LineNumber, string? QuotationLifecycleStatus, Guid? SelectedQuotationItemId);
    public sealed record QuotationFacts(Guid Id, string? SupplierName);

    public sealed record ItemResult(
        Guid QuotationItemId, string AdjudicationStatus, int? BatchNumber, string? ReleaseKind,
        string? LineState, string? LineCurrentSupplierName, string? ReuseStatus);

    public sealed record QuotationResult(Guid QuotationId, string LifecycleStatus, int ActiveAwardedItemCount, int HistoricalItemCount, int? HistoricalBatchNumber, string? HistoricalReleaseKind, IReadOnlyList<ItemResult> Items);

    public static IReadOnlyDictionary<Guid, QuotationResult> Project(
        IReadOnlyList<QuotationFacts> quotations,
        IReadOnlyList<QuotationItemFacts> items,
        IReadOnlyList<BatchFacts> batches,
        IReadOnlyList<LineFacts> lines,
        IReadOnlyList<QuotationReuseEligibilityPolicy.FinishedUse> finishedUses,
        IReadOnlyList<QuotationReuseEligibilityPolicy.AuthorizationFacts> authorizations)
    {
        var lineById = lines.ToDictionary(l => l.Id);
        var itemById = items.ToDictionary(i => i.Id);
        var quotationOfItem = items.ToDictionary(i => i.Id, i => i.QuotationId);
        var supplierOfQuotation = quotations.ToDictionary(q => q.Id, q => q.SupplierName);

        // Every batch reference of each quotation item, newest batch first.
        var refs = new Dictionary<Guid, List<(BatchFacts Batch, BatchItemFacts Item, bool Winner)>>();
        foreach (var b in batches.OrderByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.BatchNumber))
        {
            foreach (var bi in b.Items)
            {
                foreach (var cid in bi.CandidateQuotationItemIds.Distinct())
                    Add(refs, cid, (b, bi, bi.SelectedQuotationItemId == cid));
                if (bi.SelectedQuotationItemId.HasValue && !bi.CandidateQuotationItemIds.Contains(bi.SelectedQuotationItemId.Value))
                    Add(refs, bi.SelectedQuotationItemId.Value, (b, bi, true));
            }
        }

        var results = new Dictionary<Guid, QuotationResult>();
        foreach (var q in quotations)
        {
            var itemResults = new List<ItemResult>();
            foreach (var qi in items.Where(i => i.QuotationId == q.Id))
            {
                var considered = qi.ReconciliationStatus != null && RequestConstants.ReconciliationStatuses.Considered.Contains(qi.ReconciliationStatus);
                if (!considered)
                {
                    itemResults.Add(new ItemResult(qi.Id, ItemStatuses.NotQuoted, null, null, null, null, null));
                    continue;
                }
                var verdict = QuotationReuseEligibilityPolicy.Resolve(qi.Id, qi.ReconciliationStatus, finishedUses, authorizations);
                string? reuse = verdict.IsReuseBlocked ? QuotationReuseEligibilityPolicy.Reasons.ReuseAuthorizationRequired
                    : verdict.IsReuseAuthorized ? QuotationReuseEligibilityPolicy.Reasons.ReuseAuthorized : null;

                if (!refs.TryGetValue(qi.Id, out var uses) || uses.Count == 0)
                {
                    itemResults.Add(new ItemResult(qi.Id, ItemStatuses.NotSubmitted, null, null, null, null, reuse));
                    continue;
                }
                // The most recent reference decides the item's status; an ACTIVE award anywhere wins over older history.
                var active = uses.FirstOrDefault(u => u.Winner && u.Batch.Status == RequestConstants.ApprovalBatchStatuses.Approved && !u.Item.ReleasedAtUtc.HasValue);
                var inApproval = uses.FirstOrDefault(u => IsInApproval(u.Batch.Status));
                var latest = uses[0];
                string status; int? batchNumber; string? releaseKind = null; string? lineState = null; string? lineSupplier = null;
                if (active.Batch != null)
                {
                    status = ItemStatuses.Awarded; batchNumber = active.Batch.BatchNumber; lineState = LineStates.AwardedHere;
                }
                else if (inApproval.Batch != null)
                {
                    status = ItemStatuses.InApproval; batchNumber = inApproval.Batch.BatchNumber;
                }
                else if (latest.Batch.Status == RequestConstants.ApprovalBatchStatuses.Rejected)
                {
                    status = ItemStatuses.Rejected; batchNumber = latest.Batch.BatchNumber;
                }
                else if (latest.Batch.Status == RequestConstants.ApprovalBatchStatuses.Cancelled || latest.Item.ReleasedAtUtc.HasValue)
                {
                    status = latest.Winner ? ItemStatuses.AwardCancelled : ItemStatuses.CandidateCancelled;
                    batchNumber = latest.Batch.BatchNumber;
                    releaseKind = latest.Item.ReleasedAtUtc.HasValue ? latest.Item.ReleaseKind : null;
                    (lineState, lineSupplier) = LineStateOf(latest.Item.RequestLineItemId, qi.Id, lineById, itemById, quotationOfItem, supplierOfQuotation, refs);
                }
                else
                {
                    status = latest.Winner ? ItemStatuses.Awarded : ItemStatuses.CandidateNotAwarded; batchNumber = latest.Batch.BatchNumber;
                }
                itemResults.Add(new ItemResult(qi.Id, status, batchNumber, releaseKind, lineState, lineSupplier, reuse));
            }

            var considered2 = itemResults.Where(r => r.AdjudicationStatus != ItemStatuses.NotQuoted).ToList();
            var historical = considered2.Where(r => r.AdjudicationStatus is ItemStatuses.AwardCancelled or ItemStatuses.CandidateCancelled).ToList();
            var awarded = considered2.Count(r => r.AdjudicationStatus == ItemStatuses.Awarded);
            string lifecycle;
            if (considered2.Count == 0) lifecycle = QuotationStatuses.Open;
            else if (historical.Count == considered2.Count) lifecycle = QuotationStatuses.HistoricalCancelled;
            else if (historical.Count > 0) lifecycle = QuotationStatuses.PartiallyHistorical;
            else if (awarded > 0) lifecycle = QuotationStatuses.Awarded;
            else if (considered2.Any(r => r.AdjudicationStatus == ItemStatuses.InApproval)) lifecycle = QuotationStatuses.InApproval;
            else if (considered2.All(r => r.AdjudicationStatus == ItemStatuses.Rejected)) lifecycle = QuotationStatuses.Rejected;
            else if (considered2.All(r => r.AdjudicationStatus is ItemStatuses.CandidateNotAwarded or ItemStatuses.Rejected)) lifecycle = QuotationStatuses.NotAwarded;
            else lifecycle = QuotationStatuses.Open;
            var hist = historical.FirstOrDefault(r => r.AdjudicationStatus == ItemStatuses.AwardCancelled) ?? historical.FirstOrDefault();
            results[q.Id] = new QuotationResult(q.Id, lifecycle, awarded, historical.Count, hist?.BatchNumber, hist?.ReleaseKind, itemResults);
        }
        return results;
    }

    private static bool IsInApproval(string status) =>
        status is not (RequestConstants.ApprovalBatchStatuses.Approved or RequestConstants.ApprovalBatchStatuses.Cancelled or RequestConstants.ApprovalBatchStatuses.Rejected);

    private static (string? State, string? Supplier) LineStateOf(
        Guid lineId, Guid thisQuotationItemId,
        IReadOnlyDictionary<Guid, LineFacts> lineById, IReadOnlyDictionary<Guid, QuotationItemFacts> itemById,
        IReadOnlyDictionary<Guid, Guid> quotationOfItem, IReadOnlyDictionary<Guid, string?> supplierOfQuotation,
        IReadOnlyDictionary<Guid, List<(BatchFacts Batch, BatchItemFacts Item, bool Winner)>> refs)
    {
        if (!lineById.TryGetValue(lineId, out var line)) return (null, null);
        if (line.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval) return (LineStates.ClosedAfterApproval, null);
        // Re-quoted: the line's CURRENT winner is another quotation item that is actively awarded.
        if (line.SelectedQuotationItemId.HasValue && line.SelectedQuotationItemId.Value != thisQuotationItemId
            && refs.TryGetValue(line.SelectedQuotationItemId.Value, out var winnerUses)
            && winnerUses.Any(u => u.Winner && u.Batch.Status == RequestConstants.ApprovalBatchStatuses.Approved && !u.Item.ReleasedAtUtc.HasValue))
        {
            var otherQuotation = quotationOfItem.TryGetValue(line.SelectedQuotationItemId.Value, out var oq) ? oq : (Guid?)null;
            return (LineStates.RequotedWithOther, otherQuotation.HasValue && supplierOfQuotation.TryGetValue(otherQuotation.Value, out var s) ? s : null);
        }
        if (line.QuotationLifecycleStatus == null || line.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending
            || line.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.BatchAssigned)
            return (LineStates.PendingRequote, null);
        return (null, null);
    }

    private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) { list = new List<TValue>(); map[key] = list; }
        list.Add(value);
    }
}
