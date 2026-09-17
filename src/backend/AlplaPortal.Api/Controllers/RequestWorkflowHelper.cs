using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Api.Controllers;

public static class RequestWorkflowHelper
{
    /// <summary>
    /// Checks whether all items (line items or winning quotation items) have been physically received.
    /// This is an item-level check only — it does NOT determine request lifecycle status.
    /// </summary>
    public static bool AreAllItemsReceived(Request request)
    {
        if (request.RequestType!.Code == RequestConstants.Types.Quotation && request.SelectedQuotationId.HasValue)
        {
            var winningQuotation = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
            if (winningQuotation != null)
            {
                return winningQuotation.Items.All(qi => qi.LineItemStatus?.Code == "RECEIVED");
            }
            return false;
        }

        return request.LineItems.Where(li => !li.IsDeleted).All(li => li.LineItemStatus?.Code == "RECEIVED");
    }

    /// <summary>
    /// Determines the next status after a Receiving "confirm receiving" action.
    /// Business rule: Receiving must NEVER move a request to COMPLETED.
    /// - All items received → WAITING_RECEIPT (stays/returns for Finance to finalize)
    /// - Partial items → IN_FOLLOWUP (needs attention before Finance can finalize)
    /// </summary>
    public static string DeterminePostConfirmReceivingStatus(Request request)
    {
        return AreAllItemsReceived(request) ? "WAITING_RECEIPT" : "IN_FOLLOWUP";
    }

    /// <summary>
    /// Checks whether all items belonging to a specific PO group have been physically received.
    /// Delegates to the domain rulebook so the Phase 4 completion projection and the receiving
    /// endpoints can never disagree about what "all received" means.
    /// </summary>
    public static bool AreAllGroupItemsReceived(RequestPoGroup group)
        => AlplaPortal.Domain.Services.OperationalReceiptFacts.AreAllGroupItemsReceived(group);

    /// <summary>v2.245.0: group completion aware of the winning quotation items (line-number fallback).</summary>
    public static bool AreAllGroupItemsReceived(RequestPoGroup group, IReadOnlyCollection<QuotationItem>? winningQuotationItems)
        => AlplaPortal.Domain.Services.OperationalReceiptFacts.AreAllGroupItemsReceived(group, winningQuotationItems);

    /// <summary>
    /// Determines the next status for a PO Group after a Receiving "confirm receiving" action.
    /// </summary>
    public static string DetermineGroupPostConfirmReceivingStatus(RequestPoGroup group)
    {
        return AreAllGroupItemsReceived(group) ? "WAITING_RECEIPT" : "IN_FOLLOWUP";
    }

    /// <summary>v2.245.0 overload: honors the winning-quotation-item receipt via the canonical resolver.</summary>
    public static string DetermineGroupPostConfirmReceivingStatus(RequestPoGroup group, IReadOnlyCollection<QuotationItem>? winningQuotationItems)
    {
        return AreAllGroupItemsReceived(group, winningQuotationItems) ? "WAITING_RECEIPT" : "IN_FOLLOWUP";
    }

    /// <summary>
    /// v2.245.2 — the status a request scalar takes as a side-effect of ITEM-QUANTITY REGISTRATION
    /// (NOT confirmation). This is deliberately distinct from <see cref="DeterminePostConfirmReceivingStatus"/>:
    /// registering quantities — even reaching 100% — is a PRE-confirmation signal and must NEVER enter
    /// WAITING_RECEIPT. WAITING_RECEIPT is reached ONLY by the explicit CONFIRM_RECEIVING action (which sets
    /// the group status and lets the aggregator derive the scalar).
    ///
    /// <para>This holds for EVERY request — grouped OR groupless. There is no groupless confirmation
    /// mechanism (ConfirmReceiving requires a group), so a groupless request must NOT be silently advanced to
    /// WAITING_RECEIPT by registration; it stays IN_FOLLOWUP and, absent auditable operational confirmation,
    /// finalization fails closed (see FinalizeRequest). No CONFIRM_RECEIVING / OPERATIONAL_RECEIPT_COMPLETED is
    /// ever fabricated.</para>
    /// </summary>
    public static string DetermineItemRegistrationSyncStatus(Request request) => "IN_FOLLOWUP";

    /// <summary>
    /// [DEPRECATED — do NOT use for item registration.] Historical alias of the POST-CONFIRMATION rule.
    /// Item-quantity registration must use <see cref="DetermineItemRegistrationSyncStatus"/> instead; reusing
    /// this post-confirmation rule during registration was the v2.245.2 premature-WAITING_RECEIPT defect.
    /// Retained only so any external caller keeps compiling; no in-repo caller uses it.
    /// </summary>
    public static string DeterminePostReceivingStatus(Request request)
    {
        return DeterminePostConfirmReceivingStatus(request);
    }

    /// <summary>
    /// Governs whether a request is in a status that allows quotation creation, editing, or deletion.
    /// Allowed: DRAFT, WAITING_QUOTATION, AREA_ADJUSTMENT, FINAL_ADJUSTMENT
    /// Restricted: All other statuses (e.g., WAITING_AREA_APPROVAL onwards)
    /// </summary>
    public static bool CanMutateQuotation(string statusCode)
    {
        var allowedStatuses = new[] 
        { 
            RequestConstants.Statuses.Draft, 
            RequestConstants.Statuses.WaitingQuotation, 
            RequestConstants.Statuses.AreaAdjustment, 
            RequestConstants.Statuses.FinalAdjustment 
        };
        return allowedStatuses.Contains(statusCode);
    }
}
