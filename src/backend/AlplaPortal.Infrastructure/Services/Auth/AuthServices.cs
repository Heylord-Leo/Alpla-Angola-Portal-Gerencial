using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Models.Auth;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AlplaPortal.Infrastructure.Logging;

namespace AlplaPortal.Infrastructure.Services.Auth;

public class PasswordHasher : IPasswordHasher
{
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword)) return false;
        try 
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch
        {
            return false;
        }
    }

    public string GenerateRandomPassword(int length = 12)
    {
        const string chars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789!@$#%&*";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

public class JwtService : IJwtService
{
    private readonly JwtOptions _options;

    public JwtService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(User user, List<string> roles)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_options.Secret);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim("mustChangePassword", user.MustChangePassword.ToString().ToLower())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly SecurityOptions _securityOptions;
    private readonly AdminLogWriter _adminLogWriter;

    public AuthService(
        ApplicationDbContext context, 
        IPasswordHasher passwordHasher, 
        IJwtService jwtService,
        IOptions<SecurityOptions> securityOptions,
        AdminLogWriter adminLogWriter)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _securityOptions = securityOptions.Value;
        _adminLogWriter = adminLogWriter;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .Where(u => u.Email == request.Email && u.IsActive)
            .FirstOrDefaultAsync();

        // 1. Check if account is currently locked
        if (user != null && user.LockoutEndUtc.HasValue && user.LockoutEndUtc > DateTime.UtcNow)
        {
            await _adminLogWriter.WriteAsync("Warning", "Auth", "LOGIN_BLOCKED", 
                $"Login attempt for locked account: {request.Email}");
            
            // Generic message for Phase 1 to avoid enumeration confirmation of lockout status per-user
            throw new UnauthorizedAccessException("Tentativas excedidas ou conta bloqueada temporariamente. Tente novamente mais tarde.");
        }

        // 2. Validate credentials
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            if (user != null)
            {
                user.AccessFailedCount++;
                
                if (user.AccessFailedCount >= _securityOptions.Authentication.MaxFailedAttempts)
                {
                    user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(_securityOptions.Authentication.LockoutDurationMinutes);
                    await _adminLogWriter.WriteAsync("Warning", "Auth", "ACCOUNT_LOCKED", 
                        $"Account {user.Email} locked for {_securityOptions.Authentication.LockoutDurationMinutes} minutes due to multiple failures.");
                }
                
                await _context.SaveChangesAsync();
            }

            // Generic error message for both wrong password and non-existent user
            return null; 
        }

        // 3. Successful login - Reset security counters
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAt = DateTime.UtcNow;

        var roles = await _context.UserRoleAssignments
            .Where(ura => ura.UserId == user.Id)
            .Include(ura => ura.Role)
            .Select(ura => ura.Role.RoleName)
            .ToListAsync();

        var token = _jwtService.GenerateToken(user, roles);
        
        await _context.SaveChangesAsync();

        return new LoginResponse
        {
            Token = token,
            User = new UserProfileDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Roles = roles,
                MustChangePassword = user.MustChangePassword
            }
        };
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<string> ResetPasswordAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found.");

        var tempPassword = _passwordHasher.GenerateRandomPassword();
        user.PasswordHash = _passwordHasher.HashPassword(tempPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return tempPassword;
    }
}
