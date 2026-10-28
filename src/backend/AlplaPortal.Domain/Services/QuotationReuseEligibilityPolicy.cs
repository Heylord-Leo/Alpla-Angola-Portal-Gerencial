using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.2 — the cancelled-batch / released-item REUSE rule (DEC-147 Option C, P.O. cancellation D3) as ONE pure,
/// side-effect-free policy. A quotation item that was a candidate or the winner of a CANCELLED batch item, or of a
/// RELEASED item of an APPROVED batch (P.O.-group cancellation "voltar a cotar"), is NOT eligible for a new approval
/// batch until the Buyer records an explicit, active, unconsumed <see cref="QuotationReuseAuthorization"/> for that
/// exact (item, source batch) pair. The most recent finished use governs the cycle.
///
/// Consumers: <c>QuotationItemEligibilityService</c> (query-backed; batch creation, authorize-reuse, DTO annotation)
/// and <c>BuyerQueueProjectionInputFactory</c> (loaded aggregate; queue / Workspace coverage). Both MUST call
/// <see cref="Resolve"/> so the coverage buckets ("Pronto para lote") can never disagree with what the batch endpoint
/// accepts — the mismatch found in TEST on REQ-28/10/2026-628 (released line counted as ready, modal with 0 candidates).
/// </summary>
public static class QuotationReuseEligibilityPolicy
{
    /// <summary>Reason codes (values identical to the Application-layer constants used by the API).</summary>
    public static class Reasons
    {
        public const string EligibleNormal = "ELIGIBLE_NORMAL";
        public const string ReuseAuthorizationRequired = "REUSE_AUTHORIZATION_REQUIRED";
        public const string ReuseAuthorized = "REUSE_AUTHORIZED";
        public const string ItemNotReconciled = "ITEM_NOT_RECONCILED";
        public const string AuthorizationConsumed = "AUTHORIZATION_CONSUMED";
        public const string AuthorizationRevoked = "AUTHORIZATION_REVOKED";
    }

    /// <summary>A finished use of a quotation item: candidate/winner of a CANCELLED batch item, or of a RELEASED item of an
    /// APPROVED batch. <paramref name="AtUtc"/> dates the cycle (batch creation for cancelled batches, release stamp for
    /// released items).</summary>
    public sealed record FinishedUse(Guid QuotationItemId, Guid SourceBatchId, int SourceBatchNumber, DateTime AtUtc);

    /// <summary>The facts of one <see cref="QuotationReuseAuthorization"/> row the rule needs.</summary>
    public sealed record AuthorizationFacts(
        Guid Id, Guid QuotationItemId, Guid SourceApprovalBatchId, DateTime AuthorizedAtUtc,
        bool IsActive, DateTime? ConsumedAtUtc, DateTime? RevokedAtUtc, Guid? ConsumedByApprovalBatchId)
    {
        public static AuthorizationFacts From(QuotationReuseAuthorization a) =>
            new(a.Id, a.QuotationItemId, a.SourceApprovalBatchId, a.AuthorizedAtUtc, a.IsActive, a.ConsumedAtUtc, a.RevokedAtUtc, a.ConsumedByApprovalBatchId);
    }

    public sealed record Verdict(
        bool IsEligible, bool IsReuseBlocked, bool IsReuseAuthorized,
        Guid? SourceBatchId, int? SourceBatchNumber, Guid? AuthorizationId, Guid? ConsumedByBatchId, string ReasonCode);

    /// <summary>
    /// Resolves one quotation item. <paramref name="consumingBatchId"/>: when re-validating an EXISTING batch
    /// (update/resubmit), an item whose authorization was consumed BY THAT batch is legitimate (eligible, nothing to consume).
    /// </summary>
    public static Verdict Resolve(
        Guid quotationItemId,
        string? reconciliationStatus,
        IEnumerable<FinishedUse> finishedUses,
        IEnumerable<AuthorizationFacts> authorizations,
        Guid? consumingBatchId = null)
    {
        // Items that never composed a quotation total are not batch candidates anyway.
        if (reconciliationStatus == null || !RequestConstants.ReconciliationStatuses.Considered.Contains(reconciliationStatus))
            return new Verdict(false, false, false, null, null, null, null, Reasons.ItemNotReconciled);

        var last = finishedUses
            .Where(u => u.QuotationItemId == quotationItemId)
            .OrderByDescending(u => u.AtUtc)
            .FirstOrDefault();
        if (last == null)
            return new Verdict(true, false, false, null, null, null, null, Reasons.EligibleNormal);

        var auths = authorizations
            .Where(a => a.QuotationItemId == quotationItemId && a.SourceApprovalBatchId == last.SourceBatchId)
            .OrderByDescending(a => a.AuthorizedAtUtc)
            .ToList();

        if (consumingBatchId.HasValue && auths.Any(a => a.ConsumedByApprovalBatchId == consumingBatchId.Value))
            return new Verdict(true, false, false, last.SourceBatchId, last.SourceBatchNumber, null, consumingBatchId, Reasons.EligibleNormal);

        var active = auths.FirstOrDefault(a => a.IsActive && a.ConsumedAtUtc == null && a.RevokedAtUtc == null);
        if (active != null)
            return new Verdict(true, false, true, last.SourceBatchId, last.SourceBatchNumber, active.Id, null, Reasons.ReuseAuthorized);

        var latest = auths.FirstOrDefault();
        var reason = latest == null ? Reasons.ReuseAuthorizationRequired
            : latest.ConsumedAtUtc != null ? Reasons.AuthorizationConsumed
            : Reasons.AuthorizationRevoked;
        return new Verdict(false, true, false, last.SourceBatchId, last.SourceBatchNumber, latest?.Id, latest?.ConsumedByApprovalBatchId, reason);
    }

    /// <summary>
    /// Finished uses derived from a loaded batch graph (Items + Candidates): every candidate and the winner of a
    /// CANCELLED batch item (dated by the batch creation) and of a RELEASED item of an APPROVED batch (dated by the
    /// release stamp). Mirrors the query-backed derivation of <c>QuotationItemEligibilityService</c>.
    /// </summary>
    public static IReadOnlyList<FinishedUse> FinishedUsesFromLoaded(IEnumerable<ApprovalBatch> batches)
    {
        var uses = new List<FinishedUse>();
        foreach (var b in batches)
        {
            if (b.Status == RequestConstants.ApprovalBatchStatuses.Cancelled)
            {
                foreach (var bi in b.Items)
                {
                    foreach (var c in bi.Candidates) uses.Add(new FinishedUse(c.QuotationItemId, b.Id, b.BatchNumber, b.CreatedAtUtc));
                    if (bi.SelectedQuotationItemId.HasValue) uses.Add(new FinishedUse(bi.SelectedQuotationItemId.Value, b.Id, b.BatchNumber, b.CreatedAtUtc));
                }
            }
            else if (b.Status == RequestConstants.ApprovalBatchStatuses.Approved)
            {
                foreach (var bi in b.Items.Where(i => i.ReleasedAtUtc.HasValue))
                {
                    var at = bi.ReleasedAtUtc!.Value;
                    foreach (var c in bi.Candidates) uses.Add(new FinishedUse(c.QuotationItemId, b.Id, b.BatchNumber, at));
                    if (bi.SelectedQuotationItemId.HasValue) uses.Add(new FinishedUse(bi.SelectedQuotationItemId.Value, b.Id, b.BatchNumber, at));
                }
            }
        }
        return uses.Distinct().ToList();
    }

    /// <summary>
    /// Quotation item ids of a loaded request that are REUSE-BLOCKED right now (finished use without an active
    /// authorization). Requires <c>ApprovalBatches.Items.Candidates</c> and <c>QuotationReuseAuthorizations</c> to be
    /// loaded. Reconciliation is ignored here on purpose: the coverage classifier already restricts candidates to
    /// MAPPED/SUBSTITUTE lines.
    /// </summary>
    public static IReadOnlySet<Guid> ReuseBlockedQuotationItemIds(Request r)
    {
        var uses = FinishedUsesFromLoaded(r.ApprovalBatches);
        if (uses.Count == 0) return new HashSet<Guid>();
        var auths = r.QuotationReuseAuthorizations.Select(AuthorizationFacts.From).ToList();
        var blocked = new HashSet<Guid>();
        foreach (var id in uses.Select(u => u.QuotationItemId).Distinct())
        {
            var v = Resolve(id, RequestConstants.ReconciliationStatuses.Mapped, uses, auths);
            if (v.IsReuseBlocked) blocked.Add(id);
        }
        return blocked;
    }
}
