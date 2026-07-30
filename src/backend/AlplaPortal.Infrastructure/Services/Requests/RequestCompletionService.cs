using System;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Requests;

/// <summary>
/// Release 1 skeleton of the two-phase Post-Payment Completion evaluation.
///
/// <para><b>What exists here</b>: the phase split, the transaction contract, the ambient-transaction
/// guard that protects the post-commit phase, and the feature gate. <b>What does not</b>: any
/// evaluation logic. While <c>PostPaymentCompletion.Enabled</c> is false — the Release 1 state in
/// every environment — both methods are pure no-ops that read nothing and write nothing.</para>
///
/// <para>No production code path calls this service in Release 1. It is registered so that the
/// architecture is represented, injectable and testable ahead of Release 4, which replaces the
/// <see cref="NotImplementedException"/> bodies with the real evaluation described in plan v6
/// §11.2 (Phase 1) and §11.4 (Phase 2).</para>
/// </summary>
public class RequestCompletionService : IRequestCompletionService
{
    private const string NotActivatedMessage =
        "Post-Payment Completion evaluation is not implemented in Release 1. " +
        "Phase 1 (group) and Phase 2 (parent) are activated in Release 4.";

    private readonly ApplicationDbContext _context;
    private readonly PostPaymentCompletionOptions _options;
    private readonly ILogger<RequestCompletionService> _logger;

    public RequestCompletionService(
        ApplicationDbContext context,
        IOptions<PostPaymentCompletionOptions> options,
        ILogger<RequestCompletionService> logger)
    {
        _context = context;
        _options = options?.Value ?? new PostPaymentCompletionOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GroupCompletionResult> EvaluateGroupCompletionAsync(
        Guid requestId,
        Guid? groupId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        // Feature gate first: while disabled this method must be indistinguishable from not
        // existing at all — no query, no tracked entity, no log noise on the hot path.
        if (PostPaymentCompletionPolicy.IsFeatureDisabled(_options))
            return Task.FromResult(GroupCompletionResult.NoOp);

        // Release 4 replaces this with the evaluation of plan v6 §11.2:
        //   skip UNCLASSIFIED groups; require operational receipt + satisfied Final Invoice +
        //   a Fiscal Receipt WITH a non-null attachment id; mark COMPLETED and write
        //   GROUP_COMPLETED keyed by GC:{GroupId}:{FiscalReceiptAttachmentId}; otherwise move a
        //   receipt+invoice-complete group to WAITING_FISCAL_RECEIPT.
        // It must not open a transaction and must not call SaveChanges.
        throw new NotImplementedException(NotActivatedMessage);
    }

    /// <inheritdoc />
    public Task<ParentCompletionResult> EvaluateParentCompletionAsync(
        Guid requestId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        if (PostPaymentCompletionPolicy.IsFeatureDisabled(_options))
            return Task.FromResult(ParentCompletionResult.NoOp);

        // Contract guard — this is the whole reason the phase exists. Running the parent
        // evaluation inside the caller's transaction makes it read the caller's own uncommitted
        // state and miss sibling groups committed elsewhere, which is exactly the race that
        // leaves every group COMPLETED and the Request permanently open. A caller that reaches
        // here with an ambient transaction has a defect that no runtime fallback can repair, so
        // it fails loudly instead of silently degrading to the broken behaviour.
        if (_context.Database.CurrentTransaction != null)
        {
            _logger.LogError(
                "EvaluateParentCompletionAsync was called inside an ambient transaction for Request {RequestId}. " +
                "The parent evaluation must run AFTER the caller's transaction has committed.",
                requestId);

            throw new InvalidOperationException(
                "EvaluateParentCompletionAsync must be called after the caller's transaction has " +
                "committed, never inside it.");
        }

        // Release 4 replaces this with the atomic transition of plan v6 §11.4:
        //   own ReadCommitted transaction; reload Request (+RowVersion); return AlreadyCompleted
        //   when already COMPLETED; re-read every active group; when all are COMPLETED, assign
        //   Request.CompletionCycleId, write REQUEST_COMPLETED (RC:{RequestId}:{CompletionCycleId})
        //   and enqueue RequestFinalized with the same CorrelationId, all in ONE SaveChanges +
        //   commit; on DbUpdateConcurrencyException reload once and retry, second failure returns
        //   ConflictUnresolved.
        throw new NotImplementedException(NotActivatedMessage);
    }
}
