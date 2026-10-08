using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// One proforma-deadline alert cycle, separated from the hosting loop so it can be unit-tested.
///
/// <para><b>Delivery model (change 3):</b> alerts are no longer sent through <c>SmtpClient</c> inside
/// the cycle. Each alert becomes an <see cref="EmailOutboxEntry"/> (EventCode
/// <c>PROFORMA_DEADLINE_&lt;LEVEL&gt;</c>, CorrelationId = the alert row Id) written in the SAME
/// SaveChanges as the <see cref="ProformaDeadlineAlert"/> row, so the record and the queued message
/// are atomic. Retries, dead-letter and expiry are the outbox processor's. An alert record therefore
/// means <b>queued</b>, never <b>delivered</b>; delivery evidence is the linked outbox row
/// (<see cref="DescribeDelivery"/>).</para>
///
/// <para><b>Dedup / retry:</b> the unique key (Request, Level, Recipient) is kept. A level is skipped
/// while its latest outbox row is pending or SENT (or for legacy rows sent directly). When the latest
/// outbox row is DEAD_LETTER or EXPIRED, the next cycle re-queues a NEW outbox row against the SAME
/// alert record (<see cref="ProformaDeadlineAlert.QueuedCount"/> increments) — a transient SMTP
/// failure no longer silences the level forever.</para>
/// </summary>
public sealed class ProformaDeadlineAlertCycle
{
    public const string EventCodePrefix = "PROFORMA_DEADLINE_";
    public const string DeliveryQueued = "QUEUED";
    public const string DeliverySent = "SENT";
    public const string DeliveryFailed = "FAILED";
    public const string DeliverySentLegacy = "SENT_LEGACY";
    public const string DeliveryFailedLegacy = "FAILED_LEGACY";

    public sealed record CycleResult(int Eligible, int Queued, int Requeued, int SkippedDedup, int NoRecipient);

    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger _logger;

    public ProformaDeadlineAlertCycle(ApplicationDbContext context, INotificationService notifications, IConfiguration config, AdminLogWriter adminLog, ILogger logger)
    {
        _context = context;
        _notifications = notifications;
        _config = config;
        _adminLog = adminLog;
        _logger = logger;
    }

    public async Task<CycleResult> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var thresholdDays = _config.GetSection("AppConfig:ProformaDeadlineAlerts:ThresholdDays").Get<int[]>() ?? new[] { 3, 1, 0 };
        var frontendBaseUrl = _config.GetValue<string>("AppConfig:FrontendBaseUrl") ?? "https://portal.alpla.com";
        var expiryHours = _config.GetValue<int>("AppConfig:ProformaDeadlineAlerts:OutboxExpiryHours", 24);
        var today = nowUtc.Date;

        var eligibleRequests = await _context.Requests
            .AsNoTracking()
            .Include(r => r.Status).Include(r => r.RequestType).Include(r => r.Requester)
            .Include(r => r.Company).Include(r => r.Plant).Include(r => r.Department)
            .Include(r => r.Supplier).Include(r => r.Currency)
            .Where(r => r.RequestType!.Code == "PAYMENT"
                     && !r.IsCancelled
                     && r.NeedByDateUtc.HasValue
                     && (r.Status!.Code == "WAITING_AREA_APPROVAL" || r.Status.Code == "WAITING_FINAL_APPROVAL"))
            .ToListAsync(ct);

        _logger.LogInformation("[ProformaDeadlineAlerts] Found {Count} eligible PAYMENT requests in approval stages.", eligibleRequests.Count);

        int queued = 0, requeued = 0, skipped = 0, noRecipient = 0;
        var routing = new ApprovalRoutingService(_context);

        foreach (var request in eligibleRequests)
        {
            if (ct.IsCancellationRequested) break;

            var daysRemaining = (request.NeedByDateUtc!.Value.Date - today).Days;
            var alertLevel = DetermineAlertLevel(daysRemaining, thresholdDays);
            if (alertLevel == null) continue;

            var recipients = await ResolveApproverRecipientsAsync(routing, request);
            if (recipients.Count == 0)
            {
                noRecipient++;
                _logger.LogWarning("[ProformaDeadlineAlerts] No approver resolved for Request {RequestId} ({RequestNumber}) in status {Status}.",
                    request.Id, request.RequestNumber, request.Status?.Code);
                continue;
            }

            foreach (var recipient in recipients)
            {
                var existing = await _context.ProformaDeadlineAlerts
                    .Include(a => a.OutboxEntry)
                    .FirstOrDefaultAsync(a => a.RequestId == request.Id && a.AlertLevel == alertLevel && a.RecipientUserId == recipient.UserId, ct);

                if (existing != null && !IsRetryable(existing))
                {
                    skipped++;
                    continue;
                }

                var previousOutboxStatus = existing?.OutboxEntry?.Status ?? "unknown"; // captured BEFORE the navigation is replaced
                var (subject, headline, bodyHtml, inAppTitle, inAppMessage, inAppType) = BuildMessages(request, recipient, alertLevel, daysRemaining);
                var actionUrl = $"{frontendBaseUrl.TrimEnd('/')}/requests/{request.Id}?mode=view";

                var alert = existing ?? new ProformaDeadlineAlert
                {
                    RequestId = request.Id,
                    AlertLevel = alertLevel,
                    RecipientUserId = recipient.UserId,
                    SentAtUtc = nowUtc
                };

                var outbox = new EmailOutboxEntry
                {
                    RecipientEmail = recipient.Email,
                    RecipientName = recipient.FullName,
                    Subject = subject,
                    Headline = headline,
                    BodyHtml = bodyHtml,
                    ActionUrl = actionUrl,
                    ActionLabel = "Ver Pedido →",
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber,
                    EventCode = EventCodePrefix + alertLevel,
                    CorrelationId = alert.Id,
                    Status = "PENDING",
                    CreatedAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.AddHours(expiryHours)
                };
                _context.EmailOutbox.Add(outbox);

                // Record = queued (atomic with the outbox row). EmailSent/InAppSent are legacy flags:
                // EmailSent stays false (delivery is read from the outbox), InAppSent reflects the bell.
                alert.OutboxEntry = outbox;
                alert.OutboxEntryId = outbox.Id;
                alert.QueuedCount += 1;
                alert.LastQueuedAtUtc = nowUtc;
                alert.EmailSent = false;
                alert.ErrorMessage = existing == null ? null : $"Re-queued after {previousOutboxStatus} (attempt {alert.QueuedCount})";
                if (existing == null) _context.ProformaDeadlineAlerts.Add(alert);

                await _context.SaveChangesAsync(ct);
                if (existing == null) queued++; else requeued++;

                // In-app notification (bell) — best effort, outside the atomic pair
                try
                {
                    await _notifications.CreateNotificationAsync(recipient.UserId, inAppTitle, inAppMessage, inAppType, $"/requests/{request.Id}?mode=view");
                    alert.InAppSent = true;
                    await _context.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ProformaDeadlineAlerts] In-app notification failed for {UserId}", recipient.UserId);
                }

                _logger.LogInformation("[ProformaDeadlineAlerts] Alert {AlertLevel} for Request {RequestNumber} → {RecipientName} ({RecipientEmail}): QUEUED (outbox {OutboxId})",
                    alertLevel, request.RequestNumber, recipient.FullName, recipient.Email, outbox.Id);
            }
        }

        var result = new CycleResult(eligibleRequests.Count, queued, requeued, skipped, noRecipient);
        _logger.LogInformation("[ProformaDeadlineAlerts] Cycle complete. Queued: {Queued}, Re-queued: {Requeued}, Skipped (dedup): {Skipped}, No recipient: {NoRecipient}.",
            queued, requeued, skipped, noRecipient);
        await _adminLog.WriteAsync("Info", "Notification", "PROFORMA_DEADLINE_CYCLE",
            $"Proforma deadline alert cycle: {queued} queued, {requeued} re-queued, {skipped} skipped (dedup), {noRecipient} without recipient. Eligible requests: {eligibleRequests.Count}. (queued = outbox row created, not delivery)",
            payload: $"{{\"Eligible\":{eligibleRequests.Count},\"Queued\":{queued},\"Requeued\":{requeued},\"SkippedDedup\":{skipped},\"NoRecipient\":{noRecipient}}}");
        return result;
    }

    /// <summary>A level is retried only when its latest outbox row is terminal-failed (DEAD_LETTER/EXPIRED). Legacy rows (no outbox link) are never retried here.</summary>
    public static bool IsRetryable(ProformaDeadlineAlert alert)
    {
        if (alert.OutboxEntryId == null) return false;
        var status = alert.OutboxEntry?.Status;
        return status == "DEAD_LETTER" || status == "EXPIRED";
    }

    /// <summary>Delivery status derived from evidence: the linked outbox row, or the legacy direct-send flag.</summary>
    public static string DescribeDelivery(ProformaDeadlineAlert alert)
    {
        if (alert.OutboxEntryId == null) return alert.EmailSent ? DeliverySentLegacy : DeliveryFailedLegacy;
        return alert.OutboxEntry?.Status switch
        {
            "SENT" => DeliverySent,
            "DEAD_LETTER" or "EXPIRED" => DeliveryFailed,
            _ => DeliveryQueued
        };
    }

    public static string? DetermineAlertLevel(int daysRemaining, int[] thresholdDays)
    {
        if (daysRemaining < 0) return "EXPIRED";
        if (daysRemaining == 0 && thresholdDays.Contains(0)) return "CRITICAL_0D";
        if (daysRemaining == 1 && thresholdDays.Contains(1)) return "WARNING_1D";
        if (daysRemaining == 3 && thresholdDays.Contains(3)) return "WARNING_3D";
        return null;
    }

    public sealed record AlertRecipient(Guid UserId, string Email, string FullName);

    /// <summary>Same population as approval authorization: area = DepartmentManager cascade; final = eligible final approvers of the company.</summary>
    private static async Task<List<AlertRecipient>> ResolveApproverRecipientsAsync(ApprovalRoutingService routing, Request request)
    {
        var recipients = new List<AlertRecipient>();
        if (request.Status?.Code == "WAITING_AREA_APPROVAL")
        {
            var resolved = await routing.ResolveAreaManagersAsync(request.DepartmentId, request.PlantId);
            recipients.AddRange(resolved.Managers.Select(m => new AlertRecipient(m.UserId, m.Email, m.FullName)));
        }
        else if (request.Status?.Code == "WAITING_FINAL_APPROVAL")
        {
            var resolved = await routing.ResolveFinalNotificationRecipientsAsync(request.FinalApproverId, request.CompanyId);
            recipients.AddRange(resolved.Recipients.Select(a => new AlertRecipient(a.UserId, a.Email, a.FullName)));
        }
        return recipients.GroupBy(r => r.UserId).Select(g => g.First()).ToList();
    }

    private static (string subject, string headline, string bodyHtml, string inAppTitle, string inAppMessage, string inAppType) BuildMessages(
        Request request, AlertRecipient recipient, string alertLevel, int daysRemaining)
    {
        var reqNum = request.RequestNumber ?? request.Id.ToString()[..8];

        var (subject, urgencyColor, urgencyBg, urgencyBorder, headlineText, daysLabel) = alertLevel switch
        {
            "WARNING_3D" => ($"[Portal Gerencial] Proforma vence em 3 dias — {reqNum}", "#b45309", "#fffbeb", "#fde68a", "⚠️ Proforma vence em 3 dias", "3 dias restantes"),
            "WARNING_1D" => ($"[Portal Gerencial] Proforma vence amanhã — {reqNum}", "#c2410c", "#fff7ed", "#fed7aa", "⚠️ Proforma vence amanhã", "1 dia restante"),
            "CRITICAL_0D" => ($"[Portal Gerencial] Proforma vence hoje — {reqNum}", "#dc2626", "#fef2f2", "#fecaca", "🔴 Proforma vence HOJE", "Vence hoje"),
            "EXPIRED" => ($"[Portal Gerencial] Proforma vencida — {reqNum}", "#991b1b", "#fef2f2", "#fca5a5", "❌ Proforma VENCIDA", $"Vencida há {Math.Abs(daysRemaining)} dia(s)"),
            _ => ($"[Portal Gerencial] Proforma — {reqNum}", "#6b7280", "#f9fafb", "#e5e7eb", "Proforma", "—")
        };

        var currencyCode = request.Currency?.Code ?? "AOA";
        var supplierName = request.Supplier?.Name ?? "—";
        var statusName = request.Status?.Name ?? "—";

        var bodyHtml = $@"
<p>O pedido abaixo contém uma Proforma com prazo de pagamento/validade próximo ou expirado e aguarda a sua aprovação.</p>

<div style='background-color:{urgencyBg}; border:1px solid {urgencyBorder}; padding:15px; border-radius:6px; margin:20px 0;'>
    <h3 style='color:{urgencyColor}; margin-top:0;'>{headlineText}</h3>
    <table style='width:100%; border-collapse:collapse; font-size:14px; color:#374151;'>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Pedido:</td><td style='padding:5px 0;'>{reqNum}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Solicitante:</td><td style='padding:5px 0;'>{request.Requester?.FullName ?? "—"}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Departamento:</td><td style='padding:5px 0;'>{request.Department?.Name ?? "—"}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Empresa / Planta:</td><td style='padding:5px 0;'>{request.Company?.Name ?? "—"}{(request.Plant != null ? $" / {request.Plant.Name}" : "")}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Fornecedor:</td><td style='padding:5px 0;'>{supplierName}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Valor Total:</td><td style='padding:5px 0;'>{request.EstimatedTotalAmount:N2} {currencyCode}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Estado Atual:</td><td style='padding:5px 0;'>{statusName}</td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Data de Vencimento:</td><td style='padding:5px 0;'><b>{request.NeedByDateUtc!.Value:dd/MM/yyyy}</b></td></tr>
        <tr><td style='padding:5px 10px 5px 0; font-weight:bold; white-space:nowrap;'>Situação:</td><td style='padding:5px 0; color:{urgencyColor}; font-weight:bold;'>{daysLabel}</td></tr>
    </table>
</div>

<p>Por favor, revise e tome uma decisão sobre este pedido o mais breve possível para evitar a perda de validade da Proforma.</p>
";

        var inAppTitle = alertLevel switch
        {
            "WARNING_3D" => $"Proforma vence em 3 dias — {reqNum}",
            "WARNING_1D" => $"Proforma vence amanhã — {reqNum}",
            "CRITICAL_0D" => $"Proforma vence HOJE — {reqNum}",
            "EXPIRED" => $"Proforma VENCIDA — {reqNum}",
            _ => $"Alerta Proforma — {reqNum}"
        };
        var inAppMessage = $"O pedido {reqNum} ({statusName}) tem vencimento de Proforma em {request.NeedByDateUtc.Value:dd/MM/yyyy}. {daysLabel}.";
        var inAppType = alertLevel == "EXPIRED" || alertLevel == "CRITICAL_0D" ? NotificationTypes.Error : NotificationTypes.Warning;

        return (subject, headlineText, bodyHtml, inAppTitle, inAppMessage, inAppType);
    }
}
