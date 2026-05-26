using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Primavera article/material master-data lookup — read-only, company-aware.
///
/// This is a Primavera-specific admin/internal endpoint for article/item master lookup.
/// Not yet connected to Portal catalog, OCR, or sync flows.
///
/// Phase 4A scope: lookup by code + search by query.
///
/// Error semantics consistent with existing Primavera employee endpoints:
/// - 400: missing/invalid company, empty/short query
/// - 404: article not found
/// - 502: SQL connectivity failure
/// - 503: provider misconfigured/disabled
/// - 500: unexpected error
/// </summary>
[ApiController]
[Route("api/admin/integrations/primavera/articles")]
[Authorize(Roles = "System Administrator")]
public class PrimaveraArticlesController : ControllerBase
{
    private readonly IPrimaveraArticleService _articleService;
    private readonly ILogger<PrimaveraArticlesController> _logger;

    public PrimaveraArticlesController(
        IPrimaveraArticleService articleService,
        ILogger<PrimaveraArticlesController> logger)
    {
        _articleService = articleService;
        _logger = logger;
    }

    /// <summary>
    /// Looks up a single Primavera article by code in the specified company.
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetArticleByCode(string code, [FromQuery] string? company = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { Error = "Article code is empty." });
        }

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var article = await _articleService.GetArticleByCodeAsync(parsedCompany, code);
            if (article == null)
            {
                return NotFound(new { Error = "Article not found.", Code = code, Company = parsedCompany.ToString() });
            }

            return Ok(article);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not enabled") || ex.Message.Contains("not configured") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on article lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera integration provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on article lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error fetching article {Code} from {Company}", code, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching article {Code} from {Company}", code, parsedCompany);
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
    }

    /// <summary>
    /// Searches Primavera articles by code or description fragment.
    /// Results bounded to max 50, minimum query length 2.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchArticles(
        [FromQuery] string? company = null,
        [FromQuery] string? q = null,
        [FromQuery] int limit = 50)
    {
        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return BadRequest(new { Error = "Search query 'q' is required and must be at least 2 characters." });
        }

        try
        {
            var results = await _articleService.SearchArticlesAsync(parsedCompany, q, limit);
            return Ok(results);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not enabled") || ex.Message.Contains("not configured") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on article search. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera integration provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on article search. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error during article search. Query: {Query}, Company: {Company}", q, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during article search. Query: {Query}, Company: {Company}", q, parsedCompany);
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
