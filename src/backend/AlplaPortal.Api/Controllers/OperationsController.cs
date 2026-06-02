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
        ILogger<OperationsController> logger)
    {
        _timelineService = timelineService;
        _transferListService = transferListService;
        _detailService = detailService;
        _liveBoardService = liveBoardService;
        _logger = logger;
    }
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
