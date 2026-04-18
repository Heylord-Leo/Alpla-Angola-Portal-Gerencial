using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Badge Management Controller — CRUD for badge layouts and print history.
///
/// Separated from HRController to maintain SRP:
/// - HRController: read-only employee data from Primavera/Innux
/// - BadgeController: Portal-only badge template and print management
///
/// Versioning workflow (immutable-active pattern):
/// - POST /layouts          → creates DRAFT layout
/// - PUT  /layouts/{id}     → edits DRAFT only (rejects if ACTIVE/ARCHIVED)
/// - POST /layouts/{id}/publish → promotes DRAFT to ACTIVE, archives previous ACTIVE
/// - POST /layouts/{id}/new-draft → creates DRAFT copy of ACTIVE with Version + 1
///
/// Access: HR role + System Administrator.
/// </summary>
[ApiController]
[Route("api/badges")]
[Authorize(Roles = "System Administrator,HR")]
public class BadgeController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<BadgeController> _logger;

    public BadgeController(ApplicationDbContext db, ILogger<BadgeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ═══════════════════════════════════════════════
    // LAYOUTS
    // ═══════════════════════════════════════════════

    /// <summary>List all layouts with optional filters.</summary>
    [HttpGet("layouts")]
    public async Task<IActionResult> GetLayouts(
        [FromQuery] string? status = null,
        [FromQuery] string? companyCode = null,
        [FromQuery] string? badgeType = null)
    {
        var query = _db.BadgeLayouts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(companyCode))
            query = query.Where(l => l.CompanyCode == null || l.CompanyCode == companyCode);

        if (!string.IsNullOrWhiteSpace(badgeType))
            query = query.Where(l => l.BadgeType == null || l.BadgeType == badgeType);

        var layouts = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => new BadgeLayoutListDto
            {
                Id = l.Id,
                Name = l.Name,
                Description = l.Description,
                Version = l.Version,
                Status = l.Status,
                CompanyCode = l.CompanyCode,
                BadgeType = l.BadgeType,
                PlantCode = l.PlantCode,
                CreatedAtUtc = l.CreatedAtUtc,
                ActivatedAtUtc = l.ActivatedAtUtc,
                CreatedByName = l.CreatedByUser != null ? l.CreatedByUser.FullName : null,
                HasHistory = _db.BadgePrintHistories.Any(h => h.BadgeLayoutId == l.Id)
            })
            .ToListAsync();

        return Ok(layouts);
    }

    /// <summary>Get a single layout by ID (includes full config).</summary>
    [HttpGet("layouts/{id:guid}")]
    public async Task<IActionResult> GetLayout(Guid id)
    {
        var layout = await _db.BadgeLayouts.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new BadgeLayoutDetailDto
            {
                Id = l.Id,
                Name = l.Name,
                Description = l.Description,
                Version = l.Version,
                Status = l.Status,
                CompanyCode = l.CompanyCode,
                BadgeType = l.BadgeType,
                PlantCode = l.PlantCode,
                LayoutConfigJson = l.LayoutConfigJson,
                CreatedAtUtc = l.CreatedAtUtc,
                UpdatedAtUtc = l.UpdatedAtUtc,
                ActivatedAtUtc = l.ActivatedAtUtc,
                CreatedByName = l.CreatedByUser != null ? l.CreatedByUser.FullName : null,
                UpdatedByName = l.UpdatedByUser != null ? l.UpdatedByUser.FullName : null,
                HasHistory = _db.BadgePrintHistories.Any(h => h.BadgeLayoutId == l.Id)
            })
            .FirstOrDefaultAsync();

        if (layout == null)
            return NotFound(new { message = "Layout não encontrado." });

        return Ok(layout);
    }

    /// <summary>Create a new DRAFT layout.</summary>
    [HttpPost("layouts")]
    public async Task<IActionResult> CreateLayout([FromBody] CreateBadgeLayoutDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Nome do layout é obrigatório." });

        // Check name uniqueness for Version 1
        var exists = await _db.BadgeLayouts.AnyAsync(l => l.Name == dto.Name && l.Version == 1);
        if (exists)
            return Conflict(new { message = $"Já existe um layout com o nome '{dto.Name}'." });

        var layout = new BadgeLayout
        {
            Name = dto.Name,
            Description = dto.Description,
            Version = 1,
            Status = BadgeLayoutStatus.Draft,
            CompanyCode = dto.CompanyCode,
            BadgeType = dto.BadgeType,
            PlantCode = dto.PlantCode,
            LayoutConfigJson = dto.LayoutConfigJson ?? "{}",
            CreatedByUserId = GetUserId()
        };

        _db.BadgeLayouts.Add(layout);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge layout created: {LayoutId} '{Name}' v{Version} by {UserId}",
            layout.Id, layout.Name, layout.Version, layout.CreatedByUserId);

        return CreatedAtAction(nameof(GetLayout), new { id = layout.Id }, new { id = layout.Id });
    }

    /// <summary>Update a DRAFT layout. Active/Archived layouts are immutable.</summary>
    [HttpPut("layouts/{id:guid}")]
    public async Task<IActionResult> UpdateLayout(Guid id, [FromBody] UpdateBadgeLayoutDto dto)
    {
        var layout = await _db.BadgeLayouts.FindAsync(id);
        if (layout == null)
            return NotFound(new { message = "Layout não encontrado." });

        if (layout.Status != BadgeLayoutStatus.Draft)
        {
            bool hasHistory = await _db.BadgePrintHistories.AnyAsync(h => h.BadgeLayoutId == id);
            if (hasHistory)
                return BadRequest(new { message = $"Layouts em uso '{layout.Status}' são imutáveis. Crie uma nova versão via 'Nova Versão'." });
        }

        if (!string.IsNullOrWhiteSpace(dto.Name)) layout.Name = dto.Name;
        if (dto.Description != null) layout.Description = dto.Description;
        if (dto.CompanyCode != null) layout.CompanyCode = dto.CompanyCode;
        if (dto.BadgeType != null) layout.BadgeType = dto.BadgeType;
        if (dto.PlantCode != null) layout.PlantCode = dto.PlantCode;
        if (dto.LayoutConfigJson != null) layout.LayoutConfigJson = dto.LayoutConfigJson;

        layout.UpdatedByUserId = GetUserId();
        layout.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge layout updated: {LayoutId} '{Name}' v{Version}", layout.Id, layout.Name, layout.Version);

        return Ok(new { id = layout.Id });
    }

    /// <summary>
    /// Publish/Activate a DRAFT layout.
    /// Archives the previous ACTIVE layout for the same activation scope.
    /// </summary>
    [HttpPost("layouts/{id:guid}/publish")]
    public async Task<IActionResult> PublishLayout(Guid id)
    {
        var layout = await _db.BadgeLayouts.FindAsync(id);
        if (layout == null)
            return NotFound(new { message = "Layout não encontrado." });

        if (layout.Status != BadgeLayoutStatus.Draft)
            return BadRequest(new { message = "Apenas layouts com status DRAFT podem ser publicados." });

        // Archive previous ACTIVE layouts in the same activation scope
        var previousActive = await _db.BadgeLayouts
            .Where(l => l.Status == BadgeLayoutStatus.Active
                && l.Name == layout.Name
                && l.Id != layout.Id)
            .ToListAsync();

        foreach (var prev in previousActive)
        {
            prev.Status = BadgeLayoutStatus.Archived;
            prev.ArchivedAtUtc = DateTime.UtcNow;
        }

        layout.Status = BadgeLayoutStatus.Active;
        layout.ActivatedAtUtc = DateTime.UtcNow;
        layout.UpdatedByUserId = GetUserId();
        layout.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge layout published: {LayoutId} '{Name}' v{Version}. Archived {Count} previous versions.",
            layout.Id, layout.Name, layout.Version, previousActive.Count);

        return Ok(new { id = layout.Id, archivedCount = previousActive.Count });
    }

    /// <summary>
    /// Create a new DRAFT version from an existing ACTIVE layout.
    /// </summary>
    [HttpPost("layouts/{id:guid}/new-draft")]
    public async Task<IActionResult> CreateDraftFromActive(Guid id)
    {
        var source = await _db.BadgeLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (source == null)
            return NotFound(new { message = "Layout não encontrado." });

        if (source.Status != BadgeLayoutStatus.Active)
            return BadRequest(new { message = "Apenas layouts ACTIVE podem gerar novas versões." });

        // Ensure no existing DRAFT for this layout name
        var existingDraft = await _db.BadgeLayouts
            .AnyAsync(l => l.Name == source.Name && l.Status == BadgeLayoutStatus.Draft);
        if (existingDraft)
            return Conflict(new { message = $"Já existe um rascunho pendente para o layout '{source.Name}'." });

        // Safely increment from the absolute max version of this layout name, even if archived
        var maxVersion = await _db.BadgeLayouts
            .Where(l => l.Name == source.Name)
            .MaxAsync(l => (int?)l.Version) ?? source.Version;

        var draft = new BadgeLayout
        {
            Name = source.Name,
            Description = source.Description,
            Version = maxVersion + 1,
            Status = BadgeLayoutStatus.Draft,
            CompanyCode = source.CompanyCode,
            BadgeType = source.BadgeType,
            PlantCode = source.PlantCode,
            LayoutConfigJson = source.LayoutConfigJson,
            CreatedByUserId = GetUserId()
        };

        _db.BadgeLayouts.Add(draft);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge layout draft created from active: {SourceId} -> {DraftId} '{Name}' v{Version}",
            source.Id, draft.Id, draft.Name, draft.Version);

        return CreatedAtAction(nameof(GetLayout), new { id = draft.Id }, new { id = draft.Id });
    }

    /// <summary>Delete or archive a layout. Hard-deletes if no print history, otherwise soft-deletes (ARCHIVED).</summary>
    [HttpDelete("layouts/{id:guid}")]
    public async Task<IActionResult> ArchiveLayout(Guid id)
    {
        var layout = await _db.BadgeLayouts.FindAsync(id);
        if (layout == null)
            return NotFound(new { message = "Layout não encontrado." });

        bool hasHistory = await _db.BadgePrintHistories.AnyAsync(h => h.BadgeLayoutId == id);

        if (!hasHistory)
        {
            _db.BadgeLayouts.Remove(layout);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Badge layout hard-deleted (no history): {LayoutId} '{Name}' v{Version}", layout.Id, layout.Name, layout.Version);
        }
        else
        {
            layout.Status = BadgeLayoutStatus.Archived;
            layout.ArchivedAtUtc = DateTime.UtcNow;
            layout.UpdatedByUserId = GetUserId();
            layout.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            _logger.LogInformation("Badge layout archived (soft-delete, has history): {LayoutId} '{Name}' v{Version}", layout.Id, layout.Name, layout.Version);
        }

        return NoContent();
    }

    // ═══════════════════════════════════════════════
    // PRINT HISTORY
    // ═══════════════════════════════════════════════

    /// <summary>List print history with pagination and optional filters.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetPrintHistory(
        [FromQuery] string? employeeCode = null,
        [FromQuery] string? companyCode = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _db.BadgePrintHistories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(employeeCode))
            query = query.Where(h => h.EmployeeCode.Contains(employeeCode));

        if (!string.IsNullOrWhiteSpace(companyCode))
            query = query.Where(h => h.CompanyCode == companyCode);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(h => h.PrintedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new PrintHistoryListDto
            {
                Id = h.Id,
                EmployeeCode = h.EmployeeCode,
                EmployeeName = h.EmployeeName,
                Department = h.Department,
                CompanyCode = h.CompanyCode,
                PlantCode = h.PlantCode,
                PhotoSource = h.PhotoSource,
                PrintedAtUtc = h.PrintedAtUtc,
                PrintedByName = h.PrintedByUser != null ? h.PrintedByUser.FullName : null,
                PrintCount = h.PrintCount,
                LayoutName = h.BadgeLayout != null ? h.BadgeLayout.Name : "Padrão V1"
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize });
    }

    /// <summary>Record a new print event (first print).</summary>
    [HttpPost("history")]
    public async Task<IActionResult> RecordPrint([FromBody] RecordPrintDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.EmployeeCode) || string.IsNullOrWhiteSpace(dto.EmployeeName))
            return BadRequest(new { message = "Código e nome do funcionário são obrigatórios." });

        var history = new BadgePrintHistory
        {
            EmployeeCode = dto.EmployeeCode,
            EmployeeName = dto.EmployeeName,
            Department = dto.Department,
            Category = dto.Category,
            CardNumber = dto.CardNumber,
            CompanyCode = dto.CompanyCode,
            PlantCode = dto.PlantCode,
            PhotoSource = dto.PhotoSource ?? BadgePhotoSource.None,
            PhotoReference = dto.PhotoReference,
            SnapshotPayloadJson = dto.SnapshotPayloadJson ?? "{}",
            BadgeLayoutId = dto.BadgeLayoutId,
            PrintedByUserId = GetUserId(),
            PrintCount = 1
        };

        _db.BadgePrintHistories.Add(history);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge print recorded: Employee {Code} by {UserId}", dto.EmployeeCode, history.PrintedByUserId);

        return CreatedAtAction(nameof(GetPrintHistory), null, new { id = history.Id });
    }

    /// <summary>
    /// Record a reprint event.
    /// Creates a BadgePrintEvent audit record AND increments PrintCount.
    /// </summary>
    [HttpPost("history/{id:guid}/reprint")]
    public async Task<IActionResult> RecordReprint(Guid id, [FromBody] ReprintDto? dto = null)
    {
        var history = await _db.BadgePrintHistories.FindAsync(id);
        if (history == null)
            return NotFound(new { message = "Registro de impressão não encontrado." });

        var userId = GetUserId();

        // Create audit event
        var printEvent = new BadgePrintEvent
        {
            BadgePrintHistoryId = history.Id,
            ReprintedByUserId = userId,
            Reason = dto?.Reason
        };

        _db.BadgePrintEvents.Add(printEvent);
        history.PrintCount += 1;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge reprint #{Count} recorded: History {HistoryId} by {UserId}",
            history.PrintCount, history.Id, userId);

        return Ok(new { printCount = history.PrintCount });
    }

    /// <summary>Get snapshot payload for reprint rendering.</summary>
    [HttpGet("history/{id:guid}/snapshot")]
    public async Task<IActionResult> GetSnapshot(Guid id)
    {
        var history = await _db.BadgePrintHistories.AsNoTracking()
            .Where(h => h.Id == id)
            .Select(h => new
            {
                h.SnapshotPayloadJson,
                h.PhotoSource,
                h.PhotoReference,
                LayoutConfigJson = h.BadgeLayout != null ? h.BadgeLayout.LayoutConfigJson : null
            })
            .FirstOrDefaultAsync();

        if (history == null)
            return NotFound(new { message = "Registro não encontrado." });

        return Ok(history);
    }

    /// <summary>Delete a print history record (useful for clearing test prints).</summary>
    [HttpDelete("history/{id:guid}")]
    public async Task<IActionResult> DeletePrintHistory(Guid id)
    {
        var history = await _db.BadgePrintHistories
            .Include(h => h.ReprintEvents)
            .FirstOrDefaultAsync(h => h.Id == id);

        if (history == null)
            return NotFound(new { message = "Registro não encontrado." });

        _db.BadgePrintEvents.RemoveRange(history.ReprintEvents);
        _db.BadgePrintHistories.Remove(history);
        
        await _db.SaveChangesAsync();

        _logger.LogInformation("Badge print history deleted: History {HistoryId} by {UserId}", id, GetUserId());

        return NoContent();
    }
}

// ─── DTOs ───

public record BadgeLayoutListDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? CompanyCode { get; init; }
    public string? BadgeType { get; init; }
    public string? PlantCode { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ActivatedAtUtc { get; init; }
    public string? CreatedByName { get; init; }
    public bool HasHistory { get; init; }
}

public record BadgeLayoutDetailDto : BadgeLayoutListDto
{
    public string LayoutConfigJson { get; init; } = "{}";
    public DateTime? UpdatedAtUtc { get; init; }
    public string? UpdatedByName { get; init; }
}

public record CreateBadgeLayoutDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? CompanyCode { get; init; }
    public string? BadgeType { get; init; }
    public string? PlantCode { get; init; }
    public string? LayoutConfigJson { get; init; }
}

public record UpdateBadgeLayoutDto
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? CompanyCode { get; init; }
    public string? BadgeType { get; init; }
    public string? PlantCode { get; init; }
    public string? LayoutConfigJson { get; init; }
}

public record PrintHistoryListDto
{
    public Guid Id { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string? Department { get; init; }
    public string? CompanyCode { get; init; }
    public string? PlantCode { get; init; }
    public string? PhotoSource { get; init; }
    public DateTime PrintedAtUtc { get; init; }
    public string? PrintedByName { get; init; }
    public int PrintCount { get; init; }
    public string? LayoutName { get; init; }
}

public record RecordPrintDto
{
    public string EmployeeCode { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string? Department { get; init; }
    public string? Category { get; init; }
    public string? CardNumber { get; init; }
    public string? CompanyCode { get; init; }
    public string? PlantCode { get; init; }
    public string? PhotoSource { get; init; }
    public string? PhotoReference { get; init; }
    public string? SnapshotPayloadJson { get; init; }
    public Guid? BadgeLayoutId { get; init; }
}

public record ReprintDto
{
    public string? Reason { get; init; }
}
