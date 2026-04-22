using System.Security.Claims;
using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
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
    private readonly ILogger<HRAttendanceController> _logger;

    public HRAttendanceController(
        ApplicationDbContext context,
        IInnuxAttendanceService attendanceService,
        IInnuxLookupService lookupService,
        ILogger<HRAttendanceController> logger)
    {
        _context = context;
        _attendanceService = attendanceService;
        _lookupService = lookupService;
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

            var attendance = await _attendanceService.GetDailyAttendanceAsync(
                innuxIds, startDate.Date, endDate.Date);

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
