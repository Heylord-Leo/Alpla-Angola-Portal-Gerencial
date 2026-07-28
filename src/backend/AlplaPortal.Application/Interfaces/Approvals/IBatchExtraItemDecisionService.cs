using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Interfaces.Approvals;

/// <summary>Canonical values for <see cref="ExtraItemDecisionDto.Decision"/> in the batch-composition
/// context (Buyer decides whether to include a genuine EXTRA_ITEM line in the batch). Distinct from
/// Area Approval's APPROVE/REJECT vocabulary on purpose — see docs on why this was renamed.</summary>
public static class ExtraItemDecisionValues
{
    public const string Include = "INCLUDE";
    public const string Exclude = "EXCLUDE";
}

/// <summary>One EXTRA_ITEM quotation line the buyer has not yet decided on for this batch.</summary>
public sealed class PendingExtraItem
{
    public Guid QuotationItemId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? QuotationDocumentNumber { get; init; }
    public string? SupplierName { get; init; }
}

/// <summary>Outcome of <see cref="IBatchExtraItemDecisionService.ApplyAsync"/>.</summary>
public sealed class ExtraItemDecisionApplyResult
{
    public bool Success { get; init; }

    /// <summary>Non-empty when Success is false because one or more EXTRA_ITEM lines have no decision yet.</summary>
    public IReadOnlyList<PendingExtraItem> PendingItems { get; init; } = Array.Empty<PendingExtraItem>();

    /// <summary>True when Success is false because a requested INCLUDE→EXCLUDE reversal was blocked.</summary>
    public bool ReversalLocked { get; init; }
    public string? ReversalLockedReason { get; init; }
    public Guid? ReversalLockedQuotationItemId { get; init; }

    /// <summary>True when Success is false because a decision's comment/validation failed (e.g. missing/weak EXCLUDE comment).</summary>
    public bool ValidationFailed { get; init; }
    public string? ValidationErrorMessage { get; init; }

    public static ExtraItemDecisionApplyResult Ok() => new() { Success = true };

    public static ExtraItemDecisionApplyResult PendingDecisions(IReadOnlyList<PendingExtraItem> items) =>
        new() { Success = false, PendingItems = items };

    public static ExtraItemDecisionApplyResult Locked(string reason, Guid quotationItemId) =>
        new() { Success = false, ReversalLocked = true, ReversalLockedReason = reason, ReversalLockedQuotationItemId = quotationItemId };

    public static ExtraItemDecisionApplyResult Invalid(string message) =>
        new() { Success = false, ValidationFailed = true, ValidationErrorMessage = message };
}

/// <summary>
/// Single source of truth for the Buyer's batch-composition decision on genuine EXTRA_ITEM
/// quotation lines (whether to include one as a new RequestLineItem in this ApprovalBatch, or
/// explicitly exclude it) — used identically by CreateBatch and UpdateBatch so the rule can never
/// diverge between the two call sites. The Area Approver never calls into this service; by the
/// time a batch reaches Area Approval every relevant EXTRA_ITEM line already has a decision.
/// </summary>
public interface IBatchExtraItemDecisionService
{
    /// <summary>
    /// Identifies every EXTRA_ITEM quotation line belonging to a quotation that contributes a
    /// winner to <paramref name="batch"/> (via <paramref name="winningQuotationItemIds"/>), and
    /// applies the Buyer's INCLUDE/EXCLUDE decisions from <paramref name="decisions"/>.
    /// Mutates the shared, tracked DbContext (new/removed RequestLineItem, ApprovalBatchItem,
    /// ApprovalBatchExtraItemDecision, RequestStatusHistory entries) — the caller is responsible
    /// for calling SaveChangesAsync; nothing is persisted if this returns a non-success result.
    /// </summary>
    Task<ExtraItemDecisionApplyResult> ApplyAsync(
        Request request,
        ApprovalBatch batch,
        IEnumerable<Guid> winningQuotationItemIds,
        Dictionary<Guid, ExtraItemDecisionDto>? decisions,
        Guid actorId);

    /// <summary>
    /// Read-only projection of a batch's informational lines for Area Approval display: buyer-
    /// excluded extras (with comment), IGNORED lines (with reconciliation justification), and
    /// unresolved legacy EXTRA_ITEM lines (no recorded decision at all — pre-dates this rule).
    /// </summary>
    Task<BatchInformationalLinesDto> GetInformationalLinesAsync(Guid batchId, IEnumerable<Guid> winningQuotationItemIds);
}
