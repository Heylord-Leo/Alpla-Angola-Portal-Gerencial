using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Central notification orchestrator for workflow events.
/// Resolves recipients per event type, builds contextual messages, and dispatches
/// through both in-app notification persistence and transactional email channels.
///
/// <para><b>Architecture Note:</b> This class is the single authority for "who gets notified, 
/// what they see, and through which channel" for all workflow transitions. Controllers should 
/// never build notification logic themselves — they emit a <see cref="WorkflowEvent"/> and 
/// this orchestrator handles the rest.</para>
/// </summary>
public class WorkflowNotificationOrchestrator : IWorkflowNotificationOrchestrator
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkflowNotificationOrchestrator> _logger;

    public WorkflowNotificationOrchestrator(
        ApplicationDbContext context,
        INotificationService notificationService,
        IEmailService emailService,
        IConfiguration config,
        ILogger<WorkflowNotificationOrchestrator> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task EmitAsync(WorkflowEvent evt)
    {
        try
        {
            _logger.LogInformation(
                "Workflow notification event: {EventCode} for Request {RequestId} (Action: {ActionTaken}, CorrelationId: {CorrelationId})",
                evt.EventCode, evt.RequestId, evt.ActionTaken, evt.CorrelationId);

            var eventConfig = await ResolveEventConfigAsync(evt);
            if (eventConfig == null)
            {
                _logger.LogDebug("No notification config for event {EventCode} — skipping", evt.EventCode);
                return;
            }

            var recipients = await ResolveRecipientsAsync(evt);
            if (!recipients.Any())
            {
                _logger.LogWarning("No recipients resolved for event {EventCode} on Request {RequestId}", evt.EventCode, evt.RequestId);
                return;
            }

            foreach (var recipient in recipients)
            {
                // Self-notify restriction has been lifted based on business requirements.
                // Every actor gets notified of their own actions to preserve email history.
                
                await DispatchToRecipientAsync(evt, eventConfig, recipient);
            }
        }
        catch (Exception ex)
        {
            // Bubble up the exception so the Controller knows that an orchestration path failed.
            _logger.LogError(ex, "Workflow notification orchestration failed for event {EventCode} on Request {RequestId}. Bubbling up.",
                evt.EventCode, evt.RequestId);
            throw;
        }
    }

    // =====================================================================
    // RECIPIENT RESOLUTION
    // =====================================================================

    private async Task<List<NotificationRecipient>> ResolveRecipientsAsync(WorkflowEvent evt)
    {
        var recipients = new List<NotificationRecipient>();
        
        // Context variables for specialized override logic
        var reqRef = $"{evt.RequestNumber}";
        var commentHtml = !string.IsNullOrWhiteSpace(evt.Comment)
            ? $"<br/><br/><b>Justificativa:</b><blockquote style='border-left: 4px solid #dc3545; margin: 10px 0; padding-left: 10px; color: #555;'>{System.Net.WebUtility.HtmlEncode(evt.Comment)}</blockquote>"
            : "";

        switch (evt.EventCode)
        {
            // --- Approval Flow: notify the next decision-maker ---
            case WorkflowEventCodes.RequestSubmitted:
                await AddUserRecipientAsync(recipients, evt.AreaApproverId);
                break;

            case WorkflowEventCodes.SubmissionConfirmed:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;

            case WorkflowEventCodes.AreaApproved:
                await AddUserRecipientAsync(recipients, evt.FinalApproverId); // Send default to Final Approver
                await HandleAreaFanningOverridesAsync(recipients, evt, reqRef, commentHtml, isApproval: true);
                break;

            case WorkflowEventCodes.FinalApproved:
                await AddUserRecipientAsync(recipients, evt.FinalApproverId);
                break;

            case WorkflowEventCodes.QuotationCompleted:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;

            // --- Rejection/Adjustment: notify the requester ---
            case WorkflowEventCodes.AreaRejected:
            case WorkflowEventCodes.AreaAdjustment:
                // Requester becomes part of the fanning overrides, handled centrally
                await HandleAreaFanningOverridesAsync(recipients, evt, reqRef, commentHtml, isApproval: false);
                break;

            case WorkflowEventCodes.FinalRejected:
            case WorkflowEventCodes.FinalAdjustment:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;

            // --- PO Registered: notify Finance users (plant-scoped) ---
            case WorkflowEventCodes.PoRegistered:
                await AddPlantScopedFinanceRecipientsAsync(recipients, evt.PlantId);
                break;

            // --- Payment Progress: notify the requester ---
            case WorkflowEventCodes.PaymentScheduled:
            case WorkflowEventCodes.PaymentCompleted:
                await HandlePaymentFanningOverridesAsync(recipients, evt, reqRef);
                break;

            // --- Finance Return: notify the buyer ---
            case WorkflowEventCodes.FinanceReturned:
                await AddUserRecipientAsync(recipients, evt.BuyerId);
                break;

            // --- Lifecycle: notify key stakeholders ---
            case WorkflowEventCodes.RequestCancelled:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                await AddUserRecipientAsync(recipients, evt.BuyerId);
                await AddUserRecipientAsync(recipients, evt.AreaApproverId);
                break;

            case WorkflowEventCodes.RequestFinalized:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;
        }

        // Deduplicate (multiple events could resolve to the same user)
        return recipients
            .GroupBy(r => r.UserId)
            .Select(g => g.First())
            .ToList();
    }

    // Helper method to keep ResolveRecipientsAsync clean
    private async Task HandleAreaFanningOverridesAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef, string commentHtml, bool isApproval)
    {
        var actionWord = isApproval ? "aprovou" : (evt.EventCode == WorkflowEventCodes.AreaAdjustment ? "pediu reajustes no" : "rejeitou o");
        var actionWordDesc = isApproval ? "aprovado" : (evt.EventCode == WorkflowEventCodes.AreaAdjustment ? "com reajuste solicitado" : "rejeitado");
        var subjectWord = isApproval ? "Aprovado" : (evt.EventCode == WorkflowEventCodes.AreaAdjustment ? "Reajuste Solicitado" : "Rejeitado");

        // 1. Requester
        await AddUserRecipientAsync(recipients, evt.RequesterId,
            emailSubjectOverride: $"O seu pedido {reqRef} foi {actionWordDesc} na área",
            emailBodyOverride: $"O aprovador de área (<b>{evt.ActorName}</b>) {actionWord} pedido <b>{reqRef}</b>.{commentHtml}");

        // 2. Approver (Actor)
        await AddUserRecipientAsync(recipients, evt.AreaApproverId, // Assuming actor is AreaApproverId
            emailSubjectOverride: $"Decisão de Área Registada: {reqRef} {subjectWord}",
            emailBodyOverride: $"Você {actionWord} pedido <b>{reqRef}</b> na etapa área.{commentHtml}",
            bypassSelfNotifyRule: true);

        // 3. Buyer (only if QUOTATION)
        var req = await _context.Requests.Include(r => r.RequestType).FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        if (req?.RequestType?.Code == "QUOTATION")
        {
            await AddUserRecipientAsync(recipients, evt.BuyerId,
                emailSubjectOverride: $"Cotação — Pedido {reqRef} {actionWordDesc} na área",
                emailBodyOverride: $"O pedido de cotação <b>{reqRef}</b> foi {actionWordDesc} pelo aprovador de área (<b>{evt.ActorName}</b>).{commentHtml}");
        }
    }

    private async Task HandlePaymentFanningOverridesAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef)
    {
        // 1. Always notify the Requester (using the default EventConfig)
        await AddUserRecipientAsync(recipients, evt.RequesterId);

        // 2. Area Approvers Fan-Out Notification
        if (!evt.DepartmentId.HasValue) return;

        var req = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        if (req == null) return;
        
        var thisAmount = req.EstimatedTotalAmount;
        
        // Month boundaries: first day of current UTC month
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        
        // Calculate departmental financial accumulation metric (SCHEDULED / PAID in the current month)
        var summedAmount = await _context.Requests
            .AsNoTracking()
            .Where(r => r.DepartmentId == evt.DepartmentId.Value 
                     && (r.Status.Code == "SCHEDULED" || r.Status.Code == "PAID" || r.Status.Code == "PARTIAL_PAID")
                     && r.UpdatedAtUtc >= monthStart)
            .SumAsync(r => r.EstimatedTotalAmount);

        // Safety against division by zero
        var percentage = summedAmount > 0 ? (thisAmount / summedAmount * 100) : 0;
        
        // Currency formatting
        var currency = req.CurrencyId.HasValue 
            ? await _context.Currencies.AsNoTracking().Where(c => c.Id == req.CurrencyId).Select(c => c.Code).FirstOrDefaultAsync() ?? "AOA" 
            : "AOA";

        var areaApproverRole = await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleName == RoleConstants.AreaApprover);
        if (areaApproverRole == null) return;

        var areaApprovers = await _context.UserDepartmentScopes
            .AsNoTracking()
            .Include(uds => uds.User)
            .Where(uds => uds.DepartmentId == evt.DepartmentId.Value && uds.User.IsActive)
            .Where(uds => _context.UserRoleAssignments.Any(ura => ura.UserId == uds.UserId && ura.RoleId == areaApproverRole.Id))
            .Select(uds => uds.User)
            .ToListAsync();

        var paymentState = evt.EventCode == WorkflowEventCodes.PaymentScheduled ? "Agendado" : "Realizado";
        var htmlOverride = $@"
<p>O processo financeiro para o pedido <b>{reqRef}</b> foi <b>{paymentState.ToLower()}</b> pelas Finanças.</p>
<div style='background-color:#f0f9ff; border:1px solid #bae6fd; padding:15px; border-radius:6px; margin:20px 0;'>
    <h3 style='color:#0369a1; margin-top:0;'>Contexto Financeiro Departamental (Mês Corrente)</h3>
    <ul style='color:#0c4a6e; font-size:14px; margin-bottom:0;'>
        <li style='margin-bottom: 5px'><b>Valor deste Pedido:</b> {thisAmount:N2} {currency}</li>
        <li style='margin-bottom: 5px'><b>Acumulado (Agendado/Pago):</b> {summedAmount:N2} {currency}</li>
        <li><b>Impacto:</b> Este pedido representa <b>{percentage:N1}%</b> do total financeiro do seu departamento neste mês.</li>
    </ul>
</div>";

        var subjectOverride = $"[{paymentState.ToUpper()}] Informação de departamento - Pedido {reqRef}";

        foreach (var approver in areaApprovers)
        {
            // The Requester is already receiving the raw Requester-scoped email above. We bypass if they are the same person.
            if (approver.Id == evt.RequesterId) continue;

            recipients.Add(new NotificationRecipient(approver.Id, approver.Email, approver.FullName)
            {
                EmailSubjectOverride = subjectOverride,
                EmailBodyOverride = htmlOverride
            });
        }
    }

    private async Task AddUserRecipientAsync(List<NotificationRecipient> recipients, Guid? userId,
        string? emailSubjectOverride = null, string? emailBodyOverride = null, bool bypassSelfNotifyRule = false)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty) return;

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId.Value && u.IsActive)
            .Select(u => new { u.Id, u.Email, u.FullName })
            .FirstOrDefaultAsync();

        if (user != null)
        {
            recipients.Add(new NotificationRecipient(user.Id, user.Email, user.FullName)
            {
                EmailSubjectOverride = emailSubjectOverride,
                EmailBodyOverride = emailBodyOverride,
                BypassSelfNotifyRule = bypassSelfNotifyRule
            });
        }
    }

    /// <summary>
    /// Resolves all active Finance users scoped to the request's plant.
    /// Falls back to all active Finance users if no plant is specified or no
    /// plant-scoped users exist (global in-app only — email suppressed for global fan-out).
    /// </summary>
    private async Task AddPlantScopedFinanceRecipientsAsync(List<NotificationRecipient> recipients, int? plantId)
    {
        IQueryable<Guid> financeUserIds;

        if (plantId.HasValue)
        {
            // Plant-scoped: Finance users who have the specific plant in their scope
            financeUserIds = _context.UserRoleAssignments
                .Where(ura => ura.Role!.RoleName == RoleConstants.Finance)
                .Select(ura => ura.UserId)
                .Intersect(
                    _context.UserPlantScopes
                        .Where(ups => ups.PlantId == plantId.Value)
                        .Select(ups => ups.UserId)
                );
        }
        else
        {
            // No plant context — fallback to all Finance users
            financeUserIds = _context.UserRoleAssignments
                .Where(ura => ura.Role!.RoleName == RoleConstants.Finance)
                .Select(ura => ura.UserId);
        }

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => financeUserIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.Email, u.FullName })
            .ToListAsync();

        foreach (var user in users)
        {
            recipients.Add(new NotificationRecipient(user.Id, user.Email, user.FullName)
            {
                // If we fell back to global (no plant scope match), suppress email
                SuppressEmail = !plantId.HasValue
            });
        }
    }

    // =====================================================================
    // DISPATCH
    // =====================================================================

    private async Task DispatchToRecipientAsync(WorkflowEvent evt, EventConfig config, NotificationRecipient recipient)
    {
        var targetPath = $"/requests/{evt.RequestId}?mode=view";

        // --- In-App Notification ---
        try
        {
            var created = await _notificationService.CreateNotificationWithDedupAsync(
                recipient.UserId,
                config.InAppTitle,
                config.InAppMessage,
                config.NotificationType,
                targetPath,
                evt.CorrelationId,
                config.Category
            );

            if (!created)
            {
                _logger.LogDebug("Dedup: notification already exists for CorrelationId {CorrelationId} + User {UserId}", evt.CorrelationId, recipient.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create in-app notification for User {UserId} (Event: {EventCode})", recipient.UserId, evt.EventCode);
        }

        // --- Email Notification ---
        if (config.SendEmail && !recipient.SuppressEmail && !string.IsNullOrWhiteSpace(recipient.Email))
        {
            var portalBaseUrl = _config["AppConfig:PortalBaseUrl"] ?? "";
            var actionUrl = !string.IsNullOrWhiteSpace(portalBaseUrl) ? $"{portalBaseUrl.TrimEnd('/')}{targetPath}" : null;

            // Notice we are NOT wrapping this in a catch. If it fails, EmailService will throw and it'll bubble up to EmitAsync's throw, bubbling back to the Controller.
            await _emailService.SendWorkflowNotificationAsync(
                recipient.Email,
                recipient.FullName,
                recipient.EmailSubjectOverride ?? config.EmailSubject,
                config.EmailHeadline,
                recipient.EmailBodyOverride ?? config.EmailBody,
                actionUrl,
                "Ver Pedido"
            );
        }
    }

    // =====================================================================
    // EVENT → MESSAGE MAPPING
    // =====================================================================

    private async Task<EventConfig?> ResolveEventConfigAsync(WorkflowEvent evt)
    {
        var reqRef = $"{evt.RequestNumber}";
        var reqContext = !string.IsNullOrWhiteSpace(evt.RequestTitle) ? $" (\"{evt.RequestTitle}\")" : "";
        var actorLabel = evt.ActorName;

        switch (evt.EventCode)
        {
            case WorkflowEventCodes.SubmissionConfirmed:
            {
                var itemsTable = await FormatLineItemsTableAsync(evt.RequestId);
                var req = await _context.Requests.Include(r => r.Currency).FirstOrDefaultAsync(r => r.Id == evt.RequestId);
                var currencyCode = req?.Currency?.Code ?? "AOA";
                var totalAmt = req?.EstimatedTotalAmount ?? 0m;

                var bodyHtml = $"Recebemos o seu pedido <b>{reqRef}</b>{reqContext} com sucesso. O pedido foi enviado para a fila de aprovação.<br/><br/>" +
                               $"<b>Valor Total Estimado:</b> {totalAmt:N2} {currencyCode}<br/><br/>" +
                               $"<b>Resumo dos Itens:</b><br/>" + itemsTable;

                return new EventConfig
                {
                    Category = NotificationCategories.Approval,
                    NotificationType = NotificationTypes.Success,
                    InAppTitle = "Submissão Confirmada",
                    InAppMessage = $"O seu pedido {reqRef} foi submetido com sucesso.",
                    SendEmail = true,
                    EmailSubject = $"Confirmação de Submissão — {reqRef}",
                    EmailHeadline = "Submissão Confirmada",
                    EmailBody = bodyHtml
                };
            }

            case WorkflowEventCodes.RequestSubmitted: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Novo Pedido para Aprovação",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi submetido por {actorLabel} e aguarda a sua aprovação de área.",
                SendEmail = true,
                EmailSubject = $"Aprovação Solicitada — {reqRef}",
                EmailHeadline = "Aprovação de Área Solicitada",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi submetido por <b>{actorLabel}</b> e aguarda a sua decisão de aprovação."
            };

            case WorkflowEventCodes.AreaApproved: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Aprovação da Área Concedida",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi aprovado na etapa de área por {actorLabel} e aguarda aprovação final.",
                SendEmail = true,
                EmailSubject = $"Aprovação Final Solicitada — {reqRef}",
                EmailHeadline = "Pedido Aguardando Aprovação Final",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} passou pela aprovação de área (por <b>{actorLabel}</b>) e aguarda a sua decisão de aprovação final."
            };

            case WorkflowEventCodes.FinalApproved: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Pedido Aprovado",
                InAppMessage = $"O pedido {reqRef}{reqContext} recebeu aprovação final de {actorLabel}.",
                SendEmail = true,
                EmailSubject = $"Pedido Aprovado — {reqRef}",
                EmailHeadline = "Pedido Aprovado com Sucesso",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi aprovado por <b>{actorLabel}</b> e prossegue para a etapa operacional."
            };

            case WorkflowEventCodes.AreaRejected: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Error,
                InAppTitle = "Pedido Rejeitado — Aprovação de Área",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi rejeitado por {actorLabel} na aprovação de área.",
                SendEmail = true,
                EmailSubject = $"Pedido Rejeitado — {reqRef}",
                EmailHeadline = "Pedido Rejeitado na Aprovação de Área",
                EmailBody = BuildRejectionBody(reqRef, reqContext, actorLabel, "aprovação de área", evt.Comment)
            };

            case WorkflowEventCodes.FinalRejected: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Error,
                InAppTitle = "Pedido Rejeitado — Aprovação Final",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi rejeitado por {actorLabel} na aprovação final.",
                SendEmail = true,
                EmailSubject = $"Pedido Rejeitado — {reqRef}",
                EmailHeadline = "Pedido Rejeitado na Aprovação Final",
                EmailBody = BuildRejectionBody(reqRef, reqContext, actorLabel, "aprovação final", evt.Comment)
            };

            case WorkflowEventCodes.AreaAdjustment: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Reajuste Solicitado — Área",
                InAppMessage = $"O pedido {reqRef}{reqContext} requer ajustes solicitados por {actorLabel} na aprovação de área.",
                SendEmail = true,
                EmailSubject = $"Reajuste Solicitado — {reqRef}",
                EmailHeadline = "Reajuste de Pedido Solicitado",
                EmailBody = BuildAdjustmentBody(reqRef, reqContext, actorLabel, "aprovação de área", evt.Comment)
            };

            case WorkflowEventCodes.FinalAdjustment: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Reajuste Solicitado — Aprovação Final",
                InAppMessage = $"O pedido {reqRef}{reqContext} requer ajustes solicitados por {actorLabel} na aprovação final.",
                SendEmail = true,
                EmailSubject = $"Reajuste Solicitado — {reqRef}",
                EmailHeadline = "Reajuste de Pedido Solicitado",
                EmailBody = BuildAdjustmentBody(reqRef, reqContext, actorLabel, "aprovação final", evt.Comment)
            };

            case WorkflowEventCodes.PoRegistered: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Nova P.O Registrada",
                InAppMessage = $"A P.O para o pedido {reqRef}{reqContext} foi registrada por {actorLabel}. Aguardando processamento financeiro.",
                SendEmail = true,
                EmailSubject = $"Nova P.O para Processamento — {reqRef}",
                EmailHeadline = "P.O Registrada — Processamento Financeiro",
                EmailBody = $"A Purchase Order para o pedido <b>{reqRef}</b>{reqContext} foi registrada por <b>{actorLabel}</b> e está pronta para processamento financeiro."
            };

            case WorkflowEventCodes.PaymentScheduled: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Pagamento Agendado",
                InAppMessage = $"O pagamento referente ao pedido {reqRef}{reqContext} foi agendado.",
                SendEmail = true,
                EmailSubject = $"Pagamento Agendado — {reqRef}",
                EmailHeadline = "Pagamento Agendado",
                EmailBody = $"O pagamento para o pedido <b>{reqRef}</b>{reqContext} foi agendado pela equipa financeira."
            };

            case WorkflowEventCodes.PaymentCompleted: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Pagamento Realizado",
                InAppMessage = $"O pagamento referente ao pedido {reqRef}{reqContext} foi realizado com sucesso.",
                SendEmail = true,
                EmailSubject = $"Pagamento Realizado — {reqRef}",
                EmailHeadline = "Pagamento Realizado com Sucesso",
                EmailBody = $"O pagamento para o pedido <b>{reqRef}</b>{reqContext} foi concluído pela equipa financeira."
            };

            case WorkflowEventCodes.FinanceReturned: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Pedido Devolvido por Finanças",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi devolvido por {actorLabel} para correção.",
                SendEmail = true,
                EmailSubject = $"Pedido Devolvido para Correção — {reqRef}",
                EmailHeadline = "Pedido Devolvido por Finanças",
                EmailBody = BuildFinanceReturnBody(reqRef, reqContext, actorLabel, evt.Comment)
            };

            case WorkflowEventCodes.RequestCancelled: return new EventConfig
            {
                Category = NotificationCategories.Info,
                NotificationType = NotificationTypes.Error,
                InAppTitle = "Pedido Cancelado",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi cancelado por {actorLabel}.",
                SendEmail = true,
                EmailSubject = $"Pedido Cancelado — {reqRef}",
                EmailHeadline = "Pedido Cancelado",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi cancelado por <b>{actorLabel}</b>. Nenhuma ação adicional é necessária."
            };

            case WorkflowEventCodes.RequestFinalized: return new EventConfig
            {
                Category = NotificationCategories.Receipt,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Pedido Finalizado",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi finalizado com sucesso. O recebimento está concluído.",
                SendEmail = true,
                EmailSubject = $"Pedido Finalizado — {reqRef}",
                EmailHeadline = "Pedido Finalizado com Sucesso",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} teve o processo de recebimento finalizado. O ciclo do pedido está completo."
            };

            case WorkflowEventCodes.QuotationCompleted: return new EventConfig
            {
                Category = NotificationCategories.Quotation,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Cotação Concluída",
                InAppMessage = $"A cotação para o pedido {reqRef}{reqContext} foi concluída e enviada para etapa de aprovação.",
                SendEmail = true,
                EmailSubject = $"Cotação Concluída — {reqRef}",
                EmailHeadline = "Cotação Concluída",
                EmailBody = $"A cotação para o pedido <b>{reqRef}</b>{reqContext} foi concluída por <b>{actorLabel}</b> e avança para a etapa de aprovação de área."
            };

            default: return null;
        }
    }

    // =====================================================================
    // EMAIL BODY HELPERS
    // =====================================================================

    private static string BuildRejectionBody(string reqRef, string reqContext, string actor, string stage, string? comment)
    {
        var commentHtml = !string.IsNullOrWhiteSpace(comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff3f3; border-left:4px solid #e74c3c;'><b>Motivo:</b> {System.Net.WebUtility.HtmlEncode(comment)}</p>"
            : "";
        return $"O pedido <b>{reqRef}</b>{reqContext} foi <b>rejeitado</b> por <b>{actor}</b> na etapa de {stage}.{commentHtml}";
    }

    private static string BuildAdjustmentBody(string reqRef, string reqContext, string actor, string stage, string? comment)
    {
        var commentHtml = !string.IsNullOrWhiteSpace(comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff8e1; border-left:4px solid #f39c12;'><b>Observação:</b> {System.Net.WebUtility.HtmlEncode(comment)}</p>"
            : "";
        return $"O pedido <b>{reqRef}</b>{reqContext} requer <b>reajustes</b> solicitados por <b>{actor}</b> na etapa de {stage}. Revise e submeta novamente.{commentHtml}";
    }

    private static string BuildFinanceReturnBody(string reqRef, string reqContext, string actor, string? comment)
    {
        var commentHtml = !string.IsNullOrWhiteSpace(comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff8e1; border-left:4px solid #f39c12;'><b>Motivo:</b> {System.Net.WebUtility.HtmlEncode(comment)}</p>"
            : "";
        return $"O pedido <b>{reqRef}</b>{reqContext} foi devolvido pela equipa de Finanças (<b>{actor}</b>) para correção da P.O ou documentação.{commentHtml}";
    }

    private async Task<string> FormatLineItemsTableAsync(Guid requestId)
    {
        var items = await _context.RequestLineItems
            .Include(li => li.Unit)
            .Where(li => li.RequestId == requestId && !li.IsDeleted)
            .OrderBy(li => li.LineNumber)
            .ToListAsync();

        if (!items.Any()) return "<p><i>Nenhum item encontrado.</i></p>";

        var sb = new System.Text.StringBuilder();
        sb.Append("<table style='width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 14px;'>");
        sb.Append("<thead><tr style='background-color: #f8f9fa; border-bottom: 2px solid #dee2e6;'>");
        sb.Append("<th style='padding: 8px; text-align: left;'>Descrição</th>");
        sb.Append("<th style='padding: 8px; text-align: right;'>Qtd</th>");
        sb.Append("<th style='padding: 8px; text-align: left;'>Un.</th>");
        sb.Append("<th style='padding: 8px; text-align: right;'>Preço Unit.</th>");
        sb.Append("<th style='padding: 8px; text-align: right;'>Total</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in items)
        {
            sb.Append("<tr style='border-bottom: 1px solid #e9ecef;'>");
            sb.Append($"<td style='padding: 8px;'>{System.Net.WebUtility.HtmlEncode(item.Description)}</td>");
            sb.Append($"<td style='padding: 8px; text-align: right;'>{item.Quantity:N2}</td>");
            sb.Append($"<td style='padding: 8px;'>{item.Unit?.Code ?? "-"}</td>");
            sb.Append($"<td style='padding: 8px; text-align: right;'>{item.UnitPrice:N2}</td>");
            sb.Append($"<td style='padding: 8px; text-align: right;'>{item.TotalAmount:N2}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    // =====================================================================
    // INTERNAL TYPES
    // =====================================================================

    private record NotificationRecipient(Guid UserId, string Email, string FullName)
    {
        /// <summary>
        /// If true, email is suppressed for this recipient (e.g., global Finance fan-out fallback).
        /// </summary>
        public bool SuppressEmail { get; init; }
        
        public string? EmailSubjectOverride { get; init; }
        public string? EmailBodyOverride { get; init; }
        public bool BypassSelfNotifyRule { get; init; }
    }

    private class EventConfig
    {
        public string Category { get; init; } = NotificationCategories.Info;
        public string NotificationType { get; init; } = NotificationTypes.Info;
        public string InAppTitle { get; init; } = string.Empty;
        public string InAppMessage { get; init; } = string.Empty;
        public bool SendEmail { get; init; }
        public string EmailSubject { get; init; } = string.Empty;
        public string EmailHeadline { get; init; } = string.Empty;
        public string EmailBody { get; init; } = string.Empty;
    }
}
