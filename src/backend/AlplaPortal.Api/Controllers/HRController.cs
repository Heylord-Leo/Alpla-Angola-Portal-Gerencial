using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// HR Employee operations — read-only consultation and badge preparation.
///
/// This controller exposes employee data for the HR workspace.
/// It is intentionally separated from the Admin/UnifiedEmployeesController
/// because it serves a different audience (HR operations vs. admin diagnostics)
/// and will evolve independently.
///
/// V1 scope:
/// - Employee search (by name, code, or card number)
/// - Unified employee profile (Primavera master + Innux complement)
/// - Employee photo retrieval (from Innux)
///
/// Access: HR role + System Administrator.
/// Local Manager no longer has implicit access — they must be assigned the HR role explicitly.
///
/// Note on future evolution:
/// This controller secures the V1 HR Employee Workspace (Cadastro de Funcionários).
/// Future HR submenus (Vacation, Time Attendance, Badge Layout Management) will require
/// an additional evolution of the authorization model to support per-submenu feature permissions.
///
/// Important:
/// - This controller is strictly READ-ONLY
/// - No Primavera write/update operations
/// - Photo upload is handled client-side for badge preparation only
/// </summary>
[ApiController]
[Route("api/hr/employees")]
[Authorize(Roles = "System Administrator,HR")]
public class HRController : ControllerBase
{
    private readonly IPrimaveraEmployeeService _primaveraService;
    private readonly IInnuxEmployeeService _innuxService;
    private readonly IUnifiedEmployeeProfileService _profileService;
    private readonly IInnuxEmployeePhotoService _photoService;
    private readonly ILogger<HRController> _logger;

    public HRController(
        IPrimaveraEmployeeService primaveraService,
        IInnuxEmployeeService innuxService,
        IUnifiedEmployeeProfileService profileService,
        IInnuxEmployeePhotoService photoService,
        ILogger<HRController> logger)
    {
        _primaveraService = primaveraService;
        _innuxService = innuxService;
        _profileService = profileService;
        _photoService = photoService;
        _logger = logger;
    }

    /// <summary>
    /// Searches employees by name, code, or card number.
    /// Combines Primavera (master) and Innux (complement) results.
    ///
    /// Search strategy:
    /// - If query looks numeric or short alphanumeric → try exact code match first
    /// - Always perform name search in Primavera
    /// - Enrich results with Innux card numbers when available
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchEmployees(
        [FromQuery] string? query = null,
        [FromQuery] string? company = null,
        [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return BadRequest(new { Error = "Search query must be at least 2 characters." });

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        if (limit <= 0 || limit > 50) limit = 20;

        var trimmedQuery = query.Trim();

        try
        {
            var results = new List<EmployeeSearchResultDto>();

            // Strategy 1: Try exact code match first
            var exactMatch = await _primaveraService.GetEmployeeByCodeAsync(parsedCompany, trimmedQuery);
            if (exactMatch != null)
            {
                var dto = MapToSearchResult(exactMatch);
                // Enrich with Innux data if possible
                await EnrichWithInnuxAsync(dto, exactMatch.Code);
                results.Add(dto);
            }

            // Strategy 2: Name search (skip if exact match already found and query is short)
            if (trimmedQuery.Length >= 2)
            {
                try
                {
                    var nameResults = await _primaveraService.SearchEmployeesByNameAsync(
                        parsedCompany, trimmedQuery, limit);

                    foreach (var emp in nameResults)
                    {
                        // Avoid duplicating exact match
                        if (results.Any(r => r.Code == emp.Code))
                            continue;

                        var dto = MapToSearchResult(emp);
                        await EnrichWithInnuxAsync(dto, emp.Code);
                        results.Add(dto);

                        if (results.Count >= limit)
                            break;
                    }
                }
                catch (ArgumentException)
                {
                    // Query too short for name search — ignore
                }
            }

            return Ok(new { results, total = results.Count });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not configured") ||
            ex.Message.Contains("not enabled") ||
            ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error during HR employee search.");
            return StatusCode(503, new { Error = "Integration provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error during HR employee search. Company: {Company}", parsedCompany);
            return StatusCode(502, new { Error = "Failed to communicate with database.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during HR employee search.");
            return StatusCode(500, new { Error = "Internal server error." });
        }
    }

    /// <summary>
    /// Retrieves a full unified employee profile by Primavera code.
    /// Delegates to the existing UnifiedEmployeeProfileService.
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetEmployeeProfile(string code, [FromQuery] string? company = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { Error = "Employee code is required." });

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var profile = await _profileService.GetUnifiedProfileAsync(parsedCompany, code);
            if (profile == null)
                return NotFound(new { Error = "Employee not found.", Code = code, Company = parsedCompany.ToString() });

            return Ok(profile);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not configured") ||
            ex.Message.Contains("not enabled") ||
            ex.Message.Contains("disabled") ||
            ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Provider error on HR profile lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Integration provider unavailable.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error fetching HR profile {Code} from {Company}", code, parsedCompany);
            return StatusCode(502, new { Error = "Database communication error.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching HR profile {Code} from {Company}", code, parsedCompany);
            return StatusCode(500, new { Error = "Internal server error." });
        }
    }

    /// <summary>
    /// Retrieves the employee photo binary from Innux.
    /// Returns the raw image bytes with appropriate content-type.
    ///
    /// Note: This endpoint is not company-scoped because Innux is a single database.
    /// The employee number is used as the lookup key (same as Primavera Codigo).
    /// </summary>
    [HttpGet("{code}/photo")]
    public async Task<IActionResult> GetEmployeePhoto(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { Error = "Employee code is required." });

        try
        {
            var photoBytes = await _photoService.GetEmployeePhotoAsync(code);
            if (photoBytes == null)
                return NotFound(new { Error = "No photo available for this employee.", Code = code });

            // Innux stores photos as JPEG typically, but detect if possible
            var contentType = DetectImageContentType(photoBytes);
            return File(photoBytes, contentType);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Innux configuration error fetching photo for {Code}", code);
            return StatusCode(503, new { Error = "Photo service unavailable.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching photo for employee {Code}", code);
            return StatusCode(500, new { Error = "Internal server error." });
        }
    }

    // ─── Helpers ───

    private async Task EnrichWithInnuxAsync(EmployeeSearchResultDto dto, string code)
    {
        try
        {
            var innux = await _innuxService.GetEmployeeByNumberAsync(code);
            if (innux != null)
            {
                dto.CardNumber = innux.CardNumber;
                dto.Category = innux.Category;
                dto.HasPhoto = innux.HasPhoto;
                dto.IsActiveOperational = innux.IsActiveOperational;
            }
        }
        catch (Exception ex)
        {
            // Innux enrichment failure never blocks search results
            _logger.LogWarning(ex, "Innux enrichment failed for employee {Code}. Continuing without.", code);
        }
    }

    private static EmployeeSearchResultDto MapToSearchResult(Application.DTOs.Integration.PrimaveraEmployeeDto emp)
    {
        return new EmployeeSearchResultDto
        {
            Code = emp.Code,
            Name = emp.Name,
            FirstName = emp.FirstName,
            LastName = emp.FirstLastName,
            DepartmentName = emp.DepartmentName,
            Company = emp.SourceCompany,
            IsTemporarilyInactive = emp.IsTemporarilyInactive
        };
    }

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

    /// <summary>
    /// Detects image content type from the first bytes (magic number detection).
    /// Falls back to JPEG if unknown.
    /// </summary>
    private static string DetectImageContentType(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";
        if (data.Length >= 4 && data[0] == 0x42 && data[1] == 0x4D)
            return "image/bmp";

        // Default fallback
        return "image/jpeg";
    }
}

/// <summary>
/// Lightweight search result DTO for the HR employee search.
/// Contains only the fields needed for the search results table.
/// Data-minimized by design — no sensitive fields.
/// </summary>
public class EmployeeSearchResultDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DepartmentName { get; set; }
    public string? Company { get; set; }
    public string? CardNumber { get; set; }
    public string? Category { get; set; }
    public bool HasPhoto { get; set; }
    public bool? IsActiveOperational { get; set; }
    public bool? IsTemporarilyInactive { get; set; }
}
