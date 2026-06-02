using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Domain.Enums;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Service interface for querying transfer timelines from AlplaPROD.
///
/// Implementations orchestrate connection management, pipeline detection,
/// SQL execution, and status mapping to return normalized timeline data.
/// </summary>
public interface IOperationsTimelineService
{
    /// <summary>
    /// Returns the ordered timeline of logistics events for a specific
    /// purchase order (IdBestellung) in the given AlplaPROD plant.
    /// </summary>
    /// <param name="plant">Target plant (VIANA1, VIANA2, VIANA3).</param>
    /// <param name="idBestellung">AlplaPROD purchase order ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Timeline response with ordered events and metadata.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the integration is disabled, the plant is not configured,
    /// or required credentials are missing.
    /// </exception>
    Task<OperationsTimelineResponseDto> GetTimelineAsync(
        AlplaProdPlant plant, int idBestellung, CancellationToken ct = default);
}
