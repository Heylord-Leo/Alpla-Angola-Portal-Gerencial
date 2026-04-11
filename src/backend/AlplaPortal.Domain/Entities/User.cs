namespace AlplaPortal.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? ExternalId { get; set; }

    // Security Phase 1: Login Protection
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    // Security Phase 2: Password Reset Protection
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiryUtc { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public ICollection<UserRoleAssignment> UserRoleAssignments { get; set; } = new List<UserRoleAssignment>();
    public ICollection<UserPlantScope> UserPlantScopes { get; set; } = new List<UserPlantScope>();
    public ICollection<UserDepartmentScope> UserDepartmentScopes { get; set; } = new List<UserDepartmentScope>();
}
