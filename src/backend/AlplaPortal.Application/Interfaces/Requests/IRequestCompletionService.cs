using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces.Requests;

/// <summary>
/// Centralized evaluation of the Post-Payment Completion Workflow, split into TWO EXPLICIT PHASES.
///
/// <para><b>Why two phases.</b> Evaluating parent completion inside the caller's transaction is
/// unsafe for a multi-group request: under ReadCommitted, two transactions completing group A and
/// group B concurrently each read the other group as still open, both decide "parent not ready",
/// and both commit — leaving every group complete but the Request permanently open. Phase 1
/// therefore decides GROUP completion only, inside the caller's transaction; Phase 2 decides
/// PARENT completion in its own short transaction AFTER the caller has committed, so the last
/// transaction to commit always observes the fully committed state.</para>
///
/// <para><b>Caller contract</b> (from Release 4, when the workflow is activated):</para>
/// <code>
/// await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
/// //  ... mutate the dimension + write its history row with the stable key ...
/// var groupResult = await completion.EvaluateGroupCompletionAsync(requestId, groupId, actorId, ct);
/// await db.SaveChangesAsync(ct);
/// await tx.CommitAsync(ct);
///
/// // AFTER COMMIT — never inside tx:
/// var parentResult = await completion.EvaluateParentCompletionAsync(requestId, actorId, ct);
/// </code>
///
/// <para>Phase 2 is invoked whenever the dimension transaction succeeded, not only when
/// <see cref="GroupCompletionResult.ParentEvaluationRequired"/> is true — that flag is an
/// optimization hint, and calling Phase 2 when nothing changed is a cheap, side-effect-free read.
/// Running it unconditionally is precisely what closes the concurrency window. A Phase 2 failure
/// never fails the user action: the dimension change is already durable, and the request simply
/// stays open until the next evaluation.</para>
///
/// <para><b>Release 1 status:</b> both methods are feature-gated no-ops. The interface, the result
/// contracts and the transaction rules exist so the architecture is represented and testable, but
/// nothing calls them in production code and nothing is activated.</para>
/// </summary>
public interface IRequestCompletionService
{
    /// <summary>
    /// PHASE 1 — runs INSIDE the caller's transaction, before the caller commits.
    ///
    /// Evaluates the post-payment dimensions of the affected group(s) and marks eligible groups
    /// COMPLETED, writing GROUP_COMPLETED history keyed by
    /// <c>GC:{GroupId}:{FiscalReceiptAttachmentId}</c>. Never touches the parent Request. Never
    /// opens, commits or rolls back a transaction, and never calls SaveChanges — the caller's
    /// single SaveChanges persists the dimension change and the completion together.
    /// </summary>
    /// <param name="requestId">Request that owns the groups.</param>
    /// <param name="groupId">Specific group to evaluate, or null to evaluate every active group.</param>
    /// <param name="actorUserId">User whose action triggered the evaluation (history actor).</param>
    Task<GroupCompletionResult> EvaluateGroupCompletionAsync(
        Guid requestId,
        Guid? groupId,
        Guid actorUserId,
        CancellationToken ct = default);

    /// <summary>
    /// PHASE 2 — runs AFTER the caller's transaction has committed.
    ///
    /// Opens its own short ReadCommitted transaction, re-reads the Request and all of its active
    /// groups, and atomically transitions the Request to COMPLETED when every active group is
    /// COMPLETED. The transition assigns <c>Request.CompletionCycleId</c> and writes the
    /// REQUEST_COMPLETED history row plus the RequestFinalized outbox entry in that SAME
    /// transaction, guarded by <c>Request.RowVersion</c>: only the winning transition emits
    /// anything. Idempotent and safe to call concurrently, repeatedly, or when nothing changed.
    ///
    /// Implementations MUST refuse to run inside an ambient transaction — doing so would
    /// reintroduce the stale-read race this phase exists to eliminate.
    /// </summary>
    Task<ParentCompletionResult> EvaluateParentCompletionAsync(
        Guid requestId,
        Guid actorUserId,
        CancellationToken ct = default);
}

/// <summary>Outcome of Phase 1 (group completion, inside the caller's transaction).</summary>
public class GroupCompletionResult
{
    /// <summary>At least one group was marked COMPLETED by this evaluation.</summary>
    public bool AnyGroupCompleted { get; set; }

    /// <summary>Groups this evaluation transitioned to COMPLETED.</summary>
    public List<Guid> CompletedGroupIds { get; set; } = new();

    /// <summary>
    /// Hint that a parent evaluation is worthwhile after commit. Callers still invoke Phase 2
    /// whenever the transaction succeeded — see the interface remarks.
    /// </summary>
    public bool ParentEvaluationRequired { get; set; }

    /// <summary>Non-null when the evaluation could not be completed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Feature disabled, or nothing to evaluate.</summary>
    public static GroupCompletionResult NoOp => new();
}

/// <summary>Outcome of Phase 2 (parent completion, after commit).</summary>
public class ParentCompletionResult
{
    /// <summary>This call performed the winning transition. It alone wrote history and enqueued the notification.</summary>
    public bool RequestCompleted { get; set; }

    /// <summary>The Request was already COMPLETED. No history and no notification were produced.</summary>
    public bool AlreadyCompleted { get; set; }

    /// <summary>
    /// The persisted <c>Request.CompletionCycleId</c> — assigned by the winning transition and
    /// returned unchanged to losers and retries, so the completion identity never drifts.
    /// </summary>
    public Guid? CompletionCycleId { get; set; }

    /// <summary>Two consecutive concurrency conflicts: caller should surface 409. The committed dimension change is unaffected.</summary>
    public bool ConflictUnresolved { get; set; }

    /// <summary>Non-null when the evaluation could not be completed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Feature disabled, or nothing to evaluate.</summary>
    public static ParentCompletionResult NoOp => new();
}
