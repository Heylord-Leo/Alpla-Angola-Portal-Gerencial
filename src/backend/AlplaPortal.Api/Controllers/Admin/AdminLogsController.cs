using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/logs")]
public class AdminLogsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AdminLogsController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Query admin log entries with server-side filtering and pagination.
    /// Default order: newest first.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level = null,
        [FromQuery] string? source = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);

        var query = _db.AdminLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(e => e.Level == level);

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(e => e.Source.Contains(source));

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(e => e.CorrelationId == correlationId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                e.Message.Contains(search) ||
                (e.Source != null && e.Source.Contains(search)) ||
                (e.EventType != null && e.EventType.Contains(search)));

        if (startDate.HasValue)
            query = query.Where(e => e.TimestampUtc >= startDate.Value.ToUniversalTime());

        if (endDate.HasValue)
            query = query.Where(e => e.TimestampUtc <= endDate.Value.ToUniversalTime());

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.TimestampUtc,
                e.Level,
                e.Source,
                e.EventType,
                e.Message,
                e.CorrelationId,
                e.UserEmail,
                HasExceptionDetail = e.ExceptionDetail != null,
                HasPayload = e.Payload != null
            })
            .ToListAsync();

        return Ok(new
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            Items = items
        });
    }

    /// <summary>
    /// Get full detail for a single admin log entry.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetLog(int id)
    {
        var entry = await _db.AdminLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entry is null) return NotFound();
        return Ok(entry);
    }
}
