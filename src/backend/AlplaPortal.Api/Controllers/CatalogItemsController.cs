using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Dedicated controller for Item Catalog management.
/// Separated from LookupsController to avoid controller bloat.
/// Supports CRUD, search/autocomplete, and bulk import for SharePoint seed.
/// </summary>
[ApiController]
[Route("api/v1/catalog-items")]
[Authorize]
public class CatalogItemsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CatalogItemsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ─── DTOs ──────────────────────────────────────────────────────────────

    public class CatalogItemResponseDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? PrimaveraCode { get; set; }
        public string? SupplierCode { get; set; }
        public int? DefaultUnitId { get; set; }
        public string? DefaultUnitCode { get; set; }
        public string? DefaultUnitName { get; set; }
        public string? Category { get; set; }
        public string Origin { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }

    public class CreateCatalogItemDto
    {
        [Required(ErrorMessage = "A descrição é obrigatória.")]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? PrimaveraCode { get; set; }

        [MaxLength(100)]
        public string? SupplierCode { get; set; }

        public int? DefaultUnitId { get; set; }

        [MaxLength(200)]
        public string? Category { get; set; }
    }

    public class UpdateCatalogItemDto
    {
        [Required(ErrorMessage = "A descrição é obrigatória.")]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? PrimaveraCode { get; set; }

        [MaxLength(100)]
        public string? SupplierCode { get; set; }

        public int? DefaultUnitId { get; set; }

        [MaxLength(200)]
        public string? Category { get; set; }
    }

    public class ImportCatalogItemDto
    {
        [Required]
        public string Description { get; set; } = string.Empty;
        public string? PrimaveraCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? Unit { get; set; }
        public string? Category { get; set; }
    }

    public class ImportResultDto
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>Request DTO for batch-matching OCR descriptions against catalog items.</summary>
    public class BatchMatchRequestDto
    {
        /// <summary>List of OCR-extracted item descriptions to match.</summary>
        public List<string> Descriptions { get; set; } = new();
    }

    /// <summary>Response DTO for batch-match results.</summary>
    public class BatchMatchResultDto
    {
        /// <summary>Dictionary mapping input index → matched catalog item (null if no match).</summary>
        public Dictionary<int, CatalogItemResponseDto?> Matches { get; set; } = new();
    }

    /// <summary>DTO for creating catalog items via the reconciliation flow.</summary>
    public class ReconciliationCreateCatalogItemDto
    {
        [Required(ErrorMessage = "A descrição é obrigatória.")]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public int? DefaultUnitId { get; set; }

        [MaxLength(100)]
        public string? SupplierCode { get; set; }

        [MaxLength(200)]
        public string? Category { get; set; }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Safe normalization applied identically to BOTH OCR input and DB records.
    /// Steps: trim → lowercase → remove diacritics/accents → collapse whitespace → strip trailing punctuation.
    /// This ensures exact comparison is reliable regardless of formatting differences.
    /// </summary>
    private static string NormalizeDescription(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return string.Empty;

        // 1. Trim and lowercase
        var normalized = desc.Trim().ToLowerInvariant();

        // 2. Remove diacritics/accents (e.g., ç→c, ã→a, é→e)
        normalized = new string(
            normalized.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray()
        ).Normalize(NormalizationForm.FormC);

        // 3. Collapse multiple whitespace into single space
        normalized = Regex.Replace(normalized, @"\s+", " ");

        // 4. Strip trailing punctuation (e.g., "item." → "item")
        normalized = normalized.TrimEnd('.', ',', ';', ':', '!');

        return normalized;
    }

    // ─── Endpoints ─────────────────────────────────────────────────────────

    /// <summary>List all catalog items with optional inactive filter.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false, [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        var query = _context.ItemCatalogItems
            .Include(ic => ic.DefaultUnit)
            .AsNoTracking();

        if (!includeInactive)
            query = query.Where(ic => ic.IsActive);

        if (!string.IsNullOrEmpty(search))
        {
            var normalizedQ = search.Trim().ToLower();
            query = query.Where(ic => 
                ic.Code.ToLower().Contains(normalizedQ) || 
                ic.Description.ToLower().Contains(normalizedQ) ||
                (ic.PrimaveraCode != null && ic.PrimaveraCode.ToLower().Contains(normalizedQ)) ||
                (ic.SupplierCode != null && ic.SupplierCode.ToLower().Contains(normalizedQ)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(ic => ic.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ic => new CatalogItemResponseDto
            {
                Id = ic.Id,
                Code = ic.Code,
                Description = ic.Description,
                PrimaveraCode = ic.PrimaveraCode,
                SupplierCode = ic.SupplierCode,
                DefaultUnitId = ic.DefaultUnitId,
                DefaultUnitCode = ic.DefaultUnit != null ? ic.DefaultUnit.Code : null,
                DefaultUnitName = ic.DefaultUnit != null ? ic.DefaultUnit.Name : null,
                Category = ic.Category,
                Origin = ic.Origin,
                IsActive = ic.IsActive,
                CreatedAtUtc = ic.CreatedAtUtc,
                UpdatedAtUtc = ic.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(new Application.DTOs.Common.PagedResult<CatalogItemResponseDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>Search catalog items by code or description. Used for autocomplete in request forms.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q = "", [FromQuery] int take = 20)
    {
        var normalizedQ = (q ?? "").Trim().ToLower();
        
        var query = _context.ItemCatalogItems
            .Include(ic => ic.DefaultUnit)
            .Where(ic => ic.IsActive)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(normalizedQ))
        {
            query = query.Where(ic => 
                ic.Code.ToLower().Contains(normalizedQ) || 
                ic.Description.ToLower().Contains(normalizedQ) ||
                (ic.PrimaveraCode != null && ic.PrimaveraCode.ToLower().Contains(normalizedQ)) ||
                (ic.SupplierCode != null && ic.SupplierCode.ToLower().Contains(normalizedQ)));
        }

        var items = await query
            .OrderBy(ic => ic.Code)
            .Take(take)
            .Select(ic => new CatalogItemResponseDto
            {
                Id = ic.Id,
                Code = ic.Code,
                Description = ic.Description,
                PrimaveraCode = ic.PrimaveraCode,
                SupplierCode = ic.SupplierCode,
                DefaultUnitId = ic.DefaultUnitId,
                DefaultUnitCode = ic.DefaultUnit != null ? ic.DefaultUnit.Code : null,
                DefaultUnitName = ic.DefaultUnit != null ? ic.DefaultUnit.Name : null,
                Category = ic.Category,
                Origin = ic.Origin,
                IsActive = ic.IsActive,
                CreatedAtUtc = ic.CreatedAtUtc,
                UpdatedAtUtc = ic.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>Create a new catalog item. Admin only.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCatalogItemDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var counter = await _context.SystemCounters.FirstOrDefaultAsync(c => c.Id == "ITEM_CATALOG_COUNTER");
        if (counter == null)
        {
            counter = new SystemCounter { Id = "ITEM_CATALOG_COUNTER", CurrentValue = 1, LastUpdatedUtc = DateTime.UtcNow };
            _context.SystemCounters.Add(counter);
        }
        else
        {
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
        }
        
        var normalizedCode = $"ITM-{counter.CurrentValue:D5}";

        // Validate DefaultUnitId if provided
        if (dto.DefaultUnitId.HasValue)
        {
            var unit = await _context.Units.FindAsync(dto.DefaultUnitId.Value);
            if (unit == null)
                return BadRequest("Unidade padrão inválida.");
        }

        var item = new ItemCatalog
        {
            Code = normalizedCode,
            Description = dto.Description.Trim(),
            PrimaveraCode = dto.PrimaveraCode?.Trim(),
            SupplierCode = dto.SupplierCode?.Trim(),
            DefaultUnitId = dto.DefaultUnitId,
            Category = dto.Category?.Trim(),
            Origin = "MANUAL",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.ItemCatalogItems.Add(item);
        await _context.SaveChangesAsync();

        // Reload with navigation properties
        await _context.Entry(item).Reference(i => i.DefaultUnit).LoadAsync();

        return Ok(new CatalogItemResponseDto
        {
            Id = item.Id,
            Code = item.Code,
            Description = item.Description,
            PrimaveraCode = item.PrimaveraCode,
            SupplierCode = item.SupplierCode,
            DefaultUnitId = item.DefaultUnitId,
            DefaultUnitCode = item.DefaultUnit?.Code,
            DefaultUnitName = item.DefaultUnit?.Name,
            Category = item.Category,
            Origin = item.Origin,
            IsActive = item.IsActive,
            CreatedAtUtc = item.CreatedAtUtc,
            UpdatedAtUtc = item.UpdatedAtUtc
        });
    }

    /// <summary>Update an existing catalog item. Admin only.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCatalogItemDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var item = await _context.ItemCatalogItems.FindAsync(id);
        if (item == null) return NotFound("Item de catálogo não encontrado.");

        // Validate DefaultUnitId if provided
        if (dto.DefaultUnitId.HasValue)
        {
            var unit = await _context.Units.FindAsync(dto.DefaultUnitId.Value);
            if (unit == null)
                return BadRequest("Unidade padrão inválida.");
        }

        item.Description = dto.Description.Trim();
        item.PrimaveraCode = dto.PrimaveraCode?.Trim();
        item.SupplierCode = dto.SupplierCode?.Trim();
        item.DefaultUnitId = dto.DefaultUnitId;
        item.Category = dto.Category?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Toggle active/inactive status. Admin only.</summary>
    [HttpPut("{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var item = await _context.ItemCatalogItems.FindAsync(id);
        if (item == null) return NotFound("Item de catálogo não encontrado.");

        item.IsActive = !item.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { item.Id, item.IsActive });
    }

    /// <summary>
    /// Bulk import catalog items from JSON array.
    /// Intended for one-time SharePoint seed or administrative bulk load.
    /// Skips items with duplicate codes. Origin is set to IMPORTED_SHAREPOINT.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] List<ImportCatalogItemDto> items)
    {
        if (items == null || !items.Any())
            return BadRequest("Nenhum item para importar.");

        var units = await _context.Units.AsNoTracking().ToListAsync();

        var result = new ImportResultDto();
        var newItems = new List<ItemCatalog>();
        
        var counter = await _context.SystemCounters.FirstOrDefaultAsync(c => c.Id == "ITEM_CATALOG_COUNTER");
        if (counter == null)
        {
            counter = new SystemCounter { Id = "ITEM_CATALOG_COUNTER", CurrentValue = 0, LastUpdatedUtc = DateTime.UtcNow };
            _context.SystemCounters.Add(counter);
        }

        foreach (var dto in items)
        {
            if (string.IsNullOrWhiteSpace(dto.Description))
            {
                result.Errors.Add($"Item ignorado: descrição vazia.");
                result.Skipped++;
                continue;
            }

            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
            var normalizedCode = $"ITM-{counter.CurrentValue:D5}";

            // Try to match unit by code
            int? unitId = null;
            if (!string.IsNullOrWhiteSpace(dto.Unit))
            {
                var unitMatch = units.FirstOrDefault(u => 
                    u.Code.Equals(dto.Unit.Trim(), StringComparison.OrdinalIgnoreCase));
                unitId = unitMatch?.Id;
            }

            var newItem = new ItemCatalog
            {
                Code = normalizedCode,
                Description = dto.Description.Trim(),
                PrimaveraCode = dto.PrimaveraCode?.Trim(),
                SupplierCode = dto.SupplierCode?.Trim(),
                DefaultUnitId = unitId,
                Category = dto.Category?.Trim(),
                Origin = "IMPORTED_SHAREPOINT",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            newItems.Add(newItem);
            result.Imported++;
        }

        if (newItems.Any())
        {
            _context.ItemCatalogItems.AddRange(newItems);
            await _context.SaveChangesAsync();
        }

        return Ok(result);
    }

    /// <summary>
    /// Create a catalog item from the reconciliation flow.
    /// Sets Origin = CREATED_PENDING_VALIDATION and IsActive = true.
    /// Items created this way are immediately usable but flagged for admin review.
    /// </summary>
    [HttpPost("reconciliation-create")]
    public async Task<IActionResult> ReconciliationCreate([FromBody] ReconciliationCreateCatalogItemDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        // Duplicate check: prevent creating items with identical descriptions
        var normalizedDesc = dto.Description.Trim().ToLower();
        var existingItem = await _context.ItemCatalogItems
            .AsNoTracking()
            .FirstOrDefaultAsync(ic => ic.Description.ToLower() == normalizedDesc && ic.IsActive);

        if (existingItem != null)
        {
            // Return the existing item instead of creating a duplicate
            await _context.Entry(existingItem).Reference(i => i.DefaultUnit).LoadAsync();
            return Ok(new CatalogItemResponseDto
            {
                Id = existingItem.Id,
                Code = existingItem.Code,
                Description = existingItem.Description,
                PrimaveraCode = existingItem.PrimaveraCode,
                SupplierCode = existingItem.SupplierCode,
                DefaultUnitId = existingItem.DefaultUnitId,
                DefaultUnitCode = existingItem.DefaultUnit?.Code,
                DefaultUnitName = existingItem.DefaultUnit?.Name,
                Category = existingItem.Category,
                Origin = existingItem.Origin,
                IsActive = existingItem.IsActive,
                CreatedAtUtc = existingItem.CreatedAtUtc,
                UpdatedAtUtc = existingItem.UpdatedAtUtc
            });
        }

        var counter = await _context.SystemCounters.FirstOrDefaultAsync(c => c.Id == "ITEM_CATALOG_COUNTER");
        if (counter == null)
        {
            counter = new SystemCounter { Id = "ITEM_CATALOG_COUNTER", CurrentValue = 1, LastUpdatedUtc = DateTime.UtcNow };
            _context.SystemCounters.Add(counter);
        }
        else
        {
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
        }

        var normalizedCode = $"ITM-{counter.CurrentValue:D5}";

        // Validate DefaultUnitId if provided
        if (dto.DefaultUnitId.HasValue)
        {
            var unit = await _context.Units.FindAsync(dto.DefaultUnitId.Value);
            if (unit == null)
                return BadRequest("Unidade padrão inválida.");
        }

        var item = new ItemCatalog
        {
            Code = normalizedCode,
            Description = dto.Description.Trim(),
            PrimaveraCode = null,
            SupplierCode = dto.SupplierCode?.Trim(),
            DefaultUnitId = dto.DefaultUnitId,
            Category = dto.Category?.Trim(),
            Origin = "CREATED_PENDING_VALIDATION",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.ItemCatalogItems.Add(item);
        await _context.SaveChangesAsync();

        // Reload with navigation properties
        await _context.Entry(item).Reference(i => i.DefaultUnit).LoadAsync();

        return Ok(new CatalogItemResponseDto
        {
            Id = item.Id,
            Code = item.Code,
            Description = item.Description,
            PrimaveraCode = item.PrimaveraCode,
            SupplierCode = item.SupplierCode,
            DefaultUnitId = item.DefaultUnitId,
            DefaultUnitCode = item.DefaultUnit?.Code,
            DefaultUnitName = item.DefaultUnit?.Name,
            Category = item.Category,
            Origin = item.Origin,
            IsActive = item.IsActive,
            CreatedAtUtc = item.CreatedAtUtc,
            UpdatedAtUtc = item.UpdatedAtUtc
        });
    }

    /// <summary>
    /// Batch-match OCR-extracted item descriptions against the catalog.
    /// Uses safe in-memory normalization on BOTH input descriptions and DB records
    /// to ensure reliable exact matching regardless of accents, whitespace, or punctuation.
    /// 
    /// Returns a dictionary mapping each input index to its matched catalog item (or null).
    /// Only returns active items. Only exact normalized matches are considered.
    /// </summary>
    [HttpPost("batch-match")]
    public async Task<IActionResult> BatchMatch([FromBody] BatchMatchRequestDto dto)
    {
        if (dto.Descriptions == null || dto.Descriptions.Count == 0)
            return Ok(new BatchMatchResultDto { Matches = new() });

        // Cap at 100 descriptions per request to prevent abuse
        var descriptions = dto.Descriptions.Take(100).ToList();

        // 1. Normalize all incoming OCR descriptions
        var normalizedInputs = descriptions
            .Select((d, i) => new { Index = i, Original = d, Normalized = NormalizeDescription(d) })
            .Where(x => !string.IsNullOrEmpty(x.Normalized))
            .ToList();

        if (normalizedInputs.Count == 0)
            return Ok(new BatchMatchResultDto { Matches = new() });

        // 2. Fetch ALL active catalog items (with their units) in a single DB query.
        //    We normalize DB descriptions in memory (C#) to guarantee identical normalization
        //    on both sides — SQL-level normalization would be unreliable for accent removal.
        var allCatalogItems = await _context.ItemCatalogItems
            .Include(ic => ic.DefaultUnit)
            .Where(ic => ic.IsActive)
            .AsNoTracking()
            .Select(ic => new
            {
                ic.Id,
                ic.Code,
                ic.Description,
                ic.PrimaveraCode,
                ic.SupplierCode,
                ic.DefaultUnitId,
                DefaultUnitCode = ic.DefaultUnit != null ? ic.DefaultUnit.Code : null,
                DefaultUnitName = ic.DefaultUnit != null ? ic.DefaultUnit.Name : null,
                ic.Category,
                ic.Origin,
                ic.IsActive,
                ic.CreatedAtUtc,
                ic.UpdatedAtUtc
            })
            .ToListAsync();

        // 3. Build a lookup dictionary: normalized DB description → catalog item
        //    If multiple items share the same normalized description, take the first active one.
        var catalogLookup = new Dictionary<string, CatalogItemResponseDto>();
        foreach (var item in allCatalogItems)
        {
            var normalizedDbDesc = NormalizeDescription(item.Description);
            if (!string.IsNullOrEmpty(normalizedDbDesc) && !catalogLookup.ContainsKey(normalizedDbDesc))
            {
                catalogLookup[normalizedDbDesc] = new CatalogItemResponseDto
                {
                    Id = item.Id,
                    Code = item.Code,
                    Description = item.Description,
                    PrimaveraCode = item.PrimaveraCode,
                    SupplierCode = item.SupplierCode,
                    DefaultUnitId = item.DefaultUnitId,
                    DefaultUnitCode = item.DefaultUnitCode,
                    DefaultUnitName = item.DefaultUnitName,
                    Category = item.Category,
                    Origin = item.Origin,
                    IsActive = item.IsActive,
                    CreatedAtUtc = item.CreatedAtUtc,
                    UpdatedAtUtc = item.UpdatedAtUtc
                };
            }
        }

        // 4. Match each input against the lookup
        var matches = new Dictionary<int, CatalogItemResponseDto?>();
        foreach (var input in normalizedInputs)
        {
            catalogLookup.TryGetValue(input.Normalized, out var matched);
            matches[input.Index] = matched; // null if no match
        }

        return Ok(new BatchMatchResultDto { Matches = matches });
    }
}
