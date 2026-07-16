using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

public class ApprovalRoutingService : IApprovalRoutingService
{
    private readonly ApplicationDbContext _context;

    public ApprovalRoutingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApprovalRoutingResultDto> ResolveAreaManagersAsync(int departmentId, int? plantId)
    {
        // Level 1 — plant-specific managers. Skipped when the request has no plant
        // (legacy requests with PlantId NULL resolve via global managers directly).
        if (plantId.HasValue)
        {
            var plantManagers = await QueryEligibleManagers(departmentId)
                .Where(dm => dm.PlantId == plantId.Value)
                .Select(dm => new AreaManagerDto
                {
                    UserId = dm.UserId,
                    FullName = dm.User.FullName,
                    Email = dm.User.Email,
                    PlantId = dm.PlantId
                })
                .ToListAsync();

            if (plantManagers.Count > 0)
            {
                return new ApprovalRoutingResultDto
                {
                    Source = ApprovalRoutingSource.PlantSpecific,
                    Managers = Distinct(plantManagers)
                };
            }
        }

        // Level 2 — global managers of the department (PlantId NULL).
        var globalManagers = await QueryEligibleManagers(departmentId)
            .Where(dm => dm.PlantId == null)
            .Select(dm => new AreaManagerDto
            {
                UserId = dm.UserId,
                FullName = dm.User.FullName,
                Email = dm.User.Email,
                PlantId = null
            })
            .ToListAsync();

        if (globalManagers.Count > 0)
        {
            return new ApprovalRoutingResultDto
            {
                Source = ApprovalRoutingSource.GlobalManagers,
                Managers = Distinct(globalManagers)
            };
        }

        // Phase B definitive cut: NO fallback to Department.ResponsibleUserId.
        // No manager registered means the request cannot be routed — callers block
        // the submit / log APPROVAL_EMAIL_NO_RECIPIENT accordingly.
        return new ApprovalRoutingResultDto { Source = ApprovalRoutingSource.None };
    }

    public async Task<bool> IsAreaManagerAsync(Guid userId, int departmentId, int? plantId)
    {
        // D1 — inclusive: plant-specific row for the request's plant OR a global row.
        // A row for a DIFFERENT plant never qualifies. Requests without a plant are
        // only actionable by global managers. The manual "Area Approver" role and the
        // legacy Department.ResponsibleUserId grant NOTHING here (Phase B cut);
        // compatibility for old in-flight requests lives at the controllers via
        // Request.AreaApproverId == actor.
        return await QueryEligibleManagers(departmentId)
            .AnyAsync(dm => dm.UserId == userId
                         && (dm.PlantId == null || (plantId.HasValue && dm.PlantId == plantId.Value)));
    }

    public async Task<List<ManagedScopeDto>> GetManagedScopesAsync(Guid userId)
    {
        return await _context.DepartmentManagers
            .AsNoTracking()
            .Where(dm => dm.IsActive && dm.UserId == userId && dm.User.IsActive)
            .Select(dm => new ManagedScopeDto { DepartmentId = dm.DepartmentId, PlantId = dm.PlantId })
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// Active manager rows of the department whose users are notifiable/actionable.
    /// Plant-specific rows pointing at deactivated plants are excluded.
    /// </summary>
    private IQueryable<Domain.Entities.DepartmentManager> QueryEligibleManagers(int departmentId)
    {
        return _context.DepartmentManagers
            .AsNoTracking()
            .Where(dm => dm.IsActive
                      && dm.DepartmentId == departmentId
                      && dm.User.IsActive
                      && dm.User.Email != null
                      && dm.User.Email != string.Empty
                      && (dm.PlantId == null || dm.Plant!.IsActive));
    }

    private static List<AreaManagerDto> Distinct(List<AreaManagerDto> managers)
        => managers.GroupBy(m => m.UserId).Select(g => g.First()).ToList();
}
