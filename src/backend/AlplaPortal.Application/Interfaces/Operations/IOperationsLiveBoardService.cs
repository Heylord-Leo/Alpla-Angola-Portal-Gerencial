using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Domain.Enums;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Service interface for the Operations Live Transfer Board.
///
/// Returns a TV-ready response with pre-classified inbound/outbound
/// transfer cards for a specific plant, optimized for 60-second refresh.
///
/// Design reference: docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md §9–§10
/// </summary>
public interface IOperationsLiveBoardService
{
    /// <summary>
    /// Returns the Live Board data for a specific plant.
    ///
    /// The response includes pre-classified inbound/outbound transfers,
    /// simplified mini-timeline steps, attention flags, and summary counters.
    /// </summary>
    /// <param name="plant">Target plant (VIANA1, VIANA2, VIANA3).</param>
    /// <param name="refreshSeconds">Suggested refresh interval (30–300). Default: 60.</param>
    /// <param name="maxInbound">Maximum inbound cards to return (1–12). Default: 6.</param>
    /// <param name="maxOutbound">Maximum outbound cards to return (1–12). Default: 6.</param>
    /// <param name="includeRecentlyCompleted">Include completed transfers within the window. Default: true.</param>
    /// <param name="completedWindowHours">Hours to keep completed transfers visible (1–24). Default: 4.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Live Board response with all sections populated.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the integration is disabled, the plant is not configured,
    /// or required credentials are missing.
    /// </exception>
    Task<OperationsLiveBoardResponseDto> GetLiveBoardAsync(
        AlplaProdPlant plant,
        int refreshSeconds = 60,
        int maxInbound = 6,
        int maxOutbound = 6,
        bool includeRecentlyCompleted = true,
        int completedWindowHours = 4,
        CancellationToken ct = default);
}
