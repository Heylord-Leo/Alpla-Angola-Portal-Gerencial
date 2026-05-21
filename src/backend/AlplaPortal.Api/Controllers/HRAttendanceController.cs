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
    ///
    /// attendanceActivity filter (30-day rule):
    ///   "active"   — only employees with lastAttendanceDate within the last 30 days (default)
    ///   "noRecent" — only employees with lastAttendanceDate older than 30 days, or no attendance at all
    ///   "all"      — no filtering
    /// The 30-day cutoff is always calculated from the current date, not from the selected calendar range.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string attendanceActivity = "active")
    {
        if (startDate == default || endDate == default)
            return BadRequest(new { message = "startDate and endDate are required." });

        if (startDate > endDate)
            return BadRequest(new { message = "startDate must be before endDate." });

        if ((endDate - startDate).TotalDays > 90)
            return BadRequest(new { message = "Date range cannot exceed 90 days." });

        // Normalize parameter
        attendanceActivity = (attendanceActivity ?? "active").Trim().ToLowerInvariant();
        if (attendanceActivity != "active" && attendanceActivity != "norecent" && attendanceActivity != "all")
            attendanceActivity = "active";

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

            var allInnuxIds = employeeRecords.Select(e => e.InnuxEmployeeId).ToList();
            if (allInnuxIds.Count == 0)
                return Ok(new
                {
                    data = Array.Empty<AttendanceDaySummaryDto>(),
                    employees = Array.Empty<object>(),
                    employeeCount = 0,
                    scopeType = ResolveScopeType(),
                    startDate = startDate.Date,
                    endDate = endDate.Date,
                    activitySummary = new { activeCount = 0, noRecentCount = 0, totalCount = 0 }
                });

            // ── 30-day activity filter ──
            // Query Innux for each employee's last attendance date
            var lastDates = await _attendanceService.GetLastAttendanceDatesAsync(allInnuxIds);
            var cutoffDate = DateTime.UtcNow.Date.AddDays(-30);

            // Classify each employee
            var activeIds = new HashSet<int>();
            var noRecentIds = new HashSet<int>();

            foreach (var empId in allInnuxIds)
            {
                if (lastDates.TryGetValue(empId, out var lastDate) && lastDate >= cutoffDate)
                {
                    activeIds.Add(empId);
                }
                else
                {
                    // Either no attendance record at all, or last date is older than 30 days
                    noRecentIds.Add(empId);
                }
            }

            // Diagnostic logging for activity filter
            _logger.LogInformation(
                "[ActivityFilter] Parameter='{Activity}', Cutoff={Cutoff:yyyy-MM-dd}, " +
                "TotalEmployees={Total}, Active={Active}, NoRecent={NoRecent}",
                attendanceActivity, cutoffDate, allInnuxIds.Count, activeIds.Count, noRecentIds.Count);

            // Log specific employees for debugging (find ABENECO)
            foreach (var emp in employeeRecords)
            {
                if (emp.FullName.Contains("ABENECO", StringComparison.OrdinalIgnoreCase))
                {
                    var hasDate = lastDates.TryGetValue(emp.InnuxEmployeeId, out var empLastDate);
                    var classification = activeIds.Contains(emp.InnuxEmployeeId) ? "ACTIVE" : "NO_RECENT";
                    _logger.LogWarning(
                        "[ActivityFilter] DEBUG Employee: '{Name}' InnuxId={InnuxId} " +
                        "LastAttendanceDate={LastDate} HasDate={HasDate} " +
                        "Cutoff={Cutoff:yyyy-MM-dd} Classification={Class}",
                        emp.FullName, emp.InnuxEmployeeId,
                        hasDate ? empLastDate.ToString("yyyy-MM-dd") : "NULL",
                        hasDate, cutoffDate, classification);
                }
            }

            // Filter employees based on the selected activity mode
            List<int> filteredInnuxIds;
            if (attendanceActivity == "norecent")
                filteredInnuxIds = allInnuxIds.Where(id => noRecentIds.Contains(id)).ToList();
            else if (attendanceActivity == "all")
                filteredInnuxIds = allInnuxIds;
            else // "active" (default)
                filteredInnuxIds = allInnuxIds.Where(id => activeIds.Contains(id)).ToList();

            var filteredEmployees = employeeRecords
                .Where(e => filteredInnuxIds.Contains(e.InnuxEmployeeId))
                .ToList();

            // Build response employee objects with lastAttendanceDate
            var responseEmployees = filteredEmployees.Select(e => new
            {
                e.InnuxEmployeeId,
                e.FullName,
                e.Department,
                e.PlantName,
                e.CompanyName,
                LastAttendanceDate = lastDates.TryGetValue(e.InnuxEmployeeId, out var ld)
                    ? (DateTime?)ld
                    : null
            }).ToList();

            // Only query Innux attendance data for filtered employees
            var attendance = filteredInnuxIds.Count > 0
                ? (await _attendanceService.GetDailyAttendanceAsync(
                    filteredInnuxIds, startDate.Date, endDate.Date)).ToList()
                : new List<AttendanceDaySummaryDto>();

            // Merge worked hours (Basic/Overtime) from AlteracoesPeriodos
            if (attendance.Count > 0)
            {
                try
                {
                    var workedHours = await _attendanceService.GetWorkedHoursAsync(
                        filteredInnuxIds, startDate.Date, endDate.Date);

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
            }

            return Ok(new
            {
                data = attendance,
                employees = responseEmployees,
                employeeCount = filteredInnuxIds.Count,
                scopeType = ResolveScopeType(),
                startDate = startDate.Date,
                endDate = endDate.Date,
                activitySummary = new
                {
                    activeCount = activeIds.Count,
                    noRecentCount = noRecentIds.Count,
                    totalCount = allInnuxIds.Count
                }
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
    ///
    /// When departmentId is provided and > 0: returns a single-department report.
    /// When departmentId is null or 0: returns a consolidated all-departments report.
    /// </summary>
    [HttpGet("reports/monthly-by-department")]
    [Authorize(Roles = "System Administrator,HR")]
    public async Task<IActionResult> GetMonthlyByDepartmentReport(
        [FromQuery] int? departmentId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string? daysFilter = null,
        [FromQuery] string? attendanceActivity = "active")
    {
        if (startDate == default || endDate == default)
            return BadRequest(new { message = "startDate and endDate are required." });

        if (startDate > endDate)
            return BadRequest(new { message = "startDate must be before endDate." });

        if ((endDate - startDate).TotalDays > 62)
            return BadRequest(new { message = "Date range cannot exceed 62 days (approx. 2 months)." });

        var generatedBy = User.FindFirstValue(ClaimTypes.Name) ?? "System";

        // Normalize attendance activity filter
        var activityMode = (attendanceActivity ?? "active").Trim().ToLowerInvariant();

        try
        {
            // ─── Single-department mode (existing behavior) ───
            if (departmentId.HasValue && departmentId.Value > 0)
            {
                var dept = await _context.DepartmentMasters.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == departmentId.Value);

                if (dept == null)
                    return NotFound(new { message = "Department not found." });

                var scopedQuery = await GetScopedEmployeesQuery();
                var employeeRecords = await scopedQuery
                    .Where(e => e.InnuxEmployeeId > 0 && e.DepartmentMasterId == departmentId.Value)
                    .Select(e => new EmployeeReportRecord
                    {
                        InnuxEmployeeId = e.InnuxEmployeeId,
                        EmployeeCode = e.EmployeeCode,
                        FullName = e.FullName,
                        Department = e.InnuxDepartmentName ?? dept.DepartmentName,
                        PlantName = e.Plant != null ? e.Plant.Name : null
                    })
                    .ToListAsync();

                // Apply 30-day attendance activity filter (based on real terminal punches)
                employeeRecords = await ApplyAttendanceActivityFilter(
                    employeeRecords, activityMode);

                if (!employeeRecords.Any())
                {
                    return Ok(new AlplaPortal.Application.DTOs.HR.AttendanceDepartmentMonthlyReportDto
                    {
                        DepartmentId = departmentId.Value,
                        DepartmentName = $"{dept.DepartmentName} ({dept.CompanyCode})",
                        StartDate = startDate.Date,
                        EndDate = endDate.Date,
                        DaysFilter = daysFilter,
                        GeneratedAt = DateTime.Now,
                        GeneratedBy = generatedBy,
                        Warnings = new List<string> { "Nenhum funcionário encontrado neste departamento após aplicar o filtro de atividade de ponto." }
                    });
                }

                var report = await BuildSingleDepartmentReportAsync(
                    departmentId.Value,
                    $"{dept.DepartmentName} ({dept.CompanyCode})",
                    employeeRecords,
                    startDate.Date, endDate.Date,
                    daysFilter, generatedBy);

                return Ok(report);
            }

            // ─── Consolidated all-departments mode ───
            _logger.LogInformation(
                "[MonthlyReport] Generating consolidated report for all departments. Range: {Start}-{End}",
                startDate.Date, endDate.Date);

            var allScopedQuery = await GetScopedEmployeesQuery();

            // Get all scoped employees with Innux IDs, grouped by DepartmentMasterId
            var allEmployees = await allScopedQuery
                .Where(e => e.InnuxEmployeeId > 0 && e.DepartmentMasterId != null)
                .Select(e => new
                {
                    e.InnuxEmployeeId,
                    e.EmployeeCode,
                    e.FullName,
                    e.DepartmentMasterId,
                    e.InnuxDepartmentName,
                    PlantName = e.Plant != null ? e.Plant.Name : (string?)null
                })
                .ToListAsync();

            if (!allEmployees.Any())
            {
                return Ok(new AlplaPortal.Application.DTOs.HR.AttendanceConsolidatedReportDto
                {
                    StartDate = startDate.Date,
                    EndDate = endDate.Date,
                    DaysFilter = daysFilter,
                    GeneratedAt = DateTime.Now,
                    GeneratedBy = generatedBy,
                    TotalDepartments = 0,
                    TotalEmployees = 0,
                    Warnings = new List<string> { "No employees found for the current user's scope." }
                });
            }

            // Apply 30-day attendance activity filter to all employees before grouping
            var allEmployeeRecords = allEmployees.Select(e => new EmployeeReportRecord
            {
                InnuxEmployeeId = e.InnuxEmployeeId,
                EmployeeCode = e.EmployeeCode,
                FullName = e.FullName,
                Department = e.InnuxDepartmentName ?? "",
                PlantName = e.PlantName,
                DepartmentMasterId = e.DepartmentMasterId
            }).ToList();

            allEmployeeRecords = await ApplyAttendanceActivityFilter(
                allEmployeeRecords, activityMode);

            if (!allEmployeeRecords.Any())
            {
                return Ok(new AlplaPortal.Application.DTOs.HR.AttendanceConsolidatedReportDto
                {
                    StartDate = startDate.Date,
                    EndDate = endDate.Date,
                    DaysFilter = daysFilter,
                    GeneratedAt = DateTime.Now,
                    GeneratedBy = generatedBy,
                    TotalDepartments = 0,
                    TotalEmployees = 0,
                    Warnings = new List<string> { "Nenhum funcionário com marcação recente de ponto encontrado." }
                });
            }

            // Get distinct department IDs and load their master records
            var deptIds = allEmployees.Select(e => e.DepartmentMasterId!.Value).Distinct().ToList();
            var departments = await _context.DepartmentMasters.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id);

            // Group filtered employees by department, sorted alphabetically by department name
            var deptGroups = allEmployeeRecords
                .Where(e => e.DepartmentMasterId.HasValue)
                .GroupBy(e => e.DepartmentMasterId!.Value)
                .Select(g => new
                {
                    DeptId = g.Key,
                    DeptName = departments.ContainsKey(g.Key)
                        ? departments[g.Key].DepartmentName
                        : "Unknown",
                    CompanyCode = departments.ContainsKey(g.Key)
                        ? departments[g.Key].CompanyCode
                        : "",
                    Employees = g.ToList()
                })
                .OrderBy(d => d.DeptName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var consolidated = new AlplaPortal.Application.DTOs.HR.AttendanceConsolidatedReportDto
            {
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                DaysFilter = daysFilter,
                GeneratedAt = DateTime.Now,
                GeneratedBy = generatedBy
            };

            int totalEmployees = 0;

            // Process departments sequentially to avoid Innux connection overload
            foreach (var deptGroup in deptGroups)
            {
                var empRecords = deptGroup.Employees.Select(e => new EmployeeReportRecord
                {
                    InnuxEmployeeId = e.InnuxEmployeeId,
                    EmployeeCode = e.EmployeeCode,
                    FullName = e.FullName,
                    Department = e.Department ?? deptGroup.DeptName,
                    PlantName = e.PlantName
                }).ToList();

                try
                {
                    var deptReport = await BuildSingleDepartmentReportAsync(
                        deptGroup.DeptId,
                        $"{deptGroup.DeptName} ({deptGroup.CompanyCode})",
                        empRecords,
                        startDate.Date, endDate.Date,
                        daysFilter, generatedBy);

                    // Only include departments with actual employee data
                    if (deptReport.Employees.Any())
                    {
                        consolidated.Departments.Add(deptReport);
                        totalEmployees += deptReport.Employees.Count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[MonthlyReport] Failed to build report for department {DeptName} (Id={DeptId}), skipping.",
                        deptGroup.DeptName, deptGroup.DeptId);
                    consolidated.Warnings.Add(
                        $"Falha ao gerar relatório para o departamento \"{deptGroup.DeptName}\". Departamento omitido.");
                }
            }

            consolidated.TotalDepartments = consolidated.Departments.Count;
            consolidated.TotalEmployees = totalEmployees;

            _logger.LogInformation(
                "[MonthlyReport] Consolidated report generated: {DeptCount} departments, {EmpCount} employees.",
                consolidated.TotalDepartments, consolidated.TotalEmployees);

            return Ok(consolidated);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Innux"))
        {
            _logger.LogWarning(ex, "Innux integration unavailable for monthly report (departmentId={DepartmentId})", departmentId);
            return StatusCode(503, new { message = "O serviço Innux não está disponível. Verifique a configuração de integração." });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "Innux query timeout for monthly report (departmentId={DepartmentId}), range {Start}-{End}", departmentId, startDate, endDate);
            return StatusCode(504, new { message = "A consulta ao Innux excedeu o tempo limite. Tente um intervalo de datas mais curto." });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex, "Innux SQL error for monthly report (departmentId={DepartmentId}): Error {ErrorNumber}", departmentId, ex.Number);
            return StatusCode(502, new { message = "Erro de comunicação com o sistema Innux. Contacte o suporte técnico." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load monthly report (departmentId={DepartmentId})", departmentId);
            return StatusCode(500, new { message = "Ocorreu um erro inesperado ao gerar o relatório. Contacte o suporte técnico." });
        }
    }

    /// <summary>
    /// Internal record for passing employee data to the report builder.
    /// </summary>
    private class EmployeeReportRecord
    {
        public int InnuxEmployeeId { get; set; }
        public string? EmployeeCode { get; set; }
        public string FullName { get; set; } = "";
        public string Department { get; set; } = "";
        public string? PlantName { get; set; }
        /// <summary>
        /// Used by the consolidated flow to preserve department grouping after filtering.
        /// Not used in single-department mode.
        /// </summary>
        public int? DepartmentMasterId { get; set; }
    }

    /// <summary>
    /// Applies the 30-day attendance activity filter to a list of employees.
    /// Uses dbo.TerminaisMarcacoes (real terminal punches) via GetLastAttendanceDatesAsync.
    /// Does NOT use dbo.Alteracoes (which contains generated schedules).
    ///
    /// Modes:
    ///   "active"   — include only employees with lastAttendanceDate within last 30 days (default)
    ///   "norecent" — include only employees with no punch or punch older than 30 days
    ///   "all"      — no filtering, return all employees
    ///
    /// Read-only: no writes to Innux or Primavera.
    /// </summary>
    private async Task<List<EmployeeReportRecord>> ApplyAttendanceActivityFilter(
        List<EmployeeReportRecord> employees,
        string activityMode)
    {
        if (activityMode == "all" || !employees.Any())
            return employees;

        var innuxIds = employees.Select(e => e.InnuxEmployeeId).Distinct().ToList();
        var lastDates = await _attendanceService.GetLastAttendanceDatesAsync(innuxIds);
        var cutoff = DateTime.UtcNow.Date.AddDays(-30);

        var beforeCount = employees.Count;

        List<EmployeeReportRecord> filtered;

        if (activityMode == "norecent")
        {
            // Only employees with NO recent punch
            filtered = employees.Where(e =>
            {
                if (!lastDates.TryGetValue(e.InnuxEmployeeId, out var lastDate))
                    return true; // No punch at all → include in "noRecent"
                return lastDate.Date < cutoff;
            }).ToList();
        }
        else // "active" (default)
        {
            // Only employees WITH recent punch
            filtered = employees.Where(e =>
            {
                if (!lastDates.TryGetValue(e.InnuxEmployeeId, out var lastDate))
                    return false; // No punch → exclude from "active"
                return lastDate.Date >= cutoff;
            }).ToList();
        }

        var excludedCount = beforeCount - filtered.Count;
        _logger.LogInformation(
            "[MonthlyReport] ActivityFilter mode={Mode}: {Before} employees → {After} (excluded {Excluded}). Cutoff={Cutoff:yyyy-MM-dd}",
            activityMode, beforeCount, filtered.Count, excludedCount, cutoff);

        return filtered;
    }

    /// <summary>
    /// Builds a single-department attendance report. Reused by both the single-department
    /// and consolidated (all-departments) flows.
    /// Read-only: SELECT only from Innux.
    /// </summary>
    private async Task<AlplaPortal.Application.DTOs.HR.AttendanceDepartmentMonthlyReportDto> BuildSingleDepartmentReportAsync(
        int departmentId,
        string departmentDisplayName,
        List<EmployeeReportRecord> employeeRecords,
        DateTime startDate,
        DateTime endDate,
        string? daysFilter,
        string generatedBy)
    {
        var innuxIds = employeeRecords.Select(e => e.InnuxEmployeeId).ToList();

        var attendanceTask = _attendanceService.GetDailyAttendanceAsync(innuxIds, startDate, endDate);
        var workedTask = _attendanceService.GetWorkedHoursAsync(innuxIds, startDate, endDate);
        var punchesTask = _attendanceService.GetRawPunchesAsync(innuxIds, startDate, endDate);

        await Task.WhenAll(attendanceTask, workedTask, punchesTask);

        var attendance = attendanceTask.Result.ToList();
        var workedHours = workedTask.Result;
        var rawPunches = punchesTask.Result.GroupBy(p => p.InnuxEmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var report = new AlplaPortal.Application.DTOs.HR.AttendanceDepartmentMonthlyReportDto
        {
            DepartmentId = departmentId,
            DepartmentName = departmentDisplayName,
            StartDate = startDate,
            EndDate = endDate,
            DaysFilter = daysFilter,
            GeneratedAt = DateTime.Now,
            GeneratedBy = generatedBy
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

            var monthSummaries = new Dictionary<string, AlplaPortal.Application.DTOs.HR.AttendanceMonthlySummaryDto>();

            foreach (var day in empDaily)
            {
                var key = (EmployeeId: emp.InnuxEmployeeId, Date: day.Date.Date);
                var dayWorked = workedHours.ContainsKey(key) ? workedHours[key] : new WorkedHoursDto { BasicMinutes = 0, OvertimeMinutes = 0 };

                var dayPunches = empPunches.Where(p =>
                    p.Date == day.Date.Date ||
                    (day.IsOvernightShift && p.Date == day.Date.Date.AddDays(1) && string.Compare(p.Time, "12:00", StringComparison.Ordinal) < 0)
                ).OrderBy(p => p.Date).ThenBy(p => p.Time).ToList();

                var isPortalInterpreted = day.AnomalyDescription?.Contains("Portal") == true;
                var hasMissingPunch = day.MissedMandatoryPeriods || (day.PunchCount == 0 && day.ExpectedMinutes > 0 && !day.IsRestDay);
                var hasInconsistentData = !string.IsNullOrWhiteSpace(day.AnomalyDescription);
                var isVacation = day.AttendanceStatus == "Vacation";
                var isHoliday = day.AttendanceStatus == "Holiday";

                string? warningMessage = null;
                if (!string.IsNullOrWhiteSpace(day.AnomalyDescription))
                    warningMessage = day.AnomalyDescription;
                if (hasMissingPunch && string.IsNullOrWhiteSpace(warningMessage))
                    warningMessage = "Marcação em falta";

                // Portal-computed report fields (DEC-124):
                // - BasicMinutes  = planned/scheduled hours (ExpectedMinutes), not worked hours
                // - TotalMinutes  = positive counted hours (worked + justified, excluding unjustified absence)
                // - DailyBalance  = TotalMinutes - BasicMinutes (replaces unreliable Innux Saldo)
                var positiveCountedMinutes = ComputePositiveCountedMinutes(day, dayWorked);
                var portalDailyBalance = positiveCountedMinutes - day.ExpectedMinutes;

                var recordDto = new AlplaPortal.Application.DTOs.HR.AttendanceDailyRecordDto
                {
                    Date = day.Date,
                    Weekday = day.Date.ToString("ddd", new System.Globalization.CultureInfo("pt-PT")).Substring(0, 3).ToUpper(),
                    BasicMinutes = day.ExpectedMinutes,
                    ExtraMinutes = dayWorked.OvertimeMinutes,
                    UnpaidMinutes = dayWorked.UnpaidMinutes,
                    TotalMinutes = positiveCountedMinutes,
                    MissingMinutes = 0,
                    AbsenceMinutes = day.AbsenceMinutes,
                    AbsenceDescription = day.Justification,
                    Justification = day.Justification,
                    DailyBalance = portalDailyBalance,
                    Status = day.AttendanceStatus,
                    IsDayOff = day.IsRestDay,
                    IsVacation = isVacation,
                    IsHoliday = isHoliday,
                    HasMissingPunch = hasMissingPunch,
                    HasInconsistentData = hasInconsistentData,
                    IsPortalInterpreted = isPortalInterpreted,
                    WarningMessage = warningMessage
                };

                // Direction-aware punch pairing
                var entries = new List<string>();
                var exits = new List<string>();
                bool hasDirectionInfo = dayPunches.Any(p =>
                    !string.IsNullOrWhiteSpace(p.DirectionLabel) &&
                    (p.DirectionLabel.Equals("Entrada", StringComparison.OrdinalIgnoreCase) ||
                     p.DirectionLabel.Equals("Saída", StringComparison.OrdinalIgnoreCase)));

                if (hasDirectionInfo)
                {
                    foreach (var p in dayPunches)
                    {
                        if (p.DirectionLabel?.Equals("Entrada", StringComparison.OrdinalIgnoreCase) == true)
                            entries.Add(p.Time);
                        else if (p.DirectionLabel?.Equals("Saída", StringComparison.OrdinalIgnoreCase) == true)
                            exits.Add(p.Time);
                        else
                        {
                            if ((entries.Count + exits.Count) % 2 == 0)
                                entries.Add(p.Time);
                            else
                                exits.Add(p.Time);
                        }
                    }
                }
                else
                {
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
                mSum.AbsenceMinutes += recordDto.AbsenceMinutes;
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
                empReport.GrandTotals.AbsenceMinutes += recordDto.AbsenceMinutes;
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

        return report;
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

    // ─── Portal-Computed Attendance Balance Helpers (DEC-124) ───

    /// <summary>
    /// Computes the positive counted minutes for the day — the value shown as H.Totais.
    /// Includes real worked hours and justified/approved absence hours.
    /// Unjustified absence hours are NOT included.
    ///
    /// For exempt categories (Vacation, Holiday, JustifiedAbsence), returns ExpectedMinutes
    /// so that H.Totais equals H.Básicas and Saldo = 0.
    /// For rest days (ExpectedMinutes = 0), returns 0.
    /// </summary>
    private static int ComputePositiveCountedMinutes(AttendanceDaySummaryDto day, WorkedHoursDto worked)
    {
        // Rest days: no expected work, no positive counted time
        if (day.IsRestDay)
            return 0;

        // Exempt categories: fully covered → H.Totais = ExpectedMinutes → Saldo = 0
        var status = day.AttendanceStatus;
        if (status == "Vacation" || status == "Holiday" || status == "JustifiedAbsence")
            return day.ExpectedMinutes;

        // Normal/worked days: actual worked minus unjustified absence, plus any justified portions.
        // Innux may record scheduled periods in AlteracoesPeriodos even on absence days,
        // so we subtract unjustified AbsenceMinutes to derive real worked time.
        var realWorkedMinutes = Math.Max(0, worked.TotalMinutes - day.AbsenceMinutes);
        return realWorkedMinutes + day.JustifiedAbsenceMinutes;
    }
}
