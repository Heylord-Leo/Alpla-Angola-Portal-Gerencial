using AlplaPortal.Application.DTOs.Users;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : BaseController
{
    private readonly AdminLogWriter _adminLogWriter;
    private readonly IAuthService _authService;
    private readonly IPasswordHasher _passwordHasher;

    // Roles allowed for Local Managers to assign
    private static readonly string[] AllowedRolesForLM = 
    { 
        "Requester", "Buyer", "Area Approver", "Final Approver", "Finance", "Receiving", "Import", "Viewer / Management", "HR" 
    };

    public UsersController(
        ApplicationDbContext context, 
        AdminLogWriter adminLogWriter,
        IAuthService authService,
        IPasswordHasher passwordHasher) : base(context)
    {
        _adminLogWriter = adminLogWriter;
        _authService = authService;
        _passwordHasher = passwordHasher;
    }
    
    [HttpGet("me")]
    public async Task<ActionResult<UserListDto>> GetMe()
    {
        var user = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.UserRoleAssignments).ThenInclude(ura => ura.Role)
            .Include(u => u.UserPlantScopes).ThenInclude(ups => ups.Plant)
            .Include(u => u.UserDepartmentScopes).ThenInclude(uds => uds.Department)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == CurrentUserId);

        if (user == null) return NotFound();

        return Ok(new UserListDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            IsActive = user.IsActive,
            Roles = user.UserRoleAssignments.Select(ra => ra.Role.RoleName).ToList(),
            Plants = user.UserPlantScopes.Select(ps => ps.Plant.Code).ToList(),
            Departments = user.UserDepartmentScopes.Select(ds => ds.Department.Code).ToList(),
            CanEdit = false
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserListDto>>> GetUsers([FromQuery] bool includeInactive = false)
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isSystemAdmin = roles.Contains("System Administrator");
        var isLocalManager = roles.Contains("Local Manager");

        var lmPlantIds = isLocalManager 
            ? await _context.UserPlantScopes.Where(s => s.UserId == CurrentUserId).Select(s => s.PlantId).ToListAsync()
            : new List<int>();
        
        var lmDeptIds = isLocalManager
            ? await _context.UserDepartmentScopes.Where(s => s.UserId == CurrentUserId).Select(s => s.DepartmentId).ToListAsync()
            : new List<int>();

        var query = _context.Users.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(u => u.IsActive);
        }

        // Project directly in SQL to avoid cartesian explosion from multiple Include chains.
        // Previous implementation used 4 Include/ThenInclude chains which produced 25s+ queries.
        var result = await query
            .OrderBy(u => u.FullName)
            .Select(u => new UserListDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive,
                Roles = u.UserRoleAssignments.Select(ra => ra.Role.RoleName).ToList(),
                Plants = u.UserPlantScopes.Select(ps => ps.Plant.Code).ToList(),
                Departments = u.UserDepartmentScopes.Select(ds => ds.Department.Code).ToList(),
                CanEdit = isSystemAdmin || (isLocalManager && (
                    u.UserPlantScopes.Any(ps => lmPlantIds.Contains(ps.PlantId)) ||
                    u.UserDepartmentScopes.Any(ds => lmDeptIds.Contains(ds.DepartmentId))
                ))
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDetailsDto>> GetUser(Guid id)
    {
        var user = await _context.Users
            .Include(u => u.UserRoleAssignments)
            .Include(u => u.UserPlantScopes)
            .Include(u => u.UserDepartmentScopes)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        return Ok(new UserDetailsDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            RoleIds = user.UserRoleAssignments.Select(ra => ra.RoleId).ToList(),
            PlantIds = user.UserPlantScopes.Select(ps => ps.PlantId).ToList(),
            DepartmentIds = user.UserDepartmentScopes.Select(ds => ds.DepartmentId).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<ResetPasswordResponseDto>> CreateUser([FromBody] CreateUserDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
        {
            await _adminLogWriter.WriteAsync("Activity", "UserManagement", "USER_CREATION_FAILED", 
                $"Tentativa de criar utilizador com e-mail já existente: {dto.Email} por {User.FindFirstValue(ClaimTypes.Email)}", 
                payload: System.Text.Json.JsonSerializer.Serialize(new { dto.Email, Reason = "Duplicate Email" }));
            
            return BadRequest(new { message = "E-mail já está em utilização." });
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isSystemAdmin = roles.Contains("System Administrator");

        // Permission check for Local Manager
        if (!isSystemAdmin)
        {
            if (!roles.Contains("Local Manager")) return Forbid();
            if (!await CheckRolePermissions(dto.RoleIds)) return BadRequest(new { message = "Não tem permissão para atribuir uma das funções selecionadas." });
            if (!await CheckScopePermissions(dto.PlantIds, dto.DepartmentIds)) return BadRequest(new { message = "Não tem permissão para atribuir este escopo de acesso." });
        }

        var tempPassword = _passwordHasher.GenerateRandomPassword();
        var user = new User
        {
            FullName = dto.FullName,
            Email = dto.Email,
            IsActive = dto.IsActive,
            MustChangePassword = true,
            PasswordHash = _passwordHasher.HashPassword(tempPassword),
            CreatedAt = DateTime.UtcNow
        };

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            foreach (var roleId in dto.RoleIds)
                _context.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = roleId });

            foreach (var plantId in dto.PlantIds)
                _context.UserPlantScopes.Add(new UserPlantScope { UserId = user.Id, PlantId = plantId });

            foreach (var deptId in dto.DepartmentIds)
                _context.UserDepartmentScopes.Add(new UserDepartmentScope { UserId = user.Id, DepartmentId = deptId });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _adminLogWriter.WriteAsync("Activity", "UserManagement", "USER_CREATED", 
                $"Utilizador {user.Email} criado por {User.FindFirstValue(ClaimTypes.Email)}", 
                payload: System.Text.Json.JsonSerializer.Serialize(new { user.Id, user.Email, dto.RoleIds }));

            return Ok(new ResetPasswordResponseDto { NewPassword = tempPassword });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Erro ao criar utilizador.", details = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        var user = await _context.Users
            .Include(u => u.UserRoleAssignments)
            .Include(u => u.UserPlantScopes)
            .Include(u => u.UserDepartmentScopes)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isSystemAdmin = roles.Contains("System Administrator");

        // Stricter subset logic for Local Manager
        if (!isSystemAdmin)
        {
            if (!roles.Contains("Local Manager")) return Forbid();
            
            // Current actor must manage the target user's CURRENT scope (subset logic)
            var currentPlants = user.UserPlantScopes.Select(s => s.PlantId).ToList();
            var currentDepts = user.UserDepartmentScopes.Select(s => s.DepartmentId).ToList();
            if (!await CheckScopePermissions(currentPlants, currentDepts)) return Forbid();

            // And the NEW scope must also be a subset of the actor's scope
            if (!await CheckScopePermissions(dto.PlantIds, dto.DepartmentIds)) return BadRequest(new { message = "Não tem permissão para atribuir este novo escopo." });
            
            // Roles check
            if (!await CheckRolePermissions(dto.RoleIds)) return BadRequest(new { message = "Não tem permissão para gerir uma das funções selecionadas." });
        }

        user.FullName = dto.FullName;
        user.IsActive = dto.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Clear existing assignments
            _context.UserRoleAssignments.RemoveRange(user.UserRoleAssignments);
            _context.UserPlantScopes.RemoveRange(user.UserPlantScopes);
            _context.UserDepartmentScopes.RemoveRange(user.UserDepartmentScopes);

            // Add new assignments
            foreach (var roleId in dto.RoleIds)
                _context.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = roleId });

            foreach (var plantId in dto.PlantIds)
                _context.UserPlantScopes.Add(new UserPlantScope { UserId = user.Id, PlantId = plantId });

            foreach (var deptId in dto.DepartmentIds)
                _context.UserDepartmentScopes.Add(new UserDepartmentScope { UserId = user.Id, DepartmentId = deptId });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _adminLogWriter.WriteAsync("Activity", "UserManagement", "USER_UPDATED", 
                $"Utilizador {user.Email} atualizado por {User.FindFirstValue(ClaimTypes.Email)}",
                payload: System.Text.Json.JsonSerializer.Serialize(new { user.Id, dto.RoleIds, dto.IsActive }));

            return NoContent();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Erro ao atualizar utilizador.", details = ex.Message });
        }
    }

    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<ResetPasswordResponseDto>> ResetPassword(Guid id)
    {
        var user = await _context.Users
            .Include(u => u.UserPlantScopes)
            .Include(u => u.UserDepartmentScopes)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var isSystemAdmin = roles.Contains("System Administrator");

        if (!isSystemAdmin)
        {
            if (!roles.Contains("Local Manager")) return Forbid();
            var currentPlants = user.UserPlantScopes.Select(s => s.PlantId).ToList();
            var currentDepts = user.UserDepartmentScopes.Select(s => s.DepartmentId).ToList();
            if (!await CheckScopePermissions(currentPlants, currentDepts)) return Forbid();
        }

        var newPassword = await _authService.ResetPasswordAsync(id);

        await _adminLogWriter.WriteAsync("Activity", "UserManagement", "PASSWORD_RESET", 
            $"Palavra-passe do utilizador {user.Email} reposta por {User.FindFirstValue(ClaimTypes.Email)}");

        return Ok(new ResetPasswordResponseDto { NewPassword = newPassword });
    }

    #region Helpers

    private bool CanManageUserSubset(User target, List<int> lmPlantIds, List<int> lmDeptIds)
    {
        // Rule: TargetUser.Plants ⊆ LM.Plants AND TargetUser.Departments ⊆ LM.Departments
        var targetPlantIds = target.UserPlantScopes.Select(s => s.PlantId).ToList();
        var targetDeptIds = target.UserDepartmentScopes.Select(s => s.DepartmentId).ToList();

        // If target has NO plants or NO departments, LM must be SA to manage it
        return targetPlantIds.Any() && targetDeptIds.Any() &&
               targetPlantIds.All(p => lmPlantIds.Contains(p)) && 
               targetDeptIds.All(d => lmDeptIds.Contains(d));
    }

    private async Task<bool> CheckRolePermissions(List<int> roleIds)
    {
        var requestedRoles = await _context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.RoleName)
            .ToListAsync();

        return requestedRoles.All(r => AllowedRolesForLM.Contains(r));
    }

    private async Task<bool> CheckScopePermissions(List<int> plantIds, List<int> departmentIds)
    {
        var lmPlantIds = await _context.UserPlantScopes.Where(s => s.UserId == CurrentUserId).Select(s => s.PlantId).ToListAsync();
        var lmDeptIds = await _context.UserDepartmentScopes.Where(s => s.UserId == CurrentUserId).Select(s => s.DepartmentId).ToListAsync();

        return plantIds.All(p => lmPlantIds.Contains(p)) && 
               departmentIds.All(d => lmDeptIds.Contains(d));
    }

    #endregion
}
