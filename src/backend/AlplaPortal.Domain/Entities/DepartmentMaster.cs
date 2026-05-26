namespace AlplaPortal.Domain.Entities;

public class DepartmentMaster
{
    public int Id { get; set; }
    
    /// <summary>
    /// Source system originating this department. Example: "PRIMAVERA"
    /// </summary>
    public string SourceSystem { get; set; } = "PRIMAVERA";
    
    /// <summary>
    /// Exact source database or context containing this department. Example: "PRI297514001"
    /// </summary>
    public string SourceDatabase { get; set; } = string.Empty;
    
    /// <summary>
    /// Business-friendly name to classify the source database. Example: "AlplaPLASTICOS"
    /// </summary>
    public string CompanyCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Department master code as defined by the source system. Example: "DTI"
    /// </summary>
    public string DepartmentCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Full description or name of the department.
    /// </summary>
    public string DepartmentName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    
    public DateTime? LastSyncedAtUtc { get; set; }

    /// <summary>
    /// Linked HR Employees mapping to this synced master department.
    /// </summary>
    public ICollection<HREmployee> Employees { get; set; } = new List<HREmployee>();
}
