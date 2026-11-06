using System.Security.Claims;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Machine-card design of the Viana 3 Production Monitor: template + per-machine designs,
/// read-only data-source catalogue and uploaded raster images.
///
/// Anonymous (read-only TV): the design bundle and the asset bytes. Everything else —
/// catalogue, designs, uploads, deletions, cleanup — is restricted to OPERATIONS and
/// System Administrator. Designs are display preferences only; no production data, no
/// AlplaPROD change, no SQL editing, no connection details.
///
///   GET    card-design                       bundle (template + overrides) — anonymous
///   GET    card-design/assets/{id}           image bytes — anonymous
///   GET    card-design/catalog               item catalogue with verified source metadata
///   GET    card-design/template | /{machineId}
///   PUT    card-design/template | /{machineId}   save (409 DESIGN_MODIFIED / ASSET_UNAVAILABLE / BOARD_BUSY)
///   DELETE card-design/template | /{machineId}   reset
///   GET    card-design/assets                list with references and quota
///   POST   card-design/assets                upload (multipart "file"; 409 QUOTA_EXCEEDED)
///   DELETE card-design/assets/{id}           two-phase delete (409 ASSET_REFERENCED)
///   POST   card-design/assets/cleanup        tombstone every unreferenced asset + sweep
/// </summary>
[ApiController]
[Route("api/operations/viana3-monitor/card-design")]
[Authorize(Roles = RoleConstants.SystemAdministrator + "," + RoleConstants.Operations)]
public class Viana3CardDesignController : ControllerBase
{
    private readonly IViana3CardDesignService _designs;
    private readonly IViana3DesignAssetService _assets;
    private readonly ILogger<Viana3CardDesignController> _logger;

    public Viana3CardDesignController(IViana3CardDesignService designs, IViana3DesignAssetService assets, ILogger<Viana3CardDesignController> logger)
    {
        _designs = designs;
        _assets = assets;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ─── Anonymous reads (TV) ───

    [AllowAnonymous]
    [HttpGet]
    public Task<IActionResult> GetBundle(CancellationToken ct) =>
        Guard(async () => Ok(await _designs.GetBundleAsync(ct)), "reading the card-design bundle");

    [AllowAnonymous]
    [HttpGet("assets/{id:guid}")]
    public async Task<IActionResult> GetAssetFile(Guid id, CancellationToken ct)
    {
        try
        {
            var file = await _assets.GetFileAsync(id, ct);
            if (file is null) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            Response.Headers["Content-Disposition"] = "inline";
            return File(file.Value.Bytes, file.Value.ContentType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CardDesign] Unexpected error serving asset {Id}", id);
            return StatusCode(500);
        }
    }

    // ─── Catalogue ───

    [HttpGet("catalog")]
    public Task<IActionResult> GetCatalog(CancellationToken ct) =>
        Guard(async () => Ok(await _designs.GetCatalogAsync(ct)), "reading the card-design catalogue");

    // ─── Designs ───

    [HttpGet("template")]
    public Task<IActionResult> GetTemplate(CancellationToken ct) =>
        Guard(async () => Ok(await _designs.GetAsync(0, ct)), "reading the template design");

    [HttpGet("{machineId:int:min(1)}")]
    public Task<IActionResult> GetMachine(int machineId, CancellationToken ct) =>
        Guard(async () => Ok(await _designs.GetAsync(machineId, ct)), $"reading the design of machine {machineId}");

    [HttpPut("template")]
    public Task<IActionResult> SaveTemplate([FromBody] SaveViana3CardDesignRequest request, CancellationToken ct) =>
        Guard(async () => Ok(await _designs.SaveAsync(0, request, GetUserId(), ct)), "saving the template design");

    [HttpPut("{machineId:int:min(1)}")]
    public Task<IActionResult> SaveMachine(int machineId, [FromBody] SaveViana3CardDesignRequest request, CancellationToken ct) =>
        Guard(async () => Ok(await _designs.SaveAsync(machineId, request, GetUserId(), ct)), $"saving the design of machine {machineId}");

    [HttpDelete("template")]
    public Task<IActionResult> ResetTemplate(CancellationToken ct) =>
        Guard(async () => Ok(await _designs.ResetAsync(0, GetUserId(), ct)), "resetting the template design");

    [HttpDelete("{machineId:int:min(1)}")]
    public Task<IActionResult> ResetMachine(int machineId, CancellationToken ct) =>
        Guard(async () => Ok(await _designs.ResetAsync(machineId, GetUserId(), ct)), $"resetting the design of machine {machineId}");

    // ─── Assets ───

    [HttpGet("assets")]
    public Task<IActionResult> ListAssets(CancellationToken ct) =>
        Guard(async () => Ok(await _assets.ListAsync(ct)), "listing design assets");

    [HttpPost("assets")]
    [RequestSizeLimit(Viana3CardDesignRules.AssetUploadRequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = Viana3CardDesignRules.AssetUploadRequestLimitBytes)]
    public Task<IActionResult> UploadAsset(IFormFile? file, CancellationToken ct) =>
        Guard(async () =>
        {
            if (file is null || file.Length == 0) return BadRequest(new { error = "ASSET_INVALID", message = "Nenhum ficheiro recebido." });
            if (file.Length > Viana3CardDesignRules.AssetMaxBytes)
                return BadRequest(new { error = "ASSET_INVALID", message = $"A imagem excede {Viana3CardDesignRules.AssetMaxBytes / (1024 * 1024)} MB." });
            await using var stream = file.OpenReadStream();
            var result = await _assets.UploadAsync(stream, file.FileName, file.ContentType, GetUserId(), ct);
            return Ok(result);
        }, "uploading a design asset");

    [HttpDelete("assets/{id:guid}")]
    public Task<IActionResult> DeleteAsset(Guid id, CancellationToken ct) =>
        Guard(async () => { await _assets.DeleteAsync(id, GetUserId(), ct); return NoContent(); }, $"deleting design asset {id}");

    [HttpPost("assets/cleanup")]
    public Task<IActionResult> CleanupAssets(CancellationToken ct) =>
        Guard(async () => Ok(await _assets.CleanupUnreferencedAsync(GetUserId(), ct)), "cleaning up design assets");

    // ─── Error mapping ───

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action, string what)
    {
        try
        {
            return await action();
        }
        catch (Viana3CardDesignValidationException ex)
        {
            return BadRequest(new { error = "DESIGN_INVALID", message = ex.Message });
        }
        catch (Viana3CardDesignNotFoundException ex)
        {
            return NotFound(new { error = "NOT_FOUND", message = ex.Message });
        }
        catch (Viana3CardDesignConflictException ex)
        {
            if (ex.Code == "BOARD_BUSY") Response.Headers["Retry-After"] = "1";
            return Conflict(new { error = ex.Code, message = ex.Message, detail = ex.Detail });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CardDesign] Unexpected error {What}", what);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno no design dos cartões. Contacte o suporte." });
        }
    }
}
