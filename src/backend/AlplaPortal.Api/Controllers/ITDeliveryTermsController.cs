using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/it/delivery-terms")]
public class ITDeliveryTermsController : BaseController
{
    private readonly IEmailService _emailService;
    private readonly ITEquipmentPdfService _pdfService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ITDeliveryTermsController> _logger;
    private readonly string _storagePath;

    // ── Upload security constants ──
    private static readonly HashSet<string> AllowedSignedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx"
    };
    private const long MaxSignedDocumentSizeBytes = 20 * 1024 * 1024; // 20 MB

    public ITDeliveryTermsController(
        ApplicationDbContext context,
        IWebHostEnvironment env,
        IEmailService emailService,
        ITEquipmentPdfService pdfService,
        IConfiguration configuration,
        ILogger<ITDeliveryTermsController> logger) : base(context)
    {
        _emailService = emailService;
        _pdfService = pdfService;
        _configuration = configuration;
        _logger = logger;

        // Resolve storage path
        var storageRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, configuration, "ITEquipment:StoragePath", Path.Combine("data", "attachments", "it-equipment"));

        _storagePath = storageRes.ResolvedPath;

        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    private bool HasITAccess() =>
        CurrentUserRoles.Contains(RoleConstants.IT) ||
        CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);

    // ═══════════════════════════════════════════════════════════════
    //  LIST / DETAIL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>GET /api/it/delivery-terms</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? plant,
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? sortBy,
        [FromQuery] bool isDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!HasITAccess()) return Forbid();

        var query = _context.ITEquipmentDeliveryTerms
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .AsQueryable();

        // ── Filters ──
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(t =>
                t.TermNumber.ToLower().Contains(s) ||
                t.EmployeeName.ToLower().Contains(s) ||
                (t.EmployeeEmail != null && t.EmployeeEmail.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(plant))
            query = query.Where(t => t.EmployeePlant == plant);

        if (DateTime.TryParse(dateFrom, out var from))
            query = query.Where(t => t.DeliveryDate >= from);

        if (DateTime.TryParse(dateTo, out var to))
            query = query.Where(t => t.DeliveryDate <= to.Date.AddDays(1));

        // ── Sort ──
        query = sortBy?.ToLower() switch
        {
            "termnumber" => isDescending ? query.OrderByDescending(t => t.TermNumber) : query.OrderBy(t => t.TermNumber),
            "employeename" => isDescending ? query.OrderByDescending(t => t.EmployeeName) : query.OrderBy(t => t.EmployeeName),
            "deliverydate" => isDescending ? query.OrderByDescending(t => t.DeliveryDate) : query.OrderBy(t => t.DeliveryDate),
            "status" => isDescending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.TermNumber,
                t.EmployeeName,
                t.EmployeeEmail,
                t.EmployeePlant,
                t.DeliveryDate,
                t.Status,
                statusDisplay = ITEquipmentConstants.DeliveryTermStatus.DisplayName(t.Status),
                itemCount = t.Items.Count(),
                t.CreatedAt,
                createdByName = t.CreatedByUser != null ? t.CreatedByUser.FullName : null
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize });
    }

    /// <summary>GET /api/it/delivery-terms/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .AsNoTracking()
            .Include(t => t.Items)
                .ThenInclude(i => i.Equipment)
            .Include(t => t.Items)
                .ThenInclude(i => i.Assignment)
            .Include(t => t.CreatedByUser)
            .Include(t => t.UpdatedByUser)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        return Ok(new
        {
            term.Id,
            term.TermNumber,
            term.EmployeeName,
            term.EmployeeEmail,
            term.EmployeeUserId,
            term.EmployeeDepartment,
            term.EmployeePosition,
            term.EmployeePlant,
            term.DeliveryDate,
            term.Status,
            statusDisplay = ITEquipmentConstants.DeliveryTermStatus.DisplayName(term.Status),
            term.GeneratedDocumentId,
            term.SignedDocumentId,
            term.ReturnDocumentId,
            term.Notes,
            term.CreatedAt,
            createdByName = term.CreatedByUser?.FullName,
            term.UpdatedAt,
            updatedByName = term.UpdatedByUser?.FullName,
            items = term.Items.Select(i => new
            {
                i.Id,
                i.EquipmentId,
                i.AssignmentId,
                i.ItemStatus,
                itemStatusDisplay = i.ItemStatus switch
                {
                    "PENDING" => "Pendente",
                    "DELIVERED" => "Entregue",
                    "RETURNED" => "Devolvido",
                    "REPLACED" => "Substituído",
                    "LOST" => "Perdido",
                    "RETIRED" => "Baixado",
                    _ => i.ItemStatus
                },
                i.DeliveredAt,
                i.ReturnedAt,
                i.ReturnCondition,
                returnConditionDisplay = i.ReturnCondition != null
                    ? ITEquipmentConstants.ReturnCondition.DisplayName(i.ReturnCondition)
                    : null,
                i.Notes,
                equipment = i.Equipment != null ? new
                {
                    i.Equipment.Id,
                    i.Equipment.AssetTag,
                    i.Equipment.Hostname,
                    i.Equipment.EquipmentType,
                    i.Equipment.Manufacturer,
                    i.Equipment.Model,
                    i.Equipment.SerialNumber,
                    i.Equipment.StatusCode,
                    statusDisplay = ITEquipmentConstants.EquipmentStatus.DisplayName(i.Equipment.StatusCode),
                    i.Equipment.CurrentOwnerName
                } : null
            }).ToList()
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  DRAFT OPERATIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms — Create a new draft delivery term.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDeliveryTermRequest request)
    {
        if (!HasITAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(request.EmployeeName))
            return BadRequest(new { detail = "Nome do funcionário é obrigatório." });

        if (request.DeliveryDate == default)
            return BadRequest(new { detail = "Data de entrega é obrigatória." });

        var termNumber = await GenerateTermNumberAsync();

        var term = new ITEquipmentDeliveryTerm
        {
            TermNumber = termNumber,
            EmployeeName = request.EmployeeName.Trim(),
            EmployeeEmail = request.EmployeeEmail?.Trim(),
            EmployeeUserId = request.EmployeeUserId,
            EmployeeDepartment = request.EmployeeDepartment?.Trim(),
            EmployeePosition = request.EmployeePosition?.Trim(),
            EmployeePlant = request.EmployeePlant?.Trim(),
            CompanyId = request.CompanyId,
            EmployeePlantId = request.EmployeePlantId,
            EmployeeDepartmentId = request.EmployeeDepartmentId,
            DeliveryDate = request.DeliveryDate,
            Status = ITEquipmentConstants.DeliveryTermStatus.Draft,
            Notes = request.Notes?.Trim(),
            CreatedByUserId = CurrentUserId
        };

        // Resolve official names from Master Data FKs
        if (request.EmployeePlantId.HasValue)
        {
            var plant = await _context.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.EmployeePlantId.Value);
            if (plant != null) term.EmployeePlant = plant.Name;
        }
        if (request.EmployeeDepartmentId.HasValue)
        {
            var dept = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.EmployeeDepartmentId.Value);
            if (dept != null) term.EmployeeDepartment = dept.Name;
        }

        _context.ITEquipmentDeliveryTerms.Add(term);

        // Optionally add equipment items if provided
        if (request.EquipmentIds?.Any() == true)
        {
            var validationError = await ValidateAndAddItemsAsync(term, request.EquipmentIds);
            if (validationError != null) return validationError;
        }

        // Save with retry for term number collision (unique index guard)
        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _context.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (attempt < maxRetries && IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning("TermNumber collision for {TermNumber}, regenerating (attempt {Attempt})", term.TermNumber, attempt + 1);
                term.TermNumber = await GenerateTermNumberAsync();
            }
        }

        return Ok(new { id = term.Id, termNumber = term.TermNumber });
    }

    /// <summary>PUT /api/it/delivery-terms/{id} — Update draft term info.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeliveryTermRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms.FindAsync(id);
        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Draft)
            return BadRequest(new { detail = "Apenas termos em rascunho podem ser editados." });

        if (!string.IsNullOrWhiteSpace(request.EmployeeName))
            term.EmployeeName = request.EmployeeName.Trim();
        if (request.EmployeeEmail != null)
            term.EmployeeEmail = request.EmployeeEmail.Trim();
        if (request.EmployeeUserId.HasValue)
            term.EmployeeUserId = request.EmployeeUserId;
        if (request.EmployeeDepartment != null)
            term.EmployeeDepartment = request.EmployeeDepartment.Trim();
        if (request.EmployeePosition != null)
            term.EmployeePosition = request.EmployeePosition.Trim();
        if (request.EmployeePlant != null)
            term.EmployeePlant = request.EmployeePlant.Trim();
        if (request.CompanyId.HasValue)
            term.CompanyId = request.CompanyId;
        if (request.EmployeePlantId.HasValue)
        {
            term.EmployeePlantId = request.EmployeePlantId;
            var plant = await _context.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.EmployeePlantId.Value);
            if (plant != null) term.EmployeePlant = plant.Name;
        }
        if (request.EmployeeDepartmentId.HasValue)
        {
            term.EmployeeDepartmentId = request.EmployeeDepartmentId;
            var dept = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.EmployeeDepartmentId.Value);
            if (dept != null) term.EmployeeDepartment = dept.Name;
        }
        if (request.DeliveryDate.HasValue)
            term.DeliveryDate = request.DeliveryDate.Value;
        if (request.Notes != null)
            term.Notes = request.Notes.Trim();

        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();

        return Ok(new { detail = "Termo de entrega atualizado." });
    }

    /// <summary>POST /api/it/delivery-terms/{id}/items — Add equipment items to a draft term.</summary>
    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItems(Guid id, [FromBody] AddItemsRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Draft)
            return BadRequest(new { detail = "Itens só podem ser adicionados a termos em rascunho." });

        if (request.EquipmentIds == null || !request.EquipmentIds.Any())
            return BadRequest(new { detail = "Nenhum equipamento selecionado." });

        var validationError = await ValidateAndAddItemsAsync(term, request.EquipmentIds);
        if (validationError != null) return validationError;

        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();

        return Ok(new { detail = $"{request.EquipmentIds.Count} equipamento(s) adicionado(s)." });
    }

    /// <summary>DELETE /api/it/delivery-terms/{id}/items/{itemId} — Remove item from draft term.</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Draft)
            return BadRequest(new { detail = "Itens só podem ser removidos de termos em rascunho." });

        var item = term.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound(new { detail = "Item não encontrado neste termo." });

        _context.ITEquipmentDeliveryItems.Remove(item);
        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();

        return Ok(new { detail = "Item removido do termo." });
    }

    // ═══════════════════════════════════════════════════════════════
    //  CONFIRM DELIVERY & GENERATE PDF (ATOMIC)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms/{id}/generate — Atomic: assign all items + generate PDF.</summary>
    [HttpPost("{id:guid}/generate")]
    public async Task<IActionResult> GenerateAndConfirm(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        using var tx = await _context.Database.BeginTransactionAsync();
        ITEquipmentDeliveryTerm? term = null;
        try
        {
            term = await _context.ITEquipmentDeliveryTerms
                .Include(t => t.Items)
                    .ThenInclude(i => i.Equipment)
                        .ThenInclude(e => e!.Acquisition)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (term == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { detail = "Termo de entrega não encontrado." });
            }

            if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Draft)
            {
                await tx.RollbackAsync();
                return BadRequest(new { detail = "Apenas termos em rascunho podem ser confirmados." });
            }

            if (!term.Items.Any())
            {
                await tx.RollbackAsync();
                return BadRequest(new { detail = "O termo precisa ter pelo menos um equipamento." });
            }

            // Validate all items are still available
            var unavailable = new List<string>();
            foreach (var item in term.Items)
            {
                var eq = item.Equipment!;
                if (eq.StatusCode != ITEquipmentConstants.EquipmentStatus.Available &&
                    eq.StatusCode != ITEquipmentConstants.EquipmentStatus.Returned)
                {
                    unavailable.Add($"{eq.AssetTag} ({ITEquipmentConstants.EquipmentStatus.DisplayName(eq.StatusCode)})");
                }
            }

            if (unavailable.Any())
            {
                await tx.RollbackAsync();
                return Conflict(new
                {
                    detail = "Os seguintes equipamentos não estão disponíveis para atribuição:",
                    unavailableItems = unavailable
                });
            }

            // Get current user info for the PDF
            var currentUser = await _context.Users.FindAsync(CurrentUserId);
            var now = DateTime.UtcNow;

            // Assign all equipment
            foreach (var item in term.Items)
            {
                var eq = item.Equipment!;

                // Create assignment
                var assignment = new ITEquipmentAssignment
                {
                    EquipmentId = eq.Id,
                    AssignedToUserId = term.EmployeeUserId,
                    AssignedToName = term.EmployeeName,
                    AssignedToEmail = term.EmployeeEmail,
                    AssignedToDepartment = term.EmployeeDepartment,
                    AssignedToPlant = term.EmployeePlant,
                    AssignedDate = term.DeliveryDate,
                    AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Active,
                    Notes = $"Atribuído via termo de entrega {term.TermNumber}",
                    CreatedByUserId = CurrentUserId
                };
                _context.ITEquipmentAssignments.Add(assignment);

                // Link assignment to delivery item
                item.AssignmentId = assignment.Id;
                item.ItemStatus = ITEquipmentConstants.DeliveryItemStatus.Delivered;
                item.DeliveredAt = now;

                // Capture actual previous status before updating
                var previousStatus = eq.StatusCode;

                // Update equipment
                eq.StatusCode = ITEquipmentConstants.EquipmentStatus.InUse;
                eq.CurrentOwnerName = term.EmployeeName;
                eq.CurrentOwnerUserId = term.EmployeeUserId;
                eq.UpdatedAt = now;
                eq.UpdatedByUserId = CurrentUserId;

                // Movement log
                _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                {
                    EquipmentId = eq.Id,
                    MovementType = ITEquipmentConstants.MovementType.DeliveryTermAssigned,
                    PreviousStatus = previousStatus,
                    NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                    NewOwnerName = term.EmployeeName,
                    Notes = $"Equipamento atribuído a {term.EmployeeName} através do termo de entrega {term.TermNumber}.",
                    CreatedByUserId = CurrentUserId
                });
            }

            // Generate multi-equipment PDF
            var pdfData = new ITEquipmentPdfService.DeliveryTermData
            {
                TermNumber = term.TermNumber,
                DeliveryDate = term.DeliveryDate,
                EmployeeName = term.EmployeeName,
                EmployeeEmail = term.EmployeeEmail ?? "—",
                Department = term.EmployeeDepartment ?? "—",
                Position = term.EmployeePosition ?? "—",
                Plant = term.EmployeePlant ?? "—",
                DeliveredByName = currentUser?.FullName ?? "Sistema",
                DeliveredByEmail = currentUser?.Email ?? "—",
                Notes = term.Notes,
                Equipment = term.Items.Select(i => new ITEquipmentPdfService.DeliveryTermEquipmentItem
                {
                    EquipmentType = i.Equipment!.EquipmentType,
                    AssetTag = i.Equipment.AssetTag,
                    Hostname = i.Equipment.Hostname,
                    Manufacturer = i.Equipment.Manufacturer,
                    Model = i.Equipment.Model,
                    SerialNumber = i.Equipment.SerialNumber,
                    Notes = i.Notes,
                    // Purchase traceability — null acquisition treated as unavailable
                    PurchaseAmount = i.Equipment.Acquisition?.PurchaseAmount,
                    Currency = i.Equipment.Acquisition?.Currency,
                    AcquisitionDate = i.Equipment.Acquisition?.AcquisitionDate,
                    InvoiceNumber = i.Equipment.Acquisition?.InvoiceNumber,
                    PurchaseInfoUnavailable = i.Equipment.Acquisition?.PurchaseInfoUnavailable ?? true
                }).ToList()
            };

            var pdfResult = await _pdfService.GenerateDeliveryTermPdfAsync(pdfData);

            // Store PDF as ITEquipmentDocument (linked to first equipment for FK compatibility)
            var firstEquipmentId = term.Items.First().EquipmentId;
            var doc = new ITEquipmentDocument
            {
                EquipmentId = firstEquipmentId,
                DeliveryTermId = term.Id,
                DocumentType = ITEquipmentConstants.DocumentType.DeliveryTermAgreement,
                FileName = pdfResult.DisplayFileName,
                StorageReference = pdfResult.StorageFileName,
                FileHash = pdfResult.FileHash,
                UploadedByUserId = CurrentUserId,
                Notes = $"Termo de entrega agrupado {term.TermNumber} — {term.Items.Count} equipamento(s)"
            };
            _context.ITEquipmentDocuments.Add(doc);

            term.GeneratedDocumentId = doc.Id;
            term.Status = ITEquipmentConstants.DeliveryTermStatus.Generated;
            term.UpdatedAt = now;
            term.UpdatedByUserId = CurrentUserId;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // IT notification (post-commit, failure-safe)
            _ = NotifyITAsync(term.TermNumber,
                "Termo de Entrega Confirmado",
                BuildTermNotificationHtml(term, "Termo de entrega confirmado e PDF gerado.", currentUser?.FullName ?? "Sistema"));

            return Ok(new
            {
                detail = $"Termo {term.TermNumber} confirmado. {term.Items.Count} equipamento(s) atribuído(s).",
                documentId = doc.Id,
                termNumber = term.TermNumber
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            
            var traceId = HttpContext.TraceIdentifier;

            var structuredLog = new
            {
                Action = "GenerateAndConfirm",
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                RequestId = traceId,
                DeliveryTermId = id,
                TermStatusBefore = term?.Status,
                ItemCount = term?.Items.Count ?? 0,
                EmployeeEmail = term?.EmployeeEmail,
                UserId = CurrentUserId,
                EquipmentDetails = term?.Items?.Select(i => new
                {
                    DeliveryItemId = i.Id,
                    EquipmentId = i.EquipmentId,
                    EquipmentStatusBefore = i.Equipment?.StatusCode,
                    ItemStatusBefore = i.ItemStatus
                }).ToList()
            };

            if (ex is FileNotFoundException)
            {
                _logger.LogError(ex, "PDF generation failed due to missing template/policy file. Structured Data: {@LogData}", structuredLog);
            }
            else
            {
                _logger.LogError(ex, "Failed to confirm delivery term {TermId}. Structured Data: {@LogData}", id, structuredLog);
            }

            return StatusCode(500, new { 
                detail = "Não foi possível confirmar a entrega porque ocorreu um erro ao gerar o PDF ou atualizar os registros. Verifique o log técnico para mais detalhes.",
                requestId = traceId
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SEND TO EMPLOYEE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms/{id}/send — Email generated PDF to employee.</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> SendToEmployee(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.GeneratedDocument)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Generated &&
            term.Status != ITEquipmentConstants.DeliveryTermStatus.Sent)
            return BadRequest(new { detail = "O termo precisa estar gerado para ser enviado." });

        if (string.IsNullOrWhiteSpace(term.EmployeeEmail))
            return BadRequest(new { detail = "E-mail do funcionário não informado. Impossível enviar." });

        if (term.GeneratedDocument == null)
            return BadRequest(new { detail = "Documento gerado não encontrado." });

        // Find the physical file
        var storagePath = ResolveStoragePath(term.GeneratedDocument.StorageReference);
        if (!System.IO.File.Exists(storagePath))
            return BadRequest(new { detail = "Ficheiro do documento não encontrado no servidor." });

        bool sent = false;
        try
        {
            sent = await _emailService.SendWithAttachmentAsync(
                term.EmployeeEmail,
                term.EmployeeName,
                $"Termo de Entrega de Equipamento — {term.TermNumber}",
                $"Termo de Entrega — {term.TermNumber}",
                $"Prezado(a) {term.EmployeeName},<br><br>" +
                $"Segue em anexo o Termo de Entrega e Responsabilidade de Equipamento de T.I referente ao termo <strong>{term.TermNumber}</strong>.<br><br>" +
                $"Por favor, revise e assine o documento conforme as instruções internas.<br><br>" +
                $"Atenciosamente,<br>Departamento de T.I — Alpla Angola",
                storagePath,
                term.GeneratedDocument.FileName,
                requiredAttachment: true);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "[ITDeliveryTerms] Missing Required Email Attachment | Env: {Env} | ReqId: {TraceId} | UserId: {UserId} | Flow: {Flow} | ExpectedPath: {Path}", 
                _configuration["AppEnvironment:Code"] ?? "UNKNOWN", HttpContext.TraceIdentifier, CurrentUserId, "Send Delivery Term Email", storagePath);
            return StatusCode(500, new { detail = "Falha técnica: O ficheiro PDF gerado não foi encontrado no servidor." });
        }

        if (sent)
        {
            term.Status = ITEquipmentConstants.DeliveryTermStatus.Sent;
            term.UpdatedAt = DateTime.UtcNow;
            term.UpdatedByUserId = CurrentUserId;
            await _context.SaveChangesAsync();

            var currentUser = await _context.Users.FindAsync(CurrentUserId);
            _ = NotifyITAsync(term.TermNumber,
                "Termo de Entrega Enviado",
                BuildTermNotificationHtml(term, $"Termo enviado por e-mail para {term.EmployeeEmail}.", currentUser?.FullName ?? "Sistema"));

            return Ok(new { detail = $"Termo enviado para {term.EmployeeEmail}." });
        }

        return StatusCode(500, new { detail = "Falha ao enviar o e-mail. Tente novamente." });
    }

    // ═══════════════════════════════════════════════════════════════
    //  UPLOAD SIGNED DOCUMENT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms/{id}/upload-signed — Upload signed document.</summary>
    [HttpPost("{id:guid}/upload-signed")]
    public async Task<IActionResult> UploadSigned(Guid id, [FromForm] IFormFile file)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        var validStatuses = new[]
        {
            ITEquipmentConstants.DeliveryTermStatus.Generated,
            ITEquipmentConstants.DeliveryTermStatus.Sent
        };
        if (!validStatuses.Contains(term.Status))
            return BadRequest(new { detail = "O upload do documento assinado só é permitido para termos gerados ou enviados." });

        if (file == null || file.Length == 0)
            return BadRequest(new { detail = "Nenhum ficheiro enviado." });

        // Validate file size
        if (file.Length > MaxSignedDocumentSizeBytes)
            return BadRequest(new { detail = $"O ficheiro excede o tamanho máximo permitido de {MaxSignedDocumentSizeBytes / (1024 * 1024)} MB." });

        // Validate file extension
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !AllowedSignedDocumentExtensions.Contains(extension))
            return BadRequest(new { detail = "Tipo de ficheiro não permitido. Envie PDF, JPG, PNG, DOC ou DOCX." });

        // Store file
        var storageFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_storagePath, storageFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var doc = new ITEquipmentDocument
        {
            EquipmentId = term.Items.First().EquipmentId,
            DeliveryTermId = term.Id,
            DocumentType = ITEquipmentConstants.DocumentType.SignedDeliveryTermAgreement,
            FileName = file.FileName,
            StorageReference = storageFileName,
            UploadedByUserId = CurrentUserId,
            Notes = $"Documento assinado para o termo {term.TermNumber}"
        };
        _context.ITEquipmentDocuments.Add(doc);

        // If replacing an existing signed doc, keep the old one (immutable history)
        if (term.SignedDocumentId.HasValue)
        {
            _logger.LogInformation("Replacing signed document for term {TermNumber}. Old doc ID: {OldDocId}", term.TermNumber, term.SignedDocumentId);
        }

        term.SignedDocumentId = doc.Id;
        term.Status = ITEquipmentConstants.DeliveryTermStatus.Signed;
        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;

        // Add movement log to each equipment
        foreach (var item in term.Items.Where(i => i.ItemStatus == ITEquipmentConstants.DeliveryItemStatus.Delivered))
        {
            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = item.EquipmentId,
                MovementType = ITEquipmentConstants.MovementType.DeliveryTermSignedUploaded,
                Notes = $"Documento assinado carregado para o termo de entrega {term.TermNumber}.",
                CreatedByUserId = CurrentUserId
            });
        }

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(CurrentUserId);
        _ = NotifyITAsync(term.TermNumber,
            "Documento Assinado Carregado",
            BuildTermNotificationHtml(term, "Documento assinado carregado com sucesso.", user?.FullName ?? "Sistema"));

        return Ok(new { detail = "Documento assinado carregado com sucesso.", documentId = doc.Id });
    }

    // ═══════════════════════════════════════════════════════════════
    //  PARTIAL RETURN
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms/{id}/items/{itemId}/return — Return one item.</summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/return")]
    public async Task<IActionResult> ReturnItem(Guid id, Guid itemId, [FromBody] ReturnItemRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.Items)
                .ThenInclude(i => i.Equipment)
            .Include(t => t.Items)
                .ThenInclude(i => i.Assignment)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        var item = term.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound(new { detail = "Item não encontrado neste termo." });

        if (item.ItemStatus != ITEquipmentConstants.DeliveryItemStatus.Delivered)
            return BadRequest(new { detail = "Apenas itens entregues podem ser devolvidos." });

        var now = DateTime.UtcNow;
        var eq = item.Equipment!;
        var previousOwner = eq.CurrentOwnerName;

        // Update delivery item
        item.ItemStatus = ITEquipmentConstants.DeliveryItemStatus.Returned;
        item.ReturnedAt = request.ReturnDate ?? now;
        item.ReturnCondition = request.ReturnCondition ?? ITEquipmentConstants.ReturnCondition.Good;
        item.Notes = request.Notes;

        // Close assignment
        if (item.Assignment != null)
        {
            item.Assignment.AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Returned;
            item.Assignment.ReturnedDate = item.ReturnedAt;
        }

        // Update equipment status based on condition
        var previousStatus = eq.StatusCode;
        eq.StatusCode = item.ReturnCondition switch
        {
            ITEquipmentConstants.ReturnCondition.Good => ITEquipmentConstants.EquipmentStatus.Available,
            ITEquipmentConstants.ReturnCondition.Damaged => ITEquipmentConstants.EquipmentStatus.InRepair,
            ITEquipmentConstants.ReturnCondition.NeedsRepair => ITEquipmentConstants.EquipmentStatus.InRepair,
            _ => ITEquipmentConstants.EquipmentStatus.Available
        };

        eq.CurrentOwnerName = null;
        eq.CurrentOwnerUserId = null;
        eq.CurrentOwnerEmployeeId = null;
        eq.UpdatedAt = now;
        eq.UpdatedByUserId = CurrentUserId;

        // Equipment movement log
        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = eq.Id,
            MovementType = ITEquipmentConstants.MovementType.DeliveryTermReturned,
            PreviousStatus = previousStatus,
            NewStatus = eq.StatusCode,
            PreviousOwnerName = previousOwner,
            Notes = $"Equipamento devolvido do termo de entrega {term.TermNumber}. " +
                         $"Condição: {ITEquipmentConstants.ReturnCondition.DisplayName(item.ReturnCondition!)}.",
            CreatedByUserId = CurrentUserId
        });

        // Recalculate term status
        var deliveredCount = term.Items.Count(i => i.ItemStatus == ITEquipmentConstants.DeliveryItemStatus.Delivered);
        if (deliveredCount == 0)
        {
            term.Status = ITEquipmentConstants.DeliveryTermStatus.Closed;
        }
        else
        {
            var totalReturned = term.Items.Count(i => i.ItemStatus == ITEquipmentConstants.DeliveryItemStatus.Returned);
            if (totalReturned > 0)
                term.Status = ITEquipmentConstants.DeliveryTermStatus.PartiallyReturned;
        }

        term.UpdatedAt = now;
        term.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(CurrentUserId);

        // ── Auto-generate Return Term when all items are returned ──
        if (term.Status == ITEquipmentConstants.DeliveryTermStatus.Closed
            && !term.ReturnDocumentId.HasValue) // Idempotency: skip if already generated
        {
            try
            {
                var returnTermData = new ITEquipmentPdfService.ReturnTermData
                {
                    OriginalTermNumber = term.TermNumber,
                    ReturnDate = now,
                    EmployeeName = term.EmployeeName,
                    EmployeeEmail = term.EmployeeEmail ?? "—",
                    Department = term.EmployeeDepartment ?? "—",
                    Plant = term.EmployeePlant ?? "—",
                    ReceivedByName = user?.FullName ?? "Sistema",
                    ReceivedByEmail = user?.Email ?? "—",
                    Notes = term.Notes,
                    Equipment = term.Items
                        .Where(i => i.ItemStatus == ITEquipmentConstants.DeliveryItemStatus.Returned)
                        .Select(i => new ITEquipmentPdfService.ReturnTermEquipmentItem
                        {
                            EquipmentType = i.Equipment?.EquipmentType ?? "—",
                            AssetTag = i.Equipment?.AssetTag ?? "—",
                            Hostname = i.Equipment?.Hostname,
                            Manufacturer = i.Equipment?.Manufacturer,
                            Model = i.Equipment?.Model,
                            SerialNumber = i.Equipment?.SerialNumber,
                            ReturnCondition = i.ReturnCondition,
                            Notes = i.Notes
                        })
                        .ToList()
                };

                var pdfResult = await _pdfService.GenerateReturnTermPdfAsync(returnTermData);

                var returnDoc = new ITEquipmentDocument
                {
                    EquipmentId = term.Items.First().EquipmentId,
                    DeliveryTermId = term.Id,
                    DocumentType = ITEquipmentConstants.DocumentType.ReturnTermAgreement,
                    FileName = pdfResult.DisplayFileName,
                    StorageReference = pdfResult.StorageFileName,
                    FileHash = pdfResult.FileHash,
                    UploadedByUserId = CurrentUserId,
                    Notes = $"Termo de devolução agrupado para o termo {term.TermNumber} — {returnTermData.Equipment.Count} equipamento(s)"
                };
                _context.ITEquipmentDocuments.Add(returnDoc);

                term.ReturnDocumentId = returnDoc.Id;
                term.UpdatedAt = DateTime.UtcNow;

                // Movement logs for return document generation
                foreach (var retItem in term.Items.Where(i => i.ItemStatus == ITEquipmentConstants.DeliveryItemStatus.Returned))
                {
                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = retItem.EquipmentId,
                        MovementType = ITEquipmentConstants.MovementType.ReturnTermDocGenerated,
                        Notes = $"Termo de devolução gerado para o termo {term.TermNumber}.",
                        CreatedByUserId = CurrentUserId
                    });
                }

                await _context.SaveChangesAsync();

                // Send email with PDF attachment to IT
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var itEmail = _configuration["AppConfig:ITNotificationEmail"];
                        if (string.IsNullOrWhiteSpace(itEmail)) return;

                        var frontendBaseUrl = _configuration["AppConfig:FrontendBaseUrl"]?.TrimEnd('/') ?? "";
                        var termLink = $"{frontendBaseUrl}/it/delivery-terms";

                        var bodyHtml = $@"
                            <p>Prezado(a) Departamento de T.I,</p>
                            <p>Todos os equipamentos do termo de entrega <strong>{term.TermNumber}</strong> foram devolvidos.</p>
                            <table style='border-collapse:collapse;width:100%;font-size:14px;margin:16px 0'>
                                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Nº do Termo de Entrega</td><td style='padding:6px 12px'>{term.TermNumber}</td></tr>
                                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Funcionário</td><td style='padding:6px 12px'>{term.EmployeeName}</td></tr>
                                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>E-mail</td><td style='padding:6px 12px'>{term.EmployeeEmail ?? "—"}</td></tr>
                                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Itens Devolvidos</td><td style='padding:6px 12px'>{returnTermData.Equipment.Count}</td></tr>
                                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Recebido por</td><td style='padding:6px 12px'>{user?.FullName ?? "Sistema"}</td></tr>
                                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Data/Hora</td><td style='padding:6px 12px'>{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</td></tr>
                            </table>
                            <p>O Termo de Devolução está em anexo. Por favor:</p>
                            <ol>
                                <li>Imprima o documento ou envie digitalmente ao funcionário.</li>
                                <li>Recolha a assinatura do funcionário (ex-utilizador).</li>
                                <li>Carregue o documento assinado no portal.</li>
                            </ol>
                            <p><a href='{termLink}'>Abrir Termos de Entrega no Portal</a></p>";

                        var storagePath = Path.Combine(_storagePath, pdfResult.StorageFileName);

                        await _emailService.SendWithAttachmentAsync(
                            itEmail, "Departamento de T.I",
                            $"Termo de Devolução — {term.TermNumber}",
                            $"Devolução Completa — {term.TermNumber}",
                            bodyHtml,
                            storagePath,
                            pdfResult.DisplayFileName,
                            requiredAttachment: true);
                    }
                    catch (FileNotFoundException ex)
                    {
                        _logger.LogError(ex, "[ITDeliveryTerms] Missing Required Email Attachment | Env: {Env} | UserId: {UserId} | Flow: {Flow} | ExpectedPath: {Path}", 
                            _configuration["AppEnvironment:Code"] ?? "UNKNOWN", CurrentUserId, "Send Return Term Email to IT", Path.Combine(_storagePath, pdfResult.StorageFileName));
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogWarning(emailEx, "Failed to send return term email for term {TermNumber}", term.TermNumber);
                    }
                });

                _logger.LogInformation("Return term PDF generated and stored for term {TermNumber}", term.TermNumber);
            }
            catch (Exception ex)
            {
                // Return term PDF generation failure should not block the main return operation
                _logger.LogError(ex, "Failed to generate return term PDF for term {TermNumber}", term.TermNumber);
            }
        }

        var action = term.Status == ITEquipmentConstants.DeliveryTermStatus.Closed
            ? "Termo encerrado — todos os itens devolvidos."
            : $"Equipamento {eq.AssetTag} devolvido.";
        _ = NotifyITAsync(term.TermNumber, "Item Devolvido", BuildTermNotificationHtml(term, action, user?.FullName ?? "Sistema"));

        return Ok(new
        {
            detail = $"Equipamento {eq.AssetTag} devolvido com sucesso.",
            termStatus = term.Status,
            termStatusDisplay = ITEquipmentConstants.DeliveryTermStatus.DisplayName(term.Status)
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  RETURN DOCUMENT DOWNLOAD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>GET /api/it/delivery-terms/{id}/return-document — Download the generated return term PDF.</summary>
    [HttpGet("{id:guid}/return-document")]
    public async Task<IActionResult> DownloadReturnDocument(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.ReturnDocument)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });
        if (term.ReturnDocument == null) return NotFound(new { detail = "Termo de devolução ainda não gerado." });

        var storagePath = ResolveStoragePath(term.ReturnDocument.StorageReference);
        if (!System.IO.File.Exists(storagePath))
            return NotFound(new { detail = "Ficheiro do documento não encontrado no servidor." });

        var bytes = await System.IO.File.ReadAllBytesAsync(storagePath);
        return File(bytes, "application/pdf", term.ReturnDocument.FileName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  UPLOAD SIGNED RETURN DOCUMENT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms/{id}/upload-signed-return — Upload a signed return document.</summary>
    [HttpPost("{id:guid}/upload-signed-return")]
    public async Task<IActionResult> UploadSignedReturn(Guid id, [FromForm] IFormFile file)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Closed)
            return BadRequest(new { detail = "O upload do documento de devolução assinado só é permitido para termos encerrados." });

        if (!term.ReturnDocumentId.HasValue)
            return BadRequest(new { detail = "O termo de devolução ainda não foi gerado." });

        if (file == null || file.Length == 0)
            return BadRequest(new { detail = "Nenhum ficheiro enviado." });

        if (file.Length > MaxSignedDocumentSizeBytes)
            return BadRequest(new { detail = $"O ficheiro excede o tamanho máximo permitido de {MaxSignedDocumentSizeBytes / (1024 * 1024)} MB." });

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !AllowedSignedDocumentExtensions.Contains(extension))
            return BadRequest(new { detail = "Tipo de ficheiro não permitido. Envie PDF, JPG, PNG, DOC ou DOCX." });

        // Store file
        var storageFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_storagePath, storageFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var doc = new ITEquipmentDocument
        {
            EquipmentId = term.Items.First().EquipmentId,
            DeliveryTermId = term.Id,
            DocumentType = ITEquipmentConstants.DocumentType.SignedReturnTermAgreement,
            FileName = file.FileName,
            StorageReference = storageFileName,
            UploadedByUserId = CurrentUserId,
            Notes = $"Documento de devolução assinado para o termo {term.TermNumber}"
        };
        _context.ITEquipmentDocuments.Add(doc);

        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;

        // Add movement log to each returned equipment
        foreach (var item in term.Items.Where(i => i.ItemStatus == ITEquipmentConstants.DeliveryItemStatus.Returned))
        {
            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = item.EquipmentId,
                MovementType = ITEquipmentConstants.MovementType.ReturnTermEmailSent,
                Notes = $"Documento de devolução assinado carregado para o termo {term.TermNumber}.",
                CreatedByUserId = CurrentUserId
            });
        }

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(CurrentUserId);
        _ = NotifyITAsync(term.TermNumber,
            "Documento de Devolução Assinado",
            BuildTermNotificationHtml(term, "Documento de devolução assinado carregado com sucesso.", user?.FullName ?? "Sistema"));

        return Ok(new { detail = "Documento de devolução assinado carregado com sucesso.", documentId = doc.Id });
    }

    // ═══════════════════════════════════════════════════════════════
    //  CANCEL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>POST /api/it/delivery-terms/{id}/cancel — Cancel a draft term.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term == null) return NotFound(new { detail = "Termo de entrega não encontrado." });

        // Only allow cancellation for DRAFT terms in the first version
        if (term.Status != ITEquipmentConstants.DeliveryTermStatus.Draft)
            return BadRequest(new { detail = "Apenas termos em rascunho podem ser cancelados. Termos já confirmados devem ser tratados manualmente." });

        term.Status = ITEquipmentConstants.DeliveryTermStatus.Cancelled;
        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = CurrentUserId;

        // Remove pending items (not yet assigned)
        foreach (var item in term.Items.ToList())
        {
            _context.ITEquipmentDeliveryItems.Remove(item);
        }

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(CurrentUserId);
        _ = NotifyITAsync(term.TermNumber, "Termo de Entrega Cancelado",
            BuildTermNotificationHtml(term, "Termo de entrega cancelado.", user?.FullName ?? "Sistema"));

        return Ok(new { detail = "Termo de entrega cancelado." });
    }

    // ═══════════════════════════════════════════════════════════════
    //  DOCUMENT DOWNLOAD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>GET /api/it/delivery-terms/{id}/document — Download generated PDF.</summary>
    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> DownloadDocument(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.GeneratedDocument)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term?.GeneratedDocument == null)
            return NotFound(new { detail = "Documento não encontrado." });

        var filePath = ResolveStoragePath(term.GeneratedDocument.StorageReference);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { detail = "Ficheiro não encontrado no servidor." });

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/pdf", term.GeneratedDocument.FileName);
    }

    /// <summary>GET /api/it/delivery-terms/{id}/signed-document — Download signed PDF.</summary>
    [HttpGet("{id:guid}/signed-document")]
    public async Task<IActionResult> DownloadSignedDocument(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var term = await _context.ITEquipmentDeliveryTerms
            .Include(t => t.SignedDocument)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (term?.SignedDocument == null)
            return NotFound(new { detail = "Documento assinado não encontrado." });

        var filePath = ResolveStoragePath(term.SignedDocument.StorageReference);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { detail = "Ficheiro não encontrado no servidor." });

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/octet-stream", term.SignedDocument.FileName);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a unique term number: TER-YYYY-NNNNN.
    /// Uses MAX sequence detection + unique index retry for safety.
    /// </summary>
    private async Task<string> GenerateTermNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"TER-{year}-";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            // Find the highest existing sequence for this year
            var lastTermNumber = await _context.ITEquipmentDeliveryTerms
                .Where(t => t.TermNumber.StartsWith(prefix))
                .OrderByDescending(t => t.TermNumber)
                .Select(t => t.TermNumber)
                .FirstOrDefaultAsync();

            int nextSeq = 1;
            if (lastTermNumber != null)
            {
                var seqPart = lastTermNumber.Substring(prefix.Length);
                if (int.TryParse(seqPart, out var lastSeq))
                    nextSeq = lastSeq + 1;
            }

            var candidate = $"{prefix}{nextSeq:D5}";

            // Check for uniqueness before saving (the unique index is the final guard)
            var exists = await _context.ITEquipmentDeliveryTerms.AnyAsync(t => t.TermNumber == candidate);
            if (!exists) return candidate;

            _logger.LogWarning("Term number collision for {Candidate}, retrying (attempt {Attempt})", candidate, attempt + 1);
        }

        // Fallback: use timestamp-based suffix
        var fallback = $"{prefix}{DateTime.UtcNow:HHmmss}";
        _logger.LogWarning("Using fallback term number {Fallback}", fallback);
        return fallback;
    }

    /// <summary>Validate equipment availability and add items to the term.</summary>
    private async Task<IActionResult?> ValidateAndAddItemsAsync(ITEquipmentDeliveryTerm term, List<Guid> equipmentIds)
    {
        var existingItemIds = term.Items.Select(i => i.EquipmentId).ToHashSet();

        var equipmentList = await _context.ITEquipments
            .Where(e => equipmentIds.Contains(e.Id))
            .ToListAsync();

        if (equipmentList.Count != equipmentIds.Count)
            return new BadRequestObjectResult(new { detail = "Um ou mais equipamentos não foram encontrados." });

        var errors = new List<string>();
        foreach (var eq in equipmentList)
        {
            if (existingItemIds.Contains(eq.Id))
            {
                errors.Add($"{eq.AssetTag}: já está neste termo.");
                continue;
            }

            // Block if purchase document is pending
            if (eq.PurchaseDocumentPending)
            {
                errors.Add($"{eq.AssetTag}: cadastro incompleto — documento de compra/entrega pendente.");
                continue;
            }

            // Check if already assigned elsewhere or in an incompatible state
            if (eq.StatusCode != ITEquipmentConstants.EquipmentStatus.Available &&
                eq.StatusCode != ITEquipmentConstants.EquipmentStatus.Returned)
            {
                errors.Add($"{eq.AssetTag}: status atual é {ITEquipmentConstants.EquipmentStatus.DisplayName(eq.StatusCode)} — indisponível.");
                continue;
            }

            // Check if already in another active DRAFT delivery term
            var inOtherDraft = await _context.ITEquipmentDeliveryItems
                .AnyAsync(di => di.EquipmentId == eq.Id &&
                               di.DeliveryTermId != term.Id &&
                               di.DeliveryTerm!.Status == ITEquipmentConstants.DeliveryTermStatus.Draft);
            if (inOtherDraft)
            {
                errors.Add($"{eq.AssetTag}: já está em outro termo de entrega em rascunho.");
                continue;
            }

            term.Items.Add(new ITEquipmentDeliveryItem
            {
                DeliveryTermId = term.Id,
                EquipmentId = eq.Id,
                ItemStatus = ITEquipmentConstants.DeliveryItemStatus.Pending
            });
        }

        if (errors.Any())
            return new BadRequestObjectResult(new { detail = "Erros na validação dos equipamentos:", errors });

        return null;
    }

    private async Task NotifyITAsync(string termNumber, string headline, string bodyHtml)
    {
        try
        {
            var email = _configuration["AppConfig:ITNotificationEmail"];
            if (string.IsNullOrWhiteSpace(email)) return;

            await _emailService.SendWorkflowNotificationAsync(
                email, "Departamento de T.I",
                $"{headline} — {termNumber}",
                headline, bodyHtml);
        }
        catch
        {
            // IT notification failure should never block the main operation
        }
    }

    private static string BuildTermNotificationHtml(ITEquipmentDeliveryTerm term, string action, string performedBy)
    {
        var itemCount = term.Items?.Count ?? 0;
        return $@"
            <table style='border-collapse:collapse;width:100%;font-size:14px'>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Ação</td><td style='padding:6px 12px'>{action}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Nº Termo</td><td style='padding:6px 12px'>{term.TermNumber}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Funcionário</td><td style='padding:6px 12px'>{term.EmployeeName}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>E-mail</td><td style='padding:6px 12px'>{term.EmployeeEmail ?? "—"}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Departamento</td><td style='padding:6px 12px'>{term.EmployeeDepartment ?? "—"}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Planta</td><td style='padding:6px 12px'>{term.EmployeePlant ?? "—"}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Status</td><td style='padding:6px 12px'>{ITEquipmentConstants.DeliveryTermStatus.DisplayName(term.Status)}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Qtd. Equipamentos</td><td style='padding:6px 12px'>{itemCount}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Realizado por</td><td style='padding:6px 12px'>{performedBy}</td></tr>
                <tr><td style='padding:6px 12px;font-weight:bold;background:#f5f5f5'>Data/Hora</td><td style='padding:6px 12px'>{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</td></tr>
            </table>";
    }

    private string ResolveStoragePath(string storageReference)
    {
        return Path.Combine(_storagePath, storageReference);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server unique constraint violation error number = 2601 / 2627
        var inner = ex.InnerException?.Message ?? "";
        return inner.Contains("2601") || inner.Contains("2627") || inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════
    //  REQUEST DTOs
    // ═══════════════════════════════════════════════════════════════

    public class CreateDeliveryTermRequest
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string? EmployeeEmail { get; set; }
        public Guid? EmployeeUserId { get; set; }
        public string? EmployeeDepartment { get; set; }
        public string? EmployeePosition { get; set; }
        public string? EmployeePlant { get; set; }
        public int? CompanyId { get; set; }
        public int? EmployeePlantId { get; set; }
        public int? EmployeeDepartmentId { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string? Notes { get; set; }
        public List<Guid>? EquipmentIds { get; set; }
    }

    public class UpdateDeliveryTermRequest
    {
        public string? EmployeeName { get; set; }
        public string? EmployeeEmail { get; set; }
        public Guid? EmployeeUserId { get; set; }
        public string? EmployeeDepartment { get; set; }
        public string? EmployeePosition { get; set; }
        public string? EmployeePlant { get; set; }
        public int? CompanyId { get; set; }
        public int? EmployeePlantId { get; set; }
        public int? EmployeeDepartmentId { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string? Notes { get; set; }
    }

    public class AddItemsRequest
    {
        public List<Guid> EquipmentIds { get; set; } = new();
    }

    public class ReturnItemRequest
    {
        public DateTime? ReturnDate { get; set; }
        public string? ReturnCondition { get; set; }
        public string? Notes { get; set; }
    }
}
