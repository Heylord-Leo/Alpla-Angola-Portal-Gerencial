using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces.Purchasing;

public interface IStatusAggregationService
{
    Task AggregateRequestStatusAsync(Guid requestId, CancellationToken cancellationToken = default);
}
