using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

public static class RequestWorkflowHelper
{
    /// <summary>
    /// Governs the single authoritative rule for determining the next status after a receiving action.
    /// </summary>
    public static string DeterminePostReceivingStatus(Request request)
    {
        bool allReceived = false;

        if (request.RequestType!.Code == "QUOTATION" && request.SelectedQuotationId.HasValue)
        {
            var winningQuotation = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
            if (winningQuotation != null)
            {
                allReceived = winningQuotation.Items.All(qi => qi.LineItemStatus?.Code == "RECEIVED");
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
        var allowedStatuses = new[] { "DRAFT", "WAITING_QUOTATION", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT" };
        return allowedStatuses.Contains(statusCode);
    }
}
