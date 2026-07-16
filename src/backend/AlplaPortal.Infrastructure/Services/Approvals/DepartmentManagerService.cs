using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// Master-data operations for DepartmentManagers, including rule D3 (confirmed
/// 2026-07-15): saving a manager auto-creates the missing visibility scopes
/// (UserDepartmentScope + UserPlantScope) in the same transaction, so a manager
/// authorized to approve is never invisible to the queue. Removing/deactivating a
/// manager never removes scopes (they may have other origins).
/// </summary>
public class DepartmentManagerService
{
    private readonly ApplicationDbContext _context;

    public DepartmentManagerService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<DepartmentManagerDto>> ListAsync(int departmentId)
    {
        return await _context.DepartmentManagers
            .AsNoTracking()
            .Where(dm => dm.DepartmentId == departmentId)
            .OrderBy(dm => dm.PlantId == null ? 0 : 1).ThenBy(dm => dm.Plant!.Name).ThenBy(dm => dm.User.FullName)
            .Select(dm => new DepartmentManagerDto
            {
                Id = dm.Id,
                DepartmentId = dm.DepartmentId,
                PlantId = dm.PlantId,
                PlantName = dm.Plant != null ? dm.Plant.Name : null,
                UserId = dm.UserId,
                UserFullName = dm.User.FullName,
                UserEmail = dm.User.Email,
                UserIsActive = dm.User.IsActive,
                IsActive = dm.IsActive,
                CreatedAtUtc = dm.CreatedAtUtc
            })
            .ToListAsync();
    }

    /// <exception cref="InvalidOperationException">Business validation failure; message is user-facing.</exception>
    public async Task<AddDepartmentManagerResultDto> AddAsync(int departmentId, Guid userId, int? plantId)
    {
        var department = await _context.Departments.FirstOrDefaultAsync(d => d.Id == departmentId)
            ?? throw new InvalidOperationException("Departamento não encontrado.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilizador não encontrado.");
        if (!user.IsActive)
            throw new InvalidOperationException("O utilizador selecionado está inativo.");

        Plant? plant = null;
        if (plantId.HasValue)
        {
            plant = await _context.Plants.FirstOrDefaultAsync(p => p.Id == plantId.Value)
                ?? throw new InvalidOperationException("Planta não encontrada.");
            if (!plant.IsActive)
                throw new InvalidOperationException("A planta selecionada está inativa.");
        }

        var existing = await _context.DepartmentManagers
            .FirstOrDefaultAsync(dm => dm.DepartmentId == departmentId && dm.UserId == userId && dm.PlantId == plantId);

        DepartmentManager manager;
        if (existing != null)
        {
            if (existing.IsActive)
                throw new InvalidOperationException("Este utilizador já é manager deste departamento/planta.");
            // Reactivate instead of violating the unique index.
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            manager = existing;
        }
        else
        {
            manager = new DepartmentManager
            {
                DepartmentId = departmentId,
                PlantId = plantId,
                UserId = userId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.DepartmentManagers.Add(manager);
        }

        // ── D3: auto-complete missing visibility scopes in the same SaveChanges ──
        var result = new AddDepartmentManagerResultDto();

        var hasDeptScope = await _context.UserDepartmentScopes
            .AnyAsync(s => s.UserId == userId && s.DepartmentId == departmentId);
        if (!hasDeptScope)
        {
            _context.UserDepartmentScopes.Add(new UserDepartmentScope { UserId = userId, DepartmentId = departmentId });
            result.CreatedDepartmentScopes.Add(department.Name);
        }

        // Global manager covers every plant → needs visibility on all active plants.
        var targetPlants = plantId.HasValue
            ? new List<Plant> { plant! }
            : await _context.Plants.Where(p => p.IsActive).ToListAsync();

        var existingPlantScopeIds = await _context.UserPlantScopes
            .Where(s => s.UserId == userId)
            .Select(s => s.PlantId)
            .ToListAsync();

        foreach (var target in targetPlants.Where(p => !existingPlantScopeIds.Contains(p.Id)))
        {
            _context.UserPlantScopes.Add(new UserPlantScope { UserId = userId, PlantId = target.Id });
            result.CreatedPlantScopes.Add(target.Name);
        }

        await _context.SaveChangesAsync();

        result.Manager = new DepartmentManagerDto
        {
            Id = manager.Id,
            DepartmentId = departmentId,
            PlantId = plantId,
            PlantName = plant?.Name,
            UserId = userId,
            UserFullName = user.FullName,
            UserEmail = user.Email,
            UserIsActive = user.IsActive,
            IsActive = true,
            CreatedAtUtc = manager.CreatedAtUtc
        };
        return result;
    }

    /// <returns>New IsActive value, or null when the manager row does not exist.</returns>
    public async Task<bool?> ToggleActiveAsync(int departmentId, int managerId)
    {
        var manager = await _context.DepartmentManagers
            .FirstOrDefaultAsync(dm => dm.Id == managerId && dm.DepartmentId == departmentId);
        if (manager == null) return null;

        manager.IsActive = !manager.IsActive;
        manager.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return manager.IsActive;
    }

    /// <returns>False when the manager row does not exist. Scopes are intentionally kept.</returns>
    public async Task<bool> RemoveAsync(int departmentId, int managerId)
    {
        var manager = await _context.DepartmentManagers
            .FirstOrDefaultAsync(dm => dm.Id == managerId && dm.DepartmentId == departmentId);
        if (manager == null) return false;

        _context.DepartmentManagers.Remove(manager);
        await _context.SaveChangesAsync();
        return true;
    }
}
