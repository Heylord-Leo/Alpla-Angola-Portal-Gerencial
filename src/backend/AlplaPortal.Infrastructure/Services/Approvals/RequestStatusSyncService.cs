using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// Syncs Request.StatusId (legacy compatibility) and computes DisplayWorkflowState
/// for QUOTATION requests based on the batch/item/group lifecycle.
/// </summary>
public class RequestStatusSyncService : IRequestStatusSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RequestStatusSyncService> _logger;

    public RequestStatusSyncService(ApplicationDbContext context, ILogger<RequestStatusSyncService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SyncStatusAsync(Guid requestId, Guid actorId)
    {
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems.Where(li => !li.IsDeleted))
            .Include(r => r.ApprovalBatches)
            .Include(r => r.PoGroups)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            _logger.LogWarning("SyncStatusAsync: Request {RequestId} not found", requestId);
            return;
        }

        // PAYMENT requests: skip — they use the legacy single-status flow
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
        {
            _logger.LogDebug("SyncStatusAsync: Request {RequestId} is not QUOTATION, skipping sync", requestId);
            return;
        }

        var previousStatusCode = request.Status?.Code ?? "";
        var newStatusCode = DetermineStatusCode(request);

        if (newStatusCode == previousStatusCode)
        {
            _logger.LogDebug("SyncStatusAsync: Request {RequestId} status unchanged ({Status})", requestId, previousStatusCode);
            return;
        }

        var newStatus = await _context.RequestStatuses
            .FirstOrDefaultAsync(s => s.Code == newStatusCode);

        if (newStatus == null)
        {
            _logger.LogError("SyncStatusAsync: Status code '{StatusCode}' not found in RequestStatuses", newStatusCode);
            return;
        }

        var previousStatusId = request.StatusId;
        request.StatusId = newStatus.Id;
        request.UpdatedAtUtc = DateTime.UtcNow;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "STATUS_SYNC",
            PreviousStatusId = previousStatusId,
            NewStatusId = newStatus.Id,
            Comment = $"Status sincronizado automaticamente: {previousStatusCode} → {newStatusCode}",
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation(
            "SyncStatusAsync: Request {RequestId} status changed {OldStatus} → {NewStatus}",
            requestId, previousStatusCode, newStatusCode);
    }

    /// <inheritdoc />
    public async Task<string> ComputeDisplayWorkflowStateAsync(Guid requestId)
    {
        var request = await _context.Requests
            .AsNoTracking()
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems.Where(li => !li.IsDeleted))
            .Include(r => r.ApprovalBatches)
            .Include(r => r.PoGroups)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return "UNKNOWN";

        return ComputeDisplayWorkflowState(
            request.RequestType?.Code ?? "",
            request.Status?.Code ?? "",
            request.LineItems.ToList(),
            request.ApprovalBatches.ToList(),
            request.PoGroups.ToList());
    }

    /// <inheritdoc />
    public string ComputeDisplayWorkflowState(
        string requestTypeCode,
        string currentStatusCode,
        IReadOnlyList<RequestLineItem> lineItems,
        IReadOnlyList<ApprovalBatch> batches,
        IReadOnlyList<RequestPoGroup> poGroups)
    {
        // PAYMENT requests: return legacy status code as-is
        if (requestTypeCode != RequestConstants.Types.Quotation)
            return currentStatusCode;

        // No batches exist yet, and no closed items — use legacy status
        if (batches.Count == 0 && lineItems.All(li =>
                li.QuotationLifecycleStatus != RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted &&
                li.QuotationLifecycleStatus != RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted))
            return currentStatusCode;


        var activeItems = lineItems.Where(li => !li.IsDeleted).ToList();
        if (activeItems.Count == 0)
            return currentStatusCode;

        // ── Classify items ──
        var pendingItems = activeItems.Where(li =>
            li.QuotationLifecycleStatus == null ||
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending).ToList();

        var batchAssignedItems = activeItems.Where(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.BatchAssigned).ToList();

        var approvedItems = activeItems.Where(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationApproved).ToList();

        // Terminally closed without quotation: new Buyer-closed status plus the
        // legacy accepted-proposal status (kept for old data).
        var closedItems = activeItems.Where(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted ||
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted).ToList();

        var notQuotedProposed = activeItems.Where(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed).ToList();

        // ── Classify batches ──
        var activeBatches = batches.Where(b =>
            b.Status != RequestConstants.ApprovalBatchStatuses.Rejected).ToList();

        var inApprovalBatches = activeBatches.Where(b =>
            b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval ||
            b.Status == RequestConstants.ApprovalBatchStatuses.AreaAdjustment ||
            b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval ||
            b.Status == RequestConstants.ApprovalBatchStatuses.FinalAdjustment).ToList();

        var approvedBatches = activeBatches.Where(b =>
            b.Status == RequestConstants.ApprovalBatchStatuses.Approved).ToList();

        // ── Classify PO groups ──
        var activePoGroups = poGroups.Where(g =>
            g.Status != RequestConstants.PoGroupStatuses.Cancelled).ToList();

        // Groups that are fully operationally done
        var completedGroups = activePoGroups.Where(g =>
            RequestConstants.PoGroupStatuses.OperationallyCompleted.Contains(g.Status)).ToList();

        // Groups still in progress (not completed, not cancelled)
        var inProgressGroups = activePoGroups.Except(completedGroups).ToList();

        var poIssuedGroups = activePoGroups.Where(g =>
            g.Status == RequestConstants.PoGroupStatuses.PoIssued ||
            g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection).ToList();

        // ── Determine state ──
        var allActionable = pendingItems.Count + batchAssignedItems.Count + notQuotedProposed.Count;
        var allClosed = closedItems.Count;
        var allApproved = approvedItems.Count;
        var totalItems = activeItems.Count;

        // All items are terminally closed (approved + not-quoted-accepted) — quotation lifecycle done
        if (allApproved + allClosed == totalItems && totalItems > 0)
        {
            // Case 1: All PO groups operationally completed
            if (activePoGroups.Count > 0 && completedGroups.Count == activePoGroups.Count)
                return allClosed > 0 ? "COMPLETED_WITH_CLOSURES" : "FULLY_COMPLETED";

            // Case 2: PO groups exist but some still in progress — return least-advanced group status
            if (activePoGroups.Count > 0 && inProgressGroups.Count > 0)
            {
                var lowestGroup = inProgressGroups
                    .OrderBy(g => PoGroupPriority(g.Status))
                    .First();
                return lowestGroup.Status;
            }

            // Case 3: All items NOT_QUOTED_ACCEPTED, no PO groups → closure
            if (allClosed == totalItems && activePoGroups.Count == 0)
                return "COMPLETED_WITH_CLOSURES";

            // Case 4: No PO groups yet — quotation approved, waiting for PO creation
            if (approvedBatches.Count > 0 && inApprovalBatches.Count == 0)
                return allClosed > 0 ? "APPROVED_WITH_CLOSURES" : "FULLY_APPROVED";
        }

        // Some PO groups issued, some batches still in approval or items pending
        if (poIssuedGroups.Count > 0 && (inApprovalBatches.Count > 0 || pendingItems.Count > 0))
            return "MIXED_PROCESSING";

        // Some approved, some still pending or in approval
        if (approvedItems.Count > 0 && (pendingItems.Count > 0 || batchAssignedItems.Count > 0))
        {
            if (poIssuedGroups.Count > 0)
                return "PARTIALLY_PO_ISSUED";
            return "PARTIALLY_APPROVED";
        }

        // All items in some batch (in approval) — nothing pending
        if (pendingItems.Count == 0 && batchAssignedItems.Count > 0 && inApprovalBatches.Count > 0)
            return "QUOTATION_IN_APPROVAL";

        // Some items in batch, some still pending
        if (pendingItems.Count > 0 && inApprovalBatches.Count > 0)
            return "PARTIALLY_IN_APPROVAL";

        // Still in quotation phase — no batches active
        if (pendingItems.Count > 0 && inApprovalBatches.Count == 0 && approvedBatches.Count == 0)
            return "QUOTATION_IN_PROGRESS";

        // Fallback
        return currentStatusCode;
    }

    // ── Private helpers ──

    /// <summary>
    /// Priority ordering for PO group statuses. Lower = further behind in the lifecycle.
    /// Used by ComputeDisplayWorkflowState to determine the most relevant active status.
    /// </summary>
    private static int PoGroupPriority(string status) => status switch
    {
        "PENDING" => 5,
        "WAITING_PO" => 10,
        "WAITING_PO_CORRECTION" => 15,
        "ADVANCE_PAYMENT_REQUIRED" => 20,
        "ADVANCE_PAYMENT_SCHEDULED" => 25,
        "ADVANCE_PAYMENT_COMPLETED" => 30,
        "WAITING_SUPPLIER_DELIVERY" => 35,
        "PO_ISSUED" => 40,
        "PAYMENT_REQUEST_SENT" => 45,
        "PAYMENT_SCHEDULED" => 50,
        "PAYMENT_COMPLETED" => 60,
        "WAITING_RECEIPT" => 70,
        "WAITING_RECONCILIATION" => 75,
        "IN_FOLLOWUP" => 80,
        "COMPLETED" => 100,
        _ => 999
    };

    /// <summary>
    /// Determines the appropriate legacy Request.StatusId code based on batch/item states.
    /// This keeps the legacy status meaningful for backwards compatibility.
    /// </summary>
    private string DetermineStatusCode(Request request)
    {
        var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var batches = request.ApprovalBatches.ToList();

        if (batches.Count == 0)
            return request.Status?.Code ?? RequestConstants.Statuses.WaitingQuotation;

        // Check if any batch is in area approval / rework
        var hasAreaApproval = batches.Any(b =>
            b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval ||
            b.Status == RequestConstants.ApprovalBatchStatuses.AreaAdjustment);

        var hasFinalApproval = batches.Any(b =>
            b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval ||
            b.Status == RequestConstants.ApprovalBatchStatuses.FinalAdjustment);

        var allBatchesApproved = batches
            .Where(b => b.Status != RequestConstants.ApprovalBatchStatuses.Rejected)
            .All(b => b.Status == RequestConstants.ApprovalBatchStatuses.Approved);

        var hasPendingItems = activeItems.Any(li =>
            string.IsNullOrWhiteSpace(li.QuotationLifecycleStatus) ||
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending);

        // Phase 7: Items with NOT_QUOTED_PROPOSED are awaiting decision — not terminal
        var hasNotQuotedProposed = activeItems.Any(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed);

        // Priority: If there are still pending items or not-quoted proposals, the Request MUST remain in WAITING_QUOTATION
        // so the buyer can continue inserting quotations for them.
        if (hasPendingItems || hasNotQuotedProposed)
            return RequestConstants.Statuses.WaitingQuotation;

        if (hasFinalApproval)
            return RequestConstants.Statuses.WaitingFinalApproval;

        if (hasAreaApproval)
            return RequestConstants.Statuses.WaitingAreaApproval;

        // All batches approved or rejected, and no pending items or not-quoted proposals

        // All batches approved, no pending items, no not-quoted proposals → legacy "completed" status
        if (allBatchesApproved && !hasPendingItems && !hasNotQuotedProposed)
            return RequestConstants.Statuses.QuotationCompleted;

        return request.Status?.Code ?? RequestConstants.Statuses.WaitingQuotation;
    }
}
