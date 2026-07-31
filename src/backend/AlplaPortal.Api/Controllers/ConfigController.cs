using AlplaPortal.Application.DTOs.Common;
using AlplaPortal.Domain.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Runtime feature configuration for the frontend.
///
/// <para>The Post-Payment Completion workflow is switched on through configuration, not through an
/// Administration screen. The frontend asks this endpoint whether the classification field should
/// be rendered at all, so a disabled environment shows exactly the UI it showed before the feature
/// existed.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/config")]
public class ConfigController : ControllerBase
{
    private readonly PostPaymentCompletionOptions _postPaymentOptions;

    public ConfigController(IOptions<PostPaymentCompletionOptions> postPaymentOptions)
    {
        _postPaymentOptions = postPaymentOptions.Value;
    }

    /// <summary>
    /// Feature flags the UI needs in order to decide what to render.
    /// Returns booleans only — no configuration secrets and no connection details.
    /// </summary>
    [HttpGet("features")]
    public ActionResult<FeatureFlagsDto> GetFeatures()
    {
        var enabled = !PostPaymentCompletionPolicy.IsFeatureDisabled(_postPaymentOptions);

        // A request created right now carries CreatedAtUtc = now, so the creation-time
        // classification rule is simply "are we at or past the effective date".
        var classificationRequired = enabled && DateTime.UtcNow >= _postPaymentOptions.EffectiveDateUtc;

        return Ok(new FeatureFlagsDto
        {
            PostPaymentCompletionEnabled = enabled,
            SourceDocumentTypeRequired = classificationRequired
        });
    }
}
