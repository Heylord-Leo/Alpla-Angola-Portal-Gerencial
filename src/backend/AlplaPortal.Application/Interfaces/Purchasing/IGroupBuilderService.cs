using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces.Purchasing;

public interface IGroupBuilderService
{
    Task BuildGroupsForRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds PO groups scoped to a single ApprovalBatch.
    /// Groups only items inside the batch (including approved extras).
    /// Winners come from ApprovalBatchItem.SelectedQuotationItemId.
    /// Groups are tagged with ApprovalBatchId and set to PENDING.
    /// Does not merge groups across batches.
    /// </summary>
    Task BuildGroupsForBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
}
