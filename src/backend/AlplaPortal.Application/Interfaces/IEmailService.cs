using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces;

public interface IEmailService
{
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetLink);
}
