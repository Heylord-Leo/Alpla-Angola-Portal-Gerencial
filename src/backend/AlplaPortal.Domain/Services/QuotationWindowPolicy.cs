using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.1 — the ITEM-LEVEL quotation window of a batch-model QUOTATION request. Pure, static, no I/O.
///
/// The request scalar (<c>RequestWorkflowHelper.CanMutateQuotation</c>: DRAFT / WAITING_QUOTATION /
/// AREA_ADJUSTMENT / FINAL_ADJUSTMENT) remains the general rule. It is deliberately NOT regressed when a
/// sibling group crosses the P.O. gate (RequestStatusCalculator: "in-flight work on REMAINING items
/// surfaces through projections, never by regressing the scalar"). Two situations therefore leave lines
/// that are genuinely open for a new quotation while the scalar is outside that window:
///   • a partial submission whose approved batch already produced a P.O. (scalar PO_ISSUED-like) while
///     other lines were never quoted;
///   • a P.O. group cancelled with "voltar a cotar" (RESTART_QUOTATION) while sibling groups stay
///     operational (scalar PO_PARTIALLY_UPLOADED / PO_ISSUED / …).
/// A line is OPEN when it is not deleted, its lifecycle is null or QUOTATION_PENDING, and no active or
/// approved batch still HOLDS it (an item released by a cancellation — ReleasedAtUtc set — is not held).
/// The window is open only for QUOTATION requests that are not terminal AND that ran the batch workflow
/// (at least one ApprovalBatch): legacy pre-batch requests with null lifecycles stay closed, exactly as
/// before. Consumers: SaveQuotation / OcrExtract / quotation-document upload (write side) and the Buyer
/// queue projection (read side) — one rule, never re-derived in the frontend.
/// </summary>
public static class QuotationWindowPolicy
{
    public const string ClosedNotQuotationType = "REQUEST_TYPE_NOT_QUOTATION";
    public const string ClosedTerminal = "REQUEST_TERMINAL";
    public const string ClosedNoBatchWorkflow = "NO_BATCH_WORKFLOW";
    public const string ClosedNoOpenLines = "NO_OPEN_LINES";

    /// <summary>Batch statuses whose (non-released) items hold their request lines — the same set the batch
    /// creation endpoint, the Workspace and the queue projection use.</summary>
    public static readonly string[] HoldingBatchStatuses =
    {
        RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
        RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
        RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
        RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
        RequestConstants.ApprovalBatchStatuses.Approved,
    };

    /// <summary>Request statuses after which no quotation work exists at all.</summary>
    public static readonly string[] TerminalRequestStatuses =
    {
        RequestConstants.Statuses.Cancelled,
        RequestConstants.Statuses.Rejected,
        RequestConstants.Statuses.Completed,
    };

    /// <summary>
    /// v2.249.2 — line lifecycles that are COMMITTED: approved and bought (or closed) after approval. Whatever the window
    /// (scalar or item-level), a MAPPED/SUBSTITUTE quotation line may never target such a request line: the write
    /// endpoints refuse the whole payload (409 "Item já comprometido") so a mixed payload persists nothing. BATCH_ASSIGNED is
    /// deliberately NOT here: the rework-revision flow maps revised lines onto items of a batch under adjustment.
    /// </summary>
    public static readonly string[] CommittedLifecycleStatuses =
    {
        RequestConstants.QuotationLifecycleStatuses.QuotationApproved,
        RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted,
        RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval,
        RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted,
    };

    public static bool IsCommittedLifecycle(string? quotationLifecycleStatus) =>
        quotationLifecycleStatus != null && CommittedLifecycleStatuses.Contains(quotationLifecycleStatus);

    public sealed record LineFacts(Guid LineItemId, bool IsDeleted, string? QuotationLifecycleStatus, bool HeldByActiveBatch);

    public sealed record Evaluation(bool IsOpen, IReadOnlyCollection<Guid> OpenLineItemIds, string? ClosedReasonCode)
    {
        public static Evaluation Closed(string reason) => new(false, Array.Empty<Guid>(), reason);
        public bool IsLineOpen(Guid lineItemId) => IsOpen && OpenLineItemIds.Contains(lineItemId);
    }

    /// <summary>Line-level rule: eligible pool (null / QUOTATION_PENDING), not deleted, not held by a batch.</summary>
    public static bool IsLineOpen(string? quotationLifecycleStatus, bool isDeleted, bool heldByActiveBatch) =>
        !isDeleted
        && !heldByActiveBatch
        && (string.IsNullOrWhiteSpace(quotationLifecycleStatus)
            || quotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending);

    /// <summary>Request lines still held by an active/approved batch (released items excluded). Requires batch.Items loaded.</summary>
    public static HashSet<Guid> HeldLineItemIds(IEnumerable<ApprovalBatch> batches) =>
        batches
            .Where(b => HoldingBatchStatuses.Contains(b.Status))
            .SelectMany(b => b.Items ?? Enumerable.Empty<ApprovalBatchItem>())
            .Where(bi => bi.ReleasedAtUtc == null)
            .Select(bi => bi.RequestLineItemId)
            .ToHashSet();

    public static Evaluation Evaluate(
        string? requestTypeCode,
        string? requestStatusCode,
        bool isCancelled,
        bool hasApprovalBatches,
        IEnumerable<LineFacts> lines)
    {
        if (requestTypeCode != RequestConstants.Types.Quotation)
            return Evaluation.Closed(ClosedNotQuotationType);
        if (isCancelled || (requestStatusCode != null && TerminalRequestStatuses.Contains(requestStatusCode)))
            return Evaluation.Closed(ClosedTerminal);
        if (!hasApprovalBatches)
            return Evaluation.Closed(ClosedNoBatchWorkflow);

        var open = lines
            .Where(l => IsLineOpen(l.QuotationLifecycleStatus, l.IsDeleted, l.HeldByActiveBatch))
            .Select(l => l.LineItemId)
            .Distinct()
            .ToList();
        return open.Count == 0 ? Evaluation.Closed(ClosedNoOpenLines) : new Evaluation(true, open, null);
    }

    /// <summary>Convenience over a loaded aggregate (RequestType, Status, LineItems, ApprovalBatches.Items).</summary>
    public static Evaluation EvaluateLoaded(Request request)
    {
        var held = HeldLineItemIds(request.ApprovalBatches);
        return Evaluate(
            request.RequestType?.Code,
            request.Status?.Code,
            request.IsCancelled,
            request.ApprovalBatches.Count > 0,
            request.LineItems.Select(li => new LineFacts(li.Id, li.IsDeleted, li.QuotationLifecycleStatus, held.Contains(li.Id))));
    }
}
