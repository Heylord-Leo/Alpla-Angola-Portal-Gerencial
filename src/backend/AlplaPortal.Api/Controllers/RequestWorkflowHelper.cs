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
    /// [DEPRECATED] Preserved for backward compatibility. Use DeterminePostConfirmReceivingStatus instead.
    /// This method previously returned "COMPLETED" which bypassed the financial receipt step.
    /// Now delegates to DeterminePostConfirmReceivingStatus (never returns COMPLETED).
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
