using AlplaPortal.Application.DTOs.Requests;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// SINGLE source of truth for area-approval responsibility, backed by the
/// DepartmentManagers table (see docs/department-manager-routing-redesign-plan.md).
/// Phase B (definitive cut): submit, queue, authorization and e-mails all route
/// through this service. Department.ResponsibleUserId and the manually assigned
/// "Area Approver" role grant nothing here.
///
/// D1 semantics (confirmed 2026-07-15):
/// - <see cref="ResolveAreaManagersAsync"/> — e-mail/display resolution — uses the STRICT
///   cascade and stops at the first non-empty level: plant-specific managers → global
///   managers (PlantId NULL) → empty (no legacy fallback).
/// - <see cref="IsAreaManagerAsync"/> — authorization/queue — is INCLUSIVE: any active
///   row for the department whose PlantId matches the request's plant OR is NULL
///   qualifies. A manager of a different plant never qualifies.
/// </summary>
public interface IApprovalRoutingService
{
    /// <summary>
    /// Resolves the managers who should be NOTIFIED for a request of the given
    /// department/plant, using the strict cascade. Filters out inactive manager rows,
    /// inactive users and users without e-mail.
    /// </summary>
    Task<ApprovalRoutingResultDto> ResolveAreaManagersAsync(int departmentId, int? plantId);

    /// <summary>
    /// Whether the user is AUTHORIZED to act as area manager for the department/plant
    /// (plant-specific or global row; legacy responsible also qualifies while the
    /// fallback level is active).
    /// </summary>
    Task<bool> IsAreaManagerAsync(Guid userId, int departmentId, int? plantId);

    /// <summary>
    /// All (department, plant) scopes the user actively manages. PlantId NULL means
    /// every plant of that department. Used by the Phase B queue query.
    /// </summary>
    Task<List<ManagedScopeDto>> GetManagedScopesAsync(Guid userId);
}
