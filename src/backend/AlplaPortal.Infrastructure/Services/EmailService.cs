using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using AlplaPortal.Infrastructure.Logging;

namespace AlplaPortal.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly ISmtpSettingsService _smtpSettingsService;
    private readonly AdminLogWriter _adminLog;

    private const string LogoContentId = "alpla-logo";
    private const string LogoFileName = "logo-v2.png";

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IWebHostEnvironment env, ISmtpSettingsService smtpSettingsService, AdminLogWriter adminLog)
    {
        _config = config;
        _logger = logger;
        _env = env;
        _smtpSettingsService = smtpSettingsService;
        _adminLog = adminLog;
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetLink)
    {
        try
        {
            // --- Resolve SMTP settings from DB → appsettings → defaults ---
            var smtp = await _smtpSettingsService.GetEffectiveSettingsAsync();

            // --- Safety guard: block localhost URLs outside Development ---
            if (!_env.IsDevelopment() && (resetLink.Contains("localhost") || resetLink.Contains("127.0.0.1")))
            {
                var errorMsg = "CRITICAL: Attempted to generate a transactional email containing localhost URLs outside of Development environment. Operation aborted to protect production integrity.";
                _logger.LogCritical(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            if (string.IsNullOrEmpty(smtp.Server) || string.IsNullOrEmpty(smtp.SenderEmail) || string.IsNullOrEmpty(smtp.Password))
            {
                _logger.LogError("SMTP configuration is missing or incomplete. Server: {Server}, SenderEmail: {SenderEmail}, HasPassword: {HasPwd}",
                    smtp.Server, smtp.SenderEmail, !string.IsNullOrEmpty(smtp.Password));
                return false;
            }

            _logger.LogInformation("Building password reset email for {Email} with resetLink base: {ResetLink}", toEmail, resetLink);

            var fromAddress = new MailAddress(smtp.SenderEmail, smtp.SenderName);
            var toAddress = new MailAddress(toEmail);

            using var smtpClient = new SmtpClient(smtp.Server, smtp.Port)
            {
                Credentials = new NetworkCredential(smtp.SenderEmail, smtp.Password),
                EnableSsl = smtp.EnableSsl
            };

            // --- Resolve logo asset with robust multi-path fallback ---
            var logoPath = ResolveLogoPath();
            var hasLogo = logoPath != null;

            // Build the logo HTML: CID inline if file exists, text fallback otherwise
            var logoHtml = hasLogo
                ? $"<img src='cid:{LogoContentId}' alt='ALPLA Portal' style='max-width: 150px; margin-bottom: 20px;' />"
                : "<h2 style='color: #002D72; margin-bottom: 20px;'>ALPLA Portal</h2>";

            var body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; color: #333;'>
                    {logoHtml}
                    <p>Olá,</p>
                    <p>Recebemos um pedido para repor a sua palavra-passe de acesso ao Portal Gerencial.</p>
                    <p>Por favor, clique no botão abaixo para definir uma nova senha. Este link <b>expira em 15 minutos</b>.</p>
                    <div style='text-align: left; margin: 30px 0;'>
                        <a href='{resetLink}' 
                           style='background-color: #002D72; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 4px; font-weight: bold;'>
                           Redefinir Palavra-passe
                        </a>
                    </div>
                    <p style='color: #666; font-size: 12px;'>
                        Se o botão não funcionar, copie este endereço para o seu navegador:<br/>
                        <span style='color: #002D72;'>{resetLink}</span>
                    </p>
                    <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;' />
                    <p style='font-size: 12px; color: #999;'>
                        Se não pediu a reposição da palavra-passe, ignore este e-mail.<br/>
                        ALPLA Mail Service - Não responda a este e-mail.
                    </p>
                </div>
            ";

            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = "Recuperação de Palavra-passe - ALPLA Portal"
            };

            // Attach logo as CID inline resource if the file was resolved
            if (hasLogo)
            {
                var htmlView = AlternateView.CreateAlternateViewFromString(body, null, MediaTypeNames.Text.Html);
                var logoResource = new LinkedResource(logoPath!, MediaTypeNames.Image.Png)
                {
                    ContentId = LogoContentId,
                    TransferEncoding = TransferEncoding.Base64
                };
                htmlView.LinkedResources.Add(logoResource);
                message.AlternateViews.Add(htmlView);
            }
            else
            {
                message.Body = body;
                message.IsBodyHtml = true;
            }

            await smtpClient.SendMailAsync(message);
            _logger.LogInformation("Password reset email dispatched to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver reset email to {Email}", toEmail);
            return false;
        }
    }


    public async Task<bool> SendWorkflowNotificationAsync(string toEmail, string recipientName, string subject, string headline, string bodyHtml, string? actionUrl = null, string? actionLabel = null)
    {
        try
        {
            var smtp = await _smtpSettingsService.GetEffectiveSettingsAsync();

            if (string.IsNullOrEmpty(smtp.Server) || string.IsNullOrEmpty(smtp.SenderEmail) || string.IsNullOrEmpty(smtp.Password))
            {
                _logger.LogError("SMTP configuration is missing or incomplete for workflow notification. Skipping email to {Email}.", toEmail);
                await _adminLog.WriteAsync(
                    "Error",
                    "EmailService",
                    "SMTP_DISPATCH_FAILED",
                    $"Erro Crítico: O servidor SMTP não está configurado para tentar enviar a notificação a {toEmail}.",
                    payload: "A configuração de SMTP não possui senha ou host."
                );
                // Cannot proceed, throw clear exception
                throw new InvalidOperationException("SMTP configuration is missing or incomplete for workflow notification.");
            }

            var fromAddress = new MailAddress(smtp.SenderEmail, smtp.SenderName);
            var toAddress = new MailAddress(toEmail);

            using var smtpClient = new SmtpClient(smtp.Server, smtp.Port)
            {
                Credentials = new NetworkCredential(smtp.SenderEmail, smtp.Password),
                EnableSsl = smtp.EnableSsl
            };

            // Build branded template
            var logoPath = ResolveLogoPath();
            var hasLogo = logoPath != null;
            var logoHtml = hasLogo
                ? $"<img src='cid:{LogoContentId}' alt='ALPLA Portal' style='max-width: 150px; margin-bottom: 20px;' />"
                : "<h2 style='color: #002D72; margin-bottom: 20px;'>ALPLA Portal</h2>";

            var actionButtonHtml = !string.IsNullOrEmpty(actionUrl)
                ? $@"<div style='text-align: left; margin: 24px 0;'>
                        <a href='{actionUrl}' 
                           style='background-color: #002D72; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 4px; font-weight: bold;'>
                           {actionLabel ?? "Ver Pedido"} &rarr;
                        </a>
                    </div>"
                : "";

            var greetingName = !string.IsNullOrWhiteSpace(recipientName) ? recipientName.Split(' ')[0] : "Utilizador";

            var body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; color: #333;'>
                    {logoHtml}
                    <h3 style='color: #002D72; margin-bottom: 8px;'>{headline}</h3>
                    <p>Olá {greetingName},</p>
                    <div style='margin: 16px 0; line-height: 1.6;'>{bodyHtml}</div>
                    {actionButtonHtml}
                    <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;' />
                    <p style='font-size: 12px; color: #999;'>
                        ALPLA Portal Gerencial — Notificação de Workflow<br/>
                        Não responda a este e-mail.
                    </p>
                </div>
            ";

            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject
            };

            if (hasLogo)
            {
                var htmlView = AlternateView.CreateAlternateViewFromString(body, null, MediaTypeNames.Text.Html);
                var logoResource = new LinkedResource(logoPath!, MediaTypeNames.Image.Png)
                {
                    ContentId = LogoContentId,
                    TransferEncoding = TransferEncoding.Base64
                };
                htmlView.LinkedResources.Add(logoResource);
                message.AlternateViews.Add(htmlView);
            }
            else
            {
                message.Body = body;
                message.IsBodyHtml = true;
            }

            await smtpClient.SendMailAsync(message);
            _logger.LogInformation("Workflow notification email dispatched to {Email} (Subject: {Subject})", toEmail, subject);
            
            await _adminLog.WriteAsync(
                "Info",
                "EmailService",
                "SMTP_DISPATCH_SUCCESS",
                $"E-mail despachado para {toEmail}. Assunto: {subject}",
                payload: $"Host: {smtp.Server}:{smtp.Port}. TLS: {smtp.EnableSsl}"
            );
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver workflow notification email to {Email} (Subject: {Subject})", toEmail, subject);
            await _adminLog.WriteAsync(
                "Error",
                "EmailService",
                "SMTP_DISPATCH_FAILED",
                $"Falha crítica ao enviar notificação por E-mail para {toEmail}",
                exceptionDetail: ex.Message
            );
            
            // Re-throw so orchestrator captures the true failure
            throw;
        }
    }
    /// <summary>
    /// Resolves the physical path for the ALPLA logo asset using multiple candidate locations.
    /// Priority: 1) Explicit config override  2) Frontend public dir (dev layout)  3) wwwroot (published layout).
    /// Returns null if the logo cannot be found at any candidate path.
    /// </summary>
    private string? ResolveLogoPath()
    {
        // 1) Allow explicit override via configuration
        var configPath = _config["AppConfig:LogoPath"];
        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            _logger.LogInformation("Logo resolved from AppConfig:LogoPath -> {Path}", configPath);
            return configPath;
        }

        // 2) Development layout: frontend/public relative to repo root
        var candidatePaths = new[]
        {
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "frontend", "public", LogoFileName)),
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot", LogoFileName)),
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, LogoFileName))
        };

        foreach (var candidate in candidatePaths)
        {
            if (File.Exists(candidate))
            {
                _logger.LogInformation("Logo resolved at candidate path -> {Path}", candidate);
                return candidate;
            }
            _logger.LogDebug("Logo candidate not found: {Path}", candidate);
        }

        _logger.LogWarning(
            "Logo file '{FileName}' not found at any candidate path. Email will use text fallback. " +
            "Searched: [{Paths}]. Set AppConfig:LogoPath for explicit override.",
            LogoFileName,
            string.Join(", ", candidatePaths));

        return null;
    }
}
