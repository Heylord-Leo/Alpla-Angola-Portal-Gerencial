using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Innux schedule/shift/work plan reference data — read-only.
///
/// Provides consultation access to Innux master data for schedules (Horarios),
/// work plans (PlanosTrabalho), cycle composition (PlanosTrabalhoHorarios),
/// and period definitions (HorariosPeriodos).
///
/// These are low-volume reference tables suitable for long-TTL caching (1 hour).
/// All methods are scope-agnostic — schedule master data is shared across all employees.
/// Employee list scoping is applied at the controller layer, not here.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public interface IInnuxScheduleService
{
    /// <summary>
    /// Retrieves all work plans with employee counts and cycle composition.
    /// Results are cached for 1 hour.
    /// </summary>
    Task<IEnumerable<WorkPlanListItemDto>> GetWorkPlansAsync();

    /// <summary>
    /// Retrieves all schedule definitions with their period breakdowns.
    /// Results are cached for 1 hour.
    /// </summary>
    Task<IEnumerable<ScheduleListItemDto>> GetSchedulesAsync();

    /// <summary>
    /// Retrieves all employees assigned to a specific work plan from Innux.
    /// NOT scope-filtered — the controller must apply Portal ACL before returning results.
    /// </summary>
    /// <param name="planId">Innux IDPlanoTrabalho value.</param>
    Task<IEnumerable<PlanEmployeeDto>> GetPlanEmployeesAsync(int planId);
}
