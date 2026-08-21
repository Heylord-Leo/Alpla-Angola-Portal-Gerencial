using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
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
                .ThenInclude(b => b.Items)
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
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);
        var newStatusCode = result.StatusCode;

        if (result.IssueCode.HasValue)
        {
            _logger.LogWarning(
                "SyncStatusAsync: Request {RequestId} — {IssueCode}. Affected PO groups: {GroupIds}",
                requestId, result.IssueCode, result.AffectedPoGroupIds);
        }

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
                .ThenInclude(b => b.Items)
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

        // v2.230.0 — terminal request states are authoritative over any group/batch mixture:
        // a CANCELLED/REJECTED request must display as such regardless of historical groups.
        if (currentStatusCode is "CANCELLED" or "REJECTED")
            return currentStatusCode;

        // v2.230.0 — superseded batches (REQ-23/07/2026-140 class: still in an approval state
        // but every item already processed by another active operational unit) never drive the
        // ACTIVE display state. They stay visible in history and admin diagnostics only —
        // without this, a single stale AREA_ADJUSTMENT batch kept a PO_ISSUED request labelled
        // "Processamento Parcial" forever. Same policy the aggregate calculator uses.
        var activeLineItemsForPolicy = lineItems.Where(li => !li.IsDeleted).ToList();
        batches = batches
            .Where(b => !SupersededBatchPolicy.IsSuperseded(b, activeLineItemsForPolicy, poGroups))
            .ToList();

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

            // Case 2: PO groups exist but some still in progress.
            // v2.230.0 — when the in-progress groups SPAN the PO gate (one still waiting for
            // its P.O. while another is already in the financial/receiving track), the request
            // is genuinely mid-flight on two structurally different tracks: report
            // MIXED_PROCESSING instead of the least-advanced label, which understated the
            // state ("Aguardando P.O." for a request that also has a payment scheduled).
            // Groups all on the SAME side of the gate keep the least-advanced status — the
            // post-gate mixed labels are the group-aware display override's responsibility
            // (RequestGroupDisplayStateCalculator, mirrored in TS), not this projection's.
            if (activePoGroups.Count > 0 && inProgressGroups.Count > 0)
            {
                var spansPoGate =
                    inProgressGroups.Any(g => !SupersededBatchPolicy.HasCrossedPoGate(g)) &&
                    inProgressGroups.Any(SupersededBatchPolicy.HasCrossedPoGate);
                if (spansPoGate)
                    return "MIXED_PROCESSING";

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

}
