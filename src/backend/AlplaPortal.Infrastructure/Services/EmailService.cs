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

    // ═══════════════════════════════════════════════════════════════
    //  Environment Identification — Centralized Policy
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads AppEnvironment:Code from configuration.
    /// Returns "PROD" if not set (safe default).
    /// </summary>
    private string GetEnvironmentCode() =>
        _config["AppEnvironment:Code"]?.Trim().ToUpperInvariant() ?? "PROD";

    /// <summary>
    /// Reads AppEnvironment:Name from configuration.
    /// </summary>
    private string GetEnvironmentName() =>
        _config["AppEnvironment:Name"] ?? GetEnvironmentCode();

    /// <summary>
    /// Returns true when the current environment is NOT production.
    /// </summary>
    private bool IsNonProduction() => GetEnvironmentCode() != "PROD";

    /// <summary>
    /// Centralized environment policy applied to every outgoing email (except SMTP test).
    /// In non-production environments, this method:
    ///   1. Prepends a subject prefix (always on in non-prod, customizable via DB).
    ///   2. Injects a styled warning banner into the email body.
    ///   3. Redirects to a test recipient if configured.
    ///   4. Logs original/final recipients, subject, environment, and timestamp.
    /// In PROD, this method is a no-op unless the admin explicitly enabled overrides.
    /// </summary>
    private async Task ApplyEnvironmentPolicy(MailMessage message, SmtpEffectiveSettings smtp, string originalToEmail)
    {
        var envCode = GetEnvironmentCode();
        var envName = GetEnvironmentName();
        var isNonProd = envCode != "PROD";

        // ── 1. Subject Prefix ──────────────────────────────────────
        // Non-production: ALWAYS prefix, regardless of EnableSubjectPrefix flag.
        // The flag only controls whether admin has explicitly customized it.
        // PROD: Only prefix if admin explicitly enabled it (safeguard).
        bool shouldPrefix = isNonProd || smtp.EnableSubjectPrefix;

        if (shouldPrefix)
        {
            var prefix = !string.IsNullOrWhiteSpace(smtp.SubjectPrefixText)
                ? smtp.SubjectPrefixText.Trim()
                : $"[{envCode} - IGNORE]";

            // Avoid double-prefixing
            if (!message.Subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                message.Subject = $"{prefix} {message.Subject}";
            }
        }

        // ── 2. Body Warning Banner ─────────────────────────────────
        // Non-production: ALWAYS inject banner.
        // PROD: Only if explicitly enabled.
        bool shouldBanner = isNonProd || smtp.EnableBodyWarningBanner;

        if (shouldBanner)
        {
            var warningText = !string.IsNullOrWhiteSpace(smtp.WarningBannerText)
                ? smtp.WarningBannerText.Trim()
                : $"Esta mensagem foi gerada pelo ambiente {envName} do ALPLA Portal. " +
                  "Não representa um pedido real e nenhuma ação é necessária.";

            var bannerHtml = $@"
                <div style='background-color: #FFF3CD; border: 2px solid #FFC107; border-radius: 8px; padding: 16px; margin-bottom: 20px; font-family: Arial, sans-serif;'>
                    <strong style='color: #856404; font-size: 14px;'>⚠️ {envName.ToUpperInvariant()} — IGNORE THIS EMAIL</strong>
                    <p style='color: #856404; font-size: 13px; margin: 8px 0 0 0;'>{warningText}</p>
                </div>";

            InjectBannerIntoMessage(message, bannerHtml);
        }

        // ── 3. Recipient Redirection ───────────────────────────────
        // Only applicable in non-production environments.
        // In PROD, redirection is never applied (safeguard).
        if (isNonProd && smtp.RedirectAllToTestRecipient && !string.IsNullOrWhiteSpace(smtp.TestRecipientEmail))
        {
            var finalRecipient = smtp.TestRecipientEmail.Trim();

            // Show original recipients in body if configured
            if (smtp.ShowOriginalRecipientsInBody)
            {
                var recipientInfoHtml = $@"
                    <div style='background-color: #E8F4FD; border: 1px solid #B3D9F2; border-radius: 6px; padding: 12px; margin-bottom: 16px; font-family: Arial, sans-serif;'>
                        <strong style='color: #1565C0; font-size: 12px;'>📧 Destinatário(s) original(is):</strong>
                        <p style='color: #1565C0; font-size: 12px; margin: 4px 0 0 0; font-family: monospace;'>{originalToEmail}</p>
                    </div>";

                InjectBannerIntoMessage(message, recipientInfoHtml);
            }

            // Replace recipients
            message.To.Clear();
            message.To.Add(new MailAddress(finalRecipient));

            _logger.LogWarning(
                "EMAIL REDIRECT [{Env}]: Original={Original} → Redirected={Final}, Subject={Subject}",
                envCode, originalToEmail, finalRecipient, message.Subject);
        }

        // ── 4. Audit Log ───────────────────────────────────────────
        var finalTo = string.Join(", ", message.To);
        await _adminLog.WriteAsync(
            "Info",
            "EmailService",
            "EMAIL_ENV_POLICY",
            $"[{envCode}] E-mail preparado. De: {message.From?.Address} → Para: {finalTo}. Assunto: {message.Subject}",
            payload: $"Original: {originalToEmail}. Redirecionado: {smtp.RedirectAllToTestRecipient}. Env: {envCode}. Timestamp: {DateTime.UtcNow:O}"
        );
    }

    /// <summary>
    /// Special environment policy for SMTP connection test emails.
    /// Applies subject prefix but never redirects or injects body banners.
    /// </summary>
    private void ApplyTestConnectionEnvironmentPolicy(MailMessage message)
    {
        var envCode = GetEnvironmentCode();

        if (envCode != "PROD")
        {
            message.Subject = $"[{envCode} - SMTP TEST] {message.Subject}";
        }
    }

    /// <summary>
    /// Injects an HTML banner at the top of the email body.
    /// Handles both AlternateView (CID inline images) and plain Body approaches.
    /// </summary>
    private static void InjectBannerIntoMessage(MailMessage message, string bannerHtml)
    {
        if (message.AlternateViews.Count > 0)
        {
            // For messages using AlternateViews (CID inline images),
            // we need to read the existing HTML, prepend the banner, and rebuild.
            var htmlView = message.AlternateViews[0];
            using var reader = new StreamReader(htmlView.ContentStream);
            var existingHtml = reader.ReadToEnd();
            var newHtml = bannerHtml + existingHtml;

            // Preserve linked resources (logo etc.)
            var resources = new System.Collections.Generic.List<LinkedResource>();
            foreach (var res in htmlView.LinkedResources)
            {
                resources.Add(res);
            }

            // Remove old view and create new one
            message.AlternateViews.Clear();
            var newView = AlternateView.CreateAlternateViewFromString(newHtml, null, MediaTypeNames.Text.Html);
            foreach (var res in resources)
            {
                newView.LinkedResources.Add(res);
            }
            message.AlternateViews.Add(newView);
        }
        else if (message.IsBodyHtml)
        {
            // Simple HTML body — just prepend
            message.Body = bannerHtml + message.Body;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Public Email Methods
    // ═══════════════════════════════════════════════════════════════

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

            // ── Apply environment policy before sending ──
            await ApplyEnvironmentPolicy(message, smtp, toEmail);

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

            // ── Apply environment policy before sending ──
            await ApplyEnvironmentPolicy(message, smtp, toEmail);

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

    public async Task<bool> SendWithAttachmentAsync(string toEmail, string recipientName, string subject, string headline, string bodyHtml, string attachmentPath, string attachmentFileName)
    {
        try
        {
            var smtp = await _smtpSettingsService.GetEffectiveSettingsAsync();

            if (string.IsNullOrEmpty(smtp.Server) || string.IsNullOrEmpty(smtp.SenderEmail) || string.IsNullOrEmpty(smtp.Password))
            {
                _logger.LogError("SMTP configuration is missing for attachment email to {Email}.", toEmail);
                throw new InvalidOperationException("SMTP configuration is missing or incomplete.");
            }

            var fromAddress = new MailAddress(smtp.SenderEmail, smtp.SenderName);
            var toAddress = new MailAddress(toEmail);

            using var smtpClient = new SmtpClient(smtp.Server, smtp.Port)
            {
                Credentials = new NetworkCredential(smtp.SenderEmail, smtp.Password),
                EnableSsl = smtp.EnableSsl
            };

            // Build branded template (same pattern as workflow notification)
            var logoPath = ResolveLogoPath();
            var hasLogo = logoPath != null;
            var logoHtml = hasLogo
                ? $"<img src='cid:{LogoContentId}' alt='ALPLA Portal' style='max-width: 150px; margin-bottom: 20px;' />"
                : "<h2 style='color: #002D72; margin-bottom: 20px;'>ALPLA Portal</h2>";

            var greetingName = !string.IsNullOrWhiteSpace(recipientName) ? recipientName.Split(' ')[0] : "Utilizador";

            var body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; color: #333;'>
                    {logoHtml}
                    <h3 style='color: #002D72; margin-bottom: 8px;'>{headline}</h3>
                    <p>Olá {greetingName},</p>
                    <div style='margin: 16px 0; line-height: 1.6;'>{bodyHtml}</div>
                    <p style='margin-top: 16px; padding: 12px; background-color: #f0f4f8; border-radius: 6px; font-size: 13px; color: #555;'>
                        📎 O documento <strong>{attachmentFileName}</strong> encontra-se em anexo a este e-mail.
                    </p>
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

            // Attach the file
            if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
            {
                // Auto-detect MIME type from extension for correct email client handling
                var ext = Path.GetExtension(attachmentPath).ToLowerInvariant();
                var mimeType = ext switch
                {
                    ".pdf" => MediaTypeNames.Application.Pdf,
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    _ => MediaTypeNames.Application.Octet
                };
                var attachment = new Attachment(attachmentPath, mimeType);
                attachment.ContentDisposition!.FileName = attachmentFileName;
                message.Attachments.Add(attachment);
            }
            else
            {
                _logger.LogWarning("Attachment file not found at {Path} for email to {Email}. Sending without attachment.", attachmentPath, toEmail);
            }

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

            // ── Apply environment policy before sending ──
            await ApplyEnvironmentPolicy(message, smtp, toEmail);

            await smtpClient.SendMailAsync(message);
            _logger.LogInformation("Email with attachment dispatched to {Email} (Subject: {Subject}, File: {File})", toEmail, subject, attachmentFileName);

            await _adminLog.WriteAsync(
                "Info",
                "EmailService",
                "SMTP_ATTACHMENT_DISPATCH_SUCCESS",
                $"E-mail com anexo despachado para {toEmail}. Assunto: {subject}. Ficheiro: {attachmentFileName}",
                payload: $"Host: {smtp.Server}:{smtp.Port}. TLS: {smtp.EnableSsl}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver email with attachment to {Email} (Subject: {Subject})", toEmail, subject);
            await _adminLog.WriteAsync(
                "Error",
                "EmailService",
                "SMTP_ATTACHMENT_DISPATCH_FAILED",
                $"Falha ao enviar e-mail com anexo para {toEmail}",
                exceptionDetail: ex.Message
            );
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Logo Resolution
    // ═══════════════════════════════════════════════════════════════

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
