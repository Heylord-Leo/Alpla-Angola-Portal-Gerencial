namespace AlplaPortal.Domain.Entities;

public class Role
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
}

public class UserRoleAssignment
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    // Optional for V1 to scope an Approver
    public int? DepartmentScopeId { get; set; }
    public Department? DepartmentScope { get; set; }
}
