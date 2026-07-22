using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

public class StatusAggregationService : IStatusAggregationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StatusAggregationService> _logger;

    public StatusAggregationService(ApplicationDbContext context, ILogger<StatusAggregationService> logger)
    {
        _context = context;
        _logger = logger;
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

        var statusEntity = await _context.RequestStatuses
            .FirstOrDefaultAsync(s => s.Code == result.StatusCode, cancellationToken);

        if (statusEntity != null && request.StatusId != statusEntity.Id)
        {
            request.StatusId = statusEntity.Id;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
