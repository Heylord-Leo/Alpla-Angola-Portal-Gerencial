using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Innux processed attendance data — read-only.
///
/// Queries dbo.Alteracoes (processed daily attendance) and related tables.
/// All methods accept pre-scoped Innux employee IDs — this service does NOT
/// apply Portal ACL or scope filtering. Scoping is resolved at the controller
/// layer using GetScopedEmployeesQuery() → InnuxEmployeeId extraction.
///
/// Scope constraints:
/// - Read-only: SELECT only, parameterized queries
/// - Date-range filtering required for all transactional queries
/// - LEFT JOIN for all Innux reference tables (no FK constraints exist)
/// - Time values converted from Innux datetime-as-duration in the service layer
/// </summary>
public interface IInnuxAttendanceService
{
    /// <summary>
    /// Retrieves daily attendance summaries for a set of employees within a date range.
    /// Returns one AttendanceDaySummaryDto per employee per day.
    /// Used for calendar grid rendering.
    /// </summary>
    /// <param name="innuxEmployeeIds">Pre-scoped Innux employee IDs.</param>
    /// <param name="startDate">Inclusive start date.</param>
    /// <param name="endDate">Inclusive end date.</param>
    Task<IEnumerable<AttendanceDaySummaryDto>> GetDailyAttendanceAsync(
        IEnumerable<int> innuxEmployeeIds,
        DateTime startDate,
        DateTime endDate);

    /// <summary>
    /// Retrieves full drill-down detail for one employee on one day.
    /// Includes the processed summary, raw punches, and period breakdown.
    /// </summary>
    /// <param name="innuxEmployeeId">Single Innux employee ID.</param>
    /// <param name="date">Target date.</param>
    Task<AttendanceDayDetailDto?> GetDayDetailAsync(int innuxEmployeeId, DateTime date);
}
