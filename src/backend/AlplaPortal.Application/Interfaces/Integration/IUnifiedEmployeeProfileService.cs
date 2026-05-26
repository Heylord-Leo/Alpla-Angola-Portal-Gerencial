using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Unified employee profile — read-only composition of Primavera (master)
/// and Innux (operational complement).
///
/// This is an application/domain composition layer above both domain services.
/// It does not access SQL directly.
///
/// Phase 3B scope: lookup by Primavera code + company only.
/// Unified search is intentionally deferred.
/// </summary>
public interface IUnifiedEmployeeProfileService
{
    /// <summary>
    /// Fetches a unified employee profile by Primavera employee code and company.
    ///
    /// Flow:
    /// 1. Lookup Primavera employee by code in the specified company
    /// 2. If not found → return null (caller produces 404)
    /// 3. Attempt Innux lookup using Primavera.Code as Innux.Numero
    /// 4. Return unified profile with match diagnostics
    ///
    /// Innux failure does not fail the request — Primavera profile is always returned
    /// if Primavera succeeds.
    /// </summary>
    Task<UnifiedEmployeeProfileDto?> GetUnifiedProfileAsync(
        PrimaveraCompany company, string employeeCode);
}
