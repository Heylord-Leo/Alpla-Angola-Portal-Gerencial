using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Domain.Enums;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Service interface for querying transfer details from AlplaPROD.
///
/// Returns enriched detail data for a single purchase order including
/// header, material, quantity, loading, goods receipt, and technical references.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §9 (Phase 6)
/// </summary>
public interface IOperationsTransferDetailService
{
    /// <summary>
    /// Returns detailed information for a specific purchase order (IdBestellung)
    /// in the given AlplaPROD plant.
    /// </summary>
    /// <param name="plant">Target plant (VIANA1, VIANA2, VIANA3).</param>
    /// <param name="idBestellung">AlplaPROD purchase order ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detail response with all sections populated. Null if PO not found.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the integration is disabled, the plant is not configured,
    /// or required credentials are missing.
    /// </exception>
    Task<OperationsTransferDetailDto?> GetTransferDetailAsync(
        AlplaProdPlant plant, int idBestellung, CancellationToken ct = default);
}
