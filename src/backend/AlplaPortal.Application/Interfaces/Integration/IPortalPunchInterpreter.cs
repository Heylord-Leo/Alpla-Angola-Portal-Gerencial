using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Interprets raw terminal punches from TerminaisMarcacoes for a given employee and date.
///
/// This service does NOT depend on Alteracoes (processed results) as a source of truth.
/// It reads raw punches directly and applies Portal-side interpretation rules to:
///   - Infer Entry/Exit directions from standard codes, alternate codes (17/18), and empty directions
///   - Flag (not remove) duplicate punches for audit transparency
///   - Match punches into Entry/Exit pairs
///   - Calculate total worked minutes
///   - Assign confidence levels and generate diagnostic warnings
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public interface IPortalPunchInterpreter
{
    /// <summary>
    /// Interprets raw terminal punches for an employee on a given date.
    /// </summary>
    /// <param name="innuxEmployeeId">Innux IDFuncionario.</param>
    /// <param name="date">Target date for punch interpretation.</param>
    /// <param name="schedule">
    /// Optional pre-resolved schedule. If null, the interpreter will call
    /// IPortalScheduleResolver internally to resolve the schedule.
    /// </param>
    /// <returns>
    /// Full interpretation result including all raw punches (with duplicates preserved and flagged),
    /// matched pairs, worked minutes, confidence level, warnings, and applied rules.
    /// </returns>
    Task<PunchInterpretationResultDto> InterpretPunchesAsync(
        int innuxEmployeeId, DateTime date, ResolvedScheduleDto? schedule = null);
}
