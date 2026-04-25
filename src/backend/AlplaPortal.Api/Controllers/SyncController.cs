using System.Text.Json;
using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.DTOs.Sync;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Controlled synchronization of Primavera master data into the Portal.
///
/// Design principles:
/// - One-way: Primavera → Portal only. Portal never writes to Primavera.
/// - Company-scoped: each sync session targets a single Primavera database.
/// - User-reviewed: preview first, then import only selected records.
/// - Create-only (V1): no updates to existing Portal records.
/// - Audit-logged: every import action is traced in AdminLogEntry.
///
/// Matching rules (V2.1 — description-based with duplicate detection):
///   Item Catalog:
///     exact normalized description match → Exists
///     similar description (contains/contained) → Conflict
///     duplicate description in Primavera result set (2+ items) → Conflict
///     duplicate description in Portal catalog (2+ items) → Conflict
///     no description match and no ambiguity → New
///   Normalization: trim, collapse spaces, case-insensitive, accent-insensitive,
///   strip simple punctuation (.,;:/-)
///   Suppliers (unchanged):
///     exact PrimaveraCode → Exists; same TaxId → Conflict;
///     similar Name → Conflict; else → New
/// </summary>
[ApiController]
[Route("api/v1/sync")]
[Authorize(Roles = RoleConstants.SystemAdministrator)]
public class SyncController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPrimaveraArticleService _articleService;
    private readonly IPrimaveraSupplierService _supplierService;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ApplicationDbContext context,
        IPrimaveraArticleService articleService,
        IPrimaveraSupplierService supplierService,
        AdminLogWriter adminLog,
        ILogger<SyncController> logger)
    {
        _context = context;
        _articleService = articleService;
        _supplierService = supplierService;
        _adminLog = adminLog;
        _logger = logger;
    }

    // ─── ITEM CATALOG ─────────────────────────────────────────────────────────

    /// <summary>
    /// Preview: compare Primavera articles against Portal Item Catalog.
    /// Supports server-side search, pagination, and status filtering.
    /// </summary>
    [HttpGet("catalog/preview")]
    public async Task<ActionResult<CatalogSyncPreviewDto>> CatalogPreview(
        [FromQuery] int companyId,
        [FromQuery] string? search = null,
        [FromQuery] string? statusFilter = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var company = ResolveCompany(companyId);
        if (company is null)
            return BadRequest(new { error = "Empresa Primavera inválida." });

        // Parse status filter upfront (supports comma-separated multi-value)
        HashSet<SyncMatchStatus>? statusSet = null;
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            statusSet = statusFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<SyncMatchStatus>(s, true, out var p) ? (SyncMatchStatus?)p : null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToHashSet();

            if (statusSet.Count == 0) statusSet = null;
        }

        try
        {
            // ── Branch: When status filter is active, we must fetch ALL items,
            // match them ALL, filter by status, then paginate the filtered result.
            // This is because status is computed post-match and Primavera cannot
            // filter by it. Without this, pagination would skip items randomly.
            List<PrimaveraArticleDto> primaveraItems;
            int totalPrimaveraCount;

            if (statusSet != null)
            {
                primaveraItems = await _articleService.ListAllArticlesAsync(company.Value, search);
                totalPrimaveraCount = primaveraItems.Count;
            }
            else
            {
                var (pagedItems, totalCount) = await _articleService.ListArticlesAsync(
                    company.Value, search, page, pageSize);
                primaveraItems = pagedItems;
                totalPrimaveraCount = totalCount;
            }

            // Load all Portal items for description-based matching (in-memory).
            var portalItems = await _context.ItemCatalogItems
                .Select(ic => new { ic.Id, ic.Code, ic.Description })
                .ToListAsync();

            // Build lookup indexes keyed by normalized description
            var portalByExactDesc = portalItems
                .Select(p => new { p.Id, p.Code, p.Description, Normalized = NormalizeDescription(p.Description) })
                .Where(p => !string.IsNullOrWhiteSpace(p.Normalized))
                .GroupBy(p => p.Normalized)
                .ToDictionary(g => g.Key, g => g.First());

            // Detect Portal-side duplicates
            var portalDuplicateDescs = portalItems
                .Select(p => NormalizeDescription(p.Description))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .GroupBy(d => d)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            // Pre-compute list for similarity checks
            var portalNormalizedList = portalItems
                .Select(p => new { p.Id, p.Code, p.Description, Normalized = NormalizeDescription(p.Description) })
                .Where(p => p.Normalized.Length >= 5)
                .ToList();

            // Detect Primavera-side duplicates
            var primaveraDuplicateDescs = primaveraItems
                .Select(p => NormalizeDescription(p.Description ?? ""))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .GroupBy(d => d)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            // Build preview items
            var previewItems = new List<CatalogSyncPreviewItemDto>();

            foreach (var pItem in primaveraItems)
            {
                var row = new CatalogSyncPreviewItemDto
                {
                    PrimaveraCode = pItem.Code,
                    PrimaveraDescription = pItem.Description,
                    PrimaveraFamily = pItem.FamilyDescription ?? pItem.Family,
                    PrimaveraBaseUnit = pItem.BaseUnit,
                    PrimaveraIsCancelled = pItem.IsCancelled
                };

                var primDescNorm = NormalizeDescription(pItem.Description ?? "");

                // 1) Exact normalized description match → Exists
                if (!string.IsNullOrWhiteSpace(primDescNorm) &&
                    portalByExactDesc.TryGetValue(primDescNorm, out var exactMatch))
                {
                    row.Status = SyncMatchStatus.Exists;
                    row.PortalItemId = exactMatch.Id;
                    row.PortalCode = exactMatch.Code;
                    row.PortalDescription = exactMatch.Description;
                }
                // 2) Similar description (contains/contained, min 5 chars) → Conflict
                else if (!string.IsNullOrWhiteSpace(primDescNorm) && primDescNorm.Length >= 5)
                {
                    var similar = portalNormalizedList.FirstOrDefault(p =>
                        p.Normalized.Contains(primDescNorm) ||
                        primDescNorm.Contains(p.Normalized));

                    if (similar is not null)
                    {
                        row.Status = SyncMatchStatus.Conflict;
                        row.ConflictDetail = $"Descrição semelhante ao item Portal #{similar.Id} " +
                            $"('{similar.Description}') — verificar manualmente.";
                        row.PortalItemId = similar.Id;
                        row.PortalCode = similar.Code;
                        row.PortalDescription = similar.Description;
                    }
                    // 3) Primavera-side duplicate description → Conflict
                    else if (primaveraDuplicateDescs.Contains(primDescNorm))
                    {
                        row.Status = SyncMatchStatus.Conflict;
                        row.ConflictDetail = "Descrição duplicada no Primavera — revisão manual necessária antes da importação.";
                    }
                    // 4) Portal-side duplicate description → Conflict
                    else if (portalDuplicateDescs.Contains(primDescNorm))
                    {
                        row.Status = SyncMatchStatus.Conflict;
                        row.ConflictDetail = "Descrição duplicada no Portal — revisão manual necessária antes da importação.";
                    }
                    else
                    {
                        row.Status = SyncMatchStatus.New;
                    }
                }
                // Short descriptions (<5 chars) still check for duplicates
                else if (!string.IsNullOrWhiteSpace(primDescNorm) &&
                         primaveraDuplicateDescs.Contains(primDescNorm))
                {
                    row.Status = SyncMatchStatus.Conflict;
                    row.ConflictDetail = "Descrição duplicada no Primavera — revisão manual necessária antes da importação.";
                }
                else
                {
                    row.Status = SyncMatchStatus.New;
                }

                previewItems.Add(row);
            }

            // ── Compute counts from the FULL matched dataset ──
            var allNewCount = previewItems.Count(i => i.Status == SyncMatchStatus.New);
            var allExistsCount = previewItems.Count(i => i.Status == SyncMatchStatus.Exists);
            var allConflictCount = previewItems.Count(i => i.Status == SyncMatchStatus.Conflict);

            // ── Apply status filter + pagination ──
            IEnumerable<CatalogSyncPreviewItemDto> resultItems;
            int resultTotalRecords;

            if (statusSet != null)
            {
                // Status-filtered path: filter the full dataset, then paginate in-memory
                var filtered = previewItems.Where(i => statusSet.Contains(i.Status)).ToList();
                resultTotalRecords = filtered.Count;
                resultItems = filtered
                    .Skip((Math.Max(page, 1) - 1) * pageSize)
                    .Take(pageSize);
            }
            else
            {
                // No status filter: items are already the correct page from Primavera
                resultTotalRecords = totalPrimaveraCount;
                resultItems = previewItems;
            }

            var result = new CatalogSyncPreviewDto
            {
                TotalPrimaveraRecords = resultTotalRecords,
                NewCount = allNewCount,
                ExistsCount = allExistsCount,
                ConflictCount = allConflictCount,
                Items = resultItems.ToList()
            };

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Primavera"))
        {
            _logger.LogWarning(ex, "Primavera connection issue during catalog preview.");
            return StatusCode(503, new { error = "Não foi possível conectar ao Primavera.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Import selected Primavera articles into the Portal Item Catalog.
    /// Create-only V1: only records matched as 'New' are importable.
    /// </summary>
    [HttpPost("catalog/import")]
    public async Task<ActionResult<SyncImportResultDto>> CatalogImport(
        [FromQuery] int companyId,
        [FromBody] SyncImportRequestDto request)
    {
        var company = ResolveCompany(companyId);
        if (company is null)
            return BadRequest(new { error = "Empresa Primavera inválida." });

        if (request.SelectedPrimaveraCodes is not { Count: > 0 })
            return BadRequest(new { error = "Nenhum código selecionado para importação." });

        var result = new SyncImportResultDto();

        try
        {
            // Fetch the selected articles from Primavera
            var primaveraArticles = new List<PrimaveraArticleDto>();
            foreach (var code in request.SelectedPrimaveraCodes)
            {
                var article = await _articleService.GetArticleByCodeAsync(company.Value, code);
                if (article is not null)
                    primaveraArticles.Add(article);
                else
                    result.Errors.Add($"Artigo '{code}' não encontrado no Primavera.");
            }

            // Check which descriptions already exist in Portal (normalized, for dedup).
            var existingDescriptions = await _context.ItemCatalogItems
                .Select(ic => ic.Description)
                .ToListAsync();
            var existingDescSet = new HashSet<string>(
                existingDescriptions
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => NormalizeDescription(d)));

            // Generate next sequential code
            var maxCode = await _context.ItemCatalogItems
                .Where(ic => ic.Code.StartsWith("CAT-"))
                .Select(ic => ic.Code)
                .ToListAsync();
            var nextSeq = maxCode
                .Select(c => int.TryParse(c.Replace("CAT-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var now = DateTime.UtcNow;
            var companyName = company.Value.ToString();

            using var transaction = await _context.Database.BeginTransactionAsync();

            foreach (var article in primaveraArticles)
            {
                var descKey = NormalizeDescription(article.Description ?? article.Code);
                if (existingDescSet.Contains(descKey))
                {
                    result.Skipped++;
                    continue;
                }

                var newItem = new ItemCatalog
                {
                    Code = $"CAT-{nextSeq:D4}",
                    Description = article.Description ?? article.Code,
                    PrimaveraCode = article.Code,
                    SupplierCode = article.SupplierCode,
                    Category = article.FamilyDescription ?? article.Family,
                    Origin = "SYNCED_PRIMAVERA",
                    SourceCompany = companyName,
                    LastSyncedAtUtc = now,
                    IsActive = true,
                    CreatedAtUtc = now
                };

                _context.ItemCatalogItems.Add(newItem);
                existingDescSet.Add(descKey);
                result.Created++;
                nextSeq++;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Audit log
            await _adminLog.WriteAsync("Activity", "Sync", "SYNC_CATALOG_IMPORT",
                $"Catálogo sincronizado: {result.Created} criados, {result.Skipped} ignorados" +
                $" de {request.SelectedPrimaveraCodes.Count} selecionados. Empresa: {companyName}.",
                payload: JsonSerializer.Serialize(new
                {
                    company = companyName,
                    selectedCount = request.SelectedPrimaveraCodes.Count,
                    created = result.Created,
                    skipped = result.Skipped,
                    errors = result.Errors,
                    codes = request.SelectedPrimaveraCodes
                }));

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during catalog sync import. Company: {Company}", company);
            return StatusCode(500, new { error = "Erro interno durante a importação.", detail = ex.Message });
        }
    }

    // ─── SUPPLIERS ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Preview: compare Primavera suppliers against Portal suppliers.
    /// Cautious matching: code match → Exists; NIF match → Conflict; similar name → Conflict.
    /// </summary>
    [HttpGet("suppliers/preview")]
    public async Task<ActionResult<SupplierSyncPreviewDto>> SuppliersPreview(
        [FromQuery] int companyId,
        [FromQuery] string? search = null,
        [FromQuery] string? statusFilter = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var company = ResolveCompany(companyId);
        if (company is null)
            return BadRequest(new { error = "Empresa Primavera inválida." });

        // Parse status filter upfront
        HashSet<SyncMatchStatus>? statusSet = null;
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            statusSet = statusFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<SyncMatchStatus>(s, true, out var p) ? (SyncMatchStatus?)p : null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToHashSet();

            if (statusSet.Count == 0) statusSet = null;
        }

        try
        {
            // ── Branch: full fetch when status filter is active ──
            List<PrimaveraSupplierDto> primaveraSuppliers;
            int totalPrimaveraCount;

            if (statusSet != null)
            {
                primaveraSuppliers = await _supplierService.ListAllSuppliersAsync(company.Value, search);
                totalPrimaveraCount = primaveraSuppliers.Count;
            }
            else
            {
                var (pagedItems, totalCount) = await _supplierService.ListSuppliersAsync(
                    company.Value, search, page, pageSize);
                primaveraSuppliers = pagedItems;
                totalPrimaveraCount = totalCount;
            }

            // Load all Portal suppliers for matching (in-memory)
            var portalSuppliers = await _context.Suppliers
                .Select(s => new { s.Id, s.Name, s.PrimaveraCode, s.TaxId })
                .ToListAsync();

            // Build lookup indexes
            var portalByPrimaveraCode = portalSuppliers
                .Where(s => !string.IsNullOrWhiteSpace(s.PrimaveraCode))
                .GroupBy(s => s.PrimaveraCode!.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            var portalByTaxId = portalSuppliers
                .Where(s => !string.IsNullOrWhiteSpace(s.TaxId))
                .GroupBy(s => s.TaxId!.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            // Normalize Portal names for fuzzy matching
            var portalNormalizedNames = portalSuppliers
                .Select(s => new { s.Id, s.Name, s.PrimaveraCode, s.TaxId, Normalized = NormalizeName(s.Name) })
                .Where(s => !string.IsNullOrWhiteSpace(s.Normalized))
                .ToList();

            // Build preview items
            var previewItems = new List<SupplierSyncPreviewItemDto>();

            foreach (var pSupplier in primaveraSuppliers)
            {
                var row = new SupplierSyncPreviewItemDto
                {
                    PrimaveraCode = pSupplier.Code,
                    PrimaveraName = pSupplier.Name ?? pSupplier.FiscalName,
                    PrimaveraTaxId = pSupplier.TaxId,
                    PrimaveraIsCancelled = pSupplier.IsCancelled
                };

                var codeKey = pSupplier.Code.Trim().ToUpperInvariant();

                // 1) Strong match: exact PrimaveraCode → Exists
                if (portalByPrimaveraCode.TryGetValue(codeKey, out var codeMatch))
                {
                    row.Status = SyncMatchStatus.Exists;
                    row.PortalSupplierId = codeMatch.Id;
                    row.PortalName = codeMatch.Name;
                    row.PortalPrimaveraCode = codeMatch.PrimaveraCode;
                    row.PortalTaxId = codeMatch.TaxId;
                }
                // 2) Conflict: same TaxId/NIF but different PrimaveraCode
                else if (!string.IsNullOrWhiteSpace(pSupplier.TaxId) &&
                         portalByTaxId.TryGetValue(pSupplier.TaxId.Trim().ToUpperInvariant(), out var taxMatch))
                {
                    row.Status = SyncMatchStatus.Conflict;
                    row.ConflictDetail = $"NIF '{pSupplier.TaxId}' já existe no Portal (fornecedor #{taxMatch.Id}: {taxMatch.Name})";
                    row.PortalSupplierId = taxMatch.Id;
                    row.PortalName = taxMatch.Name;
                    row.PortalPrimaveraCode = taxMatch.PrimaveraCode;
                    row.PortalTaxId = taxMatch.TaxId;
                }
                // 3) Suspicious: very similar name
                else
                {
                    var primaveraNameNorm = NormalizeName(pSupplier.Name ?? pSupplier.FiscalName ?? "");
                    var nameConflict = !string.IsNullOrWhiteSpace(primaveraNameNorm)
                        ? portalNormalizedNames.FirstOrDefault(p =>
                            p.Normalized == primaveraNameNorm ||
                            p.Normalized.Contains(primaveraNameNorm) ||
                            primaveraNameNorm.Contains(p.Normalized))
                        : null;

                    if (nameConflict is not null)
                    {
                        row.Status = SyncMatchStatus.Conflict;
                        row.ConflictDetail = $"Nome semelhante ao fornecedor #{nameConflict.Id}: '{nameConflict.Name}'";
                        row.PortalSupplierId = nameConflict.Id;
                        row.PortalName = nameConflict.Name;
                        row.PortalPrimaveraCode = nameConflict.PrimaveraCode;
                        row.PortalTaxId = nameConflict.TaxId;
                    }
                    else
                    {
                        row.Status = SyncMatchStatus.New;
                    }
                }

                previewItems.Add(row);
            }

            // ── Compute counts from the FULL matched dataset ──
            var allNewCount = previewItems.Count(i => i.Status == SyncMatchStatus.New);
            var allExistsCount = previewItems.Count(i => i.Status == SyncMatchStatus.Exists);
            var allConflictCount = previewItems.Count(i => i.Status == SyncMatchStatus.Conflict);

            // ── Apply status filter + pagination ──
            IEnumerable<SupplierSyncPreviewItemDto> resultItems;
            int resultTotalRecords;

            if (statusSet != null)
            {
                var filtered = previewItems.Where(i => statusSet.Contains(i.Status)).ToList();
                resultTotalRecords = filtered.Count;
                resultItems = filtered
                    .Skip((Math.Max(page, 1) - 1) * pageSize)
                    .Take(pageSize);
            }
            else
            {
                resultTotalRecords = totalPrimaveraCount;
                resultItems = previewItems;
            }

            var result = new SupplierSyncPreviewDto
            {
                TotalPrimaveraRecords = resultTotalRecords,
                NewCount = allNewCount,
                ExistsCount = allExistsCount,
                ConflictCount = allConflictCount,
                Items = resultItems.ToList()
            };

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Primavera"))
        {
            _logger.LogWarning(ex, "Primavera connection issue during supplier preview.");
            return StatusCode(503, new { error = "Não foi possível conectar ao Primavera.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Import selected Primavera suppliers into the Portal.
    /// Create-only V1: only records matched as 'New' are importable.
    /// </summary>
    [HttpPost("suppliers/import")]
    public async Task<ActionResult<SyncImportResultDto>> SuppliersImport(
        [FromQuery] int companyId,
        [FromBody] SyncImportRequestDto request)
    {
        var company = ResolveCompany(companyId);
        if (company is null)
            return BadRequest(new { error = "Empresa Primavera inválida." });

        if (request.SelectedPrimaveraCodes is not { Count: > 0 })
            return BadRequest(new { error = "Nenhum código selecionado para importação." });

        var result = new SyncImportResultDto();

        try
        {
            // Fetch the selected suppliers from Primavera
            var primaveraSuppliers = new List<PrimaveraSupplierDto>();
            foreach (var code in request.SelectedPrimaveraCodes)
            {
                var supplier = await _supplierService.GetSupplierByCodeAsync(company.Value, code);
                if (supplier is not null)
                    primaveraSuppliers.Add(supplier);
                else
                    result.Errors.Add($"Fornecedor '{code}' não encontrado no Primavera.");
            }

            // Check existing Portal suppliers
            var existingByCodes = await _context.Set<Supplier>()
                .Where(s => s.PrimaveraCode != null)
                .Select(s => s.PrimaveraCode!)
                .ToListAsync();
            var existingCodeSet = new HashSet<string>(
                existingByCodes.Select(c => c.Trim().ToUpperInvariant()));

            var existingByTaxId = await _context.Set<Supplier>()
                .Where(s => s.TaxId != null && s.TaxId != "")
                .Select(s => s.TaxId!)
                .ToListAsync();
            var existingTaxIdSet = new HashSet<string>(
                existingByTaxId.Select(t => t.Trim().ToUpperInvariant()));

            // Generate next sequential PortalCode
            var maxPortalCode = await _context.Set<Supplier>()
                .Where(s => s.PortalCode != null && s.PortalCode.StartsWith("SUP-"))
                .Select(s => s.PortalCode!)
                .ToListAsync();
            var nextSeq = maxPortalCode
                .Select(c => int.TryParse(c.Replace("SUP-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var now = DateTime.UtcNow;
            var companyName = company.Value.ToString();

            using var transaction = await _context.Database.BeginTransactionAsync();

            foreach (var supplier in primaveraSuppliers)
            {
                var codeKey = supplier.Code.Trim().ToUpperInvariant();

                // Skip if PrimaveraCode already exists
                if (existingCodeSet.Contains(codeKey))
                {
                    result.Skipped++;
                    continue;
                }

                // Skip if TaxId already exists (conflict — don't create duplicate)
                if (!string.IsNullOrWhiteSpace(supplier.TaxId) &&
                    existingTaxIdSet.Contains(supplier.TaxId.Trim().ToUpperInvariant()))
                {
                    result.Skipped++;
                    result.Errors.Add($"Fornecedor '{supplier.Code}' ignorado: NIF '{supplier.TaxId}' já existe no Portal.");
                    continue;
                }

                var newSupplier = new Supplier
                {
                    Name = supplier.Name ?? supplier.FiscalName ?? supplier.Code,
                    PortalCode = $"SUP-{nextSeq:D4}",
                    PrimaveraCode = supplier.Code,
                    TaxId = supplier.TaxId,
                    Origin = "SYNCED_PRIMAVERA",
                    SourceCompany = companyName,
                    LastSyncedAtUtc = now,
                    IsActive = true
                };

                _context.Set<Supplier>().Add(newSupplier);
                existingCodeSet.Add(codeKey);
                if (!string.IsNullOrWhiteSpace(supplier.TaxId))
                    existingTaxIdSet.Add(supplier.TaxId.Trim().ToUpperInvariant());
                result.Created++;
                nextSeq++;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Audit log
            await _adminLog.WriteAsync("Activity", "Sync", "SYNC_SUPPLIER_IMPORT",
                $"Fornecedores sincronizados: {result.Created} criados, {result.Skipped} ignorados" +
                $" de {request.SelectedPrimaveraCodes.Count} selecionados. Empresa: {companyName}.",
                payload: JsonSerializer.Serialize(new
                {
                    company = companyName,
                    selectedCount = request.SelectedPrimaveraCodes.Count,
                    created = result.Created,
                    skipped = result.Skipped,
                    errors = result.Errors,
                    codes = request.SelectedPrimaveraCodes
                }));

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during supplier sync import. Company: {Company}", company);
            return StatusCode(500, new { error = "Erro interno durante a importação.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Import reviewed suppliers into the Portal.
    /// V2 endpoint: accepts user-edited data (Name, TaxId, Notes) from the review modal.
    /// All suppliers are created as DRAFT with Origin = SYNCED_PRIMAVERA.
    /// The existing /import endpoint remains unchanged for backward compatibility.
    /// </summary>
    [HttpPost("suppliers/import-reviewed")]
    public async Task<ActionResult<SyncImportResultDto>> SuppliersImportReviewed(
        [FromQuery] int companyId,
        [FromBody] SyncSupplierReviewedImportRequestDto request)
    {
        var company = ResolveCompany(companyId);
        if (company is null)
            return BadRequest(new { error = "Empresa Primavera inválida." });

        if (request.Suppliers is not { Count: > 0 })
            return BadRequest(new { error = "Nenhum fornecedor enviado para importação." });

        // Validate required fields
        var invalidRows = request.Suppliers
            .Where(s => string.IsNullOrWhiteSpace(s.PrimaveraCode) || string.IsNullOrWhiteSpace(s.Name))
            .ToList();
        if (invalidRows.Count > 0)
            return BadRequest(new { error = $"{invalidRows.Count} fornecedor(es) sem código Primavera ou nome." });

        var result = new SyncImportResultDto();

        try
        {
            // Check existing Portal suppliers for duplicate detection
            var existingByCodes = await _context.Set<Supplier>()
                .Where(s => s.PrimaveraCode != null)
                .Select(s => s.PrimaveraCode!)
                .ToListAsync();
            var existingCodeSet = new HashSet<string>(
                existingByCodes.Select(c => c.Trim().ToUpperInvariant()));

            var existingByTaxId = await _context.Set<Supplier>()
                .Where(s => s.TaxId != null && s.TaxId != "")
                .Select(s => s.TaxId!)
                .ToListAsync();
            var existingTaxIdSet = new HashSet<string>(
                existingByTaxId.Select(t => t.Trim().ToUpperInvariant()));

            // Generate next sequential PortalCode
            var maxPortalCode = await _context.Set<Supplier>()
                .Where(s => s.PortalCode != null && s.PortalCode.StartsWith("SUP-"))
                .Select(s => s.PortalCode!)
                .ToListAsync();
            var nextSeq = maxPortalCode
                .Select(c => int.TryParse(c.Replace("SUP-", ""), out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var now = DateTime.UtcNow;
            var companyName = company.Value.ToString();

            using var transaction = await _context.Database.BeginTransactionAsync();

            foreach (var reviewed in request.Suppliers)
            {
                var codeKey = reviewed.PrimaveraCode.Trim().ToUpperInvariant();

                // Skip if PrimaveraCode already exists in Portal
                if (existingCodeSet.Contains(codeKey))
                {
                    result.Skipped++;
                    result.Errors.Add($"Fornecedor '{reviewed.PrimaveraCode}' ignorado: código Primavera já existe no Portal.");
                    continue;
                }

                // Skip if TaxId already exists (duplicate NIF)
                var reviewedTaxId = reviewed.TaxId?.Trim();
                if (!string.IsNullOrWhiteSpace(reviewedTaxId) &&
                    existingTaxIdSet.Contains(reviewedTaxId.ToUpperInvariant()))
                {
                    result.Skipped++;
                    result.Errors.Add($"Fornecedor '{reviewed.PrimaveraCode}' ignorado: NIF '{reviewedTaxId}' já existe no Portal.");
                    continue;
                }

                var newSupplier = new Supplier
                {
                    Name = reviewed.Name.Trim(),
                    PortalCode = $"SUP-{nextSeq:D4}",
                    PrimaveraCode = reviewed.PrimaveraCode.Trim(),
                    TaxId = string.IsNullOrWhiteSpace(reviewedTaxId) ? null : reviewedTaxId,
                    Origin = "SYNCED_PRIMAVERA",
                    SourceCompany = companyName,
                    LastSyncedAtUtc = now,
                    IsActive = true,
                    RegistrationStatus = SupplierConstants.Statuses.Draft,
                    Notes = string.IsNullOrWhiteSpace(reviewed.Notes) ? null : reviewed.Notes.Trim()
                };

                _context.Set<Supplier>().Add(newSupplier);
                existingCodeSet.Add(codeKey);
                if (!string.IsNullOrWhiteSpace(reviewedTaxId))
                    existingTaxIdSet.Add(reviewedTaxId.ToUpperInvariant());
                result.Created++;
                nextSeq++;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Audit log
            await _adminLog.WriteAsync("Activity", "Sync", "SYNC_SUPPLIER_IMPORT_REVIEWED",
                $"Fornecedores importados (revisados): {result.Created} criados, {result.Skipped} ignorados" +
                $" de {request.Suppliers.Count} enviados. Empresa: {companyName}.",
                payload: JsonSerializer.Serialize(new
                {
                    company = companyName,
                    totalSent = request.Suppliers.Count,
                    created = result.Created,
                    skipped = result.Skipped,
                    errors = result.Errors,
                    codes = request.Suppliers.Select(s => s.PrimaveraCode).ToList()
                }));

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reviewed supplier sync import. Company: {Company}", company);
            return StatusCode(500, new { error = "Erro interno durante a importação.", detail = ex.Message });
        }
    }

    // ─── HELPERS ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps companyId (Portal Company.Id) to PrimaveraCompany enum.
    /// Convention: Company ID 1 → ALPLAPLASTICO, Company ID 2 → ALPLASOPRO.
    /// Falls back to trying the ID directly as an enum value.
    /// </summary>
    private static PrimaveraCompany? ResolveCompany(int companyId)
    {
        return companyId switch
        {
            1 => PrimaveraCompany.ALPLAPLASTICO,
            2 => PrimaveraCompany.ALPLASOPRO,
            _ => Enum.IsDefined(typeof(PrimaveraCompany), companyId)
                ? (PrimaveraCompany)companyId
                : null
        };
    }

    /// <summary>
    /// Normalize supplier name for fuzzy comparison.
    /// Strips common legal suffixes, punctuation, and extra whitespace.
    /// </summary>
    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var normalized = name.Trim().ToUpperInvariant();

        // Remove common legal entity suffixes
        var suffixes = new[] { " LDA", " LDA.", " S.A.", " SA", " UNIPESSOAL", " LIMITADA", " - ", ",", "." };
        foreach (var suffix in suffixes)
            normalized = normalized.Replace(suffix, " ");

        // Collapse multiple spaces
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        return normalized.Trim();
    }

    /// <summary>
    /// Normalize item description for catalog sync comparison.
    ///
    /// Normalization rules applied (in order):
    /// 1. Trim leading/trailing whitespace
    /// 2. Convert to uppercase (case-insensitive)
    /// 3. Strip diacritics/accents (e.g., ã→A, é→E, ç→C)
    /// 4. Remove simple punctuation: . , ; : / -
    /// 5. Collapse repeated whitespace into single space
    /// 6. Final trim
    ///
    /// This produces a stable, deterministic string for equality comparison.
    /// </summary>
    private static string NormalizeDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        // 1-2: Trim + uppercase
        var normalized = description.Trim().ToUpperInvariant();

        // 3: Strip diacritics (accent-insensitive)
        // Decompose into base char + combining marks, then remove the marks.
        normalized = new string(
            normalized.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray());

        // 4: Remove simple punctuation
        foreach (var ch in new[] { '.', ',', ';', ':', '/', '-' })
            normalized = normalized.Replace(ch.ToString(), " ");

        // 5: Collapse multiple spaces
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        // 6: Final trim
        return normalized.Trim();
    }
}
