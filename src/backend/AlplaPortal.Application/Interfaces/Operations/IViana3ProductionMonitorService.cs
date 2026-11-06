using AlplaPortal.Application.DTOs.Operations;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Service interface for the Viana 3 Production Monitor (TV page).
///
/// Read-only projection of AlplaPROD_aovia3: current lot + two previous
/// production lots per machine, Warehouse 3 preform stock, AlplaLABEL Waste,
/// provisional deviation. Caches the last successful result so the TV keeps
/// showing figures (marked STALE) when AlplaPROD is unavailable.
///
/// Design reference: docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md
/// </summary>
public interface IViana3ProductionMonitorService
{
    /// <summary>
    /// Returns the monitor payload. A payload younger than <paramref name="refreshSeconds"/>
    /// is served from cache; otherwise AlplaPROD is queried. When the query fails and a
    /// previous snapshot exists, that snapshot is returned with <c>source.status = STALE</c>.
    /// </summary>
    /// <param name="refreshSeconds">Client refresh window (60–300).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Integration disabled / plant not configured and no snapshot available.</exception>
    Task<Viana3MonitorResponseDto> GetMonitorAsync(int refreshSeconds, CancellationToken ct = default);

    /// <summary>
    /// Returns the diagnostic payload (historical open system balance at machine areas, Waste on
    /// deleted lots). Cached for one hour; same stale-snapshot behaviour as the monitor.
    /// </summary>
    Task<Viana3MonitorDiagnosticsResponseDto> GetDiagnosticsAsync(CancellationToken ct = default);
}
