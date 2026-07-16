using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class DepartmentManagerDto
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public int? PlantId { get; set; }
    public string? PlantName { get; set; }
    public Guid UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public bool UserIsActive { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AddDepartmentManagerDto
{
    [Required(ErrorMessage = "O utilizador é obrigatório.")]
    public Guid? UserId { get; set; }

    /// <summary>Null = manager global (todas as plantas).</summary>
    public int? PlantId { get; set; }
}

/// <summary>
/// Result of adding a manager. Lists the visibility scopes auto-created by rule D3
/// so the UI can show the post-save confirmation.
/// </summary>
public class AddDepartmentManagerResultDto
{
    public DepartmentManagerDto Manager { get; set; } = null!;

    /// <summary>Plant names whose UserPlantScope was created by this operation.</summary>
    public List<string> CreatedPlantScopes { get; set; } = new();

    /// <summary>Department names whose UserDepartmentScope was created by this operation.</summary>
    public List<string> CreatedDepartmentScopes { get; set; } = new();
}
