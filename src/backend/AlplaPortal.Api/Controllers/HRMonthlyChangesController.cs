using System.Security.Claims;
using AlplaPortal.Application.DTOs.MonthlyChanges;
using AlplaPortal.Application.Interfaces.MonthlyChanges;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// HR Monthly Changes Middleware — processing pipeline API.
///
/// This controller exposes the backend of the Innux → Portal → Primavera
/// HR monthly export workflow. Phase 2A endpoints:
///   - Create processing runs
///   - Execute runs (sync + detect)
///   - Query results (runs, items, anomalies, logs)
///
/// Authorization: System Administrator or HR roles only.
/// These are operational/admin endpoints, not self-service.
///
/// Future phases will add:
///   - Item review/approval endpoints
///   - Export generation endpoints
///   - Configuration CRUD (thresholds, code mappings)
/// </summary>
[ApiController]
[Route("api/hr/monthly-changes")]
[Authorize]
public class HRMonthlyChangesController : ControllerBase
{
    private readonly IMonthlyChangesOrchestrator _orchestrator;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HRMonthlyChangesController> _logger;

    public HRMonthlyChangesController(
        IMonthlyChangesOrchestrator orchestrator,
        ApplicationDbContext context,
        ILogger<HRMonthlyChangesController> logger)
    {
        _orchestrator = orchestrator;
        _context = context;
        _logger = logger;
    }

    // ─── Processing Runs ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a new DRAFT processing run for the specified entity and month.
    /// </summary>
    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] CreateProcessingRunRequest request)
    {
        if (!IsAuthorized())
            return Forbid();

        if (request.EntityId != 1 && request.EntityId != 6)
            return BadRequest(new { message = "EntityId must be 1 (AlplaPLASTICO) or 6 (AlplaSOPRO)." });

        if (request.Year < 2020 || request.Year > 2030)
            return BadRequest(new { message = "Year must be between 2020 and 2030." });

        if (request.Month < 1 || request.Month > 12)
            return BadRequest(new { message = "Month must be between 1 and 12." });

        try
        {
            var result = await _orchestrator.CreateRunAsync(request, CurrentUserId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Returns all processing runs, optionally filtered by entity.
    /// </summary>
    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns([FromQuery] int? entityId = null)
    {
        if (!IsAuthorized())
            return Forbid();

        var runs = await _orchestrator.ListRunsAsync(entityId);
        return Ok(runs);
    }

    /// <summary>
    /// Returns full detail for a specific processing run.
    /// </summary>
    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetRunDetail(Guid runId)
    {
        if (!IsAuthorized())
            return Forbid();

        var detail = await _orchestrator.GetRunDetailAsync(runId);
        if (detail == null) return NotFound(new { message = $"Run {runId} not found." });
        return Ok(detail);
    }

    /// <summary>
    /// Executes a DRAFT or FAILED run: sync from Innux → detect occurrences.
    /// This is the main processing trigger.
    /// </summary>
    [HttpPost("runs/{runId:guid}/execute")]
    public async Task<IActionResult> ExecuteRun(Guid runId)
    {
        if (!IsAuthorized())
            return Forbid();

        try
        {
            var result = await _orchestrator.ExecuteRunAsync(runId, CurrentUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ─── Items ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns detected monthly change items for a run, optionally filtered by status.
    /// </summary>
    [HttpGet("runs/{runId:guid}/items")]
    public async Task<IActionResult> ListItems(Guid runId, [FromQuery] string? status = null)
    {
        if (!IsAuthorized())
            return Forbid();

        var items = await _orchestrator.ListItemsAsync(runId, status);
        return Ok(items);
    }

    /// <summary>
    /// Approves an AUTO_CODED item for export.
    /// </summary>
    [HttpPost("runs/{runId:guid}/items/{itemId:guid}/approve")]
    public async Task<IActionResult> ApproveItem(Guid runId, Guid itemId)
    {
        if (!IsAuthorized())
            return Forbid();

        try
        {
            var result = await _orchestrator.ApproveItemAsync(runId, itemId, CurrentUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// Excludes an item from export.
    /// </summary>
    [HttpPost("runs/{runId:guid}/items/{itemId:guid}/exclude")]
    public async Task<IActionResult> ExcludeItem(Guid runId, Guid itemId, [FromBody] ExcludeItemRequest request)
    {
        if (!IsAuthorized())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Exclusion reason is required." });

        try
        {
            var result = await _orchestrator.ExcludeItemAsync(runId, itemId, request, CurrentUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// Re-includes an previously excluded item.
    /// </summary>
    [HttpPost("runs/{runId:guid}/items/{itemId:guid}/reinclude")]
    public async Task<IActionResult> ReincludeItem(Guid runId, Guid itemId)
    {
        if (!IsAuthorized())
            return Forbid();

        try
        {
            var result = await _orchestrator.ReincludeItemAsync(runId, itemId, CurrentUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // ─── Anomalies ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns only anomaly items for a run — items requiring human triage.
    /// </summary>
    [HttpGet("runs/{runId:guid}/anomalies")]
    public async Task<IActionResult> ListAnomalies(Guid runId)
    {
        if (!IsAuthorized())
            return Forbid();

        var anomalies = await _orchestrator.ListAnomaliesAsync(runId);
        return Ok(anomalies);
    }

    /// <summary>
    /// Resolves an anomaly by approving or excluding it.
    /// </summary>
    [HttpPost("runs/{runId:guid}/items/{itemId:guid}/resolve")]
    public async Task<IActionResult> ResolveAnomaly(Guid runId, Guid itemId, [FromBody] ResolveAnomalyRequest request)
    {
        if (!IsAuthorized())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.ResolutionNote))
            return BadRequest(new { message = "Resolution note is required." });

        if (request.Action != "APPROVE" && request.Action != "EXCLUDE")
            return BadRequest(new { message = "Action must be APPROVE or EXCLUDE." });

        try
        {
            var result = await _orchestrator.ResolveAnomalyAsync(runId, itemId, request, CurrentUserId);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // ─── Run State Actions ───────────────────────────────────────────────

    /// <summary>
    /// Marks a NEEDS_REVIEW run as READY_FOR_EXPORT.
    /// </summary>
    [HttpPost("runs/{runId:guid}/ready-for-export")]
    public async Task<IActionResult> MarkReadyForExport(Guid runId)
    {
        if (!IsAuthorized())
            return Forbid();

        try
        {
            var result = await _orchestrator.MarkRunReadyForExportAsync(runId, CurrentUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// Reverts a READY_FOR_EXPORT run back to NEEDS_REVIEW.
    /// </summary>
    [HttpPost("runs/{runId:guid}/revert-to-review")]
    public async Task<IActionResult> RevertToReview(Guid runId)
    {
        if (!IsAuthorized())
            return Forbid();

        try
        {
            var result = await _orchestrator.RevertRunToReviewAsync(runId, CurrentUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // ─── Logs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns processing pipeline logs for a run (diagnostic).
    /// </summary>
    [HttpGet("runs/{runId:guid}/logs")]
    public async Task<IActionResult> GetLogs(Guid runId)
    {
        if (!IsAuthorized())
            return Forbid();

        var logs = await _orchestrator.GetLogsAsync(runId);
        return Ok(logs);
    }

    // ─── Authorization ───────────────────────────────────────────────────

    private bool IsAuthorized()
    {
        return User.IsInRole(RoleConstants.SystemAdministrator) || User.IsInRole(RoleConstants.HR);
    }

    private Guid CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(id) ? Guid.Empty : Guid.Parse(id);
        }
    }
}
