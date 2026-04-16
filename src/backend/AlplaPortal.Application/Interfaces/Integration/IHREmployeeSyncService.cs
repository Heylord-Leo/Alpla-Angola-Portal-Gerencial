using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Syncs Innux employees into the local HREmployee projection table.
/// Provides scoped employee queries for HR module operations.
/// </summary>
public interface IHREmployeeSyncService
{
    /// <summary>
    /// Syncs all active employees from Innux into HREmployees table.
    /// Upserts by InnuxEmployeeId. Deactivates employees no longer in Innux.
    /// </summary>
    Task<HRSyncLog> SyncFromInnuxAsync(Guid? triggeredByUserId = null);

    /// <summary>Returns the most recent sync log entry.</summary>
    Task<HRSyncLog?> GetLastSyncAsync();
}
