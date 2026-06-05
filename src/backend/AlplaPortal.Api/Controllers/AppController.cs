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

    public AppController(IOptions<AppEnvironmentOptions> envOptions)
    {
        _envOptions = envOptions.Value;
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
}
