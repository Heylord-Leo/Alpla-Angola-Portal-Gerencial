using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces.Extraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "System Administrator")]
[Route("api/admin/document-extraction-settings")]
public class DocumentExtractionSettingsController : ControllerBase
{
    private readonly IDocumentExtractionSettingsService _settingsService;

    public DocumentExtractionSettingsController(IDocumentExtractionSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    public async Task<ActionResult<DocumentExtractionSettingsDto>> Get(CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] DocumentExtractionSettingsDto dto, CancellationToken ct)
    {
        try
        {
            await _settingsService.UpdateSettingsAsync(dto, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<ConnectionTestResultDto>> TestConnection(CancellationToken ct)
    {
        var result = await _settingsService.TestConnectionAsync(ct);
        return Ok(result);
    }
}
