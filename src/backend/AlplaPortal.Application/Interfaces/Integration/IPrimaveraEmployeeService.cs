using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Primavera employee lookup — read-only, company-aware.
/// Every call requires an explicit PrimaveraCompany target.
/// </summary>
public interface IPrimaveraEmployeeService
{
    Task<PrimaveraEmployeeDto?> GetEmployeeByCodeAsync(PrimaveraCompany company, string code);
    Task<IEnumerable<PrimaveraEmployeeDto>> SearchEmployeesByNameAsync(PrimaveraCompany company, string nameQuery, int limit = 50);
}
