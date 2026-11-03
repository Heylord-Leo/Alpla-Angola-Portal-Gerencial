using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>
/// Implements the cancelled-batch reuse rule (Option C) as the single source of truth.
///
/// Blocking condition (derived — no backfill needed): the quotation item appears as
/// SelectedQuotationItemId in an ApprovalBatchItem whose parent batch is CANCELLED for the same
/// request. Unblocking condition: an active (IsActive, unconsumed, unrevoked) authorization for
/// that EXACT (item, source cancelled batch) pair. A later cancelled use (new source batch)
/// requires a brand-new authorization.
/// </summary>
public class QuotationItemEligibilityService : IQuotationItemEligibilityService
{
    private readonly ApplicationDbContext _context;

    public QuotationItemEligibilityService(ApplicationDbContext context)
    {
        _context = context;
    }

    private sealed record CancelledUse(Guid QuotationItemId, Guid BatchId, int BatchNumber, DateTime BatchCreatedAtUtc);

    public async Task<IReadOnlyDictionary<Guid, QuotationItemEligibility>> GetEligibilityMapAsync(Guid requestId)
    {
        var items = await _context.QuotationItems.AsNoTracking()
            .Where(qi => qi.Quotation.RequestId == requestId)
            .Select(qi => new { qi.Id, qi.QuotationId, qi.ReconciliationStatus })
            .ToListAsync();

        var cancelledUses = await LoadCancelledUsesAsync(requestId);
        var authorizations = await LoadAuthorizationsAsync(requestId);

        var map = new Dictionary<Guid, QuotationItemEligibility>();
        foreach (var qi in items)
        {
            map[qi.Id] = Resolve(qi.Id, qi.QuotationId, qi.ReconciliationStatus, cancelledUses, authorizations, consumingBatchId: null);
        }
        return map;
    }

    public async Task<(IReadOnlyList<QuotationItemEligibility> Blocked, IReadOnlyList<QuotationItemEligibility> AuthorizedToConsume)>
        ValidateSelectionAsync(Guid requestId, IEnumerable<Guid> quotationItemIds, Guid? consumingBatchId = null)
    {
        var ids = quotationItemIds.Distinct().ToList();
        if (ids.Count == 0)
            return (Array.Empty<QuotationItemEligibility>(), Array.Empty<QuotationItemEligibility>());

        var items = await _context.QuotationItems.AsNoTracking()
            .Where(qi => ids.Contains(qi.Id) && qi.Quotation.RequestId == requestId)
            .Select(qi => new { qi.Id, qi.QuotationId, qi.ReconciliationStatus })
            .ToListAsync();

        var cancelledUses = await LoadCancelledUsesAsync(requestId);
        var authorizations = await LoadAuthorizationsAsync(requestId);

        var blocked = new List<QuotationItemEligibility>();
        var authorized = new List<QuotationItemEligibility>();
        foreach (var qi in items)
        {
            var e = Resolve(qi.Id, qi.QuotationId, qi.ReconciliationStatus, cancelledUses, authorizations, consumingBatchId);
            if (e.IsReuseBlocked) blocked.Add(e);
            else if (e.IsReuseAuthorized) authorized.Add(e);
        }
        return (blocked, authorized);
    }

    private async Task<List<CancelledUse>> LoadCancelledUsesAsync(Guid requestId)
    {
        // Candidate model: a cancelled batch "used" every candidate option it put forward (the
        // batch may have been cancelled before any winner existed). Legacy batch items have no
        // candidate rows, so their buyer-selected winner remains the derived use.
        var candidateUses = await _context.ApprovalBatchItemCandidates.AsNoTracking()
            .Where(c => c.ApprovalBatchItem.ApprovalBatch.RequestId == requestId
                     && c.ApprovalBatchItem.ApprovalBatch.Status == RequestConstants.ApprovalBatchStatuses.Cancelled)
            .Select(c => new CancelledUse(
                c.QuotationItemId,
                c.ApprovalBatchItem.ApprovalBatchId,
                c.ApprovalBatchItem.ApprovalBatch.BatchNumber,
                c.ApprovalBatchItem.ApprovalBatch.CreatedAtUtc))
            .ToListAsync();

        var winnerUses = await _context.ApprovalBatchItems.AsNoTracking()
            .Where(abi => abi.ApprovalBatch.RequestId == requestId
                       && abi.ApprovalBatch.Status == RequestConstants.ApprovalBatchStatuses.Cancelled
                       && abi.SelectedQuotationItemId != null)
            .Select(abi => new CancelledUse(
                abi.SelectedQuotationItemId!.Value,
                abi.ApprovalBatchId,
                abi.ApprovalBatch.BatchNumber,
                abi.ApprovalBatch.CreatedAtUtc))
            .ToListAsync();

        // Phase 1 P.O. cancellation (D3): a RELEASED item of an APPROVED batch is a finished use too — its candidates
        // (and its winner) need an explicit reuse authorization before they can enter a new batch, exactly like the
        // candidates of a CANCELLED batch. The release stamp, not the batch creation, dates the cycle.
        var releasedCandidateUses = await _context.ApprovalBatchItemCandidates.AsNoTracking()
            .Where(c => c.ApprovalBatchItem.ApprovalBatch.RequestId == requestId
                     && c.ApprovalBatchItem.ApprovalBatch.Status == RequestConstants.ApprovalBatchStatuses.Approved
                     && c.ApprovalBatchItem.ReleasedAtUtc != null)
            .Select(c => new CancelledUse(
                c.QuotationItemId,
                c.ApprovalBatchItem.ApprovalBatchId,
                c.ApprovalBatchItem.ApprovalBatch.BatchNumber,
                c.ApprovalBatchItem.ReleasedAtUtc!.Value))
            .ToListAsync();
        var releasedWinnerUses = await _context.ApprovalBatchItems.AsNoTracking()
            .Where(abi => abi.ApprovalBatch.RequestId == requestId
                       && abi.ApprovalBatch.Status == RequestConstants.ApprovalBatchStatuses.Approved
                       && abi.ReleasedAtUtc != null
                       && abi.SelectedQuotationItemId != null)
            .Select(abi => new CancelledUse(
                abi.SelectedQuotationItemId!.Value,
                abi.ApprovalBatchId,
                abi.ApprovalBatch.BatchNumber,
                abi.ReleasedAtUtc!.Value))
            .ToListAsync();

        return candidateUses.Concat(winnerUses).Concat(releasedCandidateUses).Concat(releasedWinnerUses).Distinct().ToList();
    }

    private async Task<List<Domain.Entities.QuotationReuseAuthorization>> LoadAuthorizationsAsync(Guid requestId)
    {
        return await _context.QuotationReuseAuthorizations.AsNoTracking()
            .Where(a => a.RequestId == requestId)
            .ToListAsync();
    }

    /// <summary>v2.249.2 — the rule itself lives in the pure Domain policy (shared with the Buyer queue/Workspace coverage
    /// projection); this method only adapts the query rows to it and the verdict to the API DTO.</summary>
    private static QuotationItemEligibility Resolve(
        Guid quotationItemId,
        Guid quotationId,
        string reconciliationStatus,
        List<CancelledUse> cancelledUses,
        List<Domain.Entities.QuotationReuseAuthorization> authorizations,
        Guid? consumingBatchId)
    {
        var v = QuotationReuseEligibilityPolicy.Resolve(
            quotationItemId,
            reconciliationStatus,
            cancelledUses.Select(u => new QuotationReuseEligibilityPolicy.FinishedUse(u.QuotationItemId, u.BatchId, u.BatchNumber, u.BatchCreatedAtUtc)),
            authorizations.Select(QuotationReuseEligibilityPolicy.AuthorizationFacts.From),
            consumingBatchId);
        return new QuotationItemEligibility
        {
            QuotationItemId = quotationItemId,
            QuotationId = quotationId,
            IsEligible = v.IsEligible,
            IsReuseBlocked = v.IsReuseBlocked,
            IsReuseAuthorized = v.IsReuseAuthorized,
            SourceCancelledBatchId = v.SourceBatchId,
            SourceCancelledBatchNumber = v.SourceBatchNumber,
            ReuseAuthorizationId = v.AuthorizationId,
            ReuseConsumedByBatchId = v.ConsumedByBatchId,
            ReasonCode = v.ReasonCode,
        };
    }
}
