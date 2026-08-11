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

    /// <summary>
    /// Group statuses eligible for a PAYMENT_SCHEDULE attachment upload, evaluated against the
    /// selected RequestPoGroup for QUOTATION requests that have PO groups. Shared by both the
    /// initial uploadability fallback and the group-linkage enforcement below the switch statement,
    /// so the rule is defined exactly once. Mirrors FinancePaymentEligibilityService's
    /// SchedulableGroupStatuses (PO_ISSUED, PAYMENT_REQUEST_SENT, ADVANCE_PAYMENT_REQUIRED) plus
    /// PAYMENT_SCHEDULED, which this same case's existing parent-level check already allows (a
    /// re-upload after scheduling). ADVANCE_PAYMENT_SCHEDULED is deliberately excluded — no
    /// existing rule supports it (ScheduleAdvancePayment only ever runs from
    /// ADVANCE_PAYMENT_REQUIRED, never re-schedules).
    /// </summary>
    private static readonly string[] PaymentScheduleEligibleGroupStatuses =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.AdvancePaymentRequired,
        RequestConstants.Statuses.PaymentScheduled
    };

    public AttachmentsController(ApplicationDbContext context, IWebHostEnvironment env, IOptions<SecurityOptions> securityOptions, Microsoft.Extensions.Configuration.IConfiguration configuration) : base(context)
    {
        _securityOptions = securityOptions.Value;
        
        var storageRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, configuration, "AppConfig:AttachmentsStoragePath", Path.Combine("data", "attachments"));
            
        _storagePath = storageRes.ResolvedPath;
        
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    [HttpPost("upload/{requestId}")]
    public async Task<IActionResult> Upload(Guid requestId, [FromForm] List<IFormFile> files, [FromForm] IFormFile? file, [FromForm] string typeCode, [FromForm] Guid? poGroupId = null)
    {
        // Scoped check: Ensure user can access this request
        var scopedQuery = await GetScopedRequestsQuery();
        var request = await scopedQuery
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.PoGroups)
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
            case AttachmentConstants.Types.Quotation:
                // Quotation/orçamento documents follow the same window as the quotation flow itself.
                isUploadable = RequestWorkflowHelper.CanMutateQuotation(statusCode);
                detail = "O documento de Cotação só pode ser carregado nos estágios de Rascunho, Reajuste ou Cotação.";
                break;
            case AttachmentConstants.Types.PurchaseOrder:
                isUploadable = new[] { "APPROVED", RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.Statuses.PoPartiallyUploaded }.Contains(statusCode);
                // QUOTATION group-first: allow PO upload when the target group is WAITING_PO/WAITING_PO_CORRECTION
                if (!isUploadable && poGroupId.HasValue && request.RequestType?.Code == RequestConstants.Types.Quotation)
                {
                    var targetGroup = await _context.RequestPoGroups.FirstOrDefaultAsync(g => g.Id == poGroupId.Value && g.RequestId == requestId);
                    if (targetGroup != null && new[] { RequestConstants.PoGroupStatuses.WaitingPo, RequestConstants.PoGroupStatuses.WaitingPoCorrection }.Contains(targetGroup.Status))
                    {
                        isUploadable = true;
                    }
                }
                detail = "O documento P.O só pode ser carregado a partir do estágio de Aprovação ou quando devolvido para correção.";
                break;
            case AttachmentConstants.Types.PaymentSchedule:
                isUploadable = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled }.Contains(statusCode);
                // QUOTATION group-first: allow schedule-document upload when the target group is
                // itself in a schedulable/already-scheduled status — mirrors the PurchaseOrder/
                // PaymentProof fallback pattern above, since the parent's aggregated status alone
                // cannot reflect a specific sibling group's state in a multi-group request.
                if (!isUploadable && poGroupId.HasValue && request.RequestType?.Code == RequestConstants.Types.Quotation)
                {
                    var targetScheduleGroup = await _context.RequestPoGroups.FirstOrDefaultAsync(g => g.Id == poGroupId.Value && g.RequestId == requestId);
                    if (targetScheduleGroup != null && PaymentScheduleEligibleGroupStatuses.Contains(targetScheduleGroup.Status))
                    {
                        isUploadable = true;
                    }
                }
                detail = "O Cronograma de Pagamento deve ser carregado nos estágios de emissão de P.O ou agendamento.";
                break;
            case AttachmentConstants.Types.PaymentProof:
                isUploadable = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentCompleted }.Contains(statusCode);
                // QUOTATION group-first: allow payment proof when the target group is in a finance-eligible status
                if (!isUploadable && poGroupId.HasValue && request.RequestType?.Code == RequestConstants.Types.Quotation)
                {
                    var targetPayGroup = await _context.RequestPoGroups.FirstOrDefaultAsync(g => g.Id == poGroupId.Value && g.RequestId == requestId);
                    var financeEligibleGroupStatuses = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled, RequestConstants.Statuses.AdvancePaymentCompleted };
                    if (targetPayGroup != null && financeEligibleGroupStatuses.Contains(targetPayGroup.Status))
                    {
                        isUploadable = true;
                    }
                }
                if (isUploadable && statusCode == RequestConstants.Statuses.AdvancePaymentCompleted && !CurrentUserRoles.Contains(RoleConstants.Finance) && !CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
                {
                    isUploadable = false;
                    detail = "Apenas usuários do Financeiro podem carregar comprovativos após a conclusão do adiantamento.";
                    break;
                }
                detail = "O Comprovante de Pagamento deve ser carregado nos estágios de emissão de P.O, agendamento, adiantamento ou conclusão.";
                break;
            case AttachmentConstants.Types.Receipt:
                isUploadable = new[] { "WAITING_RECEIPT" }.Contains(statusCode);
                if (isUploadable && !CurrentUserRoles.Contains(RoleConstants.Finance) && !CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
                {
                    isUploadable = false;
                    detail = "Apenas usuários do Financeiro podem carregar o Recibo.";
                    break;
                }
                detail = "O Recibo só pode ser carregado no estágio de Aguardando Recibo.";
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

        // ── QUOTATION group-linkage enforcement for PO and PaymentProof ──
        // When a QUOTATION request has PO groups, PO/PaymentProof uploads MUST be linked to a valid group.
        // PAYMENT requests and legacy QUOTATION without groups preserve existing behavior.
        var isQuotationWithGroups = request.RequestType?.Code == RequestConstants.Types.Quotation
            && request.PoGroups != null && request.PoGroups.Any();

        if (isQuotationWithGroups && typeCodeStr == AttachmentConstants.Types.PurchaseOrder)
        {
            if (!poGroupId.HasValue)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Grupo P.O. Obrigatório",
                    Detail = "Para pedidos do tipo Cotação com grupos de P.O., é obrigatório informar o RequestPoGroupId ao carregar a P.O.",
                    Status = 400
                });
            }
            var poGroup = request.PoGroups.FirstOrDefault(g => g.Id == poGroupId.Value);
            if (poGroup == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Grupo P.O. Inválido",
                    Detail = "O RequestPoGroupId informado não pertence a este pedido.",
                    Status = 400
                });
            }
            var allowedPoGroupStatuses = new[] { RequestConstants.PoGroupStatuses.WaitingPo, RequestConstants.PoGroupStatuses.WaitingPoCorrection };
            if (!allowedPoGroupStatuses.Contains(poGroup.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Upload Bloqueado",
                    Detail = $"O Grupo P.O. não está em status que permita upload de P.O. Status atual: {poGroup.Status}. Status permitidos: {string.Join(", ", allowedPoGroupStatuses)}.",
                    Status = 400
                });
            }
        }

        if (isQuotationWithGroups && typeCodeStr == AttachmentConstants.Types.PaymentProof)
        {
            if (!poGroupId.HasValue)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Grupo P.O. Obrigatório",
                    Detail = "Para pedidos do tipo Cotação com grupos de P.O., é obrigatório informar o RequestPoGroupId ao carregar o comprovante de pagamento.",
                    Status = 400
                });
            }
            var payGroup = request.PoGroups.FirstOrDefault(g => g.Id == poGroupId.Value);
            if (payGroup == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Grupo P.O. Inválido",
                    Detail = "O RequestPoGroupId informado não pertence a este pedido.",
                    Status = 400
                });
            }
            var financeEligible = new[] { 
                RequestConstants.Statuses.PoIssued, 
                RequestConstants.Statuses.PaymentScheduled, 
                RequestConstants.Statuses.PaymentCompleted, 
                RequestConstants.Statuses.InFollowup, 
                RequestConstants.Statuses.AdvancePaymentRequired, 
                RequestConstants.Statuses.AdvancePaymentScheduled, 
                RequestConstants.Statuses.AdvancePaymentCompleted 
            };
            if (!financeEligible.Contains(payGroup.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Upload Bloqueado",
                    Detail = $"O Grupo P.O. não está em status que permita upload de comprovante de pagamento. Status atual: {payGroup.Status}.",
                    Status = 400
                });
            }
        }

        if (isQuotationWithGroups && typeCodeStr == AttachmentConstants.Types.PaymentSchedule)
        {
            if (!poGroupId.HasValue)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Grupo P.O. Obrigatório",
                    Detail = "Para pedidos do tipo Cotação com grupos de P.O., é obrigatório informar o RequestPoGroupId ao carregar o cronograma de pagamento.",
                    Status = 400
                });
            }
            var scheduleGroup = request.PoGroups.FirstOrDefault(g => g.Id == poGroupId.Value);
            if (scheduleGroup == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Grupo P.O. Inválido",
                    Detail = "O RequestPoGroupId informado não pertence a este pedido.",
                    Status = 400
                });
            }
            if (!PaymentScheduleEligibleGroupStatuses.Contains(scheduleGroup.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Upload Bloqueado",
                    Detail = $"O Grupo P.O. não está em status que permita upload de cronograma de pagamento. Status atual: {scheduleGroup.Status}. Status permitidos: {string.Join(", ", PaymentScheduleEligibleGroupStatuses)}.",
                    Status = 400
                });
            }
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
                RequestPoGroupId = poGroupId,
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
        else if (typeCode == AttachmentConstants.Types.Quotation) typeLabel = "Cotação";
        else if (typeCode == AttachmentConstants.Types.PurchaseOrder) typeLabel = "P.O";
        else if (typeCode == AttachmentConstants.Types.PaymentSchedule) typeLabel = "Cronograma de Pagamento";
        else if (typeCode == AttachmentConstants.Types.PaymentProof) typeLabel = "Comprovante de Pagamento";
        else if (typeCode == AttachmentConstants.Types.Receipt) typeLabel = "Recibo";

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

            if (!hasAccess)
            {
                // The binary duplicate is real, but the current user has no access to the
                // original request — confirm the duplicate without disclosing ANY identifying
                // metadata about a request/document/user they cannot see.
                return Ok(new { isDuplicate = true });
            }

            return Ok(new {
                isDuplicate = true,
                requestNumber = attachment.Request.RequestNumber,
                requestId = attachment.RequestId,
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
        if (attachment == null) 
        {
            return NotFound();
        }

        // Optional Scoped Check for Download
        var scopedQuery = await GetScopedRequestsQuery();
        var hasAccess = await scopedQuery.AnyAsync(r => r.Id == attachment.RequestId);
        if (!hasAccess) 
        {
            return Forbid("Sem permissão para baixar este anexo.");
        }

        var filePath = Path.Combine(_storagePath, attachment.StorageReference);
        
        if (!System.IO.File.Exists(filePath)) 
        {
            return NotFound("Arquivo físico não encontrado.");
        }

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
            case AttachmentConstants.Types.Quotation:
                isDeletable = RequestWorkflowHelper.CanMutateQuotation(attachment.Request.Status!.Code);
                detail = "O documento de Cotação só pode ser removido nos estágios de Rascunho, Reajuste ou Cotação.";
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
            case AttachmentConstants.Types.Receipt:
                isDeletable = new[] { "WAITING_RECEIPT" }.Contains(attachment.Request.Status!.Code);
                if (isDeletable && !CurrentUserRoles.Contains(RoleConstants.Finance) && !CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
                {
                    isDeletable = false;
                    detail = "Apenas usuários do Financeiro podem remover o Recibo.";
                    break;
                }
                detail = "O Recibo só pode ser removido no estágio de Aguardando Recibo.";
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
