using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Primavera article-supplier relationship lookup — read-only, company-aware.
///
/// This is a Primavera-specific admin/internal endpoint for discovering
/// article-supplier contextual linkages. Not yet connected to Portal
/// procurement, sourcing, or sync flows.
///
/// Phase 4C scope: article → suppliers + supplier → articles.
///
/// Error semantics consistent with existing Primavera endpoints:
/// - 400: missing/invalid company, empty code
/// - 404: article or supplier not found in target company
/// - 502: SQL connectivity failure
/// - 503: provider misconfigured/disabled
/// - 500: unexpected error
/// </summary>
[ApiController]
[Route("api/admin/integrations/primavera")]
[Authorize(Roles = "System Administrator")]
public class PrimaveraArticleSupplierController : ControllerBase
{
    private readonly IPrimaveraArticleSupplierService _articleSupplierService;
    private readonly ILogger<PrimaveraArticleSupplierController> _logger;

    public PrimaveraArticleSupplierController(
        IPrimaveraArticleSupplierService articleSupplierService,
        ILogger<PrimaveraArticleSupplierController> logger)
    {
        _articleSupplierService = articleSupplierService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all suppliers linked to a given article code.
    /// Returns the article identity with its linked suppliers enriched from Fornecedores.
    /// </summary>
    [HttpGet("articles/{code}/suppliers")]
    public async Task<IActionResult> GetSuppliersForArticle(string code, [FromQuery] string? company = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { Error = "Article code is empty." });
        }

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var result = await _articleSupplierService.GetSuppliersForArticleAsync(parsedCompany, code);
            if (result == null)
            {
                return NotFound(new { Error = "Article not found.", Code = code, Company = parsedCompany.ToString() });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not enabled") || ex.Message.Contains("not configured") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on article-supplier lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera integration provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on article-supplier lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error fetching suppliers for article {Code} from {Company}", code, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching suppliers for article {Code} from {Company}", code, parsedCompany);
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
    }

    /// <summary>
    /// Gets all articles linked to a given supplier code.
    /// Returns the supplier identity with its linked articles enriched from Artigo.
    /// </summary>
    [HttpGet("suppliers/{code}/articles")]
    public async Task<IActionResult> GetArticlesForSupplier(string code, [FromQuery] string? company = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { Error = "Supplier code is empty." });
        }

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var result = await _articleSupplierService.GetArticlesForSupplierAsync(parsedCompany, code);
            if (result == null)
            {
                return NotFound(new { Error = "Supplier not found.", Code = code, Company = parsedCompany.ToString() });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not enabled") || ex.Message.Contains("not configured") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on supplier-article lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera integration provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on supplier-article lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error fetching articles for supplier {Code} from {Company}", code, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching articles for supplier {Code} from {Company}", code, parsedCompany);
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
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
}
