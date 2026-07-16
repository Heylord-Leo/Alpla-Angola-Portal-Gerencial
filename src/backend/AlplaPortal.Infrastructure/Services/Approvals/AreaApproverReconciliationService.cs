using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// Reconciliation report Role × DepartmentManager (rule D2, confirmed 2026-07-15).
/// Mandatory gate before Phase C makes the "Area Approver" role 100% derived:
/// the business validates, user by user, who keeps/gains/loses access.
/// See docs/department-manager-routing-redesign-plan.md §16.1.
/// </summary>
public class AreaApproverReconciliationService
{
    public const string OkDerivado = "OK_DERIVADO";
    public const string PerdeAcesso = "PERDE_ACESSO";
    public const string SoCadastro = "SO_CADASTRO";
    public const string InativoComVinculo = "INATIVO_COM_VINCULO";
    public const string Inconsistente = "INCONSISTENTE";

    private readonly ApplicationDbContext _context;

    public AreaApproverReconciliationService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Phase C — audits the requests still depending on the temporary legacy-nominee
    /// compatibility clause: pending in the area stage with AreaApproverId filled
    /// (only possible for requests submitted before the Phase B cut). Once this list
    /// is empty in PRODUCTION, the IsLegacyNamedAreaApprover clause can be removed.
    /// </summary>
    public async Task<List<LegacyPendingRequestRow>> BuildLegacyPendingRequestsAsync()
    {
        return await _context.Requests
            .AsNoTracking()
            .Where(r => r.AreaApproverId != null
                     && (r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval
                      || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter))
            .Select(r => new LegacyPendingRequestRow
            {
                RequestId = r.Id,
                RequestNumber = r.RequestNumber ?? "S/N",
                DepartmentName = r.Department != null ? r.Department.Name : r.DepartmentId.ToString(),
                PlantName = r.Plant != null ? r.Plant.Name : null,
                NomineeName = r.AreaApprover != null ? r.AreaApprover.FullName : r.AreaApproverId.ToString()!,
                NomineeIsCompatibleManager = _context.DepartmentManagers.Any(dm =>
                    dm.UserId == r.AreaApproverId && dm.IsActive && dm.DepartmentId == r.DepartmentId
                    && (dm.PlantId == null || dm.PlantId == r.PlantId))
            })
            .ToListAsync();
    }

    public class LegacyPendingRequestRow
    {
        public Guid RequestId { get; set; }
        public string RequestNumber { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string? PlantName { get; set; }
        public string NomineeName { get; set; } = string.Empty;
        /// <summary>True when the nominee would be authorized anyway via DepartmentManagers.</summary>
        public bool NomineeIsCompatibleManager { get; set; }
    }

    public async Task<List<ReconciliationRow>> BuildAsync()
    {
        var areaApproverRole = await _context.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleName == RoleConstants.AreaApprover);

        var manualRoleUserIds = areaApproverRole == null
            ? new HashSet<Guid>()
            : (await _context.UserRoleAssignments.AsNoTracking()
                .Where(ura => ura.RoleId == areaApproverRole.Id)
                .Select(ura => ura.UserId)
                .ToListAsync()).ToHashSet();

        var managerRows = await _context.DepartmentManagers.AsNoTracking()
            .Include(dm => dm.Department)
            .Include(dm => dm.Plant)
            .ToListAsync();

        // Universe of the report: anyone touching either side.
        var userIds = manualRoleUserIds
            .Union(managerRows.Select(dm => dm.UserId))
            .ToHashSet();

        var users = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email, u.IsActive })
            .ToListAsync();

        var report = new List<ReconciliationRow>();
        foreach (var user in users.OrderBy(u => u.FullName))
        {
            var rows = managerRows.Where(dm => dm.UserId == user.Id).ToList();
            var activeRows = rows.Where(dm => dm.IsActive).ToList();
            var hasManualRole = manualRoleUserIds.Contains(user.Id);

            var inconsistencies = new List<string>();
            if (string.IsNullOrWhiteSpace(user.Email))
                inconsistencies.Add("Utilizador sem e-mail.");
            foreach (var dm in activeRows)
            {
                if (dm.Department is { IsActive: false })
                    inconsistencies.Add($"Manager de departamento inativo ({dm.Department.Name}).");
                if (dm.Plant is { IsActive: false })
                    inconsistencies.Add($"Manager de planta inativa ({dm.Plant.Name}).");
            }
            // Informational: global + plant-specific rows in the same department (legal, listed for review).
            var dualDepts = activeRows.Where(dm => dm.PlantId == null)
                .Select(dm => dm.DepartmentId)
                .Intersect(activeRows.Where(dm => dm.PlantId != null).Select(dm => dm.DepartmentId))
                .ToList();
            foreach (var deptId in dualDepts)
            {
                var deptName = activeRows.First(dm => dm.DepartmentId == deptId).Department?.Name ?? deptId.ToString();
                inconsistencies.Add($"Global e específico de planta no mesmo departamento ({deptName}).");
            }

            // Priority: INATIVO_COM_VINCULO > INCONSISTENTE > OK/PERDE/SO.
            string classification;
            if (!user.IsActive)
                classification = InativoComVinculo;
            else if (inconsistencies.Count > 0)
                classification = Inconsistente;
            else if (hasManualRole && activeRows.Count > 0)
                classification = OkDerivado;
            else if (hasManualRole)
                classification = PerdeAcesso;
            else
                classification = SoCadastro;

            report.Add(new ReconciliationRow
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                UserIsActive = user.IsActive,
                HasManualAreaApproverRole = hasManualRole,
                ActiveManagerScopes = activeRows
                    .Select(dm => $"{dm.Department?.Name ?? dm.DepartmentId.ToString()} @ {dm.Plant?.Name ?? "Global"}")
                    .ToList(),
                Classification = classification,
                Inconsistencies = inconsistencies
            });
        }

        return report;
    }

    public class ReconciliationRow
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool UserIsActive { get; set; }
        public bool HasManualAreaApproverRole { get; set; }
        public List<string> ActiveManagerScopes { get; set; } = new();
        public string Classification { get; set; } = string.Empty;
        public List<string> Inconsistencies { get; set; } = new();
    }
}
