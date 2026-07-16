using AlplaPortal.Application.DTOs.Users;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

using Microsoft.Extensions.Configuration;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : BaseController
{
    private readonly AdminLogWriter _adminLogWriter;
    private readonly IAuthService _authService;
    private readonly IPasswordHasher _passwordHasher;

    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;

    public UsersController(
        ApplicationDbContext context, 
        AdminLogWriter adminLogWriter,
        IAuthService authService,
        IPasswordHasher passwordHasher,
        IEmailService emailService,
        IConfiguration configuration) : base(context)
    {
        _adminLogWriter = adminLogWriter;
        _authService = authService;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _configuration = configuration;
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
            Plants = user.UserPlantScopes.Select(ps => ps.Plant.Code ?? string.Empty).ToList(),
            Departments = user.UserDepartmentScopes.Select(ds => ds.Department.Code ?? string.Empty).ToList(),
            CanEdit = false
        });
    }

    [HttpGet("assignable-roles")]
    public async Task<ActionResult<IEnumerable<string>>> GetAssignableRoles()
    {
        var assignableRoles = await GetAssignableRolesForCurrentUserAsync();
        return Ok(assignableRoles);
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
                Plants = u.UserPlantScopes.Select(ps => ps.Plant.Code ?? string.Empty).ToList(),
                Departments = u.UserDepartmentScopes.Select(ds => ds.Department.Code ?? string.Empty).ToList(),
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

        // Phase C: read-only view of area-approval responsibilities, derived from
        // DepartmentManagers. Edited exclusively in Dados Mestres → Departamentos.
        var approvalResponsibilities = await _context.DepartmentManagers
            .AsNoTracking()
            .Where(dm => dm.UserId == id)
            .OrderBy(dm => dm.Department.Name).ThenBy(dm => dm.PlantId == null ? 0 : 1)
            .Select(dm => new UserApprovalResponsibilityDto
            {
                DepartmentName = dm.Department.Name,
                PlantName = dm.Plant != null ? dm.Plant.Name : null,
                IsActive = dm.IsActive
            })
            .ToListAsync();

        return Ok(new UserDetailsDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            RoleIds = user.UserRoleAssignments.Select(ra => ra.RoleId).ToList(),
            PlantIds = user.UserPlantScopes.Select(ps => ps.PlantId).ToList(),
            DepartmentIds = user.UserDepartmentScopes.Select(ds => ds.DepartmentId).ToList(),
            ApprovalResponsibilities = approvalResponsibilities
        });
    }

    /// <summary>
    /// Phase C guard: the "Area Approver" role is derived from DepartmentManagers and can
    /// never be assigned manually. Old clients that still send it get a controlled error
    /// instead of a silently ineffective assignment.
    /// </summary>
    private async Task<string?> ValidateNoManualAreaApproverAsync(List<int> roleIds)
    {
        var areaApproverRoleId = await _context.Roles
            .Where(r => r.RoleName == RoleConstants.AreaApprover)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync();

        if (areaApproverRoleId.HasValue && roleIds.Contains(areaApproverRoleId.Value))
            return "A role Area Approver é atribuída automaticamente através do cadastro de managers por departamento e planta (Dados Mestres → Departamentos).";

        return null;
    }

    private readonly string[] _allowedCorporateDomains = new[] { "alpla.com" };

    private bool IsValidCorporateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        email = email.Trim();
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            if (addr.Address != email) return false;
            
            var domain = addr.Host.ToLowerInvariant();
            return _allowedCorporateDomains.Contains(domain);
        }
        catch
        {
            return false;
        }
    }

    [HttpPost]
    public async Task<ActionResult<CreateUserResponseDto>> CreateUser([FromBody] CreateUserDto dto)
    {
        if (!IsValidCorporateEmail(dto.Email))
        {
            await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_CREATE_BLOCKED", 
                $"Tentativa de criar utilizador com e-mail corporativo inválido: {dto.Email} por {User.FindFirstValue(ClaimTypes.Email)}", 
                payload: System.Text.Json.JsonSerializer.Serialize(new { dto.Email, Reason = "Invalid corporate email address" }));
            
            return BadRequest(new { message = "Apenas e-mails corporativos ALPLA são permitidos." });
        }

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
            if (!roles.Contains("Local Manager")) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_CREATE_BLOCKED", 
                    $"Tentativa não autorizada de criar utilizador por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: Não é gestor.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { dto.Email, Reason = "Manager Role Missing" }));
                return Forbid();
            }
            if (!await CheckRolePermissions(dto.RoleIds)) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_CREATE_BLOCKED", 
                    $"Tentativa não autorizada de criar utilizador por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: Current manager does not have permission to assign requested roles.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { dto.Email, dto.RoleIds, Reason = "Restricted Role" }));
                return BadRequest(new { message = "Não tem permissão para atribuir uma das funções selecionadas." });
            }
            if (!await CheckScopePermissions(dto.PlantIds, dto.DepartmentIds)) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_CREATE_BLOCKED", 
                    $"Tentativa não autorizada de criar utilizador por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: Escopo restrito.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { dto.Email, dto.PlantIds, dto.DepartmentIds, Reason = "Out of Scope" }));
                return BadRequest(new { message = "Não tem permissão para atribuir este escopo de acesso." });
            }
        }

        var user = new User
        {
            FullName = dto.FullName,
            Email = dto.Email,
            IsActive = dto.IsActive,
            MustChangePassword = true,
            PasswordHash = null,
            CreatedAt = DateTime.UtcNow
        };

        // Phase C: "Area Approver" is derived-only — reject manual assignment for every
        // caller, including System Administrators and old clients.
        var manualAreaApproverError = await ValidateNoManualAreaApproverAsync(dto.RoleIds);
        if (manualAreaApproverError != null)
            return BadRequest(new { message = manualAreaApproverError });

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
            
            // Password setup token generation
            var token = await _authService.GeneratePasswordSetupTokenAsync(user.Id);
            
            // Try to send email
            var frontendBaseUrl = _configuration["AppConfig:FrontendBaseUrl"] ?? "http://localhost:5173";
            var encodedEmail = Uri.EscapeDataString(user.Email);
            var passwordSetupUrl = $"{frontendBaseUrl}/reset-password?token={token}&email={encodedEmail}";
            
            var expirationHoursConfig = _configuration["AppConfig:UserOnboarding:PasswordSetupTokenExpirationHours"];
            if (!int.TryParse(expirationHoursConfig, out int expirationHours))
            {
                expirationHours = 24;
            }

            var emailSent = await _emailService.SendOnboardingEmailAsync(user.Email, user.FullName, frontendBaseUrl, passwordSetupUrl, expirationHours);

            if (emailSent)
            {
                await _adminLogWriter.WriteAsync("Activity", "UserManagement", "USER_CREATED", 
                    $"Utilizador {user.Email} criado por {User.FindFirstValue(ClaimTypes.Email)} e e-mail de onboarding enviado.", 
                    payload: System.Text.Json.JsonSerializer.Serialize(new { user.Id, user.Email, dto.RoleIds, EmailSent = true }));
                
                return Ok(new CreateUserResponseDto { EmailSent = true, Message = "Utilizador criado e e-mail de onboarding enviado com sucesso." });
            }
            else
            {
                await _adminLogWriter.WriteAsync("Warning", "UserManagement", "ONBOARDING_EMAIL_FAILED", 
                    $"Utilizador {user.Email} criado por {User.FindFirstValue(ClaimTypes.Email)}, mas envio do e-mail falhou.", 
                    payload: System.Text.Json.JsonSerializer.Serialize(new { user.Id, user.Email, dto.RoleIds, EmailSent = false }));
                
                return Ok(new CreateUserResponseDto { EmailSent = false, Message = "Utilizador criado, mas não foi possível enviar o e-mail de onboarding. Verifique as configurações SMTP ou contacte o administrador." });
            }
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
        if (!IsValidCorporateEmail(dto.Email))
        {
            await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_UPDATE_BLOCKED", 
                $"Tentativa de atualizar utilizador com e-mail corporativo inválido: {dto.Email} por {User.FindFirstValue(ClaimTypes.Email)}", 
                payload: System.Text.Json.JsonSerializer.Serialize(new { TargetUserId = id, dto.Email, Reason = "Invalid corporate email address" }));
            
            return BadRequest(new { message = "Apenas e-mails corporativos ALPLA são permitidos." });
        }

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
            if (!roles.Contains("Local Manager")) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_UPDATE_BLOCKED", 
                    $"Tentativa não autorizada de atualizar utilizador por {User.FindFirstValue(ClaimTypes.Email)}.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { TargetUserId = user.Id, TargetUserEmail = user.Email, Reason = "Manager Role Missing" }));
                return Forbid();
            }
            
            // Check if target user HAS restricted roles that the CURRENT user cannot manage
            var targetUserRoles = user.UserRoleAssignments.Select(ra => ra.Role.RoleName).ToList();
            var assignableRoles = await GetAssignableRolesForCurrentUserAsync();
            
            if (targetUserRoles.Any(r => !assignableRoles.Contains(r)))
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_UPDATE_BLOCKED", 
                    $"Tentativa não autorizada de atualizar utilizador {user.Email} por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: Target user has restricted roles.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { TargetUserId = user.Id, TargetUserEmail = user.Email, Reason = "Target User Restricted Role" }));
                return BadRequest(new { message = "Não tem permissão para editar um utilizador com funções restritas fora da sua autoridade." });
            }

            // Current actor must manage the target user's CURRENT scope (subset logic)
            var currentPlants = user.UserPlantScopes.Select(s => s.PlantId).ToList();
            var currentDepts = user.UserDepartmentScopes.Select(s => s.DepartmentId).ToList();
            if (!await CheckScopePermissions(currentPlants, currentDepts)) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_UPDATE_BLOCKED", 
                    $"Tentativa não autorizada de atualizar utilizador {user.Email} por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: Target user is out of current scope.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { TargetUserId = user.Id, TargetUserEmail = user.Email, Reason = "Target User Out of Scope" }));
                return Forbid();
            }

            // And the NEW scope must also be a subset of the actor's scope
            if (!await CheckScopePermissions(dto.PlantIds, dto.DepartmentIds)) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_UPDATE_BLOCKED", 
                    $"Tentativa não autorizada de atualizar utilizador {user.Email} por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: New scope is out of manager's scope.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { TargetUserId = user.Id, TargetUserEmail = user.Email, Reason = "New Scope Restricted" }));
                return BadRequest(new { message = "Não tem permissão para atribuir este novo escopo." });
            }
            
            // Roles check
            if (!await CheckRolePermissions(dto.RoleIds)) 
            {
                await _adminLogWriter.WriteAsync("Security", "UserManagement", "USER_UPDATE_BLOCKED", 
                    $"Tentativa não autorizada de atualizar utilizador {user.Email} por {User.FindFirstValue(ClaimTypes.Email)}. Motivo: Cannot assign requested roles.",
                    payload: System.Text.Json.JsonSerializer.Serialize(new { TargetUserId = user.Id, TargetUserEmail = user.Email, dto.RoleIds, Reason = "Restricted Role Assigned" }));
                return BadRequest(new { message = "Não tem permissão para gerir uma das funções selecionadas." });
            }
        }

        user.FullName = dto.FullName;
        user.IsActive = dto.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Phase C: "Area Approver" is derived-only — reject manual assignment for
            // every caller, including System Administrators and old clients.
            var manualAreaApproverError = await ValidateNoManualAreaApproverAsync(dto.RoleIds);
            if (manualAreaApproverError != null)
                return BadRequest(new { message = manualAreaApproverError });

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

            // Check restricted roles
            var assignableRoles = await GetAssignableRolesForCurrentUserAsync();
            var targetUserRoles = await _context.UserRoleAssignments.Where(ra => ra.UserId == user.Id).Select(ra => ra.Role.RoleName).ToListAsync();
            if (targetUserRoles.Any(r => !assignableRoles.Contains(r))) return BadRequest(new { message = "Não tem permissão para repor a palavra-passe de um utilizador com funções restritas." });
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

        var assignableRoles = await GetAssignableRolesForCurrentUserAsync();
        return requestedRoles.All(r => assignableRoles.Contains(r));
    }

    private async Task<List<string>> GetAssignableRolesForCurrentUserAsync()
    {
        var currentUserRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (currentUserRoles.Contains("System Administrator"))
        {
            // Phase C: "Area Approver" is derived from DepartmentManagers — never
            // manually assignable, so it is not offered to any administrator.
            return await _context.Roles
                .Where(r => r.RoleName != RoleConstants.AreaApprover)
                .Select(r => r.RoleName)
                .ToListAsync();
        }

        var roles = new List<string>
        {
            "Requester",
            "Receiving",
            "Viewer / Management"
        };

        if (currentUserRoles.Contains("HR"))
        {
            roles.Add("HR");
        }

        if (currentUserRoles.Contains("Import"))
        {
            roles.Add("Import");
        }

        return roles;
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
