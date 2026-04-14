namespace AlplaPortal.Application.DTOs.Integration;

public class PrimaveraEmployeeDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ShortName { get; set; }
    public string? FirstName { get; set; }
    public string? FirstLastName { get; set; }
    public string? SecondLastName { get; set; }
    
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    
    public string? DepartmentCode { get; set; }
    public string? DepartmentName { get; set; }
    
    public DateTime? HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public bool? IsTemporarilyInactive { get; set; }

    /// <summary>Which Primavera company/database this record was read from.</summary>
    public string? SourceCompany { get; set; }
}
