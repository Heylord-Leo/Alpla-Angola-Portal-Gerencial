using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

public class StatusAggregationService : IStatusAggregationService
{
    private readonly ApplicationDbContext _context;

    public StatusAggregationService(ApplicationDbContext context)
    {
        _context = context;
    }

    private static readonly Dictionary<string, int> _statusPriorityMap = new()
    {
        // 1. Initial State for a Group
        { RequestConstants.Statuses.FinalApproved, 10 },
        { RequestConstants.Statuses.WaitingPoCorrection, 20 },
        
        // 2. Buy-to-Pay (Advance Payment steps before PO Issuance or after, depending on workflow, but typically upfront)
        { RequestConstants.Statuses.AdvancePaymentRequired, 24 },
        { RequestConstants.Statuses.AdvancePaymentScheduled, 25 },
        { RequestConstants.Statuses.AdvancePaymentCompleted, 26 },
        { RequestConstants.Statuses.WaitingSupplierDelivery, 27 },

        // 3. PO Issuance
        { RequestConstants.Statuses.PoIssued, 30 },

        // 4. Invoicing / Payment (Post-Paid or Final Payment)
        { RequestConstants.Statuses.PaymentRequestSent, 40 },
        { RequestConstants.Statuses.PaymentScheduled, 50 },
        { RequestConstants.Statuses.PaymentCompleted, 60 },

        // 5. Receiving / Reconciliation
        { RequestConstants.Statuses.WaitingReceipt, 70 },
        { RequestConstants.Statuses.WaitingReconciliation, 80 },

        // 6. Follow-up
        { RequestConstants.Statuses.InFollowup, 90 },

        // 7. Terminal
        { RequestConstants.Statuses.Completed, 100 },
        { RequestConstants.Statuses.Cancelled, 999 } // Cancelled groups shouldn't block others, they are conceptually "done"
    };

    public async Task AggregateRequestStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _context.Requests
            .Include(r => r.PoGroups)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
            return;

        if (!request.PoGroups.Any())
            return;

        // Determine priority based on map. Lower number = further behind.
        var activeGroups = request.PoGroups.Where(g => g.Status != RequestConstants.Statuses.Cancelled).ToList();

        if (!activeGroups.Any())
            return; // All cancelled, nothing to aggregate (or fallback to cancelled)

        var furthestBehindGroup = activeGroups
            .OrderBy(g => _statusPriorityMap.TryGetValue(g.Status, out var p) ? p : 9999) // unknown statuses treated as lowest priority (highest number)
            .First();

        // Fetch the corresponding RequestStatus entity
        var statusEntity = await _context.RequestStatuses
            .FirstOrDefaultAsync(s => s.Code == furthestBehindGroup.Status, cancellationToken);

        if (statusEntity != null && request.StatusId != statusEntity.Id)
        {
            request.StatusId = statusEntity.Id;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
