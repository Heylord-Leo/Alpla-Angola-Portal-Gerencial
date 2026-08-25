using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Data;

/// <summary>
/// The CANONICAL "which requests may this user access" rule, extracted so it has a single source of truth
/// (Phase 3D / Layer B.1). Historically this lived only in <c>BaseController.GetScopedRequestsQuery</c>;
/// it is now shared verbatim by that controller helper AND by the Supplier Sheet capability evaluator, so
/// Buyer supplier access is scoped by the SAME policy that governs <c>/buyer/requests/{id}</c> — never by
/// request ownership (BuyerId), which is only a queue filter.
///
/// Rule: System Administrator sees every request; everyone else is filtered by their Plant scopes
/// (<c>UserPlantScopes</c>) and Department scopes (<c>UserDepartmentScopes</c>). A user with no plant and
/// no department scope is unfiltered — identical to the previous behavior.
/// </summary>
public static class RequestAccessScope
{
    public static async Task<IQueryable<Request>> ScopedRequestsAsync(
        ApplicationDbContext context, Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        if (roles.Contains(RoleConstants.SystemAdministrator))
            return context.Requests.AsNoTracking();

        var plantIds = await context.UserPlantScopes
            .Where(s => s.UserId == userId).Select(s => s.PlantId).ToListAsync(ct);
        var departmentIds = await context.UserDepartmentScopes
            .Where(s => s.UserId == userId).Select(s => s.DepartmentId).ToListAsync(ct);

        var query = context.Requests.AsNoTracking().AsQueryable();
        if (plantIds.Any())
            query = query.Where(r => r.PlantId.HasValue && plantIds.Contains(r.PlantId.Value));
        if (departmentIds.Any())
            query = query.Where(r => departmentIds.Contains(r.DepartmentId));
        return query;
    }
}
