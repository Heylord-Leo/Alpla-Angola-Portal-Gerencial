using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

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
                (e.EventType != null && e.EventType.Contains(search)) ||
                (e.ExceptionDetail != null && e.ExceptionDetail.Contains(search)) ||
                (e.Payload != null && e.Payload.Contains(search)));

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

    /// <summary>
    /// Gets activity histogram for the logs.
    /// Groups by a reasonable interval based on date range.
    /// </summary>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(
        [FromQuery] string? level = null,
        [FromQuery] string? source = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var query = _db.AdminLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level)) query = query.Where(e => e.Level == level);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(e => e.Source.Contains(source));
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(e => e.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(correlationId)) query = query.Where(e => e.CorrelationId == correlationId);
        
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                e.Message.Contains(search) ||
                (e.Source != null && e.Source.Contains(search)) ||
                (e.EventType != null && e.EventType.Contains(search)) ||
                (e.ExceptionDetail != null && e.ExceptionDetail.Contains(search)) ||
                (e.Payload != null && e.Payload.Contains(search)));

        if (startDate.HasValue) query = query.Where(e => e.TimestampUtc >= startDate.Value.ToUniversalTime());
        if (endDate.HasValue) query = query.Where(e => e.TimestampUtc <= endDate.Value.ToUniversalTime());

        // Limits to recently retrieved items for performance
        var items = await query.OrderByDescending(e => e.TimestampUtc)
                               .Take(5000)
                               .Select(e => new { e.TimestampUtc, e.Level })
                               .ToListAsync();

        var diffHours = endDate.HasValue && startDate.HasValue 
            ? (endDate.Value - startDate.Value).TotalHours 
            : 24;

        // Group by Hour if period is short, else by Day
        var grouped = items.GroupBy(e => 
        {
            if(diffHours <= 72)
                return new DateTime(e.TimestampUtc.Year, e.TimestampUtc.Month, e.TimestampUtc.Day, e.TimestampUtc.Hour, 0, 0);
            return new DateTime(e.TimestampUtc.Year, e.TimestampUtc.Month, e.TimestampUtc.Day);
        });

        var result = grouped.OrderBy(g => g.Key).Select(g => new
        {
            Period = g.Key,
            PeriodLabel = diffHours <= 72 ? g.Key.ToString("dd/MM HH:mm") : g.Key.ToString("dd/MM/yyyy"),
            InfoCount = g.Count(x => x.Level == "Information"),
            WarningCount = g.Count(x => x.Level == "Warning"),
            ErrorCount = g.Count(x => x.Level == "Error" || x.Level == "Critical"),
            TotalCount = g.Count()
        });

        return Ok(result);
    }

    /// <summary>
    /// Exports the queried logs as a CSV file. Max 5000 rows.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportLogs(
        [FromQuery] string? level = null,
        [FromQuery] string? source = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var query = _db.AdminLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level)) query = query.Where(e => e.Level == level);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(e => e.Source.Contains(source));
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(e => e.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(correlationId)) query = query.Where(e => e.CorrelationId == correlationId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                e.Message.Contains(search) ||
                (e.Source != null && e.Source.Contains(search)) ||
                (e.EventType != null && e.EventType.Contains(search)) ||
                (e.ExceptionDetail != null && e.ExceptionDetail.Contains(search)) ||
                (e.Payload != null && e.Payload.Contains(search)));

        if (startDate.HasValue) query = query.Where(e => e.TimestampUtc >= startDate.Value.ToUniversalTime());
        if (endDate.HasValue) query = query.Where(e => e.TimestampUtc <= endDate.Value.ToUniversalTime());

        var items = await query.OrderByDescending(e => e.TimestampUtc)
                               .Take(5000)
                               .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("ID,TimestampUtc,Level,Source,EventType,Message,CorrelationId,UserEmail");

        foreach (var e in items)
        {
            var msg = e.Message?.Replace("\"", "\"\"") ?? "";
            var sourceStr = e.Source?.Replace("\"", "\"\"") ?? "";
            var eventTypeStr = e.EventType?.Replace("\"", "\"\"") ?? "";
            var email = e.UserEmail?.Replace("\"", "\"\"") ?? "";

            sb.AppendLine($"{e.Id},{e.TimestampUtc:o},\"{e.Level}\",\"{sourceStr}\",\"{eventTypeStr}\",\"{msg}\",\"{e.CorrelationId}\",\"{email}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"SystemLogs_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }
}
