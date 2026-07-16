using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Daily background service that scans PAYMENT requests in approval stages
/// and sends Proforma deadline alerts to the responsible approver.
///
/// <para><b>Alert levels:</b></para>
/// <list type="bullet">
///     <item><b>WARNING_3D</b> — 3 calendar days before <c>NeedByDateUtc</c></item>
///     <item><b>WARNING_1D</b> — 1 calendar day before</item>
///     <item><b>CRITICAL_0D</b> — same day</item>
///     <item><b>EXPIRED</b> — past due</item>
/// </list>
///
/// Deduplication is global per (RequestId, AlertLevel, RecipientUserId),
/// so each recipient receives at most one alert per level per request, ever.
/// If the request moves to another approval stage and the responsible approver
/// changes, the new recipient can still receive the relevant alert.
///
/// Configuration section: <c>AppConfig:ProformaDeadlineAlerts</c> in appsettings.json.
/// </summary>
public class ProformaDeadlineAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ProformaDeadlineAlertService> _logger;

    public ProformaDeadlineAlertService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ProformaDeadlineAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue<bool>("AppConfig:ProformaDeadlineAlerts:Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("[ProformaDeadlineAlerts] Service is DISABLED via configuration. Exiting.");
            return;
        }

        var intervalHours = _config.GetValue<int>("AppConfig:ProformaDeadlineAlerts:CheckIntervalHours", 24);
        var checkTimeUtcHour = _config.GetValue<int>("AppConfig:ProformaDeadlineAlerts:CheckTimeUtcHour", 7);

        _logger.LogInformation(
            "[ProformaDeadlineAlerts] Service started. Interval: {IntervalHours}h, CheckTimeUtcHour: {CheckTimeUtcHour}.",
            intervalHours, checkTimeUtcHour);

        // Wait for the first scheduled time before running
        await WaitUntilNextCheckTimeAsync(checkTimeUtcHour, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAlertCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[ProformaDeadlineAlerts] Unhandled error in alert cycle. Will retry on next tick.");
            }

            // Wait for next cycle
            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[ProformaDeadlineAlerts] Service stopped.");
    }

    // =====================================================================
    // CORE ALERT CYCLE
    // =====================================================================

    private async Task RunAlertCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("[ProformaDeadlineAlerts] Starting alert cycle at {UtcNow:u}.", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var adminLog = scope.ServiceProvider.GetRequiredService<AdminLogWriter>();

        var thresholdDays = config.GetSection("AppConfig:ProformaDeadlineAlerts:ThresholdDays")
            .Get<int[]>() ?? new[] { 3, 1, 0 };

        var frontendBaseUrl = config.GetValue<string>("AppConfig:FrontendBaseUrl") ?? "https://portal.alpla.com";

        var today = DateTime.UtcNow.Date;

        // Query eligible requests
        var eligibleRequests = await context.Requests
            .AsNoTracking()
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.Requester)
            .Include(r => r.Company)
            .Include(r => r.Plant)
            .Include(r => r.Department)
            .Include(r => r.Supplier)
            .Include(r => r.Currency)
            .Where(r =>
                r.RequestType!.Code == "PAYMENT"
                && !r.IsCancelled
                && r.NeedByDateUtc.HasValue
                && (r.Status!.Code == "WAITING_AREA_APPROVAL" || r.Status.Code == "WAITING_FINAL_APPROVAL"))
            .ToListAsync(ct);

        _logger.LogInformation("[ProformaDeadlineAlerts] Found {Count} eligible PAYMENT requests in approval stages.", eligibleRequests.Count);

        int alertsSent = 0;
        int alertsSkipped = 0;

        foreach (var request in eligibleRequests)
        {
            if (ct.IsCancellationRequested) break;

            var daysRemaining = (request.NeedByDateUtc!.Value.Date - today).Days;
            var alertLevel = DetermineAlertLevel(daysRemaining, thresholdDays);

            if (alertLevel == null)
            {
                continue; // No alert needed at this threshold
            }

            // Resolve recipient based on current approval stage
            var recipients = await ResolveApproverRecipientsAsync(context, request);
            if (!recipients.Any())
            {
                _logger.LogWarning(
                    "[ProformaDeadlineAlerts] No approver resolved for Request {RequestId} ({RequestNumber}) in status {Status}.",
                    request.Id, request.RequestNumber, request.Status?.Code);
                continue;
            }

            foreach (var recipient in recipients)
            {
                // Dedup: check if this exact (Request, Level, Recipient) was already sent
                var alreadySent = await context.ProformaDeadlineAlerts
                    .AnyAsync(a =>
                        a.RequestId == request.Id
                        && a.AlertLevel == alertLevel
                        && a.RecipientUserId == recipient.UserId, ct);

                if (alreadySent)
                {
                    alertsSkipped++;
                    continue;
                }

                // Build and send alert
                var success = await SendAlertAsync(
                    emailService, notificationService, config,
                    request, recipient, alertLevel, daysRemaining, frontendBaseUrl);

                // Persist audit record
                var alertRecord = new ProformaDeadlineAlert
                {
                    RequestId = request.Id,
                    AlertLevel = alertLevel,
                    RecipientUserId = recipient.UserId,
                    EmailSent = success,
                    InAppSent = success,
                    ErrorMessage = success ? null : "Email dispatch failed",
                    SentAtUtc = DateTime.UtcNow
                };

                context.ProformaDeadlineAlerts.Add(alertRecord);

                if (success) alertsSent++;

                _logger.LogInformation(
                    "[ProformaDeadlineAlerts] Alert {AlertLevel} for Request {RequestNumber} → {RecipientName} ({RecipientEmail}): {Status}",
                    alertLevel, request.RequestNumber, recipient.FullName, recipient.Email,
                    success ? "SENT" : "FAILED");
            }
        }

        await context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ProformaDeadlineAlerts] Cycle complete. Sent: {Sent}, Skipped (dedup): {Skipped}.",
            alertsSent, alertsSkipped);

        // Admin audit trail
        await adminLog.WriteAsync("Info", "Notification", "PROFORMA_DEADLINE_CYCLE",
            $"Proforma deadline alert cycle: {alertsSent} sent, {alertsSkipped} skipped (dedup). Eligible requests: {eligibleRequests.Count}.");
    }

    // =====================================================================
    // ALERT LEVEL DETERMINATION
    // =====================================================================

    private static string? DetermineAlertLevel(int daysRemaining, int[] thresholdDays)
    {
        if (daysRemaining < 0) return "EXPIRED";
        if (daysRemaining == 0 && thresholdDays.Contains(0)) return "CRITICAL_0D";
        if (daysRemaining == 1 && thresholdDays.Contains(1)) return "WARNING_1D";
        if (daysRemaining == 3 && thresholdDays.Contains(3)) return "WARNING_3D";
        return null; // No alert for this threshold
    }

    // =====================================================================
    // RECIPIENT RESOLUTION
    // =====================================================================

    /// <summary>
    /// Resolves the approver(s) for the request based on its current status.
    /// Follows the same pattern as <see cref="WorkflowNotificationOrchestrator"/>.
    /// </summary>
    private static async Task<List<AlertRecipient>> ResolveApproverRecipientsAsync(
        ApplicationDbContext context, Request request)
    {
        var recipients = new List<AlertRecipient>();

        if (request.Status?.Code == "WAITING_AREA_APPROVAL")
        {
            // Legacy in-flight nominee (requests submitted before the Phase B cut)
            if (request.AreaApproverId.HasValue)
            {
                var user = await context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == request.AreaApproverId.Value && u.IsActive);
                if (user != null)
                    recipients.Add(new AlertRecipient(user.Id, user.Email, user.FullName));
            }
            else
            {
                // Phase B: resolve via DepartmentManager routing (department + plant,
                // strict cascade). No role fan-out, no Department.ResponsibleUserId.
                var routing = new Approvals.ApprovalRoutingService(context);
                var resolved = await routing.ResolveAreaManagersAsync(request.DepartmentId, request.PlantId);
                foreach (var m in resolved.Managers)
                    recipients.Add(new AlertRecipient(m.UserId, m.Email, m.FullName));
            }
        }
        else if (request.Status?.Code == "WAITING_FINAL_APPROVAL")
        {
            if (request.FinalApproverId.HasValue)
            {
                var user = await context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == request.FinalApproverId.Value && u.IsActive);
                if (user != null)
                    recipients.Add(new AlertRecipient(user.Id, user.Email, user.FullName));
            }
        }

        return recipients;
    }

    // =====================================================================
    // EMAIL + IN-APP DISPATCH
    // =====================================================================

    private static async Task<bool> SendAlertAsync(
        IEmailService emailService,
        INotificationService notificationService,
        IConfiguration config,
        Request request,
        AlertRecipient recipient,
        string alertLevel,
        int daysRemaining,
        string frontendBaseUrl)
    {
        var reqNum = request.RequestNumber ?? request.Id.ToString()[..8];
        var firstName = recipient.FullName.Split(' ').FirstOrDefault() ?? recipient.FullName;
        var requestUrl = $"{frontendBaseUrl}/requests/{request.Id}?mode=view";

        // Build subject and urgency styling
        var (subject, urgencyColor, urgencyBg, urgencyBorder, headlineText, daysLabel) = alertLevel switch
        {
            "WARNING_3D" => (
                $"[Portal Gerencial] Proforma vence em 3 dias — {reqNum}",
                "#b45309", "#fffbeb", "#fde68a",
                "⚠️ Proforma vence em 3 dias",
                "3 dias restantes"),
            "WARNING_1D" => (
                $"[Portal Gerencial] Proforma vence amanhã — {reqNum}",
                "#c2410c", "#fff7ed", "#fed7aa",
                "⚠️ Proforma vence amanhã",
                "1 dia restante"),
            "CRITICAL_0D" => (
                $"[Portal Gerencial] Proforma vence hoje — {reqNum}",
                "#dc2626", "#fef2f2", "#fecaca",
                "🔴 Proforma vence HOJE",
                "Vence hoje"),
            "EXPIRED" => (
                $"[Portal Gerencial] Proforma vencida — {reqNum}",
                "#991b1b", "#fef2f2", "#fca5a5",
                "❌ Proforma VENCIDA",
                $"Vencida há {Math.Abs(daysRemaining)} dia(s)"),
            _ => ($"[Portal Gerencial] Proforma — {reqNum}", "#6b7280", "#f9fafb", "#e5e7eb", "Proforma", "—")
        };

        var currencyCode = request.Currency?.Code ?? "AOA";
        var supplierName = request.Supplier?.Name ?? "—";
        var statusName = request.Status?.Name ?? "—";

        var bodyHtml = $@"
<p>Olá <b>{firstName}</b>,</p>
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

        bool emailSent = false;
        try
        {
            emailSent = await emailService.SendWorkflowNotificationAsync(
                recipient.Email,
                recipient.FullName,
                subject,
                headlineText,
                bodyHtml,
                requestUrl,
                "Ver Pedido →");
        }
        catch
        {
            // Best-effort — email failure should not stop processing
            emailSent = false;
        }

        // In-app notification (bell icon)
        try
        {
            var inAppTitle = alertLevel switch
            {
                "WARNING_3D" => $"Proforma vence em 3 dias — {reqNum}",
                "WARNING_1D" => $"Proforma vence amanhã — {reqNum}",
                "CRITICAL_0D" => $"Proforma vence HOJE — {reqNum}",
                "EXPIRED" => $"Proforma VENCIDA — {reqNum}",
                _ => $"Alerta Proforma — {reqNum}"
            };

            var inAppMessage = $"O pedido {reqNum} ({statusName}) tem vencimento de Proforma em {request.NeedByDateUtc.Value:dd/MM/yyyy}. {daysLabel}.";

            await notificationService.CreateNotificationAsync(
                recipient.UserId,
                inAppTitle,
                inAppMessage,
                alertLevel == "EXPIRED" || alertLevel == "CRITICAL_0D" ? NotificationTypes.Error : NotificationTypes.Warning,
                $"/requests/{request.Id}?mode=view");
        }
        catch
        {
            // Best-effort — in-app notification failure should not stop processing
        }

        return emailSent;
    }

    // =====================================================================
    // SCHEDULE HELPERS
    // =====================================================================

    private static async Task WaitUntilNextCheckTimeAsync(int checkTimeUtcHour, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var nextRun = now.Date.AddHours(checkTimeUtcHour);

        if (nextRun <= now)
            nextRun = nextRun.AddDays(1);

        var delay = nextRun - now;

        if (delay.TotalMinutes > 1)
        {
            // In development, don't wait — run immediately
            // In production, wait until the scheduled time
            // We use a short initial delay to allow the app to fully start
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    // =====================================================================
    // INTERNAL TYPES
    // =====================================================================

    private record AlertRecipient(Guid UserId, string Email, string FullName);
}
