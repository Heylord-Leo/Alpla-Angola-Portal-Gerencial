using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
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
        return await _context.ApprovalBatchItems.AsNoTracking()
            .Where(abi => abi.ApprovalBatch.RequestId == requestId
                       && abi.ApprovalBatch.Status == RequestConstants.ApprovalBatchStatuses.Cancelled)
            .Select(abi => new CancelledUse(
                abi.SelectedQuotationItemId,
                abi.ApprovalBatchId,
                abi.ApprovalBatch.BatchNumber,
                abi.ApprovalBatch.CreatedAtUtc))
            .ToListAsync();
    }

    private async Task<List<Domain.Entities.QuotationReuseAuthorization>> LoadAuthorizationsAsync(Guid requestId)
    {
        return await _context.QuotationReuseAuthorizations.AsNoTracking()
            .Where(a => a.RequestId == requestId)
            .ToListAsync();
    }

    private static QuotationItemEligibility Resolve(
        Guid quotationItemId,
        Guid quotationId,
        string reconciliationStatus,
        List<CancelledUse> cancelledUses,
        List<Domain.Entities.QuotationReuseAuthorization> authorizations,
        Guid? consumingBatchId)
    {
        // Items that never composed a quotation total are not batch candidates anyway.
        var reconciled = RequestConstants.ReconciliationStatuses.Considered.Contains(reconciliationStatus);
        if (!reconciled)
        {
            return new QuotationItemEligibility
            {
                QuotationItemId = quotationItemId,
                QuotationId = quotationId,
                IsEligible = false,
                ReasonCode = QuotationItemEligibilityReasons.ItemNotReconciled
            };
        }

        // Most recent cancelled use governs the reuse cycle (a new cancelled use of an already
        // reauthorized item opens a NEW cycle requiring a NEW authorization).
        var lastCancelledUse = cancelledUses
            .Where(u => u.QuotationItemId == quotationItemId)
            .OrderByDescending(u => u.BatchCreatedAtUtc)
            .FirstOrDefault();

        if (lastCancelledUse == null)
        {
            return new QuotationItemEligibility
            {
                QuotationItemId = quotationItemId,
                QuotationId = quotationId,
                IsEligible = true,
                ReasonCode = QuotationItemEligibilityReasons.EligibleNormal
            };
        }

        var auths = authorizations
            .Where(a => a.QuotationItemId == quotationItemId && a.SourceApprovalBatchId == lastCancelledUse.BatchId)
            .OrderByDescending(a => a.AuthorizedAtUtc)
            .ToList();

        // Re-validation of the batch that ALREADY consumed this authorization (update/resubmit):
        // the item legitimately belongs to that batch — neither blocked nor to-consume again.
        if (consumingBatchId.HasValue &&
            auths.Any(a => a.ConsumedByApprovalBatchId == consumingBatchId.Value))
        {
            return new QuotationItemEligibility
            {
                QuotationItemId = quotationItemId,
                QuotationId = quotationId,
                IsEligible = true,
                SourceCancelledBatchId = lastCancelledUse.BatchId,
                SourceCancelledBatchNumber = lastCancelledUse.BatchNumber,
                ReuseConsumedByBatchId = consumingBatchId,
                ReasonCode = QuotationItemEligibilityReasons.EligibleNormal
            };
        }

        var active = auths.FirstOrDefault(a => a.IsActive && a.ConsumedAtUtc == null && a.RevokedAtUtc == null);
        if (active != null)
        {
            return new QuotationItemEligibility
            {
                QuotationItemId = quotationItemId,
                QuotationId = quotationId,
                IsEligible = true,
                IsReuseAuthorized = true,
                SourceCancelledBatchId = lastCancelledUse.BatchId,
                SourceCancelledBatchNumber = lastCancelledUse.BatchNumber,
                ReuseAuthorizationId = active.Id,
                ReasonCode = QuotationItemEligibilityReasons.ReuseAuthorized
            };
        }

        // Blocked — distinguish why for diagnostics (consumed vs revoked vs never authorized).
        var latest = auths.FirstOrDefault();
        var reason = latest == null
            ? QuotationItemEligibilityReasons.ReuseAuthorizationRequired
            : latest.ConsumedAtUtc != null
                ? QuotationItemEligibilityReasons.AuthorizationConsumed
                : QuotationItemEligibilityReasons.AuthorizationRevoked;

        return new QuotationItemEligibility
        {
            QuotationItemId = quotationItemId,
            QuotationId = quotationId,
            IsEligible = false,
            IsReuseBlocked = true,
            SourceCancelledBatchId = lastCancelledUse.BatchId,
            SourceCancelledBatchNumber = lastCancelledUse.BatchNumber,
            ReuseAuthorizationId = latest?.Id,
            ReuseConsumedByBatchId = latest?.ConsumedByApprovalBatchId,
            ReasonCode = reason
        };
    }
}
