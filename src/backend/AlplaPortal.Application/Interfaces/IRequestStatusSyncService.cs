using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// Syncs Request.StatusId and computes DisplayWorkflowState for QUOTATION requests
/// based on the batch/item/group lifecycle — not the legacy single-status model.
/// 
/// Request.StatusId is preserved for legacy compatibility (filtering, PAYMENT flow).
/// DisplayWorkflowState is the computed source of truth for UI display.
/// </summary>
public interface IRequestStatusSyncService
{
    /// <summary>
    /// Recalculates Request.StatusId based on current batch and item states.
    /// Called after batch creation, approval actions, and PO group transitions.
    /// Does NOT call SaveChangesAsync — caller is responsible for persisting.
    /// </summary>
    Task SyncStatusAsync(Guid requestId, Guid actorId);

    /// <summary>
    /// Computes the DisplayWorkflowState for a QUOTATION request.
    /// This is a read-only computation, not persisted in DB.
    /// For PAYMENT requests, returns the legacy status code as-is.
    /// </summary>
    Task<string> ComputeDisplayWorkflowStateAsync(Guid requestId);

    /// <summary>
    /// Lightweight overload that computes DisplayWorkflowState from pre-loaded data.
    /// Used in list projections to avoid N+1 queries.
    /// </summary>
    string ComputeDisplayWorkflowState(
        string requestTypeCode,
        string currentStatusCode,
        IReadOnlyList<RequestLineItem> lineItems,
        IReadOnlyList<ApprovalBatch> batches,
        IReadOnlyList<RequestPoGroup> poGroups);
}
