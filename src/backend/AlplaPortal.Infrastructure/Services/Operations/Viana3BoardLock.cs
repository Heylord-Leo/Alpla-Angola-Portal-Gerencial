using System.Data;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Operations;

/// <summary>
/// Per-board mutual exclusion for every mutation of card designs and design assets.
///
/// The lock is a SQL Server application lock (<c>sp_getapplock</c>) owned by the
/// transaction, so it serialises writers across every API instance and is released
/// by the commit or rollback. The delegate runs inside the context's execution
/// strategy; it must therefore be database-only (or idempotent) — callers keep file
/// writes outside and use idempotency keys (asset id, save token) so a retried
/// delegate after a lost commit acknowledgement recognises its own earlier work.
///
/// Lock timeout → <see cref="Viana3CardDesignConflictException"/> BOARD_BUSY (409 + Retry-After).
/// </summary>
public static class Viana3BoardLock
{
    public const int LockTimeoutMs = 5000;
    private const int BusyErrorNumber = 51000;

    public static string ResourceFor(string boardKey) => $"viana3-card-design:{boardKey}";

    /// <summary>Runs <paramref name="body"/> inside a locked transaction and commits it.</summary>
    public static async Task<T> RunAsync<T>(ApplicationDbContext db, string boardKey, Func<CancellationToken, Task<T>> body, CancellationToken ct, int? lockTimeoutMs = null)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async innerCt =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, innerCt);
            await AcquireAsync(db, boardKey, lockTimeoutMs ?? LockTimeoutMs, innerCt);
            var result = await body(innerCt);
            await tx.CommitAsync(innerCt);
            return result;
        }, ct);
    }

    private static async Task AcquireAsync(ApplicationDbContext db, string boardKey, int timeoutMs, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1}; " +
                "IF @r < 0 THROW 51000, 'BOARD_BUSY', 1;",
                new object[] { ResourceFor(boardKey), timeoutMs }, ct);
        }
        catch (SqlException ex) when (ex.Number == BusyErrorNumber)
        {
            throw new Viana3CardDesignConflictException("BOARD_BUSY", "Outro utilizador está a guardar neste momento; tente novamente.");
        }
    }
}
