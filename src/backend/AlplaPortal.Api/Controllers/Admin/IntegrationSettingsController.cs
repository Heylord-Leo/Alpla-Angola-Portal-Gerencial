using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing integration provider settings.
/// All endpoints require System Administrator role.
/// Secrets are NEVER returned in response DTOs — only HasPassword/HasApiKey booleans.
/// </summary>
[ApiController]
[Authorize(Roles = "System Administrator")]
[Route("api/admin/integration-settings")]
public class IntegrationSettingsController : ControllerBase
{
    private readonly IIntegrationSettingsService _settingsService;
    private readonly IIntegrationHealthService _healthService;

    public IntegrationSettingsController(
        IIntegrationSettingsService settingsService,
        IIntegrationHealthService healthService)
    {
        _settingsService = settingsService;
        _healthService = healthService;
    }

    /// <summary>List all providers with their masked settings.</summary>
    [HttpGet]
    public async Task<ActionResult<List<IntegrationSettingsDto>>> GetAll(CancellationToken ct)
    {
        var result = await _settingsService.GetAllAsync(ct);
        return Ok(result);
    }

    /// <summary>Get a single provider's masked settings by code.</summary>
    [HttpGet("{code}")]
    public async Task<ActionResult<IntegrationSettingsDto>> GetByCode(string code, CancellationToken ct)
    {
        var result = await _settingsService.GetByCodeAsync(code, ct);
        if (result == null)
            return NotFound(new { message = $"Provider '{code}' not found." });
        return Ok(result);
    }

    /// <summary>Update non-secret settings for a provider.</summary>
    [HttpPut("{code}")]
    public async Task<IActionResult> Update(string code, [FromBody] UpdateIntegrationSettingsDto dto, CancellationToken ct)
    {
        try
        {
            // Note: userId is stubbed until full RBAC is implemented (DEC-065)
            await _settingsService.UpdateSettingsAsync(code, dto, 0, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    /// <summary>Replace an encrypted secret (password or API key).</summary>
    [HttpPost("{code}/secret")]
    public async Task<IActionResult> ReplaceSecret(string code, [FromBody] ReplaceIntegrationSecretDto dto, CancellationToken ct)
    {
        try
        {
            await _settingsService.ReplaceSecretAsync(code, dto, 0, ct);
            return Ok(new { message = "Segredo atualizado com sucesso." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    /// <summary>Test connection using current settings (delegates to health service).</summary>
    [HttpPost("{code}/test")]
    public async Task<IActionResult> TestConnection(string code, [FromQuery] string? companyKey, CancellationToken ct)
    {
        var result = await _healthService.TestProviderConnectionAsync(code, companyKey, ct);
        return Ok(result);
    }

    /// <summary>Update Primavera company-specific settings.</summary>
    [HttpPut("PRIMAVERA/company")]
    public async Task<IActionResult> UpdatePrimaveraCompany([FromBody] UpdatePrimaveraCompanyDto dto, CancellationToken ct)
    {
        try
        {
            await _settingsService.UpdatePrimaveraCompanyAsync(dto, 0, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    /// <summary>Replace Primavera company-specific password.</summary>
    [HttpPost("PRIMAVERA/company/secret")]
    public async Task<IActionResult> ReplacePrimaveraCompanySecret([FromBody] ReplacePrimaveraCompanySecretDto dto, CancellationToken ct)
    {
        try
        {
            await _settingsService.ReplacePrimaveraCompanySecretAsync(dto, 0, ct);
            return Ok(new { message = "Senha da empresa Primavera atualizada com sucesso." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    /// <summary>Enable a provider.</summary>
    [HttpPost("{code}/enable")]
    public async Task<IActionResult> Enable(string code, CancellationToken ct)
    {
        try
        {
            await _settingsService.SetEnabledAsync(code, true, 0, ct);
            return Ok(new { message = $"Provider '{code}' habilitado." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Disable a provider.</summary>
    [HttpPost("{code}/disable")]
    public async Task<IActionResult> Disable(string code, CancellationToken ct)
    {
        try
        {
            await _settingsService.SetEnabledAsync(code, false, 0, ct);
            return Ok(new { message = $"Provider '{code}' desabilitado." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
