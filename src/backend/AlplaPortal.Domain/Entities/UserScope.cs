namespace AlplaPortal.Domain.Entities;

public class UserPlantScope
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public int PlantId { get; set; }
    public Plant Plant { get; set; } = null!;
}

public class UserDepartmentScope
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
}
