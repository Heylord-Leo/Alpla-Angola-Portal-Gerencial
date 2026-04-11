using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Constants;
using System.IO;
using System.Text.RegularExpressions;
using AlplaPortal.Application.Models.Configuration;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/attachments")]
public class AttachmentsController : BaseController
{
    private readonly string _storagePath;
    private readonly SecurityOptions _securityOptions;

    public AttachmentsController(ApplicationDbContext context, IWebHostEnvironment env, IOptions<SecurityOptions> securityOptions) : base(context)
    {
        _securityOptions = securityOptions.Value;
        // Local storage path for V1: c:\dev\alpla-portal\data\attachments
        _storagePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "..", "data", "attachments"));
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    [HttpPost("upload/{requestId}")]
    public async Task<IActionResult> Upload(Guid requestId, [FromForm] List<IFormFile> files, [FromForm] IFormFile? file, [FromForm] string typeCode)
    {
        // Scoped check: Ensure user can access this request
        var scopedQuery = await GetScopedRequestsQuery();
        var request = await scopedQuery
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        
        if (request == null) return NotFound("Pedido não encontrado ou sem permissão de acesso.");

        // Backward compatibility: check both 'files' list and single 'file' parameter
        var filesToProcess = new List<IFormFile>();
        if (files != null && files.Count > 0) 
        {
            filesToProcess.AddRange(files);
        }
        else if (file != null) 
        {
            filesToProcess.Add(file);
        }

        if (filesToProcess.Count == 0) return BadRequest("Nenhum arquivo enviado.");

        // 0. Security Validation: Whitelist and Size
        foreach (var f in filesToProcess)
        {
            var extension = Path.GetExtension(f.FileName).ToLowerInvariant();
            
            // a) Check Whitelist
            if (!_securityOptions.Upload.AllowedExtensions.Contains(extension))
            {
                return BadRequest($"Extensão de arquivo não permitida: {extension}. Tipos permitidos: {string.Join(", ", _securityOptions.Upload.AllowedExtensions)}");
            }

            // b) Check Blocklist (Second layer)
            if (_securityOptions.Upload.BlockedExtensions.Contains(extension))
            {
                return BadRequest("O ficheiro enviado contém uma extensão proibida por motivos de segurança.");
            }

            // c) Check Size
            if (f.Length > _securityOptions.Upload.MaxFileSizeBytes)
            {
                return BadRequest($"O ficheiro {f.FileName} excede o limite de tamanho permitido (máximo {(_securityOptions.Upload.MaxFileSizeBytes / (1024 * 1024))}MB).");
            }

            // d) Content-Type consistency (Basic signal)
            bool isTypeConsistent = CheckContentTypeConsistency(extension, f.ContentType);
            if (!isTypeConsistent)
            {
                 // We don't block yet to avoid false positives, but we log it as a warning
                 Console.WriteLine($"[SECURITY WARNING] Extension/Mime mismatch: {extension} vs {f.ContentType} for file {f.FileName}");
            }
        }

        // 1. Validation: Upload permission based on Status, Request Type and Attachment Type
        bool isUploadable = false;
        string detail = "Não é permitido carregar este tipo de documento no estágio atual do workflow.";
        string statusCode = request.Status!.Code;
        string typeCodeStr = typeCode;

        switch (typeCodeStr)
        {
            case AttachmentConstants.Types.Proforma:
                isUploadable = RequestWorkflowHelper.CanMutateQuotation(statusCode);
                detail = "O documento Proforma só pode ser carregado nos estágios de Rascunho, Reajuste ou Cotação.";
                break;
            case AttachmentConstants.Types.PurchaseOrder:
                isUploadable = new[] { "APPROVED", RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.WaitingPoCorrection }.Contains(statusCode);
                detail = "O documento P.O só pode ser carregado a partir do estágio de Aprovação ou quando devolvido para correção.";
                break;
            case AttachmentConstants.Types.PaymentSchedule:
                isUploadable = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled }.Contains(statusCode);
                detail = "O Cronograma de Pagamento deve ser carregado nos estágios de emissão de P.O ou agendamento.";
                break;
            case AttachmentConstants.Types.PaymentProof:
                isUploadable = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup }.Contains(statusCode);
                detail = "O Comprovante de Pagamento deve ser carregado nos estágios de emissão de P.O, agendamento, conclusão ou recebimento.";
                break;
            default:
                // For any other types, only allow upload in editable stages
                isUploadable = new[] { RequestConstants.Statuses.Draft, RequestConstants.Statuses.AreaAdjustment, RequestConstants.Statuses.FinalAdjustment, RequestConstants.Statuses.WaitingQuotation }.Contains(statusCode);
                detail = "Este documento só pode ser carregado em estágios editáveis.";
                break;
        }

        if (!isUploadable)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Upload Bloqueado",
                Detail = detail,
                Status = 400
            });
        }

        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized("Usuário não identificado.");

        var savedAttachments = new List<object>();

        foreach (var f in filesToProcess)
        {
            // 1. Generate local storage path
            var fileId = Guid.NewGuid();
            var extension = Path.GetExtension(f.FileName).ToLowerInvariant();
            var storageFileName = $"{fileId}{extension}"; // Guaranteed unique and extension-safe
            var filePath = Path.Combine(_storagePath, storageFileName);

            var fileHash = string.Empty;
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await f.CopyToAsync(stream);
            }

            // Compute hash for anti-duplication
            using (var streamForHash = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = await sha256.ComputeHashAsync(streamForHash);
                fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }

            // Sanitized name for display/audit
            var sanitizedDisplayFileName = SanitizeFileName(f.FileName);

            // 2. Create Attachment record
            var attachment = new RequestAttachment
            {
                Id = fileId,
                RequestId = requestId,
                FileName = sanitizedDisplayFileName, // Audit-safe and display-safe
                FileExtension = extension,
                FileSizeMBytes = (decimal)f.Length / (1024 * 1024),
                StorageReference = storageFileName, // Physical link
                AttachmentTypeCode = typeCode,
                FileHash = fileHash,
                UploadedByUserId = actorId,
                UploadedAtUtc = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.RequestAttachments.Add(attachment);
            savedAttachments.Add(new { id = attachment.Id, fileName = attachment.FileName });
        }

        // 3. Add Aggregated History Entry
        string typeLabel = typeCode;
        if (typeCode == AttachmentConstants.Types.Proforma) typeLabel = "Proforma";
        else if (typeCode == AttachmentConstants.Types.PurchaseOrder) typeLabel = "P.O";
        else if (typeCode == AttachmentConstants.Types.PaymentSchedule) typeLabel = "Cronograma de Pagamento";
        else if (typeCode == AttachmentConstants.Types.PaymentProof) typeLabel = "Comprovante de Pagamento";

        string comment = filesToProcess.Count == 1 
            ? $"Documento \"{filesToProcess[0].FileName}\" ({typeLabel}) adicionado ao pedido por {user.FullName}."
            : $"{filesToProcess.Count} documentos do tipo {typeLabel} adicionados ao pedido por {user.FullName}.";

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "DOCUMENTO ADICIONADO",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = comment,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.RequestStatusHistories.Add(history);
        
        // 4. Update parent request modification metadata
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();

        return Ok(savedAttachments);
    }

    [HttpGet("check-duplicate")]
    public async Task<IActionResult> CheckDuplicate([FromQuery] string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return BadRequest();

        var scopedQuery = await GetScopedRequestsQuery();
        
        var attachment = await _context.RequestAttachments
            .Include(a => a.Request)
            .Include(a => a.UploadedByUser)
            .Where(a => a.FileHash == hash && !a.IsDeleted)
            .FirstOrDefaultAsync();

        if (attachment != null)
        {
            // Calculate if the current user has access to that specific request
            bool hasAccess = await scopedQuery.AnyAsync(r => r.Id == attachment.RequestId);
            
            return Ok(new { 
                isDuplicate = true, 
                requestNumber = attachment.Request.RequestNumber, 
                requestId = hasAccess ? attachment.RequestId : (Guid?)null, // Only expose ID for navigation if they have access
                uploadedBy = attachment.UploadedByUser?.FullName ?? "Usuário Desconhecido",
                createdAtUtc = attachment.UploadedAtUtc 
            });
        }

        return Ok(new { isDuplicate = false });
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var attachment = await _context.RequestAttachments.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (attachment == null) return NotFound();

        // Optional Scoped Check for Download
        var scopedQuery = await GetScopedRequestsQuery();
        var hasAccess = await scopedQuery.AnyAsync(r => r.Id == attachment.RequestId);
        if (!hasAccess) return Forbid("Sem permissão para baixar este anexo.");

        var filePath = Path.Combine(_storagePath, attachment.StorageReference);
        if (!System.IO.File.Exists(filePath)) return NotFound("Arquivo físico não encontrado.");

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/octet-stream", attachment.FileName);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var attachment = await _context.RequestAttachments
            .Include(a => a.Request)
            .ThenInclude(r => r.Status)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attachment == null) return NotFound();

        // Scoped check
        var scopedQuery = await GetScopedRequestsQuery();
        var hasAccess = await scopedQuery.AnyAsync(r => r.Id == attachment.RequestId);
        if (!hasAccess) return Forbid("Sem permissão para remover este anexo.");

        // Constraint: Deletion is only allowed when the request is in a specific status for that attachment type
        bool isDeletable = false;
        string detail = "Não é permitido excluir este tipo de documento no estágio atual do workflow.";

        switch (attachment.AttachmentTypeCode)
        {
            case AttachmentConstants.Types.Proforma:
                isDeletable = RequestWorkflowHelper.CanMutateQuotation(attachment.Request.Status!.Code);
                detail = "O documento Proforma só pode ser removido nos estágios de Rascunho, Reajuste ou Cotação.";
                break;
            case AttachmentConstants.Types.PurchaseOrder:
                isDeletable = new[] { "APPROVED", RequestConstants.Statuses.WaitingPoCorrection }.Contains(attachment.Request.Status!.Code);
                detail = "O documento P.O só pode ser removido enquanto o pedido está no status Aprovado ou devolvido para correção.";
                break;
            case AttachmentConstants.Types.PaymentSchedule:
                isDeletable = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled }.Contains(attachment.Request.Status!.Code);
                detail = "O Cronograma de Pagamento só pode ser removido enquanto estiver em emissão de P.O ou agendamento.";
                break;
            case AttachmentConstants.Types.PaymentProof:
                isDeletable = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup }.Contains(attachment.Request.Status!.Code);
                detail = "O Comprovante de Pagamento só pode ser removido antes da finalização do pedido.";
                break;
            default:
                // For any other types, only allow deletion in editable stages
                isDeletable = new[] { RequestConstants.Statuses.Draft, RequestConstants.Statuses.AreaAdjustment, RequestConstants.Statuses.FinalAdjustment, RequestConstants.Statuses.WaitingQuotation }.Contains(attachment.Request.Status!.Code);
                detail = "Este documento só pode ser removido em estágios editáveis.";
                break;
        }

        if (!isDeletable)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Exclusão Bloqueada",
                Detail = detail,
                Status = 400
            });
        }

        attachment.IsDeleted = true;

        // Record removal in history
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user != null)
        {
            var history = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = attachment.RequestId,
                ActorUserId = actorId,
                ActionTaken = "DOCUMENTO REMOVIDO",
                PreviousStatusId = attachment.Request.StatusId,
                NewStatusId = attachment.Request.StatusId, // Preserve status
                Comment = $"Documento \"{attachment.FileName}\" ({attachment.AttachmentTypeCode}) removido do pedido por {user.FullName}.",
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(history);

            // Update parent request modification metadata to enable workflow progression
            attachment.Request.UpdatedAtUtc = DateTime.UtcNow;
            attachment.Request.UpdatedByUserId = actorId;
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "unnamed_file";

        // 1. Get name and extension separatly to avoid messing with the dot
        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        // 2. Remove any character that is not alphanumeric, space, hyphen or underscore
        // This prevents path traversal (../) and other malicious character injections in UI
        string sanitized = Regex.Replace(nameOnly, @"[^a-zA-Z0-9\s-_]", "");
        
        // 3. Replace spaces with underscores and trim
        sanitized = sanitized.Replace(" ", "_").Trim();

        if (string.IsNullOrEmpty(sanitized)) sanitized = "file";

        // 4. Truncate if too long (max 100 chars for DB safety)
        if (sanitized.Length > 100) sanitized = sanitized.Substring(0, 100);

        return sanitized + extension.ToLowerInvariant();
    }

    private bool CheckContentTypeConsistency(string extension, string contentType)
    {
        // Basic mapping of allowed extensions to expected MIME types
        // This is a "soft" check in Phase 1
        return extension switch
        {
            ".pdf" => contentType == "application/pdf",
            ".jpg" or ".jpeg" => contentType == "image/jpeg",
            ".png" => contentType == "image/png",
            ".docx" => contentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => contentType == "application/msword",
            ".xlsx" => contentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => contentType == "application/vnd.ms-excel",
            _ => true // Skip for others or if unknown
        };
    }
}
