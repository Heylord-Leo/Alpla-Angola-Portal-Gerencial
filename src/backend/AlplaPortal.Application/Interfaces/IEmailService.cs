using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces;

public interface IEmailService
{
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetLink);

    /// <summary>
    /// Sends a branded workflow notification email using the shared transactional template.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="recipientName">Recipient display name for personalization.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="headline">Primary heading inside the email body.</param>
    /// <param name="bodyHtml">HTML content for the main message area.</param>
    /// <param name="actionUrl">Optional CTA button URL.</param>
    /// <param name="actionLabel">Optional CTA button label (e.g., "Ver Pedido").</param>
    Task<bool> SendWorkflowNotificationAsync(string toEmail, string recipientName, string subject, string headline, string bodyHtml, string? actionUrl = null, string? actionLabel = null);
}
