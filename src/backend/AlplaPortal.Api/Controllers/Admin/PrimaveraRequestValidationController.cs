using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Primavera request validation — validates Portal request inputs against
/// Primavera master data using the composition/validation layer.
///
/// This is an internal/admin endpoint for testing and future Portal integration.
/// All operations are read-only.
///
/// Phase 5A scope: line-level and batch validation.
///
/// Error http semantics:
///   - 200: validation completed (result may be VALID, WARNING, INVALID, or ERROR)
///   - 400: malformed request payload
///   - 500: unexpected server error
///
/// Business validation outcomes (VALID/WARNING/INVALID) are returned as 200
/// because the validation operation itself succeeded. The ValidationStatus
/// field in the response communicates the business-level result.
///
/// Technical failures during validation are surfaced as ERROR status in the
/// response (still 200), unless the failure is at the HTTP/controller layer.
/// </summary>
[ApiController]
[Route("api/admin/integrations/primavera/request-validation")]
[Authorize(Roles = "System Administrator")]
public class PrimaveraRequestValidationController : ControllerBase
{
    private readonly IPrimaveraRequestValidationService _validationService;
    private readonly ILogger<PrimaveraRequestValidationController> _logger;

    public PrimaveraRequestValidationController(
        IPrimaveraRequestValidationService validationService,
        ILogger<PrimaveraRequestValidationController> logger)
    {
        _validationService = validationService;
        _logger = logger;
    }

    /// <summary>
    /// Validates a single request line against Primavera master data.
    ///
    /// Checks article existence, supplier existence (if provided),
    /// and article-supplier relationship (if both exist).
    ///
    /// Always returns 200 with a validation result. The ValidationStatus field
    /// indicates whether the input is VALID, WARNING, INVALID, or ERROR.
    /// </summary>
    [HttpPost("validate-line")]
    public async Task<IActionResult> ValidateLine([FromBody] PrimaveraRequestValidationInputDto? input)
    {
        if (input == null)
        {
            return BadRequest(new { Error = "Request body is required." });
        }

        try
        {
            var result = await _validationService.ValidateRequestLineAsync(input);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in request validation controller.");
            return StatusCode(500, new { Error = "Internal server error during validation." });
        }
    }

    /// <summary>
    /// Validates multiple request lines in a single call.
    /// Each line is validated independently against the same rules.
    ///
    /// Always returns 200 with a list of validation results.
    /// </summary>
    [HttpPost("validate-batch")]
    public async Task<IActionResult> ValidateBatch([FromBody] List<PrimaveraRequestValidationInputDto>? inputs)
    {
        if (inputs == null || inputs.Count == 0)
        {
            return BadRequest(new { Error = "Request body must contain at least one validation input." });
        }

        if (inputs.Count > 50)
        {
            return BadRequest(new { Error = "Batch validation is limited to 50 lines per request." });
        }

        try
        {
            var results = await _validationService.ValidateRequestLinesAsync(inputs);
            return Ok(new
            {
                TotalLines = results.Count,
                Valid = results.Count(r => r.ValidationStatus == "VALID"),
                Warning = results.Count(r => r.ValidationStatus == "WARNING"),
                Invalid = results.Count(r => r.ValidationStatus == "INVALID"),
                Error = results.Count(r => r.ValidationStatus == "ERROR"),
                Results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in batch validation controller.");
            return StatusCode(500, new { Error = "Internal server error during batch validation." });
        }
    }
}
