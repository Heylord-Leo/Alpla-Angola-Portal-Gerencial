namespace AlplaPortal.Application.Models.Auth;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public UserProfileDto User { get; set; } = null!;
}

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Plants { get; set; } = new();
    public List<string> Departments { get; set; } = new();

    /// <summary>
    /// Department IDs where this user is the ResponsibleUser.
    /// Used to grant implicit HR module access to Department Managers
    /// without requiring the HR role.
    /// </summary>
    public List<int> ManagedDepartmentIds { get; set; } = new();

    public bool MustChangePassword { get; set; }
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
