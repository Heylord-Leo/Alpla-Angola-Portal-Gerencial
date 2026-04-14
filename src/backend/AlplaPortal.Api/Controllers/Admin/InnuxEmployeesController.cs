using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Admin-internal API for Innux employee operational identity lookup.
///
/// Read-only, single database (no company parameter needed).
/// Error handling semantics aligned with PrimaveraEmployeesController:
/// - 400: invalid/too-short input
/// - 404: employee not found
/// - 502: SQL connectivity failure
/// - 503: provider not configured / disabled / credentials missing
/// - 500: unexpected error
/// </summary>
[ApiController]
[Route("api/admin/integrations/innux/employees")]
[Authorize(Roles = "System Administrator")]
public class InnuxEmployeesController : ControllerBase
{
    private readonly IInnuxEmployeeService _employeeService;
    private readonly ILogger<InnuxEmployeesController> _logger;

    public InnuxEmployeesController(IInnuxEmployeeService employeeService, ILogger<InnuxEmployeesController> logger)
    {
        _employeeService = employeeService;
        _logger = logger;
    }

    [HttpGet("{number}")]
    public async Task<IActionResult> GetEmployeeByNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return BadRequest(new { Error = "Employee number is empty." });
        }

        try
        {
            var employee = await _employeeService.GetEmployeeByNumberAsync(number);
            if (employee == null)
            {
                return NotFound(new { Error = "Employee not found.", Number = number });
            }

            return Ok(employee);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured") || ex.Message.Contains("not enabled") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Innux provider configuration error on employee lookup.");
            return StatusCode(503, new { Error = "Innux Integration Provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Innux credential configuration error on employee lookup.");
            return StatusCode(503, new { Error = "Innux credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL Exception while fetching Innux employee {Number}", number);
            return StatusCode(502, new { Error = "Failed to communicate with Innux SQL backend.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching Innux employee {Number}", number);
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchEmployeesByName([FromQuery] string q, [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return BadRequest(new { Error = "Invalid input: Search query ('q') must be at least 2 characters long." });
        }

        try
        {
            var employees = await _employeeService.SearchEmployeesByNameAsync(q, limit);
            return Ok(employees);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured") || ex.Message.Contains("not enabled") || ex.Message.Contains("disabled"))
        {
            _logger.LogWarning(ex, "Innux provider configuration error on employee search.");
            return StatusCode(503, new { Error = "Innux Integration Provider misconfigured or disabled.", Details = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("credentials"))
        {
            _logger.LogWarning(ex, "Innux credential configuration error on employee search.");
            return StatusCode(503, new { Error = "Innux credentials not configured.", Details = ex.Message });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL Exception while searching Innux employees with query {Query}", q);
            return StatusCode(502, new { Error = "Failed to communicate with Innux SQL backend.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error searching Innux employees");
            return StatusCode(500, new { Error = "Internal server error occurred." });
        }
    }
}
