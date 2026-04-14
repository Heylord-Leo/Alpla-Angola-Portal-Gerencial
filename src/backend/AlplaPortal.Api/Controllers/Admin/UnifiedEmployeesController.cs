using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Unified employee profile — read-only composition of Primavera (master)
/// and Innux (operational complement).
///
/// This endpoint represents the cross-system unified employee profile,
/// not a provider-specific view.
///
/// Phase 3B scope: lookup by Primavera code + company only.
/// Unified search is intentionally deferred.
///
/// Error semantics:
/// - 400: missing/invalid company or employee code
/// - 404: employee not found in Primavera
/// - 502: Primavera SQL connectivity failure
/// - 503: Primavera provider not configured / disabled
/// - 200 with HasInnuxMatch=false: Innux no-match or Innux error (Primavera still returned)
/// - 500: unexpected error
/// </summary>
[ApiController]
[Route("api/admin/integrations/employees")]
[Authorize(Roles = "System Administrator")]
public class UnifiedEmployeesController : ControllerBase
{
    private readonly IUnifiedEmployeeProfileService _profileService;
    private readonly ILogger<UnifiedEmployeesController> _logger;

    public UnifiedEmployeesController(
        IUnifiedEmployeeProfileService profileService,
        ILogger<UnifiedEmployeesController> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a unified employee profile by Primavera employee code and company.
    /// Innux enrichment is attempted automatically using the Primavera code as match key.
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetUnifiedProfile(string code, [FromQuery] string? company = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { Error = "Employee code is empty." });
        }

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var profile = await _profileService.GetUnifiedProfileAsync(parsedCompany, code);
            if (profile == null)
            {
                return NotFound(new { Error = "Employee not found.", Code = code, Company = parsedCompany.ToString() });
            }

            return Ok(profile);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured") || ex.Message.Contains("not enabled") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on unified profile lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Integration Provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on unified profile lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL Exception while fetching unified profile {Code} from {Company}", code, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching unified profile {Code} from {Company}", code, parsedCompany);
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
    }

    /// <summary>
    /// Validates and parses the company query parameter.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    private static string? ValidateCompanyParam(string? value, out PrimaveraCompany company)
    {
        company = default;
        var validValues = string.Join(", ", Enum.GetNames<PrimaveraCompany>());

        if (string.IsNullOrWhiteSpace(value))
            return $"Company parameter is required. Valid values: {validValues}";

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out company))
            return $"Invalid company '{value}'. Valid values: {validValues}";

        return null;
    }
}
