using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/it/equipment/{equipmentId}/documents")]
public class ITEquipmentDocumentsController : BaseController
{
    private readonly string _storagePath;

    public ITEquipmentDocumentsController(ApplicationDbContext context, IWebHostEnvironment env) : base(context)
    {
        string rootDir = env.ContentRootPath;
        var sep = Path.DirectorySeparatorChar.ToString();
        var srcToken = $"{sep}src{sep}";
        var srcIdx = rootDir.IndexOf(srcToken, StringComparison.OrdinalIgnoreCase);
        if (srcIdx > 0)
        {
            rootDir = rootDir.Substring(0, srcIdx);
        }
        else
        {
            rootDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", ".."));
        }

        _storagePath = Path.GetFullPath(Path.Combine(rootDir, "data", "attachments", "it-equipment"));

        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    private bool HasITAccess()
    {
        return CurrentUserRoles.Contains(RoleConstants.IT) ||
               CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);
    }

    // ─── POST /api/it/equipment/{equipmentId}/documents/upload ───
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(Guid equipmentId, [FromForm] IFormFile file, [FromForm] string documentType, [FromForm] string? notes, [FromForm] Guid? acquisitionId, [FromForm] Guid? assignmentId)
    {
        if (!HasITAccess()) return Forbid();

        var equipment = await _context.ITEquipments.FirstOrDefaultAsync(e => e.Id == equipmentId);
        if (equipment == null) return NotFound("Equipamento não encontrado.");

        if (file == null || file.Length == 0) return BadRequest("Nenhum ficheiro enviado.");

        // Size limit: 25MB
        if (file.Length > 25 * 1024 * 1024)
            return BadRequest("O ficheiro excede o limite de 25MB.");

        // Validate file extension for signed terms: PDF, JPG, JPEG, PNG only
        var allowedSignedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
        var isSignedTermType = documentType == ITEquipmentConstants.DocumentType.SignedAssignmentAgreement
                            || documentType == ITEquipmentConstants.DocumentType.SignedReturnAgreement;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (isSignedTermType && !allowedSignedExtensions.Contains(extension))
            return BadRequest("Formato de ficheiro não permitido para termos assinados. Utilize PDF, JPG ou PNG.");

        // Validate PURCHASE_DOCUMENT: 10MB limit, PDF/JPG/PNG only
        var isPurchaseDocument = documentType == ITEquipmentConstants.DocumentType.PurchaseDocument;
        if (isPurchaseDocument)
        {
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest("O ficheiro de compra excede o limite de 10MB.");
            if (!allowedSignedExtensions.Contains(extension))
                return BadRequest("Formato de ficheiro não permitido para documentos de compra. Utilize PDF, JPG ou PNG.");
        }

        // Validate assignmentId exists when uploading signed terms
        if (isSignedTermType && assignmentId.HasValue)
        {
            var assignmentExists = await _context.ITEquipmentAssignments
                .AnyAsync(a => a.Id == assignmentId.Value && a.EquipmentId == equipmentId);
            if (!assignmentExists)
                return BadRequest("Atribuição não encontrada para este equipamento.");
        }

        var userId = CurrentUserId;
        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}{extension}";
        var filePath = Path.Combine(_storagePath, storageFileName);

        string fileHash;
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        using (var streamForHash = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(streamForHash);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var sanitizedName = SanitizeFileName(file.FileName);

        var doc = new ITEquipmentDocument
        {
            Id = fileId,
            EquipmentId = equipmentId,
            AcquisitionId = acquisitionId,
            AssignmentId = assignmentId,
            DocumentType = documentType ?? ITEquipmentConstants.DocumentType.Other,
            FileName = sanitizedName,
            StorageReference = storageFileName,
            FileHash = fileHash,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = userId,
            Notes = notes?.Trim(),
            IsDeleted = false
        };

        _context.ITEquipmentDocuments.Add(doc);

        // Movement log
        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = equipmentId,
            MovementType = ITEquipmentConstants.MovementType.Updated,
            PreviousStatus = equipment.StatusCode,
            NewStatus = equipment.StatusCode,
            Notes = $"Documento '{sanitizedName}' ({ITEquipmentConstants.DocumentType.DisplayName(doc.DocumentType)}) adicionado.",
            CreatedByUserId = userId
        });

        equipment.UpdatedAt = DateTime.UtcNow;
        equipment.UpdatedByUserId = userId;

        // Clear PurchaseDocumentPending flag when a purchase document is uploaded
        if (isPurchaseDocument && equipment.PurchaseDocumentPending)
        {
            equipment.PurchaseDocumentPending = false;
            _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
            {
                EquipmentId = equipmentId,
                MovementType = ITEquipmentConstants.MovementType.Updated,
                PreviousStatus = equipment.StatusCode,
                NewStatus = equipment.StatusCode,
                Notes = "Documento de compra/entrega carregado. Cadastro completo.",
                CreatedByUserId = userId
            });
        }

        await _context.SaveChangesAsync();

        return Ok(new { id = doc.Id, fileName = doc.FileName });
    }

    // ─── GET /api/it/equipment/{equipmentId}/documents ───
    [HttpGet]
    public async Task<IActionResult> List(Guid equipmentId)
    {
        if (!HasITAccess()) return Forbid();

        var docs = await _context.ITEquipmentDocuments.AsNoTracking()
            .Where(d => d.EquipmentId == equipmentId && !d.IsDeleted)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new
            {
                d.Id, d.DocumentType, d.FileName, d.UploadedAt, d.Notes, d.AcquisitionId,
                UploadedByName = d.UploadedByUser != null ? d.UploadedByUser.FullName : null
            })
            .ToListAsync();

        return Ok(docs);
    }

    // ─── GET /api/it/equipment/{equipmentId}/documents/{docId}/download ───
    [HttpGet("{docId}/download")]
    public async Task<IActionResult> Download(Guid equipmentId, Guid docId)
    {
        if (!HasITAccess()) return Forbid();

        var doc = await _context.ITEquipmentDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.EquipmentId == equipmentId && !d.IsDeleted);
        if (doc == null) return NotFound("Documento não encontrado.");

        var filePath = Path.Combine(_storagePath, doc.StorageReference);
        if (!System.IO.File.Exists(filePath))
            return NotFound("Ficheiro físico não encontrado.");

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);

        // Auto-detect MIME type for correct browser behavior (PDF viewer, DOCX download)
        var ext = Path.GetExtension(doc.FileName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };

        return File(bytes, contentType, doc.FileName);
    }

    // ─── DELETE /api/it/equipment/{equipmentId}/documents/{docId} ───
    [HttpDelete("{docId}")]
    public async Task<IActionResult> Delete(Guid equipmentId, Guid docId)
    {
        if (!HasITAccess()) return Forbid();

        var doc = await _context.ITEquipmentDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.EquipmentId == equipmentId);
        if (doc == null) return NotFound("Documento não encontrado.");

        doc.IsDeleted = true;

        var userId = CurrentUserId;
        _context.ITEquipmentMovementLogs.Add(new ITEquipmentMovementLog
        {
            EquipmentId = equipmentId,
            MovementType = ITEquipmentConstants.MovementType.Updated,
            Notes = $"Documento '{doc.FileName}' removido.",
            CreatedByUserId = userId
        });

        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "unnamed_file";
        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string sanitized = Regex.Replace(nameOnly, @"[^a-zA-Z0-9\s\-_]", "");
        sanitized = sanitized.Replace(" ", "_").Trim();
        if (string.IsNullOrEmpty(sanitized)) sanitized = "file";
        if (sanitized.Length > 100) sanitized = sanitized.Substring(0, 100);
        return sanitized + extension.ToLowerInvariant();
    }
}
