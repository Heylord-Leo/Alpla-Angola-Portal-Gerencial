using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Api.Controllers;

public static class RequestWorkflowHelper
{
    /// <summary>
    /// Governs the single authoritative rule for determining the next status after a receiving action.
    /// </summary>
    public static string DeterminePostReceivingStatus(Request request)
    {
        bool allReceived = false;

        if (request.RequestType!.Code == RequestConstants.Types.Quotation && request.SelectedQuotationId.HasValue)
        {
            var winningQuotation = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
            if (winningQuotation != null)
            {
                allReceived = winningQuotation.Items.All(qi => qi.LineItemStatus?.Code == "RECEIVED"); // RECEIVED status not yet in constants
            }
        }
        else
        {
            allReceived = request.LineItems.Where(li => !li.IsDeleted).All(li => li.LineItemStatus?.Code == "RECEIVED");
        }

        return allReceived ? "COMPLETED" : "IN_FOLLOWUP";
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
