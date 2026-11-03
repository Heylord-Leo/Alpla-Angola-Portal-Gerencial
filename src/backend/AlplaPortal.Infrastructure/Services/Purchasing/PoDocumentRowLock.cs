using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>
/// v2.249.5 — serializes the two writers of ONE P.O. file row: <c>register-po</c> (which makes the file the group's current
/// document) and <c>DELETE /attachments/{id}</c> (which soft-deletes a temporary upload). Both run inside a transaction and
/// call <see cref="LockAndReloadAsync"/> before deciding: on SQL Server the row is read <c>WITH (UPDLOCK, HOLDLOCK)</c>
/// (repo convention: PriorEcfConfirmationGuard / PoGroupCancellationService), so the second writer waits for the first
/// transaction to end and then re-reads the committed row — a registration never commits with a file deleted meanwhile
/// (<see cref="ConcurrentChangeCode"/>), and a delete never commits against a file registered meanwhile (the evidence
/// guard re-evaluates after the lock and answers 409 PO_EVIDENCE_PROTECTED). On other providers (tests) only the re-read
/// happens: the tracked instance always reflects the committed state at decision time.
/// </summary>
public static class PoDocumentRowLock
{
    public const string LockSql = "SELECT * FROM [RequestAttachments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}";

    /// <summary>409 code of register-po when the chosen P.O. file was deleted (or voided) between its selection and the lock.</summary>
    public const string ConcurrentChangeCode = "PO_DOCUMENT_CONCURRENT_CHANGE";

    /// <summary>
    /// Locks the attachment row for the rest of the caller's transaction (SQL Server) and reloads the tracked instance from
    /// the database. Pending modifications on that instance are discarded (callers lock BEFORE they modify). Returns false when
    /// the row no longer exists (hard delete — never done by the portal, kept for completeness).
    /// </summary>
    public static async Task<bool> LockAndReloadAsync(ApplicationDbContext context, RequestAttachment attachment)
    {
        if (context.Database.IsSqlServer())
        {
            // The composed query keeps the hint inside the sub-select; AsNoTracking so the locked read never touches the
            // tracked instance (ReloadAsync below is the single source of the fresh values).
            await context.RequestAttachments.FromSqlRaw(LockSql, attachment.Id).AsNoTracking().Select(a => a.Id).ToListAsync();
        }
        var entry = context.Entry(attachment);
        await entry.ReloadAsync();
        return entry.State != EntityState.Detached;
    }
}
