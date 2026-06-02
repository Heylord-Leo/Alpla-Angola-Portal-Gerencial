using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Domain.Enums;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Service interface for listing transfers/purchase orders from AlplaPROD.
///
/// Separated from <see cref="IOperationsTimelineService"/> because listing
/// uses different SQL queries (paginated SELECT with joins) versus the
/// timeline (UNION ALL across multiple entity tables).
/// </summary>
public interface IOperationsTransferListService
{
    /// <summary>
    /// Returns a paginated list of transfers/purchase orders for the given plant
    /// within the specified date range.
    /// </summary>
    /// <param name="plant">Target plant (VIANA1, VIANA2, VIANA3).</param>
    /// <param name="dateFrom">Start date (inclusive) for Add_Date filter.</param>
    /// <param name="dateTo">End date (inclusive — the whole final day is included).</param>
    /// <param name="status">Optional status filter: ACTIVE, COMPLETED, CANCELLED.</param>
    /// <param name="pipelineModelFilter">Optional pipeline model filter (not used in single-plant queries).</param>
    /// <param name="articleSearch">Optional article name/alias search (LIKE).</param>
    /// <param name="poSearch">Optional PO ID / JournalNummer search (LIKE).</param>
    /// <param name="page">Page number (1-based). Default: 1.</param>
    /// <param name="pageSize">Items per page. Default: 25, max: 100.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated response with transfer items and metadata.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the integration is disabled, the plant is not configured,
    /// or required credentials are missing.
    /// </exception>
    Task<OperationsTransferListResponseDto> GetTransferListAsync(
        AlplaProdPlant plant,
        DateTime dateFrom,
        DateTime dateTo,
        string? status,
        string? pipelineModelFilter,
        string? articleSearch,
        string? poSearch,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
