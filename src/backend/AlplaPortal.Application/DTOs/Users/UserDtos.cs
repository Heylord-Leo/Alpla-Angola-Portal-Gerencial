using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Users;

public class UserListDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Plants { get; set; } = new();
    public List<string> Departments { get; set; } = new();
    public bool CanEdit { get; set; } // Computed server-side based on actor's scope
}

public class UserDetailsDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public List<int> RoleIds { get; set; } = new();
    public List<int> PlantIds { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();
}

public class CreateUserDto
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<int> RoleIds { get; set; } = new();
    public List<int> PlantIds { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();
}

public class UpdateUserDto
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<int> RoleIds { get; set; } = new();
    public List<int> PlantIds { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();
}

public class ResetPasswordResponseDto
{
    public string NewPassword { get; set; } = string.Empty;
}
