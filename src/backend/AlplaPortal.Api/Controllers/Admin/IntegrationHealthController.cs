using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Admin controller for integration provider health and diagnostics.
///
/// This controller is provider-agnostic — it delegates to IIntegrationHealthService
/// which iterates over all registered providers without knowledge of specific
/// business domains (employees, materials, etc.).
///
/// Note: This is intentionally separate from AdminDiagnosticsController, which
/// handles internal service health (Backend, Database, OCR, OpenAI).
/// IntegrationHealthController handles external system connectivity
/// (Primavera, Innux, future providers).
/// </summary>
[ApiController]
[Route("api/admin/integrations")]
public class IntegrationHealthController : ControllerBase
{
    private readonly IIntegrationHealthService _healthService;

    public IntegrationHealthController(IIntegrationHealthService healthService)
    {
        _healthService = healthService;
    }

    /// <summary>
    /// Returns health status for all registered integration providers.
    /// Includes provider metadata, connection status, and capability indicators.
    /// Does NOT expose credentials or sensitive connection details.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var summary = await _healthService.GetHealthSummaryAsync(ct);
        return Ok(summary);
    }

    /// <summary>
    /// Triggers a manual connection test for a specific provider.
    /// Only succeeds if the provider is enabled, configured, and has a registered implementation.
    /// Planned/disabled/unconfigured providers return descriptive error messages.
    /// </summary>
    [HttpPost("{providerCode}/test-connection")]
    public async Task<IActionResult> TestConnection(string providerCode, CancellationToken ct)
    {
        var result = await _healthService.TestProviderConnectionAsync(providerCode, ct);
        return Ok(result);
    }
}
