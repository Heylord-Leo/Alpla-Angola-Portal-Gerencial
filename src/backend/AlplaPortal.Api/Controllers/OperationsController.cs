using System.Security.Claims;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Domain.Enums;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Services.Integration.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// REST controller for the Operations module.
///
/// Phase 2: Timeline endpoint — single PO timeline query.
/// Phase 4: Transfer list endpoint — paginated, filterable PO listing.
/// Phase 6: Transfer details endpoint — single PO detail query.
/// Phase Live 2: Live Board endpoint — TV-ready inbound/outbound transfer cards.
///
/// Queries AlplaPROD production databases (read-only) to build transfer
/// timelines, listings, detail views, and live board displays.
///
/// Error handling:
///   • Invalid plant             → 400 Bad Request
///   • Missing/invalid params    → 400 Bad Request
///   • Integration disabled      → 503 Service Unavailable
///   • Plant disabled            → 503 Service Unavailable
///   • Missing credentials       → 503 Service Unavailable
///   • Transfer not found        → 404 Not Found
///   • SQL timeout / connection  → 503 Service Unavailable
///   • Unexpected exception      → 500 Internal Server Error
///
/// No credentials, connection strings, raw SQL, or stack traces are exposed
/// in API responses. Technical details are logged server-side only.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §7–§9
///                   docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md §9
/// </summary>
[Authorize]
[ApiController]
[Route("api/operations")]
public class OperationsController : ControllerBase
{
    private readonly IOperationsTimelineService _timelineService;
    private readonly IOperationsTransferListService _transferListService;
    private readonly IOperationsTransferDetailService _detailService;
    private readonly IOperationsLiveBoardService _liveBoardService;
    private readonly IViana3ProductionMonitorService _viana3Monitor;
    private readonly IViana3MonitorLayoutService _viana3Layout;
    private readonly ILogger<OperationsController> _logger;

    /// <summary>Maximum allowed date range for transfer list queries (days).</summary>
    private const int MaxDateRangeDays = 90;

    /// <summary>Default page size for transfer list queries.</summary>
    private const int DefaultPageSize = 25;

    /// <summary>Maximum page size for transfer list queries.</summary>
    private const int MaxPageSize = 100;

    public OperationsController(
        IOperationsTimelineService timelineService,
        IOperationsTransferListService transferListService,
        IOperationsTransferDetailService detailService,
        IOperationsLiveBoardService liveBoardService,
        IViana3ProductionMonitorService viana3Monitor,
        IViana3MonitorLayoutService viana3Layout,
        ILogger<OperationsController> logger)
    {
        _timelineService = timelineService;
        _transferListService = transferListService;
        _detailService = detailService;
        _liveBoardService = liveBoardService;
        _viana3Monitor = viana3Monitor;
        _viana3Layout = viana3Layout;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Viana 3 Production Monitor (TV)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the Viana 3 Production Monitor payload: per machine the current
    /// lot, the Warehouse 3 stock of its preform, planned / reported / scanned
    /// figures, AlplaLABEL Waste and the two previous production lots.
    ///
    /// Read-only projection of AlplaPROD_aovia3, cached for the refresh window;
    /// when AlplaPROD is unavailable the last successful snapshot is returned
    /// with source.status = STALE. Anonymous for the same reason as the Live
    /// Board (TV signage; no financial values, no usernames).
    ///
    /// Examples:
    ///   GET /api/operations/viana3-monitor
    ///   GET /api/operations/viana3-monitor?refreshSeconds=60
    /// </summary>
    [AllowAnonymous]
    [HttpGet("viana3-monitor")]
    public async Task<IActionResult> GetViana3Monitor([FromQuery] int? refreshSeconds, CancellationToken ct)
    {
        var resolvedRefresh = Math.Clamp(refreshSeconds ?? 120, 60, 300);
        try
        {
            var result = await _viana3Monitor.GetMonitorAsync(resolvedRefresh, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Operations] Service unavailable for Viana3 monitor");
            return StatusCode(503, new { error = "SERVICE_UNAVAILABLE", message = ex.Message });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "[Operations] SQL timeout for Viana3 monitor");
            return StatusCode(503, new { error = "QUERY_TIMEOUT", message = "A consulta ao AlplaPROD excedeu o tempo limite. Tente novamente." });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex, "[Operations] SQL error for Viana3 monitor: SqlError={Number}", ex.Number);
            return StatusCode(503, new { error = "CONNECTION_ERROR", message = "Erro de conexão com o AlplaPROD. Tente novamente." });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error for Viana3 monitor");
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao processar o monitor de produção. Contacte o suporte." });
        }
    }

    /// <summary>
    /// Diagnostic view of the Viana 3 monitor: historical open SYSTEM balance at
    /// the machine areas (not stock) and AlplaLABEL Waste on deleted lots.
    /// Cached for one hour. Never shown on the main TV page. Restricted to
    /// OPERATIONS / System Administrator (same policy as the editors).
    ///
    ///   GET /api/operations/viana3-monitor/diagnostics
    /// </summary>
    [Authorize(Roles = Viana3EditorRoles)] // restricted like the editors: OPERATIONS or System Administrator; the TV never calls it
    [HttpGet("viana3-monitor/diagnostics")]
    public async Task<IActionResult> GetViana3MonitorDiagnostics(CancellationToken ct)
    {
        try
        {
            var result = await _viana3Monitor.GetDiagnosticsAsync(ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Operations] Service unavailable for Viana3 diagnostics");
            return StatusCode(503, new { error = "SERVICE_UNAVAILABLE", message = ex.Message });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "[Operations] SQL timeout for Viana3 diagnostics");
            return StatusCode(503, new { error = "QUERY_TIMEOUT", message = "A consulta ao AlplaPROD excedeu o tempo limite. Tente novamente." });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex, "[Operations] SQL error for Viana3 diagnostics: SqlError={Number}", ex.Number);
            return StatusCode(503, new { error = "CONNECTION_ERROR", message = "Erro de conexão com o AlplaPROD. Tente novamente." });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error for Viana3 diagnostics");
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao processar o diagnóstico do monitor. Contacte o suporte." });
        }
    }

    // ─── Viana 3 Production Monitor: shared TV screens / layouts ───
    //
    // The TV is read-only and loads its screen's layout anonymously (display
    // preferences only: machine ids, order, sizes, hidden flags — no production
    // data, no usernames). Editing is restricted to OPERATIONS and System
    // Administrator, like the Operations workspace. Hiding or removing a card is
    // a display decision; it never deletes or changes AlplaPROD records.
    //
    //   GET    viana3-monitor/layout                       default screen layout (historical TV URL) — anonymous
    //   GET    viana3-monitor/screens/{key}/layout         one screen's layout — anonymous (TV)
    //   GET    viana3-monitor/screens                      screen list (editor)
    //   POST   viana3-monitor/screens                      create a named screen
    //   PUT    viana3-monitor/screens/{key}                rename
    //   PUT    viana3-monitor/screens/{key}/layout         save layout
    //   DELETE viana3-monitor/screens/{key}/layout         reset layout
    //   DELETE viana3-monitor/screens/{key}                delete a named screen (default refused)

    private const string Viana3EditorRoles = RoleConstants.SystemAdministrator + "," + RoleConstants.Operations;

    /// <summary>Layout of the default screen (backs the historical TV URL).</summary>
    [AllowAnonymous]
    [HttpGet("viana3-monitor/layout")]
    public Task<IActionResult> GetViana3MonitorLayout(CancellationToken ct) =>
        GetViana3MonitorScreenLayout(Viana3MonitorLayoutRules.DefaultScreenKey, ct);

    /// <summary>Layout of one screen (404 when the key is unknown).</summary>
    [AllowAnonymous]
    [HttpGet("viana3-monitor/screens/{screenKey}/layout")]
    public async Task<IActionResult> GetViana3MonitorScreenLayout(string screenKey, CancellationToken ct)
    {
        try
        {
            var layout = await _viana3Layout.GetLayoutAsync(screenKey, ct);
            return layout is null
                ? NotFound(new { error = "SCREEN_NOT_FOUND", message = "Ecrã da TV não encontrado." })
                : Ok(layout);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error reading the Viana3 monitor layout for screen {ScreenKey}", screenKey);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao ler o layout do monitor. Contacte o suporte." });
        }
    }

    /// <summary>Every screen of the board (editor).</summary>
    [Authorize(Roles = Viana3EditorRoles)]
    [HttpGet("viana3-monitor/screens")]
    public async Task<IActionResult> ListViana3MonitorScreens(CancellationToken ct)
    {
        try
        {
            return Ok(await _viana3Layout.ListScreensAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error listing the Viana3 monitor screens");
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao listar os ecrãs do monitor. Contacte o suporte." });
        }
    }

    /// <summary>Creates a named screen with a generated stable key.</summary>
    [Authorize(Roles = Viana3EditorRoles)]
    [HttpPost("viana3-monitor/screens")]
    public async Task<IActionResult> CreateViana3MonitorScreen([FromBody] CreateViana3MonitorScreenRequest request, CancellationToken ct)
    {
        try
        {
            var layout = await _viana3Layout.CreateScreenAsync(request, GetUserId(), ct);
            return Ok(layout);
        }
        catch (Viana3MonitorLayoutValidationException ex)
        {
            return BadRequest(new { error = "SCREEN_INVALID", message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error creating a Viana3 monitor screen");
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao criar o ecrã. Contacte o suporte." });
        }
    }

    /// <summary>Renames a screen.</summary>
    [Authorize(Roles = Viana3EditorRoles)]
    [HttpPut("viana3-monitor/screens/{screenKey}")]
    public async Task<IActionResult> RenameViana3MonitorScreen(string screenKey, [FromBody] RenameViana3MonitorScreenRequest request, CancellationToken ct)
    {
        try
        {
            var screen = await _viana3Layout.RenameScreenAsync(screenKey, request, GetUserId(), ct);
            return screen is null
                ? NotFound(new { error = "SCREEN_NOT_FOUND", message = "Ecrã da TV não encontrado." })
                : Ok(screen);
        }
        catch (Viana3MonitorLayoutValidationException ex)
        {
            return BadRequest(new { error = "SCREEN_INVALID", message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error renaming Viana3 monitor screen {ScreenKey}", screenKey);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao renomear o ecrã. Contacte o suporte." });
        }
    }

    /// <summary>Replaces the layout of one screen (validated; 400 on an invalid document).</summary>
    [Authorize(Roles = Viana3EditorRoles)]
    [HttpPut("viana3-monitor/screens/{screenKey}/layout")]
    public async Task<IActionResult> SaveViana3MonitorScreenLayout(string screenKey, [FromBody] SaveViana3MonitorLayoutRequest request, CancellationToken ct)
    {
        try
        {
            var layout = await _viana3Layout.SaveLayoutAsync(screenKey, request, GetUserId(), ct);
            return layout is null
                ? NotFound(new { error = "SCREEN_NOT_FOUND", message = "Ecrã da TV não encontrado." })
                : Ok(layout);
        }
        catch (Viana3MonitorLayoutValidationException ex)
        {
            return BadRequest(new { error = "LAYOUT_INVALID", message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error saving the Viana3 monitor layout for screen {ScreenKey}", screenKey);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao guardar o layout do monitor. Contacte o suporte." });
        }
    }

    /// <summary>Resets the layout of one screen (default screen → discovery layout; named screen → empty selection).</summary>
    [Authorize(Roles = Viana3EditorRoles)]
    [HttpDelete("viana3-monitor/screens/{screenKey}/layout")]
    public async Task<IActionResult> ResetViana3MonitorScreenLayout(string screenKey, CancellationToken ct)
    {
        try
        {
            var layout = await _viana3Layout.ResetLayoutAsync(screenKey, GetUserId(), ct);
            return layout is null
                ? NotFound(new { error = "SCREEN_NOT_FOUND", message = "Ecrã da TV não encontrado." })
                : Ok(layout);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error resetting the Viana3 monitor layout for screen {ScreenKey}", screenKey);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao repor o layout do monitor. Contacte o suporte." });
        }
    }

    /// <summary>Deletes a named screen (the default screen is refused with 400).</summary>
    [Authorize(Roles = Viana3EditorRoles)]
    [HttpDelete("viana3-monitor/screens/{screenKey}")]
    public async Task<IActionResult> DeleteViana3MonitorScreen(string screenKey, CancellationToken ct)
    {
        try
        {
            var deleted = await _viana3Layout.DeleteScreenAsync(screenKey, GetUserId(), ct);
            return deleted
                ? NoContent()
                : NotFound(new { error = "SCREEN_NOT_FOUND", message = "Ecrã da TV não encontrado." });
        }
        catch (Viana3MonitorLayoutValidationException ex)
        {
            return BadRequest(new { error = "SCREEN_INVALID", message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operations] Unexpected error deleting Viana3 monitor screen {ScreenKey}", screenKey);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Erro interno ao eliminar o ecrã. Contacte o suporte." });
        }
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ═══════════════════════════════════════════════════════════════════════
    // Phase Live 2: Live Board
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a TV-ready Live Board response with pre-classified inbound
    /// and outbound transfer cards for a specific plant.
    ///
    /// Optimized for 60-second auto-refresh on TV/kiosk displays.
    /// Returns simplified mini-timeline steps, attention flags,
    /// and quantity data — no further business logic needed on the client.
    ///
    /// Examples:
    ///   GET /api/operations/live-board?plant=VIANA1
    ///   GET /api/operations/live-board?plant=VIANA1&amp;refreshSeconds=30&amp;maxInbound=3
    ///   GET /api/operations/live-board?plant=VIANA1&amp;includeRecentlyCompleted=true&amp;completedWindowHours=8
    /// </summary>
    [AllowAnonymous]
    [HttpGet("live-board")]
    public async Task<IActionResult> GetLiveBoard(
        [FromQuery] string? plant,
        [FromQuery] int? refreshSeconds,
        [FromQuery] int? maxInbound,
        [FromQuery] int? maxOutbound,
        [FromQuery] bool? includeRecentlyCompleted,
        [FromQuery] int? completedWindowHours,
        CancellationToken ct)
    {
        // ─── Validate plant ───

        if (string.IsNullOrWhiteSpace(plant))
        {
            return BadRequest(new
            {
                error = "INVALID_PLANT",
                message = $"O parâmetro 'plant' é obrigatório. Valores aceitos: {string.Join(", ", Enum.GetNames<AlplaProdPlant>())}."
            });
        }

        if (!Enum.TryParse<AlplaProdPlant>(plant, ignoreCase: true, out var parsedPlant))
        {
            return BadRequest(new
            {
                error = "INVALID_PLANT",
                message = $"Planta inválida: '{plant}'. Valores aceitos: {string.Join(", ", Enum.GetNames<AlplaProdPlant>())}."
            });
        }

        // ─── Validate optional params with safe bounds ───

        var resolvedRefresh = Math.Clamp(refreshSeconds ?? 60, 30, 300);
        var resolvedMaxIn = Math.Clamp(maxInbound ?? 6, 1, 12);
        var resolvedMaxOut = Math.Clamp(maxOutbound ?? 6, 1, 12);
        var resolvedCompleted = includeRecentlyCompleted ?? true;
        var resolvedWindow = Math.Clamp(completedWindowHours ?? 4, 1, 24);

        // ─── Execute ───

        try
        {
            var result = await _liveBoardService.GetLiveBoardAsync(
                parsedPlant,
                resolvedRefresh,
                resolvedMaxIn,
                resolvedMaxOut,
                resolvedCompleted,
                resolvedWindow,
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "[Operations] Service unavailable for live board: Plant={Plant}",
                parsedPlant);

            return StatusCode(503, new
            {
                error = "SERVICE_UNAVAILABLE",
                message = ex.Message
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex,
                "[Operations] SQL timeout for live board: Plant={Plant}",
                parsedPlant);

            return StatusCode(503, new
            {
                error = "QUERY_TIMEOUT",
                message = "A consulta ao AlplaPROD excedeu o tempo limite. Tente novamente."
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex,
                "[Operations] SQL error for live board: Plant={Plant}, SqlError={Number}",
                parsedPlant, ex.Number);

            return StatusCode(503, new
            {
                error = "CONNECTION_ERROR",
                message = "Erro de conexão com o AlplaPROD. Tente novamente."
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Operations] Unexpected error for live board: Plant={Plant}",
                parsedPlant);

            return StatusCode(500, new
            {
                error = "INTERNAL_ERROR",
                message = "Erro interno ao processar o live board. Contacte o suporte."
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 4: Transfer List
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a paginated list of transfers/purchase orders for a plant
    /// within the specified date range.
    ///
    /// Examples:
    ///   GET /api/operations/transfers?plant=VIANA1&amp;dateFrom=2026-05-01&amp;dateTo=2026-05-31
    ///   GET /api/operations/transfers?plant=VIANA2&amp;dateFrom=2026-05-01&amp;dateTo=2026-05-31&amp;status=ACTIVE
    ///   GET /api/operations/transfers?plant=VIANA3&amp;dateFrom=2026-02-01&amp;dateTo=2026-03-31&amp;pipelineModel=INHOUSE
    /// </summary>
    [Authorize(Roles = RoleConstants.SystemAdministrator + "," + RoleConstants.Operations)]
    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransferList(
        [FromQuery] string? plant,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? status,
        [FromQuery] string? pipelineModel,
        [FromQuery] string? articleSearch,
        [FromQuery] string? poSearch,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        // ─── Validate plant ───

        if (string.IsNullOrWhiteSpace(plant))
        {
            return BadRequest(new
            {
                error = "INVALID_PLANT",
                message = $"O parâmetro 'plant' é obrigatório. Valores aceitos: {string.Join(", ", Enum.GetNames<AlplaProdPlant>())}."
            });
        }

        if (!Enum.TryParse<AlplaProdPlant>(plant, ignoreCase: true, out var parsedPlant))
        {
            return BadRequest(new
            {
                error = "INVALID_PLANT",
                message = $"Planta inválida: '{plant}'. Valores aceitos: {string.Join(", ", Enum.GetNames<AlplaProdPlant>())}."
            });
        }

        // ─── Validate date range ───

        if (!dateFrom.HasValue || !dateTo.HasValue)
        {
            return BadRequest(new
            {
                error = "MISSING_DATE_RANGE",
                message = "Os parâmetros 'dateFrom' e 'dateTo' são obrigatórios."
            });
        }

        if (dateFrom.Value > dateTo.Value)
        {
            return BadRequest(new
            {
                error = "INVALID_DATE_RANGE",
                message = "'dateFrom' deve ser anterior ou igual a 'dateTo'."
            });
        }

        var rangeDays = (dateTo.Value.Date - dateFrom.Value.Date).TotalDays;
        if (rangeDays > MaxDateRangeDays)
        {
            return BadRequest(new
            {
                error = "DATE_RANGE_TOO_LARGE",
                message = $"O intervalo de datas não pode exceder {MaxDateRangeDays} dias. Intervalo solicitado: {(int)rangeDays} dias."
            });
        }

        // ─── Validate status filter ───

        if (!OperationsTransferListQueryBuilder.IsValidStatusFilter(status))
        {
            return BadRequest(new
            {
                error = "INVALID_STATUS_FILTER",
                message = $"Filtro de status inválido: '{status}'. Valores aceitos: ACTIVE, COMPLETED, CANCELLED."
            });
        }

        // ─── Validate pagination ───

        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPage < 1)
        {
            return BadRequest(new
            {
                error = "INVALID_PAGINATION",
                message = "'page' deve ser um número inteiro positivo (mínimo: 1)."
            });
        }

        if (resolvedPageSize < 1 || resolvedPageSize > MaxPageSize)
        {
            return BadRequest(new
            {
                error = "INVALID_PAGINATION",
                message = $"'pageSize' deve estar entre 1 e {MaxPageSize}."
            });
        }

        // ─── Execute ───

        try
        {
            var result = await _transferListService.GetTransferListAsync(
                parsedPlant,
                dateFrom.Value,
                dateTo.Value,
                status,
                pipelineModel,
                articleSearch,
                poSearch,
                resolvedPage,
                resolvedPageSize,
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "[Operations] Service unavailable for transfer list: Plant={Plant}",
                parsedPlant);

            return StatusCode(503, new
            {
                error = "SERVICE_UNAVAILABLE",
                message = ex.Message
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex,
                "[Operations] SQL timeout for transfer list: Plant={Plant}",
                parsedPlant);

            return StatusCode(503, new
            {
                error = "QUERY_TIMEOUT",
                message = "A consulta ao AlplaPROD excedeu o tempo limite. Tente novamente."
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex,
                "[Operations] SQL error for transfer list: Plant={Plant}, SqlError={Number}",
                parsedPlant, ex.Number);

            return StatusCode(503, new
            {
                error = "CONNECTION_ERROR",
                message = "Erro de conexão com o AlplaPROD. Tente novamente."
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Operations] Unexpected error for transfer list: Plant={Plant}",
                parsedPlant);

            return StatusCode(500, new
            {
                error = "INTERNAL_ERROR",
                message = "Erro interno ao processar a lista de transferências. Contacte o suporte."
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 6: Transfer Details
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns detailed information for a specific purchase order (IdBestellung)
    /// in the given AlplaPROD plant.
    ///
    /// Examples:
    ///   GET /api/operations/transfers/VIANA1/3579/details
    ///   GET /api/operations/transfers/VIANA2/3425/details
    ///   GET /api/operations/transfers/VIANA3/5/details
    /// </summary>
    [Authorize(Roles = RoleConstants.SystemAdministrator + "," + RoleConstants.Operations)]
    [HttpGet("transfers/{plant}/{idBestellung:int}/details")]
    public async Task<IActionResult> GetTransferDetails(
        string plant, int idBestellung, CancellationToken ct)
    {
        // ─── Validate plant enum ───

        if (!Enum.TryParse<AlplaProdPlant>(plant, ignoreCase: true, out var parsedPlant))
        {
            return BadRequest(new
            {
                error = "INVALID_PLANT",
                message = $"Planta inválida: '{plant}'. Valores aceitos: {string.Join(", ", Enum.GetNames<AlplaProdPlant>())}."
            });
        }

        if (idBestellung <= 0)
        {
            return BadRequest(new
            {
                error = "INVALID_ID",
                message = "O IdBestellung deve ser um número inteiro positivo."
            });
        }

        try
        {
            var result = await _detailService.GetTransferDetailAsync(parsedPlant, idBestellung, ct);

            // null signals PO not found
            if (result == null)
            {
                return NotFound(new
                {
                    error = "TRANSFER_NOT_FOUND",
                    message = $"Pedido de compra {idBestellung} não encontrado na planta {parsedPlant}.",
                    plant = parsedPlant.ToString(),
                    idBestellung
                });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "[Operations] Service unavailable for transfer detail: Plant={Plant}, Id={Id}",
                parsedPlant, idBestellung);

            return StatusCode(503, new
            {
                error = "SERVICE_UNAVAILABLE",
                message = ex.Message
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex,
                "[Operations] SQL timeout for transfer detail: Plant={Plant}, Id={Id}",
                parsedPlant, idBestellung);

            return StatusCode(503, new
            {
                error = "QUERY_TIMEOUT",
                message = "A consulta ao AlplaPROD excedeu o tempo limite. Tente novamente."
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _logger.LogError(ex,
                "[Operations] SQL error for transfer detail: Plant={Plant}, Id={Id}, SqlError={Number}",
                parsedPlant, idBestellung, ex.Number);

            return StatusCode(503, new
            {
                error = "CONNECTION_ERROR",
                message = "Erro de conexão com o AlplaPROD. Tente novamente."
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Operations] Unexpected error for transfer detail: Plant={Plant}, Id={Id}",
                parsedPlant, idBestellung);

            return StatusCode(500, new
            {
                error = "INTERNAL_ERROR",
                message = "Erro interno ao processar os detalhes da transferência. Contacte o suporte."
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 2: Timeline
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the ordered timeline of logistics events for a specific
    /// purchase order (IdBestellung) in the given AlplaPROD plant.
    ///
    /// Examples:
    ///   GET /api/operations/transfers/VIANA1/26/timeline
    ///   GET /api/operations/transfers/VIANA2/100/timeline
    ///   GET /api/operations/transfers/VIANA3/42/timeline
    /// </summary>
    [Authorize(Roles = RoleConstants.SystemAdministrator + "," + RoleConstants.Operations)]
    [HttpGet("transfers/{plant}/{idBestellung:int}/timeline")]
    public async Task<IActionResult> GetTimeline(
        string plant, int idBestellung, CancellationToken ct)
    {
        // ─── Validate plant enum ───

        if (!Enum.TryParse<AlplaProdPlant>(plant, ignoreCase: true, out var parsedPlant))
        {
            return BadRequest(new
            {
                error = "INVALID_PLANT",
                message = $"Planta inválida: '{plant}'. Valores aceitos: {string.Join(", ", Enum.GetNames<AlplaProdPlant>())}."
            });
        }

        if (idBestellung <= 0)
        {
            return BadRequest(new
            {
                error = "INVALID_ID",
                message = "O IdBestellung deve ser um número inteiro positivo."
            });
        }

        try
        {
            var result = await _timelineService.GetTimelineAsync(parsedPlant, idBestellung, ct);

            // null signals PO not found
            if (result == null)
            {
                return NotFound(new
                {
                    error = "TRANSFER_NOT_FOUND",
                    message = $"Pedido de compra {idBestellung} não encontrado na planta {parsedPlant}.",
                    plant = parsedPlant.ToString(),
                    idBestellung
                });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Configuration/credential issues from AlplaProdConnectionFactory
            _logger.LogWarning(ex,
                "[Operations] Service unavailable for timeline: Plant={Plant}, Id={Id}",
                parsedPlant, idBestellung);

            return StatusCode(503, new
            {
                error = "SERVICE_UNAVAILABLE",
                message = ex.Message
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            // SQL timeout
            _logger.LogError(ex,
                "[Operations] SQL timeout for timeline: Plant={Plant}, Id={Id}",
                parsedPlant, idBestellung);

            return StatusCode(503, new
            {
                error = "QUERY_TIMEOUT",
                message = "A consulta ao AlplaPROD excedeu o tempo limite. Tente novamente."
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            // SQL connection failure or other SQL error
            _logger.LogError(ex,
                "[Operations] SQL error for timeline: Plant={Plant}, Id={Id}, SqlError={Number}",
                parsedPlant, idBestellung, ex.Number);

            return StatusCode(503, new
            {
                error = "CONNECTION_ERROR",
                message = "Erro de conexão com o AlplaPROD. Tente novamente."
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — no response needed
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected error — 500
            _logger.LogError(ex,
                "[Operations] Unexpected error for timeline: Plant={Plant}, Id={Id}",
                parsedPlant, idBestellung);

            return StatusCode(500, new
            {
                error = "INTERNAL_ERROR",
                message = "Erro interno ao processar a timeline. Contacte o suporte."
            });
        }
    }
}
