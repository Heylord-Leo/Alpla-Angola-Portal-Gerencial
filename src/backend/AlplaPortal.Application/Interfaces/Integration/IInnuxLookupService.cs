using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Innux lookup/reference data — read-only.
///
/// Provides absence codes (CodigosAusencia) and work codes (CodigosTrabalho)
/// for use in attendance visualization and calendar rendering.
/// These are low-volume, rarely-changing reference tables suitable for
/// long-TTL caching (1 hour recommended).
///
/// This service is scope-agnostic — lookup codes are shared across
/// all employees and do not require access control filtering.
/// </summary>
public interface IInnuxLookupService
{
    /// <summary>
    /// Retrieves all absence codes from Innux.
    /// Results are cached for 1 hour.
    /// </summary>
    Task<IEnumerable<InnuxAbsenceCodeDto>> GetAbsenceCodesAsync();

    /// <summary>
    /// Retrieves all work codes from Innux.
    /// Results are cached for 1 hour.
    /// </summary>
    Task<IEnumerable<InnuxWorkCodeDto>> GetWorkCodesAsync();
}
