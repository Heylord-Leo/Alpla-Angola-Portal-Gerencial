using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AlplaPortal.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/integrations/primavera/employees")]
[Authorize(Roles = "System Administrator")]
public class PrimaveraEmployeesController : ControllerBase
{
    private readonly IPrimaveraEmployeeService _employeeService;
    private readonly ILogger<PrimaveraEmployeesController> _logger;

    public PrimaveraEmployeesController(IPrimaveraEmployeeService employeeService, ILogger<PrimaveraEmployeesController> logger)
    {
        _employeeService = employeeService;
        _logger = logger;
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> GetEmployeeByCode(string code, [FromQuery] string? company = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { Error = "Employee code is empty." });
        }

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var employee = await _employeeService.GetEmployeeByCodeAsync(parsedCompany, code);
            if (employee == null)
            {
                return NotFound(new { Error = "Employee not found.", Code = code, Company = parsedCompany.ToString() });
            }

            return Ok(employee);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured") || ex.Message.Contains("not enabled") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on employee lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Integration Provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on employee lookup. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL Exception while fetching employee {Code} from {Company}", code, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching employee {Code} from {Company}", code, parsedCompany);
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchEmployeesByName([FromQuery] string q, [FromQuery] string? company = null, [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return BadRequest(new { Error = "Invalid input: Search query ('q') must be at least 2 characters long." });
        }

        var companyError = ValidateCompanyParam(company, out var parsedCompany);
        if (companyError != null) return BadRequest(new { Error = companyError });

        try
        {
            var employees = await _employeeService.SearchEmployeesByNameAsync(parsedCompany, q, limit);
            return Ok(employees);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured") || ex.Message.Contains("not enabled") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Provider configuration error on employee search. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Integration Provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Credential configuration error on employee search. Company: {Company}", parsedCompany);
            return StatusCode(503, new { Error = "Primavera credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL Exception while searching employees with query {Query} on {Company}", q, parsedCompany);
            return StatusCode(502, new { Error = $"Failed to communicate with Primavera SQL backend for company {parsedCompany}.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error searching employees on {Company}", parsedCompany);
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
