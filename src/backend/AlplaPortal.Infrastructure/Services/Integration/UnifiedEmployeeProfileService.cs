using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Unified employee profile — read-only composition layer.
///
/// Sits above both domain services (IPrimaveraEmployeeService, IInnuxEmployeeService).
/// Never touches SQL directly. Maps existing DTOs into profile sections.
///
/// Source hierarchy:
/// - Primavera is the authoritative master source
/// - Innux is an optional operational complement
///
/// Match strategy:
/// - Primavera.Codigo → Innux.Numero (deterministic, code-based)
///
/// Innux failure policy:
/// - Innux failure does NOT fail the unified profile
/// - Primavera profile is always returned if Primavera succeeds
/// - Innux status is explicit via InnuxLookupStatus
///
/// Scope:
/// - Phase 3B: lookup by code only
/// - Unified search intentionally deferred
/// </summary>
public class UnifiedEmployeeProfileService : IUnifiedEmployeeProfileService
{
    private readonly IPrimaveraEmployeeService _primaveraService;
    private readonly IInnuxEmployeeService _innuxService;
    private readonly ILogger<UnifiedEmployeeProfileService> _logger;

    public UnifiedEmployeeProfileService(
        IPrimaveraEmployeeService primaveraService,
        IInnuxEmployeeService innuxService,
        ILogger<UnifiedEmployeeProfileService> logger)
    {
        _primaveraService = primaveraService;
        _innuxService = innuxService;
        _logger = logger;
    }

    public async Task<UnifiedEmployeeProfileDto?> GetUnifiedProfileAsync(
        PrimaveraCompany company, string employeeCode)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            return null;

        var code = employeeCode.Trim();

        _logger.LogInformation(
            "Unified profile lookup started. Code: {Code}, Company: {Company}",
            code, company);

        // ─── Step 1: Primavera lookup (master source) ───

        PrimaveraEmployeeDto? primaveraEmployee;

        try
        {
            primaveraEmployee = await _primaveraService.GetEmployeeByCodeAsync(company, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Primavera lookup failed for unified profile. Code: {Code}, Company: {Company}",
                code, company);
            throw; // Primavera failure propagates — controller handles HTTP semantics
        }

        if (primaveraEmployee == null)
        {
            _logger.LogInformation(
                "Primavera employee not found. Code: {Code}, Company: {Company}",
                code, company);
            return null; // Caller produces 404
        }

        _logger.LogInformation(
            "Primavera employee found. Code: {Code}, Name: {Name}, Company: {Company}",
            code, primaveraEmployee.Name, company);

        // ─── Step 2: Innux lookup (optional complement) ───

        var profile = new UnifiedEmployeeProfileDto
        {
            EmployeeCode = code,
            Company = company.ToString(),
            Primavera = MapPrimaveraSection(primaveraEmployee),
            Innux = null,
            HasInnuxMatch = false,
            InnuxLookupStatus = "NOT_FOUND",
            InnuxLookupMessage = null
        };

        try
        {
            var innuxEmployee = await _innuxService.GetEmployeeByNumberAsync(code);

            if (innuxEmployee != null)
            {
                profile.Innux = MapInnuxSection(innuxEmployee);
                profile.HasInnuxMatch = true;
                profile.InnuxLookupStatus = "MATCHED";

                _logger.LogInformation(
                    "Innux match found. Code: {Code}, InnuxId: {InnuxId}, InnuxNumber: {Number}",
                    code, innuxEmployee.InnuxEmployeeId, innuxEmployee.EmployeeNumber);
            }
            else
            {
                _logger.LogInformation(
                    "Innux match not found for employee. Code: {Code}", code);
            }
        }
        catch (InvalidOperationException ex)
        {
            // Innux configuration/provider error — degrade gracefully
            profile.InnuxLookupStatus = "ERROR";
            profile.InnuxLookupMessage = "Innux provider is not available.";

            _logger.LogWarning(ex,
                "Innux lookup failed (configuration). Code: {Code}. Returning Primavera-only profile.",
                code);
        }
        catch (Exception ex)
        {
            // Innux runtime error (SQL, network, etc.) — degrade gracefully
            profile.InnuxLookupStatus = "ERROR";
            profile.InnuxLookupMessage = "Innux lookup encountered a temporary error.";

            _logger.LogWarning(ex,
                "Innux lookup failed (runtime). Code: {Code}. Returning Primavera-only profile.",
                code);
        }

        return profile;
    }

    // ─── Mapping helpers ───

    private static PrimaveraProfileSection MapPrimaveraSection(PrimaveraEmployeeDto dto)
    {
        return new PrimaveraProfileSection
        {
            Code = dto.Code,
            Name = dto.Name,
            ShortName = dto.ShortName,
            FirstName = dto.FirstName,
            FirstLastName = dto.FirstLastName,
            SecondLastName = dto.SecondLastName,
            Email = dto.Email,
            Phone = dto.Phone,
            Mobile = dto.Mobile,
            DepartmentCode = dto.DepartmentCode,
            DepartmentName = dto.DepartmentName,
            HireDate = dto.HireDate,
            TerminationDate = dto.TerminationDate,
            IsTemporarilyInactive = dto.IsTemporarilyInactive,
            SourceCompany = dto.SourceCompany
        };
    }

    private static InnuxProfileSection MapInnuxSection(InnuxEmployeeDto dto)
    {
        return new InnuxProfileSection
        {
            InnuxEmployeeId = dto.InnuxEmployeeId,
            EmployeeNumber = dto.EmployeeNumber,
            CardNumber = dto.CardNumber,
            IsActiveOperational = dto.IsActiveOperational,
            DepartmentId = dto.DepartmentId,
            DepartmentName = dto.DepartmentName,
            LoginAd = dto.LoginAd,
            Category = dto.Category,
            CostCenter = dto.CostCenter,
            HasPhoto = dto.HasPhoto,
            Source = dto.Source
        };
    }
}
