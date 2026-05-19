using System.Security.Claims;
using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Infrastructure.Services.Integration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// HR Attendance module — read-only Innux attendance data for Portal users.
///
/// Controller placement decision: /api/hr/attendance/
/// Placed under the HR namespace because attendance is functionally an HR feature
/// consumed by HR users, managers, and employees. The technical source system
/// (Innux) is an implementation detail that should not leak into the API surface.
/// This controller is separate from HRLeaveController and HRController to allow
/// independent evolution of the attendance module.
///
/// Authorization model (mirrors HRLeaveController pattern):
/// 1. Feature entitlement: Any authenticated user can access (self-calendar).
///    Broader visibility requires System Administrator, HR role, Local Manager,
///    or Department Manager status.
/// 2. Data scope: Resolved via GetScopedEmployeesQuery() → InnuxEmployeeId bridge.
///    Innux services receive only pre-filtered employee IDs and are scope-agnostic.
///
/// Read-only: This controller exposes attendance data from Innux.
/// No writes to Innux. No writes to Primavera. All Innux access is SELECT-only.
/// </summary>
[ApiController]
[Route("api/hr/attendance")]
[Authorize]
public class HRAttendanceController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IInnuxAttendanceService _attendanceService;
    private readonly IInnuxLookupService _lookupService;
    private readonly IPortalScheduleResolver _scheduleResolver;
    private readonly IPortalPunchInterpreter _punchInterpreter;
    private readonly IAttendanceComparisonService _comparisonService;
    private readonly ILogger<HRAttendanceController> _logger;

    public HRAttendanceController(
        ApplicationDbContext context,
        IInnuxAttendanceService attendanceService,
        IInnuxLookupService lookupService,
        IPortalScheduleResolver scheduleResolver,
        IPortalPunchInterpreter punchInterpreter,
        IAttendanceComparisonService comparisonService,
        ILogger<HRAttendanceController> logger)
    {
        _context = context;
        _attendanceService = attendanceService;
        _lookupService = lookupService;
        _scheduleResolver = scheduleResolver;
        _punchInterpreter = punchInterpreter;
        _comparisonService = comparisonService;
        _logger = logger;
    }

    // ─── Endpoints ───

    /// <summary>
    /// Returns daily attendance summaries for scoped employees within a date range.
    /// Used to render the attendance calendar grid.
    ///
    /// Date range is mandatory and capped at 90 days.
    /// Results are scoped by the caller's Portal ACL via HREmployee → InnuxEmployeeId bridge.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        if (startDate == default || endDate == default)
            return BadRequest(new { message = "startDate and endDate are required." });

        if (startDate > endDate)
            return BadRequest(new { message = "startDate must be before endDate." });

        if ((endDate - startDate).TotalDays > 90)
            return BadRequest(new { message = "Date range cannot exceed 90 days." });

        try
        {
            var scopedQuery = await GetScopedEmployeesQuery();
            var employeeRecords = await scopedQuery
                .Where(e => e.InnuxEmployeeId > 0)
                .Select(e => new
                {
                    e.InnuxEmployeeId,
                    e.FullName,
                    Department = e.InnuxDepartmentName ?? "Sem Departamento",
                    PlantName = e.Plant != null ? e.Plant.Name : (string?)null,
                    CompanyName = e.Plant != null && e.Plant.Company != null ? e.Plant.Company.Name : (string?)null
                })
                .ToListAsync();

            var innuxIds = employeeRecords.Select(e => e.InnuxEmployeeId).ToList();
            if (innuxIds.Count == 0)
                return Ok(new
                {
                    data = Array.Empty<AttendanceDaySummaryDto>(),
                    employees = Array.Empty<object>(),
                    employeeCount = 0,
                    scopeType = ResolveScopeType(),
                    startDate = startDate.Date,
                    endDate = endDate.Date
                });

            var attendance = (await _attendanceService.GetDailyAttendanceAsync(
                innuxIds, startDate.Date, endDate.Date)).ToList();

            // Merge worked hours (Basic/Overtime) from AlteracoesPeriodos
            try
            {
                var workedHours = await _attendanceService.GetWorkedHoursAsync(
                    innuxIds, startDate.Date, endDate.Date);

                foreach (var record in attendance)
                {
                    var key = (record.InnuxEmployeeId, record.Date.Date);
                    if (workedHours.TryGetValue(key, out var hours))
                    {
                        record.BasicWorkedMinutes = hours.BasicMinutes;
                        record.OvertimeMinutes = hours.OvertimeMinutes;
                        record.TotalWorkedMinutes = hours.TotalMinutes;
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-critical: if worked hours fail, calendar still renders with 0 values
                _logger.LogWarning(ex, "Failed to merge worked hours into calendar data — continuing without metrics");
            }

            return Ok(new
            {
                data = attendance,
                employees = employeeRecords,
                employeeCount = innuxIds.Count,
                scopeType = ResolveScopeType(),
                startDate = startDate.Date,
                endDate = endDate.Date
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error in attendance calendar");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load attendance calendar");
            return StatusCode(500, new { message = "An error occurred loading attendance data." });
        }
    }

    /// <summary>
    /// Returns full drill-down detail for one employee on one day.
    /// Includes processed summary, period breakdown, and raw clock punches.
    ///
    /// The target employee must be within the caller's scope.
    /// </summary>
    [HttpGet("detail/{innuxEmployeeId:int}/{date:datetime}")]
    public async Task<IActionResult> GetDayDetail(int innuxEmployeeId, DateTime date)
    {
        try
        {
            // Verify the requested employee is within scope
            var innuxIds = await GetScopedInnuxEmployeeIdsAsync();
            if (!innuxIds.Contains(innuxEmployeeId))
                return Forbid();

            var detail = await _attendanceService.GetDayDetailAsync(innuxEmployeeId, date.Date);
            if (detail == null)
                return NotFound(new { message = "No attendance data found for the specified employee and date." });

            return Ok(detail);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error in attendance detail");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load attendance detail for employee {EmployeeId} on {Date}",
                innuxEmployeeId, date.ToString("yyyy-MM-dd"));
            return StatusCode(500, new { message = "An error occurred loading attendance detail." });
        }
    }

    [AllowAnonymous]
    [HttpGet("test-verify/{innuxEmployeeId:int}/{date:datetime}")]
    public async Task<IActionResult> TestVerifyAffected(int innuxEmployeeId, DateTime date)
    {
        try
        {
            var detail = await _attendanceService.GetDayDetailAsync(innuxEmployeeId, date.Date);
            return Ok(detail);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.ToString());
        }
    }

    /// <summary>
    /// Generates the Monthly Attendance Report by Department, mimicking the Innux
    /// "Resultados mensais por departamento" PDF.
    /// Backend-driven aggregation, calculation and grouping of attendance data.
    /// </summary>
    [HttpGet("reports/monthly-by-department")]
    [Authorize(Roles = "System Administrator,HR")]
    public async Task<IActionResult> GetMonthlyByDepartmentReport(
        [FromQuery] int departmentId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string? daysFilter = null)
    {
        if (departmentId <= 0)
            return BadRequest(new { message = "departmentId is required." });

        if (startDate == default || endDate == default)
            return BadRequest(new { message = "startDate and endDate are required." });

        if (startDate > endDate)
            return BadRequest(new { message = "startDate must be before endDate." });

        if ((endDate - startDate).TotalDays > 62)
            return BadRequest(new { message = "Date range cannot exceed 62 days (approx. 2 months)." });

        try
        {
            var dept = await _context.DepartmentMasters.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == departmentId);

            if (dept == null)
                return NotFound(new { message = "Department not found." });

            var scopedQuery = await GetScopedEmployeesQuery();
            var employeeRecords = await scopedQuery
                .Where(e => e.InnuxEmployeeId > 0 && e.DepartmentMasterId == departmentId)
                .Select(e => new
                {
                    e.InnuxEmployeeId,
                    e.EmployeeCode,
                    e.FullName,
                    Department = e.InnuxDepartmentName ?? dept.DepartmentName,
                    PlantName = e.Plant != null ? e.Plant.Name : (string?)null
                })
                .ToListAsync();

            if (!employeeRecords.Any())
            {
                return Ok(new AlplaPortal.Application.DTOs.HR.AttendanceDepartmentMonthlyReportDto
                {
                    DepartmentId = departmentId,
                    DepartmentName = $"{dept.DepartmentName} ({dept.CompanyCode})",
                    StartDate = startDate.Date,
                    EndDate = endDate.Date,
                    DaysFilter = daysFilter,
                    GeneratedAt = DateTime.Now,
                    GeneratedBy = User.FindFirstValue(ClaimTypes.Name) ?? "System",
                    Warnings = new List<string> { "No employees found in this department for the current user's scope." }
                });
            }

            var innuxIds = employeeRecords.Select(e => e.InnuxEmployeeId).ToList();

            var attendanceTask = _attendanceService.GetDailyAttendanceAsync(innuxIds, startDate.Date, endDate.Date);
            var workedTask = _attendanceService.GetWorkedHoursAsync(innuxIds, startDate.Date, endDate.Date);
            var punchesTask = _attendanceService.GetRawPunchesAsync(innuxIds, startDate.Date, endDate.Date);

            await Task.WhenAll(attendanceTask, workedTask, punchesTask);

            var attendance = attendanceTask.Result.ToList();
            var workedHours = workedTask.Result;
            var rawPunches = punchesTask.Result.GroupBy(p => p.InnuxEmployeeId).ToDictionary(g => g.Key, g => g.ToList());

            var report = new AlplaPortal.Application.DTOs.HR.AttendanceDepartmentMonthlyReportDto
            {
                DepartmentId = departmentId,
                DepartmentName = $"{dept.DepartmentName} ({dept.CompanyCode})",
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                DaysFilter = daysFilter,
                GeneratedAt = DateTime.Now,
                GeneratedBy = User.FindFirstValue(ClaimTypes.Name) ?? "System"
            };

            foreach (var emp in employeeRecords.OrderBy(e => e.FullName))
            {
                var empReport = new AlplaPortal.Application.DTOs.HR.AttendanceEmployeeReportDto
                {
                    InnuxId = emp.InnuxEmployeeId,
                    EmployeeId = emp.EmployeeCode,
                    Name = emp.FullName,
                    DepartmentName = emp.Department,
                    PlantName = emp.PlantName
                };

                var empDaily = attendance.Where(a => a.InnuxEmployeeId == emp.InnuxEmployeeId).OrderBy(a => a.Date).ToList();
                var empPunches = rawPunches.ContainsKey(emp.InnuxEmployeeId) ? rawPunches[emp.InnuxEmployeeId] : new List<AttendancePunchDto>();

                // Month buckets for summaries
                var monthSummaries = new Dictionary<string, AlplaPortal.Application.DTOs.HR.AttendanceMonthlySummaryDto>();

                foreach (var day in empDaily)
                {
                    var key = (EmployeeId: emp.InnuxEmployeeId, Date: day.Date.Date);
                    var dayWorked = workedHours.ContainsKey(key) ? workedHours[key] : new WorkedHoursDto { BasicMinutes = 0, OvertimeMinutes = 0 };

                    // Get punches for this day (or early next day for overnight shifts, handled simplistically here by looking at punches where Date == day.Date or (Date == nextDay and time < 12:00) if overnight)
                    // Note: Since GetRawPunches returns data by date of punch, we need to map them back to the shift date.
                    // For simplicity and report mimicking, we just grab punches on this Date, and maybe next morning if IsOvernightShift.
                    var dayPunches = empPunches.Where(p => 
                        p.Date == day.Date.Date || 
                        (day.IsOvernightShift && p.Date == day.Date.Date.AddDays(1) && string.Compare(p.Time, "12:00", StringComparison.Ordinal) < 0)
                    ).OrderBy(p => p.Date).ThenBy(p => p.Time).ToList();

                    // Determine if this day uses portal-interpreted punches
                    var isPortalInterpreted = day.AnomalyDescription?.Contains("Portal") == true;
                    var hasMissingPunch = day.MissedMandatoryPeriods || (day.PunchCount == 0 && day.ExpectedMinutes > 0 && !day.IsRestDay);
                    var hasInconsistentData = !string.IsNullOrWhiteSpace(day.AnomalyDescription);
                    var isVacation = day.AttendanceStatus == "Vacation";
                    var isHoliday = day.AttendanceStatus == "Holiday";

                    // Build warning message from available anomaly data
                    string? warningMessage = null;
                    if (!string.IsNullOrWhiteSpace(day.AnomalyDescription))
                        warningMessage = day.AnomalyDescription;
                    if (hasMissingPunch && string.IsNullOrWhiteSpace(warningMessage))
                        warningMessage = "Marcação em falta";

                    var recordDto = new AlplaPortal.Application.DTOs.HR.AttendanceDailyRecordDto
                    {
                        Date = day.Date,
                        Weekday = day.Date.ToString("ddd", new System.Globalization.CultureInfo("pt-PT")).Substring(0, 3).ToUpper(),
                        BasicMinutes = dayWorked.BasicMinutes,
                        ExtraMinutes = dayWorked.OvertimeMinutes,
                        UnpaidMinutes = dayWorked.UnpaidMinutes,
                        TotalMinutes = dayWorked.TotalMinutes,
                        MissingMinutes = 0,
                        AbsenceMinutes = day.AbsenceMinutes,
                        AbsenceDescription = day.Justification,
                        Justification = day.Justification,
                        DailyBalance = day.BalanceMinutes,
                        Status = day.AttendanceStatus,
                        IsDayOff = day.IsRestDay,
                        IsVacation = isVacation,
                        IsHoliday = isHoliday,
                        HasMissingPunch = hasMissingPunch,
                        HasInconsistentData = hasInconsistentData,
                        IsPortalInterpreted = isPortalInterpreted,
                        WarningMessage = warningMessage
                    };

                    // Direction-aware punch pairing:
                    // When DirectionLabel is available (EN/SA, Entrada/Saída), use it for smarter pairing.
                    // For Code 17/18 ambiguous punches, fall back to positional pairing.
                    var entries = new List<string>();
                    var exits = new List<string>();
                    bool hasDirectionInfo = dayPunches.Any(p =>
                        !string.IsNullOrWhiteSpace(p.DirectionLabel) &&
                        (p.DirectionLabel.Equals("Entrada", StringComparison.OrdinalIgnoreCase) ||
                         p.DirectionLabel.Equals("Saída", StringComparison.OrdinalIgnoreCase)));

                    if (hasDirectionInfo)
                    {
                        // Direction-aware: separate entries and exits
                        foreach (var p in dayPunches)
                        {
                            if (p.DirectionLabel?.Equals("Entrada", StringComparison.OrdinalIgnoreCase) == true)
                                entries.Add(p.Time);
                            else if (p.DirectionLabel?.Equals("Saída", StringComparison.OrdinalIgnoreCase) == true)
                                exits.Add(p.Time);
                            else
                            {
                                // Ambiguous direction — assign by position (odd = entry, even = exit)
                                if ((entries.Count + exits.Count) % 2 == 0)
                                    entries.Add(p.Time);
                                else
                                    exits.Add(p.Time);
                            }
                        }
                    }
                    else
                    {
                        // No direction info — positional pairing (1st=entry, 2nd=exit, ...)
                        for (int pi = 0; pi < dayPunches.Count; pi++)
                        {
                            if (pi % 2 == 0) entries.Add(dayPunches[pi].Time);
                            else exits.Add(dayPunches[pi].Time);
                        }
                    }

                    if (entries.Count > 0) recordDto.Entrada1 = entries[0];
                    if (exits.Count > 0) recordDto.Saida1 = exits[0];
                    if (entries.Count > 1) recordDto.Entrada2 = entries[1];
                    if (exits.Count > 1) recordDto.Saida2 = exits[1];
                    if (entries.Count > 2) recordDto.Entrada3 = entries[2];
                    if (exits.Count > 2) recordDto.Saida3 = exits[2];
                    if (entries.Count > 3) recordDto.Entrada4 = entries[3];
                    if (exits.Count > 3) recordDto.Saida4 = exits[3];

                    // Filtering
                    bool includeDay = true;
                    if (!string.IsNullOrEmpty(daysFilter))
                    {
                        if (daysFilter == "missing_punches" && !hasMissingPunch) includeDay = false;
                        if (daysFilter == "inconsistent" && !hasInconsistentData) includeDay = false;
                        if (daysFilter == "absences" && day.AbsenceMinutes <= 0) includeDay = false;
                    }

                    if (includeDay)
                        empReport.DailyRecords.Add(recordDto);

                    // Update Monthly Summaries
                    var monthKey = $"{day.Date.Year}-{day.Date.Month:D2}";
                    if (!monthSummaries.ContainsKey(monthKey))
                    {
                        monthSummaries[monthKey] = new AlplaPortal.Application.DTOs.HR.AttendanceMonthlySummaryDto
                        {
                            Year = day.Date.Year,
                            Month = day.Date.Month,
                            MonthLabel = day.Date.ToString("MMMM yyyy", new System.Globalization.CultureInfo("pt-PT"))
                        };
                    }

                    var mSum = monthSummaries[monthKey];
                    mSum.BasicMinutes += recordDto.BasicMinutes;
                    mSum.ExtraMinutes += recordDto.ExtraMinutes;
                    mSum.UnpaidMinutes += recordDto.UnpaidMinutes;
                    mSum.TotalMinutes += recordDto.TotalMinutes;
                    mSum.BalanceMinutes += recordDto.DailyBalance;
                    
                    if (recordDto.IsVacation) mSum.VacationDays++;
                    if (recordDto.IsDayOff) mSum.DayOffDays++;
                    if (recordDto.BasicMinutes > 0 || recordDto.TotalMinutes > 0) mSum.WorkedDays++;
                    if (recordDto.HasMissingPunch) mSum.MissingPunchDays++;
                    if (recordDto.HasInconsistentData) mSum.InconsistentDays++;
                    
                    // Grand Totals
                    empReport.GrandTotals.BasicMinutes += recordDto.BasicMinutes;
                    empReport.GrandTotals.ExtraMinutes += recordDto.ExtraMinutes;
                    empReport.GrandTotals.UnpaidMinutes += recordDto.UnpaidMinutes;
                    empReport.GrandTotals.TotalMinutes += recordDto.TotalMinutes;
                    empReport.GrandTotals.BalanceMinutes += recordDto.DailyBalance;

                    if (recordDto.IsVacation) empReport.GrandTotals.VacationDays++;
                    if (recordDto.IsDayOff) empReport.GrandTotals.DayOffDays++;
                    if (recordDto.BasicMinutes > 0 || recordDto.TotalMinutes > 0) empReport.GrandTotals.WorkedDays++;
                    if (recordDto.HasMissingPunch) empReport.GrandTotals.MissingPunchDays++;
                    if (recordDto.HasInconsistentData) empReport.GrandTotals.InconsistentDays++;
                }

                empReport.MonthlyTotals = monthSummaries.Values.OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();
                report.Employees.Add(empReport);

                // Add to Department Grand Totals
                report.DepartmentGrandTotals.BasicMinutes += empReport.GrandTotals.BasicMinutes;
                report.DepartmentGrandTotals.ExtraMinutes += empReport.GrandTotals.ExtraMinutes;
                report.DepartmentGrandTotals.UnpaidMinutes += empReport.GrandTotals.UnpaidMinutes;
                report.DepartmentGrandTotals.TotalMinutes += empReport.GrandTotals.TotalMinutes;
                report.DepartmentGrandTotals.BalanceMinutes += empReport.GrandTotals.BalanceMinutes;

                report.DepartmentGrandTotals.VacationDays += empReport.GrandTotals.VacationDays;
                report.DepartmentGrandTotals.DayOffDays += empReport.GrandTotals.DayOffDays;
                report.DepartmentGrandTotals.WorkedDays += empReport.GrandTotals.WorkedDays;
                report.DepartmentGrandTotals.MissingPunchDays += empReport.GrandTotals.MissingPunchDays;
                report.DepartmentGrandTotals.InconsistentDays += empReport.GrandTotals.InconsistentDays;
            }

            return Ok(report);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Innux"))
        {
            _logger.LogWarning(ex, "Innux integration unavailable for department {DepartmentId}", departmentId);
            return StatusCode(503, new { message = "O serviço Innux não está disponível. Verifique a configuração de integração." });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "Innux query timeout for department {DepartmentId}, range {Start}-{End}", departmentId, startDate, endDate);
            return StatusCode(504, new { message = "A consulta ao Innux excedeu o tempo limite. Tente um intervalo de datas mais curto." });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex, "Innux SQL error for department {DepartmentId}: Error {ErrorNumber}", departmentId, ex.Number);
            return StatusCode(502, new { message = "Erro de comunicação com o sistema Innux. Contacte o suporte técnico." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load monthly department report for department {DepartmentId}", departmentId);
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao gerar o relatório. Contacte o suporte técnico." });
        }
    }

    /// <summary>
    /// Returns all absence codes from Innux.
    /// Reference data — cached 1 hour server-side.
    /// Available to any authenticated user (codes are not scoped).
    /// </summary>
    [HttpGet("lookup/absence-codes")]
    public async Task<IActionResult> GetAbsenceCodes()
    {
        try
        {
            var codes = await _lookupService.GetAbsenceCodesAsync();
            return Ok(codes);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error loading absence codes");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load absence codes");
            return StatusCode(500, new { message = "An error occurred loading absence codes." });
        }
    }

    /// <summary>
    /// Returns all work codes from Innux.
    /// Reference data — cached 1 hour server-side.
    /// Available to any authenticated user (codes are not scoped).
    /// </summary>
    [HttpGet("lookup/work-codes")]
    public async Task<IActionResult> GetWorkCodes()
    {
        try
        {
            var codes = await _lookupService.GetWorkCodesAsync();
            return Ok(codes);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error loading work codes");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load work codes");
            return StatusCode(500, new { message = "An error occurred loading work codes." });
        }
    }

    // ─── Scoping (mirrors HRLeaveController pattern) ───

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());

    private List<string> CurrentUserRoles =>
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    private bool IsAdminOrHR
    {
        get
        {
            var roles = CurrentUserRoles;
            return roles.Contains(RoleConstants.SystemAdministrator) || roles.Contains(RoleConstants.HR);
        }
    }

    /// <summary>
    /// Resolves Portal-scoped HREmployee records, then extracts their InnuxEmployeeId values.
    /// This is the identity bridge between Portal ACL and Innux data.
    ///
    /// Scope tiers (same as HRLeaveController.GetScopedEmployeesQuery):
    /// - System Admin: all employees
    /// - HR role: filtered by plant/department scope
    /// - Local Manager: filtered by plant + department intersection
    /// - Department Manager: employees they manage or in managed departments
    /// - Self-only: single employee matched by email
    ///
    /// Employees without a valid InnuxEmployeeId (> 0) are excluded —
    /// they have no Innux identity to query against.
    /// </summary>
    private async Task<List<int>> GetScopedInnuxEmployeeIdsAsync()
    {
        var scopedQuery = await GetScopedEmployeesQuery();
        return await scopedQuery
            .Where(e => e.InnuxEmployeeId > 0)
            .Select(e => e.InnuxEmployeeId)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the set of HREmployee records visible to the current user.
    /// Mirrors the scoping logic from HRLeaveController.GetScopedEmployeesQuery().
    /// </summary>
    private async Task<IQueryable<HREmployee>> GetScopedEmployeesQuery()
    {
        var userId = CurrentUserId;
        var roles = CurrentUserRoles;

        var query = _context.HREmployees.AsNoTracking();

        // System Admin sees everything
        if (roles.Contains(RoleConstants.SystemAdministrator))
            return query;

        // HR role: filter by plant and/or department scope
        if (roles.Contains(RoleConstants.HR))
        {
            var plantIds = await _context.UserPlantScopes
                .Where(s => s.UserId == userId)
                .Select(s => s.PlantId)
                .ToListAsync();

            var deptIds = await _context.UserDepartmentScopes
                .Where(s => s.UserId == userId)
                .Select(s => s.DepartmentId)
                .ToListAsync();

            if (plantIds.Any() || deptIds.Any())
            {
                query = query.Where(e =>
                    (e.PlantId.HasValue && plantIds.Contains(e.PlantId.Value)) ||
                    (e.PortalDepartmentId.HasValue && deptIds.Contains(e.PortalDepartmentId.Value))
                );
            }

            return query;
        }

        // Local Manager: plant + department scope intersection
        if (roles.Contains(RoleConstants.LocalManager))
        {
            var lmPlantIds = await _context.UserPlantScopes
                .Where(s => s.UserId == userId)
                .Select(s => s.PlantId)
                .ToListAsync();

            var lmDeptIds = await _context.UserDepartmentScopes
                .Where(s => s.UserId == userId)
                .Select(s => s.DepartmentId)
                .ToListAsync();

            var hasPlants = lmPlantIds.Any();
            var hasDepts = lmDeptIds.Any();

            if (hasPlants && hasDepts)
            {
                query = query.Where(e =>
                    (e.PlantId.HasValue && lmPlantIds.Contains(e.PlantId.Value)) &&
                    (e.PortalDepartmentId.HasValue && lmDeptIds.Contains(e.PortalDepartmentId.Value))
                );
            }
            else if (hasDepts)
            {
                query = query.Where(e =>
                    e.PortalDepartmentId.HasValue && lmDeptIds.Contains(e.PortalDepartmentId.Value)
                );
            }
            else if (hasPlants)
            {
                query = query.Where(e =>
                    e.PlantId.HasValue && lmPlantIds.Contains(e.PlantId.Value)
                );
            }
            else
            {
                return query.Where(e => false); // No scope configured — fail-safe
            }

            return query;
        }

        // Department Manager: sees employees they manage or in their managed departments
        var managedDeptIds = await _context.Departments
            .Where(d => d.ResponsibleUserId == userId)
            .Select(d => d.Id)
            .ToListAsync();

        if (managedDeptIds.Any())
        {
            query = query.Where(e =>
                e.ManagerUserId == userId ||
                (e.PortalDepartmentId.HasValue && managedDeptIds.Contains(e.PortalDepartmentId.Value))
            );
            return query;
        }

        // Fallback: self-calendar — matched by email
        var userEmail = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(userEmail))
        {
            var emailLower = userEmail.ToLower();
            return query.Where(e => e.Email != null && e.Email.ToLower() == emailLower);
        }

        // No identity match — fail safe
        return query.Where(e => false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Portal-Side Attendance Interpretation — Diagnostic Endpoints
    //
    // These endpoints are investigative/diagnostic only.
    // They expose the Portal-side schedule resolution and raw punch interpretation
    // services for HR/IT analysis. They are NOT consumed by the production
    // calendar UI and should NOT replace the current Innux-based calendar yet.
    //
    // Read-only: No writes to Innux or Primavera.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [DIAGNOSTIC] Resolves the expected schedule for an employee on a specific date.
    ///
    /// Uses the Portal Schedule Day Resolver to compute which Innux schedule (Horario)
    /// applies based on the employee's work plan cycle. Returns the resolved schedule
    /// with periods, expected entry/exit times, expected minutes, and overnight flag.
    ///
    /// This endpoint is for diagnostic/investigative purposes only.
    /// It is not consumed by the production calendar UI.
    /// </summary>
    /// <param name="innuxEmployeeId">Innux IDFuncionario.</param>
    /// <param name="date">Target date (yyyy-MM-dd).</param>
    [HttpGet("portal/resolve-schedule/{innuxEmployeeId:int}/{date}")]
    public async Task<IActionResult> DiagnosticResolveSchedule(int innuxEmployeeId, DateTime date)
    {
        // Restrict to System Administrator and HR roles
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.HR))
        {
            return Forbid();
        }

        try
        {
            var result = await _scheduleResolver.ResolveScheduleForDateAsync(innuxEmployeeId, date);

            if (result == null)
            {
                return NotFound(new
                {
                    message = $"No work plan assigned for Innux employee {innuxEmployeeId}.",
                    innuxEmployeeId,
                    date = date.ToString("yyyy-MM-dd")
                });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = "Innux connection unavailable.", detail = ex.Message });
        }
    }

    /// <summary>
    /// [DIAGNOSTIC] Interprets raw terminal punches for an employee on a specific date.
    ///
    /// Uses the Portal Raw Punch Interpreter to read TerminaisMarcacoes directly,
    /// infer Entry/Exit directions, flag duplicates (without removing them),
    /// build punch pairs, calculate worked minutes, and assign confidence scores.
    ///
    /// This endpoint is for diagnostic/investigative purposes only.
    /// It is not consumed by the production calendar UI.
    /// The response includes ALL raw punches (including flagged duplicates) for audit transparency.
    /// </summary>
    /// <param name="innuxEmployeeId">Innux IDFuncionario.</param>
    /// <param name="date">Target date (yyyy-MM-dd).</param>
    [HttpGet("portal/interpret-punches/{innuxEmployeeId:int}/{date}")]
    public async Task<IActionResult> DiagnosticInterpretPunches(int innuxEmployeeId, DateTime date)
    {
        // Restrict to System Administrator and HR roles
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.HR))
        {
            return Forbid();
        }

        try
        {
            var result = await _punchInterpreter.InterpretPunchesAsync(innuxEmployeeId, date);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = "Innux connection unavailable.", detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 3 — Attendance Comparison Engine — Diagnostic Endpoints
    //
    // Compares Innux processed attendance against Portal raw-punch interpretation.
    // Diagnostic only — does NOT replace the production calendar.
    // Read-only: No writes to Innux or Primavera.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [DIAGNOSTIC] Compares Innux processed attendance vs Portal raw-punch
    /// interpretation for one employee on one day.
    ///
    /// Returns a comparison result with discrepancy severity, type codes,
    /// Portuguese messages, and a recommended review action.
    ///
    /// This endpoint is for diagnostic/investigative purposes only.
    /// It is not consumed by the production calendar UI.
    /// </summary>
    /// <param name="innuxEmployeeId">Innux IDFuncionario.</param>
    /// <param name="date">Target date (yyyy-MM-dd).</param>
    [HttpGet("portal/compare/{innuxEmployeeId:int}/{date}")]
    public async Task<IActionResult> DiagnosticCompareEmployeeDay(int innuxEmployeeId, DateTime date)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.HR))
        {
            return Forbid();
        }

        try
        {
            var result = await _comparisonService.CompareEmployeeDayAsync(innuxEmployeeId, date);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = "Innux connection unavailable.", detail = ex.Message });
        }
    }

    /// <summary>
    /// [DIAGNOSTIC] Compares Innux vs Portal attendance across a date range.
    ///
    /// Supports optional filtering by innuxEmployeeId, departmentId, and
    /// onlyDiscrepancies flag. Maximum date range: 31 days.
    ///
    /// Returns a batch comparison result with summary statistics and
    /// individual employee-day comparison results.
    ///
    /// This endpoint is for diagnostic/investigative purposes only.
    /// </summary>
    [HttpGet("portal/compare-range")]
    public async Task<IActionResult> DiagnosticCompareRange(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] int? innuxEmployeeId = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] bool onlyDiscrepancies = false)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.HR))
        {
            return Forbid();
        }

        if (startDate == default || endDate == default)
            return BadRequest(new { message = "startDate e endDate são obrigatórios." });

        if (startDate > endDate)
            return BadRequest(new { message = "startDate deve ser anterior a endDate." });

        if ((endDate - startDate).TotalDays > 31)
            return BadRequest(new { message = "O intervalo de datas não pode exceder 31 dias." });

        try
        {
            // If a specific employee is requested, delegate directly
            if (innuxEmployeeId.HasValue)
            {
                var result = await _comparisonService.CompareDateRangeAsync(
                    startDate, endDate, innuxEmployeeId, departmentId, onlyDiscrepancies);
                return Ok(result);
            }

            // If departmentId is specified, resolve employees from that department
            if (departmentId.HasValue)
            {
                var empIds = await _context.Set<Domain.Entities.HREmployee>()
                    .Where(e => e.PortalDepartmentId == departmentId.Value && e.InnuxEmployeeId > 0)
                    .Select(e => e.InnuxEmployeeId)
                    .ToListAsync();

                if (empIds.Count == 0)
                    return Ok(new DateRangeComparisonResultDto
                    {
                        StartDate = startDate.Date,
                        EndDate = endDate.Date
                    });

                var batchResult = await ((AttendanceComparisonService)_comparisonService)
                    .CompareDateRangeBatchAsync(empIds, startDate, endDate, onlyDiscrepancies);
                return Ok(batchResult);
            }

            // No filter — use all scoped employees
            var scopedQuery = await GetScopedEmployeesQuery();
            var allIds = await scopedQuery
                .Where(e => e.InnuxEmployeeId > 0)
                .Select(e => e.InnuxEmployeeId)
                .ToListAsync();

            if (allIds.Count == 0)
                return Ok(new DateRangeComparisonResultDto
                {
                    StartDate = startDate.Date,
                    EndDate = endDate.Date
                });

            var allResult = await ((AttendanceComparisonService)_comparisonService)
                .CompareDateRangeBatchAsync(allIds, startDate, endDate, onlyDiscrepancies);
            return Ok(allResult);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = "Innux connection unavailable.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Returns a scope type string based on the current user's roles.
    /// Used by the frontend for scope-aware header rendering.
    /// </summary>
    private string ResolveScopeType()
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.SystemAdministrator))
            return "all";
        if (roles.Contains(RoleConstants.HR))
            return "hr";
        if (roles.Contains(RoleConstants.LocalManager))
            return "department";
        // Check if user manages any departments
        var userId = CurrentUserId;
        var managesDepts = _context.Departments.Any(d => d.ResponsibleUserId == userId);
        if (managesDepts)
            return "department";
        return "self";
    }
}
