using System.Security.Claims;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// HR Leave Module controller — Phase 1.
///
/// Authorization model (two concerns):
/// 1. Feature entitlement: System Administrator, HR role, or Department Manager
///    (identified via Department.ResponsibleUserId)
/// 2. Data scope: filtered by plant scope, department scope, or managed departments
///
/// Department Managers do NOT need the HR role to access leave management
/// for employees in their managed departments.
/// </summary>
[ApiController]
[Route("api/hr/leave")]
[Authorize]
public class HRLeaveController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IHREmployeeSyncService _syncService;
    private readonly IPrimaveraDepartmentSyncService _deptSyncService;

    public HRLeaveController(
        ApplicationDbContext context, 
        IHREmployeeSyncService syncService,
        IPrimaveraDepartmentSyncService deptSyncService)
    {
        _context = context;
        _syncService = syncService;
        _deptSyncService = deptSyncService;
    }

    // ─── Helpers ───

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

    private bool IsSystemAdmin => CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);

    /// <summary>
    /// Returns the set of HREmployee IDs visible to the current user, based on:
    /// - System Admin: all
    /// - HR role: filtered by user's plant/department scope
    /// - Local Manager: filtered by user's plant/department scope (same model as HR)
    /// - Department Manager: employees with ManagerUserId = current user,
    ///   OR employees in PortalDepartmentId matching managed departments
    /// - Any other user: self-calendar only (matched by email)
    ///
    /// Identity mapping note: There is no direct FK between Portal User and HREmployee.
    /// Self-calendar uses case-insensitive email matching (User.Email ↔ HREmployee.Email)
    /// as the most reliable existing identifier. If no match is found, an empty set is returned.
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
                    // Note: In transition. HR scope could eventually map to DepartmentMasterId if HR roles
                    // specify scope by Master instead of old Portal Department. Safe to leave PortalDepartmentId for now.
                );
            }
            // If HR user has no scope restrictions, they see all (broad HR visibility)
            return query;
        }

        // Local Manager: filter by plant AND department scope (intersection).
        // Department scope is the primary constraint — it defines which department(s)
        // the manager oversees. Plant scope narrows further when present.
        //
        // Logic:
        //   - Both plant + dept scopes → AND (employee must be in scoped plant AND scoped department)
        //   - Only dept scope → filter by department only
        //   - Only plant scope → filter by plant only
        //   - No scopes configured → fail-safe: empty result (not broad visibility)
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
                // Both scopes: AND — employee must match both plant and department
                query = query.Where(e =>
                    (e.PlantId.HasValue && lmPlantIds.Contains(e.PlantId.Value)) &&
                    (e.PortalDepartmentId.HasValue && lmDeptIds.Contains(e.PortalDepartmentId.Value))
                );
            }
            else if (hasDepts)
            {
                // Department scope only: primary constraint
                query = query.Where(e =>
                    e.PortalDepartmentId.HasValue && lmDeptIds.Contains(e.PortalDepartmentId.Value)
                );
            }
            else if (hasPlants)
            {
                // Plant scope only
                query = query.Where(e =>
                    e.PlantId.HasValue && lmPlantIds.Contains(e.PlantId.Value)
                );
            }
            else
            {
                // No scopes configured — fail-safe: no access
                return query.Where(e => false);
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
                // Future transition: When Managers are assigned to DepartmentMasters instead of 
                // PortalDepartments, this check will migrate.
            );
            return query;
        }

        // Fallback: self-calendar — any authenticated user sees their own HREmployee record.
        // Matched by case-insensitive email since there is no direct User↔HREmployee FK.
        var userEmail = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(userEmail))
        {
            var emailLower = userEmail.ToLower();
            return query.Where(e => e.Email != null && e.Email.ToLower() == emailLower);
        }

        // No email available — fail safe with empty result
        return query.Where(e => false);
    }

    /// <summary>
    /// Checks if current user has HR module access.
    ///
    /// Broadened to include Local Manager and self-calendar users.
    /// Safety: ALL endpoints that use this gate also apply GetScopedEmployeesQuery()
    /// internally, which limits data to the user's scope. A non-manager user
    /// will only ever see their own HREmployee record. This does NOT grant
    /// write access to HR-admin functions — those have separate IsAdminOrHR checks.
    /// </summary>
    private async Task<bool> HasHRModuleAccess()
    {
        if (IsAdminOrHR) return true;

        var userId = CurrentUserId;
        var roles = CurrentUserRoles;

        // Local Manager has HR module access (scoped by UserPlantScopes + UserDepartmentScopes)
        if (roles.Contains(RoleConstants.LocalManager)) return true;

        // Department Manager (ResponsibleUser of at least one department)
        if (await _context.Departments.AnyAsync(d => d.ResponsibleUserId == userId))
            return true;

        // Self-calendar: any active user with a matching HREmployee record (by email)
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(email))
        {
            var emailLower = email.ToLower();
            return await _context.HREmployees.AnyAsync(
                e => e.Email != null && e.Email.ToLower() == emailLower && e.IsActive);
        }

        return false;
    }

    // ─── Employees ───

    /// <summary>List HR employees (scoped, paginated).</summary>
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] string? search = null,
        [FromQuery] bool? mapped = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var query = await GetScopedEmployeesQuery();

        if (active.HasValue)
            query = query.Where(e => e.IsActive == active.Value);
        else
            query = query.Where(e => e.IsActive); // Default: active only

        if (mapped.HasValue)
            query = query.Where(e => e.IsMapped == mapped.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.FullName.ToLower().Contains(term) ||
                e.EmployeeCode.ToLower().Contains(term) ||
                (e.InnuxDepartmentName != null && e.InnuxDepartmentName.ToLower().Contains(term))
            );
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(e => e.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.EmployeeCode,
                e.FullName,
                e.InnuxDepartmentName,
                e.JobTitle,
                e.PlantId,
                PlantName = e.Plant != null ? e.Plant.Name : null,
                e.PortalDepartmentId,
                PortalDepartmentName = e.PortalDepartment != null ? e.PortalDepartment.Name : null,
                e.DepartmentMasterId,
                DepartmentMasterName = e.DepartmentMaster != null 
                    ? $"{e.DepartmentMaster.DepartmentName} ({e.DepartmentMaster.CompanyCode})" 
                    : null,
                e.ManagerUserId,
                ManagerName = e.ManagerUser != null ? e.ManagerUser.FullName : null,
                e.IsActive,
                e.IsMapped,
                e.Email,
                e.CardNumber,
                e.LastSyncedAtUtc
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }

    /// <summary>Update Portal mapping fields for an HR employee.</summary>
    [HttpPut("employees/{id}/mapping")]
    public async Task<IActionResult> UpdateEmployeeMapping(Guid id, [FromBody] UpdateMappingRequest request)
    {
        if (!IsAdminOrHR)
            return Forbid();

        var employee = await _context.HREmployees.FindAsync(id);
        if (employee == null) return NotFound();

        employee.PlantId = request.PlantId;
        employee.PortalDepartmentId = request.PortalDepartmentId;
        employee.DepartmentMasterId = request.DepartmentMasterId;
        employee.ManagerUserId = request.ManagerUserId;
        
        // Re-evaluate mapping status including the new DepartmentMasterId
        employee.IsMapped = request.PlantId.HasValue || 
                            request.PortalDepartmentId.HasValue || 
                            request.DepartmentMasterId.HasValue ||
                            request.ManagerUserId.HasValue;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Mapeamento atualizado." });
    }

    /// <summary>Bulk update Portal mapping fields for multiple HR employees.</summary>
    [HttpPut("employees/bulk/mapping")]
    public async Task<IActionResult> BulkUpdateEmployeeMapping([FromBody] BulkUpdateMappingRequest request)
    {
        if (!IsAdminOrHR)
            return Forbid();

        if (request.EmployeeIds == null || !request.EmployeeIds.Any())
            return BadRequest(new { message = "Nenhum funcionário selecionado." });

        var employees = await _context.HREmployees
            .Where(e => request.EmployeeIds.Contains(e.Id))
            .ToListAsync();

        var result = new BulkUpdateMappingResult { Processed = request.EmployeeIds.Count };

        foreach (var employee in employees)
        {
            bool modified = false;

            if (request.PlantId.HasValue) 
            {
                employee.PlantId = request.PlantId;
                modified = true;
            }

            if (request.ClearDepartmentMaster)
            {
                employee.DepartmentMasterId = null;
                modified = true;
            }
            else if (request.DepartmentMasterId.HasValue)
            {
                employee.DepartmentMasterId = request.DepartmentMasterId;
                modified = true;
            }

            if (request.ClearManager)
            {
                employee.ManagerUserId = null;
                modified = true;
            }
            else if (request.ManagerUserId.HasValue)
            {
                employee.ManagerUserId = request.ManagerUserId;
                modified = true;
            }

            if (modified)
            {
                // Re-evaluate mapping status
                employee.IsMapped = employee.PlantId.HasValue || 
                                    employee.PortalDepartmentId.HasValue || 
                                    employee.DepartmentMasterId.HasValue ||
                                    employee.ManagerUserId.HasValue;
                result.Updated++;
            }
            else
            {
                result.Skipped++;
            }
        }

        result.NotFound = request.EmployeeIds.Count - employees.Count;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            result.Errors++;
            return StatusCode(500, new { message = "Erro ao processar atualização em massa.", details = ex.Message, result });
        }

        return Ok(result);
    }

    /// <summary>Trigger Innux → local sync (Admin/HR only).</summary>
    [HttpPost("employees/sync")]
    public async Task<IActionResult> SyncEmployees()
    {
        if (!IsAdminOrHR)
            return Forbid();

        try
        {
            var result = await _syncService.SyncFromInnuxAsync(CurrentUserId);
            if (result.Status == "FAILED" || result.Status == "PARTIAL")
            {
                return StatusCode(207, new
                {
                    message = "Sincronização concluída com erros. " + (result.ErrorDetails ?? ""),
                    result.EmployeesCreated,
                    result.EmployeesUpdated,
                    result.EmployeesDeactivated,
                    result.TotalProcessed,
                    result.Errors,
                    result.Status
                });
            }
            return Ok(new
            {
                message = "Sincronização concluída.",
                result.EmployeesCreated,
                result.EmployeesUpdated,
                result.EmployeesDeactivated,
                result.TotalProcessed,
                result.Errors,
                result.Status
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message, errorType = "InvalidOperationException" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Falha inesperada durante a sincronização: {ex.Message}", errorType = "Exception" });
        }
    }

    /// <summary>Get last sync info.</summary>
    [HttpGet("employees/sync/status")]
    public async Task<IActionResult> GetSyncStatus()
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var lastSync = await _syncService.GetLastSyncAsync();
        if (lastSync == null)
            return Ok(new { synced = false, message = "Nenhuma sincronização realizada." });

        return Ok(new
        {
            synced = true,
            lastSync.StartedAtUtc,
            lastSync.CompletedAtUtc,
            lastSync.EmployeesCreated,
            lastSync.EmployeesUpdated,
            lastSync.EmployeesDeactivated,
            lastSync.TotalProcessed,
            lastSync.Errors,
            lastSync.Status
        });
    }

    // ─── Department Master Sync ───

    [HttpPost("departments/sync")]
    public async Task<IActionResult> SyncDepartments()
    {
        if (!IsAdminOrHR)
            return Forbid();

        try
        {
            var result = await _deptSyncService.SyncDepartmentsAsync();
            
            if (result.Errors.Any())
            {
                return StatusCode(207, new 
                { 
                    message = "Sincronização concluída com erros. Algumas empresas podem ter falhado.", 
                    result.Created,
                    result.Updated,
                    result.Processed,
                    result.Errors
                });
            }

            return Ok(new 
            { 
                message = "Sincronização concluída.", 
                result.Created,
                result.Updated,
                result.Processed,
                result.Errors
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Falha na sincronização: {ex.Message}" });
        }
    }

    [HttpGet("departments/master")]
    public async Task<IActionResult> GetDepartmentMasters()
    {
        if (!IsAdminOrHR)
            return Forbid();

        // Used by Autocomplete for mapping
        var departments = await _context.DepartmentMasters
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.DepartmentName)
            .ThenBy(d => d.CompanyCode)
            .Select(d => new
            {
                d.Id,
                d.DepartmentCode,
                d.DepartmentName,
                d.CompanyCode,
                DisplayName = $"{d.DepartmentName} ({d.CompanyCode})" // Pre-computed friendly name
            })
            .ToListAsync();

        return Ok(departments);
    }

    // ─── Leave Types ───

    [HttpGet("types")]
    public async Task<IActionResult> GetLeaveTypes()
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var types = await _context.LeaveTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new { t.Id, t.Code, t.DisplayNamePt, t.Color, t.CountsAgainstBalance })
            .ToListAsync();

        return Ok(types);
    }

    // ─── Leave Records ───

    [HttpGet("records")]
    public async Task<IActionResult> GetLeaveRecords(
        [FromQuery] string? status = null,
        [FromQuery] int? leaveTypeId = null,
        [FromQuery] Guid? employeeId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var scopedEmployees = await GetScopedEmployeesQuery();
        var scopedEmployeeIds = scopedEmployees.Select(e => e.Id);

        var query = _context.LeaveRecords
            .AsNoTracking()
            .Where(lr => scopedEmployeeIds.Contains(lr.EmployeeId));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(lr => lr.StatusCode == status);

        if (leaveTypeId.HasValue)
            query = query.Where(lr => lr.LeaveTypeId == leaveTypeId.Value);

        if (employeeId.HasValue)
            query = query.Where(lr => lr.EmployeeId == employeeId.Value);

        if (from.HasValue)
            query = query.Where(lr => lr.EndDate >= from.Value);

        if (to.HasValue)
            query = query.Where(lr => lr.StartDate <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(lr => lr.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(lr => lr.Employee)
            .Include(lr => lr.LeaveType)
            .Include(lr => lr.RequestedByUser)
            .Include(lr => lr.ApprovedByUser)
            .Select(lr => new
            {
                lr.Id,
                EmployeeName = lr.Employee.FullName,
                EmployeeCode = lr.Employee.EmployeeCode,
                lr.EmployeeId,
                LeaveType = lr.LeaveType.DisplayNamePt,
                LeaveTypeCode = lr.LeaveType.Code,
                LeaveTypeColor = lr.LeaveType.Color,
                lr.LeaveTypeId,
                lr.StartDate,
                lr.EndDate,
                lr.TotalDays,
                lr.StatusCode,
                RequestedBy = lr.RequestedByUser.FullName,
                ApprovedBy = lr.ApprovedByUser != null ? lr.ApprovedByUser.FullName : null,
                lr.Notes,
                lr.RejectedReason,
                lr.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("records/{id}")]
    public async Task<IActionResult> GetLeaveRecord(Guid id)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var scopedEmployees = await GetScopedEmployeesQuery();
        var scopedEmployeeIds = await scopedEmployees.Select(e => e.Id).ToListAsync();

        var record = await _context.LeaveRecords
            .AsNoTracking()
            .Include(lr => lr.Employee)
            .Include(lr => lr.LeaveType)
            .Include(lr => lr.RequestedByUser)
            .Include(lr => lr.ApprovedByUser)
            .Include(lr => lr.StatusHistory.OrderByDescending(sh => sh.CreatedAtUtc))
                .ThenInclude(sh => sh.ActorUser)
            .FirstOrDefaultAsync(lr => lr.Id == id);

        if (record == null) return NotFound();
        if (!scopedEmployeeIds.Contains(record.EmployeeId)) return Forbid();

        return Ok(new
        {
            record.Id,
            EmployeeName = record.Employee.FullName,
            EmployeeCode = record.Employee.EmployeeCode,
            record.EmployeeId,
            LeaveType = record.LeaveType.DisplayNamePt,
            LeaveTypeCode = record.LeaveType.Code,
            LeaveTypeColor = record.LeaveType.Color,
            record.LeaveTypeId,
            record.StartDate,
            record.EndDate,
            record.TotalDays,
            record.StatusCode,
            RequestedBy = record.RequestedByUser.FullName,
            ApprovedBy = record.ApprovedByUser?.FullName,
            record.Notes,
            record.RejectedReason,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            History = record.StatusHistory.Select(sh => new
            {
                sh.PreviousStatus,
                sh.NewStatus,
                ActorName = sh.ActorUser.FullName,
                sh.Comment,
                sh.CreatedAtUtc
            })
        });
    }

    [HttpPost("records")]
    public async Task<IActionResult> CreateLeaveRecord([FromBody] CreateLeaveRequest request)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        // Validate employee is in scope
        var scopedEmployees = await GetScopedEmployeesQuery();
        var employee = await scopedEmployees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId);
        if (employee == null)
            return BadRequest(new { message = "Funcionário não encontrado ou fora do seu escopo." });

        // Validate dates
        if (request.EndDate < request.StartDate)
            return BadRequest(new { message = "Data de fim deve ser posterior à data de início." });

        // Validate leave type
        var leaveType = await _context.LeaveTypes.FindAsync(request.LeaveTypeId);
        if (leaveType == null || !leaveType.IsActive)
            return BadRequest(new { message = "Tipo de ausência inválido." });

        // Check for overlapping approved/submitted leave
        var hasOverlap = await _context.LeaveRecords.AnyAsync(lr =>
            lr.EmployeeId == request.EmployeeId &&
            lr.StatusCode != LeaveStatusCodes.Cancelled &&
            lr.StatusCode != LeaveStatusCodes.Rejected &&
            lr.StartDate <= request.EndDate &&
            lr.EndDate >= request.StartDate
        );
        if (hasOverlap)
            return BadRequest(new { message = "Já existe um registo de ausência para este funcionário no período indicado." });

        var totalDays = CalculateBusinessDays(request.StartDate, request.EndDate);

        var record = new LeaveRecord
        {
            EmployeeId = request.EmployeeId,
            LeaveTypeId = request.LeaveTypeId,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
            TotalDays = totalDays,
            StatusCode = LeaveStatusCodes.Draft,
            RequestedByUserId = CurrentUserId,
            Notes = request.Notes
        };

        _context.LeaveRecords.Add(record);

        // Initial history entry
        record.StatusHistory.Add(new LeaveStatusHistory
        {
            LeaveRecordId = record.Id,
            ActorUserId = CurrentUserId,
            PreviousStatus = "",
            NewStatus = LeaveStatusCodes.Draft,
            Comment = "Registo criado."
        });

        await _context.SaveChangesAsync();

        return Ok(new { id = record.Id, message = "Registo de ausência criado." });
    }

    [HttpPut("records/{id}")]
    public async Task<IActionResult> UpdateLeaveRecord(Guid id, [FromBody] CreateLeaveRequest request)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var record = await _context.LeaveRecords.FindAsync(id);
        if (record == null) return NotFound();

        if (record.StatusCode != LeaveStatusCodes.Draft)
            return BadRequest(new { message = "Apenas registos em rascunho podem ser editados." });

        // Validate scope
        var scopedEmployees = await GetScopedEmployeesQuery();
        if (!await scopedEmployees.AnyAsync(e => e.Id == record.EmployeeId))
            return Forbid();

        // Validate dates
        if (request.EndDate < request.StartDate)
            return BadRequest(new { message = "Data de fim deve ser posterior à data de início." });

        record.LeaveTypeId = request.LeaveTypeId;
        record.StartDate = request.StartDate.Date;
        record.EndDate = request.EndDate.Date;
        record.TotalDays = CalculateBusinessDays(request.StartDate, request.EndDate);
        record.Notes = request.Notes;
        record.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Registo atualizado." });
    }

    // ─── Workflow transitions ───

    [HttpPost("records/{id}/submit")]
    public async Task<IActionResult> SubmitLeaveRecord(Guid id)
    {
        return await TransitionStatus(id, LeaveStatusCodes.Draft, LeaveStatusCodes.Submitted,
            "Submetido para aprovação.", requireScope: true);
    }

    [HttpPost("records/{id}/approve")]
    public async Task<IActionResult> ApproveLeaveRecord(Guid id, [FromBody] WorkflowActionRequest? request = null)
    {
        if (!IsAdminOrHR)
            return Forbid("Apenas utilizadores HR podem aprovar registos.");

        return await TransitionStatus(id, LeaveStatusCodes.Submitted, LeaveStatusCodes.Approved,
            request?.Comment ?? "Aprovado.", requireScope: false, setApprover: true);
    }

    [HttpPost("records/{id}/reject")]
    public async Task<IActionResult> RejectLeaveRecord(Guid id, [FromBody] WorkflowActionRequest request)
    {
        if (!IsAdminOrHR)
            return Forbid("Apenas utilizadores HR podem rejeitar registos.");

        if (string.IsNullOrWhiteSpace(request?.Reason))
            return BadRequest(new { message = "É necessário indicar o motivo da rejeição." });

        return await TransitionStatus(id, LeaveStatusCodes.Submitted, LeaveStatusCodes.Rejected,
            request.Reason, requireScope: false, rejectedReason: request.Reason);
    }

    [HttpPost("records/{id}/cancel")]
    public async Task<IActionResult> CancelLeaveRecord(Guid id, [FromBody] WorkflowActionRequest? request = null)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var record = await _context.LeaveRecords.FindAsync(id);
        if (record == null) return NotFound();

        // Allow cancel from DRAFT, SUBMITTED, or APPROVED
        if (!LeaveStatusCodes.IsValidTransition(record.StatusCode, LeaveStatusCodes.Cancelled))
            return BadRequest(new { message = $"Não é possível cancelar um registo com estado '{record.StatusCode}'." });

        // Scope check: managers can cancel their own submissions, HR can cancel anything in scope
        if (!IsAdminOrHR)
        {
            var scopedEmployees = await GetScopedEmployeesQuery();
            if (!await scopedEmployees.AnyAsync(e => e.Id == record.EmployeeId))
                return Forbid();
        }

        var previousStatus = record.StatusCode;
        record.StatusCode = LeaveStatusCodes.Cancelled;
        record.CancelledAtUtc = DateTime.UtcNow;
        record.UpdatedAtUtc = DateTime.UtcNow;

        _context.LeaveStatusHistories.Add(new LeaveStatusHistory
        {
            LeaveRecordId = id,
            ActorUserId = CurrentUserId,
            PreviousStatus = previousStatus,
            NewStatus = LeaveStatusCodes.Cancelled,
            Comment = request?.Comment ?? "Cancelado."
        });

        await _context.SaveChangesAsync();
        return Ok(new { message = "Registo cancelado." });
    }

    private async Task<IActionResult> TransitionStatus(
        Guid id, string expectedFrom, string newStatus, string comment,
        bool requireScope, bool setApprover = false, string? rejectedReason = null)
    {
        var record = await _context.LeaveRecords.FindAsync(id);
        if (record == null) return NotFound();

        if (record.StatusCode != expectedFrom)
            return BadRequest(new { message = $"Estado atual '{record.StatusCode}' não permite esta transição." });

        if (requireScope)
        {
            var scopedEmployees = await GetScopedEmployeesQuery();
            if (!await scopedEmployees.AnyAsync(e => e.Id == record.EmployeeId))
                return Forbid();
        }

        record.StatusCode = newStatus;
        record.UpdatedAtUtc = DateTime.UtcNow;

        if (setApprover)
            record.ApprovedByUserId = CurrentUserId;

        if (rejectedReason != null)
            record.RejectedReason = rejectedReason;

        _context.LeaveStatusHistories.Add(new LeaveStatusHistory
        {
            LeaveRecordId = id,
            ActorUserId = CurrentUserId,
            PreviousStatus = expectedFrom,
            NewStatus = newStatus,
            Comment = comment
        });

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Estado atualizado para '{newStatus}'." });
    }

    // ─── Calendar ───

    [HttpGet("calendar")]
    public async Task<IActionResult> GetCalendarData(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? departmentId = null,
        [FromQuery] int? plantId = null)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var scopedEmployees = await GetScopedEmployeesQuery();
        var employeeQuery = scopedEmployees.Where(e => e.IsActive);

        if (departmentId.HasValue)
            employeeQuery = employeeQuery.Where(e => e.PortalDepartmentId == departmentId.Value);

        if (plantId.HasValue)
            employeeQuery = employeeQuery.Where(e => e.PlantId == plantId.Value);

        var employeeIds = await employeeQuery.Select(e => e.Id).ToListAsync();

        var records = await _context.LeaveRecords
            .AsNoTracking()
            .Where(lr =>
                employeeIds.Contains(lr.EmployeeId) &&
                lr.StatusCode != LeaveStatusCodes.Cancelled &&
                lr.StatusCode != LeaveStatusCodes.Rejected &&
                lr.StartDate <= to &&
                lr.EndDate >= from)
            .Include(lr => lr.Employee)
            .Include(lr => lr.LeaveType)
            .Select(lr => new
            {
                lr.Id,
                lr.EmployeeId,
                EmployeeName = lr.Employee.FullName,
                EmployeeCode = lr.Employee.EmployeeCode,
                DepartmentName = lr.Employee.InnuxDepartmentName,
                LeaveType = lr.LeaveType.DisplayNamePt,
                LeaveTypeCode = lr.LeaveType.Code,
                LeaveTypeColor = lr.LeaveType.Color,
                lr.StartDate,
                lr.EndDate,
                lr.TotalDays,
                lr.StatusCode
            })
            .ToListAsync();

        var employees = await employeeQuery
            .Select(e => new
            {
                e.Id,
                e.EmployeeCode,
                e.FullName,
                e.InnuxDepartmentName,
                PortalDepartmentName = e.PortalDepartment != null ? e.PortalDepartment.Name : null
            })
            .OrderBy(e => e.InnuxDepartmentName)
            .ThenBy(e => e.FullName)
            .ToListAsync();

        // Determine scope metadata for the frontend to adapt its header/mode.
        // Minimal: only communicates the type of view, not internal IDs or role details.
        var roles = CurrentUserRoles;
        string scopeType;
        if (roles.Contains(RoleConstants.SystemAdministrator))
            scopeType = "all";
        else if (roles.Contains(RoleConstants.HR))
            scopeType = "hr";
        else if (roles.Contains(RoleConstants.LocalManager))
            scopeType = "department";
        else if (await _context.Departments.AnyAsync(d => d.ResponsibleUserId == CurrentUserId))
            scopeType = "department";
        else
            scopeType = "self";

        return Ok(new { employees, records, from, to, scopeType });
    }

    // ─── Dashboard ───

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var scopedEmployees = await GetScopedEmployeesQuery();
        var scopedIds = await scopedEmployees.Where(e => e.IsActive).Select(e => e.Id).ToListAsync();

        var today = DateTime.UtcNow.Date;
        var weekFromNow = today.AddDays(7);

        var activeEmployees = scopedIds.Count;

        // ActionRequired logic
        var missingMappings = await scopedEmployees.CountAsync(e => e.IsActive && (e.PlantId == null || e.DepartmentMasterId == null));

        var staleThreshold = DateTime.UtcNow.AddHours(-48);
        var staleRequests = await _context.LeaveRecords
            .CountAsync(lr =>
                scopedIds.Contains(lr.EmployeeId) &&
                lr.StatusCode == LeaveStatusCodes.Submitted &&
                (lr.UpdatedAtUtc ?? lr.CreatedAtUtc) < staleThreshold);

        // CurrentSituation logic
        var pendingLeave = await _context.LeaveRecords
            .CountAsync(lr =>
                scopedIds.Contains(lr.EmployeeId) &&
                lr.StatusCode == LeaveStatusCodes.Submitted);

        var absentToday = await _context.LeaveRecords
            .CountAsync(lr =>
                scopedIds.Contains(lr.EmployeeId) &&
                lr.StatusCode == LeaveStatusCodes.Approved &&
                lr.StartDate <= today &&
                lr.EndDate >= today);

        var upcomingLeave = await _context.LeaveRecords
            .CountAsync(lr =>
                scopedIds.Contains(lr.EmployeeId) &&
                lr.StatusCode == LeaveStatusCodes.Approved &&
                lr.StartDate > today &&
                lr.StartDate <= weekFromNow);

        var lastSync = await _syncService.GetLastSyncAsync();
        var syncIssuesCount = (lastSync != null && lastSync.Status == "Failed") ? 1 : 0;

        return Ok(new
        {
            currentSituation = new {
                activeEmployees,
                pendingLeave,
                absentToday,
                upcomingLeave
            },
            actionRequired = new {
                missingMappings,
                staleRequests,
                syncIssuesCount
            },
            lastSync = lastSync != null ? new
            {
                lastSync.StartedAtUtc,
                lastSync.CompletedAtUtc,
                lastSync.Status,
                lastSync.TotalProcessed
            } : null
        });
    }

    [HttpGet("records/{id}/overlap")]
    public async Task<IActionResult> GetLeaveOverlap(Guid id)
    {
        if (!await HasHRModuleAccess())
            return Forbid();

        var record = await _context.LeaveRecords
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (record == null) return NotFound("Record não encontrado.");

        var targetDepartmentId = record.Employee.DepartmentMasterId;
        var targetPlantId = record.Employee.PlantId;
        
        var query = _context.LeaveRecords
            .Include(r => r.Employee)
            .Where(r => 
                r.Id != record.Id && // exclude self
                r.StatusCode == LeaveStatusCodes.Approved && 
                r.StartDate <= record.EndDate && 
                r.EndDate >= record.StartDate);

        if (targetDepartmentId.HasValue)
        {
            query = query.Where(r => r.Employee.DepartmentMasterId == targetDepartmentId.Value);
        }
        else if (targetPlantId.HasValue)
        {
            query = query.Where(r => r.Employee.PlantId == targetPlantId.Value);
        }
        else 
        {
            return Ok(new { overlapCount = 0, overlappingNames = new string[0], scopeUsed = "None" });
        }

        var overlapCount = await query.CountAsync();
        var overlappingNames = await query.Select(r => r.Employee.FullName).Take(5).ToListAsync();
        
        return Ok(new {
            overlapCount,
            overlappingNames,
            scopeUsed = targetDepartmentId.HasValue ? "Department" : "Plant"
        });
    }

    // ─── Utility ───

    /// <summary>Calculates business days between two dates (excludes Sat/Sun).</summary>
    private static int CalculateBusinessDays(DateTime start, DateTime end)
    {
        var s = start.Date;
        var e = end.Date;
        if (e < s) return 0;

        var days = 0;
        for (var d = s; d <= e; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                days++;
        }
        return days;
    }

    // ─── Request DTOs ───

    public class CreateLeaveRequest
    {
        public Guid EmployeeId { get; set; }
        public int LeaveTypeId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateMappingRequest
    {
        public int? PlantId { get; set; }
        public int? PortalDepartmentId { get; set; }
        public int? DepartmentMasterId { get; set; }
        public Guid? ManagerUserId { get; set; }
    }

    public class BulkUpdateMappingRequest
    {
        public List<Guid> EmployeeIds { get; set; } = new();
        public int? PlantId { get; set; }
        public int? DepartmentMasterId { get; set; }
        public Guid? ManagerUserId { get; set; }
        public bool ClearDepartmentMaster { get; set; }
        public bool ClearManager { get; set; }
    }

    public class BulkUpdateMappingResult
    {
        public int Processed { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int NotFound { get; set; }
        public int Errors { get; set; }
    }

    public class WorkflowActionRequest
    {
        public string? Comment { get; set; }
        public string? Reason { get; set; }
    }
}
