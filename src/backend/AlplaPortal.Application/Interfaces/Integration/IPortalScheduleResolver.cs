using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Resolves which Innux schedule applies to an employee on a specific date.
///
/// Computes the work plan cycle day index and loads the corresponding schedule
/// definition with its mandatory/optional periods, expected times, and overnight flag.
///
/// Data path: Funcionarios → PlanosTrabalho → PlanosTrabalhoHorarios → Horarios → HorariosPeriodos.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public interface IPortalScheduleResolver
{
    /// <summary>
    /// Resolves the expected schedule for a given employee on a given date.
    /// </summary>
    /// <param name="innuxEmployeeId">Innux IDFuncionario.</param>
    /// <param name="date">Target date for schedule resolution.</param>
    /// <returns>
    /// Fully resolved schedule with work plan, schedule definition, periods,
    /// expected entry/exit times, expected minutes, and overnight flag.
    /// Returns null if the employee has no work plan assigned.
    /// </returns>
    Task<ResolvedScheduleDto?> ResolveScheduleForDateAsync(int innuxEmployeeId, DateTime date);
}
