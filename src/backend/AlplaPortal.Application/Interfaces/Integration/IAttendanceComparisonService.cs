using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Attendance Comparison Engine — Phase 3 (diagnostic, read-only).
///
/// Compares Innux processed attendance (from Alteracoes) against
/// Portal raw-punch interpretation (from TerminaisMarcacoes) for a given
/// employee/date or date range. Identifies discrepancies, assigns severity,
/// and recommends review actions.
///
/// This service is strictly diagnostic:
/// - Does not replace the current Innux-based HR calendar.
/// - Does not write to Innux or Primavera.
/// - Attendance evidence comes exclusively from PortalPunchInterpreter.
/// - Schedule context from Alteracoes.IDHorario is a fallback for Escala plans,
///   NOT proof of attendance.
/// </summary>
public interface IAttendanceComparisonService
{
    /// <summary>
    /// Compares Innux processed result vs Portal interpretation for one employee on one day.
    /// </summary>
    /// <param name="innuxEmployeeId">Innux IDFuncionario.</param>
    /// <param name="date">Target date.</param>
    Task<AttendanceComparisonResultDto> CompareEmployeeDayAsync(
        int innuxEmployeeId, DateTime date);

    /// <summary>
    /// Compares Innux vs Portal for a date range, optionally filtered by employee or department.
    /// Maximum date range: 31 days.
    /// </summary>
    /// <param name="startDate">Inclusive start date.</param>
    /// <param name="endDate">Inclusive end date.</param>
    /// <param name="innuxEmployeeId">Optional: filter to a single employee.</param>
    /// <param name="departmentId">Optional: filter to employees in a Portal department.</param>
    /// <param name="onlyDiscrepancies">If true, only returns results where HasDiscrepancy = true.</param>
    Task<DateRangeComparisonResultDto> CompareDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        int? innuxEmployeeId = null,
        int? departmentId = null,
        bool onlyDiscrepancies = false);
}
