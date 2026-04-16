namespace AlplaPortal.Application.Interfaces.Integration;

public class DepartmentSyncResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Processed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public interface IPrimaveraDepartmentSyncService
{
    Task<DepartmentSyncResult> SyncDepartmentsAsync(CancellationToken cancellationToken = default);
}
