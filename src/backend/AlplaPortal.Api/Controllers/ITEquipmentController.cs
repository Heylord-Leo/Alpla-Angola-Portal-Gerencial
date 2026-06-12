using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/it/equipment")]
public class ITEquipmentController : BaseController
{
    private readonly IEmailService _emailService;
    private readonly ITEquipmentAgreementService _agreementService;
    private readonly ITEquipmentPdfService _pdfService;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public ITEquipmentController(ApplicationDbContext context, IEmailService emailService, ITEquipmentAgreementService agreementService, ITEquipmentPdfService pdfService, Microsoft.Extensions.Configuration.IConfiguration configuration) : base(context)
    {
        _emailService = emailService;
        _agreementService = agreementService;
        _pdfService = pdfService;
        _configuration = configuration;
    }

    // ─── Helper: Check IT role ───
    private bool HasITAccess()
    {
        return CurrentUserRoles.Contains(RoleConstants.IT) ||
               CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);
    }

    // ─── GET /api/it/equipment/summary ───
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        if (!HasITAccess()) return Forbid();

        var total = await _context.ITEquipments.CountAsync(e => e.IsActive);
        var inUse = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.InUse);
        var available = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.Available);
        var inRepair = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.InRepair);
        var lost = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.Lost);
        var retired = await _context.ITEquipments.CountAsync(e => e.StatusCode == ITEquipmentConstants.EquipmentStatus.Retired);
        var reserved = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.Reserved);
        var unknown = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.Unknown);
        var noOwner = await _context.ITEquipments.CountAsync(e => e.IsActive && e.StatusCode == ITEquipmentConstants.EquipmentStatus.InUse && string.IsNullOrEmpty(e.CurrentOwnerName));
        var noSerial = await _context.ITEquipments.CountAsync(e => e.IsActive && string.IsNullOrEmpty(e.SerialNumber));
        var noType = await _context.ITEquipments.CountAsync(e => e.IsActive && (e.EquipmentType == ITEquipmentConstants.EquipmentType.UnknownType || string.IsNullOrEmpty(e.EquipmentType)));

        return Ok(new
        {
            total, inUse, available, inRepair, lost, retired,
            reserved, unknown, noOwner, noSerial, noType
        });
    }

    // ─── GET /api/it/equipment ───
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? statusCode,
        [FromQuery] string? equipmentType,
        [FromQuery] string? plant,
        [FromQuery] string? manufacturer,
        [FromQuery] bool? hasOwner,
        [FromQuery] bool? biometricMfa,
        [FromQuery] string? sortBy,
        [FromQuery] bool isDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        if (!HasITAccess()) return Forbid();

        var query = _context.ITEquipments.AsNoTracking().Where(e => e.IsActive);

        // Filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(e =>
                e.AssetTag.ToLower().Contains(s) ||
                (e.Hostname != null && e.Hostname.ToLower().Contains(s)) ||
                (e.SerialNumber != null && e.SerialNumber.ToLower().Contains(s)) ||
                (e.CurrentOwnerName != null && e.CurrentOwnerName.ToLower().Contains(s)) ||
                (e.Model != null && e.Model.ToLower().Contains(s)) ||
                (e.Manufacturer != null && e.Manufacturer.ToLower().Contains(s)) ||
                (e.MacAddress != null && e.MacAddress.ToLower().Contains(s))
            );
        }

        if (!string.IsNullOrWhiteSpace(statusCode))
        {
            var codes = statusCode.Split(',', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(e => codes.Contains(e.StatusCode));
        }
        if (!string.IsNullOrWhiteSpace(equipmentType))
        {
            var types = equipmentType.Split(',', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(e => types.Contains(e.EquipmentType));
        }
        if (!string.IsNullOrWhiteSpace(plant))
            query = query.Where(e => e.Plant == plant);
        if (!string.IsNullOrWhiteSpace(manufacturer))
            query = query.Where(e => e.Manufacturer == manufacturer);
        if (hasOwner == true)
            query = query.Where(e => e.CurrentOwnerName != null && e.CurrentOwnerName != "");
        if (hasOwner == false)
            query = query.Where(e => e.CurrentOwnerName == null || e.CurrentOwnerName == "");
        if (biometricMfa.HasValue)
            query = query.Where(e => e.BiometricMfaEnabled == biometricMfa.Value);

        var totalCount = await query.CountAsync();

        // Sorting
        query = (sortBy?.ToLower()) switch
        {
            "assettag" => isDescending ? query.OrderByDescending(e => e.AssetTag) : query.OrderBy(e => e.AssetTag),
            "hostname" => isDescending ? query.OrderByDescending(e => e.Hostname) : query.OrderBy(e => e.Hostname),
            "type" => isDescending ? query.OrderByDescending(e => e.EquipmentType) : query.OrderBy(e => e.EquipmentType),
            "status" => isDescending ? query.OrderByDescending(e => e.StatusCode) : query.OrderBy(e => e.StatusCode),
            "owner" => isDescending ? query.OrderByDescending(e => e.CurrentOwnerName) : query.OrderBy(e => e.CurrentOwnerName),
            "manufacturer" => isDescending ? query.OrderByDescending(e => e.Manufacturer) : query.OrderBy(e => e.Manufacturer),
            "updatedat" => isDescending ? query.OrderByDescending(e => e.UpdatedAt) : query.OrderBy(e => e.UpdatedAt),
            _ => query.OrderByDescending(e => e.CreatedAt)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.AssetTag, e.Hostname, e.Plant, e.EquipmentType, e.StatusCode,
                e.Manufacturer, e.Model, e.SerialNumber, e.MacAddress,
                e.CurrentOwnerName, e.BiometricMfaEnabled,
                e.UpdatedAt, e.CreatedAt
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize });
    }

    // ─── GET /api/it/equipment/filters ───
    [HttpGet("filters")]
    public async Task<IActionResult> GetFilterOptions()
    {
        if (!HasITAccess()) return Forbid();

        var plants = await _context.ITEquipments.AsNoTracking()
            .Where(e => e.IsActive && e.Plant != null && e.Plant != "")
            .Select(e => e.Plant!).Distinct().OrderBy(p => p).ToListAsync();

        var manufacturers = await _context.ITEquipments.AsNoTracking()
            .Where(e => e.IsActive && e.Manufacturer != null && e.Manufacturer != "")
            .Select(e => e.Manufacturer!).Distinct().OrderBy(m => m).ToListAsync();

        return Ok(new { plants, manufacturers });
    }

    // ─── GET /api/it/equipment/{id} ───
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.AsNoTracking()
            .Include(e => e.Acquisition)
            .Include(e => e.Documents.Where(d => !d.IsDeleted))
            .Include(e => e.CurrentOwnerUser)
            .Include(e => e.CreatedByUser)
            .Include(e => e.UpdatedByUser)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (eq == null) return NotFound("Equipamento não encontrado.");

        var assignments = await _context.ITEquipmentAssignments.AsNoTracking()
            .Where(a => a.EquipmentId == id)
            .OrderByDescending(a => a.AssignedDate)
            .Select(a => new
            {
                a.Id, a.AssignedToName, a.AssignedToDepartment, a.AssignedToPlant,
                a.AssignedDate, a.ExpectedReturnDate, a.ReturnedDate,
                a.AssignmentStatus, a.Notes
            })
            .ToListAsync();

        var movements = await _context.ITEquipmentMovementLogs.AsNoTracking()
            .Where(m => m.EquipmentId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .Select(m => new
            {
                m.Id, m.MovementType, m.PreviousStatus, m.NewStatus,
                m.PreviousOwnerName, m.NewOwnerName, m.Notes, m.CreatedAt,
                CreatedByName = m.CreatedByUser != null ? m.CreatedByUser.FullName : null
            })
            .ToListAsync();

        return Ok(new
        {
            eq.Id, eq.AssetTag, eq.Hostname, eq.Plant, eq.EquipmentType, eq.StatusCode,
            eq.Manufacturer, eq.Model, eq.SerialNumber, eq.MacAddress,
            eq.Processor, eq.MemoryRam, eq.Color, eq.BiometricMfaEnabled, eq.IdCard,
            eq.DevicePhotoUrl, eq.CurrentOwnerName,
            CurrentOwnerEmail = eq.CurrentOwnerUser?.Email,
            eq.CurrentOwnerUserId, eq.CurrentOwnerEmployeeId,
            eq.Notes, eq.SourceType, eq.IsActive, eq.CreatedAt, eq.UpdatedAt,
            CreatedByName = eq.CreatedByUser?.FullName,
            UpdatedByName = eq.UpdatedByUser?.FullName,
            Acquisition = eq.Acquisition == null ? null : new
            {
                eq.Acquisition.Id, eq.Acquisition.AcquisitionDate, eq.Acquisition.SupplierName,
                eq.Acquisition.PurchaseOrderNumber, eq.Acquisition.InvoiceNumber,
                eq.Acquisition.PaymentReference, eq.Acquisition.PaymentDate,
                eq.Acquisition.PurchaseAmount, eq.Acquisition.Currency,
                eq.Acquisition.WarrantyStartDate, eq.Acquisition.WarrantyEndDate,
                eq.Acquisition.WarrantyNotes, eq.Acquisition.AcquisitionNotes,
                eq.Acquisition.PurchaseRequestNumber
            },
            Documents = eq.Documents.Select(d => new
            {
                d.Id, d.DocumentType, d.FileName, d.UploadedAt, d.Notes, d.AcquisitionId, d.AssignmentId
            }),
            Assignments = assignments,
            Movements = movements
        });
    }

    // ─── POST /api/it/equipment ───
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEquipmentRequest request)
    {
        if (!HasITAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(request.AssetTag))
            return BadRequest(new { detail = "Asset Tag é obrigatório." });

        // Validate unique AssetTag
        var exists = await _context.ITEquipments.AnyAsync(e => e.AssetTag == request.AssetTag.Trim());
        if (exists) return BadRequest(new { detail = $"Asset Tag '{request.AssetTag}' já existe." });

        // Validate unique SerialNumber (if provided)
        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var snExists = await _context.ITEquipments.AnyAsync(e => e.SerialNumber == request.SerialNumber.Trim());
            if (snExists) return BadRequest(new { detail = $"Serial Number '{request.SerialNumber}' já está registado." });
        }

        var userId = CurrentUserId;
        var sourceType = request.SourceType ?? ITEquipmentConstants.SourceType.ManualRegistration;
        var statusCode = request.StatusCode ?? ITEquipmentConstants.EquipmentStatus.Available;

        var equipment = new ITEquipment
        {
            AssetTag = request.AssetTag.Trim(),
            Hostname = request.Hostname?.Trim(),
            Plant = request.Plant?.Trim(),
            EquipmentType = request.EquipmentType ?? ITEquipmentConstants.EquipmentType.UnknownType,
            StatusCode = statusCode,
            Manufacturer = request.Manufacturer?.Trim(),
            Model = request.Model?.Trim(),
            SerialNumber = request.SerialNumber?.Trim(),
            MacAddress = request.MacAddress?.Trim(),
            Processor = request.Processor?.Trim(),
            MemoryRam = request.MemoryRam?.Trim(),
            Color = request.Color?.Trim(),
            BiometricMfaEnabled = request.BiometricMfaEnabled ?? false,
            IdCard = request.IdCard?.Trim(),
            Notes = request.Notes?.Trim(),
            SourceType = sourceType,
            IsActive = true,
            CreatedByUserId = userId
        };

        _context.ITEquipments.Add(equipment);

        // Create acquisition record if this is a purchase
        if (sourceType == ITEquipmentConstants.SourceType.ManualPurchase && request.Acquisition != null)
        {
            var acq = new ITEquipmentAcquisition
            {
                EquipmentId = equipment.Id,
                AcquisitionDate = request.Acquisition.AcquisitionDate,
                SupplierName = request.Acquisition.SupplierName?.Trim(),
                PurchaseOrderNumber = request.Acquisition.PurchaseOrderNumber?.Trim(),
                InvoiceNumber = request.Acquisition.InvoiceNumber?.Trim(),
                PaymentReference = request.Acquisition.PaymentReference?.Trim(),
                PaymentDate = request.Acquisition.PaymentDate,
                PurchaseAmount = request.Acquisition.PurchaseAmount,
                Currency = request.Acquisition.Currency?.Trim(),
                WarrantyStartDate = request.Acquisition.WarrantyStartDate,
                WarrantyEndDate = request.Acquisition.WarrantyEndDate,
                WarrantyNotes = request.Acquisition.WarrantyNotes?.Trim(),
                AcquisitionNotes = request.Acquisition.AcquisitionNotes?.Trim(),
                CreatedByUserId = userId
            };
            _context.ITEquipmentAcquisitions.Add(acq);
        }

        // Movement log: CREATED
        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = equipment.Id,
            MovementType = ITEquipmentConstants.MovementType.Created,
            NewStatus = statusCode,
            Notes = $"Equipamento criado via registo manual ({sourceType}).",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();

        await NotifyITAsync(equipment.AssetTag, "Novo Equipamento Registado",
            $"O equipamento <strong>{equipment.AssetTag}</strong> ({equipment.EquipmentType}) foi registado no inventário.");

        return Ok(new { id = equipment.Id, assetTag = equipment.AssetTag });
    }

    // ─── PUT /api/it/equipment/{id} ───
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEquipmentRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        // Check unique constraints if changed
        if (request.AssetTag != null && request.AssetTag.Trim() != eq.AssetTag)
        {
            var exists = await _context.ITEquipments.AnyAsync(e => e.AssetTag == request.AssetTag.Trim() && e.Id != id);
            if (exists) return BadRequest(new { detail = $"Asset Tag '{request.AssetTag}' já existe." });
            eq.AssetTag = request.AssetTag.Trim();
        }

        if (request.SerialNumber != null && !string.IsNullOrWhiteSpace(request.SerialNumber) && request.SerialNumber.Trim() != eq.SerialNumber)
        {
            var snExists = await _context.ITEquipments.AnyAsync(e => e.SerialNumber == request.SerialNumber.Trim() && e.Id != id);
            if (snExists) return BadRequest(new { detail = $"Serial Number '{request.SerialNumber}' já está registado." });
        }

        var userId = CurrentUserId;
        // Track field-level old → new diffs for enhanced audit
        var diffs = new List<string>();
        var criticalChange = false;

        void TrackDiff(string label, string? oldVal, string? newVal, bool critical = false) {
            if (newVal != null && newVal != oldVal) {
                diffs.Add($"{label}: \"{oldVal ?? "—"}\" → \"{newVal}\"");
                if (critical) criticalChange = true;
            }
        }

        if (request.Hostname != null && request.Hostname != eq.Hostname) { TrackDiff("Hostname", eq.Hostname, request.Hostname.Trim()); eq.Hostname = request.Hostname.Trim(); }
        if (request.Plant != null && request.Plant != eq.Plant) { TrackDiff("Planta", eq.Plant, request.Plant.Trim(), true); eq.Plant = request.Plant.Trim(); }
        if (request.EquipmentType != null && request.EquipmentType != eq.EquipmentType) { TrackDiff("Tipo", eq.EquipmentType, request.EquipmentType, true); eq.EquipmentType = request.EquipmentType; }
        if (request.Manufacturer != null && request.Manufacturer != eq.Manufacturer) { TrackDiff("Fabricante", eq.Manufacturer, request.Manufacturer.Trim()); eq.Manufacturer = request.Manufacturer.Trim(); }
        if (request.Model != null && request.Model != eq.Model) { TrackDiff("Modelo", eq.Model, request.Model.Trim()); eq.Model = request.Model.Trim(); }
        if (request.SerialNumber != null && request.SerialNumber.Trim() != eq.SerialNumber) { TrackDiff("Serial", eq.SerialNumber, request.SerialNumber.Trim(), true); eq.SerialNumber = request.SerialNumber.Trim(); }
        if (request.MacAddress != null && request.MacAddress != eq.MacAddress) { TrackDiff("MAC", eq.MacAddress, request.MacAddress.Trim()); eq.MacAddress = request.MacAddress.Trim(); }
        if (request.Processor != null && request.Processor != eq.Processor) { TrackDiff("Processador", eq.Processor, request.Processor.Trim()); eq.Processor = request.Processor.Trim(); }
        if (request.MemoryRam != null && request.MemoryRam != eq.MemoryRam) { TrackDiff("RAM", eq.MemoryRam, request.MemoryRam.Trim()); eq.MemoryRam = request.MemoryRam.Trim(); }
        if (request.Color != null && request.Color != eq.Color) { TrackDiff("Cor", eq.Color, request.Color.Trim()); eq.Color = request.Color.Trim(); }
        if (request.BiometricMfaEnabled.HasValue && request.BiometricMfaEnabled != eq.BiometricMfaEnabled) { TrackDiff("Biometria", eq.BiometricMfaEnabled.ToString(), request.BiometricMfaEnabled.Value.ToString()); eq.BiometricMfaEnabled = request.BiometricMfaEnabled.Value; }
        if (request.IdCard != null && request.IdCard != eq.IdCard) { TrackDiff("ID Card", eq.IdCard, request.IdCard.Trim()); eq.IdCard = request.IdCard.Trim(); }
        if (request.Notes != null && request.Notes != eq.Notes) { eq.Notes = request.Notes.Trim(); diffs.Add("Notas atualizadas"); }

        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        if (diffs.Any())
        {
            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = id,
                MovementType = ITEquipmentConstants.MovementType.Updated,
                PreviousStatus = eq.StatusCode,
                NewStatus = eq.StatusCode,
                Notes = string.Join("\n", diffs),
                CreatedByUserId = userId
            });
        }

        await _context.SaveChangesAsync();

        // Send IT notification only for critical field changes
        if (criticalChange)
        {
            await NotifyITAsync(eq.AssetTag, "Atualização de Equipamento",
                $"O equipamento <strong>{eq.AssetTag}</strong> teve campos críticos alterados:<br/>{string.Join("<br/>", diffs)}");
        }

        return Ok(new { message = "Equipamento atualizado com sucesso." });
    }

    // ─── POST /api/it/equipment/{id}/assign ───
    [HttpPost("{id}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments
            .Include(e => e.Acquisition)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        // Block assignment if non-assignable status
        if (ITEquipmentConstants.EquipmentStatus.NonAssignable.Contains(eq.StatusCode))
            return BadRequest(new { detail = $"Equipamento com status '{ITEquipmentConstants.EquipmentStatus.DisplayName(eq.StatusCode)}' não pode ser atribuído." });

        // Block if already has an active assignment
        var activeAssignment = await _context.ITEquipmentAssignments
            .AnyAsync(a => a.EquipmentId == id && a.AssignmentStatus == ITEquipmentConstants.AssignmentStatus.Active);
        if (activeAssignment)
            return BadRequest(new { detail = "Este equipamento já possui uma atribuição ativa. Faça a devolução primeiro." });

        // Resolve assignee email: prefer user record, fallback to request
        string? assigneeEmail = request.AssignedToEmail?.Trim();
        if (request.AssignedToUserId.HasValue)
        {
            var assigneeUser = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId.Value);
            if (assigneeUser != null && !string.IsNullOrWhiteSpace(assigneeUser.Email))
                assigneeEmail = assigneeUser.Email;
        }
        if (string.IsNullOrWhiteSpace(assigneeEmail))
            return BadRequest(new { detail = "Email do utilizador é obrigatório para atribuição de equipamento." });

        // Resolve assigner info (current user)
        var userId = CurrentUserId;
        var assignerUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var assignerName = assignerUser?.FullName ?? "Administrador";
        var assignerEmail = assignerUser?.Email ?? "";

        var previousStatus = eq.StatusCode;

        var assignment = new ITEquipmentAssignment
        {
            EquipmentId = id,
            AssignedToUserId = request.AssignedToUserId,
            AssignedToName = request.AssignedToName?.Trim() ?? "",
            AssignedToEmail = assigneeEmail,
            AssignedToDepartment = request.AssignedToDepartment?.Trim(),
            AssignedToPlant = request.AssignedToPlant?.Trim(),
            AssignedDate = request.AssignedDate ?? DateTime.UtcNow,
            ExpectedReturnDate = request.ExpectedReturnDate,
            Notes = request.Notes?.Trim(),
            CreatedByUserId = userId
        };

        _context.ITEquipmentAssignments.Add(assignment);

        eq.StatusCode = ITEquipmentConstants.EquipmentStatus.InUse;
        eq.CurrentOwnerName = assignment.AssignedToName;
        eq.CurrentOwnerUserId = assignment.AssignedToUserId;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.Assigned,
            PreviousStatus = previousStatus,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewOwnerName = assignment.AssignedToName,
            Notes = request.Notes?.Trim(),
            CreatedByUserId = userId
        });

        // ── Generate responsibility agreement ──
        ITEquipmentAgreementService.AgreementResult? agreementResult = null;
        try
        {
            var agreementData = new ITEquipmentAgreementService.AgreementData
            {
                AssigneeName = assignment.AssignedToName,
                AssigneeEmail = assigneeEmail,
                AssigneeDepartment = assignment.AssignedToDepartment ?? "—",
                AssigneePlant = assignment.AssignedToPlant ?? "—",
                AssetTag = eq.AssetTag,
                Hostname = eq.Hostname,
                EquipmentType = eq.EquipmentType,
                Manufacturer = eq.Manufacturer,
                Model = eq.Model,
                SerialNumber = eq.SerialNumber,
                MacAddress = eq.MacAddress,
                PurchaseAmount = eq.Acquisition?.PurchaseAmount,
                Currency = eq.Acquisition?.Currency,
                AssignedDate = assignment.AssignedDate,
                AssignedByName = assignerName,
                AssignedByEmail = assignerEmail,
                Notes = assignment.Notes
            };

            agreementResult = await _pdfService.GenerateAssignmentPdfAsync(agreementData);

            // Save document record
            var agreementDoc = new ITEquipmentDocument
            {
                Id = Guid.NewGuid(),
                EquipmentId = id,
                AssignmentId = assignment.Id,
                DocumentType = ITEquipmentConstants.DocumentType.AssignmentAgreement,
                FileName = agreementResult.DisplayFileName,
                StorageReference = agreementResult.StorageFileName,
                FileHash = agreementResult.FileHash,
                UploadedAt = DateTime.UtcNow,
                UploadedByUserId = userId,
                Notes = $"Termo gerado automaticamente para atribuição a {assignment.AssignedToName}.",
                IsDeleted = false
            };
            _context.ITEquipmentDocuments.Add(agreementDoc);

            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = id,
                MovementType = ITEquipmentConstants.MovementType.AgreementGenerated,
                PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                Notes = $"Termo de Responsabilidade gerado: {agreementResult.DisplayFileName}",
                CreatedByUserId = userId
            });
        }
        catch (FileNotFoundException)
        {
            return BadRequest(new { detail = "Template do Termo de Responsabilidade não encontrado. Contacte o administrador do sistema." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { detail = $"Falha ao gerar Termo de Responsabilidade: {ex.Message}" });
        }

        await _context.SaveChangesAsync();

        // ── Send emails (post-commit — failure does not roll back assignment) ──
        var warnings = new List<string>();

        if (agreementResult != null)
        {
            var subject = $"Termo de Responsabilidade — {eq.AssetTag}";
            var headline = "Atribuição de Equipamento de T.I";
            var bodyHtml = $@"
                <p>O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi atribuído a <strong>{assignment.AssignedToName}</strong>.</p>
                <p>Em anexo encontra-se o Termo de Responsabilidade que rege a utilização deste equipamento.</p>
                <p>Por favor, leia atentamente os termos e condições descritos no documento anexo.</p>";

            // Email to assignee
            try
            {
                var sent = await _emailService.SendWithAttachmentAsync(
                    assigneeEmail, assignment.AssignedToName, subject, headline, bodyHtml,
                    agreementResult.FilePath, agreementResult.DisplayFileName);

                _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                {
                    EquipmentId = id,
                    MovementType = sent ? ITEquipmentConstants.MovementType.EmailSent : ITEquipmentConstants.MovementType.EmailFailed,
                    PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                    NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                    Notes = sent
                        ? $"E-mail com Termo enviado ao utilizador: {assigneeEmail}"
                        : $"Falha ao enviar e-mail ao utilizador: {assigneeEmail}",
                    CreatedByUserId = userId
                });

                if (!sent) warnings.Add($"Não foi possível enviar o e-mail para o utilizador ({assigneeEmail}).");
            }
            catch
            {
                warnings.Add($"Falha ao enviar e-mail para o utilizador ({assigneeEmail}).");
                _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                {
                    EquipmentId = id,
                    MovementType = ITEquipmentConstants.MovementType.EmailFailed,
                    Notes = $"Excepção ao enviar e-mail ao utilizador: {assigneeEmail}",
                    CreatedByUserId = userId
                });
            }

            // Email to assigner
            if (!string.IsNullOrWhiteSpace(assignerEmail))
            {
                try
                {
                    var sent = await _emailService.SendWithAttachmentAsync(
                        assignerEmail, assignerName, subject, headline, bodyHtml,
                        agreementResult.FilePath, agreementResult.DisplayFileName);

                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = id,
                        MovementType = sent ? ITEquipmentConstants.MovementType.EmailSent : ITEquipmentConstants.MovementType.EmailFailed,
                        PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                        NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
                        Notes = sent
                            ? $"E-mail com Termo enviado a quem atribui: {assignerEmail}"
                            : $"Falha ao enviar e-mail a quem atribui: {assignerEmail}",
                        CreatedByUserId = userId
                    });

                    if (!sent) warnings.Add($"Não foi possível enviar o e-mail para quem atribui ({assignerEmail}).");
                }
                catch
                {
                    warnings.Add($"Falha ao enviar e-mail para quem atribui ({assignerEmail}).");
                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = id,
                        MovementType = ITEquipmentConstants.MovementType.EmailFailed,
                        Notes = $"Excepção ao enviar e-mail a quem atribui: {assignerEmail}",
                        CreatedByUserId = userId
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        // IT notification for assignment
        await NotifyITAsync(eq.AssetTag, "Equipamento Atribuído",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi atribuído a <strong>{assignment.AssignedToName}</strong>.<br/>Departamento: {assignment.AssignedToDepartment ?? "—"}<br/>Planta: {assignment.AssignedToPlant ?? "—"}");

        return Ok(new
        {
            message = "Equipamento atribuído com sucesso.",
            warnings = warnings.Count > 0 ? warnings : null
        });
    }

    // ─── POST /api/it/equipment/{id}/return ───
    [HttpPost("{id}/return")]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments
            .Include(e => e.Acquisition)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        var activeAssignment = await _context.ITEquipmentAssignments
            .FirstOrDefaultAsync(a => a.EquipmentId == id && a.AssignmentStatus == ITEquipmentConstants.AssignmentStatus.Active);

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;
        var previousOwner = eq.CurrentOwnerName;
        var returnDateTime = request.ReturnDate ?? DateTime.UtcNow;

        // Resolve current user (receiver) info
        var receiverUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var receiverName = receiverUser?.FullName ?? "Administrador";
        var receiverEmail = receiverUser?.Email ?? "";

        // Capture assignment data before closing (needed for return document)
        string returningUserName = previousOwner ?? "—";
        string returningUserEmail = "";
        string returningDepartment = "—";
        string returningPlant = "—";
        Guid? assignmentId = null;

        if (activeAssignment != null)
        {
            returningUserName = activeAssignment.AssignedToName;
            returningUserEmail = activeAssignment.AssignedToEmail ?? "";
            returningDepartment = activeAssignment.AssignedToDepartment ?? "—";
            returningPlant = activeAssignment.AssignedToPlant ?? "—";
            assignmentId = activeAssignment.Id;

            activeAssignment.AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Returned;
            activeAssignment.ReturnedDate = returnDateTime;
            activeAssignment.Notes = string.IsNullOrWhiteSpace(activeAssignment.Notes)
                ? request.Notes?.Trim()
                : $"{activeAssignment.Notes}\nDevolução: {request.Notes?.Trim()}";
        }

        // Determine new status based on condition
        var newStatus = request.Condition?.ToUpper() switch
        {
            "DAMAGED" => ITEquipmentConstants.EquipmentStatus.Damaged,
            "NEEDS_REPAIR" => ITEquipmentConstants.EquipmentStatus.InRepair,
            _ => ITEquipmentConstants.EquipmentStatus.Available
        };

        eq.StatusCode = newStatus;
        eq.CurrentOwnerName = null;
        eq.CurrentOwnerUserId = null;
        eq.CurrentOwnerEmployeeId = null;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.Returned,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            PreviousOwnerName = previousOwner,
            Notes = request.Notes?.Trim(),
            CreatedByUserId = userId
        });

        // ── Generate return document ──
        ITEquipmentAgreementService.AgreementResult? returnDocResult = null;
        try
        {
            var returnData = new ITEquipmentAgreementService.ReturnData
            {
                UserName = returningUserName,
                UserEmail = returningUserEmail,
                Department = returningDepartment,
                Plant = returningPlant,
                AssetTag = eq.AssetTag,
                Hostname = eq.Hostname,
                EquipmentType = eq.EquipmentType,
                Manufacturer = eq.Manufacturer,
                Model = eq.Model,
                SerialNumber = eq.SerialNumber,
                MacAddress = eq.MacAddress,
                PurchaseAmount = eq.Acquisition?.PurchaseAmount,
                Currency = eq.Acquisition?.Currency,
                ReturnDateTime = returnDateTime,
                ReceivedByName = receiverName,
                ReceivedByEmail = receiverEmail,
                Condition = request.Condition ?? "GOOD",
                Notes = request.Notes?.Trim()
            };

            returnDocResult = await _pdfService.GenerateReturnPdfAsync(returnData);

            // Save document record
            var returnDoc = new ITEquipmentDocument
            {
                Id = Guid.NewGuid(),
                EquipmentId = id,
                AssignmentId = assignmentId,
                DocumentType = ITEquipmentConstants.DocumentType.ReturnAgreement,
                FileName = returnDocResult.DisplayFileName,
                StorageReference = returnDocResult.StorageFileName,
                FileHash = returnDocResult.FileHash,
                UploadedAt = DateTime.UtcNow,
                UploadedByUserId = userId,
                Notes = $"Termo de Devolução gerado automaticamente — devolvido por {returningUserName}.",
                IsDeleted = false
            };
            _context.ITEquipmentDocuments.Add(returnDoc);

            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = id,
                MovementType = ITEquipmentConstants.MovementType.ReturnDocumentGenerated,
                PreviousStatus = newStatus,
                NewStatus = newStatus,
                Notes = $"Termo de Devolução gerado com sucesso.",
                CreatedByUserId = userId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { detail = $"Falha ao gerar Termo de Devolução: {ex.Message}" });
        }

        await _context.SaveChangesAsync();

        // ── Send emails (post-commit — failure does not roll back return) ──
        var warnings = new List<string>();

        if (returnDocResult != null)
        {
            var conditionLabel = request.Condition?.ToUpper() switch
            {
                "GOOD" => "Em bom estado",
                "DAMAGED" => "Danificado",
                "NEEDS_REPAIR" => "Necessita conserto",
                _ => request.Condition ?? "—"
            };
            var subject = $"Termo de Devolução de Equipamento de T.I - {eq.AssetTag}";
            var headline = "Devolução de Equipamento de T.I";
            var notesHtml = string.IsNullOrWhiteSpace(request.Notes) ? "Sem observações." : request.Notes.Trim();
            var bodyHtml = $@"
                <p>Olá,</p>
                <p>Segue em anexo o Termo de Devolução referente ao equipamento de T.I devolvido.</p>
                <p><strong>Equipamento:</strong> {eq.EquipmentType} {eq.Manufacturer} {eq.Model}<br/>
                <strong>Asset Tag:</strong> {eq.AssetTag}<br/>
                <strong>Condição na devolução:</strong> {conditionLabel}<br/>
                <strong>Data de devolução:</strong> {returnDateTime:dd/MM/yyyy HH:mm} UTC<br/>
                <strong>Recebido por:</strong> {receiverName}</p>
                <p><strong>Observações:</strong><br/>{notesHtml}</p>
                <p><em>Este é um e-mail automático do Portal Gerencial.</em></p>";

            // Email to user who returned the equipment
            if (!string.IsNullOrWhiteSpace(returningUserEmail))
            {
                try
                {
                    var sent = await _emailService.SendWithAttachmentAsync(
                        returningUserEmail, returningUserName, subject, headline, bodyHtml,
                        returnDocResult.FilePath, returnDocResult.DisplayFileName);

                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = id,
                        MovementType = sent
                            ? ITEquipmentConstants.MovementType.ReturnEmailSent
                            : ITEquipmentConstants.MovementType.ReturnEmailFailed,
                        PreviousStatus = newStatus,
                        NewStatus = newStatus,
                        Notes = sent
                            ? $"Termo de Devolução enviado para o utilizador: {returningUserEmail}"
                            : $"Falha ao enviar Termo de Devolução para o utilizador: {returningUserEmail}",
                        CreatedByUserId = userId
                    });

                    if (!sent) warnings.Add($"Não foi possível enviar o Termo de Devolução para o utilizador ({returningUserEmail}).");
                }
                catch (Exception ex)
                {
                    warnings.Add($"Falha ao enviar Termo de Devolução para o utilizador ({returningUserEmail}).");
                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = id,
                        MovementType = ITEquipmentConstants.MovementType.ReturnEmailFailed,
                        Notes = $"Falha ao enviar Termo de Devolução para o utilizador: {returningUserEmail} — {ex.Message}",
                        CreatedByUserId = userId
                    });
                }
            }

            // Email to receiver (logged-in user who processed the return)
            if (!string.IsNullOrWhiteSpace(receiverEmail))
            {
                try
                {
                    var sent = await _emailService.SendWithAttachmentAsync(
                        receiverEmail, receiverName, subject, headline, bodyHtml,
                        returnDocResult.FilePath, returnDocResult.DisplayFileName);

                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = id,
                        MovementType = sent
                            ? ITEquipmentConstants.MovementType.ReturnEmailSent
                            : ITEquipmentConstants.MovementType.ReturnEmailFailed,
                        PreviousStatus = newStatus,
                        NewStatus = newStatus,
                        Notes = sent
                            ? $"Termo de Devolução enviado para quem recebeu a devolução: {receiverEmail}"
                            : $"Falha ao enviar Termo de Devolução para quem recebeu a devolução: {receiverEmail}",
                        CreatedByUserId = userId
                    });

                    if (!sent) warnings.Add($"Não foi possível enviar o Termo de Devolução para quem recebeu ({receiverEmail}).");
                }
                catch (Exception ex)
                {
                    warnings.Add($"Falha ao enviar Termo de Devolução para quem recebeu ({receiverEmail}).");
                    _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                    {
                        EquipmentId = id,
                        MovementType = ITEquipmentConstants.MovementType.ReturnEmailFailed,
                        Notes = $"Falha ao enviar Termo de Devolução para quem recebeu: {receiverEmail} — {ex.Message}",
                        CreatedByUserId = userId
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        // IT notification for return
        await NotifyITAsync(eq.AssetTag, "Equipamento Devolvido",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi devolvido por <strong>{returningUserName}</strong>.<br/>Condição: {request.Condition ?? "GOOD"}<br/>Novo status: {ITEquipmentConstants.EquipmentStatus.DisplayName(newStatus)}");

        return Ok(new
        {
            message = "Equipamento devolvido com sucesso.",
            newStatus,
            warnings = warnings.Count > 0 ? warnings : null
        });
    }
    // ─── POST /api/it/equipment/{id}/change-user ───
    [HttpPost("{id}/change-user")]
    public async Task<IActionResult> ChangeUser(Guid id, [FromBody] ChangeUserRequest request)
    {
        if (!HasITAccess()) return Forbid();

        // ── Validate inputs ──
        if (string.IsNullOrWhiteSpace(request.NewAssignedToName))
            return BadRequest(new { detail = "Informe o nome do novo utilizador." });
        if (string.IsNullOrWhiteSpace(request.NewAssignedToEmail))
            return BadRequest(new { detail = "Informe o email do novo utilizador." });

        // Block transfer when equipment is damaged/needs repair
        var condUpper = request.ReturnCondition?.ToUpper() ?? "GOOD";
        if (condUpper is "DAMAGED" or "NEEDS_REPAIR")
            return BadRequest(new { detail = "Não é possível transferir o equipamento para outro utilizador quando a condição da devolução indica dano ou necessidade de conserto. Faça a devolução normal e envie o equipamento para conserto." });

        var eq = await _context.ITEquipments
            .Include(e => e.Acquisition)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        var activeAssignment = await _context.ITEquipmentAssignments
            .FirstOrDefaultAsync(a => a.EquipmentId == id && a.AssignmentStatus == ITEquipmentConstants.AssignmentStatus.Active);
        if (activeAssignment == null)
            return BadRequest(new { detail = "Este equipamento não possui uma atribuição ativa." });

        var userId = CurrentUserId;
        var now = DateTime.UtcNow;

        // Resolve current logged-in user (person processing transfer)
        var processingUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var processingUserName = processingUser?.FullName ?? "Administrador";
        var processingUserEmail = processingUser?.Email ?? "";

        // ── Capture previous assignment data before closing ──
        var prevUserName = activeAssignment.AssignedToName;
        var prevUserEmail = activeAssignment.AssignedToEmail ?? "";
        var prevDepartment = activeAssignment.AssignedToDepartment ?? "—";
        var prevPlant = activeAssignment.AssignedToPlant ?? "—";
        var prevAssignmentId = activeAssignment.Id;

        // ── Resolve new user email ──
        var newEmail = request.NewAssignedToEmail!.Trim();
        if (request.NewAssignedToUserId.HasValue)
        {
            var newUser = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.NewAssignedToUserId.Value);
            if (newUser != null && !string.IsNullOrWhiteSpace(newUser.Email))
                newEmail = newUser.Email;
        }

        // ── 1. Generate return document ──
        ITEquipmentAgreementService.AgreementResult? returnDocResult;
        try
        {
            var returnData = new ITEquipmentAgreementService.ReturnData
            {
                UserName = prevUserName,
                UserEmail = prevUserEmail,
                Department = prevDepartment,
                Plant = prevPlant,
                AssetTag = eq.AssetTag,
                Hostname = eq.Hostname,
                EquipmentType = eq.EquipmentType,
                Manufacturer = eq.Manufacturer,
                Model = eq.Model,
                SerialNumber = eq.SerialNumber,
                MacAddress = eq.MacAddress,
                PurchaseAmount = eq.Acquisition?.PurchaseAmount,
                Currency = eq.Acquisition?.Currency,
                ReturnDateTime = now,
                ReceivedByName = processingUserName,
                ReceivedByEmail = processingUserEmail,
                Condition = request.ReturnCondition ?? "GOOD",
                Notes = request.ReturnNotes?.Trim()
            };
            returnDocResult = await _pdfService.GenerateReturnPdfAsync(returnData);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { detail = $"Falha ao gerar Termo de Devolução: {ex.Message}" });
        }

        // ── 2. Close current assignment ──
        activeAssignment.AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Returned;
        activeAssignment.ReturnedDate = now;
        activeAssignment.Notes = string.IsNullOrWhiteSpace(activeAssignment.Notes)
            ? $"Troca de utilizador — devolução. {request.ReturnNotes?.Trim() ?? ""}"
            : $"{activeAssignment.Notes}\nTroca de utilizador — devolução. {request.ReturnNotes?.Trim() ?? ""}";

        // ── 3. Create new assignment ──
        var newAssignment = new ITEquipmentAssignment
        {
            EquipmentId = id,
            AssignedToUserId = request.NewAssignedToUserId,
            AssignedToName = request.NewAssignedToName!.Trim(),
            AssignedToEmail = newEmail,
            AssignedToDepartment = request.NewAssignedToDepartment?.Trim(),
            AssignedToPlant = request.NewAssignedToPlant?.Trim(),
            AssignedDate = now,
            ExpectedReturnDate = request.NewExpectedReturnDate,
            Notes = request.NewAssignmentNotes?.Trim(),
            CreatedByUserId = userId
        };
        _context.ITEquipmentAssignments.Add(newAssignment);

        // ── 4. Update equipment (stays IN_USE, new owner) ──
        eq.CurrentOwnerName = newAssignment.AssignedToName;
        eq.CurrentOwnerUserId = newAssignment.AssignedToUserId;
        eq.UpdatedAt = now;
        eq.UpdatedByUserId = userId;

        // ── 5. Generate assignment agreement for new user ──
        ITEquipmentAgreementService.AgreementResult? assignDocResult;
        try
        {
            var agreementData = new ITEquipmentAgreementService.AgreementData
            {
                AssigneeName = newAssignment.AssignedToName,
                AssigneeEmail = newEmail,
                AssigneeDepartment = newAssignment.AssignedToDepartment ?? "—",
                AssigneePlant = newAssignment.AssignedToPlant ?? "—",
                AssetTag = eq.AssetTag,
                Hostname = eq.Hostname,
                EquipmentType = eq.EquipmentType,
                Manufacturer = eq.Manufacturer,
                Model = eq.Model,
                SerialNumber = eq.SerialNumber,
                MacAddress = eq.MacAddress,
                PurchaseAmount = eq.Acquisition?.PurchaseAmount,
                Currency = eq.Acquisition?.Currency,
                AssignedDate = now,
                AssignedByName = processingUserName,
                AssignedByEmail = processingUserEmail,
                Notes = newAssignment.Notes
            };
            assignDocResult = await _pdfService.GenerateAssignmentPdfAsync(agreementData);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { detail = $"Falha ao gerar Termo de Responsabilidade: {ex.Message}" });
        }

        // ── 6. Store both documents ──
        var returnDoc = new ITEquipmentDocument
        {
            Id = Guid.NewGuid(),
            EquipmentId = id,
            AssignmentId = prevAssignmentId,
            DocumentType = ITEquipmentConstants.DocumentType.ReturnAgreement,
            FileName = returnDocResult.DisplayFileName,
            StorageReference = returnDocResult.StorageFileName,
            FileHash = returnDocResult.FileHash,
            UploadedAt = now,
            UploadedByUserId = userId,
            Notes = $"Troca de utilizador — Termo de Devolução gerado para {prevUserName}.",
            IsDeleted = false
        };
        _context.ITEquipmentDocuments.Add(returnDoc);

        var assignDoc = new ITEquipmentDocument
        {
            Id = Guid.NewGuid(),
            EquipmentId = id,
            AssignmentId = newAssignment.Id,
            DocumentType = ITEquipmentConstants.DocumentType.AssignmentAgreement,
            FileName = assignDocResult.DisplayFileName,
            StorageReference = assignDocResult.StorageFileName,
            FileHash = assignDocResult.FileHash,
            UploadedAt = now,
            UploadedByUserId = userId,
            Notes = $"Troca de utilizador — Termo de Responsabilidade gerado para {newAssignment.AssignedToName}.",
            IsDeleted = false
        };
        _context.ITEquipmentDocuments.Add(assignDoc);

        // ── 7. Movement logs ──
        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.UserChangeReturned,
            PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            PreviousOwnerName = prevUserName,
            Notes = $"Troca de utilizador — devolução de {prevUserName}.",
            CreatedByUserId = userId
        });

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.ReturnDocumentGenerated,
            PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            Notes = $"Termo de Devolução gerado para {prevUserName}.",
            CreatedByUserId = userId
        });

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.UserChangeAssigned,
            PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewOwnerName = newAssignment.AssignedToName,
            Notes = $"Troca de utilizador — entrega a {newAssignment.AssignedToName}.",
            CreatedByUserId = userId
        });

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.AgreementGenerated,
            PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            Notes = $"Termo de Responsabilidade gerado para {newAssignment.AssignedToName}.",
            CreatedByUserId = userId
        });

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.UserChanged,
            PreviousStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InUse,
            PreviousOwnerName = prevUserName,
            NewOwnerName = newAssignment.AssignedToName,
            Notes = $"Equipamento transferido de {prevUserName} para {newAssignment.AssignedToName}.",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();

        // ── 8. Emails (post-commit — failures are warnings) ──
        var warnings = new List<string>();

        // -- Return document emails --
        var returnSubject = $"Termo de Devolução de Equipamento de T.I - {eq.AssetTag}";
        var returnHeadline = "Devolução de Equipamento de T.I — Troca de Utilizador";
        var returnNotesHtml = string.IsNullOrWhiteSpace(request.ReturnNotes) ? "Sem observações." : request.ReturnNotes.Trim();
        var returnBodyHtml = $@"
            <p>Olá,</p>
            <p>Segue em anexo o Termo de Devolução referente ao equipamento de T.I devolvido durante a troca de utilizador.</p>
            <p><strong>Equipamento:</strong> {eq.EquipmentType} {eq.Manufacturer} {eq.Model}<br/>
            <strong>Asset Tag:</strong> {eq.AssetTag}<br/>
            <strong>Condição na devolução:</strong> Em bom estado<br/>
            <strong>Transferido de:</strong> {prevUserName}<br/>
            <strong>Transferido para:</strong> {newAssignment.AssignedToName}<br/>
            <strong>Processado por:</strong> {processingUserName}</p>
            <p><strong>Observações:</strong><br/>{returnNotesHtml}</p>
            <p><em>Este é um e-mail automático do Portal Gerencial.</em></p>";

        await SendEmailWithLog(id, prevUserEmail, prevUserName, returnSubject, returnHeadline, returnBodyHtml,
            returnDocResult, ITEquipmentConstants.MovementType.ReturnEmailSent, ITEquipmentConstants.MovementType.ReturnEmailFailed,
            "Termo de Devolução enviado para o utilizador anterior", userId, warnings, ITEquipmentConstants.EquipmentStatus.InUse);

        await SendEmailWithLog(id, processingUserEmail, processingUserName, returnSubject, returnHeadline, returnBodyHtml,
            returnDocResult, ITEquipmentConstants.MovementType.ReturnEmailSent, ITEquipmentConstants.MovementType.ReturnEmailFailed,
            "Termo de Devolução enviado para quem processou a troca", userId, warnings, ITEquipmentConstants.EquipmentStatus.InUse);

        // -- Assignment agreement emails --
        var assignSubject = $"Termo de Responsabilidade de Equipamento de T.I - {eq.AssetTag}";
        var assignHeadline = "Atribuição de Equipamento de T.I — Troca de Utilizador";
        var assignBodyHtml = $@"
            <p>O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi atribuído a <strong>{newAssignment.AssignedToName}</strong> durante uma troca de utilizador.</p>
            <p>Em anexo encontra-se o Termo de Responsabilidade que rege a utilização deste equipamento.</p>
            <p>Por favor, leia atentamente os termos e condições descritos no documento anexo.</p>";

        await SendEmailWithLog(id, newEmail, newAssignment.AssignedToName, assignSubject, assignHeadline, assignBodyHtml,
            assignDocResult, ITEquipmentConstants.MovementType.EmailSent, ITEquipmentConstants.MovementType.EmailFailed,
            "Termo de Responsabilidade enviado para o novo utilizador", userId, warnings, ITEquipmentConstants.EquipmentStatus.InUse);

        await SendEmailWithLog(id, processingUserEmail, processingUserName, assignSubject, assignHeadline, assignBodyHtml,
            assignDocResult, ITEquipmentConstants.MovementType.EmailSent, ITEquipmentConstants.MovementType.EmailFailed,
            "Termo de Responsabilidade enviado para quem processou a troca", userId, warnings, ITEquipmentConstants.EquipmentStatus.InUse);

        await _context.SaveChangesAsync();

        // IT notification for user change
        await NotifyITAsync(eq.AssetTag, "Troca de Utilizador",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi transferido de <strong>{prevUserName}</strong> para <strong>{newAssignment.AssignedToName}</strong>.<br/>Departamento: {newAssignment.AssignedToDepartment ?? "—"}<br/>Planta: {newAssignment.AssignedToPlant ?? "—"}");

        return Ok(new
        {
            success = true,
            equipmentId = id,
            previousAssignmentId = prevAssignmentId,
            newAssignmentId = newAssignment.Id,
            returnDocumentId = returnDoc.Id,
            assignmentAgreementDocumentId = assignDoc.Id,
            warnings = warnings.Count > 0 ? warnings : null
        });
    }

    /// <summary>Helper: send email with movement logging to avoid repetition.</summary>
    private async Task SendEmailWithLog(Guid equipmentId, string email, string name, string subject,
        string headline, string bodyHtml, ITEquipmentAgreementService.AgreementResult doc,
        string successMovement, string failMovement, string notePrefix,
        Guid userId, List<string> warnings, string statusCode)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        try
        {
            var sent = await _emailService.SendWithAttachmentAsync(
                email, name, subject, headline, bodyHtml, doc.FilePath, doc.DisplayFileName);

            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = equipmentId,
                MovementType = sent ? successMovement : failMovement,
                PreviousStatus = statusCode,
                NewStatus = statusCode,
                Notes = sent ? $"{notePrefix}: {email}" : $"Falha — {notePrefix}: {email}",
                CreatedByUserId = userId
            });

            if (!sent) warnings.Add($"Não foi possível enviar o e-mail para {name} ({email}).");
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao enviar e-mail para {name} ({email}).");
            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = equipmentId,
                MovementType = failMovement,
                Notes = $"Falha — {notePrefix}: {email} — {ex.Message}",
                CreatedByUserId = userId
            });
        }
    }

    // ─── POST /api/it/equipment/{id}/send-to-repair ───
    [HttpPost("{id}/send-to-repair")]
    public async Task<IActionResult> SendToRepair(Guid id, [FromBody] RepairRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;

        eq.StatusCode = ITEquipmentConstants.EquipmentStatus.InRepair;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.SentToRepair,
            PreviousStatus = previousStatus,
            NewStatus = ITEquipmentConstants.EquipmentStatus.InRepair,
            Notes = $"Motivo: {request.Reason?.Trim()}\nVendor: {request.RepairVendor?.Trim()}",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();
        await NotifyITAsync(eq.AssetTag, "Equipamento Enviado para Conserto",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi enviado para conserto.<br/>Motivo: {request.Reason?.Trim() ?? "—"}<br/>Vendor: {request.RepairVendor?.Trim() ?? "—"}");
        return Ok(new { message = "Equipamento enviado para conserto." });
    }

    // ─── POST /api/it/equipment/{id}/return-from-repair ───
    [HttpPost("{id}/return-from-repair")]
    public async Task<IActionResult> ReturnFromRepair(Guid id, [FromBody] ReturnFromRepairRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;

        var newStatus = request.Result?.ToUpper() switch
        {
            "NOT_REPAIRABLE" => ITEquipmentConstants.EquipmentStatus.Retired,
            _ => ITEquipmentConstants.EquipmentStatus.Available
        };

        eq.StatusCode = newStatus;
        if (newStatus == ITEquipmentConstants.EquipmentStatus.Retired)
            eq.IsActive = false;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.ReturnedFromRepair,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Notes = $"Resultado: {request.Result}\n{request.Notes?.Trim()}",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();
        await NotifyITAsync(eq.AssetTag, "Equipamento Retornou do Conserto",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) retornou do conserto.<br/>Resultado: {request.Result ?? "—"}");
        return Ok(new { message = "Equipamento retornou do conserto.", newStatus });
    }

    // ─── POST /api/it/equipment/{id}/mark-lost ───
    [HttpPost("{id}/mark-lost")]
    public async Task<IActionResult> MarkLost(Guid id, [FromBody] MarkLostRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;

        // Close active assignment if any
        var activeAssignment = await _context.ITEquipmentAssignments
            .FirstOrDefaultAsync(a => a.EquipmentId == id && a.AssignmentStatus == ITEquipmentConstants.AssignmentStatus.Active);
        if (activeAssignment != null)
        {
            activeAssignment.AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Lost;
            activeAssignment.Notes = string.IsNullOrWhiteSpace(activeAssignment.Notes)
                ? request.Notes?.Trim()
                : $"{activeAssignment.Notes}\nPerda: {request.Notes?.Trim()}";
        }

        eq.StatusCode = ITEquipmentConstants.EquipmentStatus.Lost;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.MarkedAsLost,
            PreviousStatus = previousStatus,
            NewStatus = ITEquipmentConstants.EquipmentStatus.Lost,
            Notes = $"Responsável: {request.ResponsiblePerson?.Trim()}\n{request.Notes?.Trim()}",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();
        await NotifyITAsync(eq.AssetTag, "Equipamento Marcado como Perdido",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi marcado como <strong>PERDIDO</strong>.<br/>Responsável: {request.ResponsiblePerson?.Trim() ?? "—"}");
        return Ok(new { message = "Equipamento marcado como perdido." });
    }

    // ─── POST /api/it/equipment/{id}/reserve ───
    [HttpPost("{id}/reserve")]
    public async Task<IActionResult> Reserve(Guid id, [FromBody] ReserveRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        if (eq.StatusCode != ITEquipmentConstants.EquipmentStatus.Available)
            return BadRequest(new { detail = "Apenas equipamentos disponíveis podem ser reservados." });

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;

        eq.StatusCode = ITEquipmentConstants.EquipmentStatus.Reserved;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.Reserved,
            PreviousStatus = previousStatus,
            NewStatus = ITEquipmentConstants.EquipmentStatus.Reserved,
            Notes = $"Reservado para: {request.ReservedFor?.Trim()}\nMotivo: {request.Reason?.Trim()}",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();

        // IT notification for reservation
        await NotifyITAsync(eq.AssetTag, "Equipamento Reservado",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi reservado.<br/>Para: {request.ReservedFor?.Trim() ?? "—"}<br/>Motivo: {request.Reason?.Trim() ?? "—"}");

        return Ok(new { message = "Equipamento reservado com sucesso." });
    }

    // ─── POST /api/it/equipment/{id}/retire ───
    [HttpPost("{id}/retire")]
    public async Task<IActionResult> Retire(Guid id, [FromBody] RetireRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;

        // Close active assignment if any
        var activeAssignment = await _context.ITEquipmentAssignments
            .FirstOrDefaultAsync(a => a.EquipmentId == id && a.AssignmentStatus == ITEquipmentConstants.AssignmentStatus.Active);
        if (activeAssignment != null)
        {
            activeAssignment.AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Cancelled;
            activeAssignment.ReturnedDate = DateTime.UtcNow;
        }

        eq.StatusCode = ITEquipmentConstants.EquipmentStatus.Retired;
        eq.IsActive = false;
        eq.CurrentOwnerName = null;
        eq.CurrentOwnerUserId = null;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.RetiredMovement,
            PreviousStatus = previousStatus,
            NewStatus = ITEquipmentConstants.EquipmentStatus.Retired,
            Notes = $"Motivo: {request.Reason?.Trim()}\n{request.Notes?.Trim()}",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();
        await NotifyITAsync(eq.AssetTag, "Equipamento Baixado",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi baixado/retirado do inventário ativo.");
        return Ok(new { message = "Equipamento baixado com sucesso." });
    }

    // ─── POST /api/it/equipment/{id}/reactivate ───
    [HttpPost("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, [FromBody] ReactivateRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var eq = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq == null) return NotFound("Equipamento não encontrado.");

        if (eq.StatusCode != ITEquipmentConstants.EquipmentStatus.Retired)
            return BadRequest(new { detail = $"Apenas equipamentos com status 'Baixado' podem ser reativados. Status atual: '{ITEquipmentConstants.EquipmentStatus.DisplayName(eq.StatusCode)}'." });

        var userId = CurrentUserId;
        var previousStatus = eq.StatusCode;
        var newStatus = string.IsNullOrWhiteSpace(request.NewStatus) || request.NewStatus == ITEquipmentConstants.EquipmentStatus.Retired
            ? ITEquipmentConstants.EquipmentStatus.Available
            : request.NewStatus;

        // Validate the target status is valid
        if (!ITEquipmentConstants.EquipmentStatus.All.Contains(newStatus) || newStatus == ITEquipmentConstants.EquipmentStatus.Retired)
            return BadRequest(new { detail = $"Status de destino inválido: '{newStatus}'." });

        eq.StatusCode = newStatus;
        eq.IsActive = true;
        eq.UpdatedAt = DateTime.UtcNow;
        eq.UpdatedByUserId = userId;

        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = id,
            MovementType = ITEquipmentConstants.MovementType.Reactivated,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Notes = $"Equipamento reativado. Motivo: {request.Reason?.Trim() ?? "—"}\n{request.Notes?.Trim() ?? ""}".Trim(),
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();
        await NotifyITAsync(eq.AssetTag, "Reativação de Equipamento",
            $"O equipamento <strong>{eq.AssetTag}</strong> ({eq.EquipmentType}) foi reativado de <strong>Baixado</strong> para <strong>{ITEquipmentConstants.EquipmentStatus.DisplayName(newStatus)}</strong>.");

        return Ok(new { message = "Equipamento reativado com sucesso.", newStatus });
    }

    // ─── Equipment Type CRUD ───

    // GET /api/it/equipment/types
    [HttpGet("types")]
    public async Task<IActionResult> ListEquipmentTypes([FromQuery] bool? activeOnly)
    {
        if (!HasITAccess()) return Forbid();

        var query = _context.ITEquipmentTypes.AsNoTracking().AsQueryable();
        if (activeOnly == true)
            query = query.Where(t => t.IsActive);

        var types = await query.OrderBy(t => t.SortOrder).ThenBy(t => t.DisplayName).ToListAsync();
        return Ok(types.Select(t => new { t.Id, t.Code, t.DisplayName, t.IsActive, t.SortOrder }));
    }

    // POST /api/it/equipment/types
    [HttpPost("types")]
    public async Task<IActionResult> CreateEquipmentType([FromBody] EquipmentTypeRequest request)
    {
        if (!HasITAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { detail = "Código do tipo é obrigatório." });
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { detail = "Nome de exibição é obrigatório." });

        var code = request.Code.Trim().ToUpperInvariant().Replace(" ", "_");
        var exists = await _context.ITEquipmentTypes.AnyAsync(t => t.Code == code);
        if (exists)
            return BadRequest(new { detail = $"Já existe um tipo com o código '{code}'." });

        var maxSort = await _context.ITEquipmentTypes.MaxAsync(t => (int?)t.SortOrder) ?? 0;
        var type = new ITEquipmentType
        {
            Code = code,
            DisplayName = request.DisplayName.Trim(),
            SortOrder = request.SortOrder ?? maxSort + 1,
            IsActive = true
        };

        _context.ITEquipmentTypes.Add(type);
        await _context.SaveChangesAsync();
        return Ok(new { type.Id, type.Code, type.DisplayName, type.IsActive, type.SortOrder });
    }

    // PUT /api/it/equipment/types/{id}
    [HttpPut("types/{id}")]
    public async Task<IActionResult> UpdateEquipmentType(Guid id, [FromBody] EquipmentTypeRequest request)
    {
        if (!HasITAccess()) return Forbid();

        var type = await _context.ITEquipmentTypes.FirstOrDefaultAsync(t => t.Id == id);
        if (type == null) return NotFound("Tipo de equipamento não encontrado.");

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            type.DisplayName = request.DisplayName.Trim();
        if (request.SortOrder.HasValue)
            type.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue)
            type.IsActive = request.IsActive.Value;

        type.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { type.Id, type.Code, type.DisplayName, type.IsActive, type.SortOrder });
    }

    // POST /api/it/equipment/types/{id}/toggle
    [HttpPost("types/{id}/toggle")]
    public async Task<IActionResult> ToggleEquipmentType(Guid id)
    {
        if (!HasITAccess()) return Forbid();

        var type = await _context.ITEquipmentTypes.FirstOrDefaultAsync(t => t.Id == id);
        if (type == null) return NotFound("Tipo de equipamento não encontrado.");

        type.IsActive = !type.IsActive;
        type.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { type.Id, type.Code, type.DisplayName, type.IsActive, type.SortOrder });
    }

    // ═══════════════════════════════════════════════════════════════
    //  MANUFACTURER CRUD
    // ═══════════════════════════════════════════════════════════════

    // GET /api/it/equipment/manufacturers
    [HttpGet("manufacturers")]
    public async Task<IActionResult> ListManufacturers([FromQuery] bool? activeOnly)
    {
        if (!HasITAccess()) return Forbid();
        var query = _context.ITEquipmentManufacturers.AsNoTracking().AsQueryable();
        if (activeOnly == true) query = query.Where(m => m.IsActive);
        var items = await query.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToListAsync();
        return Ok(items.Select(m => new { m.Id, m.Name, m.IsActive, m.SortOrder }));
    }

    // POST /api/it/equipment/manufacturers
    [HttpPost("manufacturers")]
    public async Task<IActionResult> CreateManufacturer([FromBody] CatalogItemRequest request)
    {
        if (!HasITAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { detail = "Nome do fabricante é obrigatório." });

        var name = request.Name.Trim();
        if (await _context.ITEquipmentManufacturers.AnyAsync(m => m.Name == name))
            return BadRequest(new { detail = $"Já existe um fabricante com o nome '{name}'." });

        var maxSort = await _context.ITEquipmentManufacturers.MaxAsync(m => (int?)m.SortOrder) ?? 0;
        var entity = new ITEquipmentManufacturer
        {
            Name = name,
            SortOrder = request.SortOrder ?? maxSort + 1,
            IsActive = true
        };
        _context.ITEquipmentManufacturers.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // PUT /api/it/equipment/manufacturers/{id}
    [HttpPut("manufacturers/{id}")]
    public async Task<IActionResult> UpdateManufacturer(Guid id, [FromBody] CatalogItemRequest request)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentManufacturers.FirstOrDefaultAsync(m => m.Id == id);
        if (entity == null) return NotFound("Fabricante não encontrado.");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            if (await _context.ITEquipmentManufacturers.AnyAsync(m => m.Name == name && m.Id != id))
                return BadRequest(new { detail = $"Já existe um fabricante com o nome '{name}'." });
            entity.Name = name;
        }
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // POST /api/it/equipment/manufacturers/{id}/toggle
    [HttpPost("manufacturers/{id}/toggle")]
    public async Task<IActionResult> ToggleManufacturer(Guid id)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentManufacturers.FirstOrDefaultAsync(m => m.Id == id);
        if (entity == null) return NotFound("Fabricante não encontrado.");
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODEL CRUD
    // ═══════════════════════════════════════════════════════════════

    // GET /api/it/equipment/models
    [HttpGet("models")]
    public async Task<IActionResult> ListModels([FromQuery] bool? activeOnly, [FromQuery] Guid? manufacturerId, [FromQuery] string? equipmentTypeCode)
    {
        if (!HasITAccess()) return Forbid();
        var query = _context.ITEquipmentModels.Include(m => m.Manufacturer).AsNoTracking().AsQueryable();
        if (activeOnly == true) query = query.Where(m => m.IsActive);
        if (manufacturerId.HasValue) query = query.Where(m => m.ManufacturerId == manufacturerId.Value);
        if (!string.IsNullOrWhiteSpace(equipmentTypeCode)) query = query.Where(m => m.EquipmentTypeCode == equipmentTypeCode);
        var items = await query.OrderBy(m => m.Manufacturer.Name).ThenBy(m => m.SortOrder).ThenBy(m => m.Name).ToListAsync();
        return Ok(items.Select(m => new { m.Id, m.ManufacturerId, ManufacturerName = m.Manufacturer.Name, m.EquipmentTypeCode, m.Name, m.IsActive, m.SortOrder }));
    }

    // POST /api/it/equipment/models
    [HttpPost("models")]
    public async Task<IActionResult> CreateModel([FromBody] EquipmentModelRequest request)
    {
        if (!HasITAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { detail = "Nome do modelo é obrigatório." });
        if (!request.ManufacturerId.HasValue)
            return BadRequest(new { detail = "Fabricante é obrigatório." });

        var manufacturer = await _context.ITEquipmentManufacturers.FirstOrDefaultAsync(m => m.Id == request.ManufacturerId.Value);
        if (manufacturer == null) return BadRequest(new { detail = "Fabricante não encontrado." });

        var name = request.Name.Trim();
        if (await _context.ITEquipmentModels.AnyAsync(m => m.ManufacturerId == request.ManufacturerId.Value && m.Name == name))
            return BadRequest(new { detail = $"Já existe um modelo '{name}' para este fabricante." });

        var maxSort = await _context.ITEquipmentModels
            .Where(m => m.ManufacturerId == request.ManufacturerId.Value)
            .MaxAsync(m => (int?)m.SortOrder) ?? 0;

        var entity = new ITEquipmentModel
        {
            ManufacturerId = request.ManufacturerId.Value,
            EquipmentTypeCode = request.EquipmentTypeCode?.Trim().ToUpperInvariant(),
            Name = name,
            SortOrder = request.SortOrder ?? maxSort + 1,
            IsActive = true
        };
        _context.ITEquipmentModels.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.ManufacturerId, ManufacturerName = manufacturer.Name, entity.EquipmentTypeCode, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // PUT /api/it/equipment/models/{id}
    [HttpPut("models/{id}")]
    public async Task<IActionResult> UpdateModel(Guid id, [FromBody] EquipmentModelRequest request)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentModels.Include(m => m.Manufacturer).FirstOrDefaultAsync(m => m.Id == id);
        if (entity == null) return NotFound("Modelo não encontrado.");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            if (await _context.ITEquipmentModels.AnyAsync(m => m.ManufacturerId == entity.ManufacturerId && m.Name == name && m.Id != id))
                return BadRequest(new { detail = $"Já existe um modelo '{name}' para este fabricante." });
            entity.Name = name;
        }
        if (request.EquipmentTypeCode != null) entity.EquipmentTypeCode = request.EquipmentTypeCode.Trim().ToUpperInvariant();
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.ManufacturerId, ManufacturerName = entity.Manufacturer.Name, entity.EquipmentTypeCode, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // POST /api/it/equipment/models/{id}/toggle
    [HttpPost("models/{id}/toggle")]
    public async Task<IActionResult> ToggleModel(Guid id)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentModels.FirstOrDefaultAsync(m => m.Id == id);
        if (entity == null) return NotFound("Modelo não encontrado.");
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // ═══════════════════════════════════════════════════════════════
    //  PROCESSOR CRUD
    // ═══════════════════════════════════════════════════════════════

    // GET /api/it/equipment/processors
    [HttpGet("processors")]
    public async Task<IActionResult> ListProcessors([FromQuery] bool? activeOnly)
    {
        if (!HasITAccess()) return Forbid();
        var query = _context.ITEquipmentProcessors.AsNoTracking().AsQueryable();
        if (activeOnly == true) query = query.Where(p => p.IsActive);
        var items = await query.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();
        return Ok(items.Select(p => new { p.Id, p.Name, p.IsActive, p.SortOrder }));
    }

    // POST /api/it/equipment/processors
    [HttpPost("processors")]
    public async Task<IActionResult> CreateProcessor([FromBody] CatalogItemRequest request)
    {
        if (!HasITAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { detail = "Nome do processador é obrigatório." });

        var name = request.Name.Trim();
        if (await _context.ITEquipmentProcessors.AnyAsync(p => p.Name == name))
            return BadRequest(new { detail = $"Já existe um processador com o nome '{name}'." });

        var maxSort = await _context.ITEquipmentProcessors.MaxAsync(p => (int?)p.SortOrder) ?? 0;
        var entity = new ITEquipmentProcessor
        {
            Name = name,
            SortOrder = request.SortOrder ?? maxSort + 1,
            IsActive = true
        };
        _context.ITEquipmentProcessors.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // PUT /api/it/equipment/processors/{id}
    [HttpPut("processors/{id}")]
    public async Task<IActionResult> UpdateProcessor(Guid id, [FromBody] CatalogItemRequest request)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentProcessors.FirstOrDefaultAsync(p => p.Id == id);
        if (entity == null) return NotFound("Processador não encontrado.");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            if (await _context.ITEquipmentProcessors.AnyAsync(p => p.Name == name && p.Id != id))
                return BadRequest(new { detail = $"Já existe um processador com o nome '{name}'." });
            entity.Name = name;
        }
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // POST /api/it/equipment/processors/{id}/toggle
    [HttpPost("processors/{id}/toggle")]
    public async Task<IActionResult> ToggleProcessor(Guid id)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentProcessors.FirstOrDefaultAsync(p => p.Id == id);
        if (entity == null) return NotFound("Processador não encontrado.");
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.Name, entity.IsActive, entity.SortOrder });
    }

    // ═══════════════════════════════════════════════════════════════
    //  MEMORY OPTION CRUD
    // ═══════════════════════════════════════════════════════════════

    // GET /api/it/equipment/memory-options
    [HttpGet("memory-options")]
    public async Task<IActionResult> ListMemoryOptions([FromQuery] bool? activeOnly)
    {
        if (!HasITAccess()) return Forbid();
        var query = _context.ITEquipmentMemoryOptions.AsNoTracking().AsQueryable();
        if (activeOnly == true) query = query.Where(m => m.IsActive);
        var items = await query.OrderBy(m => m.SortOrder).ThenBy(m => m.DisplayName).ToListAsync();
        return Ok(items.Select(m => new { m.Id, m.DisplayName, m.ValueInGb, m.IsActive, m.SortOrder }));
    }

    // POST /api/it/equipment/memory-options
    [HttpPost("memory-options")]
    public async Task<IActionResult> CreateMemoryOption([FromBody] MemoryOptionRequest request)
    {
        if (!HasITAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { detail = "Nome da opção de memória é obrigatório." });

        var displayName = request.DisplayName.Trim();
        if (await _context.ITEquipmentMemoryOptions.AnyAsync(m => m.DisplayName == displayName))
            return BadRequest(new { detail = $"Já existe uma opção de memória '{displayName}'." });

        var maxSort = await _context.ITEquipmentMemoryOptions.MaxAsync(m => (int?)m.SortOrder) ?? 0;
        var entity = new ITEquipmentMemoryOption
        {
            DisplayName = displayName,
            ValueInGb = request.ValueInGb,
            SortOrder = request.SortOrder ?? maxSort + 1,
            IsActive = true
        };
        _context.ITEquipmentMemoryOptions.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.DisplayName, entity.ValueInGb, entity.IsActive, entity.SortOrder });
    }

    // PUT /api/it/equipment/memory-options/{id}
    [HttpPut("memory-options/{id}")]
    public async Task<IActionResult> UpdateMemoryOption(Guid id, [FromBody] MemoryOptionRequest request)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentMemoryOptions.FirstOrDefaultAsync(m => m.Id == id);
        if (entity == null) return NotFound("Opção de memória não encontrada.");

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            var displayName = request.DisplayName.Trim();
            if (await _context.ITEquipmentMemoryOptions.AnyAsync(m => m.DisplayName == displayName && m.Id != id))
                return BadRequest(new { detail = $"Já existe uma opção de memória '{displayName}'." });
            entity.DisplayName = displayName;
        }
        if (request.ValueInGb.HasValue) entity.ValueInGb = request.ValueInGb;
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.DisplayName, entity.ValueInGb, entity.IsActive, entity.SortOrder });
    }

    // POST /api/it/equipment/memory-options/{id}/toggle
    [HttpPost("memory-options/{id}/toggle")]
    public async Task<IActionResult> ToggleMemoryOption(Guid id)
    {
        if (!HasITAccess()) return Forbid();
        var entity = await _context.ITEquipmentMemoryOptions.FirstOrDefaultAsync(m => m.Id == id);
        if (entity == null) return NotFound("Opção de memória não encontrada.");
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.DisplayName, entity.ValueInGb, entity.IsActive, entity.SortOrder });
    }

    // ─── IT Notification Helper ───
    private async Task NotifyITAsync(string assetTag, string headline, string bodyHtml)
    {
        try
        {
            var email = _configuration["AppConfig:ITNotificationEmail"];
            if (string.IsNullOrWhiteSpace(email)) return;

            await _emailService.SendWorkflowNotificationAsync(email, "Departamento de T.I", $"{headline} — {assetTag}", headline, bodyHtml);
        }
        catch
        {
            // IT notification failure should never block the main operation
        }
    }

    // ─── POST /api/it/equipment/import ── (CSV multipart upload) ───
    [HttpPost("import")]
    public async Task<IActionResult> ImportCsv([FromForm] IFormFile file)
    {
        if (!HasITAccess()) return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { detail = "Nenhum ficheiro CSV enviado." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".csv")
            return BadRequest(new { detail = "Apenas ficheiros .csv são aceites." });

        var userId = CurrentUserId;
        var lines = new List<string>();

        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (line != null) lines.Add(line);
            }
        }

        if (lines.Count < 2)
            return BadRequest(new { detail = "Ficheiro CSV vazio ou sem dados." });

        // Parse header
        var headerLine = lines[0];
        var headers = ParseCsvLine(headerLine);
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim().Replace("\"", "");
            headerMap[h] = i;
        }

        // Validate required column
        if (!headerMap.ContainsKey("Asset Tag") && !headerMap.ContainsKey("AssetTag"))
            return BadRequest(new { detail = "Coluna 'Asset Tag' não encontrada no CSV." });

        var created = 0;
        var skipped = 0;
        var errors = new List<object>();
        var duplicateHostnames = new List<object>();

        // Pre-load existing asset tags for dedup
        var existingAssetTags = await _context.ITEquipments.AsNoTracking()
            .Select(e => e.AssetTag).ToListAsync();
        var existingTagsSet = new HashSet<string>(existingAssetTags, StringComparer.OrdinalIgnoreCase);

        // Track hostnames within the import batch for duplicate detection
        var batchHostnames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existingHostnames = await _context.ITEquipments.AsNoTracking()
            .Where(e => e.Hostname != null && e.Hostname != "")
            .Select(e => e.Hostname!).ToListAsync();
        var existingHostnamesSet = new HashSet<string>(existingHostnames, StringComparer.OrdinalIgnoreCase);

        int GetCol(string name) => headerMap.TryGetValue(name, out var idx) ? idx : -1;
        string? GetVal(string[] cols, string name)
        {
            var idx = GetCol(name);
            if (idx < 0 || idx >= cols.Length) return null;
            var val = cols[idx].Trim().Replace("\"", "");
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }

        for (int lineNum = 1; lineNum < lines.Count; lineNum++)
        {
            try
            {
                var cols = ParseCsvLine(lines[lineNum]);
                var assetTag = GetVal(cols, "Asset Tag") ?? GetVal(cols, "AssetTag");
                if (string.IsNullOrWhiteSpace(assetTag))
                {
                    errors.Add(new { line = lineNum + 1, error = "Asset Tag vazio" });
                    continue;
                }

                if (existingTagsSet.Contains(assetTag))
                {
                    skipped++;
                    continue;
                }

                var hostname = GetVal(cols, "Hostname") ?? GetVal(cols, "Host Name");

                // Check hostname duplicates
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    if (existingHostnamesSet.Contains(hostname))
                    {
                        duplicateHostnames.Add(new { line = lineNum + 1, hostname, conflictWith = "existing" });
                        skipped++;
                        continue;
                    }
                    if (batchHostnames.TryGetValue(hostname, out var prevLine))
                    {
                        duplicateHostnames.Add(new { line = lineNum + 1, hostname, conflictWith = $"line {prevLine}" });
                        skipped++;
                        continue;
                    }
                    batchHostnames[hostname] = lineNum + 1;
                    existingHostnamesSet.Add(hostname);
                }

                var statusRaw = GetVal(cols, "Status");
                var typeRaw = GetVal(cols, "Type") ?? GetVal(cols, "Equipment Type");
                var statusCode = ITEquipmentConstants.NormalizeCsvStatus(statusRaw);
                var equipmentType = ITEquipmentConstants.NormalizeCsvType(typeRaw);

                var currentUser = GetVal(cols, "Current User") ?? GetVal(cols, "User") ?? GetVal(cols, "Owner");
                var biometric = GetVal(cols, "Biometric / MFA") ?? GetVal(cols, "Biometric") ?? GetVal(cols, "MFA");
                var biometricEnabled = biometric?.Trim().ToLowerInvariant() == "sim" || biometric?.Trim().ToLowerInvariant() == "yes" || biometric?.Trim().ToLowerInvariant() == "true";

                var eq = new ITEquipment
                {
                    AssetTag = assetTag.Trim(),
                    Hostname = hostname,
                    Plant = GetVal(cols, "Plant") ?? GetVal(cols, "Planta"),
                    EquipmentType = equipmentType,
                    StatusCode = statusCode,
                    Manufacturer = GetVal(cols, "Manufacturer") ?? GetVal(cols, "Fabricante"),
                    Model = GetVal(cols, "Model") ?? GetVal(cols, "Modelo"),
                    SerialNumber = GetVal(cols, "Serial Number") ?? GetVal(cols, "SerialNumber") ?? GetVal(cols, "S/N"),
                    MacAddress = GetVal(cols, "MAC Address") ?? GetVal(cols, "MacAddress") ?? GetVal(cols, "MAC"),
                    Processor = GetVal(cols, "Processor") ?? GetVal(cols, "CPU") ?? GetVal(cols, "Processador"),
                    MemoryRam = GetVal(cols, "Memory (RAM)") ?? GetVal(cols, "RAM") ?? GetVal(cols, "Memory"),
                    Color = GetVal(cols, "Color") ?? GetVal(cols, "Cor"),
                    BiometricMfaEnabled = biometricEnabled,
                    IdCard = GetVal(cols, "ID Card"),
                    CurrentOwnerName = currentUser,
                    Notes = GetVal(cols, "Notes") ?? GetVal(cols, "Notas") ?? GetVal(cols, "Observations"),
                    SourceType = ITEquipmentConstants.SourceType.ImportedLegacy,
                    IsActive = true,
                    CreatedByUserId = userId
                };

                _context.ITEquipments.Add(eq);
                existingTagsSet.Add(assetTag);

                // Create movement log: IMPORTED
                _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
                {
                    EquipmentId = eq.Id,
                    MovementType = ITEquipmentConstants.MovementType.Imported,
                    NewStatus = statusCode,
                    Notes = $"Importado via CSV. Status original: {statusRaw ?? "(vazio)"}",
                    CreatedByUserId = userId
                });

                // If "In Use" and has an owner, create initial assignment
                if (statusCode == ITEquipmentConstants.EquipmentStatus.InUse && !string.IsNullOrWhiteSpace(currentUser))
                {
                    _context.ITEquipmentAssignments.Add(new ITEquipmentAssignment
                    {
                        EquipmentId = eq.Id,
                        AssignedToName = currentUser.Trim(),
                        AssignedDate = DateTime.UtcNow,
                        AssignmentStatus = ITEquipmentConstants.AssignmentStatus.Active,
                        Notes = "Atribuição inicial criada durante importação CSV.",
                        CreatedByUserId = userId
                    });
                }

                created++;
            }
            catch (Exception ex)
            {
                errors.Add(new { line = lineNum + 1, error = ex.Message });
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Importação concluída: {created} criados, {skipped} ignorados.",
            created,
            skipped,
            totalLines = lines.Count - 1,
            errors,
            duplicateHostnames
        });
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var field = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }
        result.Add(field.ToString());
        return result.ToArray();
    }

    // ─── Request DTOs ───

    public class CreateEquipmentRequest
    {
        public string AssetTag { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public string? Plant { get; set; }
        public string? EquipmentType { get; set; }
        public string? StatusCode { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? MacAddress { get; set; }
        public string? Processor { get; set; }
        public string? MemoryRam { get; set; }
        public string? Color { get; set; }
        public bool? BiometricMfaEnabled { get; set; }
        public string? IdCard { get; set; }
        public string? Notes { get; set; }
        public string? SourceType { get; set; }
        public AcquisitionDto? Acquisition { get; set; }
    }

    public class AcquisitionDto
    {
        public DateTime? AcquisitionDate { get; set; }
        public string? SupplierName { get; set; }
        public string? PurchaseOrderNumber { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? PaymentReference { get; set; }
        public DateTime? PaymentDate { get; set; }
        public decimal? PurchaseAmount { get; set; }
        public string? Currency { get; set; }
        public DateTime? WarrantyStartDate { get; set; }
        public DateTime? WarrantyEndDate { get; set; }
        public string? WarrantyNotes { get; set; }
        public string? AcquisitionNotes { get; set; }
    }

    public class UpdateEquipmentRequest
    {
        public string? AssetTag { get; set; }
        public string? Hostname { get; set; }
        public string? Plant { get; set; }
        public string? EquipmentType { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? MacAddress { get; set; }
        public string? Processor { get; set; }
        public string? MemoryRam { get; set; }
        public string? Color { get; set; }
        public bool? BiometricMfaEnabled { get; set; }
        public string? IdCard { get; set; }
        public string? Notes { get; set; }
    }

    public class AssignRequest
    {
        public Guid? AssignedToUserId { get; set; }
        public string? AssignedToName { get; set; }
        public string? AssignedToEmail { get; set; }
        public string? AssignedToDepartment { get; set; }
        public string? AssignedToPlant { get; set; }
        public DateTime? AssignedDate { get; set; }
        public DateTime? ExpectedReturnDate { get; set; }
        public string? Notes { get; set; }
    }

    public class ReturnRequest
    {
        public DateTime? ReturnDate { get; set; }
        public string? Condition { get; set; } // GOOD, DAMAGED, NEEDS_REPAIR
        public string? Notes { get; set; }
    }

    public class RepairRequest
    {
        public string? Reason { get; set; }
        public string? RepairVendor { get; set; }
        public DateTime? SentDate { get; set; }
        public DateTime? ExpectedReturnDate { get; set; }
        public string? Notes { get; set; }
    }

    public class ReturnFromRepairRequest
    {
        public DateTime? ReturnDate { get; set; }
        public string? Result { get; set; } // REPAIRED, NOT_REPAIRABLE
        public string? Notes { get; set; }
    }

    public class MarkLostRequest
    {
        public DateTime? LossDate { get; set; }
        public string? ResponsiblePerson { get; set; }
        public string? Notes { get; set; }
    }

    public class ReserveRequest
    {
        public string? ReservedFor { get; set; }
        public string? Reason { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public string? Notes { get; set; }
    }

    public class RetireRequest
    {
        public string? Reason { get; set; }
        public DateTime? RetireDate { get; set; }
        public string? Notes { get; set; }
    }

    public class ChangeUserRequest
    {
        public string? ReturnCondition { get; set; } // GOOD, DAMAGED, NEEDS_REPAIR
        public string? ReturnNotes { get; set; }
        public Guid? NewAssignedToUserId { get; set; }
        public string? NewAssignedToName { get; set; }
        public string? NewAssignedToEmail { get; set; }
        public string? NewAssignedToDepartment { get; set; }
        public string? NewAssignedToPlant { get; set; }
        public string? NewAssignmentNotes { get; set; }
        public DateTime? NewExpectedReturnDate { get; set; }
    }

    public class ReactivateRequest
    {
        public string? NewStatus { get; set; } // Target status after reactivation (defaults to AVAILABLE)
        public string? Reason { get; set; }
        public string? Notes { get; set; }
    }

    public class EquipmentTypeRequest
    {
        public string? Code { get; set; }
        public string? DisplayName { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>Generic request for simple catalog items (manufacturers, processors).</summary>
    public class CatalogItemRequest
    {
        public string? Name { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>Request for equipment model CRUD (linked to manufacturer + optional type).</summary>
    public class EquipmentModelRequest
    {
        public Guid? ManufacturerId { get; set; }
        public string? EquipmentTypeCode { get; set; }
        public string? Name { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>Request for memory option CRUD.</summary>
    public class MemoryOptionRequest
    {
        public string? DisplayName { get; set; }
        public int? ValueInGb { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsActive { get; set; }
    }
}
