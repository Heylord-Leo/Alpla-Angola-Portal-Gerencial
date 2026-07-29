using AlplaPortal.Api.Services;
using AlplaPortal.Application.Models.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Lightweight application metadata endpoint (DEC-140).
/// Returns environment configuration for frontend visual differentiation.
/// </summary>
[ApiController]
[Route("api/app")]
public class AppController : ControllerBase
{
    private readonly AppEnvironmentOptions _envOptions;
    private readonly IBuildInfoProvider _buildInfo;

    public AppController(IOptions<AppEnvironmentOptions> envOptions, IBuildInfoProvider buildInfo)
    {
        _envOptions = envOptions.Value;
        _buildInfo = buildInfo;
    }

    /// <summary>
    /// Returns the current application environment configuration.
    /// Used by the frontend to display environment indicators (banner, badge, title prefix).
    /// Anonymous because it must be accessible on the login page before authentication.
    /// No sensitive data is exposed.
    /// </summary>
    [HttpGet("environment")]
    [AllowAnonymous]
    public IActionResult GetEnvironment()
    {
        return Ok(new
        {
            code = _envOptions.Code,
            name = _envOptions.Name,
            showBanner = _envOptions.ShowBanner
        });
    }

    /// <summary>
    /// Returns the running backend's canonical build identity so the frontend can detect a newer
    /// deployment (version-mismatch protection). Anonymous (the login page must reach it before auth),
    /// database-independent, and DETERMINISTIC — it only echoes metadata loaded once at startup.
    /// Only non-sensitive fields are exposed here; audit fields (gitSha, deployment/run IDs) are on the
    /// authenticated diagnostics endpoint. Environment comes from AppEnvironment config (single source),
    /// not the manifest.
    /// </summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult GetVersion()
    {
        var b = _buildInfo.Current;
        return Ok(new
        {
            version = b.Version,
            buildId = b.BuildId,
            shortSha = b.ShortSha,
            environment = _envOptions.Code,
            builtAtUtc = b.BuiltAtUtc,
            buildMetadataStatus = b.Status.ToString()
        });
    }
}
