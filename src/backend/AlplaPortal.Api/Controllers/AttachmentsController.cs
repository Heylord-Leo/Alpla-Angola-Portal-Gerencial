using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IO;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/attachments")]
public class AttachmentsController : BaseController
{
    private readonly string _storagePath;

    public AttachmentsController(ApplicationDbContext context, IWebHostEnvironment env) : base(context)
    {
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

        // 0. Validation: Upload permission based on Status, Request Type and Attachment Type
        bool isUploadable = false;
        string detail = "Não é permitido carregar este tipo de documento no estágio atual do workflow.";
        string statusCode = request.Status!.Code;
        string typeCodeStr = typeCode;

        switch (typeCodeStr)
        {
            case "PROFORMA":
                isUploadable = RequestWorkflowHelper.CanMutateQuotation(statusCode);
                detail = "O documento Proforma só pode ser carregado nos estágios de Rascunho, Reajuste ou Cotação.";
                break;
            case "PO":
                isUploadable = new[] { "APPROVED", "PO_ISSUED", "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "WAITING_RECEIPT" }.Contains(statusCode);
                detail = "O documento P.O só pode ser carregado a partir do estágio de Aprovação.";
                break;
            case "PAYMENT_SCHEDULE":
                isUploadable = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED" }.Contains(statusCode);
                detail = "O Cronograma de Pagamento deve ser carregado nos estágios de emissão de P.O ou agendamento.";
                break;
            case "PAYMENT_PROOF":
                isUploadable = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "WAITING_RECEIPT" }.Contains(statusCode);
                detail = "O Comprovante de Pagamento deve ser carregado nos estágios de emissão de P.O, agendamento, conclusão ou recebimento.";
                break;
            default:
                // For any other types, only allow upload in editable stages
                isUploadable = new[] { "DRAFT", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT", "WAITING_QUOTATION" }.Contains(statusCode);
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
            var extension = Path.GetExtension(f.FileName);
            var storageFileName = $"{fileId}{extension}";
            var filePath = Path.Combine(_storagePath, storageFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await f.CopyToAsync(stream);
            }

            // 2. Create Attachment record
            var attachment = new RequestAttachment
            {
                Id = fileId,
                RequestId = requestId,
                FileName = f.FileName,
                FileExtension = extension,
                FileSizeMBytes = (decimal)f.Length / (1024 * 1024),
                StorageReference = storageFileName,
                AttachmentTypeCode = typeCode,
                UploadedByUserId = actorId,
                UploadedAtUtc = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.RequestAttachments.Add(attachment);
            savedAttachments.Add(new { id = attachment.Id, fileName = attachment.FileName });
        }

        // 3. Add Aggregated History Entry
        string typeLabel = typeCode;
        if (typeCode == "PROFORMA") typeLabel = "Proforma";
        else if (typeCode == "PO") typeLabel = "P.O";
        else if (typeCode == "PAYMENT_SCHEDULE") typeLabel = "Cronograma de Pagamento";
        else if (typeCode == "PAYMENT_PROOF") typeLabel = "Comprovante de Pagamento";

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
            case "PROFORMA":
                isDeletable = RequestWorkflowHelper.CanMutateQuotation(attachment.Request.Status!.Code);
                detail = "O documento Proforma só pode ser removido nos estágios de Rascunho, Reajuste ou Cotação.";
                break;
            case "PO":
                isDeletable = new[] { "APPROVED" }.Contains(attachment.Request.Status!.Code);
                detail = "O documento P.O só pode ser removido enquanto o pedido está no status Aprovado.";
                break;
            case "PAYMENT_SCHEDULE":
                isDeletable = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED" }.Contains(attachment.Request.Status!.Code);
                detail = "O Cronograma de Pagamento só pode ser removido enquanto estiver em emissão de P.O ou agendamento.";
                break;
            case "PAYMENT_PROOF":
                isDeletable = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "WAITING_RECEIPT" }.Contains(attachment.Request.Status!.Code);
                detail = "O Comprovante de Pagamento só pode ser removido antes da finalização do pedido.";
                break;
            default:
                // For any other types, only allow deletion in editable stages
                isDeletable = new[] { "DRAFT", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT", "WAITING_QUOTATION" }.Contains(attachment.Request.Status!.Code);
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
}
