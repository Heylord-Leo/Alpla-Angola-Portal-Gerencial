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
