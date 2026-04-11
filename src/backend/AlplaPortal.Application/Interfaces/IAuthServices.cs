using AlplaPortal.Domain.Entities;
using AlplaPortal.Application.Models.Auth;

namespace AlplaPortal.Application.Interfaces;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
    string GenerateRandomPassword(int length = 12);
}

public interface IJwtService
{
    string GenerateToken(User user, List<string> roles);
}

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<string> ResetPasswordAsync(Guid userId); // Returns new temp password
    Task<bool> ForgotPasswordAsync(string email, string frontendBaseUrl);
    Task<bool> ResetPasswordWithTokenAsync(string email, string token, string newPassword);
}
