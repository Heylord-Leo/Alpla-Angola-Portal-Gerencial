using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Innux employee lookup — read-only, single database.
///
/// Unlike Primavera, Innux has a single database target, so no company
/// parameter is needed. The connection is resolved via InnuxConnectionFactory.
///
/// This interface is Innux-specific and remains separate from the generic
/// provider contract. It provides operational identity lookup only —
/// no attendance events, no biometric reconciliation, no sync.
/// </summary>
public interface IInnuxEmployeeService
{
    /// <summary>
    /// Retrieves a single Innux employee by their employee number.
    /// Returns null if the employee is not found.
    /// </summary>
    Task<InnuxEmployeeDto?> GetEmployeeByNumberAsync(string number);

    /// <summary>
    /// Searches Innux employees by partial name match.
    /// Requires at least 2 characters. Results are bounded by limit (max 50).
    /// </summary>
    Task<IEnumerable<InnuxEmployeeDto>> SearchEmployeesByNameAsync(string nameQuery, int limit = 50);
}
