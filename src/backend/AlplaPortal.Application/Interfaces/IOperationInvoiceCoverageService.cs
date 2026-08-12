using AlplaPortal.Domain.Services;

namespace AlplaPortal.Application.Interfaces;

/// <summary>One group whose cached aggregate status was recomputed by a coverage re-derivation.</summary>
public sealed record GroupCoverageChange(
    Guid RequestPoGroupId,
    string PreviousStatus,
    string NewStatus,
    OperationInvoiceCoverage Coverage)
{
    public bool StatusChanged => !string.Equals(PreviousStatus, NewStatus, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Recomputes the cached <c>RequestPoGroup.OperationInvoiceStatus</c> through
/// <see cref="OperationInvoiceAggregateDeriver"/> — the single aggregate policy — for a set of
/// groups, inside the CALLER's transaction (this service never calls SaveChanges).
///
/// <para>Every allocation / validation / rejection / void / short-close write path re-derives its
/// touched groups through here, so the cached status can never drift from the allocation set
/// within a request-scoped operation.</para>
/// </summary>
public interface IOperationInvoiceCoverageService
{
    /// <summary>
    /// Loads the groups TRACKED, recomputes each aggregate, and writes the cached status when it
    /// changed. When <paramref name="forceGroupTouch"/> is true every group's UpdatedAtUtc is
    /// stamped even without a status change — forcing a RowVersion-checked UPDATE so two
    /// concurrent effective-coverage writers can never both commit against the same stale
    /// coverage reading (the second one hits a concurrency conflict instead of double-covering).
    /// </summary>
    Task<List<GroupCoverageChange>> RederiveAsync(
        IReadOnlyCollection<Guid> requestPoGroupIds,
        bool forceGroupTouch,
        CancellationToken cancellationToken = default);
}
