using System.Security.Claims;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Api.Controllers;

public abstract class BaseController : ControllerBase
{
    protected readonly ApplicationDbContext _context;

    protected BaseController(ApplicationDbContext context)
    {
        _context = context;
    }

    protected Guid CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(id) ? Guid.Empty : Guid.Parse(id);
        }
    }

    protected List<string> CurrentUserRoles => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    protected async Task<IQueryable<AlplaPortal.Domain.Entities.Request>> GetScopedRequestsQuery()
    {
        var userId = CurrentUserId;
        var roles = CurrentUserRoles;

        // System Administrator sees everything
        if (roles.Contains(RoleConstants.SystemAdministrator))
        {
            return _context.Requests.AsNoTracking();
        }

        // Local Manager or others: Filter by Plant/Department scope
        var plantIds = await _context.UserPlantScopes
            .Where(s => s.UserId == userId)
            .Select(s => s.PlantId)
            .ToListAsync();

        var departmentIds = await _context.UserDepartmentScopes
            .Where(s => s.UserId == userId)
            .Select(s => s.DepartmentId)
            .ToListAsync();

        var query = _context.Requests.AsNoTracking().AsQueryable();

        if (plantIds.Any())
        {
            query = query.Where(r => r.PlantId.HasValue && plantIds.Contains(r.PlantId.Value));
        }

        if (departmentIds.Any())
        {
            query = query.Where(r => departmentIds.Contains(r.DepartmentId));
        }

        return query;
    }
}
