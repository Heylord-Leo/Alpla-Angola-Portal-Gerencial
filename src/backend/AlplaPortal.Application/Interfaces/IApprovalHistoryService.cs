using AlplaPortal.Application.DTOs.Approvals;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 2. Read-only approval history + audit timeline over
/// RequestStatusHistories. All request selection is caller-scoped: the controller passes the set of
/// request ids the current user may see (from GetScopedRequestsQuery), so the service can never widen
/// visibility. No writes, no persistence.
/// </summary>
public interface IApprovalHistoryService
{
    Task<ApprovalHistoryPageDto> GetHistoryAsync(
        IReadOnlyCollection<Guid> scopedRequestIds,
        ApprovalHistoryFilter filter,
        int page,
        int pageSize,
        string sort,
        CancellationToken ct = default);

    /// <summary>Same filters/scope/sort as the table; capped for a single export file.</summary>
    Task<List<ApprovalHistoryRowDto>> GetExportRowsAsync(
        IReadOnlyCollection<Guid> scopedRequestIds,
        ApprovalHistoryFilter filter,
        string sort,
        CancellationToken ct = default);

    /// <summary>Chronological approval-relevant events for ONE request (caller must have scope-checked it).</summary>
    Task<List<ApprovalTimelineEventDto>> GetTimelineAsync(Guid requestId, CancellationToken ct = default);
}

/// <summary>
/// v2.244.0 Phase 3 — read-only approval analytics (SLA/duration, bottleneck, approver performance,
/// decision mix, trend) over RequestStatusHistories. Caller-scoped like the history endpoints.
/// </summary>
public interface IApprovalAnalyticsService
{
    Task<AlplaPortal.Application.DTOs.Approvals.ApprovalAnalyticsDto> GetAnalyticsAsync(
        IReadOnlyCollection<Guid> scopedRequestIds,
        ApprovalHistoryFilter filter,
        string resolution,
        CancellationToken ct = default);
}
