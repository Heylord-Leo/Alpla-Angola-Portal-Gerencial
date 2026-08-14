using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

public class StatusAggregationService : IStatusAggregationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StatusAggregationService> _logger;
    private readonly PostPaymentCompletionOptions? _postPaymentOptions;

    /// <summary>
    /// Options are optional-by-default so the many existing direct constructions (tests) keep
    /// compiling and behave exactly as before Phase 4; DI always supplies the registered
    /// options in production. Null options mean "Phase 4 disabled" — the legacy behaviour.
    /// </summary>
    public StatusAggregationService(
        ApplicationDbContext context,
        ILogger<StatusAggregationService> logger,
        IOptions<PostPaymentCompletionOptions>? postPaymentOptions = null)
    {
        _context = context;
        _logger = logger;
        _postPaymentOptions = postPaymentOptions?.Value;
    }

    public async Task AggregateRequestStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems.Where(li => !li.IsDeleted))
            .Include(r => r.ApprovalBatches)
            .Include(r => r.PoGroups)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
            return;

        if (!request.PoGroups.Any())
            return;

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        if (result.IssueCode.HasValue)
        {
            _logger.LogWarning(
                "AggregateRequestStatusAsync: Request {RequestId} — {IssueCode}. Affected PO groups: {GroupIds}",
                requestId, result.IssueCode, result.AffectedPoGroupIds);
        }

        if (request.Status?.Code == result.StatusCode)
            return;

        // ── Phase 4C competing-writer safeguard ──
        // While the Phase 4 completion lifecycle is active, a grouped request reaches COMPLETED
        // exclusively through RequestCompletionService.EvaluateParentCompletionAsync — which
        // assigns CompletionCycleId, writes REQUEST_COMPLETED history and correlates the
        // RequestFinalized notification under RowVersion protection. Aggregation projecting
        // "all groups COMPLETED" must therefore never be the FIRST completing writer: it defers
        // and lets the next Phase-2 evaluation perform the audited transition. Once the request
        // IS completed, aggregation naturally reaffirms it (the equality early-return above).
        // With the feature off (or options absent — direct construction), legacy behaviour is
        // byte-identical.
        if (result.StatusCode == RequestConstants.Statuses.Completed &&
            _postPaymentOptions != null &&
            !PostPaymentCompletionPolicy.IsCompletionDisabled(_postPaymentOptions))
        {
            _logger.LogInformation(
                "AggregateRequestStatusAsync: Request {RequestId} projects COMPLETED; deferred to " +
                "the post-payment completion service (aggregation is never the first completing writer).",
                requestId);
            return;
        }

        var statusEntity = await _context.RequestStatuses
            .FirstOrDefaultAsync(s => s.Code == result.StatusCode, cancellationToken);

        if (statusEntity != null && request.StatusId != statusEntity.Id)
        {
            request.StatusId = statusEntity.Id;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
