using System.Security.Claims;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// HR Schedule consultation — read-only Innux schedule/shift/work plan data.
///
/// Controller placement: /api/hr/schedules/
/// Provides consultation access to Innux master data: work plans, schedule
/// definitions, cycle composition, and per-plan employee lists.
///
/// Authorization model:
/// - Schedule/plan master data (GET /plans, GET /schedules) is reference data
///   accessible to any user with HR module access.
/// - Employee lists (GET /plans/{id}/employees) are scoped using the same
///   Portal ACL rules as HRAttendanceController to prevent data leakage.
///
/// Read-only: No writes to Innux. All access is SELECT-only.
/// </summary>
[ApiController]
[Route("api/hr/schedules")]
[Authorize]
public class HRScheduleController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IInnuxScheduleService _scheduleService;
    private readonly ILogger<HRScheduleController> _logger;

    public HRScheduleController(
        ApplicationDbContext context,
        IInnuxScheduleService scheduleService,
        ILogger<HRScheduleController> logger)
    {
        _context = context;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    // ─── Endpoints ───

    /// <summary>
    /// Returns all work plans with cycle composition and employee counts.
    /// Reference data — cached 1 hour server-side.
    /// </summary>
    [HttpGet("plans")]
    public async Task<IActionResult> GetWorkPlans()
    {
        try
        {
            var plans = await _scheduleService.GetWorkPlansAsync();
            return Ok(plans);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error loading work plans");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load work plans");
            return StatusCode(500, new { message = "An error occurred loading work plan data." });
        }
    }

    /// <summary>
    /// Returns all schedule definitions with period breakdowns.
    /// Reference data — cached 1 hour server-side.
    /// </summary>
    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules()
    {
        try
        {
            var schedules = await _scheduleService.GetSchedulesAsync();
            return Ok(schedules);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error loading schedules");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load schedules");
            return StatusCode(500, new { message = "An error occurred loading schedule data." });
        }
    }

    /// <summary>
    /// Returns employees assigned to a specific work plan, scoped by Portal ACL.
    /// The raw Innux employee list is intersected with the caller's visibility scope
    /// to ensure data access rules are respected.
    /// </summary>
    [HttpGet("plans/{planId:int}/employees")]
    public async Task<IActionResult> GetPlanEmployees(int planId)
    {
        try
        {
            // Get all employees from Innux for this plan (unscoped)
            var innuxEmployees = (await _scheduleService.GetPlanEmployeesAsync(planId)).ToList();

            if (innuxEmployees.Count == 0)
                return Ok(Array.Empty<object>());

            // Get scoped HREmployee records for the current user
            var scopedQuery = await GetScopedEmployeesQuery();
            var scopedRecords = await scopedQuery
                .Where(e => e.InnuxEmployeeId > 0)
                .Select(e => new
                {
                    e.InnuxEmployeeId,
                    PlantName = e.Plant != null ? e.Plant.Name : (string?)null
                })
                .ToListAsync();

            var scopedInnuxIds = scopedRecords.Select(e => e.InnuxEmployeeId).ToHashSet();
            var plantLookup = scopedRecords
                .Where(e => e.PlantName != null)
                .ToDictionary(e => e.InnuxEmployeeId, e => e.PlantName);

            // Intersect: only return employees visible to the caller
            var result = innuxEmployees
                .Where(e => scopedInnuxIds.Contains(e.InnuxEmployeeId))
                .Select(e =>
                {
                    e.PlantName = plantLookup.TryGetValue(e.InnuxEmployeeId, out var plant) ? plant : null;
                    return e;
                })
                .ToList();

            return Ok(new
            {
                employees = result,
                totalInPlan = innuxEmployees.Count,
                visibleCount = result.Count,
                scopeType = ResolveScopeType()
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error loading plan employees");
            return StatusCode(503, new { message = "Attendance system is not available.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load employees for plan {PlanId}", planId);
            return StatusCode(500, new { message = "An error occurred loading employee data." });
        }
    }

    // ─── Scoping (mirrors HRAttendanceController pattern) ───

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());

    private List<string> CurrentUserRoles =>
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    private async Task<IQueryable<AlplaPortal.Domain.Entities.HREmployee>> GetScopedEmployeesQuery()
    {
        var userId = CurrentUserId;
        var roles = CurrentUserRoles;

        var query = _context.HREmployees.AsNoTracking();

        if (roles.Contains(RoleConstants.SystemAdministrator))
            return query;

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
                return query.Where(e => false);
            }

            return query;
        }

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

        var userEmail = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(userEmail))
        {
            var emailLower = userEmail.ToLower();
            return query.Where(e => e.Email != null && e.Email.ToLower() == emailLower);
        }

        return query.Where(e => false);
    }

    private string ResolveScopeType()
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.SystemAdministrator))
            return "all";
        if (roles.Contains(RoleConstants.HR))
            return "hr";
        if (roles.Contains(RoleConstants.LocalManager))
            return "department";
        var userId = CurrentUserId;
        var managesDepts = _context.Departments.Any(d => d.ResponsibleUserId == userId);
        if (managesDepts)
            return "department";
        return "self";
    }
}
