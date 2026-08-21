using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces.Purchasing;

public interface IStatusAggregationService
{
    /// <summary>
    /// Recomputes the derived Request.StatusId aggregate from the operational units. Every
    /// transition it performs is audited as a STATUS_SYNC RequestStatusHistories row attributed
    /// to <paramref name="actorUserId"/> (the user whose action triggered the aggregation) —
    /// silent aggregate writes are forbidden (REQ-23/07/2026-140 regression class).
    /// </summary>
    Task AggregateRequestStatusAsync(Guid requestId, Guid actorUserId, CancellationToken cancellationToken = default);
}
