using AlplaPortal.Application.DTOs;
using AlplaPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "System Administrator")]
[Route("api/admin/smtp-settings")]
public class SmtpSettingsController : ControllerBase
{
    private readonly ISmtpSettingsService _smtpSettingsService;

    public SmtpSettingsController(ISmtpSettingsService smtpSettingsService)
    {
        _smtpSettingsService = smtpSettingsService;
    }

    [HttpGet]
    public async Task<ActionResult<SmtpSettingsDto>> Get(CancellationToken ct)
    {
        var settings = await _smtpSettingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SmtpSettingsDto dto, CancellationToken ct)
    {
        try
        {
            await _smtpSettingsService.UpdateSettingsAsync(dto, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult> TestConnection(CancellationToken ct)
    {
        var result = await _smtpSettingsService.TestConnectionAsync(ct);
        return Ok(result);
    }
}
